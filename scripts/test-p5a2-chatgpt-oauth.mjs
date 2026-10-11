// P5a.2 end-to-end checks against a production-like stack: API in Production mode, Keycloak behind the
// internal Caddy under /auth and a simulated home-server edge Caddy with HTTPS (local test CA).
// A simulated "ChatGPT" OAuth client performs the documented authorization-code flow; no real ChatGPT access.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import {
  Browser, clientUuid, createChecker, createCompose, createKeycloakUser, csrfToken, decodePayload,
  keycloakLogin, pwaLogin, randomValue, s256, sleep,
} from './lib/p5a-test-support.mjs';

const env = process.env;
const origin = env.KUECHENFUCHS_PUBLIC_URL;
const realm = 'kuechenfuchs';
const issuer = `${origin}/auth/realms/${realm}`;
const oidc = `${issuer}/protocol/openid-connect`;
const chatGptRedirectUri = `${origin}/p5a2-simulated-chatgpt/oauth/callback`;
const chatGptSecret = env.KEYCLOAK_CHATGPT_ACTIONS_CLIENT_SECRET;
const { compose, sql, kcadm } = createCompose(JSON.parse(env.P5A_COMPOSE_ARGS), env);
const check = createChecker();

function configureClient(...args) {
  return execFileSync('bash', ['scripts/keycloak-chatgpt-client.sh', ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...env, KF_COMPOSE_FILES: env.P5A_COMPOSE_FILES, KF_COMPOSE_PROJECT: env.P5A_PROJECT, KF_ALLOW_TEST_REDIRECT_URIS: '1' },
  });
}

function configureClientFails(...args) {
  try {
    configureClient(...args);
  } catch (error) {
    return error.stderr ?? String(error);
  }
  assert.fail(`client configuration unexpectedly accepted: ${args.join(' ')}`);
}

/** Authorization request as ChatGPT sends it (authorization code, state; PKCE only when requested). */
async function authorize(user, { scope = 'openid recipes:read', pkce = null, redirectUri = chatGptRedirectUri, state = randomValue() } = {}) {
  const params = { response_type: 'code', client_id: 'chatgpt-actions', redirect_uri: redirectUri, scope, state };
  if (pkce) Object.assign(params, { code_challenge: s256(pkce), code_challenge_method: 'S256' });
  const location = await keycloakLogin(new Browser(), `${oidc}/auth?${new URLSearchParams(params)}`, user.username, user.password);
  return { location: new URL(location), state };
}

async function tokenRequest(form, { mode = 'post', secret = chatGptSecret } = {}) {
  const headers = { 'Content-Type': 'application/x-www-form-urlencoded' };
  const body = new URLSearchParams(form);
  if (mode === 'basic') {
    headers.Authorization = `Basic ${Buffer.from(`chatgpt-actions:${secret}`).toString('base64')}`;
  } else {
    body.set('client_id', 'chatgpt-actions');
    body.set('client_secret', secret);
  }

  const response = await fetch(`${oidc}/token`, { method: 'POST', headers, body });
  return { status: response.status, body: await response.json() };
}

async function exchange(code, options = {}) {
  const form = { grant_type: 'authorization_code', code, redirect_uri: chatGptRedirectUri };
  if (options.verifier) form.code_verifier = options.verifier;
  return tokenRequest(form, options);
}

async function chatGptLogin(user, options = {}) {
  const { location, state } = await authorize(user, options);
  assert.equal(location.searchParams.get('state'), state, 'state must be returned unchanged');
  const response = await exchange(location.searchParams.get('code'), options);
  assert.equal(response.status, 200, JSON.stringify(response.body));
  return response.body;
}

const refresh = (refreshToken, options = {}) =>
  tokenRequest({ grant_type: 'refresh_token', refresh_token: refreshToken }, options);

async function bearerCheck(token, householdId) {
  return new Browser().request(`${origin}/api/v1/integrations/chatgpt/households/${householdId}/authorization-check`, { bearer: token });
}

async function link(pwa, csrf, householdId, user) {
  const start = await pwa.request(`${origin}/api/v1/integrations/keycloak/link-requests`, {
    method: 'POST', headers: { 'X-KF-CSRF': csrf }, json: { householdId },
  });
  assert.equal(start.status, 200, start.text);
  assert.ok(pwa.has('__Secure-KF.KeycloakLink'), 'production binding cookie missing');
  const callback = await keycloakLogin(new Browser(), start.json().authorizationUrl, user.username, user.password);
  assert.ok(callback.startsWith(`${origin}/api/v1/integrations/keycloak/callback?`));
  const result = await pwa.request(callback);
  assert.equal(result.status, 302);
  const target = new URL(result.location, origin);
  assert.equal(target.pathname, '/settings/chatgpt');
  return target.searchParams.get('keycloakLink');
}

async function links(pwa) {
  const response = await pwa.request(`${origin}/api/v1/integrations/keycloak/links`);
  assert.equal(response.status, 200);
  return response.json();
}

const waitForNewTokenGeneration = () => sleep(2_100);

async function main() {
  console.log('Checking HTTPS edge, same-origin OIDC metadata and exposure...');
  let response = await new Browser().request(`${issuer}/.well-known/openid-configuration`);
  assert.equal(response.status, 200);
  const discovery = response.json();
  assert.equal(discovery.issuer, issuer);
  for (const key of ['authorization_endpoint', 'token_endpoint', 'jwks_uri', 'end_session_endpoint']) {
    assert.ok(discovery[key].startsWith(`${origin}/auth/realms/${realm}/`), `${key} must use the public origin: ${discovery[key]}`);
  }
  assert.ok(discovery.token_endpoint_auth_methods_supported.includes('client_secret_basic'));
  assert.ok(discovery.token_endpoint_auth_methods_supported.includes('client_secret_post'));
  assert.ok(discovery.code_challenge_methods_supported.includes('S256'));
  check('discovery via HTTPS: fixed issuer and all OAuth endpoints on the API origin, basic/post and S256 supported');

  for (const path of ['/auth/admin/', '/auth/admin/master/console/', '/auth/realms/master/.well-known/openid-configuration',
    '/auth/health/ready', '/auth/metrics', '/auth/', '/auth/realms/other/.well-known/openid-configuration']) {
    assert.equal((await new Browser().request(`${origin}${path}`)).status, 404, path);
  }
  check('Keycloak admin, master realm, other realms, health and metrics are not reachable via the public origin');

  response = await new Browser().request(`${origin}/`);
  assert.equal(response.status, 200);
  assert.match(response.headers.get('strict-transport-security') ?? '', /max-age=31536000/);
  assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
  assert.equal(response.headers.get('server'), null);
  assert.equal((await new Browser().request(`${origin}/api/openapi/v1.json`)).status, 404);
  check('edge sets HSTS/nosniff, hides Server; API runs in Production (no OpenAPI document)');

  console.log('Checking PWA login in Production mode...');
  const pwa = new Browser();
  response = await pwa.request(`${origin}/api/auth/login`, {
    method: 'POST', json: { email: env.INITIAL_ADMIN_EMAIL, password: env.INITIAL_ADMIN_PASSWORD },
  });
  assert.equal(response.status, 400);
  let csrf = await pwaLogin(pwa, origin, env.INITIAL_ADMIN_EMAIL, env.INITIAL_ADMIN_PASSWORD);
  const identityCookie = pwa.rawSetCookies.find((cookie) => cookie.startsWith('__Host-KF.Identity='));
  assert.ok(identityCookie, 'production identity cookie missing');
  assert.match(identityCookie, /;\s*secure/i);
  assert.match(identityCookie, /;\s*httponly/i);
  assert.match(identityCookie, /;\s*samesite=strict/i);
  const session = (await pwa.request(`${origin}/api/auth/session`)).json();
  const userId = session.userId;
  const homeId = session.households[0].householdId;
  check('PWA login with __Host- cookie (Secure, HttpOnly, SameSite=Strict) and CSRF unchanged');

  const secondHomeId = randomUUID();
  const foreignHomeId = randomUUID();
  sql(`INSERT INTO "Households" ("Id","Name","CreatedAt") VALUES ('${secondHomeId}','P5a2 Zweithaushalt',now()),('${foreignHomeId}','P5a2 Fremdhaushalt',now());
       INSERT INTO "HouseholdMembers" ("HouseholdId","UserId","Role","JoinedAt") VALUES ('${secondHomeId}','${userId}','Member',now());`);

  console.log('Configuring the ChatGPT client with the operator script...');
  kcadm('config', 'credentials', '--server', 'http://localhost:8080/auth', '--realm', 'master',
    '--user', env.KEYCLOAK_ADMIN_USERNAME, '--password', env.KEYCLOAK_ADMIN_PASSWORD);
  const chatGptClient = clientUuid(kcadm, realm, 'chatgpt-actions');
  assert.match(configureClient('show'), /"redirectUris" : \[ \]/);
  for (const invalid of ['https://chatgpt.com/aip/*/oauth/callback', 'http://chatgpt.com/aip/g-abc/oauth/callback',
    'https://chatgpt.com.evil.test/aip/g-abc/oauth/callback', 'https://evil.test/callback']) {
    assert.match(configureClientFails('set-redirect-uris', invalid), /Abgelehnt/);
  }
  configureClient('set-redirect-uris', chatGptRedirectUri);
  assert.ok(configureClient('show').includes(chatGptRedirectUri));
  check('client starts without redirect URIs; script rejects wildcards, HTTP and foreign hosts, sets exact URI');

  const alice = createKeycloakUser(kcadm, realm, 'p5a2-alice', 'p5a2-alice@example.test');
  const mallory = createKeycloakUser(kcadm, realm, 'p5a2-mallory', env.INITIAL_ADMIN_EMAIL);

  console.log('Running OAuth protocol checks for the ChatGPT client...');
  const unregistered = new URLSearchParams({
    response_type: 'code', client_id: 'chatgpt-actions', redirect_uri: `${origin}/other/callback`, scope: 'openid', state: 'x',
  });
  response = await new Browser().request(`${oidc}/auth?${unregistered}`);
  assert.equal(response.status, 400);
  assert.equal(response.location, null);
  const prefixed = new URLSearchParams({
    response_type: 'code', client_id: 'chatgpt-actions', redirect_uri: `${chatGptRedirectUri}/../x`, scope: 'openid', state: 'x',
  });
  assert.equal((await new Browser().request(`${oidc}/auth?${prefixed}`)).status, 400);
  check('unregistered or modified redirect URIs are rejected without redirect');

  const specialState = `${randomValue()}.~-_`;
  let tokens = await chatGptLogin(alice, { state: specialState });
  assert.equal(tokens.token_type, 'Bearer');
  assert.equal(tokens.expires_in, 300);
  assert.ok(tokens.refresh_token, 'refresh token expected');
  let payload = decodePayload(tokens.access_token);
  assert.equal(payload.iss, issuer);
  assert.equal(payload.azp, 'chatgpt-actions');
  assert.ok([payload.aud].flat().includes('kuechenfuchs-api'));
  assert.deepEqual(payload.scope.split(' ').sort(), ['openid', 'recipes:read']);
  for (const claim of ['email', 'name', 'preferred_username', 'given_name', 'family_name', 'realm_access', 'resource_access']) {
    assert.equal(payload[claim], undefined, `access token must not contain ${claim}`);
  }
  check('code flow without PKCE (as documented by OpenAI), client_secret_post: state unchanged, Bearer/300 s/refresh, minimal claims');

  tokens = await chatGptLogin(alice, { mode: 'basic' });
  assert.equal(tokens.token_type, 'Bearer');
  check('code flow with client_secret_basic works as well');

  let auth = await authorize(alice);
  for (const mode of ['post', 'basic']) {
    const wrong = await exchange(auth.location.searchParams.get('code'), { mode, secret: 'wrong-secret' });
    assert.equal(wrong.status, 401);
    assert.equal(wrong.body.error, 'unauthorized_client');
  }
  check('invalid client secret is rejected (post and basic)');

  auth = await authorize(alice);
  let code = auth.location.searchParams.get('code');
  assert.equal((await exchange(code)).status, 200);
  response = await exchange(code);
  assert.equal(response.status, 400);
  assert.equal(response.body.error, 'invalid_grant');
  check('authorization code cannot be redeemed twice');

  const verifier = randomValue();
  auth = await authorize(alice, { pkce: verifier });
  response = await exchange(auth.location.searchParams.get('code'), { verifier: randomValue() });
  assert.equal(response.status, 400);
  assert.equal(response.body.error, 'invalid_grant');
  auth = await authorize(alice, { pkce: verifier });
  response = await exchange(auth.location.searchParams.get('code'));
  assert.equal(response.status, 400);
  assert.equal(response.body.error, 'invalid_grant');
  auth = await authorize(alice, { pkce: verifier });
  assert.equal((await exchange(auth.location.searchParams.get('code'), { verifier })).status, 200);
  check('PKCE S256 when sent: wrong or missing verifier rejected, correct verifier accepted');

  configureClient('pkce', 'required');
  auth = await authorize(alice);
  assert.equal(auth.location.searchParams.get('error'), 'invalid_request');
  assert.equal(auth.location.searchParams.get('code'), null);
  assert.equal(auth.location.searchParams.get('state'), auth.state);
  await chatGptLogin(alice, { pkce: randomValue(), verifier: undefined }).then(
    () => assert.fail('exchange without verifier must fail'),
    (error) => assert.match(String(error), /invalid_grant|AssertionError/),
  );
  const requiredVerifier = randomValue();
  tokens = await chatGptLogin(alice, { pkce: requiredVerifier, verifier: requiredVerifier });
  configureClient('pkce', 'optional');
  check('PKCE can be enforced by script once ChatGPT support is confirmed (without PKCE then rejected) and relaxed again');

  auth = await authorize(alice, { scope: 'openid recipes:read recipes:admin offline_access' });
  if (auth.location.searchParams.get('code')) {
    response = await exchange(auth.location.searchParams.get('code'));
    assert.equal(response.status, 200);
    payload = decodePayload(response.body.access_token);
    assert.ok(!payload.scope.includes('recipes:admin'), 'unknown scope must not be granted');
    assert.ok(!payload.scope.includes('offline_access'), 'offline access must not be granted');
    assert.notEqual(decodePayload(response.body.refresh_token).typ, 'Offline');
  } else {
    assert.equal(auth.location.searchParams.get('error'), 'invalid_scope');
  }
  check('unknown scopes and offline_access are never granted');

  console.log('Linking and ChatGPT authorization status...');
  assert.equal(await link(pwa, csrf, foreignHomeId, alice).catch(() => 'rejected'), 'rejected');
  assert.equal(await link(pwa, csrf, homeId, alice), 'linked');
  let [active] = await links(pwa);
  assert.equal(active.householdId, homeId);
  assert.equal(active.chatGpt.state, 'not-authorized');
  assert.equal(active.lastBearerAccessAt, null);
  check('link from PWA session to a chosen own household (foreign household refused); ChatGPT not yet shown as authorized');

  assert.equal((await bearerCheck(tokens.access_token, homeId)).status, 401);
  assert.equal((await refresh(tokens.refresh_token)).body.error, 'invalid_grant');
  check('tokens and refresh tokens from before the link are unusable (sessions ended at linking)');

  await waitForNewTokenGeneration();
  tokens = await chatGptLogin(alice);
  response = await bearerCheck(tokens.access_token, homeId);
  assert.equal(response.status, 200, response.text);
  [active] = await links(pwa);
  assert.equal(active.chatGpt.state, 'authorized');
  assert.ok(active.chatGpt.sessionLastAccessAt);
  assert.ok(active.lastBearerAccessAt);
  assert.ok(!JSON.stringify(active).includes(alice.id), 'status must not expose the external subject');
  check('after ChatGPT authorization and a bearer call the server reports "authorized" with last access');

  assert.equal((await bearerCheck(tokens.access_token, secondHomeId)).status, 404);
  assert.equal((await bearerCheck(tokens.access_token, foreignHomeId)).status, 404);
  assert.equal((await bearerCheck((await chatGptLogin(mallory)).access_token, homeId)).status, 401);
  check('other own household and foreign household hidden (404); unlinked identity with same e-mail rejected (401)');

  response = await refresh(tokens.refresh_token);
  assert.equal(response.status, 200, JSON.stringify(response.body));
  const rotated = response.body;
  assert.notEqual(rotated.refresh_token, tokens.refresh_token);
  assert.equal((await bearerCheck(rotated.access_token, homeId)).status, 200);
  response = await refresh(tokens.refresh_token);
  assert.equal(response.status, 400);
  assert.equal(response.body.error, 'invalid_grant');
  check('refresh token rotation: new pair works, reuse of the old refresh token is rejected');

  kcadm('update', `clients/${chatGptClient}`, '-r', realm, '-s', 'attributes."access.token.lifespan"=10');
  const shortLived = (await refresh((await chatGptLogin(alice)).refresh_token)).body;
  kcadm('update', `clients/${chatGptClient}`, '-r', realm, '-s', 'attributes."access.token.lifespan"=300');
  assert.equal((await bearerCheck(shortLived.access_token, homeId)).status, 200);
  await sleep(13_000);
  assert.equal((await bearerCheck(shortLived.access_token, homeId)).status, 401);
  check('expired access token is rejected (401)');

  sql(`UPDATE "AspNetUsers" SET "IsEnabled"=false WHERE "Id"='${userId}'`);
  assert.equal((await bearerCheck(rotated.access_token, homeId)).status, 401);
  sql(`UPDATE "AspNetUsers" SET "IsEnabled"=true WHERE "Id"='${userId}'`);
  sql(`DELETE FROM "HouseholdMembers" WHERE "HouseholdId"='${homeId}' AND "UserId"='${userId}'`);
  assert.equal((await bearerCheck(rotated.access_token, homeId)).status, 404);
  sql(`INSERT INTO "HouseholdMembers" ("HouseholdId","UserId","Role","JoinedAt") VALUES ('${homeId}','${userId}','Owner',now())`);
  assert.equal((await bearerCheck(rotated.access_token, homeId)).status, 200);
  check('blocked user (401) and removed membership (404) take effect immediately');

  let limited = 0;
  for (let attempt = 0; attempt < Number(env.CHATGPT_BEARER_REQUESTS_PER_MINUTE) + 5 && limited === 0; attempt += 1) {
    response = await bearerCheck(rotated.access_token, homeId);
    if (response.status === 429) limited += 1;
  }
  assert.equal(limited, 1, 'rate limit expected');
  assert.equal(response.headers.get('retry-after'), '60');
  check('bearer endpoints are rate limited per linked identity (429 with Retry-After)');

  console.log('Disconnect and re-link...');
  response = await pwa.request(`${origin}/api/v1/integrations/keycloak/links/${active.id}`, {
    method: 'DELETE', headers: { 'X-KF-CSRF': csrf },
  });
  assert.equal(response.status, 200);
  assert.deepEqual(response.json(), { revoked: true, providerSessionsEnded: true });
  assert.equal((await bearerCheck(rotated.access_token, homeId)).status, 401);
  response = await refresh(rotated.refresh_token);
  assert.equal(response.body.error, 'invalid_grant');
  const [revoked] = await links(pwa);
  assert.ok(revoked.revokedAt && revoked.providerSessionsRevokedAt);
  assert.equal(revoked.chatGpt, null);
  check('disconnect revokes the link and ends Keycloak sessions: access token 401, refresh token invalid_grant');

  const issuedWhileUnlinked = await chatGptLogin(alice);
  assert.equal(await link(pwa, csrf, secondHomeId, alice), 'linked');
  for (const old of [rotated.access_token, issuedWhileUnlinked.access_token]) {
    assert.equal((await bearerCheck(old, secondHomeId)).status, 401);
  }
  for (const oldRefresh of [rotated.refresh_token, issuedWhileUnlinked.refresh_token]) {
    assert.equal((await refresh(oldRefresh)).body.error, 'invalid_grant');
  }
  await waitForNewTokenGeneration();
  tokens = await chatGptLogin(alice);
  assert.equal((await bearerCheck(tokens.access_token, secondHomeId)).status, 200);
  assert.equal((await bearerCheck(tokens.access_token, homeId)).status, 404);
  check('after re-linking: old access and refresh tokens stay invalid, new token only for the newly chosen household');

  csrf = await csrfToken(pwa, origin);
  response = await pwa.request(`${origin}/api/auth/logout`, { method: 'POST', headers: { 'X-KF-CSRF': csrf } });
  assert.equal(response.status, 204);
  check('PWA logout still works');

  const logs = compose(['logs', 'api', 'edge', 'caddy']);
  for (const secret of [tokens.access_token, tokens.refresh_token, chatGptSecret, env.KEYCLOAK_API_ADMIN_CLIENT_SECRET,
    env.KEYCLOAK_LINK_CLIENT_SECRET, alice.password, env.INITIAL_ADMIN_PASSWORD]) {
    assert.ok(!logs.includes(secret), 'logs must not contain tokens, secrets or passwords');
  }
  assert.ok(!/[?&]code=[A-Za-z0-9.-]{20,}/.test(logs), 'logs must not contain authorization codes');
  check('API and proxy logs contain no tokens, codes, client secrets or passwords');

  console.log(`P5a.2 ChatGPT OAuth checks passed (${check.count()} checks).`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});

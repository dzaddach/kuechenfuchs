// P5a.1 Keycloak end-to-end checks. Started by scripts/test-p5a-keycloak.sh against an isolated,
// disposable Compose stack. Uses only synthetic users, households and randomly generated secrets.
import assert from 'node:assert/strict';
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';

const env = process.env;
const appBase = `http://localhost:${env.HTTP_PORT}`;
const keycloakBase = `http://localhost:${env.KEYCLOAK_PORT}`;
const realm = 'kuechenfuchs';
const issuer = `${keycloakBase}/realms/${realm}`;
const composeArgs = JSON.parse(env.P5A_COMPOSE_ARGS);
const testRedirectUri = `${appBase}/p5a-test-oauth-callback`;
const linkRedirectUri = `${appBase}/api/v1/integrations/keycloak/callback`;

let step = 0;
function check(label) {
  step += 1;
  console.log(`  ok ${String(step).padStart(2, '0')} ${label}`);
}

function compose(args) {
  return execFileSync('docker', ['compose', ...composeArgs, ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
}

function sql(query) {
  return compose([
    'exec', '-T', 'db', 'psql', '-v', 'ON_ERROR_STOP=1',
    '-U', env.POSTGRES_USER, '-d', env.POSTGRES_DB, '-Atc', query,
  ]).trim();
}

function kcadm(...args) {
  return compose(['exec', '-T', 'keycloak', '/opt/keycloak/bin/kcadm.sh', ...args]);
}

const randomValue = () => randomBytes(32).toString('base64url');
const s256 = (verifier) => createHash('sha256').update(verifier).digest('base64url');
const randomPassword = () => `Kc-${randomBytes(18).toString('base64url')}9!`;

/** Minimal browser: manual redirects and a host/path cookie jar (ports are ignored, as in browsers). */
class Browser {
  #cookies = new Map();

  has(name) {
    return [...this.#cookies.values()].some((cookie) => cookie.name === name);
  }

  async request(url, { method = 'GET', headers = {}, json, form, bearer } = {}) {
    const requestHeaders = { ...headers };
    const cookie = this.#cookieHeader(url);
    if (cookie) requestHeaders.Cookie = cookie;
    if (bearer) requestHeaders.Authorization = `Bearer ${bearer}`;
    let body;
    if (json !== undefined) {
      requestHeaders['Content-Type'] = 'application/json';
      body = JSON.stringify(json);
    } else if (form !== undefined) {
      body = new URLSearchParams(form);
    }

    const response = await fetch(url, { method, headers: requestHeaders, body, redirect: 'manual' });
    this.#store(url, response);
    const text = await response.text();
    return {
      status: response.status,
      location: response.headers.get('location'),
      headers: response.headers,
      text,
      json: () => JSON.parse(text),
    };
  }

  #cookieHeader(url) {
    const { hostname, pathname } = new URL(url);
    return [...this.#cookies.values()]
      .filter((cookie) => cookie.host === hostname && pathname.startsWith(cookie.path))
      .map((cookie) => `${cookie.name}=${cookie.value}`)
      .join('; ');
  }

  #store(url, response) {
    const host = new URL(url).hostname;
    for (const raw of response.headers.getSetCookie()) {
      const [pair, ...attributes] = raw.split(';');
      const separator = pair.indexOf('=');
      const name = pair.slice(0, separator).trim();
      const value = pair.slice(separator + 1).trim();
      let path = '/';
      let expired = value === '';
      for (const attribute of attributes) {
        const [key, attributeValue = ''] = attribute.trim().split('=');
        const normalized = key.toLowerCase();
        if (normalized === 'path') path = attributeValue || '/';
        if (normalized === 'max-age' && Number(attributeValue) <= 0) expired = true;
        if (normalized === 'expires' && Date.parse(attributeValue) < Date.now()) expired = true;
      }

      const id = `${host}|${name}|${path}`;
      if (expired) this.#cookies.delete(id);
      else this.#cookies.set(id, { host, name, value, path });
    }
  }
}

async function pwaLogin(browser, email, password) {
  let response = await browser.request(`${appBase}/api/auth/csrf`);
  assert.equal(response.status, 200);
  response = await browser.request(`${appBase}/api/auth/login`, {
    method: 'POST',
    headers: { 'X-KF-CSRF': response.json().requestToken },
    json: { email, password },
  });
  assert.equal(response.status, 200, `PWA login failed: ${response.text}`);
  return csrfToken(browser);
}

async function csrfToken(browser) {
  const response = await browser.request(`${appBase}/api/auth/csrf`);
  assert.equal(response.status, 200);
  return response.json().requestToken;
}

/** Performs the Keycloak login form and returns the redirect (with code/state) to the client. */
async function keycloakLogin(browser, authorizationUrl, username, password) {
  const page = await browser.request(authorizationUrl);
  assert.equal(page.status, 200, `Keycloak login page not shown: ${page.status}`);
  const match = page.text.match(/<form id="kc-form-login"[^>]*action="([^"]+)"/);
  assert.ok(match, 'Keycloak login form not found');
  const action = match[1].replaceAll('&amp;', '&');
  const response = await browser.request(action, { method: 'POST', form: { username, password, credentialId: '' } });
  assert.equal(response.status, 302,
    `Keycloak login did not redirect (HTTP ${response.status}): ${response.text.replace(/\s+/g, ' ').match(/<title>.*?<\/title>|kc-feedback-text">[^<]*|alert-error[^<]*<[^<]*/g)}`);
  return response.location;
}

async function startLink(browser, csrf, householdId) {
  return browser.request(`${appBase}/api/v1/integrations/keycloak/link-requests`, {
    method: 'POST',
    headers: { 'X-KF-CSRF': csrf },
    json: { householdId },
  });
}

/** Starts a link request, signs in at Keycloak and returns the callback URL (not yet followed). */
async function linkUntilCallback(browser, csrf, householdId, user) {
  const response = await startLink(browser, csrf, householdId);
  assert.equal(response.status, 200, `link start failed: ${response.text}`);
  assert.equal(response.headers.get('cache-control'), 'no-store');
  const { authorizationUrl } = response.json();
  const url = new URL(authorizationUrl);
  assert.equal(`${url.origin}${url.pathname}`, `${issuer}/protocol/openid-connect/auth`);
  assert.equal(url.searchParams.get('code_challenge_method'), 'S256');
  assert.equal(url.searchParams.get('prompt'), 'login');
  assert.equal(url.searchParams.get('redirect_uri'), linkRedirectUri);
  assert.ok(browser.has('KF.KeycloakLink'), 'browser binding cookie missing');
  // Fresh Keycloak session per login, as with a user who has no Keycloak SSO session yet.
  const callbackUrl = await keycloakLogin(new Browser(), authorizationUrl, user.username, user.password);
  assert.ok(callbackUrl.startsWith(`${linkRedirectUri}?`), `unexpected callback ${callbackUrl}`);
  return callbackUrl;
}

async function followCallback(browser, callbackUrl) {
  const response = await browser.request(callbackUrl);
  assert.equal(response.status, 302);
  assert.equal(response.headers.get('cache-control'), 'no-store');
  return new URL(response.location, appBase).searchParams.get('keycloakLink');
}

async function link(browser, csrf, householdId, user) {
  return followCallback(browser, await linkUntilCallback(browser, csrf, householdId, user));
}

/** Authorization code + PKCE for a confidential client; returns the raw token response. */
async function clientTokens({ realmName = realm, clientId, clientSecret, redirectUri, scope, user }) {
  const browser = new Browser();
  const verifier = randomValue();
  const state = randomValue();
  const base = `${keycloakBase}/realms/${realmName}/protocol/openid-connect`;
  const authorizationUrl = `${base}/auth?${new URLSearchParams({
    response_type: 'code',
    client_id: clientId,
    redirect_uri: redirectUri,
    scope,
    state,
    nonce: randomValue(),
    code_challenge: s256(verifier),
    code_challenge_method: 'S256',
  })}`;
  const location = new URL(await keycloakLogin(browser, authorizationUrl, user.username, user.password));
  assert.equal(location.searchParams.get('state'), state);
  const response = await browser.request(`${base}/token`, {
    method: 'POST',
    form: {
      grant_type: 'authorization_code',
      code: location.searchParams.get('code'),
      redirect_uri: redirectUri,
      client_id: clientId,
      client_secret: clientSecret,
      code_verifier: verifier,
    },
  });
  assert.equal(response.status, 200, `token exchange failed: ${response.text}`);
  return response.json();
}

const chatGptTokens = (user, scope = 'openid recipes:read') => clientTokens({
  clientId: 'chatgpt-actions',
  clientSecret: env.KEYCLOAK_CHATGPT_ACTIONS_CLIENT_SECRET,
  redirectUri: testRedirectUri,
  scope,
  user,
});

async function bearerCheck(token, householdId) {
  const response = await new Browser().request(
    `${appBase}/api/v1/integrations/chatgpt/households/${householdId}/authorization-check`,
    { bearer: token },
  );
  return response;
}

function decodePayload(token) {
  return JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8'));
}

function clientUuid(realmName, clientId) {
  return kcadm('get', 'clients', '-r', realmName, '-q', `clientId=${clientId}`, '--fields', 'id', '--format', 'csv', '--noquotes').trim();
}

function createKeycloakUser(realmName, username, email) {
  const password = randomPassword();
  kcadm('create', 'users', '-r', realmName, '-s', `username=${username}`, '-s', 'enabled=true',
    '-s', `email=${email}`, '-s', 'emailVerified=true', '-s', 'firstName=P5a', '-s', 'lastName=Test');
  kcadm('set-password', '-r', realmName, '--username', username, '--new-password', password);
  const id = kcadm('get', 'users', '-r', realmName, '-q', `username=${username}`, '-q', 'exact=true',
    '--fields', 'id', '--format', 'csv', '--noquotes').trim();
  return { username, password, id };
}

async function main() {
  console.log('Configuring synthetic Keycloak test users and a test-only redirect URI...');
  kcadm('config', 'credentials', '--server', 'http://localhost:8080', '--realm', 'master',
    '--user', env.KEYCLOAK_ADMIN_USERNAME, '--password', env.KEYCLOAK_ADMIN_PASSWORD);
  const chatGptClient = clientUuid(realm, 'chatgpt-actions');
  kcadm('update', `clients/${chatGptClient}`, '-r', realm, '-s', `redirectUris=["${testRedirectUri}"]`);
  const alice = createKeycloakUser(realm, 'p5a-alice', 'p5a-alice@example.test');
  const bob = createKeycloakUser(realm, 'p5a-bob', 'p5a-bob@example.test');
  // Same e-mail address as the Küchenfuchs user: must never lead to an automatic link.
  const mallory = createKeycloakUser(realm, 'p5a-mallory', env.INITIAL_ADMIN_EMAIL);

  console.log('Running PWA and linking checks...');
  const pwa = new Browser();
  let csrf = await pwaLogin(pwa, env.INITIAL_ADMIN_EMAIL, env.INITIAL_ADMIN_PASSWORD);
  const session = (await pwa.request(`${appBase}/api/auth/session`)).json();
  const userId = session.userId;
  const homeId = session.households[0].householdId;
  check('PWA cookie login and session still work with Keycloak enabled');

  const secondHomeId = randomUUID();
  const foreignHomeId = randomUUID();
  sql(`INSERT INTO "Households" ("Id","Name","CreatedAt") VALUES ('${secondHomeId}','P5a second household',now()),('${foreignHomeId}','P5a foreign household',now());
       INSERT INTO "HouseholdMembers" ("HouseholdId","UserId","Role","JoinedAt") VALUES ('${secondHomeId}','${userId}','Member',now());`);

  let response = await pwa.request(`${appBase}/api/v1/integrations/keycloak/link-requests`, {
    method: 'POST', json: { householdId: homeId },
  });
  assert.equal(response.status, 400);
  check('link request without CSRF token is rejected');

  response = await startLink(new Browser(), csrf, homeId);
  assert.equal(response.status, 401);
  check('link request without PWA session is rejected');

  assert.equal((await startLink(pwa, csrf, foreignHomeId)).status, 404);
  assert.equal((await startLink(pwa, csrf, randomUUID())).status, 404);
  check('link request for a foreign or unknown household is hidden (404)');

  let callbackUrl = await linkUntilCallback(pwa, csrf, homeId, alice);
  const tampered = new URL(callbackUrl);
  tampered.searchParams.set('state', randomValue());
  assert.equal(await followCallback(pwa, tampered.toString()), 'failed');
  check('callback with manipulated state is rejected');

  assert.equal(await followCallback(new Browser(), callbackUrl), 'failed');
  check('callback from a different browser (missing binding cookie) is rejected');

  assert.equal(await followCallback(pwa, callbackUrl), 'failed');
  check('consumed link request cannot be reused, even from the original browser');

  const manipulatedCode = new URL(await linkUntilCallback(pwa, csrf, homeId, alice));
  manipulatedCode.searchParams.set('code', 'manipulated-code');
  assert.equal(await followCallback(pwa, manipulatedCode.toString()), 'failed');
  check('callback with a manipulated authorization code is rejected');

  callbackUrl = await linkUntilCallback(pwa, csrf, homeId, alice);
  sql(`UPDATE "ExternalIdentityLinkRequests" SET "ExpiresAt" = now() - interval '1 minute' WHERE "ConsumedAt" IS NULL`);
  assert.equal(await followCallback(pwa, callbackUrl), 'expired');
  check('expired link request is rejected');

  const otherUserId = randomUUID();
  sql(`INSERT INTO "AspNetUsers" ("Id","UserName","NormalizedUserName","Email","NormalizedEmail","EmailConfirmed","SecurityStamp","ConcurrencyStamp","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","CreatedAt","IsEnabled")
       VALUES ('${otherUserId}','p5a-other@example.test','P5A-OTHER@EXAMPLE.TEST','p5a-other@example.test','P5A-OTHER@EXAMPLE.TEST',true,'${randomUUID()}','${randomUUID()}',false,false,true,0,now(),true);
       INSERT INTO "HouseholdMembers" ("HouseholdId","UserId","Role","JoinedAt") VALUES ('${foreignHomeId}','${otherUserId}','Owner',now());
       INSERT INTO "ExternalIdentityLinks" ("Id","Issuer","Subject","UserId","HouseholdId","CreatedAt") VALUES ('${randomUUID()}','${issuer}','${bob.id}','${otherUserId}','${foreignHomeId}',now());`);
  assert.equal(await link(pwa, csrf, homeId, bob), 'conflict');
  check('Keycloak identity already linked to another user is rejected (conflict)');

  assert.equal(await link(pwa, csrf, homeId, alice), 'linked');
  const links = sql(`SELECT "Subject" || '|' || "UserId" || '|' || "HouseholdId" FROM "ExternalIdentityLinks" WHERE "Issuer"='${issuer}' AND "UserId"='${userId}' AND "RevokedAt" IS NULL`);
  assert.equal(links, `${alice.id}|${userId}|${homeId}`);
  check('explicit link succeeds and binds (issuer, subject) to the user and exactly one household');

  response = await pwa.request(`${appBase}/api/v1/integrations/keycloak/links`);
  assert.equal(response.status, 200);
  const [activeLink] = response.json();
  assert.equal(activeLink.householdId, homeId);
  assert.equal(activeLink.revokedAt, null);
  assert.ok(!response.text.includes(alice.id), 'link list must not expose the external subject');
  check('linked identities are listed for the signed-in user');

  assert.equal(await link(pwa, csrf, secondHomeId, alice), 'conflict');
  check('second active link for the same user and issuer is rejected');

  assert.equal(sql(`SELECT count(*) FROM "ExternalIdentityLinks" WHERE "Subject"='${mallory.id}'`), '0');
  check('no link was created from the matching e-mail address');

  console.log('Running bearer checks...');
  const aliceTokens = await chatGptTokens(alice);
  const accessToken = aliceTokens.access_token;
  const payload = decodePayload(accessToken);
  assert.equal(payload.iss, issuer);
  assert.ok([payload.aud].flat().includes('kuechenfuchs-api'));
  assert.equal(payload.email, undefined, 'access token must not carry e-mail claims');

  response = await bearerCheck(accessToken, homeId);
  assert.equal(response.status, 200, response.text);
  assert.deepEqual(response.json(), { status: 'authorized', householdId: homeId, scopes: ['openid', 'recipes:read'] });
  assert.equal(response.headers.get('cache-control'), 'no-store');
  check('bearer token of the linked identity is accepted for the bound household');

  assert.equal((await bearerCheck(accessToken, secondHomeId)).status, 404);
  check('other household of the same user is not reachable with this authorization (404)');

  assert.equal((await bearerCheck(accessToken, foreignHomeId)).status, 404);
  check('foreign household is hidden (404)');

  assert.equal((await bearerCheck(undefined, homeId)).status, 401);
  assert.equal((await bearerCheck('not-a-token', homeId)).status, 401);
  check('missing or malformed bearer token is rejected (401)');

  const [header, body, signature] = accessToken.split('.');
  const forged = Buffer.from(JSON.stringify({ ...payload, scope: 'openid recipes:read recipes:write' })).toString('base64url');
  assert.equal((await bearerCheck(`${header}.${forged}.${signature}`, homeId)).status, 401);
  assert.equal((await bearerCheck(`${header}.${body}.${signature.slice(0, -4)}AAAA`, homeId)).status, 401);
  check('manipulated payload or signature is rejected (401)');

  assert.equal((await bearerCheck(aliceTokens.id_token, homeId)).status, 401);
  check('ID token is not accepted as access token (401)');

  const noScopeTokens = await chatGptTokens(alice, 'openid');
  assert.equal((await bearerCheck(noScopeTokens.access_token, homeId)).status, 403);
  check('token without recipes:read scope is forbidden (403)');

  const malloryTokens = await chatGptTokens(mallory);
  assert.equal((await bearerCheck(malloryTokens.access_token, homeId)).status, 401);
  check('valid token of an unlinked Keycloak identity (same e-mail as the user) is rejected (401)');

  const linkClientTokens = await clientTokens({
    clientId: 'kuechenfuchs-link',
    clientSecret: env.KEYCLOAK_LINK_CLIENT_SECRET,
    redirectUri: linkRedirectUri,
    scope: 'openid',
    user: alice,
  });
  assert.ok(![decodePayload(linkClientTokens.access_token).aud].flat().includes('kuechenfuchs-api'));
  assert.equal((await bearerCheck(linkClientTokens.access_token, homeId)).status, 401);
  check('token with wrong audience (linking client) is rejected (401)');

  const foreignRealm = 'p5a-foreign';
  kcadm('create', 'realms', '-s', `realm=${foreignRealm}`, '-s', 'enabled=true');
  const foreignSecret = randomValue();
  kcadm('create', 'clients', '-r', foreignRealm, '-s', 'clientId=chatgpt-actions', '-s', 'enabled=true',
    '-s', 'publicClient=false', '-s', `secret=${foreignSecret}`, '-s', 'standardFlowEnabled=true',
    '-s', 'directAccessGrantsEnabled=false', '-s', `redirectUris=["${testRedirectUri}"]`);
  kcadm('create', `clients/${clientUuid(foreignRealm, 'chatgpt-actions')}/protocol-mappers/models`, '-r', foreignRealm,
    '-s', 'name=audience', '-s', 'protocol=openid-connect', '-s', 'protocolMapper=oidc-audience-mapper',
    '-s', 'config."included.custom.audience"=kuechenfuchs-api', '-s', 'config."access.token.claim"=true');
  const foreignUser = createKeycloakUser(foreignRealm, 'p5a-alice', 'p5a-alice@example.test');
  const foreignIssuer = `${keycloakBase}/realms/${foreignRealm}`;
  // Even an (inadmissible) link row for the foreign issuer must not make its tokens acceptable.
  sql(`INSERT INTO "ExternalIdentityLinks" ("Id","Issuer","Subject","UserId","HouseholdId","CreatedAt") VALUES ('${randomUUID()}','${foreignIssuer}','${foreignUser.id}','${userId}','${homeId}',now())`);
  const foreignTokens = await clientTokens({
    realmName: foreignRealm,
    clientId: 'chatgpt-actions',
    clientSecret: foreignSecret,
    redirectUri: testRedirectUri,
    scope: 'openid',
    user: foreignUser,
  });
  const foreignPayload = decodePayload(foreignTokens.access_token);
  assert.equal(foreignPayload.iss, foreignIssuer);
  assert.ok([foreignPayload.aud].flat().includes('kuechenfuchs-api'));
  assert.equal((await bearerCheck(foreignTokens.access_token, homeId)).status, 401);
  sql(`DELETE FROM "ExternalIdentityLinks" WHERE "Issuer"='${foreignIssuer}'`);
  check('token with wrong issuer is rejected (401)');

  kcadm('update', `clients/${chatGptClient}`, '-r', realm, '-s', 'attributes."access.token.lifespan"=10');
  const shortLived = await chatGptTokens(alice);
  kcadm('update', `clients/${chatGptClient}`, '-r', realm, '-s', 'attributes."access.token.lifespan"=300');
  assert.equal((await bearerCheck(shortLived.access_token, homeId)).status, 200);
  await new Promise((resolve) => setTimeout(resolve, 13_000));
  assert.equal((await bearerCheck(shortLived.access_token, homeId)).status, 401);
  check('expired token is rejected (401)');

  assert.equal((await pwa.request(`${appBase}/api/v1/me`, { bearer: accessToken })).status, 200);
  assert.equal((await new Browser().request(`${appBase}/api/v1/me`, { bearer: accessToken })).status, 401);
  assert.equal((await pwa.request(`${appBase}/api/v1/integrations/chatgpt/households/${homeId}/authorization-check`)).status, 401);
  check('bearer token does not open cookie endpoints and the PWA cookie does not open bearer endpoints');

  sql(`DELETE FROM "HouseholdMembers" WHERE "HouseholdId"='${homeId}' AND "UserId"='${userId}'`);
  assert.equal((await bearerCheck(accessToken, homeId)).status, 404);
  sql(`INSERT INTO "HouseholdMembers" ("HouseholdId","UserId","Role","JoinedAt") VALUES ('${homeId}','${userId}','Owner',now())`);
  assert.equal((await bearerCheck(accessToken, homeId)).status, 200);
  check('removed household membership blocks the still-valid token immediately (404)');

  sql(`UPDATE "AspNetUsers" SET "IsEnabled"=false WHERE "Id"='${userId}'`);
  assert.equal((await bearerCheck(accessToken, homeId)).status, 401);
  sql(`UPDATE "AspNetUsers" SET "IsEnabled"=true, "LockoutEnd"=now() + interval '1 hour' WHERE "Id"='${userId}'`);
  assert.equal((await bearerCheck(accessToken, homeId)).status, 401);
  sql(`UPDATE "AspNetUsers" SET "LockoutEnd"=NULL WHERE "Id"='${userId}'`);
  assert.equal((await bearerCheck(accessToken, homeId)).status, 200);
  check('disabled or locked user is rejected immediately (401)');

  const linkId = activeLink.id;
  response = await pwa.request(`${appBase}/api/v1/integrations/keycloak/links/${linkId}`, { method: 'DELETE' });
  assert.equal(response.status, 400);
  check('revocation without CSRF token is rejected');

  csrf = await csrfToken(pwa);
  response = await pwa.request(`${appBase}/api/v1/integrations/keycloak/links/${randomUUID()}`, {
    method: 'DELETE', headers: { 'X-KF-CSRF': csrf },
  });
  assert.equal(response.status, 404);
  response = await pwa.request(`${appBase}/api/v1/integrations/keycloak/links/${linkId}`, {
    method: 'DELETE', headers: { 'X-KF-CSRF': csrf },
  });
  assert.equal(response.status, 204);
  assert.equal((await bearerCheck(accessToken, homeId)).status, 401);
  const revoked = (await pwa.request(`${appBase}/api/v1/integrations/keycloak/links`)).json();
  assert.ok(revoked.find((item) => item.id === linkId).revokedAt);
  check('revoked link stops the still-valid token immediately (401)');

  assert.equal(await link(pwa, csrf, secondHomeId, alice), 'linked');
  const relinked = await chatGptTokens(alice);
  assert.equal((await bearerCheck(relinked.access_token, secondHomeId)).status, 200);
  assert.equal((await bearerCheck(relinked.access_token, homeId)).status, 404);
  check('new explicit link after revocation binds the newly chosen household only');

  const keycloakUsers = compose(['exec', '-T', 'keycloak-db', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'keycloak', '-d', 'keycloak',
    '-Atc', "SELECT count(*) FROM user_entity WHERE username = 'p5a-alice'"]).trim();
  assert.notEqual(keycloakUsers, '0', 'Keycloak users must be stored in the separate keycloak database');
  assert.equal(sql(`SELECT count(*) FROM information_schema.tables WHERE table_name = 'user_entity'`), '0');
  compose(['rm', '--stop', '--force', 'keycloak']);
  compose(['up', '--detach', 'keycloak']);
  for (let attempt = 0; ; attempt += 1) {
    const ready = await fetch(`${issuer}/.well-known/openid-configuration`).then((r) => r.ok, () => false);
    if (ready) break;
    assert.ok(attempt < 60, 'Keycloak did not come back after recreation');
    await new Promise((resolve) => setTimeout(resolve, 5_000));
  }
  const afterRestart = await chatGptTokens(alice);
  assert.equal((await bearerCheck(afterRestart.access_token, secondHomeId)).status, 200);
  check('Keycloak realm, users and client secrets persist in the separate database across container recreation');

  response = await pwa.request(`${appBase}/api/auth/logout`, { method: 'POST', headers: { 'X-KF-CSRF': csrf } });
  assert.equal(response.status, 204);
  assert.equal((await pwa.request(`${appBase}/api/auth/session`)).status, 401);
  const again = new Browser();
  await pwaLogin(again, env.INITIAL_ADMIN_EMAIL, env.INITIAL_ADMIN_PASSWORD);
  assert.equal((await again.request(`${appBase}/api/v1/households/${homeId}`)).status, 200);
  check('PWA logout and renewed cookie login still work');

  const logs = compose(['logs', 'api']);
  for (const secret of [accessToken, aliceTokens.id_token, env.KEYCLOAK_LINK_CLIENT_SECRET, alice.password]) {
    assert.ok(!logs.includes(secret), 'API logs must not contain tokens or secrets');
  }
  check('API logs contain no tokens, client secrets or passwords');

  console.log(`P5a Keycloak integration checks passed (${step} checks).`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});

// P5a.1 Keycloak end-to-end checks. Started by scripts/test-p5a-keycloak.sh against an isolated,
// disposable Compose stack. Uses only synthetic users, households and randomly generated secrets.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import {
  Browser, clientUuid as findClientUuid, createChecker, createCompose, createKeycloakUser as createUser,
  csrfToken as fetchCsrfToken, decodePayload, keycloakLogin, pwaLogin as login, randomValue, s256, sleep,
} from './lib/p5a-test-support.mjs';

const env = process.env;
const appBase = `http://localhost:${env.HTTP_PORT}`;
// Keycloak is served by the Küchenfuchs Caddy under the same origin; the admin port is loopback-only.
const keycloakBase = `${appBase}/auth`;
const keycloakAdminBase = `http://localhost:${env.KEYCLOAK_ADMIN_PORT}/auth`;
const realm = 'kuechenfuchs';
const issuer = `${keycloakBase}/realms/${realm}`;
const testRedirectUri = `${appBase}/p5a-test-oauth-callback`;
const linkRedirectUri = `${appBase}/api/v1/integrations/keycloak/callback`;
const { compose, sql, kcadm } = createCompose(JSON.parse(env.P5A_COMPOSE_ARGS), env);
const check = createChecker();

// Tokens are only accepted from the next whole second after linking (+ clock skew, 0 in this test).
const waitForNewTokenGeneration = () => sleep(2_100);
const pwaLogin = (browser, email, password) => login(browser, appBase, email, password);
const csrfToken = (browser) => fetchCsrfToken(browser, appBase);
const createKeycloakUser = (realmName, username, email) => createUser(kcadm, realmName, username, email);
const clientUuid = (realmName, clientId) => findClientUuid(kcadm, realmName, clientId);

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
  const target = new URL(response.location, appBase);
  assert.equal(target.pathname, '/settings/chatgpt');
  return target.searchParams.get('keycloakLink');
}

async function link(browser, csrf, householdId, user) {
  return followCallback(browser, await linkUntilCallback(browser, csrf, householdId, user));
}

/** Authorization code + PKCE for a confidential client; returns the raw token response. */
async function clientTokens({ realmName = realm, baseUrl = keycloakBase, clientId, clientSecret, redirectUri, scope, user }) {
  const browser = new Browser();
  const verifier = randomValue();
  const state = randomValue();
  const base = `${baseUrl}/realms/${realmName}/protocol/openid-connect`;
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

async function main() {
  console.log('Configuring synthetic Keycloak test users and a test-only redirect URI...');
  kcadm('config', 'credentials', '--server', 'http://localhost:8080/auth', '--realm', 'master',
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

  const preLinkToken = (await chatGptTokens(alice)).access_token;
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
  assert.equal((await bearerCheck(preLinkToken, homeId)).status, 401);
  check('token issued before the link existed is rejected after linking (401)');

  await waitForNewTokenGeneration();
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
  // A second realm is not routed through Caddy; it gets its own frontend URL on the loopback admin port.
  kcadm('create', 'realms', '-s', `realm=${foreignRealm}`, '-s', 'enabled=true',
    '-s', `attributes.frontendUrl=${keycloakAdminBase}`);
  const foreignSecret = randomValue();
  kcadm('create', 'clients', '-r', foreignRealm, '-s', 'clientId=chatgpt-actions', '-s', 'enabled=true',
    '-s', 'publicClient=false', '-s', `secret=${foreignSecret}`, '-s', 'standardFlowEnabled=true',
    '-s', 'directAccessGrantsEnabled=false', '-s', `redirectUris=["${testRedirectUri}"]`);
  kcadm('create', `clients/${clientUuid(foreignRealm, 'chatgpt-actions')}/protocol-mappers/models`, '-r', foreignRealm,
    '-s', 'name=audience', '-s', 'protocol=openid-connect', '-s', 'protocolMapper=oidc-audience-mapper',
    '-s', 'config."included.custom.audience"=kuechenfuchs-api', '-s', 'config."access.token.claim"=true');
  const foreignUser = createKeycloakUser(foreignRealm, 'p5a-alice', 'p5a-alice@example.test');
  const foreignIssuer = `${keycloakAdminBase}/realms/${foreignRealm}`;
  // Even an (inadmissible) link row for the foreign issuer must not make its tokens acceptable.
  sql(`INSERT INTO "ExternalIdentityLinks" ("Id","Issuer","Subject","UserId","HouseholdId","CreatedAt") VALUES ('${randomUUID()}','${foreignIssuer}','${foreignUser.id}','${userId}','${homeId}',now())`);
  const foreignTokens = await clientTokens({
    realmName: foreignRealm,
    baseUrl: keycloakAdminBase,
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
  await sleep(13_000);
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
  assert.equal(response.status, 200);
  assert.deepEqual(response.json(), { revoked: true, providerSessionsEnded: true });
  // Re-linking scenario: (1) token works, (2) revoked, (3) re-linked, (4) old token stays invalid,
  // (5) new token works for the newly chosen household only.
  assert.equal((await bearerCheck(accessToken, homeId)).status, 401);
  const revoked = (await pwa.request(`${appBase}/api/v1/integrations/keycloak/links`)).json();
  assert.ok(revoked.find((item) => item.id === linkId).revokedAt);
  check('revoked link stops the still-valid token immediately (401)');

  const issuedWhileUnlinked = (await chatGptTokens(alice)).access_token;
  assert.equal((await bearerCheck(issuedWhileUnlinked, homeId)).status, 401);
  assert.equal(await link(pwa, csrf, secondHomeId, alice), 'linked');
  assert.equal(sql(`SELECT count(*) FROM "ExternalIdentityLinks" WHERE "Subject"='${alice.id}' AND "RevokedAt" IS NULL AND "HouseholdId"='${secondHomeId}'`), '1');
  check('same Keycloak identity can be linked again explicitly for another household');

  for (const oldToken of [accessToken, issuedWhileUnlinked]) {
    assert.equal(decodePayload(oldToken).exp * 1000 > Date.now(), true, 'old token must still be unexpired');
    assert.equal((await bearerCheck(oldToken, secondHomeId)).status, 401);
    assert.equal((await bearerCheck(oldToken, homeId)).status, 401);
  }
  check('unexpired tokens issued before revocation or while unlinked stay invalid after re-linking (401)');

  await waitForNewTokenGeneration();
  const relinked = await chatGptTokens(alice);
  assert.equal((await bearerCheck(relinked.access_token, secondHomeId)).status, 200);
  assert.equal((await bearerCheck(relinked.access_token, homeId)).status, 404);
  check('newly issued token works for the newly chosen household only');

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
  assert.ok(!/[?&]code=[A-Za-z0-9.-]{20,}/.test(logs), 'API logs must not contain authorization codes');
  check('API logs contain no tokens, client secrets or passwords');

  console.log(`P5a Keycloak integration checks passed (${check.count()} checks).`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});

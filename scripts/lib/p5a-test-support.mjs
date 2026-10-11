// Shared helpers for the P5a Keycloak/ChatGPT end-to-end tests. Test-only: synthetic users,
// random secrets, disposable Compose stacks. Uses the platform fetch; TLS trust via NODE_EXTRA_CA_CERTS.
import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { execFileSync } from 'node:child_process';

export const randomValue = () => randomBytes(32).toString('base64url');
export const s256 = (verifier) => createHash('sha256').update(verifier).digest('base64url');
export const randomPassword = () => `Kc-${randomBytes(18).toString('base64url')}9!`;
export const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
export const decodePayload = (token) => JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8'));

export function createChecker() {
  let step = 0;
  const check = (label) => {
    step += 1;
    console.log(`  ok ${String(step).padStart(2, '0')} ${label}`);
  };
  check.count = () => step;
  return check;
}

export function createCompose(composeArgs, env) {
  const compose = (args, options = {}) => execFileSync('docker', ['compose', ...composeArgs, ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    ...options,
  });
  const sql = (query) => compose([
    'exec', '-T', 'db', 'psql', '-v', 'ON_ERROR_STOP=1',
    '-U', env.POSTGRES_USER, '-d', env.POSTGRES_DB, '-Atc', query,
  ]).trim();
  const kcadm = (...args) => compose(['exec', '-T', 'keycloak', '/opt/keycloak/bin/kcadm.sh', ...args]);
  return { compose, sql, kcadm };
}

/** Minimal browser: manual redirects and a host/path cookie jar (ports are ignored, as in browsers). */
export class Browser {
  #cookies = new Map();

  has(name) {
    return [...this.#cookies.values()].some((cookie) => cookie.name === name);
  }

  rawSetCookies = [];

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
      this.rawSetCookies.push(raw);
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

export async function csrfToken(browser, appBase) {
  const response = await browser.request(`${appBase}/api/auth/csrf`);
  assert.equal(response.status, 200);
  return response.json().requestToken;
}

export async function pwaLogin(browser, appBase, email, password) {
  const response = await browser.request(`${appBase}/api/auth/login`, {
    method: 'POST',
    headers: { 'X-KF-CSRF': await csrfToken(browser, appBase) },
    json: { email, password },
  });
  assert.equal(response.status, 200, `PWA login failed: ${response.text}`);
  return csrfToken(browser, appBase);
}

/** Performs the Keycloak login form and returns the redirect target (with code/state or error). */
export async function keycloakLogin(browser, authorizationUrl, username, password) {
  const page = await browser.request(authorizationUrl);
  if (page.status === 302) return page.location;
  assert.equal(page.status, 200, `Keycloak login page not shown: ${page.status}`);
  const match = page.text.match(/<form id="kc-form-login"[^>]*action="([^"]+)"/);
  assert.ok(match, 'Keycloak login form not found');
  const action = match[1].replaceAll('&amp;', '&');
  const response = await browser.request(action, { method: 'POST', form: { username, password, credentialId: '' } });
  assert.equal(response.status, 302,
    `Keycloak login did not redirect (HTTP ${response.status}): ${response.text.replace(/\s+/g, ' ').match(/<title>.*?<\/title>|kc-feedback-text">[^<]*/g)}`);
  return response.location;
}

export function createKeycloakUser(kcadm, realmName, username, email) {
  const password = randomPassword();
  kcadm('create', 'users', '-r', realmName, '-s', `username=${username}`, '-s', 'enabled=true',
    '-s', `email=${email}`, '-s', 'emailVerified=true', '-s', 'firstName=P5a', '-s', 'lastName=Test');
  kcadm('set-password', '-r', realmName, '--username', username, '--new-password', password);
  const id = kcadm('get', 'users', '-r', realmName, '-q', `username=${username}`, '-q', 'exact=true',
    '--fields', 'id', '--format', 'csv', '--noquotes').trim();
  return { username, password, id };
}

export function clientUuid(kcadm, realmName, clientId) {
  return kcadm('get', 'clients', '-r', realmName, '-q', `clientId=${clientId}`, '--fields', 'id', '--format', 'csv', '--noquotes').trim();
}

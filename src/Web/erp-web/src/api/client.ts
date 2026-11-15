import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';
import { browser } from '../app/browser';

// Clientul API generat din OpenAPI (UI-03), pe aceeasi origine, cu cookie-ul de sesiune (08, 8.7).
export const antiforgeryHeader = 'X-XSRF-TOKEN';
export const antiforgeryCookie = 'XSRF-TOKEN';

/** Codurile din ProblemDetails care cer o pagina de cont inainte de orice alt ecran. */
export const accountRedirects: Readonly<Record<string, string>> = {
  IDS_PASSWORD_CHANGE_REQUIRED: '/account/password',
  IDS_TWO_FACTOR_SETUP_REQUIRED: '/account/2fa',
};

const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export function readCookie(name: string): string | undefined {
  const prefix = `${name}=`;
  const value = document.cookie
    .split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));
  return value === undefined ? undefined : decodeURIComponent(value.slice(prefix.length));
}

/** Tokenul antiforgery pentru scrieri: din cookie-ul XSRF-TOKEN, cerut de la server daca lipseste sau daca se cere unul nou. */
export async function antiforgeryToken(fresh = false): Promise<string> {
  let token = fresh ? undefined : readCookie(antiforgeryCookie);
  if (token === undefined) {
    await fetch('/api/antiforgery', { credentials: 'same-origin' });
    token = readCookie(antiforgeryCookie);
  }
  if (token === undefined) {
    throw new Error('Tokenul antiforgery lipsește.');
  }
  return token;
}

export function loginUrl(): string {
  const current = browser.currentPath();
  return current === '/' ? '/account/login' : `/account/login?returnUrl=${encodeURIComponent(current)}`;
}

/** Fara sesiune sau cu pasi de cont in asteptare, browserul merge la paginile Razor /account/*. */
export const authMiddleware: Middleware = {
  async onRequest({ request }) {
    if (unsafeMethods.has(request.method)) {
      request.headers.set(antiforgeryHeader, await antiforgeryToken());
    }
    return request;
  },
  async onResponse({ response }) {
    if (response.status === 401) {
      browser.navigate(loginUrl());
    } else if (response.status === 403) {
      const code = await problemCode(response.clone());
      const target = code === undefined ? undefined : accountRedirects[code];
      if (target !== undefined) {
        browser.navigate(target);
      }
    }
    return response;
  },
};

async function problemCode(response: Response): Promise<string | undefined> {
  if (!response.headers.get('content-type')?.includes('json')) {
    return undefined;
  }
  const body: unknown = await response.json().catch(() => undefined);
  return typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : undefined;
}

export const api = createClient<paths>({ baseUrl: '', credentials: 'same-origin' });
api.use(authMiddleware);

import { afterEach, describe, expect, it, vi } from 'vitest';
import { browser } from '../app/browser';
import { authMiddleware, loginUrl } from './client';

type OnResponse = NonNullable<typeof authMiddleware.onResponse>;
type OnRequest = NonNullable<typeof authMiddleware.onRequest>;

function respond(status: number, body?: object) {
  const response = new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: body === undefined ? {} : { 'content-type': 'application/problem+json' },
  });
  return (authMiddleware.onResponse as OnResponse)({ response } as Parameters<OnResponse>[0]);
}

describe('clientul API', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });

  it('trimite la autentificare la 401, cu pagina curentă ca returnUrl', async () => {
    vi.spyOn(browser, 'currentPath').mockReturnValue('/parteneri?pagina=2');
    const navigate = vi.spyOn(browser, 'navigate').mockImplementation(() => undefined);

    await respond(401);

    expect(navigate).toHaveBeenCalledWith('/account/login?returnUrl=%2Fparteneri%3Fpagina%3D2');
  });

  it('de pe Acasă trimite la autentificare fără returnUrl', () => {
    vi.spyOn(browser, 'currentPath').mockReturnValue('/');
    expect(loginUrl()).toBe('/account/login');
  });

  it.each([
    ['IDS_PASSWORD_CHANGE_REQUIRED', '/account/password'],
    ['IDS_TWO_FACTOR_SETUP_REQUIRED', '/account/2fa'],
  ])('la 403 %s trimite la %s', async (code, target) => {
    const navigate = vi.spyOn(browser, 'navigate').mockImplementation(() => undefined);
    await respond(403, { code });
    expect(navigate).toHaveBeenCalledWith(target);
  });

  it('un 403 fără drept nu părăsește aplicația', async () => {
    const navigate = vi.spyOn(browser, 'navigate').mockImplementation(() => undefined);
    const response = await respond(403, { code: 'CORE_FORBIDDEN' });
    expect(navigate).not.toHaveBeenCalled();
    expect(response).toBeInstanceOf(Response);
  });

  it('scrierile primesc antetul antiforgery din cookie; citirile nu', async () => {
    document.cookie = 'XSRF-TOKEN=token%2Ftest';
    const onRequest = authMiddleware.onRequest as OnRequest;

    const post = (await onRequest({ request: new Request('https://erp/api/x', { method: 'POST' }) } as Parameters<OnRequest>[0])) as Request;
    const get = (await onRequest({ request: new Request('https://erp/api/x') } as Parameters<OnRequest>[0])) as Request;

    expect(post.headers.get('X-XSRF-TOKEN')).toBe('token/test');
    expect(get.headers.has('X-XSRF-TOKEN')).toBe(false);
  });
});

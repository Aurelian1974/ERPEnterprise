import { t } from '../i18n';

/** Antetul firmei pe care se verifica permisiunile; 0 = nivelul instalarii (grupuri si firme noi). */
export const companyHeader = 'X-Company-Id';

/** Eroarea unui apel API, din ProblemDetails (NET-10): mesajul in „title”, codul stabil, erorile pe camp. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;

  constructor(status: number, problem: unknown) {
    const body = typeof problem === 'object' && problem !== null ? (problem as Record<string, unknown>) : {};
    super(typeof body.title === 'string' ? body.title : t('app.loadError'));
    this.status = status;
    this.code = typeof body.code === 'string' ? body.code : undefined;
    this.fieldErrors = typeof body.errors === 'object' && body.errors !== null ? (body.errors as Record<string, string[]>) : {};
  }
}

/** Datele raspunsului sau ApiError; folosit de toate apelurile din ecrane. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.data !== undefined) {
    return result.data;
  }
  throw new ApiError(result.response.status, result.error);
}

/** Optiunile de cerere pentru o firma anume (sau instalarea), independent de firma curenta din antet. */
export function forCompany(companyId: number) {
  return { headers: { [companyHeader]: String(companyId) } };
}

import { antiforgeryToken } from '../api/client';

/**
 * Iesirea: formular POST catre pagina Razor /account/logout, cu tokenul antiforgery (08, 8.7). Serverul inchide sesiunea
 * si trimite browserul la /account/login.
 */
export async function signOut(): Promise<void> {
  const token = await antiforgeryToken(true);
  const form = document.createElement('form');
  form.method = 'post';
  form.action = '/account/logout';
  const field = document.createElement('input');
  field.type = 'hidden';
  field.name = '__RequestVerificationToken';
  field.value = token;
  form.append(field);
  document.body.append(form);
  form.submit();
}

import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { e2eUser, passwordFile } from './global-setup';

async function signIn(page: Page, userName: string, password: string) {
  await page.locator('#UserName').fill(userName);
  await page.locator('#Password').fill(password);
  await page.locator('button[type="submit"]').click();
}

test('autentificare cu parola temporară, schimbarea parolei, apoi Acasă și ieșirea', async ({ page }) => {
  const temporary = readFileSync(passwordFile, 'utf8').trim();
  const newPassword = `Nou#${Date.now().toString(36)}Ab9`;

  // Fara sesiune, aplicatia trimite la pagina de autentificare.
  await page.goto('/');
  await expect(page).toHaveURL(/\/account\/login/);

  // Numele de utilizator se accepta cu orice majuscule.
  await signIn(page, e2eUser.toUpperCase(), temporary);
  await expect(page).toHaveURL(/\/account\/password$/);
  await page.locator('#NewPassword').fill(newPassword);
  await page.locator('#ConfirmPassword').fill(newPassword);
  await page.locator('form button[type="submit"].primary').click();

  // Dupa schimbare toate sesiunile se inchid; autentificarea se reia cu parola noua.
  await expect(page).toHaveURL(/\/account\/login\?Message=changed/);
  await signIn(page, e2eUser, newPassword);

  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Bună ziua, Utilizator' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Meniul principal' }).getByRole('link', { name: 'Acasă' })).toBeVisible();

  // O ruta inexistenta ramane in aplicatie.
  await page.goto('/nu-exista');
  await expect(page.getByRole('heading', { level: 1, name: 'Pagina nu există' })).toBeVisible();

  await page.getByRole('button', { name: 'Meniul utilizatorului' }).click();
  await page.getByRole('menuitem', { name: 'Ieșire' }).click();
  await expect(page).toHaveURL(/\/account\/login\?Message=signedout/);

  // Dupa iesire, aplicatia cere din nou autentificarea.
  await page.goto('/');
  await expect(page).toHaveURL(/\/account\/login/);
});

import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';

/** Autentificarea cu parola temporara, schimbarea ei si a doua autentificare (paginile Razor /account/*). */
export async function signInFirstTime(page: Page, userName: string, passwordFile: string): Promise<void> {
  const temporary = readFileSync(passwordFile, 'utf8').trim();
  const newPassword = `Nou#${Date.now().toString(36)}Ab9`;
  await page.goto('/');
  await expect(page).toHaveURL(/\/account\/login/);
  await signIn(page, userName, temporary);
  await expect(page).toHaveURL(/\/account\/password$/);
  await page.locator('#NewPassword').fill(newPassword);
  await page.locator('#ConfirmPassword').fill(newPassword);
  await page.locator('form button[type="submit"].primary').click();
  await expect(page).toHaveURL(/\/account\/login\?Message=changed/);
  await signIn(page, userName, newPassword);
  await expect(page).toHaveURL(/\/$/);
}

export async function signIn(page: Page, userName: string, password: string): Promise<void> {
  await page.locator('#UserName').fill(userName);
  await page.locator('#Password').fill(password);
  await page.locator('button[type="submit"]').click();
}

/** Un CUI valid (cifra de control ANAF, cheia 753217532), diferit la fiecare rulare. */
export function newFiscalCode(): string {
  const body = String(Date.now() % 100_000_000).padStart(8, '1').slice(0, 8);
  const key = '753217532';
  const padded = body.padStart(9, '0');
  let sum = 0;
  for (let i = 0; i < 9; i++) {
    sum += Number(padded[i]) * Number(key[i]);
  }
  return body + String(((sum * 10) % 11) % 10);
}

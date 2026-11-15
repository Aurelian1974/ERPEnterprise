import { expect, test, type Page } from '@playwright/test';
import { e2eUsers } from './global-setup';
import { newFiscalCode, signInFirstTime } from './helpers';

/** Alege o valoare dintr-un DropDownList Syncfusion ca utilizatorul de la tastatura: deschide lista, scrie, Enter. */
async function choose(page: Page, fieldId: string, text: string, typed = text.slice(0, 4)) {
  await page.locator(`#${fieldId}`).locator('xpath=ancestor::span[contains(@class,"e-ddl")]').click();
  await expect(page.locator(`#${fieldId}_popup`)).toBeVisible();
  const filter = page.locator(`#${fieldId}_popup input.e-input-filter`);
  if ((await filter.count()) > 0) {
    await expect(filter).toBeFocused();
    await page.keyboard.type(typed, { delay: 30 });
  } else {
    await page.keyboard.type(typed);
  }
  await expect(page.locator(`#${fieldId}_popup li[role="option"]`).first()).toContainText(text);
  await page.keyboard.press('Enter');
  await expect(page.locator(`#${fieldId}`)).toHaveValue(text);
}

/** Un IBAN romanesc valid (mod 97), diferit la fiecare rulare. */
function newIban(): string {
  const account = `AAAA${String(Date.now()).padStart(16, '0').slice(-16)}`;
  const numeric = `${account}RO00`.replace(/[A-Z]/g, (c) => String(c.charCodeAt(0) - 55));
  let remainder = 0;
  for (const digit of numeric) {
    remainder = (remainder * 10 + Number(digit)) % 97;
  }
  return `RO${String(98 - remainder).padStart(2, '0')}${account}`;
}

test('administratorul creează un grup, o firmă cu cont bancar și o serie de facturi', async ({ page }) => {
  const suffix = Date.now().toString(36).toUpperCase().slice(-5);
  await signInFirstTime(page, e2eUsers.admin.userName, e2eUsers.admin.passwordFile);

  await page.getByRole('navigation', { name: 'Meniul principal' }).getByRole('link', { name: 'Firme și structură' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Firme și structură organizatorică' })).toBeVisible();

  // Grup nou
  await page.getByRole('button', { name: '+ Grup' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Grup nou' })).toBeVisible();
  await page.getByLabel('Cod', { exact: true }).fill(`G${suffix}`);
  await page.getByLabel('Denumire', { exact: true }).fill(`Grup E2E ${suffix}`);
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page).toHaveURL(/nod=g-\d+/);
  await expect(page.locator('.e-toast .e-toast-content').last()).toContainText('Modificările au fost salvate.');
  await expect(page.locator('.tree-card')).toContainText(`Grup E2E ${suffix}`);

  // Firma noua, cu validarea CUI-ului pe server
  await page.getByRole('button', { name: '+ Firmă' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Firmă nouă' })).toBeVisible();
  await page.getByLabel('Cod', { exact: true }).fill(`F${suffix}`);
  await page.locator('#f-name').fill(`Firma E2E ${suffix} SRL`);
  await page.locator('#f-fiscalCode').fill('RO12345670');
  await page.locator('#f-tradeRegisterNo').fill('J40/1234/2015');
  await page.locator('#f-shareCapital').fill('200,00');
  await page.locator('#f-address').fill('Str. Exemplului nr. 10');
  await choose(page, 'f-countyCode', 'București');
  await page.locator('#f-city').fill('Sector 1');
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page.locator('#f-fiscalCode-error')).toContainText('CUI');
  await expect(page.locator('.e-toast.e-toast-danger').last()).toBeVisible();

  await page.locator('#f-fiscalCode').fill(`RO${newFiscalCode()}`);
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page).toHaveURL(/nod=c-\d+/);
  await expect(page.getByRole('heading', { level: 2, name: new RegExp(`Firma E2E ${suffix} SRL`) })).toBeVisible();
  await expect(page.locator('#f-shareCapital')).toHaveValue('200,00');

  // Cont bancar implicit
  await page.getByRole('tab', { name: 'Conturi bancare' }).click();
  await page.getByRole('button', { name: '+ Cont bancar' }).click();
  const iban = newIban();
  await page.locator('#f-iban').fill(iban);
  await page.locator('#f-bankName').fill('Banca E2E');
  await page.getByRole('form', { name: 'Cont bancar' }).getByRole('button', { name: 'Salvează' }).click();
  await expect(page.locator('.e-grid')).toContainText(iban);

  // Seria de facturi a firmei, din Configurare › Serii
  await page.getByRole('tab', { name: 'Serii' }).click();
  await page.getByRole('link', { name: /Configurare › Serii/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Configurare' })).toBeVisible();
  await page.getByRole('button', { name: '+ Serie nouă' }).click();
  await choose(page, 'f-documentTypeCode', 'Factură fiscală');
  await page.locator('#f-prefix').fill(`E${suffix}`);
  await expect(page.locator('.example')).toContainText(`E${suffix}-${new Date().getFullYear()}-000001`);
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page.locator('.e-grid')).toContainText(`E${suffix}`);
});

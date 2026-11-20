import { expect, test, type Page } from '@playwright/test';
import { e2eUsers } from './global-setup';
import { newFiscalCode, signInFirstTime } from './helpers';

/** Alege o valoare dintr-un DropDownList Syncfusion ca utilizatorul de la tastatura: deschide lista, scrie, Enter. */
async function choose(page: Page, fieldId: string, text: string, typed = text.slice(0, 4)) {
  // Reincarcarile in curs (de exemplu dupa schimbarea firmei) pot re-randa lista si inchide popup-ul.
  await page.waitForLoadState('networkidle');
  await page.locator(`#${fieldId}`).locator('xpath=ancestor::span[contains(@class,"e-ddl")]').click();
  await expect(page.locator(`#${fieldId}_popup`)).toBeVisible();
  const filter = page.locator(`#${fieldId}_popup input.e-input-filter`);
  if ((await filter.count()) === 0) {
    // Lista scurta, fara filtrare: un singur clic pe element trebuie sa schimbe valoarea (fara reincercari care ar ascunde erori).
    const option = page.locator(`#${fieldId}_popup li[role="option"]`, { hasText: text }).first();
    await expect(option).toBeVisible();
    await page.waitForTimeout(300);
    await option.click();
    await expect(page.locator(`#${fieldId}`)).toHaveValue(text);
    return;
  }
  await expect(filter).toBeFocused();
  await page.keyboard.type(typed, { delay: 30 });
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
  // A1: schimbarea tarii goleste judetul (altfel ramanea ascuns si salvarea pica pe server).
  await choose(page, 'f-countryCode', 'Germania', 'Germ');
  await expect(page.locator('#f-countyCode')).toHaveValue('');
  // A3: pentru Romania judetul se cere pe camp, inainte de trimitere.
  await choose(page, 'f-countryCode', 'România', 'Rom');
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page.locator('#f-countyCode-error')).toHaveText('Alegeți județul.');
  await choose(page, 'f-countyCode', 'București');
  await page.locator('#f-city').fill('Sector 1');
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page.locator('#f-fiscalCode-error')).toContainText('CUI');
  await expect(page.locator('.e-toast.e-toast-danger').last()).toBeVisible();

  await page.locator('#f-fiscalCode').fill(`RO${newFiscalCode()}`);
  await page.getByRole('button', { name: 'Salvează' }).click();
  await expect(page).toHaveURL(/nod=c-\d+/);
  await expect(page.getByRole('heading', { level: 2, name: new RegExp(`Firma E2E ${suffix} SRL`) })).toBeVisible();
  await expect(page.locator('.e-treeview li.e-list-item.e-active > .e-text-content .tree-name')).toHaveText(`Firma E2E ${suffix} SRL`);
  await expect(page.locator('#f-shareCapital')).toHaveValue('200,00');

  // UI-18: editarea din Date generale supravietuieste schimbarii tab-ului.
  await page.locator('#f-city').fill('Sector 2');
  await page.getByRole('tab', { name: 'Conturi bancare' }).click();
  await page.getByRole('tab', { name: 'Date generale' }).click();
  await expect(page.locator('#f-city')).toHaveValue('Sector 2');
  // Tastatura tab-urilor (WAI-ARIA): sageata dreapta trece la tab-ul urmator si il activeaza.
  await page.getByRole('tab', { name: 'Date generale' }).press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Conturi bancare' })).toBeFocused();
  await expect(page.getByRole('tab', { name: 'Conturi bancare' })).toHaveAttribute('aria-selected', 'true');
  // Plecarea cu modificari nesalvate cere confirmare; „Rămân” pastreaza fisa si editarea.
  await page.getByRole('button', { name: '+ Grup' }).click();
  const unsaved = page.getByRole('dialog', { name: 'Aveți modificări nesalvate' });
  await expect(unsaved).toBeVisible();
  await unsaved.getByRole('button', { name: 'Rămân' }).click();
  await expect(unsaved).toBeHidden();
  await expect(page).toHaveURL(/nod=c-\d+/);
  await page.getByRole('tab', { name: 'Date generale' }).click();
  await expect(page.locator('#f-city')).toHaveValue('Sector 2');
  // „Anulează” readuce valorile salvate, deci fisa nu mai are modificari.
  await page.getByRole('button', { name: 'Anulează' }).click();
  await expect(page.locator('#f-city')).toHaveValue('Sector 1');

  // Cont bancar implicit
  await page.getByRole('tab', { name: 'Conturi bancare' }).click();
  await page.getByRole('button', { name: '+ Cont bancar' }).click();
  const iban = newIban();
  await page.locator('#bank-iban').fill(iban);
  await page.locator('#bank-bankName').fill('Banca E2E');
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

  // Schimbarea firmei curente din antet reincarca imediat ecranul, de fiecare data (fara reincarcare manuala).
  await page.goto('/admin/config/serii');
  const before = await page.locator('#company').inputValue();
  await choose(page, 'company', `Firma E2E ${suffix} SRL`, 'Firma');
  await expect(page.locator('#series-title')).toContainText(`Firma E2E ${suffix} SRL`);
  await choose(page, 'company', before, before.slice(0, 4));
  await expect(page.locator('#series-title')).toContainText(before);

  // Pe ecranul Firme, firma aleasa din antet devine nodul selectat si fisa ei se afiseaza.
  await page.goto('/admin/firme?nod=g-1');
  await choose(page, 'company', `Firma E2E ${suffix} SRL`, 'Firma');
  await expect(page).toHaveURL(/nod=c-\d+/);
  await expect(page.getByRole('heading', { level: 2, name: new RegExp(`Firma E2E ${suffix} SRL`) })).toBeVisible();
  await expect(page.locator('.e-treeview li.e-list-item.e-active > .e-text-content .tree-name')).toHaveText(`Firma E2E ${suffix} SRL`);
  await choose(page, 'company', before, before.slice(0, 4));
  await expect(page.getByRole('heading', { level: 2, name: new RegExp(before) })).toBeVisible();
});

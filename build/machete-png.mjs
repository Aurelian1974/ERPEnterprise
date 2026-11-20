// Regenereaza PNG-urile machetelor din HTML-ul lor, ca sa nu se depărteze unul de altul
// (Docs/arhitectura/10-interfata-tema/INTERFATA_TEMA.md, secțiunea 8).
//
//   node build/machete-png.mjs                      machetele si plansa paletei
//   node build/machete-png.mjs 01-login 03-firme    numai machetele numite
//   node build/machete-png.mjs Docs/.../paleta.html un fisier anume, dupa cale
//
// Cere Playwright cu Chromium instalat. Machetele sunt desenate pe 1440 x 900, iar scara 2 da
// exact dimensiunea fisierelor existente (2880 x 1800).
import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import { readdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const ECRANE = resolve(REPO, 'Docs/arhitectura/02-module-principale/01-nucleu/ecrane');

// Playwright poate fi instalat global (mediul de dezvoltare) sau local, cu aplicatia React.
function loadChromium() {
  for (const [from, pachet] of [
    [import.meta.url, 'playwright'],
    [resolve(REPO, 'src/Web/erp-web/package.json'), '@playwright/test'],
  ]) {
    try {
      return createRequire(from)(pachet).chromium;
    } catch {
      continue;
    }
  }
  throw new Error('Playwright lipsește: instalează-l global sau rulează „npm ci” în src/Web/erp-web.');
}

const chromium = loadChromium();
const VIEWPORT = { width: 1440, height: 900 };

const PALETA = resolve(REPO, 'Docs/arhitectura/10-interfata-tema/paleta.html');
const toate = (await readdir(ECRANE)).filter((name) => name.endsWith('.html')).sort();

const cerute = process.argv.slice(2);
const fisiere = cerute.length === 0
  ? [...toate.map((name) => resolve(ECRANE, name)), PALETA]
  : cerute.map((arg) => (arg.includes('/') ? resolve(REPO, arg) : resolve(ECRANE, `${arg.replace(/\.html$/, '')}.html`)));

const lipsa = fisiere.filter((path) => !existsSync(path));
if (lipsa.length > 0) {
  console.error(`Fișiere inexistente:\n  ${lipsa.join('\n  ')}`);
  process.exit(1);
}

const browser = await chromium.launch({ args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: VIEWPORT, deviceScaleFactor: 2 });
try {
  for (const html of fisiere) {
    const png = html.replace(/\.html$/, '.png');
    await page.goto(`file://${html}`, { waitUntil: 'load' });
    // Machetele sunt fixate pe 1440 x 900; o plansa mai scurta (paleta) se taie la inaltimea ei reala.
    const height = await page.evaluate(() => Math.ceil(Math.max(
      0,
      ...[...document.body.children].map((element) => element.getBoundingClientRect().bottom),
    )));
    await page.screenshot({ path: png, clip: { x: 0, y: 0, width: VIEWPORT.width, height: Math.min(height, VIEWPORT.height) } });
    console.log(`${html.slice(REPO.length + 1)} -> ${png.slice(png.lastIndexOf('/') + 1)}`);
  }
} finally {
  await browser.close();
}

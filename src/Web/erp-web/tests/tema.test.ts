import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * Pazeste regulile temei din Bune practici (UI-13, UI-14) si deciziile T-01/T-02 din
 * Docs/arhitectura/10-interfata-tema/INTERFATA_TEMA.md: culorile stau numai in tokens.css si nicio
 * suprafata nu e alba. Testul citeste foile de stil ca text, deci prinde si ce nu se randeaza in jsdom.
 */
const here = dirname(fileURLToPath(import.meta.url));
const web = resolve(here, '..');
const repo = resolve(web, '..', '..', '..');
const theme = resolve(web, 'src', 'theme');

const read = (path: string): string => readFileSync(path, 'utf8');
const tokens = read(resolve(theme, 'tokens.css'));
const syncfusion = read(resolve(theme, 'syncfusion.css'));
const styles = read(resolve(web, 'src', 'styles.css'));
const indexHtml = read(resolve(web, 'index.html'));
const accountLayout = read(resolve(repo, 'src', 'Auth', 'Erp.Auth', 'Pages', 'Account', '_AccountLayout.cshtml'));

/** Valoarea unui token din tokens.css, de exemplu --surface-app. */
function token(name: string): string {
  const value = new RegExp(`${name}:\\s*([^;]+);`).exec(tokens)?.[1];
  expect(value, `tokenul ${name} lipsește din tokens.css`).toBeDefined();
  return (value ?? '').trim();
}

/** Declarațiile de fundal dintr-o foaie de stil, cu valoarea lor. */
function backgrounds(css: string): string[] {
  return [...css.matchAll(/background(?:-color)?\s*:\s*([^;}]+)/g)].map((match) => (match[1] ?? '').trim().toLowerCase());
}

const white = /#fff\b|#ffffff\b|\bwhite\b|#fefefe\b|rgba?\(\s*255\s*,\s*255\s*,\s*255/;

describe('tema vizuală', () => {
  it('nu are nicio suprafață albă (UI-14)', () => {
    for (const [name, css] of [
      ['tokens.css', tokens],
      ['syncfusion.css', syncfusion],
      ['styles.css', styles],
      ['index.html', indexHtml],
      ['_AccountLayout.cshtml', accountLayout],
    ] as const) {
      const albe = backgrounds(css).filter((value) => white.test(value));
      expect(albe, `${name} are fundaluri albe: ${albe.join(', ')}`).toEqual([]);
    }
  });

  it('albul rămâne doar text pe zonele închise (UI-14)', () => {
    expect(token('--on-dark')).toBe('#fff');
  });

  it('stilurile aplicației nu scriu culori directe, doar var(--…) (UI-13)', () => {
    const directe = [...styles.matchAll(/#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/g)].map((m) => m[0]);
    expect(directe, `styles.css are culori directe: ${directe.join(', ')}`).toEqual([]);
  });

  it('fundalul de rezervă din index.html e același cu tokenii (fără clipire albă)', () => {
    const fallback = read(resolve(web, 'index.html'));
    expect(fallback).toContain(`background: ${token('--surface-app')};`);
    expect(fallback).toContain(`color: ${token('--ink')};`);
  });

  it('paginile /account/* folosesc tokenii serviți de Erp.Auth, nu o paletă proprie', () => {
    expect(accountLayout).toContain('_content/Erp.Auth/tokens.css');
  });
});

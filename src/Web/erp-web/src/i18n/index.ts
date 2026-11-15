import { ro, type TextKey } from './ro';

/** Textul pentru cheie, cu parametrii {nume} inlocuiti. */
export function t(key: TextKey, params?: Record<string, string | number>): string {
  const text: string = ro[key];
  return params ? text.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match)) : text;
}

/** Numele afisat al modulului; un cod necunoscut ramane ca atare. */
export function moduleName(code: string): string {
  const key = `module.${code}`;
  return key in ro ? t(key as TextKey) : code;
}

export type { TextKey };

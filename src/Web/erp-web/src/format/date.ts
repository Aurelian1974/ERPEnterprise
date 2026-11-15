// Formatul romanesc (UI-04): date zz.ll.aaaa, fusul orar Europe/Bucharest, indiferent de setarile calculatorului.
export const timeZone = 'Europe/Bucharest';

const longDate = new Intl.DateTimeFormat('ro-RO', { timeZone, weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const shortDate = new Intl.DateTimeFormat('ro-RO', { timeZone, day: '2-digit', month: '2-digit', year: 'numeric' });

/** „Duminică, 15 noiembrie 2026”. */
export function formatLongDate(value: Date): string {
  const text = longDate.format(value);
  return text.charAt(0).toLocaleUpperCase('ro-RO') + text.slice(1);
}

/** „15.11.2026”. */
export function formatDate(value: Date): string {
  return shortDate.format(value);
}

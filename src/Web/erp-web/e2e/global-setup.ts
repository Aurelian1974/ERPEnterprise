import { execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const e2eUser = 'e2e.operator';
const here = dirname(fileURLToPath(import.meta.url));
export const passwordFile = resolve(here, '.auth', 'password.txt');

/** Reseteaza utilizatorul E2E (parola temporara noua, schimbare obligatorie) pe baza de dezvoltare. */
export default function globalSetup(): void {
  mkdirSync(dirname(passwordFile), { recursive: true });
  const script = resolve(here, '..', '..', '..', '..', 'build', 'dev-database.ps1');
  const args = ['-NoProfile', '-File', script, '-E2EUser', e2eUser, '-PasswordFile', passwordFile];
  if (process.env.ERP_E2E_SQL_SERVER) {
    args.push('-Server', process.env.ERP_E2E_SQL_SERVER);
  }
  if (process.env.ERP_E2E_DATABASE) {
    args.push('-Database', process.env.ERP_E2E_DATABASE);
  }
  execFileSync('pwsh', args, { stdio: 'inherit' });
}

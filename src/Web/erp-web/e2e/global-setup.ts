import { execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

/** Utilizatorii E2E: STANDARD (autentificarea) si ADMIN la nivel de instalare (firme si serii). */
export const e2eUsers = {
  operator: { userName: 'e2e.operator', role: 'STANDARD', passwordFile: resolve(here, '.auth', 'operator.txt') },
  admin: { userName: 'e2e.admin', role: 'ADMIN', passwordFile: resolve(here, '.auth', 'admin.txt') },
} as const;

export const e2eUser = e2eUsers.operator.userName;
export const passwordFile = e2eUsers.operator.passwordFile;

/** Reseteaza utilizatorii E2E (parola temporara noua, schimbare obligatorie) pe baza de dezvoltare. */
export default function globalSetup(): void {
  const script = resolve(here, '..', '..', '..', '..', 'build', 'dev-database.ps1');
  for (const user of Object.values(e2eUsers)) {
    mkdirSync(dirname(user.passwordFile), { recursive: true });
    const args = ['-NoProfile', '-File', script, '-E2EUser', user.userName, '-E2ERole', user.role, '-PasswordFile', user.passwordFile];
    if (process.env.ERP_E2E_SQL_SERVER) {
      args.push('-Server', process.env.ERP_E2E_SQL_SERVER);
    }
    if (process.env.ERP_E2E_DATABASE) {
      args.push('-Database', process.env.ERP_E2E_DATABASE);
    }
    execFileSync('pwsh', args, { stdio: 'inherit' });
  }
}

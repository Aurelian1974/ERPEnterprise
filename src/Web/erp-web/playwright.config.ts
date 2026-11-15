import { defineConfig, devices } from '@playwright/test';

// E2E pe Erp.Host pornit local (cu interfata construita in wwwroot) si baza de dezvoltare (TST-08).
// Utilizatorul e2e.operator se reseteaza inainte de rulare cu build/dev-database.ps1 (e2e/global-setup.ts).
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.ERP_E2E_BASE_URL ?? 'https://localhost:7180',
    ignoreHTTPSErrors: true,
    locale: 'ro-RO',
    timezoneId: 'Europe/Bucharest',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});

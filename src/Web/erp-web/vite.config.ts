/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Erp.Host ruleaza pe https://localhost:7180 (launchSettings). In dezvoltare, Vite trimite catre el API-ul, paginile Razor
// /account/* si health-check-ul; cookie-urile sunt pe localhost, deci aceeasi sesiune pe ambele porturi.
const host = process.env.ERP_HOST_URL ?? 'https://localhost:7180';
const proxied = ['/api', '/account', '/health', '/openapi', '/_content'];

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: Object.fromEntries(proxied.map((path) => [path, { target: host, secure: false }])),
  },
  build: {
    // ST2: fisierele statice ajung in wwwroot al Erp.Host (servite de acelasi proces, same-origin).
    outDir: '../../Host/Erp.Host/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    // Bibliotecile (Syncfusion, React) in fisiere separate: raman in cache-ul browserului cand se schimba doar codul ERP.
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: 'syncfusion-grids', test: /node_modules[\\/]@syncfusion[\\/]ej2-(react-)?grids/ },
            { name: 'syncfusion', test: /node_modules[\\/]@syncfusion/ },
            { name: 'vendor', test: /node_modules/ },
          ],
        },
      },
    },
    chunkSizeWarningLimit: 900,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    // tests/ pastreaza verificarile care citesc fisiere (tema, UI-14): au nevoie de API-ul Node, nu de DOM.
    include: ['src/**/*.test.{ts,tsx}', 'tests/**/*.test.ts'],
    css: false,
  },
});

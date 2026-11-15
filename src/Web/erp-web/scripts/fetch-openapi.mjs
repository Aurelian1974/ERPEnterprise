// Preia documentul OpenAPI de la Erp.Host pornit in Development (MapOpenApi), din care openapi-typescript genereaza tipurile
// clientului (UI-03). Certificatul de dezvoltare de pe localhost poate sa nu fie de incredere; doar pentru localhost se accepta.
import { writeFile } from 'node:fs/promises';
import https from 'node:https';

const url = new URL(process.env.ERP_OPENAPI_URL ?? 'https://localhost:7180/openapi/v1.json');
const local = url.hostname === 'localhost' || url.hostname === '127.0.0.1';

const body = await new Promise((resolve, reject) => {
  https
    .get(url, { rejectUnauthorized: !local }, (response) => {
      if (response.statusCode !== 200) {
        reject(new Error(`${url} a răspuns ${response.statusCode}. Porniți Erp.Host în Development.`));
        response.resume();
        return;
      }
      let text = '';
      response.setEncoding('utf8');
      response.on('data', (chunk) => (text += chunk));
      response.on('end', () => resolve(text));
    })
    .on('error', (error) => reject(new Error(`${url} nu răspunde (${error.message}). Porniți Erp.Host în Development.`)));
});

const document = JSON.parse(body);
// Versiunea build-ului se schimba la fiecare compilare, iar adresa serverului depinde de masina; nu fac parte din contract.
document.info.version = 'dev';
delete document.servers;
// Contractul interfetei: doar API-ul (/api/*), nu health-check-urile sau pagina provizorie.
document.paths = Object.fromEntries(Object.entries(document.paths ?? {}).filter(([path]) => path.startsWith('/api/')));
await writeFile(new URL('../openapi/erp-api.json', import.meta.url), JSON.stringify(document, null, 2) + '\n');
console.log(`OpenAPI: ${Object.keys(document.paths ?? {}).length} rute salvate în openapi/erp-api.json`);

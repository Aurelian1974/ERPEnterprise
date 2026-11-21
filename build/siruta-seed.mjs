// Genereaza datele livrate core.City (14_Cities.sql) din nomenclatorul SIRUTA al INS (02.01, 3.5).
// Sursa: fisierul CSV al INS (separator „;”, UTF-8, coloanele SIRUTA;DENLOC;CODP;JUD;SIRSUP;TIP;NIV;...), descarcat de pe
// data.gov.ro / insse.ro sau din copia publica https://github.com/strainu/SIRUTA (siruta.csv).
// Utilizare: node build/siruta-seed.mjs <siruta.csv> [fisier .sql de iesire]
import { readFileSync, writeFileSync } from 'node:fs';

const [input, output = 'src/Modules/Core/Erp.Core.Infrastructure/Database/Seed/14_Cities.sql'] = process.argv.slice(2);
if (!input) {
  console.error('Utilizare: node build/siruta-seed.mjs <siruta.csv> [iesire.sql]');
  process.exit(1);
}

// Indicativele ISO 3166-2:RO ale judetelor (05_Counties.sql), dupa numele din SIRUTA fara diacritice.
const counties = {
  ALBA: 'AB', ARAD: 'AR', ARGES: 'AG', BACAU: 'BC', BIHOR: 'BH', 'BISTRITA-NASAUD': 'BN', BOTOSANI: 'BT', BRASOV: 'BV', BRAILA: 'BR',
  BUCURESTI: 'B', BUZAU: 'BZ', 'CARAS-SEVERIN': 'CS', CALARASI: 'CL', CLUJ: 'CJ', CONSTANTA: 'CT', COVASNA: 'CV', DAMBOVITA: 'DB', DOLJ: 'DJ',
  GALATI: 'GL', GIURGIU: 'GR', GORJ: 'GJ', HARGHITA: 'HR', HUNEDOARA: 'HD', IALOMITA: 'IL', IASI: 'IS', ILFOV: 'IF', MARAMURES: 'MM',
  MEHEDINTI: 'MH', MURES: 'MS', NEAMT: 'NT', OLT: 'OT', PRAHOVA: 'PH', 'SATU MARE': 'SM', SALAJ: 'SJ', SIBIU: 'SB', SUCEAVA: 'SV',
  TELEORMAN: 'TR', TIMIS: 'TM', TULCEA: 'TL', VASLUI: 'VS', VALCEA: 'VL', VRANCEA: 'VN',
};

const plain = (text) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase();
// Sedila veche (Ş, Ţ) devine virgula (Ș, Ț), ca in restul datelor.
const commas = (text) => text.replace(/Ş/g, 'Ș').replace(/ş/g, 'ș').replace(/Ţ/g, 'Ț').replace(/ţ/g, 'ț');

// Cuvintele de legatura raman cu litera mica in interiorul numelui („Gârbova de Jos”, „Baia de Arieș”).
const particles = new Set(['de', 'din', 'pe', 'la', 'lui', 'cu', 'sub', 'peste', 'și', 'în', 'dintre', 'după', 'a', 'al', 'ale']);
function titleCase(text) {
  return text.toLocaleLowerCase('ro-RO').split(' ').map((word, index) => {
    if (index > 0 && particles.has(word)) {
      return word;
    }
    if (index > 0 && /^(i{1,3}|iv|vi{0,3}|ix|x)$/.test(word)) {
      return word.toUpperCase(); // „Gersa II”
    }
    return word.split('-').map((part) => part.charAt(0).toLocaleUpperCase('ro-RO') + part.slice(1)).join('-');
  }).join(' ');
}

// Numele fara prefixul tipului: „MUNICIPIUL ALBA IULIA” → „Alba Iulia”, „BUCUREȘTI SECTORUL 1” → „Sectorul 1”.
function cityName(raw) {
  const name = commas(raw.trim()).replace(/^(MUNICIPIUL|ORAȘ|ORAŞ|COMUNA|JUDEȚUL) /, '').replace(/^BUCUREȘTI (SECTORUL \d)$/, '$1');
  return titleCase(name);
}

const lines = readFileSync(input, 'utf8').replace(/^﻿/, '').split(/\r?\n/).filter((l) => l.trim() !== '');
const header = lines[0].split(';').map((h) => h.trim().toUpperCase());
const col = (name) => {
  const index = header.indexOf(name);
  if (index < 0) {
    throw new Error(`Lipseste coloana ${name} din ${input}.`);
  }
  return index;
};
const [cSiruta, cName, cPostal, cCounty, cParent, cKind, cLevel] = ['SIRUTA', 'DENLOC', 'CODP', 'JUD', 'SIRSUP', 'TIP', 'NIV'].map(col);
const rows = lines.slice(1).map((l) => l.split(';'));

// JUD = codul numeric INS al judetului; numele judetului vine din randul de tip 40 (nivelul 1).
const countyByJud = new Map();
for (const r of rows.filter((r) => r[cLevel] === '1')) {
  const key = plain(commas(r[cName])).replace(/^(JUDETUL|MUNICIPIUL) /, '');
  const code = counties[key];
  if (!code) {
    throw new Error(`Judet necunoscut in SIRUTA: ${r[cName]}`);
  }
  countyByJud.set(r[cCounty], code);
}
if (countyByJud.size !== 42) {
  throw new Error(`Se asteptau 42 de judete (inclusiv Bucuresti), s-au gasit ${countyByJud.size}.`);
}

const sql = (text) => `N'${text.replace(/'/g, "''")}'`;
const cities = rows.filter((r) => r[cLevel] === '2' || r[cLevel] === '3').map((r) => {
  const county = countyByJud.get(r[cCounty]);
  if (!county) {
    throw new Error(`Judetul ${r[cCounty]} al localitatii ${r[cSiruta]} nu exista.`);
  }
  const postal = Number(r[cPostal]) > 0 ? `'${r[cPostal].trim().padStart(6, '0')}'` : 'NULL';
  const parent = r[cLevel] === '3' ? r[cParent].trim() : 'NULL';
  return `(${Number(r[cSiruta])}, ${sql(cityName(r[cName]))}, N'${county}', ${parent}, ${Number(r[cKind])}, ${Number(r[cLevel])}, ${postal})`;
});

const batches = [];
for (let i = 0; i < cities.length; i += 1000) {
  batches.push(`INSERT INTO #City (SirutaCode, Name, CountyCode, ParentSirutaCode, Kind, [Level], PostalCode) VALUES\n${cities.slice(i, i + 1000).join(',\n')};`);
}

writeFileSync(output, `-- Localitatile Romaniei din nomenclatorul SIRUTA al INS (02.01, 3.5): nivelul 2 = unitatile administrativ-teritoriale
-- (municipii, orase, comune, Bucuresti), nivelul 3 = localitatile (sate, localitati componente, sectoarele Bucurestiului), cu
-- unitatea lor (ParentSirutaCode) si codul postal. Generat cu build/siruta-seed.mjs; nu se editeaza de mana.
-- MERGE pe SirutaCode: o localitate care dispare din SIRUTA se dezactiveaza, nu se sterge (adresele o pot folosi).
SET NOCOUNT ON;
SET XACT_ABORT ON;

CREATE TABLE #City (
    SirutaCode       int           NOT NULL PRIMARY KEY,
    Name             nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL,
    CountyCode       nvarchar(3)   COLLATE DATABASE_DEFAULT NOT NULL,
    ParentSirutaCode int           NULL,
    Kind             tinyint       NOT NULL,
    [Level]          tinyint       NOT NULL,
    PostalCode       char(6)       COLLATE DATABASE_DEFAULT NULL
);

${batches.join('\n\n')}

MERGE core.City AS target
USING #City AS source ON target.SirutaCode = source.SirutaCode
WHEN MATCHED AND (target.Name <> source.Name OR target.CountyCode <> source.CountyCode
                  OR ISNULL(target.ParentSirutaCode, 0) <> ISNULL(source.ParentSirutaCode, 0) OR target.Kind <> source.Kind
                  OR target.[Level] <> source.[Level] OR ISNULL(target.PostalCode, '') <> ISNULL(source.PostalCode, '') OR target.IsActive = 0)
    THEN UPDATE SET Name = source.Name, CountyCode = source.CountyCode, ParentSirutaCode = source.ParentSirutaCode, Kind = source.Kind,
                    [Level] = source.[Level], PostalCode = source.PostalCode, IsActive = 1
WHEN NOT MATCHED BY TARGET
    THEN INSERT (SirutaCode, Name, CountyCode, ParentSirutaCode, Kind, [Level], PostalCode, IsActive)
         VALUES (source.SirutaCode, source.Name, source.CountyCode, source.ParentSirutaCode, source.Kind, source.[Level], source.PostalCode, 1)
WHEN NOT MATCHED BY SOURCE AND target.IsActive = 1
    THEN UPDATE SET IsActive = 0;

DROP TABLE #City;
`, 'utf8');
console.log(`${cities.length} localitati scrise in ${output}`);

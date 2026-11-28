# ERP · 02.13 Raportare și BI · BI1 Configurare, catalogul rapoartelor și stratul semantic

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: Aurelian a cerut lucru autonom și analiza altor producători înainte de BI ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P1–P24); împărțirea BI1–BI5 și deciziile au fost adoptate pe recomandare. Prima din cele 5 părți ale modulului 02.13.

Modulul 02.13 are schema SQL `rpt`, prefixul de cod `Erp.Reporting` și plaja de erori `62000–62999`, împărțită pe părți: BI1 `62000–62099`, BI2 `62100–62249`, BI3 `62250–62449`, BI4 `62450–62599`, BI5 `62600–62749`, rezervă `62750–62999`. Faza 1 → 4 din roadmap: BI1, BI2 și BI3 intră în faza 1 (catalogul, faptele Finanțelor, rapoartele), BI4 și BI5 cresc odată cu modulele. Pachetul de licență: catalogul, rapoartele și tablourile livrate sunt în „Bază”; generatorul de analize, tablourile proprii, alertele și bugetarea sunt în pachetul „BI” (cere „Bază”). Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [Nucleu](../01-nucleu/NUCLEU.md) (§3.5 motorul comun de rapoarte `core.ReportView` și `core.ReportSchedule`, utilizatori, roluri, joburi, notificări), [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP on-premise, SQL Server case sensitive (`Latin1_General_100_CS_AS_SC_UTF8`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite, fără servicii în nor obligatorii.
- **Ce există deja și nu se reface:**
  - Motorul comun de rapoarte din Nucleu (§3.5) face filtrele, desfășurarea până la document, exportul Excel, PDF și CSV, vizualizările salvate (`core.ReportView`) și trimiterea programată (`core.ReportSchedule`).
  - Fiecare modul are rapoartele lui. Exemple: S7, A7, V7, C5, M7, CR5.
  - Rapoartele legale stau în modulele lor: bilanțul în C5, declarațiile în C4.
- **Ce adaugă 02.13:**
  - catalogul unic al tuturor rapoartelor (BI1);
  - stratul semantic, adică măsurile și dimensiunile definite o singură dată (BI1);
  - depozitul de date în schema `rpt`, din vederile publicate de module (BI2);
  - analizele fără SQL (BI3);
  - tablourile de bord și alertele (BI4);
  - bugetarea și comparația plan vs realizat (BI5).
- **`rpt` e singura schemă în care se fac join-uri între schemele modulelor** (MODULE_PRINCIPALE, P1). Le face doar peste vederile publicate de module (`sales.vw_SalesFact`, `purch.vw_PurchaseFact` și celelalte), niciodată peste tabelele lor interne.
- 02.13 **nu publică evenimente de business** și nu scrie în schemele altor module. Bugetele din BI5 sunt singurele date proprii introduse de utilizatori.

## 1. Scop și perimetru

| Zonă | Ce face BI1 | Ce NU face |
| --- | --- | --- |
| Catalogul rapoartelor | Registrul tuturor rapoartelor (din module și din 02.13), cu cod, modul, categorie, tip, permisiune, filtrul de vizibilitate, descriere, proprietar; meniul „Rapoarte” și căutarea | Rapoartele modulelor (rămân în module) |
| Stratul semantic | Catalogul de măsuri (formula SQL, agregarea, unitatea, formatul, descrierea pe înțelesul utilizatorului) și de dimensiuni (atribute, ierarhii) peste faptele din BI2 | Formule scrise de utilizatori (decizia 3) |
| Securitatea | Regula de aplicare a filtrelor de vizibilitate ale modulelor pe orice citire din `rpt`; marcarea măsurilor sensibile (marjă, cost, salarii) | Definirea filtrelor (module) |
| Setări și roluri | Setările BI ale instalării și firmei, rolurile livrate | — |

## 2. Model de domeniu

```
 core.ReportView ── core.ReportSchedule                 (Nucleu: vizualizări salvate și programări, neschimbate)

 rpt.ReportCatalog (Code = <modul>.<raport>)            (un rând pe raport, înregistrat de modul la instalare sau actualizare)
   ├─ Module, Category, Kind, Permission, DataScopeKind, ProcName, ParamsSchemaJson, Version, IsLegal
   └─ rpt.ReportCatalogTag × N                          (etichete de căutare: „lunar”, „director”, „TVA”)

 rpt.Dataset (FactView = sales.vw_SalesFact …)          (faptele înregistrate, BI2)
   ├─ rpt.Measure × N                                   (CA netă, Marja, Cantitate, Valoare stoc, DSO …)
   └─ rpt.DatasetDimension × N ── rpt.Dimension         (Data, Firmă, Partener, Articol, Agent, Centru de cost …)
                                     └─ rpt.DimensionAttribute × N, rpt.DimensionHierarchy × N

 rpt.Settings · rpt.SensitiveMeasureGrant
```

Reguli de business:

1. **Codul raportului e unic** în instalare și are forma `<modul>.<raport>` (Nucleu §3.5), de exemplu `inv.StockBalance`, `sales.MarginByCustomer`, `crm.R07`, `rpt.ExecutiveMonthly`. Codurile modulelor nu se schimbă între versiuni; un raport retras se marchează `IsActive = 0`, nu se șterge (`62001`), ca vizualizările salvate și programările să rămână citibile.
2. **Un raport apare în catalog doar dacă modulul lui e licențiat** și utilizatorul are permisiunea raportului (`Permission`).
3. **Filtrul de vizibilitate se aplică pe server.** Fiecare raport și fiecare set de date declară `DataScopeKind`:
   - `Company`: firmele la care utilizatorul are acces;
   - `SalesScope`: `SalesDataScope`, din V1;
   - `BuyerScope`: `BuyerDataScope`, din A1;
   - `CrmScope`: `CrmVisibility`, din CR1;
   - `Location`: gestiunile utilizatorului, din S1;
   - `None`: doar pentru datele fără conținut restricționat.

   Procedura de citire primește utilizatorul și aplică filtrul. Interfața nu poate ocoli filtrul (P3, `62010`).
4. **Măsurile sensibile** (`IsSensitive = 1`: marja, costul, prețul de achiziție, salariile) apar doar pentru rolurile cu permisiunea măsurii. Utilizatorii fără permisiune nu văd coloana sau widget-ul, nici în export, nici în programările trimise lor.
5. **O măsură are o singură definiție.** Același nume (de exemplu „Cifra de afaceri netă”) înseamnă aceeași formulă în raportul V7, în tabloul de bord și în generatorul de analize. Măsurile modulelor se înregistrează din cod (seed), nu din interfață.

## 3. Entități

### 3.1 Catalogul rapoartelor (`rpt.ReportCatalog`, P12)

| Câmp | Reguli |
| --- | --- |
| `Code`, `Module`, `Name`, `Description` | Descrierea spune pe scurt ce răspunde raportul („Ce clienți îmi aduc marja cea mai mare?”) |
| `Category` | Vânzări · Achiziții · Stocuri · Finanțe · Contabilitate · Fiscal · Mijloace fixe · CRM · Management · Personalizate |
| `Kind` | 1 Listă (motorul Nucleu) · 2 Analiză (generatorul BI3) · 3 Tipărit cu parametri (QuestPDF) · 4 Financiar pe rânduri (C5 `fin.ReportDefinition`) · 5 Tablou de bord (BI4) |
| `Permission`, `DataScopeKind`, `IsLegal` | `IsLegal` marchează rapoartele cu formă reglementată (balanța, registrele, declarațiile): nu se pot modifica în generator, doar rula |
| `ProcName`, `ParamsSchemaJson`, `Version` | Procedura de citire și parametrii ei (JSON Schema simplu: tip, obligatoriu, listă de valori, implicit) |
| `OwnerUserId`, `SharedWithRoleIds` | Doar pentru rapoartele salvate de utilizatori din generator (`Kind = 2`, categoria „Personalizate”) |

Modulele se înregistrează prin contractul `IReportCatalogContributor` (§6). Fiecare modul livrează lista lui, iar la pornire o procedură `MERGE` o sincronizează: inserează și actualizează, nu șterge.

### 3.2 Măsurile (`rpt.Measure`, P2)

| Câmp | Reguli |
| --- | --- |
| `Code`, `Name`, `Description` | De exemplu `sales.NetRevenue` „Cifra de afaceri netă: valoarea fără TVA a facturilor emise, minus notele de credit” |
| `DatasetId`, `SqlExpression`, `Aggregation` | Expresia peste coloanele faptului (`SUM(NetAmountRon)`), sau `Derived`: o formulă peste alte măsuri (`sales.Margin / sales.NetRevenue`) |
| `Unit`, `Format`, `Decimals` | lei · bucăți · % · zile; formatul românesc (`1.234,56`) |
| `IsSensitive`, `Permission` | Regula 4 |
| `IsAdditive` | 1 Aditivă (sumă pe orice dimensiune) · 2 Semi-aditivă (sold: nu se adună pe timp, se ia ultima valoare) · 3 Neaditivă (procent, medie: se recalculează din componente) |
| `GoodDirection` | Sus sau jos (folosit de semafoarele BI4: „mai mult e mai bine” pentru cifra de afaceri, „mai puțin e mai bine” pentru DSO) |

**Măsurile livrate** (seed, extinse de fiecare modul):

| Domeniu | Măsuri |
| --- | --- |
| Vânzări (V7) | Cifra de afaceri netă, Cantitatea vândută, Marja brută, Marja %, Numărul de facturi, Valoarea medie a facturii, Comenzi deschise, Nivelul de servire (OTIF) |
| Achiziții (A7) | Achiziții nete, Prețul mediu de achiziție, Livrări la timp %, Comenzi de achiziție deschise |
| Stocuri (S7) | Valoarea stocului (semi-aditivă), Zile de acoperire, Rotația stocului, Stoc fără mișcare 180 de zile |
| Finanțe (F3, C5) | Creanțe, Creanțe restante, DSO, Datorii, DPO, Disponibil în bănci și casă, Venituri, Cheltuieli, Rezultat (din balanță, prin `IBalanceQuery`) |
| Mijloace fixe (M7) | Valoarea de intrare, Amortizarea lunii, Valoarea netă |
| CRM (CR5) | Pipeline deschis, Pipeline ponderat, Rata de câștig, Lead-uri, Rata de calificare |
| Buget (BI5) | Bugetat, Abaterea, Abaterea %, Realizare % |

### 3.3 Dimensiunile conformate (`rpt.Dimension`)

| Dimensiune | Atribute | Ierarhii | Sursa |
| --- | --- | --- | --- |
| Data | zi, săptămână ISO, lună, trimestru, an, zi lucrătoare, lună fiscală | An → Trimestru → Lună → Zi | `rpt.DimDate` (BI2) |
| Firma | firmă, subgrup, grup | Grup → Subgrup → Firmă → Punct de lucru | `core.Company`, `core.CompanyGroup` |
| Partener | denumire, CUI, județ, grup de parteneri, segment, industrie (CRM), agent din portofoliu | Grup → Partener | `core.Partner`, V1, CR1 |
| Articol | cod, denumire, categorie, familie, model, SKU, marcă | Categorie → Familie → Model → SKU | `core.Item`, `core.ItemVariant` |
| Agent și echipă | agent, șef, zonă | Șef → Agent | V1 |
| Locație și gestiune | locație, gestiune | Firmă → Locație → Gestiune | Nucleu, S1 |
| Dimensiunile analitice | centru de cost, proiect, șantier (ierarhia `ParentId`) | Pe `ParentId` | `core.DimensionValue` |
| Cont contabil | cont, grupă, clasă, categorie din situațiile financiare | Clasă → Grupă → Cont sintetic → Analitic | C1 |

### 3.4 Setările (`rpt.Settings`)

| Setare | Implicit | Ce face |
| --- | --- | --- |
| `GroupCurrency` | RON | Moneda de raportare a grupului (BI2 §3.5) |
| `GroupFxMethod` | Medie lunară BNR pentru fluxuri, închidere pentru solduri | BI2 §3.5 |
| `SnapshotRunTime` | 02:30 | Ora jobului nocturn de instantanee (BI2) |
| `ResultCacheMinutes` | 15 | Cât timp se refolosește rezultatul unei analize cu aceiași parametri (BI3) |
| `MaxAdHocRows` | 200.000 | Limita de rânduri afișate în generator; exportul nu are limită (în flux) |
| `ExternalReadAccess` | 0 | Activează utilizatorul SQL doar citire și endpoint-ul OData (BI2 §3.7) |
| `KioskEnabled` | 0 | Modul ecran mare (BI4) |
| `FiscalYearStartMonth` | 1 | Pentru firmele cu an fiscal diferit (art. 16 din Codul fiscal) |

## 4. Fluxuri principale

### 4.1 Înregistrarea la pornire

La pornire și după fiecare actualizare, `ReportCatalogSync` cheamă toți `IReportCatalogContributor` ai modulelor licențiate, sincronizează catalogul, măsurile și seturile de date și scrie diferențele în jurnal: rapoarte noi, rapoarte retrase, măsuri cu formula schimbată. O măsură cu formula schimbată primește `Version + 1`, iar tablourile care o folosesc afișează o notă „definiție actualizată pe …”.

### 4.2 Meniul „Rapoarte”

Pagina „Toate rapoartele” are:

- căutare după nume, descriere și etichete;
- filtrele modul, categorie și tip;
- lista „Folosite recent” și „Favorite”;
- marcajul „legal” pe rapoartele reglementate;
- pentru fiecare raport: descrierea, ultimul utilizator care l-a rulat și programările active.

Rapoartele salvate de utilizatori apar la „Personalizate” și pot fi partajate pe rol.

### 4.3 Exemplu

Ion Popescu (Contabil șef) caută „restanțe”. Catalogul îi arată:

- `fin.AgedReceivables` (F3, listă);
- `fin.CustomerStatement` (F3, tipărit);
- `crm.R09` Clienți în risc (CR5): nu îl vede, pentru că nu are rolul CRM.

Mai vede și analiza personalizată „Restanțe pe agent și județ”, partajată de Radu Stan pe rolul „Director economic”, din care Ion face parte.

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `LicentaActualizata` v1 | Consumat (Nucleu) | Modulele licențiate | Resincronizarea catalogului |
| `RolModificat` v1 | Consumat (Nucleu) | Permisiunile | Invalidarea cache-ului de rezultate pentru utilizatorii afectați |

02.13 nu publică evenimente de business.

## 6. Contracte publice

```csharp
public interface IReportCatalogContributor          // implementat de fiecare modul
{
    IEnumerable<ReportCatalogEntry> GetReports();
    IEnumerable<DatasetDefinition> GetDatasets();     // faptele publicate și măsurile lor (BI2)
}
public sealed record ReportCatalogEntry(string Code, string Name, string Description, string Category, int Kind,
    string Permission, string DataScopeKind, string? ProcName, string? ParamsSchemaJson, int Version, bool IsLegal);

public interface IDataScopeResolver                  // implementat de modulul care deține filtrul (V1, A1, CR1, S1)
{
    string Kind { get; }                             // "SalesScope", "BuyerScope" …
    Task<DataScopeFilter> ResolveAsync(int userId, int companyId, CancellationToken ct);  // listele de Id-uri permise
}
```

`DataScopeFilter` se transmite procedurii ca parametru tabelar (`rpt.IdList`), iar procedura face join pe el. Astfel filtrul nu se construiește ca text SQL (`SQL-08`, fără SQL dinamic din text liber).

## 7. Schema SQL `rpt` (partea BI1)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `rpt.ReportCatalog` | `Id int` | UX `(Code)`; IX `(Module, Category) WHERE IsActive = 1` |
| `rpt.ReportCatalogTag` | `(ReportCatalogId, Tag)` | — |
| `rpt.Dataset` | `Id int` | UX `(Code)`; `FactView` numele vederii (verificat la sincronizare că există, `62020`) |
| `rpt.Measure` | `Id int` | UX `(Code)`; FK `DatasetId`; `CHECK (Aggregation IN ('SUM','COUNT','COUNT_DISTINCT','AVG','MIN','MAX','LAST','DERIVED'))` |
| `rpt.Dimension`, `rpt.DimensionAttribute`, `rpt.DimensionHierarchy`, `rpt.DatasetDimension` | `Id int` | UX `(Code)` |
| `rpt.Settings` | `(Scope, ScopeId, [Key])` | — |
| Tipul tabelar `rpt.IdList` | — | `(Id bigint PRIMARY KEY)` |

**Erorile BI1** (`62000–62099`):

- `62001` `RPT_CATALOG_DELETE_FORBIDDEN`
- `62010` `RPT_DATA_SCOPE_MISSING`: o procedură de citire chemată fără filtru, deși are `DataScopeKind`
- `62011` `RPT_MEASURE_FORBIDDEN`
- `62020` `RPT_DATASET_VIEW_MISSING`
- `62021` `RPT_MEASURE_DERIVED_CYCLE`
- `62030` `RPT_REPORT_NOT_LICENSED`

## 8. Permisiuni și roluri

Permisiunile BI1:

- `Rpt.Catalog.View`, oricine are cel puțin un raport;
- `Rpt.Catalog.Manage`, pentru eticheta și categoria rapoartelor personalizate;
- `Rpt.Settings.Manage`;
- `Rpt.SensitiveMeasures.View.<Domeniu>`, de exemplu `Rpt.SensitiveMeasures.View.Margin`.

Roluri livrate, noi în Nucleu:

- **Director general**: toate tablourile, toate măsurile, toate firmele, doar citire;
- **Director economic**: Finanțe, Mijloace fixe, Buget, plus măsurile sensibile financiare;
- **Analist**: generatorul de analize pe seturile la care are drept, plus crearea de tablouri partajate;
- **Responsabil buget**: BI5.

Rolurile existente (Contabil șef, Șef vânzări, Șef achiziții, Șef depozit) primesc tablourile lor livrate (BI4).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă |
| --- | --- |
| GDPR art. 5 (minimizare), art. 25 (protecția datelor implicită) | Faptele nu conțin date personale ale persoanelor fizice (doar identificatori); salariile, dacă vor intra în BI din 02.09, sunt măsuri sensibile, agregate pe minim 5 persoane (regula BI2 §3.6) |
| Legea 82/1991 | Rapoartele legale rămân în modulele lor și nu se modifică în generator (`IsLegal`) |
| Nucleu | Roluri noi, motorul de rapoarte, joburile, setările |

## 10. Rapoarte

„Toate rapoartele” (catalogul), „Utilizarea rapoartelor” (cine ce rulează, cât durează: rapoartele lente apar primele, pentru optimizare), „Programări active”.

## 11. Ecrane principale

Machetele sunt în [ecrane/bi1-catalog/](ecrane/bi1-catalog/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Toate rapoartele | Catalogul cu căutare, categorii, favorite, recente, etichete, programări | `bi1-01-catalog-rapoarte` |
| Măsuri și dimensiuni | Stratul semantic: lista măsurilor pe set de date, definiția „Marja brută”, dimensiunile și ierarhiile | `bi1-02-masuri-dimensiuni` |
| Configurare › Raportare | Setările BI în ecranul comun de configurare | `bi1-03-configurare-bi` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| BI1-A1 | Sincronizarea înregistrează rapoartele tuturor modulelor licențiate; un modul nelicențiat nu apare | Test de integrare cu două licențe |
| BI1-A2 | Ștergerea unui raport din catalog e refuzată (`62001`); raportul retras rămâne citibil în vizualizările salvate | Test de integrare |
| BI1-A3 | Un agent cu `SalesDataScope = 3` vede în orice raport sau analiză doar clienții din portofoliu, inclusiv în export și în programarea trimisă lui | Test de integrare |
| BI1-A4 | Utilizatorul fără `Rpt.SensitiveMeasures.View.Margin` nu primește coloana marjă în niciun răspuns API | Test de integrare pe API |
| BI1-A5 | O măsură derivată ciclică e refuzată la sincronizare (`62021`) | Test unitar |
| BI1-A6 | Aceeași „Cifra de afaceri netă” pe noiembrie 2026 dă aceeași valoare în V7, în tablou și în generator | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Motor BI separat (Metabase, Superset) sau în aplicație | În aplicație, peste motorul Nucleu; acces extern doar citire, opțional | ✓ Aprobat autonom (27.11.2026) |
| 2 | Catalogul | Unic, cu înregistrarea modulelor prin contract la pornire | ✓ Aprobat autonom |
| 3 | Cine scrie formulele măsurilor | Dezvoltatorii, în cod (seed); utilizatorii doar aleg | ✓ Aprobat autonom |
| 4 | Securitatea pe rânduri | Filtrele modulelor, rezolvate prin `IDataScopeResolver` și aplicate în proceduri | ✓ Aprobat autonom |
| 5 | Măsurile sensibile | Ascunse complet, inclusiv în export și programări | ✓ Aprobat autonom |
| 6 | Licențierea | Catalogul, rapoartele și tablourile livrate în „Bază”; analizele, tablourile proprii, alertele și bugetarea în pachetul „BI” | ✓ Aprobat autonom |
| 7 | Rapoartele legale | Rămân în module, marcate `IsLegal`, nemodificabile | ✓ Aprobat autonom |

# ERP · 02.13 Raportare și BI · BI2 Depozitul de date: fapte, dimensiuni, instantanee, consolidare, acces extern

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P1, P10, P11, P13–P15). A doua din cele 5 părți ale modulului 02.13.

Plaja de erori BI2: `62100–62249`. Documente părinte:

- [BI1](BI1-CONFIGURARE-CATALOG.md): catalogul, măsurile, dimensiunile și securitatea;
- vederile publicate de module:
  - [V7](../03-vanzari/V7-RAPOARTE.md): `sales.vw_SalesFact`, `vw_OrderBook`, `vw_OrderLineService`, `vw_CommissionStatement`;
  - [A7](../04-achizitii/A7-RAPOARTE-TRASABILITATE.md): `purch.vw_PurchaseFact`, `vw_SupplierPerformanceMonth`, `vw_SupplierEvaluation`;
  - [CR5](../12-crm/CR5-FISA360-RAPOARTE.md): `crm.vw_*Fact`;
- [C5](../02-finante-contabilitate/C5-RAPOARTE.md): `IBalanceQuery`, singurul mod de a citi soldurile contabile;
- [Nucleu](../01-nucleu/NUCLEU.md): firmele, grupurile, cursurile BNR, joburile;
- [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- **Nu există un server separat de depozit de date.** Depozitul de date e **schema `rpt` din baza principală**.
  - Conține vederi peste vederile publicate de module, dimensiunile conformate și câteva tabele de instantanee scrise noaptea.
  - Clienții mari pot cere o replică doar citire (Always On cu secundar citibil sau replicare tranzacțională). BI-ul citește atunci din replică, setarea `ReportingConnection`.
  - Aceasta e decizia din schița de arhitectură: „view-uri și proceduri pe baza principală; o replică read-only doar la clienții mari”.
- **Faptul** e o vedere cu o granularitate fixă, de exemplu „o linie de factură”. Are cheile spre dimensiuni (`CompanyId`, `PartnerId`, `ItemVariantId`, `DateKey`), măsurile numerice și **referința la documentul sursă** (`SourceModule`, `SourceDocType`, `SourceId`), pentru desfășurare (P15).
- Modulele care nu au încă vederi de fapte (Stocuri, Finanțe, Mijloace fixe) le primesc prin precizări (§9). Vederile publicate sunt un **contract versionat**: o coloană nu se scoate și nu își schimbă sensul fără o vedere nouă (`_v2`) și o perioadă de tranziție.

## 1. Scop și perimetru

| Zonă | Ce face BI2 | Ce NU face |
| --- | --- | --- |
| Fapte | Registrul faptelor publicate de module (BI1 `rpt.Dataset`), granularitatea, cheile, măsurile | Vederile modulelor (le scriu modulele) |
| Dimensiuni | Vederile `rpt.vw_Dim*` conformate peste Nucleu și module; calendarul `rpt.DimDate` | — |
| Instantanee | Stocul, creanțele, datoriile, pipeline-ul CRM și soldurile de trezorerie la sfârșit de zi și de lună, pentru istorie | Recalcularea istoricului contabil (C5 are soldurile pe perioadă) |
| Consolidare | Moneda grupului, conversia la cursul BNR, marcarea tranzacțiilor între firmele grupului | Consolidarea contabilă IFRS (în afara produsului, C5 decizia 3) |
| Performanță | Indecși columnstore pe faptele mari, cache, replica | — |
| Acces extern | Utilizatorul SQL doar citire pe vederile `rpt.ext_*`, endpoint-ul OData doar citire | Scriere din exterior |

## 2. Model de domeniu

```
 Modulele publică (contract versionat):
   sales.vw_SalesFact · sales.vw_OrderBook · purch.vw_PurchaseFact · inv.vw_StockMovementFact (nou) · fin.vw_GlLineFact (nou)
   fin.vw_OpenItemFact (nou) · fa.vw_AssetLedgerFact (nou) · crm.vw_OpportunityFact … · (02.08, 02.09 la documentarea lor)

 rpt (citire, join între scheme doar aici):
   rpt.DimDate (tabel, 2000–2050) · rpt.vw_DimCompany · rpt.vw_DimPartner · rpt.vw_DimItem · rpt.vw_DimAgent · rpt.vw_DimAccount …
   rpt.vw_Fact<Domeniu> (faptul modulului + dimensiunile + moneda grupului + filtrul de firmă)
   rpt.SnapshotStockDay · rpt.SnapshotReceivablesMonth · rpt.SnapshotPayablesMonth · rpt.SnapshotPipelineWeek · rpt.SnapshotCashDay
   rpt.SnapshotRun (jurnalul joburilor)
   rpt.ext_* (vederile expuse în exterior, BI2 §3.7)
```

Reguli de business:

1. **Join-urile între scheme se fac doar în `rpt`**, peste vederile publicate (MODULE_PRINCIPALE P1). Procedurile `rpt` nu citesc tabelele interne ale modulelor (`62101`, verificare automată la build: testul de arhitectură caută în codul SQL din `rpt` referințe la tabele care nu sunt vederi publicate).
2. **Soldurile contabile se citesc doar prin `IBalanceQuery`** (C5) sau prin `fin.vw_GlLineFact`. BI-ul nu recalculează soldurile cu propria logică.
3. **Instantaneele sunt imuabile după închiderea lunii** (C3). Instantaneul unei luni închise nu se rescrie. Dacă o corecție ulterioară modifică trecutul, raportul arată nota „instantaneu din dd.mm, înainte de corecția X” (`62110` informativ).
4. **Tranzacțiile între firmele grupului** (partenerul are `GroupCompanyId`, din precizarea Nucleu de la 02.15) sunt marcate `IsIntercompany = 1` în fapte. Vederile „grup” le exclud implicit, ca cifra de afaceri a grupului să nu fie umflată.
5. **Datele personale nu intră în fapte** (P22): nici numele persoanelor fizice, nici e-mailurile sau telefoanele. Partenerul persoană fizică apare în `rpt.vw_DimPartner` cu numele doar pentru rolurile care îl văd și în modul. Pentru celelalte roluri apare ca „Persoană fizică #id”.

## 3. Detalii

### 3.1 Faptele publicate (registrul)

| Set de date (`rpt.Dataset`) | Vederea sursă | Granularitate | Volume tipice (distribuție, 5 ani) |
| --- | --- | --- | --- |
| Vânzări | `sales.vw_SalesFact` | Linie de factură sau notă de credit | 4–8 milioane de rânduri |
| Comenzi de vânzare | `sales.vw_OrderBook` | Linie de comandă deschisă | Zeci de mii |
| Achiziții | `purch.vw_PurchaseFact` | Linie de factură de furnizor | 1–2 milioane |
| Mișcări de stoc | `inv.vw_StockMovementFact` (nou, S7) | Mișcare pe SKU, gestiune, lot | 10–20 de milioane |
| Note contabile | `fin.vw_GlLineFact` (nou, C2 / C5) | Linie de notă validată, cu dimensiunile analitice | 5–15 milioane |
| Partide deschise | `fin.vw_OpenItemFact` (nou, F3) | Partidă de client sau furnizor, cu scadența și soldul la zi | Zeci de mii |
| Mijloace fixe | `fa.vw_AssetLedgerFact` (nou, M7) | Mișcare de valoare pe bun și regim | Zeci de mii |
| CRM | `crm.vw_LeadFact`, `vw_OpportunityFact`, `vw_ActivityFact`, `vw_CampaignFact`, `vw_CaseFact`, `vw_ForecastFact` | Vezi CR5 §6 | Zeci de mii |
| Buget | `rpt.vw_BudgetFact` (BI5) | Linie de buget pe lună | Zeci de mii |

Coloanele obligatorii ale oricărui fapt publicat:

- `CompanyId`;
- `DateKey` (`int`, `yyyymmdd`, data economică: data facturii, data mișcării);
- `SourceModule`, `SourceDocType`, `SourceId`, `SourceLineId`;
- `CurrencyCode`, sumele în lei (`*Ron`) și, unde există, în moneda documentului;
- `IsIntercompany`;
- `IsCancelled` (stornările rămân ca rânduri negative, nu se șterg).

### 3.2 Calendarul (`rpt.DimDate`)

Tabel generat la instalare pentru 2000–2050. Conține:

- data, ziua din săptămână, săptămâna ISO, luna, trimestrul, anul;
- luna și anul fiscal (după `FiscalYearStartMonth`);
- marcajul „zi lucrătoare” și „sărbătoare legală”, din calendarul Nucleu, actualizat la modificarea lui;
- „aceeași zi anul trecut”, pentru comparații;
- etichetele în română („nov. 2026”, „T4 2026”).

### 3.3 Instantaneele (P14)

Jobul `RptSnapshots` rulează noaptea la 02:30 (`SnapshotRunTime`) și scrie:

| Instantaneu | Ce reține | Frecvență | Păstrare |
| --- | --- | --- | --- |
| `SnapshotStockDay` | Cantitatea și valoarea pe SKU, gestiune și firmă | Zilnic | 400 de zile zilnic, apoi doar ultima zi a lunii |
| `SnapshotReceivablesMonth` | Creanțele pe client, pe intervale de vechime (0, 1–30, 31–60, 61–90, peste 90), din `fin.vw_OpenItemFact` | La sfârșit de lună și zilnic pentru luna curentă | Nelimitat (lunar) |
| `SnapshotPayablesMonth` | La fel, pentru furnizori | Idem | Nelimitat |
| `SnapshotPipelineWeek` | Pipeline-ul CRM pe etapă, agent și lună de închidere | Săptămânal, duminică | Nelimitat |
| `SnapshotCashDay` | Disponibilul pe cont de trezorerie (F1, F2) | Zilnic | 400 de zile, apoi lunar |

Jobul e idempotent: rerularea pentru aceeași zi înlocuiește instantaneul zilei dacă luna e deschisă. Durata și numărul de rânduri se scriu în `rpt.SnapshotRun`. Dacă jobul a eșuat, tabloul de bord al administratorului arată avertismentul, iar rapoartele care depind de instantanee afișează data ultimului instantaneu reușit.

### 3.4 Performanța (P10)

- **Columnstore:** modulele își pun indecși columnstore neclusterizați pe tabelele de linii mari care alimentează faptele (liniile de factură, mișcările de stoc, liniile de notă). Funcția există în SQL Server Standard începând cu 2016 SP1, deci nu e o componentă plătită în plus. Precizarea e aplicată în V5, S2 și C2 (§9).
- **Cache-ul rezultatelor:** rezultatul unei analize (BI3) se păstrează `ResultCacheMinutes` minute pentru aceeași combinație de măsuri, dimensiuni, filtre și același filtru de vizibilitate. Cheia include identificatorul filtrului de vizibilitate, deci doi utilizatori cu drepturi diferite nu primesc același rezultat.
- **Limite:** interogările din generator rulează cu `MAXDOP` și un timeout setabil (implicit 60 de secunde, `62120`). Exportul mare rulează ca job în fundal și trimite linkul pe notificare.
- **Replica:** cu setarea `ReportingConnection`, toate citirile din `rpt` și din procedurile de raport merg pe replică. Instantaneele se scriu întotdeauna pe baza principală.

### 3.5 Consolidarea și moneda grupului (P13)

Fiecare fapt monetar are și varianta `*Group`, calculată în vederea `rpt.vw_Fact*`:

- fluxurile (vânzările, achizițiile, cheltuielile) folosesc cursul BNR mediu al lunii;
- soldurile (creanțele, stocul, disponibilul) folosesc cursul de la sfârșitul lunii;
- cursurile vin din `core.ExchangeRate`.

Implicit moneda grupului e leul, deci conversia contează doar pentru grupurile care raportează în euro. Rapoartele „pe grup” însumează firmele la care utilizatorul are acces și exclud tranzacțiile între firmele grupului (regula 4). Rapoartele „pe firmă” le includ.

**Exemplu:** cifra de afaceri pe noiembrie 2026 a Grupului Exemplu (situația la 27.11):

| Firma | Cifra de afaceri |
| --- | --- |
| Exemplu Distribuție | 3.842.600 lei |
| Exemplu Producție | 2.118.400 lei, din care 1.264.300 lei vânzări către Exemplu Distribuție |
| Exemplu Construct | 906.200 lei |
| Exemplu Holding | 84.000 lei, servicii de management facturate firmelor grupului |
| **Grupul** | 6.951.200 − 1.264.300 − 84.000 = **5.602.900 lei** |

### 3.6 Datele sensibile

- Măsurile sensibile (BI1 regula 4) se calculează doar în procedurile care primesc permisiunea.
- Salariile (02.09, când va fi documentat) vor intra doar agregat, pe grupuri de cel puțin 5 persoane. Grupurile mai mici apar „sub prag” (`62130`), ca salariul unei persoane să nu poată fi dedus din raport.

### 3.7 Accesul extern doar citire (P11)

Cu `ExternalReadAccess = 1`:

- **Utilizatorul SQL** `erp_reporting_reader` primește `SELECT` doar pe vederile `rpt.ext_*`:
  - vânzări, achiziții, stoc, creanțe, buget;
  - fără date personale și fără măsuri sensibile;
  - filtrate pe firmele alese de administrator la activare.
- **Endpoint-ul OData doar citire** `/odata/rpt/*` (ASP.NET Core OData, open source) are aceleași vederi și autentificare cu cheie API (Nucleu). Excel Power Query și Power BI Desktop (gratuit) se pot conecta direct.
- **Ce NU include:** nicio dependență de Power BI. Accesul extern e o opțiune pentru clienții care au deja instrumentele lor.
- **Jurnalul:** fiecare acces OData se scrie în jurnalul de audit, cu cheia, vederea și numărul de rânduri.

## 4. Fluxuri

### 4.1 Noaptea, 28.11.2026

| Ora | Ce se întâmplă |
| --- | --- |
| 02:30 | `RptSnapshots` pornește |
| 02:31 | Stocul pe zi: 18.420 de rânduri, pentru 4 firme și 61 de gestiuni |
| 02:33 | Creanțele: 2.906 clienți cu sold |
| 02:34 | Datoriile: 611 furnizori |
| 02:34 | Disponibilul: 14 conturi |
| 02:36 | Rularea se termină în 6 minute și 12 secunde, starea OK |

La 07:00, tabloul Directorului general (BI4) se deschide din cache-ul încălzit de job: primele 6 widget-uri sunt precalculate după instantanee.

### 4.2 Desfășurarea până la document

1. În tabloul „Vânzări”, Radu Stan apasă pe bara „Pizzeria Napoli · noiembrie”.
2. Se deschide analiza cu liniile de factură din `rpt.vw_FactSales`, filtrate pe client și lună.
3. Clicul pe o linie deschide factura `FV EXD 10231` în V5, cu permisiunile Vânzărilor.

## 5. Evenimente

| Eveniment | Direcție | Folosire |
| --- | --- | --- |
| `LunaInchisa` v1 | Consumat (C3) | Îngheață instantaneele lunii (regula 3), scrie instantaneul final de sfârșit de lună |
| `LunaRedeschisa` v1 | Consumat (C3) | Deblochează rescrierea instantaneelor lunii, cu jurnal |
| `CursBnrPreluat` v1 | Consumat (02.14) | Invalidează cache-ul măsurilor în moneda grupului |

## 6. Contracte publice

```csharp
public interface IReportingQueryExecutor     // folosit de BI3 și BI4; singura cale de rulare a interogărilor pe faptele rpt
{
    Task<QueryResult> ExecuteAsync(SemanticQuery query, int userId, int companyId, CancellationToken ct);
}
public sealed record SemanticQuery(string DatasetCode, IReadOnlyList<string> Measures, IReadOnlyList<string> Dimensions,
    IReadOnlyList<QueryFilter> Filters, IReadOnlyList<QuerySort> Sort, int? Top, bool GroupCurrency);
```

`SemanticQuery` se traduce în SQL de un generator intern. Acesta pune în textul SQL doar expresiile măsurilor și ale dimensiunilor din catalog (de încredere, din cod). Valorile filtrelor sunt mereu parametri (`SQL-08`), iar filtrul de vizibilitate vine ca parametru tabelar (BI1 §6).

## 7. Schema SQL (partea BI2)

| Obiect | Cheie / indexuri |
| --- | --- |
| `rpt.DimDate` | PK `DateKey int`; IX `(YearMonth)` |
| `rpt.vw_Dim*`, `rpt.vw_Fact*` | Vederi; fără `SCHEMABINDING` peste schemele modulelor (ca modulele să își poată evolua tabelele), testul de contract verifică coloanele |
| `rpt.SnapshotStockDay` | PK `(DateKey, CompanyId, LocationId, ItemVariantId)`; columnstore neclusterizat |
| `rpt.SnapshotReceivablesMonth`, `rpt.SnapshotPayablesMonth` | PK `(MonthKey, CompanyId, PartnerId)` |
| `rpt.SnapshotPipelineWeek` | PK `(WeekKey, CompanyId, PipelineId, StageId, OwnerAgentId, CloseMonthKey)` |
| `rpt.SnapshotCashDay` | PK `(DateKey, TreasuryAccountId)` |
| `rpt.SnapshotRun` | PK `Id int`; IX `(SnapshotKind, RunDate)` |
| `rpt.ext_*` | Vederi pentru accesul extern (§3.7); `GRANT SELECT` doar pe ele |

**Erorile BI2** (`62100–62249`):

- `62101` `RPT_CROSS_SCHEMA_TABLE_ACCESS` (testul de arhitectură)
- `62110` `RPT_SNAPSHOT_STALE` (informativ)
- `62111` `RPT_SNAPSHOT_MONTH_FROZEN`
- `62120` `RPT_QUERY_TIMEOUT`
- `62121` `RPT_QUERY_TOO_LARGE`
- `62130` `RPT_BELOW_PRIVACY_THRESHOLD`
- `62140` `RPT_EXTERNAL_ACCESS_DISABLED`

## 8. Permisiuni

- `Rpt.Snapshots.Run` (administratorul, rulare manuală);
- `Rpt.ExternalAccess.Manage` (administratorul: activarea, firmele expuse, cheile OData);
- `Rpt.GroupView` (rapoartele pe grup: Director general, Director economic).

## 9. Precizări în alte module (aplicate pe 27.11.2026)

| Modul | Ce se adaugă |
| --- | --- |
| Stocuri S7 | Vederea publicată `inv.vw_StockMovementFact`: mișcarea pe SKU, gestiune, lot, cantitate, valoare, tip de mișcare, document sursă. Columnstore pe `inv.StockMovement` |
| Finanțe C2 / C5 | `fin.vw_GlLineFact`: linia de notă validată, cont, dimensiuni analitice, debit și credit în lei și valută, jurnal, document sursă. Columnstore pe `fin.JournalEntryLine` |
| Finanțe F3 | `fin.vw_OpenItemFact`: partida, partenerul, scadența, soldul la zi, zilele de întârziere |
| Mijloace fixe M7 | `fa.vw_AssetLedgerFact`: mișcarea de valoare pe bun, regim, tip de operație, document |
| Vânzări V5 | Columnstore pe `sales.SalesInvoiceLine` |
| Nucleu | Partenerul cu `GroupCompanyId` marchează tranzacțiile între firmele grupului; setarea de instalare `ReportingConnection` |

## 10. Rapoarte

„Starea instantaneelor” (ultima rulare, durata, rândurile), „Accesul extern” (cheile, ultima folosire, rândurile citite).

## 11. Ecrane principale

Machetele sunt în [ecrane/bi2-depozit/](ecrane/bi2-depozit/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Surse de date | Seturile de date publicate, granularitatea, rândurile, versiunea, ultima actualizare, instantaneele și starea lor | `bi2-01-surse-date` |
| Acces extern | Activarea, firmele expuse, vederile `ext_*`, cheile OData, jurnalul de acces | `bi2-02-acces-extern` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| BI2-A1 | Testul de arhitectură găsește orice referință din `rpt` la un tabel intern al altui modul | Test de arhitectură în pipeline-ul de build |
| BI2-A2 | Cifra de afaceri a grupului pe noiembrie 2026 dă 5.602.900 lei, fără tranzacțiile între firmele grupului | Test de integrare pe datele demo |
| BI2-A3 | Instantaneul unei luni închise nu se rescrie (`62111`); redeschiderea lunii îl deblochează | Test de integrare |
| BI2-A4 | Jobul de instantanee rulat de două ori în aceeași noapte nu dublează rândurile | Test de integrare |
| BI2-A5 | Utilizatorul `erp_reporting_reader` nu poate citi nimic în afara `rpt.ext_*` | Test de integrare pe permisiuni SQL |
| BI2-A6 | Analiza vânzărilor pe 5 milioane de rânduri, pe lună și categorie, răspunde sub 3 secunde cu columnstore | Test de performanță pe date generate |
| BI2-A7 | Grupurile de salarii sub 5 persoane apar „sub prag” | Test unitar (pregătit pentru 02.09) |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Unde stă depozitul | În schema `rpt` din baza principală; replica doar citire doar la cerere | ✓ Aprobat autonom (27.11.2026) |
| 2 | Sursele | Doar vederile publicate de module, contract versionat | ✓ Aprobat autonom |
| 3 | Istoria | Instantanee nocturne pentru solduri, înghețate la închiderea lunii | ✓ Aprobat autonom |
| 4 | Performanța | Columnstore (SQL Server Standard), cache pe rezultat cu cheia filtrului de vizibilitate | ✓ Aprobat autonom |
| 5 | Moneda grupului | Curs BNR mediu pentru fluxuri, de închidere pentru solduri; fără tranzacțiile din grup | ✓ Aprobat autonom |
| 6 | Accesul extern | Opțional: SQL doar citire pe `rpt.ext_*` și OData, fără date personale și fără măsuri sensibile | ✓ Aprobat autonom |
| 7 | Datele personale | Nu intră în fapte; salariile doar pe grupuri de minim 5 | ✓ Aprobat autonom |

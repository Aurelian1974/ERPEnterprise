# ERP · 02.13 Raportare și BI · BI3 Rapoarte și analize: generatorul fără SQL, rapoarte cu parametri, pachete, export, programare

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P4, P9, P18, P21). A treia din cele 5 părți ale modulului 02.13.

Plaja de erori BI3: `62250–62449`. Documente părinte:

- [BI1](BI1-CONFIGURARE-CATALOG.md): catalogul, măsurile, dimensiunile și securitatea;
- [BI2](BI2-DEPOZIT-DATE.md): faptele și `IReportingQueryExecutor`;
- [Nucleu](../01-nucleu/NUCLEU.md) §3.5: `core.ReportView` și `core.ReportSchedule`, exportul, joburile și notificările;
- [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- **Analiza** e raportul pe care utilizatorul îl construiește singur, fără SQL. Alege:
  - un set de date (de exemplu Vânzări);
  - măsurile (Cifra de afaceri netă, Marja %);
  - dimensiunile pe rânduri și pe coloane (Categorie × Lună);
  - filtrele.

  Rezultatul e un tabel pivot și, opțional, un grafic (P4, ca „întrebările” din Metabase și „Explore” din Superset). Analiza se salvează ca `core.ReportView` cu `ReportCode = rpt.Analysis` și definiția în `FiltersJson` / `ColumnsJson`. Apare în catalog la „Personalizate”.
- **Raportul cu parametri** e un raport fix, scris de dezvoltatori: o procedură de citire și un șablon QuestPDF, cu parametrii declarați în catalog (`ParamsSchemaJson`). Exemple: „Raportul lunar al conducerii”, „Fișa clientului”.
- **Pachetul de raportare** strânge mai multe rapoarte și analize într-un singur PDF, cu cuprins. Se trimite programat (P18).
- Exportul, programarea și desfășurarea folosesc motorul Nucleu. BI3 adaugă doar generatorul, pivotul, graficele și pachetele.

## 1. Scop și perimetru

| Zonă | Ce face BI3 | Ce NU face |
| --- | --- | --- |
| Generatorul de analize | Alegerea setului de date, a măsurilor, a dimensiunilor și a filtrelor; pivot cu subtotaluri și totaluri; sortare, top N, comparație cu perioada anterioară și cu anul anterior; grafic; desfășurare; salvare și partajare | SQL scris de utilizator (decizia 2) |
| Rapoarte cu parametri | Rapoartele tipărite ale modulului BI (de conducere) și cadrul pentru cele ale modulelor | Rapoartele modulelor (le scriu modulele) |
| Pachete | Mai multe rapoarte într-un PDF, cu copertă, cuprins și parametri comuni (firmă, lună) | — |
| Export | Excel cu tabel și pivot gata făcut (ClosedXML), PDF cu grafice, CSV | — |
| Programare | Prin `core.ReportSchedule`, extins la analize și pachete; „trimite doar dacă există rânduri” | — |

## 2. Model de domeniu

```
 core.ReportView (ReportCode = 'rpt.Analysis' | codul raportului) ── core.ReportSchedule
   └─ FiltersJson / ColumnsJson: definiția analizei (setul, măsurile, rândurile, coloanele, filtrele, graficul)

 rpt.ReportPack (pachet) ── rpt.ReportPackItem × N (ReportViewId sau ReportCatalogId + parametri, ordine)
 rpt.ResultCache (cheia interogării, utilizatorul de vizibilitate, rezultatul comprimat, expiră la)
 rpt.ExportJob (exporturile mari, în fundal)
```

Definiția unei analize (JSON validat la salvare):

```json
{
  "dataset": "sales",
  "measures": ["sales.NetRevenue", "sales.GrossMarginPct", "sales.Quantity"],
  "rows": ["item.Category", "item.Family"],
  "columns": ["date.Month"],
  "filters": [
    { "field": "date.Month", "op": "between", "value": ["2026-09", "2026-11"] },
    { "field": "partner.County", "op": "in", "value": ["CJ", "AB"] }
  ],
  "compare": "PreviousYear",
  "top": null,
  "chart": { "type": "bar", "stacked": false, "measure": "sales.NetRevenue", "series": "date.Month" },
  "groupCurrency": false
}
```

Reguli de business:

1. **Măsurile se aleg doar dintr-un singur set de date** pe analiză. Combinarea vânzărilor cu bugetul se face prin seturile combinate publicate (de exemplu `budget.SalesVsBudget`, BI5), nu în generator. Altfel totalurile ar fi greșite, din cauza granularităților diferite (`62251`).
2. **Măsurile neaditive** (procente, medii) se recalculează la fiecare nivel din componente, nu se adună. Marja % la total e marja totală împărțită la cifra de afaceri totală, nu suma procentelor (BI1 §3.2).
3. **Măsurile semi-aditive** (soldurile) se iau la ultima zi a perioadei pe axa timpului și se adună pe celelalte dimensiuni.
4. **Limita de afișare** e `MaxAdHocRows` (200.000). Peste ea, generatorul cere restrângerea filtrelor sau oferă exportul în fundal (`62260`).
5. **O analiză partajată rulează cu drepturile celui care o deschide**, nu ale autorului. Același tabel arată date diferite unui agent și șefului lui.
6. **Rapoartele legale** (`IsLegal`, BI1) nu se pot deschide în generator pentru modificare. Se pot doar rula și exporta (`62270`).

## 3. Fluxuri detaliate

### 3.1 Generatorul de analize

Ecranul are trei zone:

- **stânga**: setul de date, cu măsurile și dimensiunile grupate pe domenii; fiecare are descrierea din catalog, la trecerea cu mouse-ul;
- **sus**: rafturile „Rânduri”, „Coloane”, „Filtre”, „Măsuri”, pe care se trag câmpurile;
- **centru**: rezultatul.

Opțiuni:

- **Comparații:** perioada anterioară sau același interval din anul anterior, cu coloanele „Δ” și „Δ %”. Calendarul vine din `rpt.DimDate`.
- **Top N și „Altele”:** de exemplu primii 10 clienți, plus un rând „Alți 2.896 clienți”.
- **Formatarea condiționată:** valorile negative cu roșu, bara de date în celulă, semaforul măsurii (după `GoodDirection`).
- **Graficul:** bare, linii, arii, combinat (bare și linie), plăcintă (doar pentru cel mult 6 felii; altfel se propune bara), puncte, hartă pe județe (fișier GeoJSON al județelor, open data), tabel termic. Graficele folosesc Apache ECharts (Apache 2.0).
- **Desfășurarea:** clicul pe o celulă deschide rândurile de fapt din spate, iar apoi documentul sursă (BI2 §4.2).
- **Salvarea:** nume, descriere, dosar, partajarea pe rol, adăugarea la un tablou (BI4) sau la un pachet.

### 3.2 Exportul în Excel (P9)

Exportul unei analize produce un fișier `.xlsx` (ClosedXML) cu trei foi:

- „Rezultat”: pivotul formatat, cu totaluri;
- „Date”: rândurile de detaliu, ca tabel Excel cu filtre, până la 1.048.576 de rânduri, generat în flux;
- „Despre”: definiția analizei, filtrele, data rulării, utilizatorul, descrierile măsurilor.

Cu „Excel cu pivot”, foaia „Date” are și un tabel pivot Excel nativ peste ea, pe care utilizatorul îl poate rearanja fără conexiune la ERP.

### 3.3 Rapoartele cu parametri

Șablonul QuestPDF are:

- antetul firmei;
- parametrii afișați;
- tabelele;
- graficele, randate pe server ca SVG cu ECharts în modul server-side și incluse ca imagine vectorială.

Rapoartele livrate de BI3:

| Cod | Raport | Conținut |
| --- | --- | --- |
| `rpt.ExecutiveMonthly` | Raportul lunar al conducerii | KPI-urile lunii față de buget și anul anterior, cifra de afaceri pe firmă și canal, marja, primele 10 abateri de la buget, creanțele pe vechime, stocul și rotația, disponibilul, pipeline-ul CRM, mijloacele fixe intrate |
| `rpt.GroupKpis` | Indicatorii grupului | Pe firme și pe grup (fără tranzacțiile din grup) |
| `rpt.CustomerProfitability` | Profitabilitatea clienților | Cifra de afaceri, marja, costul de servire estimat (livrări, retururi, zile de încasare), clasarea ABC |
| `rpt.ItemPerformance` | Performanța articolelor | Vânzări, marjă, rotație, stoc fără mișcare, ABC × XYZ |

### 3.4 Pachetele de raportare (P18)

Un pachet are:

- parametri comuni (firmele, luna);
- coperta;
- cuprinsul generat;
- rapoartele și analizele, în ordine.

Programarea pachetului trimite un singur PDF. Pachetul livrat „Închiderea lunii pentru conducere” cuprinde:

1. `rpt.ExecutiveMonthly`;
2. balanța sintetică (C5);
3. contul de profit și pierdere pe centre de cost (C5);
4. creanțele pe vechime (F3);
5. stocul pe gestiuni (S7);
6. plan vs realizat pe BVC (BI5).

Se trimite pe 5 ale lunii, la 08:00, după închiderea lunii, iar condiția „după `LunaInchisa`” amână trimiterea până la închidere.

### 3.5 Programarea extinsă

`core.ReportSchedule` primește câmpurile:

- `ReportPackId`, pentru pachete;
- `SendOnlyIfRows`, pentru alerte de tip listă (de exemplu „clienți peste limita de credit”, trimis doar dacă există);
- `WaitForEvent` (`LunaInchisa`);
- `ParamsJson` cu parametri relativi: „luna trecută”, „ieri”, „săptămâna curentă”.

Destinatarii primesc datele filtrate după propriile drepturi (regula Nucleu). Un destinatar fără drept pe raport e refuzat la salvare (`62280`).

### 3.6 Filtrele asociative și vederile salvate (P25, P26, adăugat pe 28.11.2026)

**Filtrele asociative.** Lista de valori a unui filtru (de exemplu „Județ” sau „Agent”) arată trei stări:

| Stare | Afișare | Înseamnă |
| --- | --- | --- |
| Selectat | verde, bifat | Valoarea e în filtru |
| Posibil | normal | Are date sub celelalte filtre |
| Fără date | gri, la finalul listei | Nu are rânduri sub celelalte filtre (de exemplu agentul fără vânzări în Alba) |

Valorile fără date nu dispar, pentru că lipsa lor e o informație. Starea se calculează cu o interogare `EXISTS` pe setul de date, cu drepturile utilizatorului, limitată la primele 500 de valori. Bara de filtre păstrează istoricul sesiunii, cu „înapoi” și „înainte” (cel mult 50 de pași).

**Vederile salvate cu filtre dinamice.** O analiză salvată (`core.ReportView`) poate avea:

- filtre relative pe dată: „luna curentă”, „trimestrul trecut”, „ultimele 12 luni”, „anul la zi”;
- filtre pe măsură, calculate la fiecare deschidere: „clienții cu cifra de afaceri pe an > 100.000 lei”, „primii 20 de clienți după marjă”;
- marcajul „implicită pentru mine”, care o deschide direct la intrarea în raport (coloana nouă `IsDefault` pe `core.ReportView`, unică pe utilizator și raport).

Exemplu: Radu Stan își salvează „Clienții HoReCa peste 100.000 lei, trimestrul curent” ca vedere implicită. Din ianuarie 2027, aceeași vedere arată trimestrul 1, fără să o modifice.

## 4. Exemplu: analiza lui Radu Stan

Radu Stan construiește „Marja pe categorie, sept.–nov. 2026, Cluj și Alba, față de anul anterior”:

- setul Vânzări;
- măsurile Cifra de afaceri netă și Marja %;
- rândurile pe categorie;
- coloanele pe lună;
- comparația cu anul anterior.

Rezultatul pe noiembrie:

| Categorie | Cifra de afaceri netă | Δ față de nov. 2025 | Marja % | Marja % în nov. 2025 |
| --- | --- | --- | --- | --- |
| Apă plată | 612.400 lei | +9,8 % | 21,4 % | 22,6 % |
| Apă minerală | 388.900 lei | +4,1 % | 23,8 % | 23,5 % |
| Cafea | 214.600 lei | −6,2 % | 31,2 % | 33,0 % |
| Ceai și răcoritoare | 176.300 lei | +12,7 % | 25,1 % | 24,8 % |
| Dozatoare (chirii) | 48.800 lei | +18,0 % | 64,0 % | 63,1 % |
| **Total** | **1.441.000 lei** | **+6,1 %** | **25,4 %** | **26,1 %** |

Clicul pe „Cafea · noiembrie” deschide liniile de factură. Prima e Pizzeria Napoli, cu cafeaua oprită din septembrie (CR5).

Radu salvează analiza, o partajează pe rolul „Agent vânzări” și o programează lunar pe e-mail, în ziua 2, către Cristina Marin, Vlad Georgescu și Ioana Rusu. Fiecare primește doar clienții din portofoliul lui.

## 5. Evenimente

| Eveniment | Direcție | Folosire |
| --- | --- | --- |
| `LunaInchisa` v1 | Consumat (C3) | Declanșează programările cu `WaitForEvent` |

## 6. Contracte publice

```csharp
public interface IReportRenderer          // implementat de BI3; folosit de motorul Nucleu pentru rapoartele cu parametri și pachete
{
    Task<Stream> RenderPdfAsync(string reportCode, JsonElement parameters, int userId, CancellationToken ct);
    Task<Stream> RenderXlsxAsync(string reportCode, JsonElement parameters, int userId, bool withPivot, CancellationToken ct);
}
```

## 7. Schema SQL (partea BI3)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `rpt.ReportPack`, `rpt.ReportPackItem` | `Id int` | UX `(CompanyGroupId, Code)`; FK `ReportPackId` |
| `rpt.ResultCache` | `CacheKey binary(32)` (SHA-256 al interogării, al filtrului de vizibilitate și al versiunilor măsurilor) | IX `(ExpiresAt)`; curățat de job la 15 minute |
| `rpt.ExportJob` | `Id bigint` | IX `(UserId, Status)` |
| `core.ReportSchedule` (extins) | — | Coloane noi: `ReportPackId`, `SendOnlyIfRows`, `WaitForEvent`, `ParamsJson` (precizare în Nucleu) |

**Erorile BI3** (`62250–62449`):

- `62251` `RPT_ANALYSIS_MIXED_DATASETS`
- `62252` `RPT_ANALYSIS_INVALID_DEFINITION`
- `62253` `RPT_ANALYSIS_UNKNOWN_FIELD`: câmp retras din catalog; analiza se deschide cu câmpul marcat
- `62260` `RPT_ANALYSIS_ROW_LIMIT`
- `62270` `RPT_LEGAL_REPORT_READONLY`
- `62280` `RPT_SCHEDULE_RECIPIENT_FORBIDDEN`
- `62290` `RPT_PACK_ITEM_FAILED`: pachetul se trimite cu pagina „raport indisponibil” și notifică autorul

## 8. Permisiuni

- `Rpt.Analysis.Create` (pachetul BI: Analist, Director economic, șefii de departament);
- `Rpt.Analysis.Share`;
- `Rpt.Pack.Manage`;
- `Rpt.Schedule.Manage`, pe rapoartele la care utilizatorul are drept.

## 9. Cerințe legale și integrări

| Cerință sau componentă | Ce înseamnă |
| --- | --- |
| Apache ECharts | Licență Apache 2.0, folosit în browser și pe server pentru SVG |
| ClosedXML | Licență MIT |
| QuestPDF | Ales deja în proiect pentru PDF, cu licența Community (gratuită sub 1 milion USD venit anual brut). Decis de Aurelian pe 28.11.2026 (decizia 8): rămâne QuestPDF; licența comercială se cumpără doar dacă și când pragul e depășit |
| Nucleu | Extinderea `core.ReportSchedule`; joburile și notificările |

## 10. Rapoarte

Cele din §3.3 și pachetul din §3.4.

## 11. Ecrane principale

Machetele sunt în [ecrane/bi3-analize/](ecrane/bi3-analize/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Generatorul de analize (principal) | Analiza lui Radu Stan: rafturile, pivotul cu comparația, graficul | `bi3-01-generator-analize` |
| Raportul lunar al conducerii | `rpt.ExecutiveMonthly` pe octombrie 2026 (trimis în pachet pe 5.11), ca pagină PDF | `bi3-02-raport-conducere` |
| Pachete și programări | Pachetul „Închiderea lunii pentru conducere”: rapoartele, destinatarii, condiția de închidere | `bi3-03-pachet-programare` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| BI3-A1 | Marja % la total e calculată din componente (25,4 %), nu din suma procentelor | Test unitar pe generatorul SQL |
| BI3-A2 | Analiza partajată arată Cristinei doar clienții ei, inclusiv în e-mailul programat | Test de integrare |
| BI3-A3 | Filtrele sunt mereu parametri SQL; o valoare cu `'; DROP` nu produce eroare de sintaxă | Test de securitate |
| BI3-A4 | Exportul cu 1 milion de rânduri rulează în fundal, sub 512 MB de memorie (în flux) | Test de performanță |
| BI3-A5 | Pachetul cu `WaitForEvent = LunaInchisa` nu pleacă înainte de închidere | Test de integrare cu ceasul simulat |
| BI3-A6 | Un raport legal nu se poate deschide în generator (`62270`) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Analizele utilizatorilor | Generator pe stratul semantic, salvate ca `core.ReportView` | ✓ Aprobat autonom (27.11.2026) |
| 2 | SQL scris de utilizatori | Nu; doar măsuri și dimensiuni din catalog | ✓ Aprobat autonom |
| 3 | Un set de date pe analiză | Da; combinațiile prin seturi publicate | ✓ Aprobat autonom |
| 4 | Graficele | Apache ECharts, în browser și pe server | ✓ Aprobat autonom |
| 5 | Exportul Excel | Trei foi, opțional cu pivot nativ | ✓ Aprobat autonom |
| 6 | Pachetele de raportare | Da, cu programare și condiția „după închiderea lunii” | ✓ Aprobat autonom |
| 7 | Drepturile la rulare | Ale celui care deschide sau primește, nu ale autorului | ✓ Aprobat autonom |
| 8 | Licența QuestPDF | Rămâne QuestPDF cu licența Community (gratuită sub 1 milion USD venit anual brut). Licența comercială se cumpără doar dacă și când pragul e depășit. PDFsharp / MigraDoc nu se mai ia în calcul | ✓ Decis de Aurelian (28.11.2026) |
| 9 | Filtrele (Qlik) | Valorile fără date rămân în listă, gri, plus istoric înapoi / înainte; calculate în SQL | ✓ Aprobat autonom (28.11.2026) |
| 10 | Vederile salvate (Qlik) | Filtre relative și filtre pe măsură, calculate la deschidere; vedere implicită pe utilizator | ✓ Aprobat autonom (28.11.2026) |

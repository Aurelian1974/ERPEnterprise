# ERP · 02.15 Mijloace fixe și obiecte de inventar · M3 Amortizare: planul, calculul lunar, contabil vs fiscal, proiecția

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)** (lucru autonom cerut de Aurelian; deciziile 1–10 adoptate pe recomandare). A treia din cele 7 părți ale modulului 02.15, după [M1](M1-CONFIGURARE.md) și [M2](M2-REGISTRU-INTRARI.md).

Schema `fa`, prefix `Erp.FixedAssets`, erorile M3 în plaja `65250–65399`. Această parte acoperă **planul de amortizare** al fiecărei fișe pe fiecare regim, **fișa de amortizare lunară** (documentul care calculează și trimite spre contare amortizarea lunii), citirile pentru amortizarea pe unități produse, transferul lunar al surplusului din reevaluare, **diferențele contabil–fiscal** pentru D101, legătura cu închiderea lunii (C3) și **proiecția** amortizării pe anii următori. Documente părinte: [M1](M1-CONFIGURARE.md) §3.6 (profiluri, metode, convenții), [M2](M2-REGISTRU-INTRARI.md) §3.2 (regimurile fișei) și §2 regula 2 (jurnalul valorilor), [C2](../02-finante-contabilitate/C2-MODELE-DE-CONTARE-SI-NOTE.md), [C3](../02-finante-contabilitate/C3-INCHIDERI.md) (verificarea `DEPRECIATION`), [C4](../02-finante-contabilitate/C4-DECLARATII.md) (D101), [F5](../02-finante-contabilitate/F5-PLANIFICARE-TREZORERIE.md). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- Produs ERP on-premise, SQL Server case sensitive (`SQL-21`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite. Calculele de valoare se fac în C# (calculatorul de plan, testat unitar, `TST-02`) și se scriu prin proceduri; rotunjirea doar prin `IPrecisionService` (`NET-14`).
- Fiecare fișă are câte un **regim** (`fa.AssetBook`): `BOOK` (contabil, singurul contat), `TAX` (fiscal), opțional `IFRS`. Amortizarea se calculează pe toate, dar doar `BOOK` produce notă contabilă (`6811` = `28xx`).
- Amortizarea începe **din luna următoare PIF-ului** și se calculează **inclusiv în luna ieșirii** (M1 §3.6, convențiile livrate). Valorile curente ale fișei vin din jurnalul `fa.AssetLedger` (M2): amortizarea lunii scrie rânduri `DEPRECIATION` în jurnal la validarea documentului lunar.
- **Planul e o prognoză, documentul lunar e realitatea.** Planul (`fa.DepreciationSchedule`) se recalculează oricând se schimbă valoarea sau durata (M4); documentul lunar validat nu se mai schimbă (se stornează doar în perioadă deschisă, 3.4).

## 1. Scop și perimetru

| Zonă | Ce face M3 | Ce NU face (și cine face) |
| --- | --- | --- |
| Planul | Planul lunar pe fișă și regim, generat la PIF și recalculat prospectiv la orice operație de valoare, durată sau suspendare | Operațiile care îl schimbă (M4) |
| Calculul lunar | Fișa de amortizare lunară pe firmă (`AML`), ca ciornă prin job, verificată, validată și contată | Nota contabilă (C2, din eveniment) |
| Unități produse | Citirile lunare (ore, km, piese) pentru fișele cu profilul pe unități | Pontajul utilajelor în producție (02.08, viitor: trimite citirile prin eveniment) |
| Reevaluare | Transferul lunar `105` = `1175` (dacă setarea e „Lunar”) | Reevaluarea (M4) |
| Fiscal | Amortizarea fiscală, partea nedeductibilă (cotă, plafon), diferențele pentru D101 | D101 (C4) |
| Închiderea lunii | Starea amortizării lunii, citită de C3 (`DEPRECIATION`) | Închiderea (C3) |
| Proiecția | Amortizarea pe următorii 1–5 ani, pe grupă și centru de cost, pentru buget și F5 | Bugetul ca atare (F5, 02.13) |

## 2. Model de domeniu

```
 fa.AssetBook (M2) ── fa.DepreciationSchedule × N   (fișă × regim × lună: cota planificată, baza, luni rămase, versiunea planului)
                  └─ fa.AssetUsageReading × N       (lună: unități folosite, pentru profilul pe unități)

 fa.DepreciationRun (antet: firmă, lună, stare, totaluri)
   └─ fa.DepreciationRunLine × N                    (fișă × regim: cota lunii, centrul de cost, grupa, motiv dacă diferă de plan)
   └─ fa.DepreciationRunException × N               (fișe sărite: fără PIF, suspendate, fără citire, amortizate complet)
 la validare → fa.AssetLedger (DEPRECIATION, REVALUATION_TRANSFER) + eveniment AmortizareCalculata (doar BOOK în Accounting)
```

Reguli de business:

1. **Un singur document de amortizare pe firmă și lună** (`UX (CompanyId, PeriodMonth) WHERE Status <> 9`). Conține toate fișele eligibile și toate regimurile active.
2. **Lunile se calculează în ordine.** Luna N nu se poate valida înainte de luna N−1 (`65251`), iar o fișă intrată cu PIF în trecut (într-o lună deja calculată) nu reface lunile validate: plusul apare ca **recuperare** în prima lună deschisă (3.5), cu motiv.
3. **Cota lunii vine din plan, nu se editează.** Utilizatorul nu scrie sume: dacă o cotă e greșită, corectează cauza (durata, valoarea, PIF-ul) prin document M4 și regenerează ciorna. Excepție: citirile pentru unități produse.
4. **Planul se recalculează prospectiv.** O schimbare de valoare sau de durată nu modifică lunile trecute: valoarea rămasă la data schimbării se împarte pe lunile rămase, după metodă (M1 §3.6). Versiunea planului crește la fiecare recalculare; fiecare rând de plan ține documentul care l-a produs.
5. **Ultima lună închide exact valoarea.** Amortizarea cumulată la sfârșitul duratei e egală cu valoarea amortizabilă (brut − valoare reziduală − depreciere), diferența de rotunjire fiind pusă în ultima lună (M1 §3.6 `RoundingMode`).
6. **Amortizarea se contează pe centrul de cost valabil în lună.** Dacă un bon de mișcare (M4) schimbă centrul de cost în mijlocul lunii, toată luna merge pe centrul de cost de la sfârșitul lunii (decizia 4); împărțirea pe zile nu se face.

## 3. Entități și documente

### 3.1 Planul de amortizare (`fa.DepreciationSchedule`)

| Câmp | Reguli |
| --- | --- |
| `AssetBookId`, `PeriodMonth` | UX pe pereche și versiune activă |
| `PlanVersion` | Crește la fiecare recalculare; rândurile vechi rămân pentru istoric (`IsCurrent = 0`) |
| `OpeningNet`, `Amount`, `ClosingNet` | Valoarea netă la început, cota, valoarea netă la sfârșit |
| `MethodPhase` | Pentru degresiv: `DEGRESSIVE` sau `LINEAR` (după trecerea pe liniar); pentru accelerat: `FIRST_YEAR` sau `LINEAR` |
| `IsSuspended` | Luna suspendată (conservare) |
| `SourceDocumentType`, `SourceDocumentId` | Documentul care a generat versiunea (PIF, modernizare, reevaluare...) |

**Calculatorul de plan** (`IDepreciationCalculator`, C#, pur): primește regimul (profil, durată, luni anterioare, valori, data de start, suspendări cunoscute) și întoarce rândurile lunare. E același cod pentru previzualizarea din M1 (`m1-04`), plan, proiecție și calculul lunar. Reguli pe metodă:

| Metodă | Cota lunară |
| --- | --- |
| Liniară | (Valoarea amortizabilă rămasă) / (lunile rămase) |
| Degresivă | (Valoarea rămasă la începutul anului de funcționare × cota degresivă) / 12; când cota liniară pe durata rămasă ≥ cota degresivă, trece pe liniar până la sfârșit |
| Accelerată | Primul an de funcționare: valoarea × procentul (cel mult 50 %) / 12; apoi liniar pe durata rămasă |
| Pe unități produse | Valoarea amortizabilă × (unitățile lunii / `UnitsTotal`), plafonat la valoarea rămasă; fără citire în lună → 0 și excepție |
| Fără amortizare | 0 (terenuri, opere de artă) |

### 3.2 Fișa de amortizare lunară (`fa.DepreciationRun`, documentul `AML`)

**Antet**: `CompanyId`, `TransactionTypeId` (`AML-LUN`), `Number` (`AML 2026-11`), `PeriodMonth`, `AccountingDate` (ultima zi a lunii), `Status` (1 Ciornă · 3 Validat · 8 Stornat · 9 Anulat), totalurile pe regim, `GeneratedBy` (job sau utilizator), `SimulationOnly` (rulare fără salvare).

**Generarea** (`fa.usp_DepreciationRun_Generate`, la `DepreciationRunDay` din M1 §3.11 sau manual):

1. Selectează fișele eligibile în lună: stare 2 sau 3, `DepreciationStart <= lună`, neieșite înainte de lună, cu valoare rămasă > 0 pe regim.
2. Pentru fiecare fișă și regim ia cota din planul curent; pentru suspendate (conservare) pune 0 pe regimurile suspendate; pentru profilul pe unități, ia citirea lunii.
3. Adaugă liniile de recuperare (regula 2) și transferul surplusului din reevaluare (`105` = `1175`, dacă setarea e „Lunar”: diferența dintre cota pe valoarea reevaluată și cota pe costul istoric, din plan).
4. Scrie excepțiile: fișe în curs de punere în funcțiune de peste 30 de zile, fără citire, cu centru de cost lipsă, amortizate complet în această lună (trec în starea 4 la validare).

**Validarea** (`fa.usp_DepreciationRun_Validate`, o tranzacție SQL): verifică luna anterioară validată (`65251`), perioada deschisă (C3), excepțiile blocante (centru de cost lipsă când `DepreciationByCostCenter = 1`, `65252`); scrie rândurile `DEPRECIATION` în `fa.AssetLedger` pe toate regimurile; trece fișele terminate în starea 4; publică `AmortizareCalculata` v1 cu blocul `Accounting` (componenta `Depreciation` pe grupă și centru de cost, sau pe fișă după `DepreciationPostingDetail`, și componenta `RevaluationSurplus` pentru transfer).

**Simularea**: același calcul, fără scriere, pentru o lună viitoare sau curentă (folosită și de proiecție).

### 3.3 Citirile pentru unități produse (`fa.AssetUsageReading`)

Pentru fișele cu profilul `UNIT` (M1 §3.6): o citire pe lună (`PeriodMonth`, `Units`, `MeterValue` opțional, sursa: manual, import, eveniment din Producție). Citirea lipsă dă cota 0 și o excepție (nu blochează). Suma citirilor nu poate depăși `UnitsTotal` fără schimbarea estimării prin M4 (`65254`).

### 3.4 Stornarea și refacerea

Un `AML` validat se poate storna doar dacă luna e deschisă (C3) și nu există `AML` validat pentru luna următoare (`65253`). Stornarea inversează rândurile din jurnal și publică evenimentul cu `IsReversal`; apoi se regenerează ciorna. După închiderea lunii, orice corecție merge în prima lună deschisă, ca recuperare (3.5).

### 3.5 Recuperarea

Linie de document lunar cu `LineKind = CATCH_UP`, generată automat când planul arată amortizare pentru luni deja validate, în care fișa nu a fost inclusă (de exemplu PIF înregistrat cu întârziere sau o corecție de durată retroactivă aprobată). Are motiv obligatoriu, e marcată în raport și se contează cu aceeași notă (pe luna curentă). Pe regimul fiscal, recuperarea retroactivă se raportează separat (pentru verificarea deductibilității).

### 3.6 Contabil vs fiscal (`fa.vw_TaxDepreciationDifferences`)

Pentru fiecare fișă, lună și an: amortizarea contabilă, amortizarea fiscală, partea fiscală **nedeductibilă** (din `AssetGroup.NonDeductibleShare` și `FiscalDepreciationCap`, M1 §3.5) și diferența. Agregat anual, pe categoriile din D101: cheltuieli cu amortizarea contabilă (nedeductibile integral) și amortizarea fiscală deductibilă (deducere), plus diferențele temporare pentru impozitul amânat (doar informativ, `IFRS`). Expus către C4 prin `IFiscalDepreciationQuery` (6).

### 3.7 Proiecția

`IDepreciationForecast` (6) rulează calculatorul pe planurile curente pentru 1–5 ani, pe lună, cu opțiunea de a include investițiile în curs (cu data PIF estimată și profilul grupei țintă) și achizițiile planificate (din necesarul aprobat A2 cu `NonStockAssetKind = 1`, opțional). Rezultatul alimentează bugetul (02.13) și planificarea de trezorerie F5 (doar ca informație de cheltuială necash).

## 4. Fluxuri principale

### 4.1 Amortizarea lunii noiembrie 2026 (exemplu)

1. Pe 01.12.2026, jobul generează ciorna `AML 2026-11` pentru Exemplu Distribuție: 389 de fișe eligibile, `BOOK` 41.836,22 lei, `TAX` 47.912,40 lei; transfer de surplus din reevaluare 1.562,50 lei (sediul administrativ Cluj, MF 000009, reevaluat la 31.12.2024).
2. Excepții: serverul MF 000442 fără PIF de 12 zile (informativ); linia de ambalare MF 000377 și alte 2 fișe pe unități (MF 000392, MF 000405) fără citirea din noiembrie (cota 0); 2 fișe în conservare (`TAX` suspendat, `BOOK` continuă); MF 000118 amortizat complet în octombrie (exclus).
3. Sorina Munteanu introduce citirea liniei de ambalare (412 ore) și regenerează ciorna (`BOOK` 42.013,90 lei); pentru celelalte două citirile vin până la validare sau cota rămâne 0 în noiembrie și se recuperează prin plan în lunile următoare (amortizarea pe unități nu are recuperare retroactivă: unitățile se cumulează). Apoi validează.
4. Nota C2 (pe grupă și centru de cost): `6811` = `2812` 9.850,00; `6811` = `2813` 23.552,85 + 177,68; `6811` = `2814` 6.900,37; `6811` = `2808` 1.533,00; `105` = `1175` 1.562,50.
5. C3 vede `DEPRECIATION` îndeplinit pentru noiembrie (documentul validat acoperă toate fișele eligibile).

### 4.2 Schimbare de durată la jumătatea vieții

Pe stivuitorul MF 000412 (`BOOK` liniar, 96 de luni, 1.025,00 / lună), după 20 de luni contabilul aprobă (M4, `MVM-DNU`) o durată totală de 120 de luni. Valoarea rămasă 77.900,00 se împarte pe cele 100 de luni rămase: 779,00 / lună din luna următoare documentului. Lunile trecute nu se schimbă. Pe `TAX` durata rămâne neschimbată (operația a fost doar pe `BOOK`).

### 4.3 Cazuri limită

| Caz | Comportament |
| --- | --- |
| PIF în ultima zi a lunii | Amortizarea începe din luna următoare (convenția), indiferent de zi |
| Ieșire în prima zi a lunii | Luna ieșirii se amortizează integral (convenția livrată); cu `StopConvention = 2`, nu |
| Fișă cu valoare rămasă sub 1,00 leu | Ultima cotă o închide; fișa trece în starea 4 |
| Lună cu amortizarea deja validată și o intrare nouă cu PIF în acea lună | Fără efect: amortizarea începe în luna următoare |
| Bun în conservare care iese în aceeași lună | Fără amortizare pe regimurile suspendate; ieșirea folosește valoarea rămasă la data ieșirii |
| Firmă cu `IFRS` activ și convenția „jumătate de lună” | Prima și ultima lună au jumătate de cotă doar pe `IFRS` |
| Amortizare fiscală a autoturismului peste plafon | Cota fiscală integrală se calculează; partea peste plafon e nedeductibilă în 3.6 |

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori / sursă |
| --- | --- | --- | --- |
| `AmortizareCalculata` v1 | Publicat | `DepreciationRunId`, luna, totalurile pe regim, `Accounting` (doar `BOOK`: `Depreciation` pe grupă și centru de cost, `RevaluationSurplus`) | C2 (notă), C3 (stare), 02.13 |
| `AmortizareStornata` v1 | Publicat | Același conținut cu `IsReversal` | C2, C3 |
| `MijlocFixAmortizatComplet` v1 | Publicat | `AssetId`, luna | M7 (rapoarte), Administrator patrimoniu (notificare: bunuri amortizate încă în folosință) |
| `CitireUtilajInregistrata` v1 | Consumat (viitor, de la 02.08) | `AssetId`, luna, unitățile | Scrie `fa.AssetUsageReading` |
| `MijlocFixModificat`, `MijlocFixPusInFunctiune`, `MijlocFixIesit` | Consumate (intern, M2, M4, M5) | — | Recalculează planul (în tranzacția documentului, nu asincron) |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M3)

```csharp
public interface IDepreciationStatusQuery
{
    /// Folosit de C3 (verificarea DEPRECIATION): luna e acoperită dacă documentul AML e validat și nu există fișe eligibile neincluse.
    Task<DepreciationMonthStatusDto> GetMonthStatusAsync(int companyId, DateOnly periodMonth, CancellationToken ct);
}
public sealed record DepreciationMonthStatusDto(bool IsValidated, string? RunNumber, int EligibleAssets,
    int MissingAssets, decimal BookAmount, decimal TaxAmount);

public interface IFiscalDepreciationQuery
{
    /// Pentru D101 (C4): amortizarea contabilă, fiscală, nedeductibilă, pe an și categorie.
    Task<FiscalDepreciationSummaryDto> GetYearSummaryAsync(int companyId, int year, CancellationToken ct);
}
public sealed record FiscalDepreciationSummaryDto(int Year, decimal BookDepreciation, decimal TaxDepreciation,
    decimal TaxNonDeductible, decimal TaxDeductible, decimal RevaluationReserveTaxable);

public interface IDepreciationForecast
{
    Task<IReadOnlyList<DepreciationForecastRowDto>> ForecastAsync(DepreciationForecastRequestDto request, CancellationToken ct);
}
public sealed record DepreciationForecastRequestDto(int CompanyId, DateOnly FromMonth, int Months, string BookCode,
    bool IncludeConstructionInProgress, bool IncludePlannedPurchases);
public sealed record DepreciationForecastRowDto(DateOnly PeriodMonth, int AssetGroupId, int? CostCenterId, decimal Amount);

public interface IDepreciationCalculator   // intern modulului, expus pentru teste și pentru ecranul m1-04
{
    IReadOnlyList<ScheduleRow> Build(AssetBookPlanInput input);
}
```

## 7. Schema SQL `fa` (partea M3)

| Tabel | Cheie primară | Chei unice / străine | Indecși |
| --- | --- | --- | --- |
| `fa.DepreciationSchedule` | `Id bigint` | UX `(AssetBookId, PeriodMonth) WHERE IsCurrent = 1`; FK `AssetBookId` | `(PeriodMonth, IsCurrent) INCLUDE (Amount)` |
| `fa.DepreciationRun` | `Id bigint` | UX `(CompanyId, PeriodMonth) WHERE Status <> 9`; UX `(CompanyId, NumberKey)` | — |
| `fa.DepreciationRunLine` | `Id bigint` | FK `DepreciationRunId`, `AssetId`, `BookCode`, `AssetGroupId`, `CostCenterId`; `CHECK (LineKind IN ('REGULAR','CATCH_UP','SURPLUS_TRANSFER'))` | `(DepreciationRunId, BookCode, AssetGroupId, CostCenterId)` |
| `fa.DepreciationRunException` | `Id bigint` | FK `DepreciationRunId`, `AssetId` | — |
| `fa.AssetUsageReading` | `Id bigint` | UX `(AssetId, PeriodMonth)` | — |

Procedurile: `fa.usp_DepreciationRun_Generate` (selecție set-based, fără cursoare, `SP-11`; planurile sunt deja calculate), `fa.usp_DepreciationRun_Validate`, `fa.usp_DepreciationRun_Reverse`, `fa.usp_DepreciationSchedule_Replace` (scrie o versiune nouă a planului primită din C#, ca TVP, `SP-09`), `fa.usp_AssetUsageReading_Save`. Volumul: 10.000 de fișe × 2 regimuri × 12 luni = 240.000 de rânduri de plan pe an; documentul lunar are 20.000 de linii, generate și validate sub 10 secunde (ținta NFR din doc 01).

**Erorile M3** (`65250–65399`): `65251` `FA_PREVIOUS_MONTH_OPEN` (luna anterioară nevalidată) · `65252` `FA_COST_CENTER_MISSING` · `65253` `FA_RUN_REVERSE_BLOCKED` (lună închisă sau lună următoare validată) · `65254` `FA_UNITS_EXCEED_TOTAL` · `65255` `FA_RUN_EXISTS` (al doilea document pe aceeași lună) · `65256` `FA_RUN_PERIOD_CLOSED`.

## 8. Permisiuni și roluri

`FixedAssets.Depreciation.View`, `.Generate` (ciorna și simularea), `.Validate`, `.Reverse`, `FixedAssets.UsageReading.Enter` (și Administratorul de patrimoniu sau șeful de secție), `FixedAssets.Forecast.View`. Contabilul mijloace fixe le are pe toate; Contabilul șef are `Reverse` și vede contabil vs fiscal.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M3 |
| --- | --- |
| Codul fiscal art. 28 | Metodele fiscale, coeficienții degresivi, acceleratul de cel mult 50 %, începutul din luna următoare PIF-ului, amortizarea nedeductibilă peste plafon și pe partea de utilizare personală (autoturisme) |
| OMFP 1802 | Amortizarea pe durata de utilizare economică, revizuirea duratei și a metodei ca schimbare de estimare (prospectiv), transferul surplusului din reevaluare în rezultatul reportat pe măsura amortizării |
| D101 (C4) | Amortizarea contabilă ca cheltuială nedeductibilă și cea fiscală ca deducere, din `IFiscalDepreciationQuery` |
| SAF-T D406 | Amortizarea anuală pe activ (`AssetTransactions` de tip amortizare), din jurnal |

**Precizări aplicate (27.11.2026):** C3 (verificarea `DEPRECIATION` folosește `IDepreciationStatusQuery`); C4 (D101 citește `IFiscalDepreciationQuery`); F5 (proiecția ca informație necash); C2 (modelul `AmortizareCalculata` cu cheile `DepreciationExpense` / `AccumulatedDepreciation` și transferul `RevaluationReserve` → `RealizedRevaluationSurplus`).

## 10. Rapoarte

| Raport | Conținut |
| --- | --- |
| Fișa de amortizare (pe bun) | Planul și realizatul pe regimuri, lună cu lună, tipăribilă |
| Amortizarea lunii | Pe grupă, cont și centru de cost; comparația cu luna anterioară; fișele noi, terminate, suspendate |
| Contabil vs fiscal | Pe an, grupă și fișă: contabil, fiscal, nedeductibil, diferență (pentru D101) |
| Proiecția amortizării | 1–5 ani, pe lună, grupă și centru de cost; cu și fără investițiile în curs |
| Fișe amortizate complet încă în folosință | Pentru decizia de casare sau reevaluare |

## 11. Ecrane principale

Machetele sunt în [ecrane/m3-amortizare/](ecrane/m3-amortizare/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Amortizarea lunii | Documentul `AML 2026-11`: totalurile pe regim, defalcarea pe grupă și cont, excepțiile, nota care va rezulta, validarea | `m3-01-amortizarea-lunii` |
| Plan de amortizare | Tab-ul fișei stivuitorului MF 000412: lunile contabil vs fiscal, trecerea pe liniar, versiunile planului | `m3-02-plan-amortizare` |
| Contabil vs fiscal | Anul 2026 pe grupe: contabil, fiscal, nedeductibil, diferența pentru D101; detaliul autoturismelor cu plafon | `m3-03-contabil-vs-fiscal` |
| Citiri utilaje | Citirile de ore pe noiembrie pentru fișele cu profil pe unități, cu lipsurile | `m3-04-citiri-utilaje` |
| Proiecția amortizării | 2027–2031, pe grupe, cu investițiile în curs, exportabilă pentru buget | `m3-05-proiectie` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| M3-A1 | Calculatorul dă exemplele din M1 §3.6 la ban (liniar, degresiv cu trecerea în anul 5, accelerat) și suma lunilor e exact valoarea amortizabilă | Test unitar (proprietăți: pentru 10.000 de combinații aleatoare, suma = valoarea) |
| M3-A2 | PIF 28.03.2026 → prima cotă în aprilie 2026; ieșire pe 01.09 → septembrie inclus | Test unitar |
| M3-A3 | Luna decembrie nu se poate valida înainte de noiembrie (`65251`); al doilea `AML` pe noiembrie e refuzat (`65255`) | Test de integrare |
| M3-A4 | `AML 2026-11` validat scrie în jurnal suma liniilor pe fiecare regim și publică `Accounting` doar pentru `BOOK`; nota pe grupe e cea din 4.1 | Test de integrare (cu C2) |
| M3-A5 | Schimbarea duratei la luna 20 (96 → 120) dă 779,00 / lună pe restul, fără a schimba lunile trecute | Test unitar și de integrare |
| M3-A6 | Fișa în conservare are 0 pe `TAX` și cota normală pe `BOOK` cu setarea implicită | Test de integrare |
| M3-A7 | PIF înregistrat în decembrie cu data din septembrie produce în `AML 2026-12` linia de recuperare pentru octombrie–noiembrie, cu motiv | Test de integrare |
| M3-A8 | C3 vede `DEPRECIATION` neîndeplinit cât `AML` e ciornă și îndeplinit după validare | Test de integrare (cu C3) |
| M3-A9 | Generarea și validarea pentru 20.000 de linii durează sub 10 secunde pe datele de test de volum | Test de performanță |
| M3-A10 | Stornarea unui `AML` dintr-o lună închisă e refuzată (`65253`) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Plan precalculat sau calcul la cerere | Plan precalculat pe lună și regim, cu versiuni; calculul lunar doar citește planul | ✓ Aprobat autonom (27.11.2026) |
| 2 | Un document pe lună | Da, pe firmă și lună, cu toate regimurile; generat ca ciornă de job, validat de contabil | ✓ Aprobat autonom |
| 3 | Editarea cotelor | Nu; se corectează cauza prin M4 și se regenerează (excepție: citirile pe unități) | ✓ Aprobat autonom |
| 4 | Centrul de cost în luna transferului | Toată luna pe centrul de cost de la sfârșitul lunii | ✓ Aprobat autonom |
| 5 | Lunile închise | Nu se refac; corecțiile intră ca recuperare în prima lună deschisă, cu motiv | ✓ Aprobat autonom |
| 6 | Schimbările de valoare și durată | Prospective, pe valoarea rămasă și lunile rămase | ✓ Aprobat autonom |
| 7 | Detaliul notei | Implicit pe grupă și centru de cost; opțional pe bun | ✓ Aprobat autonom |
| 8 | Transferul surplusului din reevaluare | În documentul lunar, ca linie separată, când setarea e „Lunar” | ✓ Aprobat autonom |
| 9 | Contabil vs fiscal | Vedere pe fișă, lună și an, cu partea nedeductibilă; contract pentru D101 | ✓ Aprobat autonom |
| 10 | Proiecția | 1–5 ani, din planuri, cu opțiunea investițiilor în curs și a achizițiilor planificate | ✓ Aprobat autonom |

# ERP · 02.04 Achiziții · A7 Rapoarte și trasabilitate: performanța și evaluarea furnizorilor, drumul documentelor, analiza achizițiilor, tabloul de bord

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026), cu ecranele `a7-01`–`a7-06`.** Ultima din cele 7 părți ale modulului 02.04 Achiziții. Se sprijină pe [A1 Configurare](A1-CONFIGURARE.md), [A2 Necesar și oferte](A2-NECESAR-SI-OFERTE.md) (validate pe 26.11.2026), [A3 Comenzi](A3-COMENZI.md), [A4 Recepție](A4-RECEPTIE.md), [A5 Facturi](A5-FACTURI.md) și [A6 Import și costuri](A6-IMPORT-COSTURI.md) (validate pe 27.11.2026).

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`; partea A7 folosește `53800–53899`. Această parte adună ce se vede **peste** documentele de achiziție:
- catalogul tuturor rapoartelor de achiziții, pe motorul comun de rapoarte;
- **performanța furnizorilor**: livrare la timp și completă, calitate, preț, facturare, reactivitate, cu un scor lunar;
- **evaluarea periodică a furnizorilor** (documentul `EVF`), cu decizia de aprobare, aprobare condiționată sau blocare;
- **drumul documentului**: de la referat până la plată, pe document și pe linie, în ambele sensuri;
- **jurnalul de excepții**: abaterile aprobate (prețuri, toleranțe, diferențe, deblocări), pentru control și audit;
- **analiza achizițiilor**: cheltuiala pe furnizori și articole, clasele ABC, istoricul prețurilor, dependența de un singur furnizor;
- **tabloul de bord** al șefului de achiziții.

Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (legăturile între documente §3.6, audit), [02.05 Stocuri](../05-stocuri/README.md) (S4 trasabilitatea loturilor și retragerea, S7 §3.1 motorul de rapoarte), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F3 plăți). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Documentele de achiziție** și tranzacțiile lor (A1 §3.2):

  | Cod | Document | Parte |
  | --- | --- | --- |
  | `RF` | Referat | A2 |
  | `NC` | Necesar de aprovizionare | A2 |
  | `CO` | Cerere de ofertă | A2 |
  | `OF` | Ofertă de la furnizor | A2 |
  | `CF` | Comandă la furnizor | A3 |
  | `AV` | Aviz de la furnizor | A4 |
  | `NIR` | Recepție | A4 |
  | `PVR` | PV de diferențe la recepție | A4 |
  | `RT` | Retur la furnizor | A4 |
  | `FF` | Factură de furnizor | A5 |
  | `FS` | Notă de credit sau storno | A5 |
  | `DVI` | Declarație vamală de import | A6 |
  | `CA` | Repartizarea costurilor de achiziție | A6 / Stocuri S3 |

  Documentele sunt legate prin `core.DocumentLink` (Nucleu §3.6), pe grupuri de consum: `ORDER`, `DELIVER`, `RECEIVE`, `INVOICE`, `RETURN`, `CREDIT`, `CUSTOMS`, `LANDED`.
- **Motorul de rapoarte** e cel descris în Stocuri S7 §3.1: filtre, desfășurare din total până la document, export Excel / PDF / CSV, vizualizări salvate, trimitere programată pe e-mail prin joburile Nucleului. A7 îl folosește și îl mută în Nucleu, ca să fie comun tuturor modulelor (decizia 1).
- **Trasabilitatea loturilor** (înapoi la furnizor, înainte la clienți) și **retragerea** (`RL`) sunt în Stocuri S4 §3.6. A7 nu le dublează: din drumul documentului se deschide trasabilitatea lotului, iar retragerile intră în scorul de calitate al furnizorului.
- Valorile se țin pe `decimal(19,4)`, procentele și scorurile pe `decimal(9,4)` (`SQL-07`). Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`).
- **Ce NU face A7:**
  - nu construiește depozitul de date și tablourile de bord între module (Raportare și BI, 02.13; A7 îi publică vederile);
  - nu face bugetarea achizițiilor (Controlling, prin Raportare);
  - nu trimite automat scorul furnizorului (îl trimite utilizatorul, din evaluare).

## 1. Scop și perimetru

| Zonă | Ce face A7 | Ce NU face (și cine face) |
| --- | --- | --- |
| Rapoarte | Catalogul rapoartelor de achiziții (cele din A1–A6 și cele noi), pe motorul comun, cu filtrele de achiziții și vizibilitatea pe achizitor | Motorul însuși (Nucleu, după mutarea din S7) |
| Performanța furnizorilor | Indicatorii lunari pe furnizor, calculați din comenzi, recepții, facturi și cereri de ofertă; scorul și clasa; propunerea de termen de livrare real | Calculul livrărilor proprii către clienți (Vânzări) |
| Evaluarea furnizorilor | Campania anuală, documentul `EVF` cu indicatorii și criteriile manuale, decizia, planul de acțiuni, blocarea furnizorului | Auditul la furnizor (în afara ERP-ului; rezultatul se atașează) |
| Trasabilitate | Drumul documentului și al liniei, în ambele sensuri, cu cantitățile pe fiecare legătură și plata | Trasabilitatea loturilor și retragerea (Stocuri S4) |
| Control | Jurnalul de excepții aprobate, din toate părțile A1–A6 | Jurnalul de audit tehnic (Nucleu) |
| Analiză | Cheltuiala pe furnizori, articole și centre de cost; ABC; istoricul prețurilor; dependența de furnizor; termenul mediu de plată | Analizele între module și bugetul (Raportare) |
| Tablou de bord | Indicatorii și alertele șefului de achiziții | Tablourile directorilor (Raportare) |

## 2. Model de domeniu

```
 Documentele A2–A6 ── core.DocumentLink (grupuri de consum) ──► Drumul documentului (citire, recursiv)
        │
        ├─► purch.SupplierPerformanceMonth (instantaneu lunar pe firmă × furnizor)
        │        indicatori: livrare, completitudine, calitate, preț, facturare, reactivitate, termen real
        │        └─► scor 0–100 și clasă A / B / C
        │
        ├─► purch.SupplierEvaluationCampaign ──< purch.SupplierEvaluation (EVF)
        │                                            ├─ indicatorii pe 12 luni (copiați la creare)
        │                                            ├─ SupplierEvaluationCriterion × N (criterii manuale)
        │                                            ├─ decizia: aprobat · aprobat condiționat · neaprobat
        │                                            └─ SupplierEvaluationAction × N (planul de acțiuni)
        │
        ├─► purch.ExceptionLog (excepțiile aprobate, scrise de A1–A6)
        │
        └─► purch.vw_PurchaseFact (vederea de analiză, la nivel de linie) ──► Raportare (02.13)
```

Reguli de business ale modelului:

1. **Indicatorii se calculează din documente, nu se introduc.** Singurele valori introduse de om sunt criteriile manuale ale evaluării și decizia.
2. **Luna închisă nu se mai schimbă.** Instantaneul lunar se recalculează în fiecare noapte pentru luna curentă; la închiderea lunii (C3) se îngheață. Un document întârziat (o factură sosită după închidere) intră în prima lună deschisă, ca în Stocuri și Finanțe.
3. **Un scor fără destule date nu se afișează ca scor.** Sub pragul minim de livrări în perioadă (implicit 5), furnizorul apare cu „date insuficiente”, nu cu un scor care ar putea induce în eroare.
4. **Drumul documentului e o citire**, nu o copie: se reconstruiește oricând din `core.DocumentLink`, deci nu poate rămâne în urmă față de documente.
5. **O excepție aprobată se scrie o singură dată**, în tranzacția documentului care o aprobă, și nu se mai modifică (doar adăugare).

## 3. Entități, stări și setări

Convențiile comune tabelelor `purch` sunt cele din A1 §3. Serie nouă: `EVF` (evaluare de furnizor).

### 3.1 Catalogul rapoartelor de achiziții

Toate rapoartele modulului folosesc motorul comun (Stocuri S7 §3.1, mutat în Nucleu, decizia 1).

**Filtrele comune de achiziții:**
- firmă (și grup), furnizor sau grup de furnizori, achizitor;
- gestiune, articol, familie, grupă de articole;
- centru de cost, proiect, monedă, tranzacție;
- perioadă sau dată.

**Desfășurarea:** total → furnizor → document → linie. Din fiecare rând se deschide documentul.

**Vizibilitatea pe achizitor** (setarea `BuyerDataScope`):
- `0` (implicit): toți achizitorii văd toate achizițiile firmei;
- `1`: un achizitor vede doar furnizorii și grupele de articole care îi sunt atribuite (`BuyerAssignment`, A1 §3.7).

Șeful de achiziții, contabilii și auditorul văd tot. Valorile pot fi ascunse rolurilor fără dreptul `Purch.Report.ViewValue` (de exemplu gestionarul vede cantitățile, nu și prețurile).

Catalogul complet e în secțiunea 10. Rapoartele părților A1–A6 rămân definite acolo unde sunt descrise; A7 le adună și adaugă rapoartele noi.

### 3.2 Performanța furnizorilor (`purch.SupplierPerformanceMonth`)

Un rând pe firmă, furnizor și lună. Indicatorii și sursa lor:

| Indicator | Calcul | Sursa |
| --- | --- | --- |
| **Livrări la timp (OTD)** | Livrările programate recepționate între data așteptată − `EarlyDays` și data așteptată + `LateDays` / livrările scadente în lună | A3 `PurchaseOrderSchedule.ExpectedDate` (data confirmată, altfel cea cerută), A4 data NIR-ului |
| **Livrări complete (IF)** | Livrările la care cantitatea acceptată ≥ cantitatea confirmată × (1 − `CompleteTolerancePct`) / livrările scadente | A3 cantitatea confirmată, A4 `AcceptedQty` |
| **OTIF** | Livrările care sunt și la timp, și complete / livrările scadente | Cele două de mai sus |
| **Calitate** | Cantitatea refuzată, lipsă sau deteriorată / cantitatea recepționată, în procente și PPM (părți pe milion); plus numărul de PV-uri de diferențe, loturile respinse din carantină și retragerile cu sursa „furnizorul” | A4 diferențele (tipurile 1–5) și PV-urile, Stocuri S4 carantina și retragerea |
| **Preț** | Abaterea ponderată a prețului facturat față de prețul din comandă; separat, abaterea prețului din comandă față de lista de prețuri | A5 diferențele de preț, A3 abaterile de preț |
| **Facturare** | Facturile potrivite fără diferențe / facturile cu comandă | A5 potrivirea |
| **Reactivitate** | Rata de răspuns la cererile de ofertă; timpul mediu de confirmare a comenzii, în zile | A2, A3 confirmările |
| **Termen real** | Media zilelor de la trimiterea comenzii până la recepție, ponderată cu valoarea | A3, A4 |

**Scorul** (0–100) e media ponderată a cinci note:

| Notă | Formula | Pondere implicită |
| --- | --- | --- |
| Livrare | OTIF % | 40 |
| Calitate | max(0, 100 − 10 × procentul de cantitate cu probleme) | 30 |
| Preț | max(0, 100 − 10 × abaterea medie pozitivă a prețului facturat, în %) | 15 |
| Facturare | procentul facturilor potrivite fără diferențe | 10 |
| Reactivitate | rata de răspuns la cererile de ofertă | 5 |

- **Ponderile** se schimbă în setări (3.6); suma lor trebuie să fie 100 (`53803`).
- **Când un furnizor nu are date** pentru o notă (de exemplu nu a primit cereri de ofertă), ponderea ei se împarte proporțional pe celelalte.
- **Clasa:** **A** de la 85 în sus, **B** între 70 și 85, **C** sub 70. Pragurile se pot schimba din setări.
- **Perioadele:** scorul afișat pe ecrane e cel pe ultimele 12 luni (rulant), calculat din instantaneele lunare, ponderat cu numărul de livrări. Se poate cere și pe orice altă perioadă.

**Termenul real față de cel declarat.** Dacă termenul real diferă de `SupplierTerms.LeadTimeDays` cu mai mult de `LeadTimeProposalDays` (implicit 1 zi) pe ultimele 6 luni, apare o propunere de actualizare a termenului. Propunerea e vizibilă pe fișa de performanță și în tabloul de bord. Achizitorul o acceptă sau o respinge; termenul nu se schimbă automat, pentru că necesarul (A2) și MRP-ul se bazează pe el.

**Unde se folosește scorul:**
- în compararea ofertelor (A2 §4.5): scorul și clasa furnizorului apar lângă fiecare ofertă;
- pe fișa furnizorului și în alegerea furnizorului pe comandă (A3), ca informație;
- în evaluarea periodică (3.3).

### 3.3 Evaluarea furnizorilor (`purch.SupplierEvaluation`, seria `EVF`)

Evaluarea periodică a furnizorilor externi e cerută de sistemele de management al calității (ISO 9001 §8.4) și e o bună practică și fără certificare.

**Campania** (`SupplierEvaluationCampaign`):
- se creează o dată pe an (sau la frecvența din setări), pentru furnizorii activi cu achiziții de peste `EvaluationMinSpendRon` (implicit 10.000 lei) în ultimele 12 luni, plus furnizorii marcați „critici”;
- campania creează câte o evaluare în ciornă pe furnizor, asignată achizitorului furnizorului.

**Evaluarea** (documentul `EVF`):

| Câmp | Regulă |
| --- | --- |
| `Number`, `PartnerId`, `PeriodFrom`, `PeriodTo` | Implicit ultimele 12 luni încheiate |
| Indicatorii | Copiați la creare din instantaneele lunare: OTD, IF, OTIF, calitate (procent, PPM, PV-uri), preț, facturare, reactivitate, termen real, cheltuiala; scorul calculat și clasa. Se pot reîmprospăta cât timp evaluarea e în ciornă |
| Criteriile manuale (`SupplierEvaluationCriterion`) | Lista din setări, fiecare notat 1–5, cu observație. Livrate: certificări valabile (ISO 9001, siguranța alimentară etc.), comunicare și suport, condiții comerciale, flexibilitate, documente de conformitate. Data de expirare a certificatelor se ține pe criteriu și dă alertă cu 30 de zile înainte |
| `ManualScore` | Media criteriilor, adusă la 0–100 |
| `FinalScore` | Scorul calculat × `AutoWeightPct` (implicit 80) + `ManualScore` × (100 − `AutoWeightPct`) |
| `Decision` | 1 Aprobat · 2 Aprobat condiționat · 3 Neaprobat |
| Planul de acțiuni (`SupplierEvaluationAction`) | Obligatoriu la decizia 2 (`53805`): acțiunea, responsabilul (la noi sau la furnizor), termenul, starea |
| `NextEvaluationDate` | Implicit peste 12 luni; la decizia 2, peste 6 luni |
| `Status` | 1 Ciornă · 2 Trimisă la aprobare · 3 Validată · 9 Anulată |

**Decizia propusă** din `FinalScore`: clasa A → Aprobat, B → Aprobat condiționat, C → Neaprobat. Achizitorul poate propune altă decizie, cu motiv (`53804`).

**Aprobarea** o dă șeful de achiziții (`Purch.SupplierEvaluation.Approve`). La validare:
- **Neaprobat:** furnizorul se blochează în firmă (`SupplierTerms.IsBlocked = 1`, A1), cu motivul „Evaluare EVF …”. Comenzile deja trimise rămân valabile; comenzile noi sunt refuzate (A1 `53010`). Deblocarea se face doar din fișa furnizorului, de șeful de achiziții, cu motiv, și intră în jurnalul de excepții.
- **Aprobat condiționat:** furnizorul rămâne activ. Acțiunile cu termen depășit apar în tabloul de bord.
- Evaluarea validată se poate trimite furnizorului ca PDF (fișa de performanță și decizia), la cererea utilizatorului.

### 3.4 Drumul documentului

**Pe document** (tab-ul „Legături” de pe orice document de achiziție și ecranul „Drumul documentului”):
- pornește de la orice document și urcă la sursele lui și coboară la documentele generate din el, recursiv, până la referat în sus și până la plată în jos;
- fiecare nod arată:
  - documentul și starea lui;
  - data și valoarea;
  - utilizatorul care l-a validat;
  - legătura (grupul de consum și cantitatea preluată);
- nodurile din afara Achizițiilor apar și ele: plata și compensarea (Finanțe F3), mișcările de stoc și loturile (Stocuri), retragerea (S4), corecția de cost (S3);
- ramurile care așteaptă ceva sunt marcate: comandă nerecepționată, NIR nefacturat, diferență nedecisă, notă de credit așteptată, factură blocată la plată.

**Pe linie** („Drumul liniei”): pentru o linie de comandă, o tabelă cu cantitatea:
- comandată, confirmată;
- recepționată, acceptată, refuzată;
- returnată;
- facturată, creditată;
- plătită (valoric).

Fiecare cantitate deschide documentele care o compun. Așa se vede imediat unde s-a pierdut o diferență.

**Pe lot:** din orice NIR sau linie de NIR se deschide trasabilitatea lotului din Stocuri S4 (`s4-05`), cu clienții care l-au primit și stocul rămas.

Implementare: procedura `purch.usp_Document_Trace` urcă și coboară pe `core.DocumentLink` cu o interogare recursivă limitată la adâncimea 12, pe indecșii `DocumentLink (SourceDocType, SourceDocId)` și `(TargetDocType, TargetDocId)`. Nodurile din alte module se citesc prin interfețele lor publice (F3 `IPaymentAllocationQuery`, Stocuri `IStockMovementQuery`), nu din tabelele lor.

### 3.5 Jurnalul de excepții (`purch.ExceptionLog`)

O excepție e o abatere de la regula normală, aprobată de cineva cu drept. Fiecare parte o scrie în tranzacția documentului, prin `IPurchaseExceptionWriter.WriteAsync`:

| Excepție | Scrisă de | Exemplu |
| --- | --- | --- |
| Preț pe comandă peste listă sau manual | A3 | `CF 2297`: +3,2% față de lista PackPro, aprobat de Dan Toma |
| Comandă revizuită după confirmare | A3 | `CF 2291` revizia 2, reaprobată |
| Recepție peste comandă, în toleranță | A4 | `NIR 4487`: +12 buc APA05 în toleranța de 2% |
| Diferență la recepție decisă (refuz, acceptare, reclamație) | A4 | `NIR 4490`: 4 role FOLIE23 refuzate, `PVR 0017` |
| Diferență de potrivire acceptată | A5 | `FF 0911`: +0,05 lei/buc la BANDA48, 47,40 lei acceptați |
| Factură fără comandă peste prag, aprobată | A5 | `VE 2611-0457`: 6.180,00 lei, aprobat de Gheorghe Ilie |
| Taxare inversă păstrată cu cod de TVA invalid în VIES | A6 | — |
| Taxă din DVI diferită de propunere | A6 | — |
| Furnizor deblocat | A1, A7 | Ambalaje Vest SRL, deblocat de Dan Toma, cu motiv |

Câmpuri: `CompanyId`, `OccurredAt`, `ExceptionCode`, `DocType`, `DocId`, `DocLineId`, `PartnerId`, `AmountRon`, `Details` (JSON scurt, de exemplu prețul de listă și prețul aprobat), `RequestedBy`, `ApprovedBy`, `Reason`. Doar adăugare (`SP-18`); nicio procedură nu face `UPDATE` sau `DELETE` pe tabel.

### 3.6 Setările A7 (completare la `PurchaseSettings`)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `OtdEarlyDays`, `OtdLateDays` | 2, 0 | Fereastra în care o livrare e „la timp” față de data așteptată |
| `CompleteTolerancePct` | 2 | Toleranța sub care o livrare e încă „completă” |
| `ScoreWeightDelivery`, `…Quality`, `…Price`, `…Invoicing`, `…Responsiveness` | 40, 30, 15, 10, 5 | Ponderile notelor; suma = 100 |
| `ClassAMin`, `ClassBMin` | 85, 70 | Pragurile claselor |
| `MinDeliveriesForScore` | 5 | Sub acest număr de livrări în perioadă: „date insuficiente” |
| `LeadTimeProposalDays` | 1 | Diferența de la care se propune actualizarea termenului de livrare |
| `EvaluationFrequencyMonths`, `EvaluationMinSpendRon`, `AutoWeightPct` | 12, 10.000, 80 | Campania de evaluare și ponderea scorului calculat în evaluare |
| `BuyerDataScope` | 0 | Vizibilitatea achizitorilor în rapoarte (3.1) |
| `SingleSourceSharePct` | 80 | Pragul de la care un articol e „dependent de un singur furnizor” (3.7) |

### 3.7 Analiza achizițiilor (`purch.vw_PurchaseFact`)

O vedere la nivel de linie, cu un rând pe linie de document și tip de fapt:
- **tipuri de fapt:** comandat, recepționat, facturat, returnat, creditat;
- **dimensiuni:** data, firma, furnizorul, achizitorul, varianta, familia și grupa de articole, gestiunea, centrul de cost, proiectul, moneda, tranzacția;
- **valori:** cantitatea în UM de bază, valoarea în lei (fără TVA) și, la recepții, costul complet (A6).

Pe ea se construiesc rapoartele de analiză:

| Analiză | Ce arată |
| --- | --- |
| **Cheltuiala pe furnizori (ABC)** | Valoarea facturată pe furnizor, cu procentul cumulat. Clasa: A până la 80%, B până la 95%, C restul. Lângă ea, scorul și OTIF-ul |
| **Cheltuiala pe articole și grupe** | Aceeași analiză pe articole sau grupe, cu numărul de furnizori pe articol |
| **Istoricul prețurilor** | Pe variantă: prețul facturat și costul complet pe furnizori, în timp (grafic și tabel), cu prețul din lista curentă și ultima ofertă |
| **Dependența de furnizor** | Articolele la care un singur furnizor are peste `SingleSourceSharePct` din cheltuială pe 12 luni, cu valoarea și alternativele din catalog (A1) |
| **Comparație cu anul trecut** | Oricare din analizele de mai sus, perioada curentă față de aceeași perioadă din anul anterior |
| **Termenul mediu de plată** | Termenul contractual și cel real (de la factură la plată, din F3), ponderate cu valoarea, pe furnizor |

`purch.vw_PurchaseFact` e și **vederea publicată pentru Raportare (02.13)**, alături de `purch.vw_SupplierPerformanceMonth`. Raportarea le citește doar prin aceste vederi (contract stabil, versionat).

### 3.8 Tabloul de bord al achizițiilor

Ecran pentru șeful de achiziții și achizitori (fiecare vede datele permise de `BuyerDataScope`).

**Indicatorii**, fiecare cu desfășurare spre raportul lui:
- valoarea comandată în luna curentă;
- OTIF pe ultimele 3 luni;
- livrările întârziate;
- facturile blocate la plată;
- recepțiile nefacturate (soldul `408`);
- importurile deschise;
- economiile din atribuiri în anul curent.

**Alertele:**
- comenzi neconfirmate de peste 3 zile;
- livrări întârziate;
- liste de prețuri care expiră;
- certificate de furnizor care expiră;
- evaluări scadente;
- acțiuni din planurile de evaluare cu termen depășit;
- propuneri de termen de livrare;
- furnizori din clasa C cu comenzi deschise.

**Clasamentul furnizorilor:** furnizorii cu cea mai mare cheltuială, cu scorul și tendința față de luna anterioară.

## 4. Fluxuri principale

### 4.1 Calculul lunar al performanței

1. **Jobul de noapte** `purch.usp_SupplierPerformance_Build` recalculează luna curentă (și luna anterioară, cât timp nu e închisă) pentru toți furnizorii cu documente în acea lună. Calculul e pe seturi, o singură trecere pe livrări, NIR-uri, facturi și cereri.
2. La **închiderea lunii** (evenimentul `LunaInchisa` din C3), rândurile lunii primesc `IsFrozen = 1` și nu se mai recalculează (`53801` la o recalculare forțată).
3. Un **document întârziat** (de exemplu un NIR validat după închidere, pentru o livrare scadentă în luna închisă) intră în prima lună deschisă, ca livrare târzie.
4. După calcul se publică `PerformantaFurnizorCalculata`. Ecranele citesc doar instantaneele, nu recalculează la deschidere.

Exemplu (ecranul `a7-02`): Furnizor Ambalaje SRL, ultimele 12 luni (decembrie 2025 – noiembrie 2026).

| Indicator | Valoare | Notă |
| --- | --- | --- |
| Livrări scadente | 46 | — |
| La timp (OTD) | 36 → 78,3% | — |
| Complete (IF) | 41 → 89,1% | — |
| OTIF | 30 → 65,2% | Livrare 65,2 |
| Calitate | 1.284 unități cu probleme din 182.400 recepționate, adică 0,70% (PPM 7.039), cu 6 PV-uri (printre ele `PVR 0017`) | 93,0 |
| Preț | Facturat față de comandă, abatere medie +0,6% | 94,0 |
| Facturare | 47 din 52 de facturi potrivite fără diferențe, adică 90,4% | 90,4 |
| Reactivitate | 8 răspunsuri la 9 cereri de ofertă, adică 88,9%; confirmare medie în 1,4 zile | 88,9 |

- **Scorul:** 0,40 × 65,2 + 0,30 × 93,0 + 0,15 × 94,0 + 0,10 × 90,4 + 0,05 × 88,9 = **81,6**, clasa **B**.
- **Termenul de livrare:** termenul real e 6,2 zile, față de 5 declarate, deci ERP-ul propune actualizarea la 6 zile.

### 4.2 Campania de evaluare

1. Șeful de achiziții creează campania pentru anul 2026 („Evaluare furnizori 2026”). Campania propune furnizorii eligibili, cu cheltuiala și ultima evaluare; poate scoate sau adăuga furnizori.
2. La lansare se creează evaluările în ciornă, fiecare la achizitorul furnizorului, cu indicatorii copiați.
3. Achizitorul notează criteriile manuale și propune decizia. Propunerea implicită vine din clasă; dacă e alta, cere motiv. La decizia „Aprobat condiționat” completează planul de acțiuni.
4. Trimite evaluarea la aprobare. Șeful de achiziții aprobă sau o returnează cu observații.
5. La validare se aplică decizia (3.3), se publică `EvaluareFurnizorValidata`, iar `NextEvaluationDate` intră în alertele tabloului de bord.

Exemplu (ecranul `a7-03`): `EVF 0012`, Furnizor Ambalaje SRL, perioada 12.2025–11.2026.
- **Scorul calculat:** 81,6.
- **Criteriile manuale:** 5 criterii, media 3,8 din 5, adică 70,0 din 100:
  - ISO 9001 valabil până la 14.03.2027: 5;
  - comunicare: 4;
  - condiții comerciale: 4;
  - flexibilitate: 3;
  - documente de conformitate: 3.
- **Scorul final:** 0,80 × 81,6 + 0,20 × 70,0 = **79,3**, clasa B, decizia propusă **Aprobat condiționat**.
- **Planul de acțiuni:**
  - furnizorul confirmă datele de livrare în 24 de ore și anunță întârzierile, termen 31.01.2027, responsabil la furnizor;
  - Laura Vasile verifică lunar OTIF-ul, termen 31.05.2027.
- **Următoarea evaluare:** 31.05.2027.

### 4.3 Urmărirea unui document

Exemplu (ecranul `a7-04`): Laura Vasile deschide drumul comenzii `CF 2289` către Furnizor Ambalaje SRL:

| Document | Ce arată nodul |
| --- | --- |
| `CF 2289` | 18.11.2026: FOLIE23 200 role la 38,50 lei și BANDA48 960 buc la 4,10 lei, adică 11.636,00 lei fără TVA |
| `AV FA 3107` | Avizul furnizorului, grupul `DELIVER` |
| `NIR 4490` | Recepție oarbă: 196 de role FOLIE23 acceptate și 4 refuzate; 948 buc BANDA48 acceptate, 12 lipsă; 11.432,80 lei; grupul `RECEIVE` |
| `PVR 0017` | PV-ul de diferențe |
| `FF 0911` | Factura FA 9921: 11.634,20 lei fără TVA, blocată la plată; grupul `INVOICE` |
| `CA` | Diferența de preț acceptată, 47,40 lei, pe `371` |
| Nota de credit așteptată | 4 role FOLIE23 facturate și refuzate: 154,00 lei + TVA |
| Plata | Neînceput; plata e blocată până la decizia pe ultima diferență |

Drumul liniei FOLIE23:
- comandat 200, confirmat 200;
- recepționat 200, acceptat 196, refuzat 4;
- facturat 200, creditat 0 (4 așteptate);
- plătit 0.

### 4.4 Excepțiile și controlul

- Contabilul șef sau auditorul deschide jurnalul de excepții pe o perioadă, filtrat pe tip, aprobator sau furnizor.
- Fiecare excepție are legătura spre document și spre drumul lui.
- **Raportul „Excepții pe aprobator”** arată, pentru fiecare aprobator, câte excepții și ce valoare a aprobat. Este controlul obișnuit de separare a atribuțiilor.
- **Raportul „Excepții repetate”** arată furnizorii și articolele cu excepții frecvente, de exemplu prețuri peste listă în fiecare lună, care cer renegocierea listei.

### 4.5 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Livrare fără dată confirmată | Data așteptată e data cerută (A3) |
| Livrare primită în mai multe NIR-uri | E la timp dacă cantitatea completă a fost recepționată în fereastră; altfel data livrării e data NIR-ului care o completează |
| Comandă anulată sau livrare închisă fără recepție, la cererea noastră | Livrarea nu intră în indicatori |
| Livrare închisă fără recepție, din vina furnizorului | Intră în indicatori ca livrare nelivrată (nici la timp, nici completă) |
| Furnizor nou, cu 2 livrări | „Date insuficiente”; evaluarea se poate face doar cu criteriile manuale, marcat ca atare |
| Furnizor din altă firmă a grupului | Scorul e pe firmă; raportul de grup le adună, ponderat cu livrările |
| Evaluare cu decizia „Neaprobat” pentru un furnizor cu comenzi deschise | Comenzile trimise rămân; tabloul de bord le arată pe toate, ca să se decidă pe fiecare |
| Recalcularea unei luni închise | Refuzată (`53801`); o corecție de date intră în luna curentă |
| Document șters (anulat) din drum | Apare tăiat, cu starea „anulat”, ca să nu dispară din istoric |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `PerformantaFurnizorCalculata` v1 | După calculul de noapte | `CompanyId`, `Month`, lista de `PartnerId` cu scorul și clasa | A2 (comparare oferte), Raportare |
| `EvaluareFurnizorValidata` v1 | Validarea unei evaluări | `EvaluationId`, `PartnerId`, `FinalScore`, `Class`, `Decision`, `NextEvaluationDate` | A1 (blocare), Raportare, Calitate (viitor) |
| `PropunereTermenLivrare` v1 | Termen real diferit de cel declarat | `PartnerId`, `ItemVariantId` (opțional), `DeclaredDays`, `ActualDays` | Notificări (achizitorul) |

### 5.2 Consumate

| Eveniment | De la | Ce face A7 |
| --- | --- | --- |
| `LunaInchisa` v1 | Finanțe C3 | Îngheață instantaneele lunii |
| `RetragereValidata` v1 | Stocuri S4 | Notează retragerea pe furnizorul lotului, pentru nota de calitate |
| `LotStareSchimbata` v1 | Stocuri S4 | Notează lotul respins din carantină (trecerea din carantină în blocat sau la retur) |
| `FurnizorBlocat` v1 | A1 | Afișează blocarea în tabloul de bord și pe evaluare |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A7)

```csharp
public enum SupplierClass : byte { A = 1, B = 2, C = 3, InsufficientData = 9 }
public enum SupplierEvaluationDecision : byte { Approved = 1, ConditionallyApproved = 2, NotApproved = 3 }

public sealed record SupplierScoreDto(int PartnerId, DateOnly From, DateOnly To, int Deliveries,
    decimal? OtdPct, decimal? InFullPct, decimal? OtifPct, decimal? QualityIssuePct, decimal? PriceDeviationPct,
    decimal? InvoiceMatchPct, decimal? RfqResponsePct, decimal? ActualLeadTimeDays, decimal? Score, SupplierClass Class);

// Citit de A2 (compararea ofertelor), A3 (alegerea furnizorului), Raportare
public interface ISupplierPerformanceReader
{
    Task<SupplierScoreDto?> GetScoreAsync(int companyId, int partnerId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<SupplierScoreDto>> GetScoresAsync(int companyId, IReadOnlyList<int> partnerIds, CancellationToken ct); // ultimele 12 luni
}

// Scris de A1–A6, în tranzacția documentului care aprobă excepția
public interface IPurchaseExceptionWriter
{
    Task WriteAsync(PurchaseExceptionDto exception, CancellationToken ct);
}
public sealed record PurchaseExceptionDto(int CompanyId, string ExceptionCode, string DocType, long DocId, long? DocLineId,
    int? PartnerId, decimal? AmountRon, string? DetailsJson, int RequestedBy, int ApprovedBy, string Reason);

// Drumul documentului, citit de orice ecran de document
public interface IPurchaseTraceQuery
{
    Task<DocumentTraceDto> TraceAsync(string docType, long docId, CancellationToken ct);
    Task<LineTraceDto> TraceOrderLineAsync(long orderLineId, CancellationToken ct);
}
```

Comenzi interne A7 (MediatR):
- performanță: `BuildSupplierPerformance`, `AcceptLeadTimeProposal`, `RejectLeadTimeProposal`;
- evaluare: `CreateEvaluationCampaign`, `LaunchEvaluationCampaign`, `SaveSupplierEvaluation`, `RefreshEvaluationIndicators`, `SubmitSupplierEvaluation`, `ApproveSupplierEvaluation`, `ReturnSupplierEvaluation`, `SendEvaluationToSupplier`, `UpdateEvaluationAction`;
- setări: `SavePerformanceSettings`, `SaveEvaluationCriteria`.

## 7. Schema SQL `purch` (partea A7)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.SupplierPerformanceMonth` | `Id bigint` | UX `(CompanyId, PartnerId, Month)`; FK `PartnerId` | `(CompanyId, Month) INCLUDE (Score, Class, Deliveries)` |
| `purch.SupplierEvaluationCampaign` | `Id int` | UX `(CompanyId, Code)` | — |
| `purch.SupplierEvaluation` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `CampaignId`, `PartnerId` | `(CompanyId, PartnerId, PeriodTo DESC)`, `(NextEvaluationDate) WHERE Status = 3` |
| `purch.SupplierEvaluationCriterion` | `Id bigint` | UQ `(EvaluationId, CriterionId)`; FK `CriterionId` spre `purch.EvaluationCriterionDef` | `(ExpiresOn) WHERE ExpiresOn IS NOT NULL` |
| `purch.SupplierEvaluationAction` | `Id bigint` | FK `EvaluationId` | `(DueDate) WHERE Status <> 3` |
| `purch.EvaluationCriterionDef` | `Id int` | UX `(CompanyId, Code)` | — |
| `purch.ExceptionLog` | `Id bigint` | FK `PartnerId`; fără UPDATE sau DELETE | `(CompanyId, OccurredAt)`, `(DocType, DocId)`, `(ApprovedBy, OccurredAt)` |
| `purch.LeadTimeProposal` | `Id bigint` | FK `PartnerId`, `ItemVariantId` | `(CompanyId, Status)` |

Exemplu de tabel complet:

```sql
CREATE TABLE purch.SupplierPerformanceMonth (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPerformanceMonth PRIMARY KEY,
    CompanyId           int            NOT NULL,
    PartnerId           int            NOT NULL CONSTRAINT FK_SPM_Partner REFERENCES core.Partner(Id),
    Month               date           NOT NULL CONSTRAINT CK_SPM_Month CHECK (DAY(Month) = 1),
    Deliveries          int            NOT NULL,
    OnTimeDeliveries    int            NOT NULL,
    InFullDeliveries    int            NOT NULL,
    OtifDeliveries      int            NOT NULL,
    ReceivedQtyBase     decimal(19,6)  NOT NULL,
    IssueQtyBase        decimal(19,6)  NOT NULL,
    DiscrepancyReports  int            NOT NULL,
    RejectedLots        int            NOT NULL,
    Recalls             int            NOT NULL,
    InvoicedNetRon      decimal(19,4)  NOT NULL,
    PriceDeviationRon   decimal(19,4)  NOT NULL,           -- Σ (preț facturat − preț comandă) × cantitate, doar pozitiv
    InvoicesWithOrder   int            NOT NULL,
    InvoicesMatched     int            NOT NULL,
    RfqSent             int            NOT NULL,
    RfqAnswered         int            NOT NULL,
    ConfirmDaysSum      decimal(19,4)  NOT NULL,
    Confirmations       int            NOT NULL,
    LeadTimeDaysWeighted decimal(19,4) NOT NULL,           -- Σ zile × valoare, pentru media ponderată
    LeadTimeValueRon    decimal(19,4)  NOT NULL,
    Score               decimal(9,4)   NULL,               -- NULL = date insuficiente în luna asta
    Class               tinyint        NOT NULL CONSTRAINT CK_SPM_Class CHECK (Class IN (1, 2, 3, 9)),
    IsFrozen            bit            NOT NULL CONSTRAINT DF_SPM_Frozen DEFAULT (0),
    CalculatedAt        datetime2(3)   NOT NULL,
    CONSTRAINT CK_SPM_Counts CHECK (OnTimeDeliveries <= Deliveries AND InFullDeliveries <= Deliveries AND OtifDeliveries <= Deliveries
                                    AND InvoicesMatched <= InvoicesWithOrder AND RfqAnswered <= RfqSent)
);
CREATE UNIQUE INDEX UX_SPM ON purch.SupplierPerformanceMonth (CompanyId, PartnerId, Month);
CREATE INDEX IX_SPM_Month ON purch.SupplierPerformanceMonth (CompanyId, Month) INCLUDE (Score, Class, Deliveries);
```

Tabelul ține **numărătorii și numitorii**, nu doar procentele, ca scorul pe 12 luni să fie calculat corect (raport de sume, nu medie de procente).

Proceduri:
- performanță: `purch.usp_SupplierPerformance_Build` (job de noapte, pe seturi), `purch.usp_SupplierPerformance_Get` (perioadă oarecare, din instantanee);
- evaluare: `purch.usp_SupplierEvaluation_Save` (TVP pentru criterii și acțiuni, `SP-07`), `purch.usp_SupplierEvaluation_Approve`;
- trasabilitate: `purch.usp_Document_Trace`, `purch.usp_OrderLine_Trace`;
- excepții: `purch.usp_ExceptionLog_Write` (doar `INSERT`), `purch.usp_ExceptionLog_List` (`SP-11`);
- rapoarte: `purch.usp_Report_<Nume>`, toate cu paginare (`SP-11`) și cu filtrul `BuyerDataScope` aplicat în procedură, nu în aplicație.

Vederi publicate pentru Raportare: `purch.vw_PurchaseFact`, `purch.vw_SupplierPerformanceMonth`, `purch.vw_SupplierEvaluation`.

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53801 | `PURCH_PERFORMANCE_MONTH_FROZEN` | 409 | Recalcularea unei luni închise |
| 53802 | `PURCH_EVALUATION_DUPLICATE` | 409 | A doua evaluare activă pentru același furnizor și aceeași perioadă |
| 53803 | `PURCH_SCORE_WEIGHTS_INVALID` | 400 | Ponderile notelor nu dau 100 sau pragurile claselor nu sunt în ordine |
| 53804 | `PURCH_EVALUATION_DECISION_REASON` | 422 | Decizie diferită de cea propusă din clasă, fără motiv |
| 53805 | `PURCH_EVALUATION_ACTIONS_REQUIRED` | 422 | Decizia „Aprobat condiționat” fără plan de acțiuni |
| 53806 | `PURCH_EVALUATION_CRITERIA_INCOMPLETE` | 422 | Trimiterea la aprobare cu criterii obligatorii nenotate |
| 53807 | `PURCH_EVALUATION_NOT_EDITABLE` | 409 | Modificarea unei evaluări trimise sau validate |
| 53808 | `PURCH_EXCEPTION_LOG_IMMUTABLE` | 409 | Încercare de modificare sau ștergere în jurnalul de excepții |
| 53809 | `PURCH_TRACE_DEPTH_EXCEEDED` | 409 | Drum mai adânc de 12 niveluri (semn de buclă în legături) |
| 53810 | `PURCH_REPORT_SCOPE_DENIED` | 403 | Raport cerut pe un furnizor din afara vizibilității achizitorului |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.Report.View`, `Purch.Report.ViewValue` | Rapoartele de achiziții; valorile din ele |
| `Purch.Performance.View`, `Purch.Performance.Settings` | Fișa de performanță; ponderile, pragurile și criteriile |
| `Purch.SupplierEvaluation.Edit`, `Purch.SupplierEvaluation.Approve`, `Purch.SupplierEvaluation.Campaign` | Evaluarea, aprobarea ei, campaniile |
| `Purch.LeadTime.Accept` | Acceptarea propunerilor de termen de livrare |
| `Purch.ExceptionLog.View` | Jurnalul de excepții |
| `Purch.Dashboard.View` | Tabloul de bord |

Roluri livrate:
- **Achizitor:** rapoarte (în vizibilitatea lui), performanță, evaluare (editare), tablou de bord, propunerile de termen;
- **Șef achiziții:** toate;
- **Contabil furnizori:** rapoarte, jurnalul de excepții;
- **Auditor** (rol nou, doar citire): rapoarte cu valori, jurnalul de excepții, drumul documentelor, evaluările;
- **Responsabil calitate:** performanță, evaluare (editarea criteriilor de calitate).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A7 |
| --- | --- |
| Legea contabilității 82/1991 și OMFP 2634/2015 (documente justificative) | Drumul documentului leagă fiecare înregistrare de documentele justificative; nimic nu se șterge din istoric |
| Controlul intern și auditul (Legea 82/1991 art. 6, standardele de audit) | Jurnalul de excepții, nemodificabil, cu aprobatorul și motivul; raportul de excepții pe aprobator |
| ISO 9001 §8.4 (nu e obligație legală, dar e cerută de clienții certificați) | Criteriile de evaluare, evaluarea periodică, păstrarea rezultatelor și a acțiunilor |
| Siguranța alimentară (Regulamentul CE 178/2002, trasabilitatea „un pas înapoi, un pas înainte”) | Drumul documentului + trasabilitatea lotului din Stocuri S4 |

Integrări interne: A1 (blocarea furnizorului, termenul de livrare), A2 (scorul în compararea ofertelor), A3–A6 (sursele indicatorilor, excepțiile), Stocuri S4 (loturi, carantină, retrageri) și S7 (motorul de rapoarte), Finanțe F3 (plăți) și C3 (închiderea lunii), Raportare 02.13 (vederile publicate). Externe: niciuna; trimiterea evaluării către furnizor folosește e-mailul din Nucleu.

## 10. Rapoarte (catalogul complet al modulului)

Rapoartele din A1–A6, cu partea în care sunt definite, și rapoartele noi din A7. Toate au filtrele comune (3.1), export și trimitere programată.

| Grupă | Rapoarte | Parte |
| --- | --- | --- |
| Configurare | Tranzacții folosite · Catalog incomplet · Potriviri automate · Liste de prețuri care expiră · Condiții furnizori | A1 |
| Necesar și oferte | Referate în aprobare · Referatele mele · Necesar neacoperit · Cereri de ofertă fără răspuns · Economii din atribuiri · Consum pe centre de cost | A2 |
| Comenzi | Comenzi deschise · Livrări întârziate · Comenzi neconfirmate · Abateri de preț · Revizii · Angajamente | A3 |
| Recepție | Recepții pe perioadă · Recepții nefacturate (`408`) la dată · Diferențe la recepție · Avize nerecepționate · Retururi la furnizori · Loturi intrate în carantină | A4 |
| Facturi | Inbox e-Factura · Facturi blocate la plată · Diferențe de potrivire · Facturi fără e-Factura · Retururi fără notă de credit · Mărfuri facturate nerecepționate · Cheltuieli pe centre de cost | A5 |
| Import | Importuri deschise · Structura costului de achiziție · Costuri auxiliare nerepartizate · Registrul DVI · Verificări VIES · Coduri NC8 de revizuit | A6 |
| **Furnizori** (nou) | **Performanța furnizorilor** (clasament, cu scor, clasă, OTIF, calitate, tendință) · **Fișa de performanță** (un furnizor, pe luni) · **Evaluări** (scadente, pe decizie, acțiuni deschise) · **Certificate care expiră** · **Propuneri de termen de livrare** | A7 |
| **Analiză** (nou) | **Cheltuiala pe furnizori (ABC)** · **Cheltuiala pe articole și grupe** · **Istoricul prețurilor** · **Dependența de furnizor** · **Comparație cu anul trecut** · **Termenul mediu de plată** | A7 |
| **Control** (nou) | **Jurnalul de excepții** · **Excepții pe aprobator** · **Excepții repetate** · **Drumul documentului** · **Drumul liniei de comandă** | A7 |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a7-rapoarte/](ecrane/a7-rapoarte/). Meniul Achiziții › Rapoarte conține: Tablou de bord, Performanța furnizorilor, Evaluări furnizori, Drumul documentului, Analiza achizițiilor, Istoric prețuri, Jurnal de excepții și catalogul celorlalte rapoarte.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Tablou de bord achiziții | Indicatorii lunii, alertele, clasamentul furnizorilor după cheltuială cu scorul și tendința | `a7-01-tablou-de-bord` |
| Fișa de performanță a furnizorului | Furnizor Ambalaje SRL pe 12 luni: notele, scorul 81,6 (B), evoluția lunară, sursele fiecărui indicator, propunerea de termen | `a7-02-performanta-furnizor` |
| Evaluarea furnizorului (fișa) | `EVF 0012`: indicatorii copiați, criteriile manuale, scorul final 79,3, decizia „Aprobat condiționat”, planul de acțiuni, aprobarea | `a7-03-evaluare-furnizor` |
| Drumul documentului | `CF 2289` → `AV` → `NIR 4490` → `PVR 0017` → `FF 0911` → `CA` → nota de credit așteptată → plata blocată; drumul liniei FOLIE23 | `a7-04-drumul-documentului` |
| Analiza achizițiilor | Cheltuiala pe furnizori cu ABC, scor și OTIF; istoricul prețului BANDA48 (furnizor intern și import din China, la cost complet) | `a7-05-analiza-achizitii` |
| Jurnal de excepții | Excepțiile aprobate din noiembrie, cu documentul, valoarea, cine a cerut, cine a aprobat și motivul | `a7-06-jurnal-exceptii` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A7-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A7-A1 | Pentru datele Furnizor Ambalaje SRL (46 de livrări, 36 la timp, 41 complete, 30 OTIF; 0,70% cantitate cu probleme; +0,6% preț; 47/52 facturi; 8/9 cereri) scorul e 81,6 și clasa B | Test unitar (calculul scorului) |
| A7-A2 | Scorul pe 12 luni se calculează din sumele lunare (numărători și numitori), nu din media procentelor lunare; o lună cu 1 livrare nu cântărește cât una cu 10 | Test unitar |
| A7-A3 | Un furnizor cu 2 livrări apare cu „date insuficiente”; un furnizor fără cereri de ofertă are ponderea reactivității împărțită pe celelalte note | Test unitar |
| A7-A4 | După `LunaInchisa`, luna nu se mai recalculează (`53801`); un NIR validat după închidere pentru o livrare din luna închisă intră în luna curentă ca livrare târzie | Test de integrare (cu C3) |
| A7-A5 | `EVF 0012` dă scorul final 79,3 și decizia propusă „Aprobat condiționat”; fără plan de acțiuni validarea e refuzată (`53805`); o evaluare „Neaprobat” validată blochează furnizorul, iar o comandă nouă e refuzată (`53010`) | Test de integrare (cu A1, A3) |
| A7-A6 | Drumul `CF 2289` întoarce `AV FA 3107`, `NIR 4490`, `PVR 0017`, `FF 0911`, `CA`, nota de credit așteptată și plata blocată; drumul liniei FOLIE23 dă 200 / 196 / 4 / 200 / 0 | Test de integrare (cu Nucleu, F3) |
| A7-A7 | Jurnalul de excepții primește o excepție la acceptarea diferenței de 47,40 lei pe `FF 0911`, în aceeași tranzacție; `UPDATE` și `DELETE` pe tabel sunt refuzate (`53808`) | Test de integrare |
| A7-A8 | Cu `BuyerDataScope = 1`, un achizitor nu vede în rapoarte furnizorii altui achizitor (`53810` pe cerere directă) | Test de integrare |
| A7-A9 | Termenul real 6,2 zile față de 5 declarate produce o propunere; acceptarea schimbă `SupplierTerms.LeadTimeDays` la 6, respingerea nu schimbă nimic | Test de integrare (cu A1) |
| A7-A10 | Toate procedurile A7 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Test de integrare |

Criteriile A7-A1, A7-A2 și A7-A6 sunt critice: ele decid încrederea în scor și în trasabilitate.

## Precizări la alte documente (aplicate pe 27.11.2026)

| Document | Precizarea |
| --- | --- |
| Nucleu și Stocuri S7 §3.1 | Motorul de rapoarte (filtre, desfășurare, export, `ReportView`, `ReportSchedule`) trece în Nucleu, ca `core.ReportView` și `core.ReportSchedule`, folosit de toate modulele; Stocuri îl folosește fără schimbări de comportament |
| A1 §3.3 | `SupplierTerms` primește `IsCritical` (furnizor critic, inclus mereu în evaluare); deblocarea unui furnizor blocat de o evaluare cere motiv și scrie în jurnalul de excepții |
| A2 §4.5 și A1 §3.3 | Compararea ofertelor afișează scorul și clasa furnizorului (`ISupplierPerformanceReader`). La baza de comparare 2 (scor ponderat), nota de „rating” e scorul A7 pe 12 luni; `SupplierTerms.Rating` (manual, 1–5) rămâne doar pentru furnizorii cu „date insuficiente” |
| A3, A4, A5, A6 | Excepțiile listate în A7 §3.5 se scriu prin `IPurchaseExceptionWriter`, în tranzacția documentului |
| Stocuri S4 | Evenimentele `RetragereValidata` și `LotStareSchimbata` poartă `SupplierPartnerId` al lotului (furnizorul din recepția care l-a creat) |

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Motorul de rapoarte | Mutat din Stocuri în Nucleu, comun tuturor modulelor; A7 îl folosește cu filtrele de achiziții | 3.1 | Decis de Aurelian (27.11.2026) |
| 2 | Vizibilitatea în rapoarte | Implicit toți achizitorii văd tot; setabil ca fiecare să vadă doar furnizorii și grupele lui | 3.1, 3.6 | Decis de Aurelian (27.11.2026) |
| 3 | Indicatorii de performanță | OTD, IF, OTIF, calitate (procent, PPM, PV-uri, carantină, retrageri), preț, facturare, reactivitate, termen real, toți calculați din documente | 3.2 | Decis de Aurelian (27.11.2026) |
| 4 | Scorul | Medie ponderată 40 livrare / 30 calitate / 15 preț / 10 facturare / 5 reactivitate, clase A ≥ 85, B ≥ 70, C sub 70, toate setabile; „date insuficiente” sub 5 livrări | 3.2 | Decis de Aurelian (27.11.2026) |
| 5 | Instantaneul lunar | Calculat în fiecare noapte, înghețat la închiderea lunii; scorul pe 12 luni din sume, nu din medii | 3.2, 4.1 | Decis de Aurelian (27.11.2026) |
| 6 | Termenul de livrare real | Propunere de actualizare, acceptată de achizitor; nu se schimbă automat | 3.2 | Decis de Aurelian (27.11.2026) |
| 7 | Evaluarea periodică | Campanie anuală pentru furnizorii peste 10.000 lei și cei critici; scorul final = 80% calculat + 20% criterii manuale | 3.3, 4.2 | Decis de Aurelian (27.11.2026) |
| 8 | Decizia evaluării | Propusă din clasă (A aprobat, B condiționat, C neaprobat), modificabilă cu motiv; condiționat cere plan de acțiuni; neaprobat blochează furnizorul pentru comenzi noi | 3.3 | Decis de Aurelian (27.11.2026) |
| 9 | Drumul documentului | Citit din legături, recursiv, în ambele sensuri, cu plata și stocul; plus drumul liniei de comandă; lotul se deschide în Stocuri S4 | 3.4 | Decis de Aurelian (27.11.2026) |
| 10 | Jurnalul de excepții | Tabel propriu, doar adăugare, scris de A1–A6 în tranzacția documentului; rapoarte pe aprobator și pe excepții repetate | 3.5 | Decis de Aurelian (27.11.2026) |
| 11 | Analiza achizițiilor | Vederea `purch.vw_PurchaseFact`, folosită de rapoartele de analiză și publicată pentru Raportare | 3.7 | Decis de Aurelian (27.11.2026) |
| 12 | Tabloul de bord | Ecran fix pentru șeful de achiziții și achizitori, cu indicatori, alerte și clasament | 3.8 | Decis de Aurelian (27.11.2026) |

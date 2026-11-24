# ERP · 02.02 Contabilitate · C3 Închideri de lună și de an

15 noiembrie 2026 · Aurelian · **✓ Validat (15.11.2026)**, cu ecranele și toate cele 7 decizii. Pornește de la deciziile deja luate în C1 și C2: conturile au atributul `ClosingTarget` (clasele 6 și 7 se închid lunar în `121`), coada „De contat” blochează închiderea lunii, iar un document datat într-o lună închisă rămâne în coadă până când luna se redeschide și documentul se contează (decizia lui Aurelian, 15.11.2026).

A treia din cele 5 sub-părți ale părții contabile a modulului 02.02 Finanțe și contabilitate (schema SQL `fin`, prefix de cod `Erp.Finance`). Acoperă perioadele contabile și starea lor, închiderea lunii (lista de verificări, concordanțele cu evidențele operative, notele de închidere), redeschiderea, închiderea și blocarea anului financiar, preluarea rezultatului și soldurile inițiale la punerea în funcțiune. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), partea financiară [F1](F1-CASIERIE.md)–[F5](F5-PLANIFICARE-TREZORERIE.md), [C1 Planuri de conturi](C1-PLANURI-DE-CONTURI.md), [C2 Modele de contare și note](C2-MODELE-DE-CONTARE-SI-NOTE.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Perioadele sunt **pe firmă**: fiecare firmă din grup își închide lunile singură. Planul de conturi românesc e comun grupului (C1).
- Nucleul refuză orice comandă cu dată contabilă într-o perioadă închisă (`PeriodLockBehavior`, 02.01, pasul 5 din pipeline). Starea perioadei o dă C3, prin `IAccountingPeriodGuard`, pe care o folosesc Nucleul și C2.
- Notele de închidere se scriu prin serviciul de note din C2 (`IJournalEntryWriter`), în jurnalul `IN`, cu `SourceKind` 4 (Închidere) sau 5 (Deschidere). Ele respectă toate regulile notelor (echilibru, conturi frunză, mapare în planurile alternative).
- Notele validate nu se modifică (P4). Excepție: recontarea într-o lună deschisă (C2, 4.6), folosită și pentru refacerea notelor de închidere după o redeschidere.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`): sumele în lei la 2 zecimale.
- **Ce NU face C3**: nu calculează declarațiile (C4, inclusiv D300 și impozitul pe profit), nu face balanța și situațiile financiare (C5), nu face notele documentelor (C2), nu închide ziua de casă (F1) și nici extrasele (F2).

## 1. Scop și perimetru

| Zonă | Ce face C3 | Ce NU face (și cine face) |
| --- | --- | --- |
| Perioade | Anii financiari și lunile fiecărei firme, cu starea lor (deschisă, închisă operativ, închisă, blocată) | Data contabilă a documentelor (modulele sursă) |
| Închiderea lunii | Lista de verificări, cu pașii livrați și pașii proprii ai clientului; rularea și rezultatul fiecărui pas | Corectarea cauzelor (fiecare modul în ecranul lui) |
| Concordanțe | Soldurile conturilor comparate cu evidențele operative: partide (F3), trezorerie (F1, F2), stocuri | Calculul soldurilor operative (modulele lor) |
| Note de închidere | Regularizarea TVA (`4426`, `4427` → `4423` / `4424`), închiderea claselor 6 și 7 în `121` | Notele documentelor (C2), impozitul pe profit (C4) |
| Redeschidere | Redeschiderea unei luni, cu motiv, efecte afișate și refacerea notelor de închidere | Contarea documentelor din coadă (C2) |
| Închiderea anului | Rezerva legală, preluarea rezultatului în anul următor, soldurile reportate, blocarea anului | Situațiile financiare și depunerea lor (C5) |
| Solduri inițiale | Preluarea balanței dintr-un sistem anterior la punerea în funcțiune | Partidele deschise (F3) și stocurile inițiale (Stocuri) |

## 2. Model de domeniu

```
 Company (Nucleu)
   └─ FiscalYear (an financiar: început, sfârșit, stare)
        └─ AccountingPeriod × 12 (lună: stare, cine și când a închis)
              │
              ├─ PeriodCloseRun (o rulare a listei de verificări)
              │     └─ PeriodCloseRunStep (rezultatul fiecărui pas: OK, blocant, avertisment, de făcut)
              │            ▲
              │            └─ PeriodCloseStepDefinition (pașii livrați + pașii clientului)
              │                    └─ IPeriodCloseCheck (implementat de C1, C2, F1–F4, Stocuri, HR...)
              │
              ├─ PeriodClosingEntry (legătura lunii cu notele ei de închidere, din C2)
              └─ PeriodStatusHistory (închideri, redeschideri, motive)

 FiscalYear închis ──► AccountOpeningBalance (soldurile reportate în anul următor)
 Punere în funcțiune ──► OpeningBalanceImport (balanța preluată, cu rulajele anului)
```

Reguli de business ale modelului:

1. **Lunile se închid în ordine.** Noiembrie nu se închide cât timp octombrie e deschisă. O lună se poate redeschide doar dacă toate lunile de după ea sunt deschise (decizia 3).
2. **Două trepte de închidere** (decizia 1):
   - **Închisă operativ**: modulele nu mai validează documente datate în lună; contabilitatea poate încă face note manuale, reclasificări, note de închidere și poate conta documentele din coadă.
   - **Închisă**: nimic nu se mai scrie în lună.
3. **Luna se închide doar cu toți pașii blocanți în regulă.** Un pas blocant care nu e în regulă se poate trece doar de Contabilul șef, cu motiv scris, păstrat pe rulare (decizia 4). Coada „De contat” și evenimentele în tranzit nu se pot trece niciodată.
4. **Notele de închidere sunt ale lunii, nu ale utilizatorului.** Se generează la închidere, cu data ultimei zile a lunii, în jurnalul `IN`. La o redeschidere urmată de o nouă închidere, ele se refac prin recontare (C2, 4.6): același număr, versiunea veche în istoric.
5. **Nicio lună închisă nu se schimbă fără redeschidere.** Un document care apare cu dată într-o lună închisă (sincronizare offline POS, import) rămâne în coada C2 cu motivul „Perioadă închisă” până la redeschiderea lunii (decizia 3 din C2).
6. **Anul se închide după ce decembrie e închisă**, iar soldurile reportate în anul următor se recalculează automat dacă anul e redeschis. Un an **blocat** (după depunerea situațiilor financiare) nu se mai redeschide decât cu dreptul `Fin.FiscalYear.Unlock`, pe care îl are doar rolul livrat „Deblocare an”, atribuit unui utilizator dedicat care nu are alt rol (decizia 6).
7. **Rezultatul nu se pierde între ani.** Soldul `121` la 31 decembrie se preia în anul următor printr-o notă de deschidere datată 1 ianuarie (`121` → `129` pentru rezerva legală, restul → `117`).

## 3. Entități, documente și ciclul lor de viață

Convențiile de coloane, tipuri și chei sunt cele din F1, secțiunea 3.

### 3.1 An financiar (`FiscalYear`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Year` | Unic pe firmă; `Year` = anul în care începe exercițiul |
| `StartDate`, `EndDate` | Implicit 1 ianuarie – 31 decembrie. Exercițiul diferit de anul calendaristic (permis de Legea 82/1991, art. 27) se setează pe firmă în Nucleu: luna de început. Primul exercițiu al unei firme noi poate fi mai scurt |
| `Status` | 1 Deschis, 2 Închis, 3 Blocat |
| `ClosedAt`, `ClosedBy`, `LockedAt`, `LockedBy`, `LockReason` | Blocarea se face după depunerea situațiilor financiare (C5 publică evenimentul) |
| `VatPeriodicity` | Copiată din Nucleu la crearea anului: 1 Lunar, 3 Trimestrial, 6 Semestrial, 12 Anual; schimbarea în timpul anului se face cu data de la care se aplică |

Anii se creează automat, cu 60 de zile înainte de început, de jobul de perioade (Hangfire); se pot crea și manual.

### 3.2 Perioadă contabilă (`AccountingPeriod`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `FiscalYearId`, `PeriodNo` | 1–12 (luna exercițiului, nu neapărat luna calendaristică) |
| `StartDate`, `EndDate` | Calculate din an |
| `Status` | 1 Deschisă, 2 Închisă operativ, 3 Închisă, 4 Blocată |
| `IsImported` | Lunile dinaintea punerii în funcțiune (3.7): închise, doar cu rulajele preluate |
| `SoftClosedAt`, `SoftClosedBy`, `ClosedAt`, `ClosedBy` | Ultima închidere |
| `LastCloseRunId` | Rularea care a închis luna |
| `RowVersion` | `SQL-11`; închiderea și redeschiderea se fac cu concurență optimistă |

Ciclul de viață:

```
 Deschisă ──(închide operativ)──► Închisă operativ ──(închide luna)──► Închisă ──(blochează anul)──► Blocată
    ▲                                   │                                  │
    └──────────(redeschide)─────────────┴────────────(redeschide)──────────┘
```

Ce se poate face în fiecare stare:

| Stare | Documente din module | Note automate din coadă | Note manuale, reclasificări | Note de închidere |
| --- | --- | --- | --- | --- |
| Deschisă | Da | Da | Da | Da (simulare și generare) |
| Închisă operativ | Nu (`CORE_PERIOD_CLOSED`) | Da | Da | Da |
| Închisă | Nu | Nu (rămân în coadă) | Nu | Nu |
| Blocată | Nu | Nu | Nu | Nu |

Excepția din coloana „Documente din module”: documentele sincronizate din POS offline și cele importate păstrează data reală; Nucleul le primește, iar contarea lor așteaptă în coadă (regula 5).

Închiderea operativă se face manual, din ecranul de perioade, sau automat în ziua `Fin.Period.AutoSoftCloseDay` a lunii următoare (implicit oprită; decizia 2).

### 3.3 Lista de verificări (`PeriodCloseStepDefinition`)

Pașii sunt livrați de sistem, în ordinea în care se rulează; clientul poate dezactiva pașii neblocanți și poate adăuga pași manuali (de exemplu „Inventarul casei la sfârșit de lună, proces-verbal atașat”), confirmați de un utilizator.

| Cod | Pas | Cine îl implementează | Tip |
| --- | --- | --- | --- |
| `PREV_PERIOD` | Luna anterioară e închisă | C3 | Blocant, fără override |
| `EVENTS_IN_TRANSIT` | Nu există evenimente netratate în outbox cu dată contabilă în lună | C3 (citește `core.Outbox`) | Blocant, fără override |
| `DRAFT_DOCUMENTS` | Ciorne de documente datate în lună (facturi, recepții, deconturi) | Fiecare modul | Avertisment |
| `CASH_DAYS` | Toate zilele de casă din lună sunt închise | F1 | Blocant |
| `BANK_STATEMENTS` | Extrasele acoperă toată luna, fără goluri, și liniile lor sunt validate | F2 | Blocant |
| `POSTING_QUEUE` | Coada „De contat” nu are documente datate în lună | C2 | Blocant, fără override |
| `DRAFT_ENTRIES` | Nu există ciorne de note manuale sau recurente în lună | C2 | Blocant |
| `FX_REVALUATION` | Reevaluarea valutară a lunii e validată (doar firmele cu elemente în valută) | F4 | Blocant |
| `PAYROLL` | Statul de salarii al lunii e închis și contat | HR (când e licențiat) | Blocant |
| `DEPRECIATION` | Amortizarea lunii e calculată și contată | Mijloace fixe (când e licențiat) | Blocant |
| `RECONCILIATION` | Concordanțele (3.4) nu au diferențe | C3 + modulele | Blocant |
| `ALT_MAPPING` | Liniile lunii sunt mapate în planurile cu `RequireCompleteMapping` | C1 | Blocant |
| `ABNORMAL_BALANCES` | Conturi cu sold pe partea greșită (`5311` creditor, `401*` debitor pe partener, `371` creditor) | C3 | Avertisment |
| `VAT_CLOSING` | Nota de regularizare TVA (doar în luna de sfârșit a perioadei fiscale de TVA) | C3 | Generare |
| `PL_CLOSING` | Nota de închidere a conturilor de venituri și cheltuieli în `121` | C3 | Generare |

Fiecare pas care nu e făcut de C3 implementează `IPeriodCloseCheck` (6) în modulul lui; C3 nu cunoaște regulile de casă, de bancă sau de stoc, doar rezultatul. Un modul nelicențiat nu își înregistrează pasul.

Fiecare rezultat negativ are un link către ecranul care rezolvă problema (de exemplu „De contat” filtrat pe lună, „Registrul de casă” pe ziua neînchisă).

### 3.4 Concordanțe (`ReconciliationCheck`)

Comparații între soldul contabil și evidența operativă, la ultima zi a lunii, pe firmă:

| Cont (rol C1) | Se compară cu | Granularitate | Sursa evidenței |
| --- | --- | --- | --- |
| `CustomerReceivable`, `CustomerAdvance` (`4111*`, `419`) | Partidele deschise ale clienților | Pe partener și monedă | F3 |
| `SupplierPayable`, `SupplierAdvance` (`401*`, `409`) | Partidele deschise ale furnizorilor | Pe partener și monedă | F3 |
| `BankAccount` (`5121*`, `5124*`) | Soldul final al ultimului extras din lună | Pe cont de trezorerie | F2 |
| `CashAccount` (`5311*`, `5314*`) | Soldul registrului de casă | Pe casă | F1 |
| `AdvanceToEmployee` (`542`) | Avansurile spre decontare nedecontate | Pe angajat | F1 |
| `Stock` (`371`, `301`, `381`...) | Valoarea stocului din evidența de stocuri | Pe gestiune și cont | Stocuri |
| `VatPending` (`4428`) | TVA neexigibilă din facturile cu TVA la încasare neîncasate | Pe partener | F3 |

Pentru fiecare diferență, ecranul arată documentele care o explică, găsite automat: documente validate dar necontate (din coadă), note manuale pe conturi controlate, partide fără notă. Toleranța e zero; diferențele de rotunjire au propria lor notă în C2.

### 3.5 Note de închidere (`PeriodClosingEntry`)

| Notă | Când | Conținut | Regulă |
| --- | --- | --- | --- |
| Regularizarea TVA | Ultima lună a perioadei fiscale de TVA a firmei (lunar sau trimestrial) | `4427` = `4426` pentru suma mai mică; diferența pe `4423` (TVA de plată) sau `4424` (TVA de recuperat); soldurile `4423` / `4424` reportate din perioada anterioară se compensează doar dacă firma a ales compensarea în D300 | Suma trebuie să fie egală cu rezultatul D300 calculat de C4; dacă nu e, pasul e blocant |
| Închiderea veniturilor și cheltuielilor | Fiecare lună, pentru conturile cu `ClosingTarget` = 1 (lunar) | `121` = clasa 6 pe fiecare cont cu sold; clasa 7 = `121` pe fiecare cont cu sold | Fără dimensiuni pe linii (o linie pe cont); rapoartele de profitabilitate pe centre de cost ignoră notele cu `SourceKind` 4 |
| Închiderea anuală | Decembrie, pentru conturile cu `ClosingTarget` = 2 | La fel, pentru conturile care se închid doar anual (setarea pe firmă „închidere doar anuală”, decizia 5) | — |

Notele se generează la închiderea lunii, dar se pot vedea oricând în simulare (ecranul „Note de închidere”), ca să se verifice rezultatul lunii înainte de închidere.

### 3.6 Închiderea anului (`FiscalYearCloseRun`)

Asistent cu pași, pe firmă:

| Pas | Ce face | Unde |
| --- | --- | --- |
| 1 | Impozitul pe profit (sau pe veniturile microîntreprinderii) al anului e contat, înainte de închiderea lui decembrie (`691` = `4411`) | C4 calculează, C2 contează |
| 2 | Rezerva legală: propune `129` = `1061` cu 5% din profitul contabil brut, până când `1061` ajunge la 20% din capitalul social (Legea 31/1990, art. 183); suma se poate modifica, cu motiv | C3 |
| 3 | Decembrie se închide (lista de verificări obișnuită) | C3 |
| 4 | Anul se închide: se calculează soldurile reportate (`AccountOpeningBalance`) pe cont, partener, monedă | C3 |
| 5 | Nota de preluare a rezultatului, datată 1 ianuarie anul următor: `121` = `129` (rezerva legală) și `121` = `117` (restul profitului) sau `117` = `121` (pierdere) | C3, jurnalul `IN`, `SourceKind` 5 |
| 6 | După hotărârea AGA: repartizarea profitului (`117` = `457` dividende, `117` = `1068` alte rezerve, `117` = `1061`...), din șablonul „Repartizarea profitului”, cu hotărârea atașată | C2, notă manuală din șablon |
| 7 | După depunerea situațiilor financiare (C5): anul se blochează | C3 |

**Soldurile reportate** (`AccountOpeningBalance`): soldul fiecărui cont de bilanț la sfârșitul anului, pe cont, partener, angajat, monedă (cu suma în valută). Nu e o notă: balanța anului nou citește soldul inițial din acest tabel, iar rulajele din note. Dacă anul precedent se redeschide și se modifică, soldurile reportate se recalculează la reînchidere și balanța anului curent se schimbă automat; utilizatorul e avertizat la redeschidere.

### 3.7 Solduri inițiale la punerea în funcțiune (`OpeningBalanceImport`)

Pentru o firmă care trece pe ERP dintr-un alt sistem:

1. Se alege **luna de start** (prima lună lucrată în ERP). Poate fi orice lună, nu doar ianuarie (decizia 7).
2. Se importă balanța de la sfârșitul lunii anterioare (Excel, tiparul de import din Nucleu): cont, partener (pentru conturile care cer partener), monedă, sold inițial al anului, rulaje cumulate debit și credit de la începutul anului, sold final.
3. Verificări: balanța e echilibrată (total debit = total credit pe fiecare coloană); conturile sunt frunze active; sold inițial + rulaje = sold final pe fiecare rând; soldurile `401*`, `4111*`, `5311*`, `5121*`, `371` sunt egale cu partidele, casele, băncile și stocurile preluate în modulele lor (aceleași concordanțe ca în 3.4).
4. La validare: soldurile inițiale ale anului intră în `AccountOpeningBalance`; rulajele cumulate devin o notă de deschidere (`SourceKind` 5, jurnalul `IN`) datată în ultima zi a lunii dinaintea startului; lunile de dinainte se creează închise, cu `IsImported` = 1.
5. Importul se poate anula și reface doar cât timp luna de start nu are nicio notă.

## 4. Fluxuri principale

### 4.1 Închiderea lunii

1. Contabilul șef deschide „Închiderea lunii” pentru firmă și lună. Lista se poate rula oricând în lună, ca verificare anticipată; rezultatul arată ce mai e de făcut.
2. Opțional, închide luna operativ (sau jobul o închide în ziua setată), ca modulele să nu mai adauge documente.
3. Rulează lista (`RunPeriodClose`): fiecare pas rulează `IPeriodCloseCheck`, în ordine; rezultatele se salvează pe rulare, cu detaliile și linkurile.
4. Rezolvă problemele în ecranele lor și rulează din nou; pașii deja în regulă se re-verifică (datele se pot schimba între rulări).
5. Când toți pașii blocanți sunt în regulă (sau trecuți cu motiv de Contabilul șef), „Închide luna”:
   - generează nota de regularizare TVA (dacă e luna de sfârșit a perioadei de TVA) și nota de închidere a claselor 6 și 7, prin `IJournalEntryWriter`;
   - re-verifică, în aceeași tranzacție, pașii fără override (coada, evenimentele în tranzit) cu blocare pe perioadă (`UPDLOCK` pe rândul `AccountingPeriod`), ca nimic să nu intre între verificare și închidere;
   - trece luna în starea Închisă și publică `PerioadaContabilaInchisa`.

### 4.2 Redeschiderea unei luni

1. Contabilul șef alege luna și scrie motivul (obligatoriu).
2. Ecranul arată efectele înainte de confirmare: notele de închidere care se vor reface, documentele din coadă care se vor putea conta, declarațiile deja depuse pentru lună (C4: D300, D394, D406), planurile alternative cu luna înghețată (C1), soldurile reportate dacă e decembrie.
3. La confirmare: luna trece în Deschisă (sau Închisă operativ, la alegere), se scrie istoricul, se publică `PerioadaContabilaRedeschisa`; coada C2 reîncearcă automat documentele cu motivul „Perioadă închisă” din lună.
4. La reînchidere, notele de închidere se refac prin recontare (regula 4). Dacă o declarație era deja depusă, după reînchidere apare sarcina „Declarație rectificativă de verificat” în C4.

### 4.3 Închiderea și blocarea anului

1. După închiderea lui decembrie, Contabilul șef deschide asistentul pentru an (3.6) și parcurge pașii.
2. „Închide anul”: soldurile reportate se calculează pe set (`SP-08`), anul trece în Închis, nota de preluare a rezultatului se scrie în anul următor și se publică `AnFinanciarInchis`.
3. Când C5 publică `SituatiiFinanciareDepuse` pentru an, ecranul propune blocarea; blocarea trece anul în Blocat și toate lunile lui în Blocată.

### 4.4 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Documente noi datate în octombrie apar după închiderea lunii (sincronizare POS) | Rămân în coada C2 (Perioadă închisă); pasul `POSTING_QUEUE` al lunii următoare nu le numără; redeschiderea lui octombrie le contează automat |
| Se cere redeschiderea lui septembrie cu octombrie închisă | Refuzat (`51905`): se redeschide întâi octombrie (regula 1) |
| Firmă cu TVA trimestrial | Regularizarea TVA se face doar în martie, iunie, septembrie, decembrie; în celelalte luni pasul `VAT_CLOSING` e „Nu se aplică” |
| Plătitor de TVA la încasare | `4428` nu se închide; trece în `4427` sau `4426` prin evenimentele F3 (`TvaLaIncasareExigibil`), înainte de regularizare |
| Diferență între nota de TVA și D300 calculat de C4 | Pasul `VAT_CLOSING` e blocant, cu rândurile D300 care diferă |
| Lună fără nicio notă (firmă inactivă) | Se închide normal; notele de închidere nu se generează |
| Două utilizatori închid aceeași lună simultan | `RowVersion` pe perioadă: al doilea primește `51902` |
| Redeschiderea lui decembrie după închiderea anului | Anul trece înapoi în Deschis; soldurile reportate și nota de preluare a rezultatului se refac la reînchidere; utilizatorul e avertizat că balanța anului curent se va schimba |
| An blocat și o eroare descoperită ulterior | Corecția se face în anul curent (`117` pentru erorile din anii precedenți, conform OMFP 1802/2014); deblocarea e doar pentru situații excepționale, de utilizatorul dedicat (regula 6) |
| Firmă nouă în grup, la jumătatea anului | Primul an financiar începe la data înființării; nu are solduri inițiale |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Payload | Consumatori |
| --- | --- | --- | --- |
| `PerioadaContabilaInchisaOperativ` v1 | Închiderea operativă | `CompanyId`, `Year`, `PeriodNo`, `EndDate` | Toate modulele (mesaj în interfață), POS |
| `PerioadaContabilaInchisa` v1 | Închiderea lunii | La fel + `CloseRunId`, notele de închidere | C1 (propune înghețarea planurilor alternative), C4 (pregătește declarațiile), Raportare |
| `PerioadaContabilaRedeschisa` v1 | Redeschiderea | La fel + `Reason` | C2 (reîncearcă coada), C4 (marchează declarațiile de verificat), Raportare |
| `AnFinanciarInchis` v1, `AnFinanciarBlocat` v1 | Închiderea și blocarea anului | `CompanyId`, `Year` | C5, Raportare |

### 5.2 Consumate

`SituatiiFinanciareDepuse` (C5, propune blocarea anului), `ContareEsuata` și `NotaContabilaValidata` (C2, pentru a actualiza rezultatul pașilor deja rulați fără să se reruleze toată lista), `ZiCasaInchisa` (F1), `ReevaluareValutaraValidata` (F4).

## 6. Contracte

```csharp
namespace Erp.Finance.Contracts.Periods;

public enum PeriodState : byte { Open = 1, SoftClosed = 2, Closed = 3, Locked = 4 }
public enum PeriodWriteKind : byte { SourceDocument = 1, AutomaticEntry = 2, ManualEntry = 3, ClosingEntry = 4 }

// Folosit de PeriodLockBehavior (Nucleu) și de C2 la fiecare scriere cu dată contabilă
public interface IAccountingPeriodGuard
{
    Task<PeriodState> GetStateAsync(int companyId, DateOnly accountingDate, CancellationToken ct);
    Task<bool> CanWriteAsync(int companyId, DateOnly accountingDate, PeriodWriteKind kind, CancellationToken ct);
}

public enum CloseCheckSeverity : byte { Blocking = 1, Warning = 2 }
public enum CloseCheckOutcome : byte { Ok = 1, Failed = 2, NotApplicable = 3 }

public sealed record CloseCheckIssue(string Message, string? DocumentRef, decimal? Amount, string? FixLink);

public sealed record CloseCheckResult(
    CloseCheckOutcome Outcome, string Summary, IReadOnlyList<CloseCheckIssue> Issues);

// Implementat de fiecare modul care contribuie la lista de verificări (F1, F2, F3, F4, C1, C2, Stocuri, HR...)
public interface IPeriodCloseCheck
{
    string Code { get; }                 // de exemplu "CASH_DAYS"
    CloseCheckSeverity Severity { get; }
    bool AllowOverride { get; }
    Task<CloseCheckResult> RunAsync(int companyId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}

// Implementat de modulele cu evidență operativă, pentru concordanțe
public interface ISubledgerBalanceProvider
{
    string AccountRole { get; }          // de exemplu "SupplierPayable"
    Task<IReadOnlyList<SubledgerBalance>> GetBalancesAsync(int companyId, DateOnly asOf, CancellationToken ct);
}

public sealed record SubledgerBalance(int? PartnerId, int? TreasuryAccountId, int? WarehouseId, int? EmployeeId,
    string CurrencyCode, decimal AmountCurrency, decimal AmountRon);
```

Implementările se înregistrează în DI de fiecare modul; C3 le descoperă prin `IEnumerable<IPeriodCloseCheck>` și `IEnumerable<ISubledgerBalanceProvider>` (`NET-05`). `IAccountingPeriodGuard` citește starea dintr-un cache în memorie invalidat la fiecare schimbare de stare (evenimentele din 5.1), ca verificarea de pe fiecare comandă să nu meargă la baza de date.

Comenzi interne C3: `CreateFiscalYear`, `SoftClosePeriod`, `RunPeriodClose`, `OverrideCloseStep`, `ClosePeriod`, `ReopenPeriod`, `ConfirmManualCloseStep`, `SaveCloseStepDefinition`, `ProposeLegalReserve`, `CloseFiscalYear`, `ReopenFiscalYear`, `LockFiscalYear`, `UnlockFiscalYear`, `ImportOpeningBalances`, `ValidateOpeningBalances`, `CancelOpeningBalances`.

## 7. Schema SQL `fin` (partea C3)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `fin.FiscalYear` | `Id int` | UQ `(CompanyId, Year)`; FK `CompanyId` | — |
| `fin.AccountingPeriod` | `Id int` | UQ `(CompanyId, FiscalYearId, PeriodNo)`; UQ `(CompanyId, StartDate)`; FK `FiscalYearId`, `LastCloseRunId` | `(CompanyId, StartDate, EndDate) INCLUDE (Status)` |
| `fin.PeriodStatusHistory` | `Id bigint` | FK `PeriodId` | `PeriodId` |
| `fin.PeriodCloseStepDefinition` | `Id int` | UQ `Code`; UQ `(CompanyId, Code)` pentru pașii clientului | — |
| `fin.PeriodCloseRun` | `Id bigint` | FK `PeriodId` | `(PeriodId, StartedAt)` |
| `fin.PeriodCloseRunStep` | `Id bigint` | UQ `(RunId, StepCode)`; FK `RunId` | — |
| `fin.PeriodClosingEntry` | `Id int` | UQ `(PeriodId, Kind)`; FK `PeriodId`, `EntryId` | — |
| `fin.ReconciliationResult` | `Id bigint` | FK `RunId`, `AccountId` | `(RunId, AccountRole)` |
| `fin.FiscalYearCloseRun` | `Id int` | UQ `FiscalYearId`; FK `FiscalYearId` | — |
| `fin.AccountOpeningBalance` | `Id bigint` | UX `(CompanyId, Year, AccountId, PartnerId, EmployeeId, CurrencyCode)` (cu `NULL` tratat prin coloane calculate) | columnstore nonclustered pentru balanță (`SQL-16`) |
| `fin.OpeningBalanceImport`, `fin.OpeningBalanceImportLine` | `Id int`, `Id bigint` | FK `CompanyId`, `AccountId`, `PartnerId` | — |

Exemplu de tabel complet:

```sql
CREATE TABLE fin.AccountingPeriod (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountingPeriod PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_AccountingPeriod_Company REFERENCES core.Company(Id),
    FiscalYearId     int               NOT NULL CONSTRAINT FK_AccountingPeriod_FiscalYear REFERENCES fin.FiscalYear(Id),
    PeriodNo         tinyint           NOT NULL,
    StartDate        date              NOT NULL,
    EndDate          date              NOT NULL,
    Status           tinyint           NOT NULL CONSTRAINT DF_AccountingPeriod_Status DEFAULT (1),  -- 1 Deschisă, 2 Închisă operativ, 3 Închisă, 4 Blocată
    IsImported       bit               NOT NULL CONSTRAINT DF_AccountingPeriod_IsImported DEFAULT (0),
    SoftClosedAt     datetime2(3)      NULL,
    SoftClosedBy     int               NULL CONSTRAINT FK_AccountingPeriod_SoftClosedBy REFERENCES core.AppUser(Id),
    ClosedAt         datetime2(3)      NULL,
    ClosedBy         int               NULL CONSTRAINT FK_AccountingPeriod_ClosedBy REFERENCES core.AppUser(Id),
    LastCloseRunId   bigint            NULL,
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_AccountingPeriod_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT UQ_AccountingPeriod_Company_Year_No UNIQUE (CompanyId, FiscalYearId, PeriodNo),
    CONSTRAINT UQ_AccountingPeriod_Company_Start UNIQUE (CompanyId, StartDate),
    CONSTRAINT CK_AccountingPeriod_PeriodNo CHECK (PeriodNo BETWEEN 1 AND 12),
    CONSTRAINT CK_AccountingPeriod_Status CHECK (Status IN (1, 2, 3, 4)),
    CONSTRAINT CK_AccountingPeriod_Dates CHECK (EndDate >= StartDate),
    CONSTRAINT CK_AccountingPeriod_Closed CHECK (Status < 3 OR (ClosedAt IS NOT NULL AND ClosedBy IS NOT NULL))
);
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `fin.usp_AccountingPeriod_GetStates` | Stările tuturor perioadelor (pentru cache-ul `IAccountingPeriodGuard`) | Nu |
| `fin.usp_AccountingPeriod_SoftClose`, `_Close`, `_Reopen` | Schimbarea stării, cu verificarea ordinii, a `RowVersion` și istoricul | Nu |
| `fin.usp_PeriodCloseRun_Start`, `_SaveStep`, `_Override` | Rularea listei și rezultatele | Nu |
| `fin.usp_PeriodClosing_BuildVat`, `_BuildProfitLoss` | Calculul liniilor notelor de închidere, pe set, din liniile lunii (fără să le scrie; scrierea e prin C2) | Nu |
| `fin.usp_Reconciliation_GlBalances` | Soldurile contabile pe rol, partener, cont de trezorerie, gestiune, la dată | Nu |
| `fin.usp_AbnormalBalance_List` | Conturile cu sold pe partea greșită | Nu |
| `fin.usp_FiscalYear_Close`, `_Reopen`, `_Lock`, `_Unlock` | Anul, soldurile reportate, nota de preluare a rezultatului | Nu |
| `fin.usp_OpeningBalance_Validate`, `_Apply`, `_Cancel` | Preluarea soldurilor inițiale | Nu |

Coduri de eroare (plaja C3 `51900–51939`, prefix API `FIN_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 51901 | `FIN_PERIOD_NOT_FOUND` | 404 | Perioadă inexistentă pentru firmă și dată (anul nu e creat) |
| 51902 | `FIN_PERIOD_CONCURRENCY_CONFLICT` | 409 | `RowVersion` diferit (`SP-18`) |
| 51903 | `FIN_PERIOD_CLOSE_BLOCKED` | 409 | Pași blocanți nerezolvați și netrecuți |
| 51904 | `FIN_PERIOD_PREVIOUS_OPEN` | 409 | Închiderea unei luni cu luna anterioară deschisă |
| 51905 | `FIN_PERIOD_REOPEN_ORDER` | 409 | Redeschiderea unei luni cu o lună ulterioară închisă |
| 51906 | `FIN_PERIOD_LOCKED` | 409 | Orice schimbare într-o lună blocată |
| 51907 | `FIN_PERIOD_REASON_REQUIRED` | 422 | Redeschidere sau override fără motiv |
| 51908 | `FIN_PERIOD_EVENTS_IN_TRANSIT` | 409 | Evenimente netratate în outbox, cu dată în lună |
| 51909 | `FIN_PERIOD_OVERRIDE_FORBIDDEN` | 403 | Override pe un pas care nu permite override sau fără drept |
| 51910 | `FIN_VAT_CLOSING_MISMATCH` | 409 | Nota de regularizare TVA diferă de D300 calculat (C4) |
| 51911 | `FIN_FISCAL_YEAR_NOT_CLOSABLE` | 409 | Închiderea anului cu decembrie deschisă sau cu pași nefăcuți |
| 51912 | `FIN_FISCAL_YEAR_LOCKED` | 409 | Redeschiderea unui an blocat fără `Fin.FiscalYear.Unlock` |
| 51913 | `FIN_LEGAL_RESERVE_INVALID` | 422 | Rezerva legală peste plafonul legal fără motiv |
| 51914 | `FIN_OPENING_BALANCE_UNBALANCED` | 422 | Balanța importată nu e echilibrată sau sold inițial + rulaje ≠ sold final |
| 51915 | `FIN_OPENING_BALANCE_SUBLEDGER_MISMATCH` | 422 | Soldurile importate diferă de partidele, casele, băncile sau stocurile preluate |
| 51916 | `FIN_OPENING_BALANCE_HAS_ENTRIES` | 409 | Anularea importului când luna de start are note |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterea:

- Abatere de la `SQL-19` și `SQL-11` pentru `fin.PeriodCloseRunStep` și `fin.ReconciliationResult`: fără `ModifiedAt`, `ModifiedBy`, `RowVersion`. Motiv: sunt rezultate scrise o singură dată pe rulare; o rulare nouă scrie rânduri noi. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Fin.Period.View` | Perioadele, lista de verificări, concordanțele, notele de închidere în simulare |
| `Fin.Period.SoftClose` | Închiderea operativă |
| `Fin.Period.Close` | Rularea listei și închiderea lunii |
| `Fin.Period.CloseOverride` | Trecerea peste un pas blocant care permite override, cu motiv |
| `Fin.Period.Reopen` | Redeschiderea unei luni |
| `Fin.Period.ManageChecklist` | Pașii proprii ai clientului și dezactivarea pașilor neblocanți |
| `Fin.FiscalYear.Close`, `.Lock` | Închiderea și blocarea anului |
| `Fin.FiscalYear.Unlock` | Deblocarea unui an blocat (doar în rolul „Deblocare an”) |
| `Fin.OpeningBalance.Import` | Preluarea soldurilor inițiale |

Roluri livrate: Contabil șef (toate, mai puțin `Unlock`), Contabil (`View`, `SoftClose`, rularea listei fără închidere), Auditor (`View`), Director financiar (`View`), **Deblocare an** (doar `Fin.Period.View` și `Fin.FiscalYear.Unlock`). Rolul „Deblocare an” se atribuie unui utilizator dedicat (de exemplu `deblocare.an`), creat de Administrator doar pentru asta, fără alt rol; Nucleul refuză atribuirea acestui rol unui utilizator care are și alte roluri. Fiecare deblocare cere motiv și e auditată (decizia lui Aurelian, 15.11.2026).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru C3 |
| --- | --- |
| Legea contabilității 82/1991 (art. 27: exercițiul financiar; art. 28: situațiile financiare anuale) | Exercițiul financiar pe firmă, implicit anul calendaristic; anul se blochează după depunerea situațiilor financiare |
| OMFP 1802/2014 | Închiderea conturilor de venituri și cheltuieli în `121`; rezerva legală pe `129`; preluarea rezultatului în `117`; corectarea erorilor din anii precedenți pe `117` |
| Legea societăților 31/1990, art. 183 | Rezerva legală: 5% din profit până la 20% din capitalul social |
| Codul fiscal (TVA) | Regularizarea TVA pe perioada fiscală a firmei (lunar, trimestrial), egală cu D300 |
| OMFP 2861/2009 (inventarierea) | Pas manual livrat, dezactivat implicit: „Inventarierea anuală, procese-verbale atașate”, în lista lui decembrie |
| Pistă de audit | Istoricul închiderilor și redeschiderilor, cu motiv; rulările listei, cu rezultatul fiecărui pas și override-urile |

Integrări: niciuna externă. Interne: Nucleu (`PeriodLockBehavior`, firme, joburi), C1 (maparea completă, înghețarea planurilor alternative), C2 (coada, notele, recontarea), C4 (D300, impozitul pe profit, declarațiile de verificat), C5 (situațiile financiare depuse), F1–F4, Stocuri, HR (pași și concordanțe).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Starea perioadelor | Firme × luni, cu starea, cine și când a închis | Contabil șef, Director financiar |
| Raportul de închidere a lunii | Rularea finală: pașii, rezultatele, override-urile cu motivele, notele de închidere; PDF arhivat pe lună | Contabil șef, Auditor |
| Concordanțe | Pe cont și pe partener / casă / bancă / gestiune: sold contabil, sold operativ, diferență, explicație | Contabil |
| Redeschideri | Toate redeschiderile, cu motiv, cine, ce s-a schimbat după | Auditor |
| Soldurile reportate | Soldurile de deschidere ale anului, comparate cu soldurile finale ale anului precedent | Contabil șef, Auditor |

Balanța de verificare, fișele de cont și situațiile financiare sunt în C5.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/c3-inchideri/](ecrane/c3-inchideri/), în meniul Finanțe și contabilitate › Închideri:

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Perioade contabile | Firmele grupului × lunile anului, cu starea fiecărei luni; fișa lunii cu istoricul și acțiunile; redeschiderea, cu efectele afișate înainte de confirmare | `c3-01-perioade-contabile` |
| Închiderea lunii | Lista de verificări a lunii, cu rezultatul fiecărui pas, linkul spre rezolvare și override-ul; butonul de închidere | `c3-02-inchiderea-lunii` |
| Concordanțe | Pe cont: sold contabil, sold operativ, diferență; documentele care explică diferența | `c3-03-concordante` |
| Note de închidere | Regularizarea TVA și închiderea în `121`, în simulare sau validate, cu comparația cu D300 | `c3-04-note-de-inchidere` |
| Închiderea anului | Anii firmei; asistentul cu pași (impozit, rezervă legală, închidere, preluarea rezultatului, AGA, blocare) | `c3-05-inchiderea-anului` |

Preluarea soldurilor inițiale folosește ecranul comun de import din Nucleu (șablon Excel, verificare, raport de erori), cu verificările din 3.7.

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.02-C3-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| C3-A1 | Noiembrie nu se închide cu octombrie deschisă (`51904`); octombrie nu se redeschide cu noiembrie închisă (`51905`) | Test de integrare |
| C3-A2 | Cu un document în coada „De contat” datat în lună, închiderea e refuzată și nu se poate trece peste pas | Test de integrare (cu C2) |
| C3-A3 | Un document validat în ultima secundă înainte de închidere (eveniment în outbox) blochează închiderea până e contat | Test de concurență |
| C3-A4 | În starea „Închisă operativ”, o factură nouă datată în lună e refuzată de Nucleu, iar o notă manuală e acceptată | Test de integrare (cu Nucleu) |
| C3-A5 | Nota de închidere aduce toate conturile cu `ClosingTarget` = 1 la sold zero, iar `121` primește exact rezultatul lunii | Test de proprietate |
| C3-A6 | Regularizarea TVA a lui octombrie (4427 412.900,00, 4426 268.330,00) dă `4423` = 144.570,00, egal cu D300 | Test de integrare (cu C4) |
| C3-A7 | Redeschiderea lui octombrie contează automat FV EXD 10198 din coadă; la reînchidere, notele de închidere au aceleași numere, iar versiunile vechi sunt în istoric | Test de integrare |
| C3-A8 | Concordanța `401*` arată diferența de 2.108,00 explicată de cele două facturi Office Line din coadă | Test de integrare |
| C3-A9 | Închiderea anului calculează soldurile reportate egale cu soldurile finale ale lui decembrie, pe cont, partener și monedă; redeschiderea și modificarea lui decembrie le recalculează | Test de proprietate |
| C3-A10 | Rezerva legală propusă respectă plafonul de 20% din capital | Test unitar |
| C3-A11 | Importul unei balanțe de deschidere dezechilibrate e refuzat (`51914`); unul cu `401*` diferit de partidele preluate e refuzat (`51915`) | Test de integrare |
| C3-A12 | Închiderea unei luni cu 2 milioane de linii de notă (o firmă de retail) se face în sub 2 minute, inclusiv concordanțele | Test de performanță |
| C3-A13 | Toate procedurile C3 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile C3-A2, C3-A3 și C3-A5 sunt critice: o lună închisă cu documente necontate sau cu `121` greșit ajunge direct în declarații și în bilanț.

## Decizii de validare

Toate deciziile au fost confirmate de Aurelian pe 15.11.2026:

| # | Întrebare | Decizia | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Câte trepte de închidere | Două: „închisă operativ” (modulele nu mai validează documente, contabilitatea mai poate face note) și „închisă” (nimic) | 2 (regula 2), 3.2 | Decis de Aurelian (15.11.2026) |
| 2 | Închiderea operativă automată | Setare pe firmă, cu ziua din luna următoare (de exemplu 5); implicit oprită, o face Contabilul șef manual | 3.2 | Decis de Aurelian (15.11.2026): rămâne oprită |
| 3 | Ordinea la redeschidere | O lună se redeschide doar dacă lunile de după ea sunt deschise (întâi noiembrie, apoi octombrie) | 2 (regula 1), 4.4 | Decis de Aurelian (15.11.2026) |
| 4 | Diferențele de concordanță | Blochează închiderea; Contabilul șef poate trece peste, cu motiv scris, păstrat pe rulare. Coada „De contat” nu se poate trece niciodată | 2 (regula 3), 3.3 | Decis de Aurelian (15.11.2026) |
| 5 | Închiderea claselor 6 și 7 | Lunar în `121`, pentru toate firmele; setare pe firmă pentru închidere doar anuală | 3.5 | Decis de Aurelian (15.11.2026) |
| 6 | Anul blocat | Se blochează după depunerea situațiilor financiare; deblocarea doar cu un drept separat, în rolul „Deblocare an”, atribuit unui utilizator dedicat care nu are alt rol | 2 (regula 6), 8 | Decis de Aurelian (15.11.2026) |
| 7 | Punerea în funcțiune la jumătatea anului | Permisă: se preiau soldurile inițiale ale anului și rulajele cumulate până la luna de start, ca balanța anului să aibă totalurile corecte | 3.7 | Decis de Aurelian (15.11.2026) |

## Note de implementare (24.11.2026)

Ordinea de implementare e cea din [PLAN-CONTABILITATE.md](PLAN-CONTABILITATE.md): perioadele (C3a) înaintea notelor contabile, restul închiderilor după.

| Pas | Ce s-a făcut | Abateri și completări față de specificație |
| --- | --- | --- |
| C3a | Migrarea `2026.11.057` (`fin.FiscalYear`, `fin.AccountingPeriod`, `fin.PeriodStatusHistory`); procedurile `fin.usp_FiscalYear_Create`, `_DueForCreation`, `fin.usp_AccountingPeriod_List`, `_SetStatus`, `_State`, `_History`; contractul `IAccountingPeriodGuard` și evenimentele `PerioadaContabilaInchisaOperativ`, `PerioadaContabilaInchisa`, `PerioadaContabilaRedeschisa` (`Erp.Finance.Contracts.Periods`); `AccountingPeriodGuard` înlocuiește `IPeriodLock`-ul implicit al Nucleului, deci `PeriodLockBehavior` și verificările din F1–F4 refuză documentele datate într-o lună închisă operativ, închisă sau blocată; comenzile `CreateFiscalYear`, `SoftClosePeriod`, `ClosePeriod`, `ReopenPeriod` și interogările `GetAccountingPeriods`, `GetPeriodHistory`; jobul `FiscalYearCreation` (zilnic la 00:20, anul următor cu 60 de zile înainte); 9 permisiuni C3 și rolul `DEBLOCARE_AN`; API `/api/finance/periods`, `/fiscal-years`; ecranul Perioade contabile (macheta c3-01, pe firma curentă); testele C3-A1, A4 | **O firmă fără an financiar pentru dată nu are perioade închise**: modulele funcționează ca până acum până la crearea primului an (contabilitatea pornește odată cu el). Jobul creează doar anii următori ai firmelor care au deja un an. Închiderea cere luna anterioară cel puțin la fel de închisă (închiderea operativă cere anterioara cel puțin operativ); redeschiderea cere lunile următoare cel mult la fel de deschise, deci redeschiderea în „închisă operativ” e permisă cu luna următoare tot operativ. `ClosePeriod` închide deocamdată direct; lista de verificări, notele de închidere și blocarea pe coada „De contat” vin cu C3.1–C3.2. `Fin.FiscalYear.Unlock` e doar în rolul `DEBLOCARE_AN` (nici `ADMIN`, nici `CONTABIL_SEF` nu îl au); refuzul Nucleului de a atribui acest rol unui utilizator cu alte roluri și deblocarea anului vin cu C3.3. Ecranul arată firma curentă, nu toate firmele grupului (vederea de grup din machetă vine cu Raportarea). Starea se citește din bază la fiecare verificare (fără cache în memorie, încă) |
| C3.1a | Migrarea `2026.11.063` (`fin.PeriodCloseStepDefinition`, `fin.PeriodCloseRun`, `fin.PeriodCloseRunStep`); seed-ul `12_PeriodCloseSteps`; procedurile `fin.usp_PeriodClose_Check` (pașii `PREV_PERIOD`, `EVENTS_IN_TRANSIT`, `CASH_DAYS`, `POSTING_QUEUE`, `DRAFT_ENTRIES`, `ALT_MAPPING`, `ABNORMAL_BALANCES`), `fin.usp_PeriodCloseRun_Save`, `_Latest`, `fin.usp_PeriodCloseRunStep_Override`, `fin.usp_PeriodCloseStep_Active`, `fin.usp_AccountingPeriod_Get`; contractul `IPeriodCloseCheck` cu `CloseCheckSeverity`, `CloseCheckOutcome`, `CloseCheckResult`; `PeriodCloseService`; comenzile `RunPeriodClose`, `OverrideCloseStep`, interogarea `GetPeriodCloseRun`; `ClosePeriod` rulează lista și refuză cu codul primului pas blocant; coada și evenimentele în tranzit re-verificate în tranzacția închiderii (`fin.usp_AccountingPeriod_SetStatus`); coduri `51903`, `51908`, `51909`; API `/api/finance/periods/{id}/close-run`, `/period-close-runs/{id}/override`; testele C3-A2, A3 | **Pașii** sunt implementări `IPeriodCloseCheck` descoperite prin DI; cei de acum sunt scriși în SQL (o procedură cu un pas pe cod), iar activarea lor vine din tabelul de definiții. **Evenimentele în tranzit** sunt evenimentele din outbox încă neprocesate ale căror tipuri au model de contare (documentele validate care n-au ajuns la contare), fără filtrul pe dată (outbox-ul nu are data contabilă): orice document netratat blochează. **Trecerea peste un pas** se face pe ultima rulare și se păstrează la rularea următoare pentru pașii care eșuează din nou. Închiderea refuzată păstrează codurile existente (`FIN_PERIOD_PREVIOUS_OPEN`, `FIN_MAPPING_INCOMPLETE`), plus `FIN_PERIOD_EVENTS_IN_TRANSIT` și `FIN_PERIOD_CLOSE_BLOCKED`. Rămân pentru C3.1b: concordanțele (`RECONCILIATION`, `ISubledgerBalanceProvider`), extrasele bancare (`BANK_STATEMENTS`), reevaluarea (`FX_REVALUATION`), pașii proprii ai clientului și confirmarea lor, închiderea operativă automată (oprită, decizia 2) |
| C3.1b | Procedura `fin.usp_Reconciliation_Get` și pasul `RECONCILIATION` (blocant, cu trecere); interogarea `GetReconciliation`; API `/api/finance/reconciliation`; testul C3-A8 | **Concordanțele de acum**: clienții (conturile rolurilor `CustomerReceivable` și `CustomerAdvance`, cu analiticele) și furnizorii (`SupplierPayable`, `SupplierAdvance`) pe partener, față de partidele F3 refăcute la dată (aceeași logică ca `fin.usp_OpenItem_AsOf`), și casele în lei față de soldul ultimei zile din registrul de casă. Soldul contabil e D − C din anul financiar al datei (notele de deschidere incluse). Diferența e explicată de documentele din coada „De contat” ale căror partide sunt ale partenerului (legătura prin documentul sursă). Concordanțele se calculează în SQL, nu prin `ISubledgerBalanceProvider`: toate evidențele de acum sunt în schema `fin`; contractul vine cu modulul Stocuri. Rămân: băncile (soldul ultimului extras), avansurile `542`, TVA neexigibilă `4428`, stocurile (RAMAS 1.25) |
| C3.2 | Migrarea `2026.11.064` (`fin.PeriodClosingEntry`); procedura `fin.usp_ClosingEntry_Generate` (scriere și simulare); `ClosingEntries`, apelat de `ClosePeriod` după lista de verificări; interogarea `PreviewClosingEntries`; setarea `Fin.Closing.AnnualOnly`; API `/api/finance/periods/{id}/closing-entries`; verificarea dimensiunilor sărită pe notele de închidere și deschidere; testele C3-A5, A6, A7 | **Notele de închidere** (jurnalul `IN`, `SourceKind` 4, datate în ultima zi a lunii, fără dimensiuni pe linii): regularizarea TVA în ultima lună a perioadei de TVA a anului (`4427` = `4426`, diferența pe contul rolului `VatPayable` sau `VatReceivable`) și închiderea claselor 6 și 7 în contul rolului `ProfitOrLoss` (o linie pe cont, `121` = cheltuielile, veniturile = `121`). Soldurile vin din anul financiar până la sfârșitul lunii, fără nota de închidere a acestei luni, deci reînchiderea rescrie aceeași notă cu același număr (istoricul în `fin.JournalEntryHistory`, evenimentul `NotaContabilaRecontata`). `ClosingTarget` se moștenește de la sinteticul contului analitic; cu `Fin.Closing.AnnualOnly` clasele 6 și 7 se închid doar în ultima lună a anului. **Abateri**: comparația regularizării TVA cu D300 vine cu C4 (pasul blocant `VAT_CLOSING`); soldurile lunare materializate (`AccountPeriodBalance`) nu se construiesc încă: balanța citește din linii (RAMAS 1.26); dacă la reînchidere nu mai e nimic de închis, nota veche rămâne neschimbată |
| C3.3 | Migrarea `2026.11.065` (`fin.FiscalYearCloseEntry`); procedurile `fin.usp_FiscalYear_CloseData`, `fin.usp_SystemEntry_Write` (nota de sistem nouă sau rescrisă, cu istoric), `fin.usp_FiscalYearCloseEntry_Save`, `fin.usp_FiscalYear_SetStatus`; redeschiderea unei luni dintr-un an închis redeschide anul; rezerva legală pură `LegalReserve` (`Erp.Finance.Domain.Periods`); `FiscalYearClosing`; comenzile `PostLegalReserve`, `CloseFiscalYear`, `LockFiscalYear`, `UnlockFiscalYear` și interogarea `GetYearClose`; cod nou în hartă `51913`; API `/api/finance/fiscal-years/{year}/close`, `/legal-reserve`, `/lock`, `/unlock`; testele C3-A9, A10 | **Soldurile reportate sunt o notă de deschidere** (`SourceKind` 5, jurnalul `IN`, prima zi a anului următor), nu tabelul `AccountOpeningBalance` (abatere de la 3.6): balanța (C5a) citește notele de deschidere ca solduri inițiale, iar reînchiderea anului rescrie aceeași notă (C3-A9). Conturile de bilanț (clasele 1–5) se reportează pe cont, partener, angajat și monedă, cu suma în valută. **Preluarea rezultatului**, tot în prima zi a anului următor: `121` = `129` (rezerva constituită) și `121` = `117` restul (sau `117` = `121` la pierdere), cu conturile rolurilor `ProfitOrLoss`, `ProfitAppropriation`, `RetainedEarnings`. **Rezerva legală**: profitul brut e rezultatul anului fără notele de închidere și fără impozitul pe profit (`691`, `694`, `697`, `698`); propunerea e 5% din el, până la 20% din soldul `1012`; peste plafon doar cu motiv (51913). Anul se închide doar cu toate lunile închise și cu anul următor creat (51911). Deblocarea cere `Fin.FiscalYear.Unlock` și motiv; refuzul Nucleului de a atribui rolul „Deblocare an” unui utilizator cu alte roluri, impozitul pe profit (C4) și repartizarea profitului după AGA (notă manuală) rămân în RAMAS 1.27 |
| C3.4 | Migrarea `2026.11.066` (`fin.OpeningBalanceImport`, `fin.OpeningBalanceImportRow`); procedurile `fin.usp_OpeningBalanceImport_Save`, `_Validate`, `_Cancel`, `_Get`; comenzile `ImportOpeningBalances`, `ValidateOpeningBalances`, `CancelOpeningBalances` și interogarea `GetOpeningBalances`; codurile `51914`–`51916` în hartă; API `/api/finance/opening-balances`; balanța și fișa contului tratează ca sold inițial doar notele de deschidere din prima zi a anului; testul C3-A11 | **Importul** e o ciornă verificată (conturi frunză active, partenerul cerut, sold inițial + rulaje = sold final pe rând, echilibru pe coloane), apoi **validarea** scrie, într-o tranzacție, nota de deschidere cu soldurile inițiale ale anului (prima zi) și nota cu rulajele cumulate (ultima zi a lunii dinaintea startului; în balanță apar la rulaje precedente), închide lunile de dinainte ca importate și verifică clienții, furnizorii și casele cu aceleași concordanțe ca la închidere, la ziua dinaintea startului (51915, totul se anulează). Soldurile inițiale sunt note de deschidere, nu `AccountOpeningBalance` (aceeași abatere ca la C3.3). **Anularea** șterge notele importului (numerele lor rămân goale în jurnalul `IN`) și redeschide lunile importate, doar fără alte note în luna de start (51916). Rândurile vin ca listă în API; citirea fișierului Excel cu tiparul de import vine cu ecranul (C3.5) |

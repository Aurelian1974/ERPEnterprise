# ERP · 02.02 Contabilitate · C1 Planuri de conturi și mapări

15 noiembrie 2026 · Aurelian · **✓ Validat de Aurelian (15.11.2026)**, cu deciziile lui din aceeași zi (planul românesc comun grupului; puține analitice, pe categorii, partenerul rămâne dimensiune pe linie; ajustările doar pentru planul alternativ pe conturi excluse din raportarea statutară; recalcularea automată a mapării, fără lunile înghețate; prioritate unică, prima regulă potrivită câștigă; împărțirea 1 → N după atribute sau procent).

Prima din cele 5 sub-părți ale părții contabile a modulului 02.02 Finanțe și contabilitate (schema SQL `fin`, prefix de cod `Erp.Finance`). Acoperă planul de conturi românesc (structura, atributele contului, conturile analitice), planurile de conturi alternative și regulile de mapare din planul românesc în cele alternative. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), partea financiară [F1](F1-CASIERIE.md)–[F5](F5-PLANIFICARE-TREZORERIE.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

Celelalte sub-părți ale părții contabile: **C2** Modele de contare și note contabile, **C3** Închideri de lună și de an, **C4** Declarații (D300, D390, D394, D406 SAF-T, D100, D101), **C5** Situații financiare și rapoarte.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Toate firmele sunt din România, cu moneda funcțională RON, și țin contabilitatea după **OMFP 1802/2014** (sau OMFP 2844/2016 pentru cele care aplică IFRS, cu același plan de conturi).
- Partea financiară (F1–F5) a scris deja simbolul contului ca text (`GlAccountCode nvarchar(20)`: `5121`, `4111`, `401`, `765`...). C1 face din aceste simboluri conturi reale ale planului românesc și le validează.
- Conturile analitice sunt **puține, pe categorii** (`401.01` furnizori interni, `401.02` furnizori UE, `4111.01` clienți interni, `5121.01` BCR-RON), decizia lui Aurelian din 15.11.2026. Partenerul, angajatul, centrul de cost, proiectul și gestiunea rămân **dimensiuni pe linia de notă**: nu se face câte un analitic pentru fiecare partener. Dimensiunile vin din Nucleu (`core.Dimension`, `core.DimensionValue`).
- **Ce NU face C1**: nu face note contabile și nu alege conturi pentru documente (C2), nu închide perioade (C3), nu calculează balanța sau bilanțul (C5). C1 dă nomenclatorul de conturi, atributele lor și maparea în planurile alternative.

## 1. Scop și perimetru

| Zonă | Ce face C1 | Ce NU face (și cine face) |
| --- | --- | --- |
| Planul românesc | Planul de conturi OMFP 1802/2014 livrat (clasele 1–9, conturi sintetice de gradul I și II), conturile analitice ale clientului, atributele fiecărui cont | Notele contabile (C2) |
| Atributele contului | Tipul (activ, pasiv, bifuncțional), dimensiunile cerute sau interzise, moneda, contul standard SAF-T, rolul contului (furnizori, clienți, TVA...) | Folosirea atributelor la contare (C2) |
| Planuri alternative | Oricâte planuri alternative (IFRS de grup, planul companiei-mamă, plan de gestiune), cu conturile lor | Situațiile financiare pe planul alternativ (C5) |
| Mapare | Regulile care duc fiecare linie de notă din planul românesc în fiecare plan alternativ: 1→1, N→1, 1→N după atribute sau procente | Consolidarea între firme (C5) |
| Verificare | Conturile cu rulaj fără mapare, regulile care se suprapun, simularea „ce cont rezultă” | Blocarea închiderii de lună (C3, care citește verificarea din C1) |
| Import | Importul conturilor analitice, al planurilor alternative și al regulilor din Excel | Migrarea soldurilor din alt sistem (C3, solduri inițiale) |

## 2. Model de domeniu

Două zone: **planul românesc**, singurul în care se fac note, și **planurile alternative**, care se obțin din cel românesc prin reguli. Legătura dintre ele e rezultatul mapării, memorat pe fiecare linie de notă.

```
 Plan românesc (Statutory, unul pe instalare)        Plan alternativ (oricâte)
 ┌───────────────────────────────┐                   ┌──────────────────────────┐
 │ GlAccount (clasă → sintetic → │                   │ GlAccount (al planului   │
 │ analitic), atribute, dimensiuni│                   │ alternativ)              │
 └──────────────┬────────────────┘                   └────────────▲─────────────┘
                │ linie de notă (C2): cont RO + dimensiuni          │
                ▼                                                  │
        AccountMappingRule (prioritate, condiții, vigoare) ──► ținte (cont, %)
                │
                ▼
        JournalLineAltAccount (rezultat memorat, recalculat la schimbarea regulilor)
```

Reguli de business ale modelului:

1. **Un singur plan statutar pe instalare**, planul românesc. Toate notele contabile se fac doar pe el; din el ies balanța, registrul jurnal, cartea mare, bilanțul și SAF-T (P2).
2. **Conturile sunt comune grupului**, cu restricție opțională pe firmă (`CompanyId`). Un cont analitic al unei singure firme (de exemplu `5121.01` BCR-RON al Exemplu Distribuție) nu apare la celelalte firme.
3. **Doar frunzele primesc note** (`IsPostable = 1`): un cont cu analitice active nu se folosește direct. Un cont sintetic fără analitice e frunză și primește note.
4. **Analitice puține, pe categorii; partenerul rămâne dimensiune.** Firma își dezvoltă doar analiticele de categorie de care are nevoie: furnizori și clienți interni, UE, non-UE (`401.01`, `401.02`, `401.03`, `4111.01`...), furnizori de imobilizări pe tip, bănci (`5121.01`, `5121.02`), TVA pe cote, venituri pe tip de activitate. Partenerul, angajatul, centrul de cost, proiectul și gestiunea stau pe linia de notă, ca dimensiuni; fișa unui furnizor vine din rulajele analiticului lui filtrate pe partener, nu dintr-un cont `401.00027` deschis pentru fiecare partener. Analiticul potrivit se alege automat la contare (C2), după categoria partenerului (3.2, rolurile cu condiții).
5. **Un plan alternativ nu primește note proprii.** Orice sumă din el vine dintr-o linie de notă a planului românesc, prin mapare. Ajustările care există doar în planul alternativ (de exemplu ajustări IFRS) sunt note pe conturi românești din clasa `8` sau pe conturi analitice dedicate, marcate „doar pentru planul alternativ” (atributul `StatutoryReporting = 0`, regula 9), ca balanța statutară să nu le vadă.
6. **Maparea e pe linie, nu pe sold.** Regula se aplică fiecărei linii de notă, cu dimensiunile ei; așa un cont românesc se poate împărți după centrul de cost sau după partener. Unde clasificarea depinde de semnul soldului final (un cont bancar cu sold creditor e datorie în IFRS), regula are o condiție de sold, evaluată în rapoarte (C5), nu pe linie.
7. **Prima regulă potrivită câștigă**, în ordinea priorității din planul alternativ. Dacă nicio regulă nu se potrivește, linia apare la „Nemapate”, cu suma ei.
8. **Regulile au dată de vigoare** și se aplică după data contabilă a liniei. O schimbare de regulă se aplică și retroactiv, prin recalculare (4.4), dar doar pe perioadele neînghețate ale planului alternativ (3.6).
9. **Codul unui cont folosit nu se mai schimbă**, iar contul nu se șterge, doar se dezactivează (`SQL-20`). Denumirea se poate schimba; istoricul denumirilor e păstrat pentru SAF-T și pentru registrele unui an închis.

## 3. Entități și ciclul lor de viață

Convențiile de coloane, tipuri și chei sunt cele din F1, secțiunea 3: `Id int` pentru nomenclatoare, `bigint` pentru tabele care cresc cu volumul (`SQL-03`), coloanele de audit `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`, `RowVersion` (`SQL-11`, `SQL-19`).

### 3.1 Plan de conturi (`ChartOfAccounts`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `Code`, `Name` | `nvarchar(20)`, `nvarchar(200)` | Unic pe instalare (`SQL-24`): `RO`, `IFRS-GRUP`, `MGMT` |
| `Kind` | `tinyint` | 1 Statutar (exact unul, `RO`, livrat), 2 Alternativ |
| `Purpose` | `tinyint` | Pentru alternative: 1 Raportare de grup, 2 IFRS, 3 Planul companiei-mamă, 4 Gestiune, 5 Altul |
| `LanguageCode` | `char(2)` | Limba denumirilor (`ro`, `en`, `de`); planul românesc e `ro` |
| `CompanyScope` | — | Tabel copil `ChartOfAccountsCompany`: firmele pentru care se calculează maparea; gol = toate firmele |
| `RequireCompleteMapping` | `bit` | Pornit: închiderea lunii e blocată dacă există linii nemapate în acest plan (C3) |
| `IsActive` | `bit` | Un plan inactiv nu mai e recalculat; rezultatele vechi rămân |

### 3.2 Cont (`GlAccount`)

Un singur tabel pentru conturile tuturor planurilor; atributele de contare au sens doar în planul românesc.

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `ChartId` | `int` | Planul contului |
| `Code` | `nvarchar(20)` | Simbolul: `401`, `4111`, `5121.01`; unic în plan (`UQ (ChartId, Code)`). În planul românesc, cifrele de la început sunt simbolul sintetic, iar analiticul se scrie după punct |
| `Name` | `nvarchar(200)` | Denumirea curentă; istoricul în `GlAccountNameHistory` |
| `ParentId` | `int` | Contul părinte (clasă → grupă → sintetic gradul I → sintetic gradul II → analitic); în planul românesc, codul copilului începe cu codul părintelui (`CK` în procedura de salvare) |
| `Level` | `tinyint` | 1 Clasă (`4`), 2 Grupă (`40`), 3 Sintetic gradul I (`401`), 4 Sintetic gradul II (`4111`), 5 Analitic (`5121.01`). La planurile alternative: nivelurile planului lor |
| `AccountType` | `tinyint` | 1 Activ, 2 Pasiv, 3 Bifuncțional; livrat pentru planul românesc, după OMFP 1802/2014 |
| `IsPostable` | `bit` | Calculat la salvare: 1 dacă e frunză activă |
| `CompanyId` | `int NULL` | Restricție pe firmă; `NULL` = toate firmele grupului |
| `IsOffBalance` | `bit` | Clasa `8` (conturi în afara bilanțului) |
| `IsManagement` | `bit` | Clasa `9` (contabilitate de gestiune) |
| `StatutoryReporting` | `bit` | Implicit 1; 0 pentru conturile de ajustări folosite doar de planurile alternative (regula 5), excluse din balanța statutară și din SAF-T |
| `CurrencyMode` | `tinyint` | 0 Doar lei, 1 Orice monedă (linia are și suma în valută), 2 O monedă anume (`CurrencyCode`) |
| `PartyRequirement` | `tinyint` | 0 Fără, 1 Partener obligatoriu, 2 Angajat obligatoriu, 3 Partener sau angajat |
| `ManualPosting` | `bit` | Implicit 1; 0 pe conturile ținute de module (`4111*`, `401*`, `371`, `5121*`, `5311*`, `4426`, `4427`): notele manuale pe ele cer un drept separat (adăugat la scrierea C2, detalii în C2, 3.8) |
| `SaftAccountCode` | `nvarchar(20)` | Contul din nomenclatorul ANAF pentru D406; analiticele îl moștenesc de la sintetic |
| `TaxTreatment` | `tinyint` | Pentru impozitul pe profit (C4): 1 Deductibil, 2 Nedeductibil, 3 Deductibil limitat (protocol, sponsorizare, ajutoare sociale...), 4 Venit neimpozabil; implicit 1 (adăugat la scrierea C4, detalii în C4, 3.6) |
| `ClosingTarget` | `tinyint` | 0 Nimic, 1 Se închide la sfârșitul lunii în `121` (clasele 6 și 7), 2 Se închide la sfârșitul anului (de exemplu `121` în `129` / `117`, detaliat în C3) |
| `ValidFrom`, `ValidTo` | `date` | Un cont nou din plan intră în vigoare de la o dată; un cont scos din plan nu mai primește note după `ValidTo` |
| `IsActive` | `bit` | Inactiv: nu mai primește note, rămâne în rapoarte |

**Dimensiunile contului** (`GlAccountDimensionRule`): pe fiecare cont al planului românesc și fiecare tip de dimensiune din Nucleu (centru de cost, proiect, gestiune, șantier), modul: 0 Interzis, 1 Opțional, 2 Obligatoriu. Exemplu livrat: clasele 6 și 7 cer centrul de cost; `371` cere gestiunea; `231` cere proiectul. Regula se moștenește de la părinte, iar analiticul o poate face mai strictă, nu mai largă.

**Rolurile contului** (`GlAccountRole`): etichete de sistem care spun ce înseamnă contul, folosite de C2 ca ultim nivel în căutarea contului și de C3, C4 la închideri și declarații. Rolurile sunt livrate (cod, nu text liber); atribuirea lor e pe firmă și poate avea condiții: tipul partenerului (juridică RO, UE, non-UE, persoană fizică), grupa de parteneri, moneda. Așa ajung automat furnizorii interni pe `401.01`, cei din UE pe `401.02` și cei non-UE pe `401.03`, fără ca analiticul să fie pus pe fiecare partener. Un partener poate avea totuși contul lui pe fișa financiară (excepție, C2).

| Rol | Cont implicit livrat | Folosit de |
| --- | --- | --- |
| `CustomerReceivable`, `CustomerAdvance` | `4111` (după categorie: `4111.01` interni, `4111.02` UE, `4111.03` non-UE), `419` | Vânzări, F3 |
| `SupplierPayable`, `SupplierAdvance`, `SupplierInvoicesToReceive` | `401` (după categorie: `401.01` interni, `401.02` UE, `401.03` non-UE), `409`, `408` | Achiziții, F3 |
| `VatCollected`, `VatDeductible`, `VatPending`, `VatPayable`, `VatReceivable` | `4427`, `4426`, `4428`, `4423`, `4424` | C2, C3 (regularizarea TVA), C4 |
| `FxGain`, `FxLoss`, `IndexGain`, `IndexLoss` | `765`, `665`, `768`, `668` | F4 |
| `CashInTransit` | `581` | F1, F2 |
| `ProfitOrLoss`, `RetainedEarnings`, `ProfitAppropriation` | `121`, `117`, `129` | C3 |
| `RoundingGain`, `RoundingLoss` | `7588`, `6588` | C2 (diferențe de rotunjire) |

Rolul e una din sursele contului în C2, nu singura: contul de pe articol, partener sau contul de trezorerie are prioritate (lanțul de căutare din C2).

### 3.3 Conturile livrate și cele ale clientului

- Kitul de instalare creează planul `RO` cu toate conturile din OMFP 1802/2014 (clasele 1–9, sintetice de gradul I și II), cu tipul, contul SAF-T, regulile de dimensiuni, rolurile implicite și `ClosingTarget`. Datele vin dintr-un script de migrare (`MIG-06`), nu din cod (P6).
- Conturile livrate au `IsSystem = 1`: denumirea și tipul nu se schimbă de client; se actualizează doar prin migrare, la o schimbare de lege.
- Clientul adaugă **analitice** sub orice cont sintetic. Un cont livrat se poate dezactiva dacă nu are rulaj.
- La o schimbare de lege care adaugă sau scoate conturi, migrarea pune `ValidFrom` / `ValidTo`; nu șterge nimic.

### 3.4 Analitic nou sub un cont cu rulaj

Cazul obișnuit: `5121` a primit note, iar firma vrea `5121.01` și `5121.02` pe bănci. Un cont cu rulaj nu poate deveni părinte la mijlocul anului, pentru că rulajele lui ar rămâne pe un cont care nu mai e frunză. Fluxul:

1. Analiticele se creează cu `ValidFrom` = începutul exercițiului următor sau prima zi a unei luni deschise.
2. Asistentul „Împarte contul” cere, pentru soldul și rulajele de la începutul anului până la `ValidFrom`, analiticul în care trec (unul singur, sau pe o dimensiune, de exemplu contul de trezorerie).
3. Se generează o notă de reclasificare (C2, tip `Reclassification`) care mută soldul pe analitice; contul vechi devine părinte de la `ValidFrom`.
4. Lunile închise nu se modifică; balanța lor arată contul vechi ca frunză.

### 3.5 Regulă de mapare (`AccountMappingRule` + `AccountMappingTarget`)

| Câmp | Reguli |
| --- | --- |
| `TargetChartId` | Planul alternativ |
| `Priority` | `int`, unic în plan; regulile se evaluează crescător, prima potrivită câștigă (regula 7) |
| `SourceKind` | 1 Un cont (cu analiticele lui), 2 Interval de coduri (`6000`–`6999` pe codul normalizat), 3 Listă de conturi (tabel copil `AccountMappingSource`) |
| `SourceAccountId`, `SourceFrom`, `SourceTo` | După `SourceKind` |
| Condiții (toate opționale, în același rând) | `CompanyId`, `Side` (0 Oricare, 1 Debit, 2 Credit), `PartnerType` (juridică RO / UE / non-UE / fizică), `PartnerGroupId`, `PartnerId`, `DimensionTypeId` + `DimensionValueId` (sau „are orice valoare” / „nu are valoare”), `ItemGroupId`, `CurrencyCode` |
| `BalanceSign` | 0 Oricare, 1 Sold debitor, 2 Sold creditor; evaluată doar în rapoartele planului alternativ (regula 6); regula cu `BalanceSign` nu folosește condiții de linie |
| `ValidFrom`, `ValidTo` | După data contabilă a liniei |
| `IsActive`, `Note` | `Note` explică de ce există regula (auditorul o citește) |

**Ținte** (`AccountMappingTarget`): `RuleId`, `TargetAccountId`, `Percent` (`decimal(9,6)`, suma = 100). O țintă = mapare 1→1 sau N→1; mai multe ținte = împărțire procentuală 1→N. Rotunjirea la împărțire: fiecare țintă la 2 zecimale, ultima ia restul (P7, `NET-14`).

Combinațiile cerute de Aurelian, cu exemplele din machete:

| Caz | Cum se scrie |
| --- | --- |
| 1 cont → 1 cont | Regulă cu `SourceKind = 1` (`371` → `1300 Inventories`) |
| N conturi → 1 cont | Regulă cu listă sau interval (`4111`, `4118`, `413`, `418` → `1200 Trade receivables`) |
| 1 cont → N conturi, după atribut | Mai multe reguli pe același cont, cu condiții diferite și priorități crescătoare, plus o regulă fără condiții la final (`628` cu centrul de cost Logistică → `6110`; Vânzări → `6210`; restul → `6900`) |
| 1 cont → N conturi, după procent | O regulă cu mai multe ținte (`612` chirie sediu → 60% `6110`, 40% `6400`) |
| Combinație | Listă de conturi + condiție (`707`, `704` cu partener UE sau non-UE → `4010 Revenue export`; restul → `4000 Revenue domestic`) |
| După semnul soldului | Regulă cu `BalanceSign` (`5121` cu sold creditor → `2150 Bank overdrafts`) |

### 3.6 Rezultatul mapării (`JournalLineAltAccount`) și înghețarea perioadei (`AltChartPeriodFreeze`)

`JournalLineAltAccount` (`bigint`): `JournalLineId` (linia de notă din C2), `ChartId`, `TargetAccountId` (`NULL` = nemapat), `RuleId`, `Percent`, `AmountRon` (partea liniei, cu semn debit / credit ca linia), `MappedAt`. Pentru fiecare linie și fiecare plan alternativ activ al firmei există cel puțin un rând (unul pe țintă).

`AltChartPeriodFreeze`: `ChartId`, `CompanyId`, `PeriodYear`, `PeriodMonth`, `FrozenAt`, `FrozenBy`. O lună înghețată pentru un plan alternativ nu mai e recalculată la schimbarea regulilor (de exemplu după trimiterea pachetului de raportare la grup). Dezghețarea cere `Fin.Mapping.Unfreeze` și intră în audit.

## 4. Fluxuri principale

### 4.1 Instalare și configurare inițială

1. Kitul creează planul `RO` (3.3) și rolurile implicite pentru fiecare firmă.
2. Contabilul șef adaugă analiticele (bănci, case, TVA pe cote) din ecranul Plan de conturi sau prin import din Excel.
3. Contul de trezorerie (F1) și celelalte referințe `GlAccountCode` din F1–F5 sunt verificate: la salvare, codul trebuie să fie un cont `IsPostable` al planului românesc, activ și permis firmei.

### 4.2 Plan alternativ nou

1. Se creează planul (cod, denumire, scop, limbă, firme) și conturile lui, de mână sau din Excel (cod, denumire, părinte).
2. Se scriu regulile de mapare; ecranul propune reguli 1→1 pentru conturile românești cu rulaj care au același cod în planul alternativ (comod pentru planurile de grup construite pe structura românească).
3. „Verifică maparea” (4.5) arată ce rămâne nemapat și ce reguli se suprapun.
4. „Calculează” rulează maparea pentru toate liniile de notă din perioadele cerute (4.4). Până la primul calcul, planul nu are rapoarte.

### 4.3 Maparea la contare

1. Când C2 validează o notă, în aceeași tranzacție, `fin.usp_AltMapping_ApplyToEntry` scrie `JournalLineAltAccount` pentru liniile notei, pentru fiecare plan alternativ activ al firmei.
2. Evaluarea e pe seturi (`SP-08`): regulile active la data contabilă se încarcă o dată, ordonate după prioritate, și se potrivesc pe toate liniile notei printr-un `CROSS APPLY ... TOP (1) ORDER BY Priority`.
3. O linie fără regulă primește un rând cu `TargetAccountId = NULL`; nota se validează oricum, pentru că planul românesc e cel legal. Nemapatele apar în verificare și, dacă planul are `RequireCompleteMapping`, blochează închiderea lunii (C3).
4. Ținta procentuală împarte `AmountRon` după regula de rotunjire din 3.5.

### 4.4 Recalculare după schimbarea regulilor

1. La salvarea unei reguli, sistemul calculează perioadele afectate (după `ValidFrom` / `ValidTo` vechi și noi) și arată câte linii se vor recalcula și diferențele pe conturile alternative („previzualizare”).
2. La confirmare, un job de fundal (Nucleu, joburi) recalculează lunile afectate, câte o lună și o firmă pe tranzacție, sărind peste lunile înghețate (3.6). Lunile sărite sunt listate.
3. Recalcularea șterge și rescrie `JournalLineAltAccount` doar pentru planul respectiv; planul românesc nu se atinge.
4. Fiecare recalculare scrie un rând în `AltMappingRun` (plan, perioade, utilizator, linii, durată), ca auditorul să vadă de ce s-a schimbat o cifră din raportarea de grup.

### 4.5 Verificarea mapării

Raport și ecran, pe plan alternativ, firmă și perioadă:

- **Nemapate**: conturile românești cu linii fără țintă, cu rulajul debit, credit și numărul de linii; clic deschide liniile.
- **Suprapuneri**: perechi de reguli care se pot potrivi pe aceleași linii; arată regula care câștigă și pe cea care nu se mai aplică niciodată („regulă moartă”).
- **Conturi alternative fără sursă**: conturi ale planului alternativ pe care nu le atinge nicio regulă (informativ).
- **Simulator**: pentru un cont românesc și un set de atribute (firmă, partener, centru de cost, dată), arată regula aleasă și contul rezultat, fără să scrie nimic.

### 4.6 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Notă pe o firmă care nu e în `CompanyScope` al planului alternativ | Nu se scrie rezultat pentru acel plan |
| Regulă cu procente care nu fac 100 | Refuzată la salvare (`51808`) |
| Două reguli cu aceeași prioritate | Imposibil: prioritatea e unică în plan; ecranul renumerotează la mutarea unei reguli |
| Dezactivarea unui cont alternativ folosit de o regulă activă | Refuzată (`51810`) până la schimbarea regulii |
| Cont românesc cu rulaj în anul curent, dezactivat | Refuzat; se poate pune `ValidTo` la sfârșitul anului |
| Linie stornată (C2) | Stornarea are propriile linii, mapate cu aceeași regulă la data ei; dacă regula s-a schimbat între timp, diferența rămâne vizibilă în planul alternativ, nu se ascunde |
| Schimbarea codului unui cont fără rulaj | Permisă; cu rulaj, refuzată (`51804`) |
| Import Excel cu un cont părinte lipsă | Rândul e respins, celelalte se importă; raport de import descărcabil |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Payload | Consumatori |
| --- | --- | --- | --- |
| `PlanConturiModificat` v1 | Cont creat, dezactivat, redenumit, schimbare de dimensiuni sau roluri | `ChartId`, `AccountId`, `Code`, `ChangeKind` | Cache-urile de validare ale modulelor (Vânzări, Achiziții, Stocuri), Raportare |
| `MapareRecalculata` v1 | Sfârșitul unei recalculări (4.4) | `ChartId`, `CompanyId`, perioadele, numărul de linii, lunile sărite | Raportare (reîmprospătarea pachetelor de grup), notificare către Contabil șef |

### 5.2 Consumate

| Eveniment | Sursă | Reacție |
| --- | --- | --- |
| `FirmaCreata` v1 | Nucleu | Atribuie rolurile implicite firmei noi |
| `DimensiuneValoareDezactivata` v1 | Nucleu | Avertizare dacă o regulă de mapare activă folosește valoarea |

## 6. Contracte

Alte module citesc planul de conturi prin contracte publice (`Erp.Finance.Contracts`), nu prin tabele (`NET-02`):

```csharp
namespace Erp.Finance.Contracts.Accounts;

public sealed record GlAccountDto(
    int Id, string Code, string Name, byte AccountType, bool IsPostable,
    byte CurrencyMode, string? CurrencyCode, byte PartyRequirement, int? CompanyId);

public sealed record GlAccountRuleDto(int DimensionTypeId, byte Mode);   // 0 Interzis, 1 Opțional, 2 Obligatoriu

public interface IChartOfAccountsQueries
{
    // Planul românesc; null dacă nu există sau nu e permis firmei la dată
    Task<GlAccountDto?> FindPostableAsync(int companyId, string code, DateOnly onDate, CancellationToken ct);

    Task<IReadOnlyList<GlAccountRuleDto>> GetDimensionRulesAsync(int accountId, CancellationToken ct);

    // Contul pe rol (ultimul nivel din lanțul de căutare din C2)
    Task<GlAccountDto?> FindByRoleAsync(int companyId, string role, string? currencyCode, DateOnly onDate, CancellationToken ct);
}

public interface IGlAccountValidator
{
    // Folosit de F1–F5 și de celelalte module la salvarea unui GlAccountCode
    Task<ValidationResult> ValidateAsync(int companyId, string code, DateOnly onDate, CancellationToken ct);
}
```

Comenzi interne C1: `SaveChart`, `SaveGlAccount`, `DeactivateGlAccount`, `SplitGlAccount` (3.4), `SaveDimensionRules`, `SaveAccountRoles`, `SaveMappingRule`, `ReorderMappingRules`, `RecalculateMapping`, `FreezeAltChartPeriod`, `UnfreezeAltChartPeriod`, `ImportChartFromExcel`, `ImportMappingRulesFromExcel`.

## 7. Schema SQL `fin` (partea C1)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `fin.ChartOfAccounts` | `Id int` | UX `Code`; UX filtrat `Kind WHERE Kind = 1` (un singur statutar) | — |
| `fin.ChartOfAccountsCompany` | `(ChartId, CompanyId)` | FK `ChartId`, `CompanyId` | — |
| `fin.GlAccount` | `Id int` | UQ `(ChartId, Code)`; FK `ChartId`, `ParentId`, `CompanyId` | `(ChartId, ParentId)`; `(ChartId, IsPostable) INCLUDE (Code, Name)` |
| `fin.GlAccountNameHistory` | `Id int` | FK `AccountId` | `(AccountId, ValidFrom)` |
| `fin.GlAccountDimensionRule` | `(AccountId, DimensionTypeId)` | FK `AccountId`, `DimensionTypeId` | — |
| `fin.GlAccountRoleAssignment` | `Id int` | UQ `(CompanyId, Role, CurrencyCode, ValidFrom)`; FK `AccountId` | — |
| `fin.AccountMappingRule` | `Id int` | UQ `(TargetChartId, Priority)`; FK `TargetChartId`, `SourceAccountId`, `CompanyId`, `PartnerId`, `DimensionValueId` | `(TargetChartId, IsActive, ValidFrom)` |
| `fin.AccountMappingSource` | `(RuleId, AccountId)` | FK `RuleId`, `AccountId` | — |
| `fin.AccountMappingTarget` | `Id int` | UQ `(RuleId, TargetAccountId)`; FK `RuleId`, `TargetAccountId` | — |
| `fin.JournalLineAltAccount` | `Id bigint` | FK `JournalLineId`, `ChartId`, `TargetAccountId`, `RuleId` | `(ChartId, JournalLineId)`; filtrat `(ChartId) WHERE TargetAccountId IS NULL` |
| `fin.AltChartPeriodFreeze` | `(ChartId, CompanyId, PeriodYear, PeriodMonth)` | FK `ChartId`, `CompanyId` | — |
| `fin.AltMappingRun` | `Id int` | FK `ChartId`, `CompanyId` | `(ChartId, StartedAt)` |

`fin.JournalLineAltAccount` crește cu volumul notelor (o linie de notă × planurile alternative active). Rapoartele pe plan alternativ citesc o vedere indexată pe `(ChartId, CompanyId, PeriodYear, PeriodMonth, TargetAccountId)` cu sumele debit și credit (`SQL-16`), ca balanța alternativă să nu parcurgă liniile.

Exemplu de tabel complet:

```sql
CREATE TABLE fin.GlAccount (
    Id                 int IDENTITY(1,1)  NOT NULL CONSTRAINT PK_GlAccount PRIMARY KEY,
    ChartId            int                NOT NULL CONSTRAINT FK_GlAccount_ChartOfAccounts REFERENCES fin.ChartOfAccounts(Id),
    Code               nvarchar(20)       NOT NULL,
    Name               nvarchar(200)      NOT NULL,
    ParentId           int                NULL CONSTRAINT FK_GlAccount_Parent REFERENCES fin.GlAccount(Id),
    Level              tinyint            NOT NULL,
    AccountType        tinyint            NULL,            -- 1 Activ, 2 Pasiv, 3 Bifuncțional (planul RO)
    IsPostable         bit                NOT NULL CONSTRAINT DF_GlAccount_IsPostable DEFAULT (1),
    CompanyId          int                NULL CONSTRAINT FK_GlAccount_Company REFERENCES core.Company(Id),
    IsOffBalance       bit                NOT NULL CONSTRAINT DF_GlAccount_IsOffBalance DEFAULT (0),
    IsManagement       bit                NOT NULL CONSTRAINT DF_GlAccount_IsManagement DEFAULT (0),
    StatutoryReporting bit                NOT NULL CONSTRAINT DF_GlAccount_StatutoryReporting DEFAULT (1),
    CurrencyMode       tinyint            NOT NULL CONSTRAINT DF_GlAccount_CurrencyMode DEFAULT (0),
    CurrencyCode       char(3)            NULL CONSTRAINT FK_GlAccount_Currency REFERENCES core.Currency(Code),
    PartyRequirement   tinyint            NOT NULL CONSTRAINT DF_GlAccount_PartyRequirement DEFAULT (0),
    SaftAccountCode    nvarchar(20)       NULL,
    ClosingTarget      tinyint            NOT NULL CONSTRAINT DF_GlAccount_ClosingTarget DEFAULT (0),
    IsSystem           bit                NOT NULL CONSTRAINT DF_GlAccount_IsSystem DEFAULT (0),
    ValidFrom          date               NOT NULL,
    ValidTo            date               NULL,
    IsActive           bit                NOT NULL CONSTRAINT DF_GlAccount_IsActive DEFAULT (1),
    CreatedAt          datetime2(3)       NOT NULL CONSTRAINT DF_GlAccount_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int                NOT NULL,
    ModifiedAt         datetime2(3)       NULL,
    ModifiedBy         int                NULL,
    RowVersion         rowversion         NOT NULL,
    CONSTRAINT UQ_GlAccount_Chart_Code UNIQUE (ChartId, Code),
    CONSTRAINT CK_GlAccount_Level CHECK (Level BETWEEN 1 AND 9),
    CONSTRAINT CK_GlAccount_Currency CHECK ((CurrencyMode = 2 AND CurrencyCode IS NOT NULL) OR (CurrencyMode <> 2 AND CurrencyCode IS NULL)),
    CONSTRAINT CK_GlAccount_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
```

Codul contului e comparat exact (baza e case sensitive, `SQL-21`); simbolurile românești sunt cifre și punct, iar codurile planurilor alternative se normalizează la salvare (fără spații la capete). Intervalul din regulă (`SourceKind = 2`) se compară pe `CodeSort`, o coloană calculată persistată care completează codul cu zerouri la dreapta până la 20 de caractere, ca `6000`–`6999` să prindă `628` și `6588`, dar nu `7`.

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `fin.usp_GlAccount_Save`, `_Deactivate`, `_Split` | Salvare cu verificarea părintelui, a codului și a rulajului | Nu |
| `fin.usp_GlAccount_FindPostable` | Contul românesc la o dată, pentru o firmă | Prin `IChartOfAccountsQueries` |
| `fin.usp_AccountMappingRule_Save`, `_Reorder` | Salvare, renumerotarea priorităților, previzualizarea efectului | Nu |
| `fin.usp_AltMapping_ApplyToEntry` | Maparea liniilor unei note la validare (pe seturi, TVP cu Id-urile liniilor) | Nu |
| `fin.usp_AltMapping_Recalculate` | O lună, o firmă, un plan: ștergere și rescriere | Nu (job) |
| `fin.usp_AltMapping_Check` | Nemapate, suprapuneri, conturi fără sursă | Nu |
| `fin.usp_AltMapping_Simulate` | Regula și contul pentru un set de atribute | Nu |

Coduri de eroare (plaja contabilă `51800–51999` se împarte: C1 `51800–51839`, C2 `51840–51899`, C3 `51900–51939`, C4 `51940–51969`, C5 `51970–51999`; prefix API `FIN_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 51801 | `FIN_ACCOUNT_NOT_FOUND` | 404 | Cont inexistent |
| 51802 | `FIN_CONCURRENCY_CONFLICT` | 409 | `RowVersion` diferit (`SP-18`) |
| 51803 | `FIN_ACCOUNT_CODE_DUPLICATE` | 409 | Cod existent în plan |
| 51804 | `FIN_ACCOUNT_HAS_MOVEMENTS` | 409 | Schimbarea codului, a tipului sau dezactivarea unui cont cu rulaj în anul deschis |
| 51805 | `FIN_ACCOUNT_PARENT_INVALID` | 422 | Codul nu începe cu codul părintelui (planul RO) sau părintele e în alt plan |
| 51806 | `FIN_ACCOUNT_NOT_POSTABLE` | 422 | Referință la un cont care nu e frunză, e inactiv sau nu e permis firmei |
| 51807 | `FIN_ACCOUNT_SYSTEM_READONLY` | 403 | Modificarea denumirii sau a tipului unui cont livrat |
| 51808 | `FIN_MAPPING_PERCENT_INVALID` | 422 | Țintele unei reguli nu fac 100% |
| 51809 | `FIN_MAPPING_PRIORITY_DUPLICATE` | 409 | Prioritate existentă în plan |
| 51810 | `FIN_MAPPING_TARGET_IN_USE` | 409 | Dezactivarea unui cont alternativ folosit de o regulă activă |
| 51811 | `FIN_MAPPING_PERIOD_FROZEN` | 409 | Recalcularea explicită a unei luni înghețate |
| 51812 | `FIN_CHART_STATUTORY_READONLY` | 403 | Ștergerea sau dezactivarea planului românesc |
| 51813 | `FIN_ACCOUNT_DIMENSION_RULE_LOOSER` | 422 | Analiticul face mai largă o regulă de dimensiune a părintelui |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu o abatere:

- Abatere de la `SQL-19` și `SQL-11` pentru `fin.JournalLineAltAccount`: fără `ModifiedAt`, `ModifiedBy`, `RowVersion`. Motiv: rezultat de calcul, șters și rescris la recalculare; istoricul e în `fin.AltMappingRun`. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Fin.Account.View` | Planul de conturi, planurile alternative, regulile |
| `Fin.Account.Manage` | Conturi analitice, dimensiuni, roluri, împărțirea unui cont |
| `Fin.Chart.Manage` | Planuri alternative și conturile lor |
| `Fin.Mapping.Manage` | Reguli de mapare, recalculare |
| `Fin.Mapping.Freeze`, `Fin.Mapping.Unfreeze` | Înghețarea și dezghețarea unei luni pe un plan alternativ (auditată) |

Roluri livrate: Contabil șef (toate), Contabil (`Fin.Account.View`), Controller de grup (nou: `View`, `Fin.Chart.Manage`, `Fin.Mapping.*`), Director financiar și Auditor (`View`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru C1 |
| --- | --- |
| OMFP 1802/2014 (planul de conturi general) | Planul `RO` livrat, cu tipul conturilor și funcțiunea lor; analiticele se dezvoltă de entitate după nevoi; actualizare prin migrare la modificarea reglementărilor |
| OMFP 2844/2016 (entități care aplică IFRS) | Același plan de conturi, cu conturile specifice IFRS incluse în livrare |
| Legea contabilității 82/1991 | Registrele (jurnal, cartea mare, balanța) se țin pe planul românesc; planurile alternative nu înlocuiesc evidența legală |
| SAF-T (D406) | Fiecare cont raportat are contul standard din nomenclatorul ANAF (`SaftAccountCode`); analiticele îl moștenesc; ecranul semnalează conturile cu rulaj fără cont SAF-T (verificat de C4) |
| Pistă de audit | Conturile cu rulaj nu se șterg; istoricul denumirilor și al regulilor e păstrat; recalculările mapării sunt jurnalizate |

Integrări: niciuna externă. Nomenclatorul SAF-T vine prin Nucleu (nomenclatoare fiscale). Interne: C2 (linii de notă), C3 (blocarea închiderii), C4 (SAF-T), C5 (balanțe și situații pe planul alternativ), F1–F5 și celelalte module (validarea `GlAccountCode`).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Plan de conturi | Arborele planului românesc cu tipul, atributele, dimensiunile, rolurile; export Excel | Contabil șef, Auditor |
| Verificarea mapării | Nemapate, suprapuneri, conturi alternative fără sursă (4.5) | Contabil șef, Controller de grup |
| Tabel de corespondență | Pe plan alternativ: cont românesc → regula → cont alternativ, cu rulajul perioadei | Controller de grup, Auditor |
| Istoricul mapării | Recalculările, cine, când, ce perioade, ce s-a schimbat | Auditor |

Balanța și situațiile pe planul alternativ sunt în C5.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/c1-planuri-conturi/](ecrane/c1-planuri-conturi/), în meniul Finanțe și contabilitate › Contabilitate:

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Plan de conturi | Arborele planului românesc (clase, grupe, sintetice, analitice) cu filtre; fișa contului cu tab-urile Date generale, Dimensiuni, Roluri, Mapări (unde ajunge contul în fiecare plan alternativ), Istoric | `c1-01-plan-de-conturi` |
| Planuri alternative | Lista planurilor (scop, firme, stare, nemapate); conturile planului selectat, cu sursele lor | `c1-02-planuri-alternative` |
| Reguli de mapare | Regulile unui plan, în ordinea priorității, cu sursă, condiții, ținte și vigoare; editare; simulator „ce cont rezultă” | `c1-03-reguli-de-mapare` |
| Verificarea mapării | Pe plan și lună: nemapate cu rulaj, suprapuneri, lunile înghețate, ultima recalculare | `c1-04-verificare-mapare` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.02-C1-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| C1-A1 | Instalarea creează planul românesc complet, cu tipul fiecărui cont și contul SAF-T, și exact un plan statutar | Test de integrare pe baza nouă |
| C1-A2 | Un cont cu analitice nu primește note; un sintetic fără analitice primește | Test de integrare |
| C1-A3 | Codul unui cont cu rulaj nu se poate schimba, iar contul nu se poate șterge | Test de integrare |
| C1-A4 | „Împarte contul” mută soldul pe analitice printr-o notă de reclasificare, iar balanța lunilor închise rămâne neschimbată | Test de integrare |
| C1-A5 | Cu regulile din 3.5, liniile pe `628` ajung în `6110`, `6210` sau `6900` după centrul de cost, iar suma pe cele trei = rulajul `628` | Test de integrare |
| C1-A6 | O regulă procentuală 60 / 40 pe o linie de 1.000,01 lei dă 600,01 și 400,00 (ultima ia restul), fără bani pierduți | Test unitar |
| C1-A7 | Totalul debit și credit pe orice plan alternativ, inclusiv rândul „Nemapat”, e egal cu totalul planului românesc pentru aceeași firmă și lună | Test de proprietate |
| C1-A8 | Schimbarea unei reguli recalculează doar lunile afectate și sare peste lunile înghețate, pe care le listează | Test de integrare |
| C1-A9 | Simulatorul dă exact contul pe care îl scrie maparea la validarea notei, pentru aceleași atribute | Test de proprietate |
| C1-A10 | Maparea unei note de 500 de linii pe 3 planuri alternative adaugă sub 100 ms la validare; recalcularea unei luni cu 2 milioane de linii durează sub 5 minute | Test de performanță |
| C1-A11 | O linie fără regulă nu blochează validarea notei, dar blochează închiderea lunii când planul cere mapare completă | Test de integrare (cu C3) |
| C1-A12 | Toate procedurile C1 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile C1-A5, C1-A7 și C1-A9 sunt critice: raportarea de grup se bazează pe ele.

## Decizii de validare (15.11.2026)

Toate cele 6 întrebări au fost decise de Aurelian pe 15.11.2026:

| # | Întrebare | Propunerea | Unde e aplicată |
| --- | --- | --- | --- |
| 1 | Conturi analitice pe partener (`401.00027`) sau partenerul ca dimensiune pe linie | **Decis de Aurelian (15.11.2026):** nu facem analitice multe; doar analitice pe categorii (`401.01` furnizori interni, `401.02` UE...); partenerul rămâne dimensiune pe linie, fără analitic pentru fiecare partener; analiticul se alege automat după categoria partenerului | 2 (regula 4), 3.2 |
| 2 | Planul românesc comun grupului sau separat pe firmă | **Decis de Aurelian (15.11.2026):** plan comun grupului, cu analitice restrânse opțional la o firmă | 2 (regula 2), 3.2 |
| 3 | Ajustări care există doar în planul alternativ (IFRS) | **Decis de Aurelian (15.11.2026):** note pe conturi marcate „doar pentru planul alternativ”, excluse din balanța statutară și din SAF-T; fără note direct pe planul alternativ | 2 (regula 5) |
| 4 | Recalcularea după schimbarea regulilor | **Decis de Aurelian (15.11.2026):** automată pe lunile afectate, cu previzualizare; lunile înghețate pentru un plan alternativ nu se recalculează | 3.6, 4.4 |
| 5 | Conflicte între reguli | **Decis de Aurelian (15.11.2026):** prioritate unică, prima regulă potrivită câștigă; verificarea arată regulile care nu se mai aplică niciodată | 3.5, 4.5 |
| 6 | Împărțirea 1 → N | **Decis de Aurelian (15.11.2026):** după atribute (reguli cu condiții) și, opțional, după procent pe o singură regulă | 3.5 |

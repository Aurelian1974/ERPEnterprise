# ERP · 02.02 Contabilitate · C2 Modele de contare și note contabile

15 noiembrie 2026 · Aurelian · **✓ Validat (15.11.2026)**, cu ecranele și toate cele 6 decizii. Pornește de la decizia lui Aurelian din 15.11.2026: modelul de contare ține de evenimentul economic (ce linii de notă se fac, din ce sume, în ce condiții), iar contul concret vine din atribute, pe un lanț de căutare (linia documentului, articolul, partenerul, contul de trezorerie, tipul de operațiune, rolul contului, contul implicit al modelului).

A doua din cele 5 sub-părți ale părții contabile a modulului 02.02 Finanțe și contabilitate (schema SQL `fin`, prefix de cod `Erp.Finance`). Acoperă transformarea evenimentelor economice din toate modulele în note contabile: contractul de date pe care îl trimite un modul ca să poată fi contat, modelele de contare, lanțul de căutare a contului, nota contabilă (automată și manuală), jurnalele, stornarea, coada documentelor necontate și recontarea. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), partea financiară [F1](F1-CASIERIE.md)–[F5](F5-PLANIFICARE-TREZORERIE.md), [C1 Planuri de conturi](C1-PLANURI-DE-CONTURI.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Modulele **nu scriu note contabile**. Ele validează documente și publică evenimente prin outbox (P8, 02.01 fluxul 4.4); C2 le consumă, idempotent (`core.InboxProcessed`), și face notele. Finanțele sunt sursa de adevăr contabil (P2).
- Notele se fac doar pe **planul românesc** (C1). Planurile alternative se calculează din liniile de notă prin regulile de mapare din C1, în aceeași tranzacție cu validarea notei.
- Conturile au roluri și reguli de dimensiuni (C1, 3.2). Analiticele sunt puține, pe categorii (`401.01` furnizori interni, `401.02` UE, `401.03` non-UE); **partenerul, angajatul, centrul de cost, proiectul și gestiunea sunt dimensiuni pe linia de notă** (decizia lui Aurelian, 15.11.2026).
- Perioadele contabile și închiderea lor sunt în C3; C2 doar întreabă dacă data contabilă e într-o perioadă deschisă (`IAccountingPeriodGuard`).
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`): sumele în lei la 2 zecimale.
- **Ce NU face C2**: nu emite documente (le emit modulele), nu închide luni (C3), nu calculează declarații (C4), nu face balanța și situațiile (C5).

## 1. Scop și perimetru

| Zonă | Ce face C2 | Ce NU face (și cine face) |
| --- | --- | --- |
| Contractul de contare | Definește ce trimite un modul pentru a fi contat (`AccountingPayload`): componente de sumă cu atributele lor | Calculul sumelor documentului (modulul sursă) |
| Modele de contare | Un model pe tip de eveniment, cu variante pe condiții și versiuni pe dată; liniile modelului spun ce componentă, pe ce parte, cu ce cheie de cont | Planul de conturi (C1) |
| Lanțul de conturi | Găsește contul concret din atribute: linia documentului, articol, partener, cont de trezorerie, tip de operațiune, rol, implicit | Atributele propriu-zise (Nucleu, Stocuri, F1, C1) |
| Note contabile | Note automate din evenimente, note manuale, note de reclasificare, storno; jurnale și numerotare | Notele de închidere (C3 le cere prin același serviciu) |
| Coada de contare | Evenimentele care nu au putut fi contate (cont negăsit, perioadă închisă, dimensiune lipsă), cu motivul și reîncercarea | Corectarea documentului sursă (modulul sursă) |
| Recontare | Refacerea notelor unui document sau ale unei perioade deschise după schimbarea modelului sau a conturilor | Perioadele închise (C3, redeschidere) |

## 2. Model de domeniu

```
 Modul sursă (Vânzări, Achiziții, Stocuri, F1–F5, HR, Mijloace fixe)
   └─ eveniment v1 + AccountingPayload (componente de sumă cu atribute)
          │  outbox → inbox (idempotent pe EventId)
          ▼
 PostingModel (tip eveniment, variante cu condiții, versiune pe dată)
   └─ PostingModelLine: componentă · parte (D/C) · cheie de cont · condiții · grupare
          │  pentru fiecare componentă: AccountResolver (lanțul din 3.4)
          ▼
 JournalEntry (jurnal, număr, dată contabilă, sursă) ── JournalLine (cont RO, sumă, dimensiuni)
          │                                                  │
          │                                                  └─ JournalLineAltAccount (C1)
          ▼
 PostingQueue (ce nu s-a putut conta, cu motivul)
```

Reguli de business ale modelului:

1. **Un eveniment sursă = cel mult o notă validă.** Notă și eveniment sunt legate prin `SourceEventId` (unic); al doilea mesaj cu același Id e ignorat. Un document sursă poate avea mai multe note doar dacă publică evenimente diferite (factura, apoi încasarea ei).
2. **Nota e echilibrată pe valoarea în lei**: total debit = total credit, la ban. Diferența de rotunjire rezultată din grupare sau din conversie (maxim `Fin.Posting.MaxRoundingDiff`, implicit 0,05 lei) merge pe contul rolului `RoundingGain` / `RoundingLoss`; peste prag, evenimentul intră în coadă.
3. **Modelul spune „ce”, atributele spun „unde”.** Liniile modelului nu conțin conturi concrete (decât ca ultim nivel implicit); conțin o **cheie de cont** (`Revenue`, `Stock`, `PartnerReceivable`...), rezolvată pe lanțul din 3.4.
4. **O notă validată nu se modifică.** Corecția se face prin storno și notă nouă (P4). Excepție: recontarea într-o perioadă deschisă (4.6), care păstrează versiunea veche în istoric.
5. **Stornarea se face „în roșu”**: nota de storno repetă liniile cu sumele negative, pe aceeași parte, ca rulajele să nu fie umflate (practica românească). Setarea `Fin.Posting.ReversalStyle` permite și stornarea „în negru” (părțile inversate), pentru clienții care o cer.
6. **Data contabilă a notei = data contabilă a documentului sursă** (02.01: fiecare document are dată de document și dată contabilă). Dacă perioada e închisă, evenimentul intră în coadă (`ClosedPeriod`) și nu e mutat singur în altă lună (decizia 3).
7. **Nicio notă fără sursă.** Orice notă are `SourceKind`: eveniment (automată), manuală, reclasificare, închidere (C3), deschidere (solduri inițiale, C3). Nota manuală are documentul justificativ atașat sau descris.
8. **Contul și dimensiunile se verifică la fiecare linie**: contul e frunză activă a planului românesc, permis firmei la data contabilă; regulile de dimensiuni ale contului (C1, `GlAccountDimensionRule`) și cerința de partener / angajat sunt respectate; moneda e permisă de cont.

## 3. Entități, documente și ciclul lor de viață

Convențiile de coloane, tipuri și chei sunt cele din F1, secțiunea 3.

### 3.1 Contractul de contare (`AccountingPayload`)

Orice eveniment care trebuie contat poartă, pe lângă datele lui de business, un bloc standard `Accounting`. Modulele nu știu nimic despre conturi; trimit **componente de sumă** cu atributele lor.

**Antet**: `CompanyId`, `SourceModule`, `DocumentType` (cod: `SalesInvoice`, `SupplierInvoice`, `CashReceipt`, `StockIssue`...), `DocumentId`, `DocumentNumber`, `DocumentDate`, `AccountingDate`, `PartnerId`, `EmployeeId`, `CurrencyCode`, `ExchangeRate`, `IsReversal`, `ReversedDocumentId`, atribute de antet (`Flags`: `VatOnCollection`, `ReverseCharge`, `Advance`, `IntraCommunity`, `Import`, `Export`), `Description`.

**Componente** (listă): 

| Câmp | Conținut |
| --- | --- |
| `Component` | Ce sumă e: `Net`, `Vat`, `Total`, `Discount`, `Cost` (cost de stoc), `Freight`, `CustomsDuty`, `FxDifference`, `Amount` (sumă simplă, pentru trezorerie), `Advance`, `NonDeductibleVat`... (nomenclator închis, extins doar prin versiune nouă a contractului) |
| `Amount`, `AmountRon` | Suma în moneda documentului și în lei (calculată de sursă cu cursul documentului) |
| `ItemId`, `ItemGroupId`, `ItemType` | Pentru conturile de stoc, venituri, cheltuieli |
| `VatRateCode`, `VatCategory` | Cota și categoria UBL (`S`, `AE`, `E`, `Z`, `O`, `K`, `G`), pentru TVA și pentru declarații (C4) |
| `TreasuryAccountId`, `OperationTypeCode` | Pentru documentele F1, F2 |
| `ExpenseTypeCode` | Pentru deconturi (F1) |
| `WarehouseId`, `CostCenterId`, `ProjectId`, `SiteId` | Dimensiuni |
| `GlAccountOverride` | Contul ales explicit pe linia documentului (de exemplu cheltuiala aleasă de operator pe o factură de servicii); primul nivel din lanț |
| `SourceLineRef` | Linia documentului sursă (pentru „de unde vine suma”) |

Exemplu, factura de vânzare FV EXD 10241 către Mega Retail (13.11.2026, 12.400 lei + TVA 21%):

```json
{ "Accounting": {
    "DocumentType": "SalesInvoice", "DocumentNumber": "FV EXD 10241",
    "DocumentDate": "2026-11-13", "AccountingDate": "2026-11-13",
    "PartnerId": 1187, "CurrencyCode": "RON", "Flags": [],
    "Components": [
      { "Component": "Net", "AmountRon": 12400.00, "ItemGroupId": 12, "VatRateCode": "S21", "VatCategory": "S", "CostCenterId": 3 },
      { "Component": "Vat", "AmountRon": 2604.00, "VatRateCode": "S21", "VatCategory": "S" },
      { "Component": "Total", "AmountRon": 15004.00 } ] } }
```

Contractul e versionat (`AccountingPayload` v1). F1–F5 și evenimentele de factură din F3 (`FacturaEmisa`, `FacturaFurnizorValidata`, conținutul minim din F3, 3.x) îl primesc în aceeași versiune; modulele următoare (Vânzări, Achiziții, Stocuri, HR, Mijloace fixe) îl respectă din prima lor versiune.

### 3.2 Model de contare (`PostingModel` + `PostingModelVariant` + `PostingModelLine`)

**Modelul** (`PostingModel`): `Code`, `Name`, `EventType` (de exemplu `FacturaEmisa`), `JournalId` (jurnalul în care intră nota), `IsSystem` (livrat), `IsActive`. Un model pe tip de eveniment.

**Varianta** (`PostingModelVariant`): `ModelId`, `Priority`, `Name`, condiții pe antet (`Flags` cerute sau interzise, `PartnerType`, `CompanyId`, `DocumentType`), `ValidFrom`, `ValidTo`. Varianta aleasă = prima, în ordinea priorității, ale cărei condiții se potrivesc (același principiu ca la maparea din C1). Exemplu pentru `FacturaEmisa`: 10 „TVA la încasare”, 20 „Taxare inversă”, 30 „Livrare intracomunitară”, 40 „Export”, 90 „Standard”.

**Liniile variantei** (`PostingModelLine`):

| Câmp | Reguli |
| --- | --- |
| `LineNo` | Ordinea în notă |
| `Component` | Componenta din payload pe care o folosește linia; o linie de model produce câte o linie de notă pentru fiecare componentă potrivită (după grupare) |
| `ComponentFilter` | Condiții pe componentă: `VatCategory`, `ItemType`, `OperationTypeCode`... (de exemplu linia de TVA colectată doar pentru `VatCategory = S`) |
| `Side` | 1 Debit, 2 Credit |
| `Sign` | 1 sau −1 (rar: linii care scad o sumă, de exemplu discountul pe aceeași parte) |
| `AccountKey` | Cheia de cont, rezolvată pe lanțul din 3.4 (`PartnerReceivable`, `Revenue`, `VatCollected`...) |
| `DefaultAccountId` | Contul implicit al liniei, ultimul nivel din lanț (opțional) |
| `CopyDimensions` | Ce dimensiuni din componentă trec pe linia de notă (partener, centru de cost, proiect, gestiune) |
| `GroupBy` | 0 Fără grupare (o linie de notă pe componentă), 1 Pe cont și dimensiuni (implicit), 2 Pe cont |
| `DescriptionTemplate` | Textul liniei, cu câmpuri: `{DocumentType} {DocumentNumber} {PartnerName}` |

**Versiuni**: o variantă se modifică doar pentru viitor. Salvarea unei variante folosite creează o versiune nouă cu `ValidFrom`; notele vechi rămân legate de versiunea folosită (`PostingModelVersionId` pe notă), ca auditorul să vadă exact ce regulă a produs nota.

Exemple livrate (contările standard, cu analiticele pe categorii din C1):

| Eveniment · variantă | Notă |
| --- | --- |
| Factură de vânzare · standard | `4111.01` = `707` + `4427` |
| Factură de vânzare · TVA la încasare | `4111.01` = `707` + `4428` |
| TVA devenit exigibil la încasare (`TvaLaIncasareExigibil`) | `4428` = `4427` |
| Factură de vânzare · livrare intracomunitară | `4111.02` = `707` (fără TVA, categoria `K`) |
| Factură de furnizor · standard | `371` / `6xx` + `4426` = `401.01` |
| Factură de furnizor · achiziție intracomunitară (taxare inversă) | `371` / `6xx` = `401.02`; `4426` = `4427` |
| Factură de furnizor · TVA la încasare | `371` / `6xx` + `4428` = `401.01`; la plată `4426` = `4428` |
| Ieșire din stoc la vânzare (Stocuri) | `607` = `371` |
| Încasare în numerar de la client (F1) | `5311` = `4111.01` |
| Plată prin OP către furnizor (F2) | `401.01` = `5121.01` |
| Comision bancar (F2) | `627` = `5121.01` |
| Avans spre decontare (F1) | `542` = `5311`; decontul: `6xx` + `4426` = `542` |
| Diferență de curs la alocare (F4) | `665` = `401.02` sau `4111.02` = `765` |
| Reevaluare lunară (F4) | pe totalurile din eveniment: `665` = cont element / cont element = `765` |
| Ajustare sub prag (F3) | `658` = `4111.01` sau `401.01` = `758` |
| Compensare (F3) | `401.01` = `4111.01` |
| Efect comercial (F3) | `5113` = `4111.01`; la încasare `5121.01` = `5113` |

### 3.3 Cheile de cont

Cheile sunt un nomenclator livrat (cod, nu text liber). Fiecare cheie are un **lanț de căutare** fix: lista surselor, în ordine, din care se ia contul. Primul nivel care dă un cont câștigă.

| Cheie | Lanțul (în ordine) |
| --- | --- |
| `Revenue` | Override pe linie → articol (`fin.ItemAccounting.RevenueAccount`, pe firmă) → grupa de articole → tipul articolului → rolul `SalesRevenue` (cu condiții) → implicit model |
| `Expense` | Override → articol (`ExpenseAccount`) → grupa → tipul → tipul de cheltuială (decont, F1) → rolul `PurchaseExpense` → implicit |
| `Stock` | Override → articol (`StockAccount`) → grupa → tipul → gestiunea (cont pe gestiune, pentru `371` vs `378` / `357`) → implicit |
| `CostOfSales` | Articol → grupa → tipul → rolul `CostOfGoodsSold` → implicit |
| `PartnerReceivable`, `PartnerPayable` | Contul de pe profilul financiar al partenerului (excepție, F3) → rolul `CustomerReceivable` / `SupplierPayable` cu condițiile lui (tip partener, grupă, monedă → `4111.01` / `.02` / `.03`, `401.01` / `.02` / `.03`) → implicit |
| `PartnerAdvance` | Profilul partenerului → rolul `CustomerAdvance` / `SupplierAdvance` → implicit |
| `Treasury` | Contul de trezorerie (`TreasuryAccount.GlAccountCode`, F1) — obligatoriu, fără alt nivel |
| `TreasuryCounter` | Override → tipul de operațiune (`TreasuryOperationType.CounterGlAccountCode`, F1) → implicit |
| `VatCollected`, `VatDeductible`, `VatPending`, `VatNonDeductible` | Cota de TVA (cont pe cotă, dacă există analitic `4427.21`) → rolul → implicit |
| `FxGain`, `FxLoss`, `IndexGain`, `IndexLoss` | Contul din eveniment (F4 trimite contul) → rolul |
| `Rounding` | Rolul `RoundingGain` / `RoundingLoss` |
| `Fixed` | Doar `DefaultAccountId` al liniei de model |

Cheile noi (de exemplu pentru Mijloace fixe sau HR) se adaugă cu versiunea modulului care le folosește, nu de client.

### 3.4 Rezolvarea contului (`AccountResolver`)

1. Pentru fiecare linie de model și fiecare componentă potrivită, resolver-ul parcurge lanțul cheii și se oprește la primul cont găsit.
2. Contul găsit trebuie să fie frunză activă a planului românesc, permisă firmei la data contabilă (C1). Un cont care nu e frunză (de exemplu `401`, care are `401.01`–`401.03`) e o eroare de configurare, nu se coboară automat într-un analitic.
3. Rezultatul ține și **de unde** vine contul (`ResolvedFrom`: override, articol, grupă, partener, rol, implicit...), afișat în ecran pe fiecare linie de notă, ca utilizatorul să știe ce să schimbe.
4. Resolver-ul e o funcție pură peste datele încărcate o dată pe notă (configurările articolelor, partenerilor, rolurilor), testată unitar pe fiecare cheie (`TST-02`).

### 3.5 Notă contabilă (`JournalEntry` + `JournalLine`)

**Antet** (`JournalEntry`, `bigint`):

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `JournalId`, `Number` | Număr pe jurnal și an, alocat la validare din seriile Nucleului, fără goluri (02.01, numerotare) |
| `EntryDate`, `AccountingDate`, `PeriodYear`, `PeriodMonth` | Perioada se calculează din data contabilă |
| `SourceKind` | 1 Eveniment, 2 Manuală, 3 Reclasificare, 4 Închidere (C3), 5 Deschidere (C3), 6 Storno |
| `SourceModule`, `SourceDocumentType`, `SourceDocumentId`, `SourceDocumentNumber`, `SourceEventId` | Legătura cu documentul; `SourceEventId` unic (UX filtrat) |
| `PostingModelVersionId` | Versiunea de model care a produs nota (automate) |
| `Status` | 1 Ciornă (doar manuale), 2 Validată, 3 Stornată (a primit storno) |
| `ReversalOfId` | Pentru storno: nota stornată |
| `Description`, `TotalDebit`, `TotalCredit` | Totalurile, egale (`CK`) |
| `ValidatedAt`, `ValidatedBy`, `ApprovedBy` | `ApprovedBy` pentru notele manuale cu patru ochi (setare) |

**Linie** (`JournalLine`, `bigint`): `EntryId`, `LineNo`, `AccountId` (planul RO), `Side` (1 Debit, 2 Credit), `AmountRon` (cu semn; negativă la storno în roșu), `CurrencyCode`, `AmountCurrency` (dacă contul ține valută), `PartnerId`, `EmployeeId`, `CostCenterId`, `ProjectId`, `WarehouseId`, `SiteId`, `ItemId` (opțional, pentru analize), `VatRateCode`, `VatCategory`, `Description`, `ResolvedFrom`, `SourceLineRef`.

Coloanele `DebitRon` și `CreditRon` sunt calculate persistate (`CASE Side`), ca balanța și registrele să adune direct.

Registrul jurnal afișează nota în formatul obișnuit al contabilului român („`4111.01` = %, `707`, `4427`”), din aceleași linii.

### 3.6 Jurnal (`Journal`)

`Code`, `Name`, `CompanyId`, `NumberSeriesDocumentType`, `IsSystem`. Livrate: `VZ` Vânzări, `AC` Achiziții, `TR` Trezorerie (casă și bancă), `ST` Stocuri, `SA` Salarii, `MF` Mijloace fixe, `VL` Valută (diferențe și reevaluări), `DV` Diverse (note manuale), `IN` Închideri și deschideri. Clientul poate adăuga jurnale pentru notele manuale.

### 3.7 Coada de contare (`PostingQueue`)

Rândul se creează când un eveniment nu poate fi contat; evenimentul **nu se pierde** și nici nu blochează inbox-ul pentru celelalte.

| Câmp | Reguli |
| --- | --- |
| `EventId`, `EventType`, `CompanyId`, `SourceDocumentType`, `SourceDocumentId`, `SourceDocumentNumber`, `AccountingDate` | Identificarea |
| `Reason` | 1 Cont negăsit, 2 Cont invalid (nu e frunză, inactiv, nepermis firmei), 3 Dimensiune obligatorie lipsă, 4 Perioadă închisă, 5 Model lipsă, 6 Notă dezechilibrată peste prag, 7 Eroare neașteptată |
| `Details` | JSON: componenta, cheia, lanțul încercat, contul găsit |
| `Status` | 1 În așteptare, 2 Rezolvat (nota creată), 3 Ignorat (cu motiv, cere `Fin.Posting.Ignore`) |
| `Attempts`, `LastAttemptAt` | Reîncercarea se face la cerere sau automat după orice schimbare de configurare care ar putea rezolva motivul (cont nou, rol, model) |

C3 nu închide luna cât timp există rânduri în așteptare cu data contabilă în acea lună.

### 3.8 Notă manuală și note recurente

- Nota manuală se creează ca ciornă în jurnalul `DV` (sau altul ales), cu justificativul atașat; validarea verifică regulile 2 și 8. Cu setarea `Fin.ManualEntry.FourEyes` (implicit oprită), nota se validează de alt utilizator decât autorul.
- Conturile cu `ManualPosting = 0` (atribut nou în C1, livrat pe conturile ținute de module: `4111*`, `401*`, `371`, `5121*`, `5311*`, `4427`, `4426`) nu acceptă note manuale fără dreptul `Fin.ManualEntry.ControlledAccounts`, ca soldul lor să rămână egal cu evidența operativă (partide în F3, stocuri, trezorerie).
- **Note recurente** (`RecurringEntryTemplate`): o notă-șablon cu periodicitate (lunar, trimestrial), sume fixe sau calculate dintr-un sold (de exemplu „1/12 din soldul `471`”), generate ca ciorne la data aleasă, validate de om.

## 4. Fluxuri principale

### 4.1 Contarea unui eveniment

1. Consumatorul C2 primește evenimentul din inbox și verifică `SourceEventId` (dacă nota există, se oprește).
2. Alege modelul după `EventType` și varianta după condițiile antetului, la data contabilă.
3. Pentru fiecare linie a variantei: filtrează componentele, rezolvă contul (3.4), copiază dimensiunile, aplică semnul și partea, grupează după `GroupBy`.
4. Verifică nota (regulile 2 și 8) și perioada (`IAccountingPeriodGuard`). La orice eroare: rând în `PostingQueue`, fără notă, și consumul mesajului se consideră reușit (mesajul nu se reîncearcă la nesfârșit în inbox).
5. Alocă numărul din seria jurnalului, scrie nota validată și liniile (`fin.usp_JournalEntry_Insert`, TVP), aplică maparea în planurile alternative (C1, 4.3) și publică `NotaContabilaValidata`, toate în aceeași tranzacție.

### 4.2 Documentul stornat la sursă

Evenimentul de storno (`IsReversal = 1`, `ReversedDocumentId`) produce nota de storno a notei originale (aceleași conturi, sume negative, regula 5), nu o contare nouă cu modelul de azi. Așa stornarea anulează exact ce s-a contat, chiar dacă modelul s-a schimbat între timp. Dacă documentul original nu a fost contat (e în coadă), storno-ul intră și el în coadă și se rezolvă împreună.

### 4.3 Nota manuală

1. Contabilul deschide editorul, alege jurnalul, data contabilă și justificativul, adaugă liniile (cont, debit sau credit, dimensiuni cerute de cont).
2. Ecranul arată în timp real diferența debit-credit, conturile cu dimensiuni lipsă și efectul în planurile alternative.
3. „Validează” verifică perioada și regulile, alocă numărul, mapează și publică evenimentul.

### 4.4 Rezolvarea cozii

1. Ecranul „De contat” arată rândurile în așteptare, grupate pe motiv; pentru „Cont negăsit” arată cheia și lanțul încercat („articolul X nu are cont de venit, grupa Y nici ea, rolul `SalesRevenue` nu e atribuit firmei”).
2. Utilizatorul corectează configurarea (contul pe articol, rolul, modelul) din linkul de pe rând.
3. După salvare, sistemul reîncearcă singur rândurile cu același motiv și aceeași cheie; ce reușește dispare din coadă.
4. Pentru perioadă închisă: fie se redeschide luna (C3), fie se cere modulului sursă un document de corecție în luna curentă. C2 nu mută singur data contabilă (decizia 3).

### 4.5 Storno manual al unei note

Doar pentru notele manuale sau de reclasificare; notele automate se stornează prin stornarea documentului sursă (în modulul lui), ca evidența operativă și contabilitatea să rămână egale. Excepție: dreptul `Fin.JournalEntry.ReverseAutomatic`, pentru situații de recuperare, cu motiv obligatoriu și audit.

### 4.6 Recontarea

Când un model sau un cont pe articol s-a schimbat și notele dintr-o perioadă **deschisă** trebuie refăcute:

1. Utilizatorul alege firma, perioada (doar luni deschise), modelul sau documentele.
2. Previzualizarea arată, pe cont, diferențele dintre notele existente și cele care ar rezulta.
3. La confirmare, pentru fiecare notă afectată: versiunea veche se copiază în `JournalEntryHistory`, liniile se rescriu cu modelul și conturile actuale, numărul și data rămân aceleași, maparea se recalculează, iar `NotaContabilaRecontata` se publică.
4. În perioadele închise nu se recontează; acolo corecția e o notă de reclasificare în luna curentă (decizia 2).

### 4.7 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Același eveniment primit de două ori | A doua oară e ignorat (regula 1) |
| Factură în EUR cu linii pe cursul facturii | `AmountRon` vine din sursă; nota nu recalculează cursul. Rotunjirea din grupare merge pe `Rounding` (regula 2) |
| Componentă care nu se potrivește cu nicio linie de model | Notă dezechilibrată → coadă (motivul 6), cu componenta afișată |
| Articol nou, fără cont de venit, și grupa lui fără cont | Coadă (motivul 1); după setarea contului pe grupă, reîncercare automată |
| Cont analitic dezactivat după validarea documentului, înainte de contare | Coadă (motivul 2) |
| Document cu data contabilă într-o lună închisă | Coadă (motivul 4); nu se mută automat |
| Notă cu 20.000 de linii (factură de retail cu multe articole) | Gruparea implicită pe cont și dimensiuni o reduce la câteva zeci de linii |
| Linie cu sumă zero după rotunjire | Nu se scrie |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Payload | Consumatori |
| --- | --- | --- | --- |
| `NotaContabilaValidata` v1 | Validarea oricărei note (automate, manuale, storno) | `EntryId`, `CompanyId`, `JournalCode`, `Number`, `AccountingDate`, `SourceKind`, `SourceDocumentType`, `SourceDocumentId`, totaluri | Raportare (02.13), C4 (SAF-T incremental), modulul sursă (marcaj „contat”) |
| `NotaContabilaRecontata` v1 | Recontarea unei note (4.6) | `EntryId`, versiunea veche și cea nouă | Raportare |
| `ContareEsuata` v1 | Un eveniment intră în coadă | `EventId`, `Reason`, documentul | Notificare către rolul Contabil șef (Nucleu, notificări) |

### 5.2 Consumate

Toate evenimentele cu bloc `Accounting` (3.1). Din partea financiară: `IncasareInregistrata`, `PlataInregistrata`, `DocumentTrezorerieStornat`, `AvansDecontareAcordat`, `DecontCheltuieliValidat` (F1, F2); `AlocareInregistrata` (doar alocările cu efect contabil: compensări, ajustări, efecte), `TvaLaIncasareExigibil`, `CompensareValidata`, `EfectComercialModificat` (F3); `DiferentaCursInregistrata`, `ReevaluareValutaraValidata`, `ReevaluareValutaraStornata` (F4). Din celelalte module: `FacturaEmisa` (Vânzări), `FacturaFurnizorValidata` (Achiziții), mișcările de stoc (Stocuri), statul de salarii (HR), amortizarea (Mijloace fixe) — fiecare cu blocul `Accounting` din prima versiune.

## 6. Contracte

```csharp
namespace Erp.Finance.Contracts.Posting;

public sealed record AccountingComponent(
    string Component, decimal Amount, decimal AmountRon,
    int? ItemId, int? ItemGroupId, byte? ItemType,
    string? VatRateCode, string? VatCategory,
    int? TreasuryAccountId, string? OperationTypeCode, string? ExpenseTypeCode,
    int? WarehouseId, int? CostCenterId, int? ProjectId, int? SiteId,
    string? GlAccountOverride, string? SourceLineRef);

public sealed record AccountingPayload(
    int Version, int CompanyId, string SourceModule, string DocumentType,
    long DocumentId, string DocumentNumber, DateOnly DocumentDate, DateOnly AccountingDate,
    int? PartnerId, int? EmployeeId, string CurrencyCode, decimal ExchangeRate,
    bool IsReversal, long? ReversedDocumentId, IReadOnlyList<string> Flags, string? Description,
    IReadOnlyList<AccountingComponent> Components);

// Contract pentru module: simularea contării înainte de validarea documentului (de exemplu în ecranul facturii)
public interface IPostingPreview
{
    Task<PostingPreviewResult> PreviewAsync(string eventType, AccountingPayload payload, CancellationToken ct);
}

public sealed record PostingPreviewLine(
    string AccountCode, string AccountName, byte Side, decimal AmountRon, string ResolvedFrom, string? Problem);

public sealed record PostingPreviewResult(bool CanPost, IReadOnlyList<PostingPreviewLine> Lines, IReadOnlyList<string> Problems);

// Folosit de C3 pentru notele de închidere și deschidere
public interface IJournalEntryWriter
{
    Task<long> PostAsync(JournalEntryDraft draft, CancellationToken ct);
}
```

`IPostingPreview` permite modulelor să arate, pe ecranul documentului, „cum se va conta” și să avertizeze înainte de validare dacă lipsește un cont (fără să blocheze documentul, decizia 4).

Comenzi interne C2: `PostEvent`, `RetryPostingQueue`, `IgnorePostingQueueItem`, `SaveManualEntry`, `ValidateManualEntry`, `ApproveManualEntry`, `ReverseEntry`, `SavePostingModelVariant`, `ReorderPostingModelVariants`, `RepostPeriod`, `SaveRecurringTemplate`, `GenerateRecurringEntries`, `SaveJournal`.

## 7. Schema SQL `fin` (partea C2)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `fin.Journal` | `Id int` | UQ `(CompanyId, Code)` | — |
| `fin.PostingModel` | `Id int` | UX `EventType` | — |
| `fin.PostingModelVariant` | `Id int` | UQ `(ModelId, Priority, ValidFrom)`; FK `ModelId` | `(ModelId, IsActive, ValidFrom)` |
| `fin.PostingModelLine` | `Id int` | UQ `(VariantId, LineNo)`; FK `VariantId`, `DefaultAccountId` | — |
| `fin.AccountKey` | `Code nvarchar(40)` | — | — |
| `fin.JournalEntry` | `Id bigint` | UX filtrat `SourceEventId WHERE SourceEventId IS NOT NULL`; UX filtrat `(CompanyId, JournalId, PeriodYear, Number) WHERE Number IS NOT NULL`; FK `JournalId`, `ReversalOfId`, `PostingModelVariantId` | `(CompanyId, AccountingDate)`; `(SourceDocumentType, SourceDocumentId)` |
| `fin.JournalLine` | `Id bigint` | FK `EntryId`, `AccountId`, `PartnerId`, `EmployeeId`, `CostCenterId`, `ProjectId` | `(AccountId, EntryId)`; columnstore nonclustered pe `(CompanyId, PeriodYear, PeriodMonth, AccountId, PartnerId, CostCenterId, DebitRon, CreditRon)` pentru balanțe și fișe (`SQL-16`) |
| `fin.JournalEntryHistory` | `Id bigint` | FK `EntryId` | `EntryId` |
| `fin.PostingQueue` | `Id bigint` | UQ `EventId`; FK `CompanyId` | `(CompanyId, Status, Reason)` |
| `fin.RecurringEntryTemplate`, `fin.RecurringEntryTemplateLine` | `Id int` | FK `JournalId`, `AccountId` | — |

`CompanyId`, `PeriodYear`, `PeriodMonth` se repetă pe `JournalLine` (denormalizare scrisă: abatere de la `SQL-05`, motiv: balanța și SAF-T citesc liniile fără join pe antet, pe zeci de milioane de rânduri; permanentă).

Exemplu de tabel complet:

```sql
CREATE TABLE fin.JournalLine (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalLine PRIMARY KEY,
    EntryId          bigint             NOT NULL CONSTRAINT FK_JournalLine_JournalEntry REFERENCES fin.JournalEntry(Id),
    CompanyId        int                NOT NULL,
    PeriodYear       smallint           NOT NULL,
    PeriodMonth      tinyint            NOT NULL,
    LineNo           int                NOT NULL,
    AccountId        int                NOT NULL CONSTRAINT FK_JournalLine_GlAccount REFERENCES fin.GlAccount(Id),
    Side             tinyint            NOT NULL,          -- 1 Debit, 2 Credit
    AmountRon        decimal(19,2)      NOT NULL,          -- negativă la storno în roșu
    DebitRon         AS (CASE WHEN Side = 1 THEN AmountRon ELSE 0 END) PERSISTED,
    CreditRon        AS (CASE WHEN Side = 2 THEN AmountRon ELSE 0 END) PERSISTED,
    CurrencyCode     char(3)            NULL CONSTRAINT FK_JournalLine_Currency REFERENCES core.Currency(Code),
    AmountCurrency   decimal(19,2)      NULL,
    PartnerId        int                NULL CONSTRAINT FK_JournalLine_Partner REFERENCES core.Partner(Id),
    EmployeeId       int                NULL CONSTRAINT FK_JournalLine_Employee REFERENCES core.Employee(Id),
    CostCenterId     int                NULL CONSTRAINT FK_JournalLine_CostCenter REFERENCES core.DimensionValue(Id),
    ProjectId        int                NULL CONSTRAINT FK_JournalLine_Project REFERENCES core.DimensionValue(Id),
    WarehouseId      int                NULL CONSTRAINT FK_JournalLine_Warehouse REFERENCES core.DimensionValue(Id),
    SiteId           int                NULL CONSTRAINT FK_JournalLine_Site REFERENCES core.DimensionValue(Id),
    ItemId           int                NULL CONSTRAINT FK_JournalLine_Item REFERENCES core.Item(Id),
    VatRateCode      nvarchar(10)       NULL,
    VatCategory      nvarchar(4)        NULL,
    Description      nvarchar(400)      NULL,
    ResolvedFrom     tinyint            NULL,              -- 1 Override, 2 Articol, 3 Grupă, 4 Tip articol, 5 Partener, 6 Trezorerie, 7 Tip operațiune, 8 Rol, 9 Implicit
    SourceLineRef    nvarchar(40)       NULL,
    CreatedAt        datetime2(3)       NOT NULL CONSTRAINT DF_JournalLine_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT UQ_JournalLine_Entry_LineNo UNIQUE (EntryId, LineNo),
    CONSTRAINT CK_JournalLine_Side CHECK (Side IN (1, 2)),
    CONSTRAINT CK_JournalLine_Amount CHECK (AmountRon <> 0),
    CONSTRAINT CK_JournalLine_Currency CHECK ((CurrencyCode IS NULL AND AmountCurrency IS NULL) OR (CurrencyCode IS NOT NULL AND AmountCurrency IS NOT NULL))
);
```

Validarea unei note (`fin.usp_JournalEntry_Insert`) verifică pe set (`SP-08`) echilibrul, conturile (frunză, active, permise firmei, la dată), regulile de dimensiuni și de partener din C1 și perioada; aruncă la prima eroare cu codul din plaja C2.

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `fin.usp_PostingModel_LoadForEvent` | Modelul, varianta și liniile la dată | Nu |
| `fin.usp_AccountResolution_LoadContext` | Conturile articolelor, grupelor, partenerilor, rolurilor pentru o notă (un singur apel) | Nu |
| `fin.usp_JournalEntry_Insert` | Scrie nota validată și liniile (TVP), verificările, numărul | Nu |
| `fin.usp_JournalEntry_Reverse` | Storno în roșu sau în negru | Nu |
| `fin.usp_JournalEntry_Repost` | Recontarea unei note într-o perioadă deschisă, cu istoric | Nu |
| `fin.usp_PostingQueue_Upsert`, `_ListPending`, `_MarkResolved` | Coada | Nu |
| `fin.usp_JournalEntry_Search` | Lista notelor cu filtre, paginată (`SP-12`) | Nu |

Coduri de eroare (plaja C2 `51840–51899`, prefix API `FIN_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 51841 | `FIN_ENTRY_NOT_FOUND` | 404 | Notă inexistentă |
| 51842 | `FIN_CONCURRENCY_CONFLICT` | 409 | `RowVersion` diferit (`SP-18`) |
| 51843 | `FIN_ENTRY_UNBALANCED` | 422 | Total debit diferit de total credit |
| 51844 | `FIN_ENTRY_ACCOUNT_INVALID` | 422 | Cont care nu e frunză, inactiv sau nepermis firmei la dată |
| 51845 | `FIN_ENTRY_DIMENSION_MISSING` | 422 | Dimensiune obligatorie lipsă sau dimensiune interzisă completată |
| 51846 | `FIN_ENTRY_PARTY_MISSING` | 422 | Partener sau angajat cerut de cont |
| 51847 | `FIN_ENTRY_CURRENCY_NOT_ALLOWED` | 422 | Valută pe un cont doar în lei sau monedă diferită de cea a contului |
| 51848 | `FIN_PERIOD_CLOSED` | 409 | Data contabilă într-o perioadă închisă |
| 51849 | `FIN_ENTRY_ALREADY_POSTED` | 409 | Eveniment deja contat |
| 51850 | `FIN_ENTRY_NOT_DRAFT` | 409 | Modificarea unei note validate |
| 51851 | `FIN_ENTRY_CONTROLLED_ACCOUNT` | 403 | Notă manuală pe un cont ținut de module, fără drept |
| 51852 | `FIN_ENTRY_AUTOMATIC_REVERSE_FORBIDDEN` | 403 | Storno manual al unei note automate fără drept |
| 51853 | `FIN_ENTRY_FOUR_EYES` | 403 | Autorul își validează nota manuală când patru ochi e pornit |
| 51854 | `FIN_REPOST_PERIOD_CLOSED` | 409 | Recontare cerută într-o perioadă închisă |
| 51855 | `FIN_POSTING_MODEL_MISSING` | 409 | Tip de eveniment fără model activ |
| 51856 | `FIN_POSTING_ACCOUNT_NOT_RESOLVED` | 409 | Lanțul cheii nu a dat niciun cont |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterile:

- Abatere de la `SQL-05` pentru `fin.JournalLine` (`CompanyId`, `PeriodYear`, `PeriodMonth` repetate): explicată mai sus. Permanentă.
- Abatere de la `SQL-19` și `SQL-11` pentru `fin.JournalLine`: fără `ModifiedAt`, `ModifiedBy`, `RowVersion`. Motiv: liniile unei note validate nu se modifică; recontarea rescrie nota întreagă, cu istoric în `fin.JournalEntryHistory`. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Fin.JournalEntry.View` | Note, registrul jurnal, sursa fiecărei note |
| `Fin.ManualEntry.Create`, `.Validate`, `.Approve` | Note manuale, validare, aprobare (patru ochi) |
| `Fin.ManualEntry.ControlledAccounts` | Note manuale pe conturile ținute de module |
| `Fin.JournalEntry.Reverse`, `.ReverseAutomatic` | Storno manual, storno pe note automate (auditat) |
| `Fin.PostingModel.View`, `.Manage` | Modele și variante |
| `Fin.Posting.Retry`, `.Ignore` | Coada de contare |
| `Fin.Posting.Repost` | Recontarea unei perioade deschise |

Roluri livrate: Contabil șef (toate), Contabil (`View`, note manuale, `Retry`), Auditor (`View`, `Fin.PostingModel.View`), Director financiar (`View`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru C2 |
| --- | --- |
| Legea contabilității 82/1991 și OMFP 2634/2015 (documente financiar-contabile) | Orice notă are document justificativ (sursa sau atașamentul); registrul jurnal cronologic, cu număr și dată; notele validate nu se modifică |
| OMFP 1802/2014 | Contările standard livrate în modele (3.2); stornarea în roșu |
| Codul fiscal (TVA) | Conturile de TVA pe categorii (`4426`, `4427`, `4428`), taxarea inversă și TVA la încasare în variante distincte; categoria TVA pe linie, pentru C4 |
| SAF-T (D406) | Liniile au partenerul, cota de TVA, documentul sursă și numărul notei, cerute de secțiunea GeneralLedgerEntries |
| Pistă de audit | Versiunea de model folosită, sursa fiecărui cont (`ResolvedFrom`), istoricul recontărilor, coada cu motivele |

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Registrul jurnal | Notele în ordine cronologică, pe jurnal și perioadă, în format „cont = cont” | Contabil, Auditor |
| Note pe document | Pentru un document sursă: toate notele lui (factura, încasarea, diferențele de curs, storno) | Contabil |
| Documente necontate | Coada, pe motiv și vechime | Contabil șef |
| Cum se contează | Pe eveniment: varianta, liniile, cheile și conturile rezultate pentru firmă (documentație pentru auditor) | Contabil șef, Auditor |
| Recontări | Ce note au fost refăcute, când, de cine, diferențele | Auditor |

Fișa de cont, cartea mare și balanța sunt în C5.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/c2-note-contabile/](ecrane/c2-note-contabile/), în meniul Finanțe și contabilitate › Contabilitate:

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Note contabile | Lista notelor (jurnal, număr, dată, sursă, total) cu filtre; fișa notei: liniile cu sursa contului, documentul sursă, planurile alternative, istoric | `c2-01-note-contabile` |
| Notă manuală | Editor de linii cu verificarea echilibrului și a dimensiunilor în timp real, justificativ, efectul în planurile alternative | `c2-02-nota-manuala` |
| Modele de contare | Evenimentele cu variantele lor; liniile variantei (componentă, parte, cheie, grupare); simulare pe un document real | `c2-03-modele-de-contare` |
| De contat | Coada de contare grupată pe motiv, cu lanțul încercat și linkul spre configurarea care lipsește | `c2-04-de-contat` |
| Conturi implicite | Rolurile conturilor pe firmă, cu condițiile lor (`401.01` interni, `401.02` UE...) și conturile pe grupe de articole | `c2-05-conturi-implicite` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.02-C2-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| C2-A1 | Factura FV EXD 10241 (12.400 + 2.604 TVA) produce `4111.01` = `707` + `4427`, cu partenerul pe `4111.01` și centrul de cost pe `707` | Test de integrare |
| C2-A2 | Aceeași factură cu TVA la încasare folosește `4428`; încasarea ei pe jumătate produce `4428` = `4427` pentru jumătate din TVA | Test de integrare (cu F3) |
| C2-A3 | Un furnizor UE ajunge automat pe `401.02` și un furnizor intern pe `401.01`, fără cont pe partener | Test de integrare |
| C2-A4 | Același eveniment primit de două ori produce o singură notă | Test de integrare (idempotență) |
| C2-A5 | Un articol fără cont de venit duce evenimentul în coadă cu motivul și lanțul încercat; după setarea contului pe grupă, nota se creează automat | Test de integrare |
| C2-A6 | Stornarea unei facturi produce nota cu aceleași conturi și sume negative, chiar dacă modelul s-a schimbat între timp | Test de integrare |
| C2-A7 | Nicio notă validată nu are total debit diferit de total credit (verificare pe toată baza după fiecare test de integrare) | Test de proprietate |
| C2-A8 | O notă manuală pe `628` fără centru de cost e refuzată (`51845`); pe `401.01` fără drept e refuzată (`51851`) | Test de integrare |
| C2-A9 | Recontarea unei luni deschise după schimbarea contului pe o grupă rescrie notele afectate, păstrează numerele și versiunea veche în istoric; într-o lună închisă e refuzată | Test de integrare |
| C2-A10 | Simularea (`IPostingPreview`) dă exact liniile pe care le scrie contarea reală pentru același payload | Test de proprietate |
| C2-A11 | Contarea a 100.000 de evenimente (facturi cu 10 linii) se face în sub 10 minute, cu maparea în 2 planuri alternative | Test de performanță |
| C2-A12 | Toate procedurile C2 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile C2-A1, C2-A4, C2-A6 și C2-A7 sunt critice: orice eroare aici ajunge direct în balanță și în declarații.

## Decizii de validare

Toate deciziile au fost confirmate de Aurelian pe 15.11.2026:

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Stornarea | În roșu (sume negative pe aceeași parte), cu setare pentru stornarea în negru | 2 (regula 5), 4.2 | Decis de Aurelian (15.11.2026) |
| 2 | Corecția după schimbarea modelului | Recontare automată doar în lunile deschise, cu istoricul versiunii vechi; în lunile închise, notă de reclasificare în luna curentă | 4.6 | Decis de Aurelian (15.11.2026) |
| 3 | Document cu dată într-o lună închisă | Rămâne în coadă; nu se mută singur în luna deschisă (se redeschide luna sau se face corecție la sursă) | 2 (regula 6), 4.4 | Decis de Aurelian (15.11.2026): luna se redeschide și documentul se contează |
| 4 | Lipsa contului la validarea documentului | Documentul se validează, evenimentul intră în coada „De contat”; ecranul documentului avertizează înainte (simulare), dar nu blochează | 3.7, 6 | Decis de Aurelian (15.11.2026) |
| 5 | Note manuale pe conturile ținute de module (`401*`, `4111*`, `371`, `5121*`...) | Interzise fără un drept separat, ca soldurile să rămână egale cu evidența operativă | 3.8 | Decis de Aurelian (15.11.2026) |
| 6 | Patru ochi pe notele manuale | Disponibil, implicit oprit | 3.8 | Decis de Aurelian (15.11.2026): rămâne oprit |

## Note de implementare (24.11.2026)

Ordinea de implementare e cea din [PLAN-CONTABILITATE.md](PLAN-CONTABILITATE.md).

| Pas | Ce s-a făcut | Abateri și completări față de specificație |
| --- | --- | --- |
| C2.1 | Migrarea `2026.11.058` (`fin.Journal`, `fin.JournalSequence`, `fin.JournalEntry`, `fin.JournalLine`); seed-ul `10_Journals` (VZ, AC, TR, ST, SA, MF, VL, DV, IN și DC pentru decontări); funcția `fin.ufn_GlAccount_DimensionModes`; procedurile `fin.usp_JournalEntry_SaveDraft`, `_Check`, `_Validate`, `_Reverse`, `_DeleteDraft`, `_Get`, `_Search`, `_Header`, `_ControlledAccounts`, `fin.usp_JournalSequence_Next`, `fin.usp_Journal_List`; evenimentul `NotaContabilaValidata` (`Erp.Finance.Contracts.Accounting`); comenzile `SaveManualEntry`, `ValidateManualEntry`, `DeleteManualEntry`, `ReverseJournalEntry` și interogările `GetJournals`, `GetJournalEntry`, `SearchJournalEntries`; 12 permisiuni C2; API `/api/finance/journals`, `/journal-entries`; testele C2-A7, A8 | **Numerotarea** notelor e o secvență proprie pe firmă, jurnal și an (`fin.JournalSequence`, cu blocare pe rând, fără goluri), afișată „jurnal + 5 cifre” (`DV 00001`), nu o serie din Nucleu: notele sunt un registru intern, iar contarea automată nu trebuie să se blocheze din lipsa unei serii configurate. Jurnalele livrate sunt comune instalării (`CompanyId NULL`), plus `DC` pentru decontări. Coloana de ordine a liniei e `LineNumber` (`LineNo` e cuvânt rezervat în SQL Server). Verificările din `fin.usp_JournalEntry_Check` (echilibru, cont frunză activ al firmei în vigoare, dimensiunile obligatorii și interzise moștenite, partener sau angajat, monedă) rulează și pentru notele automate (C2.3). Perioada se verifică în aceeași tranzacție: notele se scriu în lunile deschise și închise operativ, nu în cele închise (51848). Patru ochi (`Fin.ManualEntry.FourEyes`) și stornarea în negru (`Fin.Posting.ReversalStyle = Black`) sunt setări de firmă, implicit oprite. Contrângerea `CK_JournalEntry_Validated` ține echilibrul notelor validate (C2-A7). Indexul columnstore pe linii vine cu testele de performanță |
| C2.2 + C2.3 | Motorul pur `PostingEngine` (`Erp.Finance.Domain.Accounting`: varianta după flaguri, tipul partenerului și document; lanțul cheilor; gruparea; rotunjirea pe `RoundingGain` / `RoundingLoss` până la `Fin.Posting.MaxRoundingDiff`, implicit 0,05); migrarea `2026.11.059` (`fin.AccountKey`, `PostingModel`, `PostingModelVariant`, `PostingModelLine`, `PostingQueue`); seed-ul `11_PostingModels` (16 chei, 9 modele); procedurile `fin.usp_PostingModel_Load`, `fin.usp_AccountResolution_LoadContext`, `fin.usp_JournalEntry_Insert`, `fin.usp_JournalEntry_FirstError`, `fin.usp_PostingQueue_Upsert`, `_List`, `_Ignore`; contractul `AccountingPayload` v1 și `IAccountable` pe facturi; `PostingService`, `PostingAdapters`, consumatorul `PostEvents` în Worker; testele C2-A1, A2, A3, A4 și coada (motivele 1, 3, 4) | **F1–F4 nu poartă bloc de contare**: un adaptor traduce fiecare eveniment în componente (mai puține schimbări în contracte deja folosite). Facturile modulelor externe pot trimite `Accounting`; lipsa lui, se contează pe totaluri (Net = Total − TVA). **Nota respinsă nu anulează tranzacția consumatorului**: `fin.usp_JournalEntry_Insert` nu aruncă pentru motivele de coadă, lucrează pe un punct de salvare și întoarce codul (51843–51849), iar evenimentul intră în coadă în aceeași tranzacție; verificările s-au mutat în `fin.usp_JournalEntry_FirstError`, iar `fin.usp_JournalEntry_Check` (nota manuală) doar aruncă prima eroare. Coduri → motive: 51844/51847 cont invalid, 51845/51846 dimensiune lipsă, 51848 lună închisă, 51843 dezechilibru; 51849 (eveniment deja contat) e reușită. **Centrul de cost implicit** `Fin.Posting.DefaultCostCenterId` completează liniile claselor 6 și 7 fără centru. `AvansDecontareAcordat` și `AlocareInregistrata` nu produc notă (acoperite de plată și de efecte). Amânate (RAMAS-DE-FACUT 1.15–1.18): reevaluarea valutară, efectele comerciale, stornarea documentelor de trezorerie, diferențele de curs fără partener, liniile de decont plătite pe factură de furnizor; simularea din ecran vine cu C2.5 |
| C2.4a | Migrarea `2026.11.060` (`fin.JournalEntrySource` cu contractul fiecărei note automate, `fin.JournalEntryHistory`); procedurile `fin.usp_JournalEntry_ReverseFromEvent`, `_RepostCandidates`, `_Repost`, `fin.usp_PostingQueue_Due`, `_PendingFor`; evenimentele `ContareEsuata`, `NotaContabilaRecontata`; comenzile `RetryPostingQueue`, `IgnorePostingQueueItem`, `RepostEntries` și interogările `GetPostingQueue`, `PreviewRepost`; jobul `PostingQueueRetry` (la 15 minute) și consumatorul `RetryQueueOnReopen`; API `/api/finance/posting-queue`, `/repost`; testele C2-A5, A6, A9 | **Stornarea la sursă** (`IsReversal` cu `ReversedDocumentId`) repetă nota automată a documentului original (roșu sau negru după `Fin.Posting.ReversalStyle`), nu recontează cu modelul de azi; dacă originalul așteaptă în coadă, storno-ul intră și el cu același motiv; dacă originalul nu e nici contat, nici în coadă, se contează cu modelul (sume negative). **Reîncercarea automată** e un job la 15 minute pentru rândurile încercate de peste 10 minute, plus redeschiderea lunii pentru motivul 4 — abatere de la 4.4 pasul 3 („după salvarea configurării”): jobul acoperă orice schimbare fără ca fiecare ecran să cunoască coada. **Recontarea** rescrie doar notele care ies altfel azi, în lunile care nu sunt închise (deschise sau închise operativ), din contractul păstrat în `fin.JournalEntrySource`; notele scrise înainte de C2.4 nu au contract și nu se recontează. Istoricul păstrează liniile vechi ca JSON |
| C2.4b | Migrarea `2026.11.061` (`fin.RecurringEntryTemplate`); procedurile `fin.usp_RecurringEntryTemplate_Save`, `_List`, `_SetActive`, `_Advance`, `_Companies`; comenzile `SaveRecurringTemplate`, `SetRecurringTemplateActive`, `GenerateRecurringEntries` și interogarea `GetRecurringTemplates`; jobul `RecurringEntryGeneration` (zilnic, 00:40); API `/api/finance/recurring-entries`; testul notelor recurente | Șablonul ține liniile ca JSON (aceeași formă ca ciorna), fiindcă se citesc și se copiază întregi. Generarea face câte o ciornă pentru fiecare perioadă rămasă în urmă (cel mult 24 pe rulare), cu justificativul „Șablon #Id”; ultima zi a lunii rămâne ultima zi. Ciornele se validează de om. Sumele calculate dintr-un sold („1/12 din `471`”) vin după balanță (RAMAS 1.19) |
| C2.5a | Ecranele Note contabile (macheta c2-01: lista lunii cu filtrele jurnal, tip și căutare, fișa notei cu formula „4111 = %”, liniile și sursa contului, validarea și ștergerea ciornelor, stornarea), Notă manuală (c2-02: editorul cu echilibrul și conturile necunoscute verificate pe loc, centrul de cost și proiectul pe linie, salvarea ca ciornă și validarea) și De contat (c2-04: coada grupată pe motiv, cu lanțul încercat, reîncercarea unui rând sau a tuturor, ignorarea cu motiv); meniul Finanțe › Contabilitate › Note contabile, De contat | Din machete lipsesc deocamdată: registrul jurnal (cu rapoartele C5), tab-urile Document sursă, Planuri alternative și Note legate ale fișei, partenerul pe linia notei manuale (selectorul de parteneri), efectul în planurile alternative în editor și, în De contat, setarea contului direct din rând („Salvează contul și reîncearcă”): utilizatorul setează contul din Plan de conturi / Conturi implicite, iar reîncercarea automată îl preia (RAMAS 1.22) |

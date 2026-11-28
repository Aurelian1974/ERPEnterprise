# ERP · 02.04 Achiziții · A5 Facturi de furnizor: inbox e-Factura, potrivirea comandă–NIR–factură, servicii pe centre de cost, avansuri, storno

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**, cu ecranele (inclusiv fișa facturii `a5-06`) și toate cele 14 decizii. A cincea din cele 7 părți ale modulului 02.04 Achiziții. Se sprijină pe [A1 Configurare](A1-CONFIGURARE.md), [A2 Necesar și oferte](A2-NECESAR-SI-OFERTE.md) (validate pe 26.11.2026), [A3 Comenzi](A3-COMENZI.md) și [A4 Recepție](A4-RECEPTIE.md) (validate pe 27.11.2026).

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`; partea A5 folosește `53400–53499`. Această parte acoperă **datoria față de furnizor**: primirea facturilor din SPV (e-Factura) într-un inbox, potrivirea liniilor cu articolele și cu documentele noastre, înregistrarea facturii (din NIR-uri, din comandă, fără sursă, cu recepție), potrivirea comandă–NIR–factură cu toleranțe și blocarea la plată, aprobarea facturilor fără comandă, repartizarea serviciilor pe centre de cost, TVA-ul (cote, taxare inversă internă, TVA la încasare, deducere parțială), facturile și plățile de avans, proformele, notele de credit și stornările, și contarea prin contul `408` hotărâtă în A4. Importul, achizițiile intracomunitare de bunuri, DVI-ul și repartizarea costurilor de achiziție sunt în A6. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.2 Parteneri, §3.6 Tranzacții și legături), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F3 partide și blocarea la plată, F4 curs, C2 contare, C3 închidere, C4 TVA), [02.05 Stocuri](../05-stocuri/README.md) (S3 corecția de cost `CA`). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile facturii** sunt livrate de A1 §3.2: `FF-NIR` (din NIR-uri), `FF-CF` (din comandă: servicii, avansuri, factura înaintea mărfii), `FF-REC` (factura cu recepție, creează și NIR-ul, D2), `FF-EF` (din e-Factura primită), `FF-MAN` (fără sursă: utilități, chirii); stornările și notele de credit: `FS-FF` (din factură), `FS-RT` (din retur), `FS-MAN` (reducere comercială). Grupurile de consum: `INVOICE` (factura consumă comanda sau NIR-ul), `CREDIT` (nota de credit consumă factura sau returul). „De facturat” se calculează din legături, nu se stochează (A1 decizia 5).
- **e-Factura**: comunicarea cu SPV (descărcarea mesajelor, XML-ul UBL, registrele ANAF de TVA la încasare și contribuabili inactivi) o face modulul Integrări (02.14). A5 primește facturile ca evenimente și le pune în inbox. Potrivirea liniilor cu variantele noastre folosește cascada din A1 §4.3 și catalogul furnizorului.
- **Partida furnizorului** (datoria, scadențele, plata, blocarea la plată, `EInvoiceStatus`) e a Finanțelor (F3 §3.1), creată din evenimentul `FacturaFurnizorValidata`, cu conținutul minim cerut de F3 §4.1. A5 cere blocarea și deblocarea prin `SetOpenItemPaymentBlock` (F3).
- **Contarea** e a C2, din blocul `Accounting` al evenimentului. Regula hotărâtă în A4 (decizia 9): liniile care intră în stoc trec prin contul `408` (NIR `371` = `408`, factura `408` = `401`); TVA-ul de pe NIR stă pe `4428` până la factură.
- **Corecția de cost** pentru diferențele de preț acceptate și pentru reducerile ulterioare se face prin `IStockLedger.AdjustInboundCostAsync` (Stocuri S3 §3.3, documentul `CA`); NIR-ul nu se modifică.
- Rotunjirea doar prin `IPrecisionService` (P7, `NET-14`). Cantități și prețuri `decimal(19,6)`, valori `decimal(19,4)` (`SQL-07`).
- **Ce NU face A5:** nu plătește (F1, F2, F3), nu calculează diferențele de curs la plată (F4), nu face decontul de TVA (C4), nu înregistrează DVI-ul și nu repartizează costurile de achiziție pe NIR-uri (A6), nu vorbește direct cu SPV (02.14).

## 1. Scop și perimetru

| Zonă | Ce face A5 | Ce NU face (și cine face) |
| --- | --- | --- |
| Inbox e-Factura | Facturile primite din SPV: identificarea furnizorului, potrivirea liniilor, propunerea documentelor sursă, crearea facturii, legarea de o factură deja înregistrată, dublurile | Descărcarea din SPV, validarea semnăturii, vizualizarea PDF (02.14) |
| Înregistrare | Factura din NIR-uri, din comandă, cu recepție sau fără sursă; numărul furnizorului, datele, scadențele, moneda și cursul | Cursul BNR (F4) |
| Potrivire | Comandă–NIR–factură pe linii: cantitate, preț, valoare; toleranțe; decizia pe diferențe; blocarea la plată | Plata (F2), alocarea (F3) |
| Aprobare | Facturile fără comandă și cele cu diferențe peste toleranță, pe regulile din A2 | — |
| Servicii | Repartizarea pe centre de cost și proiecte, cu șabloane | Bugetele (02.11, viitor) |
| TVA | Cota pe linie, tipul de operațiune fiscală (C4), taxarea inversă internă, TVA la încasare, deducerea parțială, rotunjirea pe cote | Registrul de TVA și declarațiile (C4) |
| Avansuri | Proforma, factura de avans, regularizarea avansului pe factura finală | Plata avansului (F2) |
| Storno și note de credit | Nota de credit din factură, din retur, reducerea comercială; stornarea înregistrării greșite | — |
| Contare | Blocul contabil al facturii; reclasificarea lunară a mărfurilor facturate și nerecepționate | Notele (C2), închiderea (C3) |

## 2. Model de domeniu

```
 EInvoiceInboxItem                               mesajul SPV: index, CUI furnizor, număr, dată, total, XML, stare
   └─ EInvoiceInboxLine × N                     linia UBL: coduri, GTIN, denumire, UM, cantitate, preț → varianta potrivită, pasul, încrederea
        → creează SupplierInvoice (FF-EF) sau se leagă de una înregistrată

 SupplierInvoice (FF)                            DocKind: 1 factură · 2 factură de avans · 3 notă de credit / storno
   ├─ SupplierInvoiceLine × N                   variantă (marfă sau serviciu), UM, cantitate, preț, cotă și tip de operațiune TVA, % deductibil
   │    ├─ SupplierInvoiceLineSplit × N         centru de cost, proiect, procent sau sumă (servicii)
   │    └─ SupplierInvoiceMatch × 0..N          linia comenzii și linia NIR-ului potrivite, diferențele, decizia
   ├─ SupplierInvoiceVat × N                    totalurile pe cotă (bază, TVA calculat, TVA de pe factură)
   ├─ SupplierInvoiceSchedule × N               scadențele (rate), trimise la F3
   ├─ PurchaseApproval × N (jurnal, comun A2/A3)
   └─ legături (Nucleu core.DocumentLink)
        ← INVOICE din linia de comandă (FF-CF) sau de NIR (FF-NIR, FF-EF)   ← CREDIT spre factura sau returul stornat
        → RECEIVE spre NIR-ul generat (FF-REC, NIR-FF)   → LANDED (A6)

 SupplierProforma                                proforma furnizorului: sumă, scadență, comanda; bază pentru plata avansului (F2)
```

Reguli de business ale modelului:

1. **Factura și nota de credit stau în același tabel** (`purch.SupplierInvoice`, `DocKind`), cu tipurile de document diferite din A1 (`SupplierInvoice`, `SupplierCreditNote`). Liniile, TVA-ul, scadențele și contarea au aceeași structură; nota de credit are cantități sau valori negative pe linie și `ReversedInvoiceId` pe antet când stornează o factură anume.
2. **O factură are un singur furnizor, o singură monedă și un singur număr al furnizorului.** Numărul furnizorului e unic pe furnizor și an fiscal al facturii (`53401`), comparat după cheia normalizată (fără spații și separatori, majuscule). Numărul nostru de înregistrare vine din seria `FF` la validare.
3. **Fiecare linie are un fel** (`LineKind`): 1 marfă (articol stocabil, trece prin `408`), 2 serviciu sau cheltuială (articol nestocabil, contat pe cheltuială sau imobilizare), 3 cost de achiziție (serviciu care se adaugă la costul mărfii, prin A6), 4 regularizare de avans, 5 garanție SGR și alte sume fără TVA în afara costului. Felul vine din articol și din tranzacție și decide contarea (4.10).
4. **Potrivirea nu modifică documentele potrivite** (decizia 3). Factura spune ce cere furnizorul; NIR-ul spune ce a intrat. Diferențele se decid explicit pe factură: acceptate (corecție de cost sau cheltuială), contestate (se cere notă de credit), sau corectate prin alt document (NIR suplimentar, retur).
5. **Factura validată nu se modifică** (P3). O greșeală de înregistrare se corectează prin stornarea internă a înregistrării și o înregistrare nouă (4.11); o corectură a furnizorului vine ca notă de credit (`FS-FF`).
6. **Nimic nu se șterge după numerotare** (`SQL-20`). Ciornele se anulează; elementele din inbox nu se șterg niciodată, doar se marchează „ignorat”, cu motiv.

## 3. Entități, stări și setări

Convențiile comune tabelelor `purch` sunt cele din A1 §3. Serii de numere: `FF` (număr de înregistrare pentru factură și notă de credit), `PF` (proformă înregistrată).

### 3.1 Inbox-ul e-Factura (`EInvoiceInboxItem`, `EInvoiceInboxLine`)

| Câmp | Regulă |
| --- | --- |
| `SpvIndexNo` | Indexul mesajului din SPV; unic pe firmă (`53402` la dublură, ignorată tăcut la re-descărcare) |
| `SupplierFiscalCode`, `PartnerId` | CUI-ul vânzătorului din XML; partenerul găsit după CUI (Nucleu); gol dacă furnizorul nu există încă |
| `InvoiceNo`, `IssueDate`, `DueDate`, `CurrencyCode`, `NetAmount`, `VatAmount`, `TotalAmount`, `InvoiceTypeCode` | Din UBL (`380` factură, `381` notă de credit, `386` factură de avans, `384` factură corectată) |
| `OrderReference`, `DespatchReference` | `cac:OrderReference/cbc:ID` și `cac:DespatchDocumentReference` din UBL, folosite la propunerea surselor (4.2) |
| `XmlFileId`, `PdfFileId` | Fișierele stocate (Nucleu); PDF-ul vizual îl generează 02.14 |
| `Status` | 1 Nou · 2 Potrivit parțial · 3 Gata (toate liniile potrivite sigur, sursele găsite) · 4 Înregistrat (are `InvoiceId`) · 8 Ignorat (cu motiv: dublură, nu ne privește, de test) · 9 Respins comercial (vezi 4.1) |
| `InvoiceId` | Factura creată sau legată |
| `ReceivedAt`, `AssignedUserId` | Momentul descărcării; contabilul de furnizori responsabil (după furnizor sau după firmă) |

Linia: `LineNo`, `SellerItemCode`, `BuyerItemCode`, `Gtin`, `Name`, `UneceCode`, `Qty`, `UnitPrice`, `NetAmount`, `VatCategory` (S, AE, K, Z, E, O), `VatRatePct`, apoi rezultatul potrivirii: `ItemVariantId`, `UomId`, `MatchStep` (1–7 din A1 §4.3), `Confidence` (1 sigură, 2 probabilă, 0 fără), `SourceLineType`, `SourceLineId` (linia de NIR sau de comandă propusă).

### 3.2 Factura (`SupplierInvoice`)

| Câmp | Tip | Regulă |
| --- | --- | --- |
| `DocKind` | `tinyint` | 1 Factură · 2 Factură de avans · 3 Notă de credit / storno |
| `Number` | `nvarchar(20) NULL` | Seria `FF`, la validare |
| `SupplierDocNo`, `SupplierDocKey` | text, calculat persistat | Numărul furnizorului și cheia normalizată (regula 2) |
| `DocumentDate` | `date` | Data facturii (emiterii) |
| `ReceivedDate` | `date` | Data primirii (SPV sau registratură); informativă, pentru termenul de înregistrare |
| `AccountingDate` | `date` | Data contabilă: implicit data facturii, dacă luna e deschisă; altfel prima zi a primei luni deschise, cu avertizare (decizia 10) |
| `PartnerId` | `int` | Furnizor activ; furnizor inactiv ANAF la data facturii → avertizare și TVA nedeductibil propus (9) |
| `CurrencyCode`, `ExchangeRate`, `ExchangeRateDate` | | Cursul BNR al datei facturii (F4); pentru `FF-NIR` din NIR-uri în valută diferența de curs față de NIR se tratează în 4.10 |
| `IsVatOnCollection` | `bit` | Copiat din registrul ANAF al furnizorului la data facturii (Nucleu `Partner.VatOnCollection`, istoric prin 02.14) sau din UBL (`cbc:TaxPointDate` / mențiunea „TVA la încasare”); diferența dintre cele două dă avertizare |
| `IsReverseCharge` | `bit` | Calculat: cel puțin o linie cu taxare inversă |
| `EInvoiceStatus`, `EInvoiceIndexNo`, `InboxItemId` | | `4` primită din SPV, `5` neprimită (F3 §3.8); `0` pentru furnizorii scutiți (străini, persoane fizice) |
| `ReversedInvoiceId`, `IsInternalReversal` | | Nota de credit: factura stornată. Stornarea internă (4.11): 1 |
| `MatchStatus` | `tinyint` | 0 Fără potrivire (fără surse) · 1 Potrivită · 2 Cu diferențe în toleranță · 3 Cu diferențe de decis · 4 Diferențe decise |
| `ApprovalStatus` | `tinyint` | 0 Nu e cerută · 1 În aprobare · 2 Aprobată · 3 Respinsă |
| `PaymentBlockReason` | `tinyint NULL` | 1 Diferențe nedecise · 2 Aprobare în curs · 3 Notă de credit așteptată · 4 Manual; trimis la F3 |
| `Status` | `tinyint` | 3.4 |
| `NetAmount`, `VatAmount`, `TotalAmount` și echivalentele `…Ron` | `decimal(19,4)` | Totaluri; `TotalAmount` trebuie să fie egal cu totalul de pe documentul furnizorului (`53405`) |
| `CostCenterId`, `ProjectId` | `int NULL` | Implicite pentru linii; trimise la F3 pentru rapoarte |

### 3.3 Linia (`SupplierInvoiceLine`), repartizarea și potrivirea

| Câmp | Regulă |
| --- | --- |
| `LineKind` | Regula 3 |
| `ItemVariantId`, `UomId`, `Qty`, `QtyBase`, `UnitPrice`, `DiscountPct`, `NetAmount` | Varianta e obligatorie (inclusiv pentru servicii, A3); din sursă sau din potrivirea inbox-ului |
| `VatRateId`, `VatOperationType`, `DeductiblePct` | Cota (Nucleu), tipul de operațiune fiscală din C4 §3.2 (`A-S21`, `A-S11`, `A-AE`, `A-IC-B`, `A-IC-S`, `A-IMP`, `A-NOD`), procentul deductibil (100 implicit; 50 pentru vehiculele cu folosință mixtă, Codul fiscal art. 298; 0 pentru nedeductibil). Tipul se propune după 4.7 și se schimbă doar cu dreptul `Purch.Invoice.EditVat` |
| `ExpenseAccountOverride` | Cont de cheltuială ales manual (cu drept); altfel lanțul cheii `Expense` (C2) |
| `CostCenterId`, `ProjectId` | Pe linie, când nu e repartizată |
| `NonStockAssetKind` | 0 Nu · 1 Mijloc fix (coada „Imobilizări de preluat”, M2; contare `231.PRL` = `404`) · 2 Investiție în curs (`231` = `404`); vezi precizările din 02.15 la final |

`SupplierInvoiceLineSplit`: `LineId`, `CostCenterId`, `ProjectId`, `Pct` sau `Amount` (unul dintre ele), cu Σ = 100% sau Σ = valoarea liniei (`53408`). Șabloanele de repartizare (`purch.CostSplitTemplate` + `CostSplitTemplateLine`: cod, denumire, procentele pe centre de cost și proiecte, opțional furnizorul și articolul pentru propunerea automată) se aplică la un clic.

`SupplierInvoiceMatch`: `LineId`, `OrderLineId`, `ReceiptLineId`, `MatchedQty`, `OrderPrice`, `ReceiptPrice`, `InvoicePrice`, `QtyDiff`, `PriceDiffAmountRon`, `Result` (1 OK, 2 în toleranță, 3 diferență de cantitate, 4 diferență de preț, 5 fără recepție, 6 fără comandă), `Decision` (1 Acceptat, 2 Notă de credit cerută, 3 Corectat prin alt document), `DecidedBy`, `DecidedAt`, `Reason`, `LandedCostId` (documentul `CA` creat la acceptarea unei diferențe de preț).

### 3.4 Stările facturii

| Stare | Cum se ajunge | Ce se poate face |
| --- | --- | --- |
| 1 Ciornă | Creare (manuală, generată, din inbox) | Modificare liberă; legăturile spre surse sunt „în ciornă” |
| 2 În aprobare | Trimiterea spre aprobare (4.5) | Doar decizia aprobatorilor; retragerea de către autor |
| 3 Validată | Validarea (4.8) | Decizia pe diferențe (care nu schimbă factura), notă de credit, plată (F3), stornare internă |
| 4 Stornată | Stornarea internă (4.11) | Doar citire |
| 9 Anulată | Anularea unei ciorne | Doar citire; elementul din inbox revine în „Gata” sau „Potrivit parțial” |

Factura se validează (deci datoria apare în F3 și TVA-ul în jurnal) **chiar dacă are diferențe nedecise**: furnizorul o datorăm oricum în registru, iar plata e blocată până la decizie (decizia 4). Aprobarea, când e cerută, se dă înainte de validare.

### 3.5 Setările A5 (completare la `PurchaseSettings`, A1 §3.7)

Setările existente din A1 folosite aici: `RequiresOrder`, `RequiresOrderAboveAmount`, `PriceVarianceTolerancePct` (0,5%) și `PriceVarianceToleranceAmount` (10,00 lei), `QtyVarianceTolerancePct` (0), `EInvoiceAutoMatch`, `EInvoiceAutoLearn`, `EInvoiceAutoDraft`, `AllowInvoiceWithReceipt`, `ReceiptDateRule`.

| Setare nouă | Implicit | Ce înseamnă |
| --- | --- | --- |
| `InvoiceApprovalWithoutOrderAboveRon` | 5.000 | Facturile fără comandă (`FF-MAN`, sau `FF-EF` fără sursă) peste prag, fără TVA, cer aprobare pe regulile `DocumentType = 'SupplierInvoice'` (A2 §3.2); implicit aprobatorul e responsabilul centrului de cost cu cea mai mare parte din valoare |
| `PaymentBlockOnDifferences` | 1 | Factura cu diferențe peste toleranță se blochează automat la plată până la decizie |
| `DomesticEInvoiceRequired` | 1 | Pentru furnizorii români plătitori de TVA: 0 fără verificare · 1 avertizare la validarea unei facturi fără e-Factura (`EInvoiceStatus = 5`) · 2 blocare (doar cu dreptul `Purch.Invoice.ValidateWithoutSpv`) |
| `VatRoundingToleranceRon` | 1,00 | Diferența acceptată pe o cotă între TVA-ul calculat și cel de pe factură (se preia cel de pe factură, cu diferența afișată) |
| `GoodsInTransitReclass` | 1 | La închiderea lunii, liniile de marfă facturate și nerecepționate se reclasifică pe contul de stocuri în curs de aprovizionare (4.10) |
| `InboxAssignment` | 1 | 1 după achizitorul furnizorului · 2 după contabilul de furnizori al firmei · 3 neasignat |
| `RegistrationDeadlineDays` | 5 | Elementele din inbox mai vechi de N zile lucrătoare apar ca „întârziate” și se notifică |

## 4. Fluxuri principale

### 4.1 Inbox-ul e-Factura

1. Jobul din 02.14 descarcă mesajele noi din SPV și publică `EFacturaPrimita` v1 (indexul, CUI-ul cumpărătorului, fișierul XML). A5 creează `EInvoiceInboxItem` în starea Nou, idempotent pe `SpvIndexNo`.
2. **Identificarea furnizorului** după CUI. Fără partener: elementul arată „Furnizor nou” și butonul „Creează partenerul” (datele din XML, completate din registrul ANAF prin 02.14).
3. **Potrivirea liniilor** cu cascada din A1 §4.3 (`EInvoiceAutoMatch = 1`). Fiecare linie arată pasul și încrederea.
4. **Propunerea surselor**, în ordine: NIR-urile nefacturate ale furnizorului citate în `DespatchReference` sau cu aceleași variante și cantități; comanda citată în `OrderReference`; altfel NIR-urile nefacturate ale furnizorului din ultimele 60 de zile cu aceleași variante. Propunerea e o legătură pe linie (`SourceLineId`), confirmată de om dacă nu e sigură.
5. **Factura deja înregistrată** (de pe hârtie sau PDF, cu `EInvoiceStatus = 5`): dacă există o factură validată cu același furnizor și aceeași cheie de număr, elementul se leagă de ea (nu se creează alta), factura trece în `EInvoiceStatus = 4`, iar F3 primește `FacturaPrimitaSPV` (F3 §3.8). Diferențele de total între XML și înregistrare se arată și cer o decizie (stornare și reînregistrare, dacă XML-ul e corect).
6. **Elementul „Gata”** (toate liniile sigure, surse găsite, totalurile egale): cu `EInvoiceAutoDraft = 1` factura se creează direct ca ciornă `FF-EF`; altfel contabilul o creează cu „Înregistrează”.
7. **Ignorarea**: dublură, factură care nu privește firma (alt punct de lucru cu CUI-ul nostru, gestionat în altă bază), factură de test. Motivul e obligatoriu, elementul rămâne.
8. **Respingerea comercială**: legea nu permite cumpărătorului să „respingă” o e-Factura în SPV; dacă factura e greșită, A5 o marchează „Respins comercial”, trimite furnizorului emailul de contestare (outbox Nucleu) și așteaptă nota de credit sau factura corectă. Factura greșită se înregistrează totuși, dacă furnizorul nu o stornează în aceeași lună, ca registrul de TVA să corespundă RO e-TVA (C4), cu plata blocată (decizia 2).

Exemplu (ecranul `a5-01`): pe 27.11 dimineață inbox-ul are 5 elemente noi. `IC 77301` de la Izvorul Carpaților (3.960,17 lei) e „Gata”: APA05 potrivită la pasul 3 (codul `IC-0500-PET`), APA15 la pasul 2 (GTIN), sursa NIR 4487 găsită după `DespatchReference = IC 5530`. `FA 9921` de la Furnizor Ambalaje (14.077,38 lei) e „Potrivit parțial”: liniile sunt sigure, dar valorile diferă de NIR 4490. `VE 2611-0457` de la Volta Energie SRL (7.477,80 lei) nu are sursă: e o factură de serviciu. `TL 3318` vine de la un furnizor nou, Transcarpat Logistic SRL. `OL 55120` de la Office Line e deja înregistrată de pe PDF și se leagă.

### 4.2 Factura din NIR-uri (`FF-NIR`, `FF-EF` cu sursă NIR)

1. Asistentul de generare (Nucleu) propune NIR-urile validate și nefacturate ale furnizorului (filtrul `NOT_INVOICED`, A4 §6). Se pot lua mai multe NIR-uri pe o factură.
2. Comportamentul `SupplierInvoice.FromReceipts` copiază din NIR: variantă, UM, cantitatea acceptată, prețul, reducerea, cota TVA, centru de cost, proiect. Legăturile `INVOICE` se scriu „în ciornă”.
3. Contabilul corectează cantitățile și prețurile **la ce scrie pe factura furnizorului** (din inbox, valorile vin deja din XML). Potrivirea (4.4) compară automat.
4. Se completează scadențele (din `SupplierTerms.PaymentTermDays` sau din factură), totalul de control și se validează (4.8).

### 4.3 Factura din comandă și factura cu recepție (`FF-CF`, `FF-REC`)

- **`FF-CF`** consumă grupul `INVOICE` pe linia comenzii, fără NIR. Folosită pentru servicii comandate (linia „Transport” din `CF 2297`), pentru factura de avans (4.9) și pentru marfa facturată înaintea livrării. Liniile de marfă facturate și nerecepționate rămân în `408` debitor până la NIR (A4 §4.7) și se reclasifică la sfârșitul lunii (4.10).
- **`FF-REC`** (D2, `AllowInvoiceWithReceipt = 1`) creează în aceeași tranzacție SQL factura și NIR-ul ei (`NIR-FF`), legate pe linii: liniile de marfă devin linii de NIR cu cantitatea acceptată = cantitatea facturată, în gestiunea aleasă pe factură. Regulile NIR-ului din A4 se aplică integral (loturi, carantină, termen, diferențe; dacă recepția fizică are diferențe, se face pe NIR, iar factura rămâne cu diferența de potrivire). Validarea facturii validează și NIR-ul; data NIR-ului urmează `ReceiptDateRule`. Folosită de firmele mici și de magazine, unde marfa vine cu factura și nu există aviz.

### 4.4 Potrivirea comandă–NIR–factură

Rulează la fiecare salvare a ciornei și la validare, pe fiecare linie de marfă și de cost (decizia 3):

| Verificare | Comparație | Toleranța |
| --- | --- | --- |
| Cantitate | Cantitatea facturată față de cantitatea acceptată pe NIR-urile legate (încă nefacturată) | `QtyVarianceTolerancePct` (0) |
| Preț | Prețul net facturat (după reducere, în moneda facturii) față de prețul NIR-ului; dacă NIR-ul e din comandă, și față de prețul comenzii (informativ) | Diferența pe linie, în lei, sub **oricare e mai mică** dintre `PriceVarianceTolerancePct` × valoare și `PriceVarianceToleranceAmount` |
| Fără recepție | Linie de marfă fără NIR (factura înaintea mărfii) | Nu e diferență, dacă linia vine dintr-o comandă (`FF-CF`); altfel diferență „fără recepție” |
| Fără comandă | Linie fără comandă, cu `RequiresOrder` = 1 sau 2 | Avertizare sau blocare (A1 §3.7) |
| Totaluri | Totalul facturii față de documentul furnizorului; TVA pe cotă față de TVA calculat | `VatRoundingToleranceRon` pe cotă |

Rezultatul pe linie se scrie în `SupplierInvoiceMatch`; antetul primește `MatchStatus`. Diferențele în toleranță se acceptă automat: diferența de preț devine cost (o corecție `CA` mică, grupată pe factură) sau, sub 1 leu pe factură, cheltuială de rotunjire.

**Decizia pe diferențele peste toleranță** (ecranul `a5-02`), de achizitorul furnizorului sau de contabilul de furnizori cu dreptul `Purch.Invoice.DecideDifferences`:

| Decizie | Efect |
| --- | --- |
| Acceptă diferența de preț | Se creează documentul `CA` (`InvoicePriceDifference`, S3 §3.3) cu ținta rândurile de urmărire ale NIR-ului (A4 regula 3): partea în stoc corectează costul, partea vândută merge pe `607`. Opțional, prețul se actualizează în lista furnizorului (A1 §3.6) |
| Acceptă diferența de cantitate (facturat mai mult decât recepționat) | Doar cu motiv: furnizorul nu va mai livra și nu emite notă de credit. Cantitatea în plus rămâne „facturată, nerecepționată” și se trece pe cheltuială (`6xx` = `408`, cheia `Expense` a articolului) la decizie |
| Cere notă de credit | Diferența rămâne deschisă până la `FS-FF`; factura rămâne blocată la plată pentru toată suma (decizia 4); furnizorul primește emailul cu diferențele (outbox) |
| Corectat prin alt document | Diferența se închide de un NIR suplimentar (furnizorul a livrat restul) sau de un retur; legătura se face automat când documentul apare |

Exemplu: factura `FA 9921` a Furnizor Ambalaje pentru NIR 4490 (A4 §4.5). Furnizorul facturează 200 role FOLIE23 la 38,5000 (7.700,00), deși s-au recepționat 196 și s-a reclamat restul prin `PVR 0017`, și 948 buc BANDA48 la 4,1500 (3.934,20) în loc de 4,1000 din comandă. Total 11.634,20 lei fără TVA, TVA 21% 2.443,18, total 14.077,38. Potrivirea dă: FOLIE23, diferență de cantitate 4 role (154,00 lei); BANDA48, diferență de preț 47,40 lei (1,22%, peste 0,5% și peste 10,00 lei). Factura se validează și e blocată la plată. Laura Vasile cere notă de credit pentru cele 4 role (PV-ul e deja trimis) și acceptă prețul nou la BANDA48 (lista furnizorului s-a schimbat din 25.11): se creează `CA` de 47,40 lei pe NIR 4490. Plata rămâne blocată până la nota de credit.

### 4.5 Aprobarea facturii

Se cere aprobare pentru:

- factura fără comandă peste `InvoiceApprovalWithoutOrderAboveRon` (implicit 5.000 lei fără TVA);
- factura cu diferențe acceptate peste un prag separat al regulilor (`DocumentType = 'SupplierInvoiceDifference'`, implicit nivelul 1, Șef achiziții, de la 500 lei).

Regulile și jurnalul sunt cele din A2 §3.2 (`purch.PurchaseApprovalRule`, `purch.PurchaseApproval`): niveluri cu prag, autorul nu aprobă, același om nu aprobă două niveluri. Felul aprobatorului „responsabilul centrului de cost” ia centrul de cost cu cea mai mare valoare de pe factură (după repartizare). Aprobarea se cere înaintea validării; factura „În aprobare” poate fi validată de urgență doar cu dreptul `Purch.Invoice.ValidateBeforeApproval`, caz în care rămâne blocată la plată până la aprobare.

### 4.6 Servicii și cheltuieli pe centre de cost

Exemplu (ecranul `a5-03`): factura `VE 2611-0457` de la Volta Energie SRL pentru energia electrică din noiembrie, 6.180,00 lei fără TVA, TVA 21% 1.297,80, total 7.477,80. Nu are comandă (utilitate). Carmen Nistor o creează din inbox (`FF-EF` fără sursă). Linia e pe articolul de serviciu „ENERGIE” (cheia `Expense` → `605`). Șablonul de repartizare „Energie Cluj”, propus automat pentru furnizor și articol, împarte 70% pe centrul de cost Logistică Cluj (4.326,00) și 30% pe Administrativ Cluj (1.854,00). Valoarea depășește 5.000 lei, deci factura cere aprobarea responsabilului centrului cu partea cea mai mare, Gheorghe Ilie. După aprobare se validează: `605` = `401` 6.180,00, pe cele două centre de cost, și `4426` = `401` 1.297,80.

Reguli:

- Repartizarea e pe linie, în procente sau sume; centrul de cost e obligatoriu dacă regula contului o cere (C2, dimensiuni obligatorii pe cont).
- Liniile de tip „cost de achiziție” (transport, manipulare, comision vamal) nu se repartizează pe centre de cost: ele se repartizează pe NIR-uri în A6 și trec prin `408` (4.10).
- Liniile de imobilizare (`NonStockAssetKind` 1 sau 2) trimit intrarea la Mijloace fixe (02.15) prin evenimentul facturii și se contează pe `231.PRL` sau `231` (precizările din 02.15 la final).

### 4.7 TVA-ul pe factura de furnizor

**Tipul de operațiune fiscală** se propune pe linie, în ordine:

1. Furnizorul din afara României: achiziție intracomunitară sau import (A6), după țara furnizorului și felul articolului (`A-IC-B` bunuri, `A-IC-S` servicii, `A-IMP`).
2. Linia UBL cu categoria `AE` sau articolul cu codul NC în lista de taxare inversă internă (Codul fiscal art. 331; lista e un nomenclator livrat și actualizat cu produsul): `A-AE`. TVA-ul se calculează și se înregistrează ca colectat și deductibil (`4426` = `4427`).
3. Furnizor inactiv ANAF la data facturii (art. 11 alin. 1^5): `A-NOD`, cu avertizare; contabilul poate schimba cu drept, dacă furnizorul a fost reactivat.
4. Altfel `A-S21` sau `A-S11` (cota liniei). `DeductiblePct` < 100 împarte TVA-ul în deductibil și nedeductibil (`635` sau cheltuiala liniei, după C2).

**TVA la încasare** (`IsVatOnCollection = 1`): TVA-ul facturii stă pe `4428` și devine deductibil la plată, prin rândurile `VatCashEntry` ale F3 (F3 §3.7). Pentru liniile de marfă recepționate înainte, TVA-ul de pe NIR (`4428`, cheia `VatNotDue`) rămâne pe `4428`, dar trece de pe analiticul recepțiilor pe cel al TVA-ului la încasare.

**Totalurile pe cotă** (`SupplierInvoiceVat`): baza și TVA-ul se calculează pe cotă, ca în EN 16931; TVA-ul de pe documentul furnizorului se poate introduce pe cotă, iar diferența până la `VatRoundingToleranceRon` se acceptă (TVA-ul înregistrat e cel al furnizorului). Peste toleranță: `53406`.

### 4.8 Validarea

`ValidateSupplierInvoice`, într-o singură tranzacție SQL (`NET-07`):

1. Verificări: furnizor, dublură (`53401`), total de control (`53405`), TVA pe cote (`53406`), repartizări (`53408`), aprobare (`53409`), perioadă contabilă deschisă (C3, `IAccountingPeriodGuard`), e-Factura după `DomesticEInvoiceRequired` (`53410`), comandă după `RequiresOrder` (`53411`).
2. Numărul din seria `FF`.
3. Pentru `FF-REC`: crearea și validarea NIR-ului (A4 §4.6), în aceeași tranzacție.
4. Activarea legăturilor `INVOICE` (și `CREDIT`, la notă de credit); potrivirea finală în `SupplierInvoiceMatch`; documentele `CA` pentru diferențele acceptate automat (în toleranță) prin `AdjustInboundCostAsync` cu `ValidateImmediately = 1`.
5. Evenimentul `FacturaFurnizorValidata` v1 în outbox, cu conținutul minim F3 și blocul `Accounting` (4.10). F3 creează partida și, dacă `PaymentBlockReason` e setat, o blochează la plată.
6. Elementul din inbox trece în „Înregistrat”. Istoricul stării în `core.DocumentStatusLog`.

### 4.9 Proforme, facturi de avans și regularizarea avansului

1. **Proforma** (`SupplierProforma`, seria `PF`): se înregistrează pe comandă (suma, moneda, scadența, procentul de avans din `SupplierTerms.AdvancePct`). Nu se contează. F2 o poate folosi ca sursă pentru un ordin de plată de avans; plata ajunge în F3 ca avans nealocat la furnizor (`409`).
2. **Factura de avans** (`DocKind = 2`, `FF-CF` din comandă, sau din inbox cu `InvoiceTypeCode = 386`): o linie pe articolul de serviciu „Avans” (`LineKind = 4`), cu TVA (TVA-ul avansului e deductibil la primirea facturii). Contare: `409` = `401` + `4426` = `401`. F3 alocă plata făcută pe baza proformei.
3. **Factura finală** are, pe lângă liniile de marfă, linia de **regularizare a avansului** (cantitate −1, valoarea avansului fără TVA, aceeași cotă), propusă automat din facturile de avans ale comenzii încă neregularizate. Contare: `401` = `409` (cu minus pe factură), iar TVA-ul avansului se stornează pe aceeași factură. Avansul regularizat nu poate depăși avansul facturat (`53412`).

### 4.10 Contarea facturii

| Linie | Notă | Observații |
| --- | --- | --- |
| Marfă cu NIR (`LineKind = 1`, legată de NIR) | `408` = `401` valoarea netă de pe factură; `408` = `401` și `4426` = `4428` pentru TVA-ul preluat de pe NIR; `4426` = `401` pentru TVA-ul rămas | `408` se închide pe valoarea NIR-ului; diferențele de cantitate și de preț rămân în `408` debitor până la decizie (4.4) |
| Marfă fără NIR (factura înaintea mărfii) | `408` = `401` și `4426` = `401` | NIR-ul ulterior: `371` = `408`, fără TVA (A4 §4.7) |
| Diferență de preț acceptată | `371` sau `607` = `408` | Prin evenimentul `CostStocAjustat` al documentului `CA` (S3); precizare la S3 §4.2: contrapartida diferențelor pe marfă este `408` |
| Diferență de cantitate acceptată fără notă de credit | `6xx` = `408` | Cheia `Expense` a articolului |
| Serviciu sau cheltuială | `6xx` (cheia `Expense`, pe centrele de cost din repartizare) + `4426` = `401` | `DeductiblePct` < 100: partea nedeductibilă pe cheltuială |
| Cost de achiziție (`LineKind = 3`) | `408` = `401` + `4426` = `401` | `CA` din A6: `371` / `607` = `408` |
| Avans și regularizare | `409` = `401` · `401` = `409` | 4.9 |
| Taxare inversă internă | rândul de mai sus fără TVA către furnizor; `4426` = `4427` | Tipul `A-AE` (C4) |
| TVA la încasare | `4428` (cheia `VatPending`) în loc de `4426` | Mutat pe `4426` la plată (F3) |
| Garanție SGR (`LineKind = 5`) | `4092` (sau contul configurat pentru garanții de ambalaje) = `401`, fără TVA | Nu intră în costul mărfii (A4 §4.12) |
| Diferența de curs între NIR și factură (NIR în valută) | `665` / `765` față de `408` | Pe linia de marfă: `408` se închide la cursul NIR-ului, `401` se creează la cursul facturii (decizia 9) |

**Reclasificarea lunară a mărfurilor în curs de aprovizionare** (`GoodsInTransitReclass = 1`, decizia 8): la închiderea lunii, pasul nou `GOODS_IN_TRANSIT` din C3 (tip „Generare”) ia liniile de marfă facturate și nerecepționate la ultima zi a lunii și face nota `32x` (stocuri în curs de aprovizionare, după tipul articolului: `321`, `322`, `323`, `327`, `328`) = `408`, stornată automat în prima zi a lunii următoare. Așa `408` la sfârșitul lunii arată doar recepțiile nefacturate, iar bilanțul arată marfa pe drum ca stoc.

### 4.11 Note de credit, stornări și reduceri

| Caz | Tranzacție | Comportament |
| --- | --- | --- |
| Furnizorul corectează o factură (preț, cantitate) | `FS-FF` | Generată din factură (grupul `CREDIT`, fără depășire); liniile cu minus. Închide diferențele „notă de credit cerută” de pe factura sursă și deblochează plata dacă nu mai rămân diferențe |
| Furnizorul stornează un retur | `FS-RT` | Generată din retur (A4 §4.11); valoarea și costul din retur. Contare: `408` = `401` cu minus și `4426` = `401` cu minus |
| Reducere comercială ulterioară (rabat de volum, discount) | `FS-MAN` | Cu legătură pe liniile facturilor sau NIR-urilor vizate: corecție de cost `CA` (`RebateAfterReceipt`, S3), `401` = `408` și `371` / `607` = `408`. Fără legătură pe livrări (rabat global pe perioadă): `401` = `609` (reduceri comerciale primite) |
| Greșeala noastră de înregistrare | Stornare internă | `ReverseSupplierInvoice`: un document `DocKind = 3`, `IsInternalReversal = 1`, cu aceleași linii cu minus, aceeași dată contabilă dacă luna e deschisă; legăturile se dezactivează, deci NIR-urile redevin nefacturate. Interzisă dacă factura are plăți alocate (`53413`; se desface întâi alocarea în F3). Elementul din inbox revine în „Gata” pentru reînregistrare |

Exemplu (ecranul `a5-04`): Izvorul Carpaților trimite prin SPV nota de credit `IC 77315` (tip `381`) pentru returul `RT 0031`: −240 buc APA05 la 1,2100 = −290,40 lei, TVA 11% −31,94, total −322,34. Inbox-ul găsește returul după `BillingReference` (factura `IC 77288`) și după variantă și cantitate; `FS-RT` consumă grupul `CREDIT` pe retur. Contare: `408` = `401` −290,40 și `4426` = `401` −31,94. Returul nu mai apare în „Retururi fără notă de credit”.

### 4.12 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Factura în SPV a fost deja înregistrată de pe hârtie cu alt număr (greșeală de tastare) | Dublura nu se detectează pe număr; inbox-ul caută și după furnizor + dată + total și propune legarea |
| O factură acoperă trei NIR-uri și un serviciu de transport | `FF-NIR` cu toate NIR-urile + o linie de cost de achiziție; A6 repartizează transportul |
| NIR-ul e în altă lună decât factura | Factura se contează în luna ei; diferențele de preț corectează costul în prima lună deschisă (S3) |
| Factura sosește după închiderea lunii ei | Data contabilă = prima zi a primei luni deschise (decizia 10); TVA-ul se deduce în luna înregistrării (C4), cu avertizare |
| Furnizorul devine inactiv după factură | Factura rămâne cu TVA deductibil (statutul de la data facturii); plățile noi primesc avertizare (Nucleu) |
| Factura în EUR din NIR în lei (sau invers) | Interzis pe `FF-NIR` (`53414`): moneda NIR-ului urmează comanda; altfel stornare și NIR nou |
| Factura citează o comandă închisă | Liniile se potrivesc pe NIR-uri; comanda închisă nu blochează facturarea (A3 regula 5) |
| SGR pe factura de apă | Linia `LineKind = 5` din UBL (categoria `O`), fără TVA, potrivită pe articolul „Garanție SGR” |
| Factură cu valoare zero (mostre) | Permisă, cu linii la preț zero; NIR-ul eventual la cost zero, cu avertizare |
| e-Factura cu mai multe cote și reduceri pe antet | Reducerea de antet se repartizează pe linii proporțional, pe fiecare cotă (EN 16931 `AllowanceCharge` cu categoria TVA) |
| Furnizor persoană fizică sau străin | `EInvoiceStatus = 0`; fără verificare SPV |

**Jurnalul de excepții (precizare din Achiziții A7, 27.11.2026).** Excepțiile aprobate în această parte se scriu în `purch.ExceptionLog` prin `IPurchaseExceptionWriter.WriteAsync`, în aceeași tranzacție cu documentul care le aprobă (A7 §3.5): diferența de potrivire acceptată (cantitate sau preț); factura fără comandă peste prag, aprobată. Se scriu documentul, linia, furnizorul, valoarea în lei, cine a cerut, cine a aprobat și motivul.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `FacturaFurnizorValidata` v1 | Validarea facturii, notei de credit sau stornării interne | Conținutul minim F3 (`InvoiceId`, `PartnerId`, `Number`, `SupplierDocNo`, `DocumentDate`, `AccountingDate`, `CurrencyCode`, `ExchangeRate`, `TotalAmount`, `TotalAmountRon`, `VatAmount`, `VatAmountRon`, `IsVatOnCollection`, `IsReversal`, `ReversedInvoiceId`, `IsAdvanceInvoice`, scadențele, `CostCenterId`, `ProjectId`, `EInvoiceStatus`, `EInvoiceIndexNo`), plus `PaymentBlockReason`, liniile pe variantă și centru de cost, blocul `Accounting` (4.10) | F3 (partida), C2, C4 (registrul TVA), A3 (de facturat, angajamente), A7, Raportare |
| `DiferenteFacturaDecise` v1 | Decizia pe diferențe | `InvoiceId`, diferențele cu decizia, `LandedCostIds`, dacă plata se deblochează | F3 (deblocare), A7 |
| `FacturaPrimitaSPV` v1 | Legarea unui element din inbox de o factură înregistrată | `InvoiceId`, `EInvoiceIndexNo` | F3 (`EInvoiceStatus = 4`) |
| `ProformaFurnizorInregistrata` v1 | Salvarea proformei | `ProformaId`, `OrderId`, `PartnerId`, `Amount`, `DueDate` | F2 (propunerea de plată), F5 |

### 5.2 Consumate

| Eveniment | De la | Ce face A5 |
| --- | --- | --- |
| `EFacturaPrimita` v1 | Integrări 02.14 | Creează elementul din inbox și rulează potrivirea |
| `ReceptieNIRValidata` v1, `ReceptieNIRStornata` v1 | A4 | Reia potrivirea elementelor din inbox „Potrivit parțial” ale furnizorului; un NIR suplimentar închide diferențele „corectat prin alt document” |
| `ReturFurnizorValidat` v1 | A4 | Adaugă returul în lista „retururi fără notă de credit” |
| `AlocareInregistrata` v1 | F3 | Actualizează starea „plătită” afișată pe factură (calculată, doar cache) |
| `PartenerModificat` v1 | Nucleu | Elementele din inbox cu furnizor necunoscut și același CUI se leagă de partener |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A5)

```csharp
public enum SupplierInvoiceKind : byte { Invoice = 1, AdvanceInvoice = 2, CreditNote = 3 }
public enum SupplierInvoiceStatus : byte { Draft = 1, PendingApproval = 2, Validated = 3, Reversed = 4, Cancelled = 9 }
public enum InvoiceLineKind : byte { Goods = 1, Service = 2, LandedCost = 3, AdvanceSettlement = 4, NonTaxable = 5 }
public enum InvoiceMatchResult : byte { Ok = 1, WithinTolerance = 2, QtyDifference = 3, PriceDifference = 4, NoReceipt = 5, NoOrder = 6 }
public enum InvoiceDifferenceDecision : byte { Accepted = 1, CreditNoteRequested = 2, CorrectedByOtherDocument = 3 }
public enum InboxItemStatus : byte { New = 1, PartiallyMatched = 2, Ready = 3, Registered = 4, Ignored = 8, CommerciallyRejected = 9 }

public sealed record SupplierInvoiceSummaryDto(long Id, string? Number, int PartnerId, string SupplierDocNo, DateOnly DocumentDate,
    SupplierInvoiceKind Kind, SupplierInvoiceStatus Status, string CurrencyCode, decimal TotalAmount, decimal TotalAmountRon,
    byte MatchStatus, byte? PaymentBlockReason, byte EInvoiceStatus);

// Citit de A6 (costurile de achiziție de pe facturi) și de Raportare
public interface ISupplierInvoiceReader
{
    Task<SupplierInvoiceSummaryDto?> GetAsync(long invoiceId, CancellationToken ct);
    Task<IReadOnlyList<LandedCostInvoiceLineDto>> GetLandedCostLinesAsync(long invoiceId, CancellationToken ct);
}
public sealed record LandedCostInvoiceLineDto(long LineId, int ItemVariantId, decimal NetAmountRon, IReadOnlyList<long> LinkedReceiptIds);

// Implementat de A5 pentru pasul C3 GOODS_IN_TRANSIT
public interface IGoodsInTransitProvider
{
    Task<IReadOnlyList<GoodsInTransitRow>> GetAsync(int companyId, DateOnly asOf, CancellationToken ct);
}
public sealed record GoodsInTransitRow(int PartnerId, long InvoiceId, long LineId, int ItemVariantId, decimal QtyBase, decimal NetAmountRon);
```

Implementarea `ILinkableDocumentSource` pentru `SupplierInvoice` expune cantitatea liniei (grupurile `RECEIVE` pentru `NIR-FF`, `CREDIT` pentru `FS-FF`, `LANDED` pentru liniile de cost), cu filtrele `SAME_SUPPLIER`, `ITEM_KIND:SERVICE`, `LANDED_COST_LINES`. `IReceivedNotInvoiced` (A4 §6) citește și facturile nerecepționate de aici.

Comenzi interne A5 (MediatR): `ImportEInvoice` (din eveniment), `RematchInboxItem`, `ConfirmInboxLineMatch`, `IgnoreInboxItem`, `RejectInboxItemCommercially`, `LinkInboxItemToInvoice`, `CreateInvoiceFromInbox`, `SaveSupplierInvoice`, `SubmitSupplierInvoice`, `ValidateSupplierInvoice`, `DecideInvoiceDifferences`, `SetInvoicePaymentBlock`, `ReverseSupplierInvoice`, `CancelSupplierInvoice`, `SaveSupplierProforma`, `SaveCostSplitTemplate`. Generarea: `SupplierInvoice.FromReceipts`, `.FromOrder`, `.WithReceipt`, `.FromEInvoice`, `.Manual`, `CreditNote.FromInvoice`, `.FromReturn`, `.Discount` (A1 §3.2). Interogări: `ListInbox`, `ListSupplierInvoices`, `GetInvoiceMatch`, `ListReturnsWithoutCreditNote`.

## 7. Schema SQL `purch` (partea A5)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.EInvoiceInboxItem` | `Id bigint` | UX `(CompanyId, SpvIndexNo)`; FK `PartnerId`, `InvoiceId`, `XmlFileId` | `(CompanyId, Status, ReceivedAt) INCLUDE (PartnerId, TotalAmount)`; `(SupplierFiscalCode, InvoiceNoKey)` |
| `purch.EInvoiceInboxLine` | `Id bigint` | UQ `(ItemId, LineNo)`; FK `ItemId`, `ItemVariantId`, `UomId` | — |
| `purch.SupplierInvoice` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; UX `(CompanyId, PartnerId, SupplierDocKey, DocKind, FiscalYear) WHERE Status <> 9 AND IsInternalReversal = 0`; FK `TransactionTypeId`, `PartnerId`, `CurrencyCode`, `ReversedInvoiceId`, `InboxItemId` | `(CompanyId, Status, DocumentDate) INCLUDE (PartnerId, TotalAmountRon, MatchStatus)`; `(PartnerId, DocumentDate)` |
| `purch.SupplierInvoiceLine` | `Id bigint` | UQ `(InvoiceId, LineNo)`; FK `InvoiceId`, `ItemVariantId`, `UomId`, `VatRateId`, `CostCenterId`, `ProjectId` | `(ItemVariantId) INCLUDE (InvoiceId, QtyBase, NetAmountRon)` |
| `purch.SupplierInvoiceLineSplit` | `Id bigint` | FK `LineId`, `CostCenterId`, `ProjectId`; `CHECK ((Pct IS NULL) <> (Amount IS NULL))` | `(LineId)` |
| `purch.SupplierInvoiceMatch` | `Id bigint` | FK `LineId`, `OrderLineId`, `ReceiptLineId` | `(LineId)`; `(ReceiptLineId)` |
| `purch.SupplierInvoiceVat` | `Id bigint` | UQ `(InvoiceId, VatRateId, VatOperationType)` | — |
| `purch.SupplierInvoiceSchedule` | `Id bigint` | UQ `(InvoiceId, Seq)` | — |
| `purch.SupplierProforma` | `Id bigint` | UX `(CompanyId, Number)`; FK `PartnerId`, `OrderId` | `(OrderId)` |
| `purch.CostSplitTemplate`, `purch.CostSplitTemplateLine` | `Id int`, `Id int` | UX `(CompanyId, CodeKey)`; FK `PartnerId NULL`, `ItemId NULL`; linii FK `CostCenterId`, `ProjectId` | — |
| `purch.ReverseChargeItemList` | `Id int` | UX `(NcCodePrefix, ValidFrom)` | — |

`FiscalYear` e coloană calculată persistată `YEAR(DocumentDate)`; `SupplierDocKey` e calculată persistată (`UPPER`, fără spații, `-`, `/`, `.`), comparată binar pe baza case sensitive (`SQL-21`).

Exemplu de tabel complet:

```sql
CREATE TABLE purch.SupplierInvoiceMatch (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierInvoiceMatch PRIMARY KEY,
    LineId              bigint            NOT NULL CONSTRAINT FK_SupplierInvoiceMatch_Line REFERENCES purch.SupplierInvoiceLine(Id),
    OrderLineId         bigint            NULL CONSTRAINT FK_SupplierInvoiceMatch_OrderLine REFERENCES purch.PurchaseOrderLine(Id),
    ReceiptLineId       bigint            NULL CONSTRAINT FK_SupplierInvoiceMatch_ReceiptLine REFERENCES purch.GoodsReceiptLine(Id),
    MatchedQty          decimal(19,6)     NOT NULL,
    OrderPrice          decimal(19,6)     NULL,
    ReceiptPrice        decimal(19,6)     NULL,
    InvoicePrice        decimal(19,6)     NOT NULL,
    QtyDiff             decimal(19,6)     NOT NULL CONSTRAINT DF_SupplierInvoiceMatch_QtyDiff DEFAULT (0),
    PriceDiffAmountRon  decimal(19,4)     NOT NULL CONSTRAINT DF_SupplierInvoiceMatch_PriceDiff DEFAULT (0),
    Result              tinyint           NOT NULL CONSTRAINT CK_SupplierInvoiceMatch_Result CHECK (Result BETWEEN 1 AND 6),
    Decision            tinyint           NULL CONSTRAINT CK_SupplierInvoiceMatch_Decision CHECK (Decision BETWEEN 1 AND 3),
    Reason              nvarchar(500)     NULL,
    DecidedBy           int               NULL CONSTRAINT FK_SupplierInvoiceMatch_DecidedBy REFERENCES core.[User](Id),
    DecidedAt           datetime2(0)      NULL,
    LandedCostId        int               NULL,                                  -- inv.LandedCost.Id, fără FK fizic între scheme de module
    CONSTRAINT CK_SupplierInvoiceMatch_Decided CHECK ((Decision IS NULL AND DecidedBy IS NULL) OR (Decision IS NOT NULL AND DecidedBy IS NOT NULL AND DecidedAt IS NOT NULL))
);
CREATE INDEX IX_SupplierInvoiceMatch_Line ON purch.SupplierInvoiceMatch (LineId);
CREATE INDEX IX_SupplierInvoiceMatch_ReceiptLine ON purch.SupplierInvoiceMatch (ReceiptLineId) WHERE ReceiptLineId IS NOT NULL;
```

Proceduri: `purch.usp_EInvoiceInbox_Import` (idempotent pe index, liniile din TVP), `purch.usp_EInvoiceInbox_Match` (cascada A1 §4.3 pe seturi, `SP-07`), `purch.usp_SupplierInvoice_Save` (antet, linii, repartizări, TVA, scadențe din TVP-uri, `RowVersion`, `SP-18`), `purch.usp_SupplierInvoice_Match` (potrivirea 4.4 pe seturi), `purch.usp_SupplierInvoice_Validate`, `purch.usp_SupplierInvoice_DecideDifferences`, `purch.usp_SupplierInvoice_Reverse`, `purch.usp_SupplierInvoice_List` (`SP-11`), `purch.usp_GoodsInTransit_AsOf`.

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53401 | `PURCH_INVOICE_DUPLICATE` | 409 | Același număr al furnizorului (cheie normalizată), același tip, același an fiscal |
| 53402 | `PURCH_INBOX_DUPLICATE_INDEX` | 409 | Index SPV deja importat (tratat tăcut la job, întors doar la import manual) |
| 53403 | `PURCH_INVOICE_NOT_EDITABLE` | 409 | Modificarea unei facturi validate, stornate sau anulate |
| 53404 | `PURCH_INVOICE_LINE_NO_VARIANT` | 400 | Linie fără variantă |
| 53405 | `PURCH_INVOICE_TOTAL_MISMATCH` | 409 | Totalul calculat diferă de totalul documentului furnizorului |
| 53406 | `PURCH_INVOICE_VAT_ROUNDING_EXCEEDED` | 409 | TVA-ul introdus pe o cotă diferă de cel calculat peste toleranță |
| 53407 | `PURCH_INVOICE_SUPPLIER_INVALID` | 409 | Furnizor inactiv în Nucleu sau blocat pentru firmă |
| 53408 | `PURCH_INVOICE_SPLIT_INVALID` | 400 | Repartizare care nu dă 100% sau valoarea liniei; centru de cost lipsă unde contul îl cere |
| 53409 | `PURCH_INVOICE_APPROVAL_REQUIRED` | 409 | Validarea unei facturi care cere aprobare, fără aprobare și fără dreptul de urgență |
| 53410 | `PURCH_INVOICE_SPV_REQUIRED` | 409 | Factură internă fără e-Factura, cu `DomesticEInvoiceRequired = 2`, fără drept |
| 53411 | `PURCH_INVOICE_ORDER_REQUIRED` | 409 | Linie stocabilă fără comandă, cu `RequiresOrder = 2` |
| 53412 | `PURCH_INVOICE_ADVANCE_EXCEEDED` | 409 | Regularizare de avans peste avansul facturat și neregularizat |
| 53413 | `PURCH_INVOICE_REVERSAL_HAS_PAYMENTS` | 409 | Stornarea internă a unei facturi cu plăți alocate |
| 53414 | `PURCH_INVOICE_CURRENCY_MISMATCH` | 409 | Factură în altă monedă decât NIR-urile sau comanda sursă |
| 53415 | `PURCH_INVOICE_DIFFERENCE_UNDECIDED` | 409 | Deblocarea plății cu diferențe încă nedecise |
| 53416 | `PURCH_INVOICE_DATE_INVALID` | 400 | Data facturii în viitor sau data contabilă înaintea datei facturii |
| 53417 | `PURCH_INBOX_PARTNER_UNKNOWN` | 409 | Înregistrarea unui element din inbox fără partener creat |
| 53418 | `PURCH_INVOICE_VAT_TYPE_LOCKED` | 403 | Schimbarea tipului de operațiune TVA fără `Purch.Invoice.EditVat` |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.Inbox.View`, `Purch.Inbox.Process` | Inbox-ul: vizualizare; potrivire, ignorare, respingere comercială, legare |
| `Purch.Invoice.View` | Facturile firmei |
| `Purch.Invoice.Edit` | Creare și modificare ciorne, proforme |
| `Purch.Invoice.Validate` | Validarea |
| `Purch.Invoice.Reverse` | Stornarea internă |
| `Purch.Invoice.DecideDifferences` | Decizia pe diferențele de potrivire |
| `Purch.Invoice.EditVat` | Tipul de operațiune și procentul deductibil |
| `Purch.Invoice.ValidateWithoutSpv` | Validarea unei facturi interne fără e-Factura, când setarea blochează |
| `Purch.Invoice.ValidateBeforeApproval` | Validarea de urgență înaintea aprobării (plata rămâne blocată) |
| `Purch.CostSplitTemplate.Manage` | Șabloanele de repartizare |

Roluri livrate: **Contabil furnizori** (toate, mai puțin `ValidateWithoutSpv` și `ValidateBeforeApproval`), **Achizitor** (`Inbox.View`, `Invoice.View`, `DecideDifferences` pentru furnizorii lui), **Șef achiziții** (`Invoice.View`, `DecideDifferences`; aprobator de nivel 1 pentru diferențe), **Contabil șef** (toate), **Responsabil centru de cost** (rol de aprobare, `Invoice.View` pe centrele lui).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A5 |
| --- | --- |
| OUG 120/2021 și Codul fiscal art. 319 (RO e-Factura, B2B obligatoriu) | Facturile între persoane impozabile stabilite în România circulă prin SPV; inbox-ul e sursa principală; factura fără SPV e semnalată (`EInvoiceStatus = 5`, `DomesticEInvoiceRequired`) și plata ei cere confirmare (F3) |
| EN 16931 și RO_CIUS | Citirea UBL: tipurile `380`, `381`, `384`, `386`; categoriile de TVA; `OrderReference`, `DespatchDocumentReference`, `BillingReference`; reducerile și taxele pe antet |
| Codul fiscal art. 297–302 (deducerea TVA) | Deducerea pe baza facturii; limitarea 50% pentru vehicule (art. 298); fără deducere pentru furnizorii inactivi (art. 11) |
| Codul fiscal art. 282 și 331 (TVA la încasare, taxarea inversă internă) | `IsVatOnCollection` din registrul ANAF la data facturii; lista codurilor NC cu taxare inversă, livrată și actualizată |
| OMFP 1802/2014 | `408` pentru recepțiile nefacturate, `32x` pentru stocurile în curs de aprovizionare, `409` pentru avansuri, `609` pentru reducerile primite ulterior |
| OMFP 2634/2015 | Factura ca document justificativ; înregistrarea în ordine cronologică, cu număr intern continuu (seria `FF`) |
| Legea 72/2013 | Scadențele propuse; avertizare la termen peste 60 de zile (ca în A3) |
| HG 1074/2021 (SGR) | Garanția nu intră în baza TVA și nici în costul mărfii (4.10) |

Integrări externe: SPV, prin 02.14 (descărcare, registre ANAF); email (outbox) pentru contestări. Interne: A1 (potrivire, catalog, toleranțe), A3 (comandă, angajamente), A4 (NIR, retur, `408`), A6 (costuri de achiziție), F3 (partide, blocare), F4 (curs), C2 (contare), C3 (`GOODS_IN_TRANSIT`, concordanța `408`), C4 (registrul TVA), Stocuri S3 (`CA`).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Inbox e-Factura | Elementele pe stare, vechime, contabil; întârzierile față de `RegistrationDeadlineDays` | Contabil furnizori |
| Facturi blocate la plată | Motivul, diferențele, cine trebuie să decidă, scadența | Contabil furnizori, Achizitor, Director financiar |
| Diferențe de potrivire | Pe furnizor și articol: diferențe de preț și cantitate, decizii, `CA` create, note de credit așteptate | Șef achiziții, A7 |
| Facturi fără e-Factura | Facturile interne cu `EInvoiceStatus = 5`, pentru verificarea cu furnizorii | Contabil furnizori |
| Retururi fără notă de credit | Retururile validate și necreditate, cu vechimea | Contabil furnizori |
| Mărfuri facturate nerecepționate | Liniile din `IGoodsInTransitProvider`, la dată | Contabil șef |
| Cheltuieli pe centre de cost | Facturile de servicii pe centre de cost, proiecte și conturi, după repartizare | Controlling, responsabilii de centre |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a5-facturi/](ecrane/a5-facturi/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Inbox e-Factura | Elementele zilei: furnizorul, totalul, starea potrivirii, sursa găsită; detaliul `IC 77301` cu liniile, pasul de potrivire și NIR-ul propus | `a5-01-inbox-efactura` |
| Potrivire comandă–NIR–factură | `FA 9921`: comandă, NIR și factură pe fiecare linie, diferențele de cantitate și preț, decizia, blocarea la plată, nota contabilă | `a5-02-potrivire` |
| Factură de servicii | `VE 2611-0457`: linia de energie, repartizarea pe centre de cost din șablon, aprobarea responsabilului | `a5-03-servicii-centre-cost` |
| Notă de credit | `IC 77315` din SPV, legată de `RT 0031` și de factura `IC 77288`, cu contarea | `a5-04-nota-credit` |
| Facturi de furnizor | Lista cu filtrele: blocate la plată, fără e-Factura, de aprobat, scadente; indicatorii | `a5-05-facturi-lista` |
| Factură de furnizor (fișa) | `FF 0912` (`IC 77301`): antetul (furnizor, numărul furnizorului, datele, scadența, moneda, TVA la încasare, tranzacția), liniile cu sursa din NIR și comandă și rezultatul potrivirii, TVA-ul pe cote, totalurile, tab-urile (potrivire, TVA, scadențe, contare, legături, atașamente, istoric), plata din F3, drumul documentului și contarea | `a5-06-factura-furnizor` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A5-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A5-A1 | Importul aceluiași index SPV de două ori creează un singur element; elementul `IC 77301` ajunge „Gata” cu APA05 la pasul 3 și APA15 la pasul 2, sursa NIR 4487 | Test de integrare (cu A1, A4) |
| A5-A2 | `FF-EF` din `IC 77301` se validează fără diferențe; nota: `408` = `401` 3.567,72 + 392,45, `4426` = `4428` 392,45; soldul `408` pentru NIR 4487 devine 0 | Test de integrare (cu C2) |
| A5-A3 | `FA 9921`: diferența de cantitate 4 role și de preț 47,40 lei sunt detectate; factura se validează și e blocată la plată; acceptarea prețului creează `CA` de 47,40 lei pe rândurile de urmărire ale NIR 4490; deblocarea cu diferența de cantitate nedecisă e refuzată (`53415`) | Test de integrare (cu S3, F3) |
| A5-A4 | Toleranța de preț: o diferență de 0,4% și 8,00 lei trece automat; una de 0,4% și 12,00 lei nu (se aplică minimul dintre procent și sumă) | Test unitar |
| A5-A5 | Dubluri: `fa-9921` și `FA 9921` de la același furnizor în același an sunt aceeași factură (`53401`); același număr la alt furnizor e permis | Test de integrare |
| A5-A6 | `VE 2611-0457` (6.180,00 lei, fără comandă) cere aprobarea lui Gheorghe Ilie (70%); repartizarea dă 4.326,00 și 1.854,00 pe centrele de cost în nota `605` = `401`; o repartizare de 90% e refuzată (`53408`) | Test de integrare |
| A5-A7 | TVA: linie cu cod NC din lista art. 331 primește `A-AE` și nota `4426` = `4427`; furnizor cu TVA la încasare duce TVA-ul pe `4428`; furnizor inactiv propune `A-NOD` | Test de integrare (cu C4) |
| A5-A8 | Factura de avans 30% pe o comandă și factura finală cu regularizarea: `409` se închide; regularizarea peste avans e refuzată (`53412`) | Test de integrare (cu F3) |
| A5-A9 | `FS-RT` din `IC 77315` stinge returul `RT 0031`; contarea `408` = `401` −290,40 și `4426` = `401` −31,94; soldul `408` pentru retur devine 0 | Test de integrare |
| A5-A10 | `FF-REC` creează factura și NIR-ul în aceeași tranzacție; o eroare la stoc anulează ambele | Test de integrare (cu A4, Stocuri) |
| A5-A11 | Pasul `GOODS_IN_TRANSIT` la 30.11 reclasifică liniile facturate și nerecepționate pe `327` = `408` și le stornează pe 01.12 | Test de integrare (cu C3) |
| A5-A12 | Stornarea internă a unei facturi cu plată alocată e refuzată (`53413`); fără plată, NIR-urile redevin nefacturate și elementul din inbox revine în „Gata” | Test de integrare |
| A5-A13 | Toate procedurile A5 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Test de integrare |

Criteriile A5-A2, A5-A3 și A5-A9 sunt critice: ele decid cât datorează firma furnizorului, ce se plătește și cum se închide contul `408`.

## Precizări la alte documente (aplicate pe 27.11.2026)

| Document | Precizarea |
| --- | --- |
| Stocuri S3 §4.2 | Contrapartida corecțiilor de cost din facturi (diferență de preț, cost de achiziție, reducere ulterioară) este `408`, nu `401`: factura închide `408` față de `401` (A4 decizia 9) |
| Finanțe C3 | Pasul nou `GOODS_IN_TRANSIT` (tip „Generare”, cu stornare automată la începutul lunii următoare), din `IGoodsInTransitProvider` |
| Finanțe C2 §3.3 | Cheia nouă `GoodsInTransit` (lanț: articol → grupa → tipul articolului: `321`, `322`, `323`, `327`, `328`) |
| Finanțe F3 §3.8 | Evenimentul `FacturaPrimitaSPV` e publicat de A5 la legarea unui element din inbox; `PaymentBlockReason` din eveniment pune `IsBlockedForPayment = 1` |
| A2 §3.2 | `DocumentType` acceptă și `SupplierInvoice` și `SupplierInvoiceDifference` în regulile de aprobare |

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **`NonStockAssetKind`**: 0 Nu · 1 Mijloc fix (linia intră în coada „Imobilizări de preluat” din M2, grupul `ASSET_IN`) · 2 Investiție în curs (linia se adaugă la o investiție, `INC-FF`, grupul `CIP_IN`). Propunerea vine din `IAssetThresholdPolicy` (pragul de 5.000 lei, M1 §3.4) și din articol.
- **Contarea liniei de imobilizare**: `231.PRL` (`AssetPendingIntake`) = `404` (`AssetSupplierPayable`) pentru tipul 1; `231` = `404` pentru tipul 2; TVA-ul ca de obicei. Intrarea M2 mută apoi valoarea pe `21x`. Varianta veche „contul din articol până la modulul MF” nu se mai folosește.
- **Diferența de preț NIR → factură** pe o linie de imobilizare intrată din NIR publică `DiferentaPretFactura` cu `AssetId`, folosită de M2 (ajustare) sau M4 (modificare prospectivă).
- **Linia preluată**: când M2 sau M4 consumă linia (intrare, modernizare), linia primește starea „preluată” prin `LinieImobilizarePreluata`; „Nu e imobilizare” o întoarce la contabilul de furnizori pentru recontare.

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Costul campaniilor**: câmpul opțional `CampaignId` pe linia facturii de furnizor (servicii de marketing, chirii de săli, materiale), vizibil doar cu pachetul CRM; valoarea fără TVA a liniei intră în costul campaniei. [CR4 §3.5](../12-crm/CR4-ACTIVITATI-CAMPANII.md)

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Controlul bugetar** la validarea facturii de furnizor fără comandă: același `IBudgetCheck` ca la A3 (avertisment cu motiv sau aprobare, fără blocare, pentru că factura primită trebuie înregistrată oricum). [BI5 §3.3](../13-raportare-bi/BI5-BUGETARE.md)

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Inbox-ul e-Factura | Toate facturile din SPV intră într-un inbox; furnizorul după CUI, liniile potrivite cu cascada din A1, sursele propuse (NIR, comandă); factura se creează din inbox sau se leagă de una deja înregistrată | 3.1, 4.1 | Decis de Aurelian (27.11.2026) |
| 2 | Factura greșită din SPV | Nu se „respinge” în SPV; se marchează „respins comercial”, se contestă pe email și se înregistrează cu plata blocată, dacă furnizorul nu o stornează în aceeași lună | 4.1 | Decis de Aurelian (27.11.2026) |
| 3 | Potrivirea comandă–NIR–factură | Pe linie: cantitatea față de NIR, prețul față de NIR (și comandă, informativ); toleranța de preț = minimul dintre procent și sumă; diferențele se decid explicit, documentele nu se modifică | 2 regula 4, 4.4 | Decis de Aurelian (27.11.2026) |
| 4 | Factura cu diferențe | Se validează (datoria și TVA-ul intră în registre), dar plata e blocată pe toată factura până la decizia pe fiecare diferență | 3.4, 4.4 | Decis de Aurelian (27.11.2026) |
| 5 | Diferența de preț acceptată | Corectează costul stocului prin documentul `CA` (partea vândută pe `607`); opțional actualizează lista de prețuri a furnizorului | 4.4, 4.10 | Decis de Aurelian (27.11.2026) |
| 6 | Aprobarea facturilor | Doar pentru facturile fără comandă peste 5.000 lei (responsabilul centrului de cost cu partea cea mai mare) și pentru diferențele acceptate peste 500 lei (Șef achiziții), pe regulile din A2 | 4.5 | Decis de Aurelian (27.11.2026) |
| 7 | Serviciile pe centre de cost | Repartizare pe linie, în procente sau sume, cu șabloane propuse după furnizor și articol | 3.3, 4.6 | Decis de Aurelian (27.11.2026) |
| 8 | Marfa facturată și nerecepționată | Rămâne în `408` debitor; la închiderea lunii se reclasifică pe `32x` (stocuri în curs de aprovizionare) și se stornează la începutul lunii următoare | 4.10 | Decis de Aurelian (27.11.2026) |
| 9 | Cursul la factura în valută | `401` la cursul facturii, `408` se închide la cursul NIR-ului, diferența pe `665` / `765` | 4.10 | Decis de Aurelian (27.11.2026) |
| 10 | Factura sosită după închiderea lunii | Data contabilă = prima zi a primei luni deschise, cu avertizare; TVA-ul se deduce în luna înregistrării | 3.2, 4.12 | Decis de Aurelian (27.11.2026) |
| 11 | Avansurile | Proforma (fără contare) pentru plată; factura de avans `409` = `401`; regularizarea pe factura finală, propusă automat, fără să depășească avansul | 4.9 | Decis de Aurelian (27.11.2026) |
| 12 | Factura fără e-Factura de la un furnizor român | Implicit avertizare la validare (setabil pe blocare); se leagă automat când apare în SPV | 3.5, 4.1 | Decis de Aurelian (27.11.2026) |
| 13 | Corectarea | Greșeala noastră: stornare internă și reînregistrare (doar fără plăți alocate); greșeala furnizorului: notă de credit `FS-FF`; rabatul global pe `609`, rabatul pe livrări prin corecție de cost | 4.11 | Decis de Aurelian (27.11.2026) |
| 14 | Factura și nota de credit | Același tabel (`DocKind`), cu tipuri de document diferite, ca liniile, TVA-ul și contarea să aibă o singură structură | 2 regula 1 | Decis de Aurelian (27.11.2026) |

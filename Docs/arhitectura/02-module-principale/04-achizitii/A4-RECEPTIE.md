# ERP · 02.04 Achiziții · A4 Recepție: aviz furnizor, NIR, loturi și carantină, diferențe și PV, stornare, retur la furnizor

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**, cu ecranele și toate cele 14 decizii. A patra din cele 7 părți ale modulului 02.04 Achiziții. Se sprijină pe [A1 Configurare](A1-CONFIGURARE.md), [A2 Necesar și oferte](A2-NECESAR-SI-OFERTE.md) (ambele validate pe 26.11.2026) și [A3 Comenzi](A3-COMENZI.md) (validat pe 27.11.2026).

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`; partea A4 folosește `53300–53399`. Această parte acoperă **intrarea fizică a mărfii de la furnizor**: înregistrarea avizului furnizorului, nota de intrare-recepție (NIR) din comandă, din aviz sau fără sursă, recepția „oarbă” (numărare fără cantitatea așteptată), loturile, termenele, seriile și carantina la recepție, diferențele de cantitate și calitate cu decizia pe fiecare, comisia de recepție și procesul-verbal de constatare, recepția în gestiunile la preț cu amănuntul, confirmarea prestării serviciilor, stornarea NIR-ului și returul la furnizor. Factura e în A5, importul și costurile de achiziție în A6. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.6 Tranzacții și legături, fluxul 4.2 Numerotare, fluxul 4.7 Notificări și email), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C2 contarea, C3 închiderea lunii), [02.05 Stocuri](../05-stocuri/README.md) (S1 gestiuni, S2 portul de stoc, S3 costul și gestiunile la amănunt, S4 loturi, serii, termene și carantină). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile recepției** sunt livrate de A1 §3.2: `AV-CF` și `AV-MAN` (aviz), `NIR-CF` (din comandă), `NIR-AV` (din aviz), `NIR-MAN` (fără sursă), `NIR-FF` și `NIR-IMP` (din factură, generate din A5 și A6, dar cu regulile NIR-ului de aici), `RT-NIR` și `RT-MAN` (retur). Grupurile de consum (Nucleu §3.6): `DELIVER` (avizul consumă comanda), `RECEIVE` (NIR-ul consumă comanda, avizul sau factura), `RETURN` (returul consumă NIR-ul). Toleranța de depășire e pe tranzacție, cu precedența tranzacție → furnizor → firmă (A1 §3.7).
- **Doar NIR-ul și returul mișcă stocul**, sincron, prin portul `IStockLedger.PostAsync` (Stocuri S2 §6), în tranzacția SQL a validării. Avizul nu mișcă stocul. Mișcările folosesc tipurile `RECEIPT_PURCHASE` și `RETURN_TO_SUPPLIER` (S2 §3.2). Dacă Stocuri nu e licențiat (`IStockLedger.IsEnabled = false`), NIR-ul rămâne document de evidență, fără mișcare.
- **Liniile stau pe variantă** (`core.ItemVariant`, SKU), cu UM de cumpărare și cantitate în UM de bază calculată din setul de UM al articolului (Nucleu). Lotul, termenul și seria vin din modelul articolului (Stocuri S1 §3.4 `TrackingMode`, S4 §3.1): 0 fără, 1 lot, 2 lot și termen, 3 serie, 4 serie și lot.
- **Carantina e o stare a lotului**, nu o gestiune (S4 decizia 5). Modelul cu `ReceiptQuarantine = 1` face ca loturile noi să intre în carantină la recepție; eliberarea o face responsabilul de calitate, în S4.
- **Costul de intrare** e prețul net de pe linia NIR-ului (după reducere), în lei la cursul recepției (S3 §3.1). Diferențele de preț față de factură și costurile de achiziție (transport, vamă) corectează costul ulterior, prin documentul `CA` din Stocuri (`IStockLedger.AdjustInboundCostAsync`, S3 §3.3), creat de A5 și A6. NIR-ul validat nu se modifică niciodată (P3).
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Cantitățile și prețurile pe `decimal(19,6)`, valorile pe `decimal(19,4)` (`SQL-07`).
- **Ce NU face A4:** nu înregistrează facturi sau note de credit (A5), nu tratează DVI-ul și repartizarea costurilor de achiziție (A6), nu eliberează loturile din carantină și nu face analize de laborator (Stocuri S4, responsabilul de calitate), nu gestionează locațiile din depozit, rampele și programarea camioanelor (WMS, TMS), nu calculează indicatorii furnizorilor (A7, din datele păstrate aici).

## 1. Scop și perimetru

| Zonă | Ce face A4 | Ce NU face (și cine face) |
| --- | --- | --- |
| Aviz furnizor | Înregistrarea avizului la sosire (numărul furnizorului, mașina, șoferul, codul UIT e-Transport), din comandă sau manual; lista „ce a sosit și nu e recepționat” | Avizul emis de noi către clienți (Stocuri S2, Vânzări) |
| NIR | NIR din comandă, din aviz sau fără sursă; prețul din comandă; cursul; gestiunea; tipărirea formularului | NIR-ul generat din factură (declanșat de A5, cu regulile de aici) |
| Recepția oarbă | Numărarea fără cantitatea așteptată, scanarea codurilor, închiderea numărării, renumărarea | Terminalele mobile cu locații (WMS) |
| Loturi, termene, serii | Captarea pe linie (manual sau GS1), verificarea termenului rămas la recepție, carantina după model | Starea lotului, eliberarea, FEFO, trasabilitatea (Stocuri S4) |
| Diferențe | Lipsă, plus, deteriorat, articol greșit, termen prea scurt; decizia pe fiecare (acceptat, refuzat, reclamat) | Nota de credit cerută furnizorului (A5) |
| Comisie și PV | Comisia de recepție pe gestiune, procesul-verbal de constatare a diferențelor, trimiterea lui la furnizor | — |
| Gestiuni la amănunt | Prețul de vânzare, adaosul și TVA-ul inclus, pe NIR | Calculul `RetailAmount` și descărcarea prin K (Stocuri S3) |
| Servicii | Opțional, confirmarea prestării pe NIR, fără stoc | Factura de servicii și centrele de cost (A5) |
| Stornare | Stornarea NIR-ului nefacturat, integral sau pe linii | Stornarea mișcărilor în registru (Stocuri S2) |
| Retur | Returul la furnizor din NIR sau manual, avizul de însoțire, codul UIT | Factura de stornare primită (A5) |
| Contare | Blocul contabil al NIR-ului și al returului (TVA neexigibilă pe `4428`), concordanța contului `408` la închidere | Notele propriu-zise (C2), închiderea (C3) |

## 2. Model de domeniu

```
 SupplierDeliveryNote (AV)                       furnizor, nr. și data furnizorului, sosire, mașină, UIT, stare
   └─ SupplierDeliveryNoteLine × N              variantă, UM, cantitatea de pe aviz, lot/termen declarate
        ← DELIVER din linia de comandă (AV-CF)

 GoodsReceipt (NIR)                              furnizor, gestiune, data recepției, monedă și curs, document furnizor, stare
   ├─ GoodsReceiptLine × N                      variantă, UM; cantitate pe document, numărată, acceptată, refuzată; preț; preț cu amănuntul
   │    ├─ GoodsReceiptLineTracking × N         câte un rând pe lot / serie (și unul singur pentru articolele fără urmărire)
   │    └─ GoodsReceiptDiscrepancy × N          diferența: tip, cantitate, decizie, motiv, fotografii
   ├─ ReceiptReport (PVR) × 0..1                procesul-verbal de constatare, cu membrii comisiei
   └─ legături (Nucleu core.DocumentLink)
        ← RECEIVE din linia de comandă (NIR-CF), de aviz (NIR-AV, plus comanda prin aviz) sau de factură (NIR-FF, NIR-IMP)
        → INVOICE factura (A5) · RETURN returul (RT-NIR) · LANDED costurile de achiziție (A6)

 SupplierReturn (RT)                             furnizor, gestiune, motiv, UIT, stare
   └─ SupplierReturnLine × N                    variantă, cantitate, lot / serie, prețul și costul din NIR
        ← RETURN din linia de NIR (RT-NIR)   → CREDIT factura de stornare (A5, FS-RT)
```

Reguli de business ale modelului:

1. **Un NIR are un singur furnizor și o singură gestiune** (decizia 1). Formularul legal al NIR-ului se semnează de gestionarul gestiunii, iar gestionarul răspunde doar de gestiunea lui. Când liniile unei comenzi sau ale unui aviz merg în gestiuni diferite (regula depozitului, Stocuri S1 §3.8: în DC-PL ambalajele în AMB-PL, restul în gestiunea implicită), generatorul creează câte un NIR pe gestiune, legate de aceeași sursă.
2. **Fiecare linie de NIR are patru cantități**: pe documentul furnizorului (`DocQty`, din aviz sau factură; egală cu restul comenzii când nu există document), numărată (`CountedQty`), acceptată (`AcceptedQty`) și refuzată (`RefusedQty`), cu `AcceptedQty + RefusedQty = CountedQty` (decizia 4). **Doar cantitatea acceptată intră în stoc, consumă sursa (`RECEIVE`) și se facturează.** Refuzatul nu intră în gestiune: pleacă înapoi cu transportatorul sau rămâne la dispoziția furnizorului, consemnat în PV.
3. **Fiecare linie are cel puțin un rând de urmărire** (`GoodsReceiptLineTracking`), chiar și pentru articolele fără lot (un rând, fără lot). Suma rândurilor = cantitatea acceptată (`53304`). Rândul de urmărire e unitatea trimisă la stoc: `StockMovementRequest.SourceLineId` = `GoodsReceiptLineTracking.Id`, deci cheia unică a sursei din S2 (regula 6) rămâne fără ambiguitate.
4. **Diferențele se decid explicit, nu se deduc** (decizia 5). Orice diferență între document și numărat, sau între numărat și acceptat, are un rând `GoodsReceiptDiscrepancy` cu tip și decizie; NIR-ul cu diferențe nedecise nu se validează (`53306`).
5. **Restul comenzii rămâne deschis implicit.** O lipsă nu închide linia comenzii: furnizorul poate completa. Închiderea restului se face explicit, la validarea NIR-ului (bifa „închide restul liniei”, cu dreptul `Purch.Order.Manage`) sau automat, sub `UnderDeliveryAutoClosePct` (A1 §3.7, A3 §4.7).
6. **NIR-ul validat nu se modifică** (P3). Corectura se face prin stornare (4.10), dacă NIR-ul nu e facturat și nu are retur, sau prin retur la furnizor (4.11). Diferențele de preț se rezolvă la factură (A5), prin corecția de cost din Stocuri.
7. **Nimic nu se șterge după numerotare** (`SQL-20`). Numărul NIR-ului se alocă la validare (seria `NIR`, continuă, OMFP 2634/2015), nu la ciornă; ciornele se anulează.

## 3. Entități, stări și setări

Convențiile comune tabelelor `purch` sunt cele din A1 §3 (`CompanyId`, `RowVersion`, `CreatedAt/By`, `ModifiedAt/By`, coduri cu `CodeKey` unde e cazul). Seriile de numere sunt în `core.NumberSeries`: `AV` (număr intern al avizului înregistrat), `NIR`, `PVR` (proces-verbal de recepție), `RT`.

### 3.1 Avizul furnizorului (`SupplierDeliveryNote`, `SupplierDeliveryNoteLine`)

| Câmp (antet) | Tip | Regulă |
| --- | --- | --- |
| `Number` | `nvarchar(20) NULL` | Număr intern din seria `AV`, la salvare (avizul nu are ciornă: se înregistrează la sosire) |
| `PartnerId` | `int` | Furnizorul; din comandă la `AV-CF` |
| `SupplierDocNo`, `SupplierDocDate` | `nvarchar(40)`, `date` | Numărul și data avizului furnizorului; unice pe furnizor și an (`53316`), comparate după `CodeKey` (fără spații, majuscule) |
| `DepotId`, `WarehouseId` | `int NULL`, `int` | Unde a sosit marfa; gestiunea propusă după regula din 4.2 |
| `ArrivedAt` | `datetime2(0)` | Momentul sosirii (poarta sau rampa) |
| `VehicleNo`, `DriverName`, `CarrierPartnerId` | text, text, `int NULL` | Mașina, șoferul, transportatorul |
| `TransportUit` | `nvarchar(20) NULL` | Codul UIT din e-Transport, când transportul îl cere (9) |
| `Status` | `tinyint` | 1 Sosit · 2 În recepție (are NIR în ciornă) · 3 Recepționat (tot consumat sau închis) · 9 Anulat |

Linia: `LineNo`, `ItemVariantId`, `UomId`, `Qty`, `QtyBase`, `SupplierItemCode` (codul furnizorului, potrivit prin catalogul din A1 §3.5), `DeclaredLotCode`, `DeclaredExpiryDate` (ce scrie pe aviz, propuse pe NIR), `UnitPrice` (opțional, dacă avizul are prețuri), legătura `DELIVER` spre linia de comandă.

Avizul nu are efect în stoc și nu se contează. Se poate modifica până la primul NIR legat (`53317`); după aceea doar se închide restul (Nucleu `DocumentLineClosure`).

### 3.2 NIR-ul (`GoodsReceipt`)

| Câmp | Tip | Regulă |
| --- | --- | --- |
| `Number` | `nvarchar(20) NULL` | Din seria `NIR`, la validare |
| `TransactionTypeId` | `int` | `NIR-CF`, `NIR-AV`, `NIR-MAN`, `NIR-FF`, `NIR-IMP` sau o copie a lor (A1 §3.2) |
| `PartnerId`, `DepotId`, `WarehouseId` | `int`, `int NULL`, `int` | Un furnizor, o gestiune (regula 1); gestiunea activă, a firmei, neblocată la intrare (`53302`) |
| `ReceiptDate` | `date` | Data recepției fizice (`ReceiptDateRule = 1`) sau a facturii pentru `NIR-FF` cu `ReceiptDateRule = 2` (A1 §3.7); nu în viitor, nu înaintea datei comenzii (`53315`) |
| `AccountingDate` | `date` | Egală cu `ReceiptDate`; luna trebuie să fie deschisă în Stocuri (`53309`) |
| `CurrencyCode`, `ExchangeRate`, `ExchangeRateDate` | | Moneda sursei; cursul BNR valabil la data recepției (F4), propus și blocat la validare (`53313` dacă lipsește) |
| `SupplierDocKind`, `SupplierDocNo`, `SupplierDocDate` | `tinyint`, text, `date` | 1 aviz · 2 factură · 3 fără document (doar cu dreptul `Purch.Receipt.NoSupplierDoc`); copiate din sursă |
| `IsBlind` | `bit` | Recepție oarbă (4.3); din tranzacție, altfel din `PurchaseSettings.BlindReceipt` |
| `CountFinishedAt`, `CountFinishedBy` | | Închiderea numărării, la recepția oarbă |
| `CommitteeRequired` | `bit` | Calculat după `ReceiptCommitteeMode` (3.6) la salvare |
| `Status` | `tinyint` | 3.7 |
| `IsReversal`, `ReversalOfId`, `ReversedById` | `bit`, `bigint NULL` | NIR de stornare și legătura cu originalul (4.10) |
| `NetAmount`, `NetAmountRon`, `VatAmountRon`, `RetailAmount` | `decimal(19,4)` | Totaluri, recalculate la salvare; `RetailAmount` doar în gestiunile la amănunt |
| `ValidatedAt`, `ValidatedBy`, `Notes` | | |

### 3.3 Linia NIR-ului (`GoodsReceiptLine`) și urmărirea (`GoodsReceiptLineTracking`)

| Câmp | Tip | Regulă |
| --- | --- | --- |
| `ItemVariantId`, `UomId` | `int` | Obligatorii; pe liniile generate din sursă nu se schimbă (Nucleu §3.6.4) |
| `IsService` | `bit` | Articol nestocabil (4.9); fără urmărire și fără mișcare de stoc |
| `DocQty` | `decimal(19,6) NULL` | Cantitatea de pe documentul furnizorului; ascunsă gestionarului la recepția oarbă |
| `ExpectedQty` | `decimal(19,6) NULL` | Restul sursei la crearea NIR-ului (din comandă sau aviz); ascuns la recepția oarbă |
| `CountedQty`, `AcceptedQty`, `RefusedQty` | `decimal(19,6)` | Regula 2; `AcceptedQtyBase` calculată persistată din factorul UM |
| `UnitPrice`, `DiscountPct`, `VatRateId` | | Din sursă (comandă, aviz cu preț, factură); pe `NIR-MAN` din `ISupplierPriceProvider` (A1 §3.6) sau manual, cu dreptul `Purch.Receipt.EditPrice` |
| `NetAmount`, `NetAmountRon`, `UnitCostRon` | | `UnitCostRon` = prețul net în lei pe UM de bază; trimis ca `InboundUnitCost`. Costul se ține pe componente în `GoodsReceiptLineCost` (marfă, taxă vamală, transport, comision...), iar `UnitCostRon` = Σ componente / cantitatea de bază; pe NIR-urile obișnuite există o singură componentă (A6 §3.2) (precizare din Achiziții A6, 27.11.2026) |
| `RetailUnitPrice`, `RetailAmount`, `RetailVatAmount`, `MarkupAmount` | `NULL` în afara gestiunilor la amănunt | Afișate pe NIR (4.8); valoarea finală o calculează Stocuri la validare |
| `CostCenterId`, `ProjectId` | `int NULL` | Din comandă; obligatorii pe liniile de servicii dacă regula contului o cere (C2) |
| `CloseSourceRemainder` | `bit` | Bifa „închide restul liniei sursă” la validare (regula 5) |

Rândul de urmărire: `Seq`, `Qty`, `QtyBase`, `LotCode`, `ProductionDate`, `ExpiryDate`, `SerialNo`, `Gs1Raw` (textul scanat, pentru audit), `IsQuarantine` (afișare, calculat din model), apoi, după validare, `LotId`, `SerialId`, `StockMovementId`. Cu `TrackingMode = 3` sau `4` fiecare rând are cantitatea 1 și o serie (cu `SerialCaptureMode = 1`, S4 §3.5).

### 3.4 Diferențele (`GoodsReceiptDiscrepancy`), comisia și PV-ul (`ReceiptReport`)

| Tip diferență (`Kind`) | Când apare | Decizii posibile |
| --- | --- | --- |
| 1 Lipsă | `CountedQty < DocQty` | 3 Reclamat (implicit), 1 Acceptat (furnizorul completează sau facturează mai puțin) |
| 2 Plus | `CountedQty > DocQty` | 1 Acceptat (în toleranță sau cu drept), 2 Refuzat |
| 3 Deteriorat | Marfă numărată, dar neconformă vizual (ambalaj spart, umezeală) | 2 Refuzat (implicit), 1 Acceptat cu motiv |
| 4 Articol greșit | Alt cod decât cel comandat | 2 Refuzat, sau 1 Acceptat ca linie nouă pe `NIR-MAN` (doar dacă tranzacția permite linii manuale) |
| 5 Termen prea scurt | Termen rămas sub `ReceiptMinShelfLifePct` (3.6) | 2 Refuzat (implicit), 1 Acceptat cu dreptul `Purch.Receipt.AcceptShortShelfLife` |

Câmpuri: `LineId`, `Kind`, `Qty`, `Decision` (1 Acceptat, 2 Refuzat, 3 Reclamat), `ReasonId` (nomenclator Nucleu, tip „Diferență recepție”), `Notes`, fotografiile (atașamente Nucleu). Cantitatea refuzată a liniei = Σ diferențe cu decizia 2. Diferențele reclamate cer furnizorului o completare sau o notă de credit; ele nu intră în stoc și nu se facturează.

**Comisia de recepție** se definește pe gestiune (`purch.ReceiptCommitteeMember`: `WarehouseId`, `UserId`, `Role` 1 președinte, 2 membru, `IsActive`). **Procesul-verbal de constatare** (`ReceiptReport`, seria `PVR`) se creează din NIR când `CommitteeRequired = 1`: `ReceiptId`, `ReportDate`, membrii (`ReceiptReportMember`: utilizator sau persoană externă, de exemplu delegatul transportatorului, cu rol și semnătura consemnată), constatările (copiate din diferențe, editabile), concluzia, `SentToSupplierAt`. PV-ul se tipărește pentru semnare și se trimite furnizorului pe email prin outbox (Nucleu fluxul 4.7), cu fotografiile atașate.

### 3.5 Returul la furnizor (`SupplierReturn`, `SupplierReturnLine`)

| Câmp | Regulă |
| --- | --- |
| `Number` | Seria `RT`, la validare; tipărit ca aviz de însoțire a mărfii |
| `PartnerId`, `WarehouseId` | Furnizorul și gestiunea din care iese marfa (pe `RT-NIR`, gestiunea NIR-ului) |
| `ReturnDate` | Data ieșirii; luna deschisă în Stocuri |
| `ReasonId` | Motivul (neconformitate calitate, eroare de livrare, retur comercial convenit); obligatoriu (`53320`) |
| `ExpectCreditNote` | 1 implicit: se așteaptă factura de stornare de la furnizor (A5, `FS-RT`); 0 doar pentru retur fără valoare (ambalaje, schimb pe loc) |
| `TransportUit`, `VehicleNo`, `DriverName` | Pentru avizul de însoțire și e-Transport |
| `Status` | 1 Ciornă · 3 Validat · 4 Stornat · 9 Anulat |

**Retururile de la clienți destinate furnizorului.** Decizia de calitate „retur la furnizor” din Vânzări V6 apare în lista „De returnat la furnizor” (citită prin `ICustomerReturnQueries.GetPendingSupplierReturnsAsync`), din gestiunea de retur. Returul se face cu `RT-MAN`, cu lotul și motivul preluate, iar `CustomerReturnLineId` se păstrează pe linie pentru trasabilitate (precizare din Vânzări V6, 27.11.2026).

Linia: `ItemVariantId`, `UomId`, `Qty`, `QtyBase`, `LotId`, `SerialId`, `SourceReceiptLineId` (pe `RT-NIR`), `UnitPrice` și `UnitCostRon` (copiate din linia NIR-ului, 4.11), `VatRateId`. Lotul și seria trebuie să provină din NIR-ul sursă (`53318`).

### 3.6 Setările A4 (completare la `PurchaseSettings`, A1 §3.7)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `BlindReceipt` | 0 | Existentă în A1; A4 adaugă precedența: tranzacție (dacă e setată explicit) → firmă |
| `BlindRecountAllowed` | 1 | După închiderea numărării, liniile cu diferențe se pot renumăra o dată, înainte de a vedea cantitatea așteptată |
| `ReceiptCommitteeMode` | 1 | 0 niciodată · 1 doar când există diferențe reclamate sau refuzate · 2 și la orice NIR peste `ReceiptCommitteeAboveRon` |
| `ReceiptCommitteeAboveRon` | — | Pragul pentru modul 2 (valoare fără TVA, în lei) |
| `ReceiptMinShelfLifePct` | 0 | Termenul rămas la recepție, în procente din durata totală (`ShelfLifeDays`, S4), sub care apare diferența „termen prea scurt” (de exemplu 66 pentru regula „două treimi”). 0 = fără verificare. Se poate suprascrie pe furnizor (`SupplierTerms.ReceiptMinShelfLifePct`) |
| `ReceiveServicesOnReceipt` | 0 | 1: liniile de servicii ale comenzii pot fi confirmate pe NIR (4.9); 0: serviciile le consumă doar factura |
| `ReceiptPhotoRequired` | 0 | 1: diferențele de tip deteriorat cer cel puțin o fotografie |
| `TransportUitCheck` | 1 | 0 fără verificare · 1 avertizare · 2 blocare, pentru retururile cu articole marcate e-Transport fără cod UIT (9) |

Furnizorul primește (în `SupplierTerms`, A1 §3.4) `ReceiptMinShelfLifePct NULL`, pe lângă `ReceiptTolerancePct` deja existent.

### 3.7 Stările NIR-ului

| Stare | Cum se ajunge | Ce se poate face |
| --- | --- | --- |
| 1 Ciornă | Creare (manuală sau generată) | Modificare liberă; legăturile spre sursă sunt „în ciornă” și rezervă cantitatea (Nucleu §3.6 regula 5) |
| 2 În numărare | Doar recepția oarbă: deschiderea numărării | Gestionarul scanează și numără; nu vede `DocQty` și `ExpectedQty`; „Termină numărarea” → Ciornă, cu diferențele calculate |
| 3 Validat | Validarea (4.6) | Tipărire, PV, factură (A5), retur (4.11), stornare (4.10) |
| 4 Stornat | Stornarea integrală | Doar citire |
| 9 Anulat | Anularea unei ciorne | Doar citire; legăturile în ciornă se șterg (Nucleu) |

Starea „parțial facturat” sau „returnat parțial” nu e stare: se calculează din legăturile `INVOICE` și `RETURN`.

## 4. Fluxuri principale

### 4.1 Sosirea mărfii și avizul

1. Camionul sosește. Gestionarul sau portarul deschide „Recepții de azi” (ecranul `a4-01`): comenzile cu livrare așteptată azi sau întârziate, din `IPurchaseOrderSupply`, plus avizele sosite și NIR-urile în lucru.
2. Pe comanda găsită alege „Înregistrează avizul” (`AV-CF`): numărul și data avizului furnizorului, mașina, șoferul, UIT-ul (dacă transportul e monitorizat în e-Transport). Liniile se preiau din comandă (restul `DELIVER`) și se corectează la cantitățile de pe aviz. Cu `AV-MAN` avizul se înregistrează fără comandă.
3. Avizul e opțional (decizia 2). Când furnizorul aduce factura odată cu marfa, NIR-ul se face din factură (`NIR-FF`, declanșat din A5) sau direct din comandă, cu factura înregistrată ulterior.

### 4.2 NIR-ul din comandă sau din aviz

1. „NIR nou” pe aviz (`NIR-AV`) sau pe comandă (`NIR-CF`) deschide asistentul de generare din Nucleu (`GenerateFromSourcesCommand<GoodsReceipt>`), cu filtrele `SAME_SUPPLIER`, `SAME_WAREHOUSE`, `STOCK_LINES_ONLY` (A3 §6). Se pot alege mai multe comenzi sau avize ale aceluiași furnizor.
2. **Gestiunea** se propune în ordinea: gestiunea comenzii (`DeliveryWarehouseId`) → regula depozitului pentru grupa articolului (S1 §3.8) → `SupplierTerms.DefaultWarehouseId` → `PurchaseSettings.DefaultReceiptWarehouseId`. Dacă liniile ajung în gestiuni diferite, se creează câte un NIR pe gestiune (regula 1).
3. Comportamentele `GoodsReceipt.FromOrder` și `GoodsReceipt.FromDeliveryNote` mapează linia: variantă, UM, preț, reducere, TVA, centru de cost, proiect; `DocQty` din aviz (sau restul comenzii, fără aviz), `ExpectedQty` = restul sursei, `CountedQty` = `AcceptedQty` = `DocQty` când recepția nu e oarbă. Lotul și termenul declarate pe aviz se propun pe rândul de urmărire. Legăturile `RECEIVE` se scriu „în ciornă”; pe `NIR-AV` se consumă și linia de comandă din spatele avizului (A1 §3.2).
4. Gestionarul verifică fizic, corectează cantitățile numărate, completează loturile (4.4) și decide diferențele (4.5).
5. Validează (4.6).

Exemplu (ecranul `a4-02`): pe 26.11, seara, sosește restul comenzii `CF 2280` de la Izvorul Carpaților, cu avizul `IC 5530`: 161 de baxuri de APA05 (1.932 buc, cu 12 peste restul de 1.920) și 600 buc APA15. Andreea Pop generează NIR-ul cu tranzacția `NIR-CF-PAL` (va primi numărul 4487 la validare; în ciornă se identifică prin furnizor și aviz) (toleranță 2%, A1 §4.1), deci plusul de 12 buc (0,6%) e acceptat fără drept special. Pe 27.11 dimineață scanează etichetele GS1 ale paleților: APA05 lotul `L2611-26`, fabricat pe 24.11.2026, termen 24.11.2027; APA15 lotul `L2611-27`, termen 25.11.2027. Ambele modele au `ReceiptQuarantine = 1`, deci loturile vor intra în carantină. Valoarea: 1.932 × 1,2100 = 2.337,72 + 600 × 2,0500 = 1.230,00, total 3.567,72 lei fără TVA.

### 4.3 Recepția oarbă

Folosită când firma vrea ca gestionarul să numere efectiv, nu să confirme o cifră (decizia 3). Se activează pe tranzacție (de exemplu o copie `NIR-AV-ORB`) sau pe firmă.

1. NIR-ul generat intră în starea „În numărare”. Gestionarul vede doar articolele (cod, denumire, codul furnizorului, UM, ambalajul), fără cantitatea de pe aviz, fără cea comandată și fără prețuri (dacă nu are `Purch.PriceList.View`).
2. Numără: tastează cantitatea sau scanează (fiecare scanare GS1 adaugă cantitatea ambalajului citit, `IGs1BarcodeParser`, S4 §3.8; GTIN-ul identifică varianta și UM-ul, AI 30 sau 37 cantitatea). Un cod necunoscut se poate asocia pe loc cu o variantă, dacă utilizatorul are `Purch.SupplierItem.Manage`, și se salvează în catalogul furnizorului (A1 §3.5).
3. „Termină numărarea” fixează `CountedQty` și trece NIR-ul în Ciornă. Abia acum apar `DocQty` și diferențele.
4. Cu `BlindRecountAllowed = 1`, gestionarul poate cere o singură renumărare a liniilor cu diferențe: liniile respective își ascund din nou cantitatea așteptată, iar a doua numărare înlocuiește prima (ambele rămân în istoric).
5. Validarea înainte de închiderea numărării e refuzată (`53305`).

### 4.4 Loturi, termene, serii și carantină

- **Captarea**: pe fiecare linie cu `TrackingMode` ≥ 1 se completează unul sau mai multe rânduri de urmărire. Cititorul GS1 completează varianta (AI 01), lotul (AI 10), termenul (AI 17), data fabricației (AI 11) și seria (AI 21) dintr-o singură scanare. Cu `LotNumbering = 2` (S4) lotul se generează la validare și câmpul rămâne gol.
- **Termenul propus**: dacă lipsește de pe etichetă și modelul are `ShelfLifeDays`, termenul = data fabricației + durata (S4 §3.2). Termenul înaintea datei recepției cere dreptul `Inv.Lot.ReceiveExpired` (S4).
- **Termenul rămas la recepție**: cu `ReceiptMinShelfLifePct` > 0, dacă `(ExpiryDate − ReceiptDate) / ShelfLifeDays` < prag, linia primește automat diferența „termen prea scurt” cu decizia propusă Refuzat.
- **Seriile**: cu `SerialCaptureMode = 1` se scanează fiecare serie (un rând pe serie; dubluri refuzate de S4, `54460` pentru lipsă); cu `SerialCaptureMode = 2` linia intră doar pe cantitate (S4 §3.5).
- **Carantina**: NIR-ul doar afișează că loturile vor intra în carantină (din `ReceiptQuarantine`, S4 §3.1); starea o pune Stocuri la `PostAsync` (S4 §4.1). Responsabilul de calitate primește notificarea „lot nou în carantină” și eliberează sau blochează lotul în S4. Pentru articolele fără lot care cer carantină, regula S4 rămâne: carantina se face prin gestiune dedicată, deci NIR-ul se face direct în gestiunea de carantină.

### 4.5 Diferențe, comisie și proces-verbal

1. La salvare, A4 compară pe fiecare linie `DocQty`, `CountedQty` și ce a marcat gestionarul ca neconform, și propune rândurile de diferență cu decizia implicită din 3.4.
2. **Toleranța**: plusul acceptat peste restul sursei este verificat față de toleranța tranzacției (Nucleu `50027` pentru consumul sursei). Dacă depășește, validarea e refuzată (`53303`), cu excepția utilizatorului cu `Purch.Receipt.AcceptOverTolerance`, care acceptă cu motiv (decizia 6). Alternativa e refuzul plusului.
3. **Comisia**: dacă `CommitteeRequired = 1`, validarea cere PV-ul cu cel puțin doi membri ai comisiei gestiunii, dintre care un președinte (`53307`). Membrii se propun din comisia gestiunii; se poate adăuga un delegat al transportatorului sau al furnizorului (nume, act de identitate, opțional).
4. **PV-ul** primește număr din seria `PVR` la validarea NIR-ului, se tipărește pentru semnare și se trimite furnizorului (buton „Trimite furnizorului”, email cu PDF-ul și fotografiile). Furnizorul primește lista lipsurilor și a refuzurilor, cu cererea de completare sau de notă de credit.
5. Linia comenzii rămâne cu restul deschis (regula 5), iar urmărirea din A3 o arată ca restanță. Dacă furnizorul anunță că nu mai livrează, achizitorul închide restul pe comandă.

Exemplu (ecranele `a4-03` și `a4-04`): pe 27.11 sosește de la Furnizor Ambalaje SRL avizul `FA 3107` pentru comanda `CF 2289`: 200 role FOLIE23 la 38,5000 și 960 buc BANDA48 la 4,1000. Tranzacția folosită de Andreea Pop este `NIR-AV-ORB`, deci numără fără să vadă avizul: 200 role FOLIE23, dintre care 4 cu miezul strivit, și 948 buc BANDA48. După „Termină numărarea” apar diferențele: FOLIE23, 4 role deteriorate (refuzate, cu fotografii); BANDA48, lipsă 12 buc (reclamate). Renumărarea BANDA48 confirmă 948. Cum există diferențe refuzate și reclamate, NIR 4490 cere comisie: Gheorghe Ilie (președinte), Andreea Pop, Mihai Stan, plus șoferul Vasile Rus ca delegat al transportatorului. Intră în stoc 196 role FOLIE23 (7.546,00 lei) și 948 buc BANDA48 (3.886,80 lei), total 11.432,80 lei. PV-ul `PVR 0017` pleacă la furnizor; pe `CF 2289` rămân deschise 4 role și 12 buc.

### 4.6 Validarea

Comanda `ValidateGoodsReceipt` rulează într-o singură tranzacție SQL (`NET-07`):

1. Verificări: stare Ciornă, numărare închisă (`53305`), gestiune și dată valide (`53302`, `53309`, `53315`), curs (`53313`), urmărire completă (`53304`), diferențe decise (`53306`), comisie (`53307`), toleranță (`53303`), termen (`53312`), preț cu amănuntul pentru gestiunile la amănunt (Stocuri `54008`, verificat înainte, ca mesajul să apară pe linie).
2. Numărul din seria `NIR` (Nucleu fluxul 4.2) și, dacă e cazul, numărul PV-ului din seria `PVR`.
3. `IStockLedger.PostAsync` cu un `StockMovementRequest` pe fiecare rând de urmărire al liniilor stocabile acceptate: `MovementTypeCode = RECEIPT_PURCHASE`, `SourceLineId` = Id-ul rândului de urmărire, `SourceRole = 1`, `InboundUnitCost` = `UnitCostRon`, `NewLotCode` și `ExpiryDate` (sau `SerialId` nou, prin S4), `CostCenterId`, `ProjectId`. Răspunsul (`PostedMovementDto`) completează `StockMovementId`, `LotId`, `SerialId` pe rândurile de urmărire.
4. Activarea legăturilor `RECEIVE` (Nucleu `IDocumentLinkService`), închiderea restului pe liniile marcate (`CloseSourceRemainder`) și închiderea automată sub `UnderDeliveryAutoClosePct`.
5. Istoricul stării (`core.DocumentStatusLog`) și evenimentul `ReceptieNIRValidata` în outbox (5.1), cu blocul contabil din 4.7. Mișcarea de stoc își scrie singură `MiscareStocValidata` (S2).
6. Dacă oricare pas eșuează, nu se salvează nimic.

După commit: A3 recalculează starea comenzii (închidere automată, livrări primite), avizul trece în „Recepționat” dacă e consumat integral, responsabilul de calitate e notificat pentru loturile în carantină, iar solicitanții referatelor legate (A2) primesc „marfa a sosit”.

### 4.7 Contarea recepției: contul `408` ca tampon uniform

Decizia 9. Contarea o face C2, din blocurile `Accounting` ale evenimentelor (`AccountingPayload` v1); A4 doar trimite sumele și cheile de cont.

| Moment | Notă | Sursa blocului |
| --- | --- | --- |
| NIR validat (gestiune la cost) | `371` (cheia `Stock`) = `408` (cheia nouă `GoodsReceivedNotInvoiced`), la costul mișcării | `MiscareStocValidata` (`RECEIPT_PURCHASE`, componenta `Cost`) |
| NIR validat (gestiune la amănunt) | `371` la `RetailAmount` = `408` costul + `378` adaosul + `4428` TVA-ul inclus în prețul de vânzare (S3 §3.7) | `MiscareStocValidata` |
| NIR validat, cantitatea încă nefacturată | `4428` (cheia `VatNotDue`) = `408`, TVA-ul de cumpărare al liniilor | `ReceptieNIRValidata` (componenta `Vat`) |
| Factura validată (A5), pentru liniile recepționate | `408` = `401`, valoarea NIR-ului; `4426` = `4428` TVA-ul preluat de pe NIR; diferența de preț față de NIR, prin `CA` (S3) | `FacturaFurnizorValidata` |
| Factura înaintea NIR-ului (`NIR-FF`, `FF-REC`) | Factura: `408` = `401` și `4426` = `401`. NIR-ul: `371` = `408`, fără partea de TVA (cantitatea e deja facturată) | Ambele |
| Retur validat | `408` = `371` la costul NIR-ului (cost impus, 4.11); dacă linia NIR-ului nu era facturată, și `408` = `4428` TVA-ul | `MiscareStocValidata` (`RETURN_TO_SUPPLIER`), `ReturFurnizorValidat` |
| Factura de stornare pentru retur (A5) | `401` = `408` și `401` = `4426` (cu minus) | `FacturaFurnizorValidata` (storno) |
| Confirmare de prestare (4.9) | `6xx` (cheia `Expense`, după articol) = `408`; plus `4428` = `408` TVA-ul | `ReceptieNIRValidata` |

Astfel soldul contului `408` = recepții nefacturate − facturi nerecepționate, oricare ar fi ordinea documentelor (D2 din A1). A4 implementează `ISubledgerBalanceProvider` pentru rolul nou `GoodsReceivedNotInvoiced` (C3 §6): întoarce, la sfârșitul lunii, valoarea NIR-urilor nefacturate minus valoarea facturilor nerecepționate, pe furnizor; C3 o compară cu soldul `408`, iar diferența blochează închiderea, ca la stocuri. Tratamentul de final de lună al facturilor nerecepționate (marfa în curs de aprovizionare) se precizează în A5.

### 4.8 Recepția în gestiunile la preț cu amănuntul

Gestiunile cu `ValuationBasis = 2` (S1 §3.1) au prețul de intrare din lista cu amănuntul a gestiunii (`RetailPriceListId`, Vânzări 02.03). NIR-ul afișează pe fiecare linie prețul de vânzare cu TVA, valoarea la amănunt, TVA-ul inclus, adaosul în lei și în procente, ca pe formularul NIR pentru unitățile de desfacere cu amănuntul (decizia 11).

- Varianta fără preț în listă nu se poate recepționa (Stocuri `54008`); NIR-ul arată eroarea pe linie înainte de validare și deschide, pentru cine are dreptul, lista de prețuri din Vânzări.
- Adaosul negativ (preț de vânzare fără TVA sub cost) nu blochează, dar cere confirmare explicită la validare.
- Valoarea finală (`RetailAmount` pe mișcare) o calculează Stocuri la data recepției; NIR-ul păstrează valorile afișate pentru tipărire.

Exemplu (ecranul `a4-05`, formularul tipărit): NIR 4492 în gestiunea MAG-CJ1 (magazinul din Cluj), din avizul `IC 5538` al Izvorul Carpaților: 240 buc APA05 la 1,1600 (cost 278,40) cu prețul de vânzare 2,49 lei (TVA 11% inclus) și 120 buc APA15 la 1,9800 (cost 237,60) cu 3,99 lei. Valoarea la amănunt 1.076,40 lei, TVA inclus 106,67, adaos 453,73 (87,93% față de cost). Nota C2: `371` 1.076,40 = `408` 516,00 + `378` 453,73 + `4428` 106,67; separat, TVA-ul de cumpărare `4428` = `408` 56,76.

### 4.9 Servicii: confirmarea prestării

Implicit (`ReceiveServicesOnReceipt = 0`) liniile de servicii ale comenzii nu apar pe NIR (filtrul `STOCK_LINES_ONLY`); le consumă factura (`FF-CF`, A5), iar pentru închiderea comenzii linia de serviciu se socotește primită când e facturată integral (precizare la A3 §4.7). Linia „Transport” din `CF 2297` urmează acest drum.

Cu setarea 1, o copie a tranzacției `NIR-CF` cu filtrul `SERVICE_LINES_ONLY` produce o **confirmare de prestare**: un NIR cu linii de servicii, fără mișcare de stoc și fără rânduri de urmărire, contat ca cheltuială pe `408` (4.7), pe centrul de cost al liniei. E util pentru serviciile recurente facturate cu întârziere (mentenanță, pază), ca cheltuiala să intre în luna prestării.

### 4.10 Stornarea NIR-ului

Folosită pentru o greșeală de operare (gestiune greșită, cantitate tastată greșit), nu pentru o relație comercială (decizia 13).

1. Se poate storna doar un NIR validat, fără factură legată (`53310`) și fără retur (`53311`), integral sau pe linii alese.
2. Stornarea creează un **NIR de stornare**: număr nou din seria `NIR`, aceleași linii, afișate și tipărite cu minus (stocate pozitiv, cu `IsReversal = 1` pe antet, 7), `ReversalOfId` spre original, data aleasă (implicit azi; data originalului doar dacă luna lui e deschisă).
3. `IStockLedger.ReverseAsync` stornează mișcările rândurilor de urmărire, la costul lor (S2 §4.6: stornarea refolosește costul mișcărilor stornate). Dacă din lot s-a ieșit deja, verificarea de stoc negativ din S2 decide (politica „blocat” refuză).
4. Legăturile `RECEIVE` ale liniilor stornate se dezactivează, deci restul comenzii sau al avizului crește la loc; o comandă închisă automat se redeschide.
5. Evenimentul `ReceptieNIRStornata` poartă blocul contabil cu semn opus; mișcările de stornare își publică propriul `MiscareStocValidata`.
6. Originalul trece în „Stornat” la stornarea integrală; la stornarea parțială rămâne „Validat”, cu liniile stornate marcate.

### 4.11 Returul la furnizor

1. **Din NIR** (`RT-NIR`, recomandat): pe NIR-ul validat, „Retur la furnizor” preia liniile cu restul returnabil (grupul `RETURN`, nu se returnează mai mult decât s-a recepționat). Lotul și seria sunt cele din NIR (`53318`). Se pot alege și loturi aflate în carantină sau blocate: S4 permite returul la furnizor din aceste stări.
2. **Manual** (`RT-MAN`): fără NIR, cu prețul introdus și dreptul `Purch.Return.Manual`; folosit pentru stocul inițial sau pentru retururile convenite comercial pe loturi vechi.
3. **Prețul și costul**: pe `RT-NIR` prețul (pentru furnizor) și costul unitar (pentru stoc) sunt cele ale liniei NIR-ului. Mișcarea `RETURN_TO_SUPPLIER` iese **la costul impus** al NIR-ului, transmis în `InboundUnitCost` (decizia 14, precizare la S3), ca valoarea scăzută din stoc să fie egală cu ce se cere furnizorului. La CMP, Stocuri tratează ieșirea ca o intrare negativă și recalculează media; la FIFO și LIFO consumă stratul NIR-ului, când există, altfel straturile în ordinea metodei, iar diferența de cost devine ajustare (S3).
4. **Validarea**, într-o tranzacție SQL: numărul din seria `RT`, `IStockLedger.PostAsync` cu `RETURN_TO_SUPPLIER` (verificarea de stoc a lotului, `53319`), activarea legăturilor `RETURN`, `ReturFurnizorValidat` în outbox, verificarea UIT (9).
5. Returul se tipărește ca **aviz de însoțire a mărfii** și se poate trimite furnizorului pe email. Cu `ExpectCreditNote = 1`, returul apare în A5 ca „retur fără factură de stornare” până când `FS-RT` îl consumă (grupul `CREDIT`).

Exemplu (ecranul `a4-04` arată și returul legat): Mihaela Dobre (Responsabil calitate) găsește la lotul `L2611-15` de APA05 din NIR 4486 (în carantină) 20 de baxuri cu sigiliul deteriorat. Le blochează în S4, iar Andreea Pop face `RT 0031` din NIR 4486: 240 buc APA05, lotul `L2611-15`, la 1,2100, valoare 290,40 lei, TVA 11% 31,94 lei. NIR 4486 e deja facturat (`FF IC 77288`), deci nota e doar `408` = `371` 290,40, iar factura de stornare a furnizorului va închide `408` și va corecta TVA-ul (A5).

### 4.12 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Marfa sosește fără nicio comandă | `NIR-MAN`, dacă rolul are tranzacția; prețul din lista furnizorului sau manual; achizitorul primește notificarea „recepție fără comandă”. Obligativitatea comenzii se verifică la factură (A5, `RequiresOrder`) |
| Furnizorul livrează alt cod (substituție) | Linia comandată primește diferența „articol greșit” (refuzat sau rămasă deschisă), iar codul livrat se recepționează ca linie manuală, dacă tranzacția permite (A3 §4.9) |
| Furnizorul livrează în altă UM (bax în loc de bucată) | Cantitatea se convertește prin setul de UM al articolului; dacă UM-ul livrat nu e în set, linia se recepționează în UM de bază |
| Un aviz pentru două comenzi | `AV-CF` acceptă mai multe comenzi ale aceluiași furnizor; NIR-ul din aviz consumă fiecare linie de comandă prin legătura avizului |
| Două NIR-uri în ciornă din același aviz | Primul rezervă cantitatea (legături „în ciornă”); al doilea vede restul redus și, la depășire, `50027` |
| NIR cu dată în trecut, după ieșiri din aceeași variantă | Permis în luna deschisă; Stocuri pune cheia în coada de recalculare (S3 §4.3) |
| Comanda în EUR | NIR-ul la cursul BNR al zilei recepției; diferența față de cursul facturii se tratează în A5 (decizia 10) |
| Ambalaje returnabile (paleți EUR) primite cu marfa | Linie separată pe articolul de ambalaj, în gestiunea de ambalaje sau „la terți” (custodie, Stocuri S2); garanția SGR a recipientelor e linie fără stoc, pe factură (A5), nu intră în costul mărfii |
| Stocuri nelicențiat | NIR-ul se validează fără mișcare de stoc; contarea se face direct din `ReceptieNIRValidata` (`6xx` sau `371` după tipul articolului, cu `408`) |
| Lotul din NIR a fost deja vândut parțial, apoi se descoperă o neconformitate | Returul pe restul din stoc; pentru partea vândută, retragerea din S4 (`Recall`) |
| Gestionarul validează din greșeală în gestiunea greșită | Stornare (4.10) și NIR nou, dacă nu există factură; altfel transfer între gestiuni (Stocuri S2) |
| Furnizorul completează lipsa cu o livrare ulterioară | NIR nou din restul comenzii, fără legătură specială cu PV-ul; PV-ul se marchează „rezolvat prin completare” manual |

**Jurnalul de excepții (precizare din Achiziții A7, 27.11.2026).** Excepțiile aprobate în această parte se scriu în `purch.ExceptionLog` prin `IPurchaseExceptionWriter.WriteAsync`, în aceeași tranzacție cu documentul care le aprobă (A7 §3.5): recepția peste comandă în toleranță; decizia pe o diferență la recepție (refuz, acceptare, reclamație, cu PV-ul). Se scriu documentul, linia, furnizorul, valoarea în lei, cine a cerut, cine a aprobat și motivul.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `AvizFurnizorInregistrat` v1 | Salvarea avizului | `CompanyId`, `DeliveryNoteId`, `PartnerId`, `WarehouseId`, `SupplierDocNo`, `ArrivedAt`, liniile (variantă, cantitate de bază) | A3 (livrarea „sosită”), WMS (de recepționat) |
| `ReceptieNIRValidata` v1 | Validarea NIR-ului | `ReceiptId`, `Number`, `PartnerId`, `WarehouseId`, `ReceiptDate`, `CurrencyCode`, `ExchangeRate`, liniile (variantă, cantitate acceptată, refuzată, preț, cost în lei, loturi), legăturile sursă, blocul `Accounting` (componenta `Vat` pe cantitatea nefacturată; `Expense` pentru confirmările de prestare) | A3 (închidere, livrări), A2 (notificare solicitanți), A5 (de facturat), A7, Finanțe C2, Raportare |
| `ReceptieNIRStornata` v1 | Stornarea | `ReceiptId`, `ReversalReceiptId`, liniile stornate, blocul `Accounting` cu semn opus | A3 (redeschidere), A5, C2 |
| `DiferenteReceptieConstatate` v1 | Validarea unui NIR cu diferențe | `ReceiptId`, `ReportId`, diferențele (tip, cantitate, decizie, motiv) | A7 (calitatea livrărilor), A5 (de reclamat), notificarea achizitorului |
| `ReturFurnizorValidat` v1 | Validarea returului | `ReturnId`, `Number`, `PartnerId`, liniile (variantă, lot, cantitate, preț, cost), `ExpectCreditNote`, blocul `Accounting` (componenta `Vat`, doar pentru liniile NIR nefacturate) | A5 (aștepta `FS-RT`), A7, C2 |

### 5.2 Consumate

| Eveniment | De la | Ce face A4 |
| --- | --- | --- |
| `ComandaFurnizorModificata` v1, `ComandaFurnizorInchisa` v1 | A3 | Avertizează NIR-urile în ciornă din comanda modificată sau închisă (cantitatea așteptată se recalculează la deschidere) |
| `FacturaFurnizorValidata` v1 | A5 | Invalidează cache-ul listei NIR (starea „facturat”, calculată); nimic de scris |
| `StareLotModificata` v1 | Stocuri S4 | Lista „Recepții de azi” arată loturile eliberate sau blocate din carantină |
| `FurnizorBlocat` v1 | A1 | NIR-urile în ciornă ale furnizorului primesc avertizarea; recepția mărfii deja sosite rămâne permisă |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A4)

```csharp
public enum GoodsReceiptStatus : byte { Draft = 1, Counting = 2, Validated = 3, Reversed = 4, Cancelled = 9 }
public enum ReceiptDiscrepancyKind : byte { Shortage = 1, Surplus = 2, Damaged = 3, WrongItem = 4, ShortShelfLife = 5 }
public enum ReceiptDiscrepancyDecision : byte { Accepted = 1, Refused = 2, Claimed = 3 }
public enum SupplierDocKind : byte { DeliveryNote = 1, Invoice = 2, None = 3 }

public sealed record GoodsReceiptSummaryDto(long Id, string? Number, int PartnerId, int WarehouseId, DateOnly ReceiptDate,
    GoodsReceiptStatus Status, string CurrencyCode, decimal NetAmount, decimal NetAmountRon, decimal InvoicedPct, bool HasDiscrepancies);

// Citit de A5 (factura din NIR-uri, potrivirea comandă–NIR–factură) și de A6 (repartizarea costurilor)
public interface IGoodsReceiptReader
{
    Task<GoodsReceiptSummaryDto?> GetAsync(long receiptId, CancellationToken ct);
    Task<IReadOnlyList<GoodsReceiptLineForInvoiceDto>> GetLinesForInvoiceAsync(long receiptId, CancellationToken ct);
}
public sealed record GoodsReceiptLineForInvoiceDto(long LineId, int ItemVariantId, int UomId, decimal AcceptedQty, decimal UnitPrice,
    decimal DiscountPct, int VatRateId, decimal UnitCostRon, long? SourceOrderLineId, IReadOnlyList<long> StockMovementIds);

// Citit de C3 (concordanța contului 408), implementează ISubledgerBalanceProvider cu rolul GoodsReceivedNotInvoiced
public interface IReceivedNotInvoiced
{
    Task<IReadOnlyList<ReceivedNotInvoicedRow>> GetAsync(int companyId, DateOnly asOf, CancellationToken ct);
}
public sealed record ReceivedNotInvoicedRow(int PartnerId, long DocumentId, string DocumentType, string Number, DateOnly Date,
    decimal NetAmountRon, decimal VatAmountRon);   // NIR nefacturat cu plus, factură nerecepționată cu minus
```

Implementarea `ILinkableDocumentSource` pentru `SupplierDeliveryNote` expune `Qty` pe linie (grupul `RECEIVE`), iar pentru `GoodsReceipt` expune `AcceptedQtyBase` (grupurile `INVOICE`, `RETURN`, `LANDED`), cu filtrele `SAME_SUPPLIER`, `SAME_WAREHOUSE`, `NOT_INVOICED`.

Comenzi interne A4 (MediatR): `SaveDeliveryNote`, `CancelDeliveryNote`, `SaveGoodsReceipt`, `StartReceiptCount`, `RecordReceiptScan`, `FinishReceiptCount`, `RequestRecount`, `DecideReceiptDiscrepancy`, `SaveReceiptReport`, `SendReceiptReport`, `ValidateGoodsReceipt`, `ReverseGoodsReceipt`, `CancelGoodsReceipt`, `SaveSupplierReturn`, `ValidateSupplierReturn`, `ReverseSupplierReturn`, `SaveReceiptCommittee`. Generarea folosește comportamentele `DeliveryNote.FromOrder`, `GoodsReceipt.FromOrder`, `GoodsReceipt.FromDeliveryNote`, `GoodsReceipt.Manual`, `SupplierReturn.FromReceipt`, `SupplierReturn.Manual` (A1 §3.2). Interogări: `ListReceiptsToday`, `ListGoodsReceipts`, `ListDeliveryNotesPending`, `ListSupplierReturns`, `GetReceiptPrint`.

## 7. Schema SQL `purch` (partea A4)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.SupplierDeliveryNote` | `Id bigint` | UX `(CompanyId, Number)`; UX `(CompanyId, PartnerId, SupplierDocKey, YEAR(SupplierDocDate))` prin coloană calculată persistată; FK `PartnerId`, `WarehouseId`, `DepotId`, `CarrierPartnerId` | `(CompanyId, Status) INCLUDE (PartnerId, ArrivedAt)` |
| `purch.SupplierDeliveryNoteLine` | `Id bigint` | UQ `(NoteId, LineNo)`; FK `NoteId`, `ItemVariantId`, `UomId` | — |
| `purch.GoodsReceipt` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `TransactionTypeId`, `PartnerId`, `WarehouseId`, `DepotId`, `CurrencyCode`, `ReversalOfId` | `(CompanyId, Status, ReceiptDate) INCLUDE (PartnerId, WarehouseId, NetAmountRon)`; `(PartnerId, ReceiptDate)` |
| `purch.GoodsReceiptLine` | `Id bigint` | UQ `(ReceiptId, LineNo)`; FK `ReceiptId`, `ItemVariantId`, `UomId`, `VatRateId`, `CostCenterId`, `ProjectId` | `(ItemVariantId) INCLUDE (ReceiptId, AcceptedQtyBase)` |
| `purch.GoodsReceiptLineTracking` | `Id bigint` | UQ `(LineId, Seq)`; FK `LineId`, `LotId`, `SerialId`; UX `(LineId, SerialNo) WHERE SerialNo IS NOT NULL` | `(LotId)` |
| `purch.GoodsReceiptDiscrepancy` | `Id bigint` | FK `LineId`, `ReasonId` | `(LineId)` |
| `purch.ReceiptReport` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; UQ `(ReceiptId)` | — |
| `purch.ReceiptReportMember` | `Id bigint` | FK `ReportId`, `UserId NULL` | — |
| `purch.ReceiptCommitteeMember` | `Id int` | UQ `(WarehouseId, UserId)`; FK `WarehouseId`, `UserId` | — |
| `purch.SupplierReturn` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `PartnerId`, `WarehouseId`, `ReasonId` | `(CompanyId, Status, ReturnDate)` |
| `purch.SupplierReturnLine` | `Id bigint` | UQ `(ReturnId, LineNo)`; FK `ReturnId`, `ItemVariantId`, `LotId`, `SerialId`, `SourceReceiptLineId` | `(SourceReceiptLineId)` |

`SupplierDocKey` e coloană calculată persistată (`UPPER(REPLACE(SupplierDocNo, ' ', ''))`), ca „IC 5530” și „ic5530” să fie același aviz pe baza case sensitive (`SQL-21`).

Exemplu de tabel complet:

```sql
CREATE TABLE purch.GoodsReceiptLine (
    Id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceiptLine PRIMARY KEY,
    ReceiptId             bigint            NOT NULL CONSTRAINT FK_GoodsReceiptLine_Receipt REFERENCES purch.GoodsReceipt(Id),
    LineNo                smallint          NOT NULL,
    ItemVariantId         int               NOT NULL CONSTRAINT FK_GoodsReceiptLine_Variant REFERENCES core.ItemVariant(Id),
    UomId                 int               NOT NULL CONSTRAINT FK_GoodsReceiptLine_Uom REFERENCES core.Uom(Id),
    UomFactor             decimal(19,6)     NOT NULL,                       -- UM de cumpărare → UM de bază, copiat la creare
    IsService             bit               NOT NULL CONSTRAINT DF_GoodsReceiptLine_IsService DEFAULT (0),
    DocQty                decimal(19,6)     NULL,
    ExpectedQty           decimal(19,6)     NULL,
    CountedQty            decimal(19,6)     NOT NULL CONSTRAINT DF_GoodsReceiptLine_Counted DEFAULT (0),
    AcceptedQty           decimal(19,6)     NOT NULL CONSTRAINT DF_GoodsReceiptLine_Accepted DEFAULT (0),
    RefusedQty            decimal(19,6)     NOT NULL CONSTRAINT DF_GoodsReceiptLine_Refused DEFAULT (0),
    AcceptedQtyBase       AS (CONVERT(decimal(19,6), AcceptedQty * UomFactor)) PERSISTED,
    UnitPrice             decimal(19,6)     NOT NULL,
    DiscountPct           decimal(9,4)      NOT NULL CONSTRAINT DF_GoodsReceiptLine_Disc DEFAULT (0),
    VatRateId             int               NOT NULL CONSTRAINT FK_GoodsReceiptLine_Vat REFERENCES core.VatRate(Id),
    NetAmount             decimal(19,4)     NOT NULL,
    NetAmountRon          decimal(19,4)     NOT NULL,
    UnitCostRon           decimal(19,6)     NOT NULL,                       -- pe UM de bază; InboundUnitCost pentru stoc
    RetailUnitPrice       decimal(19,6)     NULL,
    RetailAmount          decimal(19,4)     NULL,
    RetailVatAmount       decimal(19,4)     NULL,
    MarkupAmount          decimal(19,4)     NULL,
    CostCenterId          int               NULL CONSTRAINT FK_GoodsReceiptLine_CostCenter REFERENCES core.CostCenter(Id),
    ProjectId             int               NULL CONSTRAINT FK_GoodsReceiptLine_Project REFERENCES core.Project(Id),
    CloseSourceRemainder  bit               NOT NULL CONSTRAINT DF_GoodsReceiptLine_Close DEFAULT (0),
    Notes                 nvarchar(500)     NULL,
    RowVersion            rowversion        NOT NULL,
    CONSTRAINT UQ_GoodsReceiptLine UNIQUE (ReceiptId, LineNo),
    CONSTRAINT CK_GoodsReceiptLine_Qty CHECK (CountedQty >= 0 AND AcceptedQty >= 0 AND RefusedQty >= 0 AND AcceptedQty + RefusedQty = CountedQty),
    CONSTRAINT CK_GoodsReceiptLine_Retail CHECK ((RetailUnitPrice IS NULL AND RetailAmount IS NULL) OR (RetailUnitPrice IS NOT NULL AND RetailAmount IS NOT NULL))
);
CREATE INDEX IX_GoodsReceiptLine_Variant ON purch.GoodsReceiptLine (ItemVariantId) INCLUDE (ReceiptId, AcceptedQtyBase);
```

NIR-ul de stornare are antetul cu `IsReversal = 1` și `ReversalOfId`; liniile lui păstrează cantitățile **pozitive** (deci `CK_GoodsReceiptLine_Qty` rămâne valabil), iar semnul îl aplică procedurile: `ReverseAsync` la stoc, minusul la tipărire și în rapoarte, semnul opus în blocul contabil. Așa nu există două reguli de verificare în același tabel.

Proceduri: `purch.usp_DeliveryNote_Save`, `purch.usp_GoodsReceipt_Save` (antet, linii, urmărire, diferențe din TVP-uri, `SP-07`, cu `RowVersion`, `SP-18`), `purch.usp_GoodsReceipt_FinishCount`, `purch.usp_GoodsReceipt_ValidateChecks` (toate verificările din 4.6 pasul 1, pe seturi, întoarce lista de erori pe linii), `purch.usp_GoodsReceipt_Validate` (stare, număr, legături; apelul `PostAsync` se face din C#, în aceeași tranzacție, `NET-07`), `purch.usp_GoodsReceipt_Reverse`, `purch.usp_ReceiptReport_Save`, `purch.usp_SupplierReturn_Save`, `purch.usp_SupplierReturn_Validate`, `purch.usp_Receipts_Today` (`SP-11`), `purch.usp_ReceivedNotInvoiced` (pentru C3 și raportul `408`).

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53301 | `PURCH_RECEIPT_NOT_EDITABLE` | 409 | Modificarea unui NIR validat, stornat sau anulat |
| 53302 | `PURCH_RECEIPT_WAREHOUSE_INVALID` | 409 | Gestiune a altei firme, inactivă, blocată la intrare sau linie în altă gestiune decât antetul |
| 53303 | `PURCH_RECEIPT_OVER_TOLERANCE` | 409 | Cantitate acceptată peste restul sursei plus toleranță, fără `Purch.Receipt.AcceptOverTolerance` |
| 53304 | `PURCH_RECEIPT_TRACKING_INCOMPLETE` | 400 | Lot, termen sau serie lipsă, ori suma rândurilor de urmărire diferită de cantitatea acceptată |
| 53305 | `PURCH_RECEIPT_COUNT_NOT_FINISHED` | 409 | Validarea unei recepții oarbe înainte de închiderea numărării |
| 53306 | `PURCH_RECEIPT_DISCREPANCY_UNDECIDED` | 409 | Diferență fără decizie sau fără motiv |
| 53307 | `PURCH_RECEIPT_COMMITTEE_REQUIRED` | 409 | PV lipsă sau cu mai puțin de doi membri ai comisiei (fără președinte) când comisia e cerută |
| 53309 | `PURCH_RECEIPT_PERIOD_CLOSED` | 409 | Data contabilă într-o lună închisă la Stocuri |
| 53310 | `PURCH_RECEIPT_REVERSAL_INVOICED` | 409 | Stornarea unui NIR (sau a unor linii) cu factură legată |
| 53311 | `PURCH_RECEIPT_REVERSAL_RETURNED` | 409 | Stornarea unui NIR cu retur legat |
| 53312 | `PURCH_RECEIPT_SHELF_LIFE_SHORT` | 409 | Termen rămas sub prag acceptat fără `Purch.Receipt.AcceptShortShelfLife` |
| 53313 | `PURCH_RECEIPT_EXCHANGE_RATE_MISSING` | 409 | NIR în valută fără curs BNR la data recepției |
| 53314 | `PURCH_RECEIPT_SERVICE_NOT_ALLOWED` | 400 | Linie de serviciu pe NIR cu `ReceiveServicesOnReceipt = 0` sau pe o tranzacție de stoc |
| 53315 | `PURCH_RECEIPT_DATE_INVALID` | 400 | Data recepției în viitor sau înaintea datei comenzii sau avizului |
| 53316 | `PURCH_DELIVERY_NOTE_DUPLICATE` | 409 | Același număr de aviz al furnizorului în același an |
| 53317 | `PURCH_DELIVERY_NOTE_HAS_RECEIPT` | 409 | Modificarea liniilor unui aviz cu NIR legat |
| 53318 | `PURCH_RETURN_TRACKING_MISMATCH` | 409 | Lot sau serie pe retur care nu provine din NIR-ul sursă |
| 53319 | `PURCH_RETURN_STOCK_INSUFFICIENT` | 409 | Lotul sau varianta nu mai are stoc în gestiune pentru retur |
| 53320 | `PURCH_RETURN_REASON_REQUIRED` | 400 | Retur fără motiv |
| 53321 | `PURCH_RECEIPT_MIXED_SOURCES` | 400 | Generarea unui NIR din surse cu furnizori sau monede diferite |
| 53322 | `PURCH_TRANSPORT_UIT_REQUIRED` | 409 | Retur cu articole marcate e-Transport, peste praguri, fără cod UIT, cu `TransportUitCheck = 2` |
| 53323 | `PURCH_RECEIPT_PHOTO_REQUIRED` | 400 | Diferență „deteriorat” fără fotografie, cu `ReceiptPhotoRequired = 1` |

Codul `53308` e rezervat (prețul cu amănuntul lipsă e eroarea Stocuri `54008`).

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.DeliveryNote.Manage` | Înregistrarea și anularea avizelor |
| `Purch.Receipt.View` | NIR-urile gestiunilor la care are acces (prețurile doar cu `Purch.PriceList.View`) |
| `Purch.Receipt.Edit` | Creare, numărare, diferențe, PV |
| `Purch.Receipt.Validate` | Validarea |
| `Purch.Receipt.Reverse` | Stornarea |
| `Purch.Receipt.EditPrice` | Prețul pe `NIR-MAN` sau diferit de sursă |
| `Purch.Receipt.AcceptOverTolerance` | Acceptarea unui plus peste toleranță, cu motiv |
| `Purch.Receipt.AcceptShortShelfLife` | Acceptarea unui lot cu termen rămas sub prag |
| `Purch.Receipt.NoSupplierDoc` | NIR fără document al furnizorului |
| `Purch.ReceiptCommittee.Manage` | Comisia de recepție a gestiunii |
| `Purch.Return.Edit`, `Purch.Return.Validate`, `Purch.Return.Manual` | Returul la furnizor; returul fără NIR |

Accesul pe gestiune vine din drepturile pe gestiuni din Stocuri (S1). Roluri livrate: **Gestionar** (`DeliveryNote.Manage`, `Receipt.View`, `Receipt.Edit`, `Receipt.Validate`, `Return.Edit`), **Șef depozit** (toate ale gestionarului, plus `Receipt.Reverse`, `AcceptOverTolerance`, `ReceiptCommittee.Manage`, `Return.Validate`), **Membru comisie** (`Receipt.View`, semnarea PV-ului), **Responsabil calitate** (`Receipt.View`, `AcceptShortShelfLife`, `Return.Edit`), **Achizitor** (`Receipt.View`, `Return.Edit`, `Return.Manual`), **Contabil furnizori** (`Receipt.View`, `Receipt.EditPrice`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A4 |
| --- | --- |
| OMFP 2634/2015 (documentele financiar-contabile) | NIR-ul (formularul 14-3-1A) ca document de recepție și de înregistrare în gestiune, numerotat continuu, cu cantitatea pe document, cea recepționată, prețul și valoarea, semnat de comisie și gestionar; varianta pentru unitățile cu amănuntul cu prețul de vânzare, adaosul și TVA-ul. Diferențele se consemnează în NIR și în PV. Avizul de însoțire a mărfii (14-3-6A) pentru retur |
| OMFP 1802/2014 (reglementările contabile) | Contul `408` pentru bunurile recepționate fără factură și `4428` pentru TVA-ul neexigibil; evidența la preț cu amănuntul (`378`, `4428`) |
| Codul fiscal (TVA, art. 282) | TVA-ul devine deductibil la primirea facturii; până atunci rămâne pe `4428` (4.7) |
| Legea contabilității 82/1991 | Documentul justificativ întocmit la momentul operațiunii; corectarea doar prin stornare (4.10) |
| OUG 41/2022 (e-Transport) | Codul UIT pe aviz la recepție (informativ, pentru verificarea transportului) și pe retur, când bunurile sunt cu risc fiscal ridicat peste pragurile legale sau transportul e intracomunitar; declararea efectivă în e-Transport se face prin 02.14 Integrări ANAF |
| HG 1074/2021 (sistemul garanție-returnare) | Garanția recipientelor nu intră în costul mărfii; linia de garanție se tratează la factură (A5) |
| Legislația sanitar-veterinară și siguranța alimentelor | Lotul și termenul obligatorii pe recepție pentru articolele cu `TrackingMode = 2` (S4); trasabilitatea înapoi până la furnizor |

Integrări externe: emailul (outbox Nucleu) pentru PV și retur; cititoarele de coduri de bare (tastatură, GS1); e-Transport prin 02.14. Interne: A1 (tranzacții, catalog, toleranțe), A3 (comanda), A5 (factura consumă NIR-ul), A6 (costuri de achiziție), Stocuri S2–S4 (`IStockLedger`, loturi, carantină, preț cu amănuntul), Finanțe C2 (contare), C3 (concordanța `408`).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Recepții pe perioadă | NIR-uri pe furnizor, gestiune, articol: cantitate, valoare, diferențe | Șef depozit, Achizitor |
| Recepții nefacturate (`408`) la dată | NIR-urile nefacturate și facturile nerecepționate, pe furnizor, cu vechimea; totalul egal cu soldul `408` | Contabil furnizori, Contabil șef |
| Diferențe la recepție | Pe furnizor și tip: lipsuri, plusuri, deteriorate, termene scurte; PV-uri trimise și rezolvate | Achizitor, Șef achiziții, A7 |
| Avize nerecepționate | Avizele sosite fără NIR validat, cu vechimea | Șef depozit |
| Retururi la furnizori | Pe motiv și furnizor, cu starea notei de credit (primită sau așteptată) | Contabil furnizori, Achizitor |
| Loturi intrate în carantină | Din recepții, cu zilele petrecute în carantină și decizia (S4) | Responsabil calitate |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a4-receptie/](ecrane/a4-receptie/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Recepții de azi | Ce e așteptat azi și întârziat (din comenzi), avizele sosite, NIR-urile în lucru, loturile din carantină | `a4-01-receptii-azi` |
| NIR cu loturi și carantină | NIR 4487 din `CF 2280`: scanarea GS1, loturile și termenele, plusul în toleranță, carantina | `a4-02-nir-loturi` |
| Recepție oarbă | NIR 4490 în numărare: articolele fără cantitate așteptată, scanările, renumărarea | `a4-03-receptie-oarba` |
| Diferențe, PV și retur | NIR 4490 după numărare: diferențele cu decizia, comisia, `PVR 0017`; returul `RT 0031` | `a4-04-diferente-pv` |
| NIR tipărit (amănunt) | Formularul NIR 4492 pentru MAG-CJ1, cu prețul de vânzare, adaosul și TVA-ul, plus nota contabilă | `a4-05-nir-amanunt` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A4-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A4-A1 | NIR 4487 din `CF 2280` cu 1.932 buc APA05 (12 peste rest) e acceptat cu `NIR-CF-PAL` (2%) și refuzat cu `NIR-CF` (0%, `53303`); cu `AcceptOverTolerance` și motiv trece | Test de integrare |
| A4-A2 | Validarea NIR 4487 face, într-o singură tranzacție: numărul, două mișcări `RECEIPT_PURCHASE` cu loturile noi în carantină, legăturile active, închiderea `CF 2280`, evenimentul; o eroare la `PostAsync` anulează tot | Test de integrare (cu Stocuri) |
| A4-A3 | La recepția oarbă, API-ul nu întoarce `DocQty`, `ExpectedQty` și prețurile cât NIR-ul e „În numărare”; validarea înainte de închiderea numărării e refuzată (`53305`) | Test de integrare |
| A4-A4 | NIR 4490: 4 role refuzate și 12 buc reclamate nu intră în stoc și nu consumă comanda; `CF 2289` rămâne cu 4 role și 12 buc de primit; fără PV cu doi membri validarea e refuzată (`53307`) | Test de integrare |
| A4-A5 | Linie cu lot fără termen la `TrackingMode = 2` sau cu suma rândurilor diferită de cantitatea acceptată e refuzată (`53304`); cu `ReceiptMinShelfLifePct = 66`, un lot cu jumătate din termen primește diferența „termen prea scurt” | Test unitar + integrare |
| A4-A6 | Contarea: NIR 4487 dă `371` = `408` 3.567,72 și `4428` = `408` 392,45; după factura `FF-NIR`, soldul `408` al furnizorului pentru NIR 4487 e 0 | Test de integrare (cu C2, A5) |
| A4-A7 | NIR 4492 în MAG-CJ1: valoarea la amănunt 1.076,40, adaosul 453,73, TVA inclus 106,67; o variantă fără preț în listă e refuzată (`54008`) | Test de integrare (cu Stocuri S3) |
| A4-A8 | Stornarea unui NIR nefacturat creează NIR-ul de stornare (minus la tipărire și în stoc), reface restul comenzii și redeschide comanda închisă automat; stornarea unui NIR facturat e refuzată (`53310`) | Test de integrare |
| A4-A9 | `RT 0031` din NIR 4486 iese la costul NIR-ului (1,2100) chiar dacă CMP-ul gestiunii e altul; nu se poate returna alt lot (`53318`) sau mai mult decât s-a recepționat (Nucleu `50027`) | Test de integrare (cu Stocuri S3) |
| A4-A10 | `IReceivedNotInvoiced` la 30.11 întoarce exact soldul `408` din C2 pentru datele demo; un NIR necontat produce diferența raportată de C3 | Test de integrare (cu C3) |
| A4-A11 | Două ciorne de NIR din același aviz: a doua vede restul redus; numărul de aviz „ic 5530” e respins ca dublură a „IC 5530” (`53316`) | Test de integrare |
| A4-A12 | Toate procedurile A4 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Test de integrare |

Criteriile A4-A2, A4-A4 și A4-A6 sunt critice: ele decid ce intră în stoc, ce datorează firma furnizorului și cum arată contul `408`.

## Precizări la alte documente (aplicate pe 27.11.2026)

| Document | Precizarea |
| --- | --- |
| Stocuri S2 §3.2 | Documentul sursă al mișcării `RETURN_TO_SUPPLIER` este `SupplierReturn` (A1 §3.1), nu `GoodsReturn` |
| Stocuri S3 §3.1 | Pentru `RETURN_TO_SUPPLIER`, câmpul `InboundUnitCost` (când e completat) este costul impus al ieșirii: CMP tratează ieșirea ca intrare negativă, FIFO și LIFO consumă stratul NIR-ului sursă, diferența devine ajustare (decizia 14) |
| Finanțe C2 §3.2–3.3 (modelele și cheile de cont) | Rândul „Recepție de la furnizor (Achiziții)”: `371` = `408`; cheia de cont nouă `GoodsReceivedNotInvoiced` (implicit `408`) și `VatNotDue` (implicit `4428`); `408` intră în conturile cu `ManualPosting = 0` |
| Finanțe C3 | Rolul nou `GoodsReceivedNotInvoiced` în `ISubledgerBalanceProvider`, cu `408`, implementat de Achiziții (4.7) |
| A1 §3.7 | `QuarantineOnReceipt` citește `ReceiptQuarantine` (numele din S4), nu `RequiresQuarantine`; setările noi din 3.6; `SupplierTerms.ReceiptMinShelfLifePct` |
| A3 §4.7 | O linie de serviciu se socotește primită când e facturată integral (sau confirmată pe NIR, 4.9) |

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Câte gestiuni pe un NIR | Un NIR = un furnizor și o gestiune; când liniile merg în gestiuni diferite, generatorul creează câte un NIR pe gestiune, din aceeași sursă | 2 regula 1, 4.2 | Decis de Aurelian (27.11.2026) |
| 2 | Avizul furnizorului | Opțional; se înregistrează la sosire (număr, mașină, UIT), nu mișcă stocul și servește la comparația „pe document” față de „numărat” | 3.1, 4.1 | Decis de Aurelian (27.11.2026) |
| 3 | Recepția oarbă | Se activează pe tranzacție sau pe firmă; gestionarul nu vede cantitățile și prețurile până închide numărarea; o singură renumărare a liniilor cu diferențe | 3.6, 4.3 | Decis de Aurelian (27.11.2026) |
| 4 | Cantitățile pe linie | Pe document, numărată, acceptată, refuzată; doar acceptatul intră în stoc, consumă sursa și se facturează | 2 regula 2, 3.3 | Decis de Aurelian (27.11.2026) |
| 5 | Diferențele | Cinci tipuri (lipsă, plus, deteriorat, articol greșit, termen prea scurt), fiecare cu decizie explicită (acceptat, refuzat, reclamat); restul comenzii rămâne deschis implicit | 3.4, 4.5 | Decis de Aurelian (27.11.2026) |
| 6 | Plusul peste toleranță | Refuzat la validare (`53303`), acceptat doar cu dreptul `Purch.Receipt.AcceptOverTolerance` și motiv | 4.5 | Decis de Aurelian (27.11.2026) |
| 7 | Comisia și PV-ul | Comisie pe gestiune; PV cerut implicit doar la diferențe refuzate sau reclamate (opțional și peste un prag valoric), cu minimum doi membri și un președinte; trimis furnizorului pe email cu fotografii | 3.4, 3.6, 4.5 | Decis de Aurelian (27.11.2026) |
| 8 | Lot, termen, serie și carantina | Rânduri de urmărire pe linie, completate prin scanare GS1; verificarea termenului rămas la recepție (setare în procente, implicit oprită); carantina după model, pusă de Stocuri | 3.3, 4.4 | Decis de Aurelian (27.11.2026) |
| 9 | Contarea recepției | Contul `408` ca tampon uniform: NIR `371` = `408` (+ `4428` = `408` TVA-ul, dacă nu e facturat), factura `408` = `401`, returul `408` = `371`; concordanța `408` la închiderea lunii | 4.7 | Decis de Aurelian (27.11.2026) |
| 10 | Cursul la NIR-ul în valută | Cursul BNR al zilei recepției; diferențele față de factură se tratează în A5, fără să se modifice NIR-ul | 3.2, 4.12 | Decis de Aurelian (27.11.2026) |
| 11 | Recepția la amănunt | Prețul de vânzare obligatoriu în lista gestiunii; NIR-ul arată adaosul și TVA-ul inclus; adaosul negativ cere doar confirmare | 4.8 | Decis de Aurelian (27.11.2026) |
| 12 | Serviciile | Implicit le consumă doar factura; opțional, confirmare de prestare pe NIR (fără stoc, contată pe cheltuială și `408`) | 3.6, 4.9 | Decis de Aurelian (27.11.2026) |
| 13 | Stornarea NIR-ului | Doar pentru NIR nefacturat și fără retur, integral sau pe linii, prin NIR de stornare cu număr nou; altfel retur la furnizor | 4.10 | Decis de Aurelian (27.11.2026) |
| 14 | Returul la furnizor | Preferat din NIR, cu lotul și seria din NIR; iese la costul NIR-ului (cost impus, precizare la S3); tipărit ca aviz de însoțire, cu UIT când legea îl cere | 3.5, 4.11 | Decis de Aurelian (27.11.2026) |

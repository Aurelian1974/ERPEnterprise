# ERP · 02.05 Stocuri · S2 Mișcări și documente de stoc

26 noiembrie 2026 · Aurelian · **✓ Validat (26.11.2026)**, cu ecranele și toate cele 7 decizii. A doua din cele 7 părți ale modulului 02.05 Stocuri. Pornește de la S1 (validată pe 26.11.2026): gestiuni, depozite, nivelul de evidență, gestiunea `TRANZIT`, politica de stoc negativ. Include **notele de mișcare** cerute de Aurelian pe 26.11.2026: nota de predare și nota de primire, care mută stocul dintr-o gestiune în alta, plus încărcarea și descărcarea stocului.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori S2 `54100–54299`. Această parte acoperă registrul unic de mișcări și soldul de stoc, documentele proprii ale modulului (notă de predare, notă de primire, încărcare, descărcare, bon de consum, notă de predare a produselor, aviz de însoțire, custodie), mișcările venite din celelalte module (NIR, livrări, producție, POS), stornarea, verificarea stocului negativ și mișcările cu dată în trecut. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), [S1 Gestiuni, depozite și setări](S1-GESTIUNI-SI-SETARI.md), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C2 contarea, C3 închiderea lunii). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Stocul stă pe variantă (`ItemVariantId`, SKU), în UM de bază. Cheia stocului e (gestiune, variantă) sau, la nivelul „depozit”, (depozit, gestiune, variantă) (S1 §3.8). Loturile și seriile adaugă o dimensiune în S4.
- **Registrul de mișcări (`inv.StockMovement`) e singura sursă de adevăr.** Soldul (`inv.Stock`) și soldurile pe zile (`inv.StockDayBalance`) sunt derivate, actualizate în aceeași tranzacție cu mișcarea.
- Costul fiecărei mișcări îl calculează S3 (FIFO, CMP, LIFO, identificare directă, preț cu amănuntul), apelat de S2 în aceeași tranzacție. S2 nu conține reguli de cost.
- Ciclul de viață al documentelor e cel comun (02 Module principale): Ciornă → Validat → Stornat. Numărul se alocă la validare (`INumberSeriesService`), data contabilă trebuie să fie într-o perioadă deschisă (`PeriodLockBehavior`, Nucleu; C3).
- Contarea se face în Finanțe (C2), din evenimentul `MiscareStocValidata` cu blocul `Accounting` (`AccountingPayload` v1, componenta `Cost`). S2 nu scrie note contabile.
- **Ce NU face S2:** nu calculează costul (S3), nu alege loturile (S4), nu rezervă (S5), nu face inventarul (S6), nu face rapoartele (S7). Nu emite facturi și nici avize către clienți (Vânzări), nu face NIR (Achiziții) și nici rapoarte de producție (Producție). Pentru acestea, S2 primește doar mișcările.

## 1. Scop și perimetru

| Zonă | Ce face S2 | Ce NU face (și cine face) |
| --- | --- | --- |
| Registrul de mișcări | Fiecare intrare și ieșire, pe cheia stocului, cu sursa ei și cu cantitatea în UM de bază | Valoarea mișcării (S3) |
| Soldul | Stocul curent și stocul pe zile, actualizate la fiecare mișcare | Rezervatul și disponibilul (S5) |
| Note de mișcare | Nota de predare și nota de primire (mutarea între gestiuni, între depozite sau între hale), în una sau două etape, prin `TRANZIT` | Transportul (TMS), declarația e-Transport (Integrări) |
| Încărcare și descărcare | Intrările și ieșirile fără document comercial: sold inițial, plusuri, bunuri primite gratuit, casare, degradare, donații, lipsuri imputate, mostre, protocol | Diferențele de inventar (S6, care folosește aceleași tipuri de mișcare) |
| Consum și predare | Bonul de consum spre centre de cost și proiecte; nota de predare a produselor finite când Producția nu e licențiată | Consumul și predarea din comenzile de producție (Producție) |
| Aviz de însoțire | Avizul pentru marfa care circulă fără factură: între locații proprii, spre terți (S1, tip „La terți”), în custodie | Avizul de livrare către client (Vânzări) |
| Custodie | Intrarea și ieșirea bunurilor altora, în evidență extrabilanțieră (`8033`) | — |
| Mișcări din alte module | Primirea mișcărilor de la Achiziții, Vânzări, Producție, POS, prin portul de stoc | Documentele sursă |
| Stornare | Stornarea oricărei mișcări, cu legătură la original | Stornarea documentelor sursă (modulul lor) |

## 2. Model de domeniu

```
 Surse de mișcări
 ├─ Documente Stocuri: StockDocument + StockDocumentLine
 │     NP notă de predare · NR notă de primire · NI încărcare · ND descărcare
 │     BC bon de consum · PF notă de predare produse · AV aviz de însoțire · CI / CE custodie
 ├─ Alte module, prin portul IStockLedger (în tranzacția documentului lor):
 │     NIR, retur la furnizor (Achiziții) · aviz de livrare, retur de la client (Vânzări)
 │     consum, predare produse (Producție)
 └─ POS, prin eveniment (bonuri deja emise, sincronizate offline)
            │
            ▼
 StockMovement (registrul: o linie pe cheie, cantitate cu semn, tip, sursă, cost de la S3)
            │ aceeași tranzacție
            ├──► Stock (soldul curent pe cheie)
            ├──► StockDayBalance (intrări și ieșiri pe zi și cheie, pentru verificări retroactive)
            └──► outbox: MiscareStocValidata (cu blocul Accounting) ──► C2 (note), S7, WMS, Raportare

 Transfer între locații: NP (gestiunea A → TRANZIT) ── TransitShipment ── NR (TRANZIT → gestiunea B)
```

Reguli de business ale modelului:

1. **O mișcare nu se modifică și nu se șterge.** Corectura se face prin stornare (P3): o mișcare de semn opus, legată de original (`ReversalOfId`). Ciornele nu au mișcări.
2. **Soldul nu se scrie niciodată direct.** Doar procedura de înregistrare a mișcărilor (`inv.usp_StockMovement_Post`) actualizează `Stock` și `StockDayBalance`, în aceeași tranzacție.
3. **Fiecare mișcare are o singură cheie de stoc**, cu cantitatea în UM de bază (din linia documentului: UM, cantitatea în acea UM, factorul copiat la emitere, cantitatea în UM de bază; Nucleu §3.3). O notă de predare produce două mișcări: ieșirea din gestiunea care predă și intrarea în cea care primește (sau în `TRANZIT`).
4. **Ordinea mișcărilor e totală.** Pe aceeași cheie, mișcările se ordonează după `MovementDate`, apoi după `PostingSeq` (un `bigint` dintr-o secvență SQL, alocat la înregistrare). FIFO, LIFO și CMP (S3) și fișa de magazie (S7) folosesc exact această ordine.
5. **Stocul negativ se verifică pe toate datele de după mișcare** (S1 §3.6): o ieșire cu dată în trecut nu poate lăsa pe minus nicio zi ulterioară. Verificarea folosește `StockDayBalance`, nu o recalculare din registru.
6. **Sursa e unică.** O linie de document sursă produce mișcările ei o singură dată: cheia (`SourceModule`, `SourceLineId`, `SourceRole`) e unică, deci reluarea unui apel sau a unui eveniment nu dublează stocul.
7. **Documentele din alte module mișcă stocul în tranzacția lor**, prin portul `IStockLedger` (6). Validarea NIR-ului, a avizului sau a predării din producție eșuează dacă mișcarea de stoc e refuzată. Excepția sunt bonurile POS: sunt fapte deja petrecute, vin prin eveniment și se acceptă mereu (decizia 1).
8. **Data mișcării e data documentului.** Data contabilă trebuie să fie într-o lună deschisă. Mișcările cu dată în trecut se controlează prin setări (decizia 6, Aurelian 26.11.2026): `BackdatingDays` (implicit 5 zile; 0 = doar data de azi) dă câte zile în urmă se poate data o mișcare fără dreptul `Inv.Movement.Backdate`, iar `BackdatingWithRight` (implicit 1) spune dacă dreptul poate trece de limită, oricum doar în luni deschise. Mișcarea retroactivă declanșează recalcularea costului (S3), după setarea `CostRecalcScope` (S1 §3.3).

## 3. Entități, documente și ciclul lor de viață

Convențiile de coloane, tipuri și chei sunt cele din S1, secțiunea 3.

### 3.1 Mișcare de stoc (`StockMovement`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `Id` | `bigint` | `SQL-13` |
| `CompanyId`, `DepotId`, `WarehouseId`, `ItemVariantId` | `int` | Cheia stocului; `DepotId` doar la nivelul „depozit” (S1 §3.8), altfel `NULL` |
| `LotId`, `SerialId` | `int NULL` | Completate de S4 pentru variantele urmărite |
| `MovementDate`, `AccountingDate` | `date` | `AccountingDate` e de obicei egală cu `MovementDate`; diferă doar la documentele sursă care o cer (de exemplu factura de furnizor sosită după recepție) |
| `PostingSeq` | `bigint` | Din secvența `inv.seq_StockPosting`; ordinea totală (regula 4) |
| `MovementTypeCode` | `varchar(30)` | Din nomenclatorul livrat `inv.MovementType` (3.2) |
| `Direction` | `smallint` | +1 intrare, −1 ieșire; egal cu semnul cantității |
| `QtyBase` | `decimal(19,6)` | Cu semn, în UM de bază, ≠ 0 |
| `UnitCost`, `CostAmount` | `decimal(19,6)`, `decimal(19,2)` | În RON, completate de S3 la înregistrare; la intrări pot veni din sursă (prețul de recepție) |
| `RetailPrice`, `RetailAmount` | `decimal(19,6) NULL`, `decimal(19,2) NULL` | Doar în gestiunile la preț cu amănuntul (S1 §3.1, S3) |
| `ReasonId` | `int NULL` | Motivul (3.5), obligatoriu la încărcări și descărcări |
| `PartnerId`, `CostCenterId`, `ProjectId`, `EmployeeId` | `int NULL` | Dimensiunile mișcării, copiate pe nota contabilă |
| `SourceModule`, `SourceDocumentType`, `SourceDocumentId`, `SourceLineId`, `SourceRole` | | Sursa (regula 6). `SourceRole`: 1 principal, 2 contrapartida (intrarea dintr-un transfer) |
| `CounterMovementId` | `bigint NULL` | Perechea dintr-un transfer (ieșirea ↔ intrarea) |
| `ReversalOfId` | `bigint NULL` | Mișcarea stornată |
| `IsForcedNegative` | `bit` | 1 = a lăsat stocul pe minus (politica permisivă sau fapt din afara ERP) |
| `CreatedAt`, `CreatedBy` | | Fără `ModifiedAt` și `RowVersion`: rândurile nu se modifică (abatere declarată în 7) |

### 3.2 Tipuri de mișcare (`MovementType`, nomenclator livrat)

Lista e închisă și livrată cu produsul (`MIG-06`). Clientul nu adaugă tipuri, ci motive (3.5).

| Cod | Semn | Sursa | Costul intrării | Eveniment C2 (`DocumentType`) |
| --- | --- | --- | --- | --- |
| `RECEIPT_PURCHASE` | + | NIR (Achiziții) | Prețul de recepție, plus costurile adăugate (S3) | `GoodsReceipt` |
| `RETURN_TO_SUPPLIER` | − | Retur la furnizor (Achiziții) | Costul impus din NIR (S3 §3.1) | `SupplierReturn` (precizare din Achiziții A4, 27.11.2026) |
| `ISSUE_SALE` | − | Aviz de livrare (Vânzări) | — | `StockIssueSale` |
| `RETURN_FROM_CUSTOMER` | + | Retur de la client (Vânzări) | Costul ieșirii inițiale | `StockReturnSale` |
| `POS_SALE` | − | Bon fiscal (POS) | — | `StockIssuePos` |
| `TRANSFER_OUT`, `TRANSFER_IN` | −, + | Notă de predare / primire | Costul ieșirii pereche | `StockTransfer` |
| `LOAD` | + | Încărcare | Introdus sau ultimul cost (3.4) | `StockLoad` |
| `UNLOAD` | − | Descărcare | — | `StockUnload` |
| `CONSUMPTION` | − | Bon de consum | — | `StockConsumption` |
| `PRODUCTION_IN` | + | Notă de predare produse (S2) sau Producție | Cost standard sau din Producție | `ProductionOutput` |
| `PRODUCTION_OUT` | − | Consum din Producție | — | `ProductionConsumption` |
| `COUNT_PLUS`, `COUNT_MINUS` | +, − | Inventar (S6) | Ultimul cost sau cel stabilit de comisie | `InventoryDifference` |
| `CUSTODY_IN`, `CUSTODY_OUT` | +, − | Custodie | Valoarea declarată de proprietar | `CustodyMovement` (extrabilanțier) |

Tipurile de transfer între gestiuni cu conturi diferite (de exemplu `371` → `357`, sau din depozit în magazinul la preț cu amănuntul) produc note; transferurile între gestiuni cu același cont nu produc notă, dar publică evenimentul (C2 îl ignoră când contul e același).

### 3.3 Documentul de stoc (`StockDocument`, `StockDocumentLine`)

Un singur tabel pentru toate documentele proprii, cu `DocumentType`. Fiecare tip are seria lui, pe firmă și an, prin `NumberSeries` (Nucleu).

| Tip | Cod | Ce mișcă | Formular (OMFP 2634/2015) |
| --- | --- | --- | --- |
| Notă de predare | `NP` | Din gestiunea care predă spre gestiunea care primește (o etapă) sau spre `TRANZIT` (două etape) | Bon de predare, transfer, restituire (14-3-3A) |
| Notă de primire | `NR` | Din `TRANZIT` în gestiunea care primește; doar în două etape, creată din NP | Același formular, partea de primire |
| Încărcare | `NI` | Intrare fără document comercial, cu motiv | Proces-verbal sau document justificativ al motivului |
| Descărcare | `ND` | Ieșire fără document comercial, cu motiv | Proces-verbal de casare, de degradare, de donație etc. |
| Bon de consum | `BC` | Ieșire spre consum, pe centru de cost sau proiect | Bon de consum (14-3-4A) |
| Notă de predare produse | `PF` | Intrare de produse finite sau semifabricate, fără Producție licențiată | Notă de predare (14-3-3A) |
| Aviz de însoțire | `AV` | Nu mișcă singur: se emite pentru o NP, o ND sau o mișcare de custodie, când marfa circulă pe drum | Aviz de însoțire a mărfii (14-3-6A) |
| Custodie | `CI`, `CE` | Intrarea și ieșirea bunurilor altora în și din gestiunea de custodie | Proces-verbal de predare-primire cu proprietarul |

**Antet** (`StockDocument`): `CompanyId`, `DocumentType`, `Number` (la validare), `DocumentDate`, `AccountingDate`, `FromDepotId`, `FromWarehouseId`, `ToDepotId`, `ToWarehouseId` (după tip), `ReasonId`, `PartnerId`, `CostCenterId`, `ProjectId`, `EmployeeId` (cel care primește la bon de consum), `TransferMode` (1 o etapă, 2 două etape), `TransitShipmentId`, `RelatedDocumentId` (NR → NP, AV → NP), `Status` (1 Ciornă, 2 Validat, 3 Stornat, 4 Anulat, doar pentru ciorne), `ApprovalStatus` (3.6), `Notes`, `IsForcedNegative`, `ValidatedAt`, `ValidatedBy`, `ReversedByDocumentId`, `RowVersion`.

**Linie** (`StockDocumentLine`): `ItemVariantId`, `ItemNameSnapshot` (Nucleu §3.3.1, regula 10), `UomId`, `Qty`, `UomFactor`, `QtyBase`, `UnitCost` (doar unde se introduce: încărcare, custodie, predare produse), `LotId` sau datele unui lot nou (S4), `DepotId` / `WarehouseId` pe linie (când gestiunea se propune pe linie, la nivelul „depozit”, S1 §3.8), `CostCenterId`, `ProjectId`, `ReceivedQtyBase` (la NR), `DifferenceQtyBase`, `DifferenceResolution` (3.4).

**Gestiunea liniei la nivelul „depozit”.** Antetul alege depozitul; gestiunea fiecărei linii se propune prin regula de propunere a depozitului (S1 §3.8) și se poate schimba. O notă de predare între două hale ale aceleiași gestiuni (DC-PL din HALA-A în HALA-B) are aceeași gestiune pe ambele părți și depozite diferite.

### 3.4 Note de mișcare: predare și primire

Cerința lui Aurelian (26.11.2026): nota de predare și nota de primire mută stocul dintr-o gestiune în alta, iar încărcarea și descărcarea pun și scot stoc. Încărcarea și descărcarea sunt în 3.5.

**O etapă sau două** (decizia 2, Aurelian 26.11.2026). Numărul de etape se alege prin setarea firmei `StockSettings.TransferMode` (S1 §3.3), cu suprascriere pe punct de lucru sau locație. Documentul nu îl alege: îl deduce din locațiile celor două gestiuni.

| Valoare | Între gestiuni din aceeași locație | Între gestiuni din locații diferite |
| --- | --- | --- |
| 1 După locație (implicit) | O etapă | Două etape, prin `TRANZIT` |
| 2 Mereu două etape | Două etape | Două etape |
| 3 Mereu o etapă | O etapă | O etapă (pentru firmele mici, unde aceeași persoană predă și primește) |

Mutarea între depozitele aceleiași locații (de exemplu HALA-A → HALA-B) se tratează ca „aceeași locație”. Gestiunile „La terți” n-au locație, deci mutarea spre ele e mereu în două etape la valoarea 1.

| | O etapă | Două etape |
| --- | --- | --- |
| Documente | O notă de predare, tipărită cu „predat” și „primit” | Nota de predare, apoi nota de primire, creată din ea |
| Mișcări | `TRANSFER_OUT` din A, `TRANSFER_IN` în B, aceeași dată | La predare: A → `TRANZIT`; la primire: `TRANZIT` → B, cu data primirii |
| Cine validează | Cel care predă (drept pe A și pe B) | Predarea: cine are drept pe A. Primirea: cine are drept pe B |
| Aviz de însoțire | Nu | Propus automat când locațiile au adrese diferite |
| Costul | Costul ieșirii din A (S3); în B intră cu același cost | La fel; marfa stă în `TRANZIT` la acel cost |

**Expediția în tranzit (`TransitShipment`).** Se creează la validarea notei de predare în două etape: `CompanyId`, `NpDocumentId`, `FromWarehouseId`, `ToWarehouseId`, `ToDepotId`, `ShippedDate`, `ExpectedDate`, `Status` (1 Trimis, 2 Primit parțial, 3 Primit, 4 Primit cu diferențe, 5 Închis), `UitCode` (e-Transport, de la Integrări, când e cazul).

**Primirea și diferențele.** Nota de primire propune cantitățile trimise. Gestionarul care primește scrie ce a primit de fapt. Pentru fiecare diferență se alege rezolvarea (`DifferenceResolution`):

| Rezolvare | Ce se întâmplă | Cine |
| --- | --- | --- |
| 1 Rămâne în tranzit | Cantitatea rămâne în `TRANZIT`, iar expediția „Primit parțial”; se primește cu o altă notă de primire | Gestionarul care primește |
| 2 Retur la expeditor | O notă de predare inversă, din `TRANZIT` înapoi în A | Gestionarul care primește |
| 3 Lipsă imputabilă | Descărcare din `TRANZIT` cu motivul „Lipsă imputată” (3.5), pe angajatul sau transportatorul responsabil | Contabil stocuri |
| 4 Lipsă neimputabilă | Descărcare cu motivul „Lipsă / degradare la transport”, în limita perisabilităților (S6) sau ca cheltuială nedeductibilă | Contabil stocuri |
| 5 Plus la primire | Încărcare în B cu motivul „Plus la transfer”, după verificarea cu expeditorul | Contabil stocuri |

La închiderea lunii, pasul `TRANSIT_OPEN` (C3, avertisment) arată expedițiile trimise de mai mult de `TransitWarningDays` (implicit 7) zile și neprimite integral.

### 3.5 Încărcare, descărcare și motive (`MovementReason`)

Încărcarea (`NI`) și descărcarea (`ND`) cer un motiv. Motivul decide cheia de cont din C2, tratamentul TVA, aprobarea și atașamentul cerut. Clientul poate adăuga motive; cele livrate nu se șterg (`IsSystem`).

| Câmp | Reguli |
| --- | --- |
| `Code`, `Name`, `Direction` | +1 încărcare, −1 descărcare |
| `PostingVariant` | Varianta modelului de contare C2 (`StockLoad` / `StockUnload`) aleasă de motiv |
| `VatTreatment` | 0 fără TVA · 1 TVA colectată ca livrare asimilată (Codul fiscal, art. 270) · 2 ajustarea TVA deduse (art. 304–305). S2 doar marchează; TVA-ul îl calculează C2 / C4 |
| `IsDeductible` | Pentru impozitul pe profit (C4); de exemplu, lipsurile neimputabile peste limite sunt nedeductibile |
| `RequiresAttachment` | Procesul-verbal semnat se atașează înainte de validare |
| `ApprovalThreshold` | Valoarea peste care descărcarea cere aprobare (3.6); gol = fără aprobare |
| `NoPosting` | 1 = mișcarea nu produce notă (sold inițial, 3.5) |
| `CostSource` | La încărcare: 1 introdus pe linie, 2 ultimul cost al gestiunii, 3 cost standard |

Motive livrate:

| Cod | Direcție | Motiv | Contare tipică (C2) | TVA | Atașament |
| --- | --- | --- | --- | --- | --- |
| `OPENING` | + | Sold inițial la punerea în funcțiune | Fără notă: soldul contabil vine din importul C3, iar concordanța verifică egalitatea | — | Nu |
| `SURPLUS` | + | Plus constatat în afara inventarului | `371` = `7588` | — | Da |
| `FREE_RECEIVED` | + | Bunuri primite cu titlu gratuit | `371` = `7582` | — | Da |
| `EMPLOYEE_RETURN` | + | Restituire de la angajat | `371` = `4282` | — | Nu |
| `TRANSFER_SURPLUS` | + | Plus la primirea unui transfer | `371` = `7588` | — | Nu |
| `SCRAP` | − | Casare sau degradare, cu proces-verbal | `6588` / `607` = `371` | 2 (ajustare, dacă nu e cauză neimputabilă dovedită) | Da |
| `EXPIRED` | − | Produse expirate distruse | `607` = `371` | 2, cu aceleași excepții | Da (PV de distrugere) |
| `DONATION` | − | Donație, sponsorizare în natură | `6582` = `371` | 1 | Da (contract) |
| `PROTOCOL` | − | Protocol | `623` = `371` | 1 peste plafon | Nu |
| `SAMPLES` | − | Mostre, bunuri promoționale | `6588` = `371` | 1 peste plafon | Nu |
| `SHORTAGE_CHARGED` | − | Lipsă imputată (angajat, transportator) | `6588` = `371`, imputarea `4282` = `7581` | 1 | Da (decizia de imputare) |
| `SHORTAGE_TRANSIT` | − | Lipsă sau degradare la transport, neimputabilă | `6588` = `371` | 2 | Da |
| `EMPLOYEE_ISSUE` | − | Ieșire către angajați (echipament, beneficii în natură) | `6xx` după natură = `371` | 1 | Nu |

Conturile din coloana „contare tipică” sunt cele livrate în modelele C2 și se pot schimba acolo. S2 trimite doar motivul.

### 3.6 Aprobarea descărcărilor

O descărcare cu valoarea estimată peste pragul motivului cere aprobare înainte de validare. Valoarea se estimează la ultimul cost (S3, calcul fără înregistrare).

- Praguri livrate: `SCRAP`, `EXPIRED`, `SHORTAGE_TRANSIT`: 5.000 lei → Contabil șef. `DONATION`: orice valoare → Director general.
- `ApprovalStatus`: 0 nu e nevoie, 1 de aprobat, 2 aprobat, 3 respins (cu motiv). Aprobarea se face din „De rezolvat” (Nucleu) sau de pe document.
- Aprobatorul nu poate fi cel care a creat documentul (patru ochi).

### 3.7 Bonul de consum și nota de predare a produselor

**Bonul de consum (`BC`)** scoate din gestiune materiale, ambalaje, piese de schimb sau obiecte de inventar spre un centru de cost, un proiect sau un șantier. Centrul de cost e obligatoriu, cu valoarea implicită din gestiune (S1 §3.1, `DefaultCostCenterId`). Pe bon apare și angajatul care primește. Contarea `6xx` = `3xx` o face C2, după grupa articolului. Obiectele de inventar au pe linie persoana sau locația primitoare și trec în evidența de folosință a modulului Mijloace fixe (M6), care contează extrabilanțier `8039` (precizările din 02.15 la final).

**Nota de predare a produselor (`PF`)** e folosită doar când modulul Producție nu e licențiat (o firmă care produce puțin): intrarea de produse finite sau semifabricate la cost standard (`ItemStockSettings.StandardCost`, adăugat de S3), cu diferențele de preț în `348`. Cu Producția licențiată, tipul nu apare în meniu, iar predarea vine din Producție.

### 3.8 Avizul de însoțire (`AV`)

Avizul nu mișcă stocul singur. Se emite pentru o mișcare care trece pe drumul public fără factură:

- nota de predare în două etape între locații cu adrese diferite (propus automat);
- nota de predare spre o gestiune „La terți” (consignație, depozit logistic, prelucrare);
- descărcarea spre distrugere la un operator autorizat, ieșirea din custodie spre proprietar.

Avizul preia liniile, cantitățile și, opțional, valorile documentului legat, plus datele de transport (mijloc de transport, delegat, buletin). Are seria lui. Dacă bunurile intră sub e-Transport (OUG 41/2022), Integrări (02.14) obține codul UIT înainte de plecare, iar codul se tipărește pe aviz și se salvează pe expediție. Avizele de livrare către clienți sunt ale Vânzărilor.

### 3.9 Custodia

Bunurile altora (S1, gestiune de tip „Custodie”) intră cu `CI` și ies cu `CE`, la valoarea declarată de proprietar, în evidență extrabilanțieră (`8033`). Nu se vând, nu se rezervă și nu se consumă din custodie. Proprietarul (`ThirdPartyId` al gestiunii) e trecut pe fiecare document, iar confirmarea de sold către proprietar se tipărește din S7.

### 3.10 Mișcările din alte module

| Modul | Document sursă | Tip de mișcare | Cum ajunge |
| --- | --- | --- | --- |
| Achiziții (02.04) | NIR; retur la furnizor | `RECEIPT_PURCHASE`, `RETURN_TO_SUPPLIER` | `IStockLedger.PostAsync`, în tranzacția validării NIR-ului |
| Vânzări (02.03) | Aviz de livrare sau factură cu livrare; retur de la client | `ISSUE_SALE`, `RETURN_FROM_CUSTOMER` | `IStockLedger.PostAsync`, în tranzacția validării |
| Producție (02.08) | Consum de materiale; predarea produselor finite | `PRODUCTION_OUT`, `PRODUCTION_IN` | `IStockLedger.PostAsync` |
| POS (02.10) | Bonurile sincronizate (fapte deja petrecute) | `POS_SALE`, retururile ca `RETURN_FROM_CUSTOMER` | Eveniment `BonuriPOSSincronizate`, tratat de S2 idempotent |
| Inventar (S6) | Diferențele validate | `COUNT_PLUS`, `COUNT_MINUS` | Intern |

Sursele nu au tabele în `inv`. Mișcarea păstrează doar sursa (`SourceModule`, `SourceDocumentType`, `SourceDocumentId`, `SourceLineId`), ca fișa de magazie să poată deschide documentul.

## 4. Fluxuri principale

### 4.1 Validarea unei mișcări (comun tuturor surselor)

1. Handler-ul documentului (Stocuri sau alt modul) construiește lista de `StockMovementRequest` (6), cu cantitatea în UM de bază.
2. `IStockPolicyProvider` (S1) dă politica pentru toate variantele o dată: blocări pe articol sau gestiune, stoc negativ, urmărire.
3. S4 completează sau verifică loturile și seriile.
4. `inv.usp_StockMovement_Post` primește lista ca TVP (`SP-07`) și, în aceeași tranzacție:
   - blochează rândurile `Stock` atinse, în ordinea cheii (`UPDLOCK, ROWLOCK`, `SP-16`), creând rândurile lipsă;
   - verifică stocul negativ la data mișcării și la toate datele ulterioare, din `StockDayBalance` (regula 5), și refuză cu `54101` dacă politica e „blocat”;
   - alocă `PostingSeq`, scrie mișcările, actualizează `Stock` și `StockDayBalance`;
   - apelează motorul de cost S3 (`inv.usp_StockCost_Apply`), care completează `UnitCost` și `CostAmount`;
   - întoarce mișcările cu costul.
5. Handler-ul scrie în outbox `MiscareStocValidata`, cu liniile și costul (blocul `Accounting`).
6. Dacă mișcarea e retroactivă (există mișcări ulterioare pe cheie), S3 marchează cheile pentru recalculare (S3).

### 4.2 Nota de predare-primire într-o etapă

1. Gestionarul care predă alege gestiunea care predă și pe cea care primește (sau depozitele, la nivelul „depozit”) și introduce liniile, prin scanare sau din listă. Lângă fiecare linie vede stocul disponibil.
2. La validare: `TRANSFER_OUT` din A și `TRANSFER_IN` în B, legate prin `CounterMovementId`, cu același cost.
3. Documentul se tipărește cu ambele semnături. Dacă gestiunile au conturi diferite, C2 face nota (de exemplu `357` = `371` spre o gestiune „La terți”).

### 4.3 Nota de predare și nota de primire în două etape

1. Gestionarul care predă validează nota de predare: A → `TRANZIT`. Se creează expediția în tranzit. Avizul de însoțire se propune automat, iar, dacă e cazul, se cere codul UIT.
2. Gestionarul care primește vede expediția în „De primit” și deschide nota de primire, cu cantitățile propuse.
3. Scrie cantitățile primite. Pentru fiecare diferență alege rezolvarea (3.4).
4. La validare: `TRANSFER_IN` din `TRANZIT` în B, cu data primirii, plus documentele rezolvărilor (retur, descărcare, încărcare), create în ciornă și legate de expediție.
5. Expediția se închide când toată cantitatea e primită sau rezolvată.

### 4.4 Încărcarea

1. Se alege gestiunea, motivul și liniile. Costul se ia după `CostSource`: introdus, ultimul cost al gestiunii sau cost standard.
2. Pentru motivele cu atașament, procesul-verbal se încarcă înainte de validare.
3. La validare: mișcări `LOAD`. Pentru `OPENING` nu se face notă; concordanța `371` din C3 compară soldul stocurilor cu soldul preluat în contabilitate.

### 4.5 Descărcarea

1. Se alege gestiunea, motivul și liniile. Pe ecran apare valoarea estimată.
2. Peste prag, documentul trece în „De aprobat”. Fără aprobare nu se validează (`54112`).
3. La validare: mișcări `UNLOAD` la costul calculat de S3. Evenimentul poartă motivul, iar C2 face nota și, după `VatTreatment`, TVA-ul colectat sau ajustarea.

### 4.6 Stornarea

1. Se stornează documentul întreg sau doar anumite linii. Documentul de stornare are tipul originalului, cantitățile cu semn opus și data aleasă (implicit azi; data originalului doar dacă luna lui e deschisă).
2. Mișcările de stornare refolosesc costul mișcărilor stornate (S3), ca valoarea să se anuleze exact.
3. Stornarea unei intrări e supusă verificării de stoc negativ: o recepție din care s-a vândut deja nu se poate storna integral cu politica „blocat”.
4. O notă de predare în două etape se stornează doar cât timp expediția nu e primită; după primire se face o notă de predare inversă.

### 4.7 Mișcările POS

1. POS publică `BonuriPOSSincronizate` după sincronizarea unei case (bonuri emise offline, cu data și ora reale).
2. S2 tratează evenimentul idempotent (sursa e `ExternalId` al bonului, `SQL-14`): scrie `POS_SALE` și retururile, fără verificarea politicii, cu `IsForcedNegative` = 1 unde stocul trece sub zero și cu alerta „Stoc negativ forțat” (S1 §3.6).
3. Bonurile datate într-o lună închisă rămân în coada evenimentului, cu motivul „Perioadă închisă”, la fel ca în C2.

### 4.8 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Două livrări simultane din ultimele 10 bucăți | Rândul `Stock` e blocat de prima tranzacție; a doua așteaptă și apoi primește `54101`. Fără „pierdere” de stoc între verificare și scriere |
| Mișcare pe o gestiune blocată (inventar, predare) | Refuzată (`54005`, S1), cu excepțiile din S1 §4.3: primirea transferurilor deja trimise și bonurile POS |
| NIR cu dată în trecut, după ce marfa s-a vândut pe minus | Acceptat; S3 recalculează costul ieșirilor de după data NIR-ului și regularizează |
| Notă de predare din gestiunea „La terți” în gestiunea proprie | Permisă (marfa se întoarce de la terț); contarea `371` = `357` |
| Transfer între firme ale grupului | Nu e transfer: e vânzare din firma A și cumpărare în firma B, cu documentele Vânzări și Achiziții |
| Mutare între hale fără schimbarea gestiunii | Nota de predare cu aceeași gestiune și depozite diferite; nu produce notă contabilă |
| Linie cu cantitate 0 sau negativă pe un document | Refuzată (`54104`); corecturile se fac prin stornare |
| Variantă inactivă sau model nestocabil | Refuzată (`54105`) |
| Variantă creată „la prima utilizare” (Nucleu §3.3.1) | `core.usp_ItemVariant_Ensure` o creează în aceeași tranzacție, înainte de mișcare |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `MiscareStocValidata` v1 | Validarea sau stornarea oricărui document de stoc (și a mișcărilor venite prin port, la cererea sursei) | `DocumentType`, `DocumentId`, `Number`, mișcările `{MovementId, WarehouseId, DepotId, ItemVariantId, QtyBase, UnitCost, CostAmount, MovementTypeCode, ReasonCode}`, blocul `Accounting` (`AccountingPayload` v1, componenta `Cost`) | Finanțe (C2), S7, WMS, Raportare |
| `TransferExpediat` v1 | Validarea unei note de predare în două etape | `TransitShipmentId`, `FromWarehouseId`, `ToWarehouseId`, liniile | WMS (pregătirea primirii), TMS, notificarea gestionarului care primește |
| `TransferPrimit` v1 | Validarea unei note de primire | `TransitShipmentId`, cantitățile primite, diferențele | WMS, Raportare |
| `StocNegativFortat` v1 | O mișcare a lăsat stocul pe minus | `WarehouseId`, `ItemVariantId`, `QtyAfter`, sursa | Notificări (gestionar, Contabil stocuri) |

Pentru mișcările venite prin port, blocul `Accounting` al ieșirii de stoc (`607` = `371` la vânzare) e publicat de Stocuri, nu de Vânzări. Factura (`FacturaEmisa`) poartă doar venitul și TVA-ul. C2 are deja această separare (C2, 3.2: „Ieșire din stoc la vânzare (Stocuri) `607` = `371`”).

### 5.2 Consumate

| Eveniment | De la | Ce face S2 |
| --- | --- | --- |
| `BonuriPOSSincronizate` v1 | POS | Mișcări `POS_SALE` și retururi (4.7) |
| `UitObtinut` v1 | Integrări | Salvează codul UIT pe expediție și pe aviz |
| `VariantaArticolModificata` v1 | Nucleu | Actualizează denumirea în cache-ul de căutare al ecranelor; nu schimbă liniile emise |

`LivrareEfectuata`, `ReceptieNIRValidata`, `ConsumMateriale` și `PredareProduseFinite` din schița inițială nu mai sunt consumate de Stocuri pentru mișcare: aceste module mișcă stocul prin port, în tranzacția lor (decizia 1). Evenimentele rămân publicate de modulele lor pentru ceilalți consumatori.

## 6. Contracte publice (`Erp.Inventory.Contracts`, partea S2)

Vânzările și Achizițiile sunt pe un nivel sub Stocuri (02 Module principale, dependențele), deci nu pot depinde de `Erp.Inventory.Contracts`. Portul de stoc se definește în Nucleu (`Erp.Core.Contracts`), iar Stocuri îl implementează. Fără Stocuri licențiat, Nucleul înregistrează o implementare goală, care acceptă totul și nu scrie nimic.

```csharp
// În Erp.Core.Contracts (port); implementat de Erp.Inventory
public interface IStockLedger
{
    bool IsEnabled { get; }   // false când Stocuri nu e licențiat
    Task<StockPostingResult> PostAsync(StockPostingRequest request, CancellationToken ct);
    Task<StockPostingResult> ReverseAsync(StockReversalRequest request, CancellationToken ct);
    Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(
        int companyId, int? depotId, int? warehouseId, IReadOnlyCollection<int> itemVariantIds, DateOnly onDate, CancellationToken ct);
}

public sealed record StockPostingRequest(
    int CompanyId, string SourceModule, string SourceDocumentType, long SourceDocumentId, string SourceDocumentNumber,
    DateOnly MovementDate, DateOnly AccountingDate, int? PartnerId, bool IsExternalFact,     // IsExternalFact: fapt deja petrecut (POS)
    IReadOnlyList<StockMovementRequest> Lines);

public sealed record StockMovementRequest(
    long SourceLineId, byte SourceRole, string MovementTypeCode, int? DepotId, int WarehouseId, int ItemVariantId,
    decimal QtyBase, decimal? InboundUnitCost, int? LotId, string? NewLotCode, DateOnly? ExpiryDate, int? SerialId,
    int? CostCenterId, int? ProjectId, int? ReasonId);

public sealed record PostedMovementDto(long MovementId, long SourceLineId, decimal QtyBase, decimal UnitCost, decimal CostAmount, bool IsForcedNegative);
public sealed record StockPostingResult(IReadOnlyList<PostedMovementDto> Movements);
public sealed record StockAvailabilityDto(int ItemVariantId, int WarehouseId, int? DepotId, decimal OnHand, decimal Reserved, decimal Available);
```

Cine apelează `PostAsync` scrie singur `MiscareStocValidata` în outbox? Nu: `PostAsync` îl scrie, în tranzacția apelantului (`IOutbox` din Nucleu), ca evenimentul de stoc să aibă mereu același format, oricare ar fi sursa.

Contracte interne Stocuri (în `Erp.Inventory.Contracts`, pentru WMS, TMS, Producție, POS, care sunt pe nivelul Stocurilor sau deasupra):

```csharp
public interface IStockQuery
{
    Task<decimal> GetOnHandAsync(int warehouseId, int? depotId, int itemVariantId, DateOnly? onDate, CancellationToken ct);
    Task<IReadOnlyList<StockMovementDto>> GetMovementsAsync(StockMovementFilter filter, CancellationToken ct);   // paginare keyset, SP-10
    Task<IReadOnlyList<TransitShipmentDto>> GetOpenShipmentsAsync(int toWarehouseId, CancellationToken ct);
}
```

Comenzi interne S2: `SaveStockDocument`, `ValidateStockDocument`, `ReverseStockDocument`, `CancelStockDraft`, `RequestApproval`, `ApproveStockDocument`, `RejectStockDocument`, `CreateReceiptFromShipment`, `ValidateReceipt`, `ResolveTransitDifference`, `IssueDispatchNote` (aviz), `SaveMovementReason`.

## 7. Schema SQL `inv` (partea S2)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.MovementType` | `Id smallint` | UQ `Code` | — |
| `inv.MovementReason` | `Id int` | UX `(CompanyId, CodeKey)`; FK `CompanyId` | — |
| `inv.StockDocument` | `Id int` | UX `(CompanyId, DocumentType, Number) WHERE Number IS NOT NULL`; FK `CompanyId`, gestiunile, depozitele, `ReasonId`, `PartnerId`, `TransitShipmentId`, `RelatedDocumentId` | `(CompanyId, DocumentDate)` (`SQL-18`); `(CompanyId, Status, DocumentType)` |
| `inv.StockDocumentLine` | `Id bigint` | FK `DocumentId` (cascadă doar pentru ciorne, `SQL-15`), `ItemVariantId`, `UomId`, `LotId` | `DocumentId`; `ItemVariantId` |
| `inv.TransitShipment` | `Id int` | UQ `NpDocumentId`; FK gestiunile | `(ToWarehouseId, Status)` |
| `inv.StockMovement` | `Id bigint` | UX `(SourceModule, SourceLineId, SourceRole, ReversalOfId)` cu `NULL` tratat prin coloană calculată; FK `WarehouseId`, `DepotId`, `ItemVariantId`, `LotId`, `ReversalOfId` | `(WarehouseId, ItemVariantId, MovementDate, PostingSeq) INCLUDE (QtyBase, CostAmount)`; `(CompanyId, AccountingDate)`; `(SourceModule, SourceDocumentId)`; columnstore nonclustered pentru rapoarte (`SQL-16`) |
| `inv.Stock` | `Id bigint` | UX `(WarehouseId, DepotKey, ItemVariantId, LotKey)` (`DepotKey` = `ISNULL(DepotId, 0)`, `LotKey` = `ISNULL(LotId, 0)`, coloane calculate persistate) | `(ItemVariantId) INCLUDE (QtyOnHand)` |
| `inv.StockDayBalance` | `Id bigint` | UX `(WarehouseId, DepotKey, ItemVariantId, LotKey, BalanceDate)` | — |

Exemplu de tabel complet:

```sql
CREATE SEQUENCE inv.seq_StockPosting AS bigint START WITH 1 INCREMENT BY 1 CACHE 100;

CREATE TABLE inv.StockMovement (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovement PRIMARY KEY,
    CompanyId           int            NOT NULL CONSTRAINT FK_StockMovement_Company REFERENCES core.Company(Id),
    DepotId             int            NULL CONSTRAINT FK_StockMovement_Depot REFERENCES inv.Depot(Id),
    WarehouseId         int            NOT NULL CONSTRAINT FK_StockMovement_Warehouse REFERENCES inv.Warehouse(Id),
    ItemVariantId       int            NOT NULL CONSTRAINT FK_StockMovement_ItemVariant REFERENCES core.ItemVariant(Id),
    LotId               int            NULL,         -- FK spre inv.Lot, adăugat de migrarea S4
    SerialId            int            NULL,         -- FK spre inv.Serial, adăugat de migrarea S4
    MovementDate        date           NOT NULL,
    AccountingDate      date           NOT NULL,
    PostingSeq          bigint         NOT NULL,
    MovementTypeId      smallint       NOT NULL CONSTRAINT FK_StockMovement_MovementType REFERENCES inv.MovementType(Id),
    Direction           smallint       NOT NULL,
    QtyBase             decimal(19,6)  NOT NULL,
    UnitCost            decimal(19,6)  NOT NULL,
    CostAmount          decimal(19,2)  NOT NULL,
    RetailPrice         decimal(19,6)  NULL,
    RetailAmount        decimal(19,2)  NULL,
    ReasonId            int            NULL CONSTRAINT FK_StockMovement_MovementReason REFERENCES inv.MovementReason(Id),
    PartnerId           int            NULL CONSTRAINT FK_StockMovement_Partner REFERENCES core.Partner(Id),
    CostCenterId        int            NULL CONSTRAINT FK_StockMovement_DimensionValue_CostCenter REFERENCES core.DimensionValue(Id),
    ProjectId           int            NULL CONSTRAINT FK_StockMovement_DimensionValue_Project REFERENCES core.DimensionValue(Id),
    EmployeeId          int            NULL CONSTRAINT FK_StockMovement_Employee REFERENCES core.Employee(Id),
    SourceModule        varchar(20)    NOT NULL,     -- 'inv', 'purch', 'sales', 'mfg', 'pos'
    SourceDocumentType  varchar(30)    NOT NULL,
    SourceDocumentId    bigint         NOT NULL,
    SourceLineId        bigint         NOT NULL,
    SourceRole          tinyint        NOT NULL CONSTRAINT DF_StockMovement_SourceRole DEFAULT (1),
    CounterMovementId   bigint         NULL,
    ReversalOfId        bigint         NULL CONSTRAINT FK_StockMovement_ReversalOf REFERENCES inv.StockMovement(Id),
    ReversalKey         AS ISNULL(ReversalOfId, 0) PERSISTED,
    IsForcedNegative    bit            NOT NULL CONSTRAINT DF_StockMovement_IsForcedNegative DEFAULT (0),
    CreatedAt           datetime2(3)   NOT NULL CONSTRAINT DF_StockMovement_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int            NOT NULL,
    CONSTRAINT CK_StockMovement_Direction CHECK (Direction IN (-1, 1)),
    CONSTRAINT CK_StockMovement_QtySign CHECK (QtyBase <> 0 AND SIGN(QtyBase) = Direction),
    CONSTRAINT CK_StockMovement_SourceRole CHECK (SourceRole IN (1, 2)),
    CONSTRAINT CK_StockMovement_Retail CHECK ((RetailPrice IS NULL AND RetailAmount IS NULL) OR (RetailPrice IS NOT NULL AND RetailAmount IS NOT NULL))
);
CREATE UNIQUE INDEX UX_StockMovement_Source ON inv.StockMovement (SourceModule, SourceLineId, SourceRole, ReversalKey);
CREATE INDEX IX_StockMovement_Key_Date ON inv.StockMovement (WarehouseId, ItemVariantId, MovementDate, PostingSeq) INCLUDE (DepotId, LotId, QtyBase, CostAmount);
CREATE INDEX IX_StockMovement_Company_AccountingDate ON inv.StockMovement (CompanyId, AccountingDate);
CREATE INDEX IX_StockMovement_SourceDocument ON inv.StockMovement (SourceModule, SourceDocumentId);
CREATE TRIGGER inv.trg_StockMovement_NoChange ON inv.StockMovement AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    -- Excepție: S3 actualizează doar UnitCost și CostAmount la recalcularea costului, prin inv.usp_StockCost_Recalculate,
    -- care setează SESSION_CONTEXT(N'inv.CostRecalc') = 1.
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 54199, N'Mișcările de stoc nu se șterg.', 1;
    IF UPDATE(QtyBase) OR UPDATE(WarehouseId) OR UPDATE(ItemVariantId) OR UPDATE(MovementDate) OR UPDATE(DepotId) OR UPDATE(LotId)
       OR ISNULL(CAST(SESSION_CONTEXT(N'inv.CostRecalc') AS int), 0) <> 1
        THROW 54199, N'Mișcările de stoc nu se modifică; doar costul se recalculează prin S3.', 1;
END;
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_StockMovement_Post` | Înregistrarea unui set de mișcări (TVP `inv.tt_StockMovementRequest`): blocare, verificare negativ, scriere, sold, apel S3 (4.1) | Da, prin portul `IStockLedger` |
| `inv.usp_StockMovement_Reverse` | Stornarea, cu refolosirea costului | Da, prin port |
| `inv.usp_StockNegative_Check` | Verificarea pe datele ulterioare, din `StockDayBalance`, pe set | Nu |
| `inv.usp_StockDocument_Save`, `_Validate`, `_Reverse`, `_Cancel` | Documentele proprii | Nu |
| `inv.usp_StockDocument_Approve`, `_Reject` | Aprobarea descărcărilor | Nu |
| `inv.usp_TransitShipment_CreateReceipt`, `_ResolveDifference`, `_Close` | Primirea în două etape | Nu |
| `inv.usp_Stock_Availability` | Stocul și disponibilul pe un set de variante (pentru port și ecrane) | Da, prin port |
| `inv.usp_StockMovement_Search` | Registrul de mișcări cu filtre (`SP-11`, keyset `SP-10`) | Nu |
| `inv.usp_Stock_Rebuild` | Reconstruirea `Stock` și `StockDayBalance` din registru, pentru o cheie sau o gestiune (diagnostic, la cerere) | Nu |

Abatere de la `SP-08`: niciuna. Verificarea negativului se face pe set: pentru fiecare cheie atinsă, minimul cumulat al soldului pe zile de la data mișcării încolo (`SUM(...) OVER (ORDER BY BalanceDate)` pe `StockDayBalance`).

Coduri de eroare (plaja S2 `54100–54299`, prefix API `INV_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 54101 | `INV_STOCK_INSUFFICIENT` | 409 | Stoc insuficient la data mișcării sau la o dată ulterioară, cu politica „blocat”; mesajul dă varianta, gestiunea, data și cantitatea lipsă |
| 54102 | `INV_SOURCE_ALREADY_POSTED` | 409 | Linia sursă are deja mișcare (regula 6); apelantul tratează ca succes idempotent |
| 54103 | `INV_BACKDATE_NOT_ALLOWED` | 403 | Data mai veche decât `BackdatingDays`, fără `Inv.Movement.Backdate` sau cu `BackdatingWithRight` = 0 |
| 54104 | `INV_LINE_QTY_INVALID` | 422 | Cantitate 0 sau negativă pe linie |
| 54105 | `INV_VARIANT_NOT_STOCKABLE` | 422 | Variantă inactivă sau model nestocabil |
| 54106 | `INV_WAREHOUSE_PAIR_INVALID` | 422 | Predare în aceeași gestiune și același depozit, transfer între firme, sau spre o gestiune de alt tip nepermis (de exemplu din custodie spre depozit) |
| 54107 | `INV_TRANSFER_MODE_MISMATCH` | 409 | Documentul cere alt număr de etape decât setarea `TransferMode` pentru locațiile alese (de exemplu o notă de primire pentru un transfer într-o etapă) |
| 54108 | `INV_TRANSIT_DIRECT_USE` | 422 | Document direct pe gestiunea `TRANZIT` |
| 54109 | `INV_RECEIPT_EXCEEDS_SHIPMENT` | 422 | Primit mai mult decât trimis, fără rezolvarea „Plus la primire” |
| 54110 | `INV_REASON_REQUIRED` | 422 | Încărcare sau descărcare fără motiv, sau motiv de direcție greșită |
| 54111 | `INV_ATTACHMENT_REQUIRED` | 422 | Motivul cere proces-verbal atașat |
| 54112 | `INV_APPROVAL_REQUIRED` | 409 | Descărcare peste prag, neaprobată |
| 54113 | `INV_APPROVER_IS_AUTHOR` | 403 | Aprobarea de către autor |
| 54114 | `INV_REVERSAL_INVALID` | 409 | Stornarea unei mișcări deja stornate, sau a unei expediții deja primite |
| 54115 | `INV_COST_CENTER_REQUIRED` | 422 | Bon de consum fără centru de cost |
| 54116 | `INV_CUSTODY_MISUSE` | 422 | Vânzare, consum sau rezervare din custodie |
| 54117 | `INV_DOCUMENT_CONCURRENCY` | 409 | `RowVersion` diferit (`SP-18`) |
| 54199 | `INV_MOVEMENT_IMMUTABLE` | 409 | Modificare sau ștergere de mișcare (trigger) |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterea:

- Abatere de la `SQL-19` și `SQL-11` pentru `inv.StockMovement` și `inv.StockDayBalance`: fără `ModifiedAt`, `ModifiedBy`, `RowVersion`. Motiv: registrul nu se modifică (doar costul, prin recalcularea S3, cu jurnalul ei), iar soldul pe zile e derivat. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Document.View` | Documentele de stoc și registrul de mișcări (în limita scope-ului pe gestiuni și depozite) |
| `Inv.Transfer.Edit`, `.Validate` | Notele de predare și de primire |
| `Inv.LoadUnload.Edit`, `.Validate` | Încărcările și descărcările |
| `Inv.LoadUnload.Approve` | Aprobarea descărcărilor peste prag |
| `Inv.Consumption.Edit`, `.Validate` | Bonurile de consum |
| `Inv.ProductionNote.Edit`, `.Validate` | Notele de predare produse (fără Producție) |
| `Inv.Custody.Edit`, `.Validate` | Custodia |
| `Inv.Document.Reverse` | Stornarea |
| `Inv.Movement.Backdate` | Mișcări cu dată mai veche decât `BackdatingDays` |
| `Inv.Reason.Manage` | Motivele de încărcare și descărcare |

Roluri livrate: **Gestionar** (`View`, `Transfer.*`, `Consumption.*`, `LoadUnload.Edit`, pe gestiunea lui), **Șef depozit** (în plus `LoadUnload.Validate`, `Custody.*`, `Reverse`), **Contabil stocuri** (toate, fără `Approve`), **Contabil șef** (toate). Aprobarea donațiilor e a rolului Director general.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S2 |
| --- | --- |
| OMFP 2634/2015 (documentele financiar-contabile) | Formularele: bon de predare, transfer, restituire (14-3-3A), bon de consum (14-3-4A), aviz de însoțire a mărfii (14-3-6A), notă de predare; câmpurile obligatorii pe tipărituri; numerotare fără goluri pe serie |
| Legea 82/1991 și OMFP 1802/2014 | Evidența cantitativ-valorică; conturile din modelele C2; documentele justificative pentru fiecare mișcare |
| Codul fiscal, art. 270 (livrări asimilate) și art. 304–305 (ajustarea TVA) | Marcajul `VatTreatment` pe motive: donații, protocol, mostre peste plafon, lipsuri imputabile; ajustarea TVA la casare și degradare, cu excepțiile cauzelor neimputabile dovedite |
| Codul fiscal, art. 25 (deductibilitate) | Marcajul `IsDeductible` pe motive, folosit de C4 la impozitul pe profit |
| OUG 41/2022 (e-Transport) | Codul UIT pentru marfa cu risc fiscal ridicat sau peste praguri, obținut de Integrări înainte de plecare, pe aviz și pe expediție |
| HG 1/2016 (normele Codului fiscal) | Documentele pentru distrugere: proces-verbal de casare și, unde e cazul, documentul operatorului autorizat |

Integrări: Integrări (02.14) pentru e-Transport. Interne: Nucleu (serii, perioade, aprobări, notificări, atașamente, variante), S1 (politici), S3 (cost), S4 (loturi), C2 (contare), C3 (pașii `TRANSIT_OPEN`, `NEGATIVE_STOCK`, concordanța `371`), Vânzări, Achiziții, Producție, POS (mișcările lor).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Registrul de mișcări | Toate mișcările, cu filtre pe gestiune, depozit, variantă, tip, sursă, perioadă; deschide documentul sursă | Contabil stocuri, Gestionar |
| Transferuri în tranzit | Expedițiile neprimite sau cu diferențe, cu vechimea lor | Șef depozit, Contabil stocuri |
| Încărcări și descărcări pe motive | Pe lună și motiv: cantitate, valoare, TVA de colectat sau de ajustat, nedeductibil | Contabil șef |
| Consumuri pe centre de cost | Bonurile de consum pe centru de cost, proiect, grupă de articole | Controlling, Contabil |
| Stoc negativ forțat | Mișcările cu `IsForcedNegative`, nerezolvate | Contabil stocuri |

Fișa de magazie, balanța de stocuri și celelalte rapoarte de sold sunt în S7.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → document cu linii → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/s2-miscari/](ecrane/s2-miscari/). Meniul Stocuri › Documente are: Note de predare, Note de primire, Încărcări și descărcări, Bonuri de consum, Note de predare produse, Avize de însoțire, Custodie; Stocuri › Stoc are în plus Registrul de mișcări.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Note de predare | Lista notelor (filtre pe gestiune, etape, stare); nota selectată cu liniile, stocul disponibil pe linie, avizul și starea expediției | `s2-01-note-de-predare` |
| Notă de primire | Primirea unei expediții din tranzit: cantitățile trimise și primite, diferențele cu rezolvarea lor | `s2-02-nota-de-primire` |
| Încărcări și descărcări | Lista pe motive; descărcarea selectată cu valoarea estimată, TVA, procesul-verbal atașat și aprobarea | `s2-03-incarcari-descarcari` |
| Bon de consum | Bonul cu centrul de cost, angajatul care primește, liniile și starea contării | `s2-04-bon-de-consum` |
| Registrul de mișcări | Mișcările unei variante sau gestiuni, în ordinea `PostingSeq`, cu sursa, cantitatea, costul și soldul după fiecare mișcare | `s2-05-registrul-de-miscari` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S2-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S2-A1 | După orice secvență de mișcări, `Stock` și `StockDayBalance` sunt egale cu agregarea registrului (`inv.usp_Stock_Rebuild` nu găsește diferențe) | Test de proprietate |
| S2-A2 | Două tranzacții simultane care scot ultimele 10 bucăți: una reușește, cealaltă primește `54101`; stocul final e 0, nu −10 | Test de concurență |
| S2-A3 | O ieșire retroactivă care lasă pe minus o zi ulterioară e refuzată cu politica „blocat”, chiar dacă la data ei stocul ajunge | Test de integrare |
| S2-A4 | Reluarea aceluiași apel `PostAsync` (aceeași linie sursă) nu dublează stocul (`54102` tratat ca succes) | Test de integrare |
| S2-A5 | Nota de predare în două etape pune marfa în `TRANZIT` la costul ieșirii; nota de primire o scoate la același cost; valoarea totală a firmei nu se schimbă | Test de proprietate (cu S3) |
| S2-A6 | O primire cu 24 de bucăți lipsă și rezolvarea „Lipsă imputată” produce descărcarea din `TRANZIT` și închide expediția | Test de integrare |
| S2-A7 | O descărcare `SCRAP` de 6.200 lei nu se validează fără aprobare (`54112`); autorul nu o poate aproba (`54113`) | Test de integrare |
| S2-A8 | Stornarea unei descărcări refolosește costul original; valoarea stocului revine exact | Test de proprietate (cu S3) |
| S2-A9 | Bonurile POS sincronizate pe stoc zero se înregistrează cu `IsForcedNegative` = 1 și alertă; reluarea sincronizării nu le dublează | Test de integrare |
| S2-A10 | Validarea unui NIR în Achiziții eșuează (rollback complet) dacă mișcarea de stoc e refuzată | Test de integrare (cu Achiziții) |
| S2-A11 | Încărcarea `OPENING` nu produce eveniment cu bloc `Accounting`; concordanța `371` din C3 iese egală după importul soldurilor | Test de integrare (cu C3) |
| S2-A12 | Modificarea sau ștergerea unei mișcări e refuzată de trigger (`54199`), cu excepția costului în recalcularea S3 | Test de integrare |
| S2-A13 | Înregistrarea a 5.000 de linii POS într-un singur apel durează sub 3 secunde; validarea unei note de predare de 200 de linii, sub 1 secundă | Test de performanță |
| S2-A14 | Toate procedurile S2 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile S2-A1, S2-A2 și S2-A4 sunt critice: un sold care nu bate cu registrul sau o mișcare dublată strică direct costul, inventarul și concordanța cu `371`.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **Bonul de consum cu obiecte de inventar**: liniile cu articole de tip obiect de inventar au `ReceiverEmployeeId` și / sau `ReceiverLocationId` (cel puțin unul, cu setarea `AutoInUseFromStockIssue`); la validare, S2 apelează `IInUseIssuePort.IssueFromStockAsync` în aceeași tranzacție SQL ([M6 §6](../15-mijloace-fixe/M6-OBIECTE-FOLOSINTA.md)). Contul `8039` **nu** mai e trecut de varianta de model a grupei din S2, ci de evenimentul `ObiectInventarDatInFolosinta` al M6 (fraza de mai sus despre „varianta de model a grupei” se înlocuiește). Stornarea bonului apelează `IInUseIssuePort.ReverseAsync`.
- **Tranzacția nouă `NIR-REC` „Intrare din recuperări”**: materialele recuperate la casarea mijloacelor fixe și a obiectelor de inventar, creată de M5 / M6 prin `IRecoveredMaterialIntakePort.CreateAsync` (definit în `Erp.Stocks.Contracts`), contare `3028` (sau contul articolului) = `7588`.

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Produse de degustare și mostre**: câmpul opțional `CampaignId` pe antetul bonului de consum (tranzacția de consum pentru mostre); costul de ieșire intră în costul campaniei CRM. [CR4 §3.5](../12-crm/CR4-ACTIVITATI-CAMPANII.md)

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Cum mișcă stocul Vânzările, Achizițiile și Producția | Sincron, prin portul `IStockLedger` definit în Nucleu și implementat de Stocuri, în tranzacția documentului lor, ca stocul negativ să poată fi blocat și să nu existe stoc „pe drum” între module. Schița inițială spunea „prin evenimente”; evenimentele rămân doar pentru POS | 2 (regula 7), 3.10, 5.2, 6 | Decis de Aurelian (26.11.2026) |
| 2 | Transfer în una sau două etape | Setarea `TransferMode` pe firmă, punct de lucru sau locație: după locație (aceeași locație o etapă, locații diferite două etape, implicit), mereu două etape, mereu o etapă; documentul deduce etapele din locații | 3.4 | Decis de Aurelian (26.11.2026) |
| 3 | Diferențele la primirea transferului | Gestionarul care primește alege: rămâne în tranzit, retur, lipsă imputabilă, lipsă neimputabilă, plus; expediția se închide doar când totul e primit sau rezolvat | 3.4, 4.3 | Decis de Aurelian (26.11.2026) |
| 4 | Soldul inițial al stocurilor | Încărcare cu motivul `OPENING`, fără notă contabilă; soldul contabil vine din importul C3, iar concordanța `371` verifică egalitatea | 3.5, 4.4 | Decis de Aurelian (26.11.2026) |
| 5 | Aprobarea descărcărilor | Peste 5.000 lei pentru casare, expirate și lipsuri la transport (Contabil șef); orice donație (Director general); fără autoaprobare | 3.6 | Decis de Aurelian (26.11.2026) |
| 6 | Mișcări cu dată în trecut | Prin setări: `BackdatingDays` (implicit 5, 0 = doar azi) și `BackdatingWithRight` (dreptul poate trece de limită); doar în luni deschise; costul se recalculează după `CostRecalcScope` (S3) | 2 (regula 8) | Decis de Aurelian (26.11.2026), cu setare |
| 7 | Nota de predare a produselor fără Producție | Disponibilă doar când Producția nu e licențiată, la cost standard, cu diferențele în `348` | 3.7 | Decis de Aurelian (26.11.2026) |

## Note de implementare (28.11.2026)

| Pas | Ce s-a implementat | Decizii și abateri |
| --- | --- | --- |
| S2.1 | Migrarea `2026.11.079` (`inv.MovementType`, `inv.MovementReason`, secvența `inv.seq_StockPosting`, `inv.StockMovement` cu triggerul de imuabilitate, `inv.Stock`, `inv.StockDayBalance`, tipul `inv.tt_StockMovementRequest`); seed-urile `03_MovementTypes` (cele 16 tipuri) și `04_MovementReasons` (cele 13 motive livrate, cu pragurile de aprobare); procedurile `inv.usp_StockMovement_Post`, `_Reverse`, `inv.usp_Stock_Availability`, `inv.usp_Stock_Verify`; portul `IStockLedger` în `Erp.Core.Contracts` cu `NoStockLedger` implicit în Nucleu, înlocuit de `StockLedger` (`Erp.Inventory.Application.Movements`); regula datei `MovementRules` și `BusinessDay` (`Erp.Inventory.Domain`); evenimentele `MiscareStocValidata` v1, `StocNegativFortat` v1; permisiunile S2 (8) și rolurile lor; verificările amânate din S1: stocul la dezactivare (54006), la schimbarea urmăririi (54010) și a nivelului (54016); testele S2-A1 (proprietate pe 60 de mișcări aleatoare), A2 (două tranzacții paralele), A3, A4, A8, A9 (prin port), A12, S1-A3, A9, A10, A12 | **Costul** e deocamdată costul mediu ponderat pe cheie (gestiune, depozit, variantă, lot), recalculat la fiecare intrare; ieșirea întregului stoc ia toată valoarea, fără rest de rotunjire; pe minus, ultimul cost cunoscut sau media firmei. FIFO, LIFO, identificarea directă, prețul cu amănuntul, recalcularea după o mișcare retroactivă și regularizarea după o ieșire pe minus vin cu S3; până atunci valoarea soldului e mereu suma exactă a registrului. **Stornarea** păstrează tipul mișcării originale, cu semn opus, și exact valoarea ei; `Direction` urmează semnul cantității. **Perechea transferului** se scrie pe intrare (`CounterMovementId` spre ieșire), nu și invers, ca registrul să nu fie actualizat după inserare. **Idempotența**: reluarea completă a unei surse întoarce mișcările existente fără eveniment nou; reluarea parțială dă 54102. **Gestiunea blocată** primește doar `TRANSFER_IN` și faptele din afara ERP. **Tranzitul** primește doar transferurile și încărcarea / descărcarea rezolvărilor (S2.3). Portul traduce singur codurile THROW, pentru apelurile din afara pipeline-ului comenzilor. Blocul `Accounting` din `MiscareStocValidata` se adaugă cu modelele de contare ale Stocurilor (S3); verificarea luni închise folosește `IPeriodLock`. `inv.usp_Stock_Verify` (diagnostic) înlocuiește `usp_Stock_Rebuild`: arată diferențele, reconstruirea rămâne de făcut la nevoie |

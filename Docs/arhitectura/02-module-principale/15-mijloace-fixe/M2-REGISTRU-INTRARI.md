# ERP · 02.15 Mijloace fixe și obiecte de inventar · M2 Registrul și intrările: fișa, intrări, investiții în curs, punerea în funcțiune

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: Aurelian a cerut lucru autonom (27.11.2026), deci deciziile 1–12 de la final au fost adoptate pe recomandare. A doua din cele 7 părți ale modulului 02.15, după [M1 Configurare](M1-CONFIGURARE.md).

Schema `fa`, prefix `Erp.FixedAssets`, erorile M2 în plaja `65100–65249`. Această parte acoperă **registrul** (fișa mijlocului fix cu valorile pe regimuri, componentele, datele tehnice și istoricul) și **intrările**: coada „Imobilizări de preluat” alimentată de Achiziții, intrarea din factură sau NIR, investiția în curs și decontarea ei, producția proprie, aportul, donația, leasingul financiar, transferul din altă firmă a grupului, preluarea inițială la implementare, plus procesul-verbal de recepție și punere în funcțiune (PIF), care pornește amortizarea. Documente părinte: [M1](M1-CONFIGURARE.md) (grupe, catalog, profiluri, prag, tranzacții), [Nucleu §3.6](../01-nucleu/NUCLEU.md), [Achiziții A4 și A5](../04-achizitii/README.md), [Stocuri S2](../05-stocuri/README.md), [Finanțe C2](../02-finante-contabilitate/C2-MODELE-DE-CONTARE-SI-NOTE.md). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP on-premise, SQL Server case sensitive (`Latin1_General_100_CS_AS_SC_UTF8`, `SQL-21`), monolit modular .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite.
- **Fișa (`fa.Asset`) e bunul concret**, cu cantitate 1 și număr de inventar unic în firmă (M1 §3.8). Valorile nu stau pe fișă, ci pe **regimuri** (`fa.AssetBook`: `BOOK`, `TAX`, opțional `IFRS`, M1 §3.6). Orice valoare de pe fișă (de intrare, amortizată, rămasă) e calculată din documentele validate, nu editată.
- Documentele M2 se creează prin **tranzacții** (M1 §3.2) cu legături pe linie (`core.DocumentLink`). Liniile de imobilizare ale facturilor de furnizor (A5, `NonStockAssetKind = 1`) și ale NIR-urilor (A4) formează coada „Imobilizări de preluat” prin grupul de consum `ASSET_IN`.
- **Contarea**: factura de furnizor contează linia de imobilizare pe un analitic de tranzit al investițiilor, `231.PRL` „Imobilizări de preluat” (cheia nouă `AssetPendingIntake`), cu datoria pe `404` (cheia `AssetSupplierPayable`) și TVA-ul pe `4426` (precizare la A5). Intrarea M2 mută valoarea pe contul de imobilizare (`21x` / `20x` = `231.PRL`). Așa factura și fișa nu se contează de două ori, iar soldul `231.PRL` e egal tot timpul cu valoarea cozii (concordanța `ASSET_PENDING` din C3).
- Amortizarea începe **din luna următoare PIF-ului** (M1 §3.6). O fișă fără PIF nu se amortizează.
- **Ce NU face M2:** nu calculează amortizarea (M3), nu modifică valoarea după intrare (M4, cu excepția ajustării de preț NIR → factură, 3.4), nu scoate bunuri din evidență (M5), nu ține obiectele de inventar în folosință (M6).

## 1. Scop și perimetru

| Zonă | Ce face M2 | Ce NU face (și cine face) |
| --- | --- | --- |
| Fișa | Identitatea bunului, încadrarea (grupă, clasă), locul (locație, responsabil, centru de cost), regimurile cu metoda și durata, componentele, datele tehnice, istoricul, atașamentele, eticheta | Valorile după operații (M4), amortizarea (M3) |
| Coada de preluat | Liniile de imobilizare din facturi și NIR-uri încă fără fișă, cu generarea fișelor în masă | Factura și NIR-ul (A4, A5) |
| Intrări | Din factură, NIR, investiție, producție proprie, aport, donație, leasing financiar, plus la inventar (din M7), transfer în grup, preluare inițială | Ieșirile (M5); plusurile constatate (M7 le constată, M2 le înregistrează) |
| Investiții în curs | Acumularea pe `231` / `235` din facturi, consumuri din stoc și manoperă proprie; decontarea pe una sau mai multe fișe, cu repartizare | Proiectele ca atare (dimensiunea „proiect” din Nucleu; Construcții 02.11 pentru șantiere de vânzare) |
| PIF | Procesul-verbal de recepție și punere în funcțiune, cu comisia, care fixează luna de început a amortizării | Calculul amortizării (M3) |
| Leasing | Bunul în leasing financiar ca mijloc fix al utilizatorului; bunul în leasing operațional doar în evidență extrabilanțieră (`8036`) | Ratele, dobânda și datoria (Finanțe F3, F5) |

## 2. Model de domeniu

```
 fa.Asset (fișa)                                  ← CompanyId, InventoryNumber, Status, AssetGroupId, ClassificationId
   ├─ fa.AssetBook × N (BOOK, TAX, IFRS)          ← metodă, durată, luna de început, luni amortizate anterior, valori derivate
   ├─ fa.Asset (componente, ParentAssetId)        ← politica grupei: amortizate separat sau cu părintele
   ├─ fa.AssetPlacement × N (istoric)             ← locație, responsabil, centru de cost, departament, de la data
   ├─ core.PropertyValue (EntityType = Asset)     ← datele tehnice
   └─ fa.AssetLedger × N                          ← jurnalul valorilor: fiecare document validat scrie rânduri pe regim

 fa.AssetIntake (antet) ── fa.AssetIntakeLine × N (o linie = o fișă)
 fa.ConstructionInProgress (antet) ── fa.ConstructionCost × N (facturi, bonuri de consum, manoperă) ── decontare → AssetIntake
 fa.AssetCommissioning (PIF, antet + comisie) ── fa.AssetCommissioningLine × N (fișe)
 Coada „Imobilizări de preluat” = vedere peste liniile A4/A5 cu rămas > 0 în grupul ASSET_IN
```

Reguli de business:

1. **O linie de intrare = o fișă.** O linie de factură cu cantitatea 5 se desface la generare în 5 linii de intrare, fiecare cu valoarea unitară (diferența de rotunjire pe ultima). Excepție: grupele cu `ComponentPolicy` și articolele marcate „set” pot crea o fișă cu componente (3.6).
2. **Jurnalul valorilor e singura sursă a valorilor.** `fa.AssetLedger` are un rând pe document, fișă și regim, cu tipul mișcării (`INTAKE`, `ADJUST`, `DEPRECIATION`, `IMPROVEMENT`, `IMPAIRMENT`, `REVALUATION`, `DISPOSAL`...), suma pe valoarea brută, pe amortizare, pe depreciere și pe rezerva din reevaluare. Valorile curente se calculează din jurnal (cu o vedere indexată pe fișă și regim, `SQL-15`), nu se scriu pe fișă.
3. **Locul bunului are istoric.** Locația, responsabilul, centrul de cost și departamentul se schimbă doar prin documente (intrare, bon de mișcare M4), ca amortizarea fiecărei luni să meargă pe centrul de cost valabil atunci.
4. **Intrarea se poate valida fără PIF**, iar fișa stă „în curs de punere în funcțiune” (de exemplu un utilaj care așteaptă montajul). Fără PIF nu se amortizează. Raportul „Fișe fără PIF” (10) le arată pe cele mai vechi de 30 de zile.
5. **Valoarea de intrare include costurile de achiziție.** Transportul, montajul, taxele vamale și comisioanele din facturile legate se adaugă la valoarea fișei: costurile repartizate de Achiziții A6 (costuri de import) pe linia de imobilizare vin cu linia; o factură separată de montaj se adaugă ca linie de ajustare pe aceeași intrare (sursa `ASSET_IN`, modul „cost suplimentar”) până la PIF, iar după PIF doar prin modernizare (M4).
6. **TVA-ul nu intră în valoare**, cu excepția TVA-ului nedeductibil (de exemplu 50 % la autoturismele cu utilizare mixtă), care se adaugă la valoarea de intrare pe toate regimurile, conform componentei `NonDeductibleVat` primite de la A5.

## 3. Entități și documente

### 3.1 Fișa mijlocului fix (`fa.Asset`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int` | Firma |
| `InventoryNumber` | `varchar(30)` | Alocat la validarea intrării (M1 §3.8); `NULL` cât intrarea e ciornă; `InventoryNumberKey` calculat |
| `PreviousInventoryNumber` | `varchar(30) NULL` | Numărul vechi (preluare inițială cu renumerotare, transfer în grup) |
| `Name` | `nvarchar(200)` | Denumirea bunului (propusă din articol, editabilă) |
| `ItemId`, `ItemVariantId` | `int NULL` | Articolul cumpărat (din linia sursă); `NULL` la preluare inițială fără articol |
| `AssetGroupId` | `int` | Grupa (M1 §3.5); schimbarea după PIF doar prin reclasificare (M4) |
| `ClassificationId` | `int NULL` | Clasa din catalogul HG 2139/2004 (frunză); obligatorie pentru corporale amortizabile (`65101`) |
| `AcquisitionModeId` | `int` | Modul de obținere (M1 §3.10) |
| `Status` | `tinyint` | 1 În curs de punere în funcțiune · 2 În funcțiune · 3 În conservare · 4 Amortizată complet · 7 Compusă (preluată de altă fișă prin compunere, M4) · 8 Ieșită · 9 Anulată |
| `IntakeDate` | `date` | Data documentului de intrare |
| `CommissioningDate` | `date NULL` | Data PIF |
| `DisposalDate` | `date NULL` | Data ieșirii (M5) |
| `ParentAssetId` | `bigint NULL` | Fișa părinte, pentru componente |
| `SerialNumber` | `nvarchar(100) NULL` | Seria de fabricație; obligatorie dacă grupa cere (`TrackSerialNumber`) |
| `ManufactureYear` | `smallint NULL` | Anul fabricației |
| `SupplierPartnerId` | `int NULL` | Furnizorul (din factură) |
| `WarrantyUntil` | `date NULL` | Garanția furnizorului, cu alertă înainte de expirare |
| `BelowThreshold` | `bit` | Mijloc fix sub prag (M1 §3.7) |
| `IsLeased` | `bit` | Bun în leasing financiar (proprietatea trece la final) |
| `Notes` | `nvarchar(1000) NULL` | Observații |
| `RowVersion` | `rowversion` | Concurență optimistă |

Locul curent (din `fa.AssetPlacement`, ultimul rând valabil): `LocationId` (`core.Location`), `ResponsibleEmployeeId` (`core.Employee`), `CostCenterId` și `DepartmentId` (dimensiuni Nucleu), `ValidFrom`, `SourceDocumentId`.

**Tab-urile fișei** (ecranul `m2-02`): General · Regimuri · Plan de amortizare (M3) · Componente · Date tehnice · Loc și responsabili (istoricul) · Istoric (documentele, din legături, cu valorile după fiecare) · Atașamente (factura, PV-ul PIF, cartea tehnică, fotografii) · Inventarieri (M7).

### 3.2 Regimurile fișei (`fa.AssetBook`)

Un rând pe fișă și regim activ al firmei, creat la validarea intrării cu valorile propuse de grupă (M1 §3.5), editabile până la prima amortizare.

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `AssetId`, `BookCode` | | UX pe pereche |
| `DepreciationProfileId` | `int` | Profilul (M1 §3.6); pe `TAX`, verificat cu restricțiile legale (`65004`) |
| `UsefulLifeMonths` | `smallint` | Durata totală; pe `TAX` în intervalul clasei (`65001`), cu excepțiile din M1 §2 regula 4 |
| `PriorDepreciationMonths` | `smallint` | Luni amortizate înainte de intrarea în evidența noastră (preluare inițială, bun second-hand, transfer în grup) |
| `PriorAccumulatedDepreciation` | `decimal(19,4)` | Amortizarea cumulată preluată |
| `DepreciationStart` | `date NULL` | Prima zi a lunii de început; setată de PIF (luna următoare) |
| `DepreciationEnd` | `date NULL` | Calculată din start și durata rămasă; recalculată la orice schimbare (M3, M4) |
| `UnitsTotal` | `decimal(19,4) NULL` | Pentru profilul pe unități produse: capacitatea totală estimată |
| `SalvageValue` | `decimal(19,4)` | Valoarea reziduală (implicit 0; doar `BOOK` și `IFRS`) |
| `IsSuspended` | `bit` | Amortizare suspendată (conservare, M4) |

Valorile curente pe regim (calculate din jurnal): valoare de intrare, modernizări, valoare brută, amortizare cumulată, depreciere, rezervă din reevaluare, valoare rămasă, luni rămase.

### 3.3 Coada „Imobilizări de preluat”

Vedere (`fa.vw_PendingAssetIntake`) peste liniile facturilor de furnizor (A5) și ale NIR-urilor (A4) cu `NonStockAssetKind = 1` (imobilizare) sau `2` (investiție în curs, precizare la A5) și rămas > 0 în grupul `ASSET_IN` / `CIP_IN`. Coloanele: documentul și linia, furnizorul, articolul, cantitatea rămasă, valoarea unitară fără TVA (cu costurile repartizate de A6), TVA nedeductibil, propunerea (grupă, clasă, durată din articol și grupă, din `IAssetThresholdPolicy`), vechimea în zile.

Acțiuni: **Generează fișe** (pe liniile bifate: o intrare `MFI-FF` / `MFI-NIR` pe document sursă, cu câte o linie pe bucată), **Adaugă la investiție** (`INC-FF`), **Adaugă ca modernizare** (`MVM-MOD`, M4, pe o fișă existentă aleasă), **Nu e imobilizare** (trimite linia înapoi la contabilul de furnizori cu motiv: A5 o recontează pe cheltuială sau stoc). Setarea `IntakeFromInvoiceMode` (M1 §3.11) poate genera automat ciornele.

Vechimea: o linie de imobilizare mai veche de 30 de zile apare în „De rezolvat”, iar la închiderea lunii C3 verifică `ASSET_PENDING` (avertizare): soldul `231.PRL` trebuie să fie egal cu valoarea cozii.

### 3.4 Intrarea de mijloc fix (`fa.AssetIntake`)

**Antet**: `CompanyId`, `TransactionTypeId`, `Number` (serie `MFI`), `DocumentDate`, `AccountingDate`, `AcquisitionModeId`, `PartnerId` (furnizor, donator, asociat), `SourceDocumentRef` (factura, NIR-ul, investiția, contractul de leasing), `CurrencyCode` și `ExchangeRate` (informativ; valorile în lei), `CommissionImmediately` (generează și PIF-ul, M1 §3.11), `Status`, `Notes`.

**Linia** (o fișă): `AssetId` (fișa creată, în ciornă cu număr gol), `Name`, `AssetGroupId`, `ClassificationId`, `IntakeValue` (fără TVA, în lei), `NonDeductibleVat`, `AdditionalCost` (cost repartizat), `LocationId`, `ResponsibleEmployeeId`, `CostCenterId`, `SerialNumber`, `ParentLineNo` (componentă), regimurile (profil, durată, luni și amortizare anterioare), `SourceLinkId` (legătura cu linia de achiziție).

**Validarea** (`fa.usp_AssetIntake_Validate`, o tranzacție SQL, `SP-03`):

1. Verifică perioada deschisă (C3), pragul la data documentului (`65007`), clasa și duratele (`65001`, `65002`), conturile grupei (`65003`), consumul pe `ASSET_IN` (`50027` dacă rămasul nu ajunge).
2. Alocă numerele de inventar (M1 §3.8), creează rândurile `fa.AssetBook`, `fa.AssetPlacement` și `fa.AssetLedger` (`INTAKE`).
3. Scrie legăturile `core.DocumentLink` și starea fișei (1, sau 2 dacă PIF-ul e generat odată cu intrarea).
4. Publică `MijlocFixIntrat` v1 (outbox) cu blocul `Accounting`: componenta `AssetCost` pe fiecare grupă, cu cheia de contrapartidă după modul de obținere (`AssetPendingIntake`, `ConstructionInProgress`, `OwnProductionAssetIncome`, `ContributionInKind`, `DonationReceived`).

**Ajustarea de preț NIR → factură.** Dacă fișa a intrat din NIR (`MFI-NIR`) și factura vine cu alt preț, A5 publică diferența pe linia de imobilizare; M2 generează automat o ajustare a valorii de intrare (`fa.AssetLedger` tip `ADJUST`, document `MFI` cu tranzacția sistem `MFI-ADJ`), dacă fișa nu e încă amortizată. După prima amortizare, diferența se tratează ca modificare de valoare prospectivă (M4), cu recalcularea cotei pe durata rămasă.

### 3.5 Investiția în curs (`fa.ConstructionInProgress`)

Fișă de lucru pentru o imobilizare care se construiește, se montează sau se dezvoltă în timp (o rampă, o hală, o linie de producție, un program dezvoltat intern).

| Câmp | Reguli |
| --- | --- |
| `Number` (serie `INC`), `Name`, `AssetGroupId` (grupa `INC`, cont `231` sau `235` pentru necorporale) | |
| `ProjectId` | Dimensiunea „proiect” (Nucleu); bonurile de consum și pontajul pe acest proiect se pot adăuga (`INC-BC`) |
| `TargetAssetGroupId`, `TargetClassificationId` | Încadrarea propusă a bunului final |
| `Budget` | Bugetul, cu alertă la depășire |
| `Status` | 1 Activă · 2 Decontată parțial · 3 Decontată · 9 Anulată |

**Costurile** (`fa.ConstructionCost`): factură de furnizor (`INC-FF`, `CIP_IN`), bon de consum din stoc (`INC-BC`; S2 contează `231` = `3xx` cu contul investiției), manoperă și regie proprie (linie manuală: `231` = `722`, la sfârșitul lunii), dobânda capitalizabilă (doar `IFRS` și, cu politica firmei, `BOOK`).

**Decontarea** (`MFI-INC`): pe una sau mai multe fișe noi, cu repartizarea valorii acumulate procentual, valoric sau pe o cheie (de exemplu suprafață); sau parțial (o parte a investiției devine mijloc fix, restul rămâne în curs). Grupul `SETTLE_CIP` limitează decontarea la valoarea acumulată și nedecontată. Nota: `21x` = `231`. Decontarea pe o fișă existentă e o modernizare (`MVM-MODM`, M4).

### 3.6 Componentele

Pentru grupele cu `ComponentPolicy` 1 sau 2 (M1 §3.5). O componentă e tot o fișă (`ParentAssetId`), cu numărul părintelui și sufix (`MF 000412/1`).

- **Amortizate separat** (politica 1, abordarea pe componente din OMFP 1802): fiecare componentă are durata ei (de exemplu bateria de tracțiune a stivuitorului, 4 ani, în fișa unui stivuitor de 8 ani); înlocuirea unei componente = ieșirea celei vechi (M5) + intrarea celei noi.
- **Amortizate cu părintele** (politica 2): componenta e doar descriptivă (pentru inventariere și etichetare); valoarea ei e inclusă în valoarea părintelui.
- Componentele se creează la intrare (linie cu `ParentLineNo`) sau ulterior prin descompunere (M4). Părintele nu poate ieși fără componente (`65110`), iar raportările pe bun (fișa, D406) arată părintele cu componentele.

### 3.7 PIF: recepția și punerea în funcțiune (`fa.AssetCommissioning`)

Proces-verbal cu comisia de recepție (`CommitteeMembers`: angajați, cu președinte), data PIF, fișele (din intrare, `PIF-MFI`, grupul `COMMISSION`) și, pe fiecare, constatările (conform, cu observații). La validare: `CommissioningDate` pe fișă, `DepreciationStart` = prima zi a lunii următoare pe toate regimurile (sau după convenția profilului `IFRS`), starea 2 „În funcțiune”, evenimentul `MijlocFixPusInFunctiune` v1 (fără notă contabilă). Stornarea PIF-ului e permisă doar înainte de prima amortizare (`65010`).

PIF automat: setarea `AutoCommissionOnIntake` sau bifa pe intrare generează PIF-ul cu data intrării, fără comisie (pentru bunuri simple: laptopuri, mobilier).

### 3.8 Preluarea inițială (`MFI-INI`)

La implementare, fișele existente se importă din Excel (Configurare › Import din Nucleu, șablon „Mijloace fixe”), în ciorne de intrare `MFI-INI` cu data de deschidere (ultima zi înaintea primei luni gestionate în ERP).

| Coloană din șablon | Obligatorie | Observații |
| --- | --- | --- |
| Număr de inventar | Da | Păstrat; unicitate verificată (M1 §3.8) |
| Denumire, Grupă, Clasă HG 2139 | Da (clasă: pentru corporale) | Grupa după cod; clasa după cod |
| Data intrării, Data PIF | Da | |
| Valoare de intrare (contabilă, fiscală) | Da | Fiscală implicit egală cu cea contabilă |
| Amortizare cumulată și luni amortizate (contabil, fiscal) | Da | Pe fiecare regim |
| Metodă și durată totală (contabil, fiscal) | Da | Verificate ca în M1 |
| Rezervă din reevaluare, depreciere | Nu | Pe regimul contabil |
| Locație, responsabil, centru de cost | După grupă | După cod |
| Serie, an de fabricație, furnizor, componentă a | Nu | |

Validarea importului nu face notă (soldurile `21x`, `28xx`, `29xx`, `105` vin din balanța de deschidere); verificarea `OPENING_ASSETS` compară totalurile pe cont cu balanța de deschidere (C3 concordanțe) și arată diferențele pe grupă înainte de validare.

### 3.9 Leasingul

- **Leasing financiar** (`MFI-LSG`): bunul intră ca mijloc fix al utilizatorului la valoarea de intrare din contract (fără dobândă), cu contrapartida `167` (datoria de leasing, cheia nouă `LeaseLiability`, precizare la C2); fișa are `IsLeased = 1` până la transferul proprietății. Ratele, dobânda și diferențele de curs sunt ale Finanțelor (F3, F4, F5). Amortizarea pe durata normală (nu pe durata contractului), conform Codului fiscal.
- **Leasing operațional**: bunul nu e mijloc fix; se ține doar în evidența extrabilanțieră (`8036` „Redevențe, locații de gestiune, chirii și alte datorii asimilate” sau contul ales de contabil), printr-o fișă de tip „bun primit” în M6 (evidență de folosință, fără valoare contabilă). Decizia 9.

### 3.10 Etichetele

Din fișă sau din lista de fișe se tipăresc etichete în formatul grupei (M1 §3.10): cod de bare sau QR cu numărul de inventar (QR-ul poate conține și un link spre fișa din aplicație, deschis doar cu drept). Tipărirea folosește QuestPDF (pagini de etichete A4) sau imprimanta de etichete prin șablon ZPL (fără componente plătite). Retipărirea se jurnalizează (de cine, câte).

## 4. Fluxuri principale

### 4.1 Laptopuri din factură (exemplu complet)

Factura FF DTS 88231 de la Ditech Solutions SRL, 10.11.2026, validată de Carmen Nistor (A5): linia 1 — 5 × Laptop Dell Latitude 5550 la 5.890,00 lei (29.450,00 lei, `NonStockAssetKind = 1`, propus grupa `IT`, clasa `2.2.9`); linia 2 — 3 × Monitor Dell 27" la 1.240,00 lei (sub prag, propus obiect de inventar: intră în stoc pe `303` prin NIR, nu ajunge în coadă).

1. A5 contează `231.PRL` = `404` (29.450,00) și `4426` = `404` (TVA 21 %, 6.184,50); linia 1 apare în coada „Imobilizări de preluat”.
2. Sorina Munteanu bifează linia, „Generează fișe”: intrarea MFI 000131 (`MFI-FF`) cu 5 linii de 5.890,00 lei, grupa `IT`, clasa `2.2.9`, `BOOK` liniar 36 de luni, `TAX` accelerat 48 de luni; locația Birou Cluj, responsabil Bogdan Neagu, centrul de cost Administrativ Cluj; seriile de fabricație scanate.
3. Bifează „Pune în funcțiune la intrare” (bunuri simple). Validează: fișele MF 000431–MF 000435, PIF 10.11.2026, amortizare din 01.12.2026; nota `214` = `231.PRL` 29.450,00; coada se golește.

### 4.2 Rampa de încărcare (investiție în curs)

INC 000004 „Rampă de încărcare depozit Cluj”, buget 190.000 lei: factura de construcții (142.000), factura pentru nivelatorul hidraulic (31.500), bonul de consum BC 7745 cu materiale din stoc (8.240,00) și manopera proprie din noiembrie (4.760,00, `231` = `722`). Total 186.500,00. La recepția lucrării, decontarea `MFI-INC`, cu repartizare valorică: MF 000440 „Rampă de încărcare” (grupa `CLAD`) 155.000,00 și MF 000441 „Nivelator hidraulic” (grupa `ECHIP`) 31.500,00, cu PIF-ul pe 30.11.2026. Notele: `212` = `231` 155.000,00; `2131` = `231` 31.500,00.

### 4.3 Preluarea inițială

Exemplu Producție SRL (a doua firmă a grupului, care trece pe ERP mai târziu) începe evidența pe 01.12.2026: importă 214 fișe (șablonul din 3.8), cu data de deschidere 30.11.2026. Verificarea `OPENING_ASSETS` arată o diferență de 1.180,00 lei pe `2814` (amortizare din fișele vechi neînregistrată); contabilul corectează amortizarea cumulată pe 3 fișe în fișierul de import și reia validarea.

### 4.4 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Linie de factură de 4.990 lei marcată imobilizare | Propusă obiect de inventar (`IAssetThresholdPolicy`); dacă contabilul insistă, intrarea e refuzată (`65007`) în afară de politica 2 sau 3 (M1 §3.7) |
| Factura de montaj vine după PIF | Nu se mai adaugă la valoarea de intrare: devine modernizare (M4) sau cheltuială, după decizia contabilului din coadă |
| Factura stornată de furnizor după generarea fișei | A5 refuză stornarea liniei consumate (`50028`) până la stornarea intrării (posibilă doar fără amortizare, `65010`); altfel stornarea facturii devine ieșire (M5, retur la furnizor `IMF-RET`, tranzacție sistem) |
| Intrare în altă firmă a grupului | Ieșirea `IMF-GRP` creează ciorna `MFI-GRP` în firma primitoare, cu valorile brute și amortizarea preluată pe fiecare regim și cu numerele noi (M1 §3.8) |
| Bun cu TVA nedeductibil 50 % (autoturism) | Valoarea de intrare include partea nedeductibilă; grupa `AUTOTUR` aplică plafonul fiscal și cota nedeductibilă pe `TAX` (M1 §3.5) |
| Valoare de intrare în valută (import) | Lei la cursul DVI-ului; costurile vamale vin din A6 pe linie |
| Intrare validată cu data într-o lună cu amortizarea deja contată | Permisă; PIF-ul din aceeași lună pornește amortizarea din luna următoare, deci nu reface luna contată |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut | Consumatori |
| --- | --- | --- | --- |
| `MijlocFixIntrat` v1 | Validarea unei intrări | `AssetIntakeId`, fișele (`AssetId`, număr, grupă, clasă, valori pe regim), modul de obținere, sursa; blocul `Accounting` | C2 (notă), C4 (D406), 02.13 |
| `InvestitieCostAdaugat` v1 | Adăugarea unui cost la o investiție | `ConstructionId`, sursa, suma; `Accounting` (doar pentru manopera proprie, celelalte sunt contate la sursă) | C2 |
| `MijlocFixPusInFunctiune` v1 | Validarea PIF-ului | `AssetId`, data PIF, luna de început pe regim | M3 (planul), 02.13 |
| `MijlocFixAnulat` v1 | Stornarea unei intrări | `AssetId`, numărul (rămas anulat în registru); `Accounting` cu `IsReversal` | C2, A5 (linia redevine „de preluat”) |

### 5.2 Consumate

| Eveniment | De la | Ce face |
| --- | --- | --- |
| `FacturaFurnizorValidata` v1 | A5 | Liniile cu `NonStockAssetKind` 1 sau 2 apar în coadă (prin vedere, fără copiere); dacă `IntakeFromInvoiceMode` = 2 sau 3, generează ciornele sau intrările |
| `NirValidat` v1 | A4 | La fel, pentru liniile de imobilizare recepționate înaintea facturii |
| `DiferentaPretFactura` v1 | A5 | Ajustarea valorii de intrare (3.4) |
| `BonConsumValidat` v1 | S2 | Liniile pe proiectul unei investiții active apar ca propunere `INC-BC` |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M2)

```csharp
public interface IAssetRegisterQueries
{
    Task<AssetSummaryDto?> GetAsync(long assetId, CancellationToken ct);
    Task<AssetSummaryDto?> FindByInventoryNumberAsync(int companyId, string inventoryNumber, CancellationToken ct);
    /// Valorile pe regim la o dată (din jurnal), folosite de M5, C3 și D406.
    Task<IReadOnlyList<AssetBookValueDto>> GetBookValuesAsync(long assetId, DateOnly asOf, CancellationToken ct);
}

public sealed record AssetSummaryDto(long Id, int CompanyId, string? InventoryNumber, string Name, int AssetGroupId,
    int? ClassificationId, byte Status, DateOnly IntakeDate, DateOnly? CommissioningDate, int? LocationId,
    int? ResponsibleEmployeeId, int? CostCenterId, long? ParentAssetId);

public sealed record AssetBookValueDto(string BookCode, decimal GrossValue, decimal AccumulatedDepreciation,
    decimal Impairment, decimal RevaluationReserve, decimal NetValue, short RemainingMonths, bool IsSuspended);

/// Folosit de A5 pentru a ști dacă o linie de imobilizare poate fi stornată.
public interface IAssetIntakeStatusQuery
{
    Task<decimal> GetConsumedQuantityAsync(long purchaseLineId, CancellationToken ct);
}
```

## 7. Schema SQL `fa` (partea M2)

| Tabel | Cheie primară | Chei unice / străine | Indecși |
| --- | --- | --- | --- |
| `fa.Asset` | `Id bigint` | UX `(CompanyId, InventoryNumberKey) WHERE InventoryNumber IS NOT NULL`; FK `AssetGroupId`, `ClassificationId`, `AcquisitionModeId`, `ParentAssetId`, `ItemId`, `ItemVariantId`, `SupplierPartnerId` | `(CompanyId, Status, AssetGroupId)`; `(ParentAssetId)`; `(CompanyId, NameSearch)` |
| `fa.AssetBook` | `Id bigint` | UX `(AssetId, BookCode)`; FK `DepreciationProfileId` | — |
| `fa.AssetPlacement` | `Id bigint` | UX `(AssetId, ValidFrom)`; FK `LocationId`, `ResponsibleEmployeeId`, `CostCenterId`, `DepartmentId` | `(LocationId, ValidTo)`; `(ResponsibleEmployeeId, ValidTo)` |
| `fa.AssetLedger` | `Id bigint` | FK `AssetId`, `BookCode`; `DocumentType` + `DocumentId`; `CHECK` pe `MovementType` | `(AssetId, BookCode, PeriodDate)` INCLUDE sumele; vedere indexată `fa.vw_AssetBookBalance` |
| `fa.AssetIntake`, `fa.AssetIntakeLine` | `Id bigint` | UX `(CompanyId, NumberKey)` pe antet; linia FK `AssetId`, `AssetIntakeId` | `(CompanyId, DocumentDate)` |
| `fa.ConstructionInProgress`, `fa.ConstructionCost` | `Id bigint` | UX `(CompanyId, NumberKey)`; FK `ProjectId`, `TargetAssetGroupId` | `(ConstructionId)` |
| `fa.AssetCommissioning`, `fa.AssetCommissioningLine`, `fa.AssetCommissioningMember` | `Id bigint` | FK `AssetId`, `EmployeeId` | — |

Coloanele din `fa.AssetLedger`: `PeriodDate` (prima zi a lunii efectului), `MovementType`, `GrossDelta`, `DepreciationDelta`, `ImpairmentDelta`, `ReserveDelta`, `UnitsDelta`, `DocumentType`, `DocumentId`, `DocumentLineId`. Sumele se țin cu semn; valoarea netă = brut − amortizare − depreciere.

Procedurile principale: `fa.usp_AssetIntake_Save`, `fa.usp_AssetIntake_Validate`, `fa.usp_AssetIntake_Reverse`, `fa.usp_AssetIntake_GenerateFromPurchase` (coada, în masă, cu `UPDLOCK` pe liniile sursă, `SP-08`), `fa.usp_Construction_AddCost`, `fa.usp_Construction_Settle`, `fa.usp_AssetCommissioning_Validate`, `fa.usp_AssetOpening_Import` (din tabelul de staging al importului Nucleu), `fa.usp_Asset_SaveDescriptive` (doar câmpurile descriptive: denumire, serie, an, garanție, note).

**Erorile M2** (`65100–65249`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 65101 | `FA_CLASSIFICATION_REQUIRED` | 422 | Fișă corporală amortizabilă fără clasă |
| 65102 | `FA_PLACEMENT_REQUIRED` | 422 | Locație sau responsabil lipsă, cerute de grupă |
| 65103 | `FA_SERIAL_REQUIRED` | 422 | Seria de fabricație lipsă, cerută de grupă |
| 65104 | `FA_INVENTORY_NUMBER_DUPLICATE` | 409 | Număr existent la preluarea inițială |
| 65105 | `FA_OPENING_BALANCE_MISMATCH` | 409 | Totalurile importului diferă de balanța de deschidere (blocant doar dacă setarea cere egalitate) |
| 65106 | `FA_CONSTRUCTION_OVERSETTLED` | 409 | Decontare peste valoarea acumulată |
| 65107 | `FA_NOT_COMMISSIONED` | 409 | Operație care cere PIF pe o fișă fără PIF |
| 65108 | `FA_COMMISSIONING_LOCKED` | 409 | Stornarea PIF-ului după prima amortizare |
| 65109 | `FA_BOOK_LOCKED` | 409 | Schimbarea profilului sau a duratei după prima amortizare, direct pe fișă (se face prin M4) |
| 65110 | `FA_COMPONENTS_OPEN` | 409 | Ieșirea sau anularea părintelui cu componente active |
| 65111 | `FA_PRIOR_DEPRECIATION_INVALID` | 422 | Amortizare anterioară mai mare decât valoarea sau luni anterioare peste durată |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `FixedAssets.Asset.View` / `.ViewValues` | Fișele (cu sau fără valori; Administratorul de patrimoniu vede fără valori) |
| `FixedAssets.Asset.EditDescriptive` | Câmpurile descriptive și datele tehnice |
| `FixedAssets.Intake.Create` / `.Validate` / `.Reverse` | Intrările |
| `FixedAssets.PendingQueue.Process` | Coada „Imobilizări de preluat” |
| `FixedAssets.Construction.Manage` | Investițiile în curs și decontarea |
| `FixedAssets.Commissioning.Manage` | PIF-urile |
| `FixedAssets.Opening.Import` | Preluarea inițială |
| `FixedAssets.Label.Print` | Etichetele |

Rolurile din M1 §8: Contabilul mijloace fixe are tot M2; Administratorul de patrimoniu are `View`, `EditDescriptive`, `Commissioning.Manage` (comisia) și `Label.Print`; Responsabilul de loc vede doar fișele lui.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M2 |
| --- | --- |
| OMFP 1802 (recunoașterea și evaluarea inițială) | Valoarea de intrare = cost de achiziție (preț + costuri direct atribuibile), cost de producție, valoare justă la aport și donație; componentele semnificative cu durate diferite (3.6) |
| Codul fiscal art. 28 | Amortizarea din luna următoare PIF-ului; durata în intervalul catalogului; leasingul financiar amortizat de utilizator |
| OMFP 2634/2015 | Procesul-verbal de recepție și punere în funcțiune; fișa mijlocului fix și registrul numerelor de inventar (tipărite în M7) |
| Legea contabilității 82/1991 | Documentul justificativ pentru fiecare intrare (factura, PV-ul, contractul), atașat |
| SAF-T D406 active | Fiecare intrare e o tranzacție de activ cu tipul ei (achiziție, producție proprie, aport, transfer) |

Integrări: niciuna externă. Interne: A4, A5, A6 (coada și costurile), S2 (consumuri în investiții), C2 (note), C3 (`ASSET_PENDING`, `OPENING_ASSETS`), Nucleu (import, serii, proprietăți).

**Precizări aplicate altor documente (27.11.2026):** A5 (cheile `AssetPendingIntake` → `231.PRL` și `AssetSupplierPayable` → `404` pe liniile de imobilizare, `NonStockAssetKind = 2` investiție, evenimentul `DiferentaPretFactura`); C2 (cheile `LeaseLiability` → `167` și `AssetPendingIntake` → `231.PRL`); C3 (concordanțele `ASSET_PENDING`, `OPENING_ASSETS`).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Imobilizări de preluat | Coada, cu vechimea, pe furnizor și document; totalul față de debitul nealocat de pe `404` | Contabil mijloace fixe, Contabil furnizori |
| Fișe fără PIF | Fișele în curs de punere în funcțiune, cu vechimea | Contabil mijloace fixe, Administrator patrimoniu |
| Investiții în curs | Pe investiție: buget, acumulat pe surse, decontat, rămas | Contabil, Director economic |
| Intrări în perioadă | Pe mod de obținere și grupă, cu valoarea | Contabil șef |
| Garanții care expiră | Fișele cu garanția furnizorului în următoarele 60 de zile | Administrator patrimoniu |

## 11. Ecrane principale

Machetele sunt în [ecrane/m2-registru-intrari/](ecrane/m2-registru-intrari/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Fișe mijloace fixe | Lista fișelor cu filtre (grupă, stare, locație, responsabil, sub prag, fără PIF), valorile contabile și fiscale | `m2-01-lista-fise` |
| Fișa mijlocului fix | Formularul principal: laptopul MF 000433, tab-ul General (identitate, încadrare, loc, garanție, sursa), panoul de valori pe regimuri, componente, acțiuni | `m2-02-fisa-mijloc-fix` |
| Imobilizări de preluat | Coada cu liniile din FF DTS 88231, FF LND 55188 și NIR-ul 4502, propunerea pe fiecare și acțiunile | `m2-03-imobilizari-de-preluat` |
| Intrare de mijloc fix | Intrarea MFI 000131 din factura Ditech: 5 linii-fișe, regimurile, locul, PIF la intrare, nota care va rezulta | `m2-04-intrare-din-factura` |
| Investiție în curs | INC 000004 Rampă de încărcare: costurile pe surse, bugetul, decontarea pe două fișe | `m2-05-investitie-in-curs` |
| PIF | Procesul-verbal de recepție și punere în funcțiune pentru stivuitorul MF 000412, cu comisia și luna de început a amortizării | `m2-06-pif` |
| Preluare inițială | Asistentul de import pentru Exemplu Producție: 214 fișe, verificarea față de balanța de deschidere pe conturi, diferența pe `2814` | `m2-07-preluare-initiala` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| M2-A1 | Linia de 5 laptopuri generează 5 fișe de 5.890,00 lei, cu numere consecutive, iar rămasul `ASSET_IN` devine 0; o a doua generare pe aceeași linie e refuzată (`50027`) | Test de integrare |
| M2-A2 | Două generări simultane din coadă pe aceeași linie: una reușește, cealaltă primește `50027` | Test de concurență |
| M2-A3 | O linie de 100,00 lei împărțită pe 3 bucăți dă 33,33 / 33,33 / 33,34 | Test unitar |
| M2-A4 | Factura contează `231.PRL` = `404`; nota intrării e `214` = `231.PRL`; după intrare, soldul `231.PRL` e egal cu coada (0 pentru factura Ditech) | Test de integrare (cu C2) |
| M2-A5 | PIF pe 28.03.2026 → `DepreciationStart` 01.04.2026 pe `BOOK` și `TAX`; stornarea PIF-ului după amortizarea lunii aprilie e refuzată (`65108`) | Test de integrare |
| M2-A6 | Decontarea INC 000004 de 186.500,00 pe 155.000,00 + 31.500,00 închide investiția; o decontare suplimentară de 1,00 e refuzată (`65106`) | Test de integrare |
| M2-A7 | Importul preluării inițiale cu un număr duplicat e refuzat pe rândul respectiv (`65104`); totalurile pe cont se compară cu balanța de deschidere | Test de integrare |
| M2-A8 | Diferența de preț NIR → factură de +120,00 lei ajustează valoarea de intrare dacă fișa nu e amortizată și generează modificare prospectivă dacă e | Test de integrare |
| M2-A9 | Valorile fișei din `fa.vw_AssetBookBalance` sunt egale cu suma jurnalului pe toate tipurile de mișcare | Test de integrare |
| M2-A10 | Procedurile M2 rulează pe baza case sensitive (`TST-09`) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Unde | Stare |
| --- | --- | --- | --- | --- |
| 1 | O fișă pe bucată | Da: o linie de intrare = o fișă, cantitate 1; seturile doar prin componente | 2 regula 1 | ✓ Aprobat autonom (27.11.2026) |
| 2 | Unde stau valorile | Doar în jurnalul `fa.AssetLedger` pe regim, cu vedere indexată pentru valorile curente | 2 regula 2, 7 | ✓ Aprobat autonom |
| 3 | Contarea liniei de imobilizare din factură | A5 contează `231.PRL` = `404`; intrarea M2 mută valoarea pe `21x` (`21x` = `231.PRL`); concordanța `ASSET_PENDING` la închidere | context, 3.3 | ✓ Aprobat autonom |
| 4 | Coada „Imobilizări de preluat” | Vedere peste A4/A5 cu rămas pe `ASSET_IN`, generare în masă, alertă după 30 de zile | 3.3 | ✓ Aprobat autonom |
| 5 | Intrare fără PIF | Permisă, fișa „în curs de punere în funcțiune”, fără amortizare; PIF automat opțional pentru bunuri simple | 2 regula 4, 3.7 | ✓ Aprobat autonom |
| 6 | Costurile suplimentare | Incluse la intrare până la PIF (costurile A6 vin pe linie; montajul ca linie de ajustare); după PIF doar ca modernizare | 2 regula 5 | ✓ Aprobat autonom |
| 7 | Investițiile în curs | Document propriu cu costuri din facturi, bonuri de consum și manoperă, decontare totală sau parțială pe una sau mai multe fișe | 3.5 | ✓ Aprobat autonom |
| 8 | Componentele | Fișe copil cu sufix, amortizate separat sau cu părintele, după politica grupei | 3.6 | ✓ Aprobat autonom |
| 9 | Leasingul | Financiar: mijloc fix cu contrapartida `167`; operațional: doar evidență extrabilanțieră în M6 | 3.9 | ✓ Aprobat autonom |
| 10 | Preluarea inițială | Import Excel în ciorne `MFI-INI`, fără notă, cu verificarea totalurilor față de balanța de deschidere | 3.8 | ✓ Aprobat autonom |
| 11 | Ajustarea de preț NIR → factură | Automat pe valoarea de intrare dacă nu există amortizare; altfel prospectiv, prin M4 | 3.4 | ✓ Aprobat autonom |
| 12 | Locul bunului | Istoric în `fa.AssetPlacement`, schimbat doar prin documente | 2 regula 3 | ✓ Aprobat autonom |

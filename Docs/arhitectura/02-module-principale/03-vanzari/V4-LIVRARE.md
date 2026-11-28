# ERP · 02.03 Vânzări · V4 Livrare: dispoziția de livrare, avizul, e-Transport și consignația

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**. A patra din cele 7 părți ale modulului 02.03 Vânzări: [V1 Configurare](V1-CONFIGURARE.md) ✓ · [V2 Prețuri și discounturi](V2-PRETURI-DISCOUNTURI.md) ✓ · [V3 Oferte, comenzi și rezervări](V3-OFERTE-COMENZI.md) ✓ · **V4 Livrare** · V5 Facturare și e-Factura · V6 Retururi și storno · V7 Rapoarte.

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`; V4 folosește `52350–52499`. Această parte acoperă drumul fizic al mărfii către client: **dispoziția de livrare** (pregătirea în depozit, cu loturile fixate și diferențele de pregătire), **planul de livrare al zilei** (ce pleacă, cu ce mașină și cu ce șofer), **avizul de însoțire** (din comandă, din dispoziție sau fără comandă), cu ieșirea din stoc, consumul rezervării și datele de transport, **e-Transport** (când e obligatoriu, ce date se trimit, codul UIT), **confirmarea livrării** și **refuzul la livrare**, **stornarea avizului nefacturat** și **consignația** (marfa la client, încă a noastră).

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile** sunt definite în V1 §3.2: `DL-CV` (dispoziție din comenzi, grupul `PREPARE`), `AV-CV` (aviz din comandă), `AV-DL` (aviz din dispoziție), `AV-MAN` (aviz fără comandă), `AV-CSG` (aviz de consignație), `AV-STO` (stornarea avizului nefacturat, grupul `REVERSE`). Avizul consumă grupul `DELIVER` pe comandă (direct sau prin dispoziție) și devine sursă pentru `INVOICE` (V5), `RETURN` (V6) și `REVERSE`. Legăturile sunt în `core.DocumentLink` (Nucleu §3.6), iar asistentul de generare e comun (Nucleu §3.6.4).
- **Avizul mișcă stocul sincron**, prin portul `IStockLedger.PostAsync` (Stocuri S2), în tranzacția SQL a validării, cu `ConsumesReservationOfSourceLineId` = linia comenzii (S5): ieșirea scade stocul și consumă rezervarea. Loturile se aleg FEFO de Stocuri S4, cu durata minimă până la expirare cerută de client; costul ieșirii vine din S3 (CMP sau FIFO) și se întoarce pe linie, ca marja realizată să fie exactă.
- **Comanda cu `CreditHold` sau `AdvanceHold` nu se pregătește și nu se livrează** (`52005`, `52222`; V1 §3.2, V3). Verificarea de credit se repetă la livrare dacă punctul „livrare” e bifat în `CreditCheckPoints` (V1 §4.3).
- **Prețurile vin din comandă** (V2 regula 8): avizul copiază prețul unitar, prețul de listă și discounturile liniei de comandă, proporțional cu cantitatea. Doar avizul fără comandă (`AV-MAN`) își calculează prețul cu motorul V2.
- Avizul e un **document fiscal** (formularul 14-3-6A, OMFP 2634/2015) cu serie fiscală continuă (Nucleu fluxul 4.2). Numărul se alocă la validare.
- **Ambalajele SGR.** Pe liniile cu articole care au ambalaj în sistemul de garanție-returnare (Nucleu `Item.DepositItemId`), documentul adaugă automat linia de garanție de 0,50 lei pe ambalaj, fără TVA (categoria `O`), legată de linia articolului. Exemplele numerice din acest document nu afișează liniile de garanție, pentru simplitate; ele nu schimbă valorile cu TVA ale exemplelor. Returul ambalajelor goale e în V6 §3.7 (precizare din Vânzări V6, 27.11.2026).
- **Ce NU face V4:** nu emite facturi, nici factura cu livrare (V5 o emite; V4 descrie doar avizul ei intern, 3.6), nu face retururile după livrare (V6), nu face picking pe locații, scanare de paleți sau rute optimizate (WMS 02.06 și TMS 02.07, module viitoare, preiau dispoziția și planul prin contractele din 6) și nu comunică direct cu ANAF (Integrări 02.14 trimite declarația e-Transport și întoarce codul UIT).

## 1. Scop și perimetru

| Zonă | Ce face V4 | Ce primește de la alții |
| --- | --- | --- |
| Pregătire | Dispoziția de livrare din una sau mai multe comenzi, pe o gestiune: loturile propuse FEFO și fixate prin rezervare specifică, lista de pregătire, cantitatea pregătită pe lot, diferențele cu motiv, coletele și masa | Loturile și durata minimă (S4), rezervările (S5), comenzile confirmate (V3) |
| Plan de livrare | Ziua, mașina, șoferul, ordinea opririlor, încărcătura (masă și valoare), documentele fiecărei opriri | Punctele de livrare, zonele și ferestrele (V1); rutele optimizate vin din TMS (viitor) |
| Aviz | Avizul din comandă, din dispoziție sau fără comandă; datele de transport și ale delegatului; ieșirea din stoc cu consumul rezervării; valorile; codurile clientului; tipărirea | Mișcarea de stoc (S2), costul (S3), loturile (S4) |
| e-Transport | Regulile care spun când avizul cere cod UIT, datele declarației (tipul și scopul operațiunii, locurile de încărcare și descărcare, vehiculul), starea codului UIT | Trimiterea la ANAF și răspunsul (Integrări 02.14) |
| După plecare | Confirmarea livrării (aviz semnat, cantitatea primită), refuzul la livrare, stornarea avizului nefacturat (`AV-STO`) | Factura (V5) consumă avizul; returul după acceptare e în V6 |
| Consignație | Avizul de consignație spre gestiunea „la client”, situația stocului la client, raportul de consum (baza facturii `FV-CSG`, V5), aducerea înapoi a nevândutului | Gestiunea de tip „La terți” (S1), transferul (S2) |
| Avize nefacturate | Lista avizelor validate și nefacturate, cu alerta din V1 (`UninvoicedDeliveryAlertDays`) și termenul legal; contarea avizului pe `418` | Contarea (C2), facturarea (V5) |

## 2. Model de domeniu

```
 SalesOrder (confirmată, fără CreditHold / AdvanceHold) ─┬─► DL-CV ─► DeliveryOrder ─► DeliveryOrderLine ─► DeliveryOrderPick × N (lot, cantitate pregătită)
                                                          │            (rezervare specifică pe lot, S5)        └─ diferență de pregătire (ReasonCode tip 8)
                                                          │                │ AV-DL
                                                          └─► AV-CV ───────┴──────────► SalesDeliveryNote ─► SalesDeliveryNoteLine ─► SalesDeliveryNoteLot × N
 AV-MAN (fără comandă, preț din V2) ─────────────────────────────────────────►   │ date de transport, delegat, ETransportDeclaration (UIT, prin 02.14)
                                                                                 │ validare: IStockLedger.PostAsync (ISSUE_SALE, consumă rezervarea)
 DeliveryPlan (zi, mașină, șofer) ─► DeliveryPlanStop × N (punct de livrare, avize) │ confirmarea livrării (POD) · refuz ─► AV-STO (REVERSE)
 AV-CSG ─► transfer în gestiunea „La terți” a clientului ─► ConsignmentReport (consum raportat) ─► FV-CSG (V5)
```

Reguli de domeniu:

1. **Dispoziția de livrare e opțională, decisă pe gestiune.** Gestiunile cu depozit organizat (`DeliveryOrderMode` = 2 pe gestiune) livrează doar prin dispoziție (`AV-DL`); cele mici, magazinele și mașinile agenților livrează direct din comandă (`AV-CV`) sau fără comandă (`AV-MAN`). Setarea 1 permite ambele drumuri. Așa, același produs servește un depozit cu zece pregătitori și o firmă cu un singur magaziner (decizia 1).
2. **O dispoziție, o gestiune.** Dispoziția poate lua linii din mai multe comenzi și pentru mai mulți clienți (de exemplu toată ruta de sâmbătă), dar dintr-o singură gestiune (`52350`). Avizul e întotdeauna pentru **un client și un punct de livrare** (`52354`), deci o dispoziție pentru o rută produce un aviz pe fiecare oprire.
3. **Lansarea dispoziției fixează loturile.** La „Lansează”, S4 propune loturile FEFO cu durata minimă a fiecărui client, iar S5 transformă rezervarea generică a liniei de comandă în rezervare **specifică pe lot** (S5 decizia 2), ca marfa pregătită să nu fie luată de altă livrare. Pregătitorul poate lua alt lot, cu motiv (`LotOverrideReason`, S4); rezervarea se mută pe lotul luat (decizia 2).
4. **Ce se pregătește e ce se livrează.** Avizul din dispoziție preia cantitățile pregătite, pe loturi; nu se poate livra mai mult decât s-a pregătit (`52352`). O diferență de pregătire (lipsă la raft, marfă deteriorată, lot sub termenul clientului) cere motiv de tip 8 (`52353`); restul rămâne pe comandă, rezervat sau în așteptare, sau se închide, după `AllowPartialDelivery` al clientului (decizia 3).
5. **Avizul validat nu se modifică.** Orice corecție e o stornare a avizului (`AV-STO`, doar pe partea nefacturată, `52016`) urmată, dacă trebuie, de un aviz nou. Stornarea întoarce marfa în stoc **la costul ieșirii inițiale** (`IStockLedger.ReverseAsync`, S2) și redeschide restul de livrat pe comandă. Refuzul la livrare (clientul nu primește o parte, marfa se întoarce cu aceeași mașină) se tratează la fel, înainte de factură; după factură, e retur (V6) (decizia 5).
6. **Avizul are întotdeauna valori.** Prețurile copiate din comandă (sau calculate de V2 la `AV-MAN`) dau valoarea fără TVA și TVA-ul pe aviz. Valorile se tipăresc opțional (setare pe client, `PrintValuesOnDeliveryNote`), dar se calculează mereu: avizul nefacturat se contează pe `418` „Clienți – facturi de întocmit” la validare (C2), iar factura (V5) preia exact aceste valori (decizia 9).
7. **e-Transport e o regulă de configurare, nu un câmp manual.** Tabelul `ETransportRule` spune când un aviz cere cod UIT (transport internațional, bunuri cu risc fiscal ridicat după codul NC, praguri de masă și valoare); regulile livrate urmează legea la data instalării și se pot schimba fără versiune nouă. Avizul care cere UIT se validează (marfa iese din stoc în evidență), dar **nu se poate tipări ca document final și nu poate trece în starea Plecat** fără UIT valabil (`52356`) (decizia 6).
8. **Consignația nu e vânzare.** Avizul de consignație mută marfa în gestiunea „La terți” a clientului (S1, tipul 5, `ThirdPartyId` = clientul), la cost, fără venit și fără TVA colectat. Vânzarea apare doar când clientul raportează consumul, prin factura `FV-CSG` (V5), care descarcă gestiunea de la client (decizia 8).
9. **Planul de livrare e simplu până la TMS.** Planul zilei grupează avizele (sau dispozițiile) pe mașină și șofer, în ordinea opririlor, cu masa și valoarea încărcăturii și cu avertizare la depășirea capacității vehiculului. Nu optimizează rute; modulul TMS (02.07) va înlocui ecranul, păstrând aceleași documente (decizia 7).

## 3. Entități, stări și setări

Convențiile comune tabelelor `sales` sunt cele din V1 §3. Seriile: `DL` (nefiscală, pe gestiune), seria fiscală a avizelor (de exemplu `EXD`, continuă, opțional pe punct de lucru), `DP` pentru planurile de livrare, `RCS` pentru rapoartele de consignație.

### 3.1 Dispoziția de livrare (`DeliveryOrder`, `DeliveryOrderLine`, `DeliveryOrderPick`)

| Câmp (antet) | Reguli |
| --- | --- |
| `Number`, `DocDate`, `TransactionTypeId` | Seria `DL`; `DL-CV` sau o variantă |
| `WarehouseId`, `DepotId` | O singură gestiune (`52350`); depozitul, dacă gestiunea e în mai multe hale (S1) |
| `PlannedDate`, `DeliveryPlanId` | Ziua de livrare și planul din care face parte (3.4) |
| `AssignedToUserId` | Pregătitorul (Gestionar sau Șef depozit) |
| `Priority` | Din comenzi (cea mai mare prioritate a rezervărilor liniilor) |
| `Status` | 1 Ciornă · 3 Lansată (de pregătit) · 5 În pregătire · 6 Pregătită · 4 Închisă (tot ce s-a pregătit e pe avize) · 9 Anulată |
| `PackagesCount`, `PalletsCount`, `GrossWeightKg`, `VolumeM3` | Coletele și paleții declarați la pregătire; masa și volumul calculate din articole (Nucleu `GrossWeightKg`) și corectabile |
| `NoteToWarehouse` | Din comenzi (V3 `NoteToWarehouse`), concatenate pe clienți |

**Linie**: `SalesOrderLineId`, `SalesOrderScheduleId`, `PartnerId`, `DeliveryPointId`, `ItemVariantId`, `UomId`, `QtyRequested` (din restul `PREPARE` al livrării cu data promisă ≤ ziua planului), `QtyPicked` (suma alegerilor), `DifferenceReasonId` (tip 8), `MinShelfLifeDays` (din comandă).

**Alegere** (`DeliveryOrderPick`): `LineId`, `LotId`, `SerialId`, `QtyProposed`, `QtyPicked`, `LotOverrideReasonId`, `PickedBy`, `PickedAt`. Propunerea vine din `IStockLedger.ProposeLotsAsync` (S4) la lansare; alegerile completate devin loturile avizului.

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se adaugă și se scot linii, se schimbă pregătitorul și data | → Lansată; → ștergere |
| 3 Lansată | Lista de pregătire se tipărește sau se vede pe terminal; rezervarea e specifică pe lot | → În pregătire (prima cantitate înregistrată); → Ciornă (retrasă, rezervarea redevine generică); → Anulată |
| 5 În pregătire | Se înregistrează cantitățile pe loturi | → Pregătită |
| 6 Pregătită | Se generează avizele (`AV-DL`), câte unul pe client și punct de livrare | → Închisă, la ultimul aviz; → În pregătire (redeschisă înainte de primul aviz) |
| 4 Închisă | Finală | — |
| 9 Anulată | Rezervările redevin generice; liniile se întorc în restul `PREPARE` al comenzilor | — |

### 3.2 Avizul (`SalesDeliveryNote`)

| Câmp (antet) | Reguli |
| --- | --- |
| `NumberSeriesId`, `Number`, `DocDate` | Serie fiscală; numărul la validare (Nucleu fluxul 4.2). `DocDate` nu poate fi într-o perioadă închisă (Nucleu) |
| `TransactionTypeId` | `AV-CV`, `AV-DL`, `AV-MAN`, `AV-CSG` sau o variantă; avizul intern al facturii cu livrare are `IsInvoiceDelivery = 1` (3.6) |
| `PartnerId`, `DeliveryPointId`, `PayerPartnerId` | Un client și un punct de livrare (`52354`); GLN-ul punctului pentru EDI și e-Factura |
| `WarehouseId`, `DepotId` | Gestiunea de ieșire; la `AV-CSG`, și gestiunea de destinație „la client” (`ConsignmentWarehouseId`) |
| `CustomerOrderRef` | Copiat din comandă (sau scris la `AV-MAN`) |
| `ShippingMethodId`, `CarrierPartnerId` | Modul de livrare (V1 §3.8) și transportatorul terț, dacă e cazul |
| `VehicleNo`, `TrailerNo`, `DriverName`, `DelegateName`, `DelegateIdDocument` | Datele de expediție din formularul 14-3-6A; cerute când modul de livrare are `RequiresVehicle` (`52355`). Din actul de identitate se ține doar seria și numărul, cu retenția firmei (GDPR) |
| `LoadingAddressId`, `DispatchAt` | Locul de încărcare (adresa gestiunii) și ora plecării |
| `PackagesCount`, `NetWeightKg`, `GrossWeightKg` | Calculate din linii (Nucleu) și corectabile; folosite la e-Transport și la plan |
| `ETransportStatus`, `Uit`, `UitValidUntil` | 3.7; `ETransportStatus`: 0 Nu e cazul · 1 Necesar · 2 Cerut · 3 Obținut · 4 Respins · 5 Anulat · 6 Închis (confirmarea sosirii, la transporturile care o cer) |
| `NetAmount`, `VatAmount`, `TotalAmount`, `CostAmount` | Valorile copiate din comandă și costul ieșirii primit de la S3 |
| `PrintValues` | Din client (`PrintValuesOnDeliveryNote`); avizul de consignație nu tipărește prețuri de vânzare |
| `Status` | 1 Ciornă · 3 Validat · 6 Plecat · 7 Livrat · 4 Închis (facturat integral) · 8 Stornat (integral) · 9 Anulat (doar ciornă) |
| `DeliveredAt`, `ReceivedByName`, `PodAttachmentId` | Confirmarea livrării (3.8) |

**Linie** (`SalesDeliveryNoteLine`): `SalesOrderLineId`, `DeliveryOrderLineId`, `ItemVariantId`, `UomId`, `Qty`, `QtyBase`, `ListPrice`, `UnitPrice`, discounturile (`DocumentLineDiscount`, copiate proporțional), `VatRateId`, `NetAmount`, `VatAmount`, `UnitCostRon` (de la S3, după validare), `CustomerItemCode` (obligatoriu cu `RequiresCustomerItemCodes`, `52358`), `ParentLineId` (bonusul, livrat odată cu linia sursă, proporțional cu cantitatea livrată, rotunjit în jos la pragul regulii), `ReceivedQty` (la confirmare), `ReceiveDifferenceReasonId`.

**Loturile liniei** (`SalesDeliveryNoteLot`): `LineId`, `LotId`, `SerialId`, `QtyBase`, `ExpiryDate`. Din dispoziție, sunt cele pregătite; din comandă sau fără comandă, propunerea FEFO a S4 la generare (modificabilă cu motiv) sau alocarea automată la validare.

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează; cantitățile consumă restul comenzii „în ciornă” (Nucleu §3.6 regula 5) | → Validat; → Anulat (ștergere, dacă n-are număr) |
| 3 Validat | Marfa a ieșit din stoc; se cere UIT, dacă e cazul; se tipărește | → Plecat; → Stornat |
| 6 Plecat | Mașina a plecat (setat din plan sau de șef depozit); necesită UIT valabil, dacă e cerut | → Livrat; → Stornat parțial (refuz) |
| 7 Livrat | Clientul a confirmat primirea (aviz semnat sau confirmare electronică) | → Închis, la facturarea integrală |
| 4 Închis | Facturat integral (`INVOICE` consumat) | — |
| 8 Stornat | Tot avizul a fost stornat | — |

Avizul validat și nefacturat apare în „Avize nefacturate” (3.9). Stările 6 și 7 sunt opționale: o firmă fără plan și fără confirmări lucrează doar cu Validat → Închis (`TrackDispatchAndPod` = 0).

### 3.3 Validarea avizului

Într-o singură tranzacție SQL, în ordine:

1. Verificările de sursă: comanda e confirmată, fără `CreditHold` și `AdvanceHold` (`52005`, `52222`), clientul nu e blocat (`52004`); cantitățile nu depășesc restul `DELIVER` (cu toleranța tranzacției, Nucleu `50027`) și, din dispoziție, cantitatea pregătită (`52352`).
2. Creditul, dacă punctul „livrare” e bifat (V1 §4.3), cu expunerea calculată fără dublarea comenzii deja incluse.
3. Codurile clientului, dacă sunt cerute (`52358`), și durata minimă a loturilor (S4, `54455`).
4. Numărul fiscal (Nucleu fluxul 4.2).
5. `IStockLedger.PostAsync`: o linie de mișcare `ISSUE_SALE` pe fiecare lot, cu `ConsumesReservationOfSourceLineId` = linia comenzii (pe `AV-MAN`, gol). S4 alocă FEFO liniile fără lot; S3 întoarce costul pe mișcare. La `AV-CSG`, mișcarea e transferul spre gestiunea de la client (S2, `TRANSFER_OUT` / `TRANSFER_IN`).
6. Legăturile (`DELIVER` pe comandă, direct sau prin dispoziție; `PREPARE` pe dispoziție se închide), istoricul, starea Validat.
7. Evaluarea regulilor e-Transport (3.7): `ETransportStatus` = Necesar sau Nu e cazul.
8. Evenimentul `LivrareEfectuata` în outbox (C2 contează pe `418`, V5 îl vede ca „de facturat”, S5 și V3 actualizează comanda).

Dacă un pas eșuează, nu se salvează nimic. Linia de comandă livrată integral sau cu restul sub `UnderDeliveryAutoClosePct` se închide automat (V3 §4.8).

### 3.4 Planul de livrare (`DeliveryPlan`, `DeliveryPlanStop`)

| Câmp | Reguli |
| --- | --- |
| `PlanDate`, `WarehouseId`, `Number` | Un plan pe zi, gestiune și vehicul |
| `VehicleId` | Vehiculul firmei (nomenclatorul `sales.Vehicle`: număr de înmatriculare, capacitate în kg și m³, remorcă implicită) sau gol pentru transportator terț |
| `DriverUserId` sau `DriverName`, `CarrierPartnerId` | Șoferul (angajat) sau transportatorul |
| `Status` | 1 Planificat · 3 Încărcat · 6 Plecat · 4 Încheiat |
| `TotalGrossWeightKg`, `TotalValueRon` | Calculate din avize; peste capacitatea vehiculului, avertizare (`52366`) |

**Oprire** (`DeliveryPlanStop`): `Seq`, `DeliveryPointId`, fereastra de livrare a punctului (V1), avizele și dispozițiile opririi, `ArrivedAt`, `DeliveredAt`. La „Plecat”, toate avizele planului trec în Plecat, cu vehiculul și șoferul planului (dacă avizul nu are altele); un aviz care cere UIT și nu îl are oprește plecarea planului (`52356`).

### 3.5 Diferențele la pregătire și la livrare

`sales.ReasonCode` primește **tipul 8, „diferență la pregătire sau la livrare”**, cu 4 motive livrate: `LIPSARAFT` Lipsă la raft (stoc scriptic fără marfă fizică: semnal pentru inventarul S6), `DETDEPOZIT` Deteriorat în depozit, `LOTSCURT` Lot sub termenul minim al clientului, `REFUZCLIENT` Refuzat de client la livrare. Motivul `LIPSARAFT` trimite automat o cerere de numărătoare pe variantă și gestiune către Stocuri (S6, inventar parțial).

### 3.6 Avizul intern al facturii cu livrare

Factura cu livrare (`FV-LIV`, V5) creează în aceeași tranzacție un `SalesDeliveryNote` cu `IsInvoiceDelivery = 1`, cu aceleași linii, legat pe linii de factură. Avizul intern nu are număr fiscal propriu (poartă numărul facturii, cu mențiunea „aviz intern al facturii”), nu are starea „de facturat” și nu se contează pe `418`; ieșirea din stoc, consumul rezervării și e-Transport se fac la validarea facturii, după aceiași pași din 3.3 (fără pasul 4). Returul unei facturi cu livrare se face pe acest aviz intern (`RC-AV`, V6).

### 3.7 e-Transport

**Regulile** (`sales.ETransportRule`, comune cu Stocuri S2 pentru avizele de transfer și cu Achiziții A4 pentru retururi): `Scope` (1 național · 2 intracomunitar · 3 import / export), `CnCodePrefixes` (lista de coduri NC ale bunurilor cu risc fiscal ridicat, ca prefixe), `MinGrossWeightKg`, `MinValueRon`, `ThresholdBasis` (1 încărcătura vehiculului pe cursă · 2 documentul), `ValidFrom`, `ValidTo`, `IsActive`. Un aviz cere UIT dacă îl cere cel puțin o regulă activă la data plecării: pentru transporturile intracomunitare și internaționale, mereu; pentru cele naționale, dacă are bunuri cu cod NC din listă și pragul de masă brută sau de valoare e depășit. Regulile livrate reproduc OUG 41/2022 cu modificările în vigoare la instalare (pragurile de 500 kg masă brută sau 10.000 lei valoare fără TVA, pe transport) și **se verifică cu consultantul fiscal al fiecărei firme la punerea în funcțiune**; o schimbare de lege înseamnă o regulă nouă cu `ValidFrom`, nu o versiune nouă de program.

**Declarația** (`ETransportDeclaration`), una pe aviz, construită din aviz: tipul operațiunii (`core.ETransportOperationType`, de exemplu `30` Transport pe teritoriul național, `20` Livrare intracomunitară, `50` Export), scopul (`core.ETransportPurpose`, de exemplu `101` Comercializare, `705` Bunuri puse la dispoziția clientului pentru consignație), partenerul (CUI sau, pentru persoane fizice și străini, identificatorul cerut de schemă), liniile (codul NC, denumirea, cantitatea, UM-ul în cod UN/ECE, masa netă și brută, valoarea fără TVA), locul de încărcare (adresa gestiunii) și de descărcare (punctul de livrare), vehiculul și remorca, transportatorul, data transportului. Cele două nomenclatoare sunt în Nucleu (`core`), cu seed, ca să le folosească și Stocuri și Achiziții.

**Fluxul**: la validarea avizului cu UIT necesar, Vânzările apelează `IETransportGateway.SubmitAsync` (port implementat de Integrări 02.14), care trimite declarația asincron și întoarce rezultatul prin evenimentul `ETransportRaspuns`: codul UIT și valabilitatea (`UitValidUntil`) sau erorile. Cererea se poate face și înainte de validare, din ciornă, cu „Cere UIT” (pentru transporturi planificate cu o zi înainte). Modificarea vehiculului după obținerea codului trimite o corecție (`CorrectAsync`); stornarea integrală a avizului nefolosit anulează declarația (`CancelAsync`). Un UIT expirat la data plecării cere o declarație nouă (`52357`). Codul se tipărește pe aviz, în antet, și pe planul de livrare.

### 3.8 Confirmarea livrării și refuzul

- **Confirmarea** (`v4-07`): din plan sau din aviz, cu data și ora, numele celui care a primit, copia scanată a avizului semnat (`PodAttachmentId`) și, pe linii, cantitatea primită. Cu cantitățile egale, avizul trece în Livrat. Confirmarea poate veni și din aplicația șoferului (TMS, viitor) prin `IDeliveryConfirmationSink` (6).
- **Refuzul la livrare**: cantitatea primită mai mică decât cea livrată, cu motiv de tip 8 (`REFUZCLIENT`, `DETDEPOZIT`…). La „Înregistrează refuzul”, se creează în ciornă stornarea parțială `AV-STO` pentru diferență, care se validează când marfa ajunge înapoi în depozit (intrare la costul ieșirii). Linia de comandă își recapătă restul de livrat, iar agentul decide: se livrează din nou (rămâne rezervat) sau se închide restul (V3 §4.8). Dacă avizul era deja facturat, „Înregistrează refuzul” deschide returul de la client (`RC-AV`, V6) în locul stornării.
- **Diferența constatată de client după primire** (lipsă în colet descoperită a doua zi) nu e refuz: e reclamație și retur sau reducere (V6).

### 3.9 Avizele nefacturate

Lista „Avize nefacturate” (`v4-09`) arată avizele validate cu rest `INVOICE`, pe client, cu vechimea, valoarea cu TVA și gruparea de facturare a clientului (V1 `InvoiceGrouping`). După `UninvoicedDeliveryAlertDays` (V1, 5 zile), avizul apare în „De rezolvat” la operatorul de facturare; în prima zi lucrătoare după sfârșitul lunii, toate avizele lunii trecute încă nefacturate devin alertă roșie, cu termenul legal de facturare (15 ale lunii următoare livrării, Codul fiscal art. 319 alin. (16)). Facturarea în masă e în V5.

**Contarea avizului** (C2, din evenimentul `LivrareEfectuata`): `418` = `707` (valoarea fără TVA) + `4428` (TVA neexigibil), plus descărcarea de gestiune `607` = `371` din mișcarea de stoc (S2/S3). Factura (V5) închide `418` pe `4111` și trece TVA-ul din `4428` în `4427`. Avizul de consignație nu are contare de venit, doar transferul `371` → `357` (S2) (decizia 9).

### 3.10 Consignația

- **Gestiunea de la client**: o gestiune de tip „La terți” (S1), cu `ThirdPartyId` = clientul și adresa punctului de livrare, creată din fișa clientului („Activează consignația”), cu prețurile de raportare (lista clientului, V2).
- **Avizul de consignație** (`AV-CSG`): transfer din depozit în gestiunea clientului, la cost, cu aviz de însoțire fiscal (fără valori de vânzare) și cu e-Transport cu scopul `705`, dacă regulile o cer. Stocul rămâne al nostru (`357` „Mărfuri aflate la terți”).
- **Raportul de consum** (`ConsignmentReport`, seria `RCS`): clientul raportează periodic (setare pe client: săptămânal, lunar) cantitățile vândute; raportul se introduce manual sau se importă (Excel, EDI `SLSRPT`). Raportul validat e sursa facturii `FV-CSG` (V5), care face ieșirea din gestiunea de la client și venitul. Raportul nu poate depăși stocul din consignație (`52362`).
- **Inventarul la client**: o numărătoare periodică a gestiunii de la client (S6), cu diferențele facturate (lipsuri imputabile clientului, după contract) sau trecute pe cheltuieli.
- **Aducerea înapoi** a nevândutului: nota de predare din gestiunea clientului în depozit, cu aviz de însoțire (S2 §3.8).

### 3.11 Setările V4 (completare la `SalesSettings`, V1 §3.9, la gestiune, S1, și la client, V1 §3.4)

| Setare | Unde | Implicit | Ce înseamnă |
| --- | --- | --- | --- |
| `DeliveryOrderMode` | Gestiune (`inv.Warehouse`, coloană nouă) | 1 | 0 doar aviz direct · 1 aviz direct sau prin dispoziție · 2 doar prin dispoziție (decizia 1) |
| `PickingDocument` | Firmă | 1 | Lista de pregătire: 1 tipărită (sortată pe locație, dacă articolul are locație implicită) · 2 pe terminal (ecranul `v4-02`) |
| `ShortPickAction` | Firmă; suprascrisă de `AllowPartialDelivery` al clientului | 1 | La o diferență de pregătire: 1 restul rămâne pe comandă · 2 restul se închide automat cu motivul diferenței |
| `TrackDispatchAndPod` | Firmă | 1 | Stările Plecat și Livrat și confirmarea livrării (3.2) |
| `PodRequiredForInvoice` | Firmă | 0 | Factura din aviz cere confirmarea livrării (pentru firmele care facturează după primire) |
| `ETransportCheck` | Firmă | 2 | 1 avertizare · 2 tipărirea finală și plecarea cer UIT valabil (decizia 6) |
| `ETransportRequestAt` | Firmă | 1 | 1 la validarea avizului · 2 doar manual („Cere UIT”) |
| `PrintValuesOnDeliveryNote` | Client (`CustomerTerms`, coloană nouă) | 1 | Valorile pe avizul tipărit |
| `ConsignmentReportFrequency` | Client (`CustomerTerms`, coloană nouă) | — | 1 săptămânal · 2 lunar; gol = clientul nu are consignație |

## 4. Fluxuri principale

Datele de exemplu continuă pe cele din V1–V3 și Stocuri: depozitul `DEP-CJ` (`DeliveryOrderMode` = 2), șeful de depozit Gheorghe Ilie, gestionara Andreea Pop, șoferul Sorin Pavel, camionul `CJ 07 EXD` (capacitate 3.500 kg). Masele brute: APA05 0,53 kg/buc, APA15 1,58 kg/buc, CEAI05 0,55 kg/buc. Firma a configurat capitolul NC 22 (băuturi) ca bunuri cu risc fiscal ridicat, după lista în vigoare (exemplu de configurare, nu o afirmație despre lege).

### 4.1 Pregătirea rutei HoReCa de sâmbătă (`DL 0412`)

1. Pe 27.11 la 14:00, Gheorghe Ilie deschide planul de livrare pentru sâmbătă 28.11, camionul `CJ 07 EXD`, și creează dispoziția `DL 0412` (`DL-CV`) din comenzile cu data promisă 28.11 din zona Cluj-centru: `CV 4441` Hotel Central (APA05 480, CEAI05 288, APA15 360 + 72 bonus) și `CV 4433` Cafe Unirii (APA05 600, deblocată de Radu Stan pe 26.11).
2. La „Lansează”, S4 propune loturile FEFO cu durata minimă a fiecărui client (HoReCa: 60 de zile): APA05 1.080 buc din `L2611-07`, APA15 432 buc din `L2610-30`, CEAI05 288 buc din `L2609-22`. S5 face rezervările specifice pe aceste loturi. Lista de pregătire ajunge la Andreea Pop.
3. Andreea pregătește. La CEAI05 găsește 2 baxuri (24 buc) cu ambalajul spart: înregistrează 264 buc cu motivul `DETDEPOZIT`. Hotel Central acceptă livrări parțiale, deci restul de 24 buc rămâne pe `CV 4441`, rezervat ferm (stocul liber din alt lot acoperă).
4. Dispoziția trece în Pregătită: 3 paleți, masa brută 572,40 (APA05) + 682,56 (APA15) + 145,20 (CEAI05) = 1.400,16 kg.
5. „Generează avizele” creează două avize `AV-DL` în ciornă, unul pe oprire.

### 4.2 Avizele și e-Transport

| Aviz | Client | Linii | Valoare fără TVA | TVA | Total |
| --- | --- | --- | --- | --- | --- |
| `EXD 30625` | Hotel Central SRL, `HC-01` | APA05 480 × 1,70 = 816,00 · CEAI05 264 × 2,90 = 765,60 · APA15 360 × 3,15 = 1.134,00 · APA15 bonus 72 × 0,00 | 2.715,60 | 214,50 + 160,78 = 375,28 | 3.090,88 |
| `EXD 30626` | Cafe Unirii SRL, `CU-01` | APA05 600 × 1,85 = 1.110,00 | 1.110,00 | 122,10 | 1.232,10 |

1. Gheorghe validează cele două avize la 16:30. Pentru fiecare: verificarea de credit (Cafe Unirii are deblocarea unică din V1 §4.3, valabilă și consumată acum), numărul fiscal, ieșirea din stoc pe loturile pregătite cu consumul rezervării, legăturile, contarea pe `418`.
2. Regula națională se aplică pe încărcătura camionului: bunuri din capitolul NC 22, masă brută 1.400,16 kg > 500 kg. Ambele avize primesc `ETransportStatus` = Necesar, iar Vânzările trimit cele două declarații (tipul `30`, scopul `101`, încărcare din adresa DEP-CJ, descărcare la punctele de livrare, vehiculul `CJ 07 EXD`, data 28.11).
3. La 16:34, Integrări întoarce codurile UIT, valabile 5 zile: avizele le tipăresc în antet, iar planul le arată pe opriri.
4. Sâmbătă la 07:10, Gheorghe trece planul în Plecat; avizele trec în Plecat.

### 4.3 Refuzul la livrare și confirmarea

1. La Hotel Central, recepția hotelului refuză 2 baxuri APA05 (24 buc) cu sticle lovite. Șoferul aduce înapoi marfa; avizul semnat are mențiunea „primit 456 buc APA05”.
2. Luni 30.11 e zi liberă legală; marți 01.12 e Ziua Națională. Miercuri 02.12, Gheorghe înregistrează confirmarea pe `EXD 30625`: primit de „Ioana Crișan, recepție”, scanarea avizului semnat, APA05 primit 456 cu motivul `REFUZCLIENT`. Se creează `AV-STO` în ciornă pentru 24 buc APA05 (40,80 lei + TVA 4,49 lei).
3. La validarea stornării, marfa intră înapoi în `DEP-CJ` pe lotul `L2611-07`, la costul ieșirii (1,21 lei/buc), iar `418` scade cu 45,29 lei. Avizul `EXD 30625` are acum de facturat 2.674,80 lei fără TVA. Pe `CV 4441`, linia APA05 are din nou 24 buc de livrat; Vlad Georgescu decide să le livreze cu ruta următoare.
4. `EXD 30626` (Cafe Unirii) se confirmă fără diferențe și trece în Livrat.

### 4.4 Aviz direct din comandă (`AV-CV`)

Gestiunea mașinii agentului (`AUTO-CJ01`, `DeliveryOrderMode` = 0) livrează fără dispoziție: Vlad deschide comanda, alege „Livrează”, asistentul propune restul de livrat cu loturile FEFO din mașină (panoul de disponibil cu loturile, P6), iar avizul se validează pe loc. Dacă tranzacția implicită a clientului e factura cu livrare (`FV-LIV-AG`, V1 §4.1), drumul e factura, nu avizul (V5).

### 4.5 Consignația la Market Turda

1. Market Turda SRL are consignație lunară. Pe 27.11, `AV-CSG` `EXD 30627` mută 600 buc CEAI05 din `DEP-CJ` în gestiunea `CSG-MT01` „La Market Turda”, la cost (1,94 lei/buc, 1.164,00 lei). Avizul nu are prețuri de vânzare; e-Transport: scopul `705`, sub praguri (600 × 0,55 = 330 kg), deci fără UIT.
2. Pe 30.11 (raportul lunar, introdus pe 02.12), Market Turda raportează 420 buc vândute. Raportul `RCS 0009` validat e sursa facturii `FV-CSG` (V5): 420 buc la prețul listei clientului. Factura face ieșirea din `CSG-MT01`; în consignație rămân 180 buc.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `DispozitieLivrareLansata` v1 | Lansarea dispoziției | `DeliveryOrderId`, `WarehouseId`, `PlannedDate`, liniile cu loturile propuse | WMS (viitor), Notificări |
| `LivrareEfectuata` v1 | Validarea avizului (și a avizului intern al facturii cu livrare) | `DeliveryNoteId`, `Number`, `PartnerId`, `IsInvoiceDelivery`, `IsConsignment`, liniile `{LineId, SalesOrderLineId, ItemVariantId, QtyBase, NetAmount, VatAmount, CostAmount}` | C2 (contare `418`), V5 (de facturat), V3 (comanda), Raportare |
| `AvizStornat` v1 | Validarea `AV-STO` | `DeliveryNoteId`, `ReversalId`, liniile stornate, motivul | C2, V3, Raportare |
| `LivrareConfirmata` v1 | Confirmarea livrării | `DeliveryNoteId`, `DeliveredAt`, diferențele | V5 (cu `PodRequiredForInvoice`), CRM |
| `PlanLivrarePlecat` v1 | Plecarea planului | `DeliveryPlanId`, vehiculul, avizele | TMS (viitor), Notificări |

### 5.2 Consumate

| Eveniment | De la | Ce face V4 |
| --- | --- | --- |
| `ETransportRaspuns` v1 | Integrări 02.14 | Scrie UIT-ul și valabilitatea sau erorile pe declarație și pe aviz |
| `RezervareNeacoperita` v1 | S5 | O rezervare specifică a unei dispoziții lansate a rămas fără lot (lot blocat în carantină): dispoziția primește semnul „de reluat”, cu propunerea FEFO nouă |
| `ComandaVanzareModificata` v1, `ComandaVanzareAnulata` v1 | V3 | Dispozițiile în ciornă sau lansate ale liniilor închise se actualizează; cele în pregătire primesc semnul „comandă modificată” |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V4)

```csharp
// Port implementat de Integrări 02.14 (Erp.Integrations); folosit și de Stocuri S2 și Achiziții A4
public interface IETransportGateway
{
    Task<Guid> SubmitAsync(ETransportDeclarationDto declaration, CancellationToken ct);   // răspunsul vine prin ETransportRaspuns
    Task<Guid> CorrectAsync(string uit, ETransportCorrectionDto correction, CancellationToken ct);
    Task<Guid> CancelAsync(string uit, string reason, CancellationToken ct);
    Task<Guid> ConfirmArrivalAsync(string uit, DateTime arrivedAt, CancellationToken ct);
}
public sealed record ETransportDeclarationDto(int CompanyId, string SourceModule, string SourceDocumentType, long SourceDocumentId,
    string OperationTypeCode, string PurposeCode, ETransportPartyDto Partner, ETransportPlaceDto Loading, ETransportPlaceDto Unloading,
    string VehicleNo, string? TrailerNo, ETransportPartyDto? Carrier, DateOnly TransportDate, IReadOnlyList<ETransportLineDto> Lines);
public sealed record ETransportLineDto(string CnCode, string Name, decimal Qty, string UneceUomCode, decimal NetWeightKg,
    decimal GrossWeightKg, decimal ValueRon);

// Citit de WMS (viitor), TMS (viitor) și de ecranul de pregătire
public interface IDeliveryOrderQueries
{
    Task<IReadOnlyList<DeliveryOrderDto>> GetReleasedAsync(int companyId, int warehouseId, DateOnly upToDate, CancellationToken ct);
    Task<DeliveryOrderDto?> GetAsync(long deliveryOrderId, CancellationToken ct);
}

// Scris de WMS (cantitățile pregătite) și de aplicația șoferului (TMS) — viitor
public interface IDeliveryConfirmationSink
{
    Task RecordPickAsync(long deliveryOrderLineId, int lotId, decimal qtyPicked, int? reasonId, CancellationToken ct);
    Task RecordDeliveryAsync(long deliveryNoteId, DeliveryConfirmationDto confirmation, CancellationToken ct);
}

// Citit de V5 (facturarea din avize) și de V7
public interface IDeliveryNoteQueries
{
    Task<IReadOnlyList<UninvoicedDeliveryDto>> GetUninvoicedAsync(int companyId, int? partnerId, DateOnly upToDate, CancellationToken ct);
}
```

Comenzi interne V4: `SaveDeliveryOrder`, `ReleaseDeliveryOrder`, `RecordPick`, `CompletePicking`, `ReopenDeliveryOrder`, `CancelDeliveryOrder`, `GenerateDeliveryNotes` (din dispoziție, câte unul pe oprire), `SaveDeliveryNote`, `ValidateDeliveryNote`, `RequestUit`, `SaveDeliveryPlan`, `DispatchPlan`, `ConfirmDelivery`, `RecordRefusal`, `ReverseDeliveryNote`, `SaveConsignmentReport`, `ValidateConsignmentReport`.

## 7. Schema SQL `sales` (partea V4)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.DeliveryOrder` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number)`; FK `WarehouseId`, `DepotId`, `DeliveryPlanId`, `AssignedToUserId` | `(CompanyId, WarehouseId, Status, PlannedDate)` |
| `sales.DeliveryOrderLine` | `Id bigint` | FK `SalesOrderLineId`, `SalesOrderScheduleId`, `ItemVariantId`, `DifferenceReasonId`; `CHECK (QtyPicked <= QtyRequested)` | `(SalesOrderLineId)` |
| `sales.DeliveryOrderPick` | `Id bigint` | FK `LineId`, `LotId`, `SerialId`, `LotOverrideReasonId` | `(LineId)` |
| `sales.SalesDeliveryNote` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number) WHERE Number IS NOT NULL AND IsInvoiceDelivery = 0`; FK `PartnerId`, `DeliveryPointId`, `WarehouseId`, `ConsignmentWarehouseId`, `ShippingMethodId`, `CarrierPartnerId`, `DeliveryPlanId`, `InvoiceId` (avizul intern) | `(CompanyId, Status, DocDate)`; `(CompanyId, PartnerId, Status) WHERE Status IN (3, 6, 7)` pentru „nefacturate” |
| `sales.SalesDeliveryNoteLine` | `Id bigint` | FK `SalesOrderLineId`, `DeliveryOrderLineId`, `ItemVariantId`, `UomId`, `VatRateId`, `ParentLineId` | `(SalesOrderLineId)` |
| `sales.SalesDeliveryNoteLot` | `Id bigint` | FK `LineId`, `LotId`, `SerialId` | `(LotId)` pentru trasabilitate (S4) |
| `sales.ETransportDeclaration` | `Id bigint` | UX `(SourceDocumentType, SourceDocumentId, Seq)`; UX `(Uit) WHERE Uit IS NOT NULL` | `(CompanyId, Status)` |
| `sales.ETransportRule` | `Id int` | `CHECK (MinGrossWeightKg >= 0 AND MinValueRon >= 0)` | `(Scope, IsActive)` |
| `sales.DeliveryPlan`, `sales.DeliveryPlanStop` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number)`; FK `VehicleId`, `DriverUserId`, `CarrierPartnerId`; `DeliveryPlanStop` UX `(PlanId, Seq)` | `(CompanyId, PlanDate, WarehouseId)` |
| `sales.Vehicle` | `Id int` | UX `(CompanyId, PlateNoKey)` | — |
| `sales.ConsignmentReport`, `sales.ConsignmentReportLine` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number)`; FK `PartnerId`, `ConsignmentWarehouseId`, `ItemVariantId` | `(CompanyId, PartnerId, PeriodTo)` |
| `core.ETransportOperationType`, `core.ETransportPurpose` (Nucleu, seed) | `Id int` | UX `(Code)`; `ETransportPurpose` FK `OperationTypeId` (scopurile permise pe tip) | — |

```sql
CREATE TABLE sales.SalesDeliveryNote
(
    Id                     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesDeliveryNote PRIMARY KEY,
    CompanyId              int            NOT NULL CONSTRAINT FK_SDN_Company REFERENCES core.Company(Id),
    TransactionTypeId      int            NOT NULL CONSTRAINT FK_SDN_Tt REFERENCES core.TransactionType(Id),
    NumberSeriesId         int            NULL     CONSTRAINT FK_SDN_Series REFERENCES core.NumberSeries(Id),
    Number                 nvarchar(20)   NULL,
    DocDate                date           NOT NULL,
    IsInvoiceDelivery      bit            NOT NULL CONSTRAINT DF_SDN_InvDel DEFAULT (0),
    InvoiceId              bigint         NULL,     -- FK spre sales.SalesInvoice, adăugat de migrarea V5
    PartnerId              int            NOT NULL CONSTRAINT FK_SDN_Partner REFERENCES core.Partner(Id),
    DeliveryPointId        int            NULL     CONSTRAINT FK_SDN_DeliveryPoint REFERENCES sales.CustomerDeliveryPoint(Id),
    PayerPartnerId         int            NULL     CONSTRAINT FK_SDN_Payer REFERENCES core.Partner(Id),
    WarehouseId            int            NOT NULL CONSTRAINT FK_SDN_Warehouse REFERENCES inv.Warehouse(Id),
    DepotId                int            NULL     CONSTRAINT FK_SDN_Depot REFERENCES inv.Depot(Id),
    ConsignmentWarehouseId int            NULL     CONSTRAINT FK_SDN_CsgWarehouse REFERENCES inv.Warehouse(Id),
    CustomerOrderRef       nvarchar(50)   NULL,
    ShippingMethodId       int            NULL     CONSTRAINT FK_SDN_Shipping REFERENCES sales.ShippingMethod(Id),
    CarrierPartnerId       int            NULL     CONSTRAINT FK_SDN_Carrier REFERENCES core.Partner(Id),
    DeliveryPlanId         bigint         NULL     CONSTRAINT FK_SDN_Plan REFERENCES sales.DeliveryPlan(Id),
    VehicleNo              nvarchar(15)   NULL,
    TrailerNo              nvarchar(15)   NULL,
    DriverName             nvarchar(100)  NULL,
    DelegateName           nvarchar(100)  NULL,
    DelegateIdDocument     nvarchar(30)   NULL,
    LoadingAddressId       int            NULL     CONSTRAINT FK_SDN_LoadingAddr REFERENCES core.Address(Id),
    DispatchAt             datetime2(0)   NULL,
    PackagesCount          smallint       NULL,
    NetWeightKg            decimal(19,3)  NOT NULL CONSTRAINT DF_SDN_Net DEFAULT (0),
    GrossWeightKg          decimal(19,3)  NOT NULL CONSTRAINT DF_SDN_Gross DEFAULT (0),
    ETransportStatus       tinyint        NOT NULL CONSTRAINT DF_SDN_Et DEFAULT (0),
    Uit                    varchar(20)    NULL,
    UitValidUntil          date           NULL,
    NetAmount              decimal(19,4)  NOT NULL CONSTRAINT DF_SDN_NetAmt DEFAULT (0),
    VatAmount              decimal(19,4)  NOT NULL CONSTRAINT DF_SDN_Vat DEFAULT (0),
    TotalAmount            decimal(19,4)  NOT NULL CONSTRAINT DF_SDN_Total DEFAULT (0),
    CostAmount             decimal(19,4)  NOT NULL CONSTRAINT DF_SDN_Cost DEFAULT (0),
    PrintValues            bit            NOT NULL CONSTRAINT DF_SDN_PrintVal DEFAULT (1),
    Status                 tinyint        NOT NULL CONSTRAINT DF_SDN_Status DEFAULT (1),
    DeliveredAt            datetime2(0)   NULL,
    ReceivedByName         nvarchar(100)  NULL,
    PodAttachmentId        bigint         NULL     CONSTRAINT FK_SDN_Pod REFERENCES core.Attachment(Id),
    NoteToCustomer         nvarchar(1000) NULL,
    CreatedAt              datetime2(0)   NOT NULL CONSTRAINT DF_SDN_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int            NOT NULL,
    ModifiedAt             datetime2(0)   NULL,
    ModifiedBy             int            NULL,
    ValidatedAt            datetime2(0)   NULL,
    ValidatedBy            int            NULL,
    RowVersion             rowversion     NOT NULL,
    CONSTRAINT CK_SDN_Status CHECK (Status IN (1, 3, 4, 6, 7, 8, 9)),
    CONSTRAINT CK_SDN_Et CHECK (ETransportStatus BETWEEN 0 AND 6),
    CONSTRAINT CK_SDN_Number CHECK (Status IN (1, 9) OR Number IS NOT NULL OR IsInvoiceDelivery = 1),
    CONSTRAINT CK_SDN_Consignment CHECK (ConsignmentWarehouseId IS NULL OR ConsignmentWarehouseId <> WarehouseId),
    CONSTRAINT CK_SDN_Uit CHECK (ETransportStatus <> 3 OR (Uit IS NOT NULL AND UitValidUntil IS NOT NULL))
);
```

Proceduri: `sales.usp_DeliveryOrder_Save`, `sales.usp_DeliveryOrder_Release` (scrie propunerea primită de la S4; rezervările specifice le face aplicația prin S5, în aceeași tranzacție), `sales.usp_DeliveryOrder_RecordPick`, `sales.usp_DeliveryOrder_Complete`, `sales.usp_DeliveryNote_Save`, `sales.usp_DeliveryNote_Validate` (numărul, legăturile, starea; mișcarea de stoc o face aplicația prin `IStockLedger`, în aceeași tranzacție), `sales.usp_DeliveryNote_Reverse`, `sales.usp_DeliveryNote_Confirm`, `sales.usp_DeliveryNote_Uninvoiced`, `sales.usp_ETransport_Evaluate` (TVP cu liniile și încărcătura vehiculului), `sales.usp_ETransport_SaveResponse`, `sales.usp_DeliveryPlan_Save`, `sales.usp_DeliveryPlan_Dispatch`, `sales.usp_ConsignmentReport_Save`, `sales.usp_ConsignmentReport_Validate`, `sales.usp_ConsignmentStock_Get`.

**Coduri de eroare V4**

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52350 | `SALES_DO_WAREHOUSE_MIXED` | 400 | Linii din gestiuni diferite pe aceeași dispoziție |
| 52351 | `SALES_DO_LOCKED` | 409 | Modificarea liniilor unei dispoziții lansate (se retrage întâi în ciornă) sau a uneia închise |
| 52352 | `SALES_DELIVERY_OVER_PICKED` | 409 | Aviz din dispoziție peste cantitatea pregătită; cantitate pregătită peste cea cerută |
| 52353 | `SALES_PICK_DIFFERENCE_REASON` | 400 | Diferență de pregătire sau de primire fără motiv de tip 8 |
| 52354 | `SALES_DN_MIXED_CUSTOMERS` | 400 | Aviz cu linii pentru mai mulți clienți sau mai multe puncte de livrare |
| 52355 | `SALES_DN_TRANSPORT_DATA` | 400 | Lipsesc vehiculul sau delegatul, cerute de modul de livrare |
| 52356 | `SALES_ETRANSPORT_UIT_REQUIRED` | 409 | Tipărirea finală, plecarea avizului sau a planului fără UIT valabil, când regulile îl cer și `ETransportCheck` = 2 |
| 52357 | `SALES_ETRANSPORT_UIT_EXPIRED` | 409 | UIT expirat la data plecării |
| 52358 | `SALES_CUSTOMER_ITEM_CODE_MISSING` | 409 | Linie fără codul clientului, când clientul îl cere |
| 52359 | `SALES_DN_VALIDATED_LOCKED` | 409 | Modificarea unui aviz validat (se stornează) |
| 52360 | `SALES_DN_NOT_DISPATCHABLE` | 409 | Plecare sau confirmare pe un aviz nevalidat sau stornat |
| 52361 | `SALES_CONSIGNMENT_WAREHOUSE_INVALID` | 400 | Gestiunea de destinație nu e de tipul „La terți” a acestui client |
| 52362 | `SALES_CONSIGNMENT_OVER_STOCK` | 409 | Raport de consum peste stocul din consignație al clientului |
| 52363 | `SALES_RECEIVED_OVER_DELIVERED` | 400 | Cantitatea primită de client mai mare decât cea livrată |
| 52364 | `SALES_REFUSAL_AFTER_INVOICE` | 409 | Stornare de refuz pe un aviz deja facturat (se face retur, V6) |
| 52365 | `SALES_PLAN_VEHICLE_BUSY` | 409 | Același vehicul pe două planuri neîncheiate în aceeași zi |
| 52366 | `SALES_PLAN_OVER_CAPACITY` | 409 | Încărcătura depășește capacitatea vehiculului (avertizare; blocare cu setarea vehiculului `StrictCapacity`) |
| 52367 | `SALES_POD_REQUIRED` | 409 | Factura din aviz fără confirmarea livrării, cu `PodRequiredForInvoice` = 1 |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.DeliveryOrder.View` / `.Edit` / `.Release` | Dispozițiile de livrare |
| `Sales.DeliveryOrder.Pick` | Înregistrarea cantităților pregătite și a diferențelor |
| `Sales.DeliveryNote.View` / `.Edit` / `.Validate` | Avizele |
| `Sales.DeliveryNote.Reverse` | Stornarea avizului nefacturat, inclusiv refuzul |
| `Sales.DeliveryNote.Confirm` | Confirmarea livrării |
| `Sales.DeliveryPlan.Manage` | Planurile de livrare, vehiculele, plecarea |
| `Sales.ETransport.Manage` | Cererea, corecția și anularea UIT; regulile e-Transport |
| `Sales.Consignment.Manage` | Gestiunile de la client, avizele și rapoartele de consum |

Roluri livrate (V1 §8, completate): **Șef depozit** (toate permisiunile V4 pe gestiunile lui), **Gestionar** (`DeliveryOrder.View`, `.Pick`, `DeliveryNote.View`, `.Edit`), **Agent de teren** (`DeliveryNote.*` pe gestiunea mașinii lui, fără `Reverse`), **Operator facturare** (`DeliveryNote.View`, `.Confirm`), **Șef vânzări** (vizualizare, `Consignment.Manage`), **Contabil șef** (vizualizare, `ETransport.Manage` pe reguli).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V4 |
| --- | --- |
| Avizul de însoțire a mărfii (OMFP 2634/2015, formularul 14-3-6A) | Serie fiscală continuă; datele expedierii (delegat, act de identitate, mijloc de transport); valorile opționale pe tipărit |
| Termenul de facturare (Codul fiscal, art. 319 alin. (16)) | Avizele nefacturate în luna livrării se facturează până pe 15 ale lunii următoare; alertele din 3.9 |
| e-Transport (OUG 41/2022, cu modificările ulterioare, și ordinul comun privind bunurile cu risc fiscal ridicat) | Regulile din 3.7, configurabile cu dată; declarația prin 02.14; codul UIT pe aviz înainte de plecare. Pragurile și lista de coduri NC se verifică la punerea în funcțiune |
| Consignația (Codul fiscal, art. 270 alin. (10)) | Transferul în consignație nu e livrare; livrarea are loc la data la care clientul vinde sau consumă bunurile, deci factura e pe raportul de consum (V5) |
| Durabilitatea minimală (Regulamentul UE 1169/2011) și cerințele clienților | Durata minimă rămasă la livrare (S4), verificată la lansare și la validare |
| GDPR | Din actul de identitate al delegatului se păstrează doar seria și numărul; copiile semnate ale avizelor au retenția documentelor contabile |

Integrări externe: e-Transport prin Integrări 02.14; EDI (`DESADV` la livrare, opțional, către lanțurile care îl cer, prin același conector de fișiere ca în V3). Interne: V3 (comenzile, rezervările), Stocuri S1 (gestiunile, tipul „La terți”, `DeliveryOrderMode`), S2 (mișcarea, stornarea, transferul), S3 (costul), S4 (loturi, FEFO, durata minimă), S5 (rezervări specifice și consum), S6 (numărătoarea la `LIPSARAFT`), C2 (contarea pe `418`), V5 (facturarea avizelor, avizul intern), V6 (retururile), WMS și TMS (viitor, prin contractele din 6).

**Precizări aplicate altor documente la validarea V4 (27.11.2026):**

- **V1 §3.8**: `sales.ReasonCode` tipul 8 „diferență la pregătire sau la livrare”, cu 4 motive în seed (32 în total). **V1 §3.4**: coloanele `PrintValuesOnDeliveryNote`, `ConsignmentReportFrequency`. **V1 §3.9**: setările din 3.11.
- **Stocuri S1**: coloana `DeliveryOrderMode` pe gestiune. **S5**: rezervarea specifică pe lot la lansarea dispoziției și revenirea la generică la retragere sau anulare.
- **Nucleu**: nomenclatoarele `core.ETransportOperationType` și `core.ETransportPurpose`, cu seed, folosite de Vânzări, Stocuri S2 și Achiziții A4.
- **Contabilitate C2**: modelul de contare al avizului nefacturat (`418` / `707` / `4428`) și al stornării lui.
- **Integrări 02.14**: portul `IETransportGateway` și evenimentul `ETransportRaspuns`.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| De livrat azi și mâine | Liniile de comandă cu data promisă, pe gestiune, zonă și client, cu rezervat, blocat și pregătit | Șef depozit, Șef vânzări |
| Productivitate la pregătire | Dispoziții, linii și cantități pregătite pe pregătitor și zi; diferențele pe motiv | Șef depozit |
| Avize nefacturate | Pe client și vechime, cu termenul legal; soldul `418` la zi, comparat cu contabilitatea | Operator facturare, Contabil șef |
| Livrări la timp și complete (OTIF) | Pe client și lună: avizele livrate la data promisă și integral, față de total; cauzele (pregătire, refuz, stoc) | Director comercial, Șef depozit |
| Refuzuri și diferențe | Cantitățile refuzate sau lipsă pe motiv, articol, șofer, client | Șef depozit, Calitate |
| e-Transport | Avizele cu UIT cerut, obținut, respins, expirat; cele plecate fără UIT (cu `ETransportCheck` = 1) | Contabil șef |
| Stoc în consignație | Pe client: stocul la client, vechimea, ultimul raport de consum, valoarea la cost și la preț | Șef vânzări, Contabil |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v4-livrare/](ecrane/v4-livrare/). Meniul Vânzări are Avize (sus) și grupul Livrare (Dispoziții de livrare, De livrat azi, e-Transport, Consignație).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| De livrat azi | Planul zilei pe vehicule, cu opririle, încărcătura, starea dispozițiilor și a avizelor, codurile UIT | `v4-01-plan-livrare` |
| Dispoziție de livrare | Formularul `DL 0412`: liniile pe clienți, loturile propuse și pregătite, diferența la CEAI05, coletele și masa | `v4-02-dispozitie` |
| Aviz | Formularul avizului `EXD 30625`: antetul cu datele de transport și UIT, liniile cu loturile și valorile, legăturile | `v4-03-aviz` |
| Generare avize din dispoziție | Pasul asistentului: un aviz pe oprire, cu valorile și verificările | `v4-04-generare-avize` |
| e-Transport | Declarația avizului: regula aplicată, tipul și scopul, locurile, vehiculul, liniile cu cod NC și masă, starea UIT; regulile configurate | `v4-05-etransport` |
| Avize | Lista avizelor cu starea, valoarea, UIT, confirmarea, ce e de facturat | `v4-06-lista-avize` |
| Confirmarea livrării și refuzul | `EXD 30625`: cantitățile primite, refuzul APA05 cu motiv, stornarea parțială generată | `v4-07-confirmare-livrare` |
| Consignație | Stocul Market Turda în `CSG-MT01`, avizele de consignație, raportul de consum `RCS 0009`, ce se facturează | `v4-08-consignatie` |
| Avize nefacturate | Pe client și vechime, cu alerta și termenul legal, soldul `418` | `v4-09-avize-nefacturate` |
| Configurare › Livrare | Setările din 3.11, regulile e-Transport, vehiculele | `v4-10-configurare-livrare` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V4-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V4-A1 | `DL 0412` lansată transformă rezervările generice ale liniilor în rezervări specifice pe `L2611-07`, `L2610-30`, `L2609-22`; retragerea în ciornă le face din nou generice | Test de integrare (cu S4, S5) |
| V4-A2 | Diferența de 24 buc CEAI05 cere motiv (`52353`); avizul `EXD 30625` are 264 buc, iar `CV 4441` păstrează 24 buc de livrat | Test de integrare |
| V4-A3 | O dispoziție cu linii din DEP-CJ și DEP-TM e refuzată (`52350`); un aviz cu două puncte de livrare e refuzat (`52354`) | Test de integrare |
| V4-A4 | Validarea `EXD 30625`: valorile 2.715,60 + 375,28 = 3.090,88 lei; o mișcare `ISSUE_SALE` pe fiecare lot cu consumul rezervării; contarea `418` / `707` / `4428`; totul într-o tranzacție (o eroare la stoc nu lasă număr fiscal consumat) | Test de integrare (cu S2, C2) |
| V4-A5 | Comanda cu `CreditHold` sau `AdvanceHold` nu intră în dispoziție și nu se livrează (`52005`, `52222`) | Test de integrare |
| V4-A6 | Regula națională pe capitolul NC 22 cu pragul de 500 kg pe vehicul: camionul cu 1.400,16 kg cere UIT pe ambele avize; un aviz singur de 330 kg, nu. Plecarea fără UIT e refuzată (`52356`) | Test unitar (reguli) + integrare |
| V4-A7 | `ETransportRaspuns` scrie UIT-ul și valabilitatea; un UIT expirat la plecare dă `52357`; schimbarea vehiculului cheamă `CorrectAsync` | Test de integrare (cu un gateway simulat) |
| V4-A8 | Refuzul a 24 buc APA05: `AV-STO` intră la costul ieșirii (1,21), `418` scade cu 45,29 lei, `CV 4441` are din nou 24 buc de livrat; refuzul pe un aviz facturat e refuzat (`52364`) și propune returul | Test de integrare (cu S2, S3) |
| V4-A9 | `AV-CSG` mută 600 buc CEAI05 în `CSG-MT01` la cost, fără venit; raportul de 420 buc e sursa `FV-CSG`; un raport de 700 buc e refuzat (`52362`) | Test de integrare |
| V4-A10 | Bonusul se livrează proporțional: 360 buc APA15 livrate → 72 buc bonus; 330 livrate → 66 | Test unitar |
| V4-A11 | Numerotarea avizelor e continuă și fără goluri la validări concurente pe aceeași serie; avizul intern al facturii cu livrare nu consumă numere din seria avizelor | Test de integrare (concurență) |
| V4-A12 | Avizul nefacturat din octombrie apare ca alertă roșie pe 02.11 și 418 din rapoarte egalează soldul contabil al `418` | Test de integrare |
| V4-A13 | Toate procedurile V4 rulează corect pe baza case sensitive; numărul de înmatriculare `cj 07 exd` e găsit ca `CJ 07 EXD`, dar nu se poate crea ca vehicul nou | Test de integrare |

Criteriile V4-A4, V4-A5, V4-A6 și V4-A11 sunt critice: ele decid ieșirea corectă din stoc și din rezervare, blocarea livrărilor neplătite, conformitatea e-Transport și numerotarea fiscală.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Când e nevoie de dispoziția de livrare | Setare pe gestiune: doar aviz direct, ambele, sau doar prin dispoziție; depozitele mari folosesc dispoziția, magazinele și mașinile agenților avizul direct | 2 regula 1, 3.11 | ✓ DA (27.11.2026) |
| 2 | Loturile la pregătire | Lansarea dispoziției fixează loturile FEFO prin rezervare specifică (S5); pregătitorul poate schimba lotul, cu motiv | 2 regula 3, 3.1 | ✓ DA (27.11.2026) |
| 3 | Lipsurile la pregătire | Cer motiv (tip nou de motiv, „diferență la pregătire sau la livrare”); restul rămâne pe comandă sau se închide, după acceptul clientului pentru livrări parțiale; „lipsă la raft” cere numărătoare în stoc | 2 regula 4, 3.5 | ✓ DA (27.11.2026) |
| 4 | Stările avizului | Ciornă → Validat → Plecat → Livrat → Închis (facturat), plus Stornat; Plecat și Livrat opționale pe firmă | 3.2 | ✓ DA (27.11.2026) |
| 5 | Refuzul la livrare | Înainte de factură: stornare parțială a avizului, la costul ieșirii, cu restul înapoi pe comandă; după factură: retur (V6) | 2 regula 5, 3.8, 4.3 | ✓ DA (27.11.2026) |
| 6 | e-Transport | Reguli configurabile cu dată (internațional mereu; național după codurile NC cu risc fiscal ridicat și pragurile pe vehicul); un UIT pe aviz, cerut automat la validare prin Integrări; fără UIT valabil, avizul nu se tipărește final și nu pleacă | 2 regula 7, 3.7 | ✓ DA (27.11.2026) |
| 7 | Planul de livrare | Plan simplu pe zi, vehicul și șofer, cu opririle, masa și valoarea, avertizare la capacitate; fără optimizare de rute până la TMS | 2 regula 9, 3.4 | ✓ DA (27.11.2026) |
| 8 | Consignația | Gestiune „La terți” pe client, aviz de consignație la cost, raport de consum periodic (manual sau importat) ca sursă a facturii, inventar la client | 2 regula 8, 3.10, 4.5 | ✓ DA (27.11.2026) |
| 9 | Valorile și contarea avizului | Avizul are mereu valori (din comandă); tipărirea lor e opțională pe client; avizul nefacturat se contează pe `418` / `707` / `4428` la validare | 2 regula 6, 3.9 | ✓ DA (27.11.2026) |
| 10 | Confirmarea livrării | Opțională, cu avizul semnat scanat și cantitatea primită pe linii; setare pentru firmele care facturează doar după confirmare | 3.8, 3.11 | ✓ DA (27.11.2026) |
| 11 | Nomenclatoare și precizări | Motivele de tip 8 (4, în seed), tipurile și scopurile e-Transport în Nucleu (seed), vehiculele; precizările din 9 pentru V1, S1, S5, Nucleu, C2 și 02.14 | 3.5, 3.7, 9 | ✓ DA (27.11.2026) |

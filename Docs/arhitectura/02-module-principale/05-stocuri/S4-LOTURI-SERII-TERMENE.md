# ERP · 02.05 Stocuri · S4 Loturi, serii și termene de valabilitate

26 noiembrie 2026 · Aurelian · **Validat (26.11.2026)**. A patra din cele 7 părți ale modulului 02.05 Stocuri. Pornește de la S1, S2 și S3, toate validate pe 26.11.2026. Urmărirea pe lot sau pe serie e o setare a modelului de articol (`ItemStockSettings.TrackingMode`, S1 §3.4): 0 fără, 1 lot, 2 lot și termen de valabilitate, 3 serie. S4 adaugă lotul și seria ca dimensiune a stocului, alege loturile la ieșire (FEFO), ține stările lotului (carantină, blocat, expirat, retras) și dă trasabilitatea înainte și înapoi.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori S4 `54450–54549`. Documente părinte: [S1 Gestiuni, depozite și setări](S1-GESTIUNI-SI-SETARI.md), [S2 Mișcări și documente](S2-MISCARI-SI-DOCUMENTE.md), [S3 Evaluare și cost](S3-EVALUARE-SI-COST.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Codurile de lot și de serie se compară **exact**, cu majuscule și minuscule (`L2611-07` ≠ `l2611-07`); căutarea din ecrane folosește coloana `...Search` cu `Latin1_General_100_CI_AI_SC_UTF8` (`SQL-22`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Stocul stă pe variantă (`ItemVariantId`, SKU), în UM de bază. Cheia stocului din S2 e (gestiune, variantă, lot) sau (depozit, gestiune, variantă, lot); `inv.Stock` are deja coloana `LotKey` = `ISNULL(LotId, 0)`. Seria nu intră în cheia lui `inv.Stock`: o serie e o singură bucată, urmărită în `inv.Serial` (3.4).
- **SKU și CKU** (decizia lui Aurelian, 26.11.2026). **SKU** e varianta (`core.ItemVariant`): ce este produsul (iaurt 250 g, grăsime 2,5%). **CKU** e unitatea urmărită pe lot: SKU + lot + termen de valabilitate, un rând în `inv.Lot`. Fiecare are un cod vizibil (SKU: `Code`; CKU: `CkuCode`, de exemplu `IAU250-25·I123·261212`) și un id unic `uniqueidentifier` (SKU: `ItemVariant.Uid`, Nucleu; CKU: `Lot.CkuUid`), folosit pe etichete, la scanare și la sincronizarea cu POS, WMS și alte instalări. Cheia primară rămâne `int` (`SQL-14`).
- Toate mișcările trec prin `inv.usp_StockMovement_Post` (S2). S4 rulează **în aceeași tranzacție**: validează sau creează lotul, alege lotul când documentul nu îl dă, verifică starea lotului și a seriei. Costul pe lot (identificare directă) e în S3.
- Alte module (Vânzări, Achiziții, Producție, WMS) trimit lotul și seria prin portul `IStockLedger` (S2 §6), extins de S4 cu propunerea de loturi (6).
- **Loturile și seriile nu pot fi niciodată pe minus** (S1, regula comună). Faptele din afara ERP (POS) fără lot cunoscut intră pe lotul tehnic `NEIDENTIFICAT`, corectat cu documentul de schimbare a lotului (3.7).
- **Ce NU face S4:** nu face controlul de calitate propriu-zis (buletinele de analiză se atașează la lot, decizia e a responsabilului de calitate), nu rezervă loturi (S5, care primește lotul ales de S4), nu face genealogia de producție (o primește de la Producție 02.08), nu gestionează garanția și service-ul (Service 02.11 citește seria și data vânzării) și nu tipărește etichete (WMS).

## 1. Scop și perimetru

| Zonă | Ce face S4 | Ce NU face (și cine face) |
| --- | --- | --- |
| Loturi | Crearea lotului la prima intrare, atributele (lot furnizor, data fabricației, termenul, țara de origine), unicitatea | Recepția (Achiziții), producția (Producție) |
| Termene | Tipul termenului (data limită de consum sau data durabilității minimale), durata minimă rămasă la vânzare, avertizarea, expirarea automată | Prețul redus pentru produsele aproape de expirare (Vânzări) |
| Alegerea la ieșire | Propunerea FEFO, verificarea lotului ales, alegerea automată pentru documentele și faptele fără lot | Picking-ul fizic pe locații (WMS) |
| Stări | Disponibil, carantină, blocat, expirat, retras; istoricul schimbărilor | Analizele de laborator |
| Serii | Seria fiecărei bucăți, starea și locul ei, istoricul, data vânzării | Garanția și reparațiile (Service) |
| Trasabilitate | Înapoi (furnizor, recepție) și înainte (clienți, transferuri, consum), pe lot și pe lot de furnizor | Genealogia materiilor prime în produse (Producție o scrie în `inv.LotGenealogy`) |
| Retragere | Documentul de retragere: blocarea lotului, lista clienților și a cantităților, stocul rămas | Notificarea autorităților și a clienților (manual, cu lista din ERP) |
| Coduri de bare | Citirea codurilor GS1 (GTIN, lot, termen, serie) | Tipărirea etichetelor (WMS) |

## 2. Model de domeniu

```
 ItemStockSettings (S1, pe model și firmă)
   TrackingMode · ExpiryKind · ShelfLifeDays · MinRemainingShelfLifeDays · ReceiptQuarantine · SerialCaptureMode · LotNumbering
        │
        ▼
 Lot = CKU (firmă + SKU + cod de lot + termen; CkuCode vizibil, CkuUid unic) ──< LotStatusHistory
   lot furnizor, data fabricației, țara, starea
        │
        ├──< Stock (S2, cheia are LotKey)  ──< StockMovement (S2, LotId)
        ├──< CostLayer (S3, la identificare directă)
        └──< LotGenealogy (lot sursă → lot rezultat: producție, reambalare)

 Serial (variantă + număr de serie) ── starea, gestiunea curentă, lotul, data vânzării
        └──< StockMovement (S2, SerialId, câte o mișcare de ±1 pe serie)

 LotChange (documentul SL: schimbare lot, corectarea lui NEIDENTIFICAT)
 Recall (documentul RL: retragerea unui lot) ──< RecallLot ──< RecallCustomer (instantaneu)
 PartnerShelfLifeRule (durata minimă rămasă cerută de un client)
```

Reguli de business ale modelului:

1. **Un CKU e identificat de firmă + SKU + cod de lot + termen** (decizia 1). Exemplu: iaurt 250 g 2,5% și iaurt 250 g 10%, amândouă cu lotul `I123` și termenul 12.12.2026, sunt două SKU-uri diferite, deci două CKU-uri diferite: `IAU250-25·I123·261212` și `IAU250-10·I123·261212`.
2. **Aceeași combinație se adună, una nouă creează alt CKU.** Recepția de azi și cea de mâine din iaurtul 2,5%, lotul `I123`, termenul 12.12.2026, intră pe același CKU, iar cantitățile se adună. Poimâine, lotul `I1234` cu termenul 16.12.2026 creează un CKU nou; SKU-ul rămâne același. Același cod de lot cu alt termen creează tot un CKU nou, dar recepția cere confirmare (`54452`), pentru că de obicei e o greșeală de introducere. Același CKU în altă firmă a grupului are alt `CkuUid`, iar legătura se face prin `CkuCode` și `SupplierLotCode`.
3. **Straturile de cost rămân pe recepție, chiar în același CKU.** CKU-ul adună cantitățile, dar FIFO și LIFO (S3) țin câte un strat de cost pentru fiecare intrare, cu prețul și data ei. La CMP, costul e media cheii de cost (gestiune + SKU + CKU). Termenul e al CKU-ului, nu al mișcării: toate bucățile unui CKU au același termen.
4. **Lotul e pe firmă, starea lui e pe firmă.** Carantina, blocarea, expirarea și retragerea se aplică lotului în toate gestiunile firmei, inclusiv în tranzit. Stocul unui lot se vede pe gestiuni în `inv.Stock`.
5. **O serie e o bucată.** Variantele urmărite pe serie au UM de bază „buc” cu factorul 1 (`54470` altfel). Fiecare mișcare a unei serii are cantitatea ±1. O serie are în orice moment o singură stare și cel mult o gestiune.
6. **Ieșirea respectă starea.** Doar loturile disponibile se vând, se transferă la clienți sau se consumă. Loturile în carantină, blocate, expirate sau retrase pot avea doar mișcările din tabelul din 3.3 (descărcare pentru casare, retur la furnizor, transfer în gestiunea de carantină, inventar).
7. **FEFO propune, omul poate schimba** (decizia 2). Propunerea e mereu primul lot care expiră și respectă condițiile clientului. Schimbarea lotului propus e permisă și se jurnalizează pe linie (`LotOverrideReason`).
8. **Fără lot pe minus.** O ieșire pe un lot cu cantitate insuficientă e refuzată (`54455`), indiferent de politica gestiunii. Singura excepție sunt faptele POS, care merg pe `NEIDENTIFICAT` (3.7).

## 3. Funcționalitate

### 3.1 Setările pe model (coloane noi în `inv.ItemStockSettings`)

S4 adaugă coloanele de mai jos la tabelul din S1 (pe firmă, moștenite de variante). Se schimbă doar cât timp modelul nu are stoc în firmă, la fel ca `TrackingMode` (`54010`), cu excepția duratelor, care se pot schimba oricând.

| Setare | Tip, implicit | Ce face |
| --- | --- | --- |
| `ExpiryKind` | `tinyint`, 1 | Doar pentru `TrackingMode` = 2. 1 **Data limită de consum** („expiră la”): la expirare lotul trece automat în starea Expirat și nu mai poate fi vândut. 2 **Data durabilității minimale** („a se consuma de preferință înainte de”): lotul rămâne disponibil; după dată se vinde sau se donează doar cu dreptul `Inv.Lot.SellAfterBestBefore` (decizia 3) |
| `ShelfLifeDays` | `int NULL` | Durata de valabilitate de la fabricație. Dacă e completată, termenul se propune la recepție ca data fabricației + durata |
| `MinRemainingShelfLifeDays` | `int`, 0 | Câte zile trebuie să mai aibă lotul la livrarea către un client. Suprascrisă pe client (3.2, decizia 4) |
| `ReceiptQuarantine` | `bit`, 0 | Loturile noi intră în carantină la recepție și se eliberează de responsabilul de calitate (3.3, decizia 5) |
| `LotNumbering` | `tinyint`, 1 | 1 codul de lot vine de la furnizor sau de pe ambalaj (scanat, GS1 AI 10); 2 codul se generează din seria Nucleu `LOT-{variantă}` (pentru producția proprie fără cod de furnizor) |
| `SerialCaptureMode` | `tinyint`, 1 | Doar pentru `TrackingMode` = 3. 1 **la intrare și la ieșire**: seria se scanează la recepție și se verifică la ieșire. 2 **doar la ieșire**: la recepție intră cantitatea, seriile se citesc la livrare (bunuri ieftine cu seria producătorului) (decizia 6) |
| `WarrantyMonths` | `int NULL` | Luni de garanție de la vânzare, pentru seria vândută. Service (02.11) o citește |

Setările pe firmă din S1 folosite de S4: `ExpiryPickRule` (1 FEFO, 2 FIFO după data recepției lotului) și `ExpiryWarningDays` (30). S4 mai adaugă în `inv.StockSettings`: `UnidentifiedLotCloseCheck` (1 blocant cu override, 2 doar avertizare, 0 dezactivat; implicit 1, decizia 8).

### 3.2 Loturile

**Crearea.** Lotul (CKU-ul) se creează la prima intrare cu o combinație nouă de SKU, cod de lot și termen, în tranzacția mișcării: recepție (Achiziții), predare din producție (Producție sau nota `PF`), încărcare (S2), plus de inventar (S6). Mișcarea trimite `NewLotCode`, `ExpiryDate`, opțional `ProductionDate`, `SupplierLotCode`, `CountryOfOriginId`. Un transfer, un retur sau o ieșire trimit `LotId`, nu cod nou.

| Câmp | Reguli |
| --- | --- |
| `LotCode` | `nvarchar(40)`, obligatoriu, fără spații la capete; comparat exact. Cu `LotNumbering` = 2 se generează din seria Nucleu |
| `CkuCode` | `nvarchar(120)`, codul vizibil al CKU-ului, generat la creare și neschimbat: `{SKU}·{lot}·{termen AAMMZZ}`, iar fără termen `{SKU}·{lot}`. Apare pe documente, etichete și ecrane |
| `CkuUid` | `uniqueidentifier`, unic, generat la creare (`NEWID()`); pe eticheta internă, în codul QR sau GS1 DataMatrix, alături de `CkuCode` |
| `SupplierLotCode` | Codul de pe ambalajul furnizorului, dacă diferă. Folosit la căutare și la retragere (legătura între firmele grupului) |
| `ExpiryDate` | Obligatoriu pentru `TrackingMode` = 2 (`54451`); trebuie să fie după data fabricației și după data recepției (altfel avertisment și dreptul `Inv.Lot.ReceiveExpired`) |
| `ProductionDate` | Opțional; dacă `ShelfLifeDays` e completat, termenul propus = fabricația + durata |
| `CountryOfOriginId` | Opțional; folosit de Intrastat (S7) și de etichete |
| `SupplierPartnerId`, `FirstReceiptMovementId` | Completate la creare, pentru trasabilitatea înapoi |
| `Status` | 1 Disponibil · 2 Carantină · 3 Blocat · 4 Expirat · 5 Retras (3.3) |

**Durata minimă rămasă la livrare.** Un lot poate fi livrat clientului C doar dacă `ExpiryDate − data livrării ≥ durata minimă`. Durata minimă se ia, în ordine: regula clientului pentru variantă sau grupa de articole (`inv.PartnerShelfLifeRule`), apoi `ItemStockSettings.MinRemainingShelfLifeDays`. Lotul sub limită nu e propus și e refuzat pentru acel client (`54458`), cu excepția dreptului `Inv.Lot.OverrideShelfLife`, cu motiv. Se aplică doar ieșirilor către clienți (vânzare, aviz), nu transferurilor interne.

**Avertizarea de expirare.** Jobul zilnic `inv.usp_Lot_ExpiryScan` (Hangfire, 00:05 ora firmei) face trei lucruri:

1. Loturile cu `ExpiryKind` = 1 și `ExpiryDate` < azi trec în starea Expirat (cu istoric și eveniment).
2. Loturile care expiră în următoarele `ExpiryWarningDays` zile intră în notificarea zilnică a gestionarului (Nucleu, notificări), una pe gestiune.
3. Raportul „Expirări” (ecranul `s4-02`) se actualizează pe date, nu din job.

### 3.3 Stările lotului

| Stare | Cum ajunge acolo | Ce mișcări de ieșire sunt permise | Cine o schimbă |
| --- | --- | --- | --- |
| 1 Disponibil | Recepție fără carantină; eliberare din carantină; deblocare | Toate | — |
| 2 Carantină | Recepție cu `ReceiptQuarantine` = 1; trecere manuală (suspiciune) | Retur la furnizor, descărcare pentru casare, transfer între gestiuni proprii, inventar | Responsabil calitate (`Inv.Lot.Release`) |
| 3 Blocat | Manual, cu motiv (reclamație, analiză) | La fel ca la carantină | `Inv.Lot.Block` |
| 4 Expirat | Jobul zilnic, doar pentru data limită de consum | Descărcare pentru casare sau donare (S2, motivele `EXPIRED`, `DONATION`), retur la furnizor, inventar | Automat; nu se poate „dezexpira” |
| 5 Retras | Documentul de retragere `RL` (3.6) | Retur la furnizor, descărcare pentru casare, inventar | Documentul `RL`; închiderea retragerii nu readuce lotul la Disponibil |

**Carantina e o stare a lotului, nu o gestiune separată** (decizia 5). Marfa rămâne în gestiunea în care a fost recepționată, iar disponibilul (S5) o exclude. Când e nevoie de separare fizică, se face cu WMS (o zonă de carantină) sau cu un transfer într-o gestiune dedicată, dar starea rămâne pe lot. Pentru articolele fără lot, carantina se face doar printr-o gestiune dedicată, fiindcă nu au un lot care să poarte starea.

**Returul de la client** (Vânzări V6 §3.2) e carantină prin gestiune dedicată, nu prin starea lotului: marfa intră în gestiunea de retur asociată gestiunii de livrare (S1 `ReturnWarehouseId`, `Purpose` = 2), pe lotul livrat, iar starea lotului nu se schimbă (restul lotului, din alte gestiuni, rămâne vandabil). Gestiunea de retur e exclusă din disponibil (S5) și acceptă doar transferul, casarea și returul la furnizor. Decizia de calitate (Vânzări V6) face transferul înapoi în gestiunea vandabilă, casarea sau returul la furnizor (precizare din Vânzări V6, 27.11.2026).

Fiecare schimbare de stare scrie un rând în `inv.LotStatusHistory` (de la, la, motiv, document, utilizator, moment) și publică `LotStareSchimbata`.

### 3.4 Urmărirea individuală: seriile și identificatorii bucății

**Modul de urmărire** se alege pe model (`ItemStockSettings.TrackingMode`, S1 §3.4), după natura articolului:

| `TrackingMode` | Ce se ține | Exemple |
| --- | --- | --- |
| 0 Fără | Doar cantitatea pe gestiune. Toată cantitatea recepționată se adună, fără lot și fără serie | Piuliță M10 10×5×5, cărămidă 240×115×63, șuruburi, folie |
| 1 Lot | Cantitatea pe lot (CKU fără termen) | Vopsea pe șarjă, ciment pe lot de fabricație |
| 2 Lot și termen | Cantitatea pe lot și termen (CKU) | Iaurt, apă minerală, sirop |
| 3 Serie | Fiecare bucată, cu seria și identificatorii ei | Dozatoare, telefoane, laptopuri, utilaje |
| 4 Serie și lot | Fiecare bucată, plus lotul de fabricație și, opțional, termenul (adăugat de S4) | Dispozitive medicale, baterii, airbaguri |

Articolele cu modul 0 nu au nimic de identificat pe bucată: recepția de 10.000 de piulițe intră ca 10.000 buc pe SKU, iar ieșirea scade cantitatea. Pentru ele nu se cer și nu se văd câmpuri de lot sau serie.

**Seria** (`inv.Serial`, o bucată):

| Câmp | Reguli |
| --- | --- |
| `SerialNo` | `nvarchar(60)`, unic pe variantă în toată baza (grupul de firme, decizia 6), comparat exact |
| `ItemVariantId`, `LotId` | Varianta; lotul opțional (o serie poate aparține unui lot de fabricație) |
| `CompanyId` | Firma care deține bucata acum. Se schimbă la vânzarea între firmele grupului |
| `Status` | 1 În stoc · 2 În tranzit · 3 Vândută · 4 Returnată de client (în stoc, cu istoric) · 5 Casată · 6 Blocată |
| `WarehouseId`, `DepotId` | Gestiunea curentă, doar pentru stările 1, 2, 4, 6 |
| `SoldAt`, `SoldToPartnerId`, `WarrantyUntil` | Completate la vânzare; garanția = data vânzării + `WarrantyMonths` |

Mișcările seriilor:

- Fiecare serie are mișcarea ei, cu `QtyBase` = ±1 și `SerialId` (S2 are coloana). Un document cu 20 de bucăți are 20 de mișcări. Volumele sunt mici pentru bunurile pe serie, deci nu e nevoie de un tabel separat de legătură.
- Cu `SerialCaptureMode` = 1, recepția cere toate seriile (`54460` dacă lipsesc), iar ieșirea cere serii aflate în stoc în gestiunea sursă (`54461`).
- Cu `SerialCaptureMode` = 2, recepția intră pe cantitate, iar seriile se creează la ieșire, când se citesc (`IsCapturedAtIssue` = 1). Stocul fără serie al variantei e ținut în `inv.Stock`, ca la un articol fără urmărire, iar ieșirea îl scade și creează seria cu starea Vândută.
- Returul de la client al unei serii vândute o readuce la starea 4 în gestiunea de retur. Un retur pentru o serie necunoscută sau vândută de altă firmă e refuzat (`54462`).

**Identificatorii suplimentari ai bucății** (decizia 6). Pe lângă numărul de serie, o bucată poate avea alte coduri care o identifică: IMEI, adresă MAC, seria de șasiu (VIN), seria motorului, codul de garanție, numărul de piesă al producătorului. Aceștia sunt configurabili, nu coloane fixe:

| Element | Ce conține |
| --- | --- |
| `inv.SerialIdentifierType` (nomenclator) | Codul (`IMEI`, `IMEI2`, `MAC`, `VIN`, `ENGINE`, `WARRANTY`, `MPN`), denumirea, masca de validare (expresie regulată) și verificarea specială (de exemplu cifra de control Luhn la IMEI, 17 caractere fără I, O, Q la VIN). Livrat cu tipurile de mai sus; firma poate adăuga altele |
| `inv.ItemSerialIdentifierRule` | Pe familie de articole sau pe model (modelul are prioritate): ce tipuri are bucata, în ce ordine, dacă sunt obligatorii la intrare sau la ieșire și dacă valoarea trebuie să fie unică. Exemplu: telefonul are seria, `IMEI` (obligatoriu, unic), `IMEI2` (opțional, unic) și `MAC` (opțional, unic); utilajul are seria, `VIN` (obligatoriu, unic) și `ENGINE` (obligatoriu, unic) |
| `inv.SerialIdentifier` | Valorile pe bucată: `SerialId`, tipul, valoarea, plus `ValueSearch` (`Latin1_General_100_CI_AI_SC_UTF8`) pentru căutare. Unicitatea se aplică pe tip, în toată baza, prin index unic filtrat pe tipurile marcate unice |

Reguli:

- Identificatorii se citesc o dată cu seria (scanare sau tastare). Dacă unul obligatoriu lipsește, mișcarea e refuzată (`54473`). O valoare care nu respectă masca sau cifra de control e refuzată (`54471`), iar o valoare unică deja folosită e refuzată (`54472`).
- Căutarea unei bucăți (ecranul Serii, Vânzări, Service, POS) merge după oricare identificator: seria, IMEI-ul, MAC-ul sau VIN-ul găsesc aceeași bucată.
- Corectarea unui identificator după intrare se face cu `Inv.Serial.EditIdentifiers`, cu motiv și istoric (valoarea veche rămâne în `inv.SerialIdentifierHistory`).
- Identificatorii apar pe documentele de ieșire (aviz, factură, certificatul de garanție) dacă regula îi marchează „pe documente”.
- **Identificarea directă pe serie** (S3): stratul de cost are `SerialId`, iar ieșirea seriei consumă exact stratul ei.

### 3.5 Alegerea lotului la ieșire (FEFO)

Ordinea de propunere, pentru o ieșire din gestiunea G (și depozitul D, la nivelul „depozit”) a variantei V, cantitatea Q, către clientul C la data L:

1. Se iau loturile cu stoc în G (D) pentru V, cu starea Disponibil.
2. Se exclud loturile rezervate pentru alte documente (S5 dă cantitatea rezervată pe lot).
3. Pentru ieșirile către clienți, se exclud loturile cu `ExpiryDate − L` sub durata minimă a lui C (3.2). La data durabilității minimale depășită, lotul se exclude, cu excepția dreptului `Inv.Lot.SellAfterBestBefore`.
4. Se ordonează după `ExpiryDate` (FEFO), apoi după data primei intrări a lotului, apoi după `LotId`. Cu `ExpiryPickRule` = 2, ordinea e după data primei intrări.
5. Se alocă Q pe loturi, în ordine, până se acoperă. Dacă nu ajunge, propunerea arată lipsa (documentul nu se poate valida, `54455`).

**Unde se folosește:**

| Situație | Ce face S4 |
| --- | --- |
| Documentul are linia fără lot (aviz, NP, BC, vânzare din Vânzări) | La validare, S4 alocă automat FEFO și scrie loturile pe mișcări. Linia documentului se împarte pe loturi |
| Utilizatorul cere propunerea înainte (ecranul `s4-03`, Vânzări, WMS) | `IStockLedger.ProposeLotsAsync` întoarce alocarea; utilizatorul o poate modifica |
| Documentul are lotul ales de om | S4 verifică starea, cantitatea și durata minimă. Dacă lotul nu e primul propus, cere `LotOverrideReason` (listă: cererea clientului, lot deja pregătit, altul) |
| Fapt POS fără lot sau cu un lot inexistent | Alocare FEFO automată; dacă stocul pe loturi nu ajunge, restul merge pe `NEIDENTIFICAT` (3.7) |
| Transfer în două etape (S2) | Lotul se păstrează: TRANZIT și gestiunea de destinație primesc același `LotId` |

### 3.6 Trasabilitatea și retragerea

**Trasabilitatea** (ecranul `s4-05`) pornește de la un lot, un cod de lot al furnizorului sau o serie și arată:

- **înapoi:** furnizorul, recepția (NIR, factura), data, cantitatea; la produsele fabricate, loturile de materii prime din `inv.LotGenealogy`;
- **înainte:** toate ieșirile (clienții, documentele, cantitățile, datele), transferurile între gestiuni, consumurile în producție și loturile rezultate;
- **acum:** stocul rămas pe gestiuni, inclusiv tranzitul.

Căutarea după `SupplierLotCode` adună loturile tuturor firmelor din grup care au același cod de la același furnizor. Interogarea e o procedură recursivă cu limită de adâncime 10 (`inv.usp_Lot_Trace`), pe indecșii `StockMovement (LotId)` și `LotGenealogy`.

**Retragerea** (documentul `RL`, `inv.Recall`):

1. Responsabilul de calitate creează retragerea pentru unul sau mai multe loturi (sau un cod de lot al furnizorului), cu motivul și sursa (furnizorul, autoritatea, reclamație proprie).
2. La validare, loturile trec în starea Retras în toate gestiunile firmei, inclusiv în tranzit (decizia 7). Documentele în ciornă care folosesc aceste loturi primesc un avertisment, iar validarea lor e refuzată (`54456`).
3. ERP propune extinderea la celelalte firme ale grupului care au același `SupplierLotCode`; fiecare firmă își validează propria retragere.
4. Retragerea salvează un instantaneu: clienții care au primit lotul, documentele, cantitățile, datele, contactele (`inv.RecallCustomer`). Lista se exportă pentru notificarea clienților și a autorităților.
5. Stocul retras se returnează la furnizor sau se casează (S2, descărcare cu motivul `RECALL`), iar retururile de la clienți se înregistrează în Vânzări pe același lot.
6. Retragerea se închide când stocul lotului e 0 în toate gestiunile și toți clienții au un rezultat notat (returnat, distrus la client, nu mai are). Închiderea nu schimbă starea lotului.

### 3.7 Lotul `NEIDENTIFICAT` și schimbarea lotului (documentul `SL`)

`NEIDENTIFICAT` e un lot tehnic pe variantă și firmă, creat la nevoie, cu `IsTechnical` = 1. E singurul lot care poate avea stoc negativ, și doar din faptele POS. Se corectează cu documentul **Schimbare lot** (`SL`, `inv.LotChange`):

- pe o gestiune, mută cantitatea de pe un lot pe altul al aceleiași variante: două mișcări `LOT_CHANGE_OUT` / `LOT_CHANGE_IN`, la cost (la identificare directă, costul vine din stratul lotului sursă);
- se folosește și pentru corectarea unei recepții cu lotul greșit, sau pentru reetichetare;
- nu schimbă cantitatea totală a variantei în gestiune și nu are notă contabilă (aceeași gestiune, același cont).

S4 adaugă în nomenclatorul de tipuri de mișcare din S2 tipurile `LOT_CHANGE_OUT` și `LOT_CHANGE_IN` (fără componentă contabilă), iar în motivele de mișcare motivul `RECALL` (retragere de lot, pentru descărcări și retururi).

Pasul de închidere a lunii `UNIDENTIFIED_LOT` (C3) arată variantele cu sold diferit de 0 pe `NEIDENTIFICAT`. Implicit e blocant cu override (`UnidentifiedLotCloseCheck` = 1, decizia 8).

### 3.8 Coduri de bare GS1

`IGs1BarcodeParser` (în `Erp.Inventory`) citește codurile GS1-128 și GS1 DataMatrix cu identificatorii de aplicație (AI) de mai jos, folosiți de recepție, WMS și POS:

| AI | Ce conține | Unde ajunge |
| --- | --- | --- |
| 01 | GTIN | Varianta, prin codul de bare din Nucleu |
| 10 | Lotul | `NewLotCode` sau `LotId` |
| 11 | Data fabricației (AAMMZZ) | `ProductionDate` |
| 17 | Data expirării (AAMMZZ; ZZ = 00 înseamnă ultima zi a lunii) | `ExpiryDate` |
| 21 | Seria | `SerialNo` |
| 37 | Cantitatea (bucăți în ambalaj) | Cantitatea liniei |

Separatorul pentru câmpurile de lungime variabilă e caracterul `GS` (ASCII 29). Codurile care nu se pot citi sunt păstrate în textul liniei și semnalate, fără să blocheze introducerea manuală.

## 4. Fluxuri principale

### 4.1 Recepția unui lot nou în carantină

1. Achizițiile validează NIR 4486 (25.11.2026) pentru 1.920 buc APA05, lot `L2611-15`, termen 22.02.2027, în DEP-CJ.
2. `PostAsync` trimite `NewLotCode` și `ExpiryDate`. S4 creează CKU-ul `APA05·L2611-15·270222`, cu `CkuUid` nou; APA05 are `ReceiptQuarantine` = 1, deci lotul intră în carantină. Stocul crește, disponibilul nu.
3. Responsabilul de calitate atașează buletinul de analiză și eliberează lotul (`s4-01`). Lotul devine Disponibil și intră în propunerile FEFO.

### 4.2 Avizul către un client cu durată minimă

1. Vânzările pregătesc avizul pentru Mega Retail SRL, 3.000 buc APA05 din DEP-CJ, livrare 26.11.2026. Clientul cere minim 45 de zile până la expirare.
2. `ProposeLotsAsync` exclude `L2609-11` (9 zile rămase) și `L2611-15` (carantină). Propune `L2610-18` 2.400 buc (45 de zile) și `L2611-07` 600 buc.
3. La validare, S4 verifică din nou (stocul se poate schimba între propunere și validare) și scrie loturile pe mișcări.

### 4.3 Retragerea unui lot

1. Furnizorul anunță o problemă la lotul de furnizor `IC-2610-18` (lotul nostru `L2610-18`).
2. Se creează `RL 000003`. La validare, lotul `L2610-18` trece în Retras în DEP-CJ, DC-PL și tranzit; două avize în ciornă primesc avertisment.
3. Instantaneul arată 14 clienți, cu cantitățile și documentele. Exportul merge la responsabilul de calitate.
4. Stocul rămas se returnează la furnizor; retururile de la clienți intră pe același lot.

### 4.4 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Recepție cu un cod de lot existent și alt termen | Cere confirmare (`54452`); după confirmare se creează un CKU nou, cu același cod de lot |
| A doua recepție pe același SKU, lot și termen | Intră pe același CKU (cantitățile se adună), cu un strat de cost nou (S3) |
| Recepție cu termenul deja depășit | Avertisment; doar cu `Inv.Lot.ReceiveExpired`, lotul intră direct în starea Expirat (pentru retur sau casare) |
| Lotul expiră între propunere și validare | Validarea reface verificarea și refuză lotul (`54457`); utilizatorul cere o propunere nouă |
| Stornarea unei recepții după ce lotul a fost vândut | Refuzată (S2, `54106` pentru cantitate insuficientă pe lot) |
| Schimbarea termenului unui lot existent | Doar cu `Inv.Lot.Edit`, cu motiv și istoric; dacă lotul era Expirat și noul termen e în viitor, starea nu revine automat (se deblochează manual) |
| Variantă trecută pe urmărire după ce are stoc | Refuzată (S1, `54010`) |
| Seria apare de două ori în același document | Refuzată (`54463`) |
| Seria vândută revine fără document de retur | Nu se poate; recepția unei serii cu starea Vândută e refuzată (`54462`) |
| Inventar (S6) cu lot găsit care nu există în sistem | Plusul creează lotul, cu termenul introdus de comisie |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `MiscareStocValidata` v1 | (S2) S4 adaugă pe linii, aditiv: `CkuUid`, `CkuCode`, `LotCode`, `ExpiryDate`, `SerialNo`; linia poartă deja `ItemVariantId`, iar evenimentul de variantă din Nucleu poartă `Uid` | — | — |
| `LotStareSchimbata` v1 | Orice schimbare de stare a unui lot | `LotId`, `ItemVariantId`, `LotCode`, `FromStatus`, `ToStatus`, `Reason`, `SourceDocument`, `SupplierPartnerId` (furnizorul din recepția care a creat lotul; adăugat aditiv (precizare din Achiziții A7, 27.11.2026)) | Vânzări (blochează ciornele), WMS, POS, Notificări, Achiziții A7 (calitatea furnizorului) |
| `LoturiAproapeDeExpirare` v1 | Jobul zilnic, câte unul pe gestiune | `WarehouseId`, lista `{LotId, ItemVariantId, ExpiryDate, QtyOnHand}` | Notificări (gestionarul), Vânzări (promoții) |
| `RetragereValidata` v1 | Validarea documentului `RL` | `RecallId`, loturile, `SupplierLotCode`, `SupplierPartnerId` (adăugat aditiv (precizare din Achiziții A7, 27.11.2026)), motivul, numărul de clienți | Vânzări, POS, Raportare, Achiziții A7 (calitatea furnizorului) |
| `SerieVanduta` v1 | Ieșirea unei serii către un client | `SerialId`, `SerialNo`, `ItemVariantId`, `PartnerId`, `SoldAt`, `WarrantyUntil` | Service (02.11) |

### 5.2 Consumate

| Eveniment | De la | Ce face S4 |
| --- | --- | --- |
| `PredareProduseValidata` v1 | Producție (02.08) | Scrie genealogia (`inv.LotGenealogy`): loturile consumate → lotul predat |
| `BonuriPOSSincronizate` v1 | POS | (prin S2) alocă FEFO loturile faptelor fără lot (3.5) |

## 6. Contracte publice (partea S4)

Extensia portului de stoc din Nucleu (`Erp.Core.Contracts`), folosită de Vânzări, Achiziții, Producție, WMS:

```csharp
public interface IStockLedger   // continuare din S2, S3
{
    Task<IReadOnlyList<LotAllocationDto>> ProposeLotsAsync(LotProposalRequest request, CancellationToken ct);
    Task<LotInfoDto?> FindLotAsync(int companyId, int itemVariantId, string lotCode, DateOnly? expiryDate, CancellationToken ct);
    Task<LotInfoDto?> FindCkuAsync(int companyId, string ckuCodeOrUid, CancellationToken ct);   // scanarea etichetei interne
    Task<SerialInfoDto?> FindSerialAsync(int itemVariantId, string serialNo, CancellationToken ct);
    Task<IReadOnlyList<SerialInfoDto>> FindSerialByIdentifierAsync(string value, string? identifierTypeCode, CancellationToken ct);   // serie, IMEI, MAC, VIN...
}

public sealed record LotProposalRequest(
    int CompanyId, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase,
    int? CustomerPartnerId, DateOnly DeliveryDate, long? ExcludeReservationsOfSourceLineId);

public sealed record LotAllocationDto(int LotId, string CkuCode, string LotCode, DateOnly? ExpiryDate, decimal QtyBase, int DaysLeft, bool IsFirstChoice);
public sealed record LotInfoDto(int LotId, Guid CkuUid, string CkuCode, string LotCode, string? SupplierLotCode, DateOnly? ExpiryDate, LotStatus Status);
public sealed record SerialInfoDto(int SerialId, string SerialNo, int CompanyId, SerialStatus Status, int? WarehouseId, DateOnly? WarrantyUntil);

public enum LotStatus : byte { Available = 1, Quarantine = 2, Blocked = 3, Expired = 4, Recalled = 5 }
public enum SerialStatus : byte { InStock = 1, InTransit = 2, Sold = 3, ReturnedByCustomer = 4, Scrapped = 5, Blocked = 6 }
public enum ExpiryKind : byte { UseBy = 1, BestBefore = 2 }
public sealed record SerialIdentifierValue(string TypeCode, string Value);
// TrackingMode (S1) primește valoarea 4: SerialAndLot
```

`StockMovementRequest` (S2) primește, aditiv, câmpurile `IReadOnlyList<SerialIdentifierValue>? SerialIdentifiers` (tipul și valoarea), `Guid? CkuUid` (alternativă la `LotId`, pentru dispozitivele care au doar eticheta), `string? NewSerialNo`, `string? SupplierLotCode`, `DateOnly? ProductionDate`, `int? CountryOfOriginId`, `byte? LotOverrideReason`, `bool ConfirmNewExpiry`. Apelanții vechi rămân valabili (valori implicite `null`).

Contracte interne Stocuri (`Erp.Inventory.Contracts`):

```csharp
public interface ILotTraceQuery
{
    Task<LotTraceDto> TraceAsync(int lotId, TraceDirection direction, int maxDepth, CancellationToken ct);
    Task<IReadOnlyList<LotInfoDto>> FindBySupplierLotAsync(int supplierPartnerId, string supplierLotCode, CancellationToken ct);  // toate firmele grupului
}

public interface IGs1BarcodeParser
{
    Gs1ParseResult Parse(string raw);   // GTIN, lot, fabricație, expirare, serie, cantitate; erorile ca listă, fără excepții
}

public sealed class UnidentifiedLotCheck : IPeriodCloseCheck   // UNIDENTIFIED_LOT, blocant cu override (setabil)
```

Comenzi interne S4: `UpdateLot`, `ChangeLotStatus` (carantină, eliberare, blocare, deblocare), `SaveLotChange`, `ValidateLotChange`, `SaveRecall`, `ValidateRecall`, `CloseRecall`, `SavePartnerShelfLifeRule`.

## 7. Schema SQL `inv` (partea S4)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.Lot` (CKU) | `Id int` | UX `(CompanyId, ItemVariantId, LotCode, ExpiryKey)` (`ExpiryKey` = `ISNULL(ExpiryDate, '19000101')`); UX `CkuUid`; UX `(CompanyId, ItemVariantId) WHERE IsTechnical = 1`; FK `CompanyId`, `ItemVariantId`, `SupplierPartnerId`, `CountryOfOriginId`, `FirstReceiptMovementId` | `(ItemVariantId, Status, ExpiryDate) INCLUDE (LotCode)`; `(SupplierPartnerId, SupplierLotCode)`; `LotCodeSearch` |
| `inv.LotStatusHistory` | `Id bigint` | FK `LotId` | `(LotId, ChangedAt)` |
| `inv.SerialIdentifierType` | `Id int` | UX `CodeKey` | — |
| `inv.ItemSerialIdentifierRule` | `Id int` | UX `(ItemFamilyId, ItemId, IdentifierTypeId)` cu `NULL` tratat prin coloane calculate; `CHECK (ItemFamilyId IS NULL OR ItemId IS NULL)` | `ItemId`; `ItemFamilyId` |
| `inv.SerialIdentifier`, `inv.SerialIdentifierHistory` | `Id bigint` | FK `SerialId`, `IdentifierTypeId`; UX `(IdentifierTypeId, Value) WHERE IsUnique = 1` | `ValueSearch INCLUDE (SerialId)`; `SerialId` |
| `inv.Serial` | `Id int` | UX `(ItemVariantId, SerialNo)`; FK `CompanyId`, `ItemVariantId`, `LotId`, `WarehouseId`, `DepotId`, `SoldToPartnerId` | `(CompanyId, WarehouseId, Status) INCLUDE (ItemVariantId)`; `SerialNoSearch` |
| `inv.LotGenealogy` | `Id bigint` | UX `(ParentLotId, ChildLotId, SourceMovementId)`; FK pe cele trei | `ChildLotId` |
| `inv.LotChange`, `inv.LotChangeLine` | `Id int`, `Id bigint` | UX `(CompanyId, Number)`; FK `WarehouseId`, `FromLotId`, `ToLotId` | `(WarehouseId, DocumentDate)` |
| `inv.Recall`, `inv.RecallLot`, `inv.RecallCustomer` | `Id int`, `Id int`, `Id bigint` | UX `(CompanyId, Number)`; FK `LotId`, `PartnerId` | `RecallLot (LotId)` |
| `inv.PartnerShelfLifeRule` | `Id int` | UX `(CompanyId, PartnerId, ItemId, ItemGroupId)` cu `NULL` tratat prin coloane calculate; `CHECK (ItemId IS NULL OR ItemGroupId IS NULL)` | `PartnerId` |
| `inv.ItemStockSettings` (coloane noi) | — | `ExpiryKind`, `ShelfLifeDays`, `MinRemainingShelfLifeDays`, `ReceiptQuarantine`, `LotNumbering`, `SerialCaptureMode`, `WarrantyMonths` (3.1) | — |
| `inv.StockSettings` (coloană nouă) | — | `UnidentifiedLotCloseCheck tinyint NOT NULL DEFAULT 1` | — |
| `inv.StockMovement`, `inv.CostLayer`, `inv.Stock` | — | Migrarea S4 adaugă FK-urile `LotId` → `inv.Lot`, `SerialId` → `inv.Serial` (coloanele există din S2, S3) | `StockMovement (LotId) WHERE LotId IS NOT NULL`; `StockMovement (SerialId) WHERE SerialId IS NOT NULL` |

Exemplu de tabel complet:

```sql
CREATE TABLE inv.Lot (
    Id                     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Lot PRIMARY KEY,
    CompanyId              int            NOT NULL CONSTRAINT FK_Lot_Company REFERENCES core.Company(Id),
    ItemVariantId          int            NOT NULL CONSTRAINT FK_Lot_ItemVariant REFERENCES core.ItemVariant(Id),
    LotCode                nvarchar(40)   NOT NULL,     -- comparat exact (collation-ul bazei, case sensitive)
    LotCodeSearch          AS CAST(LotCode AS nvarchar(40)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 PERSISTED,
    CkuCode                nvarchar(120)  NOT NULL,     -- {SKU}·{lot}·{AAMMZZ}, generat la creare
    CkuCodeSearch          AS CAST(CkuCode AS nvarchar(120)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 PERSISTED,
    CkuUid                 uniqueidentifier NOT NULL CONSTRAINT DF_Lot_CkuUid DEFAULT (NEWID()),
    SupplierLotCode        nvarchar(40)   NULL,
    SupplierPartnerId      int            NULL CONSTRAINT FK_Lot_Supplier REFERENCES core.Partner(Id),
    ProductionDate         date           NULL,
    ExpiryDate             date           NULL,
    ExpiryKey              AS ISNULL(ExpiryDate, CONVERT(date, '19000101', 112)) PERSISTED,
    ExpiryKind             tinyint        NULL,         -- copiat din ItemStockSettings la creare: 1 data limită de consum, 2 durabilitate minimală
    CountryOfOriginId      int            NULL CONSTRAINT FK_Lot_Country REFERENCES core.Country(Id),
    Status                 tinyint        NOT NULL CONSTRAINT DF_Lot_Status DEFAULT (1),
    IsTechnical            bit            NOT NULL CONSTRAINT DF_Lot_IsTechnical DEFAULT (0),   -- 1 = NEIDENTIFICAT
    FirstReceiptMovementId bigint         NULL CONSTRAINT FK_Lot_FirstReceipt REFERENCES inv.StockMovement(Id),
    FirstReceiptDate       date           NULL,
    StatusReason           nvarchar(400)  NULL,
    CreatedAt              datetime2(3)   NOT NULL CONSTRAINT DF_Lot_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int            NOT NULL,
    ModifiedAt             datetime2(3)   NULL,
    ModifiedBy             int            NULL,
    RowVersion             rowversion     NOT NULL,
    CONSTRAINT UX_Lot_Cku UNIQUE (CompanyId, ItemVariantId, LotCode, ExpiryKey),
    CONSTRAINT UX_Lot_CkuUid UNIQUE (CkuUid),
    CONSTRAINT CK_Lot_Status CHECK (Status IN (1, 2, 3, 4, 5)),
    CONSTRAINT CK_Lot_ExpiryKind CHECK (ExpiryKind IS NULL OR ExpiryKind IN (1, 2)),
    CONSTRAINT CK_Lot_Dates CHECK (ProductionDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate > ProductionDate),
    CONSTRAINT CK_Lot_Technical CHECK (IsTechnical = 0 OR (Status = 1 AND ExpiryDate IS NULL))
);
CREATE UNIQUE INDEX UX_Lot_Technical ON inv.Lot (CompanyId, ItemVariantId) WHERE IsTechnical = 1;
CREATE INDEX IX_Lot_Pick ON inv.Lot (ItemVariantId, Status, ExpiryDate) INCLUDE (LotCode, FirstReceiptDate);
CREATE INDEX IX_Lot_SupplierLot ON inv.Lot (SupplierPartnerId, SupplierLotCode) WHERE SupplierLotCode IS NOT NULL;
CREATE INDEX IX_Lot_Search ON inv.Lot (LotCodeSearch) INCLUDE (ItemVariantId, CompanyId);
CREATE INDEX IX_Lot_CkuSearch ON inv.Lot (CkuCodeSearch) INCLUDE (ItemVariantId, CompanyId);
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_Lot_ResolveForMovements` | Din `usp_StockMovement_Post`: creează loturile noi, verifică starea, alocă FEFO liniile fără lot, împarte liniile pe loturi | Nu |
| `inv.usp_Lot_Propose` | Propunerea FEFO pentru o variantă, gestiune, client, dată | Da (citire) |
| `inv.usp_Serial_ResolveForMovements` | Creează și verifică seriile, actualizează starea și gestiunea | Nu |
| `inv.usp_Lot_ChangeStatus` | Carantină, eliberare, blocare, deblocare, cu istoric | Nu |
| `inv.usp_Lot_ExpiryScan` | Jobul zilnic (3.2) | Nu |
| `inv.usp_LotChange_Validate` | Documentul `SL` | Nu |
| `inv.usp_Recall_Validate`, `_Close` | Retragerea: blocarea, instantaneul clienților, închiderea | Nu |
| `inv.usp_Lot_Trace` | Trasabilitatea înapoi și înainte, recursiv, cu limită de adâncime | Da (citire) |

Coduri de eroare (plaja S4 `54450–54549`, prefix API `INV_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 54451 | `INV_LOT_EXPIRY_REQUIRED` | 422 | Intrare pe un model cu lot și termen, fără termen |
| 54452 | `INV_LOT_EXPIRY_MISMATCH` | 409 | Cod de lot existent cu alt termen, fără confirmarea `ConfirmNewExpiry` (creează un CKU nou) |
| 54453 | `INV_LOT_REQUIRED` | 422 | Intrare pe un model urmărit pe lot, fără lot |
| 54454 | `INV_LOT_NOT_FOUND` | 404 | `LotId` inexistent sau al altei variante sau firme |
| 54455 | `INV_LOT_INSUFFICIENT_QTY` | 409 | Cantitate insuficientă pe lot sau pe loturile disponibile |
| 54456 | `INV_LOT_STATUS_FORBIDS_ISSUE` | 409 | Ieșire nepermisă pentru starea lotului (3.3) |
| 54457 | `INV_LOT_EXPIRED` | 409 | Ieșire către client dintr-un lot expirat |
| 54458 | `INV_LOT_SHELF_LIFE_TOO_SHORT` | 409 | Lot sub durata minimă rămasă cerută de client |
| 54459 | `INV_LOT_OVERRIDE_REASON_REQUIRED` | 422 | Lot ales în afara propunerii FEFO, fără motiv |
| 54460 | `INV_SERIAL_REQUIRED` | 422 | Lipsesc seriile la o mișcare care le cere |
| 54461 | `INV_SERIAL_NOT_IN_STOCK` | 409 | Seria nu e în stoc în gestiunea sursă |
| 54462 | `INV_SERIAL_STATUS_INVALID` | 409 | Serie vândută, casată sau a altei firme, la recepție sau retur |
| 54463 | `INV_SERIAL_DUPLICATE` | 409 | Aceeași serie de două ori în document sau deja existentă la recepție |
| 54464 | `INV_LOT_STATUS_TRANSITION_INVALID` | 409 | Schimbare de stare nepermisă (de exemplu din Expirat în Disponibil) |
| 54465 | `INV_LOT_CHANGE_VARIANT_MISMATCH` | 422 | Schimbare de lot între variante diferite |
| 54466 | `INV_RECALL_OPEN_STOCK` | 409 | Închiderea unei retrageri cu stoc rămas |
| 54467 | `INV_UNIDENTIFIED_LOT_OPEN` | 409 | Închiderea lunii cu sold pe `NEIDENTIFICAT`, fără override |
| 54468 | `INV_GS1_PARSE_FAILED` | 422 | Cod GS1 ilizibil, când e cerut explicit (API) |
| 54469 | `INV_TRACKING_SETTING_LOCKED` | 409 | Schimbarea setărilor de lot sau serie (3.1) pentru un model cu stoc |
| 54470 | `INV_SERIAL_UOM_INVALID` | 422 | Model pe serie cu UM de bază cu factor diferit de 1 |
| 54471 | `INV_SERIAL_IDENTIFIER_INVALID` | 422 | Identificator care nu respectă masca sau cifra de control (IMEI, VIN) |
| 54472 | `INV_SERIAL_IDENTIFIER_DUPLICATE` | 409 | Identificator marcat unic, deja folosit pe altă bucată |
| 54473 | `INV_SERIAL_IDENTIFIER_REQUIRED` | 422 | Lipsește un identificator obligatoriu la intrare sau la ieșire |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterile:

- Abatere de la `SP-08` pentru `inv.usp_Lot_Trace`: recursivitatea e făcută cu un CTE recursiv, cu `OPTION (MAXRECURSION 10)` și tabel temporar `COLLATE DATABASE_DEFAULT` (`SQL-23`). Permanentă.
- Abatere de la `SQL-11` pentru `inv.LotStatusHistory`, `inv.LotGenealogy`, `inv.RecallCustomer`: fără `RowVersion`, sunt doar de adăugare. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Lot.View` | Loturile, seriile, trasabilitatea |
| `Inv.Serial.EditIdentifiers` | Corectarea identificatorilor unei bucăți (IMEI, MAC, VIN), cu motiv |
| `Inv.Settings.SerialIdentifiers` | Tipurile de identificatori și regulile pe familie sau model |
| `Inv.Lot.Edit` | Atributele lotului (lot furnizor, țară, termen, cu motiv) |
| `Inv.Lot.Release` | Eliberarea din carantină și trecerea în carantină |
| `Inv.Lot.Block` | Blocarea și deblocarea |
| `Inv.Lot.OverrideShelfLife` | Livrarea unui lot sub durata minimă a clientului, cu motiv |
| `Inv.Lot.SellAfterBestBefore` | Vânzarea sau donarea după data durabilității minimale |
| `Inv.Lot.ReceiveExpired` | Recepția unui lot deja expirat (pentru retur sau casare) |
| `Inv.LotChange.Edit`, `.Validate` | Documentul `SL` |
| `Inv.Recall.Edit`, `.Validate`, `.Close` | Retragerile |

Roluri livrate: **Responsabil calitate** (rol nou: `Lot.View`, `Lot.Edit`, `Lot.Release`, `Lot.Block`, `Recall.*`), **Contabil stocuri** (`Lot.View`, `LotChange.Validate`), **Șef depozit** (`Lot.View`, `Lot.Block`, `LotChange.Edit`, `OverrideShelfLife`), **Gestionar** (`Lot.View`, `LotChange.Edit`), **Agent vânzări** (`Lot.View`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S4 |
| --- | --- |
| Regulamentul (CE) 178/2002, art. 18 (trasabilitatea alimentelor) | Un pas înapoi (furnizorul, lotul lui) și un pas înainte (clientul business, documentul, cantitatea), disponibile la cerere; trasabilitatea din 3.6 merge mai departe, pe tot lanțul intern |
| Regulamentul (UE) 1169/2011, art. 24 | Distincția între data limită de consum (produsul expirat nu se mai vinde) și data durabilității minimale (`ExpiryKind`) |
| Legea 217/2016 privind diminuarea risipei alimentare | Produsele aproape de data durabilității minimale pot fi donate; motivul `DONATION` din S2 și raportul de expirări (`s4-02`) sprijină asta |
| Regulamentul (UE) 2017/745 (dispozitive medicale, UDI) | Pentru firmele care distribuie dispozitive medicale: seria sau lotul și GS1 DataMatrix (3.8) |
| OUG 140/2021 (garanția de conformitate) | Data vânzării pe serie și termenul de garanție, pentru Service |
| Legea 82/1991 | Documentele justificative pentru schimbarea lotului (`SL`) și pentru retragere (`RL`) |

Integrări: niciuna externă directă (coduri GS1 citite local). Interne: S1 (setări), S2 (mișcări), S3 (straturi pe lot și serie), S5 (disponibilul fără loturile nedisponibile), S6 (inventarul pe loturi și serii), C3 (pasul `UNIDENTIFIED_LOT`), Vânzări (propunerea, durata minimă pe client), Achiziții (recepția loturilor), Producție (genealogia), Service (seriile vândute), WMS (picking pe lot), POS (alocarea FEFO).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Stoc pe loturi | Pe gestiune și variantă: loturile, termenele, zilele rămase, starea, cantitatea, rezervatul | Gestionar, Șef depozit, Vânzări |
| Expirări | Loturile care expiră într-un interval sau au expirat, cu valoarea la cost și acțiunea propusă | Șef depozit, Responsabil calitate, Director comercial |
| Trasabilitatea unui lot | Înapoi, înainte și stocul curent, cu documentele | Responsabil calitate, Auditor |
| Istoricul unei serii | Toate mișcările, starea, clientul, garanția | Service, Vânzări |
| Retrageri | Retragerile deschise și închise, clienții notificați, stocul rămas | Responsabil calitate, Director general |
| Lotul `NEIDENTIFICAT` | Soldurile pe variante și gestiuni, cu faptele POS care le-au produs | Contabil stocuri |

## 11. Ecrane principale

Machetele (HTML + PNG) sunt în [ecrane/s4-loturi-serii/](ecrane/s4-loturi-serii/), în meniul Stocuri › Stoc și Stocuri › Rapoarte.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Loturi | Loturile unei variante pe gestiune, cu termen, zile rămase, stare, cantitate; fișa lotului în carantină cu buletinul, istoricul și eliberarea | `s4-01-loturi` |
| Expirări | Loturile care expiră în 30 de zile și cele expirate, pe gestiuni, cu acțiunea propusă (transfer, promoție, donare, retur, casare) | `s4-02-expirari` |
| Alegerea loturilor | Linia unui aviz: propunerea FEFO cu durata minimă a clientului, loturile excluse și de ce | `s4-03-alegere-loturi` |
| Serii | Seriile unei variante: starea, gestiunea sau clientul, garanția; istoricul unei serii | `s4-04-serii` |
| Trasabilitate și retragere | Lotul `L2610-18`: înapoi, înainte, stocul rămas; documentul de retragere `RL 000003` | `s4-05-trasabilitate-retragere` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S4-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S4-A1 | Pe orice gestiune și variantă: suma stocului pe loturi = stocul variantei; niciun lot (în afară de `NEIDENTIFICAT`) nu are cantitate negativă la nicio dată | Test de proprietate |
| S4-A2 | FEFO: loturi cu termenele 05.12.2026 (480), 10.01.2027 (2.400), 14.01.2027 (960), cerere 3.000 pentru un client cu 45 de zile la 26.11.2026 → 2.400 din `L2610-18` și 600 din `L2611-07` | Test unitar |
| S4-A3 | Un lot în carantină, blocat, expirat sau retras nu apare în propunere și e refuzat la vânzare (`54456`, `54457`) | Test de integrare |
| S4-A4 | Jobul zilnic trece în Expirat doar loturile cu data limită de consum depășită; cele cu durabilitate minimală rămân disponibile | Test de integrare |
| S4-A5 | Două recepții `IAU250-25`, lot `I123`, termen 12.12.2026, în zile diferite, dau un singur CKU `IAU250-25·I123·261212` cu cantitățile adunate și două straturi de cost; lotul `I1234` cu termenul 16.12.2026 dă un CKU nou pe același SKU; `IAU250-10` cu lotul `I123` dă alt CKU; `L2611-07` și `l2611-07` sunt loturi diferite (baza e case sensitive) | Test de integrare (`TST-09`) |
| S4-A6 | Un transfer în două etape păstrează lotul în TRANZIT și la destinație | Test de integrare (cu S2) |
| S4-A7 | O serie nu poate fi în stoc în două gestiuni și nu poate fi vândută de două ori (`54461`, `54462`) | Test de proprietate |
| S4-A8 | Cu `SerialCaptureMode` = 2, recepția fără serii trece, iar livrarea creează seriile cu starea Vândută | Test de integrare |
| S4-A9 | Validarea unei retrageri blochează lotul în toate gestiunile firmei, inclusiv în tranzit, și salvează clienții care l-au primit | Test de integrare |
| S4-A10 | Trasabilitatea unui lot cu 50.000 de mișcări se întoarce în sub 2 secunde | Test de performanță |
| S4-A11 | Un bon POS cu un lot inexistent se alocă FEFO; partea care nu ajunge merge pe `NEIDENTIFICAT`, iar documentul `SL` o corectează fără notă contabilă | Test de integrare (cu S2) |
| S4-A12 | Parserul GS1 citește `(01)05941234000016(17)270222(10)L2611-15` și `(21)` cu separatorul `GS` | Test unitar |
| S4-A13 | La identificare directă, ieșirea unui lot consumă doar straturile lui (S3) | Test de integrare (cu S3) |
| S4-A14 | Un telefon cu regula „IMEI obligatoriu și unic” e refuzat la recepție fără IMEI (`54473`), cu un IMEI cu cifra de control greșită (`54471`) și cu un IMEI deja folosit (`54472`); căutarea după IMEI găsește seria. Recepția a 10.000 de piulițe (`TrackingMode` = 0) nu cere lot sau serie | Test de integrare |

Criteriile S4-A1, S4-A3 și S4-A9 sunt critice: ele împiedică vânzarea unui lot care nu trebuie vândut și păstrează trasabilitatea completă.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Cum se identifică un lot | CKU = SKU + lot + termen, cu cod vizibil (`CkuCode`) și id unic (`CkuUid`); SKU-ul are și el id unic (`ItemVariant.Uid`, Nucleu). Aceeași combinație se adună pe același CKU, una nouă creează alt CKU; straturile de cost rămân pe recepție | 2 (regulile 1–3), 3.2, 7 | Decis (26.11.2026) |
| 2 | Alegerea lotului la ieșire | ERP propune FEFO și alocă automat când documentul nu are lot; omul poate alege alt lot, cu motiv jurnalizat | 2 (regula 7), 3.5 | Decis (26.11.2026) |
| 3 | Două tipuri de termen | „Data limită de consum”: lotul se blochează automat la expirare. „A se consuma de preferință înainte de”: doar avertisment, vânzarea sau donarea după dată cere un drept | 3.1, 3.2, 3.3 | Decis (26.11.2026) |
| 4 | Durata minimă rămasă la livrare | Pe articol, suprascrisă pe client sau pe grupa de articole a clientului; lotul sub limită nu e propus și e refuzat, cu excepția unui drept special | 3.2, 3.5 | Decis (26.11.2026) |
| 5 | Carantina | E o stare a lotului, nu o gestiune separată; recepția intră automat în carantină doar pentru modelele care cer asta; eliberarea o face responsabilul de calitate | 3.3 | Decis (26.11.2026) |
| 6 | Urmărirea individuală | Modul de urmărire e pe model: fără (doar cantitate: piulițe, cărămizi), lot, lot și termen, serie, serie și lot. La serie, o serie = o bucată, unică pe variantă în toată baza; bucata are și identificatori suplimentari configurabili pe familie sau model (IMEI, MAC, VIN, serie motor, cod de garanție), cu validare, obligativitate și unicitate; seria se citește la intrare și la ieșire, sau doar la ieșire, după setarea modelului | 3.4 | Decis (26.11.2026) |
| 7 | Retragerea unui lot | Blochează imediat lotul în toate gestiunile firmei, inclusiv în tranzit, salvează lista clienților și propune retragerea în celelalte firme ale grupului cu același lot de furnizor | 3.6 | Decis (26.11.2026) |
| 8 | Lotul `NEIDENTIFICAT` la închiderea lunii | Soldul pe `NEIDENTIFICAT` blochează închiderea lunii, cu override; se corectează cu documentul de schimbare a lotului (`SL`); setabil pe firmă | 3.7 | Decis (26.11.2026) |

## Note de implementare (28.11.2026)

| Pas | Ce s-a implementat | Decizii și abateri |
| --- | --- | --- |
| S4.1 | Migrarea `2026.11.086` (`inv.Lot` cu `CkuCode`, `CkuUid`, `inv.LotStatusHistory`, setările de lot pe model `ExpiryKind`, `ShelfLifeDays`, `MinRemainingShelfLifeDays`, `ReceiptQuarantine`, `LotNumbering`, `SerialCaptureMode`, `WarrantyMonths`, `StockSettings.UnidentifiedLotCloseCheck`, FK-urile `LotId` pe mișcări, sold și straturi, `inv.tt_LotRequest`); procedura `inv.usp_Lot_ResolveForMovements` (lotul dat prin Id sau `CkuUid`, găsit după cod și termen sau creat, carantina la recepție, termenul din durata de valabilitate, codul generat, 54451–54454, 54457, lotul tehnic `NEIDENTIFICAT` pentru faptele POS, disponibilul pe lot la data mișcării și la datele ulterioare); alocatorul pur `LotAllocator` (`Erp.Inventory.Domain.Lots`: FEFO, regula 2 după prima intrare, starea care permite ieșirea, termenul la vânzare, durata minimă a clientului, cererile din același apel pe același disponibil); portul de stoc împarte liniile fără lot pe loturi, iar perechea unui transfer primește aceleași loturi; `StockMovementRequest` primește aditiv `CkuUid`, `SupplierLotCode`, `ProductionDate`, `CountryOfOriginCode`, `ConfirmNewExpiry`, `LotOverrideReason`; `MiscareStocValidata` poartă `LotId`, `LotCode`, `CkuCode`, `CkuUid`, `ExpiryDate`; permisiunile `Inv.Lot.*`; testele S4-A1, A2 (unitar), A3 (unitar și în bază), A5, A6, A11 (fără `SL`), A13 | **Cheia de idempotență** a mișcărilor include lotul și seria (`UX_StockMovement_Source` pe `LotKey`, `SerialKey`), pentru că o linie se împarte pe loturi; reluarea întoarce aceleași loturi. **Alocarea** e în C# (`LotAllocator`), nu în SQL, ca să fie testabilă unitar (ca decizia 9 din S3); SQL creează loturile și dă disponibilul. **Țara de origine** e codul ISO (`core.Country` are cheia pe cod), nu un Id. **Codul generat** (`LotNumbering` = 2) e `L{AAMMZZ}-{n}`, nu din seria Nucleu `LOT-{variantă}`. Ieșirea pe un lot cu cantitate insuficientă dă acum `INV_LOT_INSUFFICIENT_QTY` (54455), inclusiv la variantele urmărite fără loturi. `LotOverrideReason` (54459), durata minimă pe client, stările manuale și jobul de expirare vin cu S4.2; seriile cu S4.3 |
| S4.2 | Migrarea `2026.11.087` (`inv.PartnerShelfLifeRule`, `LotOverrideReason` în tipul mișcărilor costate); funcția `inv.ufn_LotAvailability` (disponibilul pe lot, folosită la validare și la propunere); procedurile `inv.usp_Lot_ChangeStatus`, `_Update`, `_ExpiryScan`, `_Propose`, `_List`, `_Get`, `inv.usp_PartnerShelfLifeRule_Save`; `LotAllocator` cu durata minimă a clientului (regula pe model, pe grupă, apoi setarea modelului), data durabilității minimale (`Inv.Lot.SellAfterBestBefore`), livrarea sub durata minimă (`Inv.Lot.OverrideShelfLife`) și motivul alegerii altui lot (54458, 54459, scris pe mișcare); `LotService`, comenzile `ChangeLotStatus`, `UpdateLot`, `SavePartnerShelfLifeRule`, `ScanLotExpiry`, interogările `GetLots`, `GetLot`, `ProposeLots`; jobul `LotExpiryJob` (00:05 ora României); portul `IStockLedger.ProposeLotsAsync`, `FindLotAsync`, `FindCkuAsync` (Nucleu, cu `LotProposalRequest`, `LotAllocationDto`, `LotInfoDto`, `LotStatus`); evenimentele `LotStareSchimbata`, `LoturiAproapeDeExpirare`; rolul Responsabil calitate și drepturile de lot pe Șef depozit și Gestionar; API `/api/inventory/lots`, `/lots/propose`, `/lots/{id}/status`, `/partner-shelf-life-rules`; testele S4-A2 (prin port), A3 (carantina, blocarea, istoricul), A4 | **Motivul** alegerii altui lot se cere doar la livrările către client (vânzare, bon), nu la retururi, descărcări sau transferuri, unde lotul ales e de obicei unul anume (carantina, expirarea). **Stările manuale** sunt doar între Disponibil, Carantină și Blocat (eliberarea și carantina cu `Inv.Lot.Release`, blocarea cu `Inv.Lot.Block`); Expirat și Retras le pune doar sistemul. **Rezervările** (S5) nu sunt încă scăzute din propunere. Notificările gestionarului pentru loturile care expiră se fac din evenimentul `LoturiAproapeDeExpirare` (consumatorul vine cu notificările Stocurilor) |

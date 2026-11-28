# ERP · 02.05 Stocuri · S3 Evaluare și cost

26 noiembrie 2026 · Aurelian · **Validat (26.11.2026)**. A treia din cele 7 părți ale modulului 02.05 Stocuri. Pornește de la deciziile din S1 (validată) și S2 (decizii confirmate pe 26.11.2026). Metodele de evaluare sunt FIFO, CMP recalculat după fiecare intrare, LIFO și identificarea directă, plus prețul de vânzare cu amănuntul în magazine. Costul se ține pe firmă + gestiune + variantă, iar la nivelul „depozit” pe firmă + depozit + gestiune + variantă. Mișcările din alte module trec prin portul `IStockLedger`, în tranzacția documentului lor.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori S3 `54300–54449`. Această parte acoperă calculul costului fiecărei mișcări, straturile de cost (FIFO, LIFO, identificare directă), costul mediu (CMP) și costurile adăugate după recepție (transport, vamă, diferențe de preț pe factură). Mai acoperă recalcularea după mișcări cu dată în trecut, regularizarea ieșirilor pe minus, gestiunile la preț cu amănuntul (coeficientul K, descărcarea lunară, modificarea prețului), costul standard și legătura cu contabilitatea (C2, C3). Documente părinte: [S1 Gestiuni, depozite și setări](S1-GESTIUNI-SI-SETARI.md), [S2 Mișcări și documente](S2-MISCARI-SI-DOCUMENTE.md), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C2 contarea, C3 închiderea, F4 multivaluta). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Cheia de cost** = cheia stocului din S2: (gestiune, variantă) sau (depozit, gestiune, variantă). La identificarea directă se adaugă lotul sau seria (S4).
- Metoda (`ItemStockSettings.ValuationMethod`) e a modelului, pe firmă: 1 FIFO, 2 CMP, 3 LIFO, 4 identificare directă (S1 §3.4). Gestiunile cu `ValuationBasis` = 2 (preț cu amănuntul) folosesc regulile din 3.7, indiferent de metodă.
- **Stocul se evaluează doar în RON.** Recepția în valută se transformă la cursul documentului de recepție (F4). Diferențele de curs de la plata facturii merg în `665` / `765` (F4) și nu schimbă costul stocului.
- S3 rulează **în tranzacția mișcării** (S2, fluxul 4.1, pasul 4) și completează `UnitCost` și `CostAmount` pe fiecare mișcare. Costul unitar are 6 zecimale; valorile se rotunjesc la 2 zecimale prin `IPrecisionService` (P7, `NET-14`).
- Contarea e a Finanțelor (C2). S3 trimite costul în blocul `Accounting` al evenimentelor (`AccountingPayload` v1, componenta `Cost`). Ajustările de valoare, fără cantitate, au evenimentul lor (`CostStocAjustat`).
- **Ce NU face S3:** nu calculează costul de producție (Producție 02.08 trimite costul produselor predate), nu face ajustările pentru deprecierea stocurilor (`397`, note manuale în C2 la inventar sau la închiderea anului) și nu calculează TVA-ul (C2, C4).

## 1. Scop și perimetru

| Zonă | Ce face S3 | Ce NU face (și cine face) |
| --- | --- | --- |
| Costul intrărilor | Costul de recepție în RON, plus costurile adăugate și diferențele de preț de pe factură | Prețul de pe factură și cursul (Achiziții, F4) |
| Costul ieșirilor | FIFO, LIFO, CMP, identificare directă, pe cheia de cost | Prețul de vânzare (Vânzări) |
| Straturi și medii | Straturile de cost, consumul lor, costul mediu curent | — |
| Recalculare | Refacerea costului ieșirilor după o mișcare cu dată în trecut sau o ajustare; regularizarea ieșirilor pe minus | Notele de regularizare (C2, din eveniment) |
| Preț cu amănuntul | Intrarea la preț cu amănuntul, coeficientul K, descărcarea lunară, nota de modificare a prețului | Prețul din lista cu amănuntul (Vânzări) |
| Cost standard | Costul standard al produselor fără Producție (S2, nota `PF`) și diferențele de preț (`348`) | Costul real de producție (Producție) |
| Închiderea lunii | Pașii `COST_RECALC_PENDING`, `RETAIL_DISCHARGE` și concordanța stocurilor cu conturile (C3) | Închiderea propriu-zisă (C3) |

## 2. Model de domeniu

```
 StockMovement (S2) ── intrare ──► CostLayer (strat: cantitate intrată, cantitate rămasă, cost unitar)
        │                              ▲
        └──── ieșire ─────────────────┘ CostLayerConsumption (ce strat, câtă cantitate, ce cost)
                                        (FIFO: cel mai vechi; LIFO: cel mai nou; identificare directă: lotul sau seria)

 Stock (S2) + QtyOnHand, ValueOnHand ──► CMP: cost mediu = ValueOnHand / QtyOnHand, refăcut la fiecare intrare

 CostAdjustment (ajustare de valoare, fără cantitate):
   costuri adăugate · diferență de preț pe factură · regularizare negativ · recalculare · modificare preț cu amănuntul
        └──► outbox: CostStocAjustat (bloc Accounting) ──► C2

 CostRecalcQueue (chei de refăcut de la o dată) ──► inv.usp_StockCost_Recalculate ──► CostAdjustment

 Gestiune la preț cu amănuntul:
   RetailPriceChange (modificare de preț) · RetailDischarge (descărcarea lunii, cu coeficientul K)
```

Reguli de business ale modelului:

1. **Fiecare mișcare are cost la înregistrare.** Nicio mișcare nu rămâne fără `UnitCost`. Costul se poate schimba ulterior doar prin recalculare (regula 5), cu urmă în `CostAdjustment`.
2. **Valoarea stocului = suma mișcărilor plus ajustările.** Pe orice cheie și la orice dată, `ValueOnHand` e egal cu suma `CostAmount` a mișcărilor plus suma `CostAdjustment`. Testul S3-A1 verifică asta.
3. **Când cantitatea ajunge la zero, valoarea ajunge la zero.** Rotunjirile rămase din CMP sau din consumul straturilor se pun pe ultima ieșire, care golește cheia. Nu rămân „bani fără marfă” în stoc.
4. **Transferul se face la cost.** Intrarea dintr-un transfer (S2) primește exact costul ieșirii pereche, strat cu strat la FIFO, LIFO și identificare directă (straturile se mută, cu data lor originală) și la costul mediu de la sursă la CMP.
5. **Trecutul se recalculează, nu se corectează de mână.** O mișcare cu dată în trecut, un cost adăugat sau o diferență de preț pune cheia în coada de recalculare. Recalcularea reface costul ieșirilor ulterioare și scrie diferențele ca ajustări, contate de C2.
6. **Lunile închise nu se schimbă** (C3). Diferența care ar privi o lună închisă se înregistrează în prima lună deschisă, ca regularizare (decizia 2).
7. **Stocul în RON, la costul de achiziție complet** (OMFP 1802/2014): prețul de cumpărare, taxele nerecuperabile (vamă, accize), transportul, manipularea și alte costuri direct atribuibile, minus reducerile comerciale.

## 3. Calculul costului

### 3.1 Costul intrărilor

| Tip de mișcare (S2) | Costul unitar al intrării |
| --- | --- |
| `RECEIPT_PURCHASE` | Prețul net de pe linia NIR-ului (după reduceri), în RON la cursul recepției, plus costurile adăugate cunoscute la recepție (taxe vamale, accize din DVI) |
| `RETURN_FROM_CUSTOMER` | Costul ieșirii inițiale (din avizul legat, prin Vânzări); fără legătură: costul curent al cheii |
| `TRANSFER_IN` | Costul ieșirii pereche (regula 4) |
| `LOAD` | După motiv (S2 §3.5, `CostSource`): introdus, ultimul cost al cheii, cost standard |
| `PRODUCTION_IN` | Costul trimis de Producție; fără Producție, costul standard (3.8) |
| `COUNT_PLUS` | Ultimul cost al cheii sau costul stabilit de comisia de inventar (S6) |

**Ieșirea cu cost impus** (precizare din Achiziții A4, 27.11.2026): mișcarea `RETURN_TO_SUPPLIER` cu `InboundUnitCost` completat iese la acest cost (costul liniei NIR-ului returnat), ca valoarea scăzută din stoc să fie egală cu ce se cere furnizorului. La CMP ieșirea se tratează ca o intrare negativă și media se recalculează; la FIFO, LIFO și identificare directă se consumă stratul NIR-ului sursă când mai există, altfel straturile în ordinea metodei, iar diferența dintre costul impus și costul straturilor consumate devine ajustare `Recalculation` pe cheie.
| `CUSTODY_IN` | Valoarea declarată (extrabilanțier, nu intră în valoarea stocului propriu) |

La FIFO, LIFO și identificare directă, fiecare intrare creează un **strat** (`CostLayer`). La CMP, intrarea refăcă media: `ValueOnHand` nou = `ValueOnHand` + valoarea intrării; costul mediu = `ValueOnHand` nou / `QtyOnHand` nou.

### 3.2 Costul ieșirilor

| Metodă | Cum se calculează costul ieșirii |
| --- | --- |
| FIFO | Se consumă straturile în ordinea (`LayerDate`, `PostingSeq`) crescătoare, până se acoperă cantitatea; costul = suma cantităților consumate × costul fiecărui strat |
| LIFO | La fel, în ordine descrescătoare: întâi stratul cel mai nou existent **la data ieșirii** (nu la data validării) |
| CMP | Cantitatea × costul mediu al cheii la momentul ieșirii (ordinea din S2, regula 4), rotunjit la 2 zecimale |
| Identificare directă | Se consumă exact stratul lotului sau al seriei de pe mișcare (S4) |

Consumul se salvează în `CostLayerConsumption` (ieșirea, stratul, cantitatea, costul), ca stornarea să refacă exact straturile consumate și ca recalcularea să știe ce s-a consumat.

**Stornarea** (S2, 4.6) refolosește costul original: stornarea unei ieșiri pune înapoi cantitatea în straturile din care a ieșit, iar stornarea unei intrări scoate stratul ei. Dacă stratul a fost consumat între timp, stornarea intrării e refuzată cu politica „blocat” (S2) sau, cu politica permisivă, lasă cheia pe minus și o regularizează (3.4).

**Algoritmul.** Consumul straturilor e secvențial pe o cheie. Se face pe set pentru toate liniile unui document: o singură trecere, cu sumă cumulată pe straturi (`SUM(QtyRemaining) OVER (ORDER BY ...)`) și intersectarea intervalelor cumulate cu cantitățile cerute. Nu se folosesc cursoare (`SP-08`). Abaterea e permisă doar în recalcularea în masă (3.5), declarată în 7.

### 3.3 Costuri adăugate și diferențe de preț (`LandedCost`, documentul `CA`)

Costurile care se află după recepție (factura transportatorului, comisionul vamal, diferența dintre prețul din NIR și cel din factură) ajustează costul stocului.

| Câmp | Reguli |
| --- | --- |
| `SourceKind` | 1 Factură de servicii legată de recepții (transport, manipulare, vamă) · 2 Diferență de preț pe factura furnizorului (factura vine cu alt preț decât NIR-ul) · 3 Reducere comercială primită ulterior, legată de livrări (fără factură de reducere separată, de exemplu rabat pe volum) |
| `SupplierInvoiceId` | Factura din Achiziții care justifică suma |
| `Amount` | Suma fără TVA, în RON (cursul facturii, F4) |
| `AllocationBasis` | 1 După valoare (implicit, setarea `LandedCostAllocation` din S1) · 2 După cantitate · 3 După greutate · 4 După volum · 5 Manual |
| Linii | Recepțiile sau liniile de recepție vizate, cu suma repartizată |

Ce se întâmplă cu suma repartizată pe o linie de recepție:

- **Partea încă în stoc** se adaugă la costul stratului (FIFO, LIFO, ID) sau la `ValueOnHand` (CMP). Cheia intră în coada de recalculare de la data recepției, ca ieșirile ulterioare din acel strat să primească noul cost.
- **Partea deja ieșită** (vândută, consumată) se înregistrează direct ca ajustare pe ieșirile respective: C2 o contează pe contul ieșirii (`607` la vânzare, `6xx` la consum), nu pe stoc (decizia 3).
- Factura vine după închiderea lunii recepției: ajustarea se face în prima lună deschisă (regula 6).

Achizițiile creează documentul `CA` automat la validarea facturii de servicii marcate „cost de achiziție” sau a facturii cu diferență de preț față de NIR, prin portul `IStockLedger.AdjustInboundCostAsync` (6). Documentul se poate crea și manual, din Stocuri.

### 3.4 Ieșirile pe minus și regularizarea lor

Cu politica permisivă (S1 §3.6) sau pentru faptele din afara ERP (POS), o ieșire poate lăsa cheia pe minus.

1. Ieșirea pe minus primește **ultimul cost cunoscut** al cheii. Fără cost cunoscut: costul mediu pe firmă al variantei; fără niciunul: costul standard; altfel zero, cu alertă.
2. Se creează un **strat negativ** (cantitate rămasă < 0) la acel cost.
3. Prima intrare ulterioară acoperă întâi stratul negativ. Diferența dintre costul intrării și costul provizoriu, pe cantitatea acoperită, devine o ajustare `NegativeRegularization` pe ieșirea pe minus (contată de C2 pe contul ieșirii).
4. La CMP, la fel: diferența dintre costul intrării și media folosită provizoriu se regularizează pe ieșirea pe minus, iar media se reface pe stocul rămas.

### 3.5 Recalcularea (`CostRecalcQueue`)

O cheie intră în coadă, cu data de la care trebuie refăcută, când apare:

- o mișcare cu dată anterioară altor mișcări ale cheii (S2, regula 8);
- un cost adăugat sau o diferență de preț pe o recepție (3.3);
- o stornare a unei mișcări vechi;
- o schimbare de metodă (S1, 4.5), doar de la data schimbării.

Recalcularea (`inv.usp_StockCost_Recalculate`) reface, pe cheie, straturile și consumurile de la data din coadă încolo, în ordinea din S2 (regula 4). Pentru fiecare mișcare al cărei cost se schimbă:

- actualizează `UnitCost` și `CostAmount` (singura actualizare permisă de trigger-ul din S2, cu `SESSION_CONTEXT`);
- scrie un rând în `CostAdjustment` cu diferența, motivul `Recalculation` și luna contabilă (cea a mișcării, dacă e deschisă; altfel prima lună deschisă, regula 6);
- la sfârșit publică `CostStocAjustat`, grupat pe document sursă și lună, ca C2 să facă o notă de regularizare pe document, nu una pe mișcare.

**Când rulează** (setarea `CostRecalcScope` din S1, decizia 5):

| Valoare | Comportament |
| --- | --- |
| 1 Imediat (implicit) | În tranzacția mișcării, dacă recalcularea atinge cel mult `CostRecalcInlineLimit` mișcări (implicit 2.000). Peste limită, cheia rămâne în coadă și rulează jobul |
| 2 Prin job | Doar jobul Hangfire, la fiecare 15 minute și la cerere |

Pasul `COST_RECALC_PENDING` din închiderea lunii (C3) e **blocant, fără override**: luna nu se închide cu chei în coadă care privesc luna.

### 3.6 Concordanța cu contabilitatea

S3 implementează `ISubledgerBalanceProvider` (C3, rolul `Stock`). Întoarce valoarea stocului la sfârșitul lunii, pe gestiune și pe contul de stoc rezolvat (C2, lanțul cheii `Stock`). C3 o compară cu soldul contabil al conturilor `371`, `301`, `302`, `303`, `345`, `381`, `357`, `354`, `351`, `378`, `4428`, iar diferențele blochează închiderea (decizia 4 din C3).

Diferențele obișnuite și cauza lor, arătate de ecranul C3:

| Diferență | Cauză tipică |
| --- | --- |
| Stoc > cont | Mișcare validată, dar necontată (în „De contat”, C2), ca BC 10255 pe DEP-CJ (+3.460,00) |
| Cont > stoc | Notă manuală pe un cont de stoc (interzisă fără dreptul `Fin.ManualEntry.ControlledAccounts`, C2) |
| Diferențe mici repetate | Ajustări de recalculare încă nepublicate (job în curs) |

### 3.7 Gestiunile la preț cu amănuntul

Pentru gestiunile cu `ValuationBasis` = 2 (S1 §3.1), stocul se ține și la cost, și la preț de vânzare cu amănuntul (cu TVA).

**La intrare.** Mișcarea are `CostAmount` (costul de achiziție, 3.1) și `RetailAmount` = cantitatea × prețul din lista cu amănuntul a gestiunii la data intrării. C2 contează `371` la `RetailAmount`, `378` la diferența fără TVA (adaosul) și `4428` la TVA-ul inclus în preț.

**Metoda de descărcare (decizia 4).** Parametrul `RetailDischargeMethod` alege cum se află costul vânzărilor:

| Valoare | Metoda | Ce se întâmplă la ieșire | La închiderea lunii |
| --- | --- | --- | --- |
| 1 (implicit) | Lunar, prin coeficientul K | Costul ieșirii nu se calculează pe fiecare vânzare. C2 contează ieșirea la preț cu amănuntul (`607` = `371`). | Documentul `DA` descarcă adaosul și TVA-ul prin K (mai jos). |
| 2 | Pe fiecare vânzare (bon, factură, aviz) | Ieșirea ia costul cu metoda de evaluare a articolului (FIFO, CMP, LIFO, ID), ca în 3.2. C2 contează trei linii: `607` = `371` costul, `378` = `371` adaosul (preț fără TVA − cost), `4428` = `371` TVA-ul. | Nu se face `DA`. Pasul `RETAIL_DISCHARGE` doar verifică: soldul `378` al gestiunii = Σ pe stoc (valoare cu amănuntul fără TVA − cost), iar diferența (din rotunjiri sau modificări de preț) se arată și se regularizează cu o notă `378` = `607`. |

Parametrul e pe firmă (`inv.StockSettings.RetailDischargeMethod`, implicit 1), cu suprascriere pe gestiune (`inv.Warehouse.RetailDischargeMethod`, `NULL` = ca pe firmă). Se schimbă doar de la începutul unei luni deschise, fără mișcări în luna respectivă pe gestiune, cu dreptul `Inv.Settings.Edit`; schimbarea se jurnalizează. În ambele metode stocul se ține și la cost, și la preț cu amănuntul, deci trecerea de la o metodă la alta nu cere reevaluare. POS trimite vânzările prin `BonuriPOSSincronizate`; la metoda 2, costul bonurilor se calculează la sincronizare.

**Descărcarea lunară (`RetailDischarge`, documentul `DA`, metoda 1).** Se calculează la închiderea lunii, pe fiecare gestiune la preț cu amănuntul cu metoda 1:

```
K = (Si 378 + Rc 378) / (Si 371 + Rd 371 − Si 4428 − Rc 4428)

Vânzări fără TVA         = Rc 371 al lunii − TVA-ul colectat pe aceste vânzări
Adaos aferent vânzărilor = Vânzări fără TVA × K
Cost al vânzărilor       = Vânzări fără TVA − Adaos aferent
```

Unde Si e soldul inițial al lunii, Rd și Rc rulajele debitoare și creditoare ale lunii, calculate din mișcările gestiunii (nu din contabilitate, ca descărcarea să nu depindă de note încă necontate). Documentul `DA` publică `DescarcareAmanuntValidata`, iar C2 face nota: `378` = `607` (adaosul), `4428` = `607` (TVA-ul). Pasul `RETAIL_DISCHARGE` din C3 e de tip „Generare”, la fel ca notele de închidere, și se reface la redeschiderea lunii.

**Modificarea prețului (`RetailPriceChange`, documentul `MP`).** Schimbarea prețului cu amănuntul pentru stocul existent (reducere, scumpire) se face cu un document: varianta, cantitatea în stoc, prețul vechi și cel nou, diferența cu TVA. C2 contează diferența pe `371` față de `378` și `4428`. Lista de prețuri cu amănuntul (Vânzări) nu se schimbă singură. La validarea unei liste noi pentru gestiunile la preț cu amănuntul, Vânzări cere documentul `MP` și îl creează în ciornă, câte unul pe gestiune magazin legată de listă; la un preț temporar (V2 §3.1, regula 4) se cer două documente, la începutul și la sfârșitul perioadei (precizare din Vânzări V2, 27.11.2026).

**Inventarul** în aceste gestiuni se face la preț cu amănuntul (S6). Diferențele se descarcă tot prin K.

### 3.8 Costul standard

`ItemStockSettings.StandardCost` (coloană adăugată de S3 la tabelul din S1, `decimal(19,6)`, pe firmă) se folosește:

- la nota de predare a produselor `PF` (S2 §3.7), când Producția nu e licențiată: intrarea se face la cost standard, iar diferența față de costul efectiv (calculat lunar din consumurile pe centrul de cost al producției) se contează în `348` de C2;
- ca ultimă variantă pentru costul unei ieșiri pe minus (3.4).

Costul standard se schimbă cu un document de actualizare (cine, când, de ce), nu direct pe fișa articolului.

## 4. Fluxuri principale

### 4.1 Costul la validarea unei mișcări

1. `inv.usp_StockMovement_Post` (S2) apelează `inv.usp_StockCost_Apply` cu mișcările din TVP.
2. Pentru intrări: creează straturile (FIFO, LIFO, ID) sau refăcă media (CMP), cu costul din 3.1.
3. Pentru ieșiri: consumă straturile sau aplică media; scrie `CostLayerConsumption`; pune restul de rotunjire pe ieșirea care golește cheia (regula 3).
4. Dacă există mișcări ulterioare pe cheie, pune cheia în coadă de la data mișcării; dacă recalcularea e sub limită, o rulează imediat (3.5).
5. Întoarce `UnitCost` și `CostAmount` pe mișcări; S2 le pune în eveniment.

### 4.2 Factura de transport după recepție

1. Achizițiile validează factura Transport Rapid SRL, marcată „cost de achiziție”, legată de NIR 4471 și NIR 4472.
2. Prin `AdjustInboundCostAsync` se creează documentul `CA` în ciornă, cu repartizarea propusă după greutate.
3. Contabilul de stocuri verifică și validează. Pentru partea în stoc se ajustează straturile și se recalculează. Partea deja vândută merge pe `607`.
4. Se publică `CostStocAjustat`; C2 contează `371` / `607` = `408` (factura de serviciu a închis deja `408` față de `401` și a contat TVA-ul) (precizare din Achiziții A5, 27.11.2026).

### 4.3 Recepția cu dată în trecut

1. NIR 4479 din 20.11 se validează pe 26.11, după ce s-au făcut 38 de ieșiri din aceeași variantă după 20.11.
2. Cheia intră în coadă de la 20.11. Cele 38 de ieșiri sunt sub limită, deci recalcularea rulează imediat.
3. Costul ieșirilor se schimbă (la FIFO, ieșirile de după 20.11 iau din stratul nou doar după epuizarea celor vechi; la CMP, media se schimbă de la 20.11).
4. Se scriu ajustările pe fiecare ieșire și un eveniment `CostStocAjustat` pe fiecare document sursă afectat. C2 face notele de regularizare în noiembrie (lună deschisă).

### 4.4 Descărcarea lunară la preț cu amănuntul

1. La rularea închiderii lunii, pasul `RETAIL_DISCHARGE` calculează K și descărcarea pentru fiecare gestiune la preț cu amănuntul cu metoda 1. Pentru gestiunile cu metoda 2 face doar verificarea soldului `378` (3.7).
2. Contabilul vede simularea (ecranul `s3-03`) și o validează.
3. La redeschiderea lunii, descărcarea se reface prin recontare (C2), cu același număr.

### 4.5 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Cheie golită cu valoare reziduală de 0,01 din rotunjiri | Valoarea merge pe ultima ieșire (regula 3); niciun stoc cu cantitate 0 și valoare ≠ 0 (S3-A2) |
| Cost adăugat pe o recepție complet consumată | Toată suma merge pe ieșiri (`607` / `6xx`), fără schimbarea stocului |
| Cost adăugat pe o recepție transferată în altă gestiune | Ajustarea urmează stratul: se aplică în gestiunea unde se află acum marfa, prin lanțul transferurilor |
| Recepție în EUR, factura cu alt curs | Costul stocului rămâne la cursul recepției; diferența de curs e a facturii (F4), nu a stocului |
| LIFO și o ieșire retroactivă | Stratul „cel mai nou” e cel existent la data ieșirii, deci rezultatul nu depinde de momentul validării |
| Schimbarea listei de prețuri cu amănuntul fără document `MP` | Vânzări refuză validarea listei pentru gestiunile la preț cu amănuntul cu stoc, până la crearea documentului |
| Recalculare care ar privi o lună închisă | Diferențele se înregistrează în prima lună deschisă (regula 6); raportul de recalculare arată luna originală |
| Volum mare: 2 milioane de mișcări pe lună (retail) | Recalcularea rulează pe cheie, în paralel pe gestiuni, prin job; închiderea așteaptă coada goală |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `MiscareStocValidata` v1 | (S2) poartă costul calculat de S3 | — | — |
| `CostStocAjustat` v1 | Validarea unui `CA`, regularizarea unui negativ, recalcularea (grupat pe document sursă și lună) | `AdjustmentKind`, `SourceDocumentType`, `SourceDocumentId`, `AccountingDate`, liniile `{WarehouseId, ItemVariantId, Amount, TargetKind}` (`Stock` sau `Expense`), blocul `Accounting` | C2, Raportare; fiecare linie poartă și `SourceLineId` (ieșirea afectată), citit de Vânzări V7 pentru marjă (precizare din Vânzări V7, 27.11.2026) |
| `LunaStocInchisa` v1 | Închiderea lunii în Stocuri (C3, după pasul `COST_RECALC_PENDING`) | `CompanyId`, luna | Vânzări V7 (marja devine definitivă) (precizare din Vânzări V7, 27.11.2026) |
| `DescarcareAmanuntValidata` v1 | Validarea documentului `DA` | `WarehouseId`, luna, K, vânzările fără TVA, adaosul, TVA-ul, costul, blocul `Accounting` | C2 |
| `ModificarePretAmanuntValidata` v1 | Validarea documentului `MP` | `WarehouseId`, liniile cu diferența, blocul `Accounting` | C2, POS (prețurile casei) |

### 5.2 Consumate

| Eveniment | De la | Ce face S3 |
| --- | --- | --- |
| `PerioadaRedeschisa` v1 | C3 | Reface descărcarea lunară a lunii redeschise (în simulare până la reînchidere) |
| `ListaPreturiValidata` v1 | Vânzări | Pentru gestiunile la preț cu amănuntul, verifică dacă există documentul `MP` cerut |

## 6. Contracte publice (partea S3)

Extensia portului de stoc din Nucleu (`Erp.Core.Contracts`), folosită de Achiziții:

```csharp
public interface IStockLedger   // continuare din S2
{
    Task<int> AdjustInboundCostAsync(InboundCostAdjustmentRequest request, CancellationToken ct);   // întoarce Id-ul documentului CA
}

public enum InboundCostSourceKind : byte { ServiceInvoice = 1, InvoicePriceDifference = 2, RebateAfterReceipt = 3 }
public enum AllocationBasis : byte { Value = 1, Quantity = 2, Weight = 3, Volume = 4, Manual = 5 }

public sealed record InboundCostAdjustmentRequest(
    int CompanyId, InboundCostSourceKind Kind, long SupplierInvoiceId, DateOnly AccountingDate, decimal AmountRon,
    AllocationBasis Basis, IReadOnlyList<InboundCostTarget> Targets, bool ValidateImmediately);

public sealed record InboundCostTarget(string SourceModule, long SourceLineId, decimal? ManualAmountRon);
```

Contracte interne Stocuri (`Erp.Inventory.Contracts`):

```csharp
public interface IStockValuationQuery
{
    Task<decimal> GetUnitCostAsync(int warehouseId, int? depotId, int itemVariantId, DateOnly onDate, CancellationToken ct);
    Task<IReadOnlyList<StockValueDto>> GetValueAsync(int companyId, DateOnly asOf, StockValueGrouping grouping, CancellationToken ct);
}

// Implementat de S3 pentru C3 (concordanța), cu rolul "Stock"
public sealed class StockSubledgerBalanceProvider : ISubledgerBalanceProvider { /* ... */ }

// Implementat de S3 pentru lista de verificări C3
public sealed class CostRecalcPendingCheck : IPeriodCloseCheck   // COST_RECALC_PENDING, blocant, fără override
public sealed class RetailDischargeCheck : IPeriodCloseCheck     // RETAIL_DISCHARGE, generare
public sealed class NegativeStockCheck : IPeriodCloseCheck       // NEGATIVE_STOCK (S1), blocant, cu override
```

Comenzi interne S3: `SaveLandedCost`, `ValidateLandedCost`, `RecalculateCost` (la cerere), `SaveRetailPriceChange`, `ValidateRetailPriceChange`, `SimulateRetailDischarge`, `ValidateRetailDischarge`, `UpdateStandardCost`.

## 7. Schema SQL `inv` (partea S3)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.CostLayer` | `Id bigint` | UQ `InboundMovementId`; FK `InboundMovementId`, `WarehouseId`, `DepotId`, `ItemVariantId`, `LotId`, `SerialId` | `(WarehouseId, DepotKey, ItemVariantId, LayerDate, PostingSeq) INCLUDE (QtyRemaining, UnitCost) WHERE QtyRemaining <> 0` |
| `inv.CostLayerConsumption` | `Id bigint` | FK `OutboundMovementId`, `CostLayerId` | `OutboundMovementId`; `CostLayerId` |
| `inv.CostAdjustment` | `Id bigint` | FK `MovementId`, `LandedCostId`, `RetailPriceChangeId`; `CHECK (Amount <> 0)` | `(CompanyId, AccountingDate)`; `MovementId` |
| `inv.CostRecalcQueue` | `Id bigint` | UX `(WarehouseId, DepotKey, ItemVariantId, LotKey)` (o singură intrare pe cheie, cu cea mai veche dată) | `FromDate` |
| `inv.LandedCost`, `inv.LandedCostLine` | `Id int`, `Id bigint` | UX `(CompanyId, Number)`; FK `SupplierInvoiceId` (fără FK fizic spre `purch`, doar Id), `MovementId` pe linie | `(CompanyId, DocumentDate)` |
| `inv.RetailPriceChange`, `inv.RetailPriceChangeLine` | `Id int`, `Id bigint` | UX `(CompanyId, Number)`; FK `WarehouseId`, `ItemVariantId` | `(WarehouseId, DocumentDate)` |
| `inv.RetailDischarge` | `Id int` | UQ `(WarehouseId, PeriodStart)`; FK `WarehouseId` | — |
| `inv.Stock` (coloane noi) | — | `ValueOnHand decimal(19,2)`, `RetailValueOnHand decimal(19,2) NULL`, `AvgUnitCost AS (CASE WHEN QtyOnHand <> 0 THEN ValueOnHand / QtyOnHand END)` | — |
| `inv.StockSettings`, `inv.Warehouse` (coloană nouă) | — | `RetailDischargeMethod tinyint` (`NOT NULL DEFAULT 1` pe firmă, `NULL` pe gestiune = ca pe firmă); `CHECK (RetailDischargeMethod IN (1, 2))` | — |

Exemplu de tabel complet:

```sql
CREATE TABLE inv.CostLayer (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostLayer PRIMARY KEY,
    CompanyId          int            NOT NULL CONSTRAINT FK_CostLayer_Company REFERENCES core.Company(Id),
    DepotId            int            NULL CONSTRAINT FK_CostLayer_Depot REFERENCES inv.Depot(Id),
    DepotKey           AS ISNULL(DepotId, 0) PERSISTED,
    WarehouseId        int            NOT NULL CONSTRAINT FK_CostLayer_Warehouse REFERENCES inv.Warehouse(Id),
    ItemVariantId      int            NOT NULL CONSTRAINT FK_CostLayer_ItemVariant REFERENCES core.ItemVariant(Id),
    LotId              int            NULL,          -- FK spre inv.Lot (S4)
    SerialId           int            NULL,          -- FK spre inv.Serial (S4)
    InboundMovementId  bigint         NOT NULL CONSTRAINT FK_CostLayer_StockMovement REFERENCES inv.StockMovement(Id),
    OriginLayerId      bigint         NULL CONSTRAINT FK_CostLayer_Origin REFERENCES inv.CostLayer(Id),   -- stratul din care vine, la transfer
    LayerDate          date           NOT NULL,      -- data intrării originale (se păstrează la transfer)
    PostingSeq         bigint         NOT NULL,
    QtyIn              decimal(19,6)  NOT NULL,
    QtyRemaining       decimal(19,6)  NOT NULL,      -- < 0 doar pentru stratul negativ (3.4)
    UnitCost           decimal(19,6)  NOT NULL,
    IsNegative         bit            NOT NULL CONSTRAINT DF_CostLayer_IsNegative DEFAULT (0),
    CreatedAt          datetime2(3)   NOT NULL CONSTRAINT DF_CostLayer_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int            NOT NULL,
    ModifiedAt         datetime2(3)   NULL,
    ModifiedBy         int            NULL,
    CONSTRAINT UQ_CostLayer_InboundMovement UNIQUE (InboundMovementId),
    CONSTRAINT CK_CostLayer_Qty CHECK ((IsNegative = 0 AND QtyIn > 0 AND QtyRemaining BETWEEN 0 AND QtyIn) OR (IsNegative = 1 AND QtyRemaining < 0)),
    CONSTRAINT CK_CostLayer_UnitCost CHECK (UnitCost >= 0)
);
CREATE INDEX IX_CostLayer_Open ON inv.CostLayer (WarehouseId, DepotKey, ItemVariantId, LayerDate, PostingSeq)
    INCLUDE (LotId, SerialId, QtyRemaining, UnitCost) WHERE QtyRemaining <> 0;
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_StockCost_Apply` | Costul unui set de mișcări (din `usp_StockMovement_Post`) | Nu |
| `inv.usp_StockCost_Recalculate` | Refacerea pe cheie de la o dată, cu ajustări | Nu |
| `inv.usp_CostRecalcQueue_Process` | Jobul: preia cheile din coadă, în loturi, pe gestiuni | Nu |
| `inv.usp_LandedCost_Allocate`, `_Validate` | Repartizarea și aplicarea costurilor adăugate | Nu |
| `inv.usp_RetailPriceChange_Validate` | Modificarea prețului cu amănuntul | Nu |
| `inv.usp_RetailDischarge_Simulate`, `_Validate` | Calculul K și al descărcării | Nu |
| `inv.usp_StockValue_AsOf` | Valoarea stocului la o dată, pe gestiune, cont, grupă (pentru C3, S7) | Da (citire) |

Coduri de eroare (plaja S3 `54300–54449`, prefix API `INV_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 54301 | `INV_COST_LAYER_NOT_FOUND` | 409 | Identificare directă fără strat pentru lotul sau seria cerută |
| 54302 | `INV_COST_REVERSAL_LAYER_CONSUMED` | 409 | Stornarea unei intrări al cărei strat a fost consumat, cu politica „blocat” |
| 54303 | `INV_LANDED_COST_TARGET_INVALID` | 422 | Cost adăugat pe o linie care nu e recepție, din altă firmă, sau stornată |
| 54304 | `INV_LANDED_COST_ALLOCATION_MISMATCH` | 422 | Repartizarea manuală nu dă suma documentului |
| 54305 | `INV_ALLOCATION_BASIS_MISSING_DATA` | 422 | Repartizare după greutate sau volum fără date pe variantă |
| 54306 | `INV_RETAIL_PRICE_CHANGE_REQUIRED` | 409 | Lista de prețuri cu amănuntul schimbată fără documentul `MP` |
| 54307 | `INV_RETAIL_DISCHARGE_EXISTS` | 409 | A doua descărcare pentru aceeași gestiune și lună |
| 54308 | `INV_RECALC_PENDING` | 409 | Închiderea lunii cu chei în coada de recalculare |
| 54309 | `INV_STANDARD_COST_MISSING` | 422 | Notă `PF` pentru o variantă fără cost standard |
| 54310 | `INV_COST_NEGATIVE` | 422 | Cost unitar negativ rezultat dintr-o ajustare (de exemplu o reducere mai mare decât valoarea stocului rămas); restul merge pe ieșiri |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterile:

- Abatere de la `SP-08` pentru `inv.usp_StockCost_Recalculate`: recalcularea unei chei e secvențială (fiecare ieșire depinde de straturile lăsate de cea dinainte). Se face cu o buclă pe mișcările cheii (nu pe rânduri de document), într-un tabel temporar cu `COLLATE DATABASE_DEFAULT` (`SQL-23`). Bune practici permit explicit excepția pentru FIFO. Permanentă.
- Abatere de la `SQL-11` pentru `inv.CostLayer`, `inv.CostLayerConsumption`, `inv.CostAdjustment`: fără `RowVersion`, pentru că nu se editează din interfață. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Cost.View` | Costurile, straturile, ajustările, coada de recalculare (fără ea, ecranele de stoc arată doar cantități) |
| `Inv.LandedCost.Edit`, `.Validate` | Costurile adăugate |
| `Inv.Cost.Recalculate` | Rularea la cerere a recalculării |
| `Inv.RetailPrice.Edit`, `.Validate` | Modificările de preț cu amănuntul |
| `Inv.RetailDischarge.Validate` | Descărcarea lunară |
| `Inv.StandardCost.Update` | Actualizarea costului standard |

Roluri livrate: **Contabil stocuri** (toate), **Contabil șef** (toate), **Șef depozit** și **Gestionar** (fără `Inv.Cost.View`: văd cantitățile, nu costurile, setabil per rol), **Gestionar magazin** (`RetailPrice.Edit`, fără validare).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S3 |
| --- | --- |
| OMFP 1802/2014 (evaluarea stocurilor) | Costul de achiziție complet; metodele FIFO, CMP, LIFO, identificare specifică; aplicarea consecventă; evidența la preț cu amănuntul cu `378` și `4428` și descărcarea prin coeficientul K |
| OMFP 1802/2014 (politici contabile) | Schimbarea metodei doar justificată (S1, 4.5) și prezentată în notele explicative |
| IAS 2 (pentru planul alternativ IFRS, C1) | LIFO nu e permis: avertisment la alegere (S1); ajustarea de valoare în IFRS se face prin note doar pe planul alternativ |
| Codul fiscal, art. 25 și 26 | Costul stocurilor ieșite e deductibil; ajustările pentru depreciere (`397`) sunt în afara S3 |
| Legea 82/1991 | Documente justificative pentru fiecare ajustare de cost (factura, documentul `CA`, `MP`, `DA`) |

Integrări: niciuna externă. Interne: S1 (metode, setări), S2 (mișcări), S4 (loturi pentru identificare directă), C2 (contarea ajustărilor), C3 (concordanța, pașii de închidere), F4 (cursul de recepție), Achiziții (facturile cu costuri adăugate), Vânzări (lista cu amănuntul).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Valoarea stocului la o dată | Pe gestiune, depozit, cont, grupă, variantă: cantitate, valoare, cost mediu | Contabil stocuri, Contabil șef |
| Straturi de cost | Straturile deschise ale unei variante, cu data, cantitatea rămasă, costul, documentul de intrare | Contabil stocuri |
| Ajustări de cost | Pe lună: costuri adăugate, diferențe de preț, regularizări de negativ, recalculări, cu documentele și luna originală | Contabil șef, Auditor |
| Descărcarea la preț cu amănuntul | Pe gestiune și lună: K, adaos, TVA, cost | Contabil |
| Marja pe vânzări | Pe variantă și client: preț de vânzare (Vânzări) minus costul ieșirii (S3) | Director comercial (prin Raportare 02.13) |

## 11. Ecrane principale

Machetele (HTML + PNG) sunt în [ecrane/s3-evaluare/](ecrane/s3-evaluare/), în meniul Stocuri › Evaluare.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Costuri adăugate | Lista documentelor `CA`; documentul cu factura, baza de repartizare și liniile de recepție: partea în stoc, partea vândută | `s3-01-costuri-adaugate` |
| Recalculare cost | Coada de recalculare și ultimele rulări; efectul unei recalculări: mișcările schimbate, diferențele pe lună și document | `s3-02-recalculare-cost` |
| Descărcare la preț cu amănuntul | Pe gestiune și lună: rulajele, K, adaosul, TVA-ul, costul, nota propusă | `s3-03-descarcare-amanunt` |
| Modificări de preț | Documentul `MP`: variantele, stocul, prețul vechi și nou, diferența cu adaos și TVA | `s3-04-modificare-pret` |
| Straturi de cost | Pentru o variantă și gestiune: straturile FIFO deschise, consumul lor, costul mediu | `s3-05-straturi-de-cost` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S3-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S3-A1 | Pe orice cheie și la orice dată: `ValueOnHand` = suma `CostAmount` + suma ajustărilor | Test de proprietate |
| S3-A2 | Când cantitatea unei chei ajunge la 0, valoarea e exact 0,00, la toate metodele | Test de proprietate |
| S3-A3 | FIFO: 100 buc la 10, 100 buc la 12, ieșire 150 → cost 1.600,00; LIFO → 1.700,00; CMP → 1.650,00 | Test unitar |
| S3-A4 | Un transfer păstrează valoarea firmei exact și mută straturile cu data lor originală | Test de proprietate (cu S2) |
| S3-A5 | O recepție retroactivă schimbă costul ieșirilor ulterioare și produce ajustări a căror sumă e egală cu diferența de valoare a stocului | Test de proprietate |
| S3-A6 | Un cost adăugat de 1.840,00 pe o recepție vândută 40%: 1.104,00 merge pe stoc, 736,00 pe `607` | Test de integrare |
| S3-A7 | O ieșire pe minus la ultimul cost, urmată de o intrare cu alt cost, produce ajustarea exactă pe ieșirea pe minus | Test unitar |
| S3-A8 | K-ul lui noiembrie pentru MAG-CJ: (41.380 + 29.610) / (251.200 + 182.400 − 21.802 − 15.829) = 0,179282; adaosul descărcat pentru vânzări fără TVA de 177.120,00 = 31.754,38 | Test unitar |
| S3-A9 | Luna nu se închide cu chei în coada de recalculare (`54308`), fără override | Test de integrare (cu C3) |
| S3-A10 | Concordanța `371` din C3 iese egală când toate mișcările și ajustările sunt contate | Test de integrare (cu C2, C3) |
| S3-A11 | Recalcularea a 100.000 de mișcări pe 5.000 de chei se termină în sub 2 minute prin job | Test de performanță |
| S3-A12 | Toate procedurile S3 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |
| S3-A13 | Pe o gestiune la preț cu amănuntul cu `RetailDischargeMethod` = 2, un bon de 12,99 lei (TVA 11%, cost FIFO 8,20) dă `607` = `371` 8,20, `378` = `371` 3,50, `4428` = `371` 1,29, iar închiderea lunii nu creează `DA` | Test de integrare |

Criteriile S3-A1, S3-A2 și S3-A5 sunt critice: ele țin valoarea stocului egală cu contabilitatea și fac ca recalcularea să nu „piardă” bani.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | CMP: pe fiecare intrare sau medie lunară | Pe fiecare intrare (perpetuu), cum s-a decis în S1; media lunară nu se oferă | 3.1, 3.2 | Decis în S1 (26.11.2026) |
| 2 | Diferențele de cost care privesc o lună închisă | Se înregistrează în prima lună deschisă, ca regularizare, cu luna originală afișată în raport; lunile închise nu se schimbă | 2 (regula 6), 3.5 | Decis (26.11.2026) |
| 3 | Costul adăugat pe marfa deja vândută | Partea vândută merge direct pe contul ieșirii (`607`, `6xx`), partea în stoc pe stoc | 3.3 | Decis (26.11.2026) |
| 4 | Costul vânzărilor în magazinele la preț cu amănuntul | Ambele metode, alese prin parametrul `RetailDischargeMethod` (firmă, suprascris pe gestiune): 1 lunar prin coeficientul K (implicit), 2 pe fiecare vânzare, la costul metodei de evaluare | 3.7 | Decis (26.11.2026) |
| 5 | Când rulează recalcularea | Imediat, în tranzacție, până la 2.000 de mișcări; peste, prin job la 15 minute; luna nu se închide cu coada nevidă | 3.5 | Decis (26.11.2026) |
| 6 | Valoarea reziduală din rotunjiri | Se pune pe ieșirea care golește cheia; nicio cheie cu cantitate 0 și valoare ≠ 0 | 2 (regula 3) | Decis (26.11.2026) |
| 7 | Costul ieșirii pe minus | Ultimul cost cunoscut, regularizat automat la prima intrare | 3.4 | Decis (26.11.2026) |
| 8 | Costurile pentru gestionari | Gestionarii și șefii de depozit nu văd costurile (doar cantitățile), setabil pe rol | 8 | Decis (26.11.2026) |

## Note de implementare (28.11.2026)

| Pas | Ce s-a implementat | Decizii și abateri |
| --- | --- | --- |
| S3.1 | Migrarea `2026.11.081` (`inv.CostLayer`, `inv.CostLayerConsumption`, `inv.CostAdjustment`, `inv.CostRecalcQueue`, `ItemStockSettings.StandardCost`, tipurile `inv.tt_StockMovementCosted`, `tt_CostKey`, `tt_CostKeyState`, `tt_CostLayerWrite`, `tt_CostConsumptionWrite`, `tt_CostAdjustmentWrite`, `tt_MovementCostUpdate`, `BigIdList`; ștergerea `inv.usp_StockMovement_Reverse`); motorul pur `CostEngine` (`Erp.Inventory.Domain.Costing`); `CostCalculator`, `CostRecalculator` (`Erp.Inventory.Application.Movements`); procedurile `inv.usp_StockCost_Load`, `inv.usp_StockMovement_ForReversal`, `inv.usp_CostRecalc_Enqueue`, `inv.usp_StockCost_History`, `inv.usp_StockCost_Rebuild`, `inv.usp_CostRecalcQueue_List`; `inv.usp_StockMovement_Post` scrie acum miscările costate, straturile, consumurile și regularizările; `inv.usp_Stock_Verify` adună ajustările care schimbă stocul; comanda `RecalculateCost`, interogarea `GetCostRecalcQueue`, jobul `CostRecalcJob` (la 15 minute); evenimentul `CostStocAjustat` v1; permisiunile S3 (8); testele S3-A1, A2 (proprietate în motor și în bază), A3 (unitar și în bază pe cele trei metode), A4 (transferul mută straturile cu data lor, stornarea le pune înapoi), A5 (recepția retroactivă la CMP, lanțul prin transfer la LIFO), A7, stornarea unei intrări consumate (54302) | **Abatere de la 4.1 și 7** (`inv.usp_StockCost_Apply` set-based): costul îl calculează motorul C# `CostEngine`, aceeași funcție la înregistrare și la recalculare (nu două algoritme care pot diverge), testabil unitar; SQL blochează cheile (`usp_StockCost_Load`, `UPDLOCK` în ordinea cheii), citește starea și persistă rezultatul în același apel și aceeași tranzacție. **Recalcularea** reface cheia pe toată istoria ei (nu doar de la data din coadă), înlocuiește straturile, consumurile și regularizările cheii, actualizează doar mișcările al căror cost se schimbă (cu ajustarea `Recalculation`, `InStock` = 0) și reface apoi cheile intrărilor pereche ale transferurilor (cel mult 50 de pași într-un apel, restul rămân în coadă). **Ajustările** au `InStock`: 1 = schimbă valoarea stocului (regularizarea negativului; costurile adăugate vor folosi tot asta), 0 = urma unei schimbări deja scrise pe mișcare; valoarea cheii = mișcările + ajustările `InStock` = 1. **Luna ajustării** e luna mișcării; mutarea în prima lună deschisă (regula 6) o face contarea (C2), odată cu blocul `Accounting`. **Stornarea unei intrări** e refuzată (54302) dacă vreun strat al ei a fost atins, indiferent de politică. LIFO și FIFO consumă întâi straturile existente la data ieșirii, apoi (la o ieșire cu dată în trecut) restul, iar recalcularea pune ordinea corectă. Gestiunile la preț cu amănuntul folosesc deocamdată CMP (S3.3). `OriginLayerId` nu are cheie străină: straturile se refac la recalculare |

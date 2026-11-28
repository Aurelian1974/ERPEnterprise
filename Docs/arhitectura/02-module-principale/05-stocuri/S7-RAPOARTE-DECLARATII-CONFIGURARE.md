# ERP · 02.05 Stocuri · S7 Rapoarte, declarații și configurare

26 noiembrie 2026 · Aurelian · **Validat (26.11.2026)**. Ultima din cele 7 părți ale modulului 02.05 Stocuri. Pornește de la S1–S6, validate pe 26.11.2026. S7 adună tot ce se citește din stoc: rapoartele comune (balanța de stocuri, concordanța cu contabilitatea, vechimea stocului, stocul sub minim), cele două declarații care pleacă din stoc (**D406 stocuri** pentru SAF-T și **Intrastat**) și ecranul **Configurare › Stocuri**, unde se editează setările definite în S1–S6.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori S7 `54800–54899`. Documente părinte: [S1 Gestiuni, depozite și setări](S1-GESTIUNI-SI-SETARI.md), [S2 Mișcări și documente](S2-MISCARI-SI-DOCUMENTE.md), [S3 Evaluare și cost](S3-EVALUARE-SI-COST.md), [S4 Loturi, serii și termene](S4-LOTURI-SERII-TERMENE.md), [S5 Rezervări și disponibil](S5-REZERVARI-SI-DISPONIBIL.md), [S6 Inventariere](S6-INVENTARIERE.md). Declarațiile fiscale: [C4 Declarații](../02-finante-contabilitate/C4-DECLARATII.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Sursa unică a rapoartelor de stoc e registrul de mișcări** (`inv.StockMovement`, S2), cu costul din S3. Soldul curent stă în `inv.Stock`. Niciun raport nu recalculează costul: îl citește.
- Stocul stă pe gestiune și pe SKU (`core.ItemVariant`), iar la articolele urmărite pe CKU (SKU + lot + termen, `inv.Lot`) sau pe serie (S4). Gestiunile au contul lor de stoc (`371`, `381`, `357` sau extrabilanțier `8033` la custodie, S1).
- **D406 (SAF-T) e al modulului Finanțe (C4)**: C4 generează fișierul, îl validează cu DUKIntegrator și ține depunerea. Stocuri doar furnizează secțiunea de stocuri, prin `ISaftSectionProvider` cu `Section = "PhysicalStock"` (C4 §6).
- Configurarea comună a aplicației e un singur ecran, **Administrare › Configurare**, cu grupuri de tab-uri pe module (Nucleu §11.1). Finanțe are grupul său; S7 adaugă grupul **Stocuri**.
- **Ce NU face S7:** nu creează comenzi de aprovizionare (Achiziții, din propunerea S7), nu înregistrează ajustările pentru depreciere (C2, din propunerea S7), nu face analizele BI între module (Raportare), nu depune D406 (C4) și nu calculează TVA-ul (C4).

## 1. Scop și perimetru

| Zonă | Ce face S7 | Ce NU face (și cine face) |
| --- | --- | --- |
| Rapoarte de stoc | Catalogul de rapoarte, motorul comun (filtre, la o dată, export, vizualizări salvate, trimitere programată), soldurile lunare pre-calculate | Tablourile de bord între module (Raportare) |
| Concordanța | Stocul pe cont și gestiune față de balanța contabilă, zilnic și la închiderea lunii | Nota de corecție (C2) |
| Vechimea stocului | Valoarea pe intervale de vechime, articolele fără mișcare, propunerea de depreciere | Ajustarea `397` (C2) |
| Stoc minim | Articolele sub minim, cantitatea propusă până la maxim | Comanda la furnizor (Achiziții) |
| D406 stocuri | Secțiunea „PhysicalStock” și mișcările de bunuri, verificările de date | Fișierul, validarea, depunerea (C4) |
| Intrastat | Declarația lunară de introduceri și expedieri, pragurile, fișierul XML | Valorile din facturi (Achiziții, Vânzări le furnizează) |
| Configurare › Stocuri | Setările firmei din S1–S6, motivele, identificatorii, regulile pe client, codurile pentru declarații | Seriile de documente (Nucleu, tab-ul Serii) |

## 2. Model de domeniu

```
 StockMovement (S2) ──► StockMonthlyBalance (soldul la sfârșit de lună, pe gestiune, SKU, CKU, cont)
        │                     │
        │                     ├─► rapoarte la o dată: sold lunar + mișcările de după
        │                     └─► ReconciliationRun (stoc ↔ balanța contabilă, pe cont și gestiune)
        │
        ├─► SaftPhysicalStock provider ──► C4 D406 stocuri
        │       SaftMovementTypeMap (tip de mișcare S2 → cod ANAF)
        │
        └─► IntrastatDeclaration ── IntrastatLine (NC8, țări, natura tranzacției, transport, livrare, masă, valori)
                IntrastatThreshold (pe an și flux)
                ItemTradeInfo (țara de origine, UM suplimentară)

 core.ReportView ── core.ReportSchedule (motorul comun de rapoarte, în Nucleu)
 StockSettings (S1, editat în Configurare › Stocuri)
```

Reguli:

1. **Un raport nu are propria lui logică de stoc.** Cantitățile vin din `inv.StockMovement` și `inv.Stock`, valorile din costul S3 scris pe mișcare. Două rapoarte cu aceleași filtre dau aceleași totaluri.
2. **„La o dată” înseamnă soldul lunar plus mișcările de după.** La închiderea lunii (C3) și la orice mișcare retroactivă (S3), `inv.StockMonthlyBalance` se recalculează pentru luna afectată și cele următoare. Raportul la 14.10 citește soldul de la 30.09 și adaugă mișcările din 01.10–14.10.
3. **Concordanța se face pe cont și gestiune.** Soldul stocului unei gestiuni cu cont `371` se compară cu soldul contului `371` analitic pe gestiune (C1/C2). Diferențele au întotdeauna o cauză afișată: notă manuală fără mișcare, mișcare fără notă (eveniment neprocesat), cont schimbat pe gestiune.
4. **Declarațiile se generează din date confirmate.** D406 stocuri și Intrastat se generează doar pentru luni închise operativ (C3) sau cu avertisment explicit, iar fișierul păstrează instantaneul datelor din care a fost făcut.
5. **Setările au istoric.** Orice modificare din Configurare › Stocuri intră în audit (cine, când, valoarea veche și cea nouă); setările care schimbă comportamentul costului sau al loturilor (de exemplu `RetailDischargeMethod`) intră în vigoare de la prima lună deschisă.

## 3. Funcționalitate

### 3.1 Catalogul de rapoarte

Rapoartele definite în S1–S6 (secțiunea 10 a fiecărei părți) folosesc același motor. Motorul a fost mutat în Nucleu ([§3.5](../01-nucleu/NUCLEU.md), `core.ReportView` și `core.ReportSchedule`) și e comun tuturor modulelor; comportamentul de mai jos rămâne neschimbat (precizare din Achiziții A7, 27.11.2026):

- **Filtre comune:** firmă (și grup), punct de lucru, locație, depozit, gestiune, cont de stoc, categorie, familie, model, SKU, CKU, perioadă sau dată, cu „include gestiunile de custodie” (implicit nu).
- **Niveluri:** gestiune → SKU → CKU sau serie, cu desfășurare din total până la mișcare (fișa de magazie, documentul sursă).
- **Unități:** UM de bază sau UM aleasă (bax, palet, kg), cantitate și valoare; la gestiunile la preț de vânzare, valoarea la cost și la preț.
- **Export:** Excel (`.xlsx`, generat în flux, fără limită de rânduri), PDF (cu antetul firmei), CSV.
- **Vizualizări salvate** (`core.ReportView`): filtre, coloane, sortare; personale sau partajate pe rol.
- **Trimitere programată** (`core.ReportSchedule`): zilnic, săptămânal, lunar, la ora aleasă, pe e-mail (PDF sau Excel), prin joburile Nucleului; destinatarii trebuie să aibă dreptul de vizualizare al raportului.

Rapoartele S7:

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Balanța de stocuri | Pe cont, gestiune, SKU (și CKU): sold inițial, intrări, ieșiri, sold final, cantitativ și valoric, pe o perioadă | Contabil stocuri, Contabil șef |
| Concordanța cu contabilitatea | Stocul pe cont și gestiune față de balanța contabilă, cu diferențele și cauzele | Contabil șef, Auditor |
| Vechimea stocului | Valoarea pe intervale de vechime, articolele fără mișcare, propunerea de depreciere | Director economic, Șef depozit |
| Stoc sub minim | SKU-urile sub `MinQty`, disponibilul proiectat, cantitatea propusă până la `MaxQty` | Achiziții, Șef depozit |
| Rotația stocului | Zilele de stoc și numărul de rotații pe SKU, categorie, gestiune | Director comercial |
| Stocul la o dată | Cantitatea și valoarea la orice dată, cu costul de la acea dată, pe gestiune, SKU și CKU (regula 2) | Contabil, Auditor, Șef depozit |
| Stocul pe interval | Pe SKU: stocul inițial la începutul intervalului, mișcările din interval și stocul final | Contabil stocuri, Gestionar |
| Fișa de magazie | Un SKU într-o gestiune: fiecare mișcare în ordine, cu soldul după ea | Gestionar, Contabil stocuri |

#### Stocul la o dată, stocul pe interval și fișa de magazie

Cerute de Aurelian pe 26.11.2026. Toate trei citesc registrul de mișcări, cu soldurile lunare ca punct de pornire (3.2).

- **Stocul la o dată** (de exemplu 01.08.2024): pentru fiecare gestiune, SKU și, opțional, CKU sau serie, cantitatea existentă la sfârșitul zilei alese și **valoarea de la acea dată**: costul scris pe mișcări până atunci (FIFO și LIFO: straturile rămase atunci; CMP: costul mediu de atunci; magazinele: și prețul cu amănuntul de atunci). Lunile închise nu se mai schimbă (diferențele de cost ulterioare intră în prima lună deschisă, S3), deci stocul la o dată dintr-o lună închisă e exact cel raportat atunci. Datele de dinaintea punerii în funcțiune arată doar soldul inițial (`OPENING`, S2) de la data pornirii, cu mesaj.
- **Stocul pe interval** (de exemplu 01.11–26.11.2026): pe fiecare SKU, un rând cu **stocul inițial** la începutul intervalului, intrările, ieșirile și **stocul final** la sfârșitul lui; rândul se desface și arată **mișcările din interval**, între stocul inițial și cel final. Se poate grupa pe cont, gestiune, categorie, SKU sau CKU. Balanța de stocuri e același raport, grupat pe cont și gestiune.
- **Fișa de magazie** (formularul 14-3-8, OMFP 2634/2015): un SKU într-o gestiune, pe o perioadă. Primul rând e soldul inițial; urmează fiecare mișcare în ordinea din registru (data, apoi `PostingSeq`, S2), cu documentul, partenerul sau gestiunea pereche, lotul sau seria, cantitatea intrată, ieșită și **stocul după mișcare**; ultimul rând e soldul final. Valorile apar doar cu dreptul `Inv.Report.ViewValue`; gestionarul vede fișa cantitativă. Din fiecare rând se deschide documentul sursă. Se tipărește pe formular, pentru semnătura gestionarului.

Toate trei au aceleași filtre, export Excel și PDF și pot fi salvate ca vizualizări sau trimise programat.

### 3.2 Soldurile lunare (`inv.StockMonthlyBalance`)

Un rând pe (firmă, an, lună, gestiune, depozit, SKU, lot) cu soldul final cantitativ și valoric și rulajele lunii (intrări, ieșiri, cantitate și valoare). Se scrie:

- la închiderea operativă a lunii (C3 apelează `inv.usp_StockMonthlyBalance_Build`);
- la recalcularea costului (S3) sau la o mișcare retroactivă, pentru luna afectată și următoarele, în același job ca recalcularea;
- pentru luna curentă nu se scrie: raportul folosește `inv.Stock` și mișcările lunii.

Verificare: suma rulajelor din `inv.StockMovement` pe lună trebuie să fie egală cu rulajul din `inv.StockMonthlyBalance` (testul S7-A1).

### 3.3 Concordanța cu contabilitatea (`inv.ReconciliationRun`)

Un job zilnic (setabil, implicit la 02:00) și închiderea lunii (C3) compară, pe fiecare cont de stoc și gestiune:

| Coloană | Sursa |
| --- | --- |
| Stoc | Valoarea stocului din `inv.Stock` sau `inv.StockMonthlyBalance` pe gestiunile cu acel cont |
| Contabilitate | Soldul contului analitic al gestiunii din balanța C2 |
| Diferență | Contabilitate − Stoc |
| Cauze | Note manuale pe cont fără mișcare de stoc; mișcări validate fără notă (evenimente neprocesate sau în eroare); mișcări în luni închise; schimbarea contului gestiunii |

- Pentru gestiunile la preț de vânzare, comparația se face pe `371` la preț cu amănuntul, iar `378` și `4428` se verifică separat față de soldurile din S3.
- Diferența poate fi explicată (motiv, document) de Contabil șef. **Închiderea lunii (C3) e refuzată cât timp există diferențe neexplicate** (`54801`), cu excepția dreptului `Inv.Reconciliation.Override`.
- Gestiunile de custodie se compară cu contul extrabilanțier `8033`, fără blocare.

### 3.4 Vechimea stocului și propunerea de depreciere

- Vechimea se calculează pe straturile de cost (S3) la FIFO și la LIFO, și pe intrările neconsumate în ordine FIFO la CMP (aproximare declarată: la CMP, cantitatea rămasă e considerată din intrările cele mai noi).
- Intervalele (`inv.AgingBucket`) sunt configurabile pe firmă, implicit 0–90, 91–180, 181–365 și peste 365 de zile. Fiecare interval poate avea un procent de depreciere propus (implicit 0%, 0%, 20%, 50%).
- „Fără mișcare” = nicio ieșire de peste N zile (setare `NoMovementDays`, implicit 180).
- Propunerea de depreciere se trimite la C2 ca eveniment `PropunereDepreciere` (același eveniment ca în S6), după ce Directorul economic o aprobă. S7 nu face nota.

### 3.5 Stocul sub minim

Pe SKU și gestiune: `MinQty` și `MaxQty` din `inv.ItemStockSettings` (S1), suprascrise opțional pe gestiune (`inv.ItemWarehouseSettings`, nou în S7). Raportul arată disponibilul de azi și disponibilul proiectat (S5) la orizontul de aprovizionare al articolului; cantitatea propusă = `MaxQty − disponibil proiectat`, rotunjită la UM de achiziție. Propunerea se trimite la Achiziții (`PropunereAprovizionare`), care decide comanda.

### 3.6 D406 stocuri (SAF-T)

D406 stocuri se depune **la cererea ANAF**, pentru perioada din notificare (C4 §3.5). Fluxul e al C4; S7 implementează `ISaftSectionProvider` cu `Section = "PhysicalStock"`, care furnizează în flux (`IAsyncEnumerable`):

| Bloc SAF-T | Conținut din Stocuri |
| --- | --- |
| Nomenclatorul de gestiuni (`Warehouses`) | Gestiunile din perioadă: cod, denumire, adresa locației; custodiile cu proprietarul |
| Nomenclatorul de produse (`Products`) | SKU-urile cu mișcări: cod, denumire, cod NC (`CnCode`), UM de bază cu cod UN/ECE, grupa |
| Stocul fizic (`PhysicalStock`) | Pe gestiune și SKU: contul de stoc (cu `SaftAccountCode`), tipul produsului, proprietarul, UM, sold inițial și final cantitativ și valoric pe perioadă, prețul unitar, metoda de evaluare |
| Mișcările de bunuri (`MovementOfGoods`) | Fiecare document cu mișcări: număr, dată, tipul de mișcare cu codul ANAF, liniile cu SKU, cantitate, UM, valoare, gestiunile sursă și destinație, partenerul |

- Tipul de mișcare din S2 se traduce în codul ANAF prin `inv.SaftMovementTypeMap`, livrat ca date (`MIG-06`) din nomenclatorul ANAF în vigoare, nu scris în cod. Un motiv de încărcare sau descărcare (S2 §3.5) poate avea propriul cod, care îl suprascrie pe al tipului.
- Elementele exacte și ordinea lor urmează schema XSD ANAF în vigoare pentru perioadă (versiunea e în `fin.TaxFormVersion`, C4); providerul livrează obiectele, C4 le scrie.
- **Verificările înainte de generare** (ecranul D406 stocuri): SKU-uri cu mișcări fără cod NC, UM fără cod UN/ECE, gestiuni fără cont SAF-T, tipuri de mișcare sau motive fără cod ANAF, mișcări în luni neînchise, concordanța (3.3) neexplicată. Fiecare problemă are legătura spre ecranul care o rezolvă.
- Generarea rulează în fundal, cu citire paginată (`NET-12`); un an cu 5 milioane de linii de mișcare nu depășește 1 GB RAM.

### 3.7 Intrastat

Declarația statistică lunară a schimburilor de bunuri cu alte state membre UE, depusă la INS, separat pe **introduceri** (din UE în România) și **expedieri** (din România în UE).

**Pragurile.** Obligația există pe fiecare flux, de la luna în care valoarea cumulată din anul curent depășește pragul anual (sau din ianuarie, dacă pragul a fost depășit în anul anterior). Pragurile se publică anual de INS și se introduc ca date (`inv.IntrastatThreshold`, pe an și flux), nu în cod. S7 urmărește cumulul lunar și avertizează când fluxul se apropie de prag (90%) și când îl depășește.

**Sursa datelor.** Mișcările de stoc cu partener din alt stat UE, pentru care bunurile trec fizic granița:

| Flux | Mișcări | Valoarea facturată | Particularități |
| --- | --- | --- | --- |
| Introduceri | `RECEIPT_PURCHASE` (NIR de la furnizor UE), `RETURN_FROM_CUSTOMER` (client UE) | Din factura de achiziție (Achiziții); până la factură, din comandă, marcată „provizorie” | Țara de expediere = țara furnizorului sau cea declarată pe recepție; țara de origine din `ItemTradeInfo` sau de pe recepție |
| Expedieri | `ISSUE_SALE` (aviz către client UE), `RETURN_TO_SUPPLIER` (furnizor UE) | Din factura de vânzare (Vânzări) | Codul de TVA al partenerului e obligatoriu |

Mișcările interne (transferuri între gestiuni din România), custodia și serviciile nu intră. Transferurile proprii în alt stat UE (gestiune `LA-LOG` în străinătate) intră cu natura tranzacției corespunzătoare.

**Linia Intrastat** (`inv.IntrastatLine`) se agregă pe: cod NC8, țara partenerului, țara de origine (introduceri), natura tranzacției, modul de transport, condiția de livrare și, la expedieri, codul de TVA al partenerului. Câmpuri: masa netă (kg, din `NetWeightKg` × cantitate sau din recepție), cantitatea în UM suplimentară (dacă NC8 o cere, din `ItemTradeInfo`), valoarea facturată (lei) și **valoarea statistică**.

- **Valoarea statistică** = valoarea facturată ajustată la granița României după condiția de livrare: la EXW/FCA se adaugă transportul până la graniță, la DAP/DDP se scade transportul de la graniță. Când costul real nu e cunoscut, se aplică procentul estimat din setări (`IntrastatTransportPct`, implicit 3%); costul real vine din costurile adăugate ale recepției (S3), dacă există.
- Codul NC8 al liniei e codul efectiv al variantei (altfel al articolului), copiat pe linia documentului; UM suplimentară vine din `core.CnCode`. Natura tranzacției, modul de transport și condiția de livrare sunt coduri din nomenclatoarele INS `core.TransactionNature`, `core.TransportMode`, `core.DeliveryTerm`. La introducerile din UE, valoarea facturată și cea statistică vin din `IImportCostReader` (Achiziții A6) (precizare din Achiziții A6, 27.11.2026).
- Natura tranzacției, modul de transport și condiția de livrare vin de pe document (Achiziții, Vânzări), cu valori implicite pe partener (`inv.PartnerIntrastatDefault`).
- Valorile în valută se convertesc la cursul BNR folosit pe factură (F4).
- **Corecțiile** (factură sosită după declarare, valoare schimbată, retur) se fac prin declarația de corecție a lunii respective, conform regulilor INS; S7 marchează declarația depusă „De corectat” și arată diferențele.
- Fișierul XML se generează după schema INS în vigoare (versiunea ca date) și se încarcă în aplicația online a INS; recipisa se atașează.

### 3.8 Configurare › Stocuri

Grupul **Stocuri** din Administrare › Configurare (Nucleu §11.1), vizibil doar cu dreptul `Inv.Settings.Manage` (sau tab cu tab, pe drepturile de mai jos). Tab-urile:

| Tab | Secțiuni | Definite în |
| --- | --- | --- |
| Generale | Metoda de evaluare propusă, nivelul de evidență, politica de stoc negativ, datarea în trecut, transferurile, recalcularea costului, repartizarea costurilor adăugate, descărcarea la amănunt | S1 §3.3, S2, S3 |
| Documente | Motivele de încărcare și descărcare (contare, TVA, deductibilitate, atașament, prag de aprobare, cod SAF-T) | S2 §3.5 |
| Loturi și serii | Regula de alegere a loturilor (FEFO), zilele de avertizare, tipurile de identificatori pe unitate (IMEI, MAC, VIN…), regulile de identificatori pe model | S4 |
| Clienți | Durata de valabilitate minimă cerută de client, durata și prioritatea rezervărilor pe client | S4, S5 |
| Inventar | Blocarea, numărarea oarbă, pragurile de renumărare și aprobare, inventarul la predare | S6 |
| Rapoarte și declarații | Intervalele de vechime și procentele de depreciere, zilele fără mișcare, ora concordanței, pragurile Intrastat, procentul de transport, codurile SAF-T ale tipurilor de mișcare | S7 |

Normele de perisabilitate și grupele de confuzie rămân în meniul Stocuri › Inventar (S6), pentru că le folosește comisia; din tab-ul Inventar există legătura spre ele.

Fiecare setare arată valoarea pe firmă și, unde există, numărul de suprascrieri (pe gestiune, punct de lucru, locație, client), cu legătura spre ele. Valorile implicite vin din kitul de instalare (`MIG-06`). Export și import din Excel, ca în grupul Finanțe.

## 4. Fluxuri principale

### 4.1 Închiderea lunii noiembrie

1. C3 pornește închiderea operativă; Stocuri rulează concordanța: `371` bate pe toate gestiunile (4.215.500,00 lei), dar `357` (`LA-LOG`) are 240,00 lei în plus în contabilitate.
2. Cauza afișată: nota manuală `NC 001842` din 20.11 pe `357`, fără mișcare de stoc. Ion Popescu stornează nota în C2; concordanța rulată din nou e la zero.
3. C3 închide luna; `inv.StockMonthlyBalance` se scrie pentru noiembrie.

### 4.2 Notificarea ANAF pentru D406 stocuri

1. ANAF cere D406 stocuri pentru 01.01–31.10.2026, cu termen 18.12.2026. Simona Iordache deschide ecranul D406 stocuri și pornește verificările.
2. Rezultă 12 SKU-uri cu mișcări fără cod NC și 2 UM fără cod UN/ECE; se corectează din Articole.
3. „Generează” trimite cererea la C4, care scrie fișierul cu secțiunea furnizată de Stocuri, îl validează cu DUKIntegrator și ține depunerea.

### 4.3 Intrastat noiembrie

1. Introducerile au depășit pragul anual în martie, deci firma declară lunar; expedierile sunt sub prag.
2. Declarația se generează după închiderea lunii: 3 linii (zahăr din Polonia, cafea din Germania, dozatoare din Italia). Pentru cafea factura nu a sosit încă: valoarea e din comandă, marcată provizorie.
3. Fișierul XML se încarcă la INS până pe 15.12.2026; dacă factura de cafea vine cu altă valoare, declarația devine „De corectat”.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `ConcordantaStocCalculata` v1 | Jobul zilnic și închiderea lunii | Pe cont și gestiune: stoc, contabilitate, diferență, cauze | C3 (verificarea la închidere), Notificări |
| `PropunereDepreciere` v1 | Aprobarea propunerii din vechimea stocului (și S6) | Liniile cu SKU, CKU, valoare, procent, motiv | C2 |
| `PropunereAprovizionare` v1 | Din raportul de stoc sub minim, la cerere sau programat | SKU, gestiune, cantitate propusă, data necesară | Achiziții |
| `DeclaratieIntrastatGenerata` v1 | Generarea și depunerea | Luna, fluxul, tipul (inițială, corecție), totaluri | Calendarul fiscal (C4), Notificări |

### 5.2 Consumate

| Eveniment | De la | Ce face S7 |
| --- | --- | --- |
| `LunaInchisaOperativ` v1 | C3 | Scrie soldurile lunare și rulează concordanța finală |
| `CostRecalculat` v1 | S3 (intern) | Rescrie soldurile lunare afectate |
| `FacturaAchizitieValidata` v1, `FacturaVanzareValidata` v1 | Achiziții, Vânzări | Completează valoarea facturată a liniilor Intrastat; marchează „De corectat” declarațiile depuse afectate |

## 6. Contracte publice (partea S7)

```csharp
// Implementarea Stocuri a contractului din C4 (Erp.Core.Contracts)
public sealed class SaftPhysicalStockProvider : ISaftSectionProvider
{
    public string Section => "PhysicalStock";
    public IAsyncEnumerable<object> ReadAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);
    // întoarce, în ordine: SaftWarehouseDto, SaftProductDto, SaftPhysicalStockDto, SaftMovementOfGoodsDto
}

public interface IStockReportQuery   // Erp.Core.Contracts, pentru Raportare și C3
{
    Task<IReadOnlyList<StockAccountBalanceDto>> GetAccountBalancesAsync(int companyId, DateOnly atDate, CancellationToken ct);
    Task<ReconciliationResultDto> ReconcileAsync(int companyId, DateOnly atDate, CancellationToken ct);
}

public sealed record StockAccountBalanceDto(int WarehouseId, string AccountCode, decimal Quantity, decimal Value, decimal? RetailValue);
public sealed record ReconciliationResultDto(DateOnly AtDate, bool IsBalanced, IReadOnlyList<ReconciliationLineDto> Lines);
public sealed record ReconciliationLineDto(int WarehouseId, string AccountCode, decimal StockValue, decimal LedgerValue,
    decimal Difference, IReadOnlyList<string> Causes, bool IsExplained);

public enum IntrastatFlow : byte { Arrival = 1, Dispatch = 2 }
```

Comenzi interne S7: `BuildMonthlyBalances`, `RunReconciliation`, `ExplainReconciliationDifference`, `ApproveDepreciationProposal`, `SendReplenishmentProposal`, `RunSaftChecks`, `GenerateIntrastat`, `RegisterIntrastatSubmission`, `CreateIntrastatCorrection`, `SaveStockSettings`, `SaveAgingBuckets`, `SaveIntrastatThreshold`, `SaveSaftMovementTypeMap`.

## 7. Schema SQL `inv` (partea S7)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.StockMonthlyBalance` | `Id bigint` | UX `(CompanyId, PeriodYear, PeriodMonth, WarehouseId, DepotKey, ItemVariantId, LotKey)` | `(CompanyId, PeriodYear, PeriodMonth, AccountCode) INCLUDE (ClosingQty, ClosingValue)` |
| `inv.ReconciliationRun`, `inv.ReconciliationLine` | `Id int`; `Id bigint` | UX `(CompanyId, AtDate, RunKind)`; FK `ReconciliationRunId`, `WarehouseId` | `(ReconciliationRunId, IsExplained)` |
| `inv.AgingBucket` | `Id int` | UX `(CompanyId, FromDays)`; `CHECK (DepreciationPct BETWEEN 0 AND 100)` | — |
| `inv.ItemWarehouseSettings` | `Id int` | UX `(WarehouseId, ItemVariantId)`; `CHECK (MaxQty IS NULL OR MinQty IS NULL OR MaxQty >= MinQty)` | — |
| `inv.ItemTradeInfo` | `Id int` | UX `(ItemId)` (pe model, moștenit de variante); `OriginCountryCode char(2)`, `SupplementaryFactor` (UM suplimentară însăși vine din `core.CnCode` (precizare din Achiziții A6, 27.11.2026)) | — |
| `inv.SaftMovementTypeMap` | `Id int` | UX `(MovementTypeCode, ReasonCodeKey, ValidFrom)` | — |
| `inv.IntrastatThreshold` | `Id int` | UX `(Year, Flow)` | — |
| `inv.PartnerIntrastatDefault` | `Id int` | UX `(CompanyId, PartnerId)` | — |
| `inv.IntrastatDeclaration` | `Id int` | UX `(CompanyId, PeriodYear, PeriodMonth, Flow, Version)`; FK `CorrectsDeclarationId`, `FileAttachmentId` | `(CompanyId, Status)` |
| `inv.IntrastatLine` | `Id bigint` | FK `IntrastatDeclarationId` | `(IntrastatDeclarationId)` |
| `inv.IntrastatLineSource` | `Id bigint` | FK `IntrastatLineId`, `StockMovementId` | `(StockMovementId)` |
| ~~`inv.ReportView`, `inv.ReportSchedule`~~ | — | Mutate în Nucleu ca `core.ReportView` și `core.ReportSchedule` (precizare din Achiziții A7, 27.11.2026) | — |
| `inv.StockSettings` (coloane noi) | — | `NoMovementDays smallint NOT NULL DEFAULT 180`, `ReconciliationTime time(0) NOT NULL DEFAULT '02:00'`, `IntrastatTransportPct decimal(5,2) NOT NULL DEFAULT 3` | — |
| `inv.MovementReason` (coloană nouă) | — | `SaftMovementCode varchar(10) NULL` (suprascrie codul tipului) | — |

Exemplu de tabel complet:

```sql
CREATE TABLE inv.IntrastatLine (
    Id                      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntrastatLine PRIMARY KEY,
    IntrastatDeclarationId  int            NOT NULL CONSTRAINT FK_IntrastatLine_Declaration REFERENCES inv.IntrastatDeclaration(Id),
    LineNo                  int            NOT NULL,
    CnCode                  char(8)        NOT NULL,
    PartnerCountryCode      char(2)        NOT NULL,      -- țara de expediere (introduceri) sau de destinație (expedieri)
    OriginCountryCode       char(2)        NULL,          -- obligatorie la introduceri
    PartnerVatNumber        varchar(20)    NULL,          -- obligatoriu la expedieri
    TransactionNatureCode   varchar(3)     NOT NULL,
    TransportModeCode       char(1)        NOT NULL,
    DeliveryTermsCode       char(3)        NOT NULL,      -- EXW, FCA, DAP...
    NetMassKg               decimal(19,3)  NOT NULL,
    SupplementaryQty        decimal(19,3)  NULL,
    InvoicedValueRon        decimal(19,2)  NOT NULL,
    StatisticalValueRon     decimal(19,2)  NOT NULL,
    IsProvisionalValue      bit            NOT NULL CONSTRAINT DF_IntrastatLine_Provisional DEFAULT (0),
    CreatedAt               datetime2(3)   NOT NULL CONSTRAINT DF_IntrastatLine_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy               int            NOT NULL,
    ModifiedAt              datetime2(3)   NULL,
    ModifiedBy              int            NULL,
    RowVersion              rowversion     NOT NULL,
    CONSTRAINT UQ_IntrastatLine_No UNIQUE (IntrastatDeclarationId, LineNo),
    CONSTRAINT CK_IntrastatLine_Cn CHECK (CnCode NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_IntrastatLine_Values CHECK (NetMassKg >= 0 AND InvoicedValueRon >= 0 AND StatisticalValueRon >= 0)
);
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_StockMonthlyBalance_Build` | Scrie soldurile lunii (și ale lunilor următoare, la recalculare) | Nu |
| `inv.usp_StockBalance_Report` | Balanța de stocuri pe perioadă, din soldurile lunare și mișcări | Da (citire) |
| `inv.usp_StockAtDate_Get` | Stocul la o dată (regula 2) | Da (citire) |
| `inv.usp_Reconciliation_Run` | Concordanța pe cont și gestiune, cu cauzele | Nu |
| `inv.usp_StockAging_Report` | Vechimea pe straturi și intervale, articolele fără mișcare | Da (citire) |
| `inv.usp_StockBelowMin_Report` | Stocul sub minim, cu disponibilul proiectat (S5) | Da (citire) |
| `inv.usp_Saft_ReadWarehouses`, `_ReadProducts`, `_ReadPhysicalStock`, `_ReadMovements` | Citirile paginate pentru D406 stocuri | Nu |
| `inv.usp_Intrastat_Build` | Agregarea liniilor Intrastat din mișcări și facturi | Nu |

Coduri de eroare (plaja S7 `54800–54899`, prefix API `INV_`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 54801 | `INV_RECONCILIATION_UNEXPLAINED` | 409 | Închiderea lunii cu diferențe de concordanță neexplicate, fără drept |
| 54802 | `INV_SAFT_CHECKS_FAILED` | 422 | Generarea D406 stocuri cu verificări blocante nerezolvate |
| 54803 | `INV_SAFT_MOVEMENT_CODE_MISSING` | 422 | Tip de mișcare sau motiv cu mișcări în perioadă, fără cod ANAF |
| 54804 | `INV_INTRASTAT_CN_MISSING` | 422 | Linie Intrastat fără cod NC8 valid |
| 54805 | `INV_INTRASTAT_VAT_MISSING` | 422 | Expediere fără codul de TVA al partenerului |
| 54806 | `INV_INTRASTAT_PERIOD_OPEN` | 409 | Generare pentru o lună neînchisă operativ, fără confirmare |
| 54807 | `INV_INTRASTAT_ALREADY_SUBMITTED` | 409 | Modificarea unei declarații depuse (se face corecție) |
| 54808 | `INV_SETTING_LOCKED` | 409 | Schimbarea unei setări care cere luna curentă închisă sau fără stoc (de exemplu metoda de descărcare la amănunt) |
| 54809 | `INV_AGING_BUCKETS_INVALID` | 422 | Intervale de vechime care se suprapun sau lasă goluri |
| 54810 | `INV_REPORT_RECIPIENT_FORBIDDEN` | 403 | Trimitere programată către un utilizator fără dreptul raportului |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterea:

- Abatere de la `SQL-11` pentru `inv.StockMonthlyBalance`: fără `RowVersion`, se rescrie doar de job, niciodată din ecran. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Report.View` | Rapoartele de stoc (filtrate pe gestiunile la care utilizatorul are acces, S1) |
| `Inv.Report.ViewValue` | Valorile și costurile (fără el, rapoartele arată doar cantități) |
| `Inv.Report.Schedule` | Trimiterea programată și vizualizările partajate |
| `Inv.Reconciliation.Explain` | Explicarea diferențelor de concordanță |
| `Inv.Reconciliation.Override` | Închiderea lunii cu diferențe neexplicate |
| `Inv.Depreciation.Approve` | Aprobarea propunerii de depreciere |
| `Inv.Declaration.Saft` | Verificările și cererea D406 stocuri |
| `Inv.Declaration.Intrastat` | Generarea, depunerea și corecția Intrastat |
| `Inv.Settings.Manage` | Grupul Stocuri din Configurare (fiecare tab poate fi dat separat) |

Roluri livrate: **Contabil stocuri** (`View`, `ViewValue`, `Schedule`, `Explain`, `Saft`, `Intrastat`), **Contabil șef** (toate), **Director economic** (`View`, `ViewValue`, `Depreciation.Approve`), **Șef depozit** (`View`, fără `ViewValue`), **Achiziții** (`View` pe stocul sub minim), **Administrator** (`Settings.Manage`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S7 |
| --- | --- |
| Legea 82/1991; OMFP 1802/2014 | Concordanța evidenței tehnico-operative cu contabilitatea; balanța de stocuri ca evidență analitică |
| Codul de procedură fiscală, art. 59^1; OPANAF 1783/2021 și modificările | D406 SAF-T; stocurile la cererea ANAF, pe perioada cerută |
| Regulamentul (UE) 2019/2152 și normele INS | Intrastat: praguri anuale, conținutul liniei, termenul (de regulă ziua 15 a lunii următoare), corecțiile |
| Nomenclatura combinată (Regulamentul CEE 2658/87, actualizat anual) | Codurile NC8 din articole; lista se actualizează anual ca date |
| IAS 2 / OMFP 1802/2014 (ajustări pentru depreciere) | Propunerea din vechimea stocului; nota rămâne la C2 |

Integrări: C4 (D406, DUKIntegrator), INS (fișierul XML Intrastat, încărcat manual în aplicația online), Nucleu (joburi, e-mail, export), Achiziții și Vânzări (valori facturate, termeni de livrare), C2/C3 (balanța, închiderea), Raportare (citiri prin `IStockReportQuery`).

## 10. Rapoarte

Rapoartele sunt chiar obiectul acestei părți (3.1). Pe lângă ele:

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Istoricul declarațiilor | D406 stocuri și Intrastat pe perioade: stare, fișier, recipisă, corecții | Contabil șef |
| Cumulul Intrastat | Pe flux, lună cu lună, față de prag | Contabil stocuri |
| Auditul setărilor de stoc | Cine a schimbat ce setare, când, de la ce valoare la ce valoare | Auditor, Administrator |

## 11. Ecrane principale

Machetele (HTML + PNG) sunt în [ecrane/s7-rapoarte/](ecrane/s7-rapoarte/). Rapoartele și declarațiile sunt în meniul Stocuri › Rapoarte; configurarea e în Administrare › Configurare › Stocuri.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Balanța de stocuri | Pe cont și gestiune, sold inițial, intrări, ieșiri, sold final, cu concordanța față de contabilitate | `s7-01-balanta-stocuri` |
| Stocul la o dată | Stocul la 01.08.2026 pe SKU, cu cantitatea, metoda, costul și valoarea de la acea dată | `s7-07-stoc-la-data` |
| Stocul pe interval | Pe SKU: stoc inițial, mișcările din interval (desfășurate la APA05), stoc final | `s7-08-stoc-pe-interval` |
| Fișa de magazie | APA05 în DEP-CJ: fiecare mișcare cu lotul, documentul și stocul după ea | `s7-09-fisa-de-magazie` |
| Vechimea stocului | Intervalele de vechime, articolele fără mișcare, propunerea de depreciere | `s7-02-vechimea-stocului` |
| D406 stocuri | Notificarea ANAF, verificările pe categorii, volumul și generarea prin C4 | `s7-03-d406-stocuri` |
| Intrastat | Cumulul față de prag, liniile introducerilor din noiembrie, valoarea statistică | `s7-04-intrastat` |
| Configurare › Stocuri › Generale | Setările firmei, cu suprascrierile și auditul | `s7-05-configurare-generale` |
| Configurare › Stocuri › Documente | Motivele de încărcare și descărcare, cu contare, TVA, aprobare și cod SAF-T | `s7-06-configurare-motive` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S7-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S7-A1 | Pentru orice lună închisă, rulajele din `inv.StockMonthlyBalance` sunt egale cu suma mișcărilor, iar soldul final = sold inițial + intrări − ieșiri | Test de proprietate |
| S7-A2 | Stocul la o dată din trecut, calculat din soldul lunar + mișcări, e egal cu cel calculat din toate mișcările de la început | Test de proprietate |
| S7-A3 | O mișcare retroactivă în septembrie rescrie soldurile din septembrie până în luna curentă | Test de integrare (cu S3) |
| S7-A4 | Concordanța găsește nota manuală pe `357` fără mișcare și blochează închiderea lunii (`54801`) până la explicare | Test de integrare (cu C2, C3) |
| S7-A5 | Balanța de stocuri pe 100.000 de SKU și 20 de gestiuni, pe o lună, se afișează în sub 5 secunde | Test de performanță |
| S7-A6 | D406 stocuri pe setul de test trece validatorul ANAF fără erori; un an cu 5 milioane de mișcări se generează cu cel mult 1 GB memorie | Test de integrare (cu C4, DUKIntegrator) |
| S7-A7 | Intrastat: introducerile din noiembrie dau 3 linii agregate corect pe NC8, țări, natură, transport și livrare; valoarea statistică la EXW include transportul estimat | Test unitar |
| S7-A8 | Expedierea fără codul de TVA al partenerului e refuzată (`54805`); factura sosită după depunere marchează declarația „De corectat” | Test de integrare |
| S7-A9 | Un raport fără `Inv.Report.ViewValue` nu întoarce valori nici prin export, nici prin trimitere programată | Test de securitate |
| S7-A10 | Schimbarea unei setări intră în audit cu valoarea veche și cea nouă; `RetailDischargeMethod` nu se schimbă în mijlocul unei luni cu vânzări (`54808`) | Test de integrare |
| S7-A11 | Fișa de magazie: soldul după ultima mișcare e egal cu stocul la sfârșitul perioadei, iar stocul pe interval: stoc inițial + intrări − ieșiri = stoc final, pe fiecare SKU | Test de proprietate |

Criteriile S7-A1, S7-A2 și S7-A4 sunt critice: rapoartele și declarațiile trebuie să bată cu registrul și cu contabilitatea.

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Vederea publicată** `inv.vw_StockMovementFact` (v1): mișcarea pe SKU, gestiune, lot, cantitate, valoare, tip de mișcare, document sursă; sursa setului de date „Mișcări de stoc” și a instantaneului nocturn „Stoc pe zi”. Index columnstore neclusterizat pe `inv.StockMovement`. [BI2 §9](../13-raportare-bi/BI2-DEPOZIT-DATE.md)
- **Tabloul livrat „Depozitul”** (rolul Șef depozit, și pe ecranul mare din depozit) folosește stocul pe gestiune, stocul sub minim, stocul fără mișcare, livrările și retururile zilei.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Cum se calculează rapoartele la o dată din trecut | Trei rapoarte de bază: stocul la o dată (cantitate și valoare de la acea dată), stocul pe interval (stoc inițial, mișcările din interval, stoc final) și fișa de magazie (fiecare mișcare cu soldul după ea). Toate pornesc din soldurile lunare salvate la închiderea lunii, plus mișcările de după; soldurile se refac automat la mișcările retroactive | 2 (regula 2), 3.1, 3.2 | Decis (26.11.2026) |
| 2 | Concordanța stoc–contabilitate | Zilnic (job la 02:00) și la închiderea lunii, pe cont și gestiune, cu cauzele găsite automat; închiderea lunii e blocată de diferențele neexplicate, cu excepția dreptului special | 3.3 | Decis (26.11.2026) |
| 3 | Vechimea stocului și deprecierea | Vechimea pe straturile de cost (FIFO, LIFO) sau pe intrările neconsumate (CMP), pe intervale configurabile cu procent propus; propunerea merge la C2 doar după aprobarea Directorului economic | 3.4 | Decis (26.11.2026) |
| 4 | Stocul sub minim | S7 doar raportează și propune cantitatea până la maxim, pe baza disponibilului proiectat; Achiziții decide comanda | 3.5 | Decis (26.11.2026) |
| 5 | D406 stocuri | Stocuri furnizează secțiunea prin contractul din C4, iar C4 generează, validează și depune; codurile ANAF ale tipurilor de mișcare sunt date livrate, suprascrise pe motiv | 3.6 | Decis (26.11.2026) |
| 6 | Intrastat | În Stocuri, din mișcările cu parteneri UE și valorile facturilor, cu urmărirea pragurilor și avertizare la 90%; valoarea statistică din costul real de transport sau, dacă lipsește, dintr-un procent estimat (3%) | 3.7 | Decis (26.11.2026) |
| 7 | Rapoartele programate și vizualizările salvate | Da: vizualizări personale sau partajate pe rol, trimitere pe e-mail în PDF sau Excel doar către cei care au dreptul raportului; valorile se ascund fără dreptul de valoare | 3.1, 8 | Decis (26.11.2026) |
| 8 | Configurare › Stocuri | Grup „Stocuri” în Administrare › Configurare, ca la Finanțe, cu 6 tab-uri; normele de perisabilitate și grupele de confuzie rămân în meniul Inventar, cu legătură din configurare | 3.8 | Decis (26.11.2026) |

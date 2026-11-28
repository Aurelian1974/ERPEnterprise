# ERP · 02.04 Achiziții · A2 Necesar și oferte: referate, necesar de aprovizionare, cereri de ofertă, oferte și atribuire

26 noiembrie 2026 · Aurelian · **✓ Validat (26.11.2026)**, cu ecranele și toate cele 13 decizii. A doua din cele 7 părți ale modulului 02.04 Achiziții. Se sprijină pe [A1 Configurare](A1-CONFIGURARE.md) (validat pe 26.11.2026): tipurile de documente, tranzacțiile livrate, grupurile de consum, catalogul furnizorului, prețurile furnizorilor, setările firmei și achizitorii responsabili.

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`; partea A2 folosește `53100–53199`. Această parte acoperă tot ce se întâmplă **înainte de comandă**: cererea unui departament (referatul de necesitate) și aprobarea ei pe praguri, calculul necesarului de aprovizionare din referate, din stocul sub minim și din MRP, cererile de ofertă trimise la mai mulți furnizori, înregistrarea ofertelor primite, compararea lor și atribuirea pe linii. Comanda la furnizor însăși e în A3. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.3.1 Variante, §3.6 Tranzacții și legături, fluxul 4.7 Notificări), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F5, tiparul aprobărilor pe praguri), [02.05 Stocuri](../05-stocuri/README.md) (S1 pragurile de stoc, S5 disponibilul proiectat). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Liniile de document stau pe variantă** (`core.ItemVariant`, SKU), niciodată pe model. Excepție introdusă de A2: linia de **referat** poate fi „necodificată” (doar o descriere), pentru lucruri pe care firma nu le-a mai cumpărat. Achizitorul o codifică înainte de comandă (regula 3). Comanda (A3) cere mereu varianta.
- **Tipul de tranzacție** (`core.TransactionType`) spune cum a apărut un document; **legăturile între documente** (`core.DocumentLink`) țin, pe linie, cantitatea preluată dintr-o linie sursă, pe un **grup de consum**. Disponibilul unei linii sursă = cantitatea ei − legăturile în ciornă și active − cantitatea închisă manual (Nucleu §3.6.2). Tranzacțiile A2 sunt deja livrate de A1 §3.2: `RF-MAN`, `NC-GEN`, `CO-MAN`, `CO-NC`, `OF-MAN`, `OF-CO`, plus `CF-OF` și `CF-NC` (comanda, detaliată în A3).
- Grupurile de consum folosite aici: `NEED` (referat → necesar), `RFQ` (necesar → cerere de ofertă, informativ), `QUOTE` (cerere de ofertă → ofertă, informativ, propus aici) și `ORDER` (ofertă sau necesar → comandă). „Informativ” înseamnă că legătura se scrie, pentru drumul documentului, dar nu limitează cantitatea (9, completare la Nucleu).
- **Disponibilul proiectat** e calculat de Stocuri (S5, `inv.usp_Availability_Projected`): disponibilul de azi plus intrările așteptate (comenzi la furnizor confirmate, transferuri, producție) minus cererile, pe zile. Pragurile `MinQty`, `MaxQty`, `ReorderQty` sunt în Stocuri (S1, `inv.ItemWarehouseSettings`, cu precedența variantă în gestiune → model în gestiune → firmă).
- Aprobările urmează tiparul validat în F5 (propunerile de plată): reguli pe niveluri 1–3 cu prag în lei, nivelurile se aprobă în ordine, autorul nu aprobă, același om nu aprobă două niveluri, decizia e jurnalizată doar prin adăugare. A2 le aplică referatelor; A3 aceleași tabele, comenzilor.
- Nimic din A2 nu mișcă stocul și nu se contează. Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Cantitățile și prețurile se țin pe `decimal(19,6)`, valorile pe `decimal(19,4)` (`SQL-07`).
- **Ce NU face A2:** nu emite, nu trimite și nu aprobă comanda (A3); nu primește marfa (A4); nu creează articole noi (Nucleu, cu dreptul de creare a articolelor); nu are portal pentru furnizori (viitor).

## 1. Scop și perimetru

| Zonă | Ce face A2 | Ce NU face (și cine face) |
| --- | --- | --- |
| Referate de necesitate | Cererea unui departament sau a unei gestiuni, cu linii codificate sau necodificate, preț estimat, centru de cost, data necesară; trimiterea la aprobare | Consumul efectiv din stoc al celor cumpărate (Stocuri, bon de consum) |
| Aprobări | Regulile de aprobare pe praguri valorice și pe niveluri, comune referatelor și comenzilor; aprobarea, returnarea, respingerea, aprobarea parțială pe linii | Aprobarea comenzii (A3, cu aceleași reguli); aprobarea plăților (F5) |
| Necesar de aprovizionare | Calculul propunerilor de cumpărare din referatele aprobate, din stocul sub minim (pe disponibilul proiectat) și din necesarul MRP; alegerea furnizorului, a prețului și a achizitorului; decizia pe linie: comandă, cerere de ofertă sau amânare | Calculul MRP din rețete și planul de producție (02.08); pragurile de stoc (S1) |
| Cereri de ofertă | Cererea către unul sau mai mulți furnizori, trimisă pe email cu PDF și cu un șablon Excel de răspuns; urmărirea răspunsurilor | Trimiterea emailului (Nucleu, outbox); portal de furnizori (viitor) |
| Oferte furnizori | Înregistrarea ofertei primite (manual sau din șablonul Excel), cu prețuri pe praguri, termene, transport, valabilitate, produse alternative | Lista de prețuri permanentă (A1 §3.6; se poate crea dintr-o ofertă) |
| Comparare și atribuire | Compararea ofertelor pe linii, la un preț comparabil în lei; atribuirea pe linii, inclusiv împărțită între furnizori, cu motiv când nu se alege cea mai bună ofertă | Emiterea comenzii din atribuire (A3, tranzacția `CF-OF`) |

## 2. Model de domeniu

```
 Referat de necesitate (PurchaseRequest, RF)        solicitant, centru de cost, gestiune, dată necesară
   └─ PurchaseRequestLine × N                        variantă SAU descriere necodificată; solicitat, aprobat, preț estimat
        └─ PurchaseApproval × N (jurnal)              nivel, aprobator, decizie (după PurchaseApprovalRule)

 Necesar de aprovizionare (PurchaseNeed, NC)         rezultatul unui calcul, pe firmă și filtre
   └─ PurchaseNeedLine × N                           variantă × gestiune × origine; furnizor, preț, achizitor, acțiune
        ├─ ← referat aprobat (NEED)                  origine 1
        ├─ ← stoc sub minim (instantaneu de calcul)  origine 2
        └─ ← MrpDemand (din evenimentul NecesarCalculat)  origine 3

 Cerere de ofertă (RequestForQuotation, CO)
   ├─ RfqLine × N  ← linie de necesar (RFQ, informativ)
   └─ RfqSupplier × N                                furnizorii invitați: trimis, ofertă primită, refuz, fără răspuns
 Ofertă furnizor (SupplierQuotation, OF)             una activă pe furnizor și cerere
   └─ SupplierQuotationLine × N ← RfqLine (QUOTE, informativ); praguri, alternative
 Atribuire (RfqAward × N)                            linie de cerere → linie de ofertă aleasă, cantitate, preț comparabil, motiv
        └─→ Comandă la furnizor (A3, CF-OF: ORDER pe linia de ofertă și, indirect, pe linia de necesar)
```

Reguli de business ale modelului:

1. **Referatul cere, necesarul decide, comanda angajează.** Referatul exprimă nevoia unui departament și se aprobă; nu alege furnizorul și nu are efect în stoc. Necesarul e lucrul achizitorului: adună nevoile, alege furnizorul și decide drumul (comandă directă sau cerere de ofertă). Doar comanda (A3) angajează firma față de furnizor.
2. **Aprobarea se face pe valoarea estimată în lei.** Fiecare linie de referat are un preț unitar estimat. Pe liniile codificate e propus automat (4.1); pe cele necodificate îl scrie solicitantul și e obligatoriu (`53101`). Pragul se compară cu totalul estimat sau cu cea mai mare linie, după regulă.
3. **O linie necodificată se codifică înainte de comandă.** Pe referat rămâne descrierea; achizitorul alege varianta pe linia de necesar sau pe linia de ofertă acceptată (cu un articol existent, cu unul nou creat în Nucleu, ori cu un articol generic nestocabil al firmei, de exemplu „Echipamente diverse”). Comanda nu acceptă linii fără variantă (`53107`, `53116`). Este singurul caz în care o linie generată își poate primi varianta după generare, pentru că sursa nu avea una.
4. **Liniile din referate și cele din stoc minim nu se compensează.** Un referat aprobat e o nevoie explicită și se cumpără ca atare. Propunerea din stoc minim se calculează separat, din disponibilul proiectat. Pentru aceeași variantă și gestiune pot exista două linii de necesar, cu origini diferite; comanda generată le poate uni pe o singură linie (A3).
5. **Necesarul nu dublează ce e deja pe drum.** Calculul din stoc minim scade ce urmează să intre: comenzile confirmate (deja în proiectatul S5), dar și comenzile trimise neconfirmate, ciornele de comandă și liniile de necesar validate necomandate încă.
6. **O variantă are o singură metodă de reaprovizionare**: fără propunere automată, min/max sau MRP (completare la S1, 9). Așa nu se cumpără de două ori aceeași cantitate, o dată din stoc minim și o dată din MRP.
7. **Cererea de ofertă e una pentru toți furnizorii invitați.** Fiecare furnizor răspunde cu cel mult o ofertă activă pe cerere (o ofertă nouă o înlocuiește pe cea veche, ca versiune, `53112`). Grupurile `RFQ` și `QUOTE` sunt informative: un furnizor poate oferta altă cantitate decât cea cerută, iar trei furnizori ofertează fiecare toată cantitatea.
8. **Atribuirea e pe linie și se poate împărți.** Pentru fiecare linie a cererii se alege una sau mai multe linii de ofertă, cu cantitatea fiecăreia (de exemplu 70% la un furnizor, 30% la altul). Suma atribuită nu trece de cantitatea cerută plus toleranța tranzacției `CF-OF` (`53115`).
9. **Abaterea de la cea mai bună ofertă se justifică.** Dacă nu se alege oferta cu cel mai mic preț comparabil (sau cu cel mai bun scor, după setarea firmei) ori dacă s-au primit mai puține oferte decât minimul cerut, motivul e obligatoriu (`53114`) și rămâne în atribuire, ca pistă de audit.
10. **Nimic nu se șterge după validare** (`SQL-20`): un referat sau o ofertă validată se anulează sau se închide, cu motiv, în `core.DocumentStatusLog`.

## 3. Entități, stări și setări

Convențiile comune tabelelor `purch` sunt cele din A1 §3: `Id` `bigint IDENTITY` pentru documente și linii (`SQL-13`), audit (`SQL-19`), `RowVersion` pe tot ce se editează (`SQL-11`, `SP-18`). Numerele vin din `core.NumberSeries` (seriile `RF`, `NC`, `CO`, `OF`, A1 §3.1).

### 3.1 Referatul de necesitate (`PurchaseRequest`, seria `RF`)

**Antet**

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `Number`, `DocDate` | | Numărul se alocă la trimiterea spre aprobare, nu la ciornă (Nucleu fluxul 4.2), ca seria să nu aibă goluri din ciornele abandonate |
| `TransactionTypeId` | `int` | `RF-MAN` sau o variantă a ei (A1 §3.2) |
| `RequesterUserId` | `int` | Solicitantul. Implicit utilizatorul curent; un utilizator cu `Purch.Request.ViewAll` poate crea în numele altuia |
| `CostCenterId`, `ProjectId` | `int NULL` | Valori de dimensiune (Nucleu `DimensionValue`). Centrul de cost decide aprobatorul de nivel 1 când regula cere „responsabilul centrului de cost” (3.2). Propuse pe linii |
| `WarehouseId` | `int NULL` | Gestiunea în care trebuie să ajungă bunurile stocabile. Implicit gestiunea solicitantului sau a centrului de cost |
| `NeededBy` | `date` | Data la care e nevoie; propusă pe linii |
| `Priority` | `tinyint` | 1 Normală · 2 Urgentă (apare cu roșu la achizitor și în necesar) |
| `Justification` | `nvarchar(1000)` | Obligatorie peste pragul nivelului 2 (`53101`) |
| `Status` | `tinyint` | 3.1.1 |
| `EstimatedValueRon` | `decimal(19,4)` | Σ cantitate aprobată (sau solicitată, înainte de aprobare) × preț estimat, în lei, calculat de handler (`SP-13`) |
| `RequiredLevel`, `CurrentLevel` | `tinyint` | Nivelul de aprobare cerut (0 = aprobat automat) și nivelul care trebuie să decidă acum; calculate la trimitere |
| `SubmittedAt`, `ApprovedAt`, `ClosedAt` | `datetime2(3) NULL` | — |

**Linie (`PurchaseRequestLine`)**

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `LineNo` | `smallint` | — |
| `ItemVariantId` | `int NULL` | Varianta (SKU). Gol = linie necodificată. Un model cu variante cere varianta, ca pe orice document (Nucleu §3.3.1) |
| `Description` | `nvarchar(300) NULL` | Obligatorie pe linia necodificată (CK); pe cea codificată, detalii opționale |
| `ItemKind` | `tinyint` | Pe linia necodificată: 1 Bun stocabil · 2 Obiect de inventar sau bun nestocabil · 3 Serviciu · 4 Imobilizare. Ajută la alegerea achizitorului și a articolului. Pe linia codificată se ia din articol |
| `UomId` | `int` | Din setul de UM al articolului; pe linia necodificată, orice UM activă din Nucleu |
| `Qty`, `ApprovedQty` | `decimal(19,6)` | Solicitată; aprobată (≤ solicitată, `53105`), completată la aprobare cu valoarea solicitată și micșorabilă de aprobator |
| `EstimatedUnitPrice`, `EstimatedPriceSource` | `decimal(19,6)`, `tinyint` | Preț fără TVA, în lei. Sursa: 1 Lista furnizorului preferat · 2 Ultimul preț de achiziție · 3 Costul mediu din stoc (S3) · 4 Scris de solicitant (4.1) |
| `SuggestedPartnerId` | `int NULL` | Furnizorul sugerat de solicitant (informativ) |
| `NeededBy`, `WarehouseId`, `CostCenterId`, `ProjectId` | | Din antet, modificabile pe linie |
| `LineStatus`, `RejectReason` | `tinyint`, `nvarchar(300)` | 1 Activă · 2 Respinsă la aprobare (cu motiv). Liniile respinse nu intră în necesar |

#### 3.1.1 Stările referatului

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează liber | → De aprobat (trimitere), → ștergere fizică doar dacă n-a fost niciodată trimis |
| 2 De aprobat | Blocat la editare (`53102`); aprobatorii nivelului curent primesc notificare | → Aprobat (ultimul nivel), → Ciornă (retras de autor sau returnat de aprobator, cu motiv; aprobările anterioare se pierd), → Respins |
| 3 Aprobat | Liniile aprobate apar în necesar | → Închis, → Anulat (doar dacă nicio linie nu e încă în necesar) |
| 4 Închis | Toate liniile aprobate au fost preluate în necesar (`NEED` consumat) sau închise manual | — |
| 7 Respins | Final, cu motiv | — |
| 9 Anulat | Final, cu motiv | — |

**Starea pe linie, văzută de solicitant.** Pe referat fiecare linie arată unde a ajuns, calculat din legături (Nucleu `GetTrailAsync`), nu stocat: aprobată → în necesar `NC 0418` → la ofertă `CO 0057` → comandată `CF 2297` → recepționată `NIR 4501` (parțial sau integral). Solicitantul primește notificare la aprobare, la respingere, la comandă și la recepție (5.1).

### 3.2 Regulile de aprobare (`PurchaseApprovalRule`) și jurnalul aprobărilor (`PurchaseApproval`)

Aceleași tabele servesc referatele (A2) și comenzile (A3), deosebite prin `DocumentType`. Tiparul e cel din F5 §3.2, cu două adăugiri: aprobatorul poate fi responsabilul centrului de cost, iar o regulă poate fi restrânsă la un centru de cost.

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int` | — |
| `DocumentType` | `varchar(40)` | `PurchaseRequest`, `PurchaseOrder`, `SupplierInvoice` (factura fără comandă) sau `SupplierInvoiceDifference` (diferențele acceptate), ultimele două adăugate de A5 |
| `Level` | `tinyint` | 1, 2, 3 |
| `ThresholdRon` | `decimal(19,4)` | De la ce valoare se cere nivelul. Crescător pe niveluri (`53106`) |
| `Basis` | `tinyint` | 1 Valoarea documentului · 2 Cea mai mare linie |
| `CostCenterId` | `int NULL` | Gol = regula generală a nivelului; completat = regula acelui centru de cost, cu prioritate față de cea generală pe același nivel |
| `ApproverKind` | `tinyint` | 1 Un rol din firmă · 2 Utilizatori anume · 3 Responsabilul centrului de cost al documentului (`DimensionValue.ResponsibleUserId`) |
| `ApproverRoleId`, `ApproverUserIds` | | După `ApproverKind`; lista de utilizatori e un tabel copil (`PurchaseApprovalRuleUser`) |
| `MinApprovers` | `tinyint` | Implicit 1 (câți din lista nivelului trebuie să aprobe) |
| `IsActive` | `bit` | O regulă schimbată nu afectează documentele deja trimise (nivelul se calculează la trimitere) |

Exemplul livrat pentru referate (se adaptează pe firmă, decizia 3): nivelul 1 de la 0 lei, responsabilul centrului de cost; nivelul 2 de la 5.000 lei, rolul Șef achiziții; nivelul 3 de la 50.000 lei, rolul Director general.

`PurchaseApproval` (`bigint`): `DocumentType`, `DocumentId`, `Level`, `UserId`, `Decision` (1 Aprobat · 2 Returnat · 3 Respins), `DecidedAt`, `Comment`, `ChangedLinesJson` (liniile cu cantitatea micșorată sau respinse la această decizie: linie, cantitate veche, cantitate nouă, motiv; validat cu `ISJSON`, `SQL-10`). Doar adăugare, cu trigger care refuză `UPDATE` și `DELETE` (`SP-21`).

Reguli:

1. **Nivelul cerut** = cel mai mare nivel al cărui prag e atins de valoare (după `Basis`). Dacă nicio regulă nu e atinsă (sau firma nu are reguli active pentru tipul de document), documentul e aprobat automat la trimitere, cu `RequiredLevel = 0` și mențiunea „aprobat automat, sub pragul nivelului 1” în istoric.
2. Nivelurile se aprobă în ordine, de la 1 la nivelul cerut; nivelul 2 nu vede documentul înainte de aprobarea nivelului 1.
3. Autorul nu aprobă la niciun nivel; același utilizator nu aprobă două niveluri ale aceluiași document (`53103`). Dacă responsabilul centrului de cost e chiar solicitantul, nivelul 1 trece la înlocuitorul lui din Nucleu sau, fără înlocuitor, la nivelul următor existent, iar istoricul arată de ce.
4. Aprobatorul poate micșora `ApprovedQty` sau respinge linii (cu motiv), dar nu poate mări cantități și nu poate adăuga linii. Valoarea se recalculează; nivelul cerut **nu scade** după trimitere (un referat redus sub prag tot trece prin nivelurile calculate inițial).
5. Returnarea readuce documentul în ciornă; la retrimitere aprobările anterioare se pierd și nivelul se recalculează.
6. Aprobatorii primesc notificare în aplicație și pe email (Nucleu fluxul 4.7); un document nedecis după `ApprovalReminderHours` (3.6, implicit 24) retrimite notificarea și apare în „De rezolvat” pe pagina Acasă. Înlocuitorii pe perioadă (Nucleu, concedii) primesc aceleași notificări.

### 3.3 Necesarul de aprovizionare (`PurchaseNeed`, seria `NC`)

Un necesar e rezultatul unui calcul (4.2), pe care achizitorul îl revizuiește și îl validează. Se pot face oricâte, de exemplu unul pe săptămână pentru fiecare achizitor.

**Antet**: `CompanyId`, `Number`, `DocDate`, `TransactionTypeId` (`NC-GEN`), `HorizonDate` (până când se acoperă nevoia), parametrii calculului (`ParamsJson`: gestiuni, grupe de articole, achizitor, surse incluse, `SQL-10`), `CalculatedAt`, `CalculatedBy`, `Status`, `ValidatedAt`, `ValidatedBy`.

**Linie (`PurchaseNeedLine`)**

| Câmp | Reguli |
| --- | --- |
| `ItemVariantId` (NULL), `Description` | Varianta; gol doar pentru liniile venite din referate necodificate, până la codificare |
| `WarehouseId`, `NeededBy` | Gestiunea de livrare și cea mai apropiată dată necesară dintre surse |
| `Origin` | 1 Referat · 2 Stoc sub minim · 3 MRP · 4 Adăugată manual |
| `QtyBase`, `PurchaseUomId`, `Qty` | Cantitatea în UM de bază și în UM de cumpărare a furnizorului ales (din catalog, A1 §3.5), rotunjită la minimul și multiplul lui |
| Instantaneul calculului | `ProjectedMinQty`, `ProjectedAtHorizonQty`, `MinQty`, `MaxQty`, `ReorderQty`, `OnOrderQty` (ce e deja pe drum, regula 5): valorile din momentul calculului, ca achizitorul să vadă de ce s-a propus cantitatea |
| `PartnerId`, `UnitPrice`, `CurrencyCode`, `DiscountPct`, `PriceSource` | Furnizorul propus și prețul lui (`ISupplierPriceProvider`, A1 §3.6) |
| `BuyerUserId` | Achizitorul responsabil (4.2 pasul 6) |
| `Action` | 1 Comandă · 2 Cerere de ofertă · 3 Amânată |
| `MrpDemandId` | Rândul MRP din care provine (origine 3) |
| `Notes` | — |

Legăturile cu referatele sunt `core.DocumentLink` pe grupul `NEED` (mai multe linii de referat pot alimenta o singură linie de necesar). Pentru originile 2 și 3 nu există document sursă: instantaneul și `MrpDemandId` țin urma.

**Stările necesarului**: 1 Ciornă (se poate recalcula; legăturile cu referatele sunt „în ciornă”, deci rezervă cantitatea) → 3 Validat (liniile „Amânată” se elimină la validare și referatele lor redevin disponibile; liniile rămase pot fi preluate în comenzi și cereri de ofertă) → 4 Închis (toate liniile consumate pe `ORDER` sau închise manual, `DocumentLineClosure`, cu motiv; solicitantul referatului e notificat). 9 Anulat doar din ciornă.

**MRP (`MrpDemand`).** Evenimentul `NecesarCalculat` (02.08) aduce, pentru fiecare rulare MRP a unei firme, rândurile de cumpărat: `MrpRunId`, `RunAt`, `ItemVariantId`, `WarehouseId`, `Qty` (UM de bază, deja net de stoc și de intrări), `NeededBy`, `PlannedOrderRef`. Se scriu în `purch.MrpDemand` cu starea 1 Activă. O rulare nouă trece rândurile active ale rulării anterioare în 2 Înlocuită (idempotent pe `MrpRunId`, iar un eveniment mai vechi decât ultima rulare primită se ignoră). Rândurile preluate într-un necesar validat trec în 3 Preluată.

### 3.4 Cererea de ofertă (`RequestForQuotation`, seria `CO`)

**Antet**: `CompanyId`, `Number`, `DocDate`, `TransactionTypeId` (`CO-MAN` sau `CO-NC`), `BuyerUserId`, `ResponseDueAt` (termenul de răspuns; implicit `DocDate` + `RfqResponseDays`, 3.6; nu în trecut, `53117`), `DeliveryWarehouseId`, `DeliveryDate` dorită, `CurrencyCode` dorită (opțional; furnizorul poate oferta în altă monedă), `Incoterm` dorit, `MessageToSuppliers`, `Language` implicită (`ro` sau `en`), atașamente (specificații, desene), `Status`.

**Linie (`RfqLine`)**: `ItemVariantId` (NULL pentru linii necodificate) sau `Description`, `UomId`, `Qty`, `NeededBy`, `Specification` (text pentru furnizor). Din `CO-NC`, legată de linia de necesar pe grupul `RFQ`.

**Furnizori invitați (`RfqSupplier`)**: `PartnerId` (furnizor, nu blocat: `53010`), `ContactEmail`, `Language`, `Status` (1 De trimis · 2 Trimisă · 3 Ofertă primită · 4 A refuzat · 5 Fără răspuns, setată automat la termen), `SentAt`, `RespondedAt`, `DeclineReason`, `QuotationId` (oferta activă). Un furnizor nou se adaugă întâi în Nucleu (fișa partenerului, completată din ANAF după CUI) cu rolul de furnizor.

**Stările cererii**: 1 Ciornă → 3 Trimisă (la primul furnizor trimis; se mai pot adăuga furnizori) → 5 Atribuită (atribuirea salvată, 3.5.1) → 4 Închisă (toate liniile atribuite au fost comandate sau închise). 9 Anulată (cu notificare pe email la furnizorii invitați, opțional).

**Trimiterea.** Pentru fiecare furnizor se generează un PDF (cu codurile și denumirile lui din catalog, când există, A1 §3.5) și **un fișier Excel de răspuns**, precompletat cu liniile cererii și cu coloanele de completat: preț unitar, reducere, cantitate minimă, termen de livrare, observații, alternativă. Emailul pleacă prin outbox (Nucleu fluxul 4.7, `SP-15`). Fișierul Excel are o foaie ascunsă cu identificatorul cererii și al furnizorului, semnată (HMAC cu cheia instalării), ca la import să se recunoască fără potrivire (4.4).

### 3.5 Oferta furnizorului (`SupplierQuotation`, seria internă `OF`)

**Antet**: `CompanyId`, `Number` (intern), `SupplierReference` (numărul ofertei furnizorului), `PartnerId`, `RfqId` (NULL pentru `OF-MAN`), `Version` (1, 2... când furnizorul revine cu altă ofertă la aceeași cerere; versiunea veche trece în „Înlocuită”), `QuoteDate`, `ValidUntil`, `CurrencyCode`, `PaymentTermDays`, `LeadTimeDays`, `Incoterm`, `IncotermPlace`, `FreightAmount` (transport și alte costuri facturate separat, în moneda ofertei; 0 = incluse), `MinOrderAmount`, `Notes`, atașamentul primit, `Status`.

**Linie (`SupplierQuotationLine`)**: `RfqLineId` (legătura `QUOTE`), `ItemVariantId` (NULL până la codificare) sau `Description`, `UomId`, `Qty` ofertată, `MinQty` (prag de cantitate, ca în lista de prețuri; mai multe linii pe același articol = praguri), `UnitPrice`, `DiscountPct`, `LeadTimeDays` (precizează antetul), `IsAlternative` (produs propus în locul celui cerut, legat de aceeași linie de cerere), `Notes`, `Decision` (0 Nedecisă · 1 Acceptată · 2 Respinsă).

**Stările ofertei**: 1 Ciornă → 3 Înregistrată → 5 Acceptată (cel puțin o linie acceptată) sau 6 Respinsă (nicio linie acceptată, la atribuirea cererii) → 4 Închisă (liniile acceptate preluate în comenzi sau închise). 8 Înlocuită (de o versiune nouă). 9 Anulată. „Expirată” nu e stare: e `ValidUntil < azi`, afișat pe ecran; o ofertă expirată nu se poate accepta (`53113`) decât după ce furnizorul confirmă prelungirea (se schimbă `ValidUntil`, cu atașament și audit).

#### 3.5.1 Atribuirea (`RfqAward`)

Un rând pe (linie de cerere, linie de ofertă aleasă): `RfqLineId`, `QuotationLineId`, `AwardedQty`, `ComparablePriceRon` (prețul comparabil al liniei alese, 4.5), `BestComparablePriceRon` și `BestQuotationLineId` (cea mai bună ofertă la momentul deciziei), `QuotesReceived`, `IsBest`, `Reason` (obligatoriu dacă `IsBest = 0` sau `QuotesReceived < MinQuotesCount` la o valoare peste `MinQuotesAboveRon`, `53114`), `DecidedBy`, `DecidedAt`. Rândurile sunt un instantaneu: o schimbare de curs ulterioară nu le modifică. Atribuirea se poate reface cât timp nicio comandă nu a fost generată din ea; după aceea se schimbă doar prin închiderea liniei de ofertă și o atribuire nouă.

### 3.6 Setările A2 (completare la `PurchaseSettings`, A1 §3.7)

Editate în Administrare › Configurare › Achiziții › Generale (`a1-06`), grupul nou „Necesar și oferte”, și în tab-ul Aprobări (`a2-06`) pentru reguli.

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `NeedReviewDays` | 7 | Cât de des se calculează necesarul; se adaugă la termenul de livrare al furnizorului pentru orizontul de acoperire (4.2) |
| `NeedDefaultWarehouseScope` | toate | Gestiunile incluse implicit în calcul |
| `RfqRequiredAboveRon` | 20.000,00 | O linie de necesar cu valoarea estimată peste prag primește implicit acțiunea „Cerere de ofertă” |
| `MinQuotesAboveRon`, `MinQuotesCount` | 20.000,00 lei, 3 | Peste prag (valoarea estimată a cererii), atribuirea cu mai puțin de 3 oferte primite cere motiv |
| `RfqResponseDays` | 5 | Termenul de răspuns propus pe cererea de ofertă, în zile lucrătoare |
| `QuoteComparisonBasis` | 1 | 1 Cel mai mic preț comparabil · 2 Scor ponderat |
| `QuoteWeightPrice`, `QuoteWeightLeadTime`, `QuoteWeightPaymentTerm`, `QuoteWeightRating` | 70, 15, 10, 5 | Ponderile scorului (suma 100, `53008`); folosite doar cu baza 2 |
| `ApprovalReminderHours` | 24 | Retrimiterea notificării de aprobare (3.2 regula 6) |

### 3.7 Metoda de reaprovizionare a variantei (completare la S1)

Coloană nouă `ReplenishmentMethod tinyint` în `inv.ItemStockSettings` (pe model, în firmă) și, opțional, în `inv.ItemWarehouseSettings` (pe variantă sau model, în gestiune), cu aceeași precedență ca pragurile (S1 §3.4): 0 Fără propunere automată · 1 Min/max (necesarul din A2) · 2 MRP (necesarul vine doar din `NecesarCalculat`). Implicit 1 dacă modelul are `MinQty` sau `MaxQty`, altfel 0. Stocuri deține coloana; Achizițiile și MRP doar o citesc (decizia 6).

## 4. Fluxuri principale

### 4.1 Referatul și aprobarea lui (exemplu complet)

Andreea Pop (Gestionar DEP-CJ) are nevoie de echipamente de protecție și de un transpalet nou. Centrul de cost „Logistică Cluj” are responsabil pe Gheorghe Ilie (Șef depozit). Firma Exemplu Distribuție are regulile livrate: nivelul 1 de la 0 lei (responsabilul centrului de cost), nivelul 2 de la 5.000 lei (Șef achiziții), nivelul 3 de la 50.000 lei.

1. Andreea creează referatul (tranzacția `RF-MAN`), centrul de cost Logistică Cluj, gestiunea DEP-CJ, necesar până la 05.12.2026, și adaugă liniile. Pe liniile codificate prețul estimat se propune în ordinea: lista furnizorului preferat (A1 §3.6) → ultimul preț de achiziție al variantei, de la orice furnizor → costul mediu din stoc (S3) → gol. Pe linia necodificată îl scrie ea:

   | Linie | Articol | Cant. | Preț estimat | Sursa prețului | Valoare |
   | --- | --- | --- | --- | --- | --- |
   | 1 | `MANUSI` Mănuși de protecție | 50 per. | 6,80 | Ultimul preț | 340,00 |
   | 2 | `CUTTER` Cutter profesional | 10 buc | 24,50 | Ultimul preț | 245,00 |
   | 3 | `VESTA` Vestă reflectorizantă | 15 buc | 12,00 | Cost mediu din stoc | 180,00 |
   | 4 | *necodificat*: Transpalet manual 2,5 t, furci 1.150 mm (imobilizare) | 1 buc | 4.800,00 | Scris de solicitant | 4.800,00 |
   | | | | | Total estimat | **5.565,00 lei** |

2. La „Trimite la aprobare” (24.11, 10:12) referatul primește numărul `RF 0233`; valoarea 5.565,00 atinge pragul nivelului 2, deci `RequiredLevel = 2`. Justificarea e obligatorie (valoarea atinge pragul nivelului 2): „Transpaletul vechi a fost casat (PV 14/2026)”. Gheorghe Ilie primește notificarea.
3. Gheorghe aprobă nivelul 1 (24.11, 15:20). Dan Toma (Șef achiziții) primește notificarea pentru nivelul 2.
4. Dan vede că în DC-PL sunt 20 de perechi de mănuși fără mișcare și micșorează linia 1 la 30, cu motivul „20 per. se transferă din DC-PL”, apoi aprobă (25.11, 09:05). Valoarea aprobată devine 5.429,00 lei; nivelul cerut rămâne 2 (3.2 regula 4). Ecranul `a2-01`.
5. Referatul trece în „Aprobat”, evenimentul `ReferatAprobat` pleacă în outbox, iar Andreea primește notificare cu modificarea făcută de Dan. Liniile apar la achizitori, în „De rezolvat”, după grupa articolului (A1 §3.8); transpaletul, necodificat, ajunge la Șeful achizițiilor.

### 4.2 Calculul necesarului de aprovizionare

Comanda `CalculatePurchaseNeed`, pornită de achizitor (ecranul `a2-02`) sau de un job programat (Nucleu, joburi), cu filtre: gestiuni, grupe de articole, achizitor („doar ale mele”), sursele incluse. Calculul ia un `sp_getapplock` pe `PN:{CompanyId}` în modul exclusiv doar cât scrie liniile (`53109` dacă un alt calcul al aceleiași firme e în curs), ca două calcule simultane să nu propună de două ori aceeași cantitate.

1. **Referatele.** Liniile active ale referatelor aprobate, cu rest pe grupul `NEED` (Nucleu `GetAvailableAsync`). Liniile codificate cu aceeași variantă și aceeași gestiune se unesc într-o linie de necesar (data necesară = cea mai apropiată); cele necodificate rămân separate. Fiecare legătură se scrie „în ciornă” (`ConsumeAsync`, `LinkState.Draft`).
2. **Stocul sub minim**, pentru variantele cu `ReplenishmentMethod = 1` (3.7) în gestiunile filtrate:
   - termenul de livrare `L` = termenul din catalogul furnizorului propus (A1 §3.5) sau, fără el, din `SupplierTerms`; orizontul `H` = azi + `L` + `NeedReviewDays`;
   - proiectatul (S5, `inv.usp_Availability_Projected`) se completează cu ce e pe drum și nu e încă în el (regula 5): comenzile trimise neconfirmate și ciornele de comandă (`IPurchaseOrderSupply` cu `IsConfirmed = 0`), plus liniile de necesar validate (sau în ciornă, din alte calcule) încă necomandate, fiecare la data ei;
   - `Pmin` = cel mai mic proiectat între azi și `H`; `PH` = proiectatul în ziua `H`, după toate intrările așteptate până atunci;
   - dacă `Pmin < MinQty`, cantitatea propusă = `MaxQty − PH` dacă există `MaxQty`, altfel `max(ReorderQty, MinQty − PH)`; apoi se convertește în UM de cumpărare și se rotunjește în sus la minimul și multiplul furnizorului (A1 §3.5). Dacă rezultatul e ≤ 0 (stocul scade sub minim doar până sosește o comandă deja făcută), linia apare cu acțiunea „Amânată” și mesajul „sub minim până la intrarea comenzii X: cere devansarea”, fără cantitate. Instantaneul (`ProjectedMinQty`, `ProjectedAtHorizonQty`, praguri, `OnOrderQty`) se salvează pe linie.
3. **MRP.** Rândurile active din `purch.MrpDemand`, pentru variantele cu `ReplenishmentMethod = 2`, cu `NeededBy ≤ H` (sau toate, după filtru); un rând = o linie de necesar (origine 3). Cantitatea MRP e deja netă și nu se mai compară cu pragurile.
4. **Furnizorul propus**, în ordine: furnizorul preferat din catalog (`SupplierItem.IsPreferred`, pe firmă apoi pe grup) → furnizorul ultimei achiziții a variantei (`LastPurchaseDate`) → furnizorul sugerat pe referat → niciunul. Un furnizor blocat (A1 §3.4) se sare, cu avertizare pe linie.
5. **Prețul** prin `ISupplierPriceProvider` (A1 §3.6), la data calculului, pe cantitatea rotunjită, în moneda furnizorului.
6. **Achizitorul**: `BuyerAssignment` pe grupa articolului (cea mai adâncă grupă cu responsabil) → pe furnizor → `SupplierTerms.BuyerUserId` → fără achizitor (linia ajunge la Șeful achizițiilor). Înlocuitorul activ preia liniile titularului în concediu.
7. **Acțiunea propusă**: „Comandă” dacă linia are variantă, furnizor și preț, iar valoarea e sub `RfqRequiredAboveRon`; altfel „Cerere de ofertă”.
8. Rezultatul e un necesar în ciornă. Un calcul nou pe același achizitor înlocuiește liniile ciornei lui anterioare (eliberează legăturile în ciornă și le reface); liniile modificate manual (cantitate, furnizor, acțiune) se păstrează și se marchează „modificată”.

Exemplul din ecranul `a2-02`: necesarul `NC 0418` din 26.11.2026, calculat de Laura Vasile pentru DEP-CJ și DC-PL, are 7 linii, 26.436,20 lei estimat:

| Articol | Gestiune | Origine | Proiectat · praguri | Propus | Furnizor · preț | Acțiune |
| --- | --- | --- | --- | --- | --- | --- |
| `APA05` Apă minerală 0,5 L | DEP-CJ | Stoc sub minim | cel mai mic 1.380 (01.12) · la orizont 2.150 (06.12, cu CF 2291 de 4.800 buc pe 02.12) · min 4.000, max 12.000 | 9.852 buc (821 baxuri) | Izvorul Carpaților · 1,1600 (prag 9.600) | Comandă |
| `APA15` Apă minerală 1,5 L | DEP-CJ | Stoc sub minim | la orizont 1.050 · min 1.500, max 4.000 | 2.952 buc (492 baxuri) | Izvorul Carpaților · 1,9900 (prag 2.400) | Comandă |
| `SUC033` Suc natural 0,33 L | DC-PL | Stoc sub minim | la orizont 310 · min 600, max 1.800 | 1.512 buc (63 baxuri de 24) | Izvorul Carpaților · 2,4500 | Comandă |
| `MANUSI` | DEP-CJ | RF 0233 | — | 30 per. | Office Line · 6,80 (ultimul preț) | Comandă |
| `CUTTER` | DEP-CJ | RF 0233 | — | 10 buc | Office Line · 24,50 | Comandă |
| `VESTA` | DEP-CJ | RF 0233 | — | 15 buc | Office Line · 12,00 | Comandă |
| Transpalet manual 2,5 t | DEP-CJ | RF 0233 (necodificat) | — | 1 buc | — · 4.800,00 estimat | Cerere de ofertă (Dan Toma) |

### 4.3 Validarea necesarului și generarea comenzilor și a cererilor de ofertă

1. Achizitorul revizuiește liniile: schimbă furnizorul (prețul se repropune), cantitatea, acțiunea; codifică linia necodificată (alege o variantă; sau creează articolul, dacă are dreptul în Nucleu).
2. „Validează”: liniile „Amânată” se elimină și legăturile lor `NEED` se șterg (referatele redevin disponibile); legăturile rămase devin active. O linie „Comandă” fără variantă (`53107`) sau fără furnizor (`53108`) blochează validarea.
3. „Generează comenzi” (tranzacția `CF-NC`, A1 §3.2): liniile „Comandă” bifate se grupează pe **furnizor × monedă × gestiune de livrare** (decizia 8); fiecare grup devine o comandă în ciornă (A3), cu legături `ORDER` pe liniile de necesar. Comanda sub `MinOrderAmount` al furnizorului primește avertizare. Din `NC 0418` rezultă 3 comenzi: Izvorul Carpaților în DEP-CJ (APA05, APA15), Izvorul Carpaților în DC-PL (SUC033), Office Line în DEP-CJ (3 linii).
4. „Generează cereri de ofertă” (`CO-NC`): liniile „Cerere de ofertă” bifate devin o cerere (sau câte una pe achizitor), cu legături `RFQ`. Din `NC 0418`, transpaletul devine `CO 0057`, a lui Dan Toma.
5. Necesarul se închide automat când toate liniile au fost consumate pe `ORDER` (direct, sau indirect prin `CF-OF`, 4.6) ori închise manual.

### 4.4 Cererea de ofertă și înregistrarea ofertelor (exemplu complet)

Pe 20.11.2026 Laura Vasile a pregătit consumabilele de ambalare pentru trimestrul următor. Linia din necesarul `NC 0409` depășea 20.000 lei, deci acțiunea propusă a fost „Cerere de ofertă”.

1. `CO 0056` (`CO-NC`), termen de răspuns 25.11, livrare în DEP-CJ: `FOLIE23` Folie stretch 23 µm, 480 role; `ETT100` Etichete termice 100×150, 720 role; `BANDA48` Bandă adezivă 48 mm, 960 role. Valoarea estimată: 35.520,00 lei (la prețurile furnizorului actual, Furnizor Ambalaje SRL).
2. Invită patru furnizori: Furnizor Ambalaje SRL (actualul), PackPro Logistic SRL, Ambalaje Vest SRL (factură în EUR) și Plastic Pack SRL. „Trimite”: fiecare primește PDF-ul și Excelul de răspuns; starea lor devine „Trimisă”. Ecranul `a2-03`.
3. Răspunsurile: Furnizor Ambalaje (OF 0191, 22.11), PackPro (OF 0192, 23.11, transport facturat separat 250,00 lei), Ambalaje Vest (OF 0193, 24.11, în EUR, DAP Cluj, valoare minimă de comandă 1.000,00 EUR). Plastic Pack refuză pe email („nu avem capacitate în decembrie”); Laura marchează „A refuzat” cu motivul.
4. Ofertele venite în Excelul de răspuns se importă direct: foaia ascunsă identifică cererea și furnizorul, liniile se recunosc după identificatorul liniei de cerere, fără potrivire după cod. Ofertele venite în alt format se introduc manual, cu ajutorul potrivirii din catalog (A1 §4.3) când furnizorul folosește codurile lui. Fiecare ofertă se generează din cerere (`OF-CO`), ca să aibă legăturile `QUOTE`. Ecranul `a2-04` arată OF 0193.
5. La termen, furnizorii care n-au răspuns trec automat în „Fără răspuns”, iar achizitorul primește notificare.

### 4.5 Compararea ofertelor și atribuirea

**Prețul comparabil** al unei linii de ofertă, în lei fără TVA, pe UM de bază:

```
PrețComparabil = PrețUnitar × (1 − Reducere%)                 (pragul aplicabil cantității cerute, dacă oferta are praguri)
               × Curs BNR al monedei ofertei, la data comparării (Nucleu fluxul 4.3)
               × (1 + TransportOfertă / ValoareaLiniilorAtribuiteDinOfertă)   (costurile separate, repartizate pe valoare)
               ÷ Factorul UM-ului ofertei către UM de bază
```

Transportul se repartizează doar pe liniile atribuite aceluiași furnizor, deci prețul comparabil se recalculează la fiecare bifare. Cu baza 2 (`QuoteComparisonBasis`), scorul fiecărei oferte pe linie este `Σ pondere × notă`, unde nota la preț = cel mai mic preț comparabil ÷ prețul ofertei × 100, la termenul de livrare = cel mai scurt ÷ termenul ofertei × 100, la termenul de plată = termenul ofertei ÷ cel mai lung × 100, la rating = scorul de performanță A7 pe 12 luni (0–100); pentru furnizorii cu „date insuficiente”, ratingul manual ÷ 5 × 100; furnizorul fără niciunul primește 60 (precizare din Achiziții A7, 27.11.2026).

Exemplul din ecranul `a2-05` (`CO 0056`, curs BNR 26.11.2026: 1 EUR = 5,0910 lei, baza 1):

| Linie | Furnizor Ambalaje (OF 0191) | PackPro (OF 0192) | Ambalaje Vest (OF 0193) | Atribuit |
| --- | --- | --- | --- | --- |
| `FOLIE23` 480 role | 38,50 | 36,90 + transport → **37,21** | 7,60 EUR → 38,69 | PackPro, cel mai bun |
| `ETT100` 720 role | 18,20 | 17,40 + transport → **17,54** | 3,80 EUR → 19,35 | PackPro, cel mai bun |
| `BANDA48` 960 role | **4,10** | 4,35 + transport → 4,39 | 0,79 EUR → **4,02** (cel mai bun) | Furnizor Ambalaje, cu motiv |
| Termen livrare · plată · rating | 3 zile · 30 zile · 4 | 5 zile · 45 zile · nou | 7 zile · 30 zile · 3 | |

Transportul PackPro (250,00 lei) se repartizează pe cele două linii atribuite lui (30.240,00 lei): +0,827%. `BANDA48` ar fi fost cea mai ieftină la Ambalaje Vest, dar 960 × 0,79 = 758,40 EUR e sub valoarea minimă de comandă a furnizorului (1.000,00 EUR), deci Laura alege Furnizor Ambalaje și scrie motivul. Total atribuit: 30.240,00 + 250,00 + 3.936,00 = 34.426,00 lei, cu 1.094,00 lei (3,08%) mai puțin decât oferta furnizorului actual pentru aceleași cantități (35.520,00 lei).

Pașii:

1. Achizitorul deschide „Comparare oferte” pe cerere: un rând pe linie de cerere, o coloană pe ofertă (inclusiv alternativele, sub linia lor), cea mai bună valoare evidențiată, ofertele expirate cu gri. Sub numele fiecărui furnizor apar scorul și clasa lui de performanță pe 12 luni (A7 §3.2, `ISupplierPerformanceReader`), sau „date insuficiente” (precizare din Achiziții A7, 27.11.2026).
2. Bifează oferta aleasă pe fiecare linie; pentru împărțire, scrie cantitatea pe fiecare ofertă. Motivul se cere la salvare acolo unde regula 9 din secțiunea 2 o impune.
3. „Salvează atribuirea” scrie `RfqAward`, trece liniile de ofertă în Acceptată sau Respinsă, ofertele în Acceptată sau Respinsă, cererea în Atribuită și publică `OfertaAtribuita`. Furnizorii respinși pot primi un email de mulțumire (opțional, șablon în Nucleu).
4. Din același ecran: „Generează comenzi” (`CF-OF`, una pe ofertă acceptată, în ciornă, detaliată în A3) și, opțional, „Creează listă de prețuri” dintr-o ofertă acceptată cu valabilitate lungă (A1 §3.6, `SourceQuotationId`).

Atribuirea nu are aprobare proprie (decizia 12): comanda generată trece prin aprobarea din A3, iar aprobatorul vede pe comandă instantaneul atribuirii (ofertele primite, prețurile comparabile, motivul).

### 4.6 Legăturile pe drumul referat → comandă

`CF-OF` consumă grupul `ORDER` pe linia de ofertă acceptată și, indirect, pe linia de necesar din care a pornit cererea (drumul ofertă → cerere → necesar), ca necesarul să știe că linia a fost comandată. Cantitatea consumată pe necesar e cel mult restul liniei de necesar; ce se comandă peste el (de exemplu un palet întreg) rămâne fără legătură cu necesarul. Este același tipar ca la `NIR-AV`, care consumă comanda prin aviz (A1 §3.2).

Pe referat solicitantul vede tot drumul (3.1.1): `RF 0233` linia 4 → `NC 0418` → `CO 0057` → (după atribuire) comanda.

### 4.7 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Solicitantul e și responsabilul centrului de cost | Nivelul 1 trece la înlocuitorul lui sau la nivelul următor (3.2 regula 3); istoricul arată motivul |
| Aprobatorul de nivel 2 e în concediu | Înlocuitorul din Nucleu primește notificarea și poate decide; dacă nu există înlocuitor, după `ApprovalReminderHours` × 3 notificarea merge și la Șeful achizițiilor (sau administratorului firmei, dacă el e aprobatorul) |
| Referatul e aprobat, dar varianta a fost dezactivată între timp | Linia apare în necesar cu avertizare și acțiunea „Amânată”; achizitorul alege altă variantă (linia devine „modificată”, iar legătura cu referatul rămâne) |
| Regula de aprobare se schimbă cât un referat e „De aprobat” | Referatul păstrează nivelul calculat la trimitere (ca F5) |
| Două calcule de necesar simultan pe aceeași firmă | Al doilea așteaptă blocarea; dacă trece de timpul de așteptare, primește `53109` și poate reîncerca. Referatele sunt protejate oricum de `sp_getapplock` pe sursă (Nucleu §3.6.2) |
| Același articol din stoc minim și din referat, în aceeași gestiune | Două linii de necesar (regula 4); comanda generată le unește pe aceeași linie dacă au același furnizor și preț |
| Stoc sub minim doar până sosește o comandă deja făcută | Linie fără cantitate, „Amânată”, cu mesajul de devansare (4.2 pasul 2); achizitorul cere furnizorului livrarea mai devreme (A3) |
| Variantă cu MRP și cu praguri min/max | Contează doar `ReplenishmentMethod` (3.7): cu 2, pragurile se ignoră în necesar, dar rămân pentru alertele de stoc (S7) |
| Rulare MRP primită după ce un necesar a preluat rânduri din rularea anterioară | Rândurile deja preluate rămân „Preluate”; noua rulare e deja netă de comenzile generate (MRP le vede ca intrări), deci nu se dublează |
| Furnizorul răspunde după termen | Oferta se poate înregistra cât cererea nu e atribuită; starea furnizorului trece din „Fără răspuns” în „Ofertă primită” |
| Furnizorul trimite o ofertă corectată | Versiune nouă (`Version` + 1); cea veche trece în „Înlocuită”, cu legăturile `QUOTE` dezactivate |
| Oferta e în altă UM (bax în loc de bucată) | Se compară pe UM de bază, cu factorul din setul de UM al articolului (A1 regula 3); o UM care nu e în set trebuie adăugată întâi ca excepție pe articol (`53005`) |
| Produs alternativ propus de furnizor | Apare sub linia cerută, marcat „alternativă”; dacă e acceptat, se codifică (o variantă existentă sau nouă) înainte de comandă (`53116`) |
| Ofertă acceptată, iar furnizorul e blocat înainte de comandă | Generarea comenzii e refuzată (`53010`); atribuirea se reface |
| Referatul e anulat după ce o linie e în necesar | Refuzat; se închide linia de necesar (`DocumentLineClosure`) și apoi referatul se închide, cu notificare la solicitant |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `ReferatAprobat` v1 | Ultimul nivel aprobat sau aprobare automată | `CompanyId`, `RequestId`, `Number`, `RequesterUserId`, `CostCenterId`, `ProjectId`, `ApprovedValueRon`, liniile (variantă sau descriere, cantitate aprobată, gestiune, dată) | Raportare (bugete pe centre de cost), notificările A2 |
| `ReferatRespins` v1 | Respingere la oricare nivel | `CompanyId`, `RequestId`, `Number`, `RequesterUserId`, `Level`, `Comment` | Raportare |
| `NecesarValidat` v1 | Validarea necesarului | `CompanyId`, `NeedId`, `Number`, liniile (variantă, gestiune, cantitate de bază, origine, acțiune, furnizor) | MRP (ce a preluat Achizițiile din rularea lui), Raportare |
| `OfertaAtribuita` v1 | Salvarea atribuirii | `CompanyId`, `RfqId`, rândurile `RfqAward` (furnizor, variantă, cantitate, preț comparabil, cel mai bun preț, `IsBest`), economia față de oferta furnizorului actual | Raportare (economii, A7) |

Notificările către oameni (aprobator, solicitant, achizitor) se trimit prin `INotificationService` în aceeași tranzacție cu schimbarea de stare (Nucleu fluxul 4.7), nu prin evenimente.

### 5.2 Consumate

| Eveniment | De la | Ce face A2 |
| --- | --- | --- |
| `NecesarCalculat` v1 | MRP (02.08) | Scrie rândurile în `purch.MrpDemand` și înlocuiește rularea anterioară (3.3). Câmpuri cerute de la MRP: `CompanyId`, `MrpRunId`, `RunAt`, rânduri (`ItemVariantId`, `WarehouseId`, `QtyBase`, `NeededBy`, `PlannedOrderRef`) |
| `ComandaFurnizorEmisa` v1 | A3 | Notifică solicitanții referatelor de pe drumul comenzii |
| `ReceptieNIRValidata` v1 | A4 | Notifică solicitanții referatelor de pe drumul NIR-ului |
| `UtilizatorDezactivat` v1 | Nucleu | Mută documentele „De aprobat” ale utilizatorului la înlocuitor sau la Șeful achizițiilor; verifică regulile de aprobare cu utilizatori anume |
| `ArticolModificat` v1 | Nucleu | La dezactivarea unei variante, marchează liniile de necesar în ciornă (4.7) |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A2)

```csharp
public enum PurchaseRequestStatus : byte { Draft = 1, PendingApproval = 2, Approved = 3, Closed = 4, Rejected = 7, Cancelled = 9 }
public enum ApprovalDecision : byte { Approved = 1, Returned = 2, Rejected = 3 }
public enum ApproverKind : byte { Role = 1, Users = 2, CostCenterOwner = 3 }
public enum NeedOrigin : byte { Request = 1, StockMinimum = 2, Mrp = 3, Manual = 4 }
public enum NeedAction : byte { Order = 1, RequestQuotation = 2, Postpone = 3 }
public enum ReplenishmentMethod : byte { None = 0, MinMax = 1, Mrp = 2 }   // deținut de Stocuri, oglindit aici pentru citire

public sealed record ApprovalStep(byte Level, ApproverKind Kind, int? RoleId, IReadOnlyList<int> UserIds, byte MinApprovers);
public sealed record ApprovalPlan(byte RequiredLevel, decimal BasisValueRon, IReadOnlyList<ApprovalStep> Steps);

// Folosit de A2 (referate) și A3 (comenzi). Intern modulului; expus în Contracts pentru ecranul comun „De rezolvat”.
public interface IPurchaseApprovalService
{
    Task<ApprovalPlan> PlanAsync(int companyId, string documentType, long documentId, CancellationToken ct);
    Task<IReadOnlyList<PendingApprovalDto>> ListPendingForUserAsync(int companyId, int userId, CancellationToken ct);
}
public sealed record PendingApprovalDto(string DocumentType, long DocumentId, string Number, int AuthorUserId,
    decimal ValueRon, byte Level, DateTime SubmittedAt, bool IsSubstitute);

// Citit de MRP (02.08), ca să nu replanifice ce e deja preluat de Achiziții
public interface IPurchaseNeedReader
{
    Task<IReadOnlyList<OpenNeedRow>> GetOpenNeedAsync(int companyId, IReadOnlyCollection<int> itemVariantIds, CancellationToken ct);
}
public sealed record OpenNeedRow(long NeedLineId, int ItemVariantId, int WarehouseId, decimal OpenQtyBase,
    DateOnly NeededBy, NeedOrigin Origin, NeedAction Action);
```

`IPurchaseNeedReader` întoarce doar liniile validate cu rest pe `ORDER`. Același calcul e folosit de pasul 2 din 4.2 (regula 5).

Comenzi interne A2 (MediatR): `SavePurchaseRequest`, `SubmitPurchaseRequest`, `WithdrawPurchaseRequest`, `DecidePurchaseApproval` (comun cu A3: aprobare, returnare, respingere, cu liniile modificate), `CancelPurchaseRequest`, `ClosePurchaseRequestLine`, `CalculatePurchaseNeed`, `SavePurchaseNeedLine`, `CodePurchaseNeedLine`, `ValidatePurchaseNeed`, `GenerateRfqFromNeed`, `SaveRfq`, `SendRfq`, `SetRfqSupplierStatus`, `SaveSupplierQuotation`, `ImportSupplierQuotation` (Excelul de răspuns), `SaveRfqAward`, `CreatePriceListFromQuotation`, `SavePurchaseApprovalRule`. Generarea comenzilor (`GenerateFromSourcesCommand<PurchaseOrder>` cu `CF-NC` sau `CF-OF`) e în A3. Interogări: `GetQuotationComparison`, `GetRequestTrail`, `ListNeedLines`.

## 7. Schema SQL `purch` (partea A2)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.PurchaseRequest` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `CompanyId`, `TransactionTypeId`, `RequesterUserId`, `CostCenterId`, `ProjectId`, `WarehouseId` | `(CompanyId, Status, CurrentLevel)`; `(RequesterUserId, Status)` |
| `purch.PurchaseRequestLine` | `Id bigint` | UQ `(RequestId, LineNo)`; FK `RequestId`, `ItemVariantId`, `UomId`, `SuggestedPartnerId`; CK `ItemVariantId IS NOT NULL OR Description IS NOT NULL`; CK `ApprovedQty <= Qty` | `(ItemVariantId) WHERE LineStatus = 1` |
| `purch.PurchaseApprovalRule` | `Id int` | UX `(CompanyId, DocumentType, Level, CostCenterKey) WHERE IsActive = 1`; FK `ApproverRoleId`, `CostCenterId` | — |
| `purch.PurchaseApprovalRuleUser` | `(RuleId, UserId)` | FK `RuleId`, `UserId` | — |
| `purch.PurchaseApproval` | `Id bigint` | FK `UserId`; doar adăugare (trigger, `SP-21`) | `(DocumentType, DocumentId, Level)` |
| `purch.PurchaseNeed` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `CompanyId`, `TransactionTypeId` | `(CompanyId, Status)` |
| `purch.PurchaseNeedLine` | `Id bigint` | FK `NeedId`, `ItemVariantId`, `WarehouseId`, `PartnerId`, `BuyerUserId`, `PurchaseUomId`, `MrpDemandId` | `(ItemVariantId, WarehouseId) INCLUDE (QtyBase, NeededBy) WHERE IsOpen = 1`; `(BuyerUserId, IsOpen)` |
| `purch.MrpDemand` | `Id bigint` | UQ `(MrpRunId, ItemVariantId, WarehouseId, NeededBy, PlannedOrderRef)`; FK `CompanyId`, `ItemVariantId`, `WarehouseId` | `(CompanyId, State, ItemVariantId)` |
| `purch.Rfq` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `CompanyId`, `TransactionTypeId`, `BuyerUserId`, `DeliveryWarehouseId` | `(CompanyId, Status, ResponseDueAt)` |
| `purch.RfqLine` | `Id bigint` | UQ `(RfqId, LineNo)`; FK `RfqId`, `ItemVariantId`, `UomId` | — |
| `purch.RfqSupplier` | `Id bigint` | UQ `(RfqId, PartnerId)`; FK `RfqId`, `PartnerId`, `QuotationId` | `(PartnerId, Status)` |
| `purch.SupplierQuotation` | `Id bigint` | UX `(RfqId, PartnerId) WHERE Status IN (1, 3, 5) AND RfqId IS NOT NULL` (regula 7, `53112`); FK `CompanyId`, `PartnerId`, `RfqId`, `CurrencyCode` | `(CompanyId, PartnerId, QuoteDate)` |
| `purch.SupplierQuotationLine` | `Id bigint` | FK `QuotationId`, `RfqLineId`, `ItemVariantId`, `UomId` | `(RfqLineId)` |
| `purch.RfqAward` | `Id bigint` | UQ `(RfqLineId, QuotationLineId)`; FK `RfqLineId`, `QuotationLineId`, `BestQuotationLineId`, `DecidedBy` | — |

`PurchaseNeedLine.IsOpen` e o coloană persistată actualizată de procedurile de stare (1 cât necesarul e în ciornă sau validat și linia are rest pe `ORDER`), ca interogarea din 4.2 pasul 2 să fie un singur seek. `CostCenterKey` = `ISNULL(CostCenterId, 0)`, persistată, ca la A1.

Exemplu de tabel complet:

```sql
CREATE TABLE purch.PurchaseRequestLine (
    Id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseRequestLine PRIMARY KEY,
    RequestId             bigint            NOT NULL CONSTRAINT FK_PurchaseRequestLine_Request REFERENCES purch.PurchaseRequest(Id),
    LineNo                smallint          NOT NULL,
    ItemVariantId         int               NULL CONSTRAINT FK_PurchaseRequestLine_ItemVariant REFERENCES core.ItemVariant(Id),
    Description           nvarchar(300)     NULL,
    ItemKind              tinyint           NOT NULL,                    -- 1 stocabil, 2 nestocabil / obiect de inventar, 3 serviciu, 4 imobilizare
    UomId                 int               NOT NULL CONSTRAINT FK_PurchaseRequestLine_Uom REFERENCES core.UnitOfMeasure(Id),
    Qty                   decimal(19,6)     NOT NULL,
    ApprovedQty           decimal(19,6)     NULL,
    EstimatedUnitPrice    decimal(19,6)     NOT NULL,
    EstimatedPriceSource  tinyint           NOT NULL,                    -- 1 listă, 2 ultimul preț, 3 cost mediu, 4 scris de solicitant
    SuggestedPartnerId    int               NULL CONSTRAINT FK_PurchaseRequestLine_Partner REFERENCES core.Partner(Id),
    NeededBy              date              NOT NULL,
    WarehouseId           int               NULL CONSTRAINT FK_PurchaseRequestLine_Warehouse REFERENCES inv.Warehouse(Id),
    CostCenterId          int               NULL CONSTRAINT FK_PurchaseRequestLine_CostCenter REFERENCES core.DimensionValue(Id),
    ProjectId             int               NULL CONSTRAINT FK_PurchaseRequestLine_Project REFERENCES core.DimensionValue(Id),
    LineStatus            tinyint           NOT NULL CONSTRAINT DF_PurchaseRequestLine_LineStatus DEFAULT (1),
    RejectReason          nvarchar(300)     NULL,
    Notes                 nvarchar(500)     NULL,
    CreatedAt             datetime2(3)      NOT NULL CONSTRAINT DF_PurchaseRequestLine_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy             int               NOT NULL,
    ModifiedAt            datetime2(3)      NULL,
    ModifiedBy            int               NULL,
    RowVersion            rowversion        NOT NULL,
    CONSTRAINT UQ_PurchaseRequestLine_LineNo UNIQUE (RequestId, LineNo),
    CONSTRAINT CK_PurchaseRequestLine_Identity CHECK (ItemVariantId IS NOT NULL OR Description IS NOT NULL),
    CONSTRAINT CK_PurchaseRequestLine_Qty CHECK (Qty > 0 AND (ApprovedQty IS NULL OR (ApprovedQty >= 0 AND ApprovedQty <= Qty))),
    CONSTRAINT CK_PurchaseRequestLine_Price CHECK (EstimatedUnitPrice >= 0),
    CONSTRAINT CK_PurchaseRequestLine_Reject CHECK (LineStatus <> 2 OR RejectReason IS NOT NULL)
);

CREATE INDEX IX_PurchaseRequestLine_Variant ON purch.PurchaseRequestLine (ItemVariantId) WHERE LineStatus = 1;
```

Proceduri: `purch.usp_PurchaseRequest_Save`, `purch.usp_PurchaseRequest_Submit` (alocă numărul, scrie planul de aprobare), `purch.usp_PurchaseApproval_Decide` (comun cu A3; blochează documentul cu `UPDLOCK`, verifică nivelul curent și aprobatorul, scrie jurnalul și liniile modificate, avansează nivelul, după șablonul din Bune practici 3.5, ca `fin.usp_PaymentProposal_Decide` din F5), `purch.usp_PurchaseNeed_Calculate` (TVP cu variantele și gestiunile; citește S5 prin `inv.usp_Availability_Projected` cu TVP, nu rând cu rând, `SP-07`, `SP-08`), `purch.usp_PurchaseNeed_Validate`, `purch.usp_MrpDemand_Upsert`, `purch.usp_Rfq_Save`, `purch.usp_Rfq_Send`, `purch.usp_SupplierQuotation_Save`, `purch.usp_QuotationComparison_Get` (întoarce liniile de ofertă cu prețurile convertite; repartizarea transportului și scorul se calculează în C#, cu `IPrecisionService`, `SP-13`), `purch.usp_RfqAward_Save`, `purch.usp_PurchaseApprovalRule_Save`, `purch.usp_PurchaseNeed_OpenLines` (pentru `IPurchaseNeedReader`).

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53101 | `PURCH_REQUEST_FIELD_REQUIRED` | 400 | Linie necodificată fără preț estimat sau fără descriere; justificare lipsă peste pragul nivelului 2 |
| 53102 | `PURCH_REQUEST_NOT_EDITABLE` | 409 | Modificarea unui referat care nu e în ciornă |
| 53103 | `PURCH_APPROVER_NOT_ALLOWED` | 403 | Utilizatorul nu e aprobator la nivelul curent, e autorul sau a aprobat deja alt nivel al documentului |
| 53104 | `PURCH_APPROVAL_NOT_PENDING` | 409 | Decizie pe un document care nu e „De aprobat” sau pe un nivel deja decis |
| 53105 | `PURCH_APPROVED_QTY_INVALID` | 400 | Cantitate aprobată negativă sau mai mare decât cea solicitată |
| 53106 | `PURCH_APPROVAL_RULE_INVALID` | 400 | Niveluri lipsă la mijloc, praguri necrescătoare, aprobator lipsă, `MinApprovers` mai mare decât lista |
| 53107 | `PURCH_NEED_LINE_UNCODED` | 409 | Validare sau comandă din necesar cu o linie „Comandă” fără variantă |
| 53108 | `PURCH_NEED_LINE_NO_SUPPLIER` | 409 | Linie „Comandă” fără furnizor |
| 53109 | `PURCH_NEED_CALC_BUSY` | 409 | Un alt calcul de necesar al firmei e în curs |
| 53110 | `PURCH_RFQ_NO_SUPPLIERS` | 409 | Trimiterea unei cereri fără furnizori invitați sau fără linii |
| 53111 | `PURCH_RFQ_SUPPLIER_NOT_INVITED` | 409 | Ofertă din cerere pentru un furnizor care nu e invitat (filtrul `INVITED_SUPPLIER` al `OF-CO`) |
| 53112 | `PURCH_QUOTATION_DUPLICATE` | 409 | A doua ofertă activă a aceluiași furnizor la aceeași cerere, fără înlocuirea primei |
| 53113 | `PURCH_QUOTATION_EXPIRED` | 409 | Acceptarea unei oferte cu `ValidUntil` depășit |
| 53114 | `PURCH_AWARD_REASON_REQUIRED` | 400 | Atribuire care nu e cea mai bună sau cu prea puține oferte, fără motiv |
| 53115 | `PURCH_AWARD_QTY_EXCEEDED` | 409 | Cantitatea atribuită pe o linie depășește cererea plus toleranța |
| 53116 | `PURCH_AWARD_LINE_UNCODED` | 409 | Generarea comenzii dintr-o linie de ofertă acceptată fără variantă |
| 53117 | `PURCH_RFQ_DUE_DATE_INVALID` | 400 | Termen de răspuns în trecut sau după data de livrare dorită |
| 53118 | `PURCH_AWARD_LOCKED` | 409 | Modificarea atribuirii după ce s-a generat o comandă din ea |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.Request.Create` | Referate proprii: creare, trimitere, retragere, anulare din ciornă |
| `Purch.Request.ViewAll` | Toate referatele firmei; crearea în numele altui utilizator |
| `Purch.Need.Manage` | Calculul, editarea și validarea necesarului; generarea cererilor de ofertă |
| `Purch.Rfq.Manage` | Cereri de ofertă și oferte furnizori |
| `Purch.Quotation.Award` | Atribuirea ofertelor |
| `Purch.ApprovalRule.Manage` | Regulile de aprobare pentru referate și comenzi |

Aprobarea nu are permisiune proprie: o poate da doar cine e desemnat de regula nivelului (rol, listă sau responsabilul centrului de cost), verificat de `purch.usp_PurchaseApproval_Decide`.

Roluri livrate: **Solicitant** (rol nou, pentru orice angajat: `Request.Create`), **Gestionar** (`Request.Create`), **Achizitor** (`Request.Create`, `Need.Manage`, `Rfq.Manage`, `Quotation.Award`), **Șef achiziții** (tot ce are Achizitorul, plus `Request.ViewAll`, `ApprovalRule.Manage`; aprobator de nivel 2 în regulile livrate), **Contabil furnizori** (doar citire pe necesar și oferte, pentru prognoza plăților).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A2 |
| --- | --- |
| Legea contabilității 82/1991 și OMFP 2634/2015 (documente justificative, control intern) | Referatul de necesitate și atribuirea nu sunt documente justificative obligatorii pentru o firmă privată, dar sunt pistă de control intern: aprobările și atribuirea se păstrează doar prin adăugare, cu autor și dată, cel puțin cât documentele contabile legate (Nucleu §4.9) |
| Legea 98/2016 (achiziții publice) | Nu se aplică firmelor private. Termenul „referat de necesitate” e preluat din practică, fără procedurile legii |
| GDPR (Regulamentul UE 2016/679) | Emailurile persoanelor de contact ale furnizorilor sunt date personale: se folosesc doar pentru cererile de ofertă și se șterg cu contactul (Nucleu) |

Integrări externe: emailul (prin outbox-ul Nucleului). Interne: Nucleu (tranzacții, legături, notificări, dimensiuni, înlocuitori), Stocuri S5 (proiectatul), S1 (pragurile și metoda de reaprovizionare), MRP (`NecesarCalculat`, `IPurchaseNeedReader`), A1 (catalog, prețuri, achizitori), A3 (comenzile generate și aprobarea lor).

**Completare la Nucleu §3.6 (decizia 9, aplicată în Nucleu pe 26.11.2026):** `ITransactionBehavior` primește proprietatea `bool LimitsConsumption` (implicit `true`). Pentru comportamentele cu `false` (`Rfq.FromNeed`, `Quotation.FromRfq`), `core.usp_DocumentLink_Consume` scrie legăturile și calculează rămasul pentru afișare, dar nu ridică `50027`. Coloana `LimitsConsumption bit` se adaugă și în `core.TransactionType` (citită din comportament, ca `Mode`).

**Completare la S1 (Stocuri) (decizia 6, aplicată în S1 pe 26.11.2026):** coloana `ReplenishmentMethod` din 3.7.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Referate în aprobare | Pe aprobator și nivel: referatele așteptate, vechimea în ore, valoarea; timpul mediu de aprobare pe nivel în ultimele 90 de zile | Șef achiziții, aprobatorii |
| Referatele mele | Pentru solicitant: fiecare linie și unde a ajuns (3.1.1) | Solicitant, Gestionar |
| Necesar neacoperit | Liniile de necesar validate fără comandă, după vechime și data necesară; liniile amânate de mai multe ori | Achizitor, Șef achiziții |
| Cereri de ofertă fără răspuns | Pe furnizor: cereri trimise, oferte primite, refuzuri, rata de răspuns | Achizitor |
| Economii din atribuiri | Pe lună, achizitor și grupă: valoarea atribuită, diferența față de oferta furnizorului actual și față de media ofertelor; atribuirile care nu au ales cea mai bună ofertă, cu motivul | Șef achiziții, Director financiar |
| Consum pe centre de cost | Referatele aprobate și comandate pe centru de cost și lună (bugetul se compară în Raportare) | Responsabilii de centre de cost |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a2-necesar-oferte/](ecrane/a2-necesar-oferte/). Meniul Achiziții › Necesar conține Referate, Necesar de aprovizionare, Cereri de ofertă, Oferte furnizori și Comparare oferte (A1 §11).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Referat (aprobare) | Fișa referatului `RF 0233` văzută de aprobatorul de nivel 2: liniile cu solicitat, aprobat, preț estimat și sursa lui, starea fiecărei linii; panoul de aprobare cu nivelurile, deciziile și butoanele Aprobă, Returnează, Respinge | `a2-01-referat-aprobare` |
| Necesar de aprovizionare | `NC 0418` în ciornă: parametrii calculului, liniile pe origini, instantaneul (proiectat, praguri, pe drum), furnizorul, prețul, achizitorul, acțiunea; panoul liniei selectate cu motivul cantității | `a2-02-necesar-aprovizionare` |
| Cerere de ofertă | `CO 0056`: liniile, furnizorii invitați cu starea fiecăruia (trimisă, ofertă primită, refuz, fără răspuns), emailul și Excelul de răspuns | `a2-03-cerere-oferta` |
| Ofertă furnizor | `OF 0193` de la Ambalaje Vest, în EUR: antetul cu valabilitate, Incoterm, minim de comandă; liniile legate de cerere, importate din Excelul de răspuns | `a2-04-oferta-furnizor` |
| Comparare oferte | `CO 0056`: o linie pe articol, o coloană pe ofertă, prețul comparabil în lei, cel mai bun evidențiat, atribuirea cu motiv, totalurile și economia | `a2-05-comparare-oferte` |
| Configurare › Achiziții › Aprobări | Regulile de aprobare pe referate și comenzi, pe niveluri, cu pragul, baza, aprobatorul și regulile pe centre de cost | `a2-06-reguli-aprobare` |

Lista referatelor (filtre: ale mele, de aprobat de mine, pe stare, centru de cost), lista necesarelor și lista ofertelor folosesc tiparul comun de listă și nu au machetă separată.

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A2-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A2-A1 | `RF 0233` (5.565,00 lei) cere nivelurile 1 și 2; un referat de 4.999,99 lei cere doar nivelul 1; cu nivelul 1 setat de la 500 lei, un referat de 300 lei e aprobat automat la trimitere, cu mențiunea în istoric | Test de integrare |
| A2-A2 | Solicitantul nu își aprobă referatul; aprobatorul de nivel 1 nu aprobă și nivelul 2 (`53103`); nivelul 2 nu poate decide înaintea nivelului 1 (`53104`) | Test de integrare |
| A2-A3 | Micșorarea mănușilor la 30 la nivelul 2 lasă valoarea 5.429,00 și nivelul cerut 2; mărirea la 60 e refuzată (`53105`); jurnalul are linia schimbată | Test de integrare |
| A2-A4 | Returnarea readuce ciorna; la retrimitere aprobările vechi nu mai contează și nivelul se recalculează | Test de integrare |
| A2-A5 | Linia necodificată fără preț estimat e refuzată (`53101`); după aprobare apare în necesar fără variantă; comanda din ea e refuzată până la codificare (`53107`) | Test de integrare |
| A2-A6 | Calculul din stoc minim pentru `APA05` în DEP-CJ: cel mai mic proiectat 1.380, proiectat la orizont 2.150 (cu CF 2291), min 4.000, max 12.000 → 9.850, rotunjit la 9.852 (bax de 12); prețul propus 1,1600 (pragul 9.600) | Test de integrare (cu Stocuri S5) |
| A2-A7 | Regula 5: o ciornă de comandă neconfirmată de 9.852 buc `APA05` face ca un nou calcul să nu mai propună nimic pentru `APA05` în DEP-CJ | Test de integrare |
| A2-A8 | O variantă cu `ReplenishmentMethod = 2` nu primește propunere din stoc minim, chiar sub prag; primește linia din `MrpDemand`. O rulare MRP nouă înlocuiește rândurile active ale celei vechi, iar un eveniment mai vechi se ignoră | Test de integrare |
| A2-A9 | Două calcule simultane pe aceeași firmă nu consumă de două ori același referat și nu propun de două ori același stoc minim | Test de concurență |
| A2-A10 | Validarea elimină liniile amânate și eliberează referatele lor (rămasul `NEED` revine); `CF-NC` generează câte o comandă pe furnizor × monedă × gestiune (3 comenzi din `NC 0418`) | Test de integrare |
| A2-A11 | Un furnizor neinvitat nu poate avea ofertă din cerere (`53111`); a doua ofertă activă a aceluiași furnizor e refuzată fără înlocuire (`53112`); cu înlocuire, prima trece în „Înlocuită” | Test de integrare |
| A2-A12 | Prețul comparabil din 4.5: `FOLIE23` PackPro 37,21 (transportul repartizat pe două linii), Ambalaje Vest 38,69 la 5,0910; atribuirea `BANDA48` la Furnizor Ambalaje fără motiv e refuzată (`53114`), cu motiv e salvată cu `IsBest = 0` | Test unitar + integrare |
| A2-A13 | Excelul de răspuns importat se leagă de cererea și furnizorul corecte după foaia semnată; un fișier cu semnătura alterată e refuzat | Test de integrare |
| A2-A14 | `CF-OF` din oferta acceptată consumă `ORDER` pe linia de ofertă și, indirect, pe linia de necesar, până la restul ei; necesarul se închide când toate liniile sunt consumate | Test de integrare (cu A3) |
| A2-A15 | Toate procedurile A2 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Test de integrare |

Criteriile A2-A6, A2-A7 și A2-A9 sunt critice: o greșeală în calculul necesarului înseamnă marfă cumpărată de două ori sau ruptură de stoc.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Linii necodificate pe referat | Da, cu descriere, tip (stocabil, nestocabil, serviciu, imobilizare) și preț estimat obligatoriu; achizitorul le codifică înainte de comandă | 2 regula 3, 3.1 | Decis de Aurelian (26.11.2026) |
| 2 | Unde stau regulile de aprobare | În Achiziții (`purch.PurchaseApprovalRule`), pe tiparul F5, comune referatelor și comenzilor; aprobatorul poate fi un rol, o listă de utilizatori sau responsabilul centrului de cost; reguli pe centru de cost cu prioritate. Alternativa ar fi un motor comun în Nucleu, cu mutarea F5 în el | 3.2 | Decis de Aurelian (26.11.2026) |
| 3 | Pragurile livrate pentru referate | Nivelul 1 de la 0 lei (responsabilul centrului de cost), nivelul 2 de la 5.000 lei (Șef achiziții), nivelul 3 de la 50.000 lei (Director general); sub pragul nivelului 1, aprobare automată | 3.2 | Decis de Aurelian (26.11.2026) |
| 4 | Ce poate face aprobatorul | Poate micșora cantități și respinge linii, cu motiv; nu poate mări și nu poate adăuga; nivelul cerut nu scade după trimitere | 3.2 regula 4 | Decis de Aurelian (26.11.2026) |
| 5 | Sursele necesarului | Referatele aprobate, stocul sub minim pe disponibilul proiectat (cu tot ce e pe drum, inclusiv comenzi neconfirmate și ciorne) și MRP; referatele nu se compensează cu stocul minim | 2 regulile 4–5, 4.2 | Decis de Aurelian (26.11.2026) |
| 6 | Metoda de reaprovizionare | Coloană nouă `ReplenishmentMethod` în setările de stoc (S1): fără, min/max sau MRP, ca o variantă să nu fie propusă din două părți | 3.7, 9 | Decis de Aurelian (26.11.2026) |
| 7 | Necesarul ca document | Document `NC` în ciornă recalculabilă, apoi validat; liniile amânate se elimină la validare și referatele lor redevin disponibile | 3.3, 4.3 | Decis de Aurelian (26.11.2026) |
| 8 | Gruparea comenzilor generate din necesar | O comandă pe furnizor × monedă × gestiune de livrare | 4.3 | Decis de Aurelian (26.11.2026) |
| 9 | Cererea de ofertă la mai mulți furnizori | O singură cerere cu furnizorii invitați; o ofertă activă pe furnizor (versiuni); grupurile `RFQ` și `QUOTE` informative, prin completarea Nucleului cu `LimitsConsumption` | 2 regula 7, 3.4, 9 | Decis de Aurelian (26.11.2026) |
| 10 | Răspunsul furnizorului | Cu cererea pleacă un Excel de răspuns precompletat și semnat, importat direct ca ofertă; alte formate se introduc manual, cu potrivirea din catalog | 3.4, 4.4 | Decis de Aurelian (26.11.2026) |
| 11 | Cum se compară ofertele | Prețul comparabil în lei (reducere, curs BNR, transport repartizat pe valoare, UM de bază); scor ponderat opțional pe firmă (70/15/10/5); atribuire pe linie, cu împărțire între furnizori; motiv obligatoriu când nu se alege cea mai bună ofertă sau, peste 20.000 lei, când sunt sub 3 oferte | 2 regulile 8–9, 3.5.1, 3.6, 4.5 | Decis de Aurelian (26.11.2026) |
| 12 | Aprobarea atribuirii | Fără aprobare separată; comanda generată trece prin aprobarea din A3, iar aprobatorul vede instantaneul atribuirii | 4.5 | Decis de Aurelian (26.11.2026) |
| 13 | Comanda din ofertă și necesarul | `CF-OF` consumă `ORDER` și pe linia de necesar de la originea cererii, până la restul ei | 4.6 | Decis de Aurelian (26.11.2026) |

# ERP · 02.04 Achiziții · A1 Configurare: tranzacții, furnizori, catalog și prețuri

26 noiembrie 2026 · Aurelian · **✓ Validat (26.11.2026)**, cu ecranele și toate cele 10 decizii. Prima din cele 7 părți ale modulului 02.04 Achiziții. Împărțirea A1–A7 și preluările din ERP-ul folosit anterior de Aurelian ([INSPIRATIE_WINDOWS_CLIENT.md](INSPIRATIE_WINDOWS_CLIENT.md), propunerile P1–P8) au fost confirmate de Aurelian pe 26.11.2026, împreună cu două decizii. D1: tipul de tranzacție și legăturile între documente stau în Nucleu, comune tuturor modulelor ([Nucleu §3.6](../01-nucleu/NUCLEU.md)). D2: la recepție se acceptă ambele ordini, aviz → NIR → factură și factură → NIR, plus „factura cu recepție”, care generează NIR-ul automat.

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`. Această parte acoperă configurarea pe care se sprijină restul modulului: tipurile de documente de achiziție și tranzacțiile livrate pentru ele, grupurile de consum, condițiile comerciale pe furnizor, catalogul furnizorului (codurile și denumirile lui legate de articolele noastre) cu regulile de potrivire, listele de prețuri ale furnizorilor, setările de achiziții ale firmei și achizitorii responsabili. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.2 Parteneri, §3.3 Articole, §3.3.1 Variante, §3.6 Tranzacții și legături), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C2 modele de contare, F3 decontări), [02.05 Stocuri](../05-stocuri/README.md) (S2 portul de stoc, S4 loturi și carantină, S5 disponibil proiectat). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Partenerul** e unic la nivel de instalare (`core.Partner`), cu rolul `IsSupplier`. Condițiile comerciale ale furnizorului se țin **per firmă**, în Achiziții (3.4). Un partener care nu e furnizor nu apare pe documentele de achiziție.
- **Stocul și liniile de document stau pe variantă** (`core.ItemVariant`, SKU), niciodată pe model (`core.Item`). Pe o linie de achiziție cu articol stocabil, `ItemVariantId` e obligatoriu (Nucleu §3.3.1). Cantitatea se ține în UM documentului și, calculat, în UM de bază a modelului. Factorii vin din setul de UM al articolului (`core.UomSet`).
- **Tipul de tranzacție** (`core.TransactionType`) și **legăturile între documente** (`core.DocumentLink`) sunt definite în Nucleu §3.6. Comportamentul fiecărei tranzacții e scris în cod. Clientul poate crea tranzacții proprii doar pe comportamentele livrate: altă denumire, alte drepturi, alte valori implicite, alt semnal pentru contare. Nu poate scrie SQL.
- **Achizițiile mișcă stocul sincron**, prin portul `IStockLedger` (Stocuri S2, decizia 1), în tranzacția SQL a documentului care are efect în stoc (NIR, retur la furnizor). Comenzile la furnizor nu mișcă stocul; ele apar doar în disponibilul proiectat (Stocuri S5).
- Contarea nu se face în Achiziții: documentele publică evenimente cu blocul `Accounting` (C2), iar partida furnizorului o deschide F3 la `FacturaFurnizorValidata`.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Cantitățile și prețurile se țin pe `decimal(19,6)`, valorile pe `decimal(19,4)` (`SQL-07`).
- **Ce NU face A1:** nu creează documente de achiziție (A2–A6) și nu descarcă facturi din SPV (Integrări 02.14). A1 definește doar configurarea pe care o citesc celelalte părți.

## 1. Scop și perimetru

| Zonă | Ce face A1 | Ce NU face (și cine face) |
| --- | --- | --- |
| Tipuri de documente | Lista tipurilor de documente ale achizițiilor, seriile implicite și partea care detaliază fiecare tip | Ciclul de viață al fiecărui document (A2–A6) |
| Tranzacții | Catalogul tranzacțiilor livrate pentru achiziții, cu sursele, grupul de consum, efectul în stoc și toleranța | Motorul de tranzacții și legături (Nucleu §3.6) |
| Furnizori | Condițiile comerciale ale furnizorului în firmă: monedă, termen de plată, termen de livrare, valoare minimă, achizitor, tranzacții implicite | Datele de identitate ale partenerului (Nucleu §3.2); partidele și plățile (F3) |
| Catalogul furnizorului | Codul, denumirea, GTIN-ul și UM-ul de la furnizor, legate de varianta noastră; cantitatea minimă, multiplul, termenul de livrare | Codurile de bare proprii (Nucleu §3.3) |
| Potrivire | Regulile de potrivire a liniilor primite de la furnizor (e-Factura, import de comenzi) cu variantele noastre | Inbox-ul de e-Factura primite (A5); descărcarea din SPV (Integrări) |
| Prețuri | Listele de prețuri ale furnizorilor, cu praguri de cantitate și reduceri; ordinea în care se propune prețul | Costul de achiziție final, cu transport și vamă (A6, Stocuri S3) |
| Setări | Setările de achiziții ale firmei: fluxul recepției, toleranțe, obligativitatea comenzii, recepția „oarbă”, potrivirea automată | Aprobările pe praguri (A2, A3) |
| Achizitori | Achizitorul responsabil pe furnizor și pe grupă de articole | Rolurile și drepturile (Nucleu) |

## 2. Model de domeniu

```
 Nucleu §3.6 (comun tuturor modulelor)
   TransactionType ── TransactionTypeSource × N ── TransactionTypeRole × N
   DocumentLink (linie sursă → linie destinație, cantitate, grup de consum)
   DocumentLineClosure · DocumentStatusLog

 Achiziții A1 (purch), per firmă
   PurchaseSettings (1 rând pe firmă)
   SupplierTerms (furnizor × firmă: monedă, termene, achizitor, tranzacții implicite)
   SupplierItem (furnizor × variantă: cod și denumire la furnizor, UM, minim, multiplu, termen)
      └─ folosit de potrivire, de comenzi (tipărite cu codurile furnizorului) și de propunerea de furnizor
   SupplierItemMatchRule (furnizor: tipare pe denumire → variantă, pentru servicii și linii fără cod)
   SupplierUomMap (furnizor: UM-ul lui sau codul UN/ECE → UM-ul nostru, când diferă de Nucleu)
   SupplierPriceList ── SupplierPriceListLine × N (variantă, UM, prag de cantitate, preț, reducere)
   BuyerAssignment (achizitor pe furnizor sau pe grupă de articole)

 Tipuri de documente (detaliate în A2–A6), fiecare cu tranzacțiile lui
   PurchaseRequest → PurchaseNeed → RequestForQuotation → SupplierQuotation → PurchaseOrder
   → SupplierDeliveryNote → GoodsReceipt (NIR) → SupplierInvoice → SupplierCreditNote
   SupplierReturn · SupplierProforma · ImportCustomsDeclaration (DVI) · LandedCostAllocation
```

Reguli de business ale modelului:

1. **Condițiile comerciale sunt per firmă.** Același furnizor poate avea termen de plată 30 de zile la Exemplu Distribuție și 60 la Exemplu Producție. Tot ce ține de relația comercială (3.4–3.6) are `CompanyId`.
2. **Catalogul furnizorului e pe variantă.** Un rând leagă un cod al furnizorului (într-o UM a lui) de exact o variantă a noastră. Aceeași variantă poate avea coduri la mai mulți furnizori. Același cod al furnizorului poate apărea în două UM-uri diferite (bucată și bax), ca două rânduri.
3. **UM-ul de cumpărare trebuie să existe în setul de UM al articolului** (sau ca excepție pe articol, `core.ItemUom`). Factorul către UM de bază nu se ia din catalog, ci din Nucleu, ca să nu existe două adevăruri. Catalogul doar alege UM-ul.
4. **O comandă se consumă separat la recepție și la facturare** (grupurile de consum `RECEIVE` și `INVOICE`, 3.3). Ce s-a recepționat și ce s-a facturat sunt două cantități independente pe aceeași linie de comandă. Diferența dintre ele e baza potrivirii comandă–NIR–factură din A5.
5. **Tranzacțiile proprii ale clientului sunt variante ale celor livrate.** O tranzacție nouă alege un comportament livrat (3.2) și moștenește de la el sursele, maparea și efectul în stoc. Poate schimba doar denumirea, drepturile, valorile implicite, toleranța (dacă comportamentul o permite) și semnalele pentru contare.
6. **Prețul propus nu e prețul facturat.** Lista de prețuri a furnizorului propune prețul pe comandă. Pe NIR prețul vine din comandă sau din factură, iar diferențele se tratează la potrivire (A5). Prețul din listă nu modifică niciodată costul din stoc.
7. **Nimic din configurare nu se șterge după folosire** (`SQL-20`): un rând de catalog, o listă de prețuri sau o tranzacție folosită se dezactivează.

## 3. Entități și setări

Convențiile comune tuturor tabelelor `purch`: cheie `Id` `int IDENTITY` pentru nomenclatoare și `bigint` pentru tabelele care cresc (`SQL-13`), coloanele de audit `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` (`SQL-19`) și `RowVersion` pe tot ce se editează din interfață (`SQL-11`, `SP-18`).

### 3.1 Tipurile de documente ale achizițiilor

Codul tipului de document e același în `core.NumberSeries`, în `core.TransactionType`, în `core.DocumentLink` și în `AccountingPayload` (C2).

| `DocumentType` | Document | Serie implicită | Stoc | Contare | Detaliat în |
| --- | --- | --- | --- | --- | --- |
| `PurchaseRequest` | Referat de necesitate (cerere de aprovizionare a unui departament) | `RF` | — | — | A2 |
| `PurchaseNeed` | Necesar de aprovizionare (consolidat din referate, stoc sub minim și MRP) | `NC` | — | — | A2 |
| `RequestForQuotation` | Cerere de ofertă către unul sau mai mulți furnizori | `CO` | — | — | A2 |
| `SupplierQuotation` | Ofertă primită de la furnizor (cu numărul ei) | `OF` intern | — | — | A2 |
| `PurchaseOrder` | Comandă la furnizor, cu confirmarea furnizorului pe linii | `CF` | Doar în disponibilul proiectat (S5) | — | A3 |
| `SupplierDeliveryNote` | Aviz de însoțire primit de la furnizor (cu numărul lui) | intern `AV` | — (intrarea o face NIR-ul) | — | A4 |
| `GoodsReceipt` | Notă de intrare-recepție (NIR), inclusiv recepția cu diferențe | `NIR` | Intrare (`RECEIPT_PURCHASE`) | Da, prin evenimentul de stoc (S3, C2) | A4 |
| `SupplierReturn` | Aviz de retur la furnizor | `RT` | Ieșire (`RETURN_TO_SUPPLIER`) | Da | A4 |
| `SupplierInvoice` | Factură de furnizor: marfă, servicii, import, intracomunitară, avans | Numărul furnizorului + număr de înregistrare `FF` | Doar „factura cu recepție” generează un NIR (D2) | Da (`FacturaFurnizorValidata`) | A5 |
| `SupplierCreditNote` | Factură de stornare sau de reducere primită | Numărul furnizorului + `FF` | Nu (returul fizic e `SupplierReturn`) | Da | A5 |
| `SupplierProforma` | Factură proforma, pentru plata în avans | Numărul furnizorului | — | — | A5 |
| `ImportCustomsDeclaration` | Declarație vamală de import (DVI), cu taxele pe cod vamal | Numărul vămii (MRN) | — | Da | A6 |
| `LandedCostAllocation` | Repartizarea costurilor de achiziție (transport, vamă, comisioane) pe liniile recepționate | `CA` | Corecție de cost (Stocuri S3, costuri adăugate) | Da | A6 |

Seriile implicite se creează la activarea modulului pentru fiecare firmă (`MIG-06`) și se pot schimba în Configurare › Documente › Serii (Nucleu). Documentele cu numărul furnizorului (factura, avizul, oferta) primesc în plus un număr intern de înregistrare, ca registrul documentelor primite să fie continuu.

**Stările comune** ale documentelor de achiziție (fiecare parte adaugă ce îi trebuie): 1 Ciornă · 2 De aprobat · 3 Validat (sau Confirmat, la comandă) · 4 Închis (toate liniile consumate sau închise manual) · 8 Stornat · 9 Anulat (doar din ciornă sau din „De aprobat”). Fiecare schimbare se scrie în `core.DocumentStatusLog` (Nucleu §3.6.3).

### 3.2 Tranzacțiile livrate

Comportamentele sunt scrise în cod (`ITransactionBehavior`, Nucleu §3.6.5) și se înregistrează la pornirea modulului. Pentru fiecare comportament se livrează o tranzacție de sistem (`IsSystem = 1`, `MIG-06`), care se poate redenumi sau dezactiva, dar nu șterge. Clientul poate adăuga oricâte tranzacții proprii pe același comportament (regula 5).

| Cod livrat | Denumire | Document destinație | Comportament (`Behavior`) | Mod | Surse (principală în **bold**) | Grup de consum | Efect în stoc | Toleranță implicită |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `RF-MAN` | Înregistrare referat | `PurchaseRequest` | `PurchaseRequest.Manual` | Manuală | — | — | — | — |
| `NC-GEN` | Necesar din referate și stoc sub minim | `PurchaseNeed` | `PurchaseNeed.FromRequests` | Generată | **`PurchaseRequest`** (aprobat) | `NEED` | — | 0 |
| `CO-MAN` | Cerere de ofertă | `RequestForQuotation` | `Rfq.Manual` | Manuală | — | — | — | — |
| `CO-NC` | Cerere de ofertă din necesar | `RequestForQuotation` | `Rfq.FromNeed` | Generată | **`PurchaseNeed`** | `RFQ` | — | 0 |
| `OF-MAN` | Înregistrare ofertă furnizor | `SupplierQuotation` | `Quotation.Manual` | Manuală | — | — | — | — |
| `OF-CO` | Ofertă furnizor din cerere de ofertă | `SupplierQuotation` | `Quotation.FromRfq` | Generată | **`RequestForQuotation`** | `QUOTE` | — | permisă |
| `CF-MAN` | Emitere comandă la furnizor | `PurchaseOrder` | `PurchaseOrder.Manual` | Manuală | — | — | Proiectat (S5) | — |
| `CF-OF` | Comandă din oferta câștigătoare | `PurchaseOrder` | `PurchaseOrder.FromQuotation` | Generată | **`SupplierQuotation`** (acceptată) | `ORDER` | Proiectat | 0 |
| `CF-NC` | Comandă din necesar | `PurchaseOrder` | `PurchaseOrder.FromNeed` | Generată | **`PurchaseNeed`** | `ORDER` | Proiectat | 0 |
| `AV-CF` | Aviz furnizor din comandă | `SupplierDeliveryNote` | `DeliveryNote.FromOrder` | Generată | **`PurchaseOrder`** (trimisă sau confirmată, precizat de A3) | `DELIVER` | — | permisă |
| `AV-MAN` | Înregistrare aviz furnizor | `SupplierDeliveryNote` | `DeliveryNote.Manual` | Manuală | — | — | — | — |
| `NIR-MAN` | Înregistrare NIR fără document sursă | `GoodsReceipt` | `GoodsReceipt.Manual` | Manuală | — | — | Intrare | — |
| `NIR-CF` | NIR din comanda furnizorului | `GoodsReceipt` | `GoodsReceipt.FromOrder` | Generată | **`PurchaseOrder`** (trimisă sau confirmată, precizat de A3) | `RECEIVE` | Intrare | permisă (setare 3.7) |
| `NIR-AV` | NIR din avizul furnizorului | `GoodsReceipt` | `GoodsReceipt.FromDeliveryNote` | Generată | **`SupplierDeliveryNote`**; comanda, prin legătura avizului | `RECEIVE` | Intrare | permisă |
| `NIR-FF` | NIR din factura furnizorului | `GoodsReceipt` | `GoodsReceipt.FromInvoice` | Generată | **`SupplierInvoice`** (liniile stocabile) | `RECEIVE` | Intrare | 0 |
| `NIR-IMP` | NIR din factura de import cu DVI și facturi auxiliare | `GoodsReceipt` | `GoodsReceipt.FromImport` | Generată | **`SupplierInvoice`** (import), `ImportCustomsDeclaration`, `SupplierInvoice` auxiliare (filtru `ITEM_KIND:SERVICE`) | `RECEIVE` | Intrare, cu cost majorat | 0 |
| `RT-NIR` | Retur la furnizor din NIR | `SupplierReturn` | `SupplierReturn.FromReceipt` | Generată | **`GoodsReceipt`** | `RETURN` | Ieșire | interzisă |
| `RT-MAN` | Retur la furnizor fără NIR | `SupplierReturn` | `SupplierReturn.Manual` | Manuală | — | — | Ieșire | — |
| `FF-MAN` | Factură de furnizor fără sursă (servicii, utilități, chirii) | `SupplierInvoice` | `SupplierInvoice.Manual` | Manuală | — | — | — | — |
| `FF-NIR` | Factură din NIR-uri | `SupplierInvoice` | `SupplierInvoice.FromReceipts` | Generată | **`GoodsReceipt`** (validat) | `INVOICE` | — | 0 |
| `FF-CF` | Factură din comandă (servicii, avansuri, „factura înaintea mărfii”) | `SupplierInvoice` | `SupplierInvoice.FromOrder` | Generată | **`PurchaseOrder`** | `INVOICE` | — | permisă |
| `FF-REC` | Factură cu recepție (generează și NIR-ul) | `SupplierInvoice` | `SupplierInvoice.WithReceipt` | Manuală sau din comandă | `PurchaseOrder` (opțional) | `INVOICE` + `RECEIVE` | Intrare, prin NIR-ul generat | permisă |
| `FF-EF` | Factură din e-Factura primită | `SupplierInvoice` | `SupplierInvoice.FromEInvoice` | Generată | **e-Factura primită** (inbox A5); opțional `PurchaseOrder`, `GoodsReceipt` | `INVOICE` | — sau intrare, dacă se alege recepția | 0 |
| `FS-FF` | Storno din factura furnizorului | `SupplierCreditNote` | `CreditNote.FromInvoice` | Generată | **`SupplierInvoice`** | `CREDIT` | — | interzisă |
| `FS-RT` | Factură de stornare pentru un retur | `SupplierCreditNote` | `CreditNote.FromReturn` | Generată | **`SupplierReturn`**; factura inițială, prin legături | `CREDIT` | — | interzisă |
| `FS-MAN` | Reducere comercială primită (fără retur) | `SupplierCreditNote` | `CreditNote.Discount` | Manuală | `SupplierInvoice` (opțional, pe antet) | — | Corecție de cost, dacă marfa e în stoc (S3) | — |
| `DVI-FF` | DVI din factura de import | `ImportCustomsDeclaration` | `Customs.FromImportInvoice` | Generată | **`SupplierInvoice`** (import, non-UE) | `CUSTOMS` | — | 0 |
| `CA-NIR` | Repartizare costuri de achiziție pe NIR-uri | `LandedCostAllocation` | `LandedCost.FromReceipts` | Generată | **`SupplierInvoice`** de servicii (transport, comisioane), `GoodsReceipt` (pe antet, valoare) | `LANDED` | Corecție de cost (S3) | 0 |

Note la tabel:

- „Permisă” înseamnă că tranzacția acceptă `OverTolerancePct` > 0. Valoarea implicită vine din setările firmei (3.7) și se poate schimba pe tranzacție. „Interzisă” înseamnă că toleranța e forțată la 0: nu se returnează și nu se stornează mai mult decât s-a primit sau s-a facturat.
- `FF-REC` (D2) e singura tranzacție care creează două documente într-o singură tranzacție SQL: factura și NIR-ul ei, legate pe linii. Validarea facturii validează și NIR-ul, deci stocul intră la data recepției de pe factură. Fluxul complet e în A5.
- Legătura „comandă → aviz → NIR” păstrează comanda ca sursă indirectă: NIR-ul din aviz consumă grupul `RECEIVE` pe liniile comenzii, prin legătura avizului, ca rămasul de primit pe comandă să fie corect oricare ar fi drumul.
- Tranzacțiile de plată (o plată pe o factură de furnizor) sunt ale Finanțelor (F1, F2, F3), cu sursa pe antet (`Level` = 2, valoare).

### 3.3 Grupurile de consum în achiziții

O linie sursă are câte un disponibil pentru fiecare grup de consum în care e folosită (Nucleu §3.6.2).

| Grup | Sursa consumată | Consumată de | Ce înseamnă rămasul |
| --- | --- | --- | --- |
| `NEED` | Linia de referat aprobat | Necesarul | Cât din referat nu e încă în necesar |
| `RFQ` | Linia de necesar | Cererea de ofertă | Cât nu s-a cerut încă la ofertă (un necesar poate merge la mai mulți furnizori, deci grupul e informativ: nu blochează) |
| `QUOTE` | Linia cererii de ofertă | Oferta furnizorului | Cât nu a ofertat furnizorul |
| `ORDER` | Linia de ofertă sau de necesar | Comanda | Cât nu s-a comandat încă |
| `DELIVER` | Linia de comandă | Avizul furnizorului | Cât nu a fost încă anunțat ca livrat |
| `RECEIVE` | Linia de comandă, de aviz sau de factură | NIR-ul | **Cât e încă de primit** (alimentează disponibilul proiectat din S5) |
| `INVOICE` | Linia de comandă sau de NIR | Factura | **Cât e încă de facturat** (recepții nefacturate, baza contului `408`) |
| `RETURN` | Linia de NIR | Returul la furnizor | Cât se mai poate returna |
| `CREDIT` | Linia de factură sau de retur | Stornarea | Cât se mai poate storna |
| `CUSTOMS` | Linia facturii de import | DVI-ul | Cât nu a trecut încă prin vamă |
| `LANDED` | Antetul facturii de servicii | Repartizarea costurilor | Valoarea încă nerepartizată |

Pe linia de comandă, Achizițiile afișează patru cantități derivate: comandată, confirmată (de furnizor, A3), recepționată (`RECEIVE`) și facturată (`INVOICE`). Niciuna nu se stochează pe linie; toate se calculează din `core.DocumentLink`. Excepție: pentru disponibilul proiectat, Stocuri citește „de primit” prin `IPurchaseOrderSupply` (6), care face calculul cu TVP.

### 3.4 Condițiile comerciale ale furnizorului (`SupplierTerms`)

Un rând pe furnizor și firmă. Se creează automat la primul document de achiziție cu furnizorul (cu valorile implicite ale firmei) sau manual, din fișa partenerului, tab-ul Achiziții.

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `PartnerId` | `int` | Partenerul trebuie să aibă `IsSupplier = 1` (`53002`) |
| `CurrencyCode` | `char(3)` | Moneda implicită a comenzilor și facturilor; o monedă activă în Nucleu |
| `PaymentTermDays` | `smallint` | Termenul de plată propus pe factură (scadența = data facturii + termen). Contractul poate cere altceva; factura are prioritate |
| `PaymentMethodId` | `int NULL` | Modalitatea de plată propusă pe factura furnizorului și la plată (FK `core.PaymentMethod`, nomenclatorul comun din Nucleu §3.5; precizare din Vânzări V1, 27.11.2026) |
| `LeadTimeDays` | `smallint` | Termenul de livrare implicit, în zile calendaristice; catalogul (3.5) îl poate preciza pe variantă |
| `MinOrderAmount` | `decimal(19,4) NULL` | Valoarea minimă a unei comenzi, în moneda furnizorului. Sub ea, comanda primește avertizare (nu blocare) |
| `Incoterm`, `IncotermPlace` | `char(3) NULL` (FK `core.DeliveryTerm`), `nvarchar(100) NULL` | Pentru importuri și achiziții intracomunitare (EXW, FCA, DAP...); propuse pe comandă, folosite la Intrastat (S7) și la costurile de achiziție (A6) (precizare din Achiziții A6, 27.11.2026) |
| `TransportModeCode`, `TransactionNatureCode` | `char(1) NULL` (FK `core.TransportMode`), `varchar(3) NULL` (FK `core.TransactionNature`) | Valorile implicite pentru comenzile și facturile furnizorului (Intrastat) (precizare din Achiziții A6, 27.11.2026) |
| `DefaultWarehouseId` | `int NULL` | Gestiunea de recepție propusă (altfel cea din setările firmei sau a articolului) |
| `BuyerUserId` | `int NULL` | Achizitorul responsabil (3.8) |
| `DefaultOrderTransactionTypeId`, `DefaultReceiptTransactionTypeId`, `DefaultInvoiceTransactionTypeId` | `int NULL` | Tranzacțiile propuse pentru acest furnizor (au prioritate față de cele implicite ale rolului, dacă rolul are voie să le folosească). Exemplu: furnizorul de utilități primește direct `FF-MAN` |
| `OrderChannel` | `tinyint` | 1 Email (comanda în PDF) · 2 Portal sau EDI (viitor) · 3 Telefon. Doar pentru afișare și pentru trimiterea din A3 |
| `OrderEmail` | `nvarchar(200) NULL` | Adresa la care pleacă comanda; altfel contactul cu rolul „comenzi” din Nucleu |
| `RequiresOrder` | `tinyint NULL` | Suprascrie setarea firmei (3.7): 0 nu, 1 avertizare, 2 blocare pentru facturile cu articole stocabile fără comandă |
| `ReceiptTolerancePct` | `decimal(9,4) NULL` | Suprascrie toleranța de recepție a firmei pentru acest furnizor |
| `ReceiptMinShelfLifePct` | `decimal(9,4) NULL` | Suprascrie pragul de termen rămas la recepție (adăugat de A4 §3.6) |
| `IsBlocked`, `BlockReason` | `bit`, `nvarchar(300)` | Furnizor blocat pentru comenzi noi (de exemplu calitate slabă sau litigiu). Facturile pentru comenzile deja trimise se pot înregistra. Blocarea vine și din evaluarea „Neaprobat” (A7 §3.3); deblocarea cere motiv, o face doar `Purch.Supplier.Block` și se scrie în jurnalul de excepții (A7 §3.5) (precizare din Achiziții A7, 27.11.2026) |
| `IsCritical` | `bit` | Furnizor critic: inclus mereu în campania de evaluare, indiferent de cheltuială (A7 §3.3) (precizare din Achiziții A7, 27.11.2026) |
| `Rating` | `tinyint NULL` | 1–5, setat manual. Scorul calculat (0–100) și clasa vin din A7 (`ISupplierPerformanceReader`); ratingul manual se folosește doar la furnizorii cu „date insuficiente” (precizare din Achiziții A7, 27.11.2026) |
| `Notes` | `nvarchar(1000) NULL` | — |

Partenerul inactiv la ANAF (`core.Partner.AnafInactive`) primește avertizare pe orice document de achiziție. TVA-ul pe facturile unui furnizor inactiv nu se deduce, regulă aplicată de C4.

### 3.5 Catalogul furnizorului (`SupplierItem`)

Leagă felul în care furnizorul numește un produs de varianta noastră. Este folosit la potrivirea facturilor e-Factura (A5), la importul comenzilor și ofertelor din Excel, la tipărirea comenzii cu codurile furnizorului (A3) și la propunerea furnizorului pe necesar (A2).

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int NULL` | Gol = valabil în toate firmele grupului (codurile furnizorului nu depind de firma care cumpără); altfel doar pentru firma dată |
| `PartnerId` | `int` | Furnizorul |
| `ItemVariantId` | `int` | Varianta noastră (SKU). Obligatoriu (regula 2) |
| `SupplierCode` | `nvarchar(60) NULL` | Codul furnizorului (în UBL: `SellersItemIdentification`). Comparat fără diferență de majuscule și fără spații (`SupplierCodeKey`, coloană calculată, `SQL-24`) |
| `SupplierName` | `nvarchar(300) NULL` | Denumirea de la furnizor, cum apare pe factură; folosită la potrivirea după denumire (`SupplierNameSearch`, `SQL-25`) |
| `Gtin` | `varchar(14) NULL` | Codul GTIN de la furnizor (în UBL: `StandardItemIdentification`, schema `0160`), dacă diferă de codurile de bare din Nucleu |
| `PurchaseUomId` | `int` | UM-ul în care cumpărăm de la acest furnizor, din setul de UM al articolului (regula 3, `53005`) |
| `MinOrderQty`, `OrderMultiple` | `decimal(19,6) NULL` | În `PurchaseUomId`. Comanda primește avertizare sub minim și rotunjire la multiplu (A3) |
| `LeadTimeDays` | `smallint NULL` | Precizează termenul din `SupplierTerms` pentru această variantă |
| `IsPreferred` | `bit` | Furnizorul preferat pentru variantă în firmă. Cel mult unul pe `(CompanyId, ItemVariantId)` (`53003`). Folosit de necesar (A2) |
| `CountryOfOriginCode` | `char(2) NULL` | Țara de origine declarată de furnizor (Intrastat, DVI) |
| `LastPrice`, `LastPriceCurrency`, `LastPurchaseDate` | | Calculate de A5 la validarea fiecărei facturi; doar citire |
| `Source` | `tinyint` | 1 Manual · 2 Import Excel · 3 Învățat la potrivire (4.3) |
| `IsActive` | `bit` | Un rând folosit nu se șterge |

**Reguli de potrivire pe denumire (`SupplierItemMatchRule`).** Pentru liniile fără cod: servicii, chirii, utilități. Un rând: `CompanyId`, `PartnerId`, `Pattern` (tipar simplu, cu `*` pentru orice text, de exemplu `Chirie * contract 12/2024*`; fără expresii regulate și fără SQL), `ItemVariantId`, `Priority`, `CostCenterId` și `ProjectId` propuse (opțional), `IsActive`. Tiparul se compară fără diferență de majuscule și de diacritice. Prima regulă potrivită, în ordinea priorității, câștigă.

**UM-ul furnizorului (`SupplierUomMap`).** Nucleu leagă fiecare UM de un cod UN/ECE (`UneceCode`), suficient pentru majoritatea facturilor. Unii furnizori trimit `H87` (bucată) pentru un bax de 12 sau `XBX` pentru o cutie. Maparea pe furnizor rezolvă asta: `PartnerId`, `SupplierUomCode` (codul UN/ECE sau textul primit), `ItemVariantId` (opțional, doar pentru o variantă), `UomId`. Are prioritate față de maparea generală din Nucleu.

### 3.6 Prețurile furnizorilor (`SupplierPriceList` + `SupplierPriceListLine`)

**Antet**: `CompanyId`, `PartnerId`, `Code`, `Name`, `CurrencyCode`, `PricesIncludeVat` (de regulă 0), `ValidFrom`, `ValidTo` (gol = fără termen), `SourceQuotationId` (oferta din care a fost creată, A2), `Status` (1 Ciornă, 2 Activă, 3 Expirată, 4 Înlocuită), `Notes`, atașamentul cu lista primită.

**Linie**: `ItemVariantId`, `UomId` (din setul articolului), `MinQty` (pragul de la care se aplică, în UM liniei; 0 = de la prima bucată), `UnitPrice` (`decimal(19,6)`, în moneda listei), `DiscountPct`, `ValidFrom` și `ValidTo` pe linie (opționale, pentru promoții).

Reguli:

- Două liste active ale aceluiași furnizor, în aceeași firmă și monedă, nu se pot suprapune în timp pe aceeași variantă și același prag (`53004`). O listă nouă care începe când alta e activă o „înlocuiește” de la data ei: cea veche primește `ValidTo` = ziua dinainte.
- Pragurile pe o variantă și un UM sunt crescătoare și unice (`53006`).
- Lista se poate crea manual, din Excel (Nucleu fluxul 4.8) sau dintr-o ofertă acceptată (A2).

**Ordinea în care se propune prețul pe o linie de comandă** (`ISupplierPriceProvider`, 6):

1. Contractul-cadru cu furnizorul (viitor, A3) dacă există.
2. Linia din lista activă a furnizorului la data comenzii, cu cel mai mare prag ≤ cantitatea comandată (convertită în UM liniei), în moneda comenzii. Dacă lista e în altă monedă, prețul se convertește la cursul zilei (Nucleu fluxul 4.3) și linia primește un semn „convertit”.
3. Ultimul preț de achiziție de la acest furnizor (`SupplierItem.LastPrice`), cu semnul „ultimul preț” și data.
4. Fără preț: linia rămâne de completat. Nu folosim un „preț standard de achiziție” pe articol, ca să nu existe un al patrulea adevăr (decizia 6).

Reducerea din listă (`DiscountPct`) se propune pe linie și rămâne vizibilă separat de preț, ca la factură să se poată compara prețul brut și reducerea.

### 3.7 Setările de achiziții ale firmei (`PurchaseSettings`)

Un rând pe firmă, creat la activarea modulului. Editat în Administrare › Configurare, grupul „Achiziții” (ecranul `a1-06`).

| Setare | Implicit | Ce înseamnă | Folosită în |
| --- | --- | --- | --- |
| `AllowInvoiceWithReceipt` | 1 | Tranzacția `FF-REC` (factura cu recepție) poate fi folosită (D2) | A5 |
| `ReceiptTolerancePct` | 0 | Toleranța implicită de depășire la recepție, față de comandă sau aviz. Se copiază în tranzacțiile noi cu toleranță permisă | A4 |
| `UnderDeliveryAutoClosePct` | 0 | O linie de comandă recepționată în proporție de cel puțin `100 − x`% se închide automat la validarea NIR-ului (0 = niciodată automat) | A3, A4 |
| `RequiresOrder` | 1 | Pentru facturile cu articole stocabile: 0 comanda nu e cerută · 1 avertizare · 2 blocare | A5 |
| `RequiresOrderAboveAmount` | — | Pragul (lei, fără TVA) de la care comanda e cerută și pentru servicii | A5 |
| `BlindReceipt` | 0 | Gestionarul nu vede cantitatea comandată pe NIR, doar o numără; diferențele apar după salvare | A4 |
| `ReceiptDateRule` | 1 | 1 Data NIR-ului = data recepției fizice · 2 data facturii (doar pentru `NIR-FF` și `FF-REC`) | A4, A5 |
| `PriceVarianceTolerancePct`, `PriceVarianceToleranceAmount` | 0,5 %, 10,00 lei | Diferența de preț acceptată fără aprobare la potrivirea comandă–NIR–factură (oricare e mai mică) | A5 |
| `QtyVarianceTolerancePct` | 0 | Diferența de cantitate factură vs. NIR acceptată fără aprobare | A5 |
| `EInvoiceAutoMatch` | 1 | Liniile e-Factura primite se potrivesc automat la intrarea în inbox (4.3) | A5 |
| `EInvoiceAutoLearn` | 1 | Potrivirea confirmată manual se salvează în catalogul furnizorului (`Source = 3`) | A5 |
| `EInvoiceAutoDraft` | 0 | Factura potrivită complet se creează direct ca ciornă (altfel așteaptă în inbox) | A5 |
| `DefaultReceiptWarehouseId` | — | Gestiunea de recepție propusă când nici furnizorul, nici articolul nu au una | A4 |
| `QuarantineOnReceipt` | după model | Informativ: carantina la recepție o cere modelul (Stocuri S4, `ReceiptQuarantine`; precizat de A4); aici doar se afișează | A4 |
| `ApprovalThresholds` | — | Pragurile de aprobare pe referate și comenzi; definite în A2 și A3 | A2, A3 |
| `OrderPdfTemplateId` | livrat | Șablonul de tipărire a comenzii (cu codurile furnizorului) | A3 |

Precedența pentru setările care există și pe furnizor (`RequiresOrder`, `ReceiptTolerancePct`): tranzacție (dacă e setată explicit) → furnizor → firmă.

### 3.8 Achizitorii responsabili (`BuyerAssignment`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `UserId` | Utilizatorul cu rolul Achizitor sau Șef achiziții în firmă |
| `PartnerId` sau `ItemGroupId` | Exact unul: achizitorul răspunde de un furnizor sau de o grupă de articole (și de subgrupele ei) |
| `IsPrimary` | Cel mult un responsabil principal pe furnizor și pe grupă |
| `SubstituteUserId`, `ValidFrom`, `ValidTo` | Înlocuitor pe perioade (concediu) |

Folosire: necesarul (A2) se împarte pe achizitori după grupa articolului; referatele aprobate ajung în „De rezolvat” la achizitorul grupei; notificările despre o comandă (confirmare întârziată, livrare parțială) merg la achizitorul furnizorului. `SupplierTerms.BuyerUserId` e o scurtătură pentru rândul principal pe furnizor.

## 4. Fluxuri principale

### 4.1 Crearea unei tranzacții proprii

Exemplu: Exemplu Distribuție vrea ca recepțiile de apă din Cluj să folosească o toleranță de 2% (furnizorul livrează pe baxuri și paleți întregi) și să fie vizibile separat la contare.

1. Administratorul deschide Configurare › Documente › Tranzacții, filtrează pe NIR și alege „Copiază” pe `NIR-CF`.
2. Tranzacția nouă `NIR-CF-PAL` „NIR din comandă, paleți întregi” moștenește comportamentul `GoodsReceipt.FromOrder`, sursele și grupul `RECEIVE`. Comportamentul nu se poate schimba (`50029` după prima folosire).
3. Setează `OverTolerancePct = 2`, semnalul de contare `X_PALET` (opțional, dacă C2 are o variantă pe el) și valoarea implicită „gestiune = DEP-CJ”.
4. În tab-ul Roluri bifează rolul Gestionar și o face implicită pentru el pe NIR.
5. La salvare se verifică: comportamentul există și e al tipului de document, cheile din valorile implicite sunt permise, nu există altă tranzacție implicită pentru același rol (`50030`). Modificarea se auditează.

### 4.2 Generarea unui document din surse (exemplu complet)

Comanda `CF 2280` din 18.11.2026 către Izvorul Carpaților SA cere 3.840 buc APA05 (320 baxuri de 12) la 1,2100 lei și 1.200 buc APA15 la 2,0500 lei, livrare în DEP-CJ. Pe 25.11 a sosit prima jumătate (avizul AV IC 5521 și NIR 4486: 1.920 buc APA05, lot L2611-15, în carantină, și 600 buc APA15). Pe 26.11 sosește restul, 161 de baxuri în loc de 160.

1. Andreea Pop (Gestionar DEP-CJ) creează un NIR. Tranzacția implicită a rolului ei e `NIR-CF-PAL`; asistentul pornește la pasul 2.
2. Pasul 2 arată comenzile confirmate ale furnizorilor cu rest de primit în DEP-CJ. `CF 2280` are 1.920 buc de primit. O bifează.
3. Pasul 3 arată liniile. Pe APA05: comandat 3.840, primit 1.920, rămas 1.920, de primit propus 1.920. Ea scrie 1.932 (161 baxuri). Maximul preluabil e 1.920 + 3.840 × 2% = 1.996,8, deci rândul se colorează în galben „+12 buc, 0,31% din linie, în toleranță”. APA15 se preia integral (600).
4. „Generează”: Achizițiile creează NIR-ul cu linia mapată din comandă (variantă, UM, preț, gestiune) și cer `IDocumentLinkService.ConsumeAsync` cu starea „în ciornă”. Rămasul pe comandă devine 0 (−12, afișat ca „depășit cu 12”).
5. Ea completează lotul și termenul (Stocuri S4). Modelul APA05 cere carantină la recepție, deci lotul nou intră în carantină.
6. La validare, într-o singură tranzacție SQL: numărul NIR-ului (Nucleu fluxul 4.2), mișcarea de stoc prin `IStockLedger.PostAsync`, activarea legăturilor, istoricul stării, evenimentul `ReceptieNIRValidata` în outbox. Comanda trece în „Închisă” (toate liniile primite). Dacă oricare pas eșuează, nu se salvează nimic.
7. Prima livrare a fost facturată pe 25.11 (`FF IC 77288`, generată cu `FF-NIR` din NIR 4486). Factura celei de a doua livrări se va genera tot cu `FF-NIR`, din NIR 4487 (grupul `INVOICE`). Pe comandă rămân de facturat 1.932 buc APA05 și 600 buc APA15 până atunci (ecranul `a1-03`).

### 4.3 Potrivirea unei linii primite cu varianta noastră

Folosită de inbox-ul e-Factura (A5) și de importul de oferte și comenzi. Intrare: furnizorul (deja identificat după CUI), codul vânzătorului, codul cumpărătorului, GTIN-ul, denumirea, UM-ul (cod UN/ECE), cantitatea și prețul. Ordinea, oprită la prima potrivire sigură:

| Pas | Criteriu | Încredere |
| --- | --- | --- |
| 1 | `BuyersItemIdentification` = codul variantei noastre (furnizorul ne cunoaște codul) | Sigură |
| 2 | GTIN = `SupplierItem.Gtin` sau un cod de bare din `core.ItemBarcode` | Sigură |
| 3 | Codul vânzătorului + UM = un rând `SupplierItem` al furnizorului cu același UM (după `SupplierUomMap`, apoi `UneceCode`) | Sigură |
| 4 | Codul vânzătorului = un singur rând `SupplierItem`, în alt UM | Sigură, cu UM de confirmat dacă factorul nu se deduce |
| 5 | Regulile pe denumire ale furnizorului (`SupplierItemMatchRule`), în ordinea priorității | Sigură (regula a fost scrisă de un om) |
| 6 | Denumirea identică cu `SupplierName` dintr-un rând de catalog al furnizorului, după normalizare (fără diferență de majuscule, diacritice și spații multiple) | Sigură (rândul a fost scris sau confirmat de un om) |
| 6b | Denumirea doar conține sau e conținută de `SupplierName` (minim 12 caractere comune) | Probabilă: se propune, nu se aplică |
| 7 | Linia comenzii deschise legate (dacă factura citează comanda, `OrderReference`) cu aceeași cantitate și același preț | Probabilă: se propune |

La „probabilă” sau fără potrivire, linia rămâne în inbox pentru om. Când utilizatorul confirmă o potrivire și `EInvoiceAutoLearn = 1`, se creează sau se completează rândul `SupplierItem` (`Source = 3`), deci data viitoare potrivirea e sigură: la pasul 3 dacă linia are cod de furnizor, la pasul 6 dacă are doar denumire. Fiecare potrivire automată păstrează pasul care a găsit-o, afișat lângă linie.

### 4.4 Importul catalogului și al prețurilor din Excel

Prin importul comun al Nucleului (fluxul 4.8), cu două șabloane noi: „Catalog furnizor” (furnizor, SKU-ul nostru, cod și denumire la furnizor, GTIN, UM, minim, multiplu, termen) și „Listă de prețuri furnizor” (antet pe foaia 1, linii pe foaia 2). Rândurile se validează ca în interfață; un SKU inexistent sau un UM care nu e în setul articolului dau erori pe rând.

### 4.5 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Același cod al furnizorului pentru două variante (furnizorul refolosește coduri) | Al doilea rând activ cu același cod și UM e refuzat (`53001`). Varianta veche se dezactivează întâi, cu `ValidTo` implicit |
| Furnizorul își schimbă codurile | Se importă rândurile noi; cele vechi rămân pentru facturile vechi, dezactivate |
| O variantă dezactivată în Nucleu are rânduri în catalog | Rândurile rămân, dar potrivirea nu le mai propune; inbox-ul cere altă variantă |
| Partenerul pierde rolul de furnizor | Nucleu refuză debifarea dacă există documente de achiziție deschise; altfel rândurile de catalog și listele de prețuri rămân, inactive |
| Lista de prețuri e în EUR, comanda în lei | Prețul se convertește la cursul BNR al zilei comenzii, cu semnul „convertit”; comanda poate fi și în EUR, după `SupplierTerms` |
| Tranzacția implicită a rolului a fost dezactivată | La creare se propune prima tranzacție activă a rolului; lipsa oricărei tranzacții active dă `53009` cu trimitere spre Configurare |
| Două ciorne de NIR din aceeași comandă, deschise în paralel | Prima rezervă cantitatea („în ciornă”, Nucleu §3.6 regula 5); a doua vede rămasul redus și, la depășire, primește `50027` |
| Comanda din care s-a recepționat parțial trebuie modificată | Liniile deja preluate nu se mai modifică (`50028`); se adaugă linii noi sau se închide restul (Nucleu `DocumentLineClosure`) |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `CatalogFurnizorModificat` v1 | Salvarea, importul sau învățarea unui rând de catalog | `CompanyId`, `PartnerId`, `ItemVariantId`, `SupplierCode`, `PurchaseUomId`, `IsPreferred`, `IsActive` | MRP (furnizorul preferat și termenul de livrare), Raportare |
| `PreturiFurnizorModificate` v1 | Activarea, înlocuirea sau expirarea unei liste | `CompanyId`, `PartnerId`, `PriceListId`, `ValidFrom`, `ValidTo`, `CurrencyCode`, numărul de linii | MRP (costul planificat), Raportare |
| `FurnizorBlocat` v1 | Blocarea sau deblocarea furnizorului în firmă | `CompanyId`, `PartnerId`, `IsBlocked`, `BlockReason` | Finanțe (avertizare la plăți), MRP, Raportare |

### 5.2 Consumate

| Eveniment | De la | Ce face A1 |
| --- | --- | --- |
| `PartenerModificat` v1 | Nucleu | La `IsSupplier` 1 → 0: dezactivează rândurile de catalog și listele de prețuri ale partenerului. La `AnafInactive` = 1: notifică achizitorul furnizorului |
| `ArticolModificat` v1 | Nucleu | La dezactivarea unei variante: marchează rândurile de catalog ca „variantă inactivă” (raportul 10) |
| `FirmaModificata` v1 | Nucleu | La activarea modulului pe o firmă: `PurchaseSettings` cu valorile implicite și seriile din 3.1 |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A1)

```csharp
public enum PurchaseDocumentType : byte
{
    PurchaseRequest = 1, PurchaseNeed = 2, RequestForQuotation = 3, SupplierQuotation = 4, PurchaseOrder = 5,
    SupplierDeliveryNote = 6, GoodsReceipt = 7, SupplierReturn = 8, SupplierInvoice = 9, SupplierCreditNote = 10,
    SupplierProforma = 11, ImportCustomsDeclaration = 12, LandedCostAllocation = 13
}
public enum RequiresOrderPolicy : byte { No = 0, Warn = 1, Block = 2 }
public enum MatchConfidence : byte { None = 0, Probable = 1, Certain = 2 }
public enum MatchStep : byte { BuyerCode = 1, Gtin = 2, SupplierCodeAndUom = 3, SupplierCode = 4, NameRule = 5, CatalogName = 6, CatalogNamePartial = 8, OpenOrderLine = 7 }

public sealed record SupplierTermsDto(int CompanyId, int PartnerId, string CurrencyCode, short PaymentTermDays, int? PaymentMethodId,
    short LeadTimeDays, decimal? MinOrderAmount, string? Incoterm, int? DefaultWarehouseId, int? BuyerUserId,
    RequiresOrderPolicy? RequiresOrder, decimal? ReceiptTolerancePct, bool IsBlocked);

public sealed record SupplierItemDto(int Id, int? CompanyId, int PartnerId, int ItemVariantId, string? SupplierCode,
    string? SupplierName, string? Gtin, int PurchaseUomId, decimal? MinOrderQty, decimal? OrderMultiple,
    short? LeadTimeDays, bool IsPreferred, decimal? LastPrice, string? LastPriceCurrency, DateOnly? LastPurchaseDate);

public sealed record IncomingLine(string? SellerCode, string? BuyerCode, string? Gtin, string Name,
    string? UomCode, decimal Qty, decimal? UnitPrice, string? OrderReference);
public sealed record MatchResult(int? ItemVariantId, int? UomId, MatchConfidence Confidence, MatchStep? Step,
    int? SupplierItemId, int? SuggestedCostCenterId, int? SuggestedProjectId);

// Citit de Achiziții (A2–A6), Integrări (pre-potrivire la descărcare), MRP
public interface ISupplierCatalog
{
    Task<SupplierTermsDto> GetTermsAsync(int companyId, int partnerId, CancellationToken ct);   // cu valorile firmei, dacă rândul lipsește
    Task<IReadOnlyList<SupplierItemDto>> ListForVariantAsync(int companyId, int itemVariantId, CancellationToken ct);
    Task<IReadOnlyList<MatchResult>> MatchAsync(int companyId, int partnerId, IReadOnlyList<IncomingLine> lines, CancellationToken ct);
    Task LearnAsync(int companyId, int partnerId, IncomingLine line, int itemVariantId, int uomId, CancellationToken ct);
}

public sealed record ProposedPrice(decimal UnitPrice, string CurrencyCode, decimal DiscountPct,
    byte Source /* 1 contract, 2 listă, 3 ultimul preț */, int? PriceListId, bool IsConverted, DateOnly? PriceDate);

public interface ISupplierPriceProvider
{
    Task<IReadOnlyDictionary<int, ProposedPrice?>> ProposeAsync(int companyId, int partnerId, DateOnly date,
        string currencyCode, IReadOnlyList<(int ItemVariantId, int UomId, decimal Qty)> lines, CancellationToken ct);
}

// Citit de Stocuri S5 (disponibil proiectat) și de MRP: ce e încă de primit pe comenzi
public interface IPurchaseOrderSupply
{
    Task<IReadOnlyList<PurchaseSupplyRow>> GetOpenSupplyAsync(int companyId, IReadOnlyCollection<int> itemVariantIds,
        int? warehouseId, CancellationToken ct);
}
public sealed record PurchaseSupplyRow(long PurchaseOrderId, string Number, int PartnerId, int ItemVariantId,
    int WarehouseId, decimal OpenQtyBase, DateOnly ExpectedDate, bool IsConfirmed);

public interface IPurchaseSettingsProvider
{
    Task<PurchaseSettingsDto> GetAsync(int companyId, CancellationToken ct);
}
```

Comportamentele din 3.2 sunt clase interne ale modulului (`Erp.Purchasing.Application.Behaviors`), care implementează `ITransactionBehavior` din Nucleu și se înregistrează în DI. Fiecare are un test care verifică maparea câmpurilor sursă → destinație (12).

Comenzi interne A1: `SaveSupplierTerms`, `BlockSupplier`, `UnblockSupplier`, `SaveSupplierItem`, `DeactivateSupplierItem`, `SaveMatchRule`, `SaveSupplierUomMap`, `SaveSupplierPriceList`, `ActivateSupplierPriceList`, `SavePurchaseSettings`, `SaveBuyerAssignment`. Tranzacțiile se salvează prin comenzile Nucleului (`SaveTransactionType`, `CopyTransactionType`).

## 7. Schema SQL `purch` (partea A1)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.PurchaseSettings` | `Id int` | UQ `CompanyId`; FK `CompanyId`, `DefaultReceiptWarehouseId`, `OrderPdfTemplateId` | — |
| `purch.SupplierTerms` | `Id int` | UQ `(CompanyId, PartnerId)`; FK `CompanyId`, `PartnerId`, `CurrencyCode`, `DefaultWarehouseId`, `BuyerUserId`, cele trei tranzacții | `(CompanyId, BuyerUserId)` |
| `purch.SupplierItem` | `Id int` | UX `(PartnerId, CompanyKey, SupplierCodeKey, PurchaseUomId) WHERE SupplierCode IS NOT NULL AND IsActive = 1`; UX `(CompanyKey, ItemVariantId) WHERE IsPreferred = 1 AND IsActive = 1`; FK `CompanyId`, `PartnerId`, `ItemVariantId`, `PurchaseUomId` | `(PartnerId, Gtin) WHERE Gtin IS NOT NULL`; `(ItemVariantId) INCLUDE (PartnerId, IsPreferred, LeadTimeDays)`; `(PartnerId, SupplierNameSearch)` |
| `purch.SupplierItemMatchRule` | `Id int` | FK `CompanyId`, `PartnerId`, `ItemVariantId`, `CostCenterId`, `ProjectId` | `(PartnerId, IsActive, Priority)` |
| `purch.SupplierUomMap` | `Id int` | UX `(PartnerId, SupplierUomCode, VariantKey)`; FK `PartnerId`, `ItemVariantId`, `UomId` | — |
| `purch.SupplierPriceList` | `Id int` | UX `(CompanyId, PartnerId, CodeKey)`; FK `CompanyId`, `PartnerId`, `CurrencyCode`, `SourceQuotationId` | `(CompanyId, PartnerId, Status, ValidFrom)` |
| `purch.SupplierPriceListLine` | `Id int` | UQ `(PriceListId, ItemVariantId, UomId, MinQty, LineValidFrom)`; FK `PriceListId` (cascadă doar pe ciornă, `SQL-15`), `ItemVariantId`, `UomId` | `(ItemVariantId) INCLUDE (PriceListId, UnitPrice, MinQty)` |
| `purch.BuyerAssignment` | `Id int` | UX `(CompanyId, PartnerId) WHERE IsPrimary = 1 AND PartnerId IS NOT NULL`; UX `(CompanyId, ItemGroupId) WHERE IsPrimary = 1 AND ItemGroupId IS NOT NULL`; CK exact unul din `PartnerId`, `ItemGroupId`; FK `UserId`, `SubstituteUserId` | `UserId` |

`CompanyKey` și `VariantKey` sunt coloane calculate persistente (`ISNULL(CompanyId, 0)`, `ISNULL(ItemVariantId, 0)`), ca unicitatea să trateze „toate firmele” și „toate variantele” ca o valoare.

Exemplu de tabel complet:

```sql
CREATE TABLE purch.SupplierItem (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierItem PRIMARY KEY,
    CompanyId            int               NULL CONSTRAINT FK_SupplierItem_Company REFERENCES core.Company(Id),
    CompanyKey           AS ISNULL(CompanyId, 0) PERSISTED,
    PartnerId            int               NOT NULL CONSTRAINT FK_SupplierItem_Partner REFERENCES core.Partner(Id),
    ItemVariantId        int               NOT NULL CONSTRAINT FK_SupplierItem_ItemVariant REFERENCES core.ItemVariant(Id),
    SupplierCode         nvarchar(60)      NULL,
    SupplierCodeKey      AS UPPER(REPLACE(SupplierCode, N' ', N'')) PERSISTED,                -- SQL-24
    SupplierName         nvarchar(300)     NULL,
    SupplierNameSearch   AS (SupplierName COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED, -- SQL-25
    Gtin                 varchar(14)       NULL,
    PurchaseUomId        int               NOT NULL CONSTRAINT FK_SupplierItem_Uom REFERENCES core.UnitOfMeasure(Id),
    MinOrderQty          decimal(19,6)     NULL,
    OrderMultiple        decimal(19,6)     NULL,
    LeadTimeDays         smallint          NULL,
    IsPreferred          bit               NOT NULL CONSTRAINT DF_SupplierItem_IsPreferred DEFAULT (0),
    CountryOfOriginCode  char(2)           NULL CONSTRAINT FK_SupplierItem_Country REFERENCES core.Country(Code),
    LastPrice            decimal(19,6)     NULL,
    LastPriceCurrency    char(3)           NULL,
    LastPurchaseDate     date              NULL,
    Source               tinyint           NOT NULL CONSTRAINT DF_SupplierItem_Source DEFAULT (1),   -- 1 manual, 2 import, 3 învățat
    IsActive             bit               NOT NULL CONSTRAINT DF_SupplierItem_IsActive DEFAULT (1),
    CreatedAt            datetime2(3)      NOT NULL CONSTRAINT DF_SupplierItem_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy            int               NOT NULL,
    ModifiedAt           datetime2(3)      NULL,
    ModifiedBy           int               NULL,
    RowVersion           rowversion        NOT NULL,
    CONSTRAINT CK_SupplierItem_Identity CHECK (SupplierCode IS NOT NULL OR SupplierName IS NOT NULL OR Gtin IS NOT NULL),
    CONSTRAINT CK_SupplierItem_Qty CHECK (ISNULL(MinOrderQty, 0) >= 0 AND ISNULL(OrderMultiple, 1) > 0)
);

CREATE UNIQUE INDEX UX_SupplierItem_Code ON purch.SupplierItem (PartnerId, CompanyKey, SupplierCodeKey, PurchaseUomId)
    WHERE SupplierCode IS NOT NULL AND IsActive = 1;
CREATE UNIQUE INDEX UX_SupplierItem_Preferred ON purch.SupplierItem (CompanyKey, ItemVariantId)
    WHERE IsPreferred = 1 AND IsActive = 1;
CREATE INDEX IX_SupplierItem_Variant ON purch.SupplierItem (ItemVariantId) INCLUDE (PartnerId, IsPreferred, LeadTimeDays);
```

Procedura de potrivire (`purch.usp_SupplierItem_Match`) primește liniile ca TVP (`SP-07`) și întoarce, pe linie, varianta, UM-ul, încrederea și pasul (4.3). Pașii 1–4, 6 și 6b sunt căutări pe indecși; pasul 5 (tipare) se face în aplicație, pe regulile furnizorului încărcate o dată (de obicei sub 50), ca să nu construim SQL dinamic din tipare (`SP-11`).

Proceduri: `purch.usp_SupplierTerms_Save`, `purch.usp_SupplierItem_Save`, `purch.usp_SupplierItem_Match`, `purch.usp_SupplierItem_Learn`, `purch.usp_SupplierPriceList_Save`, `purch.usp_SupplierPriceList_Activate` (închide lista înlocuită în aceeași tranzacție), `purch.usp_SupplierPrice_Propose` (TVP), `purch.usp_PurchaseSettings_Save`, `purch.usp_BuyerAssignment_Save`, `purch.usp_PurchaseOrderSupply_Open` (TVP, pentru S5).

Plaja de erori `53000–53999` se împarte pe părți: A1 `53000–53099`, A2 `53100–53199`, A3 `53200–53299`, A4 `53300–53449`, A5 `53450–53649`, A6 `53650–53799`, A7 `53800–53899`, rezervă `53900–53999`. Codurile API au prefixul `PURCH_` (`NET-09`).

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53001 | `PURCH_SUPPLIER_ITEM_DUPLICATE` | 409 | Același cod de furnizor, în același UM, e activ la altă variantă |
| 53002 | `PURCH_PARTNER_NOT_SUPPLIER` | 409 | Partenerul nu are rolul de furnizor |
| 53003 | `PURCH_PREFERRED_SUPPLIER_DUPLICATE` | 409 | Varianta are deja un furnizor preferat în firmă |
| 53004 | `PURCH_PRICE_LIST_OVERLAP` | 409 | Două liste active se suprapun pe aceeași variantă și același prag |
| 53005 | `PURCH_UOM_NOT_IN_ITEM_SET` | 400 | UM-ul de cumpărare nu e în setul de UM al articolului și nici excepție pe articol |
| 53006 | `PURCH_PRICE_BREAK_INVALID` | 400 | Praguri de cantitate duplicate sau negative |
| 53007 | `PURCH_MATCH_RULE_INVALID` | 400 | Tipar gol, doar `*` sau mai lung de 200 de caractere |
| 53008 | `PURCH_SETTINGS_INVALID` | 400 | Toleranță în afara intervalului 0–100 sau prag negativ |
| 53009 | `PURCH_NO_ACTIVE_TRANSACTION` | 409 | Utilizatorul nu are nicio tranzacție activă pentru tipul de document |
| 53010 | `PURCH_SUPPLIER_BLOCKED` | 409 | Comandă nouă pentru un furnizor blocat în firmă |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.SupplierTerms.View` / `.Manage` | Condițiile furnizorului în firmă, tab-ul Achiziții din fișa partenerului |
| `Purch.Supplier.Block` | Blocarea și deblocarea furnizorului |
| `Purch.SupplierItem.Manage` | Catalogul furnizorului, regulile de potrivire și maparea UM-urilor |
| `Purch.PriceList.View` / `.Manage` | Listele de prețuri ale furnizorilor |
| `Purch.Settings.Manage` | Setările de achiziții ale firmei |
| `Purch.Buyer.Manage` | Achizitorii responsabili |

Roluri livrate (noi, în Nucleu, pentru modulul Achiziții): **Șef achiziții** (toate permisiunile A1, plus aprobările din A2 și A3), **Achizitor** (`SupplierTerms.Manage`, `SupplierItem.Manage`, `PriceList.Manage`, `View` pe rest), **Gestionar** (primește din A4 dreptul pe NIR; din A1 doar `PriceList.View`, fără prețuri dacă recepția e „oarbă”), **Contabil furnizori** (`View` pe tot A1, plus `SupplierItem.Manage` pentru potrivirea facturilor). Tranzacțiile se administrează cu `Core.TransactionType.Manage` (Nucleu).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A1 |
| --- | --- |
| Codul fiscal art. 319 și OUG 120/2021 (e-Factura, RO_CIUS, EN 16931) | Câmpurile din catalog corespund identificatorilor UBL: `SellersItemIdentification`, `BuyersItemIdentification`, `StandardItemIdentification` (GTIN, schema `0160`), `UnitCode` UN/ECE Rec. 20. Potrivirea (4.3) le folosește în această ordine |
| OMFP 2634/2015 (documentele financiar-contabile) | Tipurile de documente din 3.1: NIR-ul ca document de recepție obligatoriu pentru bunurile intrate, cu numerotare continuă (seria `NIR`); avizul și factura cu numărul emitentului |
| Codul fiscal art. 11 (parteneri inactivi) | Avertizare pe documentele de achiziție; TVA-ul nu se deduce (C4) |
| Legea 72/2013 (întârzieri la plată între profesioniști) | Termenul de plată propus pe furnizor; peste 60 de zile, avertizare la salvare (termenul trebuie stabilit expres în contract) |
| Intrastat (INS) și regulile DVI | `Incoterm` pe furnizor și țara de origine pe catalog, preluate pe documente (S7, A6) |

Integrări externe: niciuna în A1. Interne: Nucleu (parteneri, variante, UM, tranzacții, import), Integrări (pre-potrivirea la descărcarea din SPV folosește `ISupplierCatalog.MatchAsync`), Stocuri S5 (`IPurchaseOrderSupply`), MRP (furnizor preferat, termen, preț).

**Completare la C2 (Finanțe), de confirmat cu această parte:** lista de semnale (`Flags`) din `AccountingPayload` acceptă și semnale proprii ale clientului, cu prefixul `X_`, puse de tranzacții (`PostingFlags`). Variantele modelului de contare le pot cere sau interzice, ca pe cele livrate. Fără variantă care să le ceară, semnalele proprii nu schimbă nimic.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Tranzacții folosite | Pe tip de document: tranzacțiile, numărul de documente pe lună, rolurile care le folosesc; tranzacțiile active nefolosite de peste 12 luni | Administrator, Șef achiziții |
| Catalog incomplet | Variantele cumpărate în ultimele 12 luni fără rând de catalog la furnizorul de la care au fost cumpărate; rândurile pe variante inactive | Achizitor |
| Potriviri automate | Pe furnizor: liniile e-Factura potrivite automat pe pași, procentul de potriviri sigure, rândurile învățate în perioadă | Contabil furnizori, Șef achiziții |
| Liste de prețuri care expiră | Listele care expiră în următoarele 30 de zile și variantele fără preț valabil la furnizorul preferat | Achizitor |
| Condiții furnizori | Furnizorii firmei cu moneda, termenul de plată și de livrare, achizitorul, starea (blocat, inactiv ANAF) | Șef achiziții, Contabil |

Rapoartele operaționale ale achizițiilor (comenzi deschise, recepții nefacturate, performanța furnizorilor) sunt în A7.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a1-configurare/](ecrane/a1-configurare/). Meniul Achiziții are sus Comenzi, Recepții (NIR) și Facturi, apoi grupurile Necesar, Documente, Import, Furnizori și Rapoarte. Grupul Furnizori conține Condiții furnizori, Catalog furnizori și Prețuri furnizori.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Configurare › Documente › Tranzacții | Tab-ul nou al ecranului comun Configurare (Nucleu §11.1): lista tranzacțiilor filtrată pe modul și tip de document; fișa tranzacției cu tab-urile General, Surse, Roluri, Contare, Istoric | `a1-01-tranzactii` |
| Asistentul de generare | Pasul 3 al asistentului (Nucleu §3.6.4) pe un NIR din comandă: liniile cu comandat, primit, rămas, de primit, toleranța folosită | `a1-02-asistent-generare` |
| Drumul documentului | Panoul „Legături” al unei comenzi: documentele din amonte și din aval, pe linii, cu cantitățile pe grupuri de consum și liniile închise | `a1-03-drumul-documentului` |
| Catalog furnizori | Catalogul unui furnizor: codurile și denumirile lui, UM-ul, minimul, multiplul, termenul, ultimul preț, sursa rândului; tab-urile Reguli de potrivire și UM furnizor | `a1-04-catalog-furnizor` |
| Prețuri furnizori | Lista de prețuri a unui furnizor cu pragurile de cantitate, reducerile și propunerea de preț pentru o cantitate | `a1-05-preturi-furnizor` |
| Configurare › Achiziții | Setările firmei din 3.7, grupate: Recepție, Facturi și potrivire, e-Factura, Comenzi | `a1-06-configurare-achizitii` |

Condițiile furnizorului (3.4) apar ca tab „Achiziții” pe fișa partenerului din Nucleu, pentru firma curentă, cu aceleași câmpuri ca tabelul; nu au machetă separată.

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A1-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A1-A1 | Fiecare comportament din 3.2 are o tranzacție de sistem după migrare; o tranzacție de sistem nu se poate șterge (`50010`), doar dezactiva | Test de integrare |
| A1-A2 | Copierea `NIR-CF` păstrează comportamentul și sursele; schimbarea comportamentului după primul NIR e refuzată (`50029`) | Test de integrare (cu Nucleu) |
| A1-A3 | Pe `CF 2280` (3.840 buc), după NIR 4486 (1.920), un NIR cu 1.932 trece cu toleranța 2% și e refuzat cu toleranța 0 (`50027`) | Test de integrare |
| A1-A4 | Două generări simultane de NIR din aceeași comandă, fiecare de 1.920 buc pe un rest de 1.920: una reușește, cealaltă primește `50027`; restul final e 0, nu −1.920 | Test de concurență |
| A1-A5 | Recepția și facturarea se consumă independent: după NIR 1.920 și factură 0, rămâne 1.920 de facturat și 1.920 de primit pe comandă | Test de integrare |
| A1-A6 | Anularea ciornei de NIR eliberează cantitatea rezervată; stornarea unui NIR validat dezactivează legăturile și readuce rămasul de primit | Test de integrare (cu Stocuri) |
| A1-A7 | Potrivirea în ordinea din 4.3: un GTIN corect câștigă în fața unei denumiri identice cu alt articol; pasul găsit e raportat | Test unitar (tabel de cazuri) |
| A1-A8 | O potrivire confirmată manual, cu învățarea pornită, creează rândul de catalog; aceeași linie, data viitoare, e potrivită „sigur” la pasul 3 (cu cod) sau 6 (doar denumire) | Test de integrare |
| A1-A9 | Același cod de furnizor activ la două variante e refuzat (`53001`); după dezactivarea primului rând e acceptat | Test de integrare |
| A1-A10 | Prețul propus respectă ordinea 3.6: pragul corect pentru 4.800 buc (1,1800), nu cel de 0 buc (1,2100); fără listă, ultimul preț cu semnul lui; fără nimic, linie fără preț | Test unitar |
| A1-A11 | Activarea unei liste noi închide lista înlocuită cu `ValidTo` = ziua dinainte; suprapunerea fără înlocuire e refuzată (`53004`) | Test de integrare |
| A1-A12 | `IPurchaseOrderSupply` întoarce doar cantitatea încă de primit (comandat − primit − închis), pe gestiune și dată estimată; o linie închisă manual dispare din disponibilul proiectat S5 | Test de integrare (cu Stocuri) |
| A1-A13 | Fiecare comportament are un test de mapare: câmpurile copiate din sursă (variantă, UM, cantitate, preț, gestiune, centru de cost) sunt cele din specificație | Test unitar per comportament |
| A1-A14 | Toate procedurile A1 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive; codul `ic-0500-pet` se potrivește cu `IC-0500-PET` | Test de integrare (`TST-09`) |

Criteriile A1-A3, A1-A4 și A1-A6 sunt critice: legăturile între documente decid cât mai e de primit și de facturat, deci stocul proiectat, contul `408` și potrivirea facturilor.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Unde stau tranzacțiile și legăturile | În Nucleu, comune tuturor modulelor; Achizițiile sunt primul utilizator | Nucleu §3.6, 2 | Decis de Aurelian (26.11.2026, D1) |
| 2 | Ordinea recepției | Ambele ordini, aviz → NIR → factură și factură → NIR, plus factura cu recepție (`FF-REC`), care generează NIR-ul | 3.2, 3.7 | Decis de Aurelian (26.11.2026, D2) |
| 3 | Ce poate configura clientul la o tranzacție | Doar variante ale comportamentelor livrate (denumire, drepturi, implicite, toleranță, semnal de contare); fără SQL și fără mapări în configurare | Nucleu §3.6 regula 2, 2 regula 5 | Decis de Aurelian (26.11.2026, cu preluările) |
| 4 | Catalogul tranzacțiilor livrate pentru achiziții | Cele 28 din 3.2, inclusiv `FF-EF` (din e-Factura), `NIR-IMP` (import cu DVI și facturi auxiliare) și `CA-NIR` (costuri de achiziție) | 3.2 | Decis de Aurelian (26.11.2026) |
| 5 | Comanda se consumă separat la recepție și la facturare | Da: grupurile `RECEIVE` și `INVOICE`; „de primit” și „de facturat” sunt independente și calculate, nu stocate pe linie | 2 regula 4, 3.3 | Decis de Aurelian (26.11.2026) |
| 6 | Toleranța de recepție | Implicit 0% pe firmă, cu suprascriere pe furnizor și pe tranzacție; interzisă la retur și storno | 3.2, 3.7 | Decis de Aurelian (26.11.2026) |
| 7 | Catalogul furnizorului | Pe variantă (SKU), valabil pe tot grupul sau pe o firmă; potrivirea în pașii din 4.3, cu învățare automată din confirmările manuale | 3.5, 4.3 | Decis de Aurelian (26.11.2026) |
| 8 | Prețul propus pe comandă | Listă de prețuri a furnizorului cu praguri → ultimul preț de achiziție → fără preț; fără „preț standard de achiziție” pe articol | 3.6 | Decis de Aurelian (26.11.2026) |
| 9 | Achizitorul responsabil | Pe furnizor și pe grupă de articole, cu înlocuitor pe perioade | 3.8 | Decis de Aurelian (26.11.2026) |
| 10 | Semnale proprii pentru contare | Tranzacțiile pot adăuga semnale `X_...` în `AccountingPayload.Flags`, folosite de variantele C2 (completare la C2) | 9 | Decis de Aurelian (26.11.2026) |

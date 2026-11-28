# ERP · 02.03 Vânzări · V1 Configurare: tranzacții, clienți, agenți și setări

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**, cu ecranele și toate cele 11 decizii („1-11 DA, ecrane validate”). Prima din cele 7 părți ale modulului 02.03 Vânzări. Împărțirea V1–V7 și preluările din ERP-ul folosit anterior de Aurelian ([INSPIRATIE_WINDOWS_CLIENT.md](INSPIRATIE_WINDOWS_CLIENT.md), propunerile P1–P16) au fost confirmate de Aurelian pe 27.11.2026 („este ok, pornim cu V1”).

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`. Această parte acoperă configurarea pe care se sprijină restul modulului: tipurile de documente de vânzare și tranzacțiile livrate pentru ele, grupurile de consum, condițiile comerciale pe client (inclusiv limita de credit și punctele de livrare), catalogul clientului (codurile lui pentru articolele noastre), agenții de vânzări cu portofoliile și zonele lor, nomenclatoarele vânzărilor și setările de vânzări ale firmei. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.2 Parteneri, §3.3 Articole, §3.3.1 Variante, §3.5 Nomenclatoare, §3.6 Tranzacții și legături), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C2 modele de contare, F3 decontări și soldul clientului), [02.05 Stocuri](../05-stocuri/README.md) (S2 portul de stoc, S4 durata minimă rămasă pe client, S5 rezervări și disponibil proiectat), [02.04 Achiziții A1](../04-achizitii/A1-CONFIGURARE.md) (modelul simetric pentru furnizori). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`); căutările fără diacritice folosesc coloane calculate cu `Latin1_General_100_CI_AI_SC_UTF8` (`SQL-25`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Partenerul** e unic la nivel de instalare (`core.Partner`), cu rolul `IsCustomer`. Condițiile comerciale ale clientului se țin **per firmă**, în Vânzări (3.4). Un partener care nu e client nu apare pe documentele de vânzare (`52001`).
- **Liniile de document stau pe variantă** (`core.ItemVariant`, SKU), niciodată pe model (`core.Item`). Pe o linie cu articol stocabil, `ItemVariantId` e obligatoriu (Nucleu §3.3.1). Cantitatea se ține în UM documentului și, calculat, în UM de bază a modelului, cu factorii din setul de UM al articolului (`core.UomSet`).
- **Tipul de tranzacție** (`core.TransactionType`) și **legăturile între documente** (`core.DocumentLink`, cu grupul de consum și cantitatea pe linie) sunt definite în Nucleu §3.6 și sunt deja folosite de Achiziții. Comportamentul fiecărei tranzacții e scris în cod (`ITransactionBehavior`). Clientul poate crea tranzacții proprii doar pe comportamentele livrate: altă denumire, alte drepturi, alte valori implicite, altă toleranță (unde e permisă) și alte semnale pentru contare. Nu poate scrie SQL.
- **Vânzările mișcă stocul sincron**, prin portul `IStockLedger` (Stocuri S2, decizia 1), în tranzacția SQL a documentului care are efect în stoc (avizul, inclusiv avizul generat de factura cu livrare, returul de la client). Comenzile nu mișcă stocul: rezervă marfa prin `IStockLedger.ReserveAsync` (Stocuri S5).
- Contarea nu se face în Vânzări: documentele publică evenimente cu blocul `Accounting` (C2), iar partida clientului o deschide F3 la `FacturaEmisa`. Soldul clientului (pentru limita de credit) se citește din F3 prin `IPartnerBalanceQueries`.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Cantitățile și prețurile se țin pe `decimal(19,6)`, cu 0–6 zecimale configurabile pe firmă, articol și listă de prețuri; valorile pe `decimal(19,4)` și se rotunjesc explicit la 2 zecimale pe linie și pe document (`SQL-07`).
- **Ce NU face V1:** nu creează documente de vânzare (V2–V6), nu definește prețuri și discounturi (V2) și nu trimite facturi în SPV (V5 și Integrări 02.14). V1 definește doar configurarea pe care o citesc celelalte părți.

## 1. Scop și perimetru

| Zonă | Ce face V1 | Ce NU face (și cine face) |
| --- | --- | --- |
| Tipuri de documente | Lista tipurilor de documente ale vânzărilor, seriile implicite, stările comune și partea care detaliază fiecare tip | Ciclul de viață al fiecărui document (V3–V6) |
| Tranzacții | Catalogul tranzacțiilor livrate pentru vânzări, cu sursele, grupul de consum, efectul în stoc și toleranța | Motorul de tranzacții și legături (Nucleu §3.6) |
| Clienți | Condițiile comerciale ale clientului în firmă: grupă comercială, listă de prețuri, monedă, termen și modalitate de plată, limită de credit, agent, zonă, canal, mod de livrare, gestiune, plătitor desemnat, gruparea facturilor, tranzacții implicite, blocare | Datele de identitate ale partenerului (Nucleu §3.2); partidele, încasările și soldul (F3) |
| Puncte de livrare | Adresele de livrare ale clientului cu codul GLN, zona, fereastra de livrare și gestiunea propusă | Adresa ca atare (Nucleu `PartnerAddress`); rutele și cursele (TMS 02.07) |
| Limita de credit | Expunerea clientului, momentele de verificare, blocarea și deblocarea unică | Soldul și restanțele (F3); scadențarul și somațiile (F3) |
| Catalogul clientului | Codul, denumirea și UM-ul clientului legate de varianta noastră; articolele listate la client | Codurile de bare proprii (Nucleu §3.3); durata minimă rămasă la livrare (Stocuri S4, doar afișată aici) |
| Agenți și zone | Agenții de vânzări, echipele, zonele comerciale, portofoliile pe client, zonă sau grupă, înlocuitorii, vizibilitatea pe portofoliu | Comisioanele agenților (V7); oportunitățile și activitățile (CRM 02.12) |
| Nomenclatoare | Modalitățile de plată cu codurile UNCL 4461 (în Nucleu, comune), grupele comerciale, canalele, modurile de livrare, motivele | Nomenclatoarele INS (Nucleu, Achiziții A6); codurile e-Transport și CPV (V4, V5) |
| Setări | Setările de vânzări ale firmei: drumurile livrare–facturare, credit, rezervare, toleranțe, oferte, facturare, e-Factura | Aprobările pe praguri ale ofertelor și comenzilor (V3); politica de prețuri (V2) |

## 2. Model de domeniu

```
 Nucleu §3.6 (comun tuturor modulelor)
   TransactionType ── TransactionTypeSource × N ── TransactionTypeRole × N
   DocumentLink (linie sursă → linie destinație, cantitate, grup de consum)
   DocumentLineClosure · DocumentStatusLog
 Nucleu §3.5 (nou, comun): PaymentMeansCode (UNCL 4461, seed) ── PaymentMethod (modalitatea de plată)

 Vânzări V1 (sales), per firmă
   SalesSettings (1 rând pe firmă)
   CustomerTerms (client × firmă: grupă, listă de prețuri, monedă, plată, credit, agent, zonă, livrare, tranzacții implicite)
      ├─ CustomerDeliveryPoint × N (adresa de livrare a clientului: GLN, zonă, fereastră, gestiune)
      └─ CreditRelease × N (deblocări unice ale limitei de credit, cu motiv și termen)
   CustomerItem (client × variantă: codul și denumirea la client, UM de comandă, listare)
   SalesAgent (agent, legat de utilizator) ── AgentPortfolio × N (client, zonă sau grupă comercială; principal, înlocuitor)
   Nomenclatoare: CustomerGroup · SalesChannel · SalesZone (arbore) · ShippingMethod · ReasonCode

 Tipuri de documente (detaliate în V2–V6), fiecare cu tranzacțiile lui
   SalesQuote → SalesOrder → DeliveryOrder → SalesDeliveryNote (aviz) → SalesInvoice → SalesCreditNote
   SalesProforma · CustomerReturn · BillingPlan · (PriceList, DiscountRule în V2)
```

Reguli de business ale modelului:

1. **Condițiile comerciale sunt per firmă.** Același client poate avea termen de plată 60 de zile la Exemplu Distribuție și 30 la Exemplu Producție, alt agent și altă listă de prețuri. Tot ce ține de relația comercială (3.4–3.7) are `CompanyId`. Catalogul clientului poate fi comun grupului (codurile clientului nu depind de firma care vinde).
2. **Clientul de pe document e cel facturat.** Factura, avizul și comanda au un singur client fiscal (cumpărătorul din e-Factura, `AccountingCustomerParty`). Punctul de livrare e întotdeauna o adresă a acestui client. **Plătitorul desemnat** (P5) e un alt client, informativ: F3 îl folosește ca să propună alocarea încasărilor venite de la el pe facturile clientului; partida rămâne pe client (precizare la F3, 9).
3. **O comandă se consumă separat la livrare și la facturare** (grupurile `DELIVER` și `INVOICE`, 3.3). Ce s-a livrat și ce s-a facturat sunt două cantități independente pe aceeași linie de comandă. Avizele livrate și nefacturate sunt baza contului `418` (V5).
4. **Orice ieșire din stoc la vânzare trece printr-un aviz.** Avizul poate fi tipărit și transportat cu marfa sau poate fi **intern**, generat de factura cu livrare (`FV-LIV`) și netipărit separat, pentru că factura ține loc de document de însoțire. Retururile, rezervările consumate, `418`-ul și e-Transport-ul citesc astfel aceeași sursă (decizia 1).
5. **Tranzacțiile proprii ale clientului sunt variante ale celor livrate.** O tranzacție nouă alege un comportament livrat (3.2) și moștenește de la el sursele, maparea și efectul în stoc. Poate schimba doar denumirea, drepturile, valorile implicite, toleranța (dacă comportamentul o permite) și semnalele pentru contare.
6. **Agentul se copiază pe document la creare.** Agentul vine din portofoliu (3.7) și se poate schimba pe document doar de cine are dreptul. Rapoartele și comisioanele (V7) folosesc agentul de pe document, nu pe cel curent al clientului, ca schimbarea portofoliului să nu rescrie istoria.
7. **Nimic din configurare nu se șterge după folosire** (`SQL-20`): o tranzacție, un rând de catalog, un agent sau o valoare de nomenclator folosită se dezactivează.

## 3. Entități și setări

Convențiile comune tuturor tabelelor `sales`: cheie `Id` `int IDENTITY` pentru nomenclatoare și `bigint` pentru tabelele care cresc (`SQL-13`), coloanele de audit `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` (`SQL-19`) și `RowVersion` pe tot ce se editează din interfață (`SQL-11`, `SP-18`). Codurile se compară prin coloane `CodeKey` calculate (`SQL-24`).

### 3.1 Tipurile de documente ale vânzărilor

Codul tipului de document e același în `core.NumberSeries`, în `core.TransactionType`, în `core.DocumentLink` și în `AccountingPayload` (C2).

| `DocumentType` | Document | Serie implicită | Stoc | Contare | Detaliat în |
| --- | --- | --- | --- | --- | --- |
| `SalesQuote` | Ofertă către client, cu revizii și valabilitate | `OV` | — | — | V3 |
| `SalesOrder` | Comandă de la client (inclusiv importată din Excel, EDI sau e-mail), cu numărul de comandă al clientului | `CV` | Rezervare (S5), la confirmare | — | V3 |
| `SalesProforma` | Factură proforma (cerere de plată în avans, fără efect fiscal) | `PF` | — | — | V3 |
| `DeliveryOrder` | Dispoziție de livrare către gestiune (pregătirea mărfii, cu loturile alese); devine sarcina de picking când există WMS (02.06) | `DL` | Fixează rezervarea pe lot (S5) | — | V4 |
| `SalesDeliveryNote` | Aviz de însoțire a mărfii către client (OMFP 2634/2015, 14-3-6A), inclusiv avizul intern al facturii cu livrare și avizul de consignație | Serie fiscală a firmei, de exemplu `EXD` | Ieșire (`ISSUE_SALE`) sau transfer în gestiunea „la client” (consignație) | Da, prin evenimentul de stoc (`607` = `371`) și `418` la avizele nefacturate (V5) | V4 |
| `SalesInvoice` | Factură de vânzare: marfă, servicii, avans, export, livrare intracomunitară, recurentă | Serie fiscală, de exemplu `FV EXD` | Doar factura cu livrare, prin avizul ei intern | Da (`FacturaEmisa`) | V5 |
| `SalesCreditNote` | Factură de stornare sau de reducere emisă (în e-Factura: notă de credit 381 sau factură negativă 380, setare V5) | Aceeași serie fiscală ca facturile, implicit | Nu (returul fizic e `CustomerReturn`) | Da (`FacturaEmisa` cu `IsReversal`) | V6 |
| `CustomerReturn` | Retur de la client (aviz de retur primit sau întocmit la primire), cu verificarea calității | `RC` | Intrare (`RETURN_FROM_CUSTOMER`), la costul ieșirii | Da, prin evenimentul de stoc | V6 |
| `BillingPlan` | Plan de facturare recurentă (abonamente, chirii, servicii lunare), sursă pentru facturile periodice | `PR` | — | — | V5 |

Listele de prețuri și regulile de discount (V2) sunt nomenclatoare, nu documente; nu au tranzacții.

Seriile implicite se creează la activarea modulului pentru fiecare firmă (`MIG-06`) și se pot schimba în Configurare › Documente › Serii (Nucleu). Seriile avizelor și ale facturilor sunt fiscale (`IsFiscal = 1`, numerotare continuă, Nucleu fluxul 4.2) și pot fi separate pe punct de lucru. Ofertele, comenzile, proformele, dispozițiile și planurile au serii interne.

**Stările comune** ale documentelor de vânzare (fiecare parte adaugă ce îi trebuie): 1 Ciornă · 2 De aprobat · 3 Validat (sau Trimisă la ofertă, Confirmată la comandă) · 4 Închis (toate liniile consumate sau închise manual) · 8 Stornat · 9 Anulat (doar din ciornă sau din „De aprobat”). Fiecare schimbare se scrie în `core.DocumentStatusLog` (Nucleu §3.6.3). Blocarea pentru credit nu e o stare: e un marcaj pe document (`CreditHold`), ca starea să spună în continuare unde e documentul în fluxul lui (4.3).

### 3.2 Tranzacțiile livrate

Comportamentele sunt scrise în cod (`ITransactionBehavior`, Nucleu §3.6.5) și se înregistrează la pornirea modulului. Pentru fiecare comportament se livrează o tranzacție de sistem (`IsSystem = 1`, `MIG-06`), care se poate redenumi sau dezactiva, dar nu șterge. Clientul poate adăuga oricâte tranzacții proprii pe același comportament (regula 5).

| Cod livrat | Denumire | Document destinație | Comportament (`Behavior`) | Mod | Surse (principală în **bold**) | Grup de consum | Efect în stoc | Toleranță implicită |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `OV-MAN` | Emitere ofertă către client | `SalesQuote` | `Quote.Manual` | Manuală | — | — | — | — |
| `CV-MAN` | Înregistrare comandă de la client | `SalesOrder` | `SalesOrder.Manual` | Manuală | — | — | Rezervare la confirmare | — |
| `CV-OV` | Comandă din oferta către client | `SalesOrder` | `SalesOrder.FromQuote` | Generată | **`SalesQuote`** (trimisă sau acceptată, în valabilitate) | `ORDER` (informativ) | Rezervare la confirmare | — (informativ) |
| `CV-IMP` | Comandă importată (Excel, EDI, e-mail) | `SalesOrder` | `SalesOrder.Import` | Manuală, din fișier | — (liniile se potrivesc prin catalogul clientului, 4.4) | — | Rezervare la confirmare | — |
| `PF-MAN` | Proformă fără sursă | `SalesProforma` | `Proforma.Manual` | Manuală | — | — | — | — |
| `PF-CV` | Proformă din comandă | `SalesProforma` | `Proforma.FromOrder` | Generată | **`SalesOrder`** (confirmată) | `PROFORMA` (informativ) | — | — (informativ) |
| `PF-OV` | Proformă din ofertă | `SalesProforma` | `Proforma.FromQuote` | Generată | **`SalesQuote`** (trimisă sau acceptată) | `PROFORMA` (informativ) | — | — (informativ) |
| `DL-CV` | Dispoziție de livrare din comenzi | `DeliveryOrder` | `DeliveryOrder.FromOrders` | Generată | **`SalesOrder`** (confirmată, fără `CreditHold`) | `PREPARE` | Rezervarea devine specifică pe lot (S5) | 0 |
| `AV-MAN` | Aviz fără comandă | `SalesDeliveryNote` | `DeliveryNote.Manual` | Manuală | — | — | Ieșire | — |
| `AV-CV` | Aviz din comanda clientului | `SalesDeliveryNote` | `DeliveryNote.FromOrder` | Generată | **`SalesOrder`** (confirmată, fără `CreditHold`) | `DELIVER` | Ieșire, consumă rezervarea | permisă (setare 3.9) |
| `AV-DL` | Aviz din dispoziția de livrare | `SalesDeliveryNote` | `DeliveryNote.FromDeliveryOrder` | Generată | **`DeliveryOrder`** (pregătită); comanda, prin legătura dispoziției | `DELIVER` | Ieșire, pe loturile pregătite | 0 |
| `AV-CSG` | Aviz de consignație (marfa rămâne a noastră la client) | `SalesDeliveryNote` | `DeliveryNote.Consignment` | Manuală sau din comandă | `SalesOrder` (opțional) | `DELIVER` | Transfer în gestiunea „la client” (Stocuri S1, tip „La terți”) | 0 |
| `AV-STO` | Stornare aviz nefacturat | `SalesDeliveryNote` | `DeliveryNote.Reversal` | Generată | **`SalesDeliveryNote`** (validat) | `REVERSE` | Intrare, la costul ieșirii inițiale | interzisă |
| `FV-MAN` | Factură fără sursă (servicii, refacturări, chirii) | `SalesInvoice` | `Invoice.Manual` | Manuală | — (doar linii nestocabile, `52015`) | — | — | — |
| `FV-AV` | Factură din avize | `SalesInvoice` | `Invoice.FromDeliveryNotes` | Generată | **`SalesDeliveryNote`** (validat); comanda, prin legătura avizului; facturile de avans ale clientului (auxiliară, pe antet, valoare) | `INVOICE`; `SETTLE` pe avansuri | — | 0 |
| `FV-CV` | Factură din comandă (servicii și linii nestocabile) | `SalesInvoice` | `Invoice.FromOrder` | Generată | **`SalesOrder`** (confirmată, filtru `NON_STOCK_LINES`); facturile de avans (auxiliară) | `INVOICE`; `SETTLE` | — | permisă |
| `FV-LIV` | Factură cu livrare (generează avizul intern) | `SalesInvoice` | `Invoice.WithDelivery` | Manuală sau din comandă | `SalesOrder` (opțional, confirmată); facturile de avans (auxiliară) | `INVOICE` + `DELIVER`; `SETTLE` | Ieșire, prin avizul generat | permisă |
| `FV-AVS` | Factură de avans | `SalesInvoice` | `Invoice.Advance` | Manuală sau din comandă | `SalesOrder` (opțional; pe antet, valoare) | `ADVANCE` | — | 0 |
| `FV-REC` | Factură din planul de facturare recurentă | `SalesInvoice` | `Invoice.FromBillingPlan` | Generată (și prin job lunar) | **`BillingPlan`** (activ) | `BILLING` | — | 0 |
| `FV-CSG` | Factură din consumul raportat în consignație | `SalesInvoice` | `Invoice.FromConsignment` | Generată | **`SalesDeliveryNote`** de consignație (validat) | `INVOICE` | Ieșire din gestiunea „la client” | 0 |
| `RC-AV` | Retur de la client din aviz | `CustomerReturn` | `Return.FromDeliveryNote` | Generată | **`SalesDeliveryNote`** (validat, inclusiv avizul intern al unei facturi cu livrare) | `RETURN` | Intrare, la costul ieșirii inițiale | interzisă |
| `RC-MAN` | Retur de la client fără document sursă | `CustomerReturn` | `Return.Manual` | Manuală (drept `Sales.Return.WithoutSource`) | — | — | Intrare, la costul curent (S3) | — |
| `RC-SGR` | Retur de ambalaje SGR goale | `CustomerReturn` | `Return.Deposit` | Manuală | — (verificat față de ambalajele SGR livrate clientului, V6 §3.7) | — | Intrare în gestiunea de ambalaje SGR | — |
| `FS-FV` | Storno din factura de vânzare | `SalesCreditNote` | `CreditNote.FromInvoice` | Generată | **`SalesInvoice`** (validată) | `CREDIT` | — | interzisă |
| `FS-RC` | Factură de stornare pentru un retur | `SalesCreditNote` | `CreditNote.FromReturn` | Generată | **`CustomerReturn`** (validat); factura inițială, prin legături | `CREDIT` | — | interzisă |
| `FS-RED` | Reducere comercială ulterioară (rabat, bonus de volum, fără retur) | `SalesCreditNote` | `CreditNote.Discount` | Manuală | `SalesInvoice` (opțional, pe antet); decontul de rabat (V6, opțional) | — | — | — |

Note la tabel:

- „Permisă” înseamnă că tranzacția acceptă `OverTolerancePct` > 0, din setarea firmei (3.9) sau de pe tranzacție; implicit 0 (nu livrăm și nu facturăm peste comandă). Exemplu de folosire: marfa cântărită la livrare (vrac, mezeluri), unde cantitatea livrată poate depăși comanda cu câteva procente. „Interzisă” înseamnă că toleranța e forțată la 0: nu se returnează, nu se stornează și nu se stornează avizul peste ce s-a livrat sau facturat.
- **Informativ** (`LimitsConsumption = 0`, Nucleu §3.6.1): legăturile se scriu și rămasul se afișează, dar depășirea nu ridică `50027`. Oferta e informativă față de comandă (clientul poate comanda mai mult decât i s-a ofertat; prețul ofertei se aplică în limitele din V2), iar proforma e informativă față de comandă și ofertă (se pot emite mai multe proforme pe aceeași comandă) (decizia 4).
- `FV-LIV` (decizia 1) e singura tranzacție de vânzare care creează două documente într-o singură tranzacție SQL: factura și avizul ei intern (`SalesDeliveryNote` cu `IsInvoiceDelivery = 1`), legate pe linii. Validarea facturii validează și avizul, deci ieșirea din stoc, consumul rezervării și e-Transport-ul (dacă e cazul) se fac la data facturii. Avizul intern nu are număr fiscal propriu: poartă numărul facturii și nu se tipărește separat. Fluxul complet e în V5.
- Legătura „comandă → aviz → factură” păstrează comanda ca sursă indirectă: factura din aviz consumă grupul `INVOICE` pe liniile avizului **și**, prin legătura avizului, pe liniile comenzii, ca „de facturat” pe comandă să fie corect oricare ar fi drumul. La fel, avizul din dispoziție consumă `DELIVER` pe comandă prin legătura dispoziției.
- `SETTLE` e grupul prin care factura finală regularizează avansul: sursa e factura de avans a clientului, pe antet și pe valoare (`Level = 2`, `Measure = 2`), iar regularizarea apare pe factura finală ca linie negativă de avans (V5). Nu se poate regulariza mai mult decât avansul facturat și neregularizat.
- `DL-CV`, `AV-CV` și `FV-LIV` nu acceptă ca sursă o comandă cu `CreditHold = 1` (`52005`, 4.3) sau cu `AdvanceHold = 1` (avans neîncasat, `52222`; precizare din V3, 27.11.2026).
- Tranzacțiile de încasare (o încasare pe o factură de vânzare) sunt ale Finanțelor (F1, F2, F3), cu sursa pe antet (`Level = 2`, valoare).

### 3.3 Grupurile de consum în vânzări

O linie sursă are câte un disponibil pentru fiecare grup de consum în care e folosită (Nucleu §3.6.2).

| Grup | Sursa consumată | Consumată de | Ce înseamnă rămasul | Limitează |
| --- | --- | --- | --- | --- |
| `ORDER` | Linia ofertei | Comanda | Cât din ofertă nu a fost încă comandat | Nu (informativ) |
| `PROFORMA` | Linia comenzii sau a ofertei | Proforma | Cât nu a fost încă cerut în avans prin proformă | Nu (informativ) |
| `PREPARE` | Linia comenzii | Dispoziția de livrare | Cât nu a fost încă trimis la pregătire | Da |
| `DELIVER` | Linia comenzii sau a dispoziției | Avizul (inclusiv cel intern al facturii cu livrare și cel de consignație) | **Cât e încă de livrat** (alimentează rezervarea și cererea din disponibilul proiectat, S5) | Da |
| `INVOICE` | Linia avizului sau a comenzii | Factura | **Cât e încă de facturat** (avize nefacturate, baza contului `418`) | Da |
| `ADVANCE` | Antetul comenzii (valoare) | Factura de avans | Cât din valoarea comenzii nu a fost încă cerut ca avans | Da |
| `SETTLE` | Antetul facturii de avans (valoare) | Factura finală | Avansul încă neregularizat | Da |
| `BILLING` | Linia planului de facturare (perioada) | Factura recurentă | Perioadele încă nefacturate | Da |
| `REVERSE` | Linia avizului | Stornarea avizului | Cât din aviz se mai poate storna (doar partea nefacturată) | Da |
| `RETURN` | Linia avizului | Returul de la client | Cât se mai poate returna | Da |
| `CREDIT` | Linia facturii sau a returului | Factura de stornare | Cât se mai poate storna | Da |

Pe linia de comandă, Vânzările afișează cinci cantități derivate: comandată, rezervată (din S5), pregătită (`PREPARE`), livrată (`DELIVER`) și facturată (`INVOICE`). Niciuna nu se stochează pe linie; toate se calculează din `core.DocumentLink` și din rezervare. Excepție: pentru disponibilul proiectat și pentru MRP, cererea deschisă se citește prin `ISalesOrderDemand` (6), care face calculul cu TVP.

`REVERSE` și `INVOICE` pe aceeași linie de aviz se limitează reciproc: maximul stornabil e cantitatea livrată minus cea facturată (`52016`). Un aviz facturat se corectează prin retur și factură de stornare (V6), nu prin stornarea avizului.

### 3.4 Condițiile comerciale ale clientului (`CustomerTerms`)

Un rând pe client și firmă. Se creează automat la primul document de vânzare cu clientul (cu valorile implicite ale firmei, 3.9, și ale grupei comerciale) sau manual, din fișa partenerului, tab-ul Vânzări (ecranul `v1-03`).

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `PartnerId` | `int` | Partenerul trebuie să aibă `IsCustomer = 1` (`52001`) |
| `CustomerGroupId` | `int` | Grupa comercială (3.8), obligatorie. Folosită de prețuri și discounturi (V2), de portofolii (3.7) și de rapoarte |
| `SalesChannelId` | `int NULL` | Canalul prin care e servit clientul (3.8) |
| `PriceListId` | `int NULL` | Lista de prețuri a clientului (V2). Gol = lista grupei comerciale, apoi lista implicită a firmei. O listă proprie a partenerului (V2 §3.1.1) are prioritate față de acest câmp (precizare din V2, 27.11.2026) |
| `CurrencyCode` | `char(3)` | Moneda implicită a ofertelor, comenzilor și facturilor; o monedă activă în Nucleu |
| `PaymentTermDays` | `smallint` | Termenul de plată propus pe factură (scadența = data facturii + termen). Peste 60 de zile, avertizare la salvare (Legea 72/2013, termenul trebuie stabilit expres în contract) |
| `PaymentMethodId` | `int` | Modalitatea de plată propusă (Nucleu `core.PaymentMethod`, 3.8); dă codul `PaymentMeansCode` din e-Factura |
| `CreditLimitAmount` | `decimal(19,4) NULL` | Limita de credit, în lei. Gol = fără limită (doar restanțele se verifică, dacă modul o cere) |
| `CreditCheckMode` | `tinyint NULL` | Suprascrie setarea firmei: 0 fără verificare · 1 avertizare · 2 blocare (4.3) |
| `OverdueBlockDays` | `smallint NULL` | Suprascrie setarea firmei: blocare când clientul are o factură restantă de mai mult de atâtea zile (0 = nu se verifică) |
| `CashOnly` | `bit` | Client „doar cu plata pe loc”: facturile primesc termen 0 și modalitate numerar sau card; folosit pentru clienții cu istoric slab, fără a-i bloca de tot |
| `PayerPartnerId` | `int NULL` | Plătitorul desemnat (regula 2): un alt client, de regulă firma-mamă sau centrala. Informativ pentru F3 (propunerea de alocare); cere atașat acordul de plată (`52008` dacă plătitorul nu e client sau are el însuși plătitor) |
| `SalesAgentId` | `int NULL` | Agentul principal; scurtătură pentru rândul principal din portofoliu (3.7) |
| `SalesZoneId` | `int NULL` | Zona comercială implicită; un punct de livrare o poate preciza (3.5) |
| `ShippingMethodId` | `int NULL` | Modul de livrare propus (3.8) |
| `DefaultWarehouseId` | `int NULL` | Gestiunea de livrare propusă (altfel cea a punctului de livrare, apoi a firmei) |
| `InvoiceGrouping` | `tinyint` | Cum se grupează avizele în facturi la facturarea în masă (V5): 1 o factură pe aviz · 2 pe comandă · 3 pe zi · 4 pe săptămână · 5 pe lună (decadă configurabilă). Implicit din setarea firmei |
| `RequiresCustomerOrderRef` | `bit` | Comanda nu se confirmă fără numărul de comandă al clientului (lanțurile de retail cer referința pe factură, în UBL `OrderReference`) |
| `RequiresCustomerItemCodes` | `bit` | Avizul și factura nu se validează dacă o linie nu are cod la client în catalog (3.6); în e-Factura codul trece în `BuyersItemIdentification` |
| `ReservationPriority` | `tinyint` | Prioritatea rezervărilor comenzilor clientului în Stocuri S5, 1–9 (1 cea mai mare), implicit 5 (precizare din V3, 27.11.2026) |
| `RequiredAdvancePct` | `decimal(9,4) NULL` | Avansul cerut pe fiecare comandă a clientului; comanda confirmată primește `AdvanceHold` până la încasare (precizare din V3 §3.11, 27.11.2026) |
| `PrintValuesOnDeliveryNote` | `bit` | Implicit 1. Valorile se tipăresc pe aviz; se calculează mereu (V4 §3.11) (precizare din Vânzări V4, 27.11.2026) |
| `ConsignmentReportFrequency` | `tinyint NULL` | 1 săptămânal · 2 lunar; gol = clientul nu are consignație (V4 §3.10) (precizare din Vânzări V4, 27.11.2026) |
| `AllowPartialDelivery` | `bit` | Implicit 1. Cu 0, comanda se livrează doar întreagă (avizul trebuie să acopere tot restul, altfel avertizare) |
| `Incoterm`, `IncotermPlace` | `char(3) NULL` (FK `core.DeliveryTerm`), `nvarchar(100) NULL` | Pentru export și livrări intracomunitare; propuse pe comandă, folosite la Intrastat expedieri (Stocuri S7) |
| `TransportModeCode`, `TransactionNatureCode` | `char(1) NULL` (FK `core.TransportMode`), `varchar(3) NULL` (FK `core.TransactionNature`) | Valorile implicite pentru Intrastat la livrările intracomunitare |
| `DefaultQuoteTransactionTypeId`, `DefaultOrderTransactionTypeId`, `DefaultDeliveryTransactionTypeId`, `DefaultInvoiceTransactionTypeId` | `int NULL` | Tranzacțiile propuse pentru acest client (au prioritate față de cele implicite ale rolului, dacă rolul are voie să le folosească). Exemplu: clienții HoReCa serviți din mașina agentului primesc `FV-LIV-AG` (4.1) |
| `ReturnPolicyId` | `int NULL` | Politica de retur a clientului (`sales.ReturnPolicy`, V6 §3.6); gol = politica grupei comerciale, apoi cea implicită a firmei (precizare din Vânzări V6, 27.11.2026) |
| `EmailOnValidation` | `bit NULL` | Suprascrie setarea firmei: factura pleacă sau nu pe email la validare (V5 §3.10) (precizare din Vânzări V5, 27.11.2026) |
| `InvoiceEmail` | `nvarchar(200) NULL` | Adresa la care pleacă PDF-ul facturii, pe lângă e-Factura; altfel contactul cu rolul „facturare” din Nucleu |
| `IsBlocked`, `BlockReason` | `bit`, `nvarchar(300)` | Client blocat pentru comenzi și livrări noi (de exemplu litigiu sau insolvență). Ofertele se pot emite, cu avertizare; facturile pentru avizele deja livrate se pot emite. Blocarea și deblocarea cer `Sales.Customer.Block` și se scriu în istoric |
| `Notes` | `nvarchar(1000) NULL` | — |

Precedența pentru setările care există și pe client: tranzacție (dacă e setată explicit) → client → grupa comercială (pentru listă de prețuri, termen de plată și mod de credit) → firmă.

Partenerul inactiv la ANAF (`core.Partner.AnafInactive`) sau cu TVA anulat primește avertizare pe orice document de vânzare; partenerul radiat (Integrări 02.14) primește blocare. Clientul persoană fizică (`PartnerType` = persoană fizică) nu are limită de credit implicită, iar factura lui merge în e-Factura după regulile B2C din V5.

### 3.5 Punctele de livrare (`CustomerDeliveryPoint`)

Fiecare punct de livrare e o adresă de tip „livrare” a clientului din Nucleu (`core.PartnerAddress`), cu datele de vânzare pe firmă. Un lanț de magazine are un client (persoana juridică facturată) și zeci de puncte de livrare.

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `PartnerId`, `PartnerAddressId` | `int` | Adresa trebuie să fie a clientului și de tip livrare (`52009`) |
| `Code` | `nvarchar(30)` | Codul punctului la client (de exemplu codul magazinului), tipărit pe aviz |
| `Gln` | `varchar(13) NULL` | Codul GLN al locației (cerut de lanțurile de retail în EDI și în e-Factura, `DeliveryLocation/ID`, schema `0088`). Se ține pe adresa din Nucleu (precizare Nucleu, 9); aici doar se afișează |
| `SalesZoneId` | `int NULL` | Zona punctului; precizează zona clientului (rutele TMS o folosesc ca prim filtru) |
| `DeliveryWindow` | `nvarchar(100) NULL` | Fereastra de livrare (de exemplu „L–V 06:00–10:00”); text, folosit de TMS ca restricție |
| `DefaultWarehouseId` | `int NULL` | Gestiunea din care se livrează de obicei acest punct |
| `ShippingMethodId` | `int NULL` | Precizează modul de livrare al clientului |
| `ContactId` | `int NULL` | Persoana de contact la recepție (Nucleu `PartnerContact`) |
| `Instructions` | `nvarchar(500) NULL` | Indicații de descărcare, tipărite pe aviz și pe foaia de parcurs |
| `IsDefault` | `bit` | Cel mult un punct implicit pe client și firmă |
| `IsActive` | `bit` | — |

### 3.6 Catalogul clientului (`CustomerItem`)

Leagă felul în care clientul numește un produs de varianta noastră. E simetric cu catalogul furnizorului din Achiziții A1 §3.5. Este folosit la potrivirea comenzilor importate (4.4), la tipărirea avizului și a facturii cu codurile clientului, la e-Factura emisă (`BuyersItemIdentification`) și la lista articolelor „listate” la client (lanțurile de retail cumpără doar ce au listat).

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int NULL` | Gol = valabil în toate firmele grupului; altfel doar pentru firma dată |
| `PartnerId` | `int` | Clientul |
| `ItemVariantId` | `int` | Varianta noastră (SKU). Obligatoriu |
| `CustomerCode` | `nvarchar(60) NULL` | Codul clientului. Comparat fără diferență de majuscule și fără spații (`CustomerCodeKey`, coloană calculată, `SQL-24`) |
| `CustomerName` | `nvarchar(300) NULL` | Denumirea la client, tipărită pe documente dacă `PrintCustomerNames = 1` pe client; folosită la potrivirea după denumire (`CustomerNameSearch`, `SQL-25`) |
| `OrderUomId` | `int` | UM-ul în care comandă clientul (de exemplu baxul de 12), din setul de UM al articolului (`52003`) |
| `OrderMultiple` | `decimal(19,6) NULL` | Multiplul de comandă în `OrderUomId` (avertizare și rotunjire propusă pe comandă) |
| `IsListed`, `ListedFrom`, `ListedTo` | `bit`, `date NULL` | Articol listat la client în perioada dată. Comenzile pe articole nelistate primesc avertizare; pe clienții cu `RequiresCustomerItemCodes = 1`, blocare la validarea avizului |
| `Source` | `tinyint` | 1 Manual · 2 Import Excel · 3 Învățat la potrivire (4.4) |
| `IsActive` | `bit` | Un rând folosit nu se șterge |

Durata minimă rămasă la livrare pe client și pe articol sau grupă stă în Stocuri (`inv.PartnerShelfLifeRule`, S4) și se afișează în catalogul clientului doar pentru citire, ca să nu existe două adevăruri. GTIN-ul vine din codurile de bare ale variantei (Nucleu `core.ItemBarcode`).

### 3.7 Agenții de vânzări și portofoliile (`SalesAgent`, `AgentPortfolio`, `SalesZone`)

**Agentul** (`SalesAgent`), pe firmă:

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Code`, `Name` | Codul unic pe firmă |
| `UserId` | Utilizatorul agentului (obligatoriu pentru agenții interni). Un utilizator e agent cel mult o dată pe firmă |
| `EmployeeId` | Angajatul din HR (02.09), opțional, pentru comisioane prin salarizare |
| `AgentKind` | 1 Agent de teren · 2 Key account (lanțuri) · 3 Telesales · 4 Agent extern comisionat (are `PartnerId` în loc de utilizator obligatoriu) |
| `ManagerAgentId` | Șeful de echipă (alt agent); dă echipa pentru vizibilitate și rapoarte, cel mult 3 niveluri |
| `DefaultWarehouseId` | Gestiunea agentului (de exemplu mașina din care vinde, la vânzarea din auto); propusă pe documentele lui |
| `MaxDiscountPct` | Plafonul de discount manual al agentului (V2); peste el, aprobare |
| `CommissionSchemeId` | Schema de comision a agentului (V7 §3.4); gol = fără comision (precizare din Vânzări V7, 27.11.2026) |
| `ValidFrom`, `ValidTo`, `IsActive` | Un agent folosit nu se șterge |

**Portofoliul** (`AgentPortfolio`): `AgentId` și exact unul dintre `PartnerId`, `SalesZoneId` sau `CustomerGroupId` (`CHECK`), `IsPrimary`, `SubstituteAgentId`, `SubstituteFrom`, `SubstituteTo`, `ValidFrom`, `ValidTo`. Cel mult un agent principal pe client, pe zonă și pe grupă, în aceeași perioadă (`52010`).

**Agentul propus pe un document nou**, în ordine: agentul tranzacției sau al utilizatorului care creează documentul, dacă e agent și are clientul în portofoliu → agentul principal al clientului → agentul principal al zonei punctului de livrare (urcând în arborele zonelor) → agentul principal al grupei comerciale → gol (avertizare: documentele fără agent apar separat în V7). Un înlocuitor în perioada lui primește documentele titularului, dar agentul de pe document rămâne titularul (regula 6), cu înlocuitorul în câmpul `HandledByAgentId`.

**Zonele** (`SalesZone`): `CompanyId`, `Code`, `Name`, `ParentZoneId` (cel mult 3 niveluri: regiune, județ, sector sau traseu), `CountyCode` (opțional, pentru propunerea automată a zonei din adresă), `IsActive`.

**Vizibilitatea pe portofoliu** (`SalesDataScope`, pe rol): 1 Toți clienții firmei · 2 Echipa mea (clienții agenților din subordine și ai mei) · 3 Doar portofoliul meu. Filtrul se aplică pe liste, căutări, rapoarte și pe trimiterile programate (Nucleu `ReportSchedule`). Un agent din scope 3 vede totuși un client în afara portofoliului dacă îl caută după CUI exact, ca să poată prelua o comandă telefonică, dar nu îi vede soldul și istoricul.

### 3.8 Nomenclatoarele vânzărilor

Fiecare nomenclator are tabel propriu, cod stabil (nu se schimbă după prima folosire, `52014`), activ/inactiv și date inițiale (seed) livrate prin scripturi `MERGE` care inserează doar ce lipsește (`MIG-06`, `database/seed/`, la fel ca nomenclatoarele INS din Achiziții A6). Seed-ul e în [seed/nomenclatoare-vanzari/](../../seed/nomenclatoare-vanzari/).

| Tabel | Ce ține | Seed livrat | Editabil de client |
| --- | --- | --- | --- |
| `core.PaymentMeansCode` | Codurile UNCL 4461 acceptate de e-Factura (CIUS-RO): `Code`, `NameRo`, `NameEn` | 14 coduri: 1, 10, 20, 30, 31, 42, 48, 49, 57, 58, 59, 68, 97, ZZZ | Nu (doar actualizare din kit) |
| `core.PaymentMethod` | Modalitatea de plată a firmei: `Code`, `Name`, `PaymentMeansCode`, `Kind` (1 numerar · 2 bancă · 3 card · 4 compensare · 5 efect comercial · 6 ramburs prin curier · 7 altul), `RequiresImmediatePayment`, `IsActive`. Comună Vânzărilor, Achizițiilor și Finanțelor (adăugată în Nucleu §3.5) | 8: `NUM` Numerar (10), `OP` Ordin de plată (42), `CARD` Card (48), `DD` Debit direct (49), `CMP` Compensare (97), `CEC` Cec (20), `BO` Bilet la ordin (ZZZ), `RAMB` Ramburs (10) | Da |
| `sales.CustomerGroup` | Grupa comercială: `Code`, `Name`, `PriceListId`, `PaymentTermDays`, `CreditCheckMode` implicite | 5: `RETAIL` Rețele de retail, `HORECA` Hoteluri, restaurante, cafenele, `DISTRIB` Distribuitori și en-gros, `INSTIT` Instituții publice, `PF` Persoane fizice | Da |
| `sales.SalesChannel` | Canalul de vânzare: `Code`, `Name` | 5: `TEREN` Agent de teren, `KAM` Key account, `TEL` Telesales, `ONLINE` Online / B2B, `LICIT` Licitații publice | Da |
| `sales.SalesZone` | Zonele comerciale (3.7), arbore | Niciuna (specifice firmei); asistentul de configurare propune o zonă pe județ | Da |
| `sales.ShippingMethod` | Modul de livrare: `Code`, `Name`, `Kind` (1 ridicare de client · 2 transport propriu · 3 curier · 4 transportator terț), `RequiresVehicle` (cere numărul de înmatriculare pe aviz și pentru e-Transport) | 4: `RIDIC` Ridicare de către client, `PROPRIU` Transport propriu, `CURIER` Curier, `TERT` Transportator terț | Da |
| `sales.ReasonCode` | Motivele cerute de flux: `Kind` (1 ofertă pierdută · 2 închidere linie de comandă · 3 retur · 4 storno · 5 deblocare credit · 6 excepție de preț: sub minim, sub cost, scris de mână, gratuitate (V2) · 7 anulare document · 8 diferență la pregătire sau la livrare (V4)), `Code`, `Name`, `RequiresNote` | 32 de motive, din care 3 adăugate de V2, 3 de V3 (`FARARASP`, `CLIENTANUL`, `DUBLURA`) și 4 de V4 (`LIPSARAFT`, `DETDEPOZIT`, `LOTSCURT`, `REFUZCLIENT`) (de exemplu `PRET` Preț necompetitiv, `TERMEN` Termen de livrare, `DETERIORAT` Marfă deteriorată la transport, `EXPIRAT` Termen de valabilitate scurt, `EROARE` Eroare de facturare, `PROMISIUNE` Promisiune de plată scrisă) | Da (cele livrate se pot dezactiva, nu șterge) |

Codurile e-Transport (tipul și scopul operațiunii) și nomenclatorul CPV pentru facturile către autorități se adaugă în V4 și V5, tot ca tabele separate cu seed.

### 3.9 Setările de vânzări ale firmei (`SalesSettings`)

Un rând pe firmă, creat la activarea modulului. Editat în Administrare › Configurare, grupul „Vânzări” (ecranul `v1-06`).

| Setare | Implicit | Ce înseamnă | Folosită în |
| --- | --- | --- | --- |
| `AllowInvoiceWithDelivery` | 1 | Tranzacția `FV-LIV` (factura cu livrare) poate fi folosită (decizia 1) | V5 |
| `AllowAdvanceInvoices` | 1 | Facturile de avans (`FV-AVS`) și regularizarea lor | V5 |
| `DeliveryTolerancePct` | 0 | Toleranța implicită de depășire la livrare, față de comandă. Se copiază în tranzacțiile noi cu toleranță permisă | V4 |
| `UnderDeliveryAutoClosePct` | 0 | O linie de comandă livrată în proporție de cel puțin `100 − x`% se închide automat la validarea avizului (0 = niciodată automat) | V3, V4 |
| `ReserveOnConfirm` | 1 | Confirmarea comenzii creează rezervări ferme (S5). Cu 0, rezervarea se face abia la dispoziția de livrare | V3 |
| `ReservationExpiryDays` | 14 | Durata implicită a rezervării (S5); suprascrisă pe client în `inv.PartnerReservationRule` | V3 |
| `CreditCheckMode` | 2 | 0 fără verificare · 1 avertizare · 2 blocare (decizia 7) | V3, V4, V5 |
| `CreditCheckPoints` | confirmare + livrare | Momentele verificării: confirmarea comenzii, dispoziția de livrare, validarea avizului sau a facturii cu livrare (bife) | V3, V4, V5 |
| `CreditExposureIncludesOrders` | 1 | Expunerea include comenzile confirmate și nelivrate (4.3) | V3 |
| `OverdueBlockDays` | 30 | Blocare la o factură restantă de mai mult de atâtea zile (0 = nu se verifică) | V3, V4 |
| `CreditReleaseValidHours` | 48 | Cât e valabilă o deblocare unică (4.3) | V3, V4 |
| `QuoteValidityDays` | 30 | Valabilitatea propusă a ofertei | V3 |
| `RequiresCustomerOrderRef` | 0 | Implicit pentru clienții noi (3.4) | V3 |
| `DefaultInvoiceGrouping` | 1 | Gruparea implicită a avizelor în facturi (3.4) | V5 |
| `UninvoicedDeliveryAlertDays` | 5 | Avizele nefacturate mai vechi de atâtea zile apar în „De rezolvat”; până pe 15 ale lunii următoare livrării toate trebuie facturate (Codul fiscal art. 319 alin. 16), altfel alertă roșie | V5, V7 |
| `MaxPaymentTermDays` | 60 | Peste această valoare, termenul de plată primește avertizare (Legea 72/2013) | 3.4, V5 |
| `DefaultDeliveryWarehouseId` | — | Gestiunea de livrare propusă când nici clientul, nici punctul de livrare, nici agentul nu au una | V3, V4 |
| `DefaultPaymentMethodId` | `OP` | Modalitatea de plată pentru clienții noi | 3.4 |
| `EInvoiceSendDeadlineDays`, `EInvoiceDeadlineKind` | 5, lucrătoare | Termenul de transmitere în e-Factura de la data emiterii, cu tipul zilelor, ținut ca setare ca să urmeze modificările legii fără versiune nouă | V5 |
| `EInvoiceCreditNoteFormat` | 381 | Stornarea în e-Factura ca notă de credit (381) sau ca factură cu valori negative (380) (P13) | V6 |
| `DocumentPdfTemplates` | livrate | Șabloanele de tipărire pentru ofertă, comandă (confirmare), proformă, aviz, factură, cu codurile clientului dacă sunt cerute | V3–V6 |
| Setările V3 (`QuoteMaxValidityDays`, `QuoteSendOnApproval`, `ReservationHorizonDays`, `PromiseHorizonDays`, `PickLeadDays`, `PromiseMode`, `DuplicateCustomerRefAction`, `ImportAutoConfirm`, `OrderConfirmationSendMode`, `AutoProformaOnAdvance`, `NewCustomerAdvancePct` și celelalte) | — | Descrise în V3 §3.12 (precizare din V3, 27.11.2026) | V3 |
| Setările V4 (`PickingDocument`, `ShortPickAction`, `TrackDispatchAndPod`, `PodRequiredForInvoice`, `ETransportCheck`, `ETransportRequestAt`) | vezi V4 §3.11 | Pregătirea, plecarea și confirmarea livrării, e-Transport; `DeliveryOrderMode` stă pe gestiune (Stocuri S1) (precizare din Vânzări V4, 27.11.2026) | V4 |
| Setările V5 (`MassInvoicingSchedule`, `MassInvoicingAutoValidate`, `EmailOnValidation`, `EInvoiceSendMode`, `EInvoiceForeignPartners`, `EInvoiceAttachPdf`, `AdvanceInvoiceSeries`, `UninvoicedVatAtMonthEnd`, `PublicAuthorityMaxTermDays`, `CashPaymentLimitCheck`) | vezi V5 §3.10 | Facturarea în masă, e-Factura, avansuri, TVA-ul avizelor nefacturate, plafoane (precizare din Vânzări V5, 27.11.2026) | V5 |
| Setările V7 (`ServiceCompleteTolerancePct`, `ServiceOnTimeLateDays`, `CommissionCalcDay`, `InactiveCustomerDays`, `DecliningCustomerPct`, `ClassAPct`, `ClassBPct`) | vezi V7 §3.8 | Nivelul de servire, comisioanele, analiza clienților. Excepțiile din V1–V6 (deblocări, prețuri, diferențe, note de credit aprobate) se scriu în `sales.ExceptionLog` prin `ISalesExceptionWriter`, în tranzacția documentului (V7 §3.7) (precizare din Vânzări V7, 27.11.2026) | V7 |
| Setările V6 (`ReturnQualityCheck`, `CreditNoteSeries`, `AutoCreditOnReturn`, `RebateSettlementDay`, `SgrReturnLookbackDays`) | vezi V6 §3.8 | Retururile, notele de credit, deconturile de rabat, ambalajele SGR; aprobarea notelor de credit e `SalesApprovalRule` cu `Kind` = 3 (precizare din Vânzări V6, 27.11.2026) | V6 |
| `ApprovalThresholds` | — | Înlocuit de tabelul `sales.SalesApprovalRule` (V2 §3.9; V3 adaugă pragurile pe valoare); setările de prețuri sunt în V2 §3.10 (precizare din V2, 27.11.2026) | V2, V3 |

Precedența pentru setările care există și pe client (`CreditCheckMode`, `OverdueBlockDays`, `RequiresCustomerOrderRef`, gruparea facturilor): tranzacție (dacă e setată explicit) → client → grupa comercială → firmă.

## 4. Fluxuri principale

### 4.1 Crearea unei tranzacții proprii

Exemplu: Exemplu Distribuție vinde cafenelelor din Cluj direct din mașina agentului Vlad Georgescu (gestiunea `AUTO-CJ01`). Factura se emite pe loc, cu livrare. Modalitatea de plată nu ține de tranzacție: vine din condițiile clientului (3.4), de exemplu `POS-T` sau `NUM`.

1. Administratorul (Andrei Matei) deschide Configurare › Documente › Tranzacții, filtrează pe Vânzări › Factură și alege „Copiază” pe `FV-LIV`.
2. Tranzacția nouă `FV-LIV-AG` „Factură cu livrare din mașina agentului” moștenește comportamentul `Invoice.WithDelivery`, sursa opțională `SalesOrder` și grupurile `INVOICE` + `DELIVER`. Comportamentul nu se poate schimba (`50029` după prima folosire).
3. Setează valorile implicite permise de comportament: gestiunea `AUTO-CJ01`, seria fiscală a mașinii (`FV CJA`), modul de livrare `PROPRIU`, și semnalul de contare `X_AUTO` (opțional, dacă C2 are o variantă pe el).
4. În tab-ul Roluri bifează rolul „Agent de teren” și o face implicită pentru el pe factură. Pe fișa clientului Cafe Unirii SRL, tranzacția de factură implicită devine `FV-LIV-AG`.
5. La salvare se verifică: comportamentul există și e al tipului de document, cheile din valorile implicite sunt permise, nu există altă tranzacție implicită pentru același rol (`50030`). Modificarea se auditează.

### 4.2 Generarea unui aviz din comandă (exemplu complet)

Comanda `CV 4421` din 20.11.2026 a lanțului Mega Retail SRL (numărul lor `PO-MR-77815`, punctul de livrare `MR-CJ-07` Mega Retail Cluj Mărăști, GLN `5940000123457`) cere 3.000 buc APA05 (250 baxuri de 12), 1.200 buc APA15 (200 baxuri de 6) și 1.440 buc CEAI05, livrare pe 27.11 din DEP-CJ. Pe 25.11 s-au livrat deja 720 buc CEAI05 (avizul `EXD 30602`). Pe 26.11, Radu Stan a preluat 480 buc APA05 din rezervarea comenzii pentru Hotel Central SRL (Stocuri S5, fluxul 4.3), deci CV 4421 are pe APA05 2.520 buc rezervate și 480 buc în așteptare, până la recepția din 02.12.

1. Gheorghe Ilie (Șef depozit) creează un aviz. Tranzacția implicită a rolului lui e `AV-CV`; asistentul pornește la pasul 2.
2. Pasul 2 arată comenzile confirmate, fără blocare de credit, cu rest de livrat din DEP-CJ și cu data de livrare până la 27.11. `CV 4421` are trei linii cu rest. O bifează.
3. Pasul 3 arată liniile. Pe APA05: comandat 3.000, livrat 0, rezervat 2.520, în așteptare 480, de livrat propus 2.520 (propunerea se oprește la cantitatea rezervată și disponibilă, nu la rest). APA15: de livrat 1.200. CEAI05: comandat 1.440, livrat 720, de livrat 720. Lanțul cere minimum 45 de zile până la expirare (S4); asistentul propune loturile FEFO care respectă regula (APA05: L2610-18 1.512 buc și L2611-07 1.008 buc; lotul L2611-15 e în carantină și nu e propus).
4. „Generează”: Vânzările creează avizul cu liniile mapate din comandă (variantă, UM, preț, gestiune, codul clientului din catalog) și cer `IDocumentLinkService.ConsumeAsync` cu starea „în ciornă”. Restul de livrat pe CV 4421 devine 480 buc APA05.
5. Avizul primește numărul de înmatriculare al mașinii (modul de livrare `PROPRIU`) și, pentru că transportul e pe teritoriul național cu bunuri cu risc fiscal ridicat doar dacă articolele sunt marcate astfel, codul UIT e cerut doar când regula din V4 o impune.
6. La validare, într-o singură tranzacție SQL: verificarea de credit (punctul „livrare”, 4.3), numărul fiscal al avizului (Nucleu fluxul 4.2), mișcarea de stoc prin `IStockLedger.PostAsync` cu `ConsumesReservationOfSourceLineId` (S5), activarea legăturilor, istoricul stării, evenimentul `LivrareEfectuata` în outbox. Dacă oricare pas eșuează, nu se salvează nimic.
7. Avizul `EXD 30618` rămâne „de facturat” (grupul `INVOICE`). Mega Retail are `InvoiceGrouping = 1` (o factură pe aviz), deci facturarea în masă de seară (V5) generează factura cu `FV-AV`. Pe comandă rămân de livrat 480 buc APA05, care se vor livra după recepția din 02.12 (ecranul `v1-07`).

### 4.3 Verificarea limitei de credit

**Expunerea** clientului, în lei, la momentul verificării:

```
Expunere = Sold de încasat (F3, IPartnerBalanceQueries: facturi deschise − încasări nealocate)
         + Avize validate și nefacturate (valoare cu TVA, din grupul INVOICE)
         + Comenzi confirmate și nelivrate (valoare cu TVA a restului de livrat; doar dacă CreditExposureIncludesOrders = 1)
         + Documentul curent (doar partea care nu e deja inclusă mai sus)
```

Documentul curent se adaugă o singură dată: o comandă confirmată e deja în expunere, deci avizul generat din ea nu o mai adaugă; un aviz fără comandă se adaugă cu valoarea lui.

**Verificările**, în punctele bifate în `CreditCheckPoints`: (a) expunerea depășește limita; (b) clientul are o factură restantă de mai mult de `OverdueBlockDays` zile; (c) clientul e blocat (`IsBlocked`, verificat mereu, indiferent de mod). Cu modul 1 (avertizare), documentul se salvează cu mesaj. Cu modul 2 (blocare), comanda se confirmă cu `CreditHold = 1` (nu poate fi livrată sau pregătită), iar avizul și factura cu livrare nu se validează (`52005` pentru limită, `52006` pentru restanță, `52004` pentru client blocat).

**Deblocarea unică** (`CreditRelease`): cine are `Sales.Credit.Release` (implicit Șef vânzări și Contabil șef) deblochează un document anume sau clientul pentru o sumă și o perioadă (`CreditReleaseValidHours`, implicit 48 de ore), cu motiv din `ReasonCode` (tipul 5). Deblocarea se consumă la prima validare care o folosește și se scrie în jurnalul de excepții al vânzărilor (V7). Deblocarea nu schimbă limita: limita o schimbă doar cine are `Sales.CustomerTerms.Manage`, cu istoric.

Exemplu: Cafe Unirii SRL are limita 10.000,00 lei. Pe 27.11.2026 soldul de încasat din F3 e 7.850,00 lei, din care factura `FV EXD 10187` (1.240,00 lei) e restantă de 38 de zile; avizele nefacturate sunt 620,00 lei. Agentul Vlad Georgescu confirmă comanda `CV 4433` (600 buc APA05 × 1,8500 = 1.110,00 lei, cu TVA 11 % 1.232,10 lei). Expunerea devine 9.702,10 lei, sub limită, dar restanța de 38 de zile depășește pragul de 30, deci comanda se confirmă cu `CreditHold`. Radu Stan vede comanda în „De rezolvat”, sună clientul, primește promisiunea de plată scrisă până pe 29.11 și acordă o deblocare unică pentru CV 4433 (motiv `PROMISIUNE`, valabilă 48 de ore). Comanda poate fi livrată; deblocarea apare în jurnalul de excepții (ecranul `v1-03`).

### 4.4 Potrivirea unei comenzi importate cu catalogul clientului

Folosită de `CV-IMP` (Excel, EDI, comenzi primite pe e-mail ca fișier) și de portalul B2B (viitor). Intrare: clientul (identificat după CUI sau după GLN-ul punctului de livrare), codul clientului, GTIN-ul, codul nostru dacă îl trimite, denumirea, UM-ul (cod UN/ECE), cantitatea și, opțional, prețul. Ordinea, oprită la prima potrivire sigură:

| Pas | Criteriu | Încredere |
| --- | --- | --- |
| 1 | Codul nostru (SKU-ul variantei) trimis de client | Sigură |
| 2 | GTIN = un cod de bare din `core.ItemBarcode` (dă și UM-ul, dacă e legat de o UM) | Sigură |
| 3 | Codul clientului + UM = un rând `CustomerItem` al clientului cu același UM | Sigură |
| 4 | Codul clientului = un singur rând `CustomerItem`, în alt UM | Sigură, cu UM de confirmat dacă factorul nu se deduce |
| 5 | Denumirea identică cu `CustomerName` dintr-un rând de catalog al clientului, după normalizare (fără diferență de majuscule, diacritice și spații multiple) | Sigură |
| 6 | Denumirea conține sau e conținută de `CustomerName` (minim 12 caractere comune) | Probabilă: se propune, nu se aplică |

La „probabilă” sau fără potrivire, linia rămâne în comanda importată, marcată, și comanda nu se poate confirma până nu e rezolvată. Confirmarea manuală creează sau completează rândul `CustomerItem` (`Source = 3`), deci data viitoare potrivirea e sigură. Fiecare linie păstrează pasul care a găsit-o. Prețul primit de la client nu înlocuiește prețul nostru: diferența se afișează pe linie și se tratează în V2 (prețul de pe comanda clientului e acceptat doar cu drept, ca abatere de preț).

### 4.5 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Același cod al clientului pentru două variante | Al doilea rând activ cu același cod și UM e refuzat (`52002`). Varianta veche se dezactivează întâi |
| Clientul își schimbă codurile | Se importă rândurile noi; cele vechi rămân pentru documentele vechi, dezactivate |
| Partenerul pierde rolul de client | Nucleu refuză debifarea dacă există documente de vânzare deschise (comenzi nelivrate, avize nefacturate); altfel condițiile, catalogul și portofoliile rămân, inactive |
| Agentul pleacă din firmă (utilizator dezactivat) | Documentele lui rămân pe el (regula 6); clienții din portofoliu apar în „De rezolvat” la șeful de echipă, care îi mută în bloc pe alt agent, cu data de la care se aplică |
| Plătitorul desemnat are la rândul lui plătitor | Refuzat (`52008`): un singur nivel, ca alocarea propusă de F3 să rămână clară |
| Tranzacția implicită a rolului a fost dezactivată | La creare se propune prima tranzacție activă a rolului; lipsa oricărei tranzacții active dă `52012` cu trimitere spre Configurare |
| Două ciorne de aviz din aceeași comandă, deschise în paralel | Prima rezervă cantitatea („în ciornă”, Nucleu §3.6 regula 5); a doua vede restul redus și, la depășire, primește `50027` |
| Comanda din care s-a livrat parțial trebuie modificată | Liniile deja preluate nu se mai modifică (`50028`); se adaugă linii noi sau se închide restul cu motiv (Nucleu `DocumentLineClosure`), iar rezervarea restului se eliberează (S5) |
| Limita de credit e în lei, clientul cumpără în EUR | Expunerea se calculează în lei, la cursul BNR al zilei verificării pentru documentele în valută nefacturate și la valoarea în lei din F3 pentru sold |
| Factura cu livrare se stornează | Stornarea facturii nu reintroduce marfa în stoc: returul fizic se face prin `RC-AV` pe avizul intern, iar factura de stornare prin `FS-RC` (V6) |
| Deblocare acordată, dar documentul se modifică în plus | Deblocarea pe document acoperă doar valoarea de la acordare; o creștere peste ea cere o deblocare nouă |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `ConditiiClientModificate` v1 | Salvarea condițiilor unui client în firmă | `CompanyId`, `PartnerId`, `CustomerGroupId`, `PriceListId`, `PaymentTermDays`, `CreditLimitAmount`, `SalesAgentId`, `IsBlocked` | POS (lista de prețuri și clienții cu discount), CRM, Raportare |
| `ClientBlocat` v1 | Blocarea sau deblocarea clientului în firmă | `CompanyId`, `PartnerId`, `IsBlocked`, `BlockReason` | Finanțe (avertizare la compensări), CRM, POS |
| `DeblocareCreditAcordata` v1 | O deblocare unică acordată sau consumată | `CompanyId`, `PartnerId`, `DocumentRef`, `Amount`, `ValidUntil`, `ReasonCode`, `GrantedBy` | Raportare (jurnalul de excepții, V7) |
| `CatalogClientModificat` v1 | Salvarea, importul sau învățarea unui rând de catalog | `CompanyId`, `PartnerId`, `ItemVariantId`, `CustomerCode`, `OrderUomId`, `IsListed` | Integrări (EDI), Raportare |
| `PortofoliuAgentModificat` v1 | Schimbarea agentului principal pe un client, o zonă sau o grupă | `CompanyId`, `AgentId`, `PartnerId` / `SalesZoneId` / `CustomerGroupId`, `ValidFrom` | CRM (proprietarul contului), Raportare |

### 5.2 Consumate

| Eveniment | De la | Ce face V1 |
| --- | --- | --- |
| `PartenerModificat` v1 | Nucleu | La `IsCustomer` 1 → 0: dezactivează condițiile, catalogul și portofoliile clientului. La `AnafInactive` = 1 sau la radiere: notifică agentul și șeful de vânzări |
| `ArticolModificat` v1 | Nucleu | La dezactivarea unei variante: marchează rândurile de catalog ca „variantă inactivă” (raportul „Catalog incomplet”) |
| `FirmaModificata` v1 | Nucleu | La activarea modulului pe o firmă: `SalesSettings` cu valorile implicite, seriile din 3.1, tranzacțiile din 3.2 și nomenclatoarele din 3.8 |
| `UtilizatorDezactivat` v1 | Nucleu | Agentul legat devine inactiv; portofoliul lui apare în „De rezolvat” la șeful de echipă (4.5) |
| `FacturaAchitata` v1, `IncasareInregistrata` v1 | Finanțe (F3, F1, F2) | Comenzile cu `CreditHold` ale clientului se reverifică automat; cele care trec sunt deblocate și agentul e notificat |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V1)

```csharp
public enum SalesDocumentType : byte
{
    SalesQuote = 1, SalesOrder = 2, SalesProforma = 3, DeliveryOrder = 4, SalesDeliveryNote = 5,
    SalesInvoice = 6, SalesCreditNote = 7, CustomerReturn = 8, BillingPlan = 9
}
public enum CreditCheckMode : byte { None = 0, Warn = 1, Block = 2 }
[Flags] public enum CreditCheckPoint : byte { OrderConfirm = 1, DeliveryOrder = 2, Delivery = 4 }
public enum InvoiceGrouping : byte { PerDeliveryNote = 1, PerOrder = 2, Daily = 3, Weekly = 4, Monthly = 5 }
public enum SalesDataScope : byte { All = 1, Team = 2, Own = 3 }

public sealed record CustomerTermsDto(int CompanyId, int PartnerId, int CustomerGroupId, int? SalesChannelId,
    int? PriceListId, string CurrencyCode, short PaymentTermDays, int PaymentMethodId, decimal? CreditLimitAmount,
    CreditCheckMode CreditCheckMode, short OverdueBlockDays, bool CashOnly, int? PayerPartnerId, int? SalesAgentId,
    int? SalesZoneId, int? ShippingMethodId, int? DefaultWarehouseId, InvoiceGrouping InvoiceGrouping,
    bool RequiresCustomerOrderRef, bool RequiresCustomerItemCodes, bool AllowPartialDelivery, bool IsBlocked);
// Întors cu precedența aplicată (client → grupă → firmă); câmpurile nu sunt goale decât unde gol are sens

public sealed record CreditExposureDto(int CompanyId, int PartnerId, decimal? CreditLimitRon, decimal OpenBalanceRon,
    decimal UninvoicedDeliveriesRon, decimal OpenOrdersRon, decimal ExposureRon, int MaxOverdueDays, bool IsBlocked);
public sealed record CreditCheckResult(bool Passed, CreditCheckMode Mode, string? FailureCode /* SALES_CREDIT_... */,
    CreditExposureDto Exposure, long? CreditReleaseId);

// Folosit de V3–V5 (în tranzacția documentului), POS (clienții pe factură), CRM (afișare)
public interface ICreditCheck
{
    Task<CreditExposureDto> GetExposureAsync(int companyId, int partnerId, CancellationToken ct);
    Task<CreditCheckResult> CheckAsync(int companyId, int partnerId, CreditCheckPoint point,
        DocumentRef document, decimal additionalAmountRon, CancellationToken ct);
}

public interface ICustomerTermsProvider
{
    Task<CustomerTermsDto> GetAsync(int companyId, int partnerId, CancellationToken ct);
    Task<int?> ProposeAgentAsync(int companyId, int partnerId, int? deliveryPointId, int userId, CancellationToken ct);
}

public sealed record IncomingOrderLine(string? OurCode, string? CustomerCode, string? Gtin, string Name,
    string? UomCode, decimal Qty, decimal? UnitPrice);
public sealed record CustomerMatchResult(int? ItemVariantId, int? UomId, byte Confidence /* 0 nimic, 1 probabilă, 2 sigură */,
    byte? Step, int? CustomerItemId);

// Folosit de V3 (import comenzi), V4–V5 (tipărire, e-Factura BuyersItemIdentification), Integrări (EDI)
public interface ICustomerCatalog
{
    Task<IReadOnlyList<CustomerMatchResult>> MatchAsync(int companyId, int partnerId,
        IReadOnlyList<IncomingOrderLine> lines, CancellationToken ct);
    Task LearnAsync(int companyId, int partnerId, IncomingOrderLine line, int itemVariantId, int uomId, CancellationToken ct);
    Task<IReadOnlyDictionary<int, string>> GetCustomerCodesAsync(int companyId, int partnerId,
        IReadOnlyCollection<int> itemVariantIds, CancellationToken ct);
}

// Citit de Stocuri S5 (disponibil proiectat) și de MRP: ce e încă de livrat pe comenzile confirmate
public interface ISalesOrderDemand
{
    Task<IReadOnlyList<SalesDemandRow>> GetOpenDemandAsync(int companyId, IReadOnlyCollection<int> itemVariantIds,
        int? warehouseId, CancellationToken ct);
}
// Un rând pe livrare (SalesOrderSchedule), cu data cerută a livrării (precizare din V3, 27.11.2026)
public sealed record SalesDemandRow(long SalesOrderId, string Number, long ScheduleId, int PartnerId, int ItemVariantId,
    int WarehouseId, decimal OpenQtyBase, DateOnly RequiredDate, bool IsReserved, bool IsOnCreditHold);

public interface ISalesSettingsProvider
{
    Task<SalesSettingsDto> GetAsync(int companyId, CancellationToken ct);
}
```

Comportamentele din 3.2 sunt clase interne ale modulului (`Erp.Sales.Application.Behaviors`), care implementează `ITransactionBehavior` din Nucleu și se înregistrează în DI. Fiecare are un test care verifică maparea câmpurilor sursă → destinație (12).

Comenzi interne V1: `SaveCustomerTerms`, `BlockCustomer`, `UnblockCustomer`, `SaveDeliveryPoint`, `GrantCreditRelease`, `RevokeCreditRelease`, `SaveCustomerItem`, `DeactivateCustomerItem`, `SaveSalesAgent`, `SaveAgentPortfolio`, `ReassignPortfolio`, `SaveSalesZone`, `SaveSalesNomenclature` (grupe, canale, moduri de livrare, motive), `SaveSalesSettings`. Modalitățile de plată se salvează prin comanda Nucleului `SavePaymentMethod`. Tranzacțiile se salvează prin comenzile Nucleului (`SaveTransactionType`, `CopyTransactionType`).

## 7. Schema SQL `sales` (partea V1)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.SalesSettings` | `Id int` | UQ `CompanyId`; FK `CompanyId`, `DefaultDeliveryWarehouseId`, `DefaultPaymentMethodId` | — |
| `sales.CustomerTerms` | `Id int` | UQ `(CompanyId, PartnerId)`; FK `CompanyId`, `PartnerId`, `CustomerGroupId`, `SalesChannelId`, `CurrencyCode`, `PaymentMethodId`, `PayerPartnerId`, `SalesAgentId`, `SalesZoneId`, `ShippingMethodId`, `DefaultWarehouseId`, cele patru tranzacții, `Incoterm`, `TransportModeCode`, `TransactionNatureCode` (`PriceListId` primește FK în migrarea V2) | `(CompanyId, SalesAgentId)`; `(CompanyId, CustomerGroupId)`; `(CompanyId, PayerPartnerId) WHERE PayerPartnerId IS NOT NULL` |
| `sales.CustomerDeliveryPoint` | `Id int` | UX `(CompanyId, PartnerAddressId)`; UX `(CompanyId, PartnerId) WHERE IsDefault = 1`; FK `PartnerId`, `PartnerAddressId`, `SalesZoneId`, `DefaultWarehouseId`, `ShippingMethodId`, `ContactId` | `(CompanyId, PartnerId, IsActive)` |
| `sales.CreditRelease` | `Id bigint` | FK `CompanyId`, `PartnerId`, `ReasonCodeId`, `GrantedBy`; `CHECK (Amount > 0)` | `(CompanyId, PartnerId, ValidUntil) WHERE ConsumedAt IS NULL AND RevokedAt IS NULL` |
| `sales.CustomerItem` | `Id int` | UX `(PartnerId, CompanyKey, CustomerCodeKey, OrderUomId) WHERE CustomerCode IS NOT NULL AND IsActive = 1`; FK `CompanyId`, `PartnerId`, `ItemVariantId`, `OrderUomId` | `(ItemVariantId) INCLUDE (PartnerId, CustomerCode)`; `(PartnerId, CustomerNameSearch)`; `(PartnerId, IsListed)` |
| `sales.SalesAgent` | `Id int` | UX `(CompanyId, CodeKey)`; UX `(CompanyId, UserId) WHERE UserId IS NOT NULL`; FK `UserId`, `EmployeeId` (fără FK între module: doar valoare, `SQL-16`), `PartnerId`, `ManagerAgentId`, `DefaultWarehouseId` | `(CompanyId, ManagerAgentId)` |
| `sales.AgentPortfolio` | `Id int` | `CHECK` exact unul din `PartnerId`, `SalesZoneId`, `CustomerGroupId`; UX pe `(CompanyId, PartnerId)`, `(CompanyId, SalesZoneId)`, `(CompanyId, CustomerGroupId)` `WHERE IsPrimary = 1 AND ValidTo IS NULL`; FK `AgentId`, `SubstituteAgentId` | `(AgentId, ValidTo)` |
| `sales.SalesZone` | `Id int` | UX `(CompanyId, CodeKey)`; FK `ParentZoneId`, `CountyCode` | `(CompanyId, ParentZoneId)` |
| `sales.CustomerGroup`, `sales.SalesChannel`, `sales.ShippingMethod` | `Id int` | UX `(CodeKey)`; `CustomerGroup` FK `PaymentMethodId` (și `PriceListId` din V2) | — |
| `sales.ReasonCode` | `Id int` | UX `(Kind, CodeKey)` | — |
| `core.PaymentMeansCode` (nou, Nucleu) | `Code varchar(3)` | — | — |
| `core.PaymentMethod` (nou, Nucleu) | `Id int` | UX `(CodeKey)`; FK `PaymentMeansCode` | — |
| `core.PartnerAddress` (coloană nouă, Nucleu) | — | `Gln varchar(13) NULL` cu `CHECK` pe 13 cifre; UX `(Gln) WHERE Gln IS NOT NULL` | — |

`CompanyKey` e o coloană calculată persistentă (`ISNULL(CompanyId, 0)`), ca unicitatea să trateze „toate firmele” ca o valoare.

Exemplu de tabel complet:

```sql
CREATE TABLE sales.CustomerTerms (
    Id                              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerTerms PRIMARY KEY,
    CompanyId                       int               NOT NULL CONSTRAINT FK_CustomerTerms_Company REFERENCES core.Company(Id),
    PartnerId                       int               NOT NULL CONSTRAINT FK_CustomerTerms_Partner REFERENCES core.Partner(Id),
    CustomerGroupId                 int               NOT NULL CONSTRAINT FK_CustomerTerms_CustomerGroup REFERENCES sales.CustomerGroup(Id),
    SalesChannelId                  int               NULL CONSTRAINT FK_CustomerTerms_SalesChannel REFERENCES sales.SalesChannel(Id),
    PriceListId                     int               NULL,   -- FK spre sales.PriceList, adăugat de migrarea V2
    CurrencyCode                    char(3)           NOT NULL CONSTRAINT FK_CustomerTerms_Currency REFERENCES core.Currency(Code),
    PaymentTermDays                 smallint          NOT NULL,
    PaymentMethodId                 int               NOT NULL CONSTRAINT FK_CustomerTerms_PaymentMethod REFERENCES core.PaymentMethod(Id),
    CreditLimitAmount               decimal(19,4)     NULL,
    CreditCheckMode                 tinyint           NULL,   -- NULL = setarea grupei, apoi a firmei
    OverdueBlockDays                smallint          NULL,
    CashOnly                        bit               NOT NULL CONSTRAINT DF_CustomerTerms_CashOnly DEFAULT (0),
    PayerPartnerId                  int               NULL CONSTRAINT FK_CustomerTerms_Payer REFERENCES core.Partner(Id),
    SalesAgentId                    int               NULL CONSTRAINT FK_CustomerTerms_SalesAgent REFERENCES sales.SalesAgent(Id),
    SalesZoneId                     int               NULL CONSTRAINT FK_CustomerTerms_SalesZone REFERENCES sales.SalesZone(Id),
    ShippingMethodId                int               NULL CONSTRAINT FK_CustomerTerms_ShippingMethod REFERENCES sales.ShippingMethod(Id),
    DefaultWarehouseId              int               NULL CONSTRAINT FK_CustomerTerms_Warehouse REFERENCES inv.Warehouse(Id),
    InvoiceGrouping                 tinyint           NOT NULL CONSTRAINT DF_CustomerTerms_InvoiceGrouping DEFAULT (1),
    RequiresCustomerOrderRef        bit               NOT NULL CONSTRAINT DF_CustomerTerms_ReqOrderRef DEFAULT (0),
    RequiresCustomerItemCodes       bit               NOT NULL CONSTRAINT DF_CustomerTerms_ReqItemCodes DEFAULT (0),
    PrintCustomerNames              bit               NOT NULL CONSTRAINT DF_CustomerTerms_PrintNames DEFAULT (0),
    AllowPartialDelivery            bit               NOT NULL CONSTRAINT DF_CustomerTerms_Partial DEFAULT (1),
    Incoterm                        char(3)           NULL CONSTRAINT FK_CustomerTerms_DeliveryTerm REFERENCES core.DeliveryTerm(Code),
    IncotermPlace                   nvarchar(100)     NULL,
    TransportModeCode               char(1)           NULL CONSTRAINT FK_CustomerTerms_TransportMode REFERENCES core.TransportMode(Code),
    TransactionNatureCode           varchar(3)        NULL CONSTRAINT FK_CustomerTerms_TransactionNature REFERENCES core.TransactionNature(Code),
    DefaultQuoteTransactionTypeId   int               NULL CONSTRAINT FK_CustomerTerms_TtQuote REFERENCES core.TransactionType(Id),
    DefaultOrderTransactionTypeId   int               NULL CONSTRAINT FK_CustomerTerms_TtOrder REFERENCES core.TransactionType(Id),
    DefaultDeliveryTransactionTypeId int              NULL CONSTRAINT FK_CustomerTerms_TtDelivery REFERENCES core.TransactionType(Id),
    DefaultInvoiceTransactionTypeId int               NULL CONSTRAINT FK_CustomerTerms_TtInvoice REFERENCES core.TransactionType(Id),
    InvoiceEmail                    nvarchar(200)     NULL,
    IsBlocked                       bit               NOT NULL CONSTRAINT DF_CustomerTerms_IsBlocked DEFAULT (0),
    BlockReason                     nvarchar(300)     NULL,
    Notes                           nvarchar(1000)    NULL,
    CreatedAt                       datetime2(3)      NOT NULL CONSTRAINT DF_CustomerTerms_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy                       int               NOT NULL,
    ModifiedAt                      datetime2(3)      NULL,
    ModifiedBy                      int               NULL,
    RowVersion                      rowversion        NOT NULL,
    CONSTRAINT UQ_CustomerTerms_Partner UNIQUE (CompanyId, PartnerId),
    CONSTRAINT CK_CustomerTerms_Credit CHECK (ISNULL(CreditLimitAmount, 0) >= 0 AND ISNULL(OverdueBlockDays, 0) >= 0),
    CONSTRAINT CK_CustomerTerms_Mode CHECK (CreditCheckMode IS NULL OR CreditCheckMode IN (0, 1, 2)),
    CONSTRAINT CK_CustomerTerms_Grouping CHECK (InvoiceGrouping BETWEEN 1 AND 5),
    CONSTRAINT CK_CustomerTerms_Payer CHECK (PayerPartnerId IS NULL OR PayerPartnerId <> PartnerId),
    CONSTRAINT CK_CustomerTerms_Block CHECK (IsBlocked = 0 OR BlockReason IS NOT NULL)
);

CREATE INDEX IX_CustomerTerms_Agent ON sales.CustomerTerms (CompanyId, SalesAgentId);
CREATE INDEX IX_CustomerTerms_Group ON sales.CustomerTerms (CompanyId, CustomerGroupId);
CREATE INDEX IX_CustomerTerms_Payer ON sales.CustomerTerms (CompanyId, PayerPartnerId) WHERE PayerPartnerId IS NOT NULL;
```

Procedura de expunere (`sales.usp_Credit_Exposure`) primește clientul și documentul curent și întoarce componentele din 4.3. Soldul vine din F3 prin contract, nu prin join în schema `fin` (`SQL-16`): aplicația cere soldul prin `IPartnerBalanceQueries`, apoi trimite valoarea ca parametru procedurii, care adaugă avizele nefacturate și comenzile deschise din `sales`. Verificarea și consumul deblocării se fac în tranzacția documentului, cu `UPDLOCK` pe rândul `CreditRelease` (`SP-03`).

Proceduri: `sales.usp_CustomerTerms_Save`, `sales.usp_CustomerTerms_Get` (cu precedența aplicată), `sales.usp_DeliveryPoint_Save`, `sales.usp_Credit_Exposure`, `sales.usp_CreditRelease_Grant`, `sales.usp_CreditRelease_Consume`, `sales.usp_CustomerItem_Save`, `sales.usp_CustomerItem_Match` (TVP, pașii 1–5 din 4.4; pasul 6 în aplicație), `sales.usp_CustomerItem_Learn`, `sales.usp_SalesAgent_Save`, `sales.usp_AgentPortfolio_Save`, `sales.usp_AgentPortfolio_Reassign`, `sales.usp_Agent_Propose`, `sales.usp_SalesSettings_Save`, `sales.usp_SalesOrderDemand_Open` (TVP, pentru S5 și MRP). Seed: `database/seed/core/44_PaymentMeansCode.sql`, `45_PaymentMethod.sql`, `database/seed/sales/50_CustomerGroup.sql` … `54_ReasonCode.sql`.

Plaja de erori `52000–52999` se împarte pe părți: V1 `52000–52099`, V2 `52100–52199`, V3 `52200–52349`, V4 `52350–52499`, V5 `52500–52699`, V6 `52700–52799`, V7 `52800–52899`, rezervă `52900–52999`. Codurile API au prefixul `SALES_` (`NET-09`).

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52001 | `SALES_PARTNER_NOT_CUSTOMER` | 409 | Partenerul nu are rolul de client |
| 52002 | `SALES_CUSTOMER_ITEM_DUPLICATE` | 409 | Același cod al clientului, în același UM, e activ la altă variantă |
| 52003 | `SALES_UOM_NOT_IN_ITEM_SET` | 400 | UM-ul de comandă nu e în setul de UM al articolului și nici excepție pe articol |
| 52004 | `SALES_CUSTOMER_BLOCKED` | 409 | Comandă confirmată sau livrare pentru un client blocat în firmă |
| 52005 | `SALES_CREDIT_LIMIT_EXCEEDED` | 409 | Expunerea depășește limita de credit, în modul blocare, fără deblocare valabilă |
| 52006 | `SALES_CREDIT_OVERDUE` | 409 | Clientul are o factură restantă peste pragul de zile, în modul blocare |
| 52007 | `SALES_CREDIT_RELEASE_INVALID` | 409 | Deblocarea a expirat, a fost consumată, revocată sau e mai mică decât valoarea documentului |
| 52008 | `SALES_PAYER_INVALID` | 400 | Plătitorul nu e client, e chiar clientul sau are el însuși plătitor desemnat |
| 52009 | `SALES_DELIVERY_POINT_INVALID` | 400 | Adresa nu e a clientului sau nu e de tip livrare |
| 52010 | `SALES_AGENT_PRIMARY_DUPLICATE` | 409 | Al doilea agent principal pe același client, zonă sau grupă, în aceeași perioadă |
| 52011 | `SALES_SETTINGS_INVALID` | 400 | Toleranță în afara intervalului 0–100, prag negativ, niciun punct de verificare cu modul blocare |
| 52012 | `SALES_NO_ACTIVE_TRANSACTION` | 409 | Utilizatorul nu are nicio tranzacție activă pentru tipul de document |
| 52013 | `SALES_REASON_REQUIRED` | 400 | Lipsește motivul (sau nota cerută de motiv) la o acțiune care îl cere |
| 52014 | `SALES_CODE_LOCKED` | 409 | Schimbarea codului unei valori de nomenclator, a unui agent sau a unei zone deja folosite |
| 52015 | `SALES_STOCK_ITEM_NOT_ALLOWED` | 409 | Articol stocabil pe o tranzacție fără efect în stoc (`FV-MAN`, `FV-CV`) |
| 52016 | `SALES_DELIVERY_ALREADY_INVOICED` | 409 | Stornarea avizului peste cantitatea livrată și nefacturată |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.CustomerTerms.View` / `.Manage` | Condițiile clientului în firmă și punctele de livrare (tab-ul Vânzări din fișa partenerului); `.Manage` include limita de credit |
| `Sales.Customer.Block` | Blocarea și deblocarea clientului |
| `Sales.Credit.Release` | Deblocările unice ale limitei de credit și ale restanțelor |
| `Sales.Credit.ViewExposure` | Vederea soldului și a expunerii clientului (agenții o au doar pe portofoliul lor) |
| `Sales.CustomerItem.Manage` | Catalogul clientului și listarea articolelor |
| `Sales.Agent.Manage` | Agenții, echipele, zonele și portofoliile |
| `Sales.Document.ChangeAgent` | Schimbarea agentului propus pe un document |
| `Sales.Return.WithoutSource` | Retur de la client fără aviz sursă (`RC-MAN`) |
| `Sales.Nomenclature.Manage` | Grupele comerciale, canalele, modurile de livrare, motivele |
| `Sales.Settings.Manage` | Setările de vânzări ale firmei |

Roluri livrate (noi, în Nucleu, pentru modulul Vânzări), cu `SalesDataScope` între paranteze: **Șef vânzări** (toate permisiunile V1, plus aprobările din V2 și V3; scope 1), **Key account** și **Agent de teren** (`CustomerTerms.View`, `Credit.ViewExposure`, `CustomerItem.Manage` pe portofoliu; scope 3, iar șeful de echipă scope 2), **Telesales** (ca agentul, scope 3), **Operator facturare** (`CustomerTerms.View`, `CustomerItem.Manage`, emiterea facturilor din V5; scope 1), **Gestionar** și **Șef depozit** (primesc din V4 dreptul pe dispoziții și avize; din V1 doar `CustomerTerms.View` pentru punctele de livrare), **Contabil clienți** (`View` pe tot V1, `Credit.Release` opțional). Tranzacțiile se administrează cu `Core.TransactionType.Manage` (Nucleu), modalitățile de plată cu `Core.Nomenclature.Manage`.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V1 |
| --- | --- |
| Codul fiscal art. 319 (facturarea) | Factura se emite cel târziu până pe 15 ale lunii următoare livrării: setarea `UninvoicedDeliveryAlertDays` și alerta din 3.9; seriile fiscale continue pe aviz și factură (3.1) |
| OUG 120/2021 și Codul fiscal art. 319 (e-Factura, RO_CIUS, EN 16931) | Câmpurile din V1 corespund elementelor UBL: `BuyersItemIdentification` (catalogul clientului), `DeliveryLocation/ID` (GLN, schema `0088`), `PaymentMeansCode` (UNCL 4461, 3.8), `OrderReference` (numărul de comandă al clientului). Termenul de transmitere e setare (3.9) |
| OMFP 2634/2015 (documentele financiar-contabile) | Avizul de însoțire a mărfii (14-3-6A) ca document al ieșirii la vânzare, cu numerotare continuă; avizul intern al facturii cu livrare nu e document separat, pentru că factura ține loc de aviz |
| Codul fiscal art. 11 (parteneri inactivi) | Avertizare pe documentele de vânzare către clienți inactivi; blocare pentru radiați |
| Legea 72/2013 (întârzieri la plată între profesioniști) | Termenul de plată propus pe client; peste 60 de zile, avertizare la salvare (cu autoritățile publice, termenul legal e de regulă 30 de zile, verificat în V5) |
| OUG 41/2022 (e-Transport) | Modul de livrare spune dacă avizul cere numărul vehiculului; regulile UIT sunt în V4 |
| Intrastat (INS) | `Incoterm`, modul de transport și natura tranzacției pe client, preluate pe livrările intracomunitare (Stocuri S7) |
| GDPR | Clientul persoană fizică: datele de contact și punctele de livrare se văd doar cu dreptul de date personale din Nucleu; agenții văd doar portofoliul lor |

Integrări externe: niciuna în V1. Interne: Nucleu (parteneri, adrese, variante, UM, tranzacții, import, modalități de plată), Finanțe F3 (`IPartnerBalanceQueries` pentru expunere), Stocuri S4 (durata minimă pe client, afișată) și S5 (`ISalesOrderDemand`, rezervări), Integrări (EDI, viitor, prin `ICustomerCatalog.MatchAsync`), CRM (portofolii, prin evenimente), HR (angajatul agentului).

**Precizări aplicate altor documente la validarea V1 (27.11.2026):**

- **Nucleu §3.5**: nomenclatoarele noi `core.PaymentMeansCode` (UNCL 4461, seed) și `core.PaymentMethod` (comună tuturor modulelor), comanda `SavePaymentMethod`; coloana `Gln` pe `core.PartnerAddress`.
- **Finanțe F3**: plătitorul desemnat (`CustomerTerms.PayerPartnerId`) e folosit doar la propunerea alocării: o încasare de la plătitor, fără factură indicată, propune facturile deschise ale clienților pentru care e plătitor desemnat (FIFO), după cele proprii; alocarea pe factura altui partener cere confirmare și se contează ca până acum (`4111` client ↔ `4111` plătitor, compensare). `PartnerBalanceDto` rămâne pe client.
- **Stocuri S5**: `ISalesOrderDemand` e sursa cererii deschise pentru disponibilul proiectat, simetric cu `IPurchaseOrderSupply`; comenzile cu `CreditHold` apar în proiectat, marcate.
- **Achiziții A1**: modalitatea de plată propusă pe furnizor (`SupplierTerms.PaymentMethodId`) folosește același `core.PaymentMethod`.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Tranzacții folosite | Pe tip de document: tranzacțiile, numărul de documente pe lună, rolurile care le folosesc; tranzacțiile active nefolosite de peste 12 luni | Administrator, Șef vânzări |
| Condiții clienți | Clienții firmei cu grupa, lista de prețuri, termenul de plată, limita de credit, agentul, zona, starea (blocat, inactiv ANAF, doar cu plata pe loc) | Șef vânzări, Contabil clienți |
| Expunere și credit | Pe client: limita, soldul, avizele nefacturate, comenzile deschise, expunerea, procentul folosit, cea mai veche restanță; comenzile cu `CreditHold` | Șef vânzări, Contabil clienți, agentul (portofoliul lui) |
| Deblocări de credit | Deblocările acordate în perioadă, cu cine, de ce, pentru ce sumă, dacă au fost consumate și dacă clientul a plătit apoi la termenul promis | Șef vânzări, Contabil șef |
| Catalog incomplet | Clienții cu `RequiresCustomerItemCodes = 1` și variantele vândute fără cod la client; rândurile pe variante inactive; articolele listate care nu s-au mai vândut de 90 de zile | Key account, Operator facturare |
| Portofolii | Pe agent: clienții, zonele și grupele din portofoliu, clienții fără agent, înlocuirile în curs | Șef vânzări |

Rapoartele operaționale ale vânzărilor (comenzi nelivrate, avize nefacturate, marjă, vânzări pe agent) sunt în V7.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v1-configurare/](ecrane/v1-configurare/). Meniul Vânzări are sus Comenzi, Avize și Facturi, apoi grupurile Oferte, Livrare, Facturare, Retururi, Clienți, Prețuri și Rapoarte. Grupul Clienți conține Condiții clienți, Catalog clienți, Agenți și zone și Expunere și credit.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Configurare › Documente › Tranzacții | Tab-ul comun Configurare (Nucleu §11.1), filtrat pe Vânzări › Factură: tranzacția proprie `FV-LIV-AG` cu tab-urile General, Surse, Roluri, Contare, Istoric | `v1-01-tranzactii` |
| Asistentul de generare | Pasul 3 al asistentului pe un aviz din comanda CV 4421: comandat, livrat, rezervat, în așteptare, rămas, de livrat, loturile propuse FEFO cu termenul minim al clientului | `v1-02-asistent-aviz` |
| Fișa clientului › Vânzări | Condițiile Cafe Unirii SRL în firmă, panoul de expunere cu restanța de 38 de zile, comanda CV 4433 cu `CreditHold` și deblocarea unică acordată de Radu Stan | `v1-03-client-conditii-credit` |
| Catalog clienți | Catalogul Mega Retail SRL: codurile lor, UM-ul de comandă, multiplul, listarea, termenul minim din S4 (citire), sursa rândului; tab-ul Puncte de livrare | `v1-04-catalog-client` |
| Agenți și zone | Lista agenților pe echipe, fișa agentului Vlad Georgescu cu portofoliul (clienți, zone), înlocuitorul, gestiunea mașinii, plafonul de discount; arborele zonelor | `v1-05-agenti-zone` |
| Configurare › Vânzări | Setările firmei din 3.9, grupate: Livrare și facturare, Credit, Comenzi și rezervări, e-Factura | `v1-06-configurare-vanzari` |
| Drumul documentului | Panoul „Legături” al comenzii CV 4421: dispoziția, avizele, factura, cantitățile pe grupuri de consum (de livrat, de facturat) și restul în așteptare | `v1-07-drumul-comenzii` |
| Configurare › Nomenclatoare › Vânzări | Nomenclatoarele din 3.8: modalitățile de plată cu codurile UNCL 4461, grupele comerciale, canalele, modurile de livrare, motivele; marcajul „livrat” al valorilor din seed | `v1-08-nomenclatoare-vanzari` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V1-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V1-A1 | Fiecare comportament din 3.2 are o tranzacție de sistem după migrare; o tranzacție de sistem nu se poate șterge (`50010`), doar dezactiva | Test de integrare |
| V1-A2 | Copierea `FV-LIV` în `FV-LIV-AG` păstrează comportamentul și sursele; schimbarea comportamentului după prima factură e refuzată (`50029`) | Test de integrare (cu Nucleu) |
| V1-A3 | Pe CV 4421 (3.000 buc APA05), un aviz de 2.520 lasă rest de livrat 480; al doilea aviz de 600 e refuzat cu toleranța 0 (`50027`) | Test de integrare |
| V1-A4 | Două generări simultane de aviz din aceeași comandă, fiecare pe întregul rest: una reușește, cealaltă primește `50027`; restul final e 0, nu negativ | Test de concurență |
| V1-A5 | Livrarea și facturarea se consumă independent: după avizul de 2.520 și nicio factură, pe comandă rămân 480 de livrat și 2.520 de facturat; factura din aviz consumă `INVOICE` și pe comandă | Test de integrare |
| V1-A6 | `FV-LIV` creează factura și avizul intern în aceeași tranzacție SQL; un eșec la mișcarea de stoc nu lasă nici factura, nici avizul | Test de integrare (cu Stocuri) |
| V1-A7 | Stornarea unui aviz facturat parțial e limitată la partea nefacturată (`52016`); returul pe același aviz e limitat la cantitatea livrată minus returnată | Test de integrare |
| V1-A8 | Expunerea Cafe Unirii = 7.850,00 + 620,00 + 1.232,10 = 9.702,10 lei; restanța de 38 de zile cu pragul 30 confirmă CV 4433 cu `CreditHold`; deblocarea unică permite livrarea o singură dată și expiră după 48 de ore (`52007`) | Test de integrare (cu F3 simulat) |
| V1-A9 | Un aviz din comandă confirmată nu adaugă din nou valoarea comenzii în expunere; un aviz fără comandă o adaugă | Test unitar |
| V1-A10 | Potrivirea comenzii importate respectă ordinea din 4.4: un GTIN corect câștigă în fața unei denumiri identice cu alt articol; confirmarea manuală învață rândul de catalog | Test unitar și de integrare |
| V1-A11 | Același cod al clientului activ la două variante e refuzat (`52002`); după dezactivarea primului rând e acceptat | Test de integrare |
| V1-A12 | Agentul propus respectă ordinea din 3.7; înlocuitorul în perioadă apare ca `HandledByAgentId`, iar agentul documentului rămâne titularul | Test unitar |
| V1-A13 | Un agent cu scope 3 nu vede în listă clienții din afara portofoliului, nici în rapoartele programate; îi găsește doar după CUI exact, fără sold | Test de integrare |
| V1-A14 | Seed-ul nomenclatoarelor rulat de două ori nu dublează rânduri și nu suprascrie denumirile schimbate de client (`MIG-06`) | Test de integrare |
| V1-A15 | Fiecare comportament are un test de mapare: câmpurile copiate din sursă (variantă, UM, cantitate, preț, discount, gestiune, punct de livrare, cod client, agent) sunt cele din specificație | Test unitar per comportament |
| V1-A16 | Toate procedurile V1 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive; codul clientului `mr-102938` se potrivește cu `MR-102938` | Test de integrare (`TST-09`) |

Criteriile V1-A4, V1-A5, V1-A6 și V1-A8 sunt critice: legăturile decid cât mai e de livrat și de facturat, deci rezervările, contul `418` și facturarea, iar verificarea de credit decide dacă marfa pleacă.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **Tranzacția nouă `FV-MF` „Factură de vânzare a mijloacelor fixe”**: fără sursă, doar linii cu articolul tehnic nestocabil `MF-VANZARE` legate de o fișă (`AssetId` obligatoriu), venitul pe cheia `AssetSaleIncome` (`7583`), grupul de consum `DISPOSE`. Drept: `Sales.Invoice.AssetLine`. Folosită și pentru transferul în altă firmă a grupului (M5 §3.6).

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Agenții și ierarhia** (`SalesAgent`, `ManagerAgentId`, `AgentPortfolio`, `SalesZone`) sunt folosite de CRM pentru proprietarul lead-urilor și oportunităților, pentru vizibilitate (cel mult 3 niveluri de ierarhie) și pentru atribuire: clientul cu agent în portofoliu merge întâi la acel agent. CRM-ul nu are agenți proprii. [CR1 §3.3, §3.7](../12-crm/CR1-CONFIGURARE.md)
- **Ținta lunară a agentului**: tabelul nou `sales.SalesAgentTarget` (`SalesAgentId`, `PeriodMonth`, `TargetAmount` fără TVA, `CompanyId`), introdus de șeful de vânzări; folosit de prognoza CRM și de rapoartele V7. [CR3 §3.7](../12-crm/CR3-OPORTUNITATI-PIPELINE.md)
- **Canalul de vânzare**: valoarea livrată `Teren` în `sales.SalesChannel`, pusă pe comenzile luate la vizitele CRM.
- **`ReasonCode` tip 1** (motive de pierdere) e comun ofertelor și oportunităților; nomenclatorul de concurenți `crm.Competitor` e folosit și pe ofertă (câmpul `CompetitorId`).

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Ținta agentului** (`sales.SalesAgentTarget`, precizare din CRM) se poate scrie din bugetul de vânzări aprobat (`BUG-VNZ`); țintele introduse manual se suprascriu doar cu confirmare. Aceeași țintă e folosită de tablourile BI4 (`IKpiTargetQuery`). [BI5 §3.4](../13-raportare-bi/BI5-BUGETARE.md)

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Ordinea livrare–facturare | Toate trei drumurile: aviz → factură (`FV-AV`), factură cu livrare (`FV-LIV`, care generează un aviz intern netipărit, ca stocul, rezervările, retururile și `418` să citească aceeași sursă) și factură înaintea livrării (avans `FV-AVS`, regularizat pe factura finală) | 2 regula 4, 3.2, 3.9 | ✓ DA (27.11.2026) |
| 2 | Catalogul tranzacțiilor livrate pentru vânzări | Cele 25 din 3.2, inclusiv dispoziția de livrare (`DL-CV`), consignația (`AV-CSG`, `FV-CSG`), facturarea recurentă (`FV-REC`) și reducerea ulterioară (`FS-RED`) | 3.2 | ✓ DA (27.11.2026) |
| 3 | Comanda se consumă separat la livrare și la facturare | Da: grupurile `DELIVER` și `INVOICE`; factura din aviz consumă `INVOICE` și pe comandă, prin legătura avizului; „de livrat” și „de facturat” sunt calculate, nu stocate | 2 regula 3, 3.3 | ✓ DA (27.11.2026) |
| 4 | Oferta și proforma față de comandă | Informative (`LimitsConsumption = 0`): clientul poate comanda mai mult decât i s-a ofertat; se pot emite mai multe proforme pe aceeași comandă | 3.2, 3.3 | ✓ DA (27.11.2026) |
| 5 | Toleranța la livrare | Implicit 0 % pe firmă, cu suprascriere pe tranzacție (de exemplu marfa cântărită); interzisă la stornarea avizului, la retur și la storno | 3.2, 3.9 | ✓ DA (27.11.2026) |
| 6 | Clientul fiscal, punctele de livrare și plătitorul | Pe document un singur client fiscal; punctele de livrare sunt adrese ale lui (cu GLN); plătitorul desemnat e informativ, doar pentru propunerea de alocare în F3 (precizare la F3) | 2 regula 2, 3.4, 3.5, 9 | ✓ DA (27.11.2026) |
| 7 | Limita de credit | Expunere = sold F3 + avize nefacturate + comenzi confirmate nelivrate; verificare la confirmarea comenzii și la livrare; implicit blocare, inclusiv la restanțe peste 30 de zile; comanda se confirmă cu `CreditHold`; deblocare unică cu drept, motiv și termen de 48 de ore, în jurnalul de excepții | 3.4, 3.9, 4.3 | ✓ DA (27.11.2026) |
| 8 | Catalogul clientului | Pe variantă (SKU), valabil pe tot grupul sau pe o firmă; folosit la importul comenzilor (pașii din 4.4, cu învățare din confirmările manuale), la tipărire și în e-Factura (`BuyersItemIdentification`); listarea articolelor la client | 3.6, 4.4 | ✓ DA (27.11.2026) |
| 9 | Agenții și vizibilitatea | Entitate proprie `SalesAgent` legată de utilizator, cu echipe, zone și portofolii pe client, zonă sau grupă, cu înlocuitor; agentul se copiază pe document la creare; vizibilitate pe rol: tot, echipa sau doar portofoliul | 3.7, 2 regula 6 | ✓ DA (27.11.2026) |
| 10 | Nomenclatoarele vânzărilor | Tabele separate, cu seed `MERGE` care doar inserează: modalitățile de plată și codurile UNCL 4461 în Nucleu (comune), grupele comerciale, canalele, zonele, modurile de livrare și motivele în `sales` | 3.8, 7 | ✓ DA (27.11.2026) |
| 11 | Seriile documentelor | Aviz și factură pe serii fiscale separate (pe punct de lucru, dacă firma vrea); factura de stornare pe aceeași serie cu facturile, implicit; oferte, comenzi, proforme, dispoziții și planuri pe serii interne | 3.1 | ✓ DA (27.11.2026) |

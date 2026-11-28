# ERP · 02.03 Vânzări · V5 Facturare și e-Factura

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**. A cincea din cele 7 părți ale modulului 02.03 Vânzări: [V1 Configurare](V1-CONFIGURARE.md) ✓ · [V2 Prețuri și discounturi](V2-PRETURI-DISCOUNTURI.md) ✓ · [V3 Oferte, comenzi și rezervări](V3-OFERTE-COMENZI.md) ✓ · [V4 Livrare](V4-LIVRARE.md) ✓ · **V5 Facturare și e-Factura** · V6 Retururi și storno · V7 Rapoarte.

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`; V5 folosește `52500–52699`. Această parte acoperă **factura de vânzare** în toate formele ei:

- factura din avize, individuală sau prin **facturare în masă** după gruparea clientului;
- **factura cu livrare** (factura și avizul ei intern, de exemplu din mașina agentului);
- factura din comandă, pentru servicii;
- factura fără sursă;
- **factura de avans** și regularizarea ei pe factura finală;
- **facturarea recurentă** din planuri (abonamente, chirii);
- factura din raportul de consum al consignației.

Pentru fiecare factură, V5 descrie numerotarea fiscală, categoriile de TVA, scadențele, moneda, tipărirea și trimiterea pe email și **transmiterea în e-Factura** cu urmărirea stării până la validarea ANAF.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile facturii** sunt definite în V1 §3.2:

  | Tranzacție | Ce consumă |
  | --- | --- |
  | `FV-AV` | Avizele, grupul `INVOICE` |
  | `FV-CV` | Liniile nestocabile ale comenzii |
  | `FV-LIV` | `INVOICE` + `DELIVER` pe comandă, prin avizul intern |
  | `FV-AVS` | Avans, grupul `ADVANCE` |
  | `FV-REC` | Planul de facturare, grupul `BILLING` |
  | `FV-CSG` | Raportul de consum al consignației |
  | `FV-MAN` | Fără sursă, doar linii nestocabile, `52015` |

  Regularizarea avansului pe factura finală consumă grupul `SETTLE`. Legăturile sunt în `core.DocumentLink` (Nucleu §3.6), iar asistentul de generare e comun (Nucleu §3.6.4).
- **Factura nu reface prețurile.** Valorile vin din avize (V4 regula 6), din comandă (V2 regula 8) sau din plan. Doar factura fără sursă și factura cu livrare fără comandă le calculează cu motorul V2.
- **Factura e documentul care intră în partida clientului.** La validare se publică `FacturaEmisa`, cu conținutul minim cerut de F3 (fluxul 4.1). F3 creează partida și scadențele, iar C2 contează factura.
- Contarea avizului nefacturat e `418` = `707` + `4428` (V4 §3.9). Factura din aviz închide `418` pe `4111` și trece TVA-ul din `4428` în `4427`.
- **e-Factura se transmite prin Integrări 02.14.** V5 construiește datele facturii, iar 02.14 le transformă în UBL 2.1 CIUS-RO, le validează local, le încarcă în SPV, urmărește starea și întoarce rezultatul prin evenimente. V5 nu scrie XML și nu cunoaște API-ul ANAF.
- Avizele sunt documentele V4, comenzile sunt V3, iar proformele și avansul cerut sunt V3 §3.11.
- **Ambalajele SGR.** Pe liniile cu articole care au ambalaj în sistemul de garanție-returnare (Nucleu `Item.DepositItemId`), documentul adaugă automat linia de garanție de 0,50 lei pe ambalaj, fără TVA (categoria `O`), legată de linia articolului. Exemplele numerice din acest document nu afișează liniile de garanție, pentru simplitate; ele nu schimbă valorile cu TVA ale exemplelor. Returul ambalajelor goale e în V6 §3.7 (precizare din Vânzări V6, 27.11.2026).
- **Ce NU face V5:**
  - nu stornează facturi și nu emite reduceri ulterioare sau facturi de retur (V6);
  - nu înregistrează încasări (F1, F2) și nu face alocarea lor (F3);
  - nu face decontul de TVA (C4);
  - nu face jurnalul de vânzări (C4, din aceleași evenimente).

## 1. Scop și perimetru

| Zonă | Ce face V5 | Ce primește de la alții |
| --- | --- | --- |
| Factura din avize | Una sau mai multe avize ale aceluiași client și plătitor într-o factură; regula lunii; valorile copiate; regularizarea avansurilor | Avizele validate (V4), condițiile clientului (V1) |
| Facturare în masă | Selecția avizelor nefacturate, gruparea pe client (`InvoiceGrouping`), previzualizarea, generarea și validarea, trimiterea pe email și în e-Factura, jobul de seară | Lista „de facturat” (V4 `IDeliveryNoteQueries`) |
| Factura cu livrare | Factura care scoate marfa din stoc prin avizul ei intern, de pe calculator sau din mașina agentului, cu încasarea pe loc | Mișcarea de stoc (S2 prin V4 §3.6), încasarea (F1) |
| Factura de avans | Emisă la încasarea avansului (din proformă sau direct), pe cote de TVA, și regularizată pe factura finală | Încasarea (F2 `IncasareInregistrata`), proforma (V3) |
| Facturare recurentă | Planurile de facturare (abonamente, chirii, servicii periodice), indexarea, generarea automată lunară | — |
| Consignație | Factura din raportul de consum (V4 §3.10), cu ieșirea din gestiunea de la client | Raportul validat (V4) |
| Elemente fiscale | Seria și numărul, data, categoriile de TVA și mențiunile, TVA la încasare, moneda și cursul, scadențele, termenul legal de plată | Cotele și categoriile (Nucleu), cursul BNR (Integrări) |
| e-Factura | Datele facturii pentru UBL, transmiterea, starea, răspunsul ANAF, corecția și retrimiterea după respingere, termenul de transmitere, persoanele fizice | Transmiterea și răspunsurile (Integrări 02.14) |
| Trimitere | PDF-ul facturii pe email, cu XML-ul e-Factura atașat, după șablonul clientului | Outbox de email (Nucleu) |

## 2. Model de domeniu

```
 SalesDeliveryNote × N (validat, nefacturat) ──FV-AV──┐
 SalesOrder (linii nestocabile) ───────────────FV-CV──┤
 SalesOrder (opțional) / manual ──────────────FV-LIV──┼─► SalesInvoice ─► SalesInvoiceLine × N (din aviz, din comandă, din plan sau manual)
 BillingPlan (activ, data următoare) ─────────FV-REC──┤        │           └─ linii de regularizare a avansului (negative, pe cote)
 ConsignmentReport (validat) ─────────────────FV-CSG──┤        ├─► SalesInvoiceVatSummary (o linie pe categorie și cotă de TVA)
 manual (doar linii nestocabile) ─────────────FV-MAN──┘        ├─► SalesInvoiceDueDate × N (scadențe)
                                                               ├─► EInvoiceSubmission × N (încărcări în SPV: index, stare, mesaje)
 IncasareInregistrata (F2) + SalesProforma / SalesOrder ─FV-AVS─► SalesInvoice (IsAdvance) ──SETTLE──► factura finală
 FV-LIV ─► SalesDeliveryNote (IsInvoiceDelivery = 1, V4 §3.6) ─► IStockLedger.PostAsync
```

Reguli de domeniu:

1. **Factura e pentru un singur client și un singur plătitor, într-o singură monedă.** Avizele unei facturi trebuie să aibă același client, același plătitor (V1 `PayerPartnerId`) și aceeași monedă a comenzii (`52500`). Punctele de livrare pot fi diferite; fiecare linie își păstrează punctul și GLN-ul. Același lucru e valabil pentru comenzile clientului.
2. **Avizele unei luni se facturează împreună, nu peste lună.** O factură nu adună avize din luni calendaristice diferite (`52501`). Așa, data exigibilității TVA (data livrării) rămâne în aceeași lună pentru toată factura. Facturarea în masă desparte automat gruparea „pe lună” sau „pe săptămână” la granița lunii (decizia 2).
3. **Valorile nu se schimbă la facturare.** Liniile facturii din aviz copiază cantitatea, prețurile, discounturile și cota de TVA ale liniei de aviz. O corecție de preț descoperită la facturare se face pe aviz, prin stornare (V4 regula 5). Pe o factură deja validată, se face prin storno sau reducere (V6).
4. **TVA-ul se calculează pe factură, pe fiecare cotă.** Factura are un rezumat al TVA-ului pe categorie și cotă (`SalesInvoiceVatSummary`): baza = suma liniilor, iar TVA-ul = baza × cota, rotunjit la 2 zecimale (EN 16931, BR-CO-17). O diferență de câțiva bani față de suma TVA-urilor avizelor se închide în nota facturii (3.9).
5. **Numărul fiscal se dă la validare, în ordine.** Seria facturilor e continuă, fără goluri (Nucleu fluxul 4.2). Data unei facturi nu poate fi anterioară datei ultimei facturi validate din aceeași serie (`52503`). Ciorna nu are număr și se poate șterge. Factura validată nu se modifică; se stornează (V6).
6. **Avansul se facturează la încasare și se regularizează la livrare.** Factura de avans se emite când banii intră, pe cotele comenzii, proporțional. Factura finală scade avansul prin linii negative pe aceleași cote, până la cel mult valoarea ei sau restul avansului (`SETTLE`). Avansul rămas nefolosit la închiderea comenzii se stornează (V6) sau se rambursează (F2) (decizia 5).
7. **Factura cu livrare e un singur document pentru utilizator.** `FV-LIV` creează factura și avizul ei intern în aceeași tranzacție SQL (V4 §3.6). Validarea face, în ordine, verificările de comandă, credit și stoc, ieșirea din stoc, numărul facturii și evenimentele. Dacă stocul refuză, nu se consumă număr fiscal (decizia 3).
8. **Factura validată pleacă în e-Factura fără intervenția operatorului.** La validare, factura intră în coada e-Factura (`EInvoiceStatus` = De trimis). Jobul o trimite imediat sau la ora setată și urmărește răspunsul ANAF. Respingerea e tratată în aplicație: datele care se pot corecta fără a schimba valorile (identificatori, adrese, coduri) se corectează și factura se retrimite cu același număr. Altfel, factura se stornează și se emite alta (decizia 7).
9. **Termenul de plată respectă contractul și legea.** Scadența vine din condițiile clientului (V1), poate fi împărțită în tranșe (`PaymentScheduleTemplate`) și primește avertizare peste `MaxPaymentTermDays` (Legea 72/2013). Pentru autoritățile publice, plafonul e 30 de zile, cu blocare (`52512`) dacă un contract nu prevede altfel.

## 3. Entități, stări și setări

Convențiile comune tabelelor `sales` sunt cele din V1 §3.

| Serie | Pentru ce |
| --- | --- |
| `FV EXD` | Facturi; serie fiscală continuă, eventual separată pe punct de lucru sau pe tranzacție |
| `FA EXD` | Facturile de avans, opțional; altfel aceeași serie |
| `PR` | Planurile de facturare recurentă |

### 3.1 Factura (`SalesInvoice`)

| Câmp (antet) | Reguli |
| --- | --- |
| `NumberSeriesId`, `Number`, `DocDate` | Seria fiscală; numărul la validare. `DocDate` e data emiterii: implicit ziua validării, într-o perioadă deschisă (Nucleu), nu înaintea ultimei facturi din serie (`52503`) |
| `TaxPointDate` | Data exigibilității TVA, dacă diferă de data facturii (UBL `TaxPointDate`, BT-7). Valoarea depinde de tipul facturii (vezi tabelul de sub antet) |
| `TransactionTypeId`, `InvoiceKind` | `InvoiceKind`: 1 standard · 2 avans · 3 recurentă · 4 consignație · 5 cu livrare. Storno și reducere se adaugă în V6 |
| `PartnerId`, `PayerPartnerId`, `BillingAddressId` | Clientul (cumpărătorul din e-Factura), plătitorul (informativ pentru F3) și adresa de facturare (sediul, Nucleu) |
| `CurrencyCode`, `ExchangeRate`, `ExchangeRateDate` | Moneda clientului sau a comenzii. Pentru valută: cursul BNR din data facturii (ultimul publicat), din Integrări. Cursul contractual (V3) se folosește doar dacă tranzacția îl permite, cu TVA-ul tot la cursul BNR (Codul fiscal art. 290) |
| `VatOnCollection` | Firma aplică TVA la încasare la data facturii (Nucleu `Company.VatOnCollection`, istoric): mențiunea „TVA la încasare” și contarea pe `4428` (C2) |
| `PaymentTermDays`, `PaymentMethodId`, `DueDate` | Din condițiile clientului (V1); scadențele în `SalesInvoiceDueDate`; `PaymentMeansCode` din modalitate (e-Factura BT-81) |
| `CustomerOrderRef`, `ContractRef`, `BuyerReference` | Numărul comenzii clientului (BT-13), numărul contractului (BT-12), referința cumpărătorului (BT-10; obligatorie pentru unele autorități) |
| `NetAmount`, `VatAmount`, `TotalAmount`, `AdvanceSettledAmount`, `AmountDue` | Valorile în moneda facturii; `AmountDue` = total − avansuri regularizate (BT-115); echivalentele în lei pentru valută |
| `CostAmount`, `MarginAmount` | Costul liniilor de marfă (din avize sau din avizul intern), pentru marja facturată |
| `EInvoiceStatus`, `EInvoiceUploadIndex`, `EInvoiceDownloadId`, `EInvoiceSentAt`, `EInvoiceDeadline` | 3.7 |
| `Status` | 1 Ciornă · 3 Validată · 4 Încasată integral (din F3, informativ) · 8 Stornată (V6) · 9 Anulată (doar ciornă) |
| `PrintTemplateId`, `EmailedAt`, `EmailTo` | Șablonul PDF (V1 `DocumentPdfTemplates`) și trimiterea pe email |
| `Notes`, `LegalMentions` | Mențiunile cerute de categoriile de TVA (3.4), adăugate automat, plus textul liber |

Valoarea `TaxPointDate` pe tipuri de factură:

| Tip | `TaxPointDate` |
| --- | --- |
| Din avize | Data ultimei livrări incluse (toate din aceeași lună, regula 2) |
| De avans | Data încasării |
| Celelalte | Gol (exigibilitatea e la data facturii) |

**Linie** (`SalesInvoiceLine`):

- **Surse:** `SourceKind` (1 aviz · 2 comandă · 3 plan · 4 raport de consum · 5 manual · 6 regularizare avans), `DeliveryNoteLineId`, `SalesOrderLineId`, `BillingPlanLineId`, `ConsignmentReportLineId`, `AdvanceInvoiceId`.
- **Articol și cantitate:** `ItemVariantId` sau `ServiceText`, `UomId` cu codul UN/ECE, `Qty`.
- **Prețuri și discounturi:** `ListPrice`, `UnitPrice`, discounturile (`DocumentLineDiscount`).
- **TVA:**
  - `VatCategoryCode` (`S` standard · `AE` taxare inversă · `K` livrare intracomunitară · `G` export · `E` scutit · `O` în afara sferei · `Z` cota zero);
  - `VatRate`, `VatExemptionReasonCode` (BT-121);
  - `NetAmount`, `VatAmount`.
- **Coduri e-Factura:** `CustomerItemCode` (BT-156), `CnCode` (BT-158, pentru taxarea inversă și unele bunuri), `CpvCode` (pentru autorități, din nomenclatorul CPV, 3.8).
- **Livrare:** `DeliveryPointId`, `DeliveryDate`.
- **Cost și analitică:** `UnitCostRon`, `CostCenterId`, `ProjectId`.

**Rezumatul TVA** (`SalesInvoiceVatSummary`): `VatCategoryCode`, `VatRate`, `TaxableAmount`, `VatAmount`, `VatAmountRon`, `ExemptionReasonCode`, `ExemptionReasonText`. Este calculat la salvare și la validare și e sursa totalurilor, a e-Facturii și a notei contabile.

**Scadențe** (`SalesInvoiceDueDate`): `DueDate`, `Amount`, `Seq`. Suma scadențelor e egală cu `AmountDue`.

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează antetul, se scot sau se adaugă surse, se schimbă data, termenul și textele; liniile din surse nu își schimbă prețul | → Validată; → Anulată (ștergere) |
| 3 Validată | Se tipărește, se trimite pe email; e-Factura merge separat (3.7); corecția datelor nefiscale doar după respingere ANAF | → Stornată (V6); → Încasată (F3) |
| 4 Încasată | Informativ, din alocările F3 | → Validată, la anularea unei alocări |
| 8 Stornată | Finală | — |

### 3.2 Validarea facturii

Într-o singură tranzacție SQL, în ordine:

1. **Sursele:** avizele sunt validate și nefacturate (`INVOICE` disponibil), au același client, plătitor și monedă (`52500`) și sunt din aceeași lună (`52501`).
   - Factura cu livrare face în plus verificările avizului (V4 §3.3, pașii 1–3), inclusiv creditul dacă punctul „livrare” e bifat.
   - Factura recurentă verifică planul activ și perioada încă nefacturată (`52540`).
2. **Elementele fiscale:**
   - fiecare linie are categorie și cotă de TVA valide la `TaxPointDate` sau `DocDate`;
   - categoriile `AE`, `K`, `G`, `E` și `O` au motivul și mențiunea (`52506`);
   - clientul are datele cerute de categoria lui: cod de TVA valid VIES pentru `K` (`52507`, prin Integrări, cu rezultatul păstrat), cod fiscal sau CNP pentru e-Factura (`52508`);
   - scadența respectă plafonul autorităților (`52512`).
3. **Avansurile:** liniile de regularizare nu depășesc restul avansului facturat al comenzii sau al clientului pe cota respectivă și nici valoarea pozitivă a facturii pe acea cotă (`52520`).
4. **Numărul fiscal** (Nucleu fluxul 4.2) și verificarea datei față de ultima factură din serie (`52503`).
5. **Factura cu livrare și factura de consignație:**
   - Factura cu livrare validează avizul intern: `IStockLedger.PostAsync` cu `ISSUE_SALE` și consumul rezervării (V4 §3.6).
   - Factura de consignație face ieșirea din gestiunea de la client (`ISSUE_SALE` din gestiunea `La terți`).
6. **Legăturile** (`INVOICE`, `SETTLE`, `BILLING`, `DELIVER` la factura cu livrare), istoricul și starea Validată. Avizele facturate integral trec în Închis (V4).
   - **Faptul de vânzare:** `sales.usp_Fact_Write` scrie câte un rând pe linie, cu vânzarea netă și costul ieșirii (V7 §3.2) (precizare din Vânzări V7, 27.11.2026).
7. **Evenimentele** în outbox:
   - `FacturaEmisa` v1, cu blocul `Accounting` și conținutul minim F3;
   - `LivrareEfectuata`, la factura cu livrare;
   - intrarea în coada e-Factura (`EInvoiceStatus` = 1).

Dacă un pas eșuează, nu se salvează nimic și nu se consumă număr. Tipărirea, emailul și trimiterea în e-Factura se fac după commit, din coadă.

### 3.3 Factura de avans și regularizarea ei

- **Când se emite.** La încasarea avansului (F2 `IncasareInregistrata`), dacă încasarea e legată de o proformă de avans sau de o comandă cu avans cerut (V3 §3.11), apare în „De rezolvat › Facturi de avans de emis”. Termenul legal este data încasării. Factura se poate emite până pe 15 ale lunii următoare (Codul fiscal art. 319 alin. (16)), cu `TaxPointDate` = data încasării. Se poate emite și manual, din comandă sau fără comandă (`FV-AVS`).
- **Cum arată.** O linie pe fiecare cotă de TVA, cu text „Avans conform comenzii CV …” și cu baza repartizată proporțional pe cotele comenzii, ca la proformă (V3 §3.11). Factura de avans nu mișcă stocul și nu are cost.
- **Regularizarea.** Pe factura finală din avize sau cu livrare, asistentul propune avansurile facturate și neregularizate ale comenzii, apoi pe cele ale clientului fără comandă (sursă auxiliară, `SETTLE`, Nucleu §3.6). Pentru fiecare cotă, apare o linie negativă egală cu baza avansului pe acea cotă, plafonată la baza pozitivă a facturii pe acea cotă. Avansul se poate regulariza în mai multe facturi. `AmountDue` scade cu avansul regularizat. F3 compensează automat partida avansului cu factura (fluxul de avans din F3).
- **Ce nu se face.** Un avans în lei nu se regularizează pe o factură în valută și invers (`52521`). Avansul încasat de la plătitor, nu de la client, se regularizează doar pe facturile acelui client (V1 regula 2).

### 3.4 Categoriile de TVA și mențiunile

Categoria se alege automat, cu prima regulă care se aplică:

1. linia are o categorie forțată de tranzacție (de exemplu `FV-EXP` cu `G`);
2. clientul e din alt stat UE, are cod de TVA valid VIES și marfa pleacă din România: `K`, mențiunea „Scutit cu drept de deducere, art. 294 alin. (2) lit. a) Cod fiscal” (Intrastat expedieri, Stocuri S7);
3. clientul e din afara UE și livrarea are dovadă de export: `G`, art. 294 alin. (1);
4. articolul și clientul (persoană impozabilă înregistrată în scopuri de TVA) intră în taxarea inversă internă (art. 331: cereale, deșeuri, energie, telefoane și circuite peste prag și altele, după `Item.DomesticReverseChargeCode`): `AE`, „Taxare inversă”, cu codul NC pe linie;
5. altfel `S` cu cota articolului la data exigibilității (Nucleu, istoricul cotelor).

Mențiunile apar automat pe PDF și în UBL (`ExemptionReason`). Factura nu se validează fără ele (`52506`).

### 3.5 Facturarea în masă

Ecranul `v5-03`, rulat manual sau de jobul de seară (`MassInvoicingSchedule`):

1. **Selecția.** Avizele validate și nefacturate până la o dată, filtrate pe gestiune, agent, zonă, client. Se exclud avizele de consignație și cele fără confirmare dacă `PodRequiredForInvoice` = 1 (V4, `52367`).
2. **Gruparea**, pe client, plătitor, monedă și lună, după `InvoiceGrouping` (V1 §3.4):

   | Valoare | Grupare |
   | --- | --- |
   | 1 | Pe aviz |
   | 2 | Pe comandă |
   | 3 | Pe zi |
   | 4 | Pe săptămână |
   | 5 | Pe lună, cu decada configurabilă |

   Un client cu „pe săptămână” primește factura după încheierea săptămânii, iar unul cu „pe decadă” după încheierea decadei. Avizele mai vechi decât regula se facturează oricum, ca să nu treacă de termenul legal.
3. **Previzualizarea.** Lista facturilor propuse, cu valorile, avansurile de regularizat, problemele (client fără cod fiscal, aviz într-o perioadă închisă) și cine nu se facturează și de ce.
4. **Generarea.** Facturile se creează și se validează una câte una, fiecare în tranzacția ei. Una care eșuează nu oprește restul și apare în raportul rulării, cu eroarea. Numerele se dau în ordinea validării.
5. **După validare:**
   - PDF-ul pleacă pe email (`InvoiceEmail` al clientului), dacă `EmailOnValidation` = 1;
   - factura intră în coada e-Factura.

O rulare are număr, utilizator, filtre, rezultatul pe factură și durata (`MassInvoicingRun`).

### 3.6 Facturarea recurentă (`BillingPlan`, `BillingPlanLine`)

| Câmp | Reguli |
| --- | --- |
| `Number`, `PartnerId`, `ContractRef`, `CurrencyCode` | Seria `PR`; clientul și contractul (BT-12) |
| `Frequency`, `IntervalCount`, `BillingDay`, `BillingMode` | Lunar, trimestrial, anual, la fiecare N luni. `BillingDay`: ziua emiterii (1–28 sau ultima). `BillingMode`: 1 în avans, pentru perioada care începe · 2 la sfârșit, pentru perioada încheiată |
| `StartDate`, `EndDate`, `NextBillingDate`, `LastBilledPeriodEnd` | Perioada planului; data următoare se calculează după fiecare factură |
| `Indexation` | 0 fără · 1 procent fix anual · 2 după indicele prețurilor de consum (INS, introdus anual) · 3 după cursul unei valute. Se aplică la data aniversară, cu istoric pe linie |
| `AutoValidate` | 1 validează și trimite factura direct · 0 o lasă în ciornă pentru verificare |
| `Status` | 1 Ciornă · 3 Activ · 5 Suspendat (fără facturi, cu motiv) · 4 Încheiat |
| **Linii** | Articol nestocabil (serviciu, chirie, abonament), cantitate, preț, cotă de TVA, `ValidFrom`, `ValidTo`, centru de cost, proiect; textul liniei acceptă `{perioada}` (de exemplu „Chirie dozatoare {perioada}”) |

Jobul zilnic `BillingPlansDue` (06:00) creează facturile `FV-REC` pentru planurile cu `NextBillingDate` ≤ azi. Rularea e idempotentă: legătura `BILLING` pe perioadă nu permite două facturi pentru aceeași perioadă (`52540`). Suspendarea sau închiderea planului nu afectează facturile emise.

### 3.7 e-Factura

**Ce se trimite.** Toate facturile, inclusiv cele de avans și cele cu livrare, către:

- persoane juridice și alte entități înregistrate în România (B2B, B2G);
- persoane fizice (B2C).

Pentru persoanele fizice, identificatorul e CNP-ul, dacă e cunoscut și clientul a fost de acord, altfel 13 cifre de zero, după regula în vigoare.

Pentru clienții nerezidenți, trimiterea e o setare (`EInvoiceForeignPartners`, implicit 0). Regulile exacte se citesc dintr-un tabel de configurare cu dată, ca la e-Transport (`EInvoiceScopeRule`), și se verifică la punerea în funcțiune.

**Stările** (`EInvoiceStatus` pe factură, `EInvoiceSubmission` pe fiecare încărcare):

| Stare | Ce înseamnă |
| --- | --- |
| 0 Nu se trimite | Clienți în afara regulii |
| 1 De trimis | În coadă |
| 2 Trimisă | Încărcată, are index de încărcare, se așteaptă răspunsul |
| 3 Validată | ANAF a acceptat factura; se păstrează XML-ul semnat de ANAF și id-ul de descărcare |
| 4 Respinsă | Erori de validare; mesajele ANAF sunt pe factură |
| 5 Eroare de transmitere | SPV indisponibil sau token expirat; se reîncearcă automat |
| 6 Întârziată | Termenul de transmitere a trecut fără stare Validată |

**Termenul.** `EInvoiceDeadline` = data facturii + `EInvoiceSendDeadlineDays` (V1, 5 zile lucrătoare, după calendarul Nucleu). O factură încă netrimisă sau respinsă cu o zi lucrătoare înainte de termen apare în „De rezolvat” la operatorul de facturare și la contabilul șef. După termen, alerta e roșie.

**Respingerea.** Mesajele ANAF se arată pe factură, cu regula încălcată tradusă. Corecția depinde de ce e greșit:

- **Date nefiscale:** CUI-ul, adresa, județul, codul poștal, GLN-ul, codul articolului la client, UM-ul UN/ECE, referința cumpărătorului. Le corectează un utilizator cu `Sales.EInvoice.Correct`, cu audit, iar factura se retrimite cu același număr și aceeași dată. O factură respinsă la validare nu se consideră primită în sistem.
- **Valori, cantități, cote:** factura se stornează (V6) și se emite alta. Ecranul propune direct stornarea.

**Fluxul tehnic.**

1. La commit, Vânzările scriu cererea în outbox.
2. Handlerul apelează `IEInvoiceGateway.SubmitAsync(EInvoiceDocumentDto)`. Portul e implementat de 02.14, care construiește UBL-ul CIUS-RO, rulează validarea locală (schematron) și, dacă trece, încarcă documentul.
3. Răspunsurile vin prin evenimentele `EFacturaTrimisa` (indexul de încărcare), `EFacturaValidata` (id-ul de descărcare, XML-ul semnat) și `EFacturaRespinsa` (mesajele).
4. Cererile sunt idempotente pe `(InvoiceId, Attempt)`.

**Fișierele.** XML-ul trimis, XML-ul semnat de ANAF și PDF-ul vizual se păstrează ca fișiere atașate facturii (Nucleu), cu retenția documentelor contabile. Emailul către client poate atașa XML-ul semnat.

### 3.8 Nomenclatoare noi

| Tabel | Conținut | Seed |
| --- | --- | --- |
| `core.VatExemptionReason` (Nucleu) | Codurile VATEX și motivele de scutire sau neimpozitare, cu textul mențiunii în română și articolul din Codul fiscal | Da, din lista EN 16931 / CIUS-RO folosită în România |
| `core.UneceUnit` (Nucleu) | Codurile UN/ECE Rec. 20 ale unităților de măsură (`H87` bucată, `KGM` kilogram, `LTR` litru, `XBX` cutie…), legate de UM-urile din Nucleu | Da, subsetul uzual (~40) |
| `core.CpvCode` (Nucleu) | Nomenclatorul CPV, pentru facturile către autorități și licitații | Da, din Regulamentul (CE) 213/2008, încărcat ca fișier |
| `sales.PaymentScheduleTemplate` | Scadențe în tranșe (de exemplu „50 % la 30 de zile, 50 % la 60”) | 3 șabloane uzuale |

### 3.9 Contarea (C2, din `FacturaEmisa`)

| Factură | Notă |
| --- | --- |
| Din avize | `4111` = `418` (totalul avizelor) și `4428` = `4427` (TVA-ul avizelor). Diferența de rotunjire dintre TVA-ul facturii și suma TVA-urilor avizelor (bani) se trece `4111` = `4427` sau invers |
| Cu livrare, din comandă, recurentă, fără sursă | `4111` = `70x` (după articol, cheia `Revenue`) + `4427`; la factura cu livrare, ieșirea `607` = `371` vine din mișcarea avizului intern |
| De avans | `4111` = `419` + `4427` |
| Regularizarea avansului pe factura finală | `419` = `4111` cu baza și `4427` negativ pe linia de regularizare (nota finală pe `4111` e netă) |
| De consignație | `4111` = `707` + `4427` și `607` = `357` |
| Cu TVA la încasare | `4428` în loc de `4427` (C2) |

Toate se fac în jurnalul `VZ`, cu analitica liniilor (centru de cost, proiect, gestiune, agent).

### 3.10 Setările V5 (completare la `SalesSettings`, V1 §3.9)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `MassInvoicingSchedule` | zilnic 20:00, doar manual | Jobul de facturare în masă: oră, zile, filtre implicite; „doar manual” = fără job |
| `MassInvoicingAutoValidate` | 1 | Facturile din rularea automată se validează sau rămân ciorne |
| `EmailOnValidation` | 1 | PDF-ul pleacă pe email la validare (pe client se poate opri) |
| `EInvoiceSendMode` | 1 | 1 imediat după validare · 2 la ore fixe (de exemplu 12:00 și 20:00) |
| `EInvoiceForeignPartners` | 0 | Trimiterea facturilor către clienți nerezidenți |
| `EInvoiceAttachPdf` | 0 | PDF-ul facturii se atașează și în UBL (`AdditionalDocumentReference`) |
| `AdvanceInvoiceSeries` | aceeași | Seria facturilor de avans |
| `UninvoicedVatAtMonthEnd` | 1 | 1 TVA-ul avizelor nefacturate la sfârșitul lunii trece în decontul lunii livrării (C3 face nota `4428` = `4427` la închiderea lunii și o inversează la facturare) · 2 TVA-ul intră în decontul lunii facturii (decizia 2; de confirmat cu consultantul fiscal) |
| `PublicAuthorityMaxTermDays` | 30 | Plafonul termenului de plată pentru autoritățile publice (Legea 72/2013) |
| `CashPaymentLimitCheck` | 1 | Facturile cu plată numerar peste plafonul legal pe zi și client (Legea 70/2015, 5.000 lei între persoane juridice) primesc blocare (`52530`) |

## 4. Fluxuri principale

Datele de exemplu continuă pe cele din V1–V4. Operatoarea de facturare e Elena Dima, iar contabilul șef Ion Popescu.

Calendarul exemplului:

| Data | Ce e |
| --- | --- |
| Luni 30.11 | Zi liberă legală |
| Marți 01.12 | Zi liberă legală |
| Miercuri 02.12 | Prima zi lucrătoare din decembrie |

Ultima factură din noiembrie are numărul `FV EXD 10243`.

### 4.1 Factura recurentă din 01.12 (`FV EXD 10244`)

Planul `PR 0007` al Biroului Notarial Pop are următorii parametri:

| Parametru | Valoare |
| --- | --- |
| Linia | Chirie 6 dozatoare de apă × 25,00 lei, TVA 21 % |
| Frecvența | Lunar, ziua 1, în avans |
| Validare | Automată |

Jobul de marți 01.12, ora 06:00, creează și validează factura „Chirie dozatoare 01.12.2026 – 31.12.2026”: 150,00 lei + TVA 31,50 lei = 181,50 lei, cu scadența 15.12. Factura pleacă pe email și intră în coada e-Factura. Termenul de transmitere e 08.12: 5 zile lucrătoare, fără 01.12.

### 4.2 Facturarea în masă de miercuri 02.12

La 08:30, Elena rulează facturarea în masă pentru avizele livrate până la 30.11 din toate gestiunile. Previzualizarea arată 23 de avize, grupate în 14 facturi, cu totalul de 48.270,91 lei. Totalul a scăzut față de 27.11 cu stornarea de 45,29 lei de la Hotel Central (V4 §4.3).

| Factură | Client | Avize | Grupare | Total (lei) |
| --- | --- | --- | --- | --- |
| `FV EXD 10245` | Distribuția Vest SRL | 2 (decada 11–20) | pe decadă | 9.412,30 |
| `FV EXD 10246` | Distribuția Vest SRL | 2 (decada 21–30) | pe decadă | 14.944,21 |
| `FV EXD 10247` | Hotel Central SRL | 3 (20.11 – 27.11) | pe săptămână, lunile nu se amestecă | 8.699,23 |
| `FV EXD 10248` | Cafe Unirii SRL | 2 | pe săptămână | 2.104,25 |
| `FV EXD 10249` | Pensiunea Arieșul SRL | 1 (`EXD 30598`) | pe aviz | 2.951,47 |
| `FV EXD 10250` | Restaurant Someș SRL | 1 | pe aviz | 1.887,60 |
| `FV EXD 10251` | Bistro 9 SRL | 1 | pe aviz | 412,30 |
| `FV EXD 10252` – `10258` | alți 7 clienți | 11 | — | 7.859,55 |

Bistro 9 are `CreditHold` pe comenzi, dar marfa e deja livrată: factura se emite. Blocajul de credit oprește doar comenzi și livrări noi.

**Factura `FV EXD 10247`** (Hotel Central, scadență 17.12, termen 15 zile):

| Aviz | Data | Fără TVA | TVA 11 % | TVA 21 % | Total |
| --- | --- | --- | --- | --- | --- |
| `EXD 30602` | 20.11 | 2.404,72 | 264,52 | — | 2.669,24 |
| `EXD 30616` | 24.11 | 2.640,00 (2.100,00 la 11 % + 540,00 la 21 %) | 231,00 | 113,40 | 2.984,40 |
| `EXD 30625` | 27.11 | 2.674,80, după stornarea refuzului | 210,01 | 160,78 | 3.045,59 |
| **Factura** | 02.12 | **7.719,52** | **705,53** (bază 6.413,92) | **274,18** (bază 1.305,60) | **8.699,23** |

Detalii despre factura `FV EXD 10247`:

- **Exigibilitatea TVA:** `TaxPointDate` = 27.11.2026, data ultimei livrări.
- **Decontul:** cu `UninvoicedVatAtMonthEnd` = 1, TVA-ul de 979,71 lei e deja în decontul lunii noiembrie, prin nota de închidere a lunii.
- **Contarea:** `4111` = `418` 8.699,23 și `4428` = `4427` 979,71 lei. Rezumatul TVA pe cote egalează suma TVA-urilor avizelor, deci nu există diferență de rotunjire.

**Rezultatul rulării:**

- **Validare:** 14 facturi validate în 41 de secunde, 13 trimise pe email.
- **Fără email:** Bistro 9 nu are `InvoiceEmail`, iar contactul de facturare lipsește.
- **e-Factura:** toate cele 14 facturi intră în coadă.

### 4.3 e-Factura: o respingere corectată

1. Până la 09:15, ANAF validează 13 din cele 14 facturi.
2. `FV EXD 10256` (Asociația Turistică Vlădeasa) e respinsă: „Adresa cumpărătorului: codul județului lipsește sau e invalid (regulă CIUS-RO)”. Partenerul fusese creat manual, fără județ.
3. Elena are `Sales.EInvoice.Correct`. Corectează adresa partenerului în Nucleu, apoi alege „Retrimite” pe factură.
4. Valorile nu se schimbă: factura pleacă din nou cu același număr și aceeași dată și e validată la 09:40.
5. Încercarea respinsă rămâne în istoric, cu mesajele ANAF.

### 4.4 Avansul Pensiunii Arieșul (`FV EXD 10259`, regularizat pe `FV EXD 10262`)

1. Pe 02.12, încasarea de 3.123,21 lei pe proforma `PF 0092` ridică `AdvanceHold` pe `CV 4449` (V3 §4.6). Factura de avans apare în „De rezolvat”.
2. Elena emite `FV EXD 10259` (`FV-AVS`), cu `TaxPointDate` = 02.12. Factura are două linii:
   - „Avans conform comenzii CV 4449”, 2.055,00 lei la 11 % (TVA 226,05);
   - același text, 696,00 lei la 21 % (TVA 146,16).

   Totalul e 3.123,21 lei, iar `AmountDue` e 0, pentru că factura e deja încasată. F3 alocă încasarea pe factura de avans.
3. Pe 03.12, `CV 4449` se livrează integral cu avizul `EXD 30632` (6.246,42 lei). Clientul are gruparea „pe aviz”, deci Elena emite factura `FV EXD 10262`. Asistentul propune avansul `FV EXD 10259`.

| Linie | Fără TVA | TVA | Total |
| --- | --- | --- | --- |
| Din aviz: APA05, APA15 (+ bonus), CEAI05 | 5.502,00 | 744,42 | 6.246,42 |
| Regularizare avans `FV EXD 10259`, 11 % | −2.055,00 | −226,05 | −2.281,05 |
| Regularizare avans `FV EXD 10259`, 21 % | −696,00 | −146,16 | −842,16 |
| **De plată** | 2.751,00 | 372,21 | **3.123,21** |

### 4.5 Factura cu livrare din mașina agentului (`FV EXD 10260`)

Pe 02.12 la 11:40, Vlad Georgescu vinde la Pizzeria Napoli SRL, client cu `CashOnly`, din gestiunea mașinii `AUTO-CJ01`, cu tranzacția `FV-LIV-AG` (V1 §4.1).

| Linie | Calcul | Fără TVA |
| --- | --- | --- |
| APA05 | 120 × 1,85 | 222,00 |
| APA15 | 60 × 3,15 | 189,00 |
| CEAI05 | 48 × 3,22 | 154,56 |

Totalul: 565,56 lei, TVA 45,21 (11 % pe 411,00) + 32,46 (21 % pe 154,56) = 77,67 lei, total 643,23 lei.

La validare, în aceeași tranzacție:

1. Se creează avizul intern și ieșirea din stoc pe loturile FEFO din mașină.
2. Factura primește numărul.
3. Clientul plătește numerar: chitanța se emite din factură (F1, `RequiresImmediatePayment`), sub plafonul de 5.000 lei pe zi.
4. Factura și chitanța se tipăresc pe imprimanta portabilă, iar factura intră în coada e-Factura.

### 4.6 Factura de consignație (`FV EXD 10261`)

Din raportul de consum `RCS 0009` (V4 §4.5), Elena emite `FV-CSG` către Market Turda: 420 buc CEAI05 × 3,07 = 1.289,40 lei, TVA 270,77 lei, total 1.560,17 lei.

Validarea face ieșirea din gestiunea `CSG-MT01`, cu costul 1,94 lei/buc (814,80 lei). Nota contabilă e `4111` = `707` + `4427`, iar ieșirea e `607` = `357`.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `FacturaEmisa` v1 | Validarea facturii | Conținutul minim F3 (`InvoiceId`, `PartnerId`, `Number`, `DocumentDate`, `AccountingDate`, `CurrencyCode`, `ExchangeRate`, totalurile în valută și lei, `IsVatOnCollection`, `IsReversal`, `ReversedInvoiceId`, `IsAdvanceInvoice`, scadențele, `CostCenterId`, `ProjectId`, `EInvoiceStatus`, `EInvoiceIndexNo`), plus `InvoiceKind`, `TaxPointDate`, rezumatul TVA, avansurile regularizate, liniile cu sursa, costul și analitica, blocul `Accounting` | F3 (partida), C2 (contare), C4 (jurnal de vânzări, decont), V3 (comanda: facturat), V4 (avizele: închise), V7, CRM |
| `FacturaEFacturaStare` v1 | Schimbarea stării e-Factura | `InvoiceId`, starea, indexul, mesajele | F3 (afișare pe partidă), Notificări, Raportare |
| `FacturaTrimisaEmail` v1 | Emailul a plecat | `InvoiceId`, adresa, ora | CRM |
| `PlanFacturareModificat` v1 | Activare, suspendare, indexare, încheiere | `BillingPlanId`, starea, valoarea lunară | F5 (încasări previzionate), Raportare |

### 5.2 Consumate

| Eveniment | De la | Ce face V5 |
| --- | --- | --- |
| `IncasareInregistrata` v1 | F2, F1 | Dacă încasarea e legată de o proformă de avans sau de o comandă cu avans, creează sarcina „Factură de avans de emis” (3.3) |
| `EFacturaTrimisa`, `EFacturaValidata`, `EFacturaRespinsa` v1 | Integrări 02.14 | Actualizează `EInvoiceSubmission` și starea facturii; atașează fișierele |
| `AlocareInregistrata` v1 | F3 | Trece factura în Încasată sau înapoi |
| `LivrareEfectuata` v1 | V4 | Actualizează lista „de facturat” (proiecția pentru facturarea în masă) |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V5)

```csharp
// Port implementat de Integrări 02.14 (Erp.Integrations); V6 îl folosește pentru storno și reduceri
public interface IEInvoiceGateway
{
    Task<Guid> SubmitAsync(EInvoiceDocumentDto document, CancellationToken ct);      // răspunsul vine prin EFacturaTrimisa / Validata / Respinsa
    Task<EInvoiceValidationResult> ValidateLocallyAsync(EInvoiceDocumentDto document, CancellationToken ct);  // schematron, fără trimitere
    Task<Guid> RefreshStatusAsync(long invoiceId, CancellationToken ct);
}
public sealed record EInvoiceDocumentDto(int CompanyId, string SourceModule, long InvoiceId, string InvoiceTypeCode /* 380, 386 avans, 381 */,
    string Number, DateOnly IssueDate, DateOnly? TaxPointDate, DateOnly? DueDate, string CurrencyCode, decimal? ExchangeRate,
    EInvoicePartyDto Buyer, EInvoicePartyDto? Payee, string? BuyerReference, string? OrderReference, string? ContractReference,
    IReadOnlyList<EInvoiceLineDto> Lines, IReadOnlyList<EInvoiceVatBreakdownDto> VatBreakdown, IReadOnlyList<EInvoicePrepaidDto> Prepaid,
    string PaymentMeansCode, string? PayeeIban, IReadOnlyList<string> Notes);

// Citit de F3 (afișarea pe partidă), CRM (istoricul clientului), portalul B2B (viitor)
public interface ISalesInvoiceQueries
{
    Task<SalesInvoiceDto?> GetAsync(long invoiceId, CancellationToken ct);
    Task<IReadOnlyList<AdvanceBalanceDto>> GetOpenAdvancesAsync(int companyId, int partnerId, CancellationToken ct);
    Task<IReadOnlyList<EInvoiceIssueDto>> GetEInvoiceIssuesAsync(int companyId, CancellationToken ct);   // respinse, întârziate
}

// Citit de F5 (încasări previzionate din abonamente)
public interface IBillingPlanForecast
{
    Task<IReadOnlyList<ForecastRowDto>> GetAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

Comenzi interne V5:

- `SaveInvoice`, `AddInvoiceSources`, `ValidateInvoice`, `DeleteInvoiceDraft`;
- `PreviewMassInvoicing`, `RunMassInvoicing`;
- `IssueAdvanceInvoice`, `SettleAdvances`;
- `SaveBillingPlan`, `ActivateBillingPlan`, `SuspendBillingPlan`, `RunBillingPlansDue`;
- `SendInvoiceEmail`, `ResubmitEInvoice`, `CorrectEInvoiceData`.

## 7. Schema SQL `sales` (partea V5)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.SalesInvoice` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number) WHERE Number IS NOT NULL`; FK `PartnerId`, `PayerPartnerId`, `BillingAddressId`, `PaymentMethodId`, `TransactionTypeId`, `BillingPlanId` | `(CompanyId, PartnerId, DocDate)`; `(CompanyId, EInvoiceStatus) WHERE EInvoiceStatus IN (1, 2, 4, 5, 6)` |
| `sales.SalesInvoiceLine` | `Id bigint` | FK `InvoiceId`, `DeliveryNoteLineId`, `SalesOrderLineId`, `BillingPlanLineId`, `ConsignmentReportLineId`, `AdvanceInvoiceId`, `ItemVariantId`, `VatRateId`, `VatExemptionReasonId`; `CHECK` exact o sursă pentru `SourceKind` 1–4 și 6 | `(DeliveryNoteLineId)`; `(AdvanceInvoiceId) WHERE AdvanceInvoiceId IS NOT NULL` |
| `sales.SalesInvoiceVatSummary` | `(InvoiceId, VatCategoryCode, VatRate)` | FK `InvoiceId` | — |
| `sales.SalesInvoiceDueDate` | `(InvoiceId, Seq)` | `CHECK (Amount > 0)` | `(DueDate)` |
| `sales.EInvoiceSubmission` | `Id bigint` | UX `(InvoiceId, Attempt)`; UX `(UploadIndex) WHERE UploadIndex IS NOT NULL` | `(Status, CreatedAt)` |
| `sales.MassInvoicingRun`, `sales.MassInvoicingRunItem` | `Id bigint` | FK `RunId`, `InvoiceId` | `(CompanyId, StartedAt)` |
| `sales.BillingPlan`, `sales.BillingPlanLine`, `sales.BillingPlanIndexation` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number)`; FK `PartnerId`, `ItemVariantId` | `(CompanyId, Status, NextBillingDate)` |
| `sales.EInvoiceScopeRule` | `Id int` | — | `(IsActive, ValidFrom)` |
| `sales.PaymentScheduleTemplate`, `…Line` | `Id int` | UX `(CompanyId, CodeKey)` | — |
| `core.VatExemptionReason`, `core.UneceUnit`, `core.CpvCode` (Nucleu, seed) | `Id int` | UX `(Code)` | `CpvCode`: `(NameSearch)` |

```sql
CREATE TABLE sales.SalesInvoice
(
    Id                     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoice PRIMARY KEY,
    CompanyId              int            NOT NULL CONSTRAINT FK_SI_Company REFERENCES core.Company(Id),
    TransactionTypeId      int            NOT NULL CONSTRAINT FK_SI_Tt REFERENCES core.TransactionType(Id),
    InvoiceKind            tinyint        NOT NULL,
    NumberSeriesId         int            NOT NULL CONSTRAINT FK_SI_Series REFERENCES core.NumberSeries(Id),
    Number                 nvarchar(20)   NULL,
    DocDate                date           NOT NULL,
    TaxPointDate           date           NULL,
    PartnerId              int            NOT NULL CONSTRAINT FK_SI_Partner REFERENCES core.Partner(Id),
    PayerPartnerId         int            NULL     CONSTRAINT FK_SI_Payer REFERENCES core.Partner(Id),
    BillingAddressId       int            NOT NULL CONSTRAINT FK_SI_BillAddr REFERENCES core.PartnerAddress(Id),
    CurrencyCode           char(3)        NOT NULL,
    ExchangeRate           decimal(19,6)  NULL,
    ExchangeRateDate       date           NULL,
    VatOnCollection        bit            NOT NULL CONSTRAINT DF_SI_VatColl DEFAULT (0),
    PaymentMethodId        int            NOT NULL CONSTRAINT FK_SI_PayMethod REFERENCES core.PaymentMethod(Id),
    PaymentTermDays        smallint       NOT NULL,
    DueDate                date           NOT NULL,
    CustomerOrderRef       nvarchar(50)   NULL,
    ContractRef            nvarchar(50)   NULL,
    BuyerReference         nvarchar(100)  NULL,
    BillingPlanId          bigint         NULL     CONSTRAINT FK_SI_Plan REFERENCES sales.BillingPlan(Id),
    NetAmount              decimal(19,4)  NOT NULL CONSTRAINT DF_SI_Net DEFAULT (0),
    VatAmount              decimal(19,4)  NOT NULL CONSTRAINT DF_SI_Vat DEFAULT (0),
    TotalAmount            decimal(19,4)  NOT NULL CONSTRAINT DF_SI_Total DEFAULT (0),
    AdvanceSettledAmount   decimal(19,4)  NOT NULL CONSTRAINT DF_SI_Adv DEFAULT (0),
    AmountDue              decimal(19,4)  NOT NULL CONSTRAINT DF_SI_Due DEFAULT (0),
    TotalAmountRon         decimal(19,4)  NOT NULL CONSTRAINT DF_SI_TotalRon DEFAULT (0),
    VatAmountRon           decimal(19,4)  NOT NULL CONSTRAINT DF_SI_VatRon DEFAULT (0),
    CostAmount             decimal(19,4)  NOT NULL CONSTRAINT DF_SI_Cost DEFAULT (0),
    EInvoiceStatus         tinyint        NOT NULL CONSTRAINT DF_SI_Ef DEFAULT (0),
    EInvoiceUploadIndex    bigint         NULL,
    EInvoiceDownloadId     bigint         NULL,
    EInvoiceSentAt         datetime2(0)   NULL,
    EInvoiceDeadline       date           NULL,
    PrintTemplateId        int            NULL,
    EmailedAt              datetime2(0)   NULL,
    Status                 tinyint        NOT NULL CONSTRAINT DF_SI_Status DEFAULT (1),
    Notes                  nvarchar(1000) NULL,
    CreatedAt              datetime2(0)   NOT NULL CONSTRAINT DF_SI_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int            NOT NULL,
    ModifiedAt             datetime2(0)   NULL,
    ModifiedBy             int            NULL,
    ValidatedAt            datetime2(0)   NULL,
    ValidatedBy            int            NULL,
    RowVersion             rowversion     NOT NULL,
    CONSTRAINT CK_SI_Kind CHECK (InvoiceKind BETWEEN 1 AND 5),
    CONSTRAINT CK_SI_Status CHECK (Status IN (1, 3, 4, 8, 9)),
    CONSTRAINT CK_SI_Ef CHECK (EInvoiceStatus BETWEEN 0 AND 6),
    CONSTRAINT CK_SI_Number CHECK (Status IN (1, 9) OR Number IS NOT NULL),
    CONSTRAINT CK_SI_Due CHECK (DueDate >= DocDate),
    CONSTRAINT CK_SI_AmountDue CHECK (AmountDue = TotalAmount - AdvanceSettledAmount),
    CONSTRAINT CK_SI_Fx CHECK (CurrencyCode = 'RON' OR ExchangeRate IS NOT NULL)
);
```

Proceduri:

- **Factura:** `sales.usp_Invoice_Save`, `sales.usp_Invoice_AddSources` (TVP cu liniile de aviz sau de comandă), `sales.usp_Invoice_Validate` (numărul, legăturile, starea, verificările 52500–52521; mișcarea de stoc a facturii cu livrare o face aplicația prin `IStockLedger`, în aceeași tranzacție), `sales.usp_Invoice_VatSummary_Compute`.
- **Facturarea în masă:** `sales.usp_MassInvoicing_Preview`, `sales.usp_MassInvoicing_RunItem`.
- **Avansuri:** `sales.usp_Advance_OpenBalances`.
- **Facturarea recurentă:** `sales.usp_BillingPlan_Save`, `sales.usp_BillingPlan_Due`.
- **e-Factura:** `sales.usp_EInvoice_Enqueue`, `sales.usp_EInvoice_SaveResponse`, `sales.usp_EInvoice_Issues`.

**Coduri de eroare V5**

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52500 | `SALES_INV_MIXED_SOURCES` | 400 | Surse cu clienți, plătitori sau monede diferite |
| 52501 | `SALES_INV_MIXED_MONTHS` | 400 | Avize din luni calendaristice diferite pe aceeași factură |
| 52502 | `SALES_INV_SOURCE_INVOICED` | 409 | Aviz sau linie deja facturată integral (consum concurent) |
| 52503 | `SALES_INV_DATE_BEFORE_LAST` | 409 | Data facturii anterioară ultimei facturi validate din serie |
| 52504 | `SALES_INV_LOCKED` | 409 | Modificarea unei facturi validate (se stornează, V6) |
| 52505 | `SALES_INV_PERIOD_CLOSED` | 409 | Data facturii într-o perioadă închisă |
| 52506 | `SALES_INV_VAT_MENTION_MISSING` | 400 | Linie cu categorie `AE`, `K`, `G`, `E`, `O` fără motiv sau mențiune |
| 52507 | `SALES_INV_VIES_INVALID` | 409 | Livrare intracomunitară către un cod de TVA invalid în VIES |
| 52508 | `SALES_INV_BUYER_ID_MISSING` | 400 | Client fără cod fiscal sau identificator cerut de e-Factura |
| 52509 | `SALES_INV_NONSTOCK_ONLY` | 400 | Linie stocabilă pe o factură fără sursă sau din comandă fără livrare (`52015` din V1, aici pe facturile V5) |
| 52510 | `SALES_INV_PRICE_CHANGED` | 409 | Preț sau cantitate modificată pe o linie copiată din aviz |
| 52511 | `SALES_INV_DUE_SPLIT` | 400 | Suma scadențelor diferită de suma de plată |
| 52512 | `SALES_INV_AUTHORITY_TERM` | 409 | Termen de plată peste plafonul autorităților publice, fără contract care să-l permită |
| 52513 | `SALES_INV_POD_REQUIRED` | 409 | Aviz fără confirmarea livrării, cu `PodRequiredForInvoice` = 1 (V4 `52367`) |
| 52520 | `SALES_ADVANCE_OVER_BALANCE` | 409 | Regularizare peste restul avansului sau peste baza facturii pe cotă |
| 52521 | `SALES_ADVANCE_CURRENCY` | 400 | Avans în altă monedă decât factura finală |
| 52522 | `SALES_ADVANCE_NOT_COLLECTED` | 409 | Factură de avans emisă dintr-o proformă neîncasată, fără drept de emitere anticipată |
| 52530 | `SALES_CASH_LIMIT` | 409 | Plată numerar peste plafonul legal pe zi și client |
| 52540 | `SALES_BILLING_PERIOD_BILLED` | 409 | Perioada planului e deja facturată |
| 52541 | `SALES_BILLING_PLAN_STOCK_ITEM` | 400 | Articol stocabil pe un plan de facturare |
| 52550 | `SALES_EINV_NOT_CORRECTABLE` | 409 | Corecție după respingere pe un câmp care schimbă valorile (se stornează) |
| 52551 | `SALES_EINV_ALREADY_VALID` | 409 | Retrimiterea unei facturi deja validate de ANAF |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.Invoice.View` / `.Edit` / `.Validate` | Facturile |
| `Sales.Invoice.Mass` | Facturarea în masă |
| `Sales.Invoice.Advance` | Facturile de avans și regularizarea |
| `Sales.Invoice.WithDelivery` | Factura cu livrare (cere și drepturile de stoc pe gestiune) |
| `Sales.BillingPlan.Manage` | Planurile de facturare recurentă |
| `Sales.EInvoice.View` / `.Resubmit` / `.Correct` | Starea e-Factura, retrimiterea, corecția datelor nefiscale după respingere |
| `Sales.Invoice.ViewCost` | Costul și marja pe factură |

Roluri livrate (V1 §8, completate):

| Rol | Permisiuni V5 |
| --- | --- |
| Operator facturare | Toate, fără `ViewCost` |
| Agent de teren | `Invoice.View`, `.Edit`, `.Validate`, doar pentru `FV-LIV` și `FV-AVS` pe clienții lui |
| Contabil șef | Vizualizare, `EInvoice.*`, `BillingPlan.Manage` |
| Șef vânzări | Vizualizare, `ViewCost` |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V5 |
| --- | --- |
| Codul fiscal art. 319 (factura și mențiunile obligatorii) | Câmpurile antetului și ale liniilor, mențiunile pe categorii de TVA (3.4), termenul de emitere (15 ale lunii următoare), facturile de avans |
| Codul fiscal art. 282 (exigibilitatea), art. 282 alin. (8)–(9) (TVA la încasare) | `TaxPointDate`, regula lunii (regula 2), `UninvoicedVatAtMonthEnd` și `4428` pentru TVA la încasare |
| OUG 120/2021 și Legea 296/2023 (e-Factura B2B, B2C), RO_CIUS, EN 16931 | Transmiterea tuturor facturilor (3.7), termenul ca setare, CNP sau 13 zerouri pentru persoane fizice, corecția după respingere. Regulile se citesc din `EInvoiceScopeRule` și se verifică la punerea în funcțiune |
| Codul fiscal art. 294 și 331 (scutiri, taxare inversă) | Categoriile `K`, `G`, `AE` și mențiunile (3.4); VIES pentru intracomunitare |
| Legea 72/2013 (termenele de plată) | Avertizare peste 60 de zile, blocare peste 30 de zile pentru autorități (`52512`) |
| Legea 70/2015 (plafonul plăților în numerar) | `CashPaymentLimitCheck` (`52530`) |
| Legea 82/1991 și OMFP 2634/2015 (arhivare) | Factura, XML-ul semnat și PDF-ul se păstrează cu retenția documentelor contabile |

Integrări externe:

- e-Factura și VIES, prin Integrări 02.14;
- cursul BNR (Integrări);
- emailul (outbox, Nucleu).

Interne:

- V4 (avizele, avizul intern);
- V3 (comenzile, proforma, avansul cerut);
- V2 (prețul la factura fără sursă);
- F1 (chitanța);
- F2 (încasarea avansului);
- F3 (partida, alocarea, compensarea avansului);
- C2 (contarea);
- C3 (nota de TVA la sfârșitul lunii);
- C4 (jurnalul de vânzări, decontul, SAF-T);
- F5 (planurile recurente în previziune);
- S2 (ieșirea la factura cu livrare și la consignație).

**Precizări aplicate altor documente la validarea V5 (27.11.2026):**

- **V1 §3.9:** setările din 3.10. **V1 §3.4:** `EmailOnValidation` pe client.
- **Nucleu §3.5:** nomenclatoarele `core.VatExemptionReason`, `core.UneceUnit`, `core.CpvCode`, cu seed, și coloana `Item.DomesticReverseChargeCode`.
- **Contabilitate C2:** contările din 3.9 (avans `419`, regularizare, diferența de rotunjire, consignație).
- **C3:** nota de TVA a avizelor nefacturate la sfârșitul lunii (`UninvoicedVatAtMonthEnd` = 1) și inversarea ei.
- **F5:** rândul de încasări previzionate din `IBillingPlanForecast`.
- **Integrări 02.14:** portul `IEInvoiceGateway` și evenimentele `EFacturaTrimisa`, `EFacturaValidata`, `EFacturaRespinsa`.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Registrul facturilor emise | Pe serie, cu numerele, datele, clienții, valorile și starea e-Factura; golurile de numerotare (ar trebui să fie zero) | Contabil șef |
| e-Factura | Facturile pe stări, cele respinse cu motivul, cele aproape de termen sau întârziate; timpul mediu până la validare | Operator facturare, Contabil șef |
| Facturare în masă | Rulările, cu facturile generate, erorile și durata | Operator facturare |
| Avansuri | Pe client și comandă: avans încasat, facturat, regularizat, rămas; avansurile vechi | Contabil, Șef vânzări |
| Facturare recurentă | Planurile active, valoarea lunară, următoarele facturi, indexările de aplicat | Contabil, Director comercial |
| Vânzări facturate și marjă | Pe client, agent, articol, lună: facturat fără TVA, costul, marja (V7 îl detaliază) | Director comercial |

## 11. Ecrane principale

Tiparul comun este listă cu filtre, fișă cu tab-uri, apoi istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v5-facturare/](ecrane/v5-facturare/). Meniul Vânzări are Facturi (sus) și grupul Facturare: Facturare în masă, Facturi de avans, Facturare recurentă, e-Factura emise.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Facturi | Lista facturilor cu tipul, valoarea, scadența, starea e-Factura și a încasării | `v5-01-lista-facturi` |
| Factură din avize | Formularul `FV EXD 10247`: avizele sursă, liniile, rezumatul TVA, scadența, e-Factura, contarea | `v5-02-factura` |
| Facturare în masă | Previzualizarea rulării de 02.12: grupările, totalurile, cine nu se facturează | `v5-03-facturare-masa` |
| Factură finală cu avans | `FV EXD 10262`: liniile din aviz și regularizarea avansului pe cote | `v5-04-factura-avans-regularizat` |
| Facturi de avans | Avansurile de facturat, emise și neregularizate, pe client și comandă | `v5-05-facturi-avans` |
| e-Factura emise | Starea transmiterilor, respingerea `FV EXD 10256` cu mesajul și corecția, termenele | `v5-06-efactura` |
| Factură cu livrare | `FV EXD 10260` din mașina agentului, cu loturile, încasarea numerar și tipărirea | `v5-07-factura-livrare` |
| Facturare recurentă | Planul `PR 0007` și lista planurilor, cu următoarele facturi | `v5-08-facturare-recurenta` |
| Configurare › Facturare | Setările din 3.10, regulile e-Factura, șabloanele de scadență | `v5-09-configurare-facturare` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V5-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V5-A1 | `FV EXD 10247` din 3 avize: total 8.699,23 lei, rezumatul TVA 705,53 (11 % pe 6.413,92) + 274,18 (21 % pe 1.305,60); `TaxPointDate` 27.11; avizele trec în Închis; nota `4111` = `418` și `4428` = `4427` | Test de integrare (cu C2) |
| V5-A2 | Un aviz din noiembrie și unul din decembrie pe aceeași factură sunt refuzate (`52501`); facturarea în masă le desparte în două facturi | Test de integrare |
| V5-A3 | Facturarea în masă: 23 de avize → 14 facturi după `InvoiceGrouping`; o factură care eșuează nu oprește restul; numerele sunt continue | Test de integrare |
| V5-A4 | Numerotarea fiscală: validări concurente pe aceeași serie dau numere continue, fără goluri; o eroare la stoc pe factura cu livrare nu consumă număr; data anterioară ultimei facturi e refuzată (`52503`) | Test de integrare (concurență) |
| V5-A5 | Avansul: `FV EXD 10259` cu 2.055,00 + 226,05 și 696,00 + 146,16; `FV EXD 10262` regularizează pe cote și are de plată 3.123,21 lei; o regularizare peste rest e refuzată (`52520`) | Test de integrare (cu F3) |
| V5-A6 | Factura cu livrare `FV EXD 10260`: 565,56 + 77,67 = 643,23 lei, avizul intern, ieșirea din stoc pe loturi, chitanța; numerar peste 5.000 lei e refuzat (`52530`) | Test de integrare (cu S2, F1) |
| V5-A7 | Factura recurentă: jobul din 01.12 creează o singură factură pentru decembrie (o a doua rulare nu dublează, `52540`); textul „{perioada}” e înlocuit | Test de integrare |
| V5-A8 | e-Factura: factura validată intră în coadă; `EFacturaValidata` o trece în Validată; `EFacturaRespinsa` păstrează mesajele; corecția adresei și retrimiterea păstrează numărul; o corecție de cantitate e refuzată (`52550`) | Test de integrare (cu un gateway simulat) |
| V5-A9 | Categoriile de TVA: client UE cu VIES valid → `K` cu mențiunea; VIES invalid → `52507`; articol cu taxare inversă → `AE` cu codul NC; fără mențiune → `52506` | Test unitar + integrare |
| V5-A10 | Termenul e-Factura: 5 zile lucrătoare după calendarul Nucleu (factura din 01.12 are termen 08.12); alerta apare cu o zi lucrătoare înainte | Test unitar |
| V5-A11 | Factura în EUR: cursul BNR al zilei, TVA-ul și totalurile în lei, `TaxCurrencyCode` RON în UBL | Test de integrare |
| V5-A12 | Toate procedurile V5 rulează corect pe baza case sensitive; căutarea după numărul `fv exd 10247` găsește factura | Test de integrare |

Criteriile V5-A1, V5-A4, V5-A5 și V5-A8 sunt critice: ele decid valorile și contarea facturii, numerotarea fiscală, avansurile și conformitatea e-Factura.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **Factura `FV-MF`**: la editarea liniei, panoul fișei cere `IAssetDisposalPort.PreviewAsync` (valoarea netă și rezultatul estimat); la validare, Vânzările apelează `IAssetDisposalPort.DisposeAsync` în aceeași tranzacție SQL, după verificările proprii și înainte de alocarea numărului fiscal. Dacă ieșirea e refuzată (`65601`, `65603`), factura rămâne ciornă. [M5 §3.2](../15-mijloace-fixe/M5-IESIRI.md)

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Reînnoirile**: jobul CRM `CrmRenewalsDue` citește planurile active (`sales.BillingPlan`) cu `EndDate` în următoarele 60 de zile și creează oportunități `OP-REN` (grupul `RENEWAL`, o dată pe perioadă). Planul are butonul nou „Prelungește planul” (propune `EndDate` + 12 luni), folosit și din oportunitatea de reînnoire câștigată. [CR3 §3.6](../12-crm/CR3-OPORTUNITATI-PIPELINE.md)
- **`OportunitateCastigata`** cu linii recurente (chirii, abonamente) afișează agentului propunerea „Creează planul de facturare” cu liniile din oferta acceptată; nu se creează automat.
- **`FacturaEmisa`** e consumat și de CRM (rolul Prospect → Client, cronologia clientului).

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Index columnstore neclusterizat** pe `sales.SalesInvoiceLine`, pentru setul de date „Vânzări” (`sales.vw_SalesFact`). [BI2 §9](../13-raportare-bi/BI2-DEPOZIT-DATE.md)

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Ce adună o factură | Un client, un plătitor, o monedă; avizele și comenzile lui, cu puncte de livrare diferite; valorile copiate, nerecalculate | 2 regulile 1 și 3 | ✓ DA (27.11.2026) |
| 2 | Luna și TVA-ul avizelor nefacturate | O factură nu adună avize din luni diferite; `TaxPointDate` = ultima livrare; TVA-ul avizelor nefacturate la sfârșitul lunii intră în decontul lunii livrării (setare, de confirmat cu consultantul fiscal) | 2 regula 2, 3.1, 3.10 | ✓ DA (27.11.2026) |
| 3 | Factura cu livrare | Un singur document pentru utilizator (factura + avizul intern), validat într-o tranzacție; fără număr consumat dacă stocul refuză | 2 regula 7, 4.5 | ✓ DA (27.11.2026) |
| 4 | Facturarea în masă | Manuală sau prin job de seară; previzualizare; o factură pe tranzacție, erorile nu opresc rularea; email și e-Factura automat | 3.5, 4.2 | ✓ DA (27.11.2026) |
| 5 | Avansurile | Factura de avans la încasare, pe cotele comenzii; regularizare automată propusă pe factura finală, pe cote, cu plafon | 2 regula 6, 3.3, 4.4 | ✓ DA (27.11.2026) |
| 6 | Facturarea recurentă | Planuri cu frecvență, zi, în avans sau la sfârșit, indexare, validare automată sau ciornă, job zilnic idempotent | 3.6, 4.1 | ✓ DA (27.11.2026) |
| 7 | e-Factura | Trimitere automată după validare prin 02.14; stări vizibile; respingerea pe date nefiscale se corectează și se retrimite cu același număr, altfel storno + factură nouă; alertă înainte de termen | 2 regula 8, 3.7, 4.3 | ✓ DA (27.11.2026) |
| 8 | Categoriile de TVA | Alegere automată (forțată pe tranzacție, intracomunitar cu VIES, export, taxare inversă internă după articol, standard) cu mențiunile obligatorii | 3.4 | ✓ DA (27.11.2026) |
| 9 | Termenele de plată | Din condițiile clientului, cu tranșe opționale; avertizare peste 60 de zile, blocare peste 30 pentru autorități publice | 2 regula 9, 3.10 | ✓ DA (27.11.2026) |
| 10 | Numerotarea și data | Număr la validare, serie continuă, fără facturi cu dată anterioară ultimei din serie; ciornele fără număr | 2 regula 5 | ✓ DA (27.11.2026) |
| 11 | Nomenclatoare și precizări | Motivele de scutire (VATEX), unitățile UN/ECE, CPV, șabloanele de scadență, cu seed; precizările din 9 pentru V1, Nucleu, C2, C3, F5 și 02.14 | 3.8, 9 | ✓ DA (27.11.2026) |

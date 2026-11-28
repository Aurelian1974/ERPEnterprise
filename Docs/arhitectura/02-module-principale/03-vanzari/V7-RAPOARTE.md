# ERP · 02.03 Vânzări · V7 Rapoarte, marjă, comisioane și trasabilitate

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**. Ultima din cele 7 părți ale modulului 02.03 Vânzări: [V1 Configurare](V1-CONFIGURARE.md) ✓ · [V2 Prețuri și discounturi](V2-PRETURI-DISCOUNTURI.md) ✓ · [V3 Oferte, comenzi și rezervări](V3-OFERTE-COMENZI.md) ✓ · [V4 Livrare](V4-LIVRARE.md) ✓ · [V5 Facturare și e-Factura](V5-FACTURARE.md) ✓ · [V6 Retururi, storno și reduceri](V6-RETURURI-STORNO.md) ✓ · **V7 Rapoarte** ✓.

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`. V7 folosește `52800–52899`.

Această parte adună ce se vede **peste** documentele de vânzare:

- **catalogul rapoartelor** de vânzări, pe motorul comun de rapoarte din Nucleu;
- **faptul de vânzare** (`sales.SalesFactLine`): vânzarea netă, costul și marja, pe linie, cu toate dimensiunile, scris la validarea documentelor;
- **marja**: provizorie la vânzare, definitivă la închiderea lunii în Stocuri, cu explicația diferenței;
- **ținte** pe agent, zonă și familie, cu realizatul;
- **comisioanele agenților**: scheme, calculul lunar pe încasări, aprobarea, trimiterea la salarizare;
- **nivelul de servire**: livrat la data promisă și complet (OTIF), vânzările pierdute;
- **analiza clienților**: clasele ABC, clienții inactivi și în scădere;
- **drumul documentului** de la ofertă la încasare, cu retururile și notele de credit;
- **jurnalul de excepții** al vânzărilor;
- **tabloul de bord** al șefului de vânzări și al agentului.

Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.5 motorul de rapoarte `ReportView` și `ReportSchedule`, §3.6 legăturile între documente), [02.05 Stocuri](../05-stocuri/README.md) (S3 costul, S4 trasabilitatea loturilor), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F3 încasările), [02.04 Achiziții A7](../04-achizitii/A7-RAPOARTE-TRASABILITATE.md) (același tipar). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Documentele de vânzare** (V1 §3.1):

  | Cod | Document | Parte |
  | --- | --- | --- |
  | `OV` | Ofertă | V3 |
  | `CV` | Comandă de la client | V3 |
  | `PF` | Proformă | V3 |
  | `DL` | Dispoziție de livrare | V4 |
  | `EXD` (aviz) | Aviz de însoțire | V4 |
  | `FV` | Factură, inclusiv nota de credit (`IsReversal`) | V5, V6 |
  | `RC` | Retur de la client | V6 |

  Documentele sunt legate prin `core.DocumentLink` (Nucleu §3.6), pe grupurile de consum din V1 §3.3.
- **Motorul de rapoarte** e cel din Nucleu §3.5: filtre, desfășurare din total până la document, export Excel / PDF / CSV, vizualizări salvate, trimitere programată pe e-mail. V7 nu face un motor propriu.
- **Vizibilitatea** pe portofoliu (`SalesDataScope`, V1 §3.7: toți, echipa mea, doar portofoliul meu) se aplică pe toate rapoartele V7 și pe trimiterile programate, pe destinatar.
- **Agentul, zona și canalul** unui rând de raport sunt cele de pe document, nu cele curente ale clientului (V1 regula 7), ca schimbarea portofoliului să nu rescrie istoria.
- **Jurnalul de vânzări** pentru TVA și decontul sunt în Contabilitate C4. Registrul facturilor emise și starea e-Factura sunt în V5. Intrastat expedieri e în Stocuri S7. V7 nu le dublează.
- Valorile se țin pe `decimal(19,4)`, procentele pe `decimal(9,4)` (`SQL-07`). Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`).

## 1. Scop și perimetru

| Zonă | Ce face V7 | Ce primește de la alții |
| --- | --- | --- |
| Catalogul rapoartelor | Rapoartele de vânzări pe motorul comun, cu filtrele și vizibilitatea vânzărilor | Motorul (Nucleu §3.5) |
| Faptul de vânzare | Rândurile de vânzare netă, cost și marjă, la validarea facturilor și a notelor de credit; corecția de cost | Documentele V5, V6; costul ieșirii (S2/S3) |
| Marja | Provizorie și definitivă, pe orice dimensiune; explicația diferenței | Închiderea lunii în Stocuri (S3) |
| Ținte | Ținte lunare pe agent, zonă, familie de articole, canal | — |
| Comisioane | Scheme, extrasul lunar (`CMS`), aprobarea, exportul | Încasările (F3), salarizarea (02.09) |
| Nivelul de servire | OTIF pe liniile de comandă, vânzările pierdute pe motiv | Comenzile (V3), livrările (V4) |
| Analiza clienților | ABC, inactivi, în scădere, frecvența comenzilor | — |
| Drumul documentului | Pe document, pe linie, pe lot | Legăturile (Nucleu), F3, Stocuri S4 |
| Jurnalul de excepții | Abaterile aprobate din V1–V6 | Scrise de V1–V6 |
| Tabloul de bord | Indicatori, alerte, clasamente | Toate cele de mai sus |

## 2. Model de domeniu

```
 FV validată (V5) ─┐                                     ┌─► rapoarte de vânzări, marjă, ABC, ținte
 notă de credit (V6)┼─► SalesFactLine (net, cost, marjă) ┼─► CommissionStatement (CMS) ─► HR 02.09 / Achiziții A5 (agent extern)
 CostStocAjustat (S3)┘        ▲ corecție de cost          └─► vw_SalesFact (Raportare 02.13)
 AlocareInregistrata (F3) ───► SalesFactCollection (cât s-a încasat pe factură)
 CV / DL / aviz ─► OrderLineService (OTIF, pierdut)        SalesTarget ─► realizat față de țintă
 V1–V6 ─► IExceptionWriter ─► sales.ExceptionLog (doar adăugare)
```

Reguli de domeniu:

1. **Vânzarea netă se raportează din facturi, minus notele de credit.** Faptul de vânzare se scrie la validarea unei facturi și a unei note de credit, în aceeași tranzacție, cu prețul net al liniei (după discounturi), în lei. Avizele nefacturate apar separat, ca „livrat nefacturat”, și se pot adăuga în rapoarte cu un filtru (decizia 1).
2. **Costul e costul ieșirii din stoc, nu un cost standard.** Rândul de vânzare ia costul liniei de aviz (S2), iar la factura cu livrare costul avizului intern. Serviciile au cost zero, dacă nu au un cost configurat pe articol. Nota de credit din retur ia costul cu care a intrat returul (V6 regula 2), deci marja returului se anulează exact. Reducerile fără retur (V6 `FS-RED`) au cost zero și scad doar venitul.
3. **Marja e provizorie până la închiderea lunii în Stocuri.** După vânzare, costul unei ieșiri se poate corecta: costul de achiziție sosit târziu (Achiziții A6), regularizarea unui stoc negativ, recalcularea (S3). V7 consumă `CostStocAjustat` și actualizează costul rândurilor afectate. La închiderea lunii în Stocuri, rândurile lunii devin definitive (`IsFinal` = 1), iar o corecție ulterioară intră pe luna în care se face, ca rând de ajustare, nu pe luna vânzării (decizia 2).
4. **Agentul, zona, canalul și grupa clientului se copiază pe rând la scriere.** Ele vin de pe document. Schimbarea portofoliului după vânzare nu mută vânzările vechi. O corecție de atribuire se face doar prin documentul `RAG` (reatribuire), cu drept și motiv, scris în jurnalul de excepții.
5. **Comisionul se calculează pe încasare.** Baza e partea încasată în lună din fiecare factură a agentului, proporțional (încasat / total factură). Notele de credit scad baza în luna în care sunt alocate. O factură neîncasată nu aduce comision. Extrasul lunar se aprobă de șeful de vânzări și nu se mai modifică. Corecțiile ulterioare intră în extrasul următor (decizia 4).
6. **Nivelul de servire se măsoară pe linia de comandă, față de data promisă.** O linie e „la timp” dacă livrarea completă s-a făcut până la data promisă (V3), și „completă” dacă s-a livrat toată cantitatea confirmată, cu toleranța `ServiceCompleteTolerancePct`. Cantitățile nelivrate închise (V3) și diferențele la pregătire (V4, `ReasonCode` tipul 8) sunt **vânzări pierdute**, cu motivul (decizia 6).

## 3. Entități, stări și setări

### 3.1 Catalogul rapoartelor de vânzări

Rapoartele sunt înregistrate în motorul comun cu codul `sales.<raport>`. Fiecare are filtrele comune (perioadă, firmă, client, grupă, agent, echipă, zonă, canal, articol, familie, grupă de articole, gestiune, tranzacție, monedă) și se desfășoară până la document. Catalogul complet e în capitolul 10.

### 3.2 Faptul de vânzare (`sales.SalesFactLine`)

Un rând pe linie de factură sau de notă de credit. Scris de `sales.usp_Fact_Write`, apelată din validarea facturii (V5) și a notei de credit (V6), în aceeași tranzacție.

| Câmp | Reguli |
| --- | --- |
| `InvoiceId`, `InvoiceLineId`, `IsReversal`, `CreditKind` | Documentul și linia; tipul notei de credit (V6 §3.3) |
| `DocDate`, `TaxPointDate`, `PeriodMonth` | Data facturii; luna raportării e luna facturii |
| `PartnerId`, `PayerPartnerId`, `DeliveryPointId`, `CustomerGroupId`, `SalesChannelId`, `SalesZoneId`, `AgentId` | Copiate de pe document (regula 4) |
| `ItemVariantId`, `ItemId`, `ItemFamilyId`, `ItemGroupId`, `WarehouseId` | Articolul și gestiunea de ieșire |
| `QtyBase` | Cantitatea în UM de bază, negativă la storno și retur |
| `ListAmountRon`, `DiscountAmountRon`, `NetAmountRon` | Valoarea la prețul de listă, discounturile, valoarea netă (fără TVA, în lei la cursul facturii) |
| `CostAmountRon`, `CostSource` | Costul (regula 2); sursa: 1 ieșire din stoc · 2 retur · 3 cost configurat (serviciu) · 4 zero |
| `MarginAmountRon` | `NetAmountRon − CostAmountRon`, coloană calculată persistată |
| `IsFinal`, `FinalizedAt` | Marja definitivă după închiderea lunii în Stocuri (regula 3) |
| `CollectedAmountRon` | Cât din linie s-a încasat, repartizat proporțional din alocările F3 (`SalesFactCollection`) |

**Rândul de ajustare de cost** (`AdjustmentOfLineId` completat, `NetAmountRon` = 0) apare când costul unei luni definitive se corectează mai târziu. El are luna corecției.

**Livrat nefacturat.** Avizele validate și nefacturate se citesc din V4 (`IDeliveryNoteQueries`) la rularea raportului, cu prețul și costul lor. Ele nu se scriu în faptul de vânzare, ca să nu apară de două ori după facturare.

### 3.3 Țintele (`sales.SalesTarget`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Year`, `Month` | Luna țintei |
| `Dimension` | 1 agent · 2 zonă · 3 echipă · 4 familie de articole · 5 canal · 6 client (key account) |
| `DimensionId` | Agentul, zona, echipa, familia, canalul sau clientul |
| `Measure` | 1 vânzare netă · 2 marjă · 3 cantitate (cu `ItemFamilyId`) · 4 clienți activi |
| `TargetValue` | Valoarea țintă |

Țintele se încarcă din Excel (Nucleu fluxul 4.8) sau se generează din aceeași lună a anului trecut cu un procent de creștere. Realizatul se calculează din faptul de vânzare, cu aceleași reguli de atribuire.

### 3.4 Comisioanele (`CommissionScheme`, `CommissionStatement`)

**Schema** (`sales.CommissionScheme`), legată de agent (`SalesAgent.CommissionSchemeId`, coloană nouă în V1 §3.7) sau de tipul de agent:

| Câmp | Reguli |
| --- | --- |
| `Code`, `Name` | De exemplu `TEREN-MARJA` „Agent de teren, 8 % din marja încasată” |
| `Basis` | 1 marja încasată · 2 vânzarea netă încasată · 3 vânzarea netă facturată (fără condiția încasării) |
| `RatePct` sau praguri (`CommissionTier`) | Procentul simplu sau praguri pe realizarea țintei (de exemplu sub 90 % → 6 %, 90–100 % → 8 %, peste 100 % → 9 %) |
| `TargetBonusPct`, `TargetBonusBasis` | Bonusul la atingerea țintei lunare: procent din vânzarea netă sau din marjă |
| `ExcludedItemGroups` | Grupele de articole fără comision (de exemplu ambalajele SGR, transportul refacturat) |
| `LateCollectionDays` | Încasările mai vechi de atâtea zile după scadență nu aduc comision (0 = fără limită) |
| `ValidFrom`, `ValidTo` | O schemă folosită într-un extras aprobat nu se modifică, se închide și se creează alta |

**Extrasul lunar** (`sales.CommissionStatement`, seria `CMS`): `AgentId`, `Year`, `Month`, `SchemeId`, `BaseAmountRon`, `CommissionRon`, `BonusRon`, `AdjustmentRon` (corecții din lunile anterioare), `TotalRon`, `Status` (1 Calculat · 2 Aprobat · 3 Exportat · 9 Anulat), `ApprovedBy`, `ExportedAt`. Liniile (`CommissionStatementLine`) arată fiecare factură cu încasarea din lună, baza și comisionul ei, ca agentul să poată verifica.

Jobul din ziua `CommissionCalcDay` a lunii calculează extrasele lunii anterioare. Șeful de vânzări le aprobă. Exportul merge:
- la **salarizare** (02.09), pentru agenții interni cu `EmployeeId`, ca element variabil al lunii;
- pentru **agenții externi** (`AgentKind` = 4), ca extras PDF trimis agentului. Factura lui se înregistrează în Achiziții A5, cu extrasul ca referință.

### 3.5 Nivelul de servire (`sales.OrderLineService`)

Un rând pe linie de comandă confirmată, actualizat la fiecare aviz, închidere de rest sau anulare: `OrderLineId`, `RequestedDate`, `PromisedDate`, `ConfirmedQty`, `DeliveredQty`, `LastDeliveryDate`, `IsOnTime`, `IsComplete`, `LostQty`, `LostReasonId`, `LostAmountRon` (la prețul liniei). Indicatorii:

| Indicator | Calcul |
| --- | --- |
| **OTIF** | Liniile la timp și complete / liniile cu data promisă în perioadă |
| **La timp** (OTD) | Liniile livrate complet până la data promisă / liniile din perioadă |
| **Rata de umplere** (fill rate) | Cantitatea livrată / cantitatea confirmată |
| **Promis față de cerut** | Liniile cu data promisă egală cu data cerută (V3 §10) |
| **Vânzări pierdute** | `LostAmountRon` pe motiv (tipul 8 și motivele de anulare), pe articol și client |

### 3.6 Drumul documentului

Același component ca în Achiziții A7 §3.4, cu procedura `sales.usp_Document_Trace` pe `core.DocumentLink`, recursiv, limitat la adâncimea 12, în ambele sensuri:

- **pe document**: de la ofertă până la încasare, cu retururile, notele de credit, avizele stornate și e-Factura; nodurile din alte module (încasarea și compensarea din F3, mișcările de stoc, lotul din S4) se citesc prin interfețele lor publice;
- **pe linia de comandă**: cantitatea ofertată, comandată, confirmată, rezervată, pregătită, livrată, returnată, facturată, creditată și valoarea încasată, fiecare cu documentele ei;
- **pe lot**: din orice aviz sau retur se deschide trasabilitatea lotului din S4.

Ramurile care așteaptă ceva sunt marcate: comandă nelivrată, aviz nefacturat, retur fără decizie, notă de credit în aprobare, factură restantă.

### 3.7 Jurnalul de excepții (`sales.ExceptionLog`)

Scris de V1–V6 prin `ISalesExceptionWriter.WriteAsync`, în tranzacția documentului. Doar adăugare (`SP-18`).

| Excepție | Scrisă de | Exemplu |
| --- | --- | --- |
| Deblocare de credit | V1 | `CV 4433` Cafe Unirii, motiv `PROMISIUNE`, Radu Stan |
| Preț sau discount peste plafon, sub cost, sub marja minimă | V2 | `CV 4441`: discount 9 % aprobat de Radu Stan |
| Comandă peste plafonul de valoare sau termen de plată extins | V3 | — |
| Diferență la pregătire | V4 | `DL 0412`: 24 buc CEAI05, `DETDEPOZIT` |
| Aviz stornat după plecare | V4 | `AV-STO` 24 buc APA05 Hotel Central |
| Retur peste termenul politicii | V6 | — |
| Notă de credit aprobată | V6 | `FV EXD 10268` −14.944,21 lei, Radu Stan |
| Reatribuire de vânzări | V7 | — |

Câmpuri: `CompanyId`, `OccurredAt`, `ExceptionCode`, `DocType`, `DocId`, `DocLineId`, `PartnerId`, `AgentId`, `AmountRon`, `Details` (JSON scurt), `RequestedBy`, `ApprovedBy`, `Reason`.

### 3.8 Setările V7 (completare la `SalesSettings`)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `ServiceCompleteTolerancePct` | 0 | Toleranța sub care o livrare e încă „completă” |
| `ServiceOnTimeLateDays` | 0 | Zilele după data promisă în care o livrare e încă „la timp” |
| `CommissionCalcDay` | 3 | Ziua lunii în care se calculează extrasele de comision |
| `InactiveCustomerDays` | 60 | Fără comandă de atâtea zile: client inactiv |
| `DecliningCustomerPct` | 20 | Scădere față de aceeași perioadă a anului trecut de la care clientul e „în scădere” |
| `ClassAPct`, `ClassBPct` | 80, 95 | Pragurile cumulate ale claselor ABC |

## 4. Fluxuri principale

Datele de exemplu sunt pentru noiembrie 2026, la Exemplu Distribuție SRL, cu patru agenți: Vlad Georgescu (teren, HoReCa Cluj), Cristina Marin (key account), Ioana Rusu (telesales) și Mihai Rus (teren, Turda).

### 4.1 Vânzările și marja din noiembrie

1. Pe 02.12, după facturarea în masă a avizelor din noiembrie (V5), raportul „Vânzări și marjă” pe agenți arată:

   | Agent | Vânzare netă | Cost | Marjă | Marjă % | Țintă | Realizat |
   | --- | --- | --- | --- | --- | --- | --- |
   | Vlad Georgescu | 118.420,00 | 84.260,00 | 34.160,00 | 28,85 % | 120.000,00 | 98,7 % |
   | Cristina Marin | 186.300,00 | 152.940,00 | 33.360,00 | 17,91 % | 180.000,00 | 103,5 % |
   | Ioana Rusu | 52.180,00 | 38.420,00 | 13.760,00 | 26,37 % | 55.000,00 | 94,9 % |
   | Mihai Rus | 61.740,00 | 44.880,00 | 16.860,00 | 27,31 % | 60.000,00 | 102,9 % |
   | **Total** | **418.640,00** | **320.500,00** | **98.140,00** | **23,44 %** | **415.000,00** | **100,9 %** |

   Din vânzarea lui Cristina Marin, 104.280,00 lei sunt la Mega Retail SRL, rulajul din noiembrie al acordului `RB-MR-Q4` (V6 §4.4).
2. Marja e **provizorie**: Stocuri nu a închis încă luna. Pe 03.12, repartizarea costurilor de transport pentru importul de ceai (Achiziții A6) crește costul CEAI05 vândut în noiembrie cu 0,04 lei/buc, adică 1.240,00 lei pe 31.000 buc. V7 primește `CostStocAjustat`, actualizează rândurile, iar marja totală devine 96.900,00 lei (23,15 %). Raportul arată diferența pe sursă: „costuri de achiziție sosite după vânzare: −1.240,00”.
3. Pe 05.12, Stocuri închide noiembrie. Rândurile lunii devin definitive. O corecție de cost venită în decembrie pentru noiembrie va apărea ca rând de ajustare în decembrie.

### 4.2 Comisionul lui Vlad Georgescu pe noiembrie (`CMS 0011`)

1. Schema `TEREN-MARJA`: 8 % din marja încasată, cu bonus de 1 % din vânzarea netă la atingerea țintei, fără ambalaje SGR.
2. Pe 03.12, jobul calculează extrasul: facturile lui Vlad încasate în noiembrie (unele emise în octombrie) aduc o marjă încasată de 31.840,00 lei. Notele de credit alocate în noiembrie scad 412,00 lei. Baza e 31.428,00 lei, comisionul 2.514,24 lei. Ținta e realizată 98,7 %, deci fără bonus.
3. Celelalte extrase:

   | Extras | Agent | Schema | Calcul | Total |
   | --- | --- | --- | --- | --- |
   | `CMS 0011` | Vlad Georgescu | `TEREN-MARJA` | 31.428,00 × 8 % | 2.514,24 |
   | `CMS 0012` | Cristina Marin | `KAM-VANZARE`: 0,6 % din vânzarea încasată, bonus 0,2 % | 171.500,00 × 0,6 % + 186.300,00 × 0,2 % | 1.401,60 |
   | `CMS 0013` | Ioana Rusu | `TEL-MARJA`: 5 % din marja încasată, fără bonus | 12.980,00 × 5 % | 649,00 |
   | `CMS 0014` | Mihai Rus | `TEREN-MARJA` | 15.920,00 × 8 % + 61.740,00 × 1 % | 1.891,00 |
   | | | | **Total** | **6.455,84** |

4. Pe 04.12, Radu Stan aprobă cele patru extrase. Exportul trimite sumele la salarizare (02.09) ca element variabil pentru noiembrie. Fiecare agent vede extrasul lui, cu factura, încasarea și baza pe fiecare rând.

### 4.3 Nivelul de servire din noiembrie

În noiembrie au fost 412 linii de comandă cu data promisă în lună. 371 au fost livrate la timp și complet, deci OTIF e 90,0 %. Rata de umplere pe cantitate e 98,2 %.

Vânzările pierdute sunt de 2.186,40 lei. Cea mai mare parte vine din CEAI05 (stoc deteriorat la pregătire, `DETDEPOZIT`, inclusiv cele 24 buc din `DL 0412`) și din APA2N (lipsă de stoc la furnizor).

### 4.4 Drumul comenzii `CV 4441` Hotel Central

Din comanda `CV 4441`, „Drumul documentului” arată:

```
CV 4441 (confirmată 26.11) ─► DL 0412 (28.11, 24 buc CEAI05 lipsă, DETDEPOZIT)
   └─► EXD 30625 (aviz, 28.11) ─► AV-STO 24 buc APA05 (refuzate la livrare)
         └─► FV EXD 10247 (02.12, 8.699,23, e-Factura validată)
               ├─► RC 0051 (04.12, 12 buc CEAI05, casare PVC 0207) ─► FV EXD 10270 (−42,11)
               └─► F3: rest de plată 8.657,12, scadență 17.12
```

„Drumul liniei” pentru CEAI05:

- comandat 288;
- pregătit și livrat 264;
- returnat 12;
- facturat 264;
- creditat 12;
- rămas de livrat 24, rezervat ferm.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut | Consumatori |
| --- | --- | --- | --- |
| `ExtrasComisionAprobat` v1 | Aprobarea unui extras `CMS` | `StatementId`, `AgentId`, `EmployeeId` sau `PartnerId`, luna, `TotalRon` | HR 02.09 (element variabil), Achiziții (agentul extern), F5 |
| `TintaDepasita` v1 | Prima zi în care realizatul trece de 100 % | `TargetId`, dimensiunea, luna | Notificări (agent, șef de echipă) |

### 5.2 Consumate

| Eveniment | De la | Ce face V7 |
| --- | --- | --- |
| `CostStocAjustat` v1 | Stocuri S3 | Actualizează costul rândurilor de vânzare afectate sau scrie rândul de ajustare (regula 3) |
| `LunaStocInchisa` v1 | Stocuri S3 (la închiderea lunii) | Marchează rândurile lunii ca definitive |
| `AlocareInregistrata` v1 | F3 | Actualizează `CollectedAmountRon` pe liniile facturii |
| `FacturaEmisa` v1 | V5, V6 | — (faptul de vânzare se scrie sincron, în tranzacția validării) |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V7)

```csharp
// Citit de HR 02.09 (comisioanele lunii) și de F5
public interface ICommissionQueries
{
    Task<IReadOnlyList<CommissionStatementDto>> GetApprovedAsync(int companyId, int year, int month, CancellationToken ct);
}

// Scris de V1–V6, în tranzacția documentului
public interface ISalesExceptionWriter
{
    Task WriteAsync(SalesExceptionDto exception, CancellationToken ct);
}

// Citit de CRM 02.12 și de portalul B2B: istoricul clientului
public interface ICustomerSalesQueries
{
    Task<CustomerSalesSummaryDto> GetSummaryAsync(int partnerId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

Comenzi interne V7:

- **Ținte:** `SaveTargets`, `ImportTargets`, `GenerateTargetsFromLastYear`.
- **Comisioane:** `SaveCommissionScheme`, `CalculateCommissionStatements` (job), `ApproveCommissionStatement`, `ExportCommissionStatements`.
- **Faptul de vânzare:** `ReassignSales` (documentul `RAG`), `RebuildSalesFact` (doar administrator, pe o perioadă, pentru recuperare).

## 7. Schema SQL `sales` (partea V7)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.SalesFactLine` | `Id bigint` | FK `InvoiceId`, `InvoiceLineId`, `AgentId`, `PartnerId`, `ItemVariantId`; `MarginAmountRon AS (NetAmountRon - CostAmountRon) PERSISTED` | Columnstore nonclustered pe `(PeriodMonth, CompanyId, AgentId, PartnerId, ItemId, NetAmountRon, CostAmountRon)`; `(InvoiceLineId)` |
| `sales.SalesFactCollection` | `Id bigint` | FK `FactLineId`; `AllocationId` (F3) | `(FactLineId)`, `(CollectedOn)` |
| `sales.SalesTarget` | `Id int` | UX `(CompanyId, Year, Month, Dimension, DimensionId, Measure)` | — |
| `sales.CommissionScheme`, `sales.CommissionTier` | `Id int` | UX `(CompanyId, CodeKey)` | — |
| `sales.CommissionStatement`, `sales.CommissionStatementLine` | `Id bigint` | UX `(AgentId, Year, Month) WHERE Status <> 9` | `(CompanyId, Status)` |
| `sales.OrderLineService` | `OrderLineId bigint` | FK `OrderLineId`, `LostReasonId` | `(PromisedDate)` |
| `sales.ExceptionLog` | `Id bigint` | — | `(CompanyId, OccurredAt)`, `(DocType, DocId)` |

Vederi publicate pentru Raportare (02.13), contract stabil și versionat: `sales.vw_SalesFact`, `sales.vw_OrderBook` (comenzile deschise, cu valoarea rămasă), `sales.vw_OrderLineService`, `sales.vw_CommissionStatement`.

Proceduri:

- `sales.usp_Fact_Write`, `sales.usp_Fact_ApplyCostAdjustment`, `sales.usp_Fact_Finalize`, `sales.usp_Fact_ApplyCollection`;
- `sales.usp_Commission_Calculate`;
- `sales.usp_Service_Update`;
- `sales.usp_Document_Trace`;
- rapoartele, câte o procedură `sales.usp_Report_<raport>` pe raport, citite de motorul comun.

**Coduri de eroare V7**

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52800 | `SALES_FACT_ALREADY_WRITTEN` | 409 | A doua scriere a faptului pentru aceeași linie de factură |
| 52801 | `SALES_FACT_PERIOD_FINAL` | 409 | Modificarea costului unui rând definitiv, altfel decât prin rând de ajustare |
| 52802 | `SALES_REASSIGN_RIGHT` | 403 | Reatribuire fără dreptul `Sales.Fact.Reassign` sau fără motiv |
| 52810 | `SALES_TARGET_DUPLICATE` | 409 | Două ținte pentru aceeași lună, dimensiune și măsură |
| 52820 | `SALES_COMMISSION_STATEMENT_LOCKED` | 409 | Modificarea sau recalcularea unui extras aprobat |
| 52821 | `SALES_COMMISSION_SCHEME_IN_USE` | 409 | Modificarea unei scheme folosite într-un extras aprobat |
| 52822 | `SALES_COMMISSION_PERIOD_OPEN` | 409 | Calcul pentru o lună neîncheiată |
| 52823 | `SALES_COMMISSION_SELF_APPROVAL` | 409 | Agentul își aprobă propriul extras |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.Report.View` | Rapoartele de vânzări, în limita `SalesDataScope` |
| `Sales.Report.ViewMargin` | Costul și marja; fără el, rapoartele arată doar vânzarea |
| `Sales.Target.Manage` | Țintele |
| `Sales.Commission.View` / `.Manage` / `.Approve` | Extrasele proprii (agentul vede doar extrasul lui), schemele, aprobarea |
| `Sales.Fact.Reassign` | Reatribuirea vânzărilor (`RAG`) |
| `Sales.ExceptionLog.View` | Jurnalul de excepții |

| Rol | Permisiuni V7 |
| --- | --- |
| Agent de teren, Telesales | `Report.View` pe portofoliu, `Commission.View` (al lui) |
| Key account | Ca agentul, plus `ViewMargin` pe portofoliu |
| Șef vânzări | Toate, `Commission.Approve`, `Fact.Reassign` |
| Director comercial | Toate, în afară de `Fact.Reassign` |
| Contabil șef | `Report.View`, `ViewMargin`, `ExceptionLog.View`, `Commission.View` |
| Responsabil salarizare | `Commission.View` pe extrasele aprobate |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V7 |
| --- | --- |
| Regulamentul (UE) 2016/679 (GDPR) | Rapoartele cu clienți persoane fizice respectă vizibilitatea și exportul cu jurnal; extrasele de comision sunt date personale ale agentului |
| Legea 53/2003 (Codul muncii) | Comisionul agentului intern e parte variabilă a salariului; regulile schemei trebuie să fie cele din contract sau din regulamentul intern |
| Regulamentul (CE) 178/2002 | Trasabilitatea lotului de la client înapoi la furnizor, din drumul documentului (S4) |

Integrări interne:

- V1–V6 (documentele, legăturile, excepțiile);
- Stocuri S3 (corecțiile de cost, închiderea lunii) și S4 (trasabilitatea lotului);
- F3 (încasările);
- HR 02.09 (comisioanele);
- Achiziții A5 (factura agentului extern);
- Raportare 02.13 (vederile publicate);
- CRM 02.12 (istoricul clientului).

**Precizări aplicate altor documente la validarea V7 (27.11.2026):**

- **V1 §3.7:** coloana `SalesAgent.CommissionSchemeId`. **V1 §3.9:** setările din 3.8.
- **V1–V6:** excepțiile din 3.7 se scriu prin `ISalesExceptionWriter`, în tranzacția documentului.
- **V5 și V6:** validarea facturii și a notei de credit apelează `sales.usp_Fact_Write` în aceeași tranzacție.
- **Stocuri S3:** evenimentul `LunaStocInchisa` la închiderea lunii; `CostStocAjustat` poartă liniile de ieșire afectate (`SourceLineId`).
- **HR 02.09:** elementul variabil „comision vânzări”, primit din `ExtrasComisionAprobat`.
- **Raportare 02.13:** vederile publicate ale vânzărilor.

## 10. Rapoarte (catalogul complet al modulului)

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Vânzări și marjă | Pe orice dimensiune, cu comparație față de luna anterioară și anul trecut; marja provizorie sau definitivă | Șef vânzări, Director, Contabil |
| Explicația marjei | Diferența dintre marja provizorie și cea definitivă, pe sursă (cost de achiziție târziu, regularizare, recalculare) | Contabil șef, Director |
| Realizat față de țintă | Pe agent, zonă, echipă, familie, canal, client, cu proiecția la sfârșitul lunii | Șef vânzări, agenți |
| Comisioane | Extrasele lunii, cu detaliul pe factură; comisioane plătite pe an | Șef vânzări, agent, salarizare |
| Nivelul de servire | OTIF, la timp, rata de umplere, promis față de cerut, pe client, articol, gestiune | Șef vânzări, Șef depozit |
| Vânzări pierdute | Pe motiv, articol, client, cu valoarea | Șef vânzări, Achiziții |
| Comenzi deschise (order book) | Comenzile confirmate nelivrate, cu valoarea rămasă, data promisă și blocajele | Șef vânzări, Șef depozit |
| Livrat nefacturat | Avizele nefacturate (V4), cu vechimea | Operator facturare, Contabil |
| Clienți ABC | Vânzarea pe client cu procentul cumulat și clasa | Șef vânzări, Key account |
| Clienți inactivi și în scădere | Fără comandă de `InactiveCustomerDays`; scădere peste `DecliningCustomerPct` | Agenți, Șef vânzări |
| Istoricul prețurilor pe client | Prețul net facturat pe variantă și client în timp, față de lista curentă | Key account, Șef vânzări |
| Promoții și discounturi | Discounturile acordate pe regulă (V2), cu vânzarea și marja generate | Șef vânzări |
| Jurnalul de excepții | Pe tip, aprobator, agent, client; excepțiile repetate | Director, Contabil șef |
| Drumul documentului | Pe document, pe linie, pe lot | Toți, în limita drepturilor |

Rapoartele din V1–V6 (catalog incomplet, rezervări, e-Transport, e-Factura, avansuri, retururi, note de credit, rabaturi, SGR) rămân în părțile lor și apar în același catalog.

## 11. Ecrane principale

Tiparul comun este listă cu filtre, fișă cu tab-uri, apoi istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v7-rapoarte/](ecrane/v7-rapoarte/). Meniul Vânzări are grupul Rapoarte.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Tablou de bord | Indicatorii lunii, alertele, clasamentele agenților și clienților | `v7-01-tablou-bord` |
| Vânzări și marjă | Raportul pe agenți din 4.1, cu marja provizorie și explicația diferenței | `v7-02-vanzari-marja` |
| Realizat față de țintă | Țintele lunii pe agenți și familii, cu proiecția | `v7-03-tinte` |
| Extras de comision | `CMS 0011` Vlad Georgescu, cu facturile încasate | `v7-04-extras-comision` |
| Nivelul de servire | OTIF, rata de umplere, vânzările pierdute pe motiv | `v7-05-nivel-servire` |
| Drumul documentului | `CV 4441` cu toate documentele și drumul liniei CEAI05 | `v7-06-drumul-documentului` |
| Jurnalul de excepții | Excepțiile lunii, pe tip și aprobator | `v7-07-jurnal-exceptii` |
| Configurare › Rapoarte și comisioane | Schemele de comision, setările din 3.8 | `v7-08-configurare-comisioane` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V7-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V7-A1 | Validarea unei facturi scrie un rând de vânzare pe fiecare linie, în aceeași tranzacție; dacă scrierea eșuează, factura nu se validează; a doua scriere e refuzată (`52800`) | Test de integrare |
| V7-A2 | Nota de credit din retur (`FV EXD 10270`) scrie −34,80 vânzare și −23,28 cost; reducerea `FV EXD 10271` scrie −60,00 vânzare și cost zero | Test de integrare |
| V7-A3 | `CostStocAjustat` de +0,04 lei/buc pe 31.000 buc CEAI05 schimbă marja din noiembrie de la 98.140,00 la 96.900,00; după închiderea lunii, o nouă corecție creează un rând de ajustare în luna curentă (`52801` la modificarea directă) | Test de integrare (cu S3) |
| V7-A4 | Agentul de pe rând rămâne cel de pe document după schimbarea portofoliului; reatribuirea cere drept și motiv (`52802`) și apare în jurnalul de excepții | Test de integrare |
| V7-A5 | Totalurile din 4.1 (418.640,00 / 320.500,00 / 98.140,00) și realizatul față de țintă ies din datele de test | Test de integrare |
| V7-A6 | `CMS 0011`: 31.840,00 − 412,00 = 31.428,00 × 8 % = 2.514,24 lei, fără bonus la 98,7 %; o factură neîncasată nu intră în bază; extrasul aprobat nu se recalculează (`52820`) | Test de integrare (cu F3) |
| V7-A7 | O încasare parțială de 50 % dintr-o factură aduce 50 % din marja ei în bază | Test unitar |
| V7-A8 | OTIF: o linie livrată complet după data promisă e completă, dar nu la timp; o linie cu rest închis are vânzare pierdută cu motivul | Test de integrare |
| V7-A9 | Drumul documentului pentru `CV 4441` conține avizul, stornarea parțială, factura, returul, nota de credit și partida din F3 | Test de integrare |
| V7-A10 | Un agent cu `SalesDataScope` = 3 vede în rapoarte doar portofoliul lui; trimiterea programată către el respectă același filtru | Test de integrare |
| V7-A11 | Fără `Sales.Report.ViewMargin`, coloanele de cost și marjă lipsesc și din export | Test de integrare |
| V7-A12 | Raportul „Vânzări și marjă” pe 12 luni, cu 2 milioane de rânduri, răspunde sub 3 secunde (columnstore) | Test de performanță |
| V7-A13 | Toate procedurile V7 rulează corect pe baza case sensitive | Test de integrare |

Criteriile V7-A1, V7-A3 și V7-A6 sunt critice: ele decid dacă vânzarea, marja și comisionul sunt corecte și explicabile.

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Consumatori noi ai vederilor**: fișa 360 și rapoartele CRM citesc `sales.vw_SalesFact` (cifra de afaceri, articolele cumpărate, articolele oprite) și `sales.vw_OrderBook` (oferte și comenzi deschise), cu permisiunile Vânzărilor (marja doar cu `Sales.Margin.View`). [CR5 §2](../12-crm/CR5-FISA360-RAPOARTE.md)
- **Ținta agentului** (`sales.SalesAgentTarget`, precizare în V1) intră în rapoartele de realizare pe agent.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Ce se raportează ca vânzare | Facturile minus notele de credit, scrise în faptul de vânzare la validare; avizele nefacturate separat, ca „livrat nefacturat”, adăugabile cu un filtru | 2 regula 1, 3.2 | ✓ DA (27.11.2026) |
| 2 | Marja | Costul real al ieșirii din stoc; provizorie până la închiderea lunii în Stocuri, actualizată din corecțiile de cost, apoi definitivă; corecțiile ulterioare pe luna lor, cu explicația diferenței | 2 regulile 2 și 3, 4.1 | ✓ DA (27.11.2026) |
| 3 | Atribuirea vânzărilor | Agentul, zona și canalul de pe document, înghețate pe rând; schimbarea doar prin reatribuire cu drept și motiv | 2 regula 4 | ✓ DA (27.11.2026) |
| 4 | Comisioanele | Scheme pe agent (marjă sau vânzare, încasată sau facturată, procent sau praguri, bonus la țintă); calcul lunar pe încasări, notele de credit scad baza; extras aprobat de șeful de vânzări, nemodificabil | 2 regula 5, 3.4, 4.2 | ✓ DA (27.11.2026) |
| 5 | Unde merge comisionul | Agenții interni la salarizare (02.09), ca element variabil; agenții externi primesc extrasul, iar factura lor intră în Achiziții | 3.4 | ✓ DA (27.11.2026) |
| 6 | Nivelul de servire | OTIF pe linia de comandă față de data promisă, rata de umplere, vânzările pierdute pe motiv | 2 regula 6, 3.5, 4.3 | ✓ DA (27.11.2026) |
| 7 | Țintele | Lunare, pe agent, zonă, echipă, familie, canal sau client, în valoare, marjă, cantitate sau clienți activi; import din Excel sau generare din anul trecut | 3.3 | ✓ DA (27.11.2026) |
| 8 | Drumul documentului | Același component ca în Achiziții: pe document (de la ofertă la încasare), pe linia de comandă și pe lot | 3.6, 4.4 | ✓ DA (27.11.2026) |
| 9 | Jurnalul de excepții | Tabel propriu, doar adăugare, scris de V1–V6 în tranzacția documentului | 3.7 | ✓ DA (27.11.2026) |
| 10 | Vizibilitatea marjei | Costul și marja doar cu drept separat, inclusiv la export; agenții de teren văd doar vânzarea | 8 | ✓ DA (27.11.2026) |
| 11 | Tabloul de bord și Raportarea | Ecran fix pentru șeful de vânzări și pentru agent; vederile `vw_SalesFact`, `vw_OrderBook`, `vw_OrderLineService`, `vw_CommissionStatement` publicate pentru 02.13 | 7, 11 | ✓ DA (27.11.2026) |
| 12 | Precizări în alte documente | Cele din 9: V1, V1–V6 (excepțiile), V5 și V6 (faptul de vânzare), Stocuri S3, HR 02.09, Raportare 02.13 | 9 | ✓ DA (27.11.2026) |

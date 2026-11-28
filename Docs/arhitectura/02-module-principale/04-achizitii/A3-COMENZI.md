# ERP · 02.04 Achiziții · A3 Comenzi la furnizor: emitere, aprobare, trimitere, confirmare și urmărirea livrării

26 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**, cu ecranele și toate cele 11 decizii. A treia din cele 7 părți ale modulului 02.04 Achiziții. Se sprijină pe [A1 Configurare](A1-CONFIGURARE.md) și [A2 Necesar și oferte](A2-NECESAR-SI-OFERTE.md), ambele validate pe 26.11.2026.

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`; partea A3 folosește `53200–53299`. Această parte acoperă **comanda la furnizor**, de la ciornă până la închidere: crearea (manual, din necesar sau din oferta câștigătoare), prețul propus, livrările eșalonate, aprobarea pe praguri, trimiterea pe email cu PDF, confirmarea furnizorului cu diferențele ei, modificările după trimitere (revizii), urmărirea livrărilor și a întârzierilor, închiderea și anularea. Recepția însăși e în A4, factura în A5. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.6 Tranzacții și legături, fluxul 4.7 Notificări și email), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F5 prognoza de cash-flow), [02.05 Stocuri](../05-stocuri/README.md) (S5 disponibilul proiectat). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Liniile comenzii stau pe variantă** (`core.ItemVariant`, SKU), obligatoriu, inclusiv pentru servicii (articole nestocabile, de exemplu „Transport”). Cantitatea se ține în UM de cumpărare și, calculat, în UM de bază; factorii vin din setul de UM al articolului (Nucleu).
- **Tranzacțiile comenzii** sunt livrate de A1 §3.2: `CF-MAN` (manuală), `CF-NC` (din necesar, grupul `ORDER` pe linia de necesar) și `CF-OF` (din oferta acceptată, `ORDER` pe linia de ofertă și, indirect, pe linia de necesar, A2 §4.6). Comanda e la rândul ei sursă pentru aviz (`DELIVER`), NIR (`RECEIVE`) și factură (`INVOICE`); „de primit” și „de facturat” se calculează din `core.DocumentLink`, nu se stochează pe linie (A1 decizia 5).
- **Comanda nu mișcă stocul.** Ce e de primit apare în disponibilul proiectat (Stocuri S5) doar pentru cantitatea confirmată, la data confirmată, prin `IPurchaseOrderSupply` (A1 §6). Calculul necesarului din A2 vede și cantitățile neconfirmate.
- **Aprobarea** folosește regulile din A2 §3.2 (`purch.PurchaseApprovalRule` cu `DocumentType = 'PurchaseOrder'`): niveluri 1–3 cu prag în lei, în ordine, autorul nu aprobă, același om nu aprobă două niveluri, jurnal doar prin adăugare (`purch.PurchaseApproval`).
- **Prețul propus** vine din `ISupplierPriceProvider` (A1 §3.6): listă de prețuri cu praguri → ultimul preț → fără preț. Nu există preț standard de achiziție.
- Comanda nu se contează. Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Cantitățile și prețurile pe `decimal(19,6)`, valorile pe `decimal(19,4)` (`SQL-07`).
- **Ce NU face A3:** nu recepționează (A4), nu înregistrează facturi, proforme sau avansuri (A5), nu tratează importul și DVI (A6), nu calculează indicatorii de performanță ai furnizorilor (A7, din datele păstrate aici). Nu are portal sau EDI cu furnizorii (viitor, `OrderChannel = 2`).

## 1. Scop și perimetru

| Zonă | Ce face A3 | Ce NU face (și cine face) |
| --- | --- | --- |
| Emitere | Comanda manuală, din necesar sau din oferta acceptată; prețul propus; minimul și multiplul furnizorului; livrările eșalonate pe linie | Calculul necesarului, oferta și atribuirea (A2) |
| Aprobare | Planul de aprobare pe praguri, aprobarea, returnarea, respingerea; reaprobarea după o modificare care mărește valoarea | Regulile de aprobare (A2 §3.2, comune) |
| Trimitere | PDF-ul comenzii cu codurile furnizorului, emailul prin outbox, retrimiterea după revizie, anularea trimisă furnizorului | Serverul de email și outbox-ul (Nucleu) |
| Confirmare | Confirmarea furnizorului pe linii și pe livrări: cantitate, preț, dată; diferențele și decizia pe ele | — |
| Modificări | Revizii ale comenzii trimise, cu istoric și comparație între versiuni | — |
| Urmărire | Lista comenzilor deschise, livrările scadente și întârziate, reamintirile automate către furnizor și notificările către achizitor și solicitant | Indicatorii de livrare la timp și completă (A7) |
| Închidere | Închiderea automată la recepția completă, închiderea manuală a restului, anularea | Recepția (A4) |
| Angajamente | Valoarea comandată și nefacturată, cu data estimată a plății, pentru prognoza de trezorerie | Prognoza însăși (F5) |

## 2. Model de domeniu

```
 PurchaseOrder (CF)                              furnizor, monedă, gestiune de livrare, termene, achizitor, stare, revizie
   ├─ PurchaseOrderLine × N                      variantă, UM, cantitate, preț, reducere, cotă TVA, sursa prețului
   │    └─ PurchaseOrderSchedule × N             livrare eșalonată: data cerută, cantitate; data și cantitatea confirmate
   ├─ PurchaseOrderRevision × N                  instantaneul versiunii anterioare, motivul, dacă a cerut reaprobare
   ├─ PurchaseOrderConfirmation × N (jurnal)     ce a confirmat furnizorul, când, din ce document
   ├─ PurchaseApproval × N (jurnal, comun cu A2)
   └─ legături (Nucleu core.DocumentLink)
        ← ORDER   din linia de necesar (CF-NC) sau de ofertă (CF-OF)
        → DELIVER aviz furnizor (A4) · RECEIVE NIR (A4) · INVOICE factură (A5)
```

Reguli de business ale modelului:

1. **O comandă are un singur furnizor, o singură monedă și o singură gestiune de livrare** (A2 decizia 8). Liniile pot avea centru de cost și proiect diferite (servicii, consumabile).
2. **Fiecare linie are cel puțin o livrare** (`PurchaseOrderSchedule`); suma cantităților livrărilor = cantitatea liniei (`53207`). O linie simplă are o singură livrare, creată automat cu data cerută din antet. Livrările nu sunt documente: NIR-ul consumă linia (`RECEIVE`), iar cantitatea primită se repartizează pe livrări în ordinea datelor, doar pentru urmărire și pentru indicatorii din A7.
3. **Ce angajează firma e comanda trimisă.** Până la trimitere comanda e internă și se poate modifica liber (cu reaprobare, dacă a fost aprobată). După trimitere orice modificare creează o revizie și se retrimite furnizorului (4.6).
4. **Confirmarea nu schimbă comanda, o completează.** Cantitatea, prețul și data confirmate stau lângă cele cerute. Diferențele se decid explicit de achizitor (4.5): se acceptă (comanda se aliniază printr-o revizie) sau se contestă. Disponibilul proiectat folosește doar ce e confirmat.
5. **„De primit” și „de facturat” sunt independente** (A1 decizia 5). Comanda se închide când toate liniile sunt primite sau închise manual; facturarea rămâne vizibilă separat până la ultima factură.
6. **Liniile deja preluate nu pierd ce s-a preluat.** Cantitatea unei linii nu scade sub cantitatea legată în aval (aviz, NIR, factură; Nucleu `50028`, aici `53205`). Articolul și UM-ul unei linii generate din sursă nu se schimbă (Nucleu §3.6.4).
7. **Nimic nu se șterge după numerotare** (`SQL-20`): o comandă numerotată se anulează (cu motiv) sau se închide; o linie se închide, nu se șterge.

## 3. Entități, stări și setări

Convențiile comune tabelelor `purch` sunt cele din A1 §3. Numărul vine din seria `CF` (`core.NumberSeries`) și se alocă la trimiterea spre aprobare (sau la aprobarea automată), nu la ciornă.

### 3.1 Antetul (`PurchaseOrder`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `Number`, `DocDate` | | — |
| `TransactionTypeId` | `int` | `CF-MAN`, `CF-NC`, `CF-OF` sau o variantă a lor |
| `PartnerId` | `int` | Furnizor (`53002`), nu blocat în firmă (`53010`, A1 §3.4) |
| `BuyerUserId` | `int` | Implicit cel din `SupplierTerms` sau utilizatorul curent |
| `CurrencyCode` | `char(3)` | Implicit din `SupplierTerms`; nu se schimbă după prima linie preluată din sursă (`53210`) |
| `PaymentTermDays`, `AdvancePct` | `smallint`, `decimal(9,4)` | Termenul de plată (din `SupplierTerms`) și procentul de avans cerut de furnizor (0 = fără avans; avansul se plătește pe proformă, A5) |
| `Incoterm`, `IncotermPlace` | | Din `SupplierTerms` sau din oferta acceptată; `Incoterm` e FK spre `core.DeliveryTerm` (precizare din Achiziții A6, 27.11.2026) |
| `TransportModeCode` | | Din `SupplierTerms`; FK spre `core.TransportMode` (precizare din Achiziții A6, 27.11.2026) |
| `DeliveryWarehouseId` | `int` | Gestiunea de livrare, a firmei, activă, fără blocare la intrare (`53212`). Adresa de livrare se ia din locația gestiunii |
| `RequestedDate` | `date` | Data cerută propusă pe livrări: `DocDate` + termenul furnizorului (catalog sau `SupplierTerms`) |
| `SupplierEmail`, `Language` | | Adresa la care pleacă (`SupplierTerms.OrderEmail` sau contactul cu rolul „comenzi”, Nucleu) și limba PDF-ului (`ro`, `en`) |
| `NoteToSupplier`, `InternalNote` | `nvarchar(1000)` | Prima apare pe PDF |
| `Status` | `tinyint` | 3.2 |
| `Revision` | `smallint` | 0 până la prima trimitere; crește la fiecare revizie trimisă (4.6) |
| `ConfirmationRequired` | `bit` | Copiat la creare din `SupplierTerms.ConfirmationRequired` → `PurchaseSettings.OrderConfirmationRequired` (3.4) |
| `SupplierConfirmationRef`, `ConfirmedAt` | | Numărul și data ultimei confirmări primite |
| `NetAmount`, `VatAmount`, `TotalAmount`, `NetAmountRon` | `decimal(19,4)` | Calculate de handler (`SP-13`); `NetAmountRon` la cursul zilei comenzii, pentru praguri |
| `ApprovedNetAmountRon` | `decimal(19,4) NULL` | Valoarea la ultima aprobare; baza reaprobării (4.6) |
| `RequiredLevel`, `CurrentLevel` | `tinyint` | Ca la referat (A2 §3.1) |
| `SentAt`, `SentBy`, `ClosedAt`, `CancelledAt`, `CancelReason` | | — |

### 3.2 Stările comenzii

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează liber | → De aprobat; → Aprobată direct, dacă valoarea e sub pragul nivelului 1 (aprobare automată, A2 §3.2 regula 1); → ștergere fizică, dacă n-a fost numerotată |
| 2 De aprobat | Blocată la editare (`53201`) | → Aprobată, → Ciornă (retrasă sau returnată), → Anulată (respinsă) |
| 5 Aprobată | Se poate trimite; o modificare o readuce în ciornă | → Trimisă, → Ciornă, → Anulată |
| 6 Trimisă | Așteaptă confirmarea; modificările creează revizii | → Confirmată (confirmare înregistrată sau, fără confirmare cerută, automat la trimitere), → Anulată (doar fără nimic preluat în aval, `53206`) |
| 3 Confirmată | Se recepționează și se facturează; modificările creează revizii | → Închisă, → Anulată (doar fără nimic preluat) |
| 4 Închisă | Toate liniile primite (`RECEIVE` consumat, cu toleranța tranzacției) sau închise manual. Facturarea poate continua | → Confirmată, la redeschiderea unei linii închise manual (`ReopenedAt` în Nucleu) |
| 9 Anulată | Finală, cu motiv; dacă a fost trimisă, furnizorul primește emailul de anulare | — |

Pe lângă stare, lista arată calculat: „parțial primită”, „primită integral, nefacturată”, „întârziată” (o livrare confirmată sau cerută cu data depășită și rest de primit) și „neconfirmată de X zile”.

### 3.3 Linia (`PurchaseOrderLine`) și livrările (`PurchaseOrderSchedule`)

**Linie**

| Câmp | Reguli |
| --- | --- |
| `LineNo`, `ItemVariantId`, `UomId` | Varianta obligatorie (`53202`). UM-ul implicit e cel de cumpărare din catalogul furnizorului (A1 §3.5), altfel UM-ul de cumpărare al setului (Nucleu) |
| `Qty`, `QtyBase` | Cantitatea comandată; rotunjită la multiplul din catalog, cu avertizare sub minim (A1 §3.5) |
| `UnitPrice`, `DiscountPct`, `PriceSource`, `PriceListLineId` | Propuse de `ISupplierPriceProvider`; sursa: 1 Contract (rezervat) · 2 Listă · 3 Ultimul preț · 4 Ofertă acceptată · 5 Manual. Un preț manual mai mare decât cel propus peste `PriceVarianceTolerancePct` (A1 §3.7) cere motiv (`53204`), afișat aprobatorului |
| `VatRateId` | Cota din articol (Nucleu), modificabilă doar cu dreptul `Purch.Order.EditVat` (taxare inversă, scutiri) |
| `NetAmount` | Calculat (`SP-13`) |
| `SupplierItemCode` | Codul furnizorului din catalog, copiat la creare pentru PDF |
| `CostCenterId`, `ProjectId` | Propuse din referat (prin necesar) sau din regula de potrivire a furnizorului; obligatorii pe serviciile cu `AllowCostCenter` pe tranzacție |
| `ConfirmedUnitPrice`, `ConfirmationState` | Prețul confirmat; starea: 0 Neconfirmată · 1 Confirmată conform · 2 Confirmată cu diferențe acceptate · 3 Diferențe de decis |
| `PriceReason` | Motivul prețului manual (`53204`) |

**Livrare (`PurchaseOrderSchedule`)**: `LineId`, `Seq`, `RequestedDate`, `Qty` (UM liniei), `ConfirmedDate`, `ConfirmedQty`, `ReceivedQty` (calculat la citire din legăturile `RECEIVE`, repartizat în ordinea datelor; nu se stochează). Data așteptată = `ConfirmedDate` ?? `RequestedDate`.

**Ce citește Stocuri S5** prin `IPurchaseOrderSupply` (A1 §6), pe fiecare livrare cu rest: cantitatea confirmată rămasă la data confirmată (`IsConfirmed = 1`) și, separat, cantitatea neconfirmată rămasă la data cerută (`IsConfirmed = 0`). S5 folosește doar primele; necesarul din A2 le folosește pe amândouă (A2 regula 5). Pe o comandă fără confirmare cerută, trimiterea marchează toate livrările confirmate, cu datele cerute.

### 3.4 Setările A3 (completare la `PurchaseSettings`, A1 §3.7, și la `SupplierTerms`, A1 §3.4)

| Setare | Unde | Implicit | Ce înseamnă |
| --- | --- | --- | --- |
| `OrderConfirmationRequired` | Firmă; suprascrisă de `SupplierTerms.ConfirmationRequired` | 0 | 0: trimiterea ține loc de confirmare. 1: comanda așteaptă confirmarea furnizorului și, până atunci, nu intră în disponibilul proiectat (decizia 3) |
| `OrderConfirmationReminderDays` | Firmă | 2 | După câte zile lucrătoare fără confirmare pleacă reamintirea automată către furnizor |
| `OverdueReminderDays` | Firmă | 1 | După câte zile de întârziere pleacă emailul de întârziere către furnizor și notificarea către achizitor și solicitant |
| `OrderReapprovalTolerancePct` | Firmă | 0 | Creșterea valorii față de valoarea aprobată peste care comanda revine la aprobare (0 = orice creștere) |
| `OrderSendMode` | Firmă | 1 | 1 Trimitere manuală (butonul „Trimite”) · 2 Trimitere automată la aprobare, dacă furnizorul are adresă de email |
| `AutoEmailSupplier` | Furnizor (`SupplierTerms`) | 1 | Reamintirile și emailurile de întârziere pleacă automat către acest furnizor (0 = doar notificări interne) |
| `OrderPdfTemplateId` | Firmă (A1) | livrat | Șablonul PDF; tab-ul Configurare › Achiziții › Tipărire |

### 3.5 Revizia (`PurchaseOrderRevision`) și jurnalul confirmărilor (`PurchaseOrderConfirmation`)

**Revizie**: `OrderId`, `Revision` (numărul versiunii înlocuite), `ChangedAt`, `ChangedBy`, `Reason`, `SnapshotJson` (antetul, liniile și livrările versiunii anterioare; `SQL-10`), `NetAmountRonBefore`, `NetAmountRonAfter`, `RequiredReapproval`, `SentAt`. Doar adăugare (`SP-21`). Ecranul „Revizii” compară două versiuni câmp cu câmp (`a3-05`).

**Confirmare**: `OrderId`, `LineId`, `ScheduleId`, `ConfirmedQty`, `ConfirmedUnitPrice`, `ConfirmedDate`, `Source` (1 Manual, din emailul furnizorului · 2 Excel de confirmare · 3 EDI sau portal, viitor), `SupplierRef`, `AttachmentId`, `RecordedAt`, `RecordedBy`, `Decision` (1 Acceptată · 2 Contestată), `DecisionReason`. Doar adăugare; valorile curente de pe linie și livrare sunt cele ale ultimei confirmări acceptate.

## 4. Fluxuri principale

### 4.1 Crearea comenzii

**Din oferta acceptată (`CF-OF`).** Din ecranul de comparare (A2 §4.5) sau din oferta acceptată: câte o comandă pe ofertă, cu liniile acceptate, cantitatea atribuită, prețul și reducerea din ofertă (`PriceSource = 4`), termenul de plată, Incoterm-ul și costul de transport din ofertă ca linie separată pe articolul de serviciu „Transport” al firmei (dacă `FreightAmount` > 0). Exemplu: `CF 2297` către PackPro Logistic SRL, din `OF 0192`: `FOLIE23` 480 role la 36,90, `ETT100` 720 role la 17,40 și `TRANSP` 1 × 250,00; 30.490,00 lei fără TVA, 36.892,90 lei cu TVA 21%. `CF 2298` către Furnizor Ambalaje SRL: `BANDA48` 960 role la 4,10, 3.936,00 lei.

**Din necesar (`CF-NC`).** Gruparea pe furnizor × monedă × gestiune (A2 §4.3). Prețul se repropune la data comenzii pe cantitatea finală. Din `NC 0418`: `CF 2299` Izvorul Carpaților în DEP-CJ (`APA05` 9.852 la 1,1600 și `APA15` 2.952 la 1,9900; 17.302,80 lei), `CF 2300` Izvorul Carpaților în DC-PL (`SUC033` 1.512 la 2,4500; 3.704,40 lei), `CF 2301` Office Line în DEP-CJ (`MANUSI`, `CUTTER`, `VESTA`; 629,00 lei, cu centrul de cost Logistică Cluj preluat din referat).

**Manual (`CF-MAN`).** Achizitorul alege furnizorul; propunerea de linii „Variante sub minim la acest furnizor” (din ultimul necesar) e opțională. Prețul vine din `ISupplierPriceProvider`.

La salvare se verifică: furnizorul nu e blocat (`53010`), gestiunea e validă (`53212`), fiecare linie are variantă (`53202`) și livrări cu suma corectă (`53207`), prețurile manuale peste toleranță au motiv (`53204`). Avertizări (nu blocări): valoare sub `MinOrderAmount` al furnizorului, cantitate sub minimul din catalog, furnizor inactiv ANAF, termen de plată peste 60 de zile (Legea 72/2013), linie fără preț.

### 4.2 Aprobarea

1. „Trimite la aprobare” alocă numărul și calculează planul (`IPurchaseApprovalService.PlanAsync`, A2 §6) pe `NetAmountRon`. Regulile livrate pentru comenzi (validate cu ecranul `a2-06`): nivelul 1 de la 0 lei (Șef achiziții), nivelul 2 de la 50.000 lei (Director financiar), nivelul 3 de la 250.000 lei (2 din Director general și Director financiar). Firma poate ridica pragul nivelului 1, ca micile comenzi să se aprobe automat.
2. Aprobatorul vede, pe lângă linii: sursa fiecărui preț și abaterea față de listă sau ultimul preț, motivul prețurilor manuale, referatele de la origine (drumul prin necesar) și, pentru `CF-OF`, instantaneul atribuirii (ofertele primite, prețurile comparabile, motivul alegerii, A2 §3.5.1). Ecranul `a3-02`.
3. Aprobatorul aprobă, returnează sau respinge (`DecidePurchaseApproval`, comun cu A2). Spre deosebire de referat, **nu modifică liniile comenzii**: le returnează cu comentariu, iar achizitorul le schimbă. Comanda e un angajament față de furnizor, deci forma ei finală trebuie să fie a achizitorului.
4. La ultimul nivel: starea 5 Aprobată, `ApprovedNetAmountRon` = valoarea curentă. Cu `OrderSendMode = 2`, comanda pleacă imediat (4.3).

Exemplu: `CF 2297` (30.490,00 lei) cere doar nivelul 1. Laura Vasile e autoarea, Dan Toma aprobă pe 26.11, la 17:50.

### 4.3 Trimiterea

1. „Trimite” (`SendPurchaseOrder`, dreptul `Purch.Order.Send`): se generează PDF-ul din șablon (`OrderPdfTemplateId`) în limba comenzii, cu numărul, revizia, datele firmei, adresa de livrare, liniile cu codul nostru și codul furnizorului (`SupplierItemCode`), cantitatea, UM-ul, prețul, reducerea, data cerută pe fiecare livrare, termenul de plată, Incoterm-ul, nota către furnizor și, opțional, un Excel de confirmare (aceeași tehnică semnată ca Excelul de răspuns din A2 §3.4).
2. Emailul (subiect „Comanda CF 2297 · Exemplu Distribuție SRL”, text din șablonul Nucleu, PDF atașat) se scrie în outbox în aceeași tranzacție cu trecerea în starea 6 Trimisă (`SP-15`); pleacă după commit. Răspunsul ajunge la adresa achizitorului. Ecranul `a3-03`.
3. Fără confirmare cerută (`ConfirmationRequired = 0`), comanda trece direct în 3 Confirmată, cu livrările confirmate la datele cerute, și intră în disponibilul proiectat.
4. Pentru `OrderChannel = 3` (telefon), „Trimite” doar marchează comanda trimisă, cu nota „transmisă telefonic” și PDF-ul arhivat.
5. Se publică `ComandaFurnizorEmisa`; solicitanții referatelor de la origine primesc notificare (A2 §5.2).

### 4.4 Confirmarea furnizorului

Furnizorul răspunde pe email (PDF propriu, text sau Excelul de confirmare). Achizitorul deschide „Confirmare” pe comandă (`a3-04`): pe fiecare livrare, cantitatea, prețul și data cerute, alături de câmpurile confirmate, propuse egale cu cele cerute. Excelul de confirmare se importă direct.

Exemplu `CF 2299` (trimisă pe 26.11, cerută pentru 29.11): Izvorul Carpaților confirmă `APA05` 9.600 buc (10 paleți întregi, nu 9.852) pe 01.12 și `APA15` 2.952 buc pe 01.12, la aceleași prețuri.

1. Liniile identice cu cererea se marchează „Confirmată conform”.
2. Liniile cu diferențe se marchează „Diferențe de decis” și trebuie decise înainte de salvare (`53211`):
   - **cantitate mai mică**: se acceptă, și restul (252 buc) fie se închide (`DocumentLineClosure`, motiv „furnizorul livrează doar paleți întregi”), fie rămâne ca restanță pe o livrare nouă, cu data dată de furnizor sau goală (neconfirmată);
   - **dată mai târzie**: se acceptă (livrarea primește data confirmată; solicitanții și, pentru variantele cu cereri de vânzare acoperite din această comandă, Vânzările primesc notificare prin `ComandaFurnizorConfirmata`) sau se contestă (rămâne neconfirmată, cu reamintire);
   - **preț diferit**: în toleranța `PriceVarianceTolerancePct` / `PriceVarianceToleranceAmount` (A1 §3.7) se acceptă direct; peste ea, acceptarea creează o revizie (4.6), care poate cere reaprobare.
3. La salvare: jurnalul `PurchaseOrderConfirmation`, valorile curente pe linii și livrări, starea 3 Confirmată, evenimentul `ComandaFurnizorConfirmata`. Disponibilul proiectat vede de acum 9.600 buc `APA05` pe 01.12 (nu 9.852 pe 29.11).

O confirmare nouă (furnizorul schimbă din nou data) se înregistrează la fel; jurnalul păstrează toate versiunile, iar A7 măsoară cât de des își schimbă furnizorul confirmările.

### 4.5 Urmărirea livrărilor

Lista „Comenzi” (`a3-01`) are vederile: De aprobat · De trimis · Neconfirmate · De primit săptămâna aceasta · Întârziate · Primite nefacturate · Toate. Pe fiecare comandă: furnizorul, valoarea, data așteptată a următoarei livrări, procentul primit și facturat, alertele.

Jobul zilnic `PurchaseOrderFollowUp` (Nucleu, joburi programate), pe fiecare firmă:

1. Comenzile trimise și neconfirmate după `OrderConfirmationReminderDays` zile lucrătoare: email de reamintire către furnizor (dacă `AutoEmailSupplier = 1`) și notificare către achizitor.
2. Livrările cu data așteptată depășită de `OverdueReminderDays` zile și cu rest de primit: email de întârziere către furnizor, notificare către achizitor și către solicitanții referatelor legate; comanda apare în „Întârziate”.
3. Livrările care scad sub minimul de stoc al variantei înainte de data așteptată (din proiectatul S5): notificare „cere devansarea” către achizitor (A2 §4.2).

Emailurile trimise automat se văd în tab-ul Istoric al comenzii.

### 4.6 Modificarea după aprobare sau trimitere (revizii)

| Ce se schimbă | Înainte de trimitere (Aprobată) | După trimitere (Trimisă, Confirmată) |
| --- | --- | --- |
| Data unei livrări, nota, adresa de email | Rămâne aprobată | Revizie, retrimisă; fără reaprobare |
| Cantitate sau preț în jos, linie închisă | Rămâne aprobată | Revizie, retrimisă; fără reaprobare |
| Cantitate sau preț în sus, linie nouă | Revine în ciornă; la retrimitere, aprobare de la nivelul 1 | Revizie; dacă `NetAmountRon` nou > `ApprovedNetAmountRon` × (1 + `OrderReapprovalTolerancePct` / 100), comanda trece în „De aprobat” (nivelurile se recalculează pe valoarea nouă) și se retrimite doar după aprobare. Până atunci, furnizorul lucrează pe revizia anterioară |
| Furnizor, monedă, gestiune | Nu se schimbă (se anulează și se face o comandă nouă) | Nu se schimbă |

Cantitatea unei linii nu scade sub ce e deja preluat în aval (`53205`). Fiecare revizie trimisă primește numărul următor, iar PDF-ul poartă mențiunea „Revizia N, înlocuiește revizia N−1 din data...”.

Exemplu (ecranul `a3-05`): `CF 2291` (Izvorul Carpaților, `APA05` 4.800 buc la 1,1800 = 5.664,00 lei) a fost aprobată pe 20.11 și trimisă. Revizia 1 (24.11): data mutată de pe 30.11 pe 02.12, la cererea furnizorului, fără reaprobare. Revizia 2 (26.11): cantitatea crește la 5.760 buc (6.796,80 lei); creșterea depășește toleranța 0%, deci comanda revine „De aprobat” la nivelul 1 (Dan Toma), iar furnizorul vede încă revizia 1 până la aprobare.

### 4.7 Închiderea și anularea

- **Liniile de servicii** (precizare din A4 §4.9): se socotesc primite când sunt facturate integral sau confirmate pe un NIR de prestare.
- **Închiderea automată**: la validarea NIR-ului care consumă ultimul rest pe toate liniile (cu toleranța tranzacției NIR), sau care lasă pe o linie un rest sub `UnderDeliveryAutoClosePct` (A1 §3.7): linia se închide automat, cu motivul „sub-livrare în toleranță”.
- **Închiderea manuală a restului**: pe linie sau pe toată comanda, cu motiv (`DocumentLineClosure`); restul dispare din disponibilul proiectat și din necesar. Furnizorul poate primi emailul „restul comenzii se anulează” (opțional).
- **Anularea**: din ciornă, „De aprobat” sau „Aprobată”, liberă; din „Trimisă” sau „Confirmată”, doar dacă nicio linie nu are legături active sau în ciornă în aval (`53206`); altfel se închide restul. Comanda trimisă și anulată trimite automat emailul de anulare. Legăturile `ORDER` către necesar și ofertă se dezactivează, deci liniile de necesar redevin disponibile.

### 4.8 Angajamente pentru prognoza de trezorerie (completare la F5)

`IPurchaseCommitments` (6) întoarce, pentru comenzile confirmate (sau trimise, cu semnul „neconfirmat”), valoarea **comandată și încă nefacturată**, cu TVA, în moneda comenzii și în lei, la data estimată a plății: data așteptată a livrării + `PaymentTermDays` (pentru avans: data comenzii, cu `AdvancePct`). F5 o adaugă în prognoză ca rând nou „Comenzi la furnizor (angajamente)”, categoria Plăți furnizori. Odată facturată, suma trece în partidele F3 și dispare din angajamente, deci nu se numără de două ori (decizia 9).

### 4.9 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Furnizorul livrează fără să fi confirmat | NIR-ul se poate face din comanda trimisă: sursele tranzacțiilor `NIR-CF` și `AV-CF` acceptă stările Trimisă și Confirmată (precizare la A1 §3.2); livrarea se consideră confirmată la data recepției |
| Furnizorul livrează alt cod decât cel comandat (substituție) | NIR-ul nu poate schimba articolul liniei generate; se recepționează ca linie manuală (dacă tranzacția permite) și linia comenzii se închide sau rămâne deschisă (A4) |
| Comanda în EUR, cursul se schimbă între aprobare și recepție | Pragul de aprobare se calculează la cursul zilei comenzii; diferența de curs nu cere reaprobare |
| Prețul din lista de prețuri expiră între comandă și livrare | Prețul comenzii rămâne; factura se compară cu prețul comenzii (A5) |
| Două livrări cerute, furnizorul aduce totul odată | NIR-ul consumă linia; repartizarea pe livrări marchează ambele primite, a doua „mai devreme” (A7) |
| Comanda din ofertă depășește cantitatea atribuită | Refuzat peste toleranța tranzacției `CF-OF` (Nucleu `50027`) |
| Furnizorul devine blocat cu comenzi deschise | Comenzile rămân; se poate recepționa și factura; o revizie cu creștere e refuzată (`53010`) |
| Utilizatorul care a trimis comanda pleacă din firmă | Comanda trece la achizitorul furnizorului (`BuyerAssignment`); răspunsurile vin la adresa generică a achizițiilor, dacă e configurată |
| Comandă de servicii (transport lunar, mentenanță) | Linia pe un articol nestocabil, cu centru de cost; „recepția” e o confirmare de prestare (A4, NIR fără efect în stoc) sau direct factura `FF-CF` (A5) |
| Comandă cu avans | `AdvancePct` > 0; proforma și plata avansului se leagă de comandă (A5, F3); NIR-ul nu așteaptă plata |

**Jurnalul de excepții (precizare din Achiziții A7, 27.11.2026).** Excepțiile aprobate în această parte se scriu în `purch.ExceptionLog` prin `IPurchaseExceptionWriter.WriteAsync`, în aceeași tranzacție cu documentul care le aprobă (A7 §3.5): prețul de pe comandă peste lista de prețuri sau introdus manual, aprobat; revizia unei comenzi după confirmarea furnizorului. Se scriu documentul, linia, furnizorul, valoarea în lei, cine a cerut, cine a aprobat și motivul.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `ComandaFurnizorEmisa` v1 | Prima trimitere | `CompanyId`, `OrderId`, `Number`, `PartnerId`, `CurrencyCode`, `NetAmount`, `NetAmountRon`, `DeliveryWarehouseId`, liniile (variantă, cantitate de bază, livrări cu date) | A2 (notifică solicitanții), MRP, Finanțe F5 (angajamente), Raportare |
| `ComandaFurnizorConfirmata` v1 | Confirmare salvată | `OrderId`, liniile cu cantitatea și data confirmate, diferențele | MRP, Vânzări (termene promise clienților din proiectat), Raportare (A7) |
| `ComandaFurnizorModificata` v1 | Revizie trimisă | `OrderId`, `Revision`, liniile schimbate, `NetAmountRonBefore`, `NetAmountRonAfter` | MRP, F5, Raportare |
| `ComandaFurnizorInchisa` v1 | Închidere sau anulare | `OrderId`, `Reason` (1 primită integral, 2 rest închis, 3 anulată), cantitățile închise | MRP, F5, Raportare |

### 5.2 Consumate

| Eveniment | De la | Ce face A3 |
| --- | --- | --- |
| `ReceptieNIRValidata` v1 | A4 | Recalculează starea comenzilor sursă (închiderea automată, 4.7); marchează livrările primite |
| `FacturaFurnizorValidata` v1 | A5 | Actualizează „de facturat” în listă (calculat, doar invalidarea cache-ului de listă) |
| `FurnizorBlocat` v1 | A1 | Notifică achizitorul despre comenzile deschise ale furnizorului |
| `UtilizatorDezactivat` v1 | Nucleu | Mută comenzile deschise ale achizitorului la achizitorul furnizorului (A1 §3.8) |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A3)

```csharp
public enum PurchaseOrderStatus : byte { Draft = 1, PendingApproval = 2, Confirmed = 3, Closed = 4, Approved = 5, Sent = 6, Cancelled = 9 }
public enum OrderLineConfirmation : byte { None = 0, AsOrdered = 1, AcceptedWithDifferences = 2, DifferencesPending = 3 }
public enum OrderPriceSource : byte { Contract = 1, PriceList = 2, LastPrice = 3, Quotation = 4, Manual = 5 }

public sealed record PurchaseOrderSummaryDto(long Id, string Number, int PartnerId, string CurrencyCode,
    PurchaseOrderStatus Status, short Revision, decimal NetAmount, decimal NetAmountRon,
    DateOnly? NextExpectedDate, decimal ReceivedPct, decimal InvoicedPct, bool IsOverdue);

// Citit de F5 (prognoza de trezorerie)
public interface IPurchaseCommitments
{
    Task<IReadOnlyList<PurchaseCommitmentRow>> GetOpenAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);
}
public sealed record PurchaseCommitmentRow(long OrderId, string Number, int PartnerId, string CurrencyCode,
    decimal AmountWithVat, decimal AmountWithVatRon, DateOnly ExpectedPaymentDate, bool IsAdvance, bool IsConfirmed);

// Citit de A4 (NIR, aviz) și A5 (factură), pe lângă ILinkableDocumentSource din Nucleu
public interface IPurchaseOrderReader
{
    Task<PurchaseOrderSummaryDto?> GetAsync(long orderId, CancellationToken ct);
    Task<IReadOnlyList<PurchaseOrderLineForReceiptDto>> GetLinesForReceiptAsync(long orderId, CancellationToken ct);
}
public sealed record PurchaseOrderLineForReceiptDto(long LineId, int ItemVariantId, int UomId, decimal Qty,
    decimal UnitPrice, decimal DiscountPct, int VatRateId, int? CostCenterId, int? ProjectId, string? SupplierItemCode);
```

`IPurchaseOrderSupply` (A1 §6) își precizează aici sursa: un rând pe livrare cu rest, cu `IsConfirmed` după 3.3. Implementarea `ILinkableDocumentSource` pentru `PurchaseOrder` expune cantitatea liniei (`QtyBase`) și filtrele `SAME_SUPPLIER`, `SAME_WAREHOUSE`, `SERVICE_LINES_ONLY`, `STOCK_LINES_ONLY`.

Comenzi interne A3 (MediatR): `SavePurchaseOrder`, `SubmitPurchaseOrder`, `WithdrawPurchaseOrder`, `DecidePurchaseApproval` (comun cu A2), `SendPurchaseOrder`, `RecordOrderConfirmation`, `ImportOrderConfirmation`, `DecideConfirmationDifferences`, `RevisePurchaseOrder`, `ClosePurchaseOrderLine`, `ReopenPurchaseOrderLine`, `CancelPurchaseOrder`, `RunPurchaseOrderFollowUp` (job). Generarea (`GenerateFromSourcesCommand<PurchaseOrder>` cu `CF-NC`, `CF-OF`) folosește comportamentele `PurchaseOrder.FromNeed` și `PurchaseOrder.FromQuotation`. Interogări: `ListPurchaseOrders` (vederile din 4.5), `GetPurchaseOrderRevisionDiff`.

## 7. Schema SQL `purch` (partea A3)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.PurchaseOrder` | `Id bigint` | UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `CompanyId`, `TransactionTypeId`, `PartnerId`, `BuyerUserId`, `CurrencyCode`, `DeliveryWarehouseId` | `(CompanyId, Status) INCLUDE (PartnerId, NetAmountRon)`; `(PartnerId, Status)`; `(BuyerUserId, Status)` |
| `purch.PurchaseOrderLine` | `Id bigint` | UQ `(OrderId, LineNo)`; FK `OrderId`, `ItemVariantId`, `UomId`, `VatRateId`, `PriceListLineId`, `CostCenterId`, `ProjectId` | `(ItemVariantId) INCLUDE (OrderId, QtyBase)` |
| `purch.PurchaseOrderSchedule` | `Id bigint` | UQ `(LineId, Seq)`; FK `LineId` | `(ExpectedDate) INCLUDE (LineId, Qty, ConfirmedQty) WHERE IsOpen = 1` |
| `purch.PurchaseOrderRevision` | `Id bigint` | UQ `(OrderId, Revision)`; doar adăugare (trigger, `SP-21`) | — |
| `purch.PurchaseOrderConfirmation` | `Id bigint` | FK `OrderId`, `LineId`, `ScheduleId`; doar adăugare | `(OrderId, RecordedAt)` |

`PurchaseOrderSchedule.ExpectedDate` e coloană calculată persistată `ISNULL(ConfirmedDate, RequestedDate)`; `IsOpen` e persistată, actualizată de procedurile de stare (comanda nu e închisă sau anulată, livrarea nu e închisă), ca jobul zilnic și `IPurchaseOrderSupply` să facă un singur seek.

Exemplu de tabel complet:

```sql
CREATE TABLE purch.PurchaseOrderSchedule (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrderSchedule PRIMARY KEY,
    LineId          bigint            NOT NULL CONSTRAINT FK_PurchaseOrderSchedule_Line REFERENCES purch.PurchaseOrderLine(Id),
    Seq             smallint          NOT NULL,
    RequestedDate   date              NOT NULL,
    Qty             decimal(19,6)     NOT NULL,
    ConfirmedDate   date              NULL,
    ConfirmedQty    decimal(19,6)     NULL,
    ExpectedDate    AS ISNULL(ConfirmedDate, RequestedDate) PERSISTED,
    IsOpen          bit               NOT NULL CONSTRAINT DF_PurchaseOrderSchedule_IsOpen DEFAULT (1),
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_PurchaseOrderSchedule_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy       int               NOT NULL,
    ModifiedAt      datetime2(3)      NULL,
    ModifiedBy      int               NULL,
    RowVersion      rowversion        NOT NULL,
    CONSTRAINT UQ_PurchaseOrderSchedule_Seq UNIQUE (LineId, Seq),
    CONSTRAINT CK_PurchaseOrderSchedule_Qty CHECK (Qty > 0 AND (ConfirmedQty IS NULL OR ConfirmedQty >= 0))
);

CREATE INDEX IX_PurchaseOrderSchedule_Expected ON purch.PurchaseOrderSchedule (ExpectedDate)
    INCLUDE (LineId, Qty, ConfirmedQty) WHERE IsOpen = 1;
```

Verificarea „Σ livrări = cantitatea liniei” (`53207`) se face în procedura de salvare, pe seturi, după scrierea liniilor și livrărilor din TVP-uri (`SP-07`), nu cu trigger.

Proceduri: `purch.usp_PurchaseOrder_Save` (antet, linii, livrări din TVP, cu `RowVersion`, `SP-18`), `purch.usp_PurchaseOrder_Submit`, `purch.usp_PurchaseApproval_Decide` (comun, A2), `purch.usp_PurchaseOrder_Send` (stare, outbox, confirmare automată), `purch.usp_PurchaseOrder_Confirm` (jurnal, valori curente, închiderea restului), `purch.usp_PurchaseOrder_Revise` (instantaneul anterior, recalculul reaprobării), `purch.usp_PurchaseOrder_Close`, `purch.usp_PurchaseOrder_Cancel` (verifică legăturile în aval, `53206`), `purch.usp_PurchaseOrder_FollowUp` (job, pe seturi, întoarce lista de emailuri și notificări de scris), `purch.usp_PurchaseOrder_List` (vederi, `SP-11`), `purch.usp_PurchaseCommitments_Open`, `purch.usp_PurchaseOrderSupply_Open` (A1, precizată aici).

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53201 | `PURCH_ORDER_NOT_EDITABLE` | 409 | Modificarea unei comenzi „De aprobat”, închise sau anulate |
| 53202 | `PURCH_ORDER_LINE_NO_VARIANT` | 400 | Linie fără variantă |
| 53203 | `PURCH_ORDER_NOT_APPROVED` | 409 | Trimiterea unei comenzi neaprobate |
| 53204 | `PURCH_ORDER_PRICE_REASON_REQUIRED` | 400 | Preț manual peste cel propus, peste toleranță, fără motiv |
| 53205 | `PURCH_ORDER_QTY_BELOW_LINKED` | 409 | Cantitatea liniei sub cantitatea deja preluată în aval |
| 53206 | `PURCH_ORDER_CANCEL_NOT_ALLOWED` | 409 | Anularea unei comenzi cu avize, NIR-uri sau facturi legate |
| 53207 | `PURCH_ORDER_SCHEDULE_MISMATCH` | 400 | Suma livrărilor diferă de cantitatea liniei |
| 53208 | `PURCH_ORDER_CONFIRMATION_INVALID` | 400 | Cantitate confirmată negativă sau dată confirmată înaintea datei comenzii |
| 53209 | `PURCH_ORDER_NO_RECIPIENT` | 409 | Trimitere pe email fără adresă la furnizor |
| 53210 | `PURCH_ORDER_HEADER_LOCKED` | 409 | Schimbarea furnizorului, monedei sau gestiunii după prima linie preluată sau după trimitere |
| 53211 | `PURCH_ORDER_DIFFERENCES_PENDING` | 409 | Salvarea confirmării cu diferențe nedecise |
| 53212 | `PURCH_ORDER_WAREHOUSE_INVALID` | 409 | Gestiune a altei firme, inactivă sau blocată la intrare |
| 53213 | `PURCH_ORDER_REAPPROVAL_PENDING` | 409 | Trimiterea unei revizii care așteaptă reaprobare |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.Order.View` | Comenzile firmei (fără prețuri pentru cine nu are `Purch.PriceList.View`, ca la recepția „oarbă”) |
| `Purch.Order.Manage` | Creare, modificare, revizii, confirmări, închiderea restului |
| `Purch.Order.Send` | Trimiterea către furnizor |
| `Purch.Order.Cancel` | Anularea |
| `Purch.Order.EditVat` | Schimbarea cotei de TVA pe linie |

Roluri livrate: **Achizitor** (`View`, `Manage`, `Send`), **Șef achiziții** (toate; aprobator de nivel 1), **Director financiar** (`View`; aprobator de nivel 2), **Gestionar** (`View`, fără prețuri dacă recepția e „oarbă”), **Contabil furnizori** (`View`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A3 |
| --- | --- |
| Codul civil (art. 1.182 și urm., formarea contractului) | Comanda trimisă e oferta firmei; confirmarea furnizorului e acceptarea. De aceea A3 păstrează versiunea trimisă (revizia) și confirmarea, cu documentul atașat |
| Legea 72/2013 (termene de plată între profesioniști) | Avertizare la termen de plată peste 60 de zile |
| Codul fiscal (TVA, taxare inversă, art. 331) | Cota de TVA pe linie din articol; taxarea inversă sau scutirea se aleg doar cu drept separat și se verifică la factură (A5, C4) |
| GDPR | Adresele de email ale persoanelor de contact se folosesc doar pentru comenzi și reamintiri |

Integrări externe: emailul (outbox Nucleu); EDI și portal de furnizori, viitor (`OrderChannel = 2`). Interne: A1 (catalog, prețuri, termeni), A2 (necesar, oferte, aprobări), A4 și A5 (consumă comanda), Stocuri S5 (`IPurchaseOrderSupply`), MRP, Vânzări (termene), F5 (`IPurchaseCommitments`).

**Completare la F5 (decizia 9, aplicată în F5 §4.4 pe 27.11.2026):** prognoza de cash-flow (F5 §4.4) primește componenta „Comenzi la furnizor (angajamente)”, din `IPurchaseCommitments`, la data estimată a plății (4.8). Implicit se includ doar comenzile confirmate; cele trimise neconfirmate apar pe un rând separat, „angajamente neconfirmate”, ca încasările incerte.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Comenzi deschise | Pe furnizor, achizitor, gestiune: valoarea comandată, primită, facturată, rămasă; următoarea livrare | Achizitor, Șef achiziții |
| Livrări întârziate | Livrările cu data depășită: zile de întârziere, cantitate, reamintiri trimise, referatele afectate | Achizitor, Gestionar |
| Comenzi neconfirmate | Trimise și neconfirmate, cu vechimea și reamintirile | Achizitor |
| Abateri de preț | Linii cu preț manual sau peste listă, cu motivul și aprobatorul | Șef achiziții, Director financiar |
| Revizii | Comenzile cu cele mai multe revizii și reaprobări, pe furnizor și achizitor | Șef achiziții |
| Angajamente | Valoarea comandată și nefacturată pe săptămâni de plată estimată (același calcul ca F5) | Director financiar |

Performanța furnizorilor (livrări la timp și complete, stabilitatea confirmărilor) e în A7.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a3-comenzi/](ecrane/a3-comenzi/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Comenzi (urmărire) | Lista cu vederile din 4.5, indicatorii (de aprobat, de trimis, neconfirmate, întârziate), procentul primit și facturat, alertele | `a3-01-comenzi-urmarire` |
| Comandă în aprobare | `CF 2297` văzută de Dan Toma: liniile cu sursa prețului, instantaneul atribuirii din A2, panoul de aprobare | `a3-02-comanda-aprobare` |
| Trimiterea comenzii | Previzualizarea PDF-ului `CF 2297` (cu codurile furnizorului) și emailul către furnizor | `a3-03-trimitere` |
| Confirmarea furnizorului | `CF 2299`: cerut și confirmat pe livrări, diferențele (cantitate, dată) și decizia pe fiecare | `a3-04-confirmare` |
| Revizii | `CF 2291`: istoricul reviziilor, comparația între versiuni, reaprobarea cerută de creșterea cantității | `a3-05-revizii` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A3-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A3-A1 | `CF-OF` din `OF 0192` creează `CF 2297` cu cele două linii acceptate, prețurile ofertei și linia de transport de 250,00; legăturile `ORDER` pe ofertă și pe necesar sunt scrise | Test de integrare (cu A2) |
| A3-A2 | `CF 2297` (30.490,00 lei) cere doar nivelul 1; o comandă de 60.000 lei cere nivelurile 1 și 2; autorul nu aprobă (`53103`) | Test de integrare |
| A3-A3 | Trimiterea scrie emailul în outbox în aceeași tranzacție cu starea; dacă tranzacția eșuează, nu pleacă niciun email | Test de integrare |
| A3-A4 | Fără confirmare cerută, comanda trimisă apare în `IPurchaseOrderSupply` cu `IsConfirmed = 1`; cu confirmare cerută, doar cu `IsConfirmed = 0` până la confirmare | Test de integrare (cu Stocuri S5) |
| A3-A5 | Confirmarea `CF 2299` cu 9.600 buc pe 01.12: fără decizie pe diferență e refuzată (`53211`); cu închiderea restului, proiectatul S5 vede 9.600 pe 01.12, iar 252 buc nu mai apar nicăieri | Test de integrare |
| A3-A6 | Revizia cu creștere peste toleranță (`CF 2291`, 5.664,00 → 6.796,80) readuce comanda la aprobare; trimiterea ei înainte de aprobare e refuzată (`53213`); revizia cu dată schimbată nu cere aprobare | Test de integrare |
| A3-A7 | Cantitatea unei linii nu scade sub cantitatea din NIR-uri și avize (`53205`); anularea unei comenzi cu NIR e refuzată (`53206`) | Test de integrare (cu A4) |
| A3-A8 | Suma livrărilor diferită de cantitatea liniei e refuzată (`53207`); un NIR care primește totul odată marchează ambele livrări primite | Test de integrare |
| A3-A9 | Jobul zilnic trimite o singură reamintire pe comandă și zi (idempotent la repornire) și nicio reamintire furnizorilor cu `AutoEmailSupplier = 0` | Test de integrare |
| A3-A10 | Închiderea automată: ultimul NIR pe `CF 2280` (1.932 buc, cu toleranța de 2%) închide comanda; o linie primită 99,5% se închide cu `UnderDeliveryAutoClosePct = 1` | Test de integrare (cu A4) |
| A3-A11 | `IPurchaseCommitments` întoarce comandat − facturat, cu TVA, la data livrării + termenul de plată; după factură suma dispare | Test de integrare (cu A5) |
| A3-A12 | Toate procedurile A3 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Test de integrare |

Criteriile A3-A4, A3-A5 și A3-A6 sunt critice: ele decid ce vede stocul proiectat și ce angajează firma față de furnizor.

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Vederea publicată** `purch.vw_OrderCommitment`: comenzile confirmate și nefacturate, pe centru de cost și cont de cheltuială; sursa coloanei „Angajat” din plan vs realizat. [BI5 §7](../13-raportare-bi/BI5-BUGETARE.md)
- **Controlul bugetar** la confirmarea comenzii: portul opțional `IBudgetCheck` (fără pachetul BI răspunde „fără buget”). Cu setarea `BudgetControlMode` = Avertisment, depășirea bugetului anual (realizat + angajat + comanda) cere un motiv; cu Aprobare, comanda merge la responsabilul centrului de cost. Nu se blochează niciodată. Exemplu: CF 2412 din 03.11.2026, depășire 19.800 lei. [BI5 §3.3](../13-raportare-bi/BI5-BUGETARE.md)

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Stările comenzii | Ciornă → De aprobat → Aprobată → Trimisă → Confirmată → Închisă, plus Anulată; „parțial primită” și „întârziată” sunt calculate, nu stări | 3.2 | Decis de Aurelian (27.11.2026) |
| 2 | Livrări eșalonate | Fiecare linie are una sau mai multe livrări cu dată și cantitate; NIR-ul consumă linia, iar primirea se repartizează pe livrări doar pentru urmărire | 2 regula 2, 3.3 | Decis de Aurelian (27.11.2026) |
| 3 | Confirmarea furnizorului | Implicit trimiterea ține loc de confirmare; pe firmă sau pe furnizor se poate cere confirmare explicită. Disponibilul proiectat folosește doar cantitățile și datele confirmate. NIR-ul se poate face și din comanda trimisă neconfirmată | 3.3, 3.4, 4.4, 4.9 | Decis de Aurelian (27.11.2026) |
| 4 | Diferențele din confirmare | Se decid explicit pe linie: restul se închide sau rămâne restanță; data mai târzie se acceptă sau se contestă; prețul peste toleranță trece prin revizie | 4.4 | Decis de Aurelian (27.11.2026) |
| 5 | Aprobarea comenzii | Regulile din A2 (0 / 50.000 / 250.000 lei, ca în ecranul `a2-06`); aprobatorul nu modifică liniile, doar aprobă, returnează sau respinge | 4.2 | Decis de Aurelian (27.11.2026) |
| 6 | Modificările după trimitere | Revizii numerotate, cu instantaneu și retrimitere; reaprobare doar când valoarea crește peste toleranța firmei (implicit 0%) | 3.5, 4.6 | Decis de Aurelian (27.11.2026) |
| 7 | Urmărirea | Job zilnic cu reamintiri automate către furnizor (neconfirmare după 2 zile, întârziere după 1 zi), oprite pe furnizor dacă se dorește, și notificări către achizitor și solicitanți | 4.5 | Decis de Aurelian (27.11.2026) |
| 8 | Închiderea | Automată la recepția completă (cu toleranța NIR-ului și sub-livrarea acceptată din A1); facturarea rămâne vizibilă separat. Anularea doar fără nimic preluat în aval | 4.7 | Decis de Aurelian (27.11.2026) |
| 9 | Angajamentele în prognoza de trezorerie | Comenzile confirmate și nefacturate intră în prognoza F5, la data livrării + termenul de plată (completare la F5) | 4.8, 9 | Decis de Aurelian (27.11.2026) |
| 10 | Transportul din ofertă | Devine o linie separată pe un articol de serviciu „Transport”, nu se împarte pe linii în comandă (repartizarea în cost se face la recepție, A6) | 4.1 | Decis de Aurelian (27.11.2026) |
| 11 | Contractul-cadru cu furnizorul | Amânat: prețurile negociate stau în listele de prețuri (A1), iar sursa „Contract” rămâne rezervată pentru o parte viitoare | 3.3 | Decis de Aurelian (27.11.2026) |

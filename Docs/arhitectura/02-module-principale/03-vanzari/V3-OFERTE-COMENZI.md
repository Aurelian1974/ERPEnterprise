# ERP · 02.03 Vânzări · V3 Oferte, comenzi și rezervări

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**, cu ecranele și toate cele 13 decizii („1-13 DA, ecrane validate”); precizările din 9 au fost aplicate în V1, V2, Stocuri S5 și Finanțe F5 în aceeași zi. A treia din cele 7 părți ale modulului 02.03 Vânzări: [V1 Configurare](V1-CONFIGURARE.md) ✓ · [V2 Prețuri și discounturi](V2-PRETURI-DISCOUNTURI.md) ✓ · **V3 Oferte, comenzi și rezervări** ✓ · V4 Livrare · V5 Facturare și e-Factura · V6 Retururi și storno · V7 Rapoarte.

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`; V3 folosește `52200–52349`. Această parte acoperă documentele dinaintea livrării: **oferta către client** (cu revizii, alternative, linii opționale, valabilitate, acceptare și pierdere), **comanda de la client** (manuală, din ofertă sau importată), **confirmarea comenzii** (verificarea de credit, aprobarea pe valoare, rezervarea fermă a mărfii, data promisă calculată din disponibilul proiectat), **modificarea comenzii confirmate** prin revizii, **închiderea și anularea**, și **proforma** (integrală sau de avans), cu legarea încasării.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`); căutările fără diacritice folosesc coloane calculate cu `Latin1_General_100_CI_AI_SC_UTF8` (`SQL-25`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile** (`core.TransactionType`) și **legăturile între documente** (`core.DocumentLink`, cu grupul de consum și cantitatea pe linie) sunt în Nucleu §3.6. V1 §3.2 a definit tranzacțiile folosite aici: `OV-MAN` (ofertă), `CV-MAN`, `CV-OV`, `CV-IMP` (comenzi), `PF-MAN`, `PF-CV`, `PF-OV` (proforme). Grupurile de consum `ORDER` (cât din ofertă s-a comandat) și `PROFORMA` (cât s-a cerut în avans) sunt **informative**: arată restul, dar nu blochează depășirea. Comanda e consumată mai departe de `PREPARE` (dispoziția de livrare, V4), `DELIVER` (avizul, V4) și `INVOICE` (factura, V5).
- **Asistentul de generare** (Nucleu §3.6.4) e comun: alegi tranzacția, documentele sursă și liniile, cu cantitățile propuse din rest. Comanda din ofertă și proforma din comandă se fac cu el.
- **Liniile stau pe variantă** (`core.ItemVariant`, SKU-ul; Nucleu §3.3.1). Prețul, discounturile, marja și excepțiile de preț vin din motorul de preț V2 (`IPriceEngine`), cu aceleași reguli pe ofertă și pe comandă. **Prețurile se îngheață la confirmare** (V2 regula 8): oferta trimisă își păstrează prețurile pe toată valabilitatea, iar comanda din ofertă le copiază, chiar dacă lista s-a schimbat între timp.
- **Comenzile nu mișcă stocul.** Rezervă marfa prin portul Stocuri S5 (`IStockLedger.ReserveAsync` / `ReleaseAsync`): rezervare **fermă** la confirmarea comenzii, **provizorie** (opțional) pe ofertă. Cantitatea care nu are acoperire rămâne **în așteptare** și se alocă automat la prima intrare (S5 §3.3). Disponibilul proiectat (S5 §3.4) dă **data promisă**.
- **Verificarea de credit** (limită, restanțe, client blocat) e definită în V1 §4.3; V3 o aplică la confirmarea comenzii, cu semnul `CreditHold`. **Aprobarea** folosește tabelul `SalesApprovalRule` din V2 §3.9; V3 adaugă tipul 2, pragurile pe valoare.
- Stările comune ale documentelor de vânzare (V1): 1 Ciornă · 2 De aprobat · 3 Validat / Confirmată · 4 Închis · 8 Stornat · 9 Anulat. Oferta adaugă stările ei (3.2), ca la comanda la furnizor din Achiziții A3.
- **Ambalajele SGR.** Pe liniile cu articole care au ambalaj în sistemul de garanție-returnare (Nucleu `Item.DepositItemId`), documentul adaugă automat linia de garanție de 0,50 lei pe ambalaj, fără TVA (categoria `O`), legată de linia articolului. Exemplele numerice din acest document nu afișează liniile de garanție, pentru simplitate; ele nu schimbă valorile cu TVA ale exemplelor. Returul ambalajelor goale e în V6 §3.7 (precizare din Vânzări V6, 27.11.2026).
- **Ce NU face V3:** nu pregătește și nu livrează marfa (V4: dispoziția de livrare, avizul, e-Transport), nu emite facturi, nici factura de avans (V5), nu face retururi (V6), nu ține evidența oportunităților de vânzare înaintea ofertei (CRM, modul viitor) și nu calculează disponibilul (S5 îl calculează, V3 îl citește).

## 1. Scop și perimetru

| Zonă | Ce face V3 | Ce primește de la alții |
| --- | --- | --- |
| Oferta | Ofertă standard sau cadru (acord de cantitate și preț pe o perioadă), cu linii alternative și opționale, livrări cerute, valabilitate, aprobare pe valoare, trimitere pe email, revizii, acceptare totală sau parțială, pierdere cu motiv, expirare | Prețurile și excepțiile (V2), condițiile clientului (V1), adresa de email a contactului (Nucleu) |
| Cererea clientului | Cererea de ofertă primită (email, telefon, licitație) se înregistrează direct ca ofertă în ciornă, cu referința și data cererii | — |
| Comanda | Comandă manuală, din ofertă sau importată (Excel, CSV, EDI), cu punctul de livrare, referința clientului, livrări eșalonate pe linie, gestiunea de livrare | Catalogul clientului și potrivirea liniilor importate (V1 §4.4) |
| Confirmarea | Verificările de confirmare (referință, prețuri, credit, aprobare), rezervarea fermă pe orizontul de rezervare, data promisă pe livrare, confirmarea trimisă clientului (PDF sau EDI) | Expunerea și restanțele (V1 §4.3, F3), disponibilul proiectat și rezervările (S5) |
| Modificarea | Revizii ale ofertei trimise și ale comenzii confirmate, cu comparare și reaprobare peste toleranță | — |
| Închiderea | Închiderea liniilor (manual cu motiv sau automat la sub-livrare în toleranță), anularea, eliberarea rezervărilor | Livrările (V4), care consumă comanda |
| Proforma | Proformă integrală sau de avans, din comandă, din ofertă sau fără sursă; legarea încasării; blocarea livrării până la încasarea avansului | Încasările (F1, F2, evenimentul `IncasareInregistrata`); factura de avans o emite V5 |

## 2. Model de domeniu

```
 Cererea clientului (email, telefon) ─► SalesQuote (ciornă, cu referința cererii)
   SalesQuote ─► SalesQuoteLine × N (alternative pe grup, linii opționale) ─► SalesQuoteSchedule × N (livrări cerute)
      │ aprobare pe valoare (SalesApprovalRule tip 2) + excepții de preț (tip 1, V2)
      │ trimitere (SalesDocumentSend) · revizii (SalesDocumentRevision, instantaneu JSON)
      │ acceptare (cantitatea acceptată pe linie) · pierdere (ReasonCode tip 1) · expirare (job)
      ▼  CV-OV (consum ORDER, informativ)            PF-OV (consum PROFORMA)
   SalesOrder ─► SalesOrderLine × N ─► SalesOrderSchedule × N (data cerută, data promisă)
      ▲ CV-MAN (manual) · CV-IMP ◄─ SalesOrderImport ─► SalesOrderImportLine (potrivire prin catalogul clientului)
      │ confirmare: referință, prețuri, credit (CreditHold), aprobare, rezervare fermă (S5), data promisă (S5)
      │ revizii după confirmare · închidere linii (Nucleu DocumentLineClosure) · anulare
      ▼  PF-CV (consum PROFORMA)            DL-CV, AV-CV, FV-LIV (V4, V5)
   SalesProforma ─► SalesProformaLine × N ─► SalesProformaPayment × N (încasările legate) ─► AdvanceHold ridicat
```

Reguli de domeniu:

1. **Oferta și comanda folosesc același motor de preț și aceleași reguli de linie.** O linie are o variantă, o UM din setul articolului, o cantitate, prețul de listă, discounturile cu sursa lor (`sales.DocumentLineDiscount`, V2), prețul unitar, cota de TVA, valoarea și marja. Liniile de bonus în marfă sunt generate de motor și legate de linia sursă (V2 §3.6.1). Există și linii de **text** (fără articol, cantitate sau preț), pentru condiții sau descrieri pe PDF.
2. **Oferta trimisă e un angajament cu termen.** Prețurile și condițiile ofertei trimise sunt garantate până la `ValidUntil` (Codul civil, art. 1191: oferta cu termen e irevocabilă până la expirarea termenului). Orice schimbare după trimitere e o **revizie** numerotată, cu instantaneul versiunii anterioare, iar PDF-ul poartă „Revizia N, înlocuiește revizia N−1”. Oferta expirată nu se mai poate accepta sau transforma în comandă (`52200`), dar se poate prelungi printr-o revizie (cu prețurile recalculate la noua dată, la alegere, 4.3).
3. **O ofertă poate avea alternative și linii opționale.** Liniile cu același `AlternativeGroup` sunt variante între care clientul alege una (de exemplu aroma); prima e cea **principală** și singura inclusă în total. O linie **opțională** e propusă clientului fără să intre în total. La acceptare se alege cel mult o alternativă pe grup (`52217`) și se pot accepta opționalele, cu cantitatea acceptată.
4. **Aprobarea se cere o singură dată, pentru nivelul cel mai mare necesar**, din excepțiile de preț (V2, tipul 1) și din pragurile pe valoare și condiții (tipul 2, 3.8). Pe ofertă, valoarea comparată cu pragul e **valoarea maximă posibilă**: liniile principale, plus opționalele, plus cea mai scumpă alternativă din fiecare grup. **Comanda făcută dintr-o ofertă aprobată nu se reaprobă** dacă păstrează prețurile ofertei și valoarea ei nu depășește valoarea aprobată a ofertei (cu toleranța de reaprobare).
5. **Confirmarea comenzii e momentul angajamentului.** La confirmare, într-o singură tranzacție SQL: verificările (3.5), prețurile înghețate, rezervarea fermă (3.9), data promisă pe fiecare livrare, starea Confirmată, istoricul și evenimentul. Confirmarea către client e un PDF sau un mesaj EDI din comanda confirmată, nu un document separat. Acceptarea ofertei de către client prin comandă încheie contractul (Codul civil, art. 1196); confirmarea noastră e acceptarea comenzii lui.
6. **Comanda confirmată se rezervă ferm doar pe orizontul de rezervare.** Livrările cu data cerută în următoarele `ReservationHorizonDays` zile (implicit 14) se rezervă la confirmare; cele mai îndepărtate rămân cerere deschisă (`ISalesOrderDemand`, citită de S5 în disponibilul proiectat și de MRP) și se rezervă automat când intră în orizont (jobul zilnic `ReserveHorizon`). Așa o comandă eșalonată pe 3 luni nu blochează stocul de azi.
7. **Data promisă se calculează, nu se scrie.** Pentru fiecare livrare, data promisă e prima zi, de la data cerută încolo, în care disponibilul proiectat al gestiunii acoperă cantitatea, plus zilele de pregătire (`PickLeadDays`), aliniată la zilele de livrare ale punctului de livrare. Agentul poate muta data mai târziu oricând; mai devreme decât calculul, doar cu drept (`52218`). Data promisă se recalculează la fiecare confirmare și revizie, nu singură în fundal: o schimbare de disponibil după confirmare produce o **alertă de risc**, nu o nouă dată trimisă clientului.
8. **Ce s-a preluat în aval nu se mai schimbă.** Cantitatea unei linii nu scade sub ce e livrat sau în dispoziții de livrare (`52215`); clientul, moneda și gestiunea nu se schimbă după confirmare (`52214`): se anulează sau se închide restul și se face o comandă nouă.
9. **Proforma nu e document fiscal.** Nu se contează, nu trece prin e-Factura și nu afectează soldul clientului. Încasarea pe proformă e, în F3, o încasare nealocată (avans), pe care V5 o regularizează prin factura de avans (`FV-AVS`) sau o alocă pe factura finală. Proforma doar arată clientului cât să plătească și leagă plata de comandă.
10. **Toate documentele V3 au agentul lor și vizibilitatea din V1** (`SalesDataScope`): agentul vede ofertele și comenzile clienților din portofoliul lui, șeful de echipă pe ale echipei, șeful de vânzări pe toate.

## 3. Entități, stări și setări

Convențiile comune tabelelor `sales` sunt cele din V1 §3: `CompanyId` pe fiecare document, `RowVersion` pentru concurență optimistă, `CreatedAt/By`, `ModifiedAt/By`, istoricul stărilor în tabelul comun al Nucleului (`core.DocumentStatusHistory`). Numerele vin din seriile `OV`, `CV`, `PF` (`core.NumberSeries`) și se alocă la prima ieșire din ciornă (trimitere spre aprobare, trimitere sau confirmare), nu la salvarea ciornei.

### 3.1 Antetul ofertei (`SalesQuote`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `Number`, `DocDate` | | — |
| `TransactionTypeId` | `int` | `OV-MAN` sau o variantă a ei |
| `QuoteKind` | `tinyint` | 1 **Standard** · 2 **Cadru**: acord de cantitate și preț pe o perioadă (de exemplu un an), din care clientul comandă de mai multe ori (`CV-OV`, consumul `ORDER` arată cât s-a comandat). Cadrul nu face rezervare provizorie și poate avea valabilitate până la 366 de zile |
| `PartnerId` | `int` | Partener din Nucleu. Poate fi un **prospect** (partener fără rolul de client): oferta se trimite cu avertizare, iar comanda din ea cere rolul de client (`52001`, V1). Clientul blocat primește avertizare, nu blocare (V1 §3.4) |
| `ContactId`, `QuoteEmail` | | Persoana de contact și adresa la care pleacă oferta (implicit contactul cu rolul „oferte” sau „comenzi”, Nucleu) |
| `DeliveryPointId` | `int NULL` | Punctul de livrare (V1 §3.5); gol pentru prospect |
| `SalesAgentId`, `SalesChannelId` | `int` | Propuse din portofoliu și din condițiile clientului (V1) |
| `CustomerRfqRef`, `CustomerRfqDate`, `RfqSource` | | Referința și data cererii de ofertă a clientului; sursa: 1 Email · 2 Telefon · 3 Vizită · 4 Licitație publică (SEAP) · 5 Portal B2B (viitor) |
| `CurrencyCode`, `PriceListId`, `PriceDate` | | Moneda, lista documentului și data prețului (V2 §3.4: data documentului sau data cerută a livrării) |
| `PaymentTermDays`, `PaymentMethodId`, `ShippingMethodId`, `Incoterm`, `IncotermPlace` | | Propuse din condițiile clientului; modificabile, cu aprobare peste condiții (3.8) |
| `WarehouseId` | `int` | Gestiunea de livrare propusă (client → punct de livrare → agent → firmă, V1) |
| `ValidUntil` | `date` | `DocDate` + `QuoteValidityDays` (V1, implicit 30); cel mult `QuoteMaxValidityDays` (`52205`) |
| `TentativeReserve` | `bit` | Rezervare provizorie în S5 pe liniile principale și acceptate, până la `ValidUntil` (cere `Sales.Quote.Reserve`, `52224`) |
| `Status`, `Revision` | | 3.2; `Revision` 0 până la prima trimitere, apoi crește la fiecare revizie trimisă |
| `NetAmount`, `DiscountAmount`, `VatAmount`, `TotalAmount` | `decimal(19,4)` | Totalurile liniilor principale (fără opționale și fără alternativele neprincipale) |
| `MaxNetAmountRon` | `decimal(19,4)` | Valoarea maximă posibilă în lei, la cursul BNR al zilei ofertei: baza pragului de aprobare (regula 4) |
| `ApprovedNetAmountRon`, `RequiredLevel`, `CurrentLevel` | | Ca la V2 §3.9; baza reaprobării |
| `AcceptedAt`, `AcceptedByContact`, `AcceptanceRef`, `AcceptanceAttachmentId` | | Acceptarea: data, cine a acceptat la client, referința (de exemplu numărul comenzii lui), documentul atașat |
| `LostAt`, `LostReasonId`, `CompetitorName`, `CompetitorPriceNote` | | Pierderea: motivul (`ReasonCode` tipul 1, obligatoriu, `52204`), concurentul și prețul lui, dacă se știu |
| `NextFollowUpDate` | `date NULL` | Data următoarei reveniri la client; propusă la trimitere (`QuoteFollowUpDays`) |
| `NoteToCustomer`, `InternalNote` | `nvarchar(2000)` | Prima apare pe PDF |

### 3.2 Stările ofertei

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează liber; prețurile se recalculează la fiecare schimbare | → De aprobat; → Aprobată direct, fără excepții și sub prag; → ștergere fizică, dacă n-a fost numerotată |
| 2 De aprobat | Blocată la editare (`52201`) | → Aprobată, → Ciornă (retrasă sau returnată), → Anulată (respinsă) |
| 5 Aprobată | Se poate trimite; o modificare o readuce în ciornă | → Trimisă (manual sau automat, `QuoteSendOnApproval`), → Ciornă, → Anulată |
| 6 Trimisă | Așteaptă răspunsul clientului; modificările creează revizii (4.3) | → Acceptată, → Pierdută, → Expirată (job), → Anulată |
| 7 Acceptată | Se fac comenzi (`CV-OV`) și proforme (`PF-OV`) din ea | → Închisă (tot ce s-a acceptat e comandat, sau la expirarea ofertei cadru), → Pierdută (clientul se răzgândește înainte de comandă) |
| 4 Închisă | Finală; restul necomandat rămâne vizibil, informativ | — |
| 10 Pierdută | Finală, cu motiv | → Trimisă, prin revizie, dacă e încă în valabilitate („clientul revine”) |
| 11 Expirată | Jobul `ExpireQuotes` (00:15) o trece aici la `ValidUntil` depășit; rezervarea provizorie se eliberează | → Trimisă, prin revizie cu valabilitate nouă; → Pierdută |
| 9 Anulată | Finală, cu motiv de tip 7 | — |

Oferta transformată direct în comandă (`CV-OV` din starea Trimisă) se marchează Acceptată automat, cu liniile și cantitățile preluate. Lista arată calculat: „expiră în 5 zile” (`QuoteExpiryReminderDays`), „fără răspuns de X zile”, „parțial comandată”.

### 3.3 Liniile ofertei (`SalesQuoteLine`) și livrările cerute (`SalesQuoteSchedule`)

| Câmp | Reguli |
| --- | --- |
| `LineNo`, `LineKind` | 1 Articol · 2 Text |
| `ItemVariantId`, `UomId`, `Qty`, `QtyBase` | Varianta vandabilă și activă (`52202`), cantitatea > 0 (`52203`). Pe o linie de text, goale |
| `AlternativeGroup`, `IsPrincipal` | Litera grupului (A, B…) și alternativa principală (exact una pe grup) |
| `IsOptional` | Linie opțională, în afara totalului |
| `ListPrice`, `PriceSource`, `PriceListLineId`, `UnitPrice`, `ManualDiscountPct`, `VatRateId`, `NetAmount`, `UnitCostRon`, `MarginPct` | Din motorul de preț (V2 §3.7); discounturile aplicate în `DocumentLineDiscount` |
| `ParentLineId` | Linia sursă a unei linii de bonus (V2 §3.6.1) |
| `CustomerItemCode` | Codul clientului din catalog (V1 §3.6), copiat pentru PDF |
| `LeadTimeText` | Termenul de livrare promis în text, pentru ofertele fără date fixe („5 zile lucrătoare de la comandă”) |
| `AcceptedQty`, `IsAccepted` | Completate la acceptare; `CV-OV` propune `AcceptedQty` |

**Livrările cerute** (`SalesQuoteSchedule`): `LineId`, `Seq`, `RequestedDate`, `Qty`; suma pe linie = cantitatea liniei (`52213`). O linie fără livrări are o singură livrare implicită, la data cerută din antet. Comanda din ofertă copiază livrările.

### 3.4 Antetul comenzii (`SalesOrder`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `Number`, `DocDate` | | — |
| `TransactionTypeId` | `int` | `CV-MAN`, `CV-OV`, `CV-IMP` sau o variantă a lor |
| `PartnerId` | `int` | Client (`52001`), neblocat (`52004`) |
| `DeliveryPointId` | `int` | Punctul de livrare al clientului, activ (`52009`); dă adresa, GLN-ul, zona, fereastra de livrare și gestiunea propusă |
| `PayerPartnerId` | `int NULL` | Copiat din condițiile clientului (V1, informativ) |
| `SalesAgentId`, `SalesChannelId` | `int` | Din portofoliu; schimbarea agentului cere `Sales.Document.ChangeAgent` |
| `CustomerOrderRef`, `CustomerOrderDate` | `nvarchar(50)`, `date` | Numărul comenzii clientului: obligatoriu la confirmare dacă clientul îl cere (`52207`); unic pe client printre comenzile neanulate (`52208`). Trece pe aviz, pe factură și în e-Factura (`OrderReference`, BT-13) |
| `CurrencyCode`, `PriceListId`, `PriceDate` | | Ca la ofertă; pe `CV-OV`, din ofertă (nu se schimbă după prima linie preluată, `52214`) |
| `PaymentTermDays`, `PaymentMethodId`, `ShippingMethodId`, `Incoterm`, `IncotermPlace` | | Din condițiile clientului sau din ofertă |
| `WarehouseId` | `int` | Gestiunea de livrare a firmei, activă, fără blocare la ieșire, nu de tranzit sau custodie (`52211`); o linie o poate preciza |
| `RequestedDate` | `date` | Data cerută propusă pe livrări; nu înaintea datei documentului (`52210`) |
| `AllowPartialDelivery` | `bit` | Din condițiile clientului (V1) |
| `ReservationPriority` | `tinyint` | 1–9, din condițiile clientului (implicit 5); trimisă la S5 (`Priority`); modificabilă cu `Inv.Reservation.Priority` |
| `AdvancePct`, `AdvanceAmount`, `AdvanceHold` | | Avansul cerut (din `CustomerTerms.RequiredAdvancePct` sau scris de agent), suma cu TVA și semnul „avans neîncasat”: cât e 1, comanda nu se pregătește și nu se livrează (`52222`) |
| `CreditHold`, `CreditHoldReason` | `bit`, `tinyint` | Din verificarea de credit (V1 §4.3): 1 limită · 2 restanță · 3 ambele. Se ridică prin deblocare (`CreditRelease`) sau automat, la reverificarea declanșată de o încasare (5.2) |
| `Status`, `Revision` | | 3.5; `Revision` crește la fiecare revizie după confirmare (3.7) |
| `NetAmount`, `DiscountAmount`, `VatAmount`, `TotalAmount`, `NetAmountRon` | `decimal(19,4)` | Calculate de handler (`SP-13`); `NetAmountRon` la cursul BNR al zilei comenzii |
| `ApprovedNetAmountRon`, `RequiredLevel`, `CurrentLevel`, `ApprovalSourceQuoteId` | | Aprobarea proprie sau oferta aprobată care o acoperă (regula 4) |
| `ConfirmedAt`, `ConfirmedBy`, `ConfirmationSentAt`, `ConfirmationSentRevision` | | Ultima confirmare trimisă clientului și revizia ei |
| `ImportId` | `bigint NULL` | Importul din care provine (`CV-IMP`, 3.10) |
| `NoteToCustomer`, `NoteToWarehouse`, `InternalNote` | `nvarchar(1000)` | A doua apare pe dispoziția de livrare și pe aviz (V4) |
| `ClosedAt`, `CancelledAt`, `CancelReasonId` | | Motivul anulării: `ReasonCode` tipul 7 |

### 3.5 Stările comenzii și verificările de confirmare

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează liber; prețurile se recalculează; rezervare provizorie opțională | → Confirmată (verificări trecute); → De aprobat (excepții de preț sau prag depășit); → ștergere fizică, dacă n-a fost numerotată |
| 2 De aprobat | Blocată la editare (`52201`) | → Confirmată, automat la aprobarea ultimului nivel (cererea de aprobare e o cerere de confirmare); → Ciornă (retrasă sau returnată); → Anulată (respinsă) |
| 3 Confirmată | Se pregătește, se livrează și se facturează (V4, V5); modificările creează revizii (3.7) | → Închisă; → Anulată (doar fără nimic preluat în aval, `52216`) |
| 4 Închisă | Toate liniile livrate (cu toleranța) sau închise manual; facturarea poate continua | → Confirmată, la redeschiderea unei linii închise manual |
| 9 Anulată | Finală, cu motiv de tip 7; rezervările se eliberează; clientul primește anunțul de anulare, dacă i s-a trimis confirmarea | — |

**Verificările la „Confirmă”**, în ordine; primele cinci opresc confirmarea, celelalte o lasă să treacă cu semn:

| # | Verificare | Rezultat |
| --- | --- | --- |
| 1 | Liniile importate sunt toate rezolvate | Altfel `52212` |
| 2 | Referința clientului, dacă e cerută, și unică | `52207`, `52208` (după `DuplicateCustomerRefAction`: avertizare sau blocare) |
| 3 | Punctul de livrare, gestiunea, datele cerute | `52009`, `52211`, `52210` |
| 4 | Clientul nu e blocat; are rolul de client | `52004`, `52001` |
| 5 | Prețurile: niciun preț lipsă, excepțiile de preț aprobate (V2 §3.9) și pragurile tip 2 (3.8) | Dacă trebuie aprobare, comanda trece în „De aprobat” (`52115` dacă se încearcă ocolirea) |
| 6 | Creditul (V1 §4.3), dacă punctul „confirmare” e bifat în `CreditCheckPoints` | Modul 2: confirmată cu `CreditHold`; modul 1: avertizare |
| 7 | Avansul cerut | `AdvanceHold = 1` și proforma de avans propusă (3.11) |
| 8 | Rezervarea fermă pe orizont (3.9) | Rezervat complet, parțial (restul în așteptare) sau nimic (articol nestocabil) |
| 9 | Data promisă pe fiecare livrare (3.9) | Data promisă după data cerută: după `PromiseMode`, avertizare (implicit) sau blocare fără drept |

Lista comenzilor arată, pe lângă stare, semnele calculate: **acoperită** (toată cantitatea pe orizont rezervată), **acoperită parțial** (are cantitate în așteptare), **în risc** (data promisă calculată azi e după cea trimisă clientului), **întârziată** (o livrare cu data promisă depășită și rest de livrat), **parțial livrată**, **livrată, nefacturată**, **credit**, **avans**.

### 3.6 Linia comenzii (`SalesOrderLine`) și livrările (`SalesOrderSchedule`)

**Linie**: aceleași câmpuri de preț ca linia ofertei (3.3), plus:

| Câmp | Reguli |
| --- | --- |
| `WarehouseId` | Gestiunea liniei, dacă diferă de cea din antet (de exemplu un articol ținut doar în alt depozit) |
| `QuoteLineId` | Linia ofertei sursă; legătura propriu-zisă e în `core.DocumentLink` (grupul `ORDER`) |
| `ImportLineId`, `MatchStep` | Linia importată și pasul de potrivire care a găsit varianta (V1 §4.4) |
| `CustomerPrice`, `CustomerPriceDiffPct` | Prețul trimis de client în import și diferența față de al nostru, doar afișate; prețul clientului se acceptă doar ca preț scris de mână, cu dreptul și excepția din V2 |
| `MinShelfLifeDays` | Zilele minime până la expirare cerute de client (Stocuri S4), copiate pentru dispoziția de livrare |
| `ClosedQty`, `ClosedReasonId` | Calculate din `core.DocumentLineClosure` (Nucleu), cu motivul de tip 2 |

**Livrare** (`SalesOrderSchedule`): `LineId`, `Seq`, `RequestedDate`, `Qty` (UM liniei), `QtyBase`, `PromisedDate`, `PromiseCalculatedAt`, `PromiseRiskDate` (data calculată la ultima verificare, dacă e după `PromisedDate`), `IsInReservationHorizon`. Cantitățile livrate, în dispoziții și rămase se calculează la citire din legăturile `PREPARE` și `DELIVER`, repartizate în ordinea datelor; nu se stochează.

**Ce citesc alții**: S5 și MRP, prin `ISalesOrderDemand` (V1 §6), restul de livrat pe fiecare livrare, cu data cerută, cu semnul rezervat și cu semnul `CreditHold`; V4 (dispoziția de livrare și avizul) liniile deschise prin `ISalesOrderQueries` (6), cu data promisă.

### 3.7 Reviziile (`SalesDocumentRevision`), comune ofertei și comenzii

| Ce se schimbă | Oferta trimisă | Comanda confirmată |
| --- | --- | --- |
| Data unei livrări, nota, contactul, adresa de email | Revizie, fără reaprobare | Revizie; rezervarea și data promisă se recalculează; fără reaprobare |
| Cantitate sau preț în jos, linie închisă | Revizie, fără reaprobare | Revizie; rezervarea scade (`ReleaseAsync` parțial) |
| Cantitate în sus, linie nouă, discount mai mare | Revizie; peste toleranță, înapoi în „De aprobat” | Revizie; peste toleranță sau cu excepție de preț nouă, comanda trece în „De aprobat” cu rezervările păstrate, iar după aprobare revine în Confirmată |
| Prelungirea valabilității | Revizie, cu alegerea „păstrează prețurile” sau „recalculează la noua dată” | — |
| Client, monedă, gestiune | Nu (anulare și document nou) | Nu (`52214`) |

**Revizie**: `DocumentType` (oferta sau comanda, enumerarea `SalesDocumentType` din V1 §6), `DocumentId`, `Revision`, `ChangedAt`, `ChangedBy`, `Reason` (obligatoriu, `52223`), `SnapshotJson` (antetul, liniile, livrările și discounturile versiunii anterioare; `SQL-10`), `NetAmountRonBefore`, `NetAmountRonAfter`, `RequiredReapproval`. Doar adăugare (`SP-21`). Ecranul „Revizii” compară două versiuni câmp cu câmp. Pe comandă, o revizie care atinge cantități sau date se trimite clientului ca „Confirmare revizuită”, dacă confirmarea fusese trimisă.

**Trimiterile** (`SalesDocumentSend`), comune ofertei, comenzii (confirmarea) și proformei: `DocumentType`, `DocumentId`, `Revision`, `Channel` (1 Email · 2 EDI · 3 Tipărit sau predat manual), `Recipient`, `SentAt`, `SentBy`, `AttachmentId` (PDF-ul exact trimis, păstrat), `OutboxMessageId`. Emailul pleacă prin outbox-ul Nucleului, nu din tranzacția documentului.

### 3.8 Aprobarea pe valoare (`SalesApprovalRule`, tipul 2)

Tabelul din V2 §3.9 primește tipul 2, cu coloanele: `AppliesTo` (1 oferte · 2 comenzi · 3 amândouă), `Level`, `RoleId`, `MaxNetAmountRon` (gol = fără limită), `MaxExtraPaymentDays` (cu câte zile poate depăși termenul de plată din condițiile clientului fără aprobare). Pentru un document se caută cel mai mic nivel care acoperă și valoarea, și termenul; nivelul cerut e maximul dintre acesta și nivelul cerut de excepțiile de preț (V2). Autorul nu își aprobă documentul (`52116`, V2).

Reguli livrate la activarea modulului (editabile în Configurare › Aprobări):

| Nivel | Rol | Oferte și comenzi până la | Termen de plată peste condiții |
| --- | --- | --- | --- |
| 0 | Agent de teren, Telesales | 30.000 lei | 0 zile |
| 0 | Key account | 150.000 lei | 0 zile |
| 1 | Șef vânzări | 500.000 lei | 30 de zile |
| 2 | Director comercial (opțional) | fără limită | fără limită |

`OrderValueApproval` (3.12) oprește pragurile pe comenzi, pentru firmele care aprobă doar ofertele. Comenzile `CV-OV` acoperite de o ofertă aprobată nu se reaprobă (regula 4).

### 3.9 Rezervările și data promisă

**Rezervarea fermă** se face la confirmare, dacă `ReserveOnConfirm` = 1 (V1), pentru fiecare linie cu articol stocabil: o cerere `IStockLedger.ReserveAsync` cu `QtyBase` = suma livrărilor rămase din orizont, `RequiredDate` = cea mai apropiată dintre ele, `Kind = Firm`, `AllowWaiting = true`, `Priority = ReservationPriority`, `LotId` gol (rezervare generică; lotul se alege la dispoziția de livrare, S5 decizia 2) și `ExpiresAt` = data cerută a ultimei livrări rezervate + `ReservationExpiryDays` (V1, implicit 14; suprascris pe client în `inv.PartnerReservationRule`). Expirarea se socotește de la data cerută, nu de la confirmare, ca o comandă pentru peste 10 zile să nu piardă marfa înainte de livrare. S5 ține o rezervare pe linie (`SourceLineId`); V3 o ajustează la fiecare schimbare (aceeași cerere, cantitate nouă).

**Jobul `ReserveHorizon`** (zilnic, 06:00) mărește rezervarea liniilor ale căror livrări au intrat în orizont și recalculează data de risc. **Eliberarea** (`ReleaseAsync`): la închiderea unei linii (motiv `LineClosed`), la scăderea cantității (`QtyReduced`), la anulare (`OrderCancelled`). Livrarea consumă rezervarea prin avizul din V4 (`ConsumesReservationOfSourceLineId`, S5).

**Rezervarea provizorie** (`Kind = Tentative`) pe oferta cu `TentativeReserve` sau pe comanda în ciornă (bifa „rezervă provizoriu”): nu blochează marfa, apare doar în disponibilul proiectat, expiră la `ValidUntil` (ofertă) sau după 3 zile (ciornă). La confirmarea comenzii din ofertă, provizoria se eliberează și se face rezervarea fermă, în aceeași tranzacție.

**Data promisă**, pe fiecare livrare, din `GetProjectedAvailabilityAsync` (S5 §3.4) pe gestiunea liniei, de azi pe `PromiseHorizonDays` (implicit 60) zile:

1. Din disponibilul proiectat se scoate partea proprie a liniei (ca linia să nu se concureze singură), apoi se parcurg livrările liniei în ordinea datelor.
2. Pentru fiecare livrare: prima zi `z` ≥ data cerută − `PickLeadDays` în care proiectatul cumulat acoperă cantitatea cumulată a livrărilor până la ea inclusiv; data promisă = `z` + `PickLeadDays` (implicit 1), mutată la următoarea zi de livrare a punctului de livrare (V1 §3.5) și la prima zi lucrătoare (calendarul firmei, Nucleu).
3. Intrările așteptate care contează sunt cele confirmate: comenzile la furnizor confirmate (`IPurchaseOrderSupply`, `IsConfirmed = 1`), transferurile în tranzit și ordinele de producție lansate. Comenzile la furnizor neconfirmate nu dau dată promisă.
4. Fără acoperire în orizont: livrarea rămâne „fără dată”; confirmarea cere data scrisă de mână, cu drept (`Sales.Order.PromiseOverride`) și cu nota „fără acoperire”.
5. Agentul poate **împărți** o livrare: ce e disponibil la data cerută, restul la data promisă (doar dacă `AllowPartialDelivery` = 1).

### 3.10 Importul comenzilor (`SalesOrderImport`, `SalesOrderImportLine`)

Un import e un fișier sau un lot de mesaje: `Format` (1 Excel cu șablonul livrat · 2 CSV cu profil de coloane · 3 EDIFACT ORDERS D96A · 4 XML UBL Order), `ProfileId` (profilul clientului: coloanele, separatorul, UM-ul implicit, dacă prețul e pe bax sau pe bucată), `FileAttachmentId`, `ReceivedAt`, `ImportedBy`, `Status` (1 Primit · 2 Potrivit · 3 Cu probleme · 4 Comenzi create · 9 Respins). Profilurile sunt în tabelul `sales.OrderImportProfile`; formatul Excel e livrat ca profil de sistem.

Fiecare mesaj sau grup de rânduri cu același client, punct de livrare și referință devine **o comandă `CV-IMP` în ciornă**. Clientul se găsește după CUI sau după GLN-ul punctului de livrare; liniile, după cei 6 pași din V1 §4.4. Linia importată păstrează textul primit (cod, GTIN, denumire, UM, cantitate, preț), pasul de potrivire și starea: 1 Sigură · 2 Probabilă, de confirmat · 3 Fără potrivire · 4 Articol nelistat la client (`CustomerItem` inactiv la data livrării) · 5 Ignorată. Referința deja folosită de o comandă neanulată a aceluiași client face mesajul **dublură**: nu se creează comanda (`52208`) și importul îl arată ca atare.

Cu `ImportAutoConfirm` = 1, comenzile care au toate liniile sigure, nicio diferență de preț peste `ImportPriceTolerancePct` și nicio excepție se confirmă singure (cu toate verificările din 3.5, sub utilizatorul tehnic al importului); celelalte rămân în ciornă, în „De rezolvat” la agent și la operatorul de comenzi.

### 3.11 Proforma (`SalesProforma`, `SalesProformaLine`, `SalesProformaPayment`)

| Câmp | Reguli |
| --- | --- |
| `Number`, `DocDate`, `TransactionTypeId` | Seria `PF`; `PF-MAN`, `PF-CV`, `PF-OV` |
| `PartnerId`, `CurrencyCode`, `SourceDocumentType`, `SourceDocumentId` | Clientul, moneda și documentul sursă (comanda sau oferta) |
| `ProformaKind` | 1 **Integrală**: liniile sursei (sau cele scrise), cu valorile lor · 2 **Avans**: un procent sau o sumă din sursă, repartizată pe cotele de TVA ale sursei, proporțional cu valoarea |
| `AdvancePct`, `AdvanceAmount` | Pentru avans; suma totală cerută prin proforme de avans neanulate nu depășește totalul cu TVA al sursei (`52219`) |
| `DueDate` | Data până la care se plătește (implicit `DocDate` + 5 zile) |
| `BankAccounts` | Conturile firmei tipărite (contul de trezorerie implicit al monedei, F2) |
| `Status` | 1 Ciornă · 3 Emisă · 4 Încasată (încasările legate ≥ total) · 9 Anulată |
| `PaidAmount` | Suma încasărilor legate |

Proforma emisă nu se modifică (`52220`): se anulează și se emite alta. Proforma cu încasări legate nu se anulează (`52221`). Pe PDF apare „Factură proformă — nu este document fiscal” și, pentru avans, „Avans X % pentru comanda CV …”.

**Legarea încasării.** V3 consumă `IncasareInregistrata` (F1, F2): dacă o linie a încasării are în `SettlementRef` sau în detaliile plății numărul unei proforme emise a aceluiași partener, se scrie `SalesProformaPayment` (`ReceiptDocumentType`, `ReceiptDocumentId`, `ReceiptLineId`, `Amount`, `MatchedBy` = automat). Încasările nepotrivite automat se leagă din proformă („Leagă încasarea”, lista încasărilor nealocate ale clientului din F3). Când suma legată acoperă avansul comenzii, `AdvanceHold` se ridică, agentul primește notificarea, iar operatorul de facturare primește în „De rezolvat” factura de avans de emis (V5), cu termenul legal (9).

### 3.12 Setările V3 (completare la `SalesSettings`, V1 §3.9, și la `CustomerTerms`, V1 §3.4)

| Setare | Unde | Implicit | Ce înseamnă |
| --- | --- | --- | --- |
| `QuoteMaxValidityDays` | Firmă | 90 | Valabilitatea maximă a ofertei standard; oferta cadru: 366 |
| `QuoteSendOnApproval` | Firmă | 1 | Oferta aprobată pleacă automat la client, dacă are adresă de email |
| `QuoteFollowUpDays` | Firmă | 7 | Data de revenire propusă după trimitere; la ea, agentul primește reamintirea |
| `QuoteExpiryReminderDays` | Firmă | 5 | Cu câte zile înainte de expirare apare oferta în „expiră curând” |
| `OrderValueApproval` | Firmă | 1 | Pragurile pe valoare (3.8) se aplică și comenzilor fără ofertă aprobată |
| `ReapprovalTolerancePct` | Firmă | 0 | Creșterea valorii față de cea aprobată peste care documentul revine la aprobare |
| `ReservationHorizonDays` | Firmă | 14 | Livrările cu data cerută în atâtea zile se rezervă ferm (regula 6) |
| `PromiseHorizonDays` | Firmă | 60 | Pe câte zile se caută data promisă |
| `PickLeadDays` | Firmă; suprascrisă pe gestiune | 1 | Zilele dintre disponibilitatea mărfii și livrare |
| `PromiseMode` | Firmă | 1 | 1 avertizare când data cerută nu se poate respecta · 2 confirmarea cere `Sales.Order.PromiseOverride` |
| `DuplicateCustomerRefAction` | Firmă | 2 | 1 avertizare · 2 blocare (`52208`) |
| `ImportAutoConfirm`, `ImportPriceTolerancePct` | Firmă | 1, 0,5 | Confirmarea automată a comenzilor importate fără probleme și toleranța de preț |
| `OrderConfirmationSendMode` | Firmă | 1 | 1 manual (butonul „Trimite confirmarea”) · 2 automat la confirmare, dacă clientul are adresă sau canal EDI |
| `AutoProformaOnAdvance` | Firmă | 1 | La confirmarea unei comenzi cu avans se creează proforma de avans în ciornă |
| `NewCustomerAdvancePct` | Firmă | 0 | Avansul propus în condițiile unui client nou (0 = fără) |
| `ReservationPriority` | Client (`CustomerTerms`, coloană nouă) | 5 | Prioritatea rezervărilor comenzilor clientului în S5 (1 cea mai mare) |
| `RequiredAdvancePct` | Client (`CustomerTerms`, coloană nouă) | gol | Avansul cerut pe fiecare comandă a clientului |

Ecranul: Configurare › Vânzări, secțiunea „Oferte și comenzi”, și Configurare › Aprobări, pragurile pe valoare (`v3-12`).

## 4. Fluxuri principale

Datele de exemplu continuă pe cele din V1, V2 și Stocuri S5: firma Exemplu Distribuție SRL, depozitul `DEP-CJ`, costurile curente APA05 1,21 lei, APA15 2,05 lei, CEAI05 1,94 lei, APA2N 2,74 lei, lista `BAZA` înainte de scumpirea din 01.12.2026 (`MPV 0014`): APA05 1,80, APA15 3,05, CEAI05 3,22 (pe model), APA2N 4,10 și 3,95 de la 600 buc.

### 4.1 De la cererea clientului la ofertă

1. Pe 19.11.2026, Hyper Nord SA (grupa RETAIL, key account Cristina Marin, termen 45 de zile) cere pe email o ofertă pentru campania de iarnă, referința `HN-RFQ-118`: apă minerală 0,5 L și 1,5 L, ceai rece și, eventual, apă plată 2 L, livrate în trei tranșe în decembrie la depozitul lor central din Cluj.
2. Pe 20.11, Cristina înregistrează cererea direct ca ofertă în ciornă (`OV-MAN`): sursa 1 Email, referința și data cererii, emailul atașat. Nu există un document separat de „cerere de ofertă de la client” (decizia 2); lista „Oferte” are filtrul „cereri fără răspuns” (ciorne cu referință de cerere, mai vechi de o zi).
3. Motorul de preț fixează lista `BAZA` (lista grupei RETAIL) și data prețului 20.11.2026. Cristina scrie liniile, livrările (08.12, 15.12, 22.12, câte o treime) și discountul manual de 4 % pe apă (plafonul ei e 7 %, deci fără excepție de preț).

### 4.2 Oferta `OV 0197`: aprobarea și trimiterea

| # | Articol | Cantitate | Preț listă | Discount | Preț unitar | Valoare | Marjă |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | APA05 Apă minerală 0,5 L | 24.000 buc | 1,80 | manual 4 % | 1,73 (1,728) | 41.520,00 | 30,1 % |
| 2 | APA15 Apă minerală 1,5 L | 18.000 buc | 3,05 | manual 4 % | 2,93 (2,928) | 52.740,00 | 30,0 % |
| 3A | CEAI05 Ceai rece piersici 0,5 L (principală) | 9.600 buc | 3,22 | — | 3,22 | 30.912,00 | 39,8 % |
| 3B | CEAI05L Ceai rece lămâie 0,5 L (alternativă) | 9.600 buc | 3,22 | — | 3,22 | (30.912,00) | 39,8 % |
| 4 | APA2N Apă plată 2 L (opțională) | 7.200 buc | 3,95 (prag ≥ 600) | — | 3,95 | (28.440,00) | 30,6 % |

1. **Totalul ofertei** (liniile 1, 2 și 3A): valoarea de listă 129.012,00 lei, discounturi 3.840,00 lei, valoarea netă 125.172,00 lei; TVA 11 % pe apă (94.260,00 → 10.368,60) și 21 % pe ceai (30.912,00 → 6.491,52), total TVA 16.860,12 lei, total 142.032,12 lei.
2. **Valoarea maximă posibilă** (regula 4): 125.172,00 + 28.440,00 (opționala) = 153.612,00 lei (alternativele au același preț). Pragul nivelului 0 pentru Key account e 150.000 lei, deci oferta cere aprobarea nivelului 1.
3. Cristina apasă „Trimite”; oferta primește numărul `OV 0197`, trece în „De aprobat”, iar Radu Stan primește notificarea cu valoarea, marjele și istoricul clientului (restanța Hyper Nord e afișată ca informație). Radu aprobă la 16:10. Cu `QuoteSendOnApproval` = 1, PDF-ul pleacă la 16:12 la `achizitii@hypernord.ro` (Revizia 0, valabilă până la 20.12.2026), cu `TentativeReserve` bifat: S5 primește rezervări provizorii pentru liniile 1, 2 și 3A, cu expirarea 20.12.
4. **Revizia 1** (24.11): Hyper Nord cere mutarea tranșei a treia de pe 22.12 pe 18.12. Cristina face revizia cu motivul „cererea clientului, email din 24.11”; valoarea nu crește, deci fără reaprobare. PDF-ul nou poartă „Revizia 1, înlocuiește revizia 0 din 20.11.2026”.
5. Pe 01.12 lista `BAZA` crește cu 4 % (`MPV 0014`, V2 §4.2). Oferta trimisă **nu se schimbă**: prețurile sunt garantate până pe 20.12 (regula 2), iar comanda din ea le copiază.

### 4.3 Acceptarea, pierderea și expirarea

- **Acceptarea** se înregistrează din ofertă („Clientul acceptă…”, ecranul `v3-03`): data, persoana, referința, atașamentul și, pe fiecare linie, dacă e acceptată și cantitatea. Pe 26.11 Hyper Nord trimite comanda `HN-PO-2026-4471`: acceptă liniile 1, 2, alternativa 3A (piersici) și opționala 4 cu doar 4.800 buc (tranșele 08.12 și 15.12). Oferta trece în Acceptată; `MaxNetAmountRon` nu se recalculează (aprobarea rămâne valabilă).
- **Acceptarea parțială** lasă restul ofertei vizibil, informativ (consumul `ORDER`). Oferta standard acceptată se închide când tot ce s-a acceptat e comandat sau la `ValidUntil`; oferta cadru, doar la `ValidUntil`.
- **Pierderea**: motivul de tip 1 obligatoriu (`52204`), opțional concurentul și prețul lui. Rezervarea provizorie se eliberează. Raportul „Oferte câștigate și pierdute” (10) folosește motivele.
- **Expirarea**: jobul `ExpireQuotes` (00:15) trece ofertele trimise cu `ValidUntil` depășit în Expirată și eliberează rezervările provizorii; agentul a primit reamintirea cu 5 zile înainte. Prelungirea se face printr-o revizie cu noua valabilitate: „păstrează prețurile” (cu excepție de preț dacă noile prețuri de listă sunt mai mari și discountul efectiv depășește plafonul) sau „recalculează la noua dată” (prețurile din listă la data revizuirii, cu diferențele afișate).
- **Clientul revine** după pierdere, în valabilitate: o revizie readuce oferta în Trimisă.

### 4.4 Comanda din ofertă și confirmarea (`CV 4452`)

1. Pe 27.11 Cristina generează comanda din ofertă cu asistentul (`CV-OV`): sursa `OV 0197`, liniile acceptate cu cantitățile acceptate, livrările copiate, punctul de livrare `HN-DC-CJ` Depozit central Hyper Nord Cluj (GLN 5940000123457), referința clientului `HN-PO-2026-4471` din 26.11. Prețurile și discounturile se copiază din ofertă (V2 regula 8): APA05 1,73 și APA15 2,93, deși `BAZA` va avea de pe 01.12 alte prețuri.

| # | Articol | Cantitate | Livrări cerute | Preț unitar | Valoare |
| --- | --- | --- | --- | --- | --- |
| 1 | APA05 | 24.000 buc | 08.12 · 15.12 · 18.12, câte 8.000 | 1,73 | 41.520,00 |
| 2 | APA15 | 18.000 buc | 08.12 · 15.12 · 18.12, câte 6.000 | 2,93 | 52.740,00 |
| 3 | CEAI05 | 9.600 buc | 08.12 · 15.12 · 18.12, câte 3.200 | 3,22 | 30.912,00 |
| 4 | APA2N | 4.800 buc | 08.12 · 15.12, câte 2.400 | 3,95 | 18.960,00 |

   Valoarea netă 144.132,00 lei; TVA 11 % pe apă (113.220,00 → 12.454,20) și 21 % pe ceai (6.491,52), total TVA 18.945,72 lei; total 163.077,72 lei.
2. **Aprobarea**: comanda păstrează prețurile ofertei și 144.132,00 ≤ 153.612,00 (valoarea aprobată a ofertei), deci nu se reaprobă (`ApprovalSourceQuoteId` = `OV 0197`).
3. **Creditul** (V1 §4.3, mod 2): limita Hyper Nord e 400.000,00 lei. Expunerea = sold 168.400,00 + avize nefacturate 6.200,00 + comenzi confirmate nelivrate 21.300,00 + comanda curentă 163.077,72 = 358.977,72 lei, sub limită. Dar factura `FV EXD 10142` (12.380,00 lei) e restantă de 34 de zile, peste pragul de 30: comanda se confirmă cu `CreditHold` (motivul 2, restanță). Nu se poate pregăti sau livra până la ridicarea semnului.
4. **Rezervarea**: orizontul de 14 zile ține până pe 11.12, deci se rezervă doar tranșa din 08.12. Prioritatea Hyper Nord e 3; expirarea 08.12 + 14 = 22.12. Rezervările provizorii ale ofertei se eliberează în aceeași tranzacție.
5. **Data promisă** pentru tranșa din 08.12 (`DEP-CJ`, `PickLeadDays` = 1):

| Linie | Cerut 08.12 | Disponibil azi | Intrări confirmate | Rezervat | În așteptare | Data promisă |
| --- | --- | --- | --- | --- | --- | --- |
| APA05 | 8.000 | 2.040 | `CF 2291` 5.760 pe 02.12, `CF 2304` 9.600 pe 04.12 | 2.040 | 5.960 | 08.12 (acoperit din 04.12 + 1 zi) |
| APA15 | 6.000 | 11.400 | — | 6.000 | 0 | 08.12 |
| CEAI05 | 3.200 | 1.150 | `CF 2297` 4.800 pe 10.12 | 1.150 | 2.050 | **11.12** (cerut 08.12) |
| APA2N | 2.400 | 3.600 | — | 2.400 | 0 | 08.12 |

6. Fereastra de confirmare (`v3-07`) arată verificările și propune, pe CEAI05, „împarte livrarea”: 1.150 buc pe 08.12 și 2.050 buc pe 11.12. Cristina alege împărțirea (Hyper Nord acceptă livrări parțiale) și confirmă la 10:40. Comanda e Confirmată, „acoperită parțial” (8.010 buc în așteptare, toate cu intrări confirmate înainte de data promisă) și „credit”.
7. Confirmarea (PDF, Revizia 0) pleacă la Hyper Nord cu datele promise, inclusiv tranșa de ceai din 11.12. Radu Stan și Ion Popescu văd comanda în „De rezolvat › Comenzi în credit”.
8. **Ridicarea semnului**: pe 28.11 Hyper Nord plătește `FV EXD 10142`. F3 publică `FacturaAchitata`; V3 reverifică creditul comenzilor clientului cu `CreditHold`: restanța a dispărut, expunerea e sub limită, deci semnul se ridică automat, cu rând în jurnalul de excepții (V7) și notificare la Cristina.
9. Pe 01.12, jobul `ReserveHorizon` rezervă tranșa din 15.12; pe 04.12, pe cea din 18.12.

### 4.5 Comanda importată (Mega Retail)

1. Pe 27.11 la 07:30, conectorul EDI preia 3 mesaje `ORDERS` de la Mega Retail SRL (profilul clientului: cantitatea în baxuri, prețul pe bax). Clientul e găsit după CUI, punctele de livrare după GLN.
2. Mesajul `PO-MR-78102`, punctul `MR-CJ-11` Cluj Mănăștur, livrare 02.12: `MR-102938` 200 bax → APA05 2.400 buc (pasul 3, sigură); `MR-102939` 150 bax → APA15 900 buc (sigură); `MR-118204` 40 bax → CEAI05 480 buc (sigură), cu prețul clientului 34,20 lei/bax = 2,85 lei/buc, față de al nostru 2,90 (BAZA 3,22 − 10 % `DR-MR26`): diferență −1,7 %, peste toleranța de 0,5 %; o linie fără cod, „APA PLATA 2L”, 60 bax → APA2N (pasul 6, **probabilă**: denumirea e conținută în „APA PLATA 2L PET”).
3. Mesajul `PO-MR-78103`, punctul `MR-TD-02` Turda, livrare 03.12: APA05 1.200 buc, APA15 480 buc sigure; `MR-102940` 50 bax → APA05N 600 buc, **nelistat** la data livrării (rândul de catalog se închide pe 30.11, V1 `v1-05`).
4. Mesajul `PO-MR-77815` e **dublură**: referința e deja pe `CV 4421` (20.11). Nu se creează comandă (`52208`).
5. Rezultatul: `CV 4453` și `CV 4454` în ciornă, nicio confirmare automată (fiecare are o problemă). Elena Dima rezolvă în ecranul importului (`v3-08`): confirmă APA2N pe linia probabilă (catalogul învață codul gol + denumirea, `Source = 3`), lasă prețul nostru pe ceai (Cristina anunță clientul) și, pe `CV 4454`, înlocuiește APA05N cu APA05 la cererea telefonică a clientului (cu nota). Valorile: `CV 4453` 3.888,00 + 2.475,00 + 1.392,00 + 1.328,40 (APA2N 360 buc × 3,69, sub pragul de 600) = 9.083,40 lei; `CV 4454` 1.944,00 + 972,00 + 1.320,00 = 4.236,00 lei. Amândouă se confirmă cu verificările obișnuite.

### 4.6 Proforma de avans (`PF 0092`)

1. Pensiunea Arieșul SRL e client nou (grupa HORECA, agent Vlad Georgescu), cu `RequiredAdvancePct` = 50 % în condiții. Pe 27.11 Vlad confirmă `CV 4449`: APA05 1.200 buc × 1,85 = 2.220,00; APA15 600 buc × 3,15 = 1.890,00, plus 120 buc bonus (`BON-APA15`); CEAI05 480 buc × 2,90 (promoția `PR-CEAI-NOV`) = 1.392,00. Valoarea netă 5.502,00 lei, TVA 452,10 (11 % pe 4.110,00) + 292,32 (21 % pe 1.392,00) = 744,42 lei, total 6.246,42 lei.
2. La confirmare, comanda primește `AdvanceAmount` = 3.123,21 lei și `AdvanceHold`; cu `AutoProformaOnAdvance`, se creează proforma de avans în ciornă (`PF-CV`, tip 2, 50 %), repartizată pe cote: apă 2.055,00 + TVA 226,05, ceai 696,00 + TVA 146,16, total 3.123,21 lei, scadentă pe 02.12.
3. Vlad emite proforma `PF 0092` și o trimite pe email.
4. Pe 02.12 extrasul BT are o încasare de 3.123,21 lei de la Pensiunea Arieșul cu detaliile „c/v PF 0092”. F2 validează linia și publică `IncasareInregistrata`; F3 o ține ca încasare nealocată (avans). V3 găsește numărul proformei, scrie legătura, trece proforma în Încasată și ridică `AdvanceHold`. Vlad primește notificarea, iar Elena Dima vede în „De rezolvat › Facturi de avans de emis” factura de avans pentru 3.123,21 lei, cu termenul 15.01.2027 (încasare în decembrie) (9).

### 4.7 Modificarea comenzii confirmate

Exemplu: pe 03.12 Hyper Nord cere încă 2.400 buc APA2N pe 18.12. Cristina face revizia 1 a `CV 4452`: o livrare nouă pe linia 4 (+9.480,00 lei, valoarea netă 153.612,00 lei). Valoarea aprobată a ofertei e 153.612,00 lei, deci revizia rămâne în toleranță: fără reaprobare. Data promisă se calculează pentru livrarea nouă; rezervarea se face când intră în orizont (04.12). Confirmarea revizuită pleacă la client. Dacă revizia ar fi depășit valoarea aprobată, comanda ar fi trecut în „De aprobat”, cu rezervările păstrate și cu livrările deja pregătite neatinse.

### 4.8 Închiderea și anularea

- **Închiderea automată**: la validarea avizului care consumă ultimul rest (V4, cu toleranța tranzacției) sau care lasă pe o linie un rest sub `UnderDeliveryAutoClosePct` (V1); linia se închide cu motivul „sub-livrare în toleranță”, iar rezervarea rămasă se eliberează.
- **Închiderea manuală a restului**: pe linie sau pe comandă, cu motiv de tip 2 (`DocumentLineClosure`, Nucleu); restul dispare din cererea deschisă și rezervarea se eliberează. Clientul primește, opțional, confirmarea revizuită.
- **Anularea**: din Ciornă sau „De aprobat”, liberă; din Confirmată, doar dacă nicio linie nu are legături în aval, active sau în ciornă (`52216`); altfel se închide restul. Anularea cere motiv de tip 7, eliberează rezervările, publică `ComandaVanzareAnulata` și, dacă i s-a trimis confirmarea, trimite clientului anunțul de anulare. Proformele emise și neîncasate ale comenzii se anulează odată cu ea; cele încasate rămân, iar încasarea rămâne nealocată în F3 (de restituit sau de folosit pe altă comandă).

### 4.9 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Clientul trimite comanda fără să răspundă la ofertă | `CV-OV` din oferta trimisă o marchează Acceptată automat, cu liniile și cantitățile preluate |
| Comanda din ofertă cu mai mult decât s-a ofertat | Permisă (consumul `ORDER` e informativ); cantitatea în plus primește prețul ofertei doar pe tranzacțiile cu „păstrează prețul sursei și peste cantitate” (implicit da), iar valoarea peste cea aprobată cere aprobare |
| Oferta în EUR, comanda după o lună | Prețurile rămân în EUR; pragul de aprobare a fost calculat la cursul zilei ofertei; comanda calculează `NetAmountRon` la cursul zilei ei, fără reaprobare pentru diferența de curs |
| Două comenzi ale aceluiași client cer aceeași marfă puțină | S5 alocă după prioritate, apoi data cerută, apoi ora cererii (S5 §3.3); a doua comandă rămâne cu cantitate în așteptare și data promisă mai târzie |
| Disponibilul scade după confirmare (lot în carantină, inventar) | S5 publică `RezervareNeacoperita`; comanda devine „în risc”, iar agentul vede data calculată nouă și decide dacă trimite o confirmare revizuită |
| Comandă pentru un articol nestocabil (serviciu, transport) | Fără rezervare; data promisă = data cerută; se livrează prin aviz de prestare sau direct prin factură (V5) |
| Livrare directă de la furnizor (drop-ship) | În afara V3: comanda se marchează „livrare directă” și generează necesar în Achiziții (A2); fluxul complet e în V4 |
| Comandă importată pentru un punct de livrare nou (GLN necunoscut) | Mesajul rămâne „cu probleme”; operatorul creează punctul de livrare (V1 §3.5) și reia potrivirea |
| Client cu `CashOnly` | Termenul de plată 0 pe comandă; nu cere avans, se încasează la livrare (V4, V5) |
| Agentul pleacă din firmă | Ofertele și comenzile deschise rămân pe el până la mutarea în bloc a portofoliului (V1 §4.5), care mută și documentele deschise |
| Comanda confirmată cu `CreditHold` stă mult | Rezervarea ei expiră normal (S5); marfa nu e blocată la nesfârșit pentru un client care nu plătește |
| Oferta cadru cu prețuri pe un an | Prețurile cadrului sunt garantate până la `ValidUntil`; fiecare `CV-OV` le copiază; după o modificare de listă, cadrul nu se schimbă decât prin revizie acceptată de client |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `OfertaTrimisa` v1 | Trimiterea ofertei sau a unei revizii | `QuoteId`, `Number`, `Revision`, `PartnerId`, `SalesAgentId`, `ValidUntil`, `NetAmountRon`, `MaxNetAmountRon` | Raportare, CRM (viitor) |
| `OfertaInchisa` v1 | Acceptare, pierdere, expirare, anulare | `QuoteId`, `Outcome` (acceptată, pierdută, expirată, anulată), `LostReasonCode`, `CompetitorName`, valorile | Raportare, CRM |
| `ComandaVanzareConfirmata` v1 | Confirmarea comenzii | `SalesOrderId`, `Number`, `PartnerId`, `DeliveryPointId`, `WarehouseId`, liniile `{LineId, ItemVariantId, QtyBase, Schedules {RequestedDate, PromisedDate, QtyBase}}`, `CreditHold`, `AdvanceHold` | Stocuri, WMS, MRP, Raportare |
| `ComandaVanzareModificata` v1 | Revizie, închidere de linie, ridicarea unui semn | `SalesOrderId`, `Revision`, ce s-a schimbat (linii, livrări, semne) | Stocuri, WMS, MRP |
| `ComandaVanzareAnulata` v1 | Anularea | `SalesOrderId`, liniile, `ReasonCode` | Stocuri S5 (anulează rezervările rămase), WMS |
| `ComandaVanzareInchisa` v1 | Toate liniile livrate sau închise | `SalesOrderId`, `ClosedAt` | Raportare |
| `ProformaEmisa` v1 | Emiterea proformei | `ProformaId`, `Number`, `PartnerId`, `SourceDocumentRef`, `Total`, `DueDate` | F5 (încasare așteptată), Notificări |

### 5.2 Consumate

| Eveniment | De la | Ce face V3 |
| --- | --- | --- |
| `RezervareAlocata` v1 | S5 | Actualizează semnul „acoperită”; notifică agentul („CV 4452 e acum acoperită complet”) |
| `RezervareNeacoperita` v1, `RezervareModificata` v1 | S5 | Recalculează data de risc a livrărilor liniei; marchează comanda „în risc” |
| `FacturaAchitata` v1, `AlocareInregistrata` v1 | F3 | Reverifică creditul comenzilor clientului cu `CreditHold`; ridică semnul dacă verificarea trece |
| `IncasareInregistrata` v1 | F1, F2 | Leagă încasarea de proforma cu numărul din detalii (3.11); ridică `AdvanceHold` |
| `AvizValidat` v1 (V4) | Vânzări V4 | Închide automat liniile livrate integral sau în toleranță (4.8) |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V3)

```csharp
// Citit de V4 (dispoziția de livrare, avizul), WMS și ecranul „De livrat azi”
public interface ISalesOrderQueries
{
    Task<IReadOnlyList<OpenOrderLineDto>> GetOpenLinesAsync(int companyId, int warehouseId, DateOnly promisedUpTo,
        int? partnerId, bool includeOnHold, CancellationToken ct);
    Task<SalesOrderSummaryDto?> GetAsync(long salesOrderId, CancellationToken ct);
}
public sealed record OpenOrderLineDto(long SalesOrderId, string Number, long LineId, long ScheduleId, int PartnerId,
    int DeliveryPointId, int ItemVariantId, int UomId, decimal OpenQty, decimal OpenQtyBase, decimal ReservedQtyBase,
    DateOnly RequestedDate, DateOnly? PromisedDate, int? MinShelfLifeDays, bool CreditHold, bool AdvanceHold, byte Priority);

// Implementarea din V3 a contractului definit în V1 §6 (citit de S5 și MRP), acum pe livrări
public interface ISalesOrderDemand
{
    Task<IReadOnlyList<SalesDemandRow>> GetOpenDemandAsync(int companyId, IReadOnlyCollection<int> itemVariantIds,
        int? warehouseId, CancellationToken ct);
}

// Citit de F5 (prognoza de trezorerie): încasările așteptate din comenzile confirmate și nefacturate și din proformele emise
public interface ISalesBacklogForecast
{
    Task<IReadOnlyList<SalesBacklogRow>> GetOpenAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);
}
public sealed record SalesBacklogRow(long DocumentId, SalesDocumentType DocumentType, string Number, int PartnerId,
    string CurrencyCode, decimal AmountWithVat, decimal AmountWithVatRon, DateOnly ExpectedReceiptDate, bool IsAdvance, bool IsOnHold);

// Folosit de V5 (factura de avans) și de ecranul încasărilor din F3
public interface IProformaQueries
{
    Task<IReadOnlyList<ProformaDto>> GetOpenAsync(int companyId, int partnerId, CancellationToken ct);
    Task<IReadOnlyList<ProformaPaymentDto>> GetPaymentsAsync(long proformaId, CancellationToken ct);
}
```

`ExpectedReceiptDate` din `ISalesBacklogForecast`: pentru comenzi, data promisă a fiecărei livrări + termenul de plată al comenzii (întârzierea medie a clientului o aplică F5, ca la partidele deschise); pentru proforme, `DueDate`. Comenzile cu `CreditHold` au `IsOnHold = 1`, iar F5 le arată pe rând separat.

Comenzi interne V3 (MediatR, `Erp.Sales.Application`): `SaveQuote`, `SubmitQuote`, `SendQuote`, `ReviseQuote`, `RecordQuoteAcceptance`, `MarkQuoteLost`, `CancelQuote`, `ExpireQuotes` (job, 00:15), `SaveSalesOrder`, `ConfirmSalesOrder`, `ReviseSalesOrder`, `SplitSchedule`, `CloseSalesOrderLines`, `ReopenSalesOrderLine`, `CancelSalesOrder`, `SendOrderConfirmation`, `ReserveHorizon` (job, 06:00), `RecheckCreditHolds` (din evenimentele F3), `ImportSalesOrders`, `ResolveImportLine`, `CreateOrdersFromImport`, `SaveProforma`, `IssueProforma`, `CancelProforma`, `LinkProformaPayment`, `UnlinkProformaPayment`.

Rezervările se fac prin `IStockLedger.ReserveAsync` / `ReleaseAsync` (S5) în aceeași tranzacție SQL cu confirmarea sau revizia (`IUnitOfWork` comun, ca la mișcările de stoc din V1); data promisă se calculează înainte de tranzacție (citire) și se verifică din nou la scriere, cu `RowVersion` pe comandă.

## 7. Schema SQL `sales` (partea V3)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.SalesQuote` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number) WHERE Number IS NOT NULL`; FK `PartnerId`, `ContactId`, `DeliveryPointId`, `SalesAgentId`, `PriceListId`, `TransactionTypeId`, `WarehouseId`, `LostReasonId` | `(CompanyId, Status, ValidUntil)`; `(CompanyId, PartnerId, DocDate DESC)`; `(CompanyId, SalesAgentId, Status)` |
| `sales.SalesQuoteLine` | `Id bigint` | UX `(QuoteId, LineNo)`; FK `ItemVariantId`, `UomId`, `VatRateId`, `ParentLineId`; `CHECK (LineKind = 2 OR Qty > 0)` | `(ItemVariantId) INCLUDE (QuoteId)` |
| `sales.SalesQuoteSchedule` | `Id bigint` | UX `(LineId, Seq)` | — |
| `sales.SalesOrder` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number) WHERE Number IS NOT NULL`; UX `(CompanyId, PartnerId, CustomerOrderRef) WHERE CustomerOrderRef IS NOT NULL AND Status <> 9` (doar cu `DuplicateCustomerRefAction` = 2; altfel index simplu); FK-urile din 3.4 | `(CompanyId, Status, WarehouseId)`; `(CompanyId, PartnerId, Status)`; `(CompanyId, Status) WHERE CreditHold = 1 OR AdvanceHold = 1` |
| `sales.SalesOrderLine` | `Id bigint` | UX `(SalesOrderId, LineNo)`; FK `ItemVariantId`, `UomId`, `VatRateId`, `WarehouseId`, `ParentLineId`, `QuoteLineId`, `ImportLineId` | `(ItemVariantId, WarehouseId) INCLUDE (SalesOrderId, QtyBase)` |
| `sales.SalesOrderSchedule` | `Id bigint` | UX `(LineId, Seq)`; `CHECK (Qty > 0)` | `(PromisedDate) INCLUDE (LineId, QtyBase)`; `(RequestedDate) WHERE IsInReservationHorizon = 0` pentru jobul de orizont |
| `sales.SalesDocumentRevision` | `Id bigint` | UX `(DocumentType, DocumentId, Revision)`; doar adăugare (`SP-21`) | — |
| `sales.SalesDocumentSend` | `Id bigint` | FK `AttachmentId`; doar adăugare | `(DocumentType, DocumentId, SentAt DESC)` |
| `sales.SalesOrderImport`, `sales.SalesOrderImportLine` | `Id bigint` | FK `ProfileId`, `PartnerId`, `DeliveryPointId`, `ItemVariantId`, `SalesOrderId` | `(CompanyId, Status, ReceivedAt DESC)` |
| `sales.OrderImportProfile` | `Id int` | UX `(CompanyId, CodeKey)`; FK `PartnerId` (gol = profil general) | — |
| `sales.SalesProforma`, `sales.SalesProformaLine` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number) WHERE Number IS NOT NULL`; FK `PartnerId`, sursa | `(CompanyId, PartnerId, Status)` |
| `sales.SalesProformaPayment` | `Id bigint` | UX `(ProformaId, ReceiptDocumentType, ReceiptLineId)`; `CHECK (Amount > 0)` | `(ReceiptDocumentType, ReceiptDocumentId)` |
| `sales.SalesApprovalRule` (coloane noi) | — | `AppliesTo`, `MaxNetAmountRon`, `MaxExtraPaymentDays` pentru `Kind` = 2 | — |
| `sales.SalesSettings`, `sales.CustomerTerms` (coloane noi) | — | Setările din 3.12 | — |

```sql
CREATE TABLE sales.SalesOrder
(
    Id                       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrder PRIMARY KEY,
    CompanyId                int            NOT NULL CONSTRAINT FK_SalesOrder_Company REFERENCES core.Company(Id),
    TransactionTypeId        int            NOT NULL CONSTRAINT FK_SalesOrder_Tt REFERENCES core.TransactionType(Id),
    NumberSeriesId           int            NULL     CONSTRAINT FK_SalesOrder_Series REFERENCES core.NumberSeries(Id),
    Number                   nvarchar(20)   NULL,
    DocDate                  date           NOT NULL,
    PartnerId                int            NOT NULL CONSTRAINT FK_SalesOrder_Partner REFERENCES core.Partner(Id),
    DeliveryPointId          int            NOT NULL CONSTRAINT FK_SalesOrder_DeliveryPoint REFERENCES sales.CustomerDeliveryPoint(Id),
    PayerPartnerId           int            NULL     CONSTRAINT FK_SalesOrder_Payer REFERENCES core.Partner(Id),
    SalesAgentId             int            NULL     CONSTRAINT FK_SalesOrder_Agent REFERENCES sales.SalesAgent(Id),
    SalesChannelId           int            NULL     CONSTRAINT FK_SalesOrder_Channel REFERENCES sales.SalesChannel(Id),
    CustomerOrderRef         nvarchar(50)   NULL,
    CustomerOrderDate        date           NULL,
    CurrencyCode             char(3)        NOT NULL CONSTRAINT FK_SalesOrder_Currency REFERENCES core.Currency(Code),
    PriceListId              int            NULL     CONSTRAINT FK_SalesOrder_PriceList REFERENCES sales.PriceList(Id),
    PriceDate                date           NOT NULL,
    PaymentTermDays          smallint       NOT NULL,
    PaymentMethodId          int            NOT NULL CONSTRAINT FK_SalesOrder_PaymentMethod REFERENCES core.PaymentMethod(Id),
    ShippingMethodId         int            NULL     CONSTRAINT FK_SalesOrder_Shipping REFERENCES sales.ShippingMethod(Id),
    Incoterm                 char(3)        NULL     CONSTRAINT FK_SalesOrder_Incoterm REFERENCES core.DeliveryTerm(Code),
    IncotermPlace            nvarchar(100)  NULL,
    WarehouseId              int            NOT NULL CONSTRAINT FK_SalesOrder_Warehouse REFERENCES inv.Warehouse(Id),
    RequestedDate            date           NOT NULL,
    AllowPartialDelivery     bit            NOT NULL CONSTRAINT DF_SalesOrder_Partial DEFAULT (1),
    ReservationPriority      tinyint        NOT NULL CONSTRAINT DF_SalesOrder_Priority DEFAULT (5),
    AdvancePct               decimal(9,4)   NOT NULL CONSTRAINT DF_SalesOrder_AdvPct DEFAULT (0),
    AdvanceAmount            decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrder_AdvAmt DEFAULT (0),
    AdvanceHold              bit            NOT NULL CONSTRAINT DF_SalesOrder_AdvHold DEFAULT (0),
    CreditHold               bit            NOT NULL CONSTRAINT DF_SalesOrder_CreditHold DEFAULT (0),
    CreditHoldReason         tinyint        NULL,
    Status                   tinyint        NOT NULL CONSTRAINT DF_SalesOrder_Status DEFAULT (1),
    Revision                 smallint       NOT NULL CONSTRAINT DF_SalesOrder_Revision DEFAULT (0),
    NetAmount                decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrder_Net DEFAULT (0),
    DiscountAmount           decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrder_Disc DEFAULT (0),
    VatAmount                decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrder_Vat DEFAULT (0),
    TotalAmount              decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrder_Total DEFAULT (0),
    NetAmountRon             decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrder_NetRon DEFAULT (0),
    ApprovedNetAmountRon     decimal(19,4)  NULL,
    RequiredLevel            tinyint        NOT NULL CONSTRAINT DF_SalesOrder_ReqLvl DEFAULT (0),
    CurrentLevel             tinyint        NOT NULL CONSTRAINT DF_SalesOrder_CurLvl DEFAULT (0),
    ApprovalSourceQuoteId    bigint         NULL     CONSTRAINT FK_SalesOrder_ApprovalQuote REFERENCES sales.SalesQuote(Id),
    ImportId                 bigint         NULL     CONSTRAINT FK_SalesOrder_Import REFERENCES sales.SalesOrderImport(Id),
    ConfirmedAt              datetime2(0)   NULL,
    ConfirmedBy              int            NULL     CONSTRAINT FK_SalesOrder_ConfirmedBy REFERENCES core.AppUser(Id),
    ConfirmationSentAt       datetime2(0)   NULL,
    ConfirmationSentRevision smallint       NULL,
    NoteToCustomer           nvarchar(1000) NULL,
    NoteToWarehouse          nvarchar(1000) NULL,
    InternalNote             nvarchar(1000) NULL,
    ClosedAt                 datetime2(0)   NULL,
    CancelledAt              datetime2(0)   NULL,
    CancelReasonId           int            NULL     CONSTRAINT FK_SalesOrder_CancelReason REFERENCES sales.ReasonCode(Id),
    CreatedAt                datetime2(0)   NOT NULL CONSTRAINT DF_SalesOrder_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                int            NOT NULL,
    ModifiedAt               datetime2(0)   NULL,
    ModifiedBy               int            NULL,
    RowVersion               rowversion     NOT NULL,
    CONSTRAINT CK_SalesOrder_Status CHECK (Status IN (1, 2, 3, 4, 9)),
    CONSTRAINT CK_SalesOrder_Priority CHECK (ReservationPriority BETWEEN 1 AND 9),
    CONSTRAINT CK_SalesOrder_Advance CHECK (AdvancePct BETWEEN 0 AND 100 AND AdvanceAmount >= 0),
    CONSTRAINT CK_SalesOrder_HoldReason CHECK ((CreditHold = 0 AND CreditHoldReason IS NULL) OR (CreditHold = 1 AND CreditHoldReason IN (1, 2, 3))),
    CONSTRAINT CK_SalesOrder_Number CHECK (Status = 1 OR Number IS NOT NULL)
);

CREATE TABLE sales.SalesOrderLine
(
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderLine PRIMARY KEY,
    SalesOrderId       bigint         NOT NULL CONSTRAINT FK_SalesOrderLine_Order REFERENCES sales.SalesOrder(Id),
    LineNo             smallint       NOT NULL,
    LineKind           tinyint        NOT NULL CONSTRAINT DF_SalesOrderLine_Kind DEFAULT (1),
    ItemVariantId      int            NULL     CONSTRAINT FK_SalesOrderLine_Variant REFERENCES core.ItemVariant(Id),
    UomId              int            NULL     CONSTRAINT FK_SalesOrderLine_Uom REFERENCES core.Uom(Id),
    Qty                decimal(19,6)  NULL,
    QtyBase            decimal(19,6)  NULL,
    WarehouseId        int            NULL     CONSTRAINT FK_SalesOrderLine_Warehouse REFERENCES inv.Warehouse(Id),
    ListPrice          decimal(19,6)  NULL,
    PriceSource        tinyint        NULL,
    PriceListLineId    bigint         NULL     CONSTRAINT FK_SalesOrderLine_PriceLine REFERENCES sales.PriceListLine(Id),
    UnitPrice          decimal(19,6)  NULL,
    ManualDiscountPct  decimal(9,4)   NOT NULL CONSTRAINT DF_SalesOrderLine_Man DEFAULT (0),
    VatRateId          int            NULL     CONSTRAINT FK_SalesOrderLine_Vat REFERENCES core.VatRate(Id),
    NetAmount          decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrderLine_Net DEFAULT (0),
    VatAmount          decimal(19,4)  NOT NULL CONSTRAINT DF_SalesOrderLine_Vat DEFAULT (0),
    UnitCostRon        decimal(19,6)  NULL,
    MarginPct          decimal(9,4)   NULL,
    ParentLineId       bigint         NULL     CONSTRAINT FK_SalesOrderLine_Parent REFERENCES sales.SalesOrderLine(Id),
    QuoteLineId        bigint         NULL     CONSTRAINT FK_SalesOrderLine_QuoteLine REFERENCES sales.SalesQuoteLine(Id),
    ImportLineId       bigint         NULL     CONSTRAINT FK_SalesOrderLine_ImportLine REFERENCES sales.SalesOrderImportLine(Id),
    MatchStep          tinyint        NULL,
    CustomerItemCode   nvarchar(50)   NULL,
    CustomerPrice      decimal(19,6)  NULL,
    MinShelfLifeDays   smallint       NULL,
    Note               nvarchar(500)  NULL,
    CONSTRAINT UQ_SalesOrderLine UNIQUE (SalesOrderId, LineNo),
    CONSTRAINT CK_SalesOrderLine_Kind CHECK ((LineKind = 1 AND ItemVariantId IS NOT NULL AND Qty > 0 AND UomId IS NOT NULL)
                                          OR (LineKind = 2 AND ItemVariantId IS NULL AND Qty IS NULL))
);

CREATE TABLE sales.SalesOrderSchedule
(
    Id                     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderSchedule PRIMARY KEY,
    LineId                 bigint        NOT NULL CONSTRAINT FK_SalesOrderSchedule_Line REFERENCES sales.SalesOrderLine(Id),
    Seq                    smallint      NOT NULL,
    RequestedDate          date          NOT NULL,
    Qty                    decimal(19,6) NOT NULL,
    QtyBase                decimal(19,6) NOT NULL,
    PromisedDate           date          NULL,
    PromiseCalculatedAt    datetime2(0)  NULL,
    PromiseRiskDate        date          NULL,
    IsInReservationHorizon bit           NOT NULL CONSTRAINT DF_SalesOrderSchedule_Hz DEFAULT (0),
    CONSTRAINT UQ_SalesOrderSchedule UNIQUE (LineId, Seq),
    CONSTRAINT CK_SalesOrderSchedule_Qty CHECK (Qty > 0 AND QtyBase > 0)
);
```

Proceduri: `sales.usp_Quote_Save` (antet + linii + livrări prin TVP), `sales.usp_Quote_SetStatus`, `sales.usp_Quote_ExpireScan` (job), `sales.usp_Quote_RecordAcceptance`, `sales.usp_Order_Save`, `sales.usp_Order_Confirm` (scrie starea, prețurile înghețate, datele promise primite de la aplicație, semnele; rezervările se fac de aplicație prin `IStockLedger`, în aceeași tranzacție), `sales.usp_Order_Revise` (instantaneul în `SalesDocumentRevision`), `sales.usp_Order_CloseLines`, `sales.usp_Order_Cancel`, `sales.usp_Order_HorizonDue` (livrările care intră în orizont, pentru job), `sales.usp_SalesOrderDemand_Get` (TVP de variante; implementează `ISalesOrderDemand`), `sales.usp_SalesOrder_OpenLines`, `sales.usp_SalesBacklog_Forecast`, `sales.usp_OrderImport_Stage`, `sales.usp_OrderImport_CreateOrders`, `sales.usp_Proforma_Save`, `sales.usp_Proforma_Issue`, `sales.usp_Proforma_LinkPayment`, `sales.usp_DocumentRevision_Add`, `sales.usp_DocumentSend_Add`. Totalurile și valorile pe linii le calculează handlerul în C# (`SP-13`), cu `IPrecisionService`.

**Coduri de eroare V3**

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52200 | `SALES_QUOTE_EXPIRED` | 409 | Trimiterea, acceptarea sau comanda dintr-o ofertă cu `ValidUntil` depășit |
| 52201 | `SALES_DOCUMENT_LOCKED` | 409 | Editarea unei oferte sau comenzi „De aprobat”, a unei oferte trimise fără revizie, a unui document final |
| 52202 | `SALES_VARIANT_NOT_SELLABLE` | 400 | Variantă inactivă, blocată la vânzare sau nevandabilă |
| 52203 | `SALES_LINE_QTY_INVALID` | 400 | Cantitate ≤ 0; UM fără factor de conversie în setul articolului |
| 52204 | `SALES_QUOTE_LOST_REASON` | 400 | Oferta pierdută fără motiv de tip 1 |
| 52205 | `SALES_QUOTE_VALIDITY_INVALID` | 400 | `ValidUntil` înainte de data ofertei sau peste valabilitatea maximă a tipului |
| 52206 | `SALES_QUOTE_STATE_FOR_ORDER` | 409 | Comandă sau proformă dintr-o ofertă care nu e Trimisă sau Acceptată (oferta cadru: doar Acceptată) |
| 52207 | `SALES_CUSTOMER_REF_REQUIRED` | 409 | Confirmarea fără referința clientului, când clientul o cere |
| 52208 | `SALES_CUSTOMER_REF_DUPLICATE` | 409 | Referința clientului există pe altă comandă neanulată a aceluiași client |
| 52209 | `SALES_CONTACT_INVALID` | 400 | Contactul nu e al partenerului sau nu are adresă de email la trimiterea pe email |
| 52210 | `SALES_REQUESTED_DATE_INVALID` | 400 | Data cerută înaintea datei documentului |
| 52211 | `SALES_WAREHOUSE_INVALID` | 400 | Gestiunea de livrare nu e a firmei, e inactivă, blocată la ieșire, de tranzit sau custodie |
| 52212 | `SALES_IMPORT_UNRESOLVED` | 409 | Confirmarea unei comenzi importate cu linii probabile, fără potrivire sau nelistate nerezolvate |
| 52213 | `SALES_SCHEDULE_QTY_MISMATCH` | 400 | Suma cantităților pe livrări diferă de cantitatea liniei |
| 52214 | `SALES_ORDER_KEY_LOCKED` | 409 | Schimbarea clientului, a monedei sau a gestiunii după confirmare sau după prima linie preluată din sursă |
| 52215 | `SALES_LINE_BELOW_DOWNSTREAM` | 409 | Cantitatea liniei sub ce e livrat sau în dispoziții de livrare |
| 52216 | `SALES_ORDER_HAS_DOWNSTREAM` | 409 | Anularea unei comenzi cu legături în aval, active sau în ciornă |
| 52217 | `SALES_ALTERNATIVE_MULTIPLE` | 400 | Două alternative acceptate din același grup; grup fără exact o alternativă principală |
| 52218 | `SALES_PROMISE_TOO_EARLY` | 403 | Data promisă mai devreme decât cea calculată, fără `Sales.Order.PromiseOverride` |
| 52219 | `SALES_PROFORMA_OVER_SOURCE` | 409 | Proformele de avans ale sursei depășesc totalul ei cu TVA |
| 52220 | `SALES_PROFORMA_ISSUED` | 409 | Modificarea unei proforme emise |
| 52221 | `SALES_PROFORMA_HAS_PAYMENTS` | 409 | Anularea unei proforme cu încasări legate |
| 52222 | `SALES_ORDER_ADVANCE_HOLD` | 409 | Dispoziție de livrare, aviz sau factură cu livrare dintr-o comandă cu avans neîncasat |
| 52223 | `SALES_REVISION_REASON` | 400 | Revizie fără motiv |
| 52224 | `SALES_TENTATIVE_NOT_ALLOWED` | 403 | Rezervare provizorie fără `Sales.Quote.Reserve` sau pe o ofertă cadru |
| 52225 | `SALES_IMPORT_PROFILE_INVALID` | 400 | Fișierul nu se potrivește profilului (coloane lipsă, format de dată sau de număr greșit) |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.Quote.View` / `.Edit` / `.Send` / `.Cancel` | Ofertele, în limita `SalesDataScope` |
| `Sales.Quote.Reserve` | Rezervarea provizorie pe ofertă |
| `Sales.Order.View` / `.Edit` / `.Confirm` / `.Cancel` | Comenzile, în limita `SalesDataScope` |
| `Sales.Order.CloseLine` | Închiderea manuală a restului unei linii și redeschiderea |
| `Sales.Order.Import` | Importul comenzilor și rezolvarea liniilor importate |
| `Sales.Order.PromiseOverride` | Data promisă scrisă de mână, mai devreme decât calculul sau fără acoperire |
| `Sales.Order.ReleaseAdvanceHold` | Ridicarea manuală a semnului „avans” (de exemplu cu garanție sau la decizia conducerii), cu motiv |
| `Sales.Approval.Value` | Aprobarea pe valoare, la nivelul dat de `SalesApprovalRule` tip 2 |
| `Sales.Proforma.Edit` / `.Issue` / `.Cancel` | Proformele |
| `Sales.Proforma.LinkPayment` | Legarea și dezlegarea manuală a încasărilor |

Roluri livrate (V1 §8, completate): **Șef vânzări** (toate permisiunile V3, nivelul 1 de aprobare), **Key account**, **Agent de teren**, **Telesales** (oferte, comenzi și proforme pe portofoliu, fără `PromiseOverride`, `ReleaseAdvanceHold` și `Cancel` pe comenzile cu livrări), **Operator comenzi** (rol nou: `Order.Edit`, `Order.Confirm`, `Order.Import`, `Order.CloseLine` pe toată firma, fără aprobări), **Operator facturare** (`Proforma.*`, `Order.View`), **Contabil șef** (`Proforma.LinkPayment`, `Order.ReleaseAdvanceHold`, vizualizare).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V3 |
| --- | --- |
| Oferta și acceptarea (Codul civil, art. 1182–1196) | Oferta cu termen e irevocabilă până la expirare (art. 1191): prețurile ofertei trimise nu se schimbă în valabilitate (regula 2). Acceptarea cu modificări (cantitate mai mică, altă dată) e o contraofertă (art. 1197): se înregistrează ca acceptare parțială sau ca revizie, iar confirmarea noastră încheie contractul |
| Proforma (Codul fiscal) | Nu e factură: nu are regim fiscal, nu se raportează în e-Factura, nu generează TVA colectat. PDF-ul o spune explicit |
| Factura de avans (Codul fiscal, art. 282 alin. (2) lit. b și art. 319 alin. (16)) | TVA-ul devine exigibil la încasarea avansului; factura se emite cel târziu pe 15 ale lunii următoare încasării. V3 doar semnalează (4.6); emiterea e în V5 (`FV-AVS`), iar e-Factura se transmite în termenul din `EInvoiceSendDeadlineDays` (V1) |
| Termenele de plată (Legea 72/2013) | Termenul de pe ofertă și comandă peste 60 de zile primește avertizarea din V1; peste termenul din condițiile clientului, aprobare (3.8) |
| Referința comenzii în e-Factura | `CustomerOrderRef` trece în `cac:OrderReference/cbc:ID` (BT-13); obligatorie pentru clienții care o cer |
| EDI | EDIFACT `ORDERS` D96A (EANCOM) la intrare și, opțional, `ORDRSP` la confirmare, prin furnizorul EDI al firmei (fișiere într-un folder sau prin API-ul lui); fără componente plătite în produs, doar conectorul de fișiere. UBL Order 2.1 ca alternativă |
| GDPR | Ofertele către persoane fizice păstrează doar datele necesare (nume, adresă, email); emailurile trimise se păstrează cu documentul, cu retenția firmei |

Integrări interne: Nucleu (tranzacții, legături, asistentul, serii, aprobări, notificări, outbox de email, calendarul), V1 (condiții, puncte de livrare, catalogul clientului, creditul), V2 (motorul de preț și excepțiile), Stocuri S5 (rezervări, disponibil proiectat), Achiziții (prin S5: comenzile la furnizor confirmate), F1/F2/F3 (încasări, expunere, restanțe), F5 (`ISalesBacklogForecast`), V4 și V5 (consumă comanda și proforma).

**Precizări aplicate altor documente la validarea V3 (27.11.2026):**

- **V1 §3.4** (`CustomerTerms`): coloanele noi `ReservationPriority` (1–9, implicit 5) și `RequiredAdvancePct`. **V1 §3.9**: setările din 3.12. **V1 §3.2**: tranzacțiile `DL-CV`, `AV-CV` și `FV-LIV` refuză și comanda cu `AdvanceHold` (`52222`), pe lângă `CreditHold`. **V1 §3.8**: 3 motive noi în seed (tipul 1 `FARARASP` „Fără răspuns de la client”; tipul 7 `CLIENTANUL` „Clientul a anulat comanda” și `DUBLURA` „Comandă dublă”), deci 28 de motive.
- **V1 §6** (`ISalesOrderDemand`): rândurile se dau pe livrare, cu data cerută a livrării, nu pe comandă.
- **V2 §3.9**: `SalesApprovalRule` primește tipul 2 (3.8), cu cele 4 reguli livrate.
- **Stocuri S5 §3.2**: prioritatea rezervării vine din `CustomerTerms.ReservationPriority`; Vânzările trimit `ExpiresAt` = data cerută a ultimei livrări rezervate + `ReservationExpiryDays`; comenzile de vânzare rezervă ferm doar pe orizontul `ReservationHorizonDays`.
- **F5 §4.4**: componenta nouă „Comenzi de la clienți (portofoliu)”, din `ISalesBacklogForecast`, la data promisă + termenul de plată, cu întârzierea medie a clientului; comenzile cu `CreditHold` pe rând separat, în afara soldului.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Pipeline de oferte | Ofertele deschise pe agent, client și lună, cu valoarea principală și maximă, vechimea și „expiră curând” | Șef vânzări, agenți |
| Oferte câștigate și pierdute | Rata de conversie (număr și valoare) pe agent, grupă, articol; motivele pierderii și concurenții; timpul de la cerere la ofertă și de la ofertă la comandă | Șef vânzări, Director comercial |
| Comenzi de livrat (portofoliu) | Restul de livrat pe client, articol, gestiune și săptămână, cu rezervat, în așteptare și fără acoperire | Șef vânzări, Șef depozit, Planificare |
| Comenzi în risc și întârziate | Livrările cu data promisă depășită sau cu data calculată după cea promisă, cu cauza (lipsă stoc, credit, avans) | Agenți, Șef vânzări |
| Comenzi blocate | Comenzile cu `CreditHold` sau `AdvanceHold`, valoarea, vechimea, cine trebuie să decidă | Șef vânzări, Contabil șef |
| Livrare la timp și completă (prognoză) | Pentru comenzile confirmate: procentul de livrări cu data promisă = data cerută; realizatul vine în V7 | Director comercial |
| Proforme neîncasate | Proformele emise și neîncasate, scadența, comanda legată | Agenți, Operator facturare |
| Importuri | Mesajele primite, dublurile, liniile probabile și timpul până la confirmare | Operator comenzi |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v3-oferte-comenzi/](ecrane/v3-oferte-comenzi/). Meniul Vânzări are Comenzi (sus) și grupul Oferte (Oferte, Proforme).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Oferte | Lista ofertelor cu stările, valoarea, valabilitatea, „expiră curând”, „fără răspuns”, cererile fără ofertă; filtre pe agent, client, stare | `v3-01-lista-oferte` |
| Ofertă | Formularul ofertei `OV 0197` Hyper Nord: antetul (cererea, valabilitatea, lista, data prețului), liniile cu alternativa și opționala, livrările, totalul și valoarea maximă, aprobarea, rezervarea provizorie | `v3-02-oferta` |
| Acceptarea ofertei | Fereastra „Clientul acceptă” pe `OV 0197`: liniile bifate, alternativa aleasă, cantitatea acceptată, referința clientului; butonul „Acceptă și creează comanda” | `v3-03-acceptare-oferta` |
| Comenzi | Lista comenzilor cu stările și semnele (acoperită, în risc, întârziată, credit, avans), valoarea și data promisă | `v3-04-lista-comenzi` |
| Comandă | Formularul comenzii `CV 4452`: antetul (punct de livrare, referință, gestiune), liniile cu rezervat și în așteptare, livrările cu datele cerute și promise, semnele, legăturile | `v3-05-comanda` |
| Livrări și data promisă | Pe linia CEAI05 a `CV 4452`: disponibilul proiectat pe zile, intrările confirmate, cererile cu prioritate mai mare, data promisă și propunerea de împărțire | `v3-06-data-promisa` |
| Confirmarea comenzii | Verificările la confirmare pentru `CV 4452`: referință, prețuri, aprobare acoperită de ofertă, credit cu restanță, rezervare, date promise; „Confirmă” cu semnele | `v3-07-confirmare` |
| Import comenzi | Importul EDI Mega Retail din 27.11: mesajele, dublura, liniile cu pasul de potrivire, linia probabilă, linia nelistată, diferența de preț | `v3-08-import-comenzi` |
| Revizii | Compararea reviziilor `OV 0197` (0 și 1): câmpurile schimbate, motivul, reaprobarea, trimiterea | `v3-09-revizii` |
| Proformă | Formularul proformei de avans `PF 0092` (Pensiunea Arieșul): sursa, procentul, repartizarea pe cote, conturile, încasarea legată | `v3-10-proforma` |
| Aprobare pe valoare | Cererea de aprobare a `OV 0197` văzută de Radu Stan: valoarea principală și maximă, pragul depășit, marjele, expunerea clientului | `v3-11-aprobare-valoare` |
| Configurare › Oferte și comenzi | Setările din 3.12 și pragurile pe valoare (`SalesApprovalRule` tip 2) | `v3-12-configurare` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V3-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V3-A1 | `OV 0197`: totalul 125.172,00 lei net, TVA 16.860,12, total 142.032,12; valoarea maximă 153.612,00 lei (opționala inclusă, o singură alternativă); cere nivelul 1 pentru Key account (prag 150.000) | Test unitar |
| V3-A2 | Oferta trimisă își păstrează prețurile după `MPV 0014` (01.12); `CV-OV` din 27.11 sau din 10.12 copiază 1,73 și 2,93, nu prețurile noi | Test de integrare |
| V3-A3 | Acceptarea cu două alternative din grupul A e refuzată (`52217`); acceptarea opționalei cu 4.800 din 7.200 lasă restul informativ în consumul `ORDER` | Test de integrare |
| V3-A4 | `ExpireQuotes` trece oferta în Expirată la `ValidUntil` + 1 zi și eliberează rezervările provizorii; comanda din ea e refuzată (`52200`); revizia cu valabilitate nouă o readuce în Trimisă | Test de integrare (cu S5) |
| V3-A5 | `CV 4452` din `OV 0197` nu cere aprobare (144.132,00 ≤ 153.612,00); revizia cu +2.400 APA2N (153.612,00) nu cere; una cu +2.401 cere | Test de integrare |
| V3-A6 | Confirmarea `CV 4452`: expunerea 358.977,72 lei, restanța de 34 de zile dă `CreditHold` (motivul 2); `FacturaAchitata` pe `FV EXD 10142` ridică semnul automat | Test de integrare (cu F3) |
| V3-A7 | Orizontul de 14 zile: la confirmarea din 27.11 se rezervă doar tranșa din 08.12; jobul din 01.12 rezervă tranșa din 15.12; `ExpiresAt` = 22.12 | Test de integrare (cu S5) |
| V3-A8 | Data promisă: APA05 08.12 (intrarea confirmată din 04.12 + 1 zi), CEAI05 11.12 (intrarea din 10.12); o comandă la furnizor neconfirmată nu dă dată promisă; o dată mai devreme e refuzată fără drept (`52218`) | Test unitar (algoritm) + integrare |
| V3-A9 | Importul Mega Retail: 3 mesaje → 2 comenzi în ciornă, 1 dublură (`52208`); linia „APA PLATA 2L” e probabilă și blochează confirmarea (`52212`) până la confirmare, după care catalogul învață rândul | Test de integrare |
| V3-A10 | `CV 4449` cu avans 50 %: proforma `PF 0092` de 3.123,21 lei, repartizată 2.055,00 + 226,05 + 696,00 + 146,16; avizul e refuzat cât timp `AdvanceHold` = 1 (`52222`); încasarea cu „PF 0092” în detalii ridică semnul | Test de integrare (cu F2) |
| V3-A11 | Anularea unei comenzi cu o dispoziție de livrare în ciornă e refuzată (`52216`); închiderea restului eliberează rezervarea prin `ReleaseAsync` | Test de integrare (cu S5) |
| V3-A12 | Revizia păstrează instantaneul complet al versiunii anterioare; reviziile nu se pot modifica sau șterge (`SP-21`) | Test de integrare |
| V3-A13 | Confirmarea și rezervarea sunt atomice: o eroare la rezervare anulează confirmarea și nu lasă rezervări orfane; două confirmări simultane ale aceleiași comenzi dau una refuzată (`RowVersion`) | Test de integrare (concurență) |
| V3-A14 | `ISalesOrderDemand` dă restul pe livrare, fără partea livrată și închisă; `ISalesBacklogForecast` dă `CV 4452` la data promisă + 45 de zile, pe rând „blocat” cât are `CreditHold` | Test de integrare |
| V3-A15 | Toate procedurile V3 rulează corect pe baza case sensitive; referința `hn-po-2026-4471` nu e considerată dublura lui `HN-PO-2026-4471` la import, dar căutarea o găsește | Test de integrare |

Criteriile V3-A2, V3-A6, V3-A7 și V3-A13 sunt critice: ele decid prețul garantat clientului, blocarea livrărilor către clienții cu probleme de plată, folosirea corectă a stocului și consistența între comandă și rezervări.

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Legătura cu oportunitatea**: coloana nouă `sales.SalesQuote.OpportunityId bigint NULL` (fără FK între scheme, index filtrat), câmpul „Oportunitate” pe formularul ofertei (propune oportunitățile deschise ale partenerului prin portul opțional `ICrmOpportunityQuery`) și grupul de consum `QUOTE_FROM_OPP` pentru oferta creată din oportunitate. [CR3 §3.3](../12-crm/CR3-OPORTUNITATI-PIPELINE.md)
- **Evenimentele** `OfertaTrimisa` și `OfertaInchisa` primesc câmpul `OpportunityId` (v1 compatibil, câmp opțional nou); `OfertaInchisa` primește și `CompetitorId`.
- **Concurentul**: `CompetitorId` opțional lângă `CompetitorName` (textul rămâne pentru concurenții nelistați).
- **Închiderea din CRM**: comanda `CloseQuoteLost` poate fi apelată și din fereastra „Închide ca pierdută” a oportunității, cu același motiv și concurent.
- **`ComandaConfirmata`** conține deja `SourceQuoteId`; CRM-ul îl folosește pentru câștigul automat al oportunității.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Stările ofertei | Ciornă → De aprobat → Aprobată → Trimisă → Acceptată / Pierdută / Expirată → Închisă, plus Anulată; schimbările după trimitere sunt revizii numerotate, cu instantaneu | 3.2, 3.7 | ✓ DA (27.11.2026) |
| 2 | Cererea de ofertă a clientului | Fără document separat: se înregistrează direct ca ofertă în ciornă, cu referința, data și sursa cererii; lista arată „cereri fără răspuns” | 3.1, 4.1 | ✓ DA (27.11.2026) |
| 3 | Alternative și linii opționale pe ofertă | Da: grupuri de alternative (una principală, în total) și linii opționale (în afara totalului); acceptarea alege alternativa și cantitatea pe linie | 2 regula 3, 3.3, 4.3 | ✓ DA (27.11.2026) |
| 4 | Oferta cadru | Tip de ofertă „Cadru” (acord de cantitate și preț până la 366 de zile), din care se fac comenzi repetate cu `CV-OV`; fără tranzacție nouă și fără rezervare | 3.1, 4.9 | ✓ DA (27.11.2026) |
| 5 | Prețurile ofertei | Garantate până la `ValidUntil` și copiate în comandă, chiar dacă lista se schimbă; prelungirea cere revizie, cu alegerea „păstrează” sau „recalculează” | 2 regula 2, 4.2, 4.3 | ✓ DA (27.11.2026) |
| 6 | Aprobarea pe valoare | `SalesApprovalRule` tip 2: prag pe valoare și pe zilele de termen peste condiții, pe rol și nivel (agent 30.000, key account 150.000, șef vânzări 500.000 lei); pe ofertă se compară valoarea maximă posibilă; comanda dintr-o ofertă aprobată nu se reaprobă în limita valorii aprobate | 2 regula 4, 3.8 | ✓ DA (27.11.2026) |
| 7 | Stările comenzii și confirmarea | Ciornă → (De aprobat) → Confirmată → Închisă, plus Anulată; confirmarea către client e PDF sau EDI din comandă, nu un document separat; comanda cu probleme de credit se confirmă cu `CreditHold` și nu se livrează | 3.5, 4.4 | ✓ DA (27.11.2026) |
| 8 | Când se rezervă marfa | Ferm la confirmare, dar doar pentru livrările din următoarele 14 zile; restul se rezervă automat zilnic când intră în orizont; ce nu are stoc rămâne în așteptare; expirarea se socotește de la data cerută; rezervarea provizorie pe ofertă, opțională | 2 regula 6, 3.9 | ✓ DA (27.11.2026) |
| 9 | Data promisă | Calculată din disponibilul proiectat, doar cu intrări confirmate, plus 1 zi de pregătire și zilele de livrare ale punctului; mai devreme doar cu drept; o schimbare ulterioară dă alertă „în risc”, nu o dată nouă trimisă automat clientului | 2 regula 7, 3.9 | ✓ DA (27.11.2026) |
| 10 | Importul comenzilor | Excel, CSV cu profil, EDIFACT ORDERS și UBL Order; un mesaj = o comandă `CV-IMP` în ciornă; dublurile de referință se sar; confirmare automată doar fără nicio problemă | 3.10, 4.5 | ✓ DA (27.11.2026) |
| 11 | Proforma și avansul | Proformă integrală sau de avans (procent sau sumă, pe cote de TVA), fără efect fiscal; avansul cerut pe client blochează livrarea până la încasare; încasarea se leagă automat după numărul proformei din detaliile plății; factura de avans o emite V5 | 2 regula 9, 3.11, 4.6 | ✓ DA (27.11.2026) |
| 12 | Prognoza de trezorerie | Comenzile confirmate și nefacturate și proformele emise intră în F5 ca „Comenzi de la clienți (portofoliu)”, la data promisă + termenul de plată, cele blocate pe rând separat | 6, 9 | ✓ DA (27.11.2026) |
| 13 | Nomenclatoare și precizări | 3 motive noi în seed (fără răspuns; clientul a anulat; comandă dublă), coloanele noi pe client (prioritatea rezervării, avansul cerut) și precizările din 9 pentru V1, V2, S5 și F5 | 3.12, 9 | ✓ DA (27.11.2026) |

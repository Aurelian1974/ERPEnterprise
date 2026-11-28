# ERP · 02.03 Vânzări · V6 Retururi, storno și reduceri ulterioare

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**. A șasea din cele 7 părți ale modulului 02.03 Vânzări: [V1 Configurare](V1-CONFIGURARE.md) ✓ · [V2 Prețuri și discounturi](V2-PRETURI-DISCOUNTURI.md) ✓ · [V3 Oferte, comenzi și rezervări](V3-OFERTE-COMENZI.md) ✓ · [V4 Livrare](V4-LIVRARE.md) ✓ · [V5 Facturare și e-Factura](V5-FACTURARE.md) ✓ · **V6 Retururi, storno și reduceri** ✓ · V7 Rapoarte.

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`. V6 folosește `52700–52799`.

Această parte acoperă tot ce micșorează o vânzare după ce a fost făcută:

- **returul de la client**: marfa care se întoarce, cu verificarea calității și decizia pe fiecare linie (repusă în stoc, casată, returnată furnizorului);
- **factura de stornare** pentru retur, pentru o eroare sau pentru o factură emisă de două ori, inclusiv **storno și refacturare**;
- **reducerile ulterioare** fără retur: diferența de preț, rabatul pe rulaj (V2 §3.11), bonusul comercial;
- **returul ambalajelor cu garanție SGR**;
- **aprobarea** notelor de credit pe valoare;
- **transmiterea în e-Factura** a notelor de credit.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile** sunt definite în V1 §3.2:

  | Tranzacție | Ce face |
  | --- | --- |
  | `RC-AV` | Retur din aviz, consumă grupul `RETURN`, intră la costul ieșirii inițiale |
  | `RC-MAN` | Retur fără sursă, cu dreptul `Sales.Return.WithoutSource`, intră la costul curent |
  | `FS-FV` | Storno din factură, consumă grupul `CREDIT` |
  | `FS-RC` | Factură de stornare pentru un retur, consumă grupul `CREDIT` pe factura inițială, prin retur |
  | `FS-RED` | Reducere comercială ulterioară, fără retur |

  V6 adaugă `RC-SGR` (returul ambalajelor SGR, 3.7). Toleranța e interzisă pe toate: nu se returnează și nu se stornează peste ce s-a livrat sau facturat.
- **Stocul se mișcă doar prin retur**, sincron, prin `IStockLedger.PostAsync` cu tipul `RETURN_FROM_CUSTOMER` (Stocuri S2): intrarea are costul și lotul ieșirii inițiale. Nota de credit nu mișcă stocul.
- **Nota de credit e tot un `SalesInvoice` din punct de vedere fiscal.** Are aceeași serie (implicit) și aceeași numerotare la validare (V5 regula 5). Publică `FacturaEmisa` cu `IsReversal` = 1 și `ReversedInvoiceId` (F3, fluxul 4.1). În e-Factura e o notă de credit (tip 381) sau o factură cu valori negative (380), după setarea V1 `EInvoiceCreditNoteFormat`, cu referința la factura inițială (BT-25).
- **TVA-ul notei de credit e cel al facturii inițiale:** aceeași cotă și aceeași categorie, chiar dacă între timp cota s-a schimbat (Codul fiscal art. 287 și 291 alin. (5)).
- Avizele, facturile și prețurile sunt cele din V4, V5 și V2. Motivele sunt în `sales.ReasonCode` (V1 §3.8): tipul 3 retur, tipul 4 storno, tipul 8 diferențe la livrare.
- **Ce NU face V6:**
  - nu stornează avizul nefacturat (V4, `AV-STO`) și nu tratează refuzul la livrare (V4 §3.8);
  - nu rambursează bani (F2) și nu compensează (F3);
  - nu face reclamația ca proces (CRM 02.12, care poate porni returul);
  - nu face returul către furnizor (Achiziții A4; V6 doar propune decizia „retur la furnizor”).

## 1. Scop și perimetru

| Zonă | Ce face V6 | Ce primește de la alții |
| --- | --- | --- |
| Retur de la client | Anunțul (opțional, cu aprobare), recepția la depozit, intrarea în gestiunea de retur, verificarea calității, decizia pe linie | Avizul și loturile (V4), costul ieșirii (S2/S3), stările lotului (S4) |
| Factura de stornare pentru retur | Cantitățile acceptate × prețurile facturii inițiale, pe aceleași cote; plafonată la ce s-a facturat | Factura inițială (V5) |
| Storno din factură | Integral sau parțial, pe linii sau pe valoare, cu motiv; storno și refacturare | Factura inițială |
| Reduceri ulterioare | Diferența de preț pe linii de factură, rabatul pe rulaj la sfârșitul perioadei, bonusul comercial pe valoare; repartizarea pe cote | Acordurile de rabat (V2 §3.11), facturile perioadei |
| Ambalaje SGR | Returul ambalajelor goale cu garanție și creditarea garanției | Articolele de garanție (Nucleu `DepositItemId`) |
| Aprobare | Notele de credit peste plafonul rolului, cu motivul și efectul asupra marjei | Regulile de aprobare (`SalesApprovalRule`, V2 §3.9) |
| e-Factura și partida | Nota de credit în e-Factura (381 sau 380 negativ), cu referința; efectul în partida clientului | Integrări 02.14 (V5 §3.7), F3 |

## 2. Model de domeniu

```
 SalesDeliveryNote (validat, livrat) ──RC-AV──► CustomerReturn ─► CustomerReturnLine × N ─► CustomerReturnLot × N (lotul și costul ieșirii)
 manual (cu drept) ──────────────────RC-MAN──►      │ validare: IStockLedger.PostAsync (RETURN_FROM_CUSTOMER, în gestiunea de retur)
 ambalaje SGR goale ─────────────────RC-SGR──►      │ verificarea calității ─► decizia pe linie: repune în stoc · casare · retur la furnizor · reparație
                                                    └──FS-RC──► SalesInvoice (IsReversal, credit) ◄──FS-FV── SalesInvoice (validată): storno integral sau parțial
 RebateAgreement (perioadă încheiată) ──decont──► RebateSettlement ──FS-RED──► SalesInvoice (IsReversal, reducere) ◄──FS-RED── diferență de preț / bonus pe factură
 FS-FV integral ─► avizele redevin „de facturat” ─► FV-AV nou (refacturare)
```

Reguli de domeniu:

1. **Marfa returnată intră întâi în gestiunea de retur, nu direct în stocul vandabil.** Fiecare gestiune de livrare are o gestiune de retur asociată (`ReturnWarehouseId`, Stocuri S1), iar returul validat intră acolo, pe lotul livrat. Gestiunea de retur ține loc de carantină: e exclusă din disponibil (S5) și nu se vinde din ea, iar starea lotului nu se schimbă, ca restul lotului din alte gestiuni să rămână vandabil (S4). Responsabilul de calitate decide apoi pe fiecare linie (`52711` până la decizie):
   - repunerea în stoc vandabil, prin transfer;
   - casarea, prin descărcare `SCRAP` (S2);
   - returul la furnizor (Achiziții A4);
   - păstrarea pentru reparație.

   La firmele mici, decizia „repune în stoc” se poate lua direct la recepție (`ReturnQualityCheck` = 0) (decizia 1).
2. **Returul intră la costul la care a ieșit.** Returul din aviz folosește lotul și costul unitar al ieșirii inițiale (S2 `RETURN_FROM_CUSTOMER`), ca marja să se anuleze exact. Returul fără sursă intră la costul curent (S3) și cere drept separat și motiv.
3. **Nu se returnează și nu se creditează mai mult decât s-a livrat și s-a facturat.** Returul consumă grupul `RETURN` pe linia de aviz (`50027` peste rest). Nota de credit consumă `CREDIT` pe linia de factură, iar suma tuturor notelor pe o linie nu depășește valoarea ei (`52720`). Un retur pe un aviz încă nefacturat nu are notă de credit: scade cantitatea de facturat a avizului (regula 6).
4. **Nota de credit copiază prețurile și TVA-ul facturii inițiale.** Prețul unitar, discounturile și cota sunt cele de pe linia facturii, nu cele de azi. O notă de credit nu amestecă linii din facturi ale altor clienți (`52721`). Poate stinge mai multe facturi ale aceluiași client, cu o referință pe fiecare linie.
5. **Factura validată nu se modifică, se stornează.** Corecția unei facturi validate în e-Factura se face cu o notă de stornare, integrală sau parțială. Pentru o eroare de date (client, referință, termen), se face **storno integral și refacturare**: stornarea eliberează avizele, care redevin „de facturat”, iar asistentul propune imediat factura nouă cu datele corectate (decizia 3). Corecția după o respingere ANAF rămâne în V5 §3.7.
6. **Returul pe un aviz nefacturat micșorează factura viitoare, nu creează notă de credit.** Dacă avizul nu e facturat, returul scade restul `INVOICE` al avizului, iar factura din V5 se face pe diferență. Dacă avizul e facturat parțial, returul se acoperă întâi din partea nefacturată (decizia 4).
7. **Reducerile fără retur se repartizează pe cote.** O reducere pe valoare (rabat, bonus) se împarte pe cotele de TVA ale facturilor la care se referă, proporțional cu baza lor. O reducere pe linie (diferență de preț) are cota liniei. Nota de reducere are întotdeauna referința la factura sau la perioada facturilor (e-Factura BG-3 sau BG-14) (decizia 5).
8. **Nota de credit peste plafon cere aprobare.** Notele `FS-FV` și `FS-RED` peste plafonul rolului (`SalesApprovalRule` tipul 3) intră în De aprobat înainte de validare. `FS-RC` dintr-un retur recepționat și verificat nu cere aprobare, pentru că marfa s-a întors fizic (decizia 7).

## 3. Entități, stări și setări

Convențiile comune tabelelor `sales` sunt cele din V1 §3. Seriile:

| Serie | Pentru ce |
| --- | --- |
| `RC` | Retururi; nefiscală, avizul de retur e documentul clientului |
| `FV EXD` | Notele de credit, implicit aceeași serie ca facturile; opțional o serie separată, `FS EXD`, setarea `CreditNoteSeries` |

### 3.1 Returul (`CustomerReturn`, `CustomerReturnLine`, `CustomerReturnLot`)

| Câmp (antet) | Reguli |
| --- | --- |
| `Number`, `DocDate`, `TransactionTypeId` | Seria `RC`; `RC-AV`, `RC-MAN`, `RC-SGR` |
| `PartnerId`, `DeliveryPointId` | Clientul și punctul de unde vine marfa |
| `CustomerDocRef`, `CustomerDocDate` | Avizul de retur emis de client, dacă e plătitor de TVA și îl emite, sau procesul-verbal de constatare |
| `WarehouseId` | Gestiunea de retur care primește marfa (din `ReturnWarehouseId` al gestiunii de livrare) |
| `ReasonId` | Motivul principal (tipul 3); fiecare linie poate avea motivul ei |
| `ComplaintRef` | Reclamația din CRM, dacă returul pornește de acolo |
| `RequiresPickup`, `PickupPlanId` | Marfa se ridică de la client cu o mașină a firmei: returul intră în planul de livrare (V4 §3.4) ca oprire de ridicare |
| `Status` | 1 Ciornă · 2 Aprobat (autorizarea returului, opțională) · 3 Recepționat (marfa a intrat în gestiunea de retur) · 5 Verificat (toate liniile au decizie) · 4 Închis (creditat sau scăzut din facturare) · 9 Anulat |

**Linie**:

| Câmpuri | Reguli |
| --- | --- |
| `DeliveryNoteLineId` (gol la `RC-MAN`), `ItemVariantId`, `UomId` | Sursa și articolul |
| `QtyAnnounced`, `QtyReceived` | Cantitatea anunțată de client și cea primită la depozit |
| `ReasonId` | Motivul liniei |
| `Disposition` | 1 repune în stoc · 2 casare · 3 retur la furnizor · 4 reparație · 5 în așteptare |
| `DispositionQty`, `DispositionBy`, `DispositionAt` | Cine a decis, când și pentru ce cantitate |
| `CreditQty` | Cât se creditează. Implicit cantitatea primită; poate fi mai mică dacă marfa e deteriorată din vina clientului și contractul nu permite creditarea |
| `UnitPrice`, `VatRateId` | Copiate de pe linia facturii inițiale sau, dacă avizul nu e facturat, de pe aviz |
| `UnitCostRon` | Costul ieșirii inițiale |

**Lot**: `LineId`, `LotId`, `SerialId`, `QtyBase`. Lotul trebuie să fie unul livrat pe avizul sursă (`52705`), ca trasabilitatea să rămână corectă.

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează; se cere aprobarea dacă politica o impune | → Aprobat; → Recepționat (fără aprobare); → Anulat |
| 2 Aprobat | Clientul a primit numărul returului (RMA); se planifică ridicarea | → Recepționat; → Anulat (expirat după `ReturnApprovalValidDays`) |
| 3 Recepționat | Marfa e în gestiunea de retur, în carantină; se înregistrează deciziile | → Verificat |
| 5 Verificat | Se generează nota de credit (`FS-RC`) sau se scade din facturare | → Închis |
| 4 Închis | Final | — |

### 3.2 Validarea returului (recepția)

Într-o singură tranzacție SQL, în ordine:

1. **Verificările de sursă:**
   - avizul e validat;
   - cantitățile nu depășesc restul `RETURN` (`50027`);
   - loturile sunt cele livrate (`52705`);
   - returul e în termenul politicii clientului (`ReturnPolicy.MaxDays`, `52703`), cu excepție pe drept;
   - `RC-MAN` cere drept și motiv (`52702`).
2. **Mișcarea de stoc:** `IStockLedger.PostAsync` cu `RETURN_FROM_CUSTOMER` în gestiunea de retur, cu `SourceLineId` = linia avizului. S2 preia costul ieșirii inițiale.
3. **Carantina:** cu `ReturnQualityCheck` = 1, marfa rămâne în gestiunea de retur, exclusă din disponibil, până la decizie. Altfel, cu decizia „repune în stoc” luată la recepție, se face transferul în gestiunea vandabilă în aceeași tranzacție.
4. **Legăturile și starea:** legătura `RETURN`, istoricul și starea Recepționat.
5. **Evenimentul** `ReturClientReceptionat` în outbox.

**Deciziile** (`RecordDisposition`) se iau după validare și fiecare face mișcarea ei prin S2:

| Decizie | Mișcare |
| --- | --- |
| Repune în stoc | Transfer din gestiunea de retur în gestiunea vandabilă și lotul trece în Disponibil (S4). Un lot sub termenul minim al clienților rămâne vandabil doar pentru clienții fără cerință |
| Casare | Descărcare `SCRAP` cu procesul-verbal, cu pragul de aprobare al S2 |
| Retur la furnizor | Propunere de retur către furnizor în Achiziții A4, cu lotul și motivul |
| Reparație | Rămâne în gestiunea de retur, cu semnul „în reparație” |

### 3.3 Nota de credit (`SalesInvoice` cu `IsReversal` = 1)

V6 folosește entitatea facturii din V5 §3.1, cu:

| Câmpuri | Reguli |
| --- | --- |
| `CreditKind` | 1 storno integral · 2 storno parțial pe linii · 3 retur (`FS-RC`) · 4 diferență de preț · 5 rabat pe rulaj · 6 bonus comercial pe valoare · 7 garanție SGR |
| `ReversedInvoiceId` | Factura inițială, când e una singură |
| `ReasonId` | Motivul, tipul 4 sau 3 |
| `ApprovalStatus` | Starea aprobării, pe tipurile din regula 8 |
| `RebateSettlementId` | Decontul de rabat, la `CreditKind` = 5 |

Liniile au `SourceKind` 7 (linie de factură stornată), 8 (linie de retur) sau 9 (reducere pe valoare). `OriginalInvoiceLineId` sau `OriginalInvoiceId` păstrează referința pentru e-Factura (BT-25, BT-26).

Valorile sunt negative pe toată nota: cantitate negativă pentru storno și retur, valoare negativă pentru reduceri. `AmountDue` e negativ. F3 o tratează ca partidă de credit și o alocă automat pe factura stornată, dacă aceasta mai are rest de plată (F3 fluxul 4.1). Altfel, rămâne credit disponibil pentru compensare sau rambursare.

**Date fiscale:**

- **Data notei:** ziua validării.
- **Cota de TVA:** cea a facturii inițiale (`52722` la încercarea de a schimba cota).
- **Exigibilitatea:** la data notei pentru reduceri și storno. Ajustarea bazei de impozitare se face în perioada în care apare evenimentul (Codul fiscal art. 287), iar decontul (C4) o ia din data notei.
- **TVA la încasare:** pentru o factură inițială cu TVA la încasare, nota e tot cu TVA la încasare (`4428`).

| Stare | Ce se poate face | Treceri |
| --- | --- | --- |
| 1 Ciornă | Se editează cantitățile sau valorile, în limita rămasului; se scot linii | → De aprobat (dacă e peste plafon); → Validată |
| 2 De aprobat | Aprobatorul vede factura inițială, motivul, efectul asupra marjei și istoricul clientului | → Ciornă (respinsă, cu motiv); → Validată |
| 3 Validată | Ca factura (V5): tipărire, email, e-Factura | — |

### 3.4 Storno integral și refacturare

„Stornează și refacturează” pe o factură validată face două lucruri:

1. Creează nota `FS-FV` integrală. La validare, consumul `INVOICE` al facturii inițiale pe avize și pe comandă se anulează prin legătura `CREDIT`, deci avizele redevin „de facturat”.
2. Deschide asistentul V5 cu aceleași surse, cu datele de antet ale facturii stornate copiate și editabile: client, plătitor, referințe, termen, adresă.

Factura nouă se face cu prețurile avizelor (V5 regula 3), la data de azi, cu `TaxPointDate` = data livrărilor.

Dacă avizele sunt din altă lună decât cea curentă, factura nouă tot nu amestecă lunile (V5 `52501`). TVA-ul a fost declarat o dată, în luna livrării (V5 `UninvoicedVatAtMonthEnd` = 1). Stornarea și refacturarea în aceeași lună se anulează în decont (C4 vede ambele documente).

### 3.5 Reducerile ulterioare (`FS-RED`)

| Tip | Sursa | Calcul | Cota |
| --- | --- | --- | --- |
| Diferență de preț | Linii din una sau mai multe facturi ale clientului | (preț facturat − preț corect) × cantitate, pe fiecare linie | Cota liniei |
| Rabat pe rulaj | Decontul acordului (`RebateSettlement`), la sfârșitul perioadei | Procentul pragului atins × rulajul perioadei (V2 §3.11), minus rabaturile deja acordate în perioadă | Repartizat pe cotele facturilor din perioadă, proporțional cu baza |
| Bonus comercial pe valoare | Una sau mai multe facturi, sau o perioadă | Suma negociată | Repartizat proporțional pe cotele facturilor de referință |

**Decontul de rabat** (`RebateSettlement`): `AgreementId`, `PeriodFrom`, `PeriodTo`, `TurnoverNet` (facturat minus storno și retururi), `TierPct`, `RebateAmount`, `AlreadyGranted`, `ToGrant`, defalcarea pe cote, `Status` (Calculat · Aprobat · Notă emisă). Jobul de la începutul lunii calculează deconturile perioadelor încheiate. Șeful de vânzări le aprobă, apoi nota `FS-RED` se generează din decont, cu perioada facturilor ca referință (e-Factura BG-14 `InvoicingPeriod`) și lista facturilor în anexa PDF.

Contarea reducerii folosește contul `709` „Reduceri comerciale acordate” (cheia `SalesDiscount`, C2), nu `707`, ca venitul brut să rămână vizibil.

### 3.6 Politica de retur (`ReturnPolicy`)

| Câmp | Reguli |
| --- | --- |
| `Code`, `Name` | De exemplu „HoReCa standard”, „Retail cu nevândut” |
| `MaxDays` | Zile de la livrare în care returul e acceptat fără excepție (`52703`) |
| `RequiresApproval` | Returul cere aprobare (stare Aprobat) înainte de ridicare |
| `ApprovalValidDays` | Cât e valabilă autorizarea |
| `AllowedReasons` | Motivele acceptate. De exemplu, „nevândut” doar pentru clienții cu acord de retur comercial |
| `RestockingFeePct` | Taxa de repunere în stoc pentru returul comercial (nu pentru defecte), facturată pe aceeași notă ca linie pozitivă de serviciu |
| `CreditMode` | 1 notă de credit pe retur · 2 notele de credit adunate pe client la sfârșitul săptămânii sau al lunii (ca gruparea facturilor) |

Politica se leagă de grupa comercială sau de client (`CustomerTerms.ReturnPolicyId`, coloană nouă în V1 §3.4). La activarea modulului se creează politica implicită a firmei, „Standard” (30 de zile, fără aprobare prealabilă, toate motivele în afară de „nevândut”, fără taxă, notă pe retur), editabilă.

### 3.7 Ambalajele SGR

Pentru articolele cu ambalaj în sistemul de garanție-returnare (Nucleu `Item.DepositItemId`, Legea 249/2015 și HG 1074/2021), documentele de vânzare V3–V5 adaugă automat linia de garanție, de 0,50 lei pe ambalaj. Linia e fără TVA (categoria `O`, garanția nu intră în baza de impozitare) și e legată de linia articolului.

Clientul HoReCa sau magazinul returnează ambalajele goale distribuitorului:

1. **Returul `RC-SGR`** are cantitatea de ambalaje pe tip (PET, metal, sticlă). Nu are aviz sursă, ci se verifică față de ambalajele livrate clientului în ultimele `SgrReturnLookbackDays`. Ambalajele intră într-o gestiune de ambalaje SGR (S1), predate apoi administratorului sistemului.
2. **Nota de credit `FS-RC`** are liniile de garanție negative, tot în categoria `O`.

**Notă:** pentru simplitate, exemplele numerice din V2–V5 nu au afișat liniile de garanție SGR. În produs, ele apar automat pe documentele cu articole SGR și nu modifică valorile cu TVA ale exemplelor.

### 3.8 Setările V6 (completare la `SalesSettings`, V1 §3.9)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `ReturnQualityCheck` | 1 | Returul intră în carantină și așteaptă decizia (1) sau se repune în stoc la recepție (0) (decizia 1) |
| `CreditNoteSeries` | aceeași | Seria notelor de credit |
| `EInvoiceCreditNoteFormat` | 381 | Din V1: nota de credit (381) sau factura cu valori negative (380) |
| `AutoCreditOnReturn` | 1 | Nota `FS-RC` se generează automat în ciornă când returul trece în Verificat |
| `RebateSettlementDay` | 3 | Ziua lunii în care se calculează deconturile perioadelor încheiate |
| `SgrReturnLookbackDays` | 365 | Fereastra de verificare a ambalajelor SGR livrate clientului |

**Aprobarea** (`SalesApprovalRule` tipul 3, note de credit fără retur fizic):

| Nivel | Rol | Plafon |
| --- | --- | --- |
| 0 | Operator facturare | 1.000 lei |
| 1 | Șef vânzări | 25.000 lei |
| 2 | Director comercial | Fără limită |

Rabaturile pe rulaj din decont aprobat nu se mai aprobă o dată la emiterea notei.

## 4. Fluxuri principale

Datele de exemplu continuă pe cele din V1–V5. Responsabila de calitate e Mihaela Dobre. Notele de credit folosesc aceeași serie `FV EXD`. Ultima factură din 03.12 are numărul `FV EXD 10267`.

### 4.1 Returul Hotel Central (`RC 0051`) și nota `FV EXD 10270`

1. **Problema (03.12).** Hotel Central anunță că un bax de CEAI05 din livrarea de 28.11 (avizul `EXD 30625`, facturat pe `FV EXD 10247`) are sticle lovite, descoperite la desfacere. Nu e refuz la livrare, pentru că marfa a fost primită și semnată. Vlad creează returul `RC 0051` (`RC-AV`) din aviz: 12 buc CEAI05, lot `L2609-22`, motivul `DETERIORAT`. Politica „HoReCa standard” are 30 de zile, fără aprobare prealabilă.
2. **Recepția (04.12).** Sorin Pavel aduce baxul cu ruta următoare. Gheorghe Ilie recepționează 12 buc în `RET-CJ` (gestiunea de retur a `DEP-CJ`), la costul ieșirii inițiale de 1,94 lei/buc (23,28 lei), pe lotul `L2609-22`; marfa e exclusă din disponibil până la decizie.
3. **Verificarea.** Mihaela Dobre constată ambalajul spart și conținutul compromis și decide casarea celor 12 buc. Descărcarea `SCRAP` din `RET-CJ` are procesul-verbal `PVC 0207` și costul de 23,28 lei, sub pragul de aprobare din S2.
4. **Nota de credit.** Returul trece în Verificat, iar nota `FS-RC` se creează în ciornă: −12 × 2,90 = −34,80 lei, TVA 21 % −7,31 lei, total −42,11 lei, cu referința `FV EXD 10247`. Elena o validează ca `FV EXD 10270`. Nu cere aprobare, pentru că marfa s-a întors fizic.
5. **După validare.** e-Factura trimite nota de credit 381 cu `BillingReference` = `FV EXD 10247` / 02.12.2026. F3 o alocă pe `FV EXD 10247`, care mai are de plată 8.657,12 lei.

### 4.2 Storno și refacturare pentru Distribuția Vest (`FV EXD 10268`, `FV EXD 10269`)

1. **Problema.** Distribuția Vest nu poate plăti factura `FV EXD 10246` (14.944,21 lei, decada 21–30). Sistemul lor de plată cere numărul comenzii de aprovizionare `DV-PO-2611`, care lipsește de pe factură. Factura e validată în e-Factura, deci nu se mai corectează pe loc.
2. **Stornarea (04.12).** Elena alege „Stornează și refacturează”, cu motivul `EROARE` (Eroare de facturare). Nota `FV EXD 10268` (`FS-FV` integral) e de −14.944,21 lei (−13.463,25 + TVA −1.480,96). Peste 1.000 lei, cere aprobarea nivelului 1, iar Radu Stan aprobă la 10:12. La validare, cele 2 avize redevin „de facturat”.
3. **Refacturarea.** Asistentul V5 propune factura nouă din aceleași 2 avize, cu antetul copiat. Elena completează referința `DV-PO-2611` și validează `FV EXD 10269`: 14.944,21 lei, aceeași scadență contractuală calculată de la data nouă (03.01.2027).
4. **Efectul în F3.** Partida veche (factura și storno) se compensează automat, iar partida nouă rămâne deschisă.

### 4.3 Diferența de preț pentru Restaurant Someș (`FV EXD 10271`)

1. **Problema (04.12).** Restaurant Someș are, din 15.11, lista de preț proprie cu APA05 la 1,75 lei. Avizul `EXD 30624` din 27.11 a plecat cu 1,85 lei, pentru că lista partenerului nu fusese activată la timp. Factura `FV EXD 10250` conține 600 buc APA05 la 1,85.
2. **Nota de reducere.** Elena emite `FS-RED`, tipul diferență de preț, pe linia facturii: 600 × (1,85 − 1,75) = −60,00 lei, TVA 11 % −6,60 lei, total −66,60 lei, cu motivul `PRETGRESIT`. Sub 1.000 lei, nu cere aprobare. Nota e validată ca `FV EXD 10271`.
3. **Contarea:** `4111` = `709` + `4427`, cu valori negative (storno în roșu). Marja facturii `FV EXD 10250` scade cu 60,00 lei în rapoartele V7.

### 4.4 Rabatul pe rulaj Mega Retail, trimestrul 4 (`RB-MR-Q4`)

1. **Decontul (05.01.2027).** Jobul calculează decontul pentru acordul `RB-MR-Q4` (V2 §3.11): rulajul net al trimestrului 4 e de 268.900,00 lei, după storno și retururi. Pragul atins e 3 %, retroactiv, deci rabatul e de 8.067,00 lei. Nu s-au acordat rabaturi în perioadă.
2. **Repartizarea** pe cotele facturilor din trimestru:

   | Cotă | Rulaj | Rabat | TVA |
   | --- | --- | --- | --- |
   | 11 % | 211.300,00 | −6.339,00 | −697,29 |
   | 21 % | 57.600,00 | −1.728,00 | −362,88 |
   | **Total** | 268.900,00 | **−8.067,00** | **−1.060,17** |

   Nota are în total −9.127,17 lei.
3. **Emiterea.** Radu Stan aprobă decontul, iar Elena emite nota `FS-RED` cu perioada 01.10–31.12.2026 ca referință și cu lista celor 31 de facturi în anexă. Mega Retail primește nota în e-Factura. F3 o compensează cu următoarea factură, după acordul din contract.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `ReturClientReceptionat` v1 | Validarea returului | `ReturnId`, `PartnerId`, `DeliveryNoteId`, liniile cu lot, cantitate, motiv, cost | V7, CRM (reclamația), Calitate |
| `ReturClientDecizie` v1 | O decizie pe linie | `ReturnLineId`, decizia, cantitatea | Achiziții (propunerea de retur la furnizor), V7 |
| `FacturaEmisa` v1 cu `IsReversal` = 1 | Validarea notei de credit | Conținutul din V5, plus `CreditKind`, `ReversedInvoiceId`, referințele pe linii | F3, C2, C4, V2 (rulajul rabaturilor), V3 (comanda), V7 |
| `DecontRabatCalculat` v1 | Jobul de deconturi | `SettlementId`, `AgreementId`, perioada, suma | Notificări (Șef vânzări), F5 |

### 5.2 Consumate

| Eveniment | De la | Ce face V6 |
| --- | --- | --- |
| `ReclamatieInregistrata` v1 | CRM (viitor) | Propune returul în ciornă, cu linia și motivul |
| `EFacturaValidata`, `EFacturaRespinsa` v1 | Integrări 02.14 | Ca la facturi (V5 §3.7) |
| `MiscareStocValidata` v1 | S2 | Confirmă deciziile de casare și transfer pe liniile de retur |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V6)

```csharp
// Citit de Achiziții A4 (propunerea de retur la furnizor) și de CRM
public interface ICustomerReturnQueries
{
    Task<CustomerReturnDto?> GetAsync(long returnId, CancellationToken ct);
    Task<IReadOnlyList<ReturnLineForSupplierDto>> GetPendingSupplierReturnsAsync(int companyId, CancellationToken ct);
}

// Citit de F3 (de ce s-a emis o notă de credit) și de V7
public interface ICreditNoteQueries
{
    Task<IReadOnlyList<CreditNoteSummaryDto>> GetForInvoiceAsync(long invoiceId, CancellationToken ct);
}

// Citit de V2 (rulajul acordurilor) și de F5 (rabaturi de acordat)
public interface IRebateSettlementQueries
{
    Task<IReadOnlyList<RebateSettlementDto>> GetOpenAsync(int companyId, CancellationToken ct);
}
```

Comenzi interne V6:

- **Retururi:** `SaveReturn`, `ApproveReturn`, `ReceiveReturn`, `RecordDisposition`, `CancelReturn`.
- **Note de credit:** `CreateCreditFromReturn`, `CreateReversal` (integral sau parțial), `ReverseAndReinvoice`, `CreatePriceDifferenceCredit`, `CreateValueCredit`, `SubmitCreditForApproval`, `ApproveCredit`, `RejectCredit`.
- **Rabaturi:** `CalculateRebateSettlements` (job), `ApproveRebateSettlement`.
- **Ambalaje SGR:** `SaveSgrReturn`.

Validarea notei de credit folosește comanda `ValidateInvoice` din V5. Validarea scrie și rândurile negative ale faptului de vânzare (V7 §3.2): nota din retur cu costul intrării returului, reducerile cu cost zero (precizare din Vânzări V7, 27.11.2026).

## 7. Schema SQL `sales` (partea V6)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.CustomerReturn` | `Id bigint` | UX `(CompanyId, NumberSeriesId, Number)`; FK `PartnerId`, `DeliveryPointId`, `WarehouseId`, `ReasonId`, `PickupPlanId` | `(CompanyId, Status, DocDate)` |
| `sales.CustomerReturnLine` | `Id bigint` | FK `ReturnId`, `DeliveryNoteLineId`, `ItemVariantId`, `ReasonId`; `CHECK (CreditQty <= QtyReceived)` | `(DeliveryNoteLineId)` |
| `sales.CustomerReturnLot` | `Id bigint` | FK `LineId`, `LotId`, `SerialId` | `(LotId)` |
| `sales.SalesInvoice` (coloane noi) | — | `IsReversal bit`, `CreditKind tinyint NULL`, `ReversedInvoiceId bigint NULL` (FK pe aceeași tabelă), `ReasonId`, `ApprovalStatus tinyint NULL`, `RebateSettlementId`; `CHECK (IsReversal = 0 OR CreditKind IS NOT NULL)`; `CHECK (IsReversal = 0 OR TotalAmount <= 0)` | `(ReversedInvoiceId) WHERE ReversedInvoiceId IS NOT NULL` |
| `sales.SalesInvoiceLine` (coloane noi) | — | `OriginalInvoiceLineId`, `OriginalInvoiceId`, `ReturnLineId` | `(OriginalInvoiceLineId)` |
| `sales.RebateSettlement`, `sales.RebateSettlementVat` | `Id bigint` | UX `(AgreementId, PeriodFrom)`; FK `AgreementId`, `CreditNoteId` | `(CompanyId, Status)` |
| `sales.ReturnPolicy` | `Id int` | UX `(CompanyId, CodeKey)` | — |

Proceduri:

- **Retururi:** `sales.usp_Return_Save`, `sales.usp_Return_Receive` (verificările, legăturile, starea; mișcarea de stoc o face aplicația prin `IStockLedger`, în aceeași tranzacție), `sales.usp_Return_Disposition`.
- **Note de credit:** `sales.usp_Credit_FromReturn`, `sales.usp_Credit_Reverse` (integral sau parțial; TVP cu liniile și cantitățile), `sales.usp_Credit_PriceDifference`, `sales.usp_Credit_ValueSplit` (repartizarea pe cote), `sales.usp_Credit_RemainingByLine`.
- **Rabaturi:** `sales.usp_RebateSettlement_Calculate`.

**Coduri de eroare V6**

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52700 | `SALES_RET_SOURCE_INVALID` | 409 | Aviz sursă nevalidat, stornat sau al altui client |
| 52701 | `SALES_RET_OVER_DELIVERED` | 409 | Cantitate peste restul de returnat (cu `50027` din Nucleu) |
| 52702 | `SALES_RET_MANUAL_RIGHT` | 403 | Retur fără sursă fără dreptul `Sales.Return.WithoutSource` sau fără motiv |
| 52703 | `SALES_RET_POLICY_DAYS` | 409 | Retur peste termenul politicii clientului, fără drept de excepție |
| 52704 | `SALES_RET_REASON_NOT_ALLOWED` | 409 | Motiv neacceptat de politica clientului |
| 52705 | `SALES_RET_LOT_NOT_DELIVERED` | 409 | Lot care nu a fost livrat pe avizul sursă |
| 52706 | `SALES_RET_APPROVAL_EXPIRED` | 409 | Recepție pe o autorizare de retur expirată |
| 52710 | `SALES_RET_LOCKED` | 409 | Modificarea liniilor unui retur recepționat |
| 52711 | `SALES_RET_DISPOSITION_PENDING` | 409 | Notă de credit pe un retur cu linii fără decizie |
| 52712 | `SALES_RET_DISPOSITION_QTY` | 400 | Cantitățile deciziilor diferă de cantitatea primită |
| 52720 | `SALES_CREDIT_OVER_INVOICED` | 409 | Creditare peste valoarea sau cantitatea rămasă a liniei de factură |
| 52721 | `SALES_CREDIT_MIXED_CUSTOMERS` | 400 | Linii din facturile altor clienți sau în altă monedă |
| 52722 | `SALES_CREDIT_VAT_CHANGED` | 400 | Cotă sau categorie de TVA diferită de cea a facturii inițiale |
| 52723 | `SALES_CREDIT_APPROVAL_REQUIRED` | 409 | Validarea unei note peste plafon fără aprobare |
| 52724 | `SALES_CREDIT_SELF_APPROVAL` | 409 | Autorul își aprobă propria notă |
| 52725 | `SALES_CREDIT_REFERENCE_MISSING` | 400 | Notă fără factură sau perioadă de referință |
| 52730 | `SALES_REBATE_PERIOD_OPEN` | 409 | Decont pentru o perioadă neîncheiată |
| 52731 | `SALES_REBATE_ALREADY_SETTLED` | 409 | A doua notă din același decont |
| 52740 | `SALES_SGR_OVER_DELIVERED` | 409 | Ambalaje SGR returnate peste cele livrate clientului în fereastra de verificare |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.Return.View` / `.Edit` / `.Approve` / `.Receive` | Retururile: întocmire, autorizare, recepție |
| `Sales.Return.WithoutSource` | Returul fără aviz sursă (`RC-MAN`) |
| `Sales.Return.PolicyOverride` | Retur peste termenul sau motivele politicii |
| `Sales.Return.Disposition` | Decizia de calitate pe linii |
| `Sales.Credit.Edit` / `.Validate` | Notele de credit |
| `Sales.Credit.Approve` | Aprobarea notelor, în limita nivelului |
| `Sales.Rebate.Settle` | Aprobarea deconturilor de rabat |

Roluri livrate (V1 §8, completate):

| Rol | Permisiuni V6 |
| --- | --- |
| Agent de teren | `Return.Edit` pe clienții lui |
| Șef depozit | `Return.Receive` |
| Responsabil calitate | `Return.Disposition` |
| Operator facturare | `Credit.Edit`, `.Validate` |
| Șef vânzări | `Return.Approve`, `Credit.Approve` nivel 1, `Rebate.Settle`, `PolicyOverride` |
| Director comercial | `Credit.Approve` nivel 2 |
| Contabil șef | Vizualizare |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V6 |
| --- | --- |
| Codul fiscal art. 287 (ajustarea bazei de impozitare) și art. 330 alin. (1) lit. d) | Nota de credit la retur, reducere, anulare sau eroare, emisă în perioada în care apare evenimentul; cota inițială (art. 291 alin. (5)) |
| Codul fiscal art. 319 alin. (29) (factura de corecție) | Referința la factura inițială pe nota de credit (BT-25 în e-Factura) |
| OUG 120/2021 (e-Factura) | Nota de credit trimisă ca toate facturile (V5 §3.7), ca 381 sau 380 negativ |
| OMFP 1802/2014 | Reducerile comerciale ulterioare pe `709`; stornarea în roșu |
| Legea 249/2015, HG 1074/2021 (SGR) | Garanția de 0,50 lei pe ambalaj, fără TVA, pe documente și la returul ambalajelor |
| Regulamentul (CE) 178/2002 (trasabilitate alimentară) | Returul păstrează lotul livrat; casarea are proces-verbal |

Integrări:

- **Externe:** e-Factura, prin Integrări 02.14 (V5).
- **Interne:**
  - V4 (avizele, planul de livrare pentru ridicări);
  - V5 (factura, validarea, asistentul);
  - V2 (rulajul și acordurile de rabat);
  - S1 (gestiunea de retur);
  - S2 (`RETURN_FROM_CUSTOMER`, `SCRAP`, transferul);
  - S4 (lotul livrat; carantina prin gestiunea de retur);
  - Achiziții A4 (returul la furnizor);
  - F3 (alocarea notelor de credit);
  - C2 (contarea);
  - C4 (decontul și jurnalul de vânzări);
  - CRM (reclamațiile).

**Precizări aplicate altor documente la validarea V6 (27.11.2026):**

- **V1:**
  - §3.4: `CustomerTerms.ReturnPolicyId`;
  - §3.2: tranzacția `RC-SGR`;
  - §3.9: setările din 3.8 și `SalesApprovalRule` tipul 3.
- **V2 §3.9:** tipul 3 de aprobare. **V2 §3.11:** decontul de rabat din V6.
- **V3, V4, V5:** linia de garanție SGR adăugată automat (Nucleu `DepositItemId`), fără TVA.
- **Stocuri:**
  - S1: coloanele `ReturnWarehouseId` și `Purpose` pe gestiune (retur, ambalaje SGR);
  - S5: gestiunile de retur și de ambalaje SGR nu intră în disponibil;
  - S4: returul de la client e carantină prin gestiunea de retur, fără schimbarea stării lotului.
- **Achiziții A4:** propunerea de retur la furnizor din decizia de calitate (`ICustomerReturnQueries`).
- **Contabilitate C2:** cheia `SalesDiscount` (`709`), contarea notelor de credit și a reducerilor, garanția SGR (cont de terți, de exemplu `462` / `419` după politica firmei, de stabilit cu contabilul).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Retururi | Pe client, articol, motiv, agent, lot: cantități, valoare, decizii; rata de retur față de livrări | Șef vânzări, Calitate |
| Retururi în așteptare | Recepționate fără decizie, cu vechimea; anunțate și neridicate | Șef depozit, Calitate |
| Note de credit | Pe tip (storno, retur, preț, rabat, bonus), motiv, client, cine a aprobat; ponderea în vânzări | Director comercial, Contabil șef |
| Erori de facturare | Storno și refacturare pe motiv și operator, ca indicator de calitate a facturării | Contabil șef |
| Rabaturi | Acorduri, rulaj, estimat, decontat, emis; diferența dintre estimare și decont | Șef vânzări, Contabil |
| Ambalaje SGR | Livrate, returnate, sold pe client; garanții încasate și restituite | Contabil, Șef depozit |

## 11. Ecrane principale

Tiparul comun este listă cu filtre, fișă cu tab-uri, apoi istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v6-retururi-storno/](ecrane/v6-retururi-storno/). Meniul Vânzări are grupul Retururi: Retururi de la clienți, Facturi storno, Reduceri ulterioare.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Retururi de la clienți | Lista retururilor cu starea, motivul, decizia, nota de credit | `v6-01-lista-retururi` |
| Retur | Formularul `RC 0051`: sursa, lotul, recepția, decizia de calitate, nota generată | `v6-02-retur` |
| Nota de credit din retur | `FV EXD 10270`: liniile negative, referința, TVA-ul inițial, e-Factura, alocarea în F3 | `v6-03-nota-credit-retur` |
| Storno și refacturare | `FV EXD 10268` → `FV EXD 10269`: aprobarea, avizele eliberate, factura nouă | `v6-04-storno-refacturare` |
| Diferență de preț | `FV EXD 10271`: liniile facturii, prețul corect, diferența | `v6-05-diferenta-pret` |
| Decont de rabat | `RB-MR-Q4`: rulajul, pragul, repartizarea pe cote, aprobarea, nota | `v6-06-decont-rabat` |
| Facturi storno și reduceri | Lista notelor de credit pe tipuri, cu aprobările în așteptare | `v6-07-lista-note-credit` |
| Configurare › Retururi | Politicile de retur, setările din 3.8, plafoanele de aprobare | `v6-08-configurare-retururi` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V6-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V6-A1 | `RC 0051`: 12 buc CEAI05 intră în `RET-CJ` pe lotul `L2609-22`, exclusă din disponibil, la 1,94 lei/buc, iar starea lotului rămâne Disponibil; un lot nelivrat pe aviz e refuzat (`52705`) | Test de integrare (cu S2, S4) |
| V6-A2 | Decizia de casare face descărcarea `SCRAP` de 23,28 lei; nota de credit nu se poate genera înainte de decizii (`52711`) | Test de integrare |
| V6-A3 | `FV EXD 10270`: −34,80 + TVA −7,31 = −42,11 lei, cu cota de 21 % a facturii inițiale; o cotă schimbată e refuzată (`52722`); F3 alocă nota pe `FV EXD 10247` | Test de integrare (cu F3) |
| V6-A4 | Storno integral și refacturare: avizele redevin „de facturat” la validarea stornării; factura nouă are aceleași valori și referința nouă; partida veche se compensează | Test de integrare |
| V6-A5 | Creditarea nu depășește linia facturii: două note parțiale care împreună depășesc linia sunt refuzate la a doua (`52720`) | Test de integrare (concurență) |
| V6-A6 | Diferența de preț: 600 × 0,10 = −60,00 + −6,60; contarea pe `709` | Test de integrare (cu C2) |
| V6-A7 | Decontul `RB-MR-Q4`: 268.900,00 → 3 % = 8.067,00, repartizat 6.339,00 la 11 % și 1.728,00 la 21 %; un al doilea decont pentru aceeași perioadă e refuzat (`52731`) | Test de integrare |
| V6-A8 | Aprobarea: storno de 14.944,21 lei de la operatorul de facturare cere nivelul 1; autorul nu se aprobă (`52724`); `FS-RC` dintr-un retur verificat nu cere aprobare | Test de integrare |
| V6-A9 | Returul pe un aviz nefacturat micșorează restul de facturat al avizului și nu creează notă de credit | Test de integrare |
| V6-A10 | Nota de credit în e-Factura: tip 381 cu `BillingReference`; cu setarea 380, valorile sunt negative | Test de integrare (cu un gateway simulat) |
| V6-A11 | SGR: returul a 480 de ambalaje creditează 240,00 lei în categoria `O`, fără TVA; peste ambalajele livrate e refuzat (`52740`) | Test de integrare |
| V6-A12 | Toate procedurile V6 rulează corect pe baza case sensitive | Test de integrare |

Criteriile V6-A1, V6-A3, V6-A4 și V6-A5 sunt critice: ele decid costul și trasabilitatea returului, TVA-ul corect al notei, refacturarea și limita creditării.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **Stornarea unei facturi `FV-MF`** apelează `IAssetDisposalPort.ReverseAsync` în aceeași tranzacție; în luna ieșirii închisă, Mijloacele fixe refac fișa printr-o intrare de corecție în luna deschisă, cu avertisment. Returul fizic al unui mijloc fix vândut se tratează ca stornare, nu ca retur de marfă.

## Precizări din 02.12 CRM (aplicate pe 27.11.2026)

Modulul 02.12 CRM ([README](../12-crm/README.md), părțile CR1–CR5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Toate legăturile spre CRM sunt opționale: fără pachetul de licență „CRM”, câmpurile și butoanele de mai jos sunt ascunse și modulul funcționează ca până acum.

- **Cazurile CRM** (`CAZ-RECL`) se pot lega de aviz, factură, lot și retur; butonul „Creează retur” din caz deschide returul cu documentul sursă completat. Evenimentul `ReturCreat` apare pe cronologia clientului. [CR4 §3.6](../12-crm/CR4-ACTIVITATI-CAMPANII.md)

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Unde intră marfa returnată | Într-o gestiune de retur, în carantină, până la decizia de calitate pe linie (repune, casare, retur la furnizor, reparație); setare pentru firmele care repun direct la recepție | 2 regula 1, 3.2, 3.8 | ✓ DA (27.11.2026) |
| 2 | Costul returului | Costul și lotul ieșirii inițiale la returul din aviz; costul curent și drept separat la returul fără sursă | 2 regula 2 | ✓ DA (27.11.2026) |
| 3 | Corectarea unei facturi validate | Doar prin notă de stornare; pentru erori de date, „Stornează și refacturează” eliberează avizele și propune factura nouă | 2 regula 5, 3.4, 4.2 | ✓ DA (27.11.2026) |
| 4 | Retur pe aviz nefacturat | Scade cantitatea de facturat a avizului, fără notă de credit | 2 regula 6 | ✓ DA (27.11.2026) |
| 5 | Reducerile fără retur | Diferența de preț pe linii (cota liniei); rabatul și bonusul pe valoare, repartizate pe cotele facturilor de referință; contare pe `709` | 2 regula 7, 3.5 | ✓ DA (27.11.2026) |
| 6 | Rabatul pe rulaj | Decont lunar automat al perioadelor încheiate, aprobat de șeful de vânzări, apoi nota cu perioada ca referință | 3.5, 4.4 | ✓ DA (27.11.2026) |
| 7 | Aprobarea notelor de credit | Plafoane pe rol pentru storno și reduceri (1.000 / 25.000 / fără limită); nota din retur verificat fără aprobare; autorul nu își aprobă nota | 2 regula 8, 3.8 | ✓ DA (27.11.2026) |
| 8 | TVA-ul notei | Cota și categoria facturii inițiale; nota la data evenimentului; TVA la încasare păstrat | 3.3 | ✓ DA (27.11.2026) |
| 9 | Politica de retur | Pe grupă sau client: termen, aprobare prealabilă, motive acceptate, taxă de repunere în stoc, notă pe retur sau cumulată | 3.6 | ✓ DA (27.11.2026) |
| 10 | Ambalajele SGR | Linia de garanție automată pe documente, fără TVA; returul ambalajelor goale cu verificare față de cele livrate și credit pe garanție | 3.7 | ✓ DA (27.11.2026) |
| 11 | Precizări în alte documente | Cele din 9: V1, V2, V3–V5 (SGR), Stocuri S1 și S4, Achiziții A4, Contabilitate C2 | 9 | ✓ DA (27.11.2026) |

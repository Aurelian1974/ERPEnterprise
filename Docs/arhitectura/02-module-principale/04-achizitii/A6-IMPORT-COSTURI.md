# ERP · 02.04 Achiziții · A6 Import și costuri de achiziție: achiziții intracomunitare, import cu DVI, facturi auxiliare, repartizarea costurilor

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026), cu ecranele `a6-01`–`a6-09`.** A șasea din cele 7 părți ale modulului 02.04 Achiziții. Se sprijină pe [A1 Configurare](A1-CONFIGURARE.md), [A2 Necesar și oferte](A2-NECESAR-SI-OFERTE.md) (validate pe 26.11.2026), [A3 Comenzi](A3-COMENZI.md), [A4 Recepție](A4-RECEPTIE.md) și [A5 Facturi](A5-FACTURI.md) (validate pe 27.11.2026).

Modulul 02.04 Achiziții are schema SQL `purch`, prefixul de cod `Erp.Purchasing` și plaja de erori SQL `53000–53999`; partea A6 folosește `53650–53799`. Această parte acoperă **marfa cumpărată din afara României și costul ei complet**:
- factura furnizorului din UE (achiziție intracomunitară de bunuri, cu taxare inversă) și factura de import din afara UE;
- declarația vamală de import (DVI), cu taxele vamale și TVA-ul în vamă, plătit sau amânat cu certificat;
- facturile auxiliare (transport extern și intern, comisionar vamal, asigurare, manipulare);
- NIR-ul de import, care adună toate costurile cunoscute la recepție;
- repartizarea costurilor sosite după recepție pe NIR-urile vizate (documentul `CA`);
- urmărirea mărfurilor pe drum și datele pentru Intrastat, D390 și e-Transport.

Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F4 curs, C2 contare, C4 TVA, D390), [02.05 Stocuri](../05-stocuri/README.md) (S3 §3.3 costuri adăugate, S7 §3.7 Intrastat). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Tranzacțiile** sunt livrate de A1 §3.2:
  - `DVI-FF`: DVI din factura de import, grupul `CUSTOMS`;
  - `NIR-IMP`: NIR din factura de import, cu DVI și facturi auxiliare;
  - `CA-NIR`: repartizarea costurilor de achiziție pe NIR-uri, grupul `LANDED`.
  
  Facturile de import, intracomunitare și auxiliare sunt facturi de furnizor obișnuite (A5), cu tipul de operațiune TVA potrivit (`A-IC-B`, `A-IC-S`, `A-IMP`, C4 §3.2) și cu liniile de tip „cost de achiziție” (`LineKind = 3`, A5 §3.3).
- **Contul `408` e tamponul pentru tot ce intră în costul stocului** (A4 decizia 9, A5 §4.10): factura de marfă, taxele vamale din DVI și facturile auxiliare se contează pe `408`, iar NIR-ul de import și documentul `CA` le mută pe `371` (sau pe `607`, pentru partea deja vândută).
- **Costul de achiziție** (OMFP 1802/2014) = prețul de cumpărare + taxele de import nerecuperabile + transportul, manipularea și alte costuri direct atribuibile, minus reducerile. TVA-ul (inclusiv cel din vamă) nu intră în cost când e deductibil.
- **Corecția de cost după recepție** se face doar prin `IStockLedger.AdjustInboundCostAsync` (Stocuri S3 §3.3): partea în stoc corectează straturile, partea vândută merge pe `607`. Ținta corecției e rândul de urmărire al NIR-ului (A4 regula 3).
- **Comunicarea cu ANAF** (e-Transport, VIES, eventual DVI electronic) o face modulul Integrări (02.14). Declarațiile (D390 în C4, Intrastat în S7) se calculează în Finanțe și Stocuri din datele de aici.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Valorile se țin pe `decimal(19,4)`, cantitățile și ratele pe `decimal(19,6)` (`SQL-07`).
- **Ce NU face A6:**
  - nu depune DVI-ul la vamă (îl depune comisionarul; A6 doar îl înregistrează);
  - nu calculează D390 și Intrastat (C4, S7);
  - nu face reevaluarea valutară (F4);
  - nu gestionează containerele și transportul fizic (TMS, viitor).

## 1. Scop și perimetru

| Zonă | Ce face A6 | Ce NU face (și cine face) |
| --- | --- | --- |
| Achiziție intracomunitară | Factura furnizorului din UE: validarea codului de TVA în VIES, taxarea inversă, data exigibilității, condiția de livrare, datele pentru Intrastat și D390, UIT-ul e-Transport | Declarațiile (C4, S7), apelul VIES și e-Transport (02.14) |
| Import non-UE | Factura de import și DVI-ul: valoarea în vamă, taxele vamale pe cod NC, accizele, TVA-ul în vamă (plătit sau cu certificat de amânare), cursul vamal | Depunerea DVI (comisionarul vamal), plata către vamă (F2) |
| Facturi auxiliare | Transport extern și intern, comision vamal, asigurare, manipulare, legate de import sau de NIR-uri | Înregistrarea facturii propriu-zise (A5) |
| NIR de import | NIR-ul generat din factura de import, DVI și facturile auxiliare, cu costul complet pe linie | Regulile generale ale NIR-ului (A4) |
| Costuri după recepție | Documentul `CA` din factura auxiliară sosită după NIR, cu baza de repartizare și împărțirea pe stoc și vândut | Aplicarea în straturi și recalcularea (S3) |
| Urmărire | Importurile deschise: comandă, factură, DVI, sosire, NIR, costuri încă așteptate | — |

## 2. Model de domeniu

```
 PurchaseOrder (A3) ──► SupplierInvoice de import sau intracomunitară (A5, A-IMP / A-IC-B)
                            │ CUSTOMS
                            ▼
                    ImportCustomsDeclaration (DVI)            MRN, data, biroul vamal, comisionar, curs vamal, regim, mod de plată TVA
                      ├─ ImportCustomsDeclarationLine × N     cod NC, țara de origine, valoarea în vamă, taxa vamală, acciza, baza și TVA-ul
                      └─ legături CUSTOMS spre liniile facturii de import

 SupplierInvoice auxiliare (A5, LineKind = 3)                 transport, comision vamal, asigurare, manipulare
                            │ LANDED
                            ▼
 GoodsReceipt NIR-IMP (A4)  ◄── factura de import + DVI + auxiliare (costul complet la recepție)
                            │ LANDED (costuri sosite după)
                            ▼
 LandedCostAllocation (CA, A6 → Stocuri S3)                   factura auxiliară, NIR-urile țintă, baza de repartizare, sumele pe linii

 ImportShipment (urmărire)                                    comanda, facturile, DVI, ETA, sosire, NIR, costurile așteptate
```

Reguli de business ale modelului:

1. **Costul complet se formează pe linia NIR-ului**, din componente cu sursa lor: valoarea mărfii (factura), taxa vamală și acciza (DVI), costurile auxiliare (facturile de cost). Fiecare componentă e păstrată separat (`GoodsReceiptLineCost`), ca raportul „din ce e făcut costul” să poată fi refăcut oricând.
2. **Un cost ajunge o singură dată în stoc.** Fiecare linie de factură auxiliară și fiecare linie de DVI se consumă o singură dată (grupurile `LANDED`, `CUSTOMS`, fără depășire): fie pe NIR-ul de import, la recepție, fie printr-un `CA`, după recepție.
3. **DVI-ul e documentul vamal, nu o factură.** El nu creează datorie către un furnizor. Datoria față de vamă (taxe, TVA) e o obligație pe `446` și se plătește prin F2. Comisionarul vamal își facturează separat serviciul (factură auxiliară) și, uneori, refacturează taxele plătite în numele firmei (4.3).
4. **Achiziția intracomunitară nu are DVI.** Costul ei = factura + transportul și celelalte costuri auxiliare; TVA-ul se autolichidează (`4426` = `4427`), fără plată.
5. **Datele vamale și statistice se iau din documente, nu se ghicesc.** Codul NC8 vine din articol: e setat pe familia de articole și moștenit de articole, iar un articol sau o variantă îl pot schimba (3.6). Țara de origine și masa vin din DVI sau, pentru UE, din catalogul furnizorului și din articol (`ItemTradeInfo`, `NetWeightKg`). Toate se pot corecta pe factură.
6. **Un cod NC8 se alege doar din nomenclatorul oficial.** Nomenclatura combinată e livrată cu produsul, ca date inițiale (seed), și se actualizează anual. Nu se acceptă coduri tastate liber.

## 3. Entități, stări și setări

Convențiile comune tabelelor `purch` sunt cele din A1 §3. Serii: `DVI` (număr intern de înregistrare; numărul oficial e MRN-ul), `CA` (al Stocurilor, S3), `IMP` (dosar de import, opțional).

### 3.1 DVI-ul (`ImportCustomsDeclaration`)

| Câmp | Regulă |
| --- | --- |
| `Mrn` | Numărul de referință al mișcării (18 caractere, din declarația vamală); unic pe firmă (`53651`) |
| `DeclarationDate`, `CustomsOfficeCode` | Data acceptării declarației și biroul vamal (de exemplu ROCT1900 Constanța Sud) |
| `BrokerPartnerId` | Comisionarul vamal (partener), opțional |
| `DispatchCountryCode` | Țara de expediere (de unde a plecat marfa spre UE). Se propune din țara furnizorului sau din dosarul de import. Pentru Intrastat contează țara de expediere, nu originea |
| `OriginCountryCode` | Țara de origine implicită pentru linii. Se propune din `ItemTradeInfo` al articolelor sau din țara furnizorului; fiecare linie o poate schimba |
| `PreferenceCode`, `ProofOfOriginType`, `ProofOfOriginNo` | Preferința tarifară implicită pentru linii (codul din DVI: `100` fără preferință, erga omnes · `200` contingent · `300` preferință tarifară · alte coduri din nomenclator) și dovada originii (certificat EUR.1, A.TR, declarație de origine pe factură, REX), cu numărul ei |
| `CustomsCurrencyCode`, `CustomsExchangeRate` | Moneda și cursul de schimb din DVI (cursul vamal, stabilit lunar, folosit de vamă pentru valoarea în vamă) |
| `CustomsRegime` | `4000` punere în liberă circulație (implicit) · `4071` cu antrepozit · altele, din nomenclator |
| `VatPaymentMode` | 1 TVA plătit în vamă (`4426` = `446`) · 2 TVA cu certificat de amânare (`4426` = `4427`, taxare inversă, tipul `A-IMP` cu autolichidare, Codul fiscal art. 326 alin. 4) |
| `CustomsValueRon`, `DutyRon`, `ExciseRon`, `OtherTaxesRon`, `VatBaseRon`, `VatRon` | Totaluri, din linii |
| `ImportInvoiceIds` | Facturile de import acoperite (legături `CUSTOMS`) |
| `Status` | 1 Ciornă · 3 Validat · 4 Stornat · 9 Anulat |

Linia (`ImportCustomsDeclarationLine`): `ItemNo` (articolul din DVI), `NcCode` (8 sau 10 cifre), `OriginCountryCode`, `PreferenceCode`, `InvoiceLineIds` (prin legături), `NetMassKg`, `SupplementaryQty`, `InvoiceValueCurrency`, `CustomsValueRon` (factura + transportul și asigurarea până la frontiera UE, după Incoterm), `ProposedDutyRatePct` și `ProposedDutyRon` (calculate de ERP, 3.1.1), `DutyRatePct`, `DutyRon` (din DVI), `AdditionalDutyRatePct`, `AdditionalDutyRon` (taxă antidumping sau compensatorie, dacă există), `ExciseRon`, `VatBaseRon` (valoarea în vamă + taxele + costurile până la primul loc de destinație din România), `VatRatePct`, `VatRon`.

### 3.1.1 Taxele propuse după țară (`purch.CustomsDutyRate`)

Taxa vamală depinde de codul NC8 (sau TARIC), de **țara de origine** și de **preferința tarifară** dovedită. ERP-ul o **propune** pe fiecare linie a DVI-ului, iar valoarea din declarație rămâne cea finală (decizia 1).

| Câmp | Regulă |
| --- | --- |
| `CnCode` | Codul NC8; opțional cu sufixul TARIC (10 cifre), când rata diferă la nivel TARIC |
| `CountryCode` sau `CountryGroupId` | Exact unul. Țara de origine, sau un grup de țări (`purch.CustomsCountryGroup` cu membrii lui), de exemplu țările unui acord de liber schimb. Ambele goale = erga omnes, orice origine |
| `PreferenceCode` | `100` fără preferință · `300` preferință tarifară · alte coduri |
| `MeasureType` | 1 taxă vamală · 2 taxă antidumping · 3 taxă compensatorie |
| `RatePct` | Rata ad valorem |
| `ProofOfOriginType` | Dovada cerută pentru a aplica rata (EUR.1, A.TR, declarație de origine, REX), sau goală |
| `ValidFrom`, `ValidTo` | Perioada de valabilitate |
| `Source` | 1 introdusă de utilizator · 2 importată din fișier |

**Cum se calculează propunerea pe linie**, la data DVI-ului:

1. **Taxa vamală.** Rata se caută în ordine:
   - rata pentru codul, țara de origine și preferința liniei;
   - apoi rata pentru grupul de țări din care face parte originea;
   - apoi rata erga omnes a codului;
   - dacă nu există nicio rată, nu se propune nimic și linia rămâne cu valorile din DVI.
   
   O rată preferențială (`300`) se aplică doar dacă linia are dovada de origine cerută (`53665`). Fără dovadă, se propune rata erga omnes.
2. **Taxa antidumping sau compensatorie:** rata găsită cu `MeasureType` 2 sau 3, pentru cod și țara de origine; se adaugă la taxa vamală.
3. **Acciza:** pentru articolele accizabile, din setările de accize ale articolului (Finanțe C4); altfel 0.
4. **Baza TVA** = valoarea în vamă + taxa vamală + taxa antidumping + acciza + costurile până la primul loc de destinație din România. **TVA-ul** = baza × cota articolului.
5. **Comparația.** Propunerea apare lângă valoarea introdusă din DVI. O diferență peste 1 leu pe linie dă avertizare cu motivul probabil: altă origine, lipsa dovezii de origine, altă rată în TARIC. Diferența nu blochează validarea: valoarea finală e cea stabilită de vamă.

**Datele inițiale.**
- Tabelul pornește gol: fișierele INS ale nomenclatorului NC8 nu au taxe vamale.
- Ratele (erga omnes, preferențiale, antidumping) se introduc de utilizator sau se importă dintr-un fișier, doar pentru codurile și țările cu care firma lucrează.
- La prima linie de DVI cu un cod fără rată, ERP-ul propune salvarea ratei din DVI ca rată erga omnes pentru cod (sau pentru țara de origine, dacă linia are preferință). Astfel tabelul se completează din folosire.
- TARIC complet nu se livrează: are sute de mii de măsuri și se schimbă des.

### 3.2 Linia de cost a NIR-ului (`GoodsReceiptLineCost`, completare la A4 §3.3)

| Câmp | Regulă |
| --- | --- |
| `ReceiptLineId` | Linia NIR-ului |
| `Component` | 1 Valoarea mărfii · 2 Taxă vamală · 3 Acciză · 4 Transport extern · 5 Transport intern · 6 Comision vamal · 7 Asigurare · 8 Manipulare · 9 Alte costuri |
| `SourceType`, `SourceLineId` | Linia facturii, a DVI-ului sau a facturii auxiliare |
| `AmountRon` | Suma repartizată pe linie |
| `AllocationBasis` | Baza folosită (3.4) |

`UnitCostRon` al liniei NIR-ului (A4 §3.3) = Σ componente / `AcceptedQtyBase`. Pe NIR-urile obișnuite există o singură componentă (1), creată automat.

### 3.3 Dosarul de import (`ImportShipment`)

Opțional, pentru urmărire. Câmpuri:
- `Number` (seria `IMP`), `PartnerId`, `OrderIds`;
- `Incoterm` (din `core.DeliveryTerm`), `IncotermPlace`, `TransportModeCode` (din `core.TransportMode`: 1 maritim, 2 feroviar, 3 rutier, 4 aerian…, 3.7);
- `ContainerOrVehicleNo`, `Etd` (plecare), `Eta` (sosire estimată), `ArrivedAt`;
- `ExpectedCosts`: lista costurilor încă așteptate (transport, comision), cu suma estimată, doar informativ;
- `Status`: 1 Comandat · 2 Expediat · 3 În vamă · 4 Sosit · 5 Recepționat · 6 Costuri complete.

Dosarul adună documentele prin legături și arată ce lipsește. Nu are efect contabil.

### 3.4 Baza de repartizare

Bazele sunt cele din S3 §3.3: valoare (implicit), cantitate, greutate, volum, manual. Ce se propune, în ordine:

1. Tipul de cost: transportul se propune după greutate dacă toate liniile au `GrossWeightKg` sau `NetWeightKg`; altfel după valoare.
2. Comisionul vamal și asigurarea: după valoare.
3. Manipularea: după cantitate.

Utilizatorul poate schimba baza pe document. Pentru taxa vamală nu există bază de repartizare: ea vine pe linie, din DVI.

### 3.5 Setările A6 (completare la `PurchaseSettings`)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `ImportCostRate` | 1 | Cursul pentru valoarea mărfii din NIR-ul de import: 1 cursul vamal din DVI · 2 cursul BNR al datei NIR-ului (decizia 3) |
| `IntraEuCostRate` | 1 | Pentru achiziția intracomunitară: 1 cursul BNR al datei NIR-ului (A4 decizia 10) · 2 cursul facturii |
| `WaitForCostsOnImportReceipt` | 0 | 1: NIR-ul de import se poate valida doar când toate costurile așteptate din dosar sunt facturate; 0: se validează oricând, iar costurile întârziate vin prin `CA` (decizia 5) |
| `ViesCheckOnInvoice` | 1 | Verificarea codului de TVA al furnizorului din UE în VIES la înregistrarea facturii; rezultatul se salvează (dovada pentru taxarea inversă) |
| `DefaultCustomsBrokerId` | — | Comisionarul propus pe DVI |
| `LandedCostAutoValidateBelowRon` | 500 | Documentele `CA` sub prag se validează automat; peste prag rămân în ciornă pentru contabilul de stocuri |

### 3.6 Codurile NC8 (nomenclatura combinată)

**Nomenclatorul (`core.CnCode`, în Nucleu).** Conține nomenclatura combinată a anului, așa cum o publică INS pentru Intrastat. Structura e cea din fișierele INS analizate pe 27.11.2026 (`CN_2026.xml` și foaia `NC8_2026` din `nomenclatoare.xls`): 15.080 de rânduri, dintre care 9.797 coduri NC8, 2.370 rânduri de grupare și 2.711 coduri cu UM suplimentară.

| Câmp | Regulă |
| --- | --- |
| `Key` | Cheia rândului. Pentru coduri e chiar codul: secțiune (`I`…`XXI`), capitol (2 cifre), poziție (4), subpoziție SA (6), cod NC8 (8). Pentru rândurile de grupare (cele cu liniuțe, fără cod), cheia vine din fișier: `@1072` în XML, sau `@` + `CNKEY` în XLS |
| `Code` | Codul, gol pe rândurile de grupare. Doar rândurile cu `Kind = 5` (cod NC8 de 8 cifre) se pot pune pe articole |
| `Kind` | 1 secțiune · 2 capitol · 3 poziție · 4 subpoziție · 5 cod NC8 · 9 grupare |
| `ParentKey` | Rândul de deasupra în arbore. Exemplu: `39191012` → grupare `Benzi, panglici acoperite cu cauciuc…` → `391910` → `3919` → grupare `II. Deșeuri…` → `39` → `VII`. În XML vine din `parentCode`; în XLS se deduce din ordinea rândurilor și din numărul de liniuțe (`NBDASH`) |
| `DashLevel`, `SortOrder` | Numărul de liniuțe și ordinea din fișier, pentru afișarea arborelui ca în nomenclatorul oficial |
| `NameRo`, `NameEn` | Descrierea în română și engleză. XLS-ul are diacritice, XML-ul nu; la seed se folosește XLS-ul |
| `SupplementaryUom` | UM suplimentară cerută de cod (`p/st`, `m2`, `l`, `1000 p/st`, `kg N`…), sau gol (`-` în fișier); o folosesc Intrastat și DVI-ul |
| `ValidFrom`, `ValidTo`, `SourceVersion` | Anul de valabilitate și versiunea fișierului; un cod scos din nomenclator primește `ValidTo` și nu se mai poate alege |

Fișierele INS **nu conțin taxe vamale**. Rata de referință pentru verificarea DVI-ului vine din tabelul de taxe (3.1.1), nu din nomenclator.

**Datele inițiale (seed).**
- ErpSetup încarcă la instalare nomenclatoarele INS ale anului curent (NC8, termeni de livrare, natura tranzacției, moduri de transport), din scriptul livrat în pachetul de instalare.
- Scriptul e generat din fișierele oficiale INS de unealta `_unelte/seed-nomenclatoare/ins_nomenclatoare.py`. Unealta citește XLS-ul și verifică rezultatul cu XML-ul.
- Ieșirea pentru 2026 e în [seed/nomenclatoare-ins/](../../seed/nomenclatoare-ins/): scripturile `sql/40_DeliveryTerm.sql` … `sql/43_CnCode_2026.sql` (`MERGE` idempotent pentru `database/seed/core/`, `MIG-06`), fișierele CSV pe nomenclator, `CnCodeChanges_2025_2026.csv` și `raport.txt`.
- **Verificarea încrucișată XLS / XML pentru 2026:**
  - aceleași 15.080 de rânduri și aceleași coduri;
  - același părinte pentru toate codurile;
  - descrierile diferă doar ca formă (diacritice, apostrof `′`, litere grecești);
  - UM suplimentară diferă doar ca scriere (`G` / `g`, `1.000 p/st` / `1000 p/st`, `Kg N` / `kg N`).
  
  Diferențele de formă nu sunt modificări (3.7).
- Anul următor nu se mai livrează seed: nomenclatorul se actualizează prin ecranul de import (3.7).

**Moștenirea codului.** Codul se setează pe trei niveluri, de sus în jos:

| Nivel | Câmp | Regulă |
| --- | --- | --- |
| Familia de articole (tipul de articol, Nucleu §3.3.1) | `ItemFamily.CnCode` | Codul implicit pentru toate articolele din familie |
| Articolul (modelul) | `Item.CnCode` + `Item.CnCodeOverridden` | La crearea în familie, primește codul familiei (`CnCodeOverridden = 0`). Dacă utilizatorul îl schimbă, `CnCodeOverridden = 1`. Un articol fără familie are codul pus direct pe el |
| Varianta (codul de articol, SKU) | `ItemVariant.CnCode` | Gol înseamnă codul articolului. Se completează doar când o variantă are alt cod (de exemplu altă compoziție a materialului) |

- **Codul efectiv** al unei linii de document este codul variantei, dacă există, altfel codul articolului. Îl dă `IItemQuery.SnapshotAsync` (Nucleu), care îl și copiază pe linie.
- **Schimbarea codului pe familie** actualizează, într-o singură tranzacție, toate articolele din familie cu `CnCodeOverridden = 0`. Articolele cu cod propriu rămân neschimbate. Înainte de salvare, ecranul arată câte articole se schimbă și care rămân.
- **Butonul „Revino la codul familiei”** de pe articol pune `CnCodeOverridden = 0` și copiază codul familiei.
- **Unde e obligatoriu codul:** pe articolele stocabile (marfă, materie primă, produs finit, ambalaj) folosite în achiziții intracomunitare, importuri, livrări intracomunitare, exporturi sau transporturi cu e-Transport. Lipsa codului blochează validarea DVI-ului (`53664`) și generarea Intrastat (S7, `54804`). Pe o factură intracomunitară dă doar avertizare.

**Codul pe DVI.** Vama lucrează cu codul TARIC, de 10 cifre. Primele 8 cifre sunt codul NC8. Pe linia DVI-ului se păstrează codul din declarație (8 sau 10 cifre), iar primele 8 trebuie să fie un cod NC8 valabil la data DVI-ului (`53663`). Dacă diferă de codul efectiv al articolului, apare o avertizare (4.2).

### 3.7 Nomenclatoarele INS și importul lor

**Tabele separate, în Nucleu** (schema `core`), fiecare cu codul oficial ca cheie, ca `core.Country` (abatere permanentă de la `SQL-13`, ca la celelalte liste standard):

| Tabel | Conținut (seed 2026) | Cine îl folosește |
| --- | --- | --- |
| `core.CnCode` | Nomenclatura combinată, pe niveluri (3.6) | Articole, DVI, Intrastat, e-Transport |
| `core.DeliveryTerm` | Termenii de livrare: `EXW`, `FCA`, `FAS`, `FOB`, `CFR`, `CIF`, `CPT`, `CIP`, `DAP`, `DPU`, `DDP`, `XXX` (alți termeni). Câmpuri: `Code char(3)`, `NameRo`, `NameEn`, plus două marcaje ale ERP-ului, nu ale INS: `MainCarriagePaidBySeller` și `InsurancePaidBySeller`, care spun dacă transportul principal și asigurarea sunt în prețul furnizorului. Pe ele se bazează valoarea în vamă (4.2) și valoarea statistică Intrastat (S7). Marcajele livrate: `C…` și `D…` au transportul plătit de vânzător; `CIF` și `CIP` au și asigurarea | `SupplierTerms`, comenzi, facturi, dosarul de import, Intrastat |
| `core.TransactionNature` | Natura tranzacției pe două niveluri: 9 grupe (`1`…`9`) și 19 coduri (`11`, `12`, `21`…`99`); grupele `6` și `8` nu au subcoduri și se folosesc ca atare. Câmpuri: `Code varchar(3)`, `ParentCode`, `NameRo`, `NameEn` | Facturi, NIR, Intrastat (`TransactionNatureCode`, S7) |
| `core.TransportMode` | Modurile de transport: `1` maritim, `2` feroviar, `3` rutier, `4` aerian, `5` poștă, `7` instalații fixe, `8` căi navigabile interioare, `9` propulsie proprie. Câmpuri: `Code char(1)`, `NameRo`, `NameEn` | Dosarul de import, facturi, Intrastat (`TransportModeCode`, S7) |

Câmpurile care foloseau până acum text liber devin chei străine spre aceste tabele: `SupplierTerms.Incoterm` (A1), `Incoterm` pe comandă (A3), pe factură (A5) și pe dosar, `TransportMode` pe dosar (3.3) și pe factură.

**Ecranul de import** („Import nomenclatoare INS”, ecranul `a6-08`, dreptul `Core.CnNomenclature.Manage`) actualizează nomenclatoarele în trei pași. Nimic nu se scrie în baza de date până la ultimul pas.

1. **Alegerea sursei.**
   - **XLS**: registrul INS (`nomenclatoare.xls`). ERP-ul recunoaște foile după nume: `NC8_<an>`, „Nomenclator termeni de livrare”, „Natura Tranzactiei”, „Nomenclator transport”. Foile „Nomenclator tari” și „Nomenclator judete” sunt ignorate, pentru că țările și județele vin din Nucleu (ISO, SIRUTA). Foile „CODURI NC NOI” și „CODURI NC STERSE” se folosesc doar pentru control.
   - **XML**: câte un fișier INS pe nomenclator, cu namespace-ul `http://www.intrastat.ro/xml/NomenclaturesSchema`. Nomenclatorul se recunoaște după elementul `Id` (de exemplu `CN`) și anul după `Version`.
   - **Ce importă utilizatorul:** bifează nomenclatoarele dorite și anul.
   - **Fișier refuzat** (`50031`): fișierul nu are foile sau elementele așteptate, sau anul din fișier diferă de cel ales.
2. **Analiza.** Fișierul se citește într-o tabelă de lucru (`core.NomenclatureImportBatch` și `core.NomenclatureImportLine`) și se compară cu baza, pe fiecare nomenclator.
   - **Ce se raportează:** rândurile noi, eliminate și modificate (descriere, UM suplimentară, părinte); fiecare modificare apare cu valoarea veche lângă cea nouă.
   - **Ce nu se raportează:** diferențele doar de formă. Descrierile se compară fără diacritice, cu apostroful și literele grecești transliterate. UM suplimentară se compară fără majuscule și fără separatorul de mii.
   - **Coduri NC8 eliminate:** pentru fiecare, ERP-ul propune înlocuitori, adică codurile noi din aceeași subpoziție SA (primele 6 cifre). Dacă fișierul are foaia „CODURI NC STERSE”, se verifică și cu ea.
   - **Cu un singur înlocuitor**, codul nou e propus automat pe articolele, familiile și variantele care foloseau codul vechi.
   - **Cu mai mulți înlocuitori**, utilizatorul alege pe fiecare articol sau familie; până atunci articolul intră în „Coduri NC8 de revizuit”.
   - **Pentru fiecare cod eliminat**, ecranul arată câte articole și familii îl folosesc.
   - **Termeni de livrare, natură, transport:** la fel. De exemplu, dacă INS schimbă descrierea unui termen de livrare, modificarea apare ca propunere.
     - Un termen eliminat de INS nu se șterge: se marchează inactiv dacă e folosit pe furnizori sau documente deschise.
     - Marcajele ERP-ului pe termenii de livrare nu se ating la import.
3. **Aplicarea.**
   - Utilizatorul acceptă sau respinge fiecare propunere, sau toate dintr-un grup.
   - „Aplică selecția” scrie, într-o singură tranzacție, doar propunerile acceptate:
     - codurile noi se adaugă cu `ValidFrom` = 1 ianuarie al anului;
     - codurile eliminate primesc `ValidTo` = 31 decembrie al anului anterior;
     - descrierile se actualizează;
     - articolele primesc codurile de înlocuire alese.
   - Propunerile respinse rămân în lot, cu motivul, pentru audit.
   - Lotul se poate reanaliza după un import nou.
   - Documentele validate își păstrează codurile copiate pe linie.

Exemplu pe fișierele reale: importul 2026 peste o bază cu nomenclatorul 2025 propune:
- 27 de coduri NC8 noi și 14 eliminate, fiecare eliminat cu 2 sau 3 înlocuitori. Exemple:
  - `38011000` Grafit artificial → `38011010` sau `38011090`;
  - `85044085` și `85044086` → `85044084` sau `85044087`;
- 168 de descrieri modificate, de exemplu `24011035`: „Tutunuri uscate cu aer cald (light air cured)” → „Tutunuri uscate natural (light air cured)”. Printre ele sunt și erori evidente ale sursei, de exemplu „2,6 m²” devenit „2,6 m22” la `41041151`, pe care utilizatorul le poate respinge;
- nicio schimbare la termenii de livrare, natura tranzacției și moduri de transport.

## 4. Fluxuri principale

### 4.1 Achiziția intracomunitară de bunuri

1. Comanda (A3) către furnizorul din UE are moneda lui și `Incoterm`-ul din `SupplierTerms` (de exemplu FCA Stuttgart: transportul principal îl plătește firma).
2. Factura furnizorului nu vine prin SPV (furnizorul nu e în RO e-Factura). Se înregistrează din PDF sau din XML-ul primit pe email (A5, `FF-CF` din comandă sau `FF-MAN`), cu `EInvoiceStatus = 0`.
3. **Verificări la înregistrare:**
   - codul de TVA al furnizorului e valid în VIES la data facturii (`ViesCheckOnInvoice`; un cod invalid dă avertizare și cere dreptul `Purch.Invoice.EditVat` pentru a păstra taxarea inversă);
   - tipul de operațiune `A-IC-B` pe liniile de marfă, `A-IC-S` pe servicii;
   - **data exigibilității TVA** = data facturii, dacă e emisă până pe 15 ale lunii următoare livrării; altfel 15 ale lunii următoare (Codul fiscal art. 285).
4. Contare: `408` = `401.02` (valoarea, la cursul facturii) și `4426` = `4427` (TVA-ul autolichidat, la cota românească a articolului).
5. **e-Transport:** transportul internațional al bunurilor se declară înainte de intrarea în țară (OUG 41/2022, cu modificările ulterioare). Codul UIT îl obține Integrări sau îl comunică transportatorul; se salvează pe dosar sau pe aviz și se verifică la recepție (A4 §3.1).
6. **NIR-ul** (`NIR-CF` sau `NIR-AV`, A4) se face la sosire. Facturile auxiliare (transportul de la Stuttgart) se adaugă pe NIR, dacă au sosit (4.4), sau prin `CA` după aceea (4.5).
7. **Declarațiile:**
   - D390 (C4) ia factura cu codul `A`;
   - Intrastat (S7 §3.7) ia NIR-ul `RECEIPT_PURCHASE` de la furnizor UE, cu valoarea facturată din A5. Valoarea statistică include transportul până la frontiera română, după Incoterm, din facturile auxiliare legate.

Exemplu (ecranul `a6-04`): comanda `CF 2303` către Wasser Technik GmbH (DE, DE287412659), FCA Stuttgart:
- 1.500 buc FILTRU10 la 9,50 EUR și 500 buc FILTRU20 la 13,20 EUR, total 20.850,00 EUR;
- factura `WT-26-4471` din 20.11.2026, înregistrată ca `FF 0914`, la cursul 5,0910: 106.147,35 lei;
- TVA autolichidat 21%: 22.290,94 lei (`4426` = `4427`);
- marfa sosește pe 26.11 cu UIT `4Y7K2M9QXB1R5T`; NIR 4489, la același curs (5,0910, BNR din 26.11, în exemplu egal cu cel al facturii).

### 4.1.1 Factura de import din afara UE

1. Comanda externă (A3) are moneda furnizorului, `Incoterm`-ul (de exemplu CIF Constanța: transportul maritim și asigurarea până în port sunt incluse în preț) și, opțional, dosarul de import (3.3).
2. Factura vine pe email, ca PDF, fără SPV. Se înregistrează în A5 (`FF-CF` din comandă sau `FF-MAN`), cu `EInvoiceStatus = 0`.
3. **Ce are în plus față de o factură internă:**
   - tipul de operațiune `A-IMP`: **pe factură nu se calculează TVA**, pentru că TVA-ul de import se stabilește în vamă, pe DVI (4.2);
   - `Incoterm`-ul, modul de transport și containerul sau mijlocul de transport, propuse din comandă sau din dosar;
   - țara de expediere și țara de origine pe fiecare linie, propuse din articol (`ItemTradeInfo`) sau din țara furnizorului; DVI-ul le preia;
   - codul NC8 efectiv al articolului pe fiecare linie (3.6).
4. **Cursul și datoria.**
   - Valoarea în lei se calculează la cursul BNR al facturii (A5).
   - Contarea e `408` = `401.03` (furnizori externi). Partida rămâne în valută și se reevaluează lunar (F4).
   - La plată, diferența de curs merge pe `665` / `765` (F2).
   - Diferența dintre cursul facturii și cursul vamal folosit de NIR se închide la NIR (4.4).
5. **Ce nu intră în declarații:** factura de import nu intră în D390 (nu e achiziție intracomunitară) și nici în Intrastat (marfa nu vine dintr-un stat membru). Importul apare în decontul de TVA prin DVI (C4).
6. Din factura validată se generează DVI-ul (`DVI-FF`, 4.2). Factura arată legăturile spre DVI și NIR și ce componente de cost mai lipsesc până la NIR: taxele din DVI, comisionul, transportul intern.

Exemplu (ecranul `a6-07`): comanda `CF 2296` către Ningbo PackTech Co., Ltd. (CN), CIF Constanța, transport maritim cu containerul MSKU 7310442, dosarul IMP 0042.
- Factura `NP-2611-088` din 20.11.2026 e înregistrată ca `FF 0910`: 24.000 buc BANDA48 la 0,42 USD, adică 10.080,00 USD.
- La cursul 4,4085, valoarea e 44.437,68 lei, contată `408` = `401.03`, cu scadența 19.01.2027.
- TVA pe factură: 0. TVA-ul de import, 9.946,38 lei, vine din DVI 0027.

### 4.2 DVI-ul

1. Comisionarul vamal trimite DVI-ul (PDF și, uneori, XML-ul declarației). Contabilul îl înregistrează cu `DVI-FF` din factura de import (A5). Liniile se propun din liniile facturii, grupate pe codul NC8 efectiv al articolelor (3.6) și pe țara de origine, cu masa netă și cantitatea în UM suplimentară calculate din articol.
2. **Țările și preferința.**
   - În antet se completează țara de expediere și țara de origine. Țara de origine se propune din articole sau din furnizor și se copiază pe linii, unde poate fi schimbată.
   - Se completează și preferința tarifară, cu dovada originii, dacă există.
   - De ele depind taxele propuse: aceeași bandă adezivă are altă taxă vamală dacă e originară din China fără preferință sau din Turcia cu certificat A.TR.
3. **Taxele propuse și cele din document.**
   - ERP-ul calculează pe fiecare linie taxa vamală, taxa antidumping, acciza, baza TVA și TVA-ul propuse (3.1.1).
   - Utilizatorul introduce din DVI MRN-ul, data, biroul, cursul vamal și, pe fiecare linie, valoarea în vamă și taxele stabilite de vamă. Butonul „Preia propunerea” copiază valorile propuse, iar utilizatorul doar le confirmă față de DVI.
   
   Verificările:
   - Σ valori în vamă ≈ factura × cursul vamal + costurile până la frontieră (avertizare peste 1%);
   - taxa vamală = valoarea × rata (avertizare la diferențe peste 1 leu pe linie);
   - taxele din DVI față de cele propuse după cod, țară și preferință (3.1.1). O diferență dă doar avertizare, cu motivul probabil;
   - codul din DVI față de codul efectiv al articolului. Dacă diferă, apare o avertizare cu două opțiuni: păstrează codul articolului sau actualizează articolul cu codul din DVI (dreptul `Core.Item.Manage`; codul devine propriu articolului, `CnCodeOverridden = 1`). Codul folosit de vamă e de regulă cel corect.
4. **Modul de plată a TVA-ului:**
   - plătit în vamă: `4426` = `446.TVA`, plătit apoi prin F2 sau de comisionar;
   - cu certificat de amânare: `4426` = `4427`, tipul `A-IMP` cu autolichidare, fără plată.
5. **Taxa vamală, taxa antidumping și acciza:** `408` = `446.TV` (obligația față de vamă). Ele intră în costul mărfii prin NIR-ul de import (4.4) sau, dacă NIR-ul există deja, printr-un `CA` cu componenta „taxă vamală” pe liniile lui.
6. Validarea scrie legăturile `CUSTOMS` pe liniile facturii de import (o factură nu poate avea două DVI-uri active pe aceeași linie, `53653`) și publică `DVIValidat`, cu blocul `Accounting`.

Exemplu (ecranul `a6-01`): Ningbo PackTech Co., Ltd. (CN) facturează `NP-2611-088`, 24.000 buc BANDA48 la 0,42 USD, total 10.080,00 USD, CIF Constanța. DVI-ul cu MRN `26ROCT1900I0047812` din 24.11.2026 are:
- cursul vamal 4,4120 lei/USD;
- valoarea în vamă 44.472,96 lei (CIF, deci fără costuri de adăugat până la frontieră);
- țara de expediere CN și țara de origine CN, fără preferință (`100`), fără taxă antidumping pe acest cod;
- cod NC 3919 10 80, taxă vamală 6,5%: 2.890,74 lei. Rata e propusă de ERP din tabelul de taxe (rata erga omnes a codului, salvată la primul import cu acest cod) și e egală cu cea din DVI. Cu origine TR și certificat A.TR, propunerea ar fi fost 0%;
- baza TVA 47.363,70 lei și TVA 21% de 9.946,38 lei, cu certificat de amânare: `4426` = `4427`;
- comisionarul Vama Expert SRL.

### 4.3 Facturile auxiliare

Facturile de transport, comision vamal, asigurare și manipulare se înregistrează în A5 ca facturi obișnuite. Linia lor are articolul de serviciu marcat „cost de achiziție” (`LineKind = 3`) și componenta de cost (3.2), din articol. Contare: `408` = `401` și `4426` = `401` (A5 §4.10).

Legătura cu marfa se face într-unul din trei feluri:

| Legătura | Când | Ce face A6 |
| --- | --- | --- |
| Pe NIR-ul de import, la generare | Factura auxiliară e înregistrată înainte de recepție | Linia de cost e consumată de NIR (grupul `LANDED`) și intră în costul la recepție (4.4) |
| Pe dosarul de import | Factura vine pentru un dosar deschis | Se propune automat pe NIR-ul dosarului sau, dacă NIR-ul e validat, într-un `CA` |
| Direct pe NIR-uri validate | Factura vine după recepție, fără dosar | `CA-NIR`: utilizatorul alege NIR-urile (filtru pe furnizorul mărfii, perioadă, articol) și baza (4.5) |

**Comisionarul care refacturează taxele vamale** (a plătit taxa și TVA-ul în numele firmei): linia de refacturare se înregistrează pe articolul „Taxe vamale refacturate”. Linia nu are TVA și nu e cost de achiziție, pentru că taxa e deja în cost din DVI. Contarea ei e `446` = `401`, adică datoria față de vamă trece la comisionar (decizia 6).

Exemplu: pentru importul Ningbo sosesc două facturi auxiliare:
- `VE-1187` de la Vama Expert SRL, comision vamal 650,00 lei + TVA 21%;
- `TL 3402` de la Transcarpat Logistic SRL, transport Constanța–Cluj 3.100,00 lei + TVA 21%.

Pentru importul Wasser, factura `TL 3318` a Transcarpat Logistic SRL (din inbox-ul A5, 2.400,00 lei + TVA) e transportul Stuttgart–Cluj.

### 4.4 NIR-ul de import (`NIR-IMP`)

1. Asistentul de generare (Nucleu) pornește de la factura de import și adaugă automat:
   - DVI-ul validat al facturii (grupul `CUSTOMS`);
   - facturile auxiliare legate de dosar sau alese de utilizator (grupul `LANDED`).
2. **Liniile NIR-ului** sunt liniile de marfă ale facturii, cu cantitățile recepționate (regulile A4: numărare, diferențe, loturi, carantină).
3. **Costul pe linie** (`GoodsReceiptLineCost`):
   - valoarea mărfii, la cursul dat de `ImportCostRate`: implicit cursul vamal, ca valoarea mărfii din stoc să fie aceeași cu valoarea în vamă (decizia 3);
   - taxa vamală și acciza de pe linia DVI corespunzătoare;
   - costurile auxiliare, repartizate după baza din 3.4.
4. Validarea face NIR-ul (A4 §4.6) cu `InboundUnitCost` = costul complet. Contarea:
   - `371` = `408` pe costul complet (din mișcarea de stoc);
   - fără TVA de pe NIR: TVA-ul de import e deja tratat pe DVI.
   
   Contul `408` se închide: factura de import, DVI-ul și facturile auxiliare au contat pe `408` exact componentele consumate.
5. **Diferența de curs.** Factura de import e contată la cursul facturii (`408` = `401.03`). Dacă NIR-ul folosește cursul vamal, diferența dintre cele două valori ale mărfii în lei rămâne în `408`. Ea se închide automat la validarea NIR-ului, pe `665` sau `765` (decizia 3, ca în A5 decizia 9).

Exemplu (ecranul `a6-02`): NIR 4494 din 27.11.2026, gestiunea DEP-CJ, 24.000 buc BANDA48. Componentele costului:

| Componentă | Lei |
| --- | --- |
| Valoarea mărfii: 10.080,00 USD × 4,4120 (curs vamal) | 44.472,96 |
| Taxa vamală (DVI) | 2.890,74 |
| Comision vamal (`VE-1187`) | 650,00 |
| Transport intern (`TL 3402`) | 3.100,00 |
| **Cost complet** | **51.113,70** |

Costul unitar e 2,1297 lei, față de 4,10 lei la furnizorul intern. Factura de import a fost contată la cursul BNR al facturii (4,4085): 44.437,68 lei. Valoarea din stoc e mai mare decât datoria înregistrată, deci diferența de 35,28 lei se închide ca `408` = `765` la validarea NIR-ului.

### 4.5 Costurile sosite după recepție (`CA-NIR`)

1. Din factura auxiliară validată (A5), butonul „Repartizează pe recepții” (sau propunerea automată din dosar) deschide `CA-NIR`:
   - sursele sunt liniile de cost ale facturii;
   - țintele sunt NIR-urile alese, cu liniile lor.
2. A6 propune baza (3.4) și calculează suma pe fiecare linie NIR. Utilizatorul poate trece pe repartizare manuală (Σ = suma facturii, `53656`).
3. **Pentru fiecare linie țintă, A6 arată:**
   - cât din cantitate mai e în stoc și cât a ieșit (din `IStockQuery`, S2);
   - unde merge corecția: `371` pentru partea în stoc, `607` sau `6xx` pentru partea ieșită (S3 §3.3).
4. Validarea apelează `IStockLedger.AdjustInboundCostAsync` cu `Kind = ServiceInvoice`, țintele = rândurile de urmărire ale liniilor NIR. Linia de cost a NIR-ului (`GoodsReceiptLineCost`) primește componenta nouă, deci costul complet al NIR-ului rămâne explicabil.
5. Sub `LandedCostAutoValidateBelowRon` documentul `CA` se validează automat. Peste prag rămâne în ciornă la contabilul de stocuri, care îl validează din Stocuri.
6. Contare (prin `CostStocAjustat`, S3): `371` / `607` = `408` (precizat în A5).

Exemplu (ecranul `a6-03`): `TL 3318` (2.400,00 lei, transport Stuttgart–Cluj) se repartizează pe NIR 4489, după greutate. Liniile au:
- FILTRU10: 1.500 buc × 0,40 kg = 600 kg;
- FILTRU20: 500 buc × 0,60 kg = 300 kg.

Rezultatul e 1.600,00 lei pe FILTRU10 și 800,00 lei pe FILTRU20. După valoare ar fi fost 1.640,29 și 759,71. Din FILTRU10 s-au vândut deja 300 buc, așa că corecția se împarte:
- 1.280,00 lei pe stoc (`371`);
- 320,00 lei pe `607`.

Costul unitar al FILTRU10 în stoc trece de la 48,3645 la 49,4312 lei.

### 4.6 Importurile deschise și mărfurile pe drum

Ecranul `a6-05` arată dosarele și comenzile externe deschise, cu:
- etapa fiecăreia: comandat, expediat, în vamă, sosit, recepționat;
- ETA și documentele lipsă (factură, DVI, UIT, facturi auxiliare);
- costurile încă așteptate.

Mărfurile facturate și nerecepționate la sfârșitul lunii intră în reclasificarea `GOODS_IN_TRANSIT` (A5 §4.10, C3).

### 4.7 Cazuri limită

| Caz | Comportament |
| --- | --- |
| O factură de import acoperă două containere, cu două DVI-uri | Fiecare DVI consumă liniile (sau cantitățile) lui din factură; NIR-urile se fac pe fiecare DVI |
| DVI-ul sosește după NIR (marfa a ieșit din vamă cu declarație simplificată) | NIR-ul se validează cu valoarea mărfii; taxa vamală vine prin `CA` din DVI, cu componenta 2 |
| Rectificarea DVI de către vamă | DVI-ul se stornează și se înregistrează rectificativa; diferența de taxă intră în cost printr-un `CA` |
| Furnizorul UE are codul de TVA invalid în VIES | Avertizare; dacă se păstrează taxarea inversă, cere drept și motiv; altfel factura se tratează ca achiziție cu TVA străin nedeductibil (cost) |
| Factura de transport acoperă mai multe importuri | Repartizare pe mai multe NIR-uri, din furnizori diferiți, într-un singur `CA` |
| Costul auxiliar sosește după închiderea lunii NIR-ului | `CA` în prima lună deschisă (S3 regula 6) |
| Marfa importată e returnată furnizorului | Retur (A4) la costul complet al NIR-ului; taxele vamale nu se recuperează automat (cererea de restituire e în afara ERP-ului) |
| Import în regim de antrepozit | DVI-ul de plasare nu creează cost; doar DVI-ul de punere în liberă circulație are taxe; regimul e pe document |
| Monedă diferită între factură (EUR) și DVI (USD, la cursul vamal) | Fiecare document în moneda lui; NIR-ul folosește lei |
| Codul NC8 al articolului a fost scos din nomenclator la 1 ianuarie | Importul anual propune înlocuitorii din aceeași subpoziție; cu un singur înlocuitor îl propune direct, altfel articolul apare în „Coduri NC8 de revizuit”, iar DVI-ul și Intrastat-ul lui sunt blocate până la alegere (3.7) |
| Familia își schimbă codul NC8 | Se schimbă doar articolele care îl moștenesc; documentele deja validate își păstrează codul copiat pe linie |
| Variante ale aceluiași articol cu coduri diferite | Codul se pune pe variantă; DVI-ul și Intrastat-ul grupează după codul variantei |

**Jurnalul de excepții (precizare din Achiziții A7, 27.11.2026).** Excepțiile aprobate în această parte se scriu în `purch.ExceptionLog` prin `IPurchaseExceptionWriter.WriteAsync`, în aceeași tranzacție cu documentul care le aprobă (A7 §3.5): taxarea inversă păstrată cu cod de TVA invalid în VIES; taxa din DVI diferită de cea propusă. Se scriu documentul, linia, furnizorul, valoarea în lei, cine a cerut, cine a aprobat și motivul.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `DVIValidat` v1 | Validarea DVI-ului | `DeclarationId`, `Mrn`, `DeclarationDate`, liniile (NC, origine, valoare în vamă, taxe, TVA), `VatPaymentMode`, blocul `Accounting` | C2, C4 (TVA la import), S7 (Intrastat nu, doar pentru verificare), Raportare |
| `DVIStornat` v1 | Stornarea | `DeclarationId`, blocul `Accounting` cu semn opus | C2, C4 |
| `CostAchizitieRepartizat` v1 | Validarea unui `CA-NIR` din A6 | `LandedCostId`, factura sursă, NIR-urile țintă, sumele | A7, Raportare (marja reală pe import) |

`ReceptieNIRValidata` (A4) poartă, pentru `NIR-IMP`, și componentele costului pe linie.

### 5.2 Consumate

| Eveniment | De la | Ce face A6 |
| --- | --- | --- |
| `FacturaFurnizorValidata` v1 | A5 | Pentru liniile de cost de achiziție: propune repartizarea pe dosar sau pe NIR; pentru facturile de import: deschide sau actualizează dosarul |
| `ReceptieNIRValidata` v1 | A4 | Actualizează etapa dosarului și propune `CA` pentru costurile auxiliare deja facturate și neconsumate |
| `CostStocAjustat` v1 | Stocuri S3 | Marchează `CA`-ul ca aplicat în dosar |
| `UitObtinut` v1 | Integrări 02.14 | Salvează codul UIT pe dosar |

## 6. Contracte publice (`Erp.Purchasing.Contracts`, partea A6)

```csharp
public enum LandedCostComponent : byte { GoodsValue = 1, CustomsDuty = 2, Excise = 3, FreightExternal = 4, FreightDomestic = 5,
    CustomsBrokerage = 6, Insurance = 7, Handling = 8, Other = 9 }
public enum ImportVatPaymentMode : byte { PaidAtCustoms = 1, DeferralCertificate = 2 }
public enum ImportShipmentStatus : byte { Ordered = 1, Shipped = 2, InCustoms = 3, Arrived = 4, Received = 5, CostsComplete = 6 }

public sealed record ReceiptLineCostDto(long ReceiptLineId, LandedCostComponent Component, string SourceType, long SourceLineId, decimal AmountRon);

// Citit de S7 (Intrastat: valoarea facturată și statistică) și de Raportare (marja pe import)
public interface IImportCostReader
{
    Task<IReadOnlyList<ReceiptLineCostDto>> GetReceiptLineCostsAsync(long receiptId, CancellationToken ct);
    Task<IReadOnlyList<IntrastatPurchaseValueDto>> GetIntrastatValuesAsync(int companyId, DateOnly month, CancellationToken ct);
}
public sealed record IntrastatPurchaseValueDto(long ReceiptLineId, decimal InvoicedValueRon, decimal StatisticalValueRon,
    string? Incoterm, string? TransportModeCode, string? TransactionNatureCode, string? OriginCountryCode);
```

Comenzi interne A6 (MediatR):
- DVI: `SaveCustomsDeclaration`, `ValidateCustomsDeclaration`, `ReverseCustomsDeclaration`;
- dosar de import: `SaveImportShipment`, `UpdateImportShipmentStage`;
- repartizarea costurilor: `ProposeLandedCostAllocation`, `SaveLandedCostAllocation`, `ValidateLandedCostAllocation` (apelează S3);
- `CheckSupplierVies` (prin 02.14).

Codul NC8 ține de Nucleu: nomenclatorul, moștenirea, comenzile `SetItemFamilyCnCode`, `SetItemCnCode`, `ResetItemCnCodeToFamily`, `ImportCnNomenclature` și evenimentul `CodNcArticolModificat` sunt precizate în Nucleu (tabelul de precizări). A6 îl citește prin `IItemQuery.SnapshotAsync`.

Comportamentele de generare sunt `Customs.FromImportInvoice`, `GoodsReceipt.FromImport` și `LandedCost.FromReceipts` (A1 §3.2).

## 7. Schema SQL `purch` (partea A6)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `purch.ImportCustomsDeclaration` | `Id bigint` | UX `(CompanyId, Mrn) WHERE Status <> 9`; UX `(CompanyId, Number) WHERE Number IS NOT NULL`; FK `BrokerPartnerId`, `CustomsCurrencyCode` | `(CompanyId, DeclarationDate)` |
| `purch.ImportCustomsDeclarationLine` | `Id bigint` | UQ `(DeclarationId, ItemNo)`; FK `DeclarationId`, `OriginCountryCode` | `(NcCode)` |
| `purch.GoodsReceiptLineCost` | `Id bigint` | FK `ReceiptLineId`; UX `(SourceType, SourceLineId, ReceiptLineId)` | `(ReceiptLineId) INCLUDE (Component, AmountRon)` |
| `purch.ImportShipment` | `Id bigint` | UX `(CompanyId, Number)`; FK `PartnerId` | `(CompanyId, Status, Eta)` |
| `purch.ImportShipmentExpectedCost` | `Id bigint` | FK `ShipmentId` | — |
| `purch.CustomsDutyRate` | `Id int` | FK `CountryCode`, `CountryGroupId`; `CK` exact unul sau niciunul dintre țară și grup | `(CnCode, MeasureType, ValidFrom) INCLUDE (CountryCode, CountryGroupId, PreferenceCode, RatePct)` |
| `purch.CustomsCountryGroup`, `purch.CustomsCountryGroupMember` | `Id int` | UX `Code`; UQ `(GroupId, CountryCode)` | `CountryCode` |
| `purch.ViesCheck` | `Id bigint` | FK `PartnerId` | `(PartnerId, CheckedAt DESC)` |

Exemplu de tabel complet:

```sql
CREATE TABLE purch.ImportCustomsDeclarationLine (
    Id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImportCustomsDeclarationLine PRIMARY KEY,
    DeclarationId         bigint            NOT NULL CONSTRAINT FK_ICDLine_Declaration REFERENCES purch.ImportCustomsDeclaration(Id),
    ItemNo                smallint          NOT NULL,
    NcCode                varchar(10)       NOT NULL CONSTRAINT CK_ICDLine_Nc CHECK (NcCode NOT LIKE '%[^0-9]%' AND LEN(NcCode) IN (8, 10)),
    OriginCountryCode     char(2)           NOT NULL CONSTRAINT FK_ICDLine_Country REFERENCES core.Country(Code),
    NetMassKg             decimal(19,6)     NOT NULL,
    SupplementaryQty      decimal(19,6)     NULL,
    InvoiceValueCurrency  decimal(19,4)     NOT NULL,
    CustomsValueRon       decimal(19,4)     NOT NULL,
    DutyRatePct           decimal(9,4)      NOT NULL CONSTRAINT DF_ICDLine_DutyRate DEFAULT (0),
    DutyRon               decimal(19,4)     NOT NULL CONSTRAINT DF_ICDLine_Duty DEFAULT (0),
    ExciseRon             decimal(19,4)     NOT NULL CONSTRAINT DF_ICDLine_Excise DEFAULT (0),
    OtherTaxesRon         decimal(19,4)     NOT NULL CONSTRAINT DF_ICDLine_Other DEFAULT (0),
    VatBaseRon            decimal(19,4)     NOT NULL,
    VatRatePct            decimal(9,4)      NOT NULL,
    VatRon                decimal(19,4)     NOT NULL,
    CONSTRAINT UQ_ICDLine UNIQUE (DeclarationId, ItemNo),
    CONSTRAINT CK_ICDLine_Amounts CHECK (CustomsValueRon >= 0 AND DutyRon >= 0 AND ExciseRon >= 0 AND VatBaseRon >= CustomsValueRon)
);
CREATE INDEX IX_ICDLine_Nc ON purch.ImportCustomsDeclarationLine (NcCode);
```

Proceduri:
- DVI: `purch.usp_CustomsDeclaration_Save` (TVP, `SP-07`, `SP-18`), `purch.usp_CustomsDeclaration_Validate`, `purch.usp_CustomsDeclaration_Reverse`;
- NIR de import: `purch.usp_ImportReceipt_BuildCosts` (componentele pentru `NIR-IMP`, pe seturi);
- repartizare: `purch.usp_LandedCost_Propose` (baza și sumele, cu împărțirea stoc / ieșit din `inv`, doar citire prin vederea publicată de Stocuri);
- dosare: `purch.usp_ImportShipment_List` (`SP-11`);
- taxe propuse: `purch.usp_CustomsDeclaration_ProposeDuties` (pe toate liniile, cu ordinea de căutare din 3.1.1).

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 53651 | `PURCH_CUSTOMS_MRN_DUPLICATE` | 409 | MRN deja înregistrat |
| 53652 | `PURCH_CUSTOMS_MRN_INVALID` | 400 | MRN care nu are 18 caractere alfanumerice |
| 53653 | `PURCH_CUSTOMS_LINE_ALREADY_DECLARED` | 409 | Linie de factură deja acoperită de alt DVI activ |
| 53654 | `PURCH_CUSTOMS_NOT_VALIDATED` | 409 | `NIR-IMP` cu un DVI în ciornă |
| 53655 | `PURCH_LANDED_COST_ALREADY_CONSUMED` | 409 | Linie de cost deja repartizată (pe NIR sau într-un `CA`) |
| 53656 | `PURCH_LANDED_COST_SUM_MISMATCH` | 400 | Repartizarea manuală nu dă suma liniei de cost |
| 53657 | `PURCH_LANDED_COST_NO_BASIS` | 409 | Bază „greutate” sau „volum” cu linii fără greutate sau volum |
| 53658 | `PURCH_LANDED_COST_TARGET_INVALID` | 409 | Țintă care nu e un NIR validat, sau NIR stornat |
| 53659 | `PURCH_IMPORT_COSTS_PENDING` | 409 | `NIR-IMP` cu costuri așteptate nefacturate, cu `WaitForCostsOnImportReceipt = 1` |
| 53660 | `PURCH_VIES_INVALID` | 409 | Taxare inversă păstrată pentru un cod de TVA invalid, fără drept și motiv |
| 53661 | `PURCH_CUSTOMS_REVERSAL_HAS_RECEIPT` | 409 | Stornarea unui DVI consumat de un NIR validat fără `CA` de corecție |
| 53662 | `PURCH_CUSTOMS_VALUE_MISMATCH` | 409 | Valoarea în vamă diferă de factură × curs peste 5% (avertizare între 1% și 5%) |
| 53663 | `PURCH_CUSTOMS_CN_INVALID` | 409 | Primele 8 cifre ale codului din DVI nu sunt un cod NC8 valabil la data DVI-ului |
| 53664 | `PURCH_ITEM_CN_MISSING` | 422 | Validarea DVI-ului cu o linie al cărei articol nu are cod NC8 |
| 53665 | `PURCH_CUSTOMS_PROOF_OF_ORIGIN_MISSING` | 422 | Linie cu preferință tarifară (`300`) fără tipul și numărul dovezii de origine |
| 53666 | `PURCH_CUSTOMS_COUNTRY_MISSING` | 422 | DVI fără țara de expediere sau linie fără țara de origine |
| 50031 | `CORE_NOMENCLATURE_FILE_INVALID` | 400 | Fișier de import fără foile sau elementele INS așteptate, sau cu alt an decât cel ales (3.7; cod din plaja Nucleu) |
| 50032 | `CORE_NOMENCLATURE_REPLACEMENT_REQUIRED` | 409 | Aplicarea unui import care elimină un cod NC8 folosit, fără înlocuitor ales și fără marcarea articolelor pentru revizuire (cod din plaja Nucleu) |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Purch.Customs.View`, `Purch.Customs.Edit`, `Purch.Customs.Validate`, `Purch.Customs.Reverse` | DVI-ul |
| `Purch.LandedCost.Edit`, `Purch.LandedCost.Validate` | Repartizarea costurilor (validarea peste prag o face Stocuri, `Inv.LandedCost.Validate`) |
| `Purch.ImportShipment.Manage` | Dosarele de import |
| `Purch.CustomsTariff.Manage` | Ratele de taxe vamale, preferențiale și antidumping, grupurile de țări, importul lor din fișier |
| `Core.CnNomenclature.Manage` (Nucleu) | Importul anual al nomenclatorului NC8 și revizuirea codurilor; codul pe familie și pe articol se schimbă cu `Core.ItemFamily.Manage`, respectiv `Core.Item.Manage` |

Roluri livrate:
- **Contabil furnizori:** DVI, repartizare;
- **Contabil stocuri:** `LandedCost.Validate`;
- **Achizitor import:** rol nou: dosare, `Customs.View`, `LandedCost.Edit`;
- **Șef achiziții:** toate pentru vizualizare.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru A6 |
| --- | --- |
| Codul vamal al Uniunii (Regulamentul UE 952/2013) | Valoarea în vamă (valoarea tranzacției + transportul și asigurarea până la frontiera UE), DVI cu MRN, regimurile vamale |
| Codul fiscal art. 285, 307, 326 | Exigibilitatea TVA la achiziția intracomunitară; taxarea inversă; TVA la import plătit în vamă sau amânat cu certificat (art. 326 alin. 4) |
| OMFP 1802/2014 | Costul de achiziție cu taxele de import și costurile direct atribuibile; contul `446` pentru taxele vamale; `665` / `765` |
| OUG 41/2022 (e-Transport) | UIT pentru transporturile internaționale de bunuri care intră în țară, salvat pe dosar și verificat la recepție |
| Regulamentul UE 2019/2152 și normele INS (Intrastat) | Valoarea facturată, valoarea statistică, condiția de livrare, modul de transport, țara de origine (S7) |
| D390 (VIES) | Achizițiile intracomunitare de bunuri și servicii, pe codul de TVA al furnizorului (C4) |
| Regulamentul (CEE) nr. 2658/87 (nomenclatura combinată) și regulamentul anual de modificare a Anexei I | Codurile NC8 valabile în anul curent, cu UM suplimentară; actualizarea anuală la 1 ianuarie (3.6) |

Integrări externe (prin 02.14): VIES, e-Transport. Interne: A3 (comanda, Incoterm), A4 (NIR, componentele costului), A5 (facturile, `LineKind = 3`), Stocuri S3 (`CA`), S7 (Intrastat), Finanțe C2, C4, F2 (plata către vamă), F4 (curs).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Importuri deschise | Dosarele pe etapă, ETA, documentele și costurile lipsă | Achizitor import, Șef depozit |
| Structura costului de achiziție | Pe NIR, articol, furnizor: valoarea mărfii, taxe, transport, comision, % din valoare | Șef achiziții, Controlling |
| Costuri auxiliare nerepartizate | Linii de cost validate și neconsumate (rămase în `408`), cu vechimea | Contabil stocuri |
| Registrul DVI | DVI-urile pe perioadă: MRN, valoare în vamă, taxe, TVA, mod de plată, comisionar | Contabil furnizori |
| Verificări VIES | Furnizorii UE cu ultima verificare și rezultatul | Contabil furnizori |
| Coduri NC8 de revizuit | Articolele și variantele stocabile fără cod NC8, cu cod scos din nomenclator sau cu cod diferit de cel din ultimul DVI; codurile noi propuse la import (3.7) | Responsabil nomenclatoare |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/a6-import-costuri/](ecrane/a6-import-costuri/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| DVI (fișa) | DVI-ul Ningbo: antetul (MRN, data, birou, curs vamal, comisionar, regim, mod de plată TVA, țara de expediere, țara de origine, preferința tarifară, dovada originii), liniile cu NC, origine, valoare în vamă, taxa din DVI lângă cea propusă, TVA, taxa codului după origine, legăturile cu factura, contarea | `a6-01-dvi` |
| NIR de import | NIR 4494: sursele (factura, DVI, facturile auxiliare) și componentele costului pe linie, costul unitar, diferența de curs | `a6-02-nir-import` |
| Repartizarea costurilor | `TL 3318` pe NIR 4489: baza (greutate față de valoare), sumele pe linii, împărțirea stoc / vândut, costul unitar înainte și după | `a6-03-repartizare-costuri` |
| Factură intracomunitară | `FF 0914` Wasser Technik: taxarea inversă, VIES, exigibilitatea, Incoterm, UIT, datele pentru D390 și Intrastat | `a6-04-factura-intracomunitara` |
| Importuri deschise | Dosarele cu etapa, ETA, documentele și costurile lipsă | `a6-05-importuri-deschise` |
| Factură de import extracomunitar | `FF 0910` Ningbo PackTech: valuta și cursul, `A-IMP` fără TVA pe factură, Incoterm, modul de transport și containerul, țara de expediere și de origine, codul NC8, legăturile cu DVI-ul și NIR-ul, componentele de cost care lipsesc, datoria pe `401.03` | `a6-07-factura-import` |
| Codul NC8 pe familie și articole | Familia „Benzi adezive”: codul NC8 ales din nomenclator, articolele care îl moștenesc și cel cu cod propriu, nomenclatorul NC8 cu ramura 3919 10 din fișierul INS 2026 | `a6-06-coduri-nc8` |
| Import nomenclatoare INS | Alegerea sursei (XLS sau XML) și a nomenclatoarelor, analiza importului 2026 peste 2025: coduri noi, eliminate cu înlocuitori și articolele afectate, descrieri modificate, propunerile de acceptat sau respins | `a6-08-import-nomenclatoare` |
| Nomenclatoare comerț exterior | Termenii de livrare cu marcajele de transport și asigurare, natura tranzacției, modurile de transport, fiecare în tabelul lui | `a6-09-nomenclatoare-ins` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.04-A6-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| A6-A1 | DVI-ul Ningbo cu certificat de amânare dă `4426` = `4427` 9.946,38 și `408` = `446` 2.890,74; un al doilea DVI pe aceeași linie de factură e refuzat (`53653`) | Test de integrare (cu C2) |
| A6-A2 | NIR 4494 are costul complet 51.113,70 lei din patru componente, costul unitar 2,1297; diferența de curs 35,28 se închide `408` = `765`; după validare, soldul `408` pentru factura de import, DVI și facturile auxiliare e 0 | Test de integrare (cu C2, Stocuri) |
| A6-A3 | `CA` din `TL 3318` după greutate dă 1.600,00 și 800,00; cu 300 buc FILTRU10 vândute, 320,00 lei merg pe `607` și 1.280,00 pe stoc | Test de integrare (cu S3) |
| A6-A4 | O linie de cost repartizată pe un NIR nu mai poate fi repartizată într-un `CA` (`53655`); bază „greutate” cu o linie fără greutate e refuzată (`53657`) | Test de integrare |
| A6-A5 | Factura Wasser din 20.11 are exigibilitatea TVA 20.11; o factură emisă pe 18.12 pentru o livrare din 20.11 are exigibilitatea 15.12 | Test unitar |
| A6-A6 | Cod de TVA invalid în VIES: taxarea inversă păstrată fără drept e refuzată (`53660`); verificarea se salvează cu data și răspunsul | Test de integrare (cu 02.14 simulat) |
| A6-A7 | `IImportCostReader.GetIntrastatValuesAsync` întoarce pentru NIR 4489 valoarea facturată 106.147,35 și valoarea statistică 108.547,35 (cu transportul FCA) | Test de integrare (cu S7) |
| A6-A12 | Pentru 3919 10 80: origine CN fără preferință propune 6,5%; origine TR cu preferință `300` și A.TR propune 0%; aceeași linie cu `300` fără dovadă e refuzată (`53665`); o rată antidumping pentru cod și CN se adaugă la taxa propusă; o taxă din DVI diferită de propunere dă avertizare, nu blochează | Test de integrare |
| A6-A13 | Factura `FF 0910` (`A-IMP`) nu are TVA, contează `408` = `401.03` 44.437,68 lei și nu apare în D390 și nici în Intrastat; DVI-ul generat din ea preia țările și codul NC8 de pe linii | Test de integrare (cu C2, C4, S7) |
| A6-A14 | Unealta de seed pe `nomenclatoare.xls` și `CN_2026.xml` dă 15.080 rânduri, 9.797 coduri NC8, aceiași părinți în ambele surse, 12 termeni de livrare, 28 de coduri de natură a tranzacției, 8 moduri de transport | Test unitar (pe fișierele INS din pachet) |
| A6-A15 | Importul 2026 peste 2025 propune 27 de coduri noi, 14 eliminate (fiecare cu înlocuitori din aceeași subpoziție) și 168 de descrieri modificate; reimportul aceluiași an din XML după XLS nu propune nimic (diferențele de formă sunt ignorate); o propunere respinsă nu se scrie | Test de integrare |
| A6-A9 | Schimbarea codului NC8 al familiei BENZI-ADEZ actualizează BANDA48, BANDA72 și BANDA48-COL, dar nu și BANDAH50 (cod propriu); varianta cu cod propriu își păstrează codul; „Revino la codul familiei” readuce moștenirea | Test de integrare (Nucleu) |
| A6-A10 | DVI cu un cod TARIC ale cărui prime 8 cifre nu sunt valabile e refuzat (`53663`); un articol fără cod NC8 blochează validarea (`53664`); un cod diferit de cel al articolului dă avertizare și poate actualiza articolul | Test de integrare |
| A6-A11 | Importul anual: un cod înlocuit unic se schimbă automat pe articole și familii; un cod împărțit în două pune articolul în „Coduri NC8 de revizuit” | Test de integrare (Nucleu) |
| A6-A8 | Toate procedurile A6 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Test de integrare |

Criteriile A6-A2 și A6-A3 sunt critice: ele decid costul mărfii importate, deci marja și valoarea stocului.

## Precizări la alte documente (aplicate pe 27.11.2026)

| Document | Precizarea |
| --- | --- |
| A4 §3.3 | Linia NIR-ului primește componentele costului (`GoodsReceiptLineCost`, 3.2); `UnitCostRon` = Σ componente / cantitatea de bază |
| Finanțe C2 §3.3 | Cheile noi `CustomsDuty` (implicit `446` analitic taxe vamale) și `CustomsVat` (implicit `446` analitic TVA în vamă) |
| Finanțe C3 | Concordanța `408` (A4) include DVI-urile și liniile de cost neconsumate |
| Stocuri S7 §3.7 | Valoarea facturată și statistică pentru introducerile din UE vin din `IImportCostReader`; codul NC8 al liniei e codul efectiv al variantei (A6 §3.6), copiat pe linia documentului; UM suplimentară vine din `core.CnCode.SupplementaryUomCode`, iar `inv.ItemTradeInfo` păstrează doar factorul de conversie și țara de origine |
| Nucleu §3.3 și §3.3.1 | `Item.CnCode` devine `varchar(8)`, cod NC8 de nivel 8 din `core.CnCode`, plus `Item.CnCodeOverridden bit`; câmpuri noi `ItemFamily.CnCode` și `ItemVariant.CnCode` (opțional); moștenirea și propagarea din A6 §3.6; `IItemQuery.SnapshotAsync` întoarce codul efectiv. Tabele noi `core.CnCode` (cheie `Key`, UX `(Code, ValidFrom) WHERE Code <> ''`), `core.DeliveryTerm`, `core.TransactionNature`, `core.TransportMode`, `core.NomenclatureImportBatch`, `core.NomenclatureImportLine` (A6 §3.6, §3.7), cu seed-ul din `seed/nomenclatoare-ins/`; comenzile `SetItemFamilyCnCode`, `SetItemCnCode`, `ResetItemCnCodeToFamily`, `AnalyzeNomenclatureImport`, `ApplyNomenclatureImport`; permisiunea `Core.CnNomenclature.Manage`; evenimentul `CodNcArticolModificat` v1. Raportul „Articole incomplete pentru e-Factura” folosește codul efectiv |
| Instalare (ErpSetup) | Seed-ul nomenclatoarelor INS (`database/seed/core/40..43_*.sql`, `MERGE` idempotent) e generat din fișierele INS de `_unelte/seed-nomenclatoare/`; actualizările anuale trec prin ecranul de import |
| A1 §3.3, A3 §3.1 | `Incoterm` devine FK spre `core.DeliveryTerm`; `SupplierTerms` primește `TransportModeCode` și `TransactionNatureCode`; comanda primește `TransportModeCode` |

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | DVI-ul și taxele | Document propriu, înregistrat din factura de import (`DVI-FF`), cu țara de expediere, țara de origine și preferința tarifară pe antet și pe linii. ERP-ul **propune** taxa vamală, taxa antidumping, acciza și TVA-ul după cod NC8 + țara de origine + preferință (+ dovada originii); utilizatorul confirmă sau corectează cu valorile din DVI, care sunt cele finale; diferențele dau avertizare | 3.1, 3.1.1, 4.2 | Decis de Aurelian (27.11.2026), completată cu țara pe DVI |
| 2 | TVA-ul la import | Ambele moduri: plătit în vamă (`4426` = `446`) sau cu certificat de amânare (`4426` = `4427`), ales pe DVI | 3.1, 4.2 | Decis de Aurelian (27.11.2026) |
| 3 | Cursul pentru valoarea mărfii importate | Cursul vamal din DVI (valoarea din stoc = valoarea în vamă); diferența față de cursul facturii pe `665` / `765` la NIR; setabil pe cursul BNR al NIR-ului | 3.5, 4.4 | Decis de Aurelian (27.11.2026) |
| 4 | Costul complet pe linia NIR-ului | Păstrat pe componente cu sursa lor (marfă, taxă vamală, acciză, transport, comision, asigurare, manipulare), ca să poată fi explicat oricând | 2 regula 1, 3.2 | Decis de Aurelian (27.11.2026) |
| 5 | Costurile care întârzie | NIR-ul de import nu așteaptă implicit toate costurile; cele sosite după vin prin `CA` (setabil: blocarea NIR-ului până la costurile complete) | 3.5, 4.5 | Decis de Aurelian (27.11.2026) |
| 6 | Taxele refacturate de comisionar | Nu intră a doua oară în cost; doar mută datoria de la vamă la comisionar (`446` = `401`) | 4.3 | Decis de Aurelian (27.11.2026) |
| 7 | Baza de repartizare propusă | Transportul după greutate (dacă toate liniile au greutate), comisionul și asigurarea după valoare, manipularea după cantitate; schimbabilă pe document | 3.4 | Decis de Aurelian (27.11.2026) |
| 8 | Validarea `CA` | Automată sub 500 lei; peste prag, de contabilul de stocuri | 3.5, 4.5 | Decis de Aurelian (27.11.2026) |
| 9 | Achiziția intracomunitară | Factura din PDF sau XML (fără SPV), VIES verificat și salvat, exigibilitatea TVA calculată după art. 285, UIT pe dosar | 4.1 | Decis de Aurelian (27.11.2026) |
| 10 | Dosarul de import | Opțional, doar pentru urmărire (etape, ETA, documente și costuri lipsă), fără efect contabil | 3.3, 4.6 | Decis de Aurelian (27.11.2026) |
| 11 | Nomenclatorul NC8 | Livrat cu produsul (seed generat din fișierele INS 2026: 9.797 coduri NC8, cu toate nivelurile și UM suplimentară); codurile nu se tastează liber | 3.6 | Decis de Aurelian (27.11.2026), la cererea lui |
| 15 | Importul nomenclatoarelor INS | Ecran de import din XLS (registrul INS) sau XML (fișier INS pe nomenclator); analiza arată codurile noi, eliminate și modificate (inclusiv la termenii de livrare) ca propuneri, cu valoarea veche și cea nouă; diferențele doar de formă nu apar; nimic nu se scrie până la acceptare; codurile eliminate primesc înlocuitori din aceeași subpoziție | 3.7 | Decis de Aurelian (27.11.2026), la cererea lui |
| 16 | Nomenclatoare separate | Tabele proprii în Nucleu pentru termenii de livrare, natura tranzacției și modurile de transport, cu seed din fișierul INS; câmpurile Incoterm și mod de transport devin chei străine spre ele; termenii de livrare primesc marcajele ERP-ului pentru transport și asigurare plătite de vânzător | 3.7 | Decis de Aurelian (27.11.2026), la cererea lui |
| 12 | Codul NC8 pe articole | Pus pe familia de articole și moștenit automat de articolele ei; modificabil pe articol (devine cod propriu) și, la nevoie, pe variantă; schimbarea pe familie nu atinge codurile proprii | 3.6 | Decis de Aurelian (27.11.2026), la cererea lui |
| 13 | Codul din DVI diferit de cel al articolului | Avertizare, cu opțiunea de a actualiza articolul cu codul din DVI | 4.2 | Decis de Aurelian (27.11.2026) |
| 14 | Tabelul de taxe pe țări | Pornește gol (fișierele INS nu au taxe); ratele se introduc, se importă sau se salvează din primul DVI pentru cod, doar pentru codurile și țările folosite; TARIC complet nu se livrează | 3.1.1 | Decis de Aurelian (27.11.2026) |

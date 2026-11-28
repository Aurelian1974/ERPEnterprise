# ERP · 02.05 Stocuri · S1 Gestiuni și setări de stoc

26 noiembrie 2026 · Aurelian · **✓ Validat (26.11.2026)**, cu ecranele și toate cele 8 decizii. Prima din cele 7 părți ale modulului 02.05 Stocuri. Împărțirea a fost confirmată de Aurelian pe 26.11.2026, cu trei adăugiri. Prima: depozitul e o entitate distinctă, care poate avea mai multe gestiuni, iar firma, punctul de lucru sau locația au bifa „stocul se ține pe gestiune / pe depozit” (3.8). A doua: metodele de evaluare sunt FIFO, CMP, LIFO și identificarea directă. A treia: notele de mișcare (predare, primire, încărcare, descărcare) intră în S2. Toate deciziile din tabelul de la final sunt confirmate de Aurelian.

Modulul 02.05 Stocuri are schema SQL `inv`, prefixul de cod `Erp.Inventory` și plaja de erori SQL `54000–54999`. Această parte acoperă gestiunile și depozitele, adică ce sunt, de ce tip pot fi, cui aparțin și cum se leagă de locații, de dimensiuni și de conturi. Mai acoperă gestionarii și predarea gestiunii, setările de stoc ale firmei, setările de stoc ale articolului (pe firmă și pe gestiune), politica de stoc negativ și accesul utilizatorilor pe gestiuni. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.1 Locații, §3.3 Articole, §3.3.1 Variante), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C2 lanțul de conturi, C3 închiderea lunii). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Stocul stă doar pe variantă** (`core.ItemVariant`, SKU), niciodată pe model (`core.Item`). Orice linie cu efect în stoc are `ItemVariantId` (Nucleu §3.3.1, regula 1). Cantitatea se ține în UM de bază a modelului.
- Setările pe firmă ale modelului (metoda de evaluare, urmărirea pe lot sau serie, gestiunea implicită) sunt în `inv.ItemStockSettings`, tabel definit în Nucleu §3.3 și deținut de Stocuri. Variantele le moștenesc.
- **Locația** (`core.Location`) e locul fizic: depozit, magazin, fabrică, șantier. Nu are sub-locații. Zonele și rafturile sunt în WMS (02.06), confirmat pe 14.11.2026.
- **Depozitul** (`inv.Depot`) e opțional: un spațiu de depozitare dintr-o locație, care conține mai multe gestiuni. Se folosește doar acolo unde firma, punctul de lucru sau locația au bifa „stocul se ține pe depozit” (3.8). Fără bifă, stocul se ține doar pe gestiune.
- **Gestiunea** (`inv.Warehouse`) e unitatea de evidență cantitativ-valorică: are un gestionar răspunzător, un tip, conturi și reguli. Tot ea e o valoare a dimensiunii „gestiune” din Nucleu (`core.DimensionValue`), creată automat, ca să poată limita drepturile utilizatorilor (`DimensionScope`) și să apară pe orice document.
- Conturile contabile ale gestiunii aparțin Finanțelor, ca la articol (`fin.ItemAccounting`). C2 le folosește în lanțul cheii `Stock`: articol → grupă → tip → **gestiune** → implicit.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Cantitățile și prețurile se țin pe `decimal(19,6)` (`SQL-07`).
- **Ce NU face S1:** nu înregistrează mișcări (S2), nu calculează costul (S3), nu ține loturi și serii (S4), nu face rezervări (S5) și nici inventarul (S6). Rapoartele și tab-ul Stocuri din Configurare sunt în S7. S1 doar definește setările pe care le citesc celelalte părți.

## 1. Scop și perimetru

| Zonă | Ce face S1 | Ce NU face (și cine face) |
| --- | --- | --- |
| Gestiuni | Nomenclatorul de gestiuni pe firmă: cod, tip, locație, proprietatea bunurilor, baza de evaluare, stare | Structura fizică a depozitului: zone, rafturi, celule (WMS 02.06) |
| Depozite | Depozitele, gestiunile din fiecare depozit și nivelul de evidență (pe gestiune sau pe depozit), setat pe firmă, punct de lucru sau locație | Amplasarea pe rafturi (WMS) |
| Gestionari | Gestionarul titular și înlocuitorii, cu perioade; predarea-primirea gestiunii | Contractul de muncă și garanția materială reținută din salariu (HR 02.09) |
| Conturi | Legătura gestiunii cu conturile (`371`, `378`, `4428`, `357`, `8033`), prin tabelul Finanțelor | Rezolvarea contului pe notă (C2) |
| Setările firmei | Metoda de evaluare implicită, politica implicită de stoc negativ, regulile de dată pentru mișcări | Execuția regulilor (S2, S3) |
| Setările articolului | Pe firmă (`inv.ItemStockSettings`) și, opțional, pe gestiune (`inv.ItemWarehouseSettings`): metodă, urmărire, gestiune implicită, minime și maxime | Calculul necesarului de aprovizionare (Achiziții 02.04, MRP 02.08) |
| Stoc negativ | Politica pe gestiune și regulile ei | Regularizarea costului după o ieșire pe minus (S3) |
| Acces | Gestiunea ca valoare de dimensiune, ca drepturile să se poată limita pe gestiuni | Rolurile și atribuirile (Nucleu) |

## 2. Model de domeniu

```
 Company (Nucleu)
   ├─ StockSettings (setările de stoc ale firmei, 1 rând)
   │
   ├─ StockLevelOverride (bifa „stocul se ține pe gestiune / depozit” pe punct de lucru sau locație)
   │
   ├─ Location (Nucleu: depozit, magazin, fabrică, șantier)
   │     ├─ [nivel „gestiune”]  Warehouse × N, legate direct de locație
   │     └─ [nivel „depozit”]   Depot × N ── DepotWarehouse ── Warehouse × N (mai multe gestiuni în depozit)
   │
   │     Warehouse (gestiune: tip, proprietate, bază de evaluare, politică stoc negativ)
   │            ├─ DimensionValue (Nucleu, tip „gestiune”, creată automat, 1:1)
   │            ├─ WarehouseAccounting (Finanțe: cont de stoc, adaos, TVA neexigibilă)
   │            ├─ WarehouseKeeper × N (gestionar titular și înlocuitori, cu perioade)
   │            │      └─ WarehouseHandover (predare-primire, cu inventarul de predare din S6)
   │            └─ ItemWarehouseSettings × N (opțional: minim, maxim, blocare pe articol)
   │
   └─ Warehouse fără locație: tranzit (unul pe firmă, creat automat), bunuri la terți

 Item (model, Nucleu) ── ItemStockSettings (pe firmă: metodă, urmărire, gestiune implicită)
   └─ ItemVariant (SKU) ── moștenește setările modelului
        stocul e pe (gestiune, variantă) sau, la nivelul „depozit”, pe (depozit, gestiune, variantă)
```

Reguli de business ale modelului:

1. **Gestiunea aparține unei singure firme.** Codul e unic pe firmă, fără diferență de majuscule (`SQL-24`). Două firme din grup pot avea fiecare gestiunea `DEP-CJ`.
2. **O locație poate avea mai multe gestiuni, iar o gestiune stă într-o singură locație** (sau în niciuna, pentru tranzit și pentru bunurile aflate la terți). Exemplu: în Magazinul Cluj sunt gestiunea de marfă la preț cu amănuntul și gestiunea de ambalaje SGR. Locația trebuie să fie a aceleiași firme (direct sau printr-un punct de lucru).
3. **Tipul gestiunii și baza de evaluare nu se schimbă după prima mișcare.** Pentru un alt tip se creează o gestiune nouă și stocul se mută prin transfer (S2).
4. **Fiecare gestiune are, la orice dată, cel mult un gestionar titular.** Schimbarea titularului cere o predare-primire. Pentru gestiunile cu bunuri proprii, predarea cere un inventar de predare (S6, OMFP 2861/2009).
5. **Costul se ține pe firmă + gestiune + variantă**, iar la nivelul „depozit” pe firmă + depozit + gestiune + variantă (decizia 3; nivelul „depozit” decis de Aurelian pe 26.11.2026, decizia 8). Două gestiuni ale aceleiași firme pot avea costuri medii diferite pentru aceeași variantă. Mutarea între gestiuni sau depozite se face la costul de unde pleacă marfa (S3). La identificarea directă, costul e al lotului sau al seriei și îl urmează.
6. **Metoda de evaluare (FIFO, CMP, LIFO sau identificare directă) e a modelului, pe firmă**, și se aplică în toate gestiunile firmei cu bază de evaluare „cost”. Gestiunile la preț de vânzare cu amănuntul folosesc metoda lor (3.1). Schimbarea metodei se face doar de la începutul unei luni deschise, cu un document de schimbare (4.5).
7. **Stocul negativ e blocat implicit.** Fiecare gestiune poate avea o politică permisivă (3.6), dar nu pentru variantele urmărite pe lot sau pe serie. Nici gestiunile de custodie nu pot avea politica permisivă.
8. **O gestiune cu stoc nu se dezactivează.** Întâi se golește (transfer sau ieșire), apoi se dezactivează. O gestiune folosită nu se șterge (`SQL-20`).

## 3. Entități și setări

Convențiile comune tuturor tabelelor `inv`: cheie `Id` `int IDENTITY` pentru nomenclatoare și `bigint` pentru tabelele care cresc (`SQL-13`), coloanele de audit `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` (`SQL-19`) și `RowVersion` pe tot ce se editează din interfață (`SQL-11`, `SP-18`).

### 3.1 Gestiune (`Warehouse`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int` | Firma; nu se schimbă |
| `Code`, `Name` | `nvarchar(20)`, `nvarchar(200)` | Cod unic pe firmă prin `CodeKey` (`SQL-24`); căutarea după denumire prin `NameSearch` (`SQL-25`). Codul nu se schimbă după prima mișcare |
| `LocationId` | `int NULL` | `core.Location` a aceleiași firme. Obligatoriu pentru tipurile 1, 2 și 4; gol pentru 3 și 5 |
| `Kind` | `tinyint` | 1 **Depozit** (marfă, materii prime, materiale, produse finite, la cost) · 2 **Magazin** (vânzare cu amănuntul) · 3 **Tranzit** (marfă pe drum între locații, una pe firmă, creată automat) · 4 **Custodie** (bunuri ale altora, păstrate de noi) · 5 **La terți** (bunuri proprii aflate la alții: depozit logistic, consignație la client, prelucrare la terți) |
| `ValuationBasis` | `tinyint` | 1 **Cost** (FIFO sau CMP, după articol) · 2 **Preț de vânzare cu amănuntul** (doar pentru `Kind` = 2). Nu se schimbă după prima mișcare |
| `RetailPriceListId` | `int NULL` | Lista de prețuri cu amănuntul (Vânzări 02.03) din care se ia prețul de intrare. Obligatorie pentru `ValuationBasis` = 2. Trebuie să fie o listă cu amănuntul (`Kind` = 2, RON, cu TVA; Vânzări V2 §3.1, `52107`; precizare din V2, 27.11.2026) |
| `IsOwnGoods` | `bit` | 1 = bunuri proprii (bilanț); 0 = bunuri ale altora (doar `Kind` = 4, evidență extrabilanțieră `8033`). Calculat din tip, stocat pentru interogări |
| `ThirdPartyId` | `int NULL` | Partenerul care deține bunurile (custodie) sau la care se află (la terți). Obligatoriu pentru tipurile 4 și 5 |
| `NegativeStockPolicy` | `tinyint` | 1 Blocat · 2 Permis cu avertisment. Implicit, valoarea din `StockSettings` (3.6) |
| `DimensionValueId` | `int` | Valoarea de dimensiune „gestiune” din Nucleu, creată la salvare prin `IDimensionLookup.EnsureAsync`; 1:1 |
| `DefaultCostCenterId` | `int NULL` | Centrul de cost propus pe bonurile de consum din gestiune (S2) |
| `IsBlocked`, `BlockReason`, `BlockedAt`, `BlockedBy` | | Blocare temporară: nu se validează mișcări cu data de după blocare. Se folosește la inventar (S6) și la predarea gestiunii. Mișcările deja în drum (tranzit) se pot recepționa |
| `DeliveryOrderMode` | `tinyint` | Livrarea către clienți din gestiune: 0 doar aviz direct · 1 aviz direct sau prin dispoziție de livrare · 2 doar prin dispoziție (Vânzări V4 §3.11). Implicit 1 (precizare din Vânzări V4, 27.11.2026) |
| `ReturnWarehouseId` | `int NULL` | Gestiunea de retur asociată: returul de la client din această gestiune intră acolo, cu lotul în Carantină, până la decizia de calitate (Vânzări V6 §3.2). O gestiune de retur e de tipul 1, cu `Purpose` = 2. Gol = returul intră direct în gestiune (precizare din Vânzări V6, 27.11.2026) |
| `Purpose` | `tinyint` | 1 Obișnuită · 2 Retur de la clienți (nu se vinde din ea; ieșirile sunt doar transfer, casare sau retur la furnizor) · 3 Ambalaje SGR (ambalajele goale returnate de clienți, predate administratorului sistemului; fără valoare de stoc, doar cantitate). Implicit 1 (precizare din Vânzări V6, 27.11.2026) |
| `IsActive` | `bit` | Dezactivarea cere stoc zero pe toate variantele (`54006`) |
| `FirstMovementAt` | `datetime2(3) NULL` | Setat de S2 la prima mișcare validată; blochează codul, tipul și baza de evaluare |

Ce înseamnă fiecare tip pentru celelalte părți:

| Tip | Cont de stoc tipic | Documente permise (S2) | Rezervări (S5) | Inventar (S6) |
| --- | --- | --- | --- | --- |
| 1 Depozit | `371`, `301`, `302`, `303`, `345`, `381` (după articol) | Toate | Da | Da |
| 2 Magazin, la cost | `371` | Toate, plus vânzările din POS | Da | Da |
| 2 Magazin, la preț cu amănuntul | `371` la preț cu TVA, `378` adaos, `4428` TVA neexigibilă | Toate, plus vânzările din POS și nota de modificare a prețului (S3) | Da | Da, la preț cu amănuntul |
| 3 Tranzit | `371` (sau contul de stoc al articolului) | Doar transferurile în două etape (S2); nu acceptă documente directe | Nu | Nu (se verifică la închiderea lunii) |
| 4 Custodie | `8033` (extrabilanțier, fără valoare în bilanț) | Intrare și ieșire de custodie (S2); nu se vinde din ea | Nu | Da, cu confirmarea proprietarului |
| 5 La terți | `357`, `354`, `351` (după natura bunului) | Transferul spre terț și înapoi, ieșirea la vânzare (consignație), consumul la prelucrare | Da (consignație) | Prin confirmarea terțului |

Contul concret îl dă lanțul C2. Coloana „cont de stoc tipic” arată ce se configurează de obicei în `fin.WarehouseAccounting` (3.5).

**Gestiunea de magazin la preț cu amănuntul.** Este metoda din OMFP 1802/2014 pentru comerțul cu amănuntul. Stocul se ține în `371` la prețul de vânzare cu TVA. Diferența față de cost stă în `378` (adaos comercial), iar TVA-ul inclus stă în `4428` (TVA neexigibilă). La ieșire, costul se calculează cu coeficientul de adaos al lunii (K). Reguli în S1:

- Prețul de intrare e prețul din `RetailPriceListId` pentru variantă, la data recepției. O variantă fără preț în listă nu se poate recepționa în această gestiune (`54008`).
- Gestiunea ține și cantitatea, pe variantă. Evidența nu se ține doar valoric (nu folosim „gestiune globală valorică”), ca să funcționeze inventarul, POS-ul și rapoartele pe SKU.
- Calculul lui K, descărcarea adaosului și nota de modificare a prețului sunt în S3.

**Gestiunea de tranzit.** Se creează automat odată cu firma (sau la activarea modulului), cu codul `TRANZIT`, și nu se poate dezactiva. Ține marfa trimisă între locații care nu a fost încă recepționată. Transferul în două etape și regulile lui sunt în S2.

### 3.2 Gestionar (`WarehouseKeeper`) și predarea gestiunii (`WarehouseHandover`)

**`inv.WarehouseKeeper`**

| Câmp | Reguli |
| --- | --- |
| `WarehouseId`, `EmployeeId` | Angajatul din `core.Employee` (Nucleu), al aceleiași firme. Un angajat poate fi gestionar la mai multe gestiuni |
| `Role` | 1 Titular · 2 Înlocuitor. Cel mult un titular la o dată (index unic filtrat pe perioade, verificat în procedură) |
| `ValidFrom`, `ValidTo` | `ValidTo` gol = în funcție. Perioadele titularilor nu se suprapun |
| `GuaranteeNote` | Text liber: decizia de numire, garanția materială. Garanția propriu-zisă e în HR |
| `HandoverId` | Predarea prin care a preluat gestiunea (gol pentru primul titular) |

Gestionarul apare pe documentele de stoc tipărite (predat de, primit de) și primește notificările gestiunii: stoc sub minim, lot care expiră, diferență la recepție.

**`inv.WarehouseHandover`** (predare-primire)

| Câmp | Reguli |
| --- | --- |
| `WarehouseId`, `HandoverDate` | Data predării |
| `FromEmployeeId`, `ToEmployeeId` | Gestionarul care predă și cel care primește |
| `InventoryCountId` | Inventarul de predare (S6). Obligatoriu la gestiunile cu bunuri proprii, dacă setarea `RequireCountOnHandover` = 1 (implicit 1) |
| `Status` | 1 Ciornă · 2 Inventar în curs · 3 Finalizată · 4 Anulată |
| `Notes`, atașamente | Procesul-verbal semnat, prin atașamentele Nucleului |

Fluxul e în 4.2. La finalizare, titularul vechi primește `ValidTo` = ziua dinaintea predării, iar cel nou e adăugat cu `ValidFrom` = data predării.

### 3.3 Setările de stoc ale firmei (`StockSettings`)

Un rând pe firmă, creat cu valorile implicite la activarea modulului. Se editează din Configurare › Stocuri (ecranul e în S7, setările sunt definite aici).

| Setare | Implicit | Folosită de | Ce face |
| --- | --- | --- | --- |
| `DefaultValuationMethod` | 2 CMP | S1 | Metoda propusă pentru articolele noi (1 FIFO, 2 CMP, 3 LIFO, 4 Identificare directă) |
| `StockKeepingLevel` | 1 Gestiune | S1, S2 | Nivelul de evidență implicit al firmei: 1 pe gestiune, 2 pe depozit; suprascris pe punct de lucru sau locație (3.8) |
| `DefaultNegativeStockPolicy` | 1 Blocat | S1 | Politica propusă pentru gestiunile noi |
| `RequireCountOnHandover` | 1 | S1, S6 | Predarea gestiunii cere inventar |
| `BackdatingDays` | 5 | S2 | Câte zile în urmă poate fi data unei mișcări față de azi, fără dreptul `Inv.Movement.Backdate` (oricum doar în lunile deschise); 0 = doar data de azi |
| `BackdatingWithRight` | 1 | S2 | Dacă dreptul `Inv.Movement.Backdate` poate trece de limita de mai sus |
| `TransferMode` | 1 După locație | S2 | 1 după locație (aceeași locație o etapă, locații diferite două etape), 2 mereu două etape, 3 mereu o etapă; suprascris pe punct de lucru sau locație (S2 §3.4) |
| `CostRecalcScope` | 1 Automat de la data mișcării retroactive | S3 | Cum se refac costurile după o mișcare cu dată în trecut |
| `LandedCostAllocation` | 1 După valoare | S3 | Repartizarea costurilor adăugate (transport, vamă) pe liniile recepției |
| `ExpiryPickRule` | 1 FEFO | S4 | Ordinea propusă la ieșirea loturilor |
| `ExpiryWarningDays` | 30 | S4 | Avertizarea pentru loturile care expiră |
| `ReservationExpiryDays` | 14 | S5 | Durata implicită a unei rezervări din comandă |
| `CountFreezeMode` | 1 Blochează gestiunea | S6 | Ce se întâmplă cu mișcările în timpul inventarului |

Setările lipsă dintr-o versiune mai veche se completează la actualizare cu valoarea implicită (`MIG-06`).

### 3.4 Setările de stoc ale articolului

**Pe firmă: `inv.ItemStockSettings`** (definit în Nucleu §3.3, detaliat aici). Un rând pe (firmă, model), doar pentru modelele stocabile (`IsStockable` = 1). Variantele moștenesc toate câmpurile.

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `ItemId` | Modelul, nu varianta |
| `DefaultWarehouseId` | Gestiunea propusă pe documente. Trebuie să fie a firmei, activă, de tip 1 sau 2 |
| `ValuationMethod` | 1 FIFO · 2 CMP · 3 LIFO · 4 Identificare directă. Obligatoriu. Se schimbă doar prin documentul de schimbare a metodei (4.5). Identificarea directă cere `TrackingMode` lot sau serie (`54015`): costul se ține pe fiecare lot sau serie. LIFO e permis de OMFP 1802/2014, dar nu de IFRS (IAS 2); dacă firma are un plan alternativ IFRS (C1), alegerea LIFO afișează avertismentul că valoarea stocului în IFRS trebuie ajustată separat |
| `TrackingMode` | 0 Fără · 1 Lot · 2 Lot și termen de valabilitate · 3 Serie · 4 Serie și lot (adăugat de S4, 26.11.2026). Se schimbă doar cât timp nicio variantă a modelului nu are stoc în firmă (`54010`). Detaliile sunt în S4 |
| `MinQty`, `MaxQty` | Pe firmă, în UM de bază, pe variantă (fiecare variantă se compară cu aceste praguri). Opționale |
| `ReplenishmentMethod` | 0 Fără propunere automată · 1 Min/max (necesarul de aprovizionare din Achiziții A2) · 2 MRP (necesarul vine doar din MRP). Implicit 1 dacă modelul are `MinQty` sau `MaxQty`, altfel 0. Citit de Achiziții și MRP, ca o variantă să nu fie propusă din două părți (adăugat pe 26.11.2026, Achiziții A2 decizia 6) |
| `IsBlockedForIssue` | Blocarea ieșirilor pe tot modelul (de exemplu la o retragere de pe piață). Lotul blocat e în S4 |

Rândul se creează automat, cu valorile implicite ale firmei, la prima salvare a unui model stocabil sau la prima mișcare, dacă lipsește.

**Pe gestiune: `inv.ItemWarehouseSettings`** (opțional). Suprascrie pragurile pentru o variantă sau pentru un model într-o anumită gestiune.

| Câmp | Reguli |
| --- | --- |
| `WarehouseId`, `ItemId`, `ItemVariantId` | `ItemVariantId` gol = toate variantele modelului. Rândul cu variantă are prioritate față de cel pe model |
| `MinQty`, `MaxQty`, `ReorderQty` | Pragurile pentru gestiune; citite de alerte (S7) și de propunerile de aprovizionare (Achiziții, MRP) |
| `ReplenishmentMethod` | Opțional; suprascrie metoda de reaprovizionare a firmei în gestiune (adăugat pe 26.11.2026, Achiziții A2) |
| `IsBlockedForIssue`, `IsBlockedForReceipt` | Blocare pe articol în gestiune (de exemplu nu se mai primește marfă veche în magazin) |
| `IsListed` | Varianta e „în sortimentul” gestiunii (folosit de magazine și de POS pentru sincronizare) |

Precedența pentru prag și blocare: rândul pe variantă în gestiune → rândul pe model în gestiune → `ItemStockSettings` pe firmă.

### 3.5 Conturile gestiunii (`fin.WarehouseAccounting`, tabel al Finanțelor)

Conturile țin de Finanțe, ca `fin.ItemAccounting`. Tabelul e definit aici pentru că apare odată cu gestiunea, iar documentul C2 se completează cu el la validarea acestei părți.

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `WarehouseId` | Unic pe gestiune |
| `StockAccountId` | Contul de stoc al gestiunii, folosit în lanțul C2 al cheii `Stock` după articol, grupă și tip. Gol = se ia din articol, grupă sau tip |
| `StockAccountOverride` | `bit`. 1 = contul gestiunii are prioritate față de articol (pentru `357`, `354`, `8033`, unde contul ține de gestiune, nu de articol) |
| `MarkupAccountId` | `378`; obligatoriu pentru gestiunile la preț cu amănuntul |
| `NonExigibleVatAccountId` | `4428`; obligatoriu pentru gestiunile la preț cu amănuntul |
| `CostCenterId` | Centrul de cost implicit pe notele gestiunii |

Lanțul cheii `Stock` din C2 (3.4) devine: override pe linie → **gestiunea cu `StockAccountOverride` = 1** → articol → grupă → tip → gestiunea → implicit. Prima poziție nouă e o completare a C2, decisă de Aurelian pe 26.11.2026 și trecută și în C2.

Cazul din datele demo: Depozitul Cluj nu are cont de stoc, iar grupa articolului din BC 10255 nu are nici ea cont de stoc. Bonul de consum așteaptă deci în „De contat” (C2), iar concordanța `371` arată +3.460,00 (C3). Ecranul gestiunii afișează lipsa contului ca avertisment.

### 3.6 Politica de stoc negativ

| Politică | Documente introduse în ERP | Fapte deja petrecute în afara ERP |
| --- | --- | --- |
| 1 Blocat (implicit) | Validarea e refuzată dacă stocul variantei în gestiune ar scădea sub zero la data mișcării sau la orice dată ulterioară (`54101`, în S2) | Acceptate, vezi mai jos |
| 2 Permis cu avertisment | Validarea trece; utilizatorul vede avertismentul înainte de confirmare, iar documentul primește marcajul „ieșire pe minus” | Acceptate |

„Fapte deja petrecute” sunt vânzările din POS sincronizate după ce bonul fiscal a fost emis și consumurile raportate din producție deja făcute fizic. Ele nu se pot refuza, pentru că bunul a ieșit deja. Mișcarea se înregistrează, gestiunea ajunge pe minus și se creează o alertă „Stoc negativ forțat” pentru gestionar și pentru contabil.

Reguli comune:

- **Loturile și seriile nu pot fi niciodată pe minus.** Politica 2 nu se aplică variantelor cu `TrackingMode` ≠ 0. Faptele din afara ERP pe astfel de variante intră pe un lot tehnic „NEIDENTIFICAT”, care se corectează manual (S4).
- **Gestiunile de custodie și de tranzit au mereu politica 1.**
- **Costul unei ieșiri pe minus** e ultimul cost cunoscut în gestiune (sau, dacă nu există, costul mediu pe firmă). Diferența față de costul real se regularizează automat la prima intrare (S3).
- **La închiderea lunii, stocul negativ blochează.** Pasul `NEGATIVE_STOCK` din lista C3 e blocant, cu override de la Contabilul șef, cu motiv scris.
- **Verificarea pe date ulterioare.** O ieșire cu dată în trecut nu poate lăsa pe minus o dată de după ea, chiar dacă la data ei stocul ajunge. Exemplu: stoc 10 pe 05.11, ieșire de 8 pe 10.11. O ieșire nouă de 5, cu data 03.11, e refuzată, deși pe 03.11 stocul era 10. După ea, stocul din 10.11 ar ajunge la −3.

### 3.7 Accesul pe gestiuni

Gestiunea e o valoare a dimensiunii „gestiune” din Nucleu, deci atribuirea unui rol poate fi limitată la anumite gestiuni prin `UserRoleAssignment.DimensionScope` (Nucleu §3.5). Efecte în Stocuri:

- Un utilizator cu scope pe gestiuni vede, introduce și validează documente doar pe acele gestiuni. Rapoartele de stoc i se filtrează la ele.
- Pe un transfer, utilizatorul trebuie să aibă drept pe gestiunea care predă. Recepția transferului o face cineva cu drept pe gestiunea care primește (S2).
- Gestionarul nu primește drepturi automat. Ecranul gestiunii propune doar adăugarea gestiunii în scope-ul rolului lui, dacă lipsește.
- Scope-ul gol înseamnă toate gestiunile firmei, inclusiv cele create ulterior.

### 3.8 Depozit (`Depot`) și nivelul de evidență a stocului

Cerința lui Aurelian (26.11.2026): pe lângă gestiune există **depozitul**, iar într-un depozit pot fi mai multe gestiuni. Firma, punctul de lucru sau locația au o bifă care spune dacă stocul se ține pe gestiune sau pe depozit.

**Nivelul de evidență** (`StockKeepingLevel`): 1 **Gestiune**, 2 **Depozit**.

- Implicit pe firmă (`StockSettings.StockKeepingLevel`). Se poate suprascrie pe un punct de lucru sau pe o locație (`inv.StockLevelOverride`). Se aplică cea mai apropiată setare: locația, apoi punctul de lucru, apoi firma.
- Nivelul se schimbă doar dacă gestiunile și depozitele din acel perimetru nu au stoc și nici documente în ciornă (`54016`). Altfel se golește stocul prin note de mișcare (S2), se schimbă nivelul și se aduce stocul înapoi.

| | Nivel „gestiune” | Nivel „depozit” |
| --- | --- | --- |
| Unde stă gestiunea | Direct în locație (`Warehouse.LocationId`) | Într-unul sau mai multe depozite (`inv.DepotWarehouse`); `Warehouse.LocationId` gol |
| Cheia stocului | (gestiune, variantă) | (depozit, gestiune, variantă) |
| Costul (FIFO, CMP, LIFO) | Pe gestiune și variantă | Pe depozit, gestiune și variantă |
| Pe documente | Se alege gestiunea | Se alege depozitul pe antet; gestiunea liniei se propune după articol (3.8, regula de propunere) și se poate schimba |
| Disponibil pentru vânzări, rezervări, WMS | Pe gestiune | Pe depozit (suma gestiunilor din el) și pe gestiune |
| Gestionar, conturi, drepturi | Pe gestiune | Tot pe gestiune; drepturile se pot limita și pe depozit |

**`inv.Depot`**

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Code`, `Name` | Cod unic pe firmă (`SQL-24`) |
| `LocationId` | Obligatoriu; locația trebuie să aibă nivelul „depozit”. O locație poate avea mai multe depozite (de exemplu două hale) |
| `DefaultWarehouseId` | Gestiunea propusă când regula de propunere nu găsește alta |
| `DimensionValueId` | Valoare a dimensiunii noi „depozit” din Nucleu, creată automat, ca drepturile să se poată limita pe depozit |
| `IsBlocked`, `IsActive` | La fel ca la gestiune (3.1); blocarea depozitului blochează toate gestiunile din el |

**`inv.DepotWarehouse`** leagă gestiunile de depozite: `DepotId`, `WarehouseId`, `IsActive`. O gestiune poate fi în mai multe depozite (de exemplu gestiunea „Mărfuri” a lui Gheorghe Ilie, în hala A și în hala B), iar un depozit are mai multe gestiuni (mărfuri, ambalaje, custodie). Tipurile Tranzit și La terți nu intră în depozite.

**Regula de propunere a gestiunii** (`inv.DepotWarehouseRule`): `DepotId`, `ItemGroupId` sau `ItemId`, `WarehouseId`. Exemplu: în depozitul DC-PL, grupa Ambalaje merge în gestiunea AMB-PL, iar restul în gestiunea implicită a depozitului.

Date demo (Exemplu Distribuție SRL): firma are nivelul „gestiune”, iar locația Depozit central Ploiești are nivelul „depozit”, cu depozitele HALA-A și HALA-B. Gestiunea DC-PL e în ambele hale, AMB-PL doar în HALA-A, iar CUST-PL doar în HALA-B. În Cluj și Timișoara stocul se ține pe gestiune.

Conturile, gestionarul, politica de stoc negativ și baza de evaluare rămân pe gestiune. Depozitul e locul și nivelul de cheie al stocului, nu unitatea de răspundere.

## 4. Fluxuri principale

### 4.1 Crearea unei gestiuni

1. Utilizatorul cu `Inv.Warehouse.Manage` completează codul, denumirea, tipul, locația, baza de evaluare și politica de stoc negativ. Partenerul se completează doar pentru custodie sau la terți.
2. Handler-ul verifică regulile din 3.1: locația e a firmei, tipul cere sau exclude locația, iar prețul cu amănuntul e permis doar la magazin, cu listă de prețuri.
3. În aceeași tranzacție: `inv.usp_Warehouse_Save` scrie gestiunea, `IDimensionLookup.EnsureAsync` creează valoarea de dimensiune „gestiune” cu același cod și aceeași denumire, iar Finanțele salvează conturile din tab-ul Contabilitate (`fin.usp_WarehouseAccounting_Save`).
4. Se publică `GestiuneModificata` v1.
5. Dacă gestiunea nu are cont de stoc și nici override, ecranul afișează avertismentul. Salvarea nu e blocată, pentru că articolele pot avea cont propriu.

### 4.2 Schimbarea gestionarului (predare-primire)

1. Contabilul sau șeful de depozit creează predarea: gestiunea, data, cine predă, cine primește.
2. Dacă e nevoie de inventar (3.2), din predare se pornește inventarul de predare (S6). Gestiunea se blochează (`IsBlocked`, motiv „Predare gestiune”) de la data predării până la finalizarea inventarului.
3. Inventarul se finalizează în S6, cu diferențele lui (plusuri, minusuri, imputații la gestionarul care predă).
4. Predarea se finalizează: perioadele gestionarilor se actualizează, gestiunea se deblochează și se publică `GestionarSchimbat` v1.
5. Procesul-verbal de predare-primire se tipărește cu ambii gestionari și cu rezultatul inventarului, apoi se atașează semnat.

### 4.3 Blocarea temporară a unei gestiuni

Gestiunea se blochează manual (motiv obligatoriu) sau automat, de inventar (S6) și de predare (4.2). Cât timp e blocată:

- documentele cu data de la blocare încolo se pot salva ca ciornă, dar nu se pot valida (`54005`);
- transferurile trimise înainte de blocare se pot recepționa, pentru că marfa a plecat deja;
- vânzările POS sincronizate se acceptă (sunt fapte deja petrecute) și apar în inventar ca mișcări după data blocării, de tratat separat (S6).

### 4.4 Dezactivarea unei gestiuni

1. Se verifică stocul zero pe toate variantele, fără mișcări în ciornă și fără rezervări active (`54006`).
2. Gestiunea devine inactivă, iar valoarea de dimensiune se dezactivează și ea, prin Nucleu.
3. Gestiunea rămâne vizibilă în rapoarte și pe documentele vechi și se poate reactiva.

### 4.5 Schimbarea metodei de evaluare

OMFP 1802/2014 permite schimbarea politicii contabile doar justificat și cu prezentare în note. Fluxul:

1. Contabilul șef (dreptul `Inv.ValuationMethod.Change`) creează documentul „Schimbare metodă de evaluare”: modelele (sau o grupă), metoda nouă, luna de la care se aplică (prima zi a unei luni deschise, cel mai devreme luna curentă) și motivul.
2. La validare, pentru fiecare variantă și gestiune cu stoc:
   - **FIFO → CMP:** straturile FIFO se adună într-un singur cost mediu, egal cu valoarea totală împărțită la cantitate. Valoarea stocului nu se schimbă.
   - **CMP → FIFO sau LIFO:** stocul existent devine un singur strat, la costul mediu curent.
   - **FIFO ↔ LIFO:** straturile rămân aceleași; se schimbă doar ordinea în care se descarcă.
   - **Spre identificare directă:** cere urmărire pe lot sau serie. Fiecare lot sau serie primește costul stratului din care provine (FIFO, LIFO) sau costul mediu (CMP).
   - **Dinspre identificare directă:** costurile loturilor devin straturi (FIFO, LIFO), în ordinea intrării, sau se adună într-un cost mediu (CMP).
3. Nu apare nicio notă contabilă, pentru că valoarea stocului rămâne aceeași. Se publică `MetodaEvaluareSchimbata` v1, iar S3 aplică noua metodă pentru mișcările de la acea dată.
4. Nu se pot valida mișcări retroactive înaintea datei schimbării în variantele schimbate (`54011`), ca să nu fie nevoie de recalcularea pe două metode.

### 4.6 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Gestiune nouă într-o locație închisă (`Location.IsActive` = 0) | Refuzat (`54003`) |
| Mutarea unei gestiuni în altă locație, după prima mișcare | Permisă doar în aceeași firmă și doar dacă locația nouă are același tip. Istoricul păstrează locația veche, iar rapoartele pe locație folosesc locația de la data mișcării |
| Angajatul gestionar pleacă din firmă (`LeaveDate`) | Jobul zilnic creează o notificare pentru contabil cu 10 zile înainte și în ziua plecării. Gestiunea rămâne fără titular până la predare, iar documentele noi afișează avertismentul „Gestiune fără gestionar” |
| Gestiune de magazin la preț cu amănuntul fără listă de prețuri | Refuzat la salvare (`54007`) |
| Schimbarea `TrackingMode` cu stoc existent | Refuzată (`54010`). Se golește stocul prin transfer într-o gestiune intermediară, se schimbă urmărirea, apoi se aduce înapoi pe loturi |
| Două firme ale grupului în aceeași locație fizică | Imposibil prin model: locația e a unei firme. Se creează două locații cu aceeași adresă, una pe fiecare firmă |
| Licența fără WMS | Stocurile funcționează complet la nivel de gestiune; locațiile din depozit (rafturi) lipsesc |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `GestiuneModificata` v1 | Creare, modificare, blocare, deblocare, dezactivare | `WarehouseId`, `Code`, `Name`, `Kind`, `ValuationBasis`, `LocationId`, `IsBlocked`, `IsActive` | POS (gestiunea casei), WMS, Vânzări, Achiziții, Raportare |
| `GestionarSchimbat` v1 | Finalizarea unei predări | `WarehouseId`, `FromEmployeeId`, `ToEmployeeId`, `HandoverDate`, `InventoryCountId` | HR (răspunderea materială), Raportare |
| `MetodaEvaluareSchimbata` v1 | Validarea schimbării de metodă | `ItemIds`, `OldMethod`, `NewMethod`, `EffectiveDate` | S3 (intern), Finanțe (notele explicative) |

### 5.2 Consumate

| Eveniment | De la | Ce face S1 |
| --- | --- | --- |
| `ArticolModificat` v1 | Nucleu | Creează `ItemStockSettings` cu valorile implicite, pentru modelele stocabile noi din fiecare firmă care are modulul activ |
| `FirmaModificata` v1 | Nucleu | La prima apariție a unei firme: `StockSettings` și gestiunea `TRANZIT` |

## 6. Contracte publice (`Erp.Inventory.Contracts`, partea S1)

```csharp
public enum WarehouseKind : byte { Depot = 1, Store = 2, Transit = 3, Custody = 4, AtThirdParty = 5 }
public enum ValuationBasis : byte { Cost = 1, RetailPrice = 2 }
public enum ValuationMethod : byte { Fifo = 1, WeightedAverage = 2, Lifo = 3, SpecificIdentification = 4 }
public enum StockKeepingLevel : byte { Warehouse = 1, Depot = 2 }

public sealed record DepotDto(int Id, int CompanyId, string Code, string Name, int LocationId,
    int? DefaultWarehouseId, IReadOnlyList<int> WarehouseIds, bool IsBlocked, bool IsActive);
public enum TrackingMode : byte { None = 0, Lot = 1, LotAndExpiry = 2, Serial = 3, SerialAndLot = 4 }   // 4 adăugat de S4
public enum NegativeStockPolicy : byte { Blocked = 1, AllowedWithWarning = 2 }

public sealed record WarehouseDto(
    int Id, int CompanyId, string Code, string Name, WarehouseKind Kind, ValuationBasis ValuationBasis,
    int? LocationId, int? ThirdPartyId, int DimensionValueId, NegativeStockPolicy NegativeStockPolicy,
    bool IsBlocked, bool IsActive);

public sealed record ItemStockPolicyDto(
    int ItemId, int? ItemVariantId, int WarehouseId, ValuationMethod Method, TrackingMode Tracking,
    NegativeStockPolicy NegativeStock, bool IsBlockedForIssue, bool IsBlockedForReceipt,
    decimal? MinQty, decimal? MaxQty, decimal? ReorderQty, ReplenishmentMethod Replenishment);   // ReorderQty, Replenishment: adăugate 26.11.2026 (Achiziții A2)

public enum ReplenishmentMethod : byte { None = 0, MinMax = 1, Mrp = 2 }

// Citit de Vânzări, Achiziții, Producție, POS, WMS
public interface IWarehouseLookup
{
    Task<WarehouseDto?> GetAsync(int warehouseId, CancellationToken ct);
    Task<IReadOnlyList<WarehouseDto>> ListForUserAsync(int companyId, CancellationToken ct);   // aplică DimensionScope
    Task<int?> GetKeeperEmployeeIdAsync(int warehouseId, DateOnly onDate, CancellationToken ct);
    Task<StockKeepingLevel> GetLevelAsync(int locationId, CancellationToken ct);          // 3.8
    Task<DepotDto?> GetDepotAsync(int depotId, CancellationToken ct);
    Task<int?> ProposeWarehouseAsync(int depotId, int itemVariantId, CancellationToken ct); // regula de propunere
}

// Politica efectivă pentru o variantă într-o gestiune (precedența din 3.4 și 3.6, deja aplicată)
public interface IStockPolicyProvider
{
    Task<ItemStockPolicyDto> GetAsync(int warehouseId, int itemVariantId, CancellationToken ct);
    Task<IReadOnlyDictionary<int, ItemStockPolicyDto>> GetManyAsync(
        int warehouseId, IReadOnlyCollection<int> itemVariantIds, CancellationToken ct);
}
```

`IStockPolicyProvider` e folosit de S2 la validarea fiecărui document, pe toate liniile o dată (`GetManyAsync`, un singur apel la baza de date cu TVP, `SP-07`). Rezultatul se păstrează într-un cache în memorie, invalidat de `GestiuneModificata` și de salvarea setărilor.

Comenzi interne S1: `SaveWarehouse`, `BlockWarehouse`, `UnblockWarehouse`, `DeactivateWarehouse`, `ReactivateWarehouse`, `SaveWarehouseKeeper`, `StartHandover`, `CompleteHandover`, `CancelHandover`, `SaveStockSettings`, `SaveItemStockSettings`, `SaveItemWarehouseSettings`, `ChangeValuationMethod`.

## 7. Schema SQL `inv` (partea S1)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.StockSettings` | `Id int` | UQ `CompanyId`; FK `CompanyId` | — |
| `inv.Warehouse` | `Id int` | UX `(CompanyId, CodeKey)`; UQ `DimensionValueId`; UX `(CompanyId) WHERE Kind = 3` (un singur tranzit pe firmă); FK `CompanyId`, `LocationId`, `ThirdPartyId`, `RetailPriceListId`, `DimensionValueId`, `DefaultCostCenterId` | `(CompanyId, NameSearch) INCLUDE (Code, Kind, IsActive)`; `LocationId` |
| `inv.StockLevelOverride` | `Id int` | UX `BranchId WHERE BranchId IS NOT NULL`; UX `LocationId WHERE LocationId IS NOT NULL`; CK exact unul din cele două; FK `CompanyId`, `BranchId`, `LocationId` | — |
| `inv.Depot` | `Id int` | UX `(CompanyId, CodeKey)`; UQ `DimensionValueId`; FK `CompanyId`, `LocationId`, `DefaultWarehouseId` | `LocationId` |
| `inv.DepotWarehouse` | `Id int` | UQ `(DepotId, WarehouseId)`; FK `DepotId`, `WarehouseId` | `WarehouseId` |
| `inv.DepotWarehouseRule` | `Id int` | UX `(DepotId, ItemGroupId) WHERE ItemId IS NULL`; UX `(DepotId, ItemId) WHERE ItemId IS NOT NULL`; FK `DepotId`, `WarehouseId`, `ItemGroupId`, `ItemId` | — |
| `inv.WarehouseKeeper` | `Id int` | FK `WarehouseId`, `EmployeeId`, `HandoverId` | `(WarehouseId, Role, ValidFrom)`; `EmployeeId` |
| `inv.WarehouseHandover` | `Id int` | FK `WarehouseId`, `FromEmployeeId`, `ToEmployeeId`, `InventoryCountId` | `(WarehouseId, HandoverDate)` |
| `inv.ItemStockSettings` | `Id int` | UQ `(CompanyId, ItemId)`; FK `CompanyId`, `ItemId`, `DefaultWarehouseId` | `DefaultWarehouseId` |
| `inv.ItemWarehouseSettings` | `Id int` | UX `(WarehouseId, ItemId) WHERE ItemVariantId IS NULL`; UX `(WarehouseId, ItemVariantId) WHERE ItemVariantId IS NOT NULL`; FK `WarehouseId`, `ItemId`, `ItemVariantId` | `ItemId`, `ItemVariantId` |
| `inv.ValuationMethodChange`, `inv.ValuationMethodChangeLine` | `Id int`, `Id int` | FK `CompanyId`, `ItemId` | `(CompanyId, EffectiveDate)` |
| `fin.WarehouseAccounting` (Finanțe) | `Id int` | UQ `(CompanyId, WarehouseId)`; FK `WarehouseId`, conturile, `CostCenterId` | — |

Exemplu de tabel complet:

```sql
CREATE TABLE inv.Warehouse (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouse PRIMARY KEY,
    CompanyId            int               NOT NULL CONSTRAINT FK_Warehouse_Company REFERENCES core.Company(Id),
    Code                 nvarchar(20)      NOT NULL,
    CodeKey              AS UPPER(Code) PERSISTED,                                            -- SQL-24
    Name                 nvarchar(200)     NOT NULL,
    NameSearch           AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,        -- SQL-25
    LocationId           int               NULL CONSTRAINT FK_Warehouse_Location REFERENCES core.Location(Id),
    Kind                 tinyint           NOT NULL,   -- 1 Depozit, 2 Magazin, 3 Tranzit, 4 Custodie, 5 La terți
    ValuationBasis       tinyint           NOT NULL CONSTRAINT DF_Warehouse_ValuationBasis DEFAULT (1),  -- 1 Cost, 2 Preț cu amănuntul
    RetailPriceListId    int               NULL,       -- FK spre sales.PriceList, adăugat de migrarea modulului Vânzări
    IsOwnGoods           bit               NOT NULL CONSTRAINT DF_Warehouse_IsOwnGoods DEFAULT (1),
    ThirdPartyId         int               NULL CONSTRAINT FK_Warehouse_Partner_ThirdParty REFERENCES core.Partner(Id),
    NegativeStockPolicy  tinyint           NOT NULL CONSTRAINT DF_Warehouse_NegativeStockPolicy DEFAULT (1),
    DimensionValueId     int               NOT NULL CONSTRAINT FK_Warehouse_DimensionValue REFERENCES core.DimensionValue(Id),
    DefaultCostCenterId  int               NULL CONSTRAINT FK_Warehouse_DimensionValue_CostCenter REFERENCES core.DimensionValue(Id),
    IsBlocked            bit               NOT NULL CONSTRAINT DF_Warehouse_IsBlocked DEFAULT (0),
    BlockReason          nvarchar(250)     NULL,
    BlockedAt            datetime2(3)      NULL,
    BlockedBy            int               NULL CONSTRAINT FK_Warehouse_User_BlockedBy REFERENCES core.[User](Id),
    IsActive             bit               NOT NULL CONSTRAINT DF_Warehouse_IsActive DEFAULT (1),
    FirstMovementAt      datetime2(3)      NULL,
    CreatedAt            datetime2(3)      NOT NULL CONSTRAINT DF_Warehouse_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int               NOT NULL,
    ModifiedAt           datetime2(3)      NULL,
    ModifiedBy           int               NULL,
    RowVersion           rowversion        NOT NULL,
    CONSTRAINT UQ_Warehouse_DimensionValue UNIQUE (DimensionValueId),
    CONSTRAINT CK_Warehouse_Kind CHECK (Kind IN (1, 2, 3, 4, 5)),
    CONSTRAINT CK_Warehouse_ValuationBasis CHECK (ValuationBasis IN (1, 2)),
    CONSTRAINT CK_Warehouse_RetailOnlyStore CHECK (ValuationBasis = 1 OR (Kind = 2 AND RetailPriceListId IS NOT NULL)),
    CONSTRAINT CK_Warehouse_Location CHECK (Kind IN (1, 2, 4) OR LocationId IS NULL),  -- 1, 2, 4: locația e obligatorie la nivelul „gestiune”, goală la nivelul „depozit” (verificat în procedură)
    CONSTRAINT CK_Warehouse_ThirdParty CHECK ((Kind IN (4, 5) AND ThirdPartyId IS NOT NULL) OR (Kind IN (1, 2, 3) AND ThirdPartyId IS NULL)),
    CONSTRAINT CK_Warehouse_OwnGoods CHECK (IsOwnGoods = CASE WHEN Kind = 4 THEN 0 ELSE 1 END),
    CONSTRAINT CK_Warehouse_NegativeStockPolicy CHECK (NegativeStockPolicy IN (1, 2) AND (Kind IN (1, 2, 5) OR NegativeStockPolicy = 1)),
    CONSTRAINT CK_Warehouse_Blocked CHECK (IsBlocked = 0 OR (BlockReason IS NOT NULL AND BlockedAt IS NOT NULL))
);
CREATE UNIQUE INDEX UX_Warehouse_CodeKey ON inv.Warehouse (CompanyId, CodeKey);
CREATE UNIQUE INDEX UX_Warehouse_Transit ON inv.Warehouse (CompanyId) WHERE Kind = 3;
CREATE INDEX IX_Warehouse_NameSearch ON inv.Warehouse (CompanyId, NameSearch) INCLUDE (Code, Kind, IsActive);
CREATE INDEX IX_Warehouse_LocationId ON inv.Warehouse (LocationId);
```

Verificările care cer date din alte tabele (locația e a firmei, perioadele gestionarilor nu se suprapun, stoc zero la dezactivare) se fac în procedură, nu prin `CHECK` cu funcții scalare (`SP-12`).

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_Warehouse_Save` | Creare și modificare, cu regulile din 3.1 și blocarea câmpurilor după `FirstMovementAt` | Nu |
| `inv.usp_Warehouse_SetBlocked` | Blocare și deblocare, cu motiv; apelată și din S6 | Nu |
| `inv.usp_Warehouse_Deactivate`, `_Reactivate` | Cu verificarea stocului, a ciornelor și a rezervărilor | Nu |
| `inv.usp_Warehouse_ListForUser` | Gestiunile firmei filtrate după `DimensionScope` (TVP cu valorile permise) | Da (citire pentru alte module) |
| `inv.usp_Depot_Save`, `inv.usp_DepotWarehouse_Save`, `inv.usp_DepotWarehouseRule_Save` | Depozitele, gestiunile din ele și regulile de propunere | Nu |
| `inv.usp_StockLevel_Set` | Nivelul de evidență pe firmă, punct de lucru sau locație, cu verificarea stocului zero | Nu |
| `inv.usp_WarehouseKeeper_Save` | Cu verificarea suprapunerii titularilor | Nu |
| `inv.usp_WarehouseHandover_Start`, `_Complete`, `_Cancel` | Predarea-primirea, cu blocarea și deblocarea gestiunii | Nu |
| `inv.usp_StockSettings_Save` | Setările firmei | Nu |
| `inv.usp_ItemStockSettings_Save`, `_EnsureDefaults` | Setările modelului pe firmă; `_EnsureDefaults` pe set, pentru modelele fără rând | Nu |
| `inv.usp_ItemWarehouseSettings_Save` | Pragurile și blocările pe gestiune | Nu |
| `inv.usp_StockPolicy_Resolve` | Politica efectivă pentru un set de variante într-o gestiune (TVP), cu precedența din 3.4 | Nu |
| `inv.usp_ValuationMethodChange_Validate` | Schimbarea metodei; transformarea straturilor (prin procedura S3) | Nu |

Coduri de eroare (plaja S1 `54000–54099`, prefix API `INV_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 54001 | `INV_WAREHOUSE_NOT_FOUND` | 404 | Gestiune inexistentă sau din altă firmă |
| 54002 | `INV_WAREHOUSE_CODE_EXISTS` | 409 | Cod existent în firmă (fără diferență de majuscule) |
| 54003 | `INV_WAREHOUSE_LOCATION_INVALID` | 422 | Locație din altă firmă, inactivă sau incompatibilă cu tipul |
| 54004 | `INV_WAREHOUSE_LOCKED_FIELD` | 409 | Schimbarea codului, a tipului sau a bazei de evaluare după prima mișcare |
| 54005 | `INV_WAREHOUSE_BLOCKED` | 409 | Validarea unei mișcări într-o gestiune blocată |
| 54006 | `INV_WAREHOUSE_NOT_EMPTY` | 409 | Dezactivare cu stoc, ciorne sau rezervări |
| 54007 | `INV_WAREHOUSE_RETAIL_PRICELIST_REQUIRED` | 422 | Preț cu amănuntul fără listă de prețuri sau pe alt tip decât magazin |
| 54008 | `INV_RETAIL_PRICE_MISSING` | 422 | Variantă fără preț în lista gestiunii, la recepția într-un magazin la preț cu amănuntul |
| 54009 | `INV_KEEPER_OVERLAP` | 409 | Doi gestionari titulari în aceeași perioadă |
| 54010 | `INV_TRACKING_CHANGE_WITH_STOCK` | 409 | Schimbarea urmăririi pe lot sau serie cu stoc existent |
| 54011 | `INV_VALUATION_CHANGE_INVALID` | 409 | Schimbarea metodei într-o lună închisă, sau o mișcare retroactivă înaintea datei schimbării |
| 54012 | `INV_NEGATIVE_POLICY_NOT_ALLOWED` | 422 | Politica permisivă pe custodie sau tranzit |
| 54013 | `INV_HANDOVER_COUNT_REQUIRED` | 409 | Finalizarea predării fără inventar finalizat, când e obligatoriu |
| 54014 | `INV_CONCURRENCY_CONFLICT` | 409 | `RowVersion` diferit (`SP-18`) |
| 54015 | `INV_SPECIFIC_ID_REQUIRES_TRACKING` | 422 | Identificare directă pe un model fără urmărire pe lot sau serie |
| 54016 | `INV_STOCK_LEVEL_CHANGE_WITH_STOCK` | 409 | Schimbarea nivelului de evidență cu stoc sau ciorne în perimetru |
| 54017 | `INV_DEPOT_LOCATION_INVALID` | 422 | Depozit într-o locație cu nivelul „gestiune”, sau gestiune legată direct de o locație cu nivelul „depozit” |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, fără abateri.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Warehouse.View` | Lista și fișa gestiunilor (în limita scope-ului) |
| `Inv.Warehouse.Manage` | Crearea, modificarea, dezactivarea gestiunilor și a depozitelor, regulile de propunere |
| `Inv.Warehouse.Block` | Blocarea și deblocarea manuală |
| `Inv.Warehouse.Keeper` | Gestionarii și predarea-primirea |
| `Inv.Settings.Manage` | Setările de stoc ale firmei |
| `Inv.ItemSettings.Manage` | Tab-ul Stoc pe fișa articolului și pragurile pe gestiune |
| `Inv.ValuationMethod.Change` | Documentul de schimbare a metodei de evaluare |

Roluri livrate (noi, în Nucleu, pentru modulul Stocuri): **Șef depozit** (`View`, `Block`, `Keeper`, `ItemSettings.Manage`), **Gestionar** (`View`, de obicei limitat la gestiunea lui), **Contabil stocuri** (toate, fără `ValuationMethod.Change`). Contabilul șef primește toate drepturile. Tab-ul Contabilitate al gestiunii cere dreptul Finanțelor `Fin.Account.Manage`.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S1 |
| --- | --- |
| Legea contabilității 82/1991 și OMFP 1802/2014 | Evidența cantitativ-valorică pe gestiuni. Conturile `371`, `378`, `4428` pentru comerțul cu amănuntul la preț de vânzare. `357`, `354`, `351` pentru bunurile proprii aflate la terți. `8033` pentru bunurile altora în custodie. Metodele FIFO și CMP; schimbarea metodei doar ca schimbare de politică contabilă, justificată |
| OMFP 2861/2009 (inventarierea) | Inventar la schimbarea gestionarului (predarea-primirea din 4.2) |
| Legea 22/1969 (angajarea gestionarilor, garanții, răspunderea în legătură cu gestionarea bunurilor) | Gestionar numit pe fiecare gestiune, cu istoric; garanția materială ține de HR |
| HG 1170/2018 și legislația SGR | Ambalajele SGR într-o gestiune separată sau cu cont propriu (`381` sau `8035` după politica firmei), legate de articolul garanție din Nucleu (`DepositItemId`) |
| Codul fiscal (TVA) | La magazinele la preț cu amănuntul, TVA-ul inclus în preț stă în `4428` până la vânzare |

Integrări: niciuna externă. Interne: Nucleu (locații, angajați, dimensiuni, drepturi), Finanțe (conturile gestiunii, pasul `NEGATIVE_STOCK` din C3), Vânzări (lista de prețuri cu amănuntul).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Lista gestiunilor | Gestiunile firmei cu tipul, locația, gestionarul, contul de stoc, starea | Contabil, Șef depozit |
| Istoricul gestionarilor | Pe gestiune: gestionarii și perioadele, predările cu inventarul și diferențele | Contabil, Auditor, HR |
| Setări incomplete | Gestiuni fără cont de stoc sau fără gestionar, magazine fără listă de prețuri, modele stocabile fără metodă | Contabil șef |

Balanța de stocuri, fișa de magazie și celelalte rapoarte de stoc sunt în S7.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/s1-gestiuni/](ecrane/s1-gestiuni/). Meniul Stocuri are sus Gestiuni, Depozite și Predări de gestiune, apoi subgrupurile Documente, Stoc, Evaluare, Inventar și Rapoarte.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Gestiuni | Lista gestiunilor firmei (filtre pe tip, locație, stare) și fișa gestiunii selectate: Date generale, Gestionari, Contabilitate, Articole (praguri și blocări), Istoric | `s1-01-gestiuni` |
| Depozite | Arborele locații → depozite → gestiuni, nivelul de evidență cu de unde vine (firmă, punct de lucru, locație), regulile de propunere a gestiunii | `s1-05-depozite` |
| Gestiune · Gestionari | Tab-ul cu gestionarii (titular, înlocuitori, perioade) și predarea-primirea în curs, cu inventarul de predare | `s1-02-gestionari-predare` |
| Articol · tab Stoc | Pe fișa articolului (Nucleu), pentru firma curentă: metoda, urmărirea, gestiunea implicită, pragurile pe firmă și pe gestiuni, stocul pe gestiuni | `s1-03-articol-stoc` |
| Schimbare metodă de evaluare | Documentul din 4.5: modelele, metoda nouă, data, previzualizarea efectului pe straturi | `s1-04-schimbare-metoda` |

Setările de stoc ale firmei (3.3) se editează în Administrare › Configurare, grupul „Stocuri”, desenat în S7.

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S1-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S1-A1 | Codul `dep-cj` e refuzat dacă există `DEP-CJ` în aceeași firmă (`54002`) și e acceptat în altă firmă | Test de integrare |
| S1-A2 | O gestiune de magazin la preț cu amănuntul fără listă de prețuri e refuzată (`54007`); o gestiune de depozit cu preț cu amănuntul e refuzată | Test de integrare |
| S1-A3 | După prima mișcare, schimbarea tipului sau a bazei de evaluare e refuzată (`54004`) | Test de integrare (cu S2) |
| S1-A4 | Crearea gestiunii creează valoarea de dimensiune „gestiune” în aceeași tranzacție; un rollback nu lasă valoarea orfană | Test de integrare |
| S1-A5 | Un utilizator cu scope pe `DEP-CJ` vede doar această gestiune în liste și nu poate valida un bon de consum din `DC-PL` | Test de integrare (cu Nucleu) |
| S1-A6 | Doi titulari cu perioade suprapuse sunt refuzați (`54009`) | Test unitar |
| S1-A7 | Predarea cu inventar obligatoriu nu se finalizează fără inventar (`54013`); la finalizare, perioadele gestionarilor sunt continue și gestiunea e deblocată | Test de integrare (cu S6) |
| S1-A8 | Politica efectivă respectă precedența: variantă în gestiune → model în gestiune → firmă | Test de proprietate |
| S1-A9 | Cu politica „blocat”, o ieșire retroactivă care lasă pe minus o dată ulterioară e refuzată; cu politica permisivă trece cu marcaj | Test de integrare (cu S2) |
| S1-A10 | Politica permisivă nu se aplică unei variante urmărite pe lot; o vânzare POS sincronizată pe lot inexistent intră pe lotul „NEIDENTIFICAT” | Test de integrare (cu S2, S4) |
| S1-A11 | Schimbarea FIFO → CMP păstrează valoarea stocului pe fiecare (gestiune, variantă), la ban | Test de proprietate (cu S3) |
| S1-A12 | Dezactivarea unei gestiuni cu stoc sau cu rezervări e refuzată (`54006`) | Test de integrare |
| S1-A14 | La nivelul „depozit”, aceeași variantă are stoc și cost separate în DC-PL hala A și hala B, pentru aceeași gestiune; la nivelul „gestiune”, cheia nu are depozit | Test de integrare (cu S2) |
| S1-A15 | Identificarea directă pe un model fără lot sau serie e refuzată (`54015`); schimbarea nivelului de evidență cu stoc e refuzată (`54016`) | Test de integrare |
| S1-A13 | Toate procedurile S1 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile S1-A5, S1-A9 și S1-A11 sunt critice: drepturile pe gestiuni protejează stocul fizic, iar stocul negativ și schimbarea metodei afectează direct costul și contabilitatea.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Gestiune vs depozit | Gestiunea e unitatea contabilă, cu gestionar și conturi. Depozitul e spațiul care conține mai multe gestiuni. Locația e locul fizic. Bifa „stocul se ține pe gestiune / pe depozit” se pune pe firmă, punct de lucru sau locație | 2, 3.1, 3.8 | Decis de Aurelian (26.11.2026) |
| 2 | Metode de evaluare | FIFO, CMP (recalculat după fiecare intrare), LIFO și identificare directă (doar cu lot sau serie). Magazinele pot ține stocul la preț cu amănuntul (`371` cu TVA, `378`, `4428`) | 3.1, 3.4, 4.5 | Decis de Aurelian (26.11.2026) |
| 3 | Nivelul costului | Pe firmă + gestiune + variantă; transferul se face la costul gestiunii care predă | 2 (regula 5) | Decis de Aurelian (26.11.2026) |
| 4 | Stoc negativ | Setare pe gestiune, implicit blocat. Politica permisivă nu se aplică pe loturi și serii. Faptele din afara ERP (POS) se acceptă mereu. Stocul negativ blochează închiderea lunii, cu override de la Contabilul șef | 3.6 | Decis de Aurelian (26.11.2026) |
| 5 | Gestiunea de tranzit | Una pe firmă, creată automat, folosită de transferurile în două etape între locații | 3.1 | Decis de Aurelian (26.11.2026) |
| 6 | Inventarul la predarea gestiunii | Obligatoriu implicit (setarea `RequireCountOnHandover`), cu gestiunea blocată până la finalizare | 3.2, 4.2 | Decis de Aurelian (26.11.2026) |
| 7 | Contul pe gestiune cu prioritate | `StockAccountOverride` pe gestiune, pentru `357`, `354`, `8033`; completare a lanțului C2 | 3.5 | Decis de Aurelian (26.11.2026) |
| 8 | Cheia stocului la nivelul „depozit” | Depozit + gestiune + variantă; gestiunea liniei se propune după grupa articolului și se poate schimba | 3.8 | Decis de Aurelian (26.11.2026) |

## Note de implementare (28.11.2026)

| Pas | Ce s-a implementat | Decizii și abateri |
| --- | --- | --- |
| S1.1 | Proiectele `Erp.Inventory.Contracts`, `Domain`, `Application`, `Infrastructure`, `Api`, `Tests` (în `Erp.Host`, `Erp.Worker`, CI); migrarea `2026.11.076` (`inv.StockSettings`, `inv.Warehouse`); procedurile `inv.usp_StockSettings_Ensure`, `_Get`, `_Save`, `inv.usp_Warehouse_Save`, `_SetBlocked`, `_SetActive`, `_List`, `_Get`, `_Lookups`, `_History`; în Nucleu `core.usp_DimensionValue_Sync` cu `IDimensionLookup.SyncAsync` și tipul de dimensiune `DEPOT`; comenzile `SaveWarehouse`, `BlockWarehouse`, `UnblockWarehouse`, `DeactivateWarehouse`, `ReactivateWarehouse`, `SaveStockSettings`, interogările `GetWarehouses`, `GetWarehouse`, `GetWarehouseHistory`, `GetStockSettings`; consumatorul `FirmaModificata` (`InitializeCompanyStock`); evenimentul `GestiuneModificata` v1; seed-ul permisiunilor și al rolurilor; API `/api/inventory/warehouses`, `/settings`; testele S1-A1, A2, A3, A4, A5 (listele și fișa), A12 (tranzitul, dimensiunea), A13 | **Cod nou `54018`** `INV_INVALID_REFERENCE` (partener, centru de cost, gestiune de retur, valoarea de dimensiune). **Dimensiunea** se creează la salvarea gestiunii și se aliniază (cod, denumire, stare) la modificare și la dezactivare, prin Nucleu. **Firma** se inițializează și la prima gestiune sau la prima citire a setărilor, nu doar la `FirmaModificata`. **Locația** e obligatorie pentru depozit, magazin și custodie până la nivelul „depozit” (S1.3). Verificarea stocului, a ciornelor și a rezervărilor la dezactivare (54006) se adaugă cu S2 și S5; `FirstMovementAt` îl scrie S2. Lista de prețuri cu amănuntul nu are încă cheie străină (vine cu Vânzări) |
| S1.2 | Migrarea `2026.11.077` (`inv.WarehouseHandover`, `inv.WarehouseKeeper`, `inv.ItemStockSettings`, `inv.ItemWarehouseSettings`, tipul `inv.IdList`); procedurile `inv.usp_WarehouseKeeper_Save`, `_List`, `inv.usp_WarehouseHandover_Start`, `_Complete`, `_Cancel`, `inv.usp_ItemStockSettings_Get`, `_Save`, `_EnsureDefaults`, `inv.usp_ItemWarehouseSettings_Save`, `_Delete`, `inv.usp_StockPolicy_Layers`; regulile pure `StockPolicyRules`, `KeeperRules` (`Erp.Inventory.Domain`); contractele `IStockPolicyProvider`, `ItemStockPolicyDto`, evenimentul `GestionarSchimbat` v1; comenzile `SaveWarehouseKeeper`, `StartHandover`, `CompleteHandover`, `CancelHandover`, `SaveItemStockSettings`, `SaveItemWarehouseSettings`, `DeleteItemWarehouseSettings`, interogările `GetWarehouseKeepers`, `GetItemStockSettings`; consumatorul `ArticolModificat` (`EnsureItemStockSettings`); API `/api/inventory/warehouses/{id}/keepers`, `/handovers`, `/items/{id}/stock-settings`, `/warehouse-settings`; testele S1-A6 (unitar și în bază), A7 (cu inventarul simulat), A8 (proprietate pe 2.000 de combinații și în bază), A15 (identificarea directă) | **Coduri noi** `54019` `INV_HANDOVER_IN_PROGRESS` (o singură predare deschisă pe gestiune), `54020` `INV_HANDOVER_NOT_OPEN`, `54021` `INV_ITEM_WAREHOUSE_SETTINGS_EXISTS`. **Precedența** (variantă în gestiune → model în gestiune → firmă) se aplică pragurilor, blocării la intrare și metodei de reaprovizionare; **blocarea ieșirilor pe tot modelul**, pusă pe firmă (retragere de pe piață), se aplică oricum, peste rândurile pe gestiune. Procedura dă straturile, iar precedența e în C# (`StockPolicyRules`), ca să poată fi testată ca proprietate; cache-ul în memorie din S1 6 nu e încă pus (S2 citește o dată pe document). **Predarea** cere inventar doar la bunurile proprii (nu la custodie și tranzit) cu `RequireCountOnHandover`; până la S6 o astfel de predare se poate doar anula. Titularul care începe la data predării sau după ea blochează finalizarea (54009). **Metoda** unui model deja configurat se schimbă doar prin documentul din 4.5 (cu S3); verificarea stocului la schimbarea urmăririi (54010) vine cu S2. Notificarea plecării gestionarului (4.6) rămâne pentru jobul zilnic, odată cu notificările Stocurilor |
| S1.3 | Migrarea `2026.11.078` (`inv.StockLevelOverride`, `inv.Depot`, `inv.DepotWarehouse`, `inv.DepotWarehouseRule`); funcția `inv.ufn_LocationStockLevel` (locația → punctul de lucru → firma); procedurile `inv.usp_StockLevel_Get`, `_Set`, `inv.usp_Depot_Save`, `_SetBlocked`, `_List`, `_ProposeWarehouse`, `inv.usp_DepotWarehouse_Save`, `inv.usp_DepotWarehouseRule_Save`, `inv.usp_WarehouseLookup_Keeper`; `inv.usp_Warehouse_Save` cunoaște nivelul „depozit” (gestiune fără locație, 54017 pentru legarea directă de o locație cu nivelul „depozit”); contractele `IWarehouseLookup`, `DepotDto`; comenzile `SaveDepot`, `SetDepotBlocked`, `SetDepotWarehouse`, `SaveDepotRule`, `SetStockLevel`, interogarea `GetDepots`; API `/api/inventory/depots`, `/stock-level`; testele 3.8 (halele din Ploiești, propunerea pe grupă și implicită), S1-A15 (legăturile), 6 (`IWarehouseLookup`) | **Coduri noi** `54022` `INV_DEPOT_CODE_EXISTS`, `54023` `INV_DEPOT_NOT_FOUND`. **Schimbarea nivelului** e refuzată (54017) când ar contrazice legăturile: gestiuni legate direct de o locație care trece pe „depozit”, depozite active într-o locație care revine pe „gestiune”; verificarea stocului și a ciornelor (54016) vine cu S2. **Regula pe grupă** caută și în grupele părinte (cea mai apropiată câștigă). **Gestiunea implicită** a depozitului se alege după ce gestiunea e pusă în depozit; ea nu se poate scoate din depozit. Blocarea depozitului e doar marcată; S2 o aplică la validare. Restrângerea drepturilor pe depozit (dimensiunea „depozit” în scope) se aplică odată cu documentele S2 |
| S1.4 | Ecranele React (`src/Web/erp-web/src/inventory`): `WarehousesPage` (lista cu căutare și filtrele Active, Blocate, Inactive; fișa cu tipul, locația sau partenerul, baza de evaluare și lista de prețuri la magazin, politica de stoc negativ, centrul de cost, livrarea, destinația, gestiunea de retur; blocare cu motiv, deblocare, dezactivare, reactivare; tab-urile Gestionari și Istoric), `KeepersTab` (gestionarii cu perioadele, adăugarea, predarea-primirea cu finalizare și anulare), `DepotsPage` (nivelul de evidență pe firmă și pe locații cu sursa lui, depozitele cu gestiunile lor și gestiunea implicită), `ItemStockTab` (tab-ul Stoc pe fișa articolului, doar cu modulul Stocuri licențiat și pentru articolele stocabile); meniul Stocuri › Gestiuni, Depozite; clientul OpenAPI regenerat; teste Vitest pentru funcțiile pure | **Lipsesc deocamdată**: tab-ul Contabilitate al gestiunii (contul de stoc, `fin.WarehouseAccounting`, cu contarea din S3) și avertismentul „fără cont de stoc”, tab-ul Articole al gestiunii, coloanele de valoare și numărul de variante cu stoc (S2/S3), exportul Excel, regulile de propunere și suprascrierea pe punct de lucru în ecranul Depozite (API-ul există), documentul de schimbare a metodei (s1-04, cu S3). Lista de prețuri cu amănuntul se alege după id până la modulul Vânzări. Ecranele nu au fost verificate vizual în browser (fără Erp.Host pornit în acest mediu) |

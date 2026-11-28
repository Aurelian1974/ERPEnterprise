# ERP · 02.05 Stocuri · S5 Rezervări și disponibil

26 noiembrie 2026 · Aurelian · **Validat (26.11.2026)**. A cincea din cele 7 părți ale modulului 02.05 Stocuri. Pornește de la S1–S4, validate pe 26.11.2026. S5 răspunde la două întrebări: **cât pot promite acum** (disponibilul) și **cât pot promite la o dată** (disponibilul proiectat). Ține rezervările cerute de comenzi, transferuri și producție, și alocă automat marfa care intră cererilor care așteaptă.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori S5 `54550–54649`. Documente părinte: [S1 Gestiuni, depozite și setări](S1-GESTIUNI-SI-SETARI.md), [S2 Mișcări și documente](S2-MISCARI-SI-DOCUMENTE.md), [S3 Evaluare și cost](S3-EVALUARE-SI-COST.md), [S4 Loturi, serii și termene](S4-LOTURI-SERII-TERMENE.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Stocul stă pe variantă (SKU) și, la articolele urmărite, pe CKU (SKU + lot + termen) sau pe serie (S4). Cheia stocului e (gestiune, SKU, CKU) sau, la nivelul „depozit”, (depozit, gestiune, SKU, CKU) (S1 §3.8, S2).
- **Rezervarea nu e o mișcare.** Nu schimbă stocul, nu are cost și nu are notă contabilă. Scade doar disponibilul. Mișcarea (S2) vine la livrare și consumă rezervarea.
- Vânzări, Producție, WMS și e-commerce rezervă prin portul `IStockLedger` (S2 §6), extins de S5 (6), în tranzacția documentului lor. Comanda e a Vânzărilor; S5 ține doar cantitatea rezervată și legătura cu linia comenzii.
- Gestiunile de tranzit și de custodie nu primesc rezervări (S1 §3.1). Loturile în carantină, blocate, expirate sau retrase nu sunt disponibile (S4 §3.3).
- **Ce NU face S5:** nu face comenzi de vânzare, oferte sau comenzi de aprovizionare (Vânzări, Achiziții), nu planifică necesarul de materiale (MRP, Producție 02.08), nu face picking-ul (WMS) și nu calculează termenul de livrare promis clientului (Vânzări îl ia din disponibilul proiectat).

## 1. Scop și perimetru

| Zonă | Ce face S5 | Ce NU face (și cine face) |
| --- | --- | --- |
| Disponibil | Stocul, minus nedisponibilul (loturi nedisponibile, gestiuni blocate), minus rezervatul, pe gestiune, depozit, firmă | Stocul în sine (S2) |
| Rezervări | Ferme și provizorii, generice pe SKU sau specifice pe CKU sau serie, cu expirare și istoric | Comanda (Vânzări), ordinul de producție (Producție) |
| Cereri în așteptare | Partea unei cereri care nu a putut fi rezervată; alocarea automată la intrările de marfă, după prioritate | Comanda la furnizor pentru lipsă (Achiziții) |
| Disponibil proiectat | Pe zile: disponibilul de azi, plus intrările așteptate, minus ieșirile rezervate și provizorii | Planificarea aprovizionării (Achiziții, MRP) |
| Preluare | Mutarea unei rezervări de la o comandă la alta, cu drept și motiv | Negocierea cu clienții |

## 2. Model de domeniu

```
 Stock (S2) ── QtyOnHand
   minus nedisponibil: loturi în carantină, blocate, expirate, retrase (S4); gestiune blocată (S1)
   minus ReservedQty (rezervările ferme specifice pe rând + cele generice pe SKU)
   = Disponibil

 Reservation (o linie de cerere: comandă, transfer, consum, producție)
   Kind: 1 Fermă · 2 Provizorie
   Scope: generică (gestiune + SKU) · specifică (gestiune + CKU sau serie)
   QtyRequested = QtyReserved + QtyWaiting ; QtyReserved scade prin QtyConsumed (livrare) și QtyReleased
   Status: Activă · În așteptare · Parțial livrată · Livrată · Expirată · Anulată · Neacoperită
        └──< ReservationLog (fiecare schimbare: cine, ce, de ce)

 Intrare de marfă (S2: recepție, transfer primit, eliberare din carantină, plus de inventar)
   └──► inv.usp_Reservation_AllocateWaiting: cererile în așteptare, după prioritate și dată

 Disponibil proiectat (calculat, nu stocat):
   azi + intrări așteptate (comenzi furnizor, transferuri în tranzit, ordine de producție) − cereri ferme și provizorii, pe zile
```

Reguli de business ale modelului:

1. **Disponibilul nu poate fi negativ din rezervări.** O rezervare fermă nu poate depăși disponibilul în momentul creării (`54551`). Ce nu ajunge intră în așteptare (decizia 4).
2. **O ieșire nu poate lua marfa rezervată pentru altcineva.** Ieșirea dintr-o gestiune poate folosi disponibilul plus rezervarea proprie (a liniei de comandă din care vine). Altfel e refuzată (`54552`), chiar dacă stocul fizic există. Excepția sunt faptele externe (POS), care se acceptă mereu; rezervarea afectată devine Neacoperită (decizia 7).
3. **Rezervarea generică nu stă pe lot.** O rezervare pe gestiune + SKU nu blochează un CKU anume. Lotul se alege la livrare prin FEFO (S4 §3.5), din loturile disponibile minus rezervările specifice. Rezervarea specifică (CKU sau serie) blochează exact acel CKU sau acea bucată (decizia 2).
4. **Rezervarea generică respectă condițiile clientului.** La creare, S5 verifică disponibilul doar pe loturile eligibile pentru client (durata minimă rămasă, S4 §3.2). Dacă un lot devine neeligibil până la livrare, rezervarea poate deveni Neacoperită.
5. **Rezervarea are un singur proprietar.** Fiecare rezervare e legată de o linie de document (`SourceModule`, `SourceLineId`). Anularea liniei anulează rezervarea; livrarea liniei o consumă.
6. **Rezervările provizorii nu blochează nimic.** Apar doar în disponibilul proiectat și în rapoarte (decizia 1).

## 3. Funcționalitate

### 3.1 Disponibilul

Pentru o cheie (gestiune sau depozit, SKU, opțional CKU), la momentul cererii:

| Element | De unde vine |
| --- | --- |
| În stoc | `inv.Stock.QtyOnHand` (S2) |
| Nedisponibil | Rândurile `inv.Stock` ale CKU-urilor cu `Lot.Status` ≠ 1 (S4); tot stocul unei gestiuni blocate (`Warehouse.IsBlocked`, S1); lotul `NEIDENTIFICAT` |
| Rezervat | Suma `QtyReserved − QtyConsumed − QtyReleased` a rezervărilor ferme active pe cheie |
| **Disponibil** | În stoc − Nedisponibil − Rezervat |
| În drum | Transferurile în tranzit spre gestiune (S2), comenzile la furnizor confirmate (Achiziții), ordinele de producție lansate (Producție), cu data așteptată |

`inv.Stock` primește coloana `ReservedQty` (doar pentru rezervările specifice, pe rândul CKU-ului), iar rezervările generice se țin în `inv.ReservationBalance` (gestiune, depozit, SKU). Ambele se actualizează în tranzacția rezervării, cu `UPDLOCK` pe rândul cheii (`SP-11`), ca două rezervări simultane să nu ia aceeași marfă. Disponibilul se citește prin `inv.usp_Availability_Get` (citire, fără blocare) și prin `IStockLedger.GetAvailabilityAsync` (S2, cu `Reserved` și `Available` completate de S5).

Gestiunile cu `Purpose` = 2 (retur de la clienți) și 3 (ambalaje SGR) din S1 nu intră în disponibil și nu se rezervă: marfa returnată devine disponibilă abia după transferul în gestiunea vandabilă, la decizia de calitate (precizare din Vânzări V6, 27.11.2026).

La nivelul „depozit” (S1 §3.8), disponibilul se arată pe depozit (suma gestiunilor din el) și pe gestiune; rezervarea se face pe gestiunea liniei, propusă după regula depozitului.

### 3.2 Rezervările

| Câmp | Reguli |
| --- | --- |
| `Kind` | 1 **Fermă**: blochează cantitatea. 2 **Provizorie**: nu blochează, apare doar în disponibilul proiectat (decizia 1) |
| `WarehouseId`, `DepotId`, `ItemVariantId` | Cheia; gestiunea nu poate fi de tranzit sau custodie (`54553`) |
| `LotId`, `SerialId` | Goale pentru rezervarea generică; completate pentru cea specifică (decizia 2) |
| `SourceModule`, `SourceDocumentType`, `SourceDocumentId`, `SourceLineId` | Linia care cere marfa: comandă de vânzare, notă de predare în ciornă, bon de consum, ordin de producție, comandă e-commerce |
| `PartnerId`, `RequiredDate` | Clientul și data cerută de livrare |
| `QtyRequested`, `QtyReserved`, `QtyWaiting`, `QtyConsumed`, `QtyReleased` | `QtyRequested = QtyReserved + QtyWaiting`; disponibilul blocat = `QtyReserved − QtyConsumed − QtyReleased` |
| `Priority` | 1–9, implicit 5; propusă din clientul comenzii (`CustomerTerms.ReservationPriority`, Vânzări V1, copiată pe comandă) și modificabilă cu `Inv.Reservation.Priority` |
| `ExpiresAt` | Data creării + `ReservationExpiryDays` (S1, implicit 14), suprascrisă pe client sau pe document (decizia 3) |
| `Status` | 1 Activă · 2 În așteptare (tot sau o parte) · 3 Parțial livrată · 4 Livrată · 5 Expirată · 6 Anulată · 7 Neacoperită |

**Comenzile de vânzare (precizare din Vânzări V3, 27.11.2026):** rezervă ferm doar livrările din orizontul `ReservationHorizonDays` (implicit 14 zile); restul rămâne cerere deschisă (`ISalesOrderDemand`, pe livrare) și se rezervă zilnic, când intră în orizont. Vânzările trimit `ExpiresAt` = data cerută a ultimei livrări rezervate + `ReservationExpiryDays`, nu data creării + durata.

**Cine creează rezervări:**

| Sursă | Felul | Când |
| --- | --- | --- |
| Comandă de vânzare confirmată (Vânzări) | Fermă | La confirmare; se ajustează la modificarea liniei |
| Ofertă sau comandă în ciornă (Vânzări) | Provizorie, opțional | Dacă agentul bifează „rezervă provizoriu” |
| Comandă e-commerce sau B2B | Fermă | La plasare, cu expirare scurtă (ex. 2 zile pentru plata cu ordin) |
| Dispoziție de livrare lansată (Vânzări V4) | Fermă, specifică pe lotul propus FEFO | La lansare, rezervarea generică a liniei de comandă devine specifică pe loturile propuse; schimbarea lotului la pregătire mută rezervarea; retragerea în ciornă sau anularea o face din nou generică; avizul o consumă (precizare din Vânzări V4, 27.11.2026) |
| Notă de predare în ciornă (S2) | Fermă, specifică dacă linia are lot | La salvare, ca marfa să nu plece între timp |
| Ordin de producție lansat (Producție) | Fermă | Pe materiile prime, la lansare |
| Manual (Stocuri, ecranul `s5-02`) | Fermă sau provizorie | Cu motiv, pentru un client sau un eveniment |

**Consumarea.** Avizul, factura sau nota de livrare trimite în `PostAsync` câmpul `ConsumesReservationOfSourceLineId` (linia comenzii). S5 scade `QtyConsumed` în aceeași tranzacție. Livrarea mai mare decât rezervarea folosește diferența din disponibil; dacă nu ajunge, e refuzată (`54552`).

**Expirarea.** Jobul `inv.usp_Reservation_ExpireScan` (Hangfire, la fiecare oră) eliberează rezervările ferme expirate și le trece în Expirată. Cu 2 zile înainte, agentul comenzii primește o notificare (Nucleu) și poate prelungi rezervarea cu `Inv.Reservation.Extend`. Marfa eliberată merge întâi cererilor în așteptare (3.3).

### 3.3 Cereri în așteptare și alocarea automată

Când disponibilul nu ajunge, rezervarea fermă se face pentru cât există, iar restul rămâne în `QtyWaiting` (decizia 4). Cererea nu se pierde și nu trebuie refăcută.

La fiecare intrare care crește disponibilul pe o cheie, `inv.usp_Reservation_AllocateWaiting` rulează în tranzacția intrării și alocă cantitatea nouă cererilor în așteptare de pe aceeași cheie:

1. Intrările care declanșează alocarea: recepția (Achiziții), primirea unui transfer (S2), predarea din producție, eliberarea unui lot din carantină sau deblocarea lui (S4), plusul de inventar (S6), expirarea sau anularea altei rezervări.
2. Ordinea: `Priority` (1 e cea mai mare), apoi `RequiredDate`, apoi data cererii.
3. Pentru cererile cu condiții de lot (durata minimă rămasă a clientului), se alocă doar dacă CKU-ul intrat e eligibil.
4. Fiecare alocare scrie în `ReservationLog` și publică `RezervareAlocata`, ca Vânzările să anunțe agentul („comanda CV 4433 e acum acoperită complet”).

Alocarea automată se poate opri pe firmă (`StockSettings.AutoAllocateWaiting` = 0); atunci cererile în așteptare se alocă din ecranul `s5-03`.

**Marfa comandată la furnizor nu se rezervă ferm** (decizia 6). Comanda la furnizor apare în disponibilul proiectat, cu data așteptată, iar cererile în așteptare se acoperă când marfa chiar intră.

### 3.4 Disponibilul proiectat

Calculat la cerere de `inv.usp_Availability_Projected`, pe o cheie și un interval de zile (implicit 30):

```
Proiectat(ziua z) = Disponibil azi
                  + Σ intrări așteptate cu data ≤ z (transferuri în tranzit, comenzi la furnizor confirmate, ordine de producție)
                  − Σ cereri în așteptare cu data cerută ≤ z
                  − Σ rezervări provizorii cu data cerută ≤ z
```

Sursele termenilor (precizare din Vânzări V1, 27.11.2026): intrările așteptate din comenzile la furnizor se citesc prin `IPurchaseOrderSupply` (Achiziții A1 §6), iar cererea deschisă din comenzile clienților prin `ISalesOrderDemand` (Vânzări V1 §6), simetric: comandat − livrat − închis, pe gestiune și data cerută, cu TVP. Partea deja rezervată ferm a unei comenzi nu se scade a doua oară. Comenzile confirmate cu `CreditHold` (limită de credit depășită) intră în proiectat și sunt marcate în ecran.

Rezervările ferme sunt deja scăzute din disponibilul de azi. Vânzările folosesc proiectatul pentru termenul de livrare propus: prima zi de la care proiectatul acoperă cantitatea cerută. O zi cu proiectat negativ e marcată în ecran (`s5-04`), ca semnal pentru Achiziții.

### 3.5 Preluarea unei rezervări

Uneori o comandă urgentă are nevoie de marfa rezervată deja pentru alta. Preluarea (decizia 5):

- se face doar cu dreptul `Inv.Reservation.Override` (implicit **Șef vânzări**), cu motiv obligatoriu;
- mută cantitatea de la rezervarea sursă la cea destinație; sursa trece în așteptare pentru cantitatea pierdută (nu se anulează);
- agentul comenzii sursă primește o notificare, cu numele celui care a preluat și motivul;
- se jurnalizează în ambele rezervări.

Preluarea nu e permisă de la o rezervare specifică pe serie a unei comenzi deja facturate în avans (`54556`).

### 3.6 Situații speciale

| Situație | Comportament |
| --- | --- |
| Fapt POS care ia marfa rezervată | Acceptat (fapt deja petrecut, S2). Rezervările de pe cheie rămase fără acoperire trec în Neacoperită, în ordinea inversă priorității; agentul e notificat (decizia 7) |
| Lot rezervat specific care trece în carantină, blocat sau retras (S4) | Rezervarea devine Neacoperită; Vânzările primesc `RezervareNeacoperita` și pot cere alt lot |
| Gestiune blocată (predare de gestiune, inventar) | Rezervările rămân; disponibilul gestiunii e 0 până la deblocare; livrările așteaptă (decizia 8) |
| Comandă modificată (cantitate mai mică) | Rezervarea scade; marfa eliberată merge la cererile în așteptare |
| Comandă anulată | Rezervarea trece în Anulată; marfa eliberată merge la cererile în așteptare |
| Inventar cu minus pe o cheie cu rezervări | Rezervările neacoperite se marchează Neacoperită (S6) |
| Gestiune dezactivată | Refuzată cât timp are rezervări active (S1, `54006`) |

## 4. Fluxuri principale

### 4.1 Comanda care nu e acoperită complet

1. Pe 26.11.2026, la 15:10, Vânzările confirmă comanda CV 4433 de la Cafe Unirii SRL: 600 buc APA05 din DEP-CJ, livrare 28.11.
2. Disponibilul în DEP-CJ e 360 buc: 5.760 în stoc, minus 1.920 în carantină (lotul `L2611-15`), minus 3.480 rezervate (CV 4421 Mega Retail 3.000, NP 000820 480).
3. S5 rezervă 360 buc și pune 240 în așteptare. Comanda arată „acoperită parțial”.
4. La 16:00, Mihaela Dobre eliberează lotul `L2611-15` din carantină (S4). Disponibilul crește cu 1.920, iar alocarea automată acoperă cele 240 buc. Agentul primește notificarea.

### 4.2 Preluarea pentru o comandă urgentă

1. Hotel Central SRL cere urgent, prin comanda CV 4436, 480 buc APA05 din DEP-CJ, cu livrare pe 27.11. În DEP-CJ nu mai e nimic disponibil.
2. Șeful de vânzări, Radu Stan, preia 480 buc de la CV 4421 (Mega Retail, livrare 02.12), cu motivul „livrare urgentă, Mega Retail se acoperă din recepția de 02.12”.
3. CV 4421 trece în așteptare pentru 480 buc; agentul ei e notificat.

### 4.3 Livrarea care consumă rezervarea

1. Avizul EXD 30618 se validează pe linia comenzii CV 4421 (3.000 buc).
2. `PostAsync` trimite `ConsumesReservationOfSourceLineId`; S4 alege loturile prin FEFO (`L2610-18` 2.400, `L2611-07` 600), iar S5 scade rezervarea generică cu 3.000.
3. Rezervarea trece în Livrată.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `RezervareModificata` v1 | Creare, modificare, prelungire, expirare, anulare, preluare | `ReservationId`, sursa, cheia, `Kind`, cantitățile, `Status`, motivul | Vânzări, WMS, e-commerce |
| `RezervareAlocata` v1 | Alocarea automată a unei cereri în așteptare | `ReservationId`, sursa, cantitatea alocată, ce intrare a declanșat-o | Vânzări (notifică agentul) |
| `RezervareNeacoperita` v1 | O rezervare rămâne fără marfă (POS, lot blocat, inventar) | `ReservationId`, sursa, cantitatea descoperită, cauza | Vânzări, Notificări |
| `DisponibilModificat` v1 | Grupat la 5 minute, pe SKU și gestiunile marcate „publice” | `ItemVariantId`, lista `{WarehouseId, Available}` | e-commerce, portal B2B, POS |

### 5.2 Consumate

| Eveniment | De la | Ce face S5 |
| --- | --- | --- |
| `MiscareStocValidata` v1 | S2 (intern) | Intrările declanșează alocarea cererilor în așteptare (3.3) |
| `LotStareSchimbata` v1 | S4 (intern) | Rezervările specifice pe lot nedisponibil devin Neacoperită; eliberarea din carantină declanșează alocarea |
| `ComandaVanzareAnulata` v1 | Vânzări | Anulează rezervările liniilor (dacă Vânzări nu a apelat deja `ReleaseAsync`) |

## 6. Contracte publice (partea S5)

Extensia portului de stoc din Nucleu (`Erp.Core.Contracts`):

```csharp
public interface IStockLedger   // continuare din S2, S3, S4
{
    Task<ReservationResult> ReserveAsync(ReservationRequest request, CancellationToken ct);        // creează sau ajustează rezervarea liniei
    Task ReleaseAsync(string sourceModule, long sourceLineId, ReleaseReason reason, CancellationToken ct);
    Task<IReadOnlyList<ProjectedAvailabilityDto>> GetProjectedAvailabilityAsync(
        int companyId, int? depotId, int warehouseId, int itemVariantId, DateOnly from, int days, CancellationToken ct);
}

public enum ReservationKind : byte { Firm = 1, Tentative = 2 }
public enum ReservationStatus : byte { Active = 1, Waiting = 2, PartiallyDelivered = 3, Delivered = 4, Expired = 5, Cancelled = 6, Uncovered = 7 }
public enum ReleaseReason : byte { LineCancelled = 1, LineReduced = 2, Manual = 3, Expired = 4 }

public sealed record ReservationRequest(
    int CompanyId, string SourceModule, string SourceDocumentType, long SourceDocumentId, long SourceLineId,
    int? DepotId, int WarehouseId, int ItemVariantId, int? LotId, int? SerialId, decimal QtyBase,
    ReservationKind Kind, int? PartnerId, DateOnly RequiredDate, byte? Priority, DateTime? ExpiresAt, bool AllowWaiting);

public sealed record ReservationResult(long ReservationId, decimal QtyReserved, decimal QtyWaiting, ReservationStatus Status);
public sealed record ProjectedAvailabilityDto(DateOnly Day, decimal Incoming, decimal Outgoing, decimal Projected);
```

`StockMovementRequest` (S2) primește, aditiv, câmpul `long? ConsumesReservationOfSourceLineId`. `StockAvailabilityDto` (S2) primește, aditiv, `Unavailable`, `Waiting` și `InTransitIn`.

Comenzi interne S5: `CreateManualReservation`, `ExtendReservation`, `ChangeReservationPriority`, `TakeOverReservation`, `AllocateWaitingManually`, `CancelReservation`.

## 7. Schema SQL `inv` (partea S5)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.Reservation` | `Id bigint` | UX `(SourceModule, SourceLineId, KindKey)` pentru rezervările nefinalizate (`WHERE Status IN (1, 2, 3, 7)`); FK `WarehouseId`, `DepotId`, `ItemVariantId`, `LotId`, `SerialId`, `PartnerId` | `(WarehouseId, ItemVariantId, Status) INCLUDE (QtyReserved, QtyConsumed, QtyReleased, QtyWaiting, Priority, RequiredDate)`; `(ExpiresAt) WHERE Status IN (1, 3)` |
| `inv.ReservationLog` | `Id bigint` | FK `ReservationId`, `RelatedReservationId` (la preluare) | `(ReservationId, CreatedAt)` |
| `inv.ReservationBalance` | `Id bigint` | UX `(WarehouseId, DepotKey, ItemVariantId)`; `CHECK (GenericReservedQty >= 0)` | — |
| `inv.Stock` (coloană nouă) | — | `ReservedQty decimal(19,6) NOT NULL DEFAULT 0` (rezervări specifice); `CHECK (ReservedQty >= 0)` | — |
| `inv.StockSettings` (coloane noi) | — | `AutoAllocateWaiting bit NOT NULL DEFAULT 1`, `ReservationWarnDays tinyint NOT NULL DEFAULT 2` | — |
| `inv.PartnerReservationRule` | `Id int` | UX `(CompanyId, PartnerId)`; FK `PartnerId` | — |

`inv.PartnerReservationRule` ține, pe client, durata rezervării (`ExpiryDays`) și prioritatea implicită.

Exemplu de tabel complet:

```sql
CREATE TABLE inv.Reservation (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reservation PRIMARY KEY,
    CompanyId           int            NOT NULL CONSTRAINT FK_Reservation_Company REFERENCES core.Company(Id),
    Kind                tinyint        NOT NULL,      -- 1 fermă, 2 provizorie
    KindKey             AS CAST(Kind AS tinyint) PERSISTED,
    DepotId             int            NULL CONSTRAINT FK_Reservation_Depot REFERENCES inv.Depot(Id),
    WarehouseId         int            NOT NULL CONSTRAINT FK_Reservation_Warehouse REFERENCES inv.Warehouse(Id),
    ItemVariantId       int            NOT NULL CONSTRAINT FK_Reservation_ItemVariant REFERENCES core.ItemVariant(Id),
    LotId               int            NULL CONSTRAINT FK_Reservation_Lot REFERENCES inv.Lot(Id),
    SerialId            int            NULL CONSTRAINT FK_Reservation_Serial REFERENCES inv.Serial(Id),
    SourceModule        varchar(20)    NOT NULL,      -- 'SALES', 'INV', 'PROD', 'ECOM', 'MANUAL'
    SourceDocumentType  varchar(20)    NOT NULL,
    SourceDocumentId    bigint         NOT NULL,
    SourceLineId        bigint         NOT NULL,
    PartnerId           int            NULL CONSTRAINT FK_Reservation_Partner REFERENCES core.Partner(Id),
    RequiredDate        date           NOT NULL,
    Priority            tinyint        NOT NULL CONSTRAINT DF_Reservation_Priority DEFAULT (5),
    QtyRequested        decimal(19,6)  NOT NULL,
    QtyReserved         decimal(19,6)  NOT NULL,
    QtyWaiting          decimal(19,6)  NOT NULL,
    QtyConsumed         decimal(19,6)  NOT NULL CONSTRAINT DF_Reservation_QtyConsumed DEFAULT (0),
    QtyReleased         decimal(19,6)  NOT NULL CONSTRAINT DF_Reservation_QtyReleased DEFAULT (0),
    Status              tinyint        NOT NULL,
    ExpiresAt           datetime2(0)   NULL,
    Reason              nvarchar(400)  NULL,
    CreatedAt           datetime2(3)   NOT NULL CONSTRAINT DF_Reservation_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int            NOT NULL,
    ModifiedAt          datetime2(3)   NULL,
    ModifiedBy          int            NULL,
    RowVersion          rowversion     NOT NULL,
    CONSTRAINT CK_Reservation_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_Reservation_Status CHECK (Status BETWEEN 1 AND 7),
    CONSTRAINT CK_Reservation_Priority CHECK (Priority BETWEEN 1 AND 9),
    CONSTRAINT CK_Reservation_Qty CHECK (QtyRequested > 0 AND QtyReserved >= 0 AND QtyWaiting >= 0
        AND QtyRequested = QtyReserved + QtyWaiting AND QtyConsumed + QtyReleased <= QtyReserved),
    CONSTRAINT CK_Reservation_Serial CHECK (SerialId IS NULL OR QtyRequested = 1)
);
CREATE UNIQUE INDEX UX_Reservation_SourceLine ON inv.Reservation (SourceModule, SourceLineId, KindKey) WHERE Status IN (1, 2, 3, 7);
CREATE INDEX IX_Reservation_Key ON inv.Reservation (WarehouseId, ItemVariantId, Status)
    INCLUDE (LotId, QtyReserved, QtyConsumed, QtyReleased, QtyWaiting, Priority, RequiredDate);
CREATE INDEX IX_Reservation_Expiry ON inv.Reservation (ExpiresAt) WHERE Status IN (1, 3);
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_Reservation_Upsert` | Creează sau ajustează rezervarea unei linii; verifică disponibilul cu `UPDLOCK`; pune restul în așteptare | Nu |
| `inv.usp_Reservation_Release` | Eliberarea (anulare, reducere, manual) și alocarea către cererile în așteptare | Nu |
| `inv.usp_Reservation_Consume` | Din `usp_StockMovement_Post`: scade rezervarea liniei livrate | Nu |
| `inv.usp_Reservation_AllocateWaiting` | Alocarea automată la intrări (3.3) | Nu |
| `inv.usp_Reservation_TakeOver` | Preluarea, cu jurnal în ambele rezervări | Nu |
| `inv.usp_Reservation_ExpireScan` | Jobul orar de expirare și avertizare | Nu |
| `inv.usp_Availability_Get` | Disponibilul pe chei, fără blocare | Da (citire) |
| `inv.usp_Availability_Projected` | Disponibilul proiectat pe zile | Da (citire) |

Coduri de eroare (plaja S5 `54550–54649`, prefix API `INV_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 54551 | `INV_RESERVATION_EXCEEDS_AVAILABLE` | 409 | Rezervare fermă peste disponibil, fără `AllowWaiting` |
| 54552 | `INV_ISSUE_TAKES_RESERVED_STOCK` | 409 | Ieșire care ar lua marfa rezervată pentru altă linie |
| 54553 | `INV_RESERVATION_WAREHOUSE_INVALID` | 422 | Rezervare în tranzit, în custodie sau într-o gestiune inactivă |
| 54554 | `INV_RESERVATION_LOT_UNAVAILABLE` | 409 | Rezervare specifică pe un lot nedisponibil sau neeligibil pentru client |
| 54555 | `INV_RESERVATION_NOT_FOUND` | 404 | Linie sursă fără rezervare (la consum, eliberare, preluare) |
| 54556 | `INV_RESERVATION_TAKEOVER_FORBIDDEN` | 409 | Preluare de la o rezervare protejată (serie facturată în avans) sau fără motiv |
| 54557 | `INV_RESERVATION_STATUS_INVALID` | 409 | Operație nepermisă pentru starea rezervării (de exemplu prelungirea uneia livrate) |
| 54558 | `INV_RESERVATION_SERIAL_QTY` | 422 | Rezervare pe serie cu cantitate diferită de 1 |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterea:

- Abatere de la `SQL-11` pentru `inv.ReservationLog`: fără `RowVersion`, e doar de adăugare. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Availability.View` | Disponibilul și disponibilul proiectat |
| `Inv.Reservation.View` | Rezervările și cererile în așteptare |
| `Inv.Reservation.Manual` | Rezervările manuale |
| `Inv.Reservation.Extend` | Prelungirea unei rezervări |
| `Inv.Reservation.Priority` | Schimbarea priorității |
| `Inv.Reservation.Override` | Preluarea unei rezervări de la altă comandă, cu motiv |
| `Inv.Reservation.AllocateManual` | Alocarea manuală a cererilor în așteptare |

Roluri livrate: **Agent vânzări** (`Availability.View`, `Reservation.View`, `Extend` pe comenzile proprii), **Șef vânzări** (toate, inclusiv `Override` și `Priority`), **Șef depozit** (`View`, `Manual`, `AllocateManual`), **Gestionar** (`Availability.View`, `Reservation.View`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S5 |
| --- | --- |
| Legea 82/1991 | Rezervarea nu e operațiune economică și nu are document justificativ contabil; jurnalul rezervărilor e intern |
| OUG 34/2014 (vânzarea la distanță) | Disponibilul afișat pe e-commerce trebuie să fie real: `DisponibilModificat` și rezervarea la plasarea comenzii |

Integrări: niciuna externă directă; e-commerce și portalul B2B citesc disponibilul prin API sau prin `DisponibilModificat`. Interne: S1 (gestiuni, blocare), S2 (mișcări, consumul rezervării), S4 (loturi disponibile, eligibilitatea pe client), S6 (inventarul), Vânzări (comenzile), Achiziții (comenzile la furnizor pentru proiectat), Producție (ordinele), WMS (picking pe rezervare).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Disponibil pe gestiuni | Pe SKU: în stoc, nedisponibil, rezervat, disponibil, în drum | Vânzări, Șef depozit |
| Rezervări active | Pe gestiune, client, agent: cantitate, vechime, expirare | Șef vânzări |
| Cereri în așteptare | Ce lipsește, pentru cine, de când, ce intrare e așteptată | Vânzări, Achiziții |
| Disponibil proiectat | Pe SKU și zile; zilele cu proiectat negativ | Achiziții, Vânzări |
| Preluări și rezervări expirate | Cine a preluat, de la cine, de ce; rezervările expirate pe agent | Șef vânzări, Director comercial |

## 11. Ecrane principale

Machetele (HTML + PNG) sunt în [ecrane/s5-rezervari/](ecrane/s5-rezervari/), în meniul Stocuri › Stoc.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Disponibil | Un SKU pe toate gestiunile: în stoc, nedisponibil (cu motivul), rezervat, disponibil, în drum | `s5-01-disponibil` |
| Rezervări | Rezervările unei gestiuni și ale unui SKU; fișa rezervării cu cantitățile, expirarea și jurnalul | `s5-02-rezervari` |
| Cereri în așteptare | Cererile neacoperite, cu prioritatea și intrarea care le poate acoperi; alocarea făcută la eliberarea unui lot | `s5-03-cereri-in-asteptare` |
| Disponibil proiectat | Pe zile: intrări, ieșiri, proiectat, cu zilele critice | `s5-04-disponibil-proiectat` |
| Preluarea unei rezervări | Comanda urgentă, rezervarea de la care se preia, motivul, efectul asupra comenzii afectate | `s5-05-preluare-rezervare` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S5-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S5-A1 | Pe orice cheie și în orice moment: disponibil = în stoc − nedisponibil − rezervat, și nu e negativ din rezervări | Test de proprietate |
| S5-A2 | Două rezervări simultane pe ultimele 100 buc: una primește 100, cealaltă 0 rezervat și 100 în așteptare | Test de concurență |
| S5-A3 | O ieșire fără rezervare nu poate lua marfa rezervată pentru altă linie (`54552`); faptul POS e acceptat și marchează rezervarea Neacoperită | Test de integrare (cu S2) |
| S5-A4 | Comanda CV 4433 (600 buc, disponibil 360) dă 360 rezervate și 240 în așteptare; eliberarea lotului `L2611-15` alocă automat cele 240 | Test de integrare (cu S4) |
| S5-A5 | Rezervarea generică pentru un client cu 45 de zile nu ia în calcul loturile cu mai puține zile | Test de integrare (cu S4) |
| S5-A6 | Rezervarea expirată se eliberează în ora următoare și marfa merge la cererea în așteptare cu prioritatea cea mai mare | Test de integrare |
| S5-A7 | Preluarea fără drept sau fără motiv e refuzată; cu drept, rezervarea sursă trece în așteptare și agentul e notificat | Test de integrare |
| S5-A8 | Livrarea pe linia comenzii consumă exact rezervarea liniei, iar loturile se aleg prin FEFO | Test de integrare (cu S4) |
| S5-A9 | Disponibilul proiectat pe 30 de zile pentru un SKU cu 50 de cereri și 10 intrări așteptate se calculează în sub 300 ms | Test de performanță |
| S5-A10 | Rezervările provizorii nu scad disponibilul de azi, dar scad proiectatul | Test unitar |

Criteriile S5-A1, S5-A2 și S5-A3 sunt critice: ele împiedică promiterea aceleiași mărfi la doi clienți.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Ce fel de rezervări | Ferme (blochează cantitatea: comenzi confirmate, transferuri, producție) și provizorii (nu blochează, apar doar în disponibilul proiectat: oferte, comenzi în ciornă, opțional) | 2 (regula 6), 3.2 | Decis (26.11.2026) |
| 2 | Pe ce se rezervă | Implicit generic, pe gestiune + SKU, iar lotul se alege la livrare prin FEFO; specific pe CKU sau serie doar când clientul sau documentul cere un lot sau o bucată anume | 2 (regula 3), 3.2 | Decis (26.11.2026) |
| 3 | Cât ține o rezervare | 14 zile implicit (setare pe firmă), suprascrisă pe client sau pe document; avertizare cu 2 zile înainte, apoi eliberare automată; prelungire cu drept | 3.2 | Decis (26.11.2026) |
| 4 | Ce se întâmplă când nu ajunge stocul | Se rezervă cât există, restul rămâne cerere în așteptare; la orice intrare de marfă, cererile în așteptare se acoperă automat, după prioritate și dată (setabil pe firmă) | 3.3 | Decis (26.11.2026) |
| 5 | Preluarea stocului rezervat pentru altă comandă | Doar cu drept (Șef vânzări) și motiv; comanda afectată trece în așteptare, iar agentul ei e notificat | 3.5 | Decis (26.11.2026) |
| 6 | Rezervarea pe marfă comandată la furnizor | Nu se rezervă ferm; marfa comandată apare în disponibilul proiectat, iar cererile se acoperă când marfa intră | 3.3, 3.4 | Decis (26.11.2026) |
| 7 | Vânzările POS din marfă rezervată | Se acceptă (vânzarea a avut loc deja); rezervarea rămasă fără marfă devine „neacoperită” și agentul e notificat | 2 (regula 2), 3.6 | Decis (26.11.2026) |
| 8 | Rezervările într-o gestiune blocată (predare, inventar) | Rămân valabile; disponibilul gestiunii e 0 până la deblocare, iar livrările așteaptă | 3.6 | Decis (26.11.2026) |

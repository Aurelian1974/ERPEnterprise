# ERP · 02.15 Mijloace fixe și obiecte de inventar · M6 Obiecte de inventar în folosință: dare, transfer, restituire, casare, fișa pe persoană, predarea la plecare

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)** (lucru autonom cerut de Aurelian; deciziile 1–10 adoptate pe recomandare). A șasea din cele 7 părți ale modulului 02.15. Fișa obiectelor de inventar e în această parte, la cererea lui Aurelian (27.11.2026).

Schema `fa`, prefix `Erp.FixedAssets`, erorile M6 în plaja `65750–65849`. Această parte acoperă **evidența obiectelor de inventar după ce au ieșit din magazie**: darea în folosință (generată din bonul de consum al Stocurilor), evidența pe persoană și pe locație ținută extrabilanțier în `8039`, transferul între persoane, restituirea, casarea și lipsa, **fișa obiectelor de inventar pe persoană**, **lista de predare la plecarea angajatului** (care adună mijloacele fixe și obiectele de inventar ale persoanei) și evidența bunurilor terților folosite de firmă (luate cu chirie sau în leasing operațional). Documente părinte: [M1](M1-CONFIGURARE.md) §3.2 (tranzacțiile `OBF`, `OBT`, `OBR`, `OBC`), §3.4 (pragul), §3.11 (setările `InUseTracking`, `AutoInUseFromStockIssue`, `InUseIndividualNumbers`); [Stocuri S2](../05-stocuri/README.md) (bonul de consum); [M4](M4-OPERATII.md) (locul mijloacelor fixe); [M5](M5-IESIRI.md) (imputarea). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- Produs ERP on-premise, SQL Server case sensitive (`SQL-21`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite.
- **Obiectul de inventar** e un bun cu valoarea de intrare sub prag (5.000 lei din 27.11.2026, `fa.AssetThreshold`, M1 §3.4) sau cu durată de folosință sub un an, cumpărat ca articol de stoc (`core.Item` cu `ItemType` = obiect de inventar). Cât stă în magazie e **stoc** (`303`, Stocuri). La darea în folosință, **bonul de consum S2** îl scoate din stoc pe cheltuială (`603` = `303`), iar M6 deschide **evidența de folosință**: cine îl are, unde, de când, cu ce valoare, ținută extrabilanțier în debitul `8039` „Alte valori în afara bilanțului” (analitic „Obiecte de inventar în folosință”).
- **Obiectele în folosință nu mai sunt stoc** (decizia confirmată pe 27.11.2026): nu apar în stocuri, nu se rezervă, nu se vând din stoc. Restituirea nu le readuce în stoc (ar trebui reevaluate și recontabilizate), ci le mută în evidența de folosință pe o **locație de depozitare** fără persoană („Magazie obiecte folosite”), de unde pot fi date din nou.
- Evidența e **pe articol, persoană, locație și cantitate** (implicit) sau **pe bucată**, cu număr individual (setarea `InUseIndividualNumbers` sau grupele cu `TrackSerialNumber`, M1 §3.8). Valoarea pe unitate e costul de ieșire din bonul de consum (CMP sau FIFO din S2).
- **Ce NU face M6:** nu face ieșirea din stoc și nota `603` = `303` (S2); nu calculează reținerea pe salariu (02.11, viitor); nu ține mijloace fixe (M2–M5), dar le citește pentru fișa pe persoană și pentru predarea la plecare.

## 1. Scop și perimetru

| Zonă | Ce face M6 | Ce NU face (și cine face) |
| --- | --- | --- |
| Dare în folosință | Din bonul de consum S2 (automat sau din lista „De dat în folosință”), din trecerea unui mijloc fix în OB (M5) sau la preluarea inițială | Ieșirea din stoc (S2) |
| Evidență | Pe persoană, locație, centru de cost; cantitate, valoare, data dării; număr individual opțional | Contabilizarea cheltuielii (S2 → C2) |
| Mișcări | Transfer între persoane și locații, restituire în depozitarea de obiecte folosite, redare în folosință | Mutarea mijloacelor fixe (M4) |
| Ieșiri | Casare cu comisie și materiale recuperate; lipsă (imputată sau nu) | Intrarea deșeurilor în stoc (S2, prin port) |
| Fișe | Fișa obiectelor de inventar pe persoană și pe locație, tipăribilă, cu semnătura de primire | — |
| Plecarea angajatului | Lista de predare: mijloace fixe (M4) și obiecte de inventar (M6), rezolvarea fiecărei poziții, imputările, documentul pentru lichidare | Lichidarea salarială (02.11) |
| Bunuri ale terților | Evidența bunurilor luate cu chirie, în leasing operațional, în custodie sau comodat (`8031`, `8033`), pe persoană și locație, pentru inventariere | Contractul și plățile (A, F) |

## 2. Model de domeniu

```
 fa.InUseRecord (poziția de evidență: articol × persoană × locație × lot de dare, cantitate curentă, valoare unitară)
   ├─ creată de fa.InUseIssue (OBF) ← bonul de consum S2 (prin IInUseIssuePort) · IMF-OB (M5) · OBF-INI
   ├─ mișcată de fa.InUseTransfer (OBT) · fa.InUseReturn (OBR)
   └─ închisă de fa.InUseWriteOff (OBC: casare, lipsă)
 fa.InUseMovement (jurnalul: fiecare document scrie rânduri cu cantitate și valoare, + / −)
 fa.EmployeeHandover (PLP, predarea la plecare) ── fa.EmployeeHandoverItem × N (mijloc fix sau poziție OB → rezolvare)
 fa.ThirdPartyAsset (bun al terților în folosința firmei: contract, proprietar, valoare declarată, loc, persoană)
```

Reguli de business:

1. **Poziția de evidență** (`fa.InUseRecord`) e unitatea urmărită: un articol dat unei persoane (sau unei locații fără persoană), dintr-un document de dare, cu cantitatea curentă și valoarea unitară de la dare. Pe bucată, cantitatea e 1 și poziția are numărul individual (`OB 000731`).
2. **Jurnalul e sursa** (`fa.InUseMovement`): cantitatea și valoarea curentă se calculează din mișcări (vedere indexată, ca la mijloacele fixe). Soldul extrabilanțier `8039` = suma valorilor curente (concordanța `IN_USE` din C3).
3. **Contarea extrabilanțieră doar la intrarea și la ieșirea din evidență**: darea în folosință publică `ObiectInventarDatInFolosinta` (debit `8039`), casarea și lipsa publică `ObiectInventarScosDinFolosinta` (credit `8039`). Transferul și restituirea nu schimbă soldul (doar analiticul pe persoană, dacă firma îl folosește, setarea `InUsePersonAnalytics`).
4. **Un obiect în folosință are mereu un loc**: persoană + locație, sau doar locație (sală de ședințe, depozitarea de obiecte folosite). Persoana trebuie să fie angajat activ sau colaborator (`65752`).
5. **Obiectele unui angajat care pleacă se rezolvă toate** înainte de închiderea listei de predare (`65760`): restituite, transferate succesorului, lăsate pe locație, imputate sau casate.

## 3. Entități și documente

### 3.1 Darea în folosință (`fa.InUseIssue`, `OBF`)

**Din bonul de consum** (`OBF-BC`): liniile bonului S2 cu articole de tip obiect de inventar au, pe lângă gestiunea de ieșire, **persoana primitoare** sau **locația** (câmpuri noi pe linia bonului, precizare S2). La validarea bonului, S2 apelează `IInUseIssuePort.IssueFromStockAsync` în aceeași tranzacție SQL (cu `AutoInUseFromStockIssue = 1`) și M6 creează `OBF` validat cu câte o poziție pe linie (sau pe bucată, cu număr individual). Fără setare, liniile ajung în lista **„De dat în folosință”**, de unde administratorul de patrimoniu alege persoana și validează.

**Preluarea inițială** (`OBF-INI`): import CSV la implementare (articol, persoană, locație, cantitate, valoare unitară, data dării), cu verificarea totalului față de soldul `8039` din balanța de deschidere.

**Din mijloc fix** (`IMF-OB`, M5): poziție cu valoarea de intrare a fișei, persoana și locul de pe fișă.

### 3.2 Transferul și restituirea (`OBT`, `OBR`)

- **Transfer** (`OBT-MAN`): de la o persoană sau locație la alta, pe poziții și cantități (o poziție se poate împărți). Predătorul și primitorul pot confirma în aplicație (ca la bonul de mișcare M4).
- **Restituire** (`OBR-MAN`): poziția se mută la o locație de depozitare marcată `IsInUseStorage` (de exemplu „Magazie obiecte folosite Cluj”), fără persoană, cu starea obiectului (bun, uzat, defect). De acolo se redă în folosință printr-un transfer sau se casează.

### 3.3 Casarea și lipsa (`fa.InUseWriteOff`, `OBC`)

- **Casare** (`OBC-MAN`): poziții și cantități, comisia (ca la M5), motivul, procesul-verbal (formularul de scoatere din folosință, OMFP 2634); materialele recuperate intră în stoc prin același port S2 ca la mijloacele fixe (`NIR-REC`, `3028` = `7588`). Credit `8039`.
- **Lipsă** (`OBC-LIP`, tranzacție nouă, generată din inventarierea M7 sau din predarea la plecare): imputată (`4282` = `7581` + `4427`, cu evenimentul `ImputareInregistrata`, ca la M5) sau neimputabilă (semnal pentru ajustarea TVA dedus către C4). Credit `8039`. Nu există cheltuială nouă: costul a fost deja recunoscut la dare.

### 3.4 Fișa obiectelor de inventar pe persoană

Raport tipăribil (QuestPDF) pentru o persoană sau o locație, la o dată: pozițiile în folosință (articol, număr individual, cantitate, valoare unitară, data dării, documentul), mișcările dintr-o perioadă și, opțional, mijloacele fixe în responsabilitatea persoanei (din M4). Are rânduri de semnătură (predat, primit). Poate fi trimisă persoanei pentru confirmare în aplicație (o confirmare pe an, folosită la inventariere, M7).

### 3.5 Predarea la plecare (`fa.EmployeeHandover`, `PLP`)

Deschisă automat când HR (02.11, viitor) înregistrează data de încetare, sau manual de administratorul de patrimoniu. Conține toate pozițiile persoanei: **mijloacele fixe** în responsabilitatea ei (`IAssetPlacementQuery.GetByEmployeeAsync`, M4) și **obiectele de inventar** în folosință. Pentru fiecare poziție se alege rezolvarea:

| Rezolvare | Document generat |
| --- | --- |
| Predat succesorului | `BMF` (mijloc fix, M4) sau `OBT` (obiect de inventar) |
| Restituit | `BMF` către locația de depozitare a bunurilor / `OBR` |
| Rămâne pe locație | `BMF` / `OBT` fără persoană |
| Lipsă imputată | `IMF-LIP` (M5) / `OBC-LIP`, cu suma de reținut |
| Casat (uzat) | `IMF-CAS` / `OBC-MAN`, cu comisia |

La închidere, documentul are toate pozițiile rezolvate și documentele validate (`65760`), totalul imputat și semnăturile. Publică `PredareLaPlecareInchisa` v1 cu suma de reținut, folosită de lichidarea salarială (02.11) sau de o factură de imputare (V5, pentru colaboratori).

### 3.6 Bunurile terților (`fa.ThirdPartyAsset`)

Evidență fără valoare în bilanț, pentru bunuri care nu sunt ale firmei dar stau la ea: luate cu chirie sau în leasing operațional (`8031`), primite în custodie sau comodat (`8033`), echipamente de la furnizori (de exemplu automat de cafea, frigider de la distribuitor). Câmpuri: proprietarul (partener), contractul (număr, perioadă, link spre contractul de achiziții A1 dacă există), valoarea declarată, contul extrabilanțier, locul și persoana, numărul de inventar opțional (seria `InUseRecordNumber`). Intrarea și ieșirea publică evenimente extrabilanțiere; apar la inventariere (M7) în liste separate și în predarea la plecare.

## 4. Fluxuri principale

### 4.1 Monitoare date în folosință din bonul de consum

`BC 7712` din 24.11.2026 (S2, Magazie Cluj): 3 monitoare Dell P2425, 1.240,00 lei fiecare (sub pragul de 5.000 lei, articol de tip obiect de inventar), date lui Carmen Nistor, Ion Popescu și Sorina Munteanu (câte unul, persoana pe linia bonului). La validarea bonului: S2 contează `603` = `303` 3.720,00; M6 creează `OBF 000214` (`OBF-BC`) cu 3 poziții și publică `ObiectInventarDatInFolosinta`: debit `8039` 3.720,00. Fiecare persoană vede monitorul în „Bunurile mele” și confirmă primirea.

### 4.2 Fișa lui Mihnea Pavel

Mihnea Pavel (specialist vânzări, Cluj) are la 31.12.2026: monitorul Dell P2425 (1.240,00), telefonul Samsung Galaxy A55 `OB 000731` (1.890,00), scaunul ergonomic (1.450,00), căștile Jabra Evolve2 (320,00), geanta de laptop (180,00) și, ca mijloc fix, videoproiectorul MF 000402 din sala de ședințe. Total obiecte de inventar: 5.080,00 lei în `8039`.

### 4.3 Predarea la plecare

Mihnea Pavel pleacă pe 31.12.2026. `PLP 000009` se deschide pe 15.12 din data de încetare: 5 obiecte de inventar și 1 mijloc fix.

| Poziție | Rezolvare | Document |
| --- | --- | --- |
| Monitor Dell P2425 | Predat succesoarei, Ana Dobre | `OBT 000088` |
| Telefon Samsung A55 `OB 000731` | Restituit în „Magazie obiecte folosite Cluj”, stare bună | `OBR 000041` |
| Scaun ergonomic | Rămâne pe locație (Birou Cluj, etaj 2) | `OBT 000089` |
| Căști Jabra Evolve2 | Lipsă, imputată 320,00 + TVA 67,20 | `OBC 000019` (`OBC-LIP`) |
| Geanta de laptop | Casată (uzată) | `OBC 000020` |
| Videoproiector MF 000402 | Lipsă la inventar, imputat 2.100,00 + TVA 441,00 | `IMF 000034` (M5) |

La închidere: total de reținut 2.928,20 lei (2.420,00 + TVA 508,20), evenimentul `PredareLaPlecareInchisa` și fișa de predare tipărită, semnată de Mihnea Pavel și de Paul Constantin.

### 4.4 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Bon de consum cu obiecte de inventar fără persoană și fără locație | Validarea bonului în S2 e refuzată dacă `AutoInUseFromStockIssue = 1` (`65751`, ridicată prin port); altfel liniile ajung în „De dat în folosință” |
| Stornarea bonului de consum | Stornează darea în folosință prin port, dacă pozițiile nu au fost mișcate între timp (`65780`) |
| Obiect restituit, redat altei persoane | Transfer din locația de depozitare; valoarea și data inițială a dării rămân |
| Angajat cu poziții nerezolvate la data plecării | Lista rămâne deschisă și apare în tabloul de bord al administratorului; HR vede alerta |
| Articol trecut peste prag după majorarea prețului | Rămâne obiect de inventar (încadrarea se face la intrare, după pragul de atunci, M1 §3.4) |
| Obiect cu număr individual mutat parțial | Nu există: pe bucată cantitatea e 1 |

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `ObiectInventarDatInFolosinta` v1 | Publicat | Pozițiile, persoanele, valorile; `Accounting` extrabilanțier (`InventoryObjectInUse`, debit) | C2 (nota extrabilanțieră), C3 (`IN_USE`) |
| `ObiectInventarScosDinFolosinta` v1 | Publicat | Casare sau lipsă; `Accounting` (credit `8039`; la lipsă imputată și `4282` / `7581` / `4427`) | C2, C4 (TVA), C3 |
| `ObiectInventarMutat` v1 | Publicat | Transfer sau restituire | M7, 02.11 |
| `PredareLaPlecareInchisa` v1 | Publicat | Persoana, pozițiile, suma de reținut | 02.11 HR (viitor), F (creanța) |
| `AngajatIncetare` v1 | Consumat (viitor, de la 02.11) | Persoana, data încetării | Deschide `PLP` |
| `BonConsumValidat` (linii OB) | Consumat, sincron prin port | — | Creează `OBF-BC` |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M6)

```csharp
public interface IInUseIssuePort
{
    /// Apelat de Stocuri (S2) în tranzacția SQL de validare a bonului de consum; aruncă BusinessRuleException 65751 dacă o linie OB nu are persoană sau locație.
    Task<long?> IssueFromStockAsync(InUseFromStockRequest request, IDbTransaction tx, CancellationToken ct);
    Task ReverseAsync(long stockIssueId, IDbTransaction tx, CancellationToken ct);
}
public sealed record InUseFromStockRequest(int CompanyId, long StockIssueId, DateOnly IssueDate, IReadOnlyList<InUseFromStockLine> Lines);
public sealed record InUseFromStockLine(long StockIssueLineId, int ItemId, decimal Quantity, decimal UnitCost,
    int? EmployeeId, int? LocationId, int? CostCenterId);

public interface IInUseQuery
{
    /// Folosit de 02.11 HR (fișa angajatului), de portalul „Bunurile mele” și de M7.
    Task<IReadOnlyList<InUsePositionDto>> GetByEmployeeAsync(int employeeId, DateOnly date, CancellationToken ct);
}
public sealed record InUsePositionDto(long InUseRecordId, string? IndividualNumber, int ItemId, string ItemName,
    decimal Quantity, decimal UnitValue, int? LocationId, DateOnly IssuedOn);
```

## 7. Schema SQL `fa` (partea M6)

| Tabel | Cheie primară | Chei unice / străine | Indecși |
| --- | --- | --- | --- |
| `fa.InUseRecord` | `Id bigint` | FK `CompanyId`, `ItemId`, `EmployeeId NULL`, `LocationId`, `IssueId`; UX `(CompanyId, IndividualNumber) WHERE IndividualNumber IS NOT NULL`; `CHECK (EmployeeId IS NOT NULL OR LocationId IS NOT NULL)` | `(EmployeeId) INCLUDE (ItemId, LocationId)`, `(LocationId)` |
| `fa.InUseMovement` | `Id bigint` | FK `InUseRecordId`; `DocumentType` + `DocumentId`; `QuantityDelta`, `ValueDelta` | `(InUseRecordId, MovementDate)`; vedere indexată `fa.vw_InUseBalance` |
| `fa.InUseIssue` · `fa.InUseTransfer` · `fa.InUseReturn` · `fa.InUseWriteOff` (+ linii) | `Id bigint` | UX `(CompanyId, NumberKey)`; FK `TransactionTypeId`; `SourceStockIssueId NULL` | `(CompanyId, DocumentDate)` |
| `fa.EmployeeHandover` · `fa.EmployeeHandoverItem` | `Id bigint` | UX `(EmployeeId) WHERE Status IN (1,2)`; pe item FK `AssetId NULL` sau `InUseRecordId NULL` (`CHECK` exact unul), `Resolution`, `ResolutionDocumentId` | — |
| `fa.ThirdPartyAsset` | `Id bigint` | FK `OwnerPartnerId`, `LocationId`, `EmployeeId NULL`; `OffBalanceAccountCode` (`8031` / `8033`) | — |

**Erorile M6** (`65750–65849`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 65751 | `FA_INUSE_RECEIVER_REQUIRED` | 400 | Linie OB fără persoană și fără locație |
| 65752 | `FA_INUSE_PERSON_INACTIVE` | 409 | Persoana nu e angajat activ sau colaborator |
| 65753 | `FA_INUSE_ITEM_NOT_OB` | 400 | Articolul nu e de tip obiect de inventar |
| 65754 | `FA_INUSE_QTY_EXCEEDS` | 409 | Cantitatea transferată, restituită sau casată depășește cantitatea poziției |
| 65760 | `FA_HANDOVER_OPEN_ITEMS` | 409 | Închiderea predării cu poziții nerezolvate sau documente nevalidate |
| 65761 | `FA_HANDOVER_EXISTS` | 409 | Persoana are deja o predare deschisă |
| 65770 | `FA_INUSE_COMMITTEE_REQUIRED` | 400 | Casare fără comisie |
| 65780 | `FA_INUSE_REVERSE_BLOCKED` | 409 | Stornarea dării după mișcări ulterioare ale pozițiilor |
| 65790 | `FA_INUSE_OPENING_MISMATCH` | 400 | Preluarea inițială nu se potrivește cu soldul `8039` din balanța de deschidere (avertisment blocant, confirmabil cu motiv) |

## 8. Permisiuni și roluri

| Permisiune | Contabil mijloace fixe | Administrator patrimoniu | Responsabil de loc | Angajat (oricine) |
| --- | --- | --- | --- | --- |
| `FixedAssets.InUse.View` | ✓ | ✓ | ✓ (locațiile lui) | doar „Bunurile mele” |
| `FixedAssets.InUse.Issue` / `.Transfer` / `.Return` | ✓ | ✓ | ✓ (transfer în locațiile lui) | — |
| `FixedAssets.InUse.WriteOff` | ✓ | ✓ (ciornă) | — | — |
| `FixedAssets.InUse.ConfirmReceipt` | — | — | ✓ | ✓ (ale lui) |
| `FixedAssets.Handover.Manage` | ✓ | ✓ | — | — |
| `FixedAssets.ThirdParty.Manage` | ✓ | ✓ | — | — |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M6 |
| --- | --- |
| OMFP 1802 | Obiectele de inventar pe `303`, trecute pe cheltuială la darea în folosință, cu evidența în folosință în conturile în afara bilanțului (`8039`); bunurile luate cu chirie în `8031`, cele primite în custodie în `8033` |
| Legea 82/1991, OMFP 2861/2009 (inventarierea) | Obiectele în folosință și bunurile terților se inventariază pe locuri și persoane (M7) |
| OMFP 2634/2015 | Bonul de consum (S2), fișa de evidență a obiectelor de inventar în folosință, procesul-verbal de scoatere din folosință |
| Codul muncii (răspunderea patrimonială) | Imputarea pe baza deciziei, cu acordul sau hotărârea necesară pentru reținere (02.11) |

**Precizări aplicate (27.11.2026):** S2 (linia bonului de consum cu `EmployeeId` / `LocationId` primitor pentru articolele de tip obiect de inventar; apelul `IInUseIssuePort` în tranzacția de validare; contul `8039` nu mai e trecut de modelul de contare al bonului, ci de evenimentul M6); Nucleu (locația cu `IsInUseStorage`; `ItemType` = obiect de inventar); C2 (cheile `InventoryObjectInUse`, `ThirdPartyAssetsRented` (`8031`), `ThirdPartyAssetsCustody` (`8033`)); C3 (concordanța `IN_USE`: soldul `8039` = evidența de folosință).

## 10. Rapoarte

| Raport | Conținut |
| --- | --- |
| Fișa obiectelor de inventar (pe persoană, pe locație) | Pozițiile la o dată și mișcările, cu semnături; formularul tipizat |
| Obiecte în folosință | Pe articol, grupă, locație, centru de cost, vechime a dării |
| Bunuri pe angajat | Mijloace fixe + obiecte de inventar + bunuri ale terților, pentru HR |
| Predări la plecare | Deschise, închise, sume imputate |
| Bunuri ale terților | Pe proprietar, contract, expirare |

## 11. Ecrane principale

Machetele sunt în [ecrane/m6-obiecte-folosinta/](ecrane/m6-obiecte-folosinta/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Obiecte în folosință | Lista pozițiilor pe persoană și locație, cu filtrele și totalul `8039` | `m6-01-obiecte-in-folosinta` |
| Dare în folosință | Formularul `OBF 000214` generat din `BC 7712`: monitoarele pe persoane, confirmările | `m6-02-dare-in-folosinta` |
| Fișa pe persoană | Fișa lui Mihnea Pavel: obiectele de inventar și mijloacele fixe, tipăribilă | `m6-03-fisa-persoana` |
| Predare la plecare | `PLP 000009`: pozițiile lui Mihnea Pavel, rezolvările, suma de reținut | `m6-04-predare-la-plecare` |
| Bunurile terților | Bunurile luate cu chirie și în custodie, cu contractele | `m6-05-bunuri-terti` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| M6-A1 | Validarea `BC 7712` creează `OBF 000214` cu 3 poziții și debit `8039` 3.720,00, în aceeași tranzacție | Test de integrare (cu S2) |
| M6-A2 | Un bon cu o linie OB fără persoană și fără locație e refuzat (`65751`) cu setarea automată | Test de integrare |
| M6-A3 | Transferul și restituirea nu schimbă soldul `8039`; casarea îl scade cu valoarea pozițiilor | Test de integrare |
| M6-A4 | `PLP 000009` nu se închide până nu sunt validate toate cele 6 documente (`65760`); la închidere suma de reținut e 2.928,20 | Test de integrare (cu M4, M5) |
| M6-A5 | Concordanța `IN_USE` e 0 după 10.000 de mișcări aleatoare | Test de proprietăți |
| M6-A6 | Stornarea bonului de consum după un transfer al poziției e refuzată (`65780`) | Test de integrare |
| M6-A7 | Fișa pe persoană tipărită la o dată arată exact pozițiile de la acea dată (inclusiv cele restituite ulterior) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Obiectele în folosință sunt stoc? | Nu; sunt evidență extrabilanțieră `8039` după bonul de consum | ✓ Aprobat (confirmat de Aurelian, 27.11.2026) |
| 2 | Persoana la darea în folosință | Pe linia bonului de consum S2, cu darea generată automat (setare) | ✓ Aprobat autonom (27.11.2026) |
| 3 | Evidența pe bucată sau pe cantitate | Pe cantitate implicit, pe bucată cu număr individual opțional | ✓ Aprobat autonom |
| 4 | Restituirea | În evidență, la o locație de depozitare a obiectelor folosite, nu înapoi în stoc | ✓ Aprobat autonom |
| 5 | Contarea `8039` | Doar la dare (debit) și la casare sau lipsă (credit); transferurile nu schimbă soldul | ✓ Aprobat autonom |
| 6 | Lipsa obiectelor de inventar | Imputată cu TVA sau neimputabilă, fără cheltuială nouă | ✓ Aprobat autonom |
| 7 | Predarea la plecare | Un document care adună mijloacele fixe și obiectele de inventar ale persoanei, cu rezolvare obligatorie pe fiecare poziție | ✓ Aprobat autonom |
| 8 | Confirmarea primirii | În aplicație, de către persoană („Bunurile mele”), o dată la dare și anual la inventariere | ✓ Aprobat autonom |
| 9 | Bunurile terților | Evidență în M6 (`8031`, `8033`), inventariate separat | ✓ Aprobat autonom |
| 10 | Fișa OB | În M6, pe persoană și pe locație, cu mijloacele fixe ale persoanei opțional | ✓ Aprobat autonom |

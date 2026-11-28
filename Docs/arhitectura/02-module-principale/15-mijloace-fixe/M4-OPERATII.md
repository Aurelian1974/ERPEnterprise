# ERP · 02.15 Mijloace fixe și obiecte de inventar · M4 Operații: mișcări, modernizări, deprecieri, reevaluări, conservare, compunere și descompunere

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)** (lucru autonom cerut de Aurelian; deciziile 1–12 adoptate pe recomandare). A patra din cele 7 părți ale modulului 02.15. Compunerea și descompunerea au fost adăugate la M4 la cererea lui Aurelian (27.11.2026).

Schema `fa`, prefix `Erp.FixedAssets`, erorile M4 în plaja `65400–65599`. Această parte acoperă **tot ce se întâmplă cu un bun între punerea în funcțiune și ieșire**: bonul de mișcare (loc, responsabil, centru de cost), modernizarea (din factură, din investiție sau manuală), deprecierea și reluarea ei, reevaluarea (ocazională și în masă), schimbarea duratei sau a estimării, reclasificarea între grupe, conservarea și repunerea în funcțiune, **compunerea** (mai multe fișe devin una) și **descompunerea** (o fișă devine mai multe). Documente părinte: [M1](M1-CONFIGURARE.md) §3.2 (tranzacțiile `BMF`, `MVM`, `CNS`, `CDM`), §3.5 (grupele), §3.9 (modelele de contare), §3.11 (setările de conservare și reevaluare); [M2](M2-REGISTRU-INTRARI.md) (fișa, regimurile, jurnalul valorilor, componentele); [M3](M3-AMORTIZARE.md) (planul, recalcularea prospectivă). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- Produs ERP on-premise, SQL Server case sensitive (`SQL-21`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite.
- Fișa (`fa.Asset`) nu se editează pentru nimic din ce are efect în valoare, loc sau amortizare. Toate schimbările se fac prin **documente** cu tranzacție (Nucleu §3.6), care se validează, scriu în jurnalul valorilor (`fa.AssetLedger`, M2 §2 regula 2) sau în istoricul locului (`fa.AssetPlacement`) și publică evenimente cu blocul `Accounting` (C2). Fiecare document poate avea **mai multe fișe** pe linii.
- După orice operație de valoare sau durată, planul de amortizare se recalculează **prospectiv** (M3 §2 regula 4): valoarea rămasă se împarte pe lunile rămase, lunile trecute nu se schimbă. Recalcularea se face în aceeași tranzacție cu validarea documentului.
- **Data operației** trebuie să fie după ultima lună cu amortizare validată pentru fișă (`65402`): o operație cu efect din octombrie nu se mai poate valida după ce octombrie are `AML` validat; se înregistrează cu data primei luni deschise.
- Regimurile: operațiile se aplică implicit pe `BOOK` și `TAX`; reevaluarea se aplică pe `TAX` doar dacă grupa are `RevaluationAffectsTax = 1` (clădiri, terenuri, M1 §3.5); deprecierea e doar contabilă (`BOOK`, și `IFRS` dacă e activ).

## 1. Scop și perimetru

| Zonă | Ce face M4 | Ce NU face (și cine face) |
| --- | --- | --- |
| Mișcări | Bonul de mișcare pentru unul sau mai multe bunuri: locație, gestiune, responsabil, centru de cost, departament; predare-primire semnată | Mutarea obiectelor de inventar în folosință (M6); transferul în altă firmă a grupului (M5, `IMF-GRP`) |
| Modernizări | Creșterea valorii din factură (coada „Imobilizări de preluat”), din investiția în curs sau din producție proprie; componentă nouă sau valoare adăugată părintelui | Reparațiile (cheltuială, A5) |
| Depreciere | Ajustarea pentru depreciere (`29xx`) și reluarea ei, cu raport de evaluare | Testul de depreciere ca atare (făcut de evaluator) |
| Reevaluare | Ocazională pe o fișă și în masă pe filtre, cu import din raportul evaluatorului; metoda netă sau brută (M1 §3.11) | Transferul lunar al surplusului (M3) |
| Estimări | Schimbarea duratei, a metodei contabile, a valorii reziduale sau a totalului de unități | Metoda fiscală (blocată după prima amortizare, M1 `65005`) |
| Reclasificare | Mutarea în altă grupă (alt cont `21x` / `28xx`), cu notă între conturi | Trecerea în obiecte de inventar (M5, `IMF-OB`) |
| Conservare | Punerea în conservare și repunerea în funcțiune, cu suspendarea amortizării pe regimurile din setare | — |
| Restructurare | Compunerea și descompunerea fișelor, cu proces-verbal | Componentele la intrare (M2 §3.6) |

## 2. Model de domeniu

```
 fa.AssetTransfer (BMF) ──── fa.AssetTransferLine × N        (fișă: loc vechi → loc nou)            → fa.AssetPlacement (rând nou)
 fa.AssetValueChange (MVM) ─ fa.AssetValueChangeLine × N     (fișă × regim: delta brut, amortizare, depreciere, rezervă, durată)
                                                                → fa.AssetLedger + plan nou (M3)
 fa.AssetConservation (CNS) ─ fa.AssetConservationLine × N   (fișă: de la / până la, regimuri suspendate) → fa.AssetBook.IsSuspended
 fa.AssetRestructure (CDM) ─┬ fa.AssetRestructureSource × N  (fișe care se închid sau se micșorează)
                            └ fa.AssetRestructureTarget × N  (fișe care se creează sau cresc, cu procent sau valoare)
```

Reguli de business:

1. **O operație = un document cu tranzacție.** Fiecare fel de operație are tipul lui de document (`BMF`, `MVM`, `CNS`, `CDM`) și tranzacțiile din M1 §3.2; documentul are antet, linii, stare (1 Ciornă · 3 Validat · 8 Stornat · 9 Anulat), atașamente și audit.
2. **Valorile rămân în jurnal.** Fiecare linie validată scrie un rând în `fa.AssetLedger` pe fiecare regim afectat, cu tipul mișcării (`IMPROVEMENT`, `IMPAIRMENT`, `IMPAIRMENT_REVERSAL`, `REVALUATION`, `RECLASS_OUT` / `RECLASS_IN`, `MERGE_OUT` / `MERGE_IN`, `SPLIT_OUT` / `SPLIT_IN`). Schimbările de durată, metodă și conservare nu au sumă, dar creează o versiune nouă a planului.
3. **Stornarea se face doar pe ultima operație.** Un document validat se stornează dacă nu există, pe aceleași fișe, o operație sau o amortizare validată ulterioară (`65490`). Altfel se face o operație inversă.
4. **Locul are istoric, fără suprapuneri.** Bonul de mișcare închide rândul curent din `fa.AssetPlacement` la data bonului și deschide unul nou. Amortizarea lunii merge pe centrul de cost valabil la sfârșitul lunii (M3 decizia 4).
5. **Compunerea și descompunerea nu schimbă totalurile.** Suma valorilor brute, a amortizării, a deprecierii și a rezervei pe fiecare regim e aceeași înainte și după (`65473`, `65482`). Nota contabilă apare doar dacă grupele sursă și țintă au conturi diferite.
6. **Operațiile cer documentul justificativ.** Deprecierea, reevaluarea și schimbarea duratei cer un atașament (raport de evaluare, decizie, notă de fundamentare) când setarea `RequireJustification` a tranzacției e activă (implicit da, `65430`).

## 3. Entități și documente

### 3.1 Bonul de mișcare (`fa.AssetTransfer`, `BMF`)

**Antet**: tranzacția `BMF-MAN`, numărul, data, motivul (nomenclatorul `TransferReason`: reorganizare, angajat nou, plecare, reparație, alt sediu), predătorul și primitorul (implicit responsabilii), starea, semnăturile (PDF semnat atașat sau confirmarea primitorului în aplicație).

**Linia**: fișa, locul vechi (citit, nu editat), locul nou: `LocationId`, `ResponsibleEmployeeId`, `CostCenterId`, `DepartmentId`, `CustodyId` (gestiunea de mijloace fixe, opțional). Selecția în masă: toate fișele unui responsabil, ale unei locații sau dintr-un filtru salvat (folosită la plecarea unui angajat, împreună cu M6).

**Validarea**: scrie `fa.AssetPlacement`; publică `MijlocFixMutat` v1. Fără notă contabilă implicit. Cu setarea `PlacementAccountAnalytics = 1` (analitice pe gestiuni sau puncte de lucru), publică și `Accounting` cu mutarea valorii brute și a amortizării între analitice (`21x.CJ` = `21x.TM`, `28xx.TM` = `28xx.CJ`).

**Confirmarea primitorului**: dacă tranzacția cere confirmare (`RequireReceiverConfirmation`), bonul validat rămâne „în așteptarea primirii” până confirmă primitorul (în aplicație sau prin scanarea etichetei); locul se schimbă la data bonului, dar raportul „bunuri neconfirmate” îl arată administratorului de patrimoniu.

### 3.2 Documentul de modificare a valorii (`fa.AssetValueChange`, `MVM`)

Un singur tip de document pentru toate operațiile de valoare și estimare, cu felul dat de tranzacție (M1 §3.2). Antetul: tranzacția, numărul, data, data efectului (prima zi a lunii de la care se aplică planul nou, implicit luna următoare datei), motivul, atașamentele. Linia: fișa, regimul (sau „toate”), valorile vechi (citite din jurnal), valorile noi sau delta, durata nouă.

| Tranzacție | Ce face pe linie | Jurnal | Nota (C2, M1 §3.9) |
| --- | --- | --- | --- |
| `MVM-MOD` (din factură) | Adaugă valoarea liniei de factură la fișă sau creează o **componentă nouă** (dacă grupa are `ComponentPolicy = 1` și utilizatorul alege „componentă”) | `IMPROVEMENT` pe `BOOK` și `TAX` | `21x` = `231.PRL` |
| `MVM-MODM` (manuală, din investiție) | Ca mai sus, cu sursa investiția în curs sau producția proprie | `IMPROVEMENT` | `21x` = `231` sau `21x` = `722` |
| `MVM-DEP` (depreciere) | Valoarea recuperabilă introdusă; deprecierea = valoarea netă − valoarea recuperabilă | `IMPAIRMENT` pe `BOOK` (și `IFRS`) | `6813` = `29xx` |
| `MVM-DEPR` (reluare) | Generată din deprecierea validată; cel mult deprecierea existentă (`65421`) | `IMPAIRMENT_REVERSAL` | `29xx` = `7813` |
| `MVM-REV` / `MVM-REVM` (reevaluare) | Valoarea justă introdusă sau importată; diferența față de valoarea netă | `REVALUATION` pe `BOOK` (și `TAX` dacă `RevaluationAffectsTax`) | Creștere: `28xx` = `21x` (metoda netă), `21x` = `105` (sau `7813` până la descreșterea anterioară recunoscută ca cheltuială); descreștere: `105` = `21x` până la rezerva existentă, apoi `6813` = `21x` |
| `MVM-DNU` (estimare) | Durata totală nouă, metoda contabilă nouă, valoarea reziduală nouă sau totalul de unități nou | Fără sumă; plan nou | Fără notă |
| `MVM-RCL` (reclasificare) | Grupa nouă (și clasa, dacă se schimbă); verifică conturile | `RECLASS_OUT` / `RECLASS_IN` | `21x` nou = `21x` vechi; `28xx` vechi = `28xx` nou; `29xx` la fel |

**Modernizarea**, reguli:

- Valoarea se adaugă la valoarea brută pe ambele regimuri. Pe `BOOK`, planul nou împarte valoarea rămasă pe durata rămasă sau pe o durată nouă (câmp opțional pe linie, „modernizarea prelungește durata”). Pe `TAX`, conform Codului fiscal: valoarea modernizării se amortizează pe durata rămasă sau pe o durată nouă în limita clasei (art. 28).
- **Componentă nouă** (doar pe `BOOK`, abordarea pe componente din OMFP 1802): se creează fișa `MF 000412/2` cu durata ei; pe `TAX`, valoarea se adaugă la fișa părinte (legea fiscală nu are componente). Fișa componentă are regimul `TAX` cu profilul `NONE`, iar raportul contabil vs fiscal (M3) arată diferența pe părinte.
- Sursa din factură consumă linia din coada „Imobilizări de preluat” (grupul `ASSET_IN`, M2 §3.3); o linie nu se consumă de două ori (`65411`).

**Reevaluarea**, reguli:

- **Metoda netă** (implicită, M1 §3.11 `RevaluationMethod = 1`): amortizarea cumulată se elimină din valoarea brută (`28xx` = `21x`), apoi valoarea netă se aduce la valoarea justă; noua valoare brută = valoarea justă, amortizarea cumulată = 0, durata rămasă neschimbată (sau nouă, din raport).
- **Metoda brută** (`RevaluationMethod = 2`): valoarea brută și amortizarea cumulată se recalculează proporțional cu indicele (valoarea justă / valoarea netă).
- Deprecierea existentă se închide la reevaluare (`29xx` = `21x`), înainte de calculul diferenței.
- Rezerva din reevaluare se ține pe fișă (`ReserveDelta` în jurnal), ca transferul lunar în `1175` (M3) și cel de la ieșire (M5) să fie pe bun.
- **În masă** (`MVM-REVM`): selecție pe filtre (grupă, locație, clasă), apoi import CSV din raportul evaluatorului (număr de inventar, valoare justă, durata rămasă opțională). Liniile fără valoare justă rămân neschimbate și apar ca avertisment; documentul se validează doar întreg.

### 3.3 Conservarea (`fa.AssetConservation`, `CNS`)

`CNS-PUN` pune fișele în starea 3 și marchează regimurile suspendate după setările `ConservationSuspendsBook` (implicit 0) și `ConservationSuspendsTax` (implicit 1) din M1 §3.11, de la luna următoare documentului. `CNS-REP`, generată din punerea validată, le repune în starea 2 și reia amortizarea de la luna următoare; durata de amortizare se prelungește cu lunile suspendate pe regimurile suspendate. Fără notă contabilă. Cel mult o conservare deschisă pe fișă (`65460`).

### 3.4 Compunerea și descompunerea (`fa.AssetRestructure`, `CDM`)

**Compunerea** (`CDM-CMP`): două sau mai multe fișe sursă (`65470`) devin o fișă nouă (sau se adaugă la o fișă țintă existentă). Exemple: server, unitate de stocare și UPS cumpărate separat, care funcționează ca un sistem; un utilaj asamblat din subansamble.

- Sursele trec în starea 7 „Compusă”, cu legătura spre ținta (`core.DocumentLink`); numerele lor de inventar nu se refolosesc.
- Ținta preia pe fiecare regim suma valorilor brute, a amortizării, a deprecierii și a rezervei (`MERGE_OUT` pe surse, `MERGE_IN` pe țintă). Durata rămasă și profilul se aleg pe document (implicit cea mai lungă durată rămasă și profilul grupei țintei). Pe `TAX`, sursele trebuie să aibă aceeași metodă (`65471`); dacă niciuna nu a început amortizarea, ținta primește profilul grupei.
- Sursele cu componente active sau în conservare nu pot fi compuse (`65472`).
- Dacă toate sursele sunt „în curs de punere în funcțiune”, ținta e tot în curs, iar PIF-ul se face pe țintă (M2 §3.7).

**Descompunerea** (`CDM-DSC`): o fișă sursă devine două sau mai multe fișe (`65481`): sursa rămâne cu o parte (sau se închide) și se creează fișe noi pentru restul. Exemple: o linie de rafturi mutată parțial în alt depozit; o clădire împărțită în corpuri cu durate diferite (abordarea pe componente aplicată ulterior).

- Repartizarea: procent pe fiecare țintă (suma 100 %, `65480`) sau valoare brută explicită; amortizarea, deprecierea și rezerva urmează aceeași proporție pe fiecare regim, cu diferența de rotunjire pe ultima țintă.
- Fiecare țintă primește grupa, clasa, locul și durata rămasă (implicit ale sursei); o țintă în alt loc evită un bon de mișcare separat.
- Componentele sursei se distribuie explicit pe ținte.

**Procesul-verbal** de compunere sau descompunere (șablon QuestPDF, semnat de comisie) se atașează documentului. Fiscal, compunerea și descompunerea nu sunt intrări sau ieșiri: valoarea fiscală totală și amortizarea fiscală cumulată se păstrează.

## 4. Fluxuri principale

### 4.1 Laptopuri mutate la Timișoara

`BMF 000057` din 25.11.2026, motiv „alt sediu”: MF 000434 și MF 000435 trec de la Birou Cluj (Bogdan Neagu, `CC-ADM-CJ`) la PL Timișoara (Cristina Marin, `CC-VNZ-TM`). Cristina confirmă primirea pe 26.11 scanând etichetele. Amortizarea din decembrie (prima lună) merge pe `CC-VNZ-TM`. Fără notă (firma nu are analitice pe gestiuni).

### 4.2 Bateria de schimb a stivuitorului

Factura `FF LND 55188` din 21.11.2026 (Linde Material Handling SRL), baterie de tracțiune 80V pentru al doilea schimb, 14.200,00 lei, apare în coada „Imobilizări de preluat” cu propunerea „Modernizare / componentă MF 000412”. Sorina Munteanu alege „Adaugă ca modernizare” → `MVM 000021` (`MVM-MOD`), varianta componentă:

- `BOOK`: fișa nouă `MF 000412/2`, durata 48 de luni, liniar, 295,83 / lună din decembrie 2026;
- `TAX`: 14.200,00 adăugați la MF 000412 (valoarea fiscală 112.600,00), cota degresivă recalculată pe durata rămasă de 88 de luni din decembrie;
- nota: `2131` = `231.PRL` 14.200,00.

### 4.3 Deprecierea autoutilitarei avariate

Autoutilitara MF 000391 (Iveco Daily CJ-21-EXD) e avariată pe 14.11.2026. Valoarea netă contabilă 62.400,00; evaluatorul estimează valoarea recuperabilă la 48.000,00. `MVM 000022` (`MVM-DEP`) cu raportul atașat: depreciere 14.400,00 pe `BOOK`, nota `6813` = `2913`; amortizarea scade din decembrie: fișa e amortizată pe kilometri, iar cota pe km se recalculează pe valoarea rămasă de 48.000,00 și kilometrii rămași (de la 0,2400 la 0,1846 lei/km). După reparație (martie 2027), o reevaluare a valorii recuperabile la 54.000,00 generează `MVM-DEPR` de 6.000,00: `2913` = `7813`.

### 4.4 Reevaluarea clădirii depozitului

`MVM 000024` (`MVM-REV`) la 31.12.2026, pentru MF 000012 Clădire depozit Cluj (grupa `CLAD`, `RevaluationAffectsTax = 1`), cu raportul evaluatorului autorizat ANEVAR atașat:

| | Înainte | După (metoda netă) |
| --- | --- | --- |
| Valoare brută | 1.250.000,00 | 1.080.000,00 |
| Amortizare cumulată | 314.583,33 | 0,00 |
| Valoare netă | 935.416,67 | 1.080.000,00 |
| Durata rămasă | 450 de luni | 450 de luni |
| Cota lunară `BOOK` | 2.083,33 | 2.400,00 |

Nota: `2812` = `212` 314.583,33 (eliminarea amortizării); `212` = `105` 144.583,33 (surplusul). Din ianuarie 2027, documentul lunar `AML` (M3) transferă `105` = `1175` 316,67 / lună (diferența dintre 2.400,00 și 2.083,33). Pe `TAX` se aplică aceeași reevaluare; amortizarea fiscală pe partea reevaluată e deductibilă, iar rezerva aferentă devine impozabilă lunar (`IFiscalDepreciationQuery.RevaluationReserveTaxable`, M3 §6).

### 4.5 Reevaluarea în masă a clădirilor

`MVM 000025` (`MVM-REVM`) la 31.12.2026: filtru grupa `CLAD` (14 fișe, fără MF 000012 deja reevaluată), import CSV din raportul evaluatorului. 12 fișe cresc (+356.900,00 în `105`), 2 scad (−25.000,00: −18.000,00 pe `105` existent din reevaluarea din 2023, −7.000,00 în `6813`). Previzualizarea arată nota pe grupă înainte de validare.

### 4.6 Conservarea depozitului din Arad

Depozitul din Arad se închide temporar. `CNS 000003` din 31.10.2026 pune MF 000288 și MF 000289 (rafturi) în conservare de la 01.11.2026: `TAX` suspendat, `BOOK` continuă (setarea implicită). Repunerea `CNS-REP` va prelungi durata fiscală cu lunile suspendate.

### 4.7 Compunerea sistemului server

MF 000442 (server Dell PowerEdge R760, 18.900,00), MF 000443 (unitate de stocare Dell ME5024, 12.400,00) și MF 000444 (UPS APC Smart-UPS 3000, 6.200,00) au intrat separat în noiembrie și sunt toate în curs de punere în funcțiune. Echipa IT decide că funcționează ca un singur sistem. `CDM 000002` (`CDM-CMP`) din 28.11.2026 creează **MF 000450** „Sistem server Dell R760 cu stocare și UPS”, 37.500,00 lei, grupa `IT`, `BOOK` liniar 48 de luni, `TAX` accelerat. Sursele trec în starea 7. Grupele sunt aceleași, deci fără notă. PIF-ul se face pe MF 000450 (`PIF 000101`, 01.12.2026), amortizarea din ianuarie 2027.

### 4.8 Descompunerea liniei de rafturi

MF 000290 Sistem rafturi paletizare depozit Cluj: brut 84.000,00, amortizat `BOOK` 21.000,00, `TAX` 26.250,00. 40 % din rafturi se mută la Timișoara. `CDM 000003` (`CDM-DSC`) din 30.11.2026: MF 000290 rămâne cu 60 % (50.400,00 / 12.600,00 / 15.750,00), iar fișa nouă **MF 000451** primește 40 % (33.600,00 / 8.400,00 / 10.500,00), cu locul PL Timișoara și centrul de cost `CC-DEP-TM`. Aceeași grupă: fără notă.

### 4.9 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Bon de mișcare pe o fișă în conservare | Permis (mutarea fizică e posibilă); conservarea rămâne |
| Modernizare pe o fișă amortizată complet | Permisă: fișa revine în starea 2, iar modernizarea se amortizează pe durata nouă introdusă obligatoriu (`65412`) |
| Depreciere mai mare decât valoarea netă | Refuzată (`65420`) |
| Reevaluare a unei fișe cu depreciere | Deprecierea se închide întâi; diferența se calculează pe valoarea netă după depreciere |
| Reevaluare cu valoare justă egală cu valoarea netă | Linie fără efect, avertisment; în masă se păstrează pentru dovada reevaluării |
| Schimbarea duratei fiscale în afara intervalului clasei HG 2139 | Refuzată (`65440`) |
| Reclasificare într-o grupă cu alt cont, după reevaluare | Rezerva se mută odată cu fișa (pe analiticul `105` al grupei noi, dacă există) |
| Compunere între grupe diferite | Permisă; nota mută valorile între conturi, ca la reclasificare |
| Descompunere cu o țintă de 0 % | Refuzată (`65480`) |
| Stornarea unei compuneri după PIF-ul țintei | Refuzată (`65490`); se face o descompunere |

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `MijlocFixMutat` v1 | Publicat | Fișele, locul vechi și nou, data, `Accounting` doar cu analitice pe gestiuni | M3 (centrul de cost), M7 (liste de inventar), 02.11 HR (bunuri pe angajat, viitor) |
| `MijlocFixModificat` v1 | Publicat | Felul operației, fișele, delta pe regim, `Accounting` (`AssetCost`, `Impairment`, `RevaluationSurplus`, `Reclassification`) | C2 (notă), M3 (plan, în aceeași tranzacție), 02.13 |
| `MijlocFixConservat` / `MijlocFixRepusInFunctiune` v1 | Publicate | Fișele, regimurile suspendate, data | M3, M7 |
| `MijlocFixRestructurat` v1 | Publicat | Sursele, țintele, valorile mutate, `Accounting` doar între grupe diferite | C2, M7 (etichete noi) |
| `LinieImobilizarePreluata` v1 | Publicat | Linia de factură consumată ca modernizare | A5 (starea liniei), C3 (`ASSET_PENDING`) |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M4)

```csharp
public interface IAssetPlacementQuery
{
    /// Locul valabil la o dată: folosit de M3 (centrul de cost), M6 (bunuri pe persoană), 02.11 HR (fișa de lichidare).
    Task<AssetPlacementDto?> GetAtAsync(long assetId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AssetPlacementDto>> GetByEmployeeAsync(int employeeId, DateOnly date, CancellationToken ct);
}
public sealed record AssetPlacementDto(long AssetId, string InventoryNumber, string Name, int? LocationId,
    int? ResponsibleEmployeeId, int? CostCenterId, int? DepartmentId, DateOnly ValidFrom);

public sealed record RevaluationImportRowDto(string InventoryNumber, decimal FairValue, int? RemainingLifeMonths, string? Note);
```

Comenzile MediatR (interne modulului): `CreateAssetTransfer`, `ValidateAssetTransfer`, `ConfirmAssetTransferReceipt`, `CreateAssetValueChange`, `ImportRevaluationRows`, `ValidateAssetValueChange`, `ReverseAssetValueChange`, `CreateConservation`, `EndConservation`, `CreateRestructure`, `ValidateRestructure`.

## 7. Schema SQL `fa` (partea M4)

| Tabel | Cheie primară | Chei unice / străine | Indecși |
| --- | --- | --- | --- |
| `fa.AssetTransfer` | `Id bigint` | UX `(CompanyId, NumberKey)`; FK `TransactionTypeId` | `(CompanyId, DocumentDate)` |
| `fa.AssetTransferLine` | `Id bigint` | FK `AssetTransferId`, `AssetId`, locul nou | `(AssetId)` |
| `fa.AssetValueChange` | `Id bigint` | UX `(CompanyId, NumberKey)`; `CHECK (ChangeKind IN ('IMPROVEMENT','IMPAIRMENT','IMPAIRMENT_REVERSAL','REVALUATION','ESTIMATE','RECLASS'))` | `(CompanyId, EffectiveMonth)` |
| `fa.AssetValueChangeLine` | `Id bigint` | FK `AssetValueChangeId`, `AssetId`, `BookCode`; `SourceLinkId` (linia de factură) | `(AssetId, BookCode)` |
| `fa.AssetConservation` · `fa.AssetConservationLine` | `Id bigint` | FK `AssetId`; UX `(AssetId) WHERE EndDate IS NULL AND Status = 3` | — |
| `fa.AssetRestructure` | `Id bigint` | UX `(CompanyId, NumberKey)`; `CHECK (RestructureKind IN ('MERGE','SPLIT'))` | — |
| `fa.AssetRestructureSource` · `fa.AssetRestructureTarget` | `Id bigint` | FK `AssetRestructureId`, `AssetId`; `SharePercent decimal(9,6) NULL`, `GrossAmount decimal(19,4) NULL` | — |

Procedurile de validare (`fa.usp_AssetTransfer_Validate`, `fa.usp_AssetValueChange_Validate`, `fa.usp_AssetConservation_Validate`, `fa.usp_AssetRestructure_Validate`) primesc planurile noi calculate în C# (`IDepreciationCalculator`, M3) ca TVP și scriu jurnalul, planul și outbox-ul în aceeași tranzacție (`SP-09`). Concurența: `rowversion` pe `fa.Asset`; validarea refuză documentul dacă o fișă a fost modificată de alt document între timp (`65403`).

**Erorile M4** (`65400–65599`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 65401 | `FA_ASSET_STATE_INVALID` | 409 | Operația nu e permisă în starea fișei (de exemplu depreciere pe o fișă ieșită) |
| 65402 | `FA_OPERATION_IN_CLOSED_MONTH` | 409 | Data efectului e într-o lună cu amortizare validată pentru fișă |
| 65403 | `FA_ASSET_CONCURRENT_CHANGE` | 409 | Fișa a fost modificată de alt document după încărcarea ciornei |
| 65404 | `FA_TRANSFER_SAME_PLACE` | 400 | Locul nou e identic cu cel vechi pe toate câmpurile |
| 65410 | `FA_IMPROVEMENT_AMOUNT_INVALID` | 400 | Valoarea modernizării ≤ 0 |
| 65411 | `FA_SOURCE_LINE_CONSUMED` | 409 | Linia de factură a fost deja preluată |
| 65412 | `FA_NEW_LIFE_REQUIRED` | 400 | Modernizare pe o fișă amortizată complet fără durată nouă |
| 65420 | `FA_IMPAIRMENT_EXCEEDS_NET` | 400 | Deprecierea depășește valoarea netă |
| 65421 | `FA_REVERSAL_EXCEEDS_IMPAIRMENT` | 400 | Reluarea depășește deprecierea existentă |
| 65430 | `FA_JUSTIFICATION_REQUIRED` | 400 | Lipsește atașamentul cerut de tranzacție |
| 65431 | `FA_REVALUATION_IMPORT_UNKNOWN_ASSET` | 400 | Număr de inventar din import inexistent sau în afara filtrului |
| 65440 | `FA_TAX_LIFE_OUT_OF_RANGE` | 400 | Durata fiscală în afara intervalului clasei |
| 65450 | `FA_RECLASS_SAME_GROUP` | 400 | Grupa nouă e aceeași |
| 65460 | `FA_CONSERVATION_OPEN` | 409 | Fișa are deja o conservare deschisă |
| 65470 | `FA_MERGE_MIN_SOURCES` | 400 | Compunere cu mai puțin de două surse |
| 65471 | `FA_MERGE_TAX_METHOD_MIXED` | 400 | Sursele au metode fiscale diferite și amortizare începută |
| 65472 | `FA_MERGE_SOURCE_BLOCKED` | 409 | Sursă cu componente active, în conservare sau ieșită |
| 65473 | `FA_RESTRUCTURE_TOTALS_MISMATCH` | 500 | Totalurile după compunere nu se potrivesc (eroare internă, verificare de siguranță) |
| 65480 | `FA_SPLIT_SHARES_INVALID` | 400 | Procentele nu fac 100 % sau o țintă are 0 % |
| 65481 | `FA_SPLIT_MIN_TARGETS` | 400 | Descompunere cu mai puțin de două fișe rezultate |
| 65482 | `FA_SPLIT_TOTALS_MISMATCH` | 500 | Verificare de siguranță la descompunere |
| 65490 | `FA_REVERSE_BLOCKED` | 409 | Există operații sau amortizare validată ulterior pe aceleași fișe |

## 8. Permisiuni și roluri

| Permisiune | Contabil mijloace fixe | Administrator patrimoniu | Șef administrativ | Responsabil de loc |
| --- | --- | --- | --- | --- |
| `FixedAssets.Transfer.Create` / `.Validate` | ✓ | ✓ | ✓ (aprobare) | — |
| `FixedAssets.Transfer.ConfirmReceipt` | — | ✓ | — | ✓ (doar ca primitor) |
| `FixedAssets.ValueChange.Create` / `.Validate` | ✓ | — | — | — |
| `FixedAssets.Revaluation.Import` | ✓ | — | — | — |
| `FixedAssets.Conservation.Manage` | ✓ | ✓ (ciornă) | ✓ | — |
| `FixedAssets.Restructure.Create` / `.Validate` | ✓ | ✓ (ciornă) | — | — |
| `FixedAssets.Operation.Reverse` | ✓ | — | — | — |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M4 |
| --- | --- |
| OMFP 1802 (evaluarea ulterioară) | Modernizările cresc valoarea; reparațiile sunt cheltuieli; reevaluarea la valoarea justă cu rezerva în `105`, transferată în `1175` pe măsura amortizării sau la ieșire; deprecierea prin `29xx`; schimbarea estimărilor prospectiv |
| Codul fiscal art. 28 și art. 26 | Investițiile la mijloace fixe majorează valoarea fiscală și se amortizează pe durata rămasă sau pe una nouă; reevaluarea clădirilor recunoscută fiscal, cu rezerva impozabilă pe măsura deducerii; conservarea suspendă amortizarea fiscală |
| HG 2139/2004 | Durata fiscală nouă în intervalul clasei |
| Legea 82/1991 și OMFP 2634/2015 | Bonul de mișcare a mijloacelor fixe (14-2-3A) ca formular tipizat (QuestPDF), procesul-verbal de compunere/descompunere |
| SAF-T D406 | Tipurile de tranzacții pe active (modernizare, reevaluare, depreciere, transfer) din jurnal |

**Precizări aplicate (27.11.2026):** M1 §3.5 (câmpul `RevaluationAffectsTax` pe grupă, cu valoarea 1 pentru `CLAD` și `TEREN` în datele livrate); M2 §3.1 (starea 7 „Compusă”); C2 (componenta `Reclassification` și modelele din M1 §3.9 pentru `MijlocFixModificat` și `MijlocFixRestructurat`); A5 (linia de imobilizare consumată ca modernizare primește starea „preluată”, ca la intrare).

## 10. Rapoarte

| Raport | Conținut |
| --- | --- |
| Bunuri pe responsabil | Fișele și obiectele de inventar (cu M6) ale unei persoane la o dată, tipăribil ca anexă la fișa postului sau la lichidare |
| Mișcări în perioadă | Bonurile de mișcare, cu neconfirmatele |
| Modernizări și reevaluări | Pe an, grupă și fișă, cu rezerva rămasă pe bun |
| Deprecieri active | Fișele cu `29xx` deschis și ultima evaluare |
| Bunuri în conservare | De când, regimurile suspendate, lunile de prelungire |
| Istoricul fișei | Toate documentele M2–M5 pe o fișă, în ordine (tab-ul „Istoric” din fișă) |

## 11. Ecrane principale

Machetele sunt în [ecrane/m4-operatii/](ecrane/m4-operatii/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Bon de mișcare | Formularul `BMF 000057`: laptopurile mutate la Timișoara, locul vechi și nou, confirmarea primitorului | `m4-01-bon-miscare` |
| Modernizare din factură | `MVM 000021`: bateria de schimb ca componentă MF 000412/2, efectul pe `BOOK` și `TAX`, nota | `m4-02-modernizare` |
| Depreciere | `MVM 000022`: autoutilitara avariată, valoarea recuperabilă, raportul atașat, planul nou | `m4-03-depreciere` |
| Reevaluare | `MVM 000024`: clădirea depozitului, metoda netă, înainte și după, nota, transferul lunar al surplusului | `m4-04-reevaluare` |
| Reevaluare în masă | `MVM 000025`: filtrul, importul raportului, liniile cu creștere și scădere, nota pe grupă | `m4-05-reevaluare-masa` |
| Conservare | `CNS 000003`: rafturile din Arad, regimurile suspendate | `m4-06-conservare` |
| Compunere | `CDM 000002`: trei fișe în curs de PIF compuse în MF 000450 | `m4-07-compunere` |
| Descompunere | `CDM 000003`: rafturile MF 000290 împărțite 60/40, ținta la Timișoara | `m4-08-descompunere` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| M4-A1 | `BMF 000057` închide locul vechi la 25.11 și deschide locul nou; amortizarea din decembrie e pe `CC-VNZ-TM` | Test de integrare (cu M3) |
| M4-A2 | `MVM 000021` creează MF 000412/2 doar pe `BOOK` (295,83 / lună) și adaugă 14.200,00 pe `TAX` la MF 000412; linia de factură e consumată o singură dată (`65411` la a doua încercare) | Test de integrare |
| M4-A3 | Deprecierea de 14.400,00 dă nota `6813` = `2913` și o cotă nouă pe valoarea de 48.000,00; o depreciere de 70.000,00 e refuzată (`65420`) | Test de integrare |
| M4-A4 | Reevaluarea MF 000012 dă exact tabelul din 4.4 (metoda netă) și transferul lunar de 316,67 din ianuarie 2027 | Test unitar (calculator) și de integrare |
| M4-A5 | Reevaluarea în masă cu o descreștere mai mare decât rezerva împarte diferența în `105` și `6813` | Test de integrare |
| M4-A6 | Conservarea suspendă doar `TAX` cu setările implicite; repunerea prelungește durata fiscală cu lunile suspendate | Test de integrare (cu M3) |
| M4-A7 | Compunerea a trei fișe păstrează totalurile pe fiecare regim; sursele trec în starea 7; compunerea a două surse cu metode fiscale diferite și amortizare începută e refuzată (`65471`) | Test de integrare |
| M4-A8 | Descompunerea 60/40 a MF 000290 dă valorile din 4.8 la ban, cu rotunjirea pe ultima țintă | Test unitar |
| M4-A9 | O operație cu efect într-o lună cu `AML` validat e refuzată (`65402`) | Test de integrare |
| M4-A10 | Stornarea unei modernizări după care s-a validat amortizarea e refuzată (`65490`) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Un document comun pentru toate operațiile de valoare | Da: `MVM` cu felul dat de tranzacție; bonul de mișcare, conservarea și restructurarea au documente proprii | ✓ Aprobat autonom (27.11.2026) |
| 2 | Notă la bonul de mișcare | Nu implicit; doar cu analitice pe gestiuni (setare) | ✓ Aprobat autonom |
| 3 | Confirmarea primitorului | Opțională pe tranzacție, cu scanarea etichetei; bunurile neconfirmate apar în raport | ✓ Aprobat autonom |
| 4 | Componentă nouă la modernizare | Permisă pe grupele cu componente amortizate separat; pe `TAX` valoarea merge la părinte | ✓ Aprobat autonom |
| 5 | Metoda de reevaluare | Metoda netă implicit, cea brută ca setare | ✓ Aprobat autonom |
| 6 | Reevaluarea pe regimul fiscal | Doar pentru grupele cu `RevaluationAffectsTax` (clădiri, terenuri) | ✓ Aprobat autonom |
| 7 | Rezerva din reevaluare | Ținută pe fișă, în jurnal, ca transferul în `1175` să fie pe bun | ✓ Aprobat autonom |
| 8 | Deprecierea | Doar contabilă, cu raport atașat obligatoriu; reluarea generată din depreciere | ✓ Aprobat autonom |
| 9 | Conservarea | Suspendă fiscal implicit, contabil doar prin setare; durata se prelungește cu lunile suspendate | ✓ Aprobat autonom |
| 10 | Compunerea | Sursele trec în starea 7 „Compusă”, ținta preia totalurile; metode fiscale diferite doar dacă nicio sursă nu a început amortizarea | ✓ Aprobat autonom |
| 11 | Descompunerea | Pe procente sau valori, cu amortizarea în aceeași proporție; ținta poate avea alt loc | ✓ Aprobat autonom |
| 12 | Corecția operațiilor | Stornare doar pe ultima operație; altfel operație inversă | ✓ Aprobat autonom |

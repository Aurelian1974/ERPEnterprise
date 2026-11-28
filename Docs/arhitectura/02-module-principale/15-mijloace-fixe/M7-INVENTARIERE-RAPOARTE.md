# ERP · 02.15 Mijloace fixe și obiecte de inventar · M7 Inventariere și rapoarte: listele, scanarea, diferențele, registrele, situațiile anuale

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)** (lucru autonom cerut de Aurelian; deciziile 1–10 adoptate pe recomandare). Ultima din cele 7 părți ale modulului 02.15. Fișa mijlocului fix tipizată se tipărește din această parte (formularul), cu datele din M2.

Schema `fa`, prefix `Erp.FixedAssets`, erorile M7 în plaja `65850–65949`. Această parte acoperă **inventarierea** mijloacelor fixe, a obiectelor de inventar în folosință și a bunurilor terților (decizia, comisiile, listele pe locații, scanarea etichetelor, constatarea stării, diferențele și documentele generate din ele, procesul-verbal) și **rapoartele modulului**: registrul numerelor de inventar, fișa mijlocului fix tipizată, balanța pe grupe, situația activelor imobilizate pentru situațiile financiare anuale, situația amortizării, contabil vs fiscal, proiecția, secțiunea de active din SAF-T (D406) și tabloul de bord. Documente părinte: [M1](M1-CONFIGURARE.md)–[M6](M6-OBIECTE-FOLOSINTA.md); [Stocuri S6](../05-stocuri/README.md) (inventarierea stocurilor, același model de liste și scanare); [Finanțe C4 și C5](../02-finante-contabilitate/README.md) (D406, situațiile financiare). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- Produs ERP on-premise, SQL Server case sensitive (`SQL-21`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite (scanarea în browser folosește biblioteca open source ZXing, licență Apache 2.0; terminalele cu scaner lucrează ca tastatură).
- Inventarierea e un document `INV` (`fa.AssetCount`) cu **instantaneul** evidenței la data inventarului (ce ar trebui să fie, unde, la cine) și cu **constatările** comisiei (ce s-a găsit). Diferențele nu schimbă nimic direct: la validare, M7 generează **documentele celorlalte părți** (lipsă `IMF-LIP` / `OBC-LIP`, plus `MFI-PLS`, bon de mișcare pentru bunurile găsite în alt loc, depreciere sau casare propuse), prin grupul de consum `COUNT_DIFF` (M1 §3.3).
- Rapoartele citesc **jurnalele** (valorile din `fa.AssetLedger`, M2; folosința din `fa.InUseMovement`, M6; locul din `fa.AssetPlacement`, M4), la orice dată. Rapoartele mari rulează pe replica de raportare, dacă există (doc 01, NFR).
- **Ce NU face M7:** nu inventariază stocurile (S6); nu întocmește situațiile financiare (C5) și nu generează fișierul D406 (C4), doar le dă datele prin contracte.

## 1. Scop și perimetru

| Zonă | Ce face M7 | Ce NU face (și cine face) |
| --- | --- | --- |
| Decizia și comisiile | Documentul de inventariere: data, perimetrul (firmă, locații, grupe, ce se inventariază), comisia centrală și comisiile pe locații | Decizia semnată (atașament) |
| Listele | Generate pe locație și persoană din instantaneu, tipărite sau pe telefon | — |
| Scanarea | În browser (camera telefonului) sau cu terminalul; marcarea găsit / alt loc / necunoscut, starea tehnică, poza | Inventarul stocurilor (S6) |
| Diferențe | Lipsuri, plusuri, bunuri în alt loc, propuneri de depreciere și casare; documentele M4–M6 generate la validare | Înregistrarea contabilă (din documentele generate) |
| Rapoarte | Registre, fișe, balanțe, situații anuale, D406, tablou de bord | Bugetul și analizele (02.13) |

## 2. Model de domeniu

```
 fa.AssetCount (INV: data, perimetrul, comisia centrală, starea)
   ├─ fa.AssetCountCommittee × N (locație sau grup de locații, membrii)
   ├─ fa.AssetCountLine × N      (instantaneul: mijloc fix / poziție OB / bun al terților, locul și persoana așteptate,
   │                               valoarea contabilă; constatarea: găsit, cantitate, loc constatat, stare, poza, scanat de, la)
   ├─ fa.AssetCountExtra × N     (bunuri găsite fără corespondent: etichetă necunoscută, bun fără etichetă)
   └─ fa.AssetCountResolution × N (diferența → documentul generat: IMF-LIP, OBC-LIP, MFI-PLS, BMF, OBT, MVM-DEP, IMF-CAS)
```

Reguli de business:

1. **Instantaneul se ia la pornire** (`Status` 2 „În desfășurare”): liniile se generează din evidență la data inventarului; operațiile cu dată ulterioară nu schimbă lista. Operațiile cu dată anterioară validate după pornire apar ca avertisment („evidența s-a schimbat după instantaneu”) și pot fi aduse în listă cu „Reîmprospătează liniile nescanate”.
2. **Un bun e într-un singur inventar deschis** (`65851`): două inventare cu perimetre care se suprapun nu pot fi în desfășurare în același timp.
3. **Pe durata inventarului, mișcările în locațiile inventariate cer confirmare** (avertisment pe bonul de mișcare și pe transfer), dar nu sunt blocate.
4. **Fiecare diferență are o rezolvare** înainte de validare (`65860`): lipsă (imputată sau nu), plus (cu valoarea justă), alt loc (mutare), stare (depreciere, casare, nicio acțiune), sau „eroare de scanare” cu motiv.
5. **Validarea generează documentele ca ciorne legate** (`COUNT_DIFF`) și închide inventarul doar după validarea lor, ca procesul-verbal să arate documentele finale (starea 3 „Validat”, apoi 4 „Închis”).
6. **Bunurile neinventariabile fizic** (necorporale, investiții în curs, terenuri) apar în listă ca „verificare documentară” (contract, licență, extras de carte funciară), confirmată de comisie fără scanare.

## 3. Entități și documente

### 3.1 Documentul de inventariere (`fa.AssetCount`, `INV`)

**Antet**: tranzacția (`INV-ANUAL`, `INV-PARTIAL`, `INV-PREDARE` pentru schimbarea gestionarului), numărul, data inventarului, perimetrul (locații, grupe; bife: mijloace fixe, obiecte în folosință, bunuri ale terților), comisia centrală (președinte, membri), decizia (atașament), termenul, starea (1 Ciornă · 2 În desfășurare · 3 Validat · 4 Închis · 9 Anulat).

**Comisiile pe locații**: fiecare comisie primește locațiile ei și vede pe telefon doar listele lor.

### 3.2 Scanarea

Pagina de scanare (responsivă, pentru telefon) are: locația curentă (aleasă sau scanată de pe eticheta locației), lista așteptată, câmpul de scanare (camera sau terminalul). La scanarea unei etichete:

| Situație | Marcare |
| --- | --- |
| Bunul e pe lista locației | Găsit ✓; opțional starea tehnică (bun, uzat, defect, nefolosit) și o poză |
| Bunul e în evidență, dar în altă locație sau la altă persoană | „Găsit în alt loc” → rezolvare propusă: bon de mișcare (`BMF` / `OBT`) |
| Eticheta nu există în evidență sau bunul e ieșit | Linie „extra”, de identificat (plus sau eroare) |
| Bun fără etichetă | Linie „extra” adăugată manual, cu descriere și poză |

Obiectele de inventar pe cantitate se numără (cantitatea găsită pe poziție); cele pe bucată se scanează. Scanarea merge și offline (lista se descarcă pe telefon, rezultatele se trimit la reconectare, cu deduplicare pe `ScanId`).

### 3.3 Diferențele și documentele generate

| Diferență | Rezolvare | Document generat (ciornă) |
| --- | --- | --- |
| Mijloc fix negăsit | Lipsă imputată sau neimputabilă | `IMF-LIP` (M5) |
| Obiect de inventar negăsit (sau cantitate mai mică) | Lipsă imputată sau neimputabilă | `OBC-LIP` (M6) |
| Bun găsit fără corespondent, care e al firmei | Plus la valoarea justă | `MFI-PLS` (M2) pentru mijloace fixe; pentru obiecte de inventar, intrare în stoc prin S2 (plus de inventar) și apoi dare în folosință |
| Găsit în alt loc | Mutare | `BMF` (M4) sau `OBT` (M6) |
| Stare „defect”, „nefolosit” | Depreciere, casare sau conservare propuse | `MVM-DEP`, `IMF-CAS`, `CNS-PUN` (M4, M5) |
| Bun al terților negăsit | Notificare proprietar (fără document contabil) | Sarcină pentru administrator |

### 3.4 Procesul-verbal și registrul-inventar

Procesul-verbal de inventariere (QuestPDF) cuprinde perimetrul, comisiile, rezultatele pe locații, diferențele cu documentele lor și propunerile. Listele de inventariere se tipăresc în formatul tipizat (lista de inventariere pentru mijloace fixe și obiecte de inventar, OMFP 2634) cu valoarea contabilă și valoarea de inventar. Rândurile pentru **registrul-inventar** (formular 14-1-2, ținut de Finanțe) se dau prin `IAssetCountResultQuery` (6), pe conturi.

### 3.5 Rapoartele modulului

| Raport | Conținut | Sursa |
| --- | --- | --- |
| Registrul numerelor de inventar | Numerele alocate în ordine, cu denumirea, data intrării, ieșirea; inclusiv cele anulate și cele ale componentelor (formularul tipizat) | `fa.Asset` |
| Fișa mijlocului fix | Formularul tipizat (OMFP 2634): identificare, intrare, PIF, durată, metodă, modernizări, reevaluări, amortizare anuală, ieșire; pe regimul contabil, opțional și fiscal | M2–M5 |
| Balanța pe grupe | Pe grupă sau cont: sold inițial, intrări, ieșiri, sold final, pentru brut, amortizare, depreciere; concordată cu balanța contabilă (C3) | Jurnalul |
| Situația activelor imobilizate | Anexa la situațiile financiare (formularul F40 al bilanțului anual): valori brute, amortizare și ajustări pe categoriile din formular, cu creșteri, reduceri, transferuri și reevaluări | Jurnalul, prin `IFixedAssetStatementQuery` către C5 |
| Situația amortizării | Pe grupă și fișă: amortizarea lunii, a anului, cumulată | M3 |
| Contabil vs fiscal · Proiecția | Din M3 (ecranele `m3-03`, `m3-05`) | M3 |
| D406 active (SAF-T) | Secțiunea `Assets` și `AssetTransactions` pentru D406 anual sau la cerere: fiecare activ cu valorile pe an, tranzacțiile de intrare, amortizare, reevaluare, ieșire | Jurnalul, prin `ISaftAssetsQuery` către C4 |
| Tablou de bord | Valoare brută și netă pe grupe, amortizarea lunii, investiții în curs, coada de preluat, fișe fără PIF, bunuri neconfirmate, garanții care expiră, inventarul în curs, predări deschise | Toate |

## 4. Fluxuri principale

### 4.1 Inventarul anual 2026

1. Pe 01.12.2026, Paul Constantin creează `INV 000012` (`INV-ANUAL`), data 31.12.2026, perimetrul: toate locațiile Exemplu Distribuție, mijloace fixe + obiecte în folosință + bunuri ale terților. Decizia administratorului 22/2026 atașată; comisia centrală: Adrian Barbu (președinte), Sorina Munteanu, Mihai Stan; 4 comisii pe locații (Cluj birouri, Cluj depozit, Timișoara, Arad).
2. Pe 29.12, la pornire, instantaneul are 441 de mijloace fixe, 1.184 de poziții de obiecte de inventar (2.906 bucăți) și 14 bunuri ale terților.
3. Comisiile scanează între 29 și 31.12. Rezultate: 1 mijloc fix lipsă (MF 000402 videoproiector, sala de ședințe Cluj), 1 plus (un monitor fără etichetă în depozitul Cluj, identificat ca obiect de inventar neînregistrat, valoare justă 600,00), 3 bunuri găsite în alt loc, 2 bunuri cu stare „defect” propuse pentru depreciere (`DEPR`: linia de ambalare MF 000377 și cântarul de platformă), căștile Jabra ale lui Mihnea Pavel lipsă.
4. Rezolvări: `IMF 000034` (M5, imputată), `OBC 000019` (M6, imputată), plus OB prin S2, 3 bonuri de mișcare, `MVM 000027` (depreciere linie ambalare, cu evaluare) și un proces-verbal de constatare pentru cântar (fără depreciere, valoare mică).
5. Validarea inventarului pe 08.01.2027 generează ciornele; după validarea lor, `INV 000012` trece în „Închis”, iar procesul-verbal se tipărește și se semnează.

### 4.2 Situația activelor imobilizate la 31.12.2026

C5 cere `IFixedAssetStatementQuery` pentru 2026: rândurile pe categoriile formularului (terenuri și construcții, instalații tehnice și mașini, alte instalații și mobilier, avansuri și imobilizări în curs, imobilizări necorporale), cu sold inițial, creșteri (intrări, modernizări, reevaluări), reduceri (ieșiri, eliminarea amortizării la reevaluare), sold final, și separat amortizarea și ajustările. Concordanța cu balanța e verificată înainte (C3).

### 4.3 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Bun scanat de două comisii | A doua scanare e marcată „deja găsit de comisia X”; nu dublează cantitatea |
| Bun vândut după data inventarului, înainte de scanare | Rămâne în instantaneu (exista la dată); scanarea lipsă e rezolvată ca „ieșit ulterior”, fără document |
| Etichetă deteriorată | Căutare după serie sau denumire; marcare „găsit, etichetă de reînnoit” → coada de etichete |
| Inventar parțial pe o locație în timpul anului | `INV-PARTIAL`, aceleași reguli, fără suprapunere cu alt inventar deschis |
| Schimbarea responsabilului de loc | `INV-PREDARE` pe locație, cu predătorul și primitorul în comisie |

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `InventariereMfPornita` v1 | Publicat | Perimetrul, data | M4, M6 (avertismentele de mișcare) |
| `InventariereMfValidata` v1 | Publicat | Rezultatele, documentele generate | Finanțe (registrul-inventar), 02.13 |
| `InventariereMfInchisa` v1 | Publicat | — | C3 (verificarea anuală „inventar efectuat”) |
| `MijlocFix*`, `ObiectInventar*` | Consumate | — | Tabloul de bord, rapoartele (proiecții de citire) |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M7)

```csharp
public interface IFixedAssetStatementQuery
{
    /// Pentru C5: situația activelor imobilizate pe categoriile formularului anual.
    Task<IReadOnlyList<FixedAssetStatementRowDto>> GetAsync(int companyId, int year, CancellationToken ct);
}
public sealed record FixedAssetStatementRowDto(string CategoryCode, decimal GrossOpening, decimal GrossIncreases,
    decimal GrossDecreases, decimal GrossClosing, decimal DepreciationOpening, decimal DepreciationYear,
    decimal DepreciationDecreases, decimal DepreciationClosing, decimal ImpairmentOpening, decimal ImpairmentIncreases,
    decimal ImpairmentDecreases, decimal ImpairmentClosing);

public interface ISaftAssetsQuery
{
    /// Pentru C4: secțiunea de active a SAF-T (D406) pe an, cu tranzacțiile pe activ.
    IAsyncEnumerable<SaftAssetDto> GetAssetsAsync(int companyId, int year, CancellationToken ct);
}

public interface IAssetCountResultQuery
{
    /// Pentru registrul-inventar (Finanțe): valoarea contabilă și de inventar pe conturi, după inventariere.
    Task<IReadOnlyList<AssetCountAccountRowDto>> GetByAccountAsync(long assetCountId, CancellationToken ct);
}
```

## 7. Schema SQL `fa` (partea M7)

| Tabel | Cheie primară | Chei unice / străine | Indecși |
| --- | --- | --- | --- |
| `fa.AssetCount` | `Id bigint` | UX `(CompanyId, NumberKey)`; FK `TransactionTypeId` | `(CompanyId, CountDate)` |
| `fa.AssetCountCommittee` · `fa.AssetCountCommitteeMember` | `Id int` | FK `AssetCountId`, `EmployeeId` | — |
| `fa.AssetCountLine` | `Id bigint` | FK `AssetCountId`; exact una dintre `AssetId`, `InUseRecordId`, `ThirdPartyAssetId` (`CHECK`); UX `(AssetCountId, AssetId)` etc. | `(AssetCountId, ExpectedLocationId)`; `(AssetCountId, LabelCode)` pentru scanare |
| `fa.AssetCountScan` | `Id bigint` | FK `AssetCountLineId NULL`; UX `(ScanId)` (deduplicare offline) | `(AssetCountId, ScannedAt)` |
| `fa.AssetCountExtra` · `fa.AssetCountResolution` | `Id bigint` | FK `AssetCountId`; rezoluția cu `DocumentType` + `DocumentId` | — |

Rapoartele mari (balanța pe grupe, situația activelor, D406) sunt proceduri set-based pe jurnal (`fa.usp_Report_GroupBalance`, `fa.usp_Report_FixedAssetStatement`, `fa.usp_Report_SaftAssets`), cu ținta sub 5 secunde pentru 10.000 de fișe și un an de mișcări.

**Erorile M7** (`65850–65949`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 65851 | `FA_COUNT_OVERLAP` | 409 | Alt inventar în desfășurare cu perimetru comun |
| 65852 | `FA_COUNT_NOT_RUNNING` | 409 | Scanare într-un inventar care nu e în desfășurare |
| 65853 | `FA_COUNT_LOCATION_NOT_ASSIGNED` | 403 | Comisia scanează într-o locație care nu e a ei |
| 65860 | `FA_COUNT_UNRESOLVED_DIFFERENCES` | 409 | Validare cu diferențe fără rezolvare |
| 65861 | `FA_COUNT_DOCUMENTS_PENDING` | 409 | Închidere cu documente generate încă în ciornă |
| 65870 | `FA_REPORT_PERIOD_OPEN` | 409 | Situația activelor imobilizate cerută pentru un an neînchis (avertisment, confirmabil pentru simulare) |

## 8. Permisiuni și roluri

| Permisiune | Contabil MF | Administrator patrimoniu | Șef administrativ | Membru comisie | Responsabil de loc |
| --- | --- | --- | --- | --- | --- |
| `FixedAssets.Count.Create` / `.Start` | ✓ | ✓ | — | — | — |
| `FixedAssets.Count.Scan` | — | ✓ | — | ✓ (locațiile comisiei) | ✓ (doar `INV-PREDARE`) |
| `FixedAssets.Count.Resolve` / `.Validate` | ✓ | ✓ (rezolvare) | ✓ (aprobare PV) | — | — |
| `FixedAssets.Reports.View` | ✓ | ✓ (fără valori fiscale) | ✓ | — | — |
| `FixedAssets.Reports.Statutory` (situații, D406) | ✓ | — | — | — | — |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M7 |
| --- | --- |
| Legea contabilității 82/1991 și OMFP 2861/2009 | Inventarierea anuală și la schimbarea gestionarului; comisia numită prin decizie; listele de inventariere; procesul-verbal; valorificarea diferențelor; registrul-inventar |
| OMFP 2634/2015 | Registrul numerelor de inventar, fișa mijlocului fix, listele de inventariere (formulare tipizate, cu conținutul minim obligatoriu) |
| OMFP 1802 și formatul situațiilor financiare anuale | Situația activelor imobilizate (anexa F40) |
| SAF-T D406 (Ordinul ANAF 1783/2021 și modificările) | Secțiunea `MasterFiles/Assets` și `AssetTransactions`, la D406 anual pentru active |

**Precizări aplicate (27.11.2026):** C4 (D406 citește `ISaftAssetsQuery` pentru secțiunea de active); C5 (situația activelor imobilizate din `IFixedAssetStatementQuery`); Finanțe (registrul-inventar primește rândurile de mijloace fixe din `IAssetCountResultQuery`); C3 (verificarea anuală „inventarierea mijloacelor fixe închisă” la închiderea anului, din `InventariereMfInchisa`); S6 (aceeași componentă de scanare, cu ZXing, refolosită).

## 10. Rapoarte

Lista completă e în 3.5. Toate se exportă în Excel (ClosedXML) și PDF (QuestPDF), se pot programa (trimitere lunară pe e-mail, 02.13) și respectă drepturile pe firmă și pe valorile fiscale.

## 11. Ecrane principale

Machetele sunt în [ecrane/m7-inventariere-rapoarte/](ecrane/m7-inventariere-rapoarte/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Inventariere | Formularul `INV 000012`: perimetrul, comisiile, progresul pe locații, diferențele | `m7-01-inventariere` |
| Scanare | Pagina de telefon a comisiei pentru sala de ședințe Cluj: lista așteptată, scanările, lipsa | `m7-02-scanare` |
| Diferențe | Diferențele inventarului cu rezolvările și documentele generate | `m7-03-diferente` |
| Fișa mijlocului fix (tipizată) | Formularul tipărit pentru stivuitorul MF 000412 | `m7-04-fisa-mf-tipizata` |
| Balanța pe grupe | Sold inițial, intrări, ieșiri, sold final pe grupe, brut și amortizare, cu concordanța | `m7-05-balanta-grupe` |
| Registrul numerelor de inventar | Numerele alocate, inclusiv componentele și numerele ieșite | `m7-06-registru-numere` |
| Tablou de bord | Indicatorii modulului pentru contabil și administratorul de patrimoniu | `m7-07-tablou-de-bord` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| M7-A1 | Instantaneul la pornire are exact evidența de la data inventarului; o operație ulterioară nu schimbă lista | Test de integrare |
| M7-A2 | Scanările offline trimise de două ori nu dublează (deduplicare pe `ScanId`) | Test de integrare |
| M7-A3 | Validarea `INV 000012` cu diferențele din 4.1 generează exact ciornele din 4.1; închiderea e refuzată cât una e ciornă (`65861`) | Test de integrare (cu M2, M4, M5, M6) |
| M7-A4 | Două inventare suprapuse nu pot fi pornite (`65851`) | Test de integrare |
| M7-A5 | Balanța pe grupe la 31.12.2026 se potrivește cu soldurile `21x`, `28xx`, `29xx` din balanța contabilă | Test de integrare (cu C3) |
| M7-A6 | Situația activelor imobilizate: sold inițial + creșteri − reduceri = sold final pe fiecare categorie | Test de proprietăți |
| M7-A7 | `ISaftAssetsQuery` produce secțiunea validă după schema XSD a SAF-T pentru datele de test | Test de integrare (cu C4) |
| M7-A8 | Rapoartele mari rulează sub 5 secunde pe 10.000 de fișe | Test de performanță |

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Vederea publicată** `fa.vw_AssetLedgerFact` (v1): mișcarea de valoare pe bun, regimul (contabil, fiscal), tipul de operație, documentul. Sursa setului de date „Mijloace fixe” din 02.13 și a secțiunii „mijloace fixe intrate” din raportul lunar al conducerii. [BI2 §9](../13-raportare-bi/BI2-DEPOZIT-DATE.md)

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Ce se inventariază | Mijloace fixe, obiecte în folosință și bunuri ale terților, în același document, cu liste separate | ✓ Aprobat autonom (27.11.2026) |
| 2 | Instantaneul | La pornire, cu reîmprospătarea liniilor nescanate la cerere | ✓ Aprobat autonom |
| 3 | Scanarea | În browser cu ZXing (open source) și cu terminale-tastatură; offline cu deduplicare | ✓ Aprobat autonom |
| 4 | Mișcările în timpul inventarului | Permise cu avertisment, nu blocate | ✓ Aprobat autonom |
| 5 | Diferențele | Rezolvare obligatorie; documentele celorlalte părți generate ca ciorne legate | ✓ Aprobat autonom |
| 6 | Plusul de obiecte de inventar | Intrare în stoc prin S2 și dare în folosință, nu direct în evidență | ✓ Aprobat autonom |
| 7 | Formularele tipizate | Registrul numerelor de inventar, fișa MF, listele, PV, cu QuestPDF | ✓ Aprobat autonom |
| 8 | Situația activelor imobilizate | Calculată din jurnal și dată C5 prin contract | ✓ Aprobat autonom |
| 9 | D406 active | Secțiunea dată C4 prin contract, din jurnal | ✓ Aprobat autonom |
| 10 | Tabloul de bord | În modul, pentru contabil și administrator; analizele avansate în 02.13 | ✓ Aprobat autonom |

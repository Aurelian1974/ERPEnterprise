# ERP · 02.05 Stocuri · S6 Inventariere

26 noiembrie 2026 · Aurelian · **Validat (26.11.2026)**. A șasea din cele 7 părți ale modulului 02.05 Stocuri. Pornește de la S1–S5, validate pe 26.11.2026. S6 compară ce e în evidență (**scriptic**) cu ce se găsește la numărare (**faptic**), stabilește cauza fiecărei diferențe și o înregistrează: plusuri, minusuri, perisabilități, compensări și imputări, cu procesul-verbal al comisiei.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori S6 `54650–54799`. Documente părinte: [S1 Gestiuni, depozite și setări](S1-GESTIUNI-SI-SETARI.md), [S2 Mișcări și documente](S2-MISCARI-SI-DOCUMENTE.md), [S3 Evaluare și cost](S3-EVALUARE-SI-COST.md), [S4 Loturi, serii și termene](S4-LOTURI-SERII-TERMENE.md), [S5 Rezervări și disponibil](S5-REZERVARI-SI-DISPONIBIL.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Stocul stă pe gestiune și pe variantă (**SKU**, `core.ItemVariant`). La articolele urmărite pe lot, stocul stă pe **CKU** (SKU + lot + termen, un rând `inv.Lot`, cod vizibil `{SKU}·{lot}·{AAMMZZ}`), iar la cele urmărite pe serie, pe fiecare bucată (`inv.Serial`). Articolele fără urmărire (piulițe, cărămizi, zahăr vrac) au doar cantitatea (S4 §3.4).
- **Inventarul nu modifică direct stocul.** Diferențele validate devin mișcări S2 obișnuite, `COUNT_PLUS` și `COUNT_MINUS`, trecute prin `inv.usp_StockMovement_Post`, cu costul din S3 și cu nota contabilă generată de C2. Corecțiile de lot în cadrul aceluiași SKU folosesc documentul de schimbare a lotului `SL` (S4 §3.7).
- Gestiunea se poate bloca pe durata inventarului (S1 §4.3, `IsBlocked`). Inventarul de predare a gestiunii pornește din predarea-primirea S1 (`inv.WarehouseHandover`, setarea `RequireCountOnHandover`).
- Comisia de inventariere e numită prin decizie scrisă a conducerii. **Gestionarul nu face parte din comisie**: asistă la numărare și semnează declarațiile și listele (OMFP 2861/2009).
- **Ce NU face S6:** nu inventariază imobilizările (Mijloace fixe), casa, conturile bancare, creanțele și datoriile (Finanțe); nu calculează reținerea din salariu pentru imputări (HR); nu face ajustările pentru depreciere (`397`, C2: S6 doar le propune); nu ghidează fizic numărătorii prin depozit pe celule (WMS 02.06, care folosește API-ul S6); nu face registrul-inventar (C3, din totalurile trimise de S6).

## 1. Scop și perimetru

| Zonă | Ce face S6 | Ce NU face (și cine face) |
| --- | --- | --- |
| Inventarul | Documentul de inventar pe o gestiune: tip, perimetru, comisie, data de referință, blocarea, stările | Decizia de numire a comisiei ca act (se atașează; numerotarea deciziilor e în Administrare) |
| Scripticul | Instantaneul stocului din evidență la data de referință, pe SKU, CKU, serie și celulă WMS | Stocul în sine (S2) |
| Numărarea | Listele de numărare pe zone și echipe, numărarea oarbă, scanarea, cântărirea, renumărarea | Ghidarea pe celule și dispozitivele mobile (WMS) |
| Diferențele | Plusuri și minusuri pe cheie, valoarea lor, reconcilierea mișcărilor din timpul inventarului | Valoarea de înlocuire ca preț de piață (o propune Achiziții, din ultimul preț) |
| Stabilirea cauzelor | Perisabilități în limita normelor, compensări între sortimente confundabile, corecții de lot, lipsuri imputabile și neimputabile | Recuperarea efectivă a sumei imputate (Finanțe și HR) |
| Documente | Listele de inventariere, lista separată pentru bunurile altora, declarațiile gestionarului, procesul-verbal | Registrul-inventar (C3) |
| Înregistrarea | Mișcările `COUNT_PLUS` / `COUNT_MINUS` și `SL`, imputările ca evenimente | Nota contabilă (C2, din evenimentele de mișcare) |

## 2. Model de domeniu

```
 InventoryCount (INV-xx nnnn) ── tip, gestiune, perimetru, data de referință, mod de blocare, stare
   ├─ InventoryCommissionMember  (președinte, membri; gestionarul asistă)
   ├─ InventoryCountScope        (ce intră: toată gestiunea sau categorii, zone WMS, articole)
   ├─ InventorySnapshot          (scripticul la data de referință, pe cheie)
   ├─ InventoryList ── InventoryCountLine   (listele de numărare, pe zone și echipe, cu rundele de numărare)
   ├─ InventoryDifference        (faptic − scriptic, pe cheie, cu modul de stabilire)
   │    └─ InventoryCompensation (perechea minus ↔ plus compensată)
   ├─ InventoryImputation        (cui se impută, cât, la ce valoare)
   └─ mișcări S2: COUNT_PLUS, COUNT_MINUS; documente SL (S4)

 ShrinkageNorm   (normele de perisabilitate pe categorie sau articol)
 ConfusableGroup (grupele de sortimente cu risc de confuzie, aprobate)
```

Reguli:

1. **Un singur inventar deschis pe o cheie.** Două inventare deschise nu pot acoperi același SKU în aceeași gestiune (`54651`). Inventarul de predare acoperă mereu toată gestiunea.
2. **Scripticul se fotografiază la data de referință.** La pornire, S6 copiază în `inv.InventorySnapshot` stocul fiecărei chei din perimetru, cu costul unitar și, la gestiunile la preț de vânzare, cu prețul. Diferența se calculează față de acest instantaneu, nu față de stocul curent (cu excepția modului 3, regula 5).
3. **Cheia de numărare urmează urmărirea articolului.** Fără urmărire: (SKU) sau (SKU, celulă WMS). Lot: (CKU). Serie: fiecare serie, cu cantitatea 1. Serie și lot: seria, cu lotul ei.
4. **Numărarea e oarbă implicit.** Lista de numărare nu arată scripticul. Doar cine are `Inv.Count.ViewBook` îl vede, iar diferențele apar abia după închiderea numărării.
5. **Blocarea depinde de `CountFreezeMode`** (S1 §3.3), pe firmă, suprascris pe gestiune:
   - **1 Blochează gestiunea** (implicit): de la data de referință până la închiderea numărării, documentele gestiunii nu se pot valida (`54005`, S1). După închiderea numărării gestiunea se deblochează, chiar dacă diferențele se stabilesc mai târziu: diferențele se raportează la scripticul de la data de referință.
   - **2 Blochează doar articolele din perimetru**: pentru inventarele parțiale (rotative). Mișcările pe celelalte articole merg normal; cele pe articolele numărate sunt refuzate (`54666`).
   - **3 Fără blocare**: pentru depozitele mari cu WMS. Scripticul fiecărei chei e instantaneul plus mișcările validate până la ora la care cheia a fost numărată (`CountedAt`). Cere numărare pe celule WMS și disciplină la scanare.
6. **Faptele POS se acceptă mereu** (S1 §4.3). Bonurile cu ora după data de referință apar separat, ca „mișcări după referință”, și nu schimbă diferența în modurile 1 și 2.
7. **Diferențele nu se înregistrează până nu au toate o cauză.** Fiecare minus are cel puțin un mod de stabilire (perisabilitate, compensare, corecție de lot, imputabil, neimputabil), iar suma cantităților e exact minusul (`54656`).
8. **Diferențele se înregistrează la data de referință** a inventarului. Dacă luna acelei date e închisă, intră în prima lună deschisă, cu avertisment, ca la S3.

## 3. Funcționalitate

### 3.1 Inventarul (`inv.InventoryCount`)

| Câmp | Reguli |
| --- | --- |
| `Number`, `Uid` | Seria `INV-{cod gestiune}`, numerotare pe gestiune (`INV-TM 0012`); `Uid uniqueidentifier` unic (`SQL-14`) |
| `CountType` | 1 Anual · 2 Periodic sau rotativ · 3 La predarea gestiunii · 4 Inopinat (control) · 5 La un eveniment (calamitate, furt, fuziune) |
| `WarehouseId` | Gestiune de tip 1 Depozit, 2 Magazin sau 4 Custodie. Nu se inventariază gestiunea de tranzit (se reconciliază cu transferurile, S2) (`54652`) |
| `ScopeKind` | 1 Toată gestiunea · 2 Selecție (categorii, zone WMS, articole, din `inv.InventoryCountScope`). Tipurile 1 și 3 cer toată gestiunea |
| `ReferenceAt` | Data și ora de referință: momentul instantaneului și al blocării. Nu poate fi într-o lună închisă fără drept (`54662`) |
| `FreezeMode` | Copiat din setare (regula 5); se poate schimba până la pornire |
| `IsBlind` | Copiat din `CountBlind` (implicit 1) |
| `DecisionNo`, `DecisionDate`, `DecisionFileId` | Decizia de inventariere. Obligatorie la pornire pentru tipurile 1, 3, 4 și 5 |
| `HandoverId` | Predarea din care a pornit (tipul 3) |
| `PreviousCountId` | Inventarul anterior al aceleiași gestiuni, baza pentru perisabilități (3.5). Propus automat |
| `Status` | 1 Ciornă · 2 În numărare · 3 Numărare închisă · 4 Diferențe stabilite · 5 Finalizat · 6 Anulat |

Comisia (`inv.InventoryCommissionMember`) are un președinte și cel puțin un membru (`54659`). Gestionarii gestiunii, inclusiv cel care predă și cel care primește, se adaugă cu rolul „asistă”: semnează declarațiile și listele, dar nu pot fi președinte sau membru (`54659`).

Pornirea (`StartCount`) face, într-o tranzacție: verificarea comisiei și a deciziei, instantaneul (`inv.InventorySnapshot`), blocarea (regula 5), generarea listelor de numărare (3.2) și evenimentul `InventarPornit`. La pornire, gestionarul dă **declarația de început** (în sistem, cu confirmarea că toate documentele de intrare și ieșire au fost predate la contabilitate); textul e cel din OMFP 2861/2009, iar formularul semnat se atașează.

### 3.2 Listele de numărare (`inv.InventoryList`)

- Listele se generează pe zone: o listă pe zonă sau grup de celule (când depozitul folosește WMS) ori pe categorie de articol și raft, după cum alege comisia. Fiecare listă are o echipă de cel puțin 2 persoane, dintre care cel puțin un membru al comisiei.
- O linie de listă e o cheie din instantaneu (regula 3). La articolele urmărite pe lot, lista arată CKU-urile cunoscute; numărătorul poate adăuga oricând o linie nouă (lot sau articol găsit, care nu era în evidență).
- **Moduri de introducere:** scanare (codul GS1-128 sau DataMatrix dă SKU-ul, lotul și termenul, deci CKU-ul; codul seriei dă seria), introducere manuală, import din fișierul terminalului mobil. API-ul de numărare e folosit și de WMS.
- **UM de numărare:** se poate număra în orice UM a articolului (bax de 24, palet de 1.920, kg), cu conversia în UM de bază. Articolele vrac fără urmărire (zahăr, ciment, cuie) se pot cântări: cântarul trimite greutatea netă, iar pentru articolele numărate la bucată pe bază de greutate se folosește factorul `kg/buc` din Nucleu.
- **Zero confirmat:** o linie din instantaneu pe care nu s-a găsit nimic se marchează explicit „0 găsit”. Închiderea numărării cere ca toate liniile să aibă o cantitate sau zero confirmat (`54654`).
- **Lot găsit cu alt termen:** dacă se scanează un lot cunoscut cu alt termen decât cel din evidență, linia cere confirmare (`54661`, ca `54452` din S4). La confirmare se creează CKU-ul nou, care apare ca plus pe CKU-ul nou și minus pe cel vechi (o corecție de lot, 3.6).
- **Marfă fără lot lizibil:** se numără pe lotul `NEIDENTIFICAT` (S4 §3.7) și se corectează după inventar cu documentul `SL`.
- **Serii:** fiecare serie se scanează. O serie citită de două ori în același inventar e refuzată (`54660`). O serie care în evidență e în altă gestiune se semnalează („serie găsită în altă gestiune”) și se stabilește ca transfer neînregistrat (3.6), nu ca plus. La seriile cu identificatori suplimentari (IMEI, MAC, VIN), lista arată identificatorul principal și îl verifică.
- **Starea lotului nu se schimbă la inventar:** loturile în carantină, blocate, expirate sau retrase se numără ca toate celelalte. Loturile expirate găsite se listează separat, ca propunere de descărcare `EXPIRED` (S2).

### 3.3 Renumărarea

O linie cere o a doua numărare, de altă echipă, când diferența față de scriptic depășește pragul procentual **sau** pragul valoric (setările `CountRecountPct`, implicit 2%, și `CountRecountValue`, implicit 500 lei). Comisia poate cere oricând renumărarea unei linii sau a unei liste întregi.

- Se păstrează toate rundele (`Round` 1, 2, 3). Cantitatea finală e cea din runda a doua dacă e egală cu prima; dacă diferă, decide runda a treia, făcută în prezența președintelui comisiei.
- Închiderea numărării e refuzată cât timp există linii care cer renumărare și nu au fost renumărate (`54655`).
- La numărarea oarbă, echipa care renumără nu vede nici scripticul, nici numărarea anterioară.

### 3.4 Diferențele (`inv.InventoryDifference`)

La închiderea numărării (`CloseCounting`), S6 calculează, pe fiecare cheie, **diferența = faptic − scriptic**:

| Câmp | Calcul |
| --- | --- |
| `BookQty` | Din instantaneu (modurile 1 și 2) sau instantaneu + mișcările până la `CountedAt` (modul 3) |
| `CountedQty` | Cantitatea finală (3.3), în UM de bază |
| `DiffQty` | `CountedQty − BookQty` |
| `UnitCost` | Minus: costul ieșirii la data de referință, după metoda articolului (FIFO pe stratul CKU-ului, CMP curent). Plus: ultimul cost de intrare al gestiunii sau valoarea stabilită de comisie (`CostSource` din S2) |
| `DiffValue` | `DiffQty × UnitCost`. La gestiunile la preț de vânzare se afișează și valoarea la preț cu amănuntul (3.8) |

Mișcările din timpul inventarului (regula 6) se afișează pe cheie, fără să intre în diferență. Diferențele se închid cu stabilirea cauzei (3.5–3.7); comisia lucrează pe ecranul „Diferențe de inventar”, cu propunerile automate pre-completate.

### 3.5 Perisabilitățile (`inv.ShrinkageNorm`)

Perisabilitatea e pierderea naturală, în limitele legale sau aprobate, la depozitare și manipulare. Normele se definesc pe firmă:

| Câmp | Reguli |
| --- | --- |
| `CategoryId` sau `ItemId` | Normele pe articol au prioritate față de cele pe categorie |
| `WarehouseKind` | Opțional: altă normă în depozit și în magazin |
| `Percent` | Procentul maxim, de exemplu 0,10% |
| `Base` | 1 Ieșirile cantitative din perioadă (implicit) · 2 Stocul mediu din perioadă |
| `LegalReference` | Textul normei: HG 831/1997 sau normele interne aprobate de conducere |
| `ValidFrom`, `ValidTo` | Normele au istoric |

**Calculul:** `Admis = Percent × Base`, pe perioada de la `PreviousCountId` (sau de la prima intrare) până la data de referință. Propunerea automată aplică perisabilitatea **doar pe minusuri**, **doar până la mărimea minusului** și **niciodată pe plusuri**. Partea de minus peste norma admisă rămâne de stabilit ca imputabilă sau neimputabilă. Perisabilitatea se calculează pe SKU (toate CKU-urile lui din gestiune), apoi se repartizează pe CKU-urile cu minus.

Exemplu (INV-CJ 0031): zahăr tos `ZAH`, normă 0,10% din ieșiri; ieșiri de la inventarul anterior 9.600 kg, deci admis 9,6 kg. Minusul e 11,5 kg: 9,6 kg perisabilitate și 1,9 kg rămân de stabilit (au fost imputate gestionarului).

### 3.6 Compensările și corecțiile de lot

**Compensarea** acoperă un minus cu un plus la două sortimente diferite care se pot confunda ușor (același ambalaj, etichete asemănătoare). Regulile, din OMFP 2861/2009:

- Ambele sortimente sunt în aceeași grupă de confuzie (`inv.ConfusableGroup`), aprobată anual de conducere, cu decizia atașată. Altfel compensarea e refuzată (`54658`).
- Aceeași gestiune, același gestionar, același inventar.
- Se compensează aceeași cantitate (cea mai mică dintre minus și plus).
- Dacă valoarea minusului compensat e mai mare decât valoarea plusului compensat, **diferența de valoare se impută gestionarului** (3.7). Dacă e mai mică, nu se impută nimic.
- Propunerea automată împerechează minusurile și plusurile din aceeași grupă, în ordinea valorii unitare cea mai apropiată; comisia o poate modifica sau refuza.

Contabil, compensarea nu e o operație separată: minusul compensat iese cu `COUNT_MINUS`, plusul intră cu `COUNT_PLUS`, iar nota (C2) folosește motivul „compensare” (`607` = `371` și `371` = `607`, nu `7588`), fără TVA de ajustat.

**Corecția de lot** e cazul în care minusul și plusul sunt pe **același SKU**, pe CKU-uri diferite (de exemplu un bax pus pe raft cu eticheta lotului vecin). Nu e o compensare și nu are imputare: se face cu documentul `SL` (S4 §3.7), care mută cantitatea de pe un CKU pe altul cu același cost total. La FIFO cu straturi pe recepție, costul stratului mutat se păstrează. Seria găsită în altă gestiune se corectează la fel, cu un transfer între gestiuni înregistrat de comisie (S2), nu cu plus și minus.

### 3.7 Lipsuri imputabile și neimputabile (`inv.InventoryImputation`)

| Mod | Când | Valoarea | Contare tipică (C2) | TVA |
| --- | --- | --- | --- | --- |
| Perisabilitate | În limita normei (3.5) | Cost | `607` / `601` = `371` / `301` | Nu se ajustează |
| Compensare | 3.6 | Cost | `607` = `371` pentru minusul compensat și `371` = `607` pentru plusul compensat; efectul net e doar diferența de valoare, imputată | Nu |
| Imputabil | Vina unei persoane (gestionar, angajat, transportator) | Imputarea: **valoarea de înlocuire** (costul actual de achiziție + cheltuielile de aprovizionare) + TVA | `6588` = `371` la cost; `4282` = `7581` + `4427` pentru imputare | TVA colectată pe imputare |
| Neimputabil | Cauze dovedite: calamitate, furt cu dosar penal fără autor, degradare din cauze externe | Cost | `6588` = `371` | Ajustare `635` = `4426`/`4427`, cu excepțiile din Codul fiscal art. 304 (de exemplu calamitatea dovedită) |
| Plus | Faptic peste scriptic, rămas după compensare | Cost (3.4) | `371` = `7588` | — |

- Imputabilul cere persoana (`54664`) și se poate împărți între mai multe persoane, în procente. Pentru gestionari, valoarea de înlocuire se propune din ultimul preț de achiziție (Achiziții), modificabilă de comisie.
- Neimputabilul cere motivul și un document atașat (dosarul de furt, procesul-verbal de calamitate). Lipsurile neimputabile peste normă se marchează **nedeductibile** pentru impozitul pe profit (C4, Codul fiscal art. 25).
- Imputările nu se încasează în S6: evenimentul `ImputareInventar` creează creanța la Finanțe (`4282`) și, pentru angajați, propunerea de reținere la HR (Codul muncii, art. 254–259: reținerea lunară nu poate depăși o treime din salariul net).
- Gestiunile cu valoare netă a minusurilor peste `CountApprovalThreshold` (implicit 5.000 lei, ca la descărcări în S2) cer aprobarea directorului economic înainte de finalizare.

### 3.8 Situații speciale

| Situație | Ce face S6 |
| --- | --- |
| Inventar la predarea gestiunii (tipul 3) | Pornit din predarea S1 (§4.2), pe toată gestiunea, cu blocare. Minusurile neacoperite de perisabilități și compensări se propun imputabile gestionarului care predă. Finalizarea inventarului completează pasul din predare; predarea se finalizează în S1 (`54013` dacă lipsește inventarul) |
| Gestiune la preț de vânzare (magazin, `MAG-CJ`) | Se numără cantitativ și se evaluează la prețul cu amănuntul cu TVA din instantaneu. Înainte de instantaneu, S3 calculează descărcarea pe perioada până la data de referință (cu metoda K, `RetailDischargeMethod` 1). Minusul se înregistrează cu `607` + `378` + `4428` = `371`; imputarea, la prețul de vânzare |
| Gestiune de custodie (bunurile altora) | Se inventariază pe **listă separată** (OMFP 2861/2009), fără valoare contabilă (extrabilanțier `8033`). Diferențele nu produc mișcări de stoc proprii: se comunică proprietarului cu procesul-verbal, iar minusurile imputabile se tratează ca pentru bunurile proprii |
| Marfa altora găsită în gestiunea proprie (nefacturată, în consignație) | Se listează separat, nu intră ca plus |
| Rezervări pe marfa lipsă | `COUNT_MINUS` pe un CKU rezervat specific face rezervarea Neacoperită; o rezervare generică care depășește noul disponibil la fel (`RezervareNeacoperita`, S5) |
| Diferențe pe cost (FIFO, CMP) | `COUNT_MINUS` consumă straturile ca o ieșire; `COUNT_PLUS` creează un strat nou. O dată de referință în trecut declanșează recalcularea din S3 |
| Inventar anulat | Se anulează doar până la starea 4; deblochează gestiunea și nu lasă mișcări |
| Bunuri depreciate sau cu mișcare lentă | Comisia le marchează pe linie (motiv, procent propus); S6 trimite lista la C2 ca propunere de ajustare `397`, fără mișcare de stoc |

### 3.9 Finalizarea și procesul-verbal

Finalizarea (`FinalizeCount`), într-o tranzacție:

1. Verifică: toate diferențele stabilite (`54656`), perisabilitățile în normă (`54657`), compensările valide (`54658`), aprobarea peste prag, procesul-verbal semnat atașat (`54663`).
2. Generează documentul de diferențe `DI` (un antet, numerotat pe gestiune), cu liniile `COUNT_MINUS` și `COUNT_PLUS` la data de referință (regula 8), plus documentele `SL` pentru corecțiile de lot. Toate trec prin `inv.usp_StockMovement_Post`.
3. Publică `InventarFinalizat` și câte un `ImputareInventar` pe persoană; pentru tipul 3, completează pasul din predarea S1.

Documentele tipărite (formularele din OMFP 2634/2015 și conținutul din OMFP 2861/2009):

| Document | Conținut |
| --- | --- |
| Lista de inventariere (14-3-12) | Pe listă: cod, denumire, UM, cantitatea faptică, prețul unitar, valoarea; scripticul și diferențele doar pe exemplarul comisiei. Semnată de comisie și de gestionar |
| Lista separată pentru bunurile altora | Bunurile în custodie, consignație sau păstrare, pe proprietar |
| Declarațiile gestionarului | De început (3.1) și de sfârșit (nu mai are bunuri în afara celor inventariate, obiecții) |
| Procesul-verbal de inventariere | Comisia, perimetrul, data, totalurile, diferențele cu cauzele și propunerile, perisabilitățile, compensările, imputările, bunurile depreciate și expirate, observațiile gestionarului |

## 4. Fluxuri principale

### 4.1 Inventarul rotativ al băuturilor din DEP-CJ (INV-CJ 0031)

1. Simona Iordache creează `INV-CJ 0031` pe `DEP-CJ`, tip 2 rotativ, perimetrul categoria „Băuturi” și articolul `ZAH`, referință 26.11.2026 07:00, modul 2 (blochează doar articolele din perimetru). Comisia: Gheorghe Ilie (președinte), Simona Iordache; asistă gestionara Andreea Pop.
2. La pornire se fotografiază scripticul (8 chei) și se generează 2 liste. Numărarea e oarbă; scanarea GS1 dă CKU-urile.
3. Linia `APA05·L2611-07` iese 936 față de 960 (2,5%, peste prag): renumărare, confirmată 936. Se găsește lotul `L2611-04` de suc de portocale, care nu era în evidență.
4. La închiderea numărării: minus 24 buc `APA05` (L2611-07), plus 18 buc `APA05G` (L2610-20), minus și plus 12 buc la `SUC1` pe două loturi, minus 11,5 kg `ZAH`.
5. Propunerile: 18 buc compensate între apa plată și cea carbogazoasă (grupa „Apă 0,5 L PET”), diferența de valoare 0,45 lei imputată; restul de 6 buc perisabilitate; la `SUC1`, corecție de lot `SL`; la `ZAH`, 9,6 kg perisabilitate și 1,9 kg imputabil.
6. Procesul-verbal se tipărește, se semnează și se atașează; finalizarea generează `DI-CJ 0031` și `SL-CJ 0007`.

### 4.2 Inventarul de predare din DEP-TM (INV-TM 0012)

1. Predarea `PG 0007` (Cristian Mureșan → Florin Vasile) a pornit inventarul `INV-TM 0012`, tip 3, toată gestiunea, cu blocare de pe 23.11.2026, 18:00.
2. Șase liste pe zone; la 26.11 sunt numărate 1.046 din 1.214 variante. Transferul `TR 004512`, trimis înainte de blocare, se recepționează: marfa era pe drum (S1 §4.3) și apare la mișcările după referință.
3. La finalizare, minusurile neacoperite se impută lui Cristian Mureșan, iar predarea se finalizează în S1.

### 4.3 Seria găsită în altă gestiune

La inventarul din `DEP-CJ` se scanează dozatorul `DZ20-260655`, care în evidență e în tranzit (`NP 000819`, DC-PL → DEP-CJ). Linia apare ca „serie găsită în altă gestiune”; comisia confirmă recepția notei de transfer, nu un plus.

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `InventarPornit` v1 | Pornirea inventarului | `InventoryCountId`, `WarehouseId`, `CountType`, `ReferenceAt`, `FreezeMode`, perimetrul | WMS (blochează celulele), POS (avertizare), Notificări |
| `InventarFinalizat` v1 | Finalizarea | `InventoryCountId`, totalurile pe moduri (plus, minus, perisabilitate, compensare, imputabil, neimputabil), `DifferenceDocumentId` | C3 (registrul-inventar), Raportare, S1 (predarea) |
| `ImputareInventar` v1 | Finalizarea, câte unul pe persoană | `InventoryCountId`, `EmployeeId` sau `PartnerId`, valoarea fără TVA, TVA, motivul | Finanțe (creanța `4282`), HR (reținerea) |
| `PropunereDepreciere` v1 | Finalizarea, dacă există linii marcate | Liniile cu SKU, CKU, valoare, procent propus, motiv | C2 (ajustarea `397`) |

Mișcările `COUNT_PLUS` și `COUNT_MINUS` publică evenimentele obișnuite ale S2 (`MiscareStocValidata`), din care C2 face nota.

### 5.2 Consumate

| Eveniment | De la | Ce face S6 |
| --- | --- | --- |
| `PredareGestiunePornita` v1 | S1 (intern) | Creează inventarul de predare în ciornă, cu comisia propusă |
| `MiscareStocValidata` v1 | S2 (intern) | În modul 3, ține mișcările pentru scripticul la ora numărării; în modurile 1 și 2, le arată ca mișcări după referință |
| `NumarareCelula` v1 | WMS | Linii de numărare venite de pe terminalele WMS (alternativa la API) |

## 6. Contracte publice (partea S6)

S6 nu extinde `IStockLedger`: alte module nu creează inventare. Expune un port de citire și API-ul de numărare:

```csharp
public interface IInventoryCountQuery   // Erp.Core.Contracts
{
    Task<bool> IsKeyFrozenAsync(int warehouseId, int itemVariantId, DateTime at, CancellationToken ct);  // WMS, POS
    Task<IReadOnlyList<OpenCountDto>> GetOpenCountsAsync(int companyId, int? warehouseId, CancellationToken ct);
}

public enum InventoryCountType : byte { Annual = 1, Periodic = 2, Handover = 3, Unannounced = 4, Event = 5 }
public enum CountFreezeMode : byte { Warehouse = 1, ScopeItems = 2, None = 3 }
public enum InventoryCountStatus : byte { Draft = 1, Counting = 2, CountingClosed = 3, Settled = 4, Finalized = 5, Cancelled = 6 }
public enum DifferenceSettlement : byte { Shrinkage = 1, Compensation = 2, LotCorrection = 3, Chargeable = 4, NonChargeable = 5, Surplus = 6 }

public sealed record OpenCountDto(long InventoryCountId, string Number, int WarehouseId, InventoryCountType Type,
    DateTime ReferenceAt, CountFreezeMode FreezeMode, InventoryCountStatus Status);
```

API de numărare (pentru terminale și WMS), idempotent prin `ClientLineId`:

| Metodă | Ruta | Rol |
| --- | --- | --- |
| `GET` | `/api/inv/counts/{uid}/lists/{listNo}` | Lista de numărare (fără scriptic, dacă e oarbă) |
| `POST` | `/api/inv/counts/{uid}/lists/{listNo}/lines` | Una sau mai multe numărări: cod scanat sau SKU + lot + termen, serie, UM, cantitate, celula WMS, runda, `ClientLineId` |
| `POST` | `/api/inv/counts/{uid}/lists/{listNo}/close` | Echipa închide lista |

Comenzi interne S6: `SaveCount`, `SaveCommission`, `StartCount`, `GenerateLists`, `RecordCountLine`, `RequestRecount`, `CloseCounting`, `ProposeSettlement`, `SettleDifference`, `SaveCompensation`, `ApproveCount`, `FinalizeCount`, `CancelCount`, `SaveShrinkageNorm`, `SaveConfusableGroup`.

## 7. Schema SQL `inv` (partea S6)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `inv.InventoryCount` | `Id bigint` | UX `(CompanyId, WarehouseId, Number)`; UX `Uid`; FK `WarehouseId`, `HandoverId`, `PreviousCountId`, `DifferenceDocumentId` | `(WarehouseId, Status) WHERE Status IN (1, 2, 3, 4)` |
| `inv.InventoryCountScope` | `Id int` | FK `InventoryCountId`; câte una din `CategoryId`, `BinPrefix` (zonă WMS), `ItemVariantId` | `(InventoryCountId)` |
| `inv.InventoryCommissionMember` | `Id int` | UX `(InventoryCountId, EmployeeId)`; `CHECK (Role IN (1, 2, 3))` (președinte, membru, asistă) | — |
| `inv.InventorySnapshot` | `Id bigint` | UX `(InventoryCountId, ItemVariantId, LotKey, SerialKey, BinKey)` | — |
| `inv.InventoryList` | `Id int` | UX `(InventoryCountId, ListNo)` | — |
| `inv.InventoryCountLine` | `Id bigint` | UX `(InventoryListId, ClientLineId)`; FK `ItemVariantId`, `LotId`, `SerialId`, `UomId`; `BinCode` din WMS | `(InventoryCountId, ItemVariantId, LotKey, SerialKey, Round)`; UX `(InventoryCountId, SerialId, Round) WHERE SerialId IS NOT NULL` |
| `inv.InventoryDifference` | `Id bigint` | UX `(InventoryCountId, ItemVariantId, LotKey, SerialKey, BinKey)` | `(InventoryCountId) INCLUDE (DiffQty, DiffValue)` |
| `inv.InventoryCompensation` | `Id int` | FK `MinusDifferenceId`, `PlusDifferenceId`, `ConfusableGroupId` | — |
| `inv.InventoryImputation` | `Id int` | FK `InventoryDifferenceId`, `EmployeeId` / `PartnerId` | `(InventoryCountId)` |
| `inv.ShrinkageNorm` | `Id int` | UX `(CompanyId, CategoryKey, ItemKey, WarehouseKindKey, ValidFrom)` | — |
| `inv.ConfusableGroup`, `inv.ConfusableGroupItem` | `Id int` | UX `(CompanyId, Code)`; UX `(ConfusableGroupId, ItemVariantId)` | `(ItemVariantId)` |
| `inv.StockSettings` (coloane noi) | — | `CountBlind bit NOT NULL DEFAULT 1`, `CountRecountPct decimal(5,2) NOT NULL DEFAULT 2`, `CountRecountValue decimal(19,2) NOT NULL DEFAULT 500`, `CountApprovalThreshold decimal(19,2) NULL DEFAULT 5000` | — |
| `inv.Warehouse` (coloană nouă) | — | `CountFreezeMode tinyint NULL` (suprascrierea setării) | — |

`LotKey` și `SerialKey` sunt coloane calculate persistate `ISNULL(…, 0)`, ca în `inv.Stock`; `BinKey` e `ISNULL(BinCode, N'')`. Celula (`BinCode`) există doar când depozitul folosește WMS (02.06): zonele și rafturile nu sunt în Stocuri (S1). La nivelul de evidență „depozit” (S1 §3.8), inventarul se face pe (depozit, gestiune), iar cheile au și `DepotId`.

Exemplu de tabel complet:

```sql
CREATE TABLE inv.InventoryDifference (
    Id                     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryDifference PRIMARY KEY,
    InventoryCountId       bigint         NOT NULL CONSTRAINT FK_InventoryDifference_Count REFERENCES inv.InventoryCount(Id),
    ItemVariantId          int            NOT NULL CONSTRAINT FK_InventoryDifference_ItemVariant REFERENCES core.ItemVariant(Id),
    LotId                  int            NULL CONSTRAINT FK_InventoryDifference_Lot REFERENCES inv.Lot(Id),
    SerialId               int            NULL CONSTRAINT FK_InventoryDifference_Serial REFERENCES inv.Serial(Id),
    BinCode                nvarchar(40)   NULL,          -- celula WMS (02.06), cheie logică, fără FK între module
    LotKey                 AS ISNULL(LotId, 0) PERSISTED,
    SerialKey              AS ISNULL(SerialId, 0) PERSISTED,
    BinKey                 AS ISNULL(BinCode, N'') PERSISTED,
    BookQty                decimal(19,6)  NOT NULL,
    CountedQty             decimal(19,6)  NOT NULL,
    DiffQty                AS (CountedQty - BookQty) PERSISTED,
    UnitCost               decimal(19,6)  NOT NULL,
    SalePrice              decimal(19,4)  NULL,          -- gestiuni la preț de vânzare
    DiffValue              decimal(19,2)  NOT NULL,
    ShrinkageQty           decimal(19,6)  NOT NULL CONSTRAINT DF_InventoryDifference_Shrinkage DEFAULT (0),
    CompensatedQty         decimal(19,6)  NOT NULL CONSTRAINT DF_InventoryDifference_Compensated DEFAULT (0),
    LotCorrectionQty       decimal(19,6)  NOT NULL CONSTRAINT DF_InventoryDifference_LotCorrection DEFAULT (0),
    ChargeableQty          decimal(19,6)  NOT NULL CONSTRAINT DF_InventoryDifference_Chargeable DEFAULT (0),
    NonChargeableQty       decimal(19,6)  NOT NULL CONSTRAINT DF_InventoryDifference_NonChargeable DEFAULT (0),
    NonChargeableReason    nvarchar(400)  NULL,
    IsDepreciationProposal bit            NOT NULL CONSTRAINT DF_InventoryDifference_Depr DEFAULT (0),
    MovementId             bigint         NULL CONSTRAINT FK_InventoryDifference_Movement REFERENCES inv.StockMovement(Id),
    CreatedAt              datetime2(3)   NOT NULL CONSTRAINT DF_InventoryDifference_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int            NOT NULL,
    ModifiedAt             datetime2(3)   NULL,
    ModifiedBy             int            NULL,
    RowVersion             rowversion     NOT NULL,
    CONSTRAINT CK_InventoryDifference_Qty CHECK (BookQty >= 0 AND CountedQty >= 0),
    CONSTRAINT CK_InventoryDifference_Parts CHECK (ShrinkageQty >= 0 AND CompensatedQty >= 0 AND LotCorrectionQty >= 0
        AND ChargeableQty >= 0 AND NonChargeableQty >= 0),
    CONSTRAINT CK_InventoryDifference_Serial CHECK (SerialId IS NULL OR (BookQty IN (0, 1) AND CountedQty IN (0, 1)))
);
CREATE UNIQUE INDEX UX_InventoryDifference_Key ON inv.InventoryDifference (InventoryCountId, ItemVariantId, LotKey, SerialKey, BinKey);
```

Regula „suma modurilor = minusul” (regula 7) se verifică în `usp_InventoryCount_Finalize`, nu prin `CHECK`, pentru că liniile se stabilesc treptat.

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `inv.usp_InventoryCount_Start` | Verificări, instantaneu, blocare, liste | Nu |
| `inv.usp_InventoryCountLine_Record` | Numărarea (idempotentă pe `ClientLineId`), cu validarea lotului, seriei și UM | Nu |
| `inv.usp_InventoryCount_CloseCounting` | Verifică zero confirmat și renumărările, calculează diferențele, deblochează (modul 1) | Nu |
| `inv.usp_InventoryCount_ProposeSettlement` | Corecții de lot, compensări, perisabilități, imputabil propus (3.5–3.7) | Nu |
| `inv.usp_InventoryCount_Finalize` | Verificări, documentul `DI`, `SL`, mișcările prin `usp_StockMovement_Post`, imputările | Nu |
| `inv.usp_InventoryCount_Cancel` | Anularea și deblocarea | Nu |
| `inv.usp_InventoryCount_IsKeyFrozen` | Verificarea folosită de `usp_StockMovement_Post` și de port | Da (citire) |

Coduri de eroare (plaja S6 `54650–54799`, prefix API `INV_`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 54651 | `INV_COUNT_SCOPE_OVERLAP` | 409 | Alt inventar deschis acoperă același SKU în aceeași gestiune |
| 54652 | `INV_COUNT_WAREHOUSE_INVALID` | 422 | Inventar pe tranzit sau pe o gestiune inactivă |
| 54653 | `INV_COUNT_STATUS_INVALID` | 409 | Operație nepermisă în starea inventarului |
| 54654 | `INV_COUNT_LINES_INCOMPLETE` | 409 | Închiderea numărării cu linii fără cantitate și fără zero confirmat |
| 54655 | `INV_COUNT_RECOUNT_PENDING` | 409 | Închiderea numărării cu renumărări nefăcute |
| 54656 | `INV_COUNT_DIFF_UNSETTLED` | 409 | Finalizare cu minusuri fără cauză sau cu suma modurilor diferită de minus |
| 54657 | `INV_COUNT_SHRINKAGE_EXCEEDED` | 409 | Perisabilitate peste norma admisă sau aplicată pe plus |
| 54658 | `INV_COUNT_COMPENSATION_INVALID` | 409 | Compensare în afara unei grupe de confuzie, între gestiuni sau cu cantități diferite |
| 54659 | `INV_COUNT_COMMISSION_INVALID` | 422 | Comisie fără președinte sau membru, ori gestionar pus membru |
| 54660 | `INV_COUNT_SERIAL_DUPLICATE` | 409 | Serie numărată de două ori în aceeași rundă |
| 54661 | `INV_COUNT_LOT_EXPIRY_CONFIRM` | 409 | Lot cunoscut scanat cu alt termen; cere confirmare |
| 54662 | `INV_COUNT_PERIOD_CLOSED` | 409 | Data de referință într-o lună închisă, fără drept |
| 54663 | `INV_COUNT_ATTACHMENT_REQUIRED` | 422 | Finalizare fără procesul-verbal semnat sau fără decizie |
| 54664 | `INV_COUNT_IMPUTATION_PERSON_REQUIRED` | 422 | Imputabil fără persoană sau cu procente care nu fac 100% |
| 54665 | `INV_COUNT_APPROVAL_REQUIRED` | 409 | Finalizare peste prag fără aprobare |
| 54666 | `INV_COUNT_ITEM_FROZEN` | 409 | Mișcare pe un articol înghețat de inventar (modul 2) |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterea:

- Abatere de la `SQL-11` pentru `inv.InventorySnapshot` și `inv.InventoryCountLine`: fără `RowVersion`, sunt doar de adăugare (o renumărare e o rundă nouă, nu o modificare). Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Inv.Count.View` | Inventarele, listele și diferențele (fără scriptic în timpul numărării oarbe) |
| `Inv.Count.Manage` | Crearea, comisia, perimetrul, pornirea, anularea |
| `Inv.Count.Enter` | Introducerea numărărilor pe listele echipei |
| `Inv.Count.ViewBook` | Scripticul în timpul numărării |
| `Inv.Count.Settle` | Stabilirea cauzelor: perisabilități, compensări, imputări |
| `Inv.Count.Approve` | Aprobarea peste prag |
| `Inv.Count.Finalize` | Finalizarea și generarea mișcărilor |
| `Inv.Count.Norms` | Normele de perisabilitate și grupele de confuzie |

Roluri livrate: **Numărător** (`Enter`), **Membru comisie** (`View`, `Enter`, `Settle`), **Contabil stocuri** (toate, fără `Approve`), **Director economic** (`View`, `ViewBook`, `Approve`), **Gestionar** (`View` pe propriile gestiuni, după închiderea numărării).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru S6 |
| --- | --- |
| Legea 82/1991, art. 7 | Inventarierea cel puțin o dată pe an, la predarea-primirea gestiunii, la fuziuni și la calamități (tipurile 1, 3, 5) |
| OMFP 2861/2009 | Comisia prin decizie, gestionarul nu e membru, declarațiile gestionarului, liste separate pentru bunurile altora, compensarea doar între sortimente confundabile, imputarea la valoarea de înlocuire, procesul-verbal |
| OMFP 2634/2015 | Formularele: lista de inventariere (14-3-12), registrul-inventar (14-1-2, în C3) |
| OMFP 1802/2014 | Contarea plusurilor, minusurilor și imputărilor (C2) |
| HG 831/1997 | Normele-limită de perisabilitate pentru mărfuri; normele interne pentru restul |
| Codul fiscal, art. 25 și 304 | Perisabilitățile în limite sunt deductibile și fără ajustare TVA; lipsurile neimputabile fără dovadă sunt nedeductibile și cer ajustarea TVA |
| Codul muncii, art. 254–259; Legea 22/1969 | Răspunderea patrimonială a gestionarului și limita reținerii (HR) |

Integrări: terminale mobile și WMS prin API (6); cântare prin integrarea din Nucleu. Interne: S1 (blocarea, predarea), S2 (mișcările, documentul `DI`), S3 (costul, recalcularea, descărcarea la amănunt), S4 (CKU, serii, `SL`, `NEIDENTIFICAT`), S5 (rezervări neacoperite), C2 (note, ajustări), C3 (registrul-inventar), C4 (nedeductibile), Finanțe (creanțe), HR (rețineri).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Situația inventarelor | Pe gestiune: ultimul inventar, data, rezultatul, gestiunile fără inventar în anul curent | Contabil șef, Auditor |
| Diferențe de inventar | Pe inventar, SKU, mod de stabilire: cantități și valori | Comisia, Contabil stocuri |
| Perisabilități | Admis față de consumat, pe norme și gestiuni | Director economic |
| Imputări | Pe persoană: valoarea, TVA, starea recuperării (din Finanțe) | HR, Contabil |
| Precizia stocului | Procentul de linii fără diferențe și valoarea absolută a diferențelor, pe gestiune și în timp | Șef depozit, Director operațional |

## 11. Ecrane principale

Machetele (HTML + PNG) sunt în [ecrane/s6-inventariere/](ecrane/s6-inventariere/), în meniul Stocuri › Inventar.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Inventar | Lista inventarelor și fișa inventarului de predare `INV-TM 0012`: comisia, blocarea, progresul pe liste, pașii | `s6-01-inventare` |
| Listă de numărare | O listă a inventarului `INV-TM 0012`, oarbă: scanări pe CKU și serie, UM de numărare, renumărare, lot găsit, zero confirmat | `s6-02-lista-numarare` |
| Diferențe de inventar | Diferențele `INV-CJ 0031` cu modul de stabilire pe fiecare linie și totalurile pe moduri | `s6-03-diferente` |
| Perisabilități și compensări | Calculul normei pe `ZAH` și `APA05`, compensarea apă plată / carbogazoasă cu diferența imputată | `s6-04-perisabilitati-compensari` |
| Finalizare și proces-verbal | Verificările, documentele generate (`DI`, `SL`), imputările, semnăturile, notele propuse | `s6-05-proces-verbal` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.05-S6-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| S6-A1 | După finalizare, stocul fiecărei chei din perimetru e egal cu faptic + mișcările validate după data de referință | Test de proprietate |
| S6-A2 | Două inventare deschise nu pot acoperi același SKU în aceeași gestiune (`54651`) | Test de integrare |
| S6-A3 | În modul 1 gestiunea e blocată de la referință la închiderea numărării; în modul 2 doar articolele din perimetru (`54666`); bonurile POS se acceptă în ambele | Test de integrare (cu S1, S2) |
| S6-A4 | Numărarea oarbă nu întoarce scripticul prin API sau ecran fără `Inv.Count.ViewBook` | Test de securitate |
| S6-A5 | Linia `APA05·L2611-07` (960 → 936, 2,5%) cere renumărare; închiderea fără ea e refuzată (`54655`) | Test de integrare |
| S6-A6 | Perisabilitatea la `ZAH`: admis 9,6 kg din 9.600 kg ieșiri, aplicat 9,6 kg pe minusul de 11,5 kg, restul 1,9 kg cere cauză (`54656`); pe plus e refuzată (`54657`) | Test unitar |
| S6-A7 | Compensarea `APA05` ↔ `APA05G` pe 18 buc: diferența 0,45 lei se impută gestionarei; compensarea cu un articol din afara grupei e refuzată (`54658`) | Test unitar |
| S6-A8 | Minusul și plusul pe același SKU și loturi diferite produc un `SL`, nu `COUNT_MINUS` + `COUNT_PLUS` | Test de integrare (cu S4) |
| S6-A9 | Finalizarea generează mișcările la data de referință, cu costul S3, iar nota C2 bate cu totalurile procesului-verbal | Test de integrare (cu S2, S3, C2) |
| S6-A10 | Seria scanată de două ori e refuzată (`54660`); seria din altă gestiune nu devine plus | Test de integrare (cu S4) |
| S6-A11 | Inventarul de predare finalizat completează pasul din predare, iar predarea fără el e refuzată (`54013`) | Test de integrare (cu S1) |
| S6-A12 | 20.000 de linii de numărare se înregistrează prin API în sub 60 de secunde, iar retrimiterea aceleiași linii nu o dublează | Test de performanță și idempotență |

Criteriile S6-A1, S6-A3 și S6-A9 sunt critice: un inventar care nu se închide corect strică stocul, costul și concordanța cu `371`.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **Componenta de scanare** (pagina responsivă cu ZXing, modul offline cu deduplicare pe `ScanId`) e comună cu inventarierea mijloacelor fixe ([M7 §3.2](../15-mijloace-fixe/M7-INVENTARIERE-RAPOARTE.md)); se livrează o singură dată, în proiectul comun de UI.
- **Plusul de obiecte de inventar găsit la inventarierea M7** intră în stoc prin plusul de inventar S6 / S2 și se dă apoi în folosință prin bonul de consum.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Ce se blochează în timpul inventarului | Implicit toată gestiunea, de la data de referință până la închiderea numărării (nu până la finalizare). Ca parametru, pe firmă și pe gestiune: doar articolele din inventar (inventare rotative) sau fără blocare (depozite mari cu WMS) | 2 (regula 5) | Decis (26.11.2026) |
| 2 | Numărarea oarbă | Implicit da: numărătorii nu văd scripticul, diferențele apar după închiderea numărării; cine are dreptul special îl vede | 2 (regula 4), 3.2 | Decis (26.11.2026) |
| 3 | Când se renumără | Automat peste 2% sau peste 500 lei pe linie (setări pe firmă), de altă echipă; dacă runda a doua diferă de prima, decide a treia, cu președintele comisiei | 3.3 | Decis (26.11.2026) |
| 4 | Cum se numără articolele urmărite și cele neurmărite | Pe CKU (scanare GS1) la loturi, pe fiecare serie la serii, doar cantitativ la cele neurmărite (piulițe, cărămizi), cu numărare în orice UM și cântărire la vrac; lotul găsit cu alt termen cere confirmare, marfa fără lot merge pe `NEIDENTIFICAT` | 3.2 | Decis (26.11.2026) |
| 5 | Perisabilitățile | Norme pe categorie sau articol, ca procent din ieșirile de la inventarul anterior; aplicate automat doar pe minusuri, doar până la mărimea minusului, niciodată pe plusuri | 3.5 | Decis (26.11.2026) |
| 6 | Compensările | Doar între sortimente din grupele de confuzie aprobate, aceeași gestiune și același inventar, cantitate egală; diferența de valoare în minus se impută gestionarului. Minus și plus pe același SKU, loturi diferite = corecție de lot `SL`, nu compensare | 3.6 | Decis (26.11.2026) |
| 7 | Imputările | Lipsurile imputabile la valoarea de înlocuire plus TVA, cu creanța la Finanțe și reținerea la HR; neimputabilele cer motiv și document, cu cheltuiala nedeductibilă și TVA ajustată unde legea o cere | 3.7 | Decis (26.11.2026) |
| 8 | Data înregistrării diferențelor | La data de referință a inventarului, chiar dacă finalizarea e mai târziu; dacă luna e închisă, în prima lună deschisă, cu avertisment | 2 (regula 8) | Decis (26.11.2026) |

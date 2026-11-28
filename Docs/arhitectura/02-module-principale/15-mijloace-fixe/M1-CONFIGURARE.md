# ERP · 02.15 Mijloace fixe și obiecte de inventar · M1 Configurare: clasificare, regimuri, amortizare, conturi și tranzacții

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: din 27.11.2026 Aurelian a cerut lucru autonom, fără validare pe fiecare parte, așa că deciziile 1–14 de la final au fost adoptate pe recomandare, iar precizările în celelalte documente au fost aplicate. Prima din cele 7 părți ale modulului 02.15. Modulul a fost adăugat pe 27.11.2026, la cererea lui Aurelian, după ce în schița inițială mijloacele fixe erau doar o funcție a Finanțelor. Împărțirea M1–M7, compunerea și descompunerea (adăugate de Aurelian la M4), pragul de 5.000 lei și preluările din alte ERP-uri ([INSPIRATIE_WINDOWS_CLIENT.md](INSPIRATIE_WINDOWS_CLIENT.md), P1–P30) au fost confirmate de Aurelian pe 27.11.2026 („da e ok”).

Modulul 02.15 are schema SQL `fa`, prefixul de cod `Erp.FixedAssets` și plaja de erori SQL `65000–65999` (plaja `64000–64999` e a schemei `ids`, Bune practici 8.3). Această parte acoperă configurarea pe care se sprijină restul modulului: tipurile de documente și tranzacțiile livrate, grupurile de consum, catalogul de clasificare HG 2139/2004, grupele de mijloace fixe ale firmei cu conturile lor, regimurile de amortizare (contabil, fiscal, opțional IFRS), profilurile și metodele de amortizare, pragul de încadrare, seriile numerelor de inventar și etichetele, nomenclatoarele modulului și setările firmei. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.1 Firme și locații, §3.3 Articole, §3.4 Dimensiuni, §3.5 Nomenclatoare și proprietăți, §3.6 Tranzacții și legături, serii de numerotare), [02.02 Finanțe](../02-finante-contabilitate/README.md) (C1 planuri de conturi, C2 modele de contare și chei de cont, C3 închiderea lunii, C4 D101 și D406), [02.04 Achiziții](../04-achizitii/README.md) (A4 NIR, A5 linia de imobilizare `NonStockAssetKind`), [02.05 Stocuri](../05-stocuri/README.md) (S2 bonul de consum pentru obiectele de inventar), [02.03 Vânzări](../03-vanzari/README.md) (V5 factura de vânzare). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`); căutările fără diacritice folosesc coloane calculate cu `Latin1_General_100_CI_AI_SC_UTF8` (`SQL-25`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Un mijloc fix e o fișă, nu stoc.** Fiecare bun are fișa lui (`fa.Asset`), cu număr de inventar unic în firmă, cantitate 1 și valori proprii. Articolul din Nucleu (`core.Item` cu `ItemType` = mijloc fix) descrie doar ce s-a cumpărat; fișa descrie bunul concret. O linie de factură cu 5 laptopuri dă 5 fișe (M2).
- **Valorile se țin pe regimuri** (`AssetBook`): **contabil** (`BOOK`, singurul contat), **fiscal** (`TAX`, necontat, folosit pentru amortizarea fiscală și diferențele din D101) și, opțional pe firmă, **IFRS** (`IFRS`, necontat, raportat prin planurile alternative C1). Fiecare regim are metoda, durata, valoarea de intrare, amortizarea și deprecierea lui.
- **Obiectele de inventar** (bunuri sub prag sau cu durată sub un an) sunt stoc cât stau în magazie (Stocuri, conturile `303` / `3xx`). La darea în folosință, bonul de consum din Stocuri S2 le trece pe cheltuială (`603` = `303`), iar acest modul deschide **evidența de folosință** pe persoană și locație, ținută extrabilanțier (`8039`). Obiectele în folosință nu mai sunt stoc (decizia aprobată pe 27.11.2026, detaliată în M6).
- **Tipul de tranzacție** (`core.TransactionType`) și **legăturile între documente** (`core.DocumentLink`, cu grupul de consum și cantitatea pe linie) sunt definite în Nucleu §3.6 și sunt deja folosite de Achiziții și Vânzări. Comportamentul fiecărei tranzacții e scris în cod (`ITransactionBehavior`). Clientul poate crea tranzacții proprii doar pe comportamentele livrate (altă denumire, alte drepturi, alte valori implicite, alte semnale pentru contare). Nu poate scrie SQL.
- **Contarea nu se face în modul.** Documentele publică evenimente cu blocul `Accounting` (C2 §3.1), cu componente noi (`AssetCost`, `Depreciation`, `Impairment`, `RevaluationSurplus`...) și cu `AssetGroupId` pe componentă; conturile se rezolvă în C2 prin cheile de cont noi din 3.9. Regimul fiscal și cel IFRS nu produc note.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`). Valorile pe `decimal(19,4)`, rotunjite la 2 zecimale pe linie și pe document (`SQL-07`); cotele lunare de amortizare se rotunjesc la 2 zecimale, iar diferența de rotunjire se închide în ultima lună de amortizare (3.6).
- **Ce NU face M1:** nu creează fișe și nu înregistrează intrări (M2), nu calculează amortizarea (M3), nu face operații pe mijloace fixe (M4), ieșiri (M5) sau evidența obiectelor în folosință (M6) și nu face inventarierea (M7). M1 definește doar configurarea pe care o citesc celelalte părți.

## 1. Scop și perimetru

| Zonă | Ce face M1 | Ce NU face (și cine face) |
| --- | --- | --- |
| Perimetrul modulului | Imobilizări corporale (`21x`), imobilizări necorporale amortizabile (`20x`: licențe, programe, brevete, cheltuieli de dezvoltare), investiții în curs (`231`, `235`), obiecte de inventar în folosință | Imobilizări financiare (`26x`, `267`: rămân în Finanțe); stocurile de obiecte de inventar din magazie (Stocuri); bunurile luate în leasing operațional (doar evidență extrabilanțieră, M2) |
| Tipuri de documente | Lista documentelor modulului, seriile implicite, stările comune și partea care detaliază fiecare tip | Ciclul de viață al fiecărui document (M2–M7) |
| Tranzacții | Catalogul tranzacțiilor livrate, cu sursele, grupul de consum și efectul | Motorul de tranzacții și legături (Nucleu §3.6) |
| Clasificare legală | Catalogul HG 2139/2004 (grupe, subgrupe, clase, durata normală minimă și maximă), cu seed și actualizare | Încadrarea unui bun concret (M2) |
| Grupe de mijloace fixe | Grupele firmei, cu conturile de imobilizare, amortizare, depreciere și reevaluare, regimurile și profilurile implicite | Planul de conturi (C1); rezolvarea contului (C2 §3.4) |
| Regimuri și metode | Regimurile de amortizare ale firmei, profilurile de amortizare (metodă, coeficient, convenție de start, rotunjire), restricțiile legale pe categorii | Planul de amortizare al fiecărui bun și calculul lunar (M3) |
| Prag de încadrare | Valoarea limită pe firmă, cu istoric și dată de aplicare (acum 5.000 lei) | Decizia pe fiecare linie de intrare (M2) |
| Numere de inventar | Seriile numerelor de inventar, formatul etichetelor (cod de bare, QR) | Tipărirea etichetelor și scanarea (M2, M7) |
| Nomenclatoare | Moduri de obținere, motive (modificare de valoare, conservare, ieșire, casare), stări fizice, proprietățile tehnice pe grupă | Nomenclatoarele comune (Nucleu: locații, angajați, centre de cost) |
| Setări | Setările de mijloace fixe ale firmei: regimuri active, crearea fișelor din facturi, ziua de rulare a amortizării, conservarea, reevaluarea, componentele, obiectele în folosință | Închiderea lunii (C3, care doar cere amortizarea contată) |

## 2. Model de domeniu

```
 Nucleu (comun tuturor modulelor)
   TransactionType ── TransactionTypeSource × N ── TransactionTypeRole × N     (§3.6)
   DocumentLink · DocumentLineClosure · DocumentStatusLog                      (§3.6)
   Item (ItemType = mijloc fix) · Location · Employee · DimensionValue (centru de cost)
   NumberSeries (tip nou: AssetInventoryNumber, InUseRecordNumber)
   PropertyDefinition (EntityType nou: Asset)

 Mijloace fixe M1 (fa)
   AssetClassification (catalogul HG 2139/2004, arbore: grupă → subgrupă → clasă, DNU min–max)   ← la nivel de instalare, seed
   DepreciationProfile (metodă, coeficient, convenție, rotunjire)                                  ← la nivel de instalare, livrate + proprii
   AssetBookType (BOOK, TAX, IFRS)                                                                 ← închis, livrat
   AssetGroup (per firmă: natura, conturi, catalog implicit, profil pe regim, politici)
      └─ AssetGroupBook × N (regim × grupă: profil implicit, permis / interzis)
   AssetThreshold (per firmă, istoric: valoare limită, de la data)
   FixedAssetSettings (1 rând pe firmă)
   Nomenclatoare: AcquisitionMode · AssetReason (Kind) · AssetCondition · LabelFormat

 Documente (detaliate în M2–M7), fiecare cu tranzacțiile lui
   AssetIntake → AssetCommissioning (PIF) → DepreciationRun (lunar)
   ConstructionInProgress (231) → AssetIntake (decontare)
   AssetTransfer · AssetValueChange (modernizare, depreciere, reevaluare, durată) · AssetConservation
   AssetRestructure (compunere, descompunere) · AssetDisposal
   InUseIssue · InUseTransfer · InUseReturn · InUseWriteOff (obiecte de inventar în folosință)
   AssetCount (inventariere)
```

Reguli de business ale modelului:

1. **Configurarea contabilă e per firmă, clasificarea legală e comună.** Catalogul HG 2139/2004 și profilurile de amortizare sunt la nivel de instalare (aceleași reguli pentru toate firmele din grup). Grupele de mijloace fixe, conturile, pragul, seriile și setările au `CompanyId`: două firme pot ține laptopurile pe conturi diferite sau cu alte durate.
2. **Un mijloc fix aparține unei singure grupe și unei singure clase din catalog.** Grupa dă conturile și valorile implicite (profilul pe regim, durata propusă, componentele, eticheta); clasa dă intervalul legal al duratei. Schimbarea grupei după prima amortizare e o reclasificare (document M4, cu notă între conturi), nu o editare a fișei.
3. **Regimul contabil e singurul contat.** Fiscalul și IFRS-ul se calculează în paralel, cu aceeași cronologie de documente (aceeași intrare, același PIF, aceeași ieșire), dar cu metodă, durată și valori proprii. Orice document care schimbă valoarea spune pe ce regimuri acționează (implicit pe toate cele active; reevaluarea contabilă nu atinge regimul fiscal, 4.5).
4. **Durata de amortizare se alege în intervalul clasei.** Pe regimul fiscal, durata trebuie să fie între minimul și maximul clasei din catalog (Codul fiscal art. 28); în afara intervalului doar pentru bunurile second-hand sau preluate cu amortizare anterioară, cu motiv (M2). Pe regimul contabil, durata e „durata de utilizare economică” și poate ieși din interval, cu motiv și drept (`FixedAssets.UsefulLife.OutOfRange`).
5. **Pragul se aplică la data intrării.** Încadrarea ca mijloc fix sau obiect de inventar se decide după valoarea de intrare fără TVA și pragul în vigoare la data documentului de intrare (3.7). Fișele existente nu se reîncadrează când se schimbă pragul.
6. **Numerele de inventar nu se refolosesc.** Numărul se alocă la validarea intrării, e unic în firmă pentru tot istoricul și rămâne pe fișă și după ieșire. Componentele primesc numărul părintelui cu sufix (`MF 000412/1`).
7. **Tranzacțiile proprii ale clientului sunt variante ale celor livrate** (ca la Achiziții și Vânzări): aleg un comportament livrat și moștenesc sursele și efectul.
8. **Nimic din configurare nu se șterge după folosire** (`SQL-20`): o grupă, un profil, o clasă din catalog sau o valoare de nomenclator folosită se dezactivează. Clasele abrogate din catalog rămân pe fișele vechi, marcate „abrogată”.

## 3. Entități și setări

Convențiile comune tuturor tabelelor `fa`: cheie `Id` `int IDENTITY` pentru nomenclatoare și `bigint` pentru tabelele care cresc (`SQL-13`), coloanele de audit `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` (`SQL-19`) și `RowVersion` pe tot ce se editează din interfață (`SQL-11`, `SP-18`). Codurile se compară prin coloane `CodeKey` calculate (`SQL-24`).

### 3.1 Tipurile de documente ale modulului

Codul tipului de document e același în `core.NumberSeries`, în `core.TransactionType`, în `core.DocumentLink` și în `AccountingPayload` (C2).

| `DocumentType` | Document | Serie implicită | Contare (regimul contabil) | Detaliat în |
| --- | --- | --- | --- | --- |
| `AssetIntake` | Intrare de mijloc fix: creează una sau mai multe fișe și le dă valoarea de intrare (din factură, NIR, investiție în curs, producție proprie, aport, donație, plus la inventar, leasing financiar, preluare inițială) | `MFI` | Da: `21x` / `20x` = `404` / `231` / `722` / `1012` / `4753` ... | M2 |
| `ConstructionInProgress` | Investiție în curs (`231`, `235`): fișa care acumulează facturi, consumuri din stoc și manoperă până la decontarea pe mijloace fixe | `INC` | Da, pe fiecare adăugare: `231` = `404` / `3xx` / `722` | M2 |
| `AssetCommissioning` | Proces-verbal de recepție și punere în funcțiune (PIF): fixează data de început a amortizării | `PIF` | Nu (doar trecerea din „în curs de punere în funcțiune” în „în funcțiune”) | M2 |
| `DepreciationRun` | Fișa de amortizare lunară: amortizarea lunii pe toate mijloacele fixe și regimurile firmei | `AML` | Da: `6811` = `28xx` (doar `BOOK`) | M3 |
| `AssetTransfer` | Bon de mișcare: schimbarea locației, a responsabilului, a centrului de cost sau a departamentului, pentru unul sau mai multe mijloace fixe | `BMF` | Doar dacă se schimbă centrul de cost și firma contează amortizarea pe centre de cost (fără notă de transfer, doar dimensiunea de pe amortizarea următoare) | M4 |
| `AssetValueChange` | Modernizare, depreciere și reluarea ei, reevaluare (ocazională sau în masă), schimbare de durată, reclasificare între grupe | `MVM` | Da, după felul operației | M4 |
| `AssetConservation` | Punere în conservare și repunere în funcțiune | `CNS` | Nu (suspendă sau reia amortizarea pe regimurile din setare, 3.11) | M4 |
| `AssetRestructure` | Compunere (mai multe fișe → una) și descompunere (o fișă → mai multe), cu PV | `CDM` | Da, doar reclasificare între `21x` / `28xx` dacă grupele diferă; fără efect în rezultat | M4 |
| `AssetDisposal` | Ieșire: vânzare (legată de factura din Vânzări), casare, lipsă la inventar, donație, aport, transfer în grup, trecere în obiecte de inventar | `IMF` | Da: `28xx` = `21x`, `6583` = `21x`, `105` = `1175` | M5 |
| `InUseIssue` | Dare în folosință a obiectelor de inventar (generată din bonul de consum S2, sau preluare inițială) | `OBF` | Doar extrabilanțier (`8039`, evidența de folosință) | M6 |
| `InUseTransfer` | Transfer de obiecte de inventar între persoane sau locații | `OBT` | Nu | M6 |
| `InUseReturn` | Restituire în magazie (la plecarea angajatului sau la schimbarea locului) | `OBR` | Extrabilanțier | M6 |
| `InUseWriteOff` | Scoatere din folosință și casare a obiectelor de inventar | `OBC` | Extrabilanțier; materialele recuperate intră în stoc (Stocuri) | M6 |
| `AssetCount` | Inventarierea mijloacelor fixe și a obiectelor în folosință: liste, scanare, diferențe | `INV` | Nu direct; diferențele generează intrări (plus) și ieșiri (lipsă) | M7 |

Fișa mijlocului fix (`fa.Asset`) nu e document: e entitatea pe care o modifică documentele de mai sus, cu istoricul lor (P1). Seriile implicite se creează la activarea modulului pentru fiecare firmă (`MIG-06`) și se pot schimba în Configurare › Documente › Serii (Nucleu). Toate seriile modulului sunt interne (`IsFiscal = 0`), numerotate continuu pe an.

**Stările comune** ale documentelor modulului: 1 Ciornă · 2 De aprobat (doar tranzacțiile cu aprobare: ieșirile, reevaluările, deprecierile, compunerile și descompunerile, implicit) · 3 Validat · 8 Stornat · 9 Anulat (doar din ciornă sau din „De aprobat”). Un document validat se corectează doar prin stornare, iar stornarea e permisă numai dacă nu există după el, pe aceleași fișe, un document validat sau o amortizare contată (`65010`). Fiecare schimbare se scrie în `core.DocumentStatusLog`.

**Stările fișei** (`fa.Asset.Status`, detaliate în M2): 1 În curs de punere în funcțiune (intrată, fără PIF) · 2 În funcțiune · 3 În conservare · 4 Amortizată complet (în funcțiune, fără valoare rămasă pe regimul contabil) · 8 Ieșită · 9 Anulată (intrare stornată).

### 3.2 Tranzacțiile livrate

Comportamentele sunt scrise în cod (`ITransactionBehavior`, Nucleu §3.6.5) și se înregistrează la pornirea modulului. Pentru fiecare comportament se livrează o tranzacție de sistem (`IsSystem = 1`, `MIG-06`), care se poate redenumi sau dezactiva, dar nu șterge.

| Cod livrat | Denumire | Document | Comportament (`Behavior`) | Mod | Surse (principală în **bold**) | Grup de consum |
| --- | --- | --- | --- | --- | --- | --- |
| `MFI-FF` | Intrare din factura de furnizor | `AssetIntake` | `Intake.FromSupplierInvoice` | Generată (și din coada „Imobilizări de preluat”, M2) | **`SupplierInvoice`** (validată; liniile cu `NonStockAssetKind = 1`) | `ASSET_IN` |
| `MFI-NIR` | Intrare din NIR (bunuri recepționate înaintea facturii) | `AssetIntake` | `Intake.FromReceipt` | Generată | **`GoodsReceipt`** (validat; linii de imobilizare) | `ASSET_IN` |
| `MFI-INC` | Decontarea investiției în curs | `AssetIntake` | `Intake.FromConstruction` | Generată | **`ConstructionInProgress`** (activă; pe antet, valoare) | `SETTLE_CIP` |
| `MFI-PRD` | Intrare din producție proprie | `AssetIntake` | `Intake.OwnProduction` | Manuală sau din comanda de producție (02.08, viitor) | — | — |
| `MFI-APT` | Intrare prin aport la capital | `AssetIntake` | `Intake.Contribution` | Manuală | — | — |
| `MFI-DON` | Intrare cu titlu gratuit (donație, sponsorizare primită) | `AssetIntake` | `Intake.Donation` | Manuală | — | — |
| `MFI-LSG` | Intrare în leasing financiar | `AssetIntake` | `Intake.FinanceLease` | Manuală | — (contractul de leasing ca atașament; ratele sunt în F3) | — |
| `MFI-PLS` | Plus la inventar | `AssetIntake` | `Intake.CountSurplus` | Generată | **`AssetCount`** (validat; liniile cu plus) | `COUNT_DIFF` |
| `MFI-GRP` | Intrare prin transfer din altă firmă a grupului | `AssetIntake` | `Intake.IntraGroup` | Generată | **`AssetDisposal`** al celeilalte firme (validat, `IES-GRP`) | `GROUP_TRANSFER` |
| `MFI-INI` | Preluare inițială (implementare), cu amortizarea deja calculată | `AssetIntake` | `Intake.OpeningBalance` | Manuală sau din import (Nucleu, Configurare › Import) | — | — |
| `INC-MAN` | Investiție în curs | `ConstructionInProgress` | `Construction.Manual` | Manuală | — | — |
| `INC-FF` | Adăugare factură la investiția în curs | `ConstructionInProgress` | `Construction.AddInvoice` | Generată | **`SupplierInvoice`** (linii cu `NonStockAssetKind = 2` investiție, M2) | `CIP_IN` |
| `INC-BC` | Adăugare consum din stoc la investiția în curs | `ConstructionInProgress` | `Construction.AddStockIssue` | Generată | **`StockIssue`** (bon de consum S2 pe proiectul investiției) | `CIP_IN` |
| `PIF-MFI` | Punere în funcțiune din intrare | `AssetCommissioning` | `Commissioning.FromIntake` | Generată (și automat la validarea intrării, setare 3.11) | **`AssetIntake`** (validată, fișe „în curs de punere în funcțiune”) | `COMMISSION` |
| `AML-LUN` | Amortizarea lunii | `DepreciationRun` | `Depreciation.Monthly` | Manuală sau prin job (ziua din 3.11) | — | — |
| `BMF-MAN` | Bon de mișcare | `AssetTransfer` | `Transfer.Manual` | Manuală (selecție individuală sau în masă) | — | — |
| `MVM-MOD` | Modernizare din factură | `AssetValueChange` | `ValueChange.ImprovementFromInvoice` | Generată | **`SupplierInvoice`** (linii de imobilizare legate de o fișă existentă) | `ASSET_IN` |
| `MVM-MODM` | Modernizare manuală (din producție proprie sau din investiție) | `AssetValueChange` | `ValueChange.Improvement` | Manuală; sau din investiția în curs | `ConstructionInProgress` (opțional) | `SETTLE_CIP` |
| `MVM-DEP` | Depreciere (ajustare pentru depreciere) | `AssetValueChange` | `ValueChange.Impairment` | Manuală (cu raport de evaluare atașat) | — | — |
| `MVM-DEPR` | Reluarea deprecierii | `AssetValueChange` | `ValueChange.ImpairmentReversal` | Generată | **`AssetValueChange`** de depreciere (validat) | `IMPAIRMENT` |
| `MVM-REV` | Reevaluare ocazională | `AssetValueChange` | `ValueChange.Revaluation` | Manuală | — | — |
| `MVM-REVM` | Reevaluare în masă (pe filtre) | `AssetValueChange` | `ValueChange.MassRevaluation` | Manuală, cu selecție pe filtre | — | — |
| `MVM-DNU` | Schimbarea duratei de amortizare | `AssetValueChange` | `ValueChange.UsefulLife` | Manuală | — | — |
| `MVM-RCL` | Reclasificare în altă grupă | `AssetValueChange` | `ValueChange.Reclassification` | Manuală | — | — |
| `CNS-PUN` | Punere în conservare | `AssetConservation` | `Conservation.Start` | Manuală | — | — |
| `CNS-REP` | Repunere în funcțiune | `AssetConservation` | `Conservation.End` | Generată | **`AssetConservation`** de punere (validat) | `CONSERVATION` |
| `CDM-CMP` | Compunere | `AssetRestructure` | `Restructure.Merge` | Manuală | — (fișele sursă se aleg pe document) | — |
| `CDM-DSC` | Descompunere | `AssetRestructure` | `Restructure.Split` | Manuală | — | — |
| `IMF-VZ` | Ieșire prin vânzare | `AssetDisposal` | `Disposal.Sale` | Generată (automat la validarea facturii) | **`SalesInvoice`** (validată, linii legate de o fișă, tranzacția `FV-MF` din Vânzări) | `DISPOSE` |
| `IMF-CAS` | Casare (scoatere din funcțiune) | `AssetDisposal` | `Disposal.Scrap` | Manuală | — (materialele recuperate fac intrare în stoc prin S2) | — |
| `IMF-LIP` | Lipsă la inventar | `AssetDisposal` | `Disposal.CountShortage` | Generată | **`AssetCount`** (validat; liniile cu minus) | `COUNT_DIFF` |
| `IMF-DON` | Ieșire prin donație sau sponsorizare | `AssetDisposal` | `Disposal.Donation` | Manuală | — | — |
| `IMF-APT` | Ieșire prin aport la capitalul altei societăți | `AssetDisposal` | `Disposal.Contribution` | Manuală | — | — |
| `IMF-GRP` | Transfer în altă firmă a grupului | `AssetDisposal` | `Disposal.IntraGroup` | Manuală (generează intrarea `MFI-GRP` în firma primitoare, ca ciornă) | — | — |
| `IMF-OB` | Trecere în obiecte de inventar | `AssetDisposal` | `Disposal.ToInventoryObject` | Manuală | — (deschide evidența de folosință, M6) | — |
| `OBF-BC` | Dare în folosință din bonul de consum | `InUseIssue` | `InUse.FromStockIssue` | Generată (automat la validarea bonului, setare 3.11) | **`StockIssue`** (bon de consum S2 cu linii de obiecte de inventar) | `IN_USE` |
| `OBF-INI` | Preluare inițială în folosință | `InUseIssue` | `InUse.OpeningBalance` | Manuală sau import | — | — |
| `OBT-MAN` | Transfer între persoane sau locații | `InUseTransfer` | `InUse.Transfer` | Manuală | — | — |
| `OBR-MAN` | Restituire în magazie | `InUseReturn` | `InUse.Return` | Manuală (și din lista de predare la plecarea angajatului, M6) | — | — |
| `OBC-MAN` | Casare de obiecte de inventar | `InUseWriteOff` | `InUse.WriteOff` | Manuală | — | — |
| `OBC-LIP` | Lipsă de obiecte de inventar (imputată sau neimputabilă) | `InUseWriteOff` | `InUse.Shortage` | Generată (din inventariere, M7, sau din predarea la plecare, M6) | **`AssetCount`** (liniile OB cu minus) | `COUNT_DIFF` |
| `INV-ANUAL` | Inventarierea anuală | `AssetCount` | `Count.Standard` | Manuală (liste pe locație, responsabil sau grupă) | — | — |
| `INV-PARTIAL` | Inventariere parțială în cursul anului | `AssetCount` | `Count.Standard` | Manuală | — | — |
| `INV-PREDARE` | Inventariere la schimbarea responsabilului de loc | `AssetCount` | `Count.Handover` | Manuală | — | — |

Note la tabel:

- Tranzacțiile de intrare `MFI-FF` și `MFI-NIR` consumă **aceeași** linie de achiziție în grupul `ASSET_IN`: o linie de NIR preluată nu mai poate fi preluată și din factura ei, iar invers. Diferența de preț dintre NIR și factură ajunge pe fișă ca ajustare a valorii de intrare (M2), nu ca a doua intrare.
- `IMF-VZ` e singura tranzacție a modulului cu sursă în alt modul care **nu** pornește de aici: Vânzările emit factura cu tranzacția nouă `FV-MF` (precizare la Vânzări V1 și V5, aplicată pe 27.11.2026), pe care linia poartă `AssetId`; la validarea facturii, Mijloacele fixe generează ieșirea în aceeași tranzacție SQL (prin portul `IAssetDisposalPort`, 6), ca factura să nu existe fără ieșire.
- `OBF-BC` e generată automat la validarea bonului de consum, dacă setarea `AutoInUseFromStockIssue` e activă; altfel bonul apare în lista „De dat în folosință” (M6). Bonul de consum rămâne al Stocurilor; aici se deschide doar evidența de folosință.
- Încasările și plățile (vânzarea unui mijloc fix, ratele de leasing) sunt ale Finanțelor (F1–F3).

### 3.3 Grupurile de consum

| Grup | Sursa consumată | Consumată de | Ce înseamnă rămasul | Limitează |
| --- | --- | --- | --- | --- |
| `ASSET_IN` | Linia de imobilizare a facturii de furnizor sau a NIR-ului | Intrarea de mijloc fix sau modernizarea | **Imobilizări de preluat**: cantitatea încă fără fișă (coada din M2) | Da |
| `CIP_IN` | Linia facturii (investiție) sau a bonului de consum | Investiția în curs | Ce nu a fost încă adăugat la o investiție | Da |
| `SETTLE_CIP` | Antetul investiției în curs (valoare) | Intrarea sau modernizarea din investiție | Valoarea încă nedecontată din `231` | Da |
| `COMMISSION` | Linia intrării (fișa) | PIF-ul | Fișele încă nepuse în funcțiune | Da |
| `IMPAIRMENT` | Linia deprecierii | Reluarea deprecierii | Deprecierea încă nereluată | Da |
| `CONSERVATION` | Linia punerii în conservare | Repunerea în funcțiune | Fișele încă în conservare | Da |
| `DISPOSE` | Linia facturii de vânzare `FV-MF` | Ieșirea prin vânzare | Liniile vândute fără ieșire (trebuie să fie mereu 0; verificat în C3, concordanța `ASSET_SALES`) | Da |
| `COUNT_DIFF` | Linia de inventar cu diferență | Intrarea din plus sau ieșirea din lipsă | Diferențele încă neînregistrate | Da |
| `GROUP_TRANSFER` | Linia ieșirii `IMF-GRP` | Intrarea `MFI-GRP` în firma primitoare | Bunurile plecate și încă neprimite | Da |
| `IN_USE` | Linia de obiect de inventar a bonului de consum | Darea în folosință | Obiectele consumate și încă nepredate unei persoane | Da |

### 3.4 Catalogul de clasificare (`AssetClassification`, HG 2139/2004)

Catalogul privind clasificarea și duratele normale de funcționare a mijloacelor fixe (HG 2139/2004, cu modificările ulterioare) e un nomenclator separat, la nivel de instalare, cu seed. Structura e un arbore de coduri: **grupa** (1 Construcții; 2 Instalații tehnice, mijloace de transport, animale și plantații; 3 Mobilier, aparatură birotică, echipamente de protecție a valorilor umane și materiale și alte active corporale), **subgrupa**, **clasa** și, unde există, **subclasa**. Doar nodurile-frunză au durată.

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `Code` | `varchar(20)` | Codul din catalog, cu puncte (`2.2.9`, `2.3.2.1.1`); unic; `CodeKey` calculat |
| `ParentId` | `int NULL` | Nodul părinte; grupele au `NULL` |
| `Level` | `tinyint` | 1 grupă · 2 subgrupă · 3 clasă · 4 subclasă · 5 nivel suplimentar |
| `Name` | `nvarchar(400)` | Denumirea din catalog |
| `IsLeaf` | `bit` | Doar frunzele se pot alege pe o fișă (`65002`) |
| `MinLifeYears`, `MaxLifeYears` | `decimal(5,2) NULL` | Durata normală minimă și maximă în ani (obligatorii pe frunze); `CHECK (MinLifeYears <= MaxLifeYears)` |
| `FiscalCategory` | `tinyint` | Categoria pentru metodele fiscale permise (3.6): 1 Construcții · 2 Echipamente tehnologice, mașini, unelte, instalații de lucru, calculatoare și periferice · 3 Alte mijloace fixe amortizabile · 4 Neamortizabile (terenuri, opere de artă, bunuri de patrimoniu) |
| `ValidFrom`, `ValidTo` | `date` | Pentru modificările catalogului; o clasă abrogată rămâne pe fișele existente, marcată |
| `Source` | `nvarchar(100)` | Actul care a introdus sau a modificat rândul |
| `IsSeed` | `bit` | Rând livrat; seed-ul nu suprascrie denumirile schimbate de client |

Exemple (ilustrative; codurile și duratele exacte se iau din textul oficial la generarea seed-ului):

| Cod | Denumire (prescurtat) | DNU | Categorie fiscală |
| --- | --- | --- | --- |
| `1.1.1` | Clădiri industriale | 40–60 ani | Construcții |
| `2.1.24` | Mașini, utilaje și instalații pentru manipularea materialelor (stivuitoare, transpalete electrice) | 8–12 ani | Echipamente tehnologice |
| `2.2.9` | Calculatoare electronice și echipamente periferice | 2–4 ani | Echipamente tehnologice (calculatoare) |
| `2.3.2.1.1` | Autoturisme | 4–6 ani | Alte mijloace fixe |
| `3.1.1` | Mobilier, inclusiv mobilier comercial | 9–15 ani | Alte mijloace fixe |

**Seed-ul**: fișier SQL `MERGE` care doar inserează (`MIG-06`), generat cu o unealtă (`_unelte/seed-hg2139/`, de făcut la implementare) din textul oficial al catalogului, cu verificarea că fiecare frunză are durate și că arborele e complet. Actualizările catalogului se livrează ca seed nou, cu `ValidFrom`, nu prin editarea rândurilor vechi. Imobilizările necorporale nu sunt în catalog: durata lor vine din contract sau din politica firmei (pe grupă, 3.5); programele informatice și licențele au implicit 3 ani.

### 3.5 Grupele de mijloace fixe (`AssetGroup`)

Grupa e unitatea de configurare contabilă a firmei: spune pe ce conturi stă un bun, cum se amortizează implicit și ce politici are. Clientul pornește de la grupele livrate (o grupă pe cont sintetic de imobilizare) și le poate împărți (de exemplu „Autoturisme” separat de „Autoutilitare”, pentru deductibilitatea limitată a autoturismelor).

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int` | Firma |
| `Code`, `Name` | `varchar(20)`, `nvarchar(100)` | Unic în firmă |
| `Nature` | `tinyint` | 1 Corporală · 2 Necorporală · 3 Teren (neamortizabil) · 4 Investiție în curs · 5 Obiect de inventar (pentru evidența de folosință și conturile `303` / `603` / `8039`) |
| `DefaultClassificationId` | `int NULL` | Clasa propusă din catalog (frunză); obligatorie pentru `Nature = 1` cu amortizare |
| `AssetAccountCode` | `varchar(20)` | Contul de imobilizare (`212`, `2131`, `2133`, `214`, `205`, `208`, `2111`...) — frunză în planul firmei (C1) |
| `DepreciationAccountCode` | `varchar(20) NULL` | Contul de amortizare (`2812`, `2813`, `2814`, `2805`, `2808`); `NULL` doar pentru terenuri |
| `DepreciationExpenseAccountCode` | `varchar(20) NULL` | Implicit `6811`; suprascris doar cu motiv (de exemplu un analitic pe activitate) |
| `ImpairmentAccountCode` | `varchar(20) NULL` | `2912`, `2913`, `2914`, `2905`, `2908`... |
| `RevaluationReserveAccountCode` | `varchar(20) NULL` | Implicit `105` (analitic pe grupă, opțional) |
| `RevaluationAffectsTax` | `bit` | 1 = reevaluarea se aplică și pe regimul `TAX` (clădirile și terenurile, a căror reevaluare e recunoscută fiscal: amortizarea fiscală se calculează pe valoarea reevaluată, iar rezerva devine impozabilă pe măsura deducerii); 0 = doar pe `BOOK` (implicit pentru celelalte grupe). Folosit de M4 |
| `NonDeductibleShare` | `decimal(5,2) NULL` | Procentul nedeductibil fiscal al amortizării (de exemplu 50 % la autoturismele cu utilizare mixtă), pentru D101; `NULL` = deductibil integral |
| `FiscalDepreciationCap` | `decimal(19,4) NULL` | Plafonul valorii de intrare pentru amortizarea fiscală deductibilă (de exemplu plafonul lunar pe autoturism din Codul fiscal), ca valoare pe bun; peste plafon amortizarea fiscală e nedeductibilă |
| `ComponentPolicy` | `tinyint` | 0 Fără componente · 1 Componente permise, amortizate separat · 2 Componente permise, amortizate cu părintele |
| `TrackSerialNumber`, `RequireResponsible`, `RequireLocation` | `bit` | Ce se cere pe fișă |
| `LabelFormatId` | `int NULL` | Formatul etichetei (3.10) |
| `InventoryNumberSeriesId` | `int NULL` | Seria proprie a grupei; `NULL` = seria implicită a firmei (3.8) |
| `IsActive` | `bit` | Dezactivare, nu ștergere |

**Regimurile pe grupă** (`AssetGroupBook`, un rând pe grupă și regim activ al firmei): `DepreciationProfileId` (profilul propus), `DefaultLifeMonths` (durata propusă; dacă lipsește, maximul clasei pentru `TAX` și media intervalului pentru `BOOK`), `AllowedProfileIds` (lista profilurilor permise; restricția legală din 3.6 se aplică oricum), `StartConvention` (moștenit din profil, suprascris doar pentru `IFRS`).

Grupele livrate pentru fiecare firmă la activarea modulului (`MIG-06`), cu conturile din planul românesc (OMFP 1802):

| Cod | Grupă | Natură | Cont imobilizare | Cont amortizare | Profil `BOOK` | Profil `TAX` |
| --- | --- | --- | --- | --- | --- | --- |
| `TEREN` | Terenuri | Teren | `2111` | — | Fără amortizare | Fără amortizare |
| `CLAD` | Clădiri și construcții | Corporală | `212` | `2812` | Liniar | Liniar (singurul permis) |
| `ECHIP` | Echipamente tehnologice | Corporală | `2131` | `2813` | Liniar | Degresiv |
| `MASUR` | Aparate de măsură și control | Corporală | `2132` | `2813` | Liniar | Liniar |
| `AUTO` | Mijloace de transport | Corporală | `2133` | `2813` | Liniar | Liniar |
| `IT` | Calculatoare și echipamente IT | Corporală | `214` | `2814` | Liniar | Accelerat |
| `MOB` | Mobilier și aparatură birotică | Corporală | `214` | `2814` | Liniar | Liniar |
| `SOFT` | Programe informatice și licențe | Necorporală | `208` | `2808` | Liniar, 36 luni | Liniar, 36 luni |
| `CONC` | Concesiuni, brevete, mărci | Necorporală | `205` | `2805` | Liniar | Liniar |
| `INC` | Investiții în curs | Investiție | `231` | — | — | — |
| `OB` | Obiecte de inventar în folosință | Obiect de inventar | `303` (în magazie) / `8039` (în folosință) | — | — | — |

### 3.6 Regimurile și profilurile de amortizare

**Regimurile** (`AssetBookType`, nomenclator închis, livrat):

| Cod | Regim | Contat | Folosit pentru | Activ |
| --- | --- | --- | --- | --- |
| `BOOK` | Contabil (OMFP 1802) | Da | Note contabile, balanță, bilanț, D406 active | Mereu |
| `TAX` | Fiscal (Codul fiscal art. 28) | Nu | Amortizarea fiscală deductibilă, diferențele contabil–fiscal pentru D101 (C4) | Mereu |
| `IFRS` | IFRS (IAS 16, IAS 38, IAS 36) | Nu (raportat prin planul alternativ C1) | Raportarea de grup și firmele care aplică IFRS | Opțional pe firmă (setare 3.11) |

**Profilul de amortizare** (`DepreciationProfile`, la nivel de instalare; cele livrate nu se pot modifica, doar copia):

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `Code`, `Name` | `varchar(20)`, `nvarchar(100)` | Unic |
| `Method` | `tinyint` | 1 Liniară · 2 Degresivă (AD1) · 3 Accelerată · 4 Pe unități produse · 5 Fără amortizare |
| `DegressiveCoefficients` | `nvarchar(100) NULL` | Pentru metoda 2: coeficienții pe intervale de durată; livrat `2-5:1.5;5-10:2.0;10-:2.5` (durata în ani, intervale închise la stânga) |
| `AcceleratedFirstYearPct` | `decimal(5,2) NULL` | Pentru metoda 3: procentul din primul an de funcționare (livrat 50 %, maximul legal) |
| `SwitchToLinear` | `bit` | Pentru metoda 2: trece pe liniar când cota liniară pe durata rămasă e cel puțin egală cu cea degresivă (regula legală, livrat 1) |
| `StartConvention` | `tinyint` | 1 Din luna următoare PIF-ului (regula românească, livrat) · 2 Din luna PIF-ului · 3 Jumătate de lună (doar `IFRS`) |
| `StopConvention` | `tinyint` | 1 Amortizarea se calculează inclusiv în luna ieșirii (livrat; ieșirea oprește calculul din luna următoare) · 2 Până în luna anterioară ieșirii |
| `RoundingMode` | `tinyint` | 1 Cota lunară rotunjită la 2 zecimale, diferența în ultima lună (livrat) · 2 Cota anuală rotunjită, împărțită egal pe 12 luni, diferența în decembrie |
| `UnitsBasis` | `nvarchar(50) NULL` | Pentru metoda 4: unitatea de măsură a producției (ore de funcționare, km, piese), raportată lunar (M3) |
| `AllowedBooks` | `tinyint` | Pe ce regimuri se poate folosi (bitmask: 1 `BOOK`, 2 `TAX`, 4 `IFRS`); metoda 4 nu e permisă pe `TAX` |

**Restricțiile legale pe regimul fiscal**, aplicate peste orice configurare (`65004`), după categoria fiscală a clasei (3.4):

| Categorie fiscală | Metode permise pe `TAX` |
| --- | --- |
| 1 Construcții | Doar liniară |
| 2 Echipamente tehnologice, mașini, unelte, instalații de lucru, calculatoare și periferice | Liniară, degresivă sau accelerată |
| 3 Alte mijloace fixe amortizabile | Liniară sau degresivă |
| 4 Neamortizabile | Fără amortizare |
| Imobilizări necorporale | Liniară; brevetele de invenție și programele informatice și accelerată |

Metoda aleasă pe regimul fiscal pentru un bun nu se mai schimbă după prima amortizare (`65005`); pe regimul contabil se poate schimba doar prin document (M4, schimbare de estimare contabilă, prospectiv).

**Exemplu de calcul** (folosit în ecranul `m1-04` și în criteriile de acceptare): stivuitor electric, valoare de intrare 98.400,00 lei, durată 8 ani (96 de luni), PIF în martie 2026, deci amortizare din aprilie 2026.

| An de funcționare | Liniar | Degresiv (coeficient 2,0 → 25 %/an) | Accelerat (50 % în primul an) |
| --- | --- | --- | --- |
| 1 | 12.300,00 | 24.600,00 | 49.200,00 |
| 2 | 12.300,00 | 18.450,00 | 7.028,57 |
| 3 | 12.300,00 | 13.837,50 | 7.028,57 |
| 4 | 12.300,00 | 10.378,13 | 7.028,57 |
| 5 | 12.300,00 | 7.783,59 (trece pe liniar: 31.134,37 / 4) | 7.028,57 |
| 6 | 12.300,00 | 7.783,59 | 7.028,57 |
| 7 | 12.300,00 | 7.783,59 | 7.028,57 |
| 8 | 12.300,00 | 7.783,60 | 7.028,58 |
| Total | 98.400,00 | 98.400,00 | 98.400,00 |

Cota lunară liniară e 1.025,00 lei. Anii sunt ani de funcționare (12 luni de la începutul amortizării), nu ani calendaristici; calendarul lunar e în M3.

### 3.7 Pragul de încadrare (`AssetThreshold`)

Valoarea limită de la care un bun corporal cu durată de utilizare peste un an e mijloc fix. Acum e **5.000 lei** (precizare Aurelian, 27.11.2026). Se ține pe firmă, cu istoric, pentru că s-a schimbat de mai multe ori (P30).

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId` | `int` | Firma |
| `ValidFrom` | `date` | Data de la care se aplică; unic pe firmă |
| `Amount` | `decimal(19,4)` | Valoarea limită, în lei, fără TVA |
| `LegalReference` | `nvarchar(200)` | Actul normativ |

Reguli:

1. La intrare (M2), valoarea de intrare fără TVA a **unui** bun se compară cu pragul în vigoare la data documentului de intrare. Peste sau egal cu pragul: mijloc fix. Sub prag: obiect de inventar, cu excepția grupelor marcate `BelowThresholdPolicy = 2` (3.11).
2. Bunurile sub prag care sunt totuși înregistrate ca mijloace fixe (opțiunea firmei, permisă de Codul fiscal) se amortizează normal; fișa poartă marcajul `BelowThreshold = 1`, iar raportul din M7 le listează.
3. Imobilizările necorporale nu au prag: o licență e imobilizare dacă durata de utilizare e peste un an, indiferent de valoare (politica firmei poate fixa un prag propriu pe grupă, `MinCapitalizationAmount`).
4. Schimbarea pragului nu reîncadrează fișele existente; ecranul arată câte fișe active sunt sub pragul nou, informativ.
5. Pragul e folosit și de Achiziții (A5, la propunerea `NonStockAssetKind` pe linia de factură) și de Stocuri (la tipul articolului), prin `IAssetThresholdPolicy` (6).

Valori livrate: un singur rând, 5.000 lei. Seed-ul pune rândul de 5.000 lei cu data activării modulului în firmă (decizia 6); firmele care preiau bunuri intrate înainte adaugă la punerea în funcțiune rândurile istorice (de exemplu 2.500 lei), cu datele din actele normative.

### 3.8 Numerele de inventar (`AssetInventoryNumber`)

Numerele de inventar folosesc serviciul de serii din Nucleu (`core.NumberSeries`), cu două tipuri noi: `AssetInventoryNumber` (mijloace fixe, investiții, necorporale) și `InUseRecordNumber` (obiecte de inventar în folosință, opțional). Precizare la Nucleu, aplicată pe 27.11.2026.

| Regulă | Descriere |
| --- | --- |
| Format | Prefix + număr cu zerouri (`MF 000412`); prefixul și lungimea se configurează pe serie; fără an în număr (numărul trăiește cât bunul) |
| Seria | Una implicită pe firmă; opțional una pe grupă (`AssetGroup.InventoryNumberSeriesId`), de exemplu `IT 00123` |
| Alocare | La validarea intrării, în aceeași tranzacție SQL (Nucleu, serviciul de numerotare); ciorna afișează „se alocă la validare” |
| Unicitate | Unic în firmă pe tot istoricul (`UX (CompanyId, InventoryNumberKey)`); nu se refolosește după ieșire sau stornare (o intrare stornată lasă numărul „anulat” în registru) |
| Număr existent | La preluarea inițială (`MFI-INI`) se acceptă numerele vechi ale clientului, așa cum sunt, cu verificarea unicității; seria continuă de la cel mai mare număr numeric preluat |
| Componente | Numărul părintelui cu sufix `/n` (`MF 000412/1`), alocat la adăugarea componentei |
| Transfer în grup | Firma primitoare alocă număr nou din seria ei; numărul vechi rămâne pe fișă ca „număr anterior” |
| Obiecte în folosință | Implicit fără număr individual (se urmăresc pe articol, cantitate, persoană); cu număr individual pentru grupele cu `TrackSerialNumber = 1` sau dacă setarea `InUseIndividualNumbers` e activă |

### 3.9 Conturile și contarea (precizări la C2)

Modulele nu știu conturi (C2 §3.1). Mijloacele fixe trimit în `AccountingPayload` componente noi și `AssetGroupId`, `AssetId`, `InventoryNumber` pe fiecare componentă. Se adaugă în C2 (precizare aplicată pe 27.11.2026) **cheile de cont** de mai jos, cu lanțul: override pe linie → grupa de mijloace fixe (`fa.AssetGroup`, pe firmă) → rolul → implicit model.

| Cheie nouă | Cont din grupă | Rol (implicit livrat) |
| --- | --- | --- |
| `FixedAsset` | `AssetAccountCode` | — (obligatoriu din grupă) |
| `AccumulatedDepreciation` | `DepreciationAccountCode` | — |
| `DepreciationExpense` | `DepreciationExpenseAccountCode` | `DepreciationExpense` (`6811`) |
| `ImpairmentAllowance` | `ImpairmentAccountCode` | — |
| `ImpairmentExpense` / `ImpairmentReversalIncome` | — | `ImpairmentExpense` (`6813`) / `ImpairmentReversal` (`7813`) |
| `RevaluationReserve` | `RevaluationReserveAccountCode` | `RevaluationReserve` (`105`) |
| `RealizedRevaluationSurplus` | — | `RealizedRevaluationSurplus` (`1175`) |
| `RevaluationLoss` / `RevaluationGain` | — | `RevaluationLoss` (`6813` sau cheltuială din reevaluare) / `RevaluationGain` (`7813`, până la descreșterea recunoscută anterior ca cheltuială) |
| `AssetDisposalExpense` | — | `AssetDisposalExpense` (`6583`) |
| `AssetSaleIncome` | — | `AssetSaleIncome` (`7583`); folosit de factura `FV-MF` în locul lui `Revenue` |
| `ConstructionInProgress` | `AssetAccountCode` al grupei `INC` | `ConstructionInProgress` (`231`) |
| `OwnProductionAssetIncome` | — | `OwnProductionAssetIncome` (`722` corporale, `721` necorporale) |
| `AssetSupplierPayable` | — | `AssetSupplierPayable` (`404`); folosit de A5 pe liniile de imobilizare (precizare la A5) |
| `AssetPendingIntake` | — | `AssetPendingIntake` (`231.PRL`, analitic „Imobilizări de preluat”); debitat de A5, creditat de intrarea M2 |
| `LeaseLiability` | — | `LeaseLiability` (`167`, datoria de leasing financiar) |
| `InventoryObjectInUse` | — | `InventoryObjectInUse` (`8039`, extrabilanțier) |
| `ContributionInKind` / `DonationReceived` | — | `ContributionInKind` (`1012` sau `456`) / `DonationReceived` (`4753`) |

**Modelele de contare livrate** (C2 §3.2, un model pe eveniment; detaliile în partea care publică evenimentul):

| Eveniment · variantă | Notă (regimul contabil) | Parte |
| --- | --- | --- |
| `MijlocFixIntrat` · din factură | `21x` / `20x` = `231.PRL` (factura de furnizor A5 contează `231.PRL` = `404` și TVA-ul; soldul `231.PRL` e coada „Imobilizări de preluat”) | M2 |
| `MijlocFixIntrat` · din investiție | `21x` = `231` | M2 |
| `MijlocFixIntrat` · producție proprie | `21x` = `722` (sau `231` = `722` pe parcurs, apoi `21x` = `231`) | M2 |
| `MijlocFixIntrat` · aport / donație / plus | `21x` = `456` / `4753` / `4753` (plusul la inventar, conform OMFP 1802) | M2, M7 |
| `MijlocFixIntrat` · preluare inițială | Fără notă (soldurile vin din balanța de deschidere); doar verificarea concordanței `21x` / `28xx` cu fișele (C3) | M2 |
| `AmortizareCalculata` | `6811` = `28xx`, pe grupă, centru de cost și (opțional) pe bun | M3 |
| `MijlocFixModificat` · modernizare | `21x` = `404` sau `231` | M4 |
| `MijlocFixModificat` · depreciere / reluare | `6813` = `29xx` / `29xx` = `7813` | M4 |
| `MijlocFixModificat` · reevaluare | Creștere: `21x` = `105` (sau `7813` până la deprecierea anterioară); descreștere: `105` = `21x`, apoi `6813` = `21x`; eliminarea amortizării (metoda netă): `28xx` = `21x` | M4 |
| `MijlocFixModificat` · transfer surplus | `105` = `1175` (lunar, pe măsura amortizării, sau la ieșire, setare 3.11) | M3, M5 |
| `MijlocFixIesit` | `28xx` = `21x` (amortizarea), `6583` = `21x` (valoarea rămasă), `105` = `1175` (rezerva rămasă) | M5 |
| `ObiectInventarDatInFolosinta` | Extrabilanțier: debit `8039` (cheltuiala `603` = `303` o face bonul de consum S2) | M6 |

### 3.10 Nomenclatoarele modulului

Tabele separate, cu seed `MERGE` care doar inserează (`MIG-06`), ca la Achiziții și Vânzări. Se editează în Configurare › Nomenclatoare › Mijloace fixe (ecranul `m1-06`).

| Nomenclator | Câmpuri | Valori livrate |
| --- | --- | --- |
| `AcquisitionMode` (mod de obținere) | `Code`, `Name`, `IntakeBehavior` (legătura cu tranzacția) | `ACHIZ` Achiziție · `PRD` Producție proprie · `APT` Aport la capital · `DON` Donație, sponsorizare · `LSG` Leasing financiar · `PLS` Plus la inventar · `SCH` Schimb · `GRP` Transfer în grup · `INI` Preluare inițială |
| `AssetReason` | `Kind` (1 modificare de valoare, 2 conservare, 3 ieșire, 4 casare, 5 durată în afara intervalului, 6 depreciere), `Code`, `Name`, `RequiresAttachment` | Exemple: modernizare, reparație capitală, uzură morală, avarie, deteriorare, lipsă de comenzi, sezonalitate, vânzare, casare la expirarea duratei, bun second-hand, raport de evaluare |
| `AssetCondition` (stare fizică, la inventariere) | `Code`, `Name`, `SuggestsImpairment` | `BUN` Bună · `UZAT` Uzat · `DEF` Defect, reparabil · `NEF` Nefuncțional · `DEPR` Depreciat (propune deprecierea, M4) |
| `LabelFormat` | `Code`, `Name`, `Symbology` (Code128, QR, DataMatrix), `WidthMm`, `HeightMm`, `Fields` (număr, denumire, firmă, locație, data PIF), `Material` (hârtie, poliester, metalică) | `ET-50x25` Code128 · `ET-40x20-QR` QR |

**Proprietățile tehnice** ale fișei (marcă, model, serie, an de fabricație, putere, capacitate, număr de înmatriculare, cod BIOS...) folosesc proprietățile configurabile din Nucleu (`core.PropertyDefinition`), cu `EntityType` nou `Asset` și filtrare pe grupă (`AppliesToAssetGroupIds`). Precizare la Nucleu §3.5, aplicată pe 27.11.2026. Locațiile sunt cele din Nucleu (`core.Location`), responsabilii sunt angajați (`core.Employee`), centrele de cost și departamentele sunt dimensiuni (`core.DimensionValue`): modulul nu are nomenclatoare proprii pentru ele.

### 3.11 Setările de mijloace fixe ale firmei (`FixedAssetSettings`)

Un rând pe firmă. Ecranul `m1-05`.

| Setare | Tip | Implicit | Ce face |
| --- | --- | --- | --- |
| `IfrsBookEnabled` | `bit` | 0 | Activează regimul `IFRS` (cere un plan alternativ C1 de tip IFRS) |
| `IntakeFromInvoiceMode` | `tinyint` | 1 | 1 Coada „Imobilizări de preluat” (contabilul generează fișele) · 2 Fișe create automat la validarea facturii, ca ciorne · 3 Fișe create și validate automat (doar dacă grupa și clasa sunt complete pe articol) |
| `AutoCommissionOnIntake` | `bit` | 0 | PIF-ul se generează și se validează odată cu intrarea, la aceeași dată (pentru bunurile puse imediat în funcțiune) |
| `DepreciationRunDay` | `tinyint` | 1 | Ziua din luna următoare în care jobul calculează amortizarea lunii trecute, ca ciornă (0 = doar manual) |
| `DepreciationByCostCenter` | `bit` | 1 | Amortizarea se contează pe centrul de cost al fișei (din ultimul transfer valabil în lună) |
| `DepreciationPostingDetail` | `tinyint` | 1 | 1 O linie de notă pe grupă și centru de cost · 2 O linie pe bun |
| `ConservationSuspendsBook` | `bit` | 0 | Conservarea suspendă și amortizarea contabilă (implicit doar pe cea fiscală; decizia 10) |
| `ConservationSuspendsTax` | `bit` | 1 | Conservarea suspendă amortizarea fiscală |
| `RevaluationMethod` | `tinyint` | 1 | 1 Metoda netă (eliminarea amortizării cumulate, `28xx` = `21x`, apoi reevaluarea valorii nete) · 2 Metoda brută (valoarea brută și amortizarea recalculate proporțional) |
| `RevaluationSurplusTransfer` | `tinyint` | 1 | 1 Lunar, pe măsura amortizării (`105` = `1175` cu diferența dintre amortizarea pe valoarea reevaluată și cea pe costul istoric) · 2 Doar la ieșire |
| `BelowThresholdPolicy` | `tinyint` | 1 | 1 Sub prag → obiect de inventar · 2 Sub prag → mijloc fix, dacă grupa o permite (`AssetGroup.AllowBelowThreshold`) · 3 Întreabă la intrare |
| `InUseTracking` | `tinyint` | 1 | 1 Evidență pe persoană și locație (M6) · 0 Fără evidență de folosință (doar cheltuială, nerecomandat) |
| `AutoInUseFromStockIssue` | `bit` | 1 | Bonul de consum cu obiecte de inventar generează automat darea în folosință (`OBF-BC`) |
| `InUseIndividualNumbers` | `bit` | 0 | Număr individual pe fiecare obiect în folosință (altfel pe articol și cantitate) |
| `RequireApprovalKinds` | `nvarchar(100)` | `DISPOSAL,REVALUATION,IMPAIRMENT,RESTRUCTURE` | Ce documente trec prin „De aprobat” (aprobarea e a rolului Șef administrativ sau Director economic) |
| `ComponentDefault` | `tinyint` | 0 | Politica implicită pentru grupele noi (3.5 `ComponentPolicy`) |
| `CountRequiresScan` | `bit` | 0 | La inventariere, bunurile se confirmă doar prin scanarea etichetei (M7) |

## 4. Fluxuri principale

### 4.1 Configurarea inițială a unei firme

1. La activarea modulului (licență cu pachetul „Active”), migrarea creează pentru fiecare firmă: grupele livrate (3.5) cu conturile din planul firmei, seria `MF` a numerelor de inventar, seriile documentelor (3.1), pragul de 5.000 lei, setările implicite (3.11) și tranzacțiile de sistem (3.2).
2. Asistentul „Punere în funcțiune · Mijloace fixe” (pas nou în asistentul de configurare din Nucleu, ecranul `10`) verifică: fiecare grupă are conturi frunză active în planul firmei (`65003`), regimul fiscal are profil pe fiecare grupă amortizabilă, catalogul HG 2139/2004 e încărcat (numărul de frunze > 0) și există cel puțin o locație și un responsabil.
3. Contabilul își ajustează grupele (de exemplu împarte `AUTO` în `AUTOTUR` cu 50 % nedeductibil și `AUTOUT`), apoi importă fișele existente (`MFI-INI`, M2).

### 4.2 Alegerea profilului și a duratei pentru un bun (exemplu)

Laptop Dell Latitude, 5.890,00 lei fără TVA, factură din 10.11.2026, grupa `IT`, clasa `2.2.9` (2–4 ani):

1. Pragul la 10.11.2026 e 5.000 lei; 5.890,00 ≥ 5.000 → mijloc fix.
2. Regimul `BOOK`: profilul grupei, liniar, durata propusă 36 de luni (media intervalului 2–4 ani). Regimul `TAX`: profilul grupei, accelerat (permis: categoria 2), durata propusă 48 de luni (maximul clasei).
3. Contabilul alege pe `TAX` 24 de luni: e în interval, se acceptă. Dacă ar alege 60 de luni, sistemul refuză (`65001`), pentru că `TAX` cere durata în interval.
4. Pe `BOOK` alege 60 de luni (politica grupului pentru laptopuri): în afara intervalului; se cere motiv din `AssetReason` `Kind = 5` și dreptul `FixedAssets.UsefulLife.OutOfRange`.

### 4.3 Schimbarea pragului

1. Contabilul adaugă un rând nou în `AssetThreshold` (ecranul `m1-05`), cu data de la care se aplică și actul normativ.
2. Sistemul verifică să nu existe intrări validate după acea dată care ar fi fost încadrate altfel (`65006`, avertizare cu lista lor, nu blocare: încadrarea lor rămâne decizia contabilului, prin reclasificare M4 sau M5 `IMF-OB`).
3. De la data nouă, liniile de achiziție sub prag nu mai sunt propuse ca imobilizare (A5) și intrările le refuză ca mijloc fix (`65007`), cu excepția politicii 2 sau 3.

### 4.4 Crearea unei tranzacții proprii

Ca la Achiziții A1 §4.1: Configurare › Documente › Tranzacții › Mijloace fixe, „Copiază” pe o tranzacție livrată (de exemplu `IMF-CAS` → `IMF-CAS-AUTO` „Casare autovehicule, cu radiere”), cu altă denumire, alte roluri, alte valori implicite (motivul, cere atașamentul certificatului de radiere) și alte semnale de contare. Comportamentul nu se schimbă după primul document (`50029`).

### 4.5 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Grupă cu cont neactiv sau neterminal în planul firmei | Salvarea grupei e refuzată (`65003`); la o schimbare ulterioară de plan (C1), grupele afectate apar în verificarea C1 „Conturi folosite de module” |
| Clasă din catalog abrogată după ce a fost folosită | Fișele existente o păstrează (marcată „abrogată” în fișă și rapoarte); pe fișe noi nu se mai poate alege (`65002`) |
| Profil livrat modificat de client | Refuzat (`65008`); se copiază ca profil propriu |
| Regimul `IFRS` activat după ce există fișe | Fișele existente primesc rândul `IFRS` cu valorile din `BOOK` la data activării (copiere de deschidere, documentată în audit); diferențele se ajustează manual prin documente M4 pe regimul `IFRS` |
| Regimul `IFRS` dezactivat | Permis doar dacă nu există documente cu efect doar pe `IFRS`; altfel rămâne activ, ascuns din formulare noi (`65009`) |
| Reevaluarea pe regimul fiscal | Nu există ca operație: reevaluarea contabilă nu schimbă valoarea fiscală (Codul fiscal: rezerva din reevaluare se impozitează pe măsura deducerii amortizării sau la ieșire, raportat în D101 din diferențe) |
| Pragul schimbat cu dată în trecut, peste o lună închisă | Refuzat (`65006` blocant) dacă data e într-o perioadă închisă (C3) |
| Bun cu valoare în valută (import) | Valoarea de intrare se ține doar în lei, la cursul din documentul sursă (NIR, DVI, A6); fișa păstrează valuta și suma originală, informativ |

## 5. Evenimente

### 5.1 Publicate (M1)

| Eveniment | Când | Conținut | Consumatori |
| --- | --- | --- | --- |
| `GrupaMijloaceFixeModificata` v1 | Creare, modificare sau dezactivare a unei grupe | `CompanyId`, `AssetGroupId`, `Code`, `Nature`, conturile, `IsActive` | Finanțe C2 (invalidează cache-ul resolver-ului), Achiziții (propunerea pe linie) |
| `PragMijloaceFixeModificat` v1 | Rând nou de prag | `CompanyId`, `ValidFrom`, `Amount` | Achiziții A5, Stocuri (încadrarea articolelor noi) |

Evenimentele documentelor (intrare, amortizare, modificare, ieșire, folosință) sunt descrise în M2–M7.

### 5.2 Consumate (M1)

| Eveniment | De la | Ce face |
| --- | --- | --- |
| `FirmaCreata` v1 | Nucleu | Creează grupele, seriile, pragul și setările implicite pentru firma nouă, dacă modulul e licențiat |
| `PlanConturiModificat` v1 | C1 | Reverifică grupele firmei; grupele cu conturi devenite invalide apar în „De rezolvat” al contabilului |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M1)

```csharp
namespace Erp.FixedAssets.Contracts;

/// Pragul de încadrare și clasificarea, folosite de Achiziții (A5) și Stocuri la propunerea tipului de linie.
public interface IAssetThresholdPolicy
{
    /// Pragul în vigoare la data dată, în lei, fără TVA.
    Task<decimal> GetThresholdAsync(int companyId, DateOnly date, CancellationToken ct);

    /// Propune încadrarea unei linii de achiziție: mijloc fix, obiect de inventar sau cheltuială.
    Task<AssetClassificationProposalDto> ProposeAsync(AssetClassificationRequestDto request, CancellationToken ct);
}

public sealed record AssetClassificationRequestDto(
    int CompanyId, DateOnly DocumentDate, int ItemId, int? ItemVariantId,
    decimal UnitValueRon, decimal Quantity, int? UsefulLifeMonthsHint);

public sealed record AssetClassificationProposalDto(
    AssetProposalKind Kind,          // FixedAsset, InventoryObject, Expense, Construction
    int? AssetGroupId, int? ClassificationId, decimal ThresholdRon, string Reason);

public enum AssetProposalKind : byte { Expense = 0, FixedAsset = 1, Construction = 2, InventoryObject = 3 }

/// Catalogul HG 2139/2004 și grupele firmei, pentru ecranele altor module (de exemplu A5, linia de imobilizare).
public interface IAssetCatalogQueries
{
    Task<IReadOnlyList<AssetGroupDto>> GetGroupsAsync(int companyId, bool activeOnly, CancellationToken ct);
    Task<ClassificationDto?> GetClassificationAsync(int classificationId, CancellationToken ct);
}

public sealed record AssetGroupDto(int Id, string Code, string Name, byte Nature,
    string AssetAccountCode, string? DepreciationAccountCode, int? DefaultClassificationId, bool IsActive);

public sealed record ClassificationDto(int Id, string Code, string Name, decimal? MinLifeYears,
    decimal? MaxLifeYears, byte FiscalCategory, bool IsLeaf, DateOnly? ValidTo);
```

Contractele documentelor (`IAssetDisposalPort` pentru factura `FV-MF`, `IInUseIssuePort` pentru bonul de consum, `IDepreciationStatusQuery` pentru C3) sunt definite în părțile care le implementează (M5, M6, M3).

## 7. Schema SQL `fa` (partea M1)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `fa.AssetClassification` | `Id int` | UX `(CodeKey)`; FK `ParentId`; `CHECK (IsLeaf = 0 OR (MinLifeYears IS NOT NULL AND MaxLifeYears IS NOT NULL))`; `CHECK (MinLifeYears <= MaxLifeYears)` | `(ParentId)`; `(NameSearch)` (coloană `CI_AI`, `SQL-25`) |
| `fa.DepreciationProfile` | `Id int` | UX `(CodeKey)`; `CHECK (Method BETWEEN 1 AND 5)` | — |
| `fa.AssetBookType` | `Code varchar(4)` | — (rânduri `BOOK`, `TAX`, `IFRS`) | — |
| `fa.AssetGroup` | `Id int` | UX `(CompanyId, CodeKey)`; FK `CompanyId`, `DefaultClassificationId`, `LabelFormatId`, `InventoryNumberSeriesId` (`core.NumberSeries`) | `(CompanyId, Nature, IsActive)` |
| `fa.AssetGroupBook` | `Id int` | UX `(AssetGroupId, BookCode)`; FK `AssetGroupId`, `BookCode`, `DepreciationProfileId` | — |
| `fa.AssetGroupAllowedProfile` | `(AssetGroupBookId, DepreciationProfileId)` | FK pe ambele | — |
| `fa.AssetThreshold` | `Id int` | UX `(CompanyId, ValidFrom)`; `CHECK (Amount > 0)` | — |
| `fa.FixedAssetSettings` | `Id int` | UX `(CompanyId)` | — |
| `fa.AcquisitionMode`, `fa.AssetCondition`, `fa.LabelFormat` | `Id int` | UX `(CodeKey)` | — |
| `fa.AssetReason` | `Id int` | UX `(Kind, CodeKey)` | — |
| `core.PropertyDefinition` (Nucleu, valoare nouă) | — | `EntityType` acceptă `Asset`; coloană nouă `AppliesToAssetGroupIds` (listă, `nvarchar(400) NULL`) | — |

Conturile din grupă se țin ca **cod** (`varchar(20)`), nu ca cheie străină spre `fin.Account`, pentru că planul de conturi e al Finanțelor și nu se fac FK între scheme de module (`SQL-16`); validitatea se verifică prin `fin.usp_Account_ValidateLeaf` apelată din procedura de salvare și reverificată la `PlanConturiModificat`.

Exemplu de tabel complet:

```sql
CREATE TABLE fa.AssetGroup (
    Id                              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssetGroup PRIMARY KEY,
    CompanyId                       int               NOT NULL CONSTRAINT FK_AssetGroup_Company REFERENCES core.Company(Id),
    Code                            varchar(20)       NOT NULL,
    CodeKey                         AS UPPER(Code) PERSISTED,
    Name                            nvarchar(100)     NOT NULL,
    Nature                          tinyint           NOT NULL CONSTRAINT CK_AssetGroup_Nature CHECK (Nature BETWEEN 1 AND 5),
    DefaultClassificationId         int               NULL CONSTRAINT FK_AssetGroup_Classification REFERENCES fa.AssetClassification(Id),
    AssetAccountCode                varchar(20)       NOT NULL,
    DepreciationAccountCode         varchar(20)       NULL,
    DepreciationExpenseAccountCode  varchar(20)       NULL,
    ImpairmentAccountCode           varchar(20)       NULL,
    RevaluationReserveAccountCode   varchar(20)       NULL,
    RevaluationAffectsTax           bit               NOT NULL CONSTRAINT DF_AssetGroup_RevaluationAffectsTax DEFAULT (0),
    NonDeductibleShare              decimal(5,2)      NULL CONSTRAINT CK_AssetGroup_NonDed CHECK (NonDeductibleShare BETWEEN 0 AND 100),
    FiscalDepreciationCap           decimal(19,4)     NULL,
    MinCapitalizationAmount         decimal(19,4)     NULL,
    AllowBelowThreshold             bit               NOT NULL CONSTRAINT DF_AssetGroup_BelowThr DEFAULT (0),
    ComponentPolicy                 tinyint           NOT NULL CONSTRAINT DF_AssetGroup_Component DEFAULT (0),
    TrackSerialNumber               bit               NOT NULL CONSTRAINT DF_AssetGroup_Serial DEFAULT (0),
    RequireResponsible              bit               NOT NULL CONSTRAINT DF_AssetGroup_ReqResp DEFAULT (1),
    RequireLocation                 bit               NOT NULL CONSTRAINT DF_AssetGroup_ReqLoc DEFAULT (1),
    LabelFormatId                   int               NULL CONSTRAINT FK_AssetGroup_LabelFormat REFERENCES fa.LabelFormat(Id),
    InventoryNumberSeriesId         int               NULL CONSTRAINT FK_AssetGroup_Series REFERENCES core.NumberSeries(Id),
    IsActive                        bit               NOT NULL CONSTRAINT DF_AssetGroup_IsActive DEFAULT (1),
    CreatedAt                       datetime2(3)      NOT NULL CONSTRAINT DF_AssetGroup_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy                       int               NOT NULL,
    ModifiedAt                      datetime2(3)      NULL,
    ModifiedBy                      int               NULL,
    RowVersion                      rowversion        NOT NULL,
    CONSTRAINT CK_AssetGroup_Depreciable CHECK (Nature IN (3, 4, 5) OR DepreciationAccountCode IS NOT NULL),
    CONSTRAINT CK_AssetGroup_Classification CHECK (Nature <> 1 OR DefaultClassificationId IS NOT NULL)
);
CREATE UNIQUE INDEX UX_AssetGroup_Company_Code ON fa.AssetGroup (CompanyId, CodeKey);
CREATE INDEX IX_AssetGroup_Company_Nature ON fa.AssetGroup (CompanyId, Nature, IsActive);
```

Procedurile M1 (toate cu șablonul din Bune practici 3.5, `SP-01`…`SP-21`): `fa.usp_AssetGroup_Save` (validează conturile prin Finanțe și restricțiile legale ale profilurilor pe `TAX`), `fa.usp_AssetGroup_Deactivate` (refuză dacă există fișe active fără altă grupă propusă, `65011`), `fa.usp_DepreciationProfile_Save` (refuză profilurile livrate, `65008`), `fa.usp_AssetThreshold_Add`, `fa.usp_FixedAssetSettings_Save`, `fa.usp_AssetClassification_SeedMerge` (folosită de migrare), `fa.usp_AssetReason_Save`, `fa.fn_AllowedTaxMethods(@ClassificationId)` (funcție inline folosită de M2 și M4).

**Erorile M1** (`65000–65099`; restul plajei pe părți: M2 `65100–65249`, M3 `65250–65399`, M4 `65400–65599`, M5 `65600–65749`, M6 `65750–65849`, M7 `65850–65949`, rezervă `65950–65999`):

| Cod | Nume (API: prefix `FA_`) | HTTP | Când |
| --- | --- | --- | --- |
| 65001 | `FA_TAX_LIFE_OUT_OF_RANGE` | 422 | Durata pe regimul fiscal în afara intervalului clasei, fără motivul de bun second-hand |
| 65002 | `FA_CLASSIFICATION_NOT_SELECTABLE` | 422 | Clasă care nu e frunză sau e abrogată la data documentului |
| 65003 | `FA_GROUP_ACCOUNT_INVALID` | 422 | Cont al grupei care nu e frunză activă în planul firmei (C1) |
| 65004 | `FA_TAX_METHOD_NOT_ALLOWED` | 422 | Metodă fiscală nepermisă pentru categoria fiscală a clasei (3.6) |
| 65005 | `FA_TAX_METHOD_LOCKED` | 409 | Schimbarea metodei fiscale după prima amortizare |
| 65006 | `FA_THRESHOLD_DATE_CONFLICT` | 409 | Prag nou cu dată într-o perioadă închisă (blocant) sau cu intrări validate după dată (avertizare) |
| 65007 | `FA_BELOW_THRESHOLD` | 422 | Intrare ca mijloc fix a unui bun sub prag, fără politica 2 sau 3 |
| 65008 | `FA_SYSTEM_PROFILE_READONLY` | 409 | Modificarea unui profil livrat |
| 65009 | `FA_BOOK_IN_USE` | 409 | Dezactivarea regimului `IFRS` folosit de documente |
| 65010 | `FA_LATER_DOCUMENT_EXISTS` | 409 | Stornarea unui document peste care există documente validate sau amortizare contată pe aceleași fișe |
| 65011 | `FA_GROUP_IN_USE` | 409 | Dezactivarea unei grupe cu fișe active |
| 65012 | `FA_USEFUL_LIFE_REASON_REQUIRED` | 422 | Durata contabilă în afara intervalului fără motiv sau fără drept |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `FixedAssets.Configuration.View` / `.Manage` | Grupele, regimurile pe grupă, profilurile proprii, pragul, seriile numerelor de inventar, formatele de etichetă, setările firmei |
| `FixedAssets.Classification.Manage` | Catalogul HG 2139/2004 (doar rândurile proprii și denumirile; seed-ul e livrat) |
| `FixedAssets.Nomenclature.Manage` | Modurile de obținere, motivele, stările fizice |
| `FixedAssets.UsefulLife.OutOfRange` | Durată contabilă în afara intervalului clasei |
| `FixedAssets.Book.Ifrs` | Documente și vederi pe regimul `IFRS` |

Roluri livrate (noi, în Nucleu, pentru modulul Mijloace fixe): **Contabil mijloace fixe** (toate permisiunile M1 și documentele M2–M5, fără aprobări), **Administrator patrimoniu** (fișele, transferurile, obiectele în folosință, inventarierea, etichetele; fără valori contabile, fără configurare), **Șef administrativ** (aprobările de ieșire și casare, vederea tuturor bunurilor), **Responsabil de loc** (vede bunurile și obiectele în folosință care îi sunt atribuite, confirmă primirea, M6), **Membru comisie** (rol existent din Stocuri S6, extins cu inventarierea și casarea mijloacelor fixe, M5 și M7). Contabilul șef și Directorul economic primesc permisiunile de vedere și aprobările de reevaluare și depreciere. Tranzacțiile se administrează cu `Core.TransactionType.Manage` (Nucleu).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M1 |
| --- | --- |
| HG 2139/2004 (catalogul de clasificare și duratele normale de funcționare) | Nomenclatorul `AssetClassification` cu seed și istoric (3.4); durata fiscală în interval (2 regula 4) |
| Codul fiscal art. 28 (amortizarea fiscală) | Regimul `TAX` separat; metodele permise pe categorie (3.6): clădirile doar liniar, degresivul cu coeficienții 1,5 / 2,0 / 2,5 și trecerea pe liniar, acceleratul cu cel mult 50 % în primul an; amortizarea din luna următoare punerii în funcțiune; plafonul și cota nedeductibilă pe autoturisme (3.5) |
| Valoarea limită a mijloacelor fixe (acum 5.000 lei) | Pragul pe firmă cu istoric (3.7); rândul livrat are data activării modulului (decizia 6) |
| OMFP 1802/2014 (reglementările contabile) | Recunoașterea, componentele, amortizarea pe durata de utilizare economică, deprecierea (`29xx`), reevaluarea cu rezerva `105` și transferul în `1175`, obiectele de inventar pe `303` / `603` / `8039`; conturile din grupele livrate (3.5) |
| OMFP 2634/2015 (documente financiar-contabile) | Registrul numerelor de inventar și fișa mijlocului fix ca formulare tipizate (M7); numerele de inventar unice și nerefolosite (3.8) |
| Legea contabilității 82/1991 și OMFP 2861/2009 (inventarierea) | Inventarierea anuală a mijloacelor fixe și a obiectelor în folosință, pe locuri de folosință și responsabili (M7); etichetele (3.10) |
| SAF-T D406 (secțiunea active, `MasterFiles/Assets` și `AssetTransactions`) | Codurile de tranzacție SAF-T pe fiecare document (intrare, amortizare, reevaluare, ieșire), mapate la tipurile din 3.1; precizare la C4, aplicată pe 27.11.2026 |
| IAS 16, IAS 36, IAS 38 (doar pentru regimul `IFRS`) | Regimul opțional cu convenție de start proprie și valori separate |

Integrări externe: niciuna în M1. Interne: Nucleu (firme, locații, angajați, dimensiuni, serii, tranzacții, proprietăți, import), Finanțe C1 (validarea conturilor), C2 (chei și modele de contare), C3 (verificarea `DEPRECIATION` și concordanțele), C4 (D101, D406 active), Achiziții A5 (propunerea liniei de imobilizare prin `IAssetThresholdPolicy`), Stocuri S2 (bonul de consum pentru obiectele de inventar), Vânzări V5 (factura `FV-MF`).

**Precizări aplicate altor documente la aprobarea M1 (27.11.2026):**

- **Nucleu**: tipurile de serie `AssetInventoryNumber` și `InUseRecordNumber`; `EntityType = Asset` pentru proprietăți, cu `AppliesToAssetGroupIds`; rolurile noi din 8; pasul „Mijloace fixe” în asistentul de configurare.
- **Finanțe C2**: cheile de cont și modelele de contare din 3.9; componentele noi în `AccountingPayload` (`AssetCost`, `Depreciation`, `Impairment`, `RevaluationSurplus`, `DisposalNetValue`) și câmpurile `AssetGroupId`, `AssetId`, `InventoryNumber` pe componentă; jurnalul `MF` deja existent.
- **Finanțe C3**: verificarea `DEPRECIATION` citește `IDepreciationStatusQuery` (M3); concordanțe noi: `21x` / `28xx` / `29xx` față de registrul mijloacelor fixe și `8039` față de evidența de folosință.
- **Finanțe C4**: D101 primește diferențele contabil–fiscal din regimul `TAX`; D406 active citește fișele și documentele modulului.
- **Achiziții A5**: `NonStockAssetKind` primește valoarea 2 (investiție în curs, merge la `INC-FF`); propunerea pe linie folosește `IAssetThresholdPolicy`; liniile de imobilizare se contează `231.PRL` = `404` (cheile `AssetPendingIntake` și `AssetSupplierPayable`), iar intrarea M2 mută valoarea pe `21x`.
- **Stocuri S2**: bonul de consum cu obiecte de inventar publică liniile cu `PersonId` / `LocationId` primitor, consumate de `OBF-BC`; contul `8039` nu mai e trecut de varianta de model a grupei din S2, ci de evenimentul `ObiectInventarDatInFolosinta` (M6).
- **Vânzări V1, V5**: tranzacția nouă `FV-MF` „Factură de vânzare a mijloacelor fixe”, cu linia legată de fișă (`AssetId` obligatoriu), venitul pe cheia `AssetSaleIncome` și ieșirea generată în aceeași tranzacție SQL prin `IAssetDisposalPort` (M5).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Grupe și conturi | Grupele firmei cu conturile, profilurile pe regim, numărul de fișe active și valoarea lor; grupele cu conturi invalide | Contabil mijloace fixe, Contabil șef |
| Catalog HG 2139/2004 | Arborele catalogului cu duratele, clasele folosite în firmă, clasele abrogate încă folosite | Contabil mijloace fixe |
| Diferențe de durată | Fișele cu durata contabilă în afara intervalului clasei, cu motivul și cine a aprobat | Contabil șef, auditor |
| Bunuri sub prag | Fișele marcate `BelowThreshold`; fișele active sub pragul curent (informativ, după o schimbare de prag) | Contabil mijloace fixe |
| Tranzacții folosite | Ca la celelalte module: tranzacțiile modulului, numărul de documente pe lună, rolurile | Administrator |

Rapoartele operaționale (registrul numerelor de inventar, fișa, balanța pe grupe, situația amortizării, D406 active) sunt în M7.

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/m1-configurare/](ecrane/m1-configurare/), generate cu [`_unelte/machete-mijloace-fixe/`](../../../_unelte/machete-mijloace-fixe/). Meniul **Mijloace fixe** are sus Fișe mijloace fixe, Obiecte în folosință și Amortizare, apoi grupurile Intrări, Operații, Ieșiri, Obiecte de inventar, Inventariere, Clasificare și Rapoarte. Grupul Clasificare conține Catalog HG 2139, Grupe de mijloace fixe și Profiluri de amortizare.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Configurare › Documente › Tranzacții | Tab-ul comun Configurare, filtrat pe Mijloace fixe › Ieșire: tranzacția proprie `IMF-CAS-AUTO` (casare autovehicule cu radiere), cu tab-urile General, Surse, Roluri, Contare, Istoric | `m1-01-tranzactii` |
| Catalog HG 2139 | Arborele catalogului (grupă → subgrupă → clasă) cu căutare, clasa `2.2.9` selectată: durata 2–4 ani, categoria fiscală, metodele fiscale permise, grupele firmei care o folosesc, fișele active | `m1-02-catalog-hg2139` |
| Grupe de mijloace fixe | Lista grupelor firmei și fișa grupei `IT` Calculatoare și echipamente IT: conturile, clasa implicită, regimurile cu profilul și durata propusă, componentele, eticheta, seria | `m1-03-grupa-mijloace-fixe` |
| Profiluri de amortizare | Profilurile livrate și proprii; profilul `DEGR-RO` cu coeficienții și previzualizarea pe stivuitorul de 98.400 lei: liniar, degresiv și accelerat pe 8 ani, cu momentul trecerii pe liniar | `m1-04-profiluri-amortizare` |
| Configurare › Mijloace fixe | Setările firmei din 3.11 grupate (Regimuri, Intrări, Amortizare, Conservare și reevaluare, Obiecte de inventar) și pragul de 5.000 lei cu istoricul | `m1-05-configurare-mijloace-fixe` |
| Configurare › Nomenclatoare › Mijloace fixe | Modurile de obținere, motivele pe tipuri, stările fizice, formatele de etichetă; marcajul „livrat” al valorilor din seed | `m1-06-nomenclatoare` |
| Fișa mijlocului fix (formularul principal, în M1 doar structura) | Fișa stivuitorului `MF 000412` Linde E20: antetul cu starea, tab-urile General, Regimuri (contabil și fiscal, cu grupa, clasa, profilul, durata), Componente, Date tehnice, Istoric; arată ce câmpuri vin din configurarea M1 | `m1-07-fisa-structura` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.15-M1-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| M1-A1 | Fiecare comportament din 3.2 are o tranzacție de sistem după migrare; o tranzacție de sistem nu se poate șterge (`50010`), doar dezactiva | Test de integrare |
| M1-A2 | La activarea modulului, fiecare firmă primește grupele din 3.5 cu conturile valide în planul ei, seria `MF`, pragul de 5.000 lei și setările implicite; rularea migrării de două ori nu dublează nimic (`MIG-06`) | Test de integrare |
| M1-A3 | Seed-ul catalogului: fiecare frunză are `MinLifeYears <= MaxLifeYears`; o clasă abrogată nu se poate alege pe o fișă nouă (`65002`), dar rămâne pe cele vechi | Test de integrare |
| M1-A4 | Profilul degresiv pe 98.400 lei și 8 ani dă anii 24.600,00 / 18.450,00 / 13.837,50 / 10.378,13 / 7.783,59 × 3 / 7.783,60, total exact 98.400,00; acceleratul 49.200,00 apoi 7.028,57 × 6 și 7.028,58; liniarul 1.025,00 pe lună | Test unitar (calculatorul de plan, folosit și de M3) |
| M1-A5 | Pe regimul fiscal, degresivul pe o clasă din categoria Construcții e refuzat (`65004`); acceleratul pe `2.2.9` e acceptat | Test unitar |
| M1-A6 | Durata fiscală de 60 de luni pe `2.2.9` (2–4 ani) e refuzată (`65001`); durata contabilă de 60 de luni cere motiv și drept (`65012`) | Test de integrare |
| M1-A7 | Pragul: un bun de 5.890,00 lei intrat după data pragului de 5.000 e propus mijloc fix; unul de 4.990,00 e propus obiect de inventar; cu politica 2 pe grupa `IT`, al doilea poate fi mijloc fix cu `BelowThreshold = 1` | Test unitar (`IAssetThresholdPolicy`) |
| M1-A8 | Un prag nou cu dată într-o lună închisă e refuzat (`65006`) | Test de integrare (cu C3) |
| M1-A9 | O grupă cu `AssetAccountCode = 213` (neterminal, are `2131`–`2134`) e refuzată (`65003`) | Test de integrare (cu C1) |
| M1-A10 | Numerele de inventar: două intrări validate simultan primesc numere diferite și consecutive; un număr al unei intrări stornate nu se mai alocă; preluarea inițială cu numărul `MF 009999` face ca seria să continue cu `MF 010000` | Test de concurență și de integrare |
| M1-A11 | Rezolvarea contului pentru `AmortizareCalculata` pe un laptop din grupa `IT` dă `6811` = `2814`, cu `ResolvedFrom = AssetGroup` | Test unitar (cu resolver-ul C2) |
| M1-A12 | Un profil livrat nu se poate modifica (`65008`); copia lui se poate | Test de integrare |
| M1-A13 | Toate procedurile M1 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive; codul de grupă `it` se potrivește cu `IT` | Test de integrare (`TST-09`) |

Criteriile M1-A4, M1-A5 și M1-A10 sunt critice: calculatorul de plan e folosit de toată amortizarea (M3), restricțiile fiscale decid deductibilitatea, iar numerele de inventar sunt cheia registrului legal.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Perimetrul modulului | Imobilizări corporale și necorporale amortizabile, terenuri, investiții în curs (`231`, `235`) și obiecte de inventar în folosință; imobilizările financiare rămân în Finanțe | 1, 3.5 | ✓ Aprobat autonom (27.11.2026) |
| 2 | Regimurile de amortizare | Contabil (`BOOK`, singurul contat) și fiscal (`TAX`, necontat, pentru D101) mereu; IFRS opțional pe firmă, necontat, raportat prin planul alternativ C1; fiecare cu metodă, durată și valori proprii | 2 regula 3, 3.6 | ✓ Aprobat autonom (27.11.2026) |
| 3 | Catalogul HG 2139/2004 | Nomenclator separat la nivel de instalare, cu seed generat din textul oficial (unealtă `_unelte/seed-hg2139/` la implementare) și istoric pe `ValidFrom`; durata fiscală obligatoriu în interval; codurile din exemplele documentului sunt ilustrative | 3.4 | ✓ Aprobat autonom (27.11.2026) |
| 4 | Grupele de mijloace fixe | Per firmă, cu conturile (imobilizare, amortizare, depreciere, rezervă), clasa implicită și profilul pe fiecare regim; 11 grupe livrate (3.5); conturile se rezolvă în C2 prin cheile noi din 3.9 | 3.5, 3.9 | ✓ Aprobat autonom (27.11.2026) |
| 5 | Metodele de amortizare | Liniară, degresivă (coeficienți 1,5 / 2,0 / 2,5 și trecere pe liniar), accelerată (cel mult 50 % în primul an), pe unități produse (doar contabil și IFRS), fără amortizare; restricțiile legale pe categorii aplicate pe regimul fiscal; start din luna următoare PIF-ului | 3.6 | ✓ Aprobat autonom (27.11.2026) |
| 6 | Pragul de încadrare | 5.000 lei, pe firmă, cu istoric și dată de aplicare; fișele existente nu se reîncadrează; sub prag implicit obiect de inventar, cu opțiunea pe grupă de a-l trata ca mijloc fix. Seed-ul pune 5.000 lei cu data activării modulului în firmă; pragurile anterioare (2.500 lei) se adaugă ca istoric la punerea în funcțiune, doar dacă firma preia bunuri intrate înainte | 3.7 | ✓ Aprobat autonom (27.11.2026) |
| 7 | Numerele de inventar | Serie a firmei (opțional pe grupă), prefix + număr, fără an; alocat la validarea intrării, unic pe tot istoricul, nerefolosit; numerele vechi acceptate la preluarea inițială; componentele cu sufix `/n` | 3.8 | ✓ Aprobat autonom (27.11.2026) |
| 8 | Tranzacțiile livrate | Cele 44 din 3.2 cu grupurile de consum din 3.3, prin Nucleu §3.6; vânzarea pornește din Vânzări cu tranzacția nouă `FV-MF`, care generează ieșirea în aceeași tranzacție SQL | 3.2, 3.3 | ✓ Aprobat autonom (27.11.2026) |
| 9 | Nomenclatoarele | Tabele separate cu seed `MERGE` doar de inserare: moduri de obținere, motive pe tipuri, stări fizice, formate de etichetă; datele tehnice prin proprietățile Nucleu (`EntityType = Asset`, pe grupă) | 3.10 | ✓ Aprobat autonom (27.11.2026) |
| 10 | Conservarea | Suspendă implicit doar amortizarea fiscală; amortizarea contabilă continuă (OMFP 1802: amortizarea nu se oprește când bunul nu e folosit), cu setare pe firmă pentru cine vrea și suspendarea contabilă | 3.11 | ✓ Aprobat autonom (27.11.2026) |
| 11 | Reevaluarea | Metoda netă implicit (eliminarea amortizării cumulate), cu metoda brută ca setare; transferul surplusului în `1175` lunar, pe măsura amortizării, implicit, sau doar la ieșire | 3.9, 3.11 | ✓ Aprobat autonom (27.11.2026) |
| 12 | Crearea fișelor din facturi | Implicit coada „Imobilizări de preluat” (contabilul generează fișele, una pe bucată); opțional ciorne automate sau validare automată când articolul are grupa și clasa | 3.11 | ✓ Aprobat autonom (27.11.2026) |
| 13 | Licențierea | Pachet separat „Active” (Mijloace fixe și obiecte de inventar), care cere doar pachetul Bază; verificarea `DEPRECIATION` din C3 apare doar când pachetul e activ | MODULE_PRINCIPALE (Licențiere) | ✓ Aprobat autonom (27.11.2026) |
| 14 | Plaja de erori și rolurile | `65000–65999` (64000 e a schemei `ids`), împărțită pe părți ca în 7; rolurile noi Contabil mijloace fixe, Administrator patrimoniu, Șef administrativ, Responsabil de loc, plus Membru comisie extins | 7, 8 | ✓ Aprobat autonom (27.11.2026) |

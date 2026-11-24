# ERP · 02.02 Finanțe · Planul de implementare a părții contabile (C1–C5)

Plan întocmit la cererea lui Aurelian (24.11.2026), din documentele validate [C1](C1-PLANURI-DE-CONTURI.md),
[C2](C2-MODELE-DE-CONTARE-SI-NOTE.md), [C3](C3-INCHIDERI.md), [C4](C4-DECLARATII.md), [C5](C5-RAPOARTE.md). Ca la partea financiară,
fiecare pas e mic: commit, push pe `main`, CI verde, nota de implementare în documentul fazei și rândul în [README](README.md).
Plaja de erori a contabilității e `51800–51999`. Ce rămâne nefăcut la sfârșitul unui pas se trece în [RAMAS-DE-FACUT.md](RAMAS-DE-FACUT.md).

## 1. Ordinea și de ce diferă de numerotarea documentelor

Documentele sunt numerotate C1…C5, dar dependențele cer o ordine puțin diferită (**decisă de Aurelian, 24.11.2026**):

```
C1 Plan de conturi ──► C3a Perioade ──► C2 Note și contare ──► C5a Balanță și fișa contului
                                                     │
                                                     ├──► C3 Închideri (restul) ──► C4 Declarații ──► C5 Situații financiare și grup
                                                     └──► C1b Mapare pe planurile alternative (la validarea notei)
```

- **Perioadele (din C3) înaintea notelor (C2)**: contarea trebuie să știe dacă luna e deschisă, închisă operativ sau închisă (coada
  „De contat”, C2 4.4), iar Nucleul are deja comportamentul `PeriodLockBehavior` care așteaptă o implementare reală a lui `IPeriodLock`.
- **Balanța și fișa contului (din C5) imediat după note**: sunt instrumentele cu care se verifică tot ce urmează (închideri,
  concordanțe, declarații); soldurile materializate (`AccountPeriodBalance`) vin la închiderea lunii (C3).
- **Maparea pe planurile alternative** se scrie la validarea notei, deci se leagă după C2, deși regulile se definesc în C1.

## 2. Etapele

### C1 — Plan de conturi și mapări

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C1.1 | Schema `fin.ChartOfAccounts`, `ChartOfAccountsCompany`, `GlAccount` (+ `NameHistory`, `DimensionRule`, `RoleAssignment`); planul românesc OMFP 1802/2014 livrat (clasele 1–9, gradele I și II, tipul contului, contul standard SAF-T, rolul, `ClosingTarget`, atributul fiscal pentru impozitul pe profit din C4); analiticele pe categorii (decizia 1), restrângerea pe firmă (decizia 2), conturile „doar pentru planul alternativ” (decizia 3); regulile: cont cu analitice fără note, cod neschimbabil după rulaj; legarea conturilor deja folosite în F1–F4 (`GlAccountCode` pe conturi de trezorerie, partide, efecte, reevaluare) de plan; consumarea `FirmaCreata`, `DimensiuneValoareDezactivata` | C1-A1, A2, A3 |
| C1.2 | Importul analiticelor din Excel; ecranul Plan de conturi (c1-01) cu arborele, fișa contului și istoricul | — |
| C1.3 | Planurile alternative și regulile de mapare (`AccountMappingRule`, `Source`, `Target`): prioritate unică (decizia 5), 1→N după atribute și procent (decizia 6), simulatorul; verificarea (reguli care nu se aplică, conturi cu rulaj fără mapare); importul planurilor și regulilor | C1-A6, A9 (simulatorul) |
| C1.4 | Ecranele Planuri alternative, Reguli de mapare, Verificarea mapării (c1-02…c1-04) | — |

### C3a — Perioade contabile (partea din C3 de care depinde contarea)

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C3a.1 | `fin.FiscalYear`, `fin.AccountingPeriod`, `PeriodStatusHistory`; crearea anului și a lunilor; stările Deschisă, Închisă operativ, Închisă, Blocată, cu ordinea la închidere și la redeschidere (decizia 3); `IPeriodLock` real în Nucleu: documentele modulelor refuzate din „Închisă operativ”, notele contabile manuale permise până la „Închisă” | C3-A1, A4 |
| C3a.2 | Ecranul Perioade contabile (c3-01) | — |

### C2 — Note contabile și contare

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C2.1 | `fin.Journal`, `JournalEntry`, `JournalLine`, `JournalEntryHistory`; nota manuală (ciornă, validare, storno în roșu cu setarea pentru negru), numerotarea pe jurnal, dimensiunile cerute de cont, conturile ținute de module doar cu drept separat (decizia 5), patru ochi disponibil și oprit (decizia 6); evenimentul `NotaContabilaValidata`; verificarea globală debit = credit | C2-A7, A8 |
| C2.2 | Modelele de contare (`PostingModel`, `Variant`, `Line`), cheile de cont (`AccountKey`), lanțul de rezolvare a contului (`AccountResolver`), conturile implicite, simularea `IPostingPreview` | C2-A3, A10 |
| C2.3 | Contractul `AccountingPayload` v1 în `Erp.Finance.Contracts`; blocul `Accounting` adăugat evenimentelor F1–F4 (`IncasareInregistrata`, `PlataInregistrata`, `DocumentTrezorerieStornat`, `AvansDecontareAcordat`, `DecontCheltuieliValidat`, `AlocareInregistrata` cu efect contabil, `TvaLaIncasareExigibil`, `CompensareValidata`, `EfectComercialModificat`, `DiferentaCursInregistrata`, `ReevaluareValutaraValidata`, `ReevaluareValutaraStornata`) și facturilor (`FacturaEmisa`, `FacturaFurnizorValidata`); modelele livrate pentru toate; consumatorul care contează, idempotent; stornarea la sursă cu conturile notei inițiale | C2-A1, A2, A4, A6 |
| C2.4 | Coada „De contat” (`PostingQueue`): motivul și lanțul încercat, reîncercarea, evenimentul `ContareEsuata`; documentul din lună închisă rămâne în coadă până la redeschidere (decizia 3); recontarea în lunile deschise și reclasificarea în luna curentă pentru cele închise (decizia 2); notele recurente | C2-A5, A9 |
| C1b | Maparea la validarea notei: `JournalLineAltAccount`, înghețarea perioadei (`AltChartPeriodFreeze`), recalcularea după schimbarea regulilor cu previzualizare (`AltMappingRun`); „Împarte contul” cu nota de reclasificare | C1-A4, A5, A7, A8, A10, A11 |
| C2.5 | Ecranele Note contabile, Notă manuală, Modele de contare, De contat, Conturi implicite (c2-01…c2-05) | — |

### C5a — Balanța și fișa contului

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C5a.1 | Balanța de verificare cu 10 coloane și varianta compactă (decizia 1), pe nivel (gradul I, II, analitic), plan (românesc sau alternativ), dimensiune, cu verificarea egalităților; fișa contului cu soldul după fiecare notă; cartea mare pe lună | C5-A1, A4 |
| C5a.2 | Ecranele Balanță de verificare și Fișa contului | — |

### C3 — Închideri (restul)

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C3.1 | Lista de verificări (`PeriodCloseStepDefinition`, pașii livrați și ai clientului), rularea (`PeriodCloseRun`, `RunStep`); coada „De contat” goală, fără trecere peste ea; evenimentele încă în outbox blochează; concordanțele cu evidența operativă (F3 partide, F1/F2 trezorerie; stocurile cu modulul Stocuri), cu trecerea peste diferențe cu motiv (decizia 4); închiderea operativă automată ca setare oprită (decizia 2) | C3-A2, A3, A8 |
| C3.2 | Notele de închidere: regularizarea TVA, închiderea claselor 6 și 7 în `121` lunar, cu setarea pentru închidere anuală (decizia 5); soldurile lunare materializate (`AccountPeriodBalance`, din C5) la închidere, șterse la redeschidere; redeschiderea cu refacerea notelor de închidere și contarea documentelor din coadă | C3-A5, A6, A7; C5-A2, A3 |
| C3.3 | Închiderea anului: rezerva legală (plafonul de 20%), preluarea rezultatului, soldurile reportate, blocarea anului și deblocarea doar cu rolul dedicat „Deblocare an” (decizia 6) | C3-A9, A10 |
| C3.4 | Soldurile inițiale la punerea în funcțiune (`OpeningBalanceImport`, `AccountOpeningBalance`), inclusiv la jumătatea anului cu rulajele cumulate (decizia 7), comparate cu partidele deschise din F3 | C3-A11 |
| C3.5 | Ecranele Închiderea lunii, Concordanțe, Note de închidere, Închiderea anului (c3-02…c3-05) | — |

### C4 — Declarații

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C4.1 | Registrul de TVA (`VatEntry`, `VatOperationType`) scris odată cu nota (decizia 2), inclusiv TVA la încasare; jurnalele de vânzări și de cumpărări | C4-A1, A3 |
| C4.2 | Cadrul declarațiilor (`TaxForm`, `TaxReturn`, `Line`, `File`), calendarul fiscal pe firmă, obligațiile de plată trimise în F5 ca sume planificate (decizia 7); fișierul final doar pe lună închisă (decizia 3); rândurile calculate needitabile (decizia 4); recipisa încărcată de utilizator (decizia 1); „De verificat” la redeschidere | C4-A5, A10, A11 |
| C4.3 | D300 cu XML și PDF; preluarea P300 (RO e-TVA) și diferențele peste prag, cu explicație (decizia 5) | C4-A2, A4, A7 |
| C4.4 | D394 și D390 | C4-A4 (partea D390) |
| C4.5 | D406 SAF-T lunar sau trimestrial (registru, parteneri, facturi, plăți), în flux, cu memorie limitată; fișierele anuale pentru active și la cerere pentru stocuri se completează cu Mijloace fixe și Stocuri | C4-A9 |
| C4.6 | Impozitul pe profit (fișa trimestrială și anuală, din atributul fiscal al conturilor, plus rânduri manuale cu motiv; decizia 6), nota de impozit, D100, D101; impozitul microîntreprinderii | C4-A8 |
| C4.7 | Ecranele Calendar declarații, Decont TVA, Jurnale de TVA, SAF-T, Impozit pe profit (c4-01…c4-05) | — |

### C5 — Situații financiare, rapoarte definite, grup

| Pas | Conținut | Criterii |
| --- | --- | --- |
| C5.1 | Motorul de rapoarte definite (`ReportDefinition`, rânduri cu formule pe conturi și dimensiuni), validarea formulelor (referințe circulare, conturi inexistente); rapoartele clientului cu drept separat (decizia 4) | C5-A8 |
| C5.2 | Situațiile financiare livrate și versionate (bilanț, cont de profit și pierdere, date informative, active imobilizate) pe categoria entității (decizia 2); raportarea semestrială; fișierul pentru ANAF doar pe an închis (decizia 5); blocarea anului după depunere (C3) | C5-A5, A6 |
| C5.3 | Registrul inventar anual | — |
| C5.4 | Balanța de grup pe planul alternativ, perechile intra-grup, eliminarea soldurilor și rulajelor și raportul de diferențe (decizia 3) | C5-A7 |
| C5.5 | Ecranele Situații financiare, Balanța de grup, Rapoarte definite | — |

## 3. Pe tot parcursul

| Ce | Când |
| --- | --- |
| Testele de collation (C1-A12, C2-A12, C3-A13, C4-A12, C5-A10, împreună cu F3-A15, F4-A13, F5-A14 din [RAMAS-DE-FACUT.md](RAMAS-DE-FACUT.md)) | Un pas comun, după C2, apoi la fiecare fază |
| Testele de performanță (C1-A10, C2-A11, C3-A12, C4-A9, C5-A9, plus cele din F3–F5) | Un pas comun cu generator de date, după C3; C4-A9 cu D406 |
| Validatoarele ANAF pentru XML (C4-A6, C5-A6) | Cu fiecare declarație; cer instrumentele oficiale ANAF (de procurat) |
| Notificările (`ContareEsuata` către Contabilul șef, termenele fiscale) | Cu notificările din Nucleu |

## 4. Decizii (Aurelian, 24.11.2026)

| # | Întrebare | Decizia |
| --- | --- | --- |
| 1 | Ordinea etapelor | Ordinea dependențelor din secțiunea 1 (perioadele înaintea notelor, balanța imediat după note) |
| 2 | Sursa planului de conturi livrat și a codurilor SAF-T | Surse oficiale: textul OMFP 1802/2014 actualizat (legislatie.just.ro) și nomenclatorul de conturi SAF-T publicat de ANAF, preluate ca seed |
| 3 | Documentele financiare validate înainte de C2 (doar date de test) | Nu se contează retroactiv; doar evenimentele de după pasul C2.3 |
| 4 | Validatoarele ANAF (DUKIntegrator, Java) | Rulate local la fiecare declarație nouă; în CI doar verificarea pe XSD |

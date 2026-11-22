# 02.02 · Finanțe și contabilitate

Modulul se documentează în două părți: întâi **partea financiară** (5 sub-părți, câte un document separat), apoi **partea contabilă** (plan de conturi, note contabile, reguli de contare, închideri, declarații), care vine ultima. Fiecare sub-parte se validează înainte de următoarea (decizia lui Aurelian, 15.11.2026). **Modulul e validat complet de Aurelian (15.11.2026)**: partea financiară F1–F5 și partea contabilă C1–C5, cu machetele de ecran în [ecrane/](ecrane/).

Schema SQL `fin`, prefix de cod `Erp.Finance`, plaja de erori SQL `51000–51999`, împărțită pe sub-părți: F1 `51000–51199`, F2 `51200–51399`, F3 `51400–51599`, F4 `51600–51699`, F5 `51700–51799`, contabilitate `51800–51999`.

| # | Sub-parte | Document | Stare |
| --- | --- | --- | --- |
| F1 | Casierie și conturi de trezorerie | [F1-CASIERIE.md](F1-CASIERIE.md) | ✓ Validat (15.11.2026) |
| F2 | Bancă: extrase, reconciliere, ordine de plată | [F2-BANCA.md](F2-BANCA.md) | ✓ Validat (15.11.2026) |
| F3 | Decontări cu partenerii: solduri, alocări, scadențar, compensări, TVA la încasare, legătura cu e-Factura | [F3-DECONTARI.md](F3-DECONTARI.md) | ✓ Validat (15.11.2026) |
| F4 | Multivalută: diferențe de curs, reevaluare | [F4-MULTIVALUTA.md](F4-MULTIVALUTA.md) | ✓ Validat (15.11.2026) |
| F5 | Planificare trezorerie: propuneri de plată, prognoză cash-flow | [F5-PLANIFICARE-TREZORERIE.md](F5-PLANIFICARE-TREZORERIE.md) | ✓ Validat (15.11.2026) |
| C1 | Planuri de conturi și mapări: planul românesc, planuri alternative, reguli de mapare | [C1-PLANURI-DE-CONTURI.md](C1-PLANURI-DE-CONTURI.md) | ✓ Validat (15.11.2026) |
| C2 | Modele de contare și note contabile | [C2-MODELE-DE-CONTARE-SI-NOTE.md](C2-MODELE-DE-CONTARE-SI-NOTE.md) | ✓ Validat 15.11.2026 |
| C3 | Închideri de lună și de an | [C3-INCHIDERI.md](C3-INCHIDERI.md) | ✓ Validat 15.11.2026 |
| C4 | Declarații (D300, D390, D394, D406 SAF-T, D100, D101) | [C4-DECLARATII.md](C4-DECLARATII.md) | ✓ Validat 15.11.2026 |
| C5 | Situații financiare și rapoarte | [C5-RAPOARTE.md](C5-RAPOARTE.md) | ✓ Validat 15.11.2026 |

Diagrame: [model-domeniu-f1.png](model-domeniu-f1.png), [model-domeniu-f2.png](model-domeniu-f2.png), [model-domeniu-f3.png](model-domeniu-f3.png), [model-domeniu-f4.png](model-domeniu-f4.png), [model-domeniu-f5.png](model-domeniu-f5.png).

Vezi documentul principal: [MODULE_PRINCIPALE.md](../MODULE_PRINCIPALE.md).

## Implementare

Ordinea de implementare (decizia lui Aurelian, 22.11.2026): F1, F2, F3, F4, F5, câte un pas mic cu commit, push și CI verde.

| Pas | Ce conține | Stare |
| --- | --- | --- |
| F1.1 | Proiectele `Erp.Finance.*` în gazdă; conturile de trezorerie (`fin.TreasuryAccount`, casierii, tipurile de operațiuni livrate), permisiunile și rolurile F1; API `/api/finance/treasury-accounts` | Implementat |
| F1.2 | Ecranul Conturi de trezorerie (macheta f1-01): lista grupată pe tip cu filtre, fișa cu datele generale (lucru zilnic, plafon, serii) și tab-ul Casieri; meniul Finanțe | Implementat (soldurile din listă și tab-ul Istoric vin cu documentele de casă) |

## Machete de ecran (`ecrane/`)

Machetele sunt grupate pe sub-părți, câte un folder în `ecrane/` (cerut de Aurelian, 15.11.2026): `f1-casierie/`, `f2-banca/`, `f3-decontari/`, `f4-multivaluta/`, `f5-planificare/`, `configurare/`.

Același stil, aceeași bară de sus și aceleași date demo ca machetele Nucleu (Grupul Exemplu, firma curentă Exemplu Distribuție SRL). În meniul din stânga, „Finanțe și contabilitate” are subgrupurile Trezorerie, Bancă, Decontări, Valută, Planificare (și Contabilitate, mai târziu). Date demo noi: casiera Elena Dumitru (rol Casier, vede doar Casa și Registrul de casă), conturile BCR-RON și ING-EUR, casele CASA-SEDIU, CASA-CLUJ, CASA-TM, CASA-EUR, ziua de lucru vineri 13.11.2026, Directorul financiar Victor Marin (aprobă propunerile de plată la nivelul 2).

| Lot | Ecrane | Stare |
| --- | --- | --- |
| F1 Casierie · [f1-casierie/](ecrane/f1-casierie/) | f1-01 Conturi de trezorerie · f1-02 Casa · f1-03 Document de casă · f1-04 Registrul de casă · f1-05 Avansuri spre decontare · f1-06 Decont de cheltuieli | ✓ Validat (15.11.2026) |
| F2 Bancă · [f2-banca/](ecrane/f2-banca/) | f2-01 Extrase bancare · f2-02 Extras de cont (reconciliere) · f2-03 Ordine de plată · f2-04 Reguli de potrivire | ✓ Validat (15.11.2026) |
| F3 Decontări · [f3-decontari/](ecrane/f3-decontari/) | f3-01 Solduri parteneri · f3-02 Fișa financiară a partenerului · f3-03 Alocare · f3-04 Scadențar · f3-05 Compensări · f3-06 Efecte comerciale · f3-07 Confirmări de sold · f3-08 Penalități | ✓ Validat (15.11.2026) |
| F4 + F5 · [f4-multivaluta/](ecrane/f4-multivaluta/), [f5-planificare/](ecrane/f5-planificare/) | Reevaluare valutară · Diferențe de curs · Poziția valutară · Propuneri de plată · De aprobat · Prognoză cash-flow · Sume planificate | ✓ Validat (15.11.2026) |
| C1 Planuri de conturi · [c1-planuri-conturi/](ecrane/c1-planuri-conturi/) | c1-01 Plan de conturi · c1-02 Planuri alternative · c1-03 Reguli de mapare · c1-04 Verificarea mapării | ✓ Validat (15.11.2026) |
| C2 Note contabile · [c2-note-contabile/](ecrane/c2-note-contabile/) | c2-01 Note contabile · c2-02 Notă manuală · c2-03 Modele de contare · c2-04 De contat · c2-05 Conturi implicite | ✓ Validat (15.11.2026) |
| C3 Închideri · [c3-inchideri/](ecrane/c3-inchideri/) | c3-01 Perioade contabile · c3-02 Închiderea lunii · c3-03 Concordanțe · c3-04 Note de închidere · c3-05 Închiderea anului | ✓ Validat (15.11.2026) |
| C4 Declarații · [c4-declaratii/](ecrane/c4-declaratii/) | c4-01 Calendar declarații · c4-02 Decont TVA (D300) · c4-03 Jurnale de TVA · c4-04 SAF-T (D406) · c4-05 Impozit pe profit | ✓ Validat (15.11.2026) |
| C5 Rapoarte · [c5-rapoarte/](ecrane/c5-rapoarte/) | c5-01 Balanță de verificare · c5-02 Fișa contului · c5-03 Situații financiare · c5-04 Balanța de grup · c5-05 Rapoarte definite | ✓ Validat (15.11.2026) |
| Configurare · [configurare/](ecrane/configurare/) | Grupul „Finanțe” în Administrare › Configurare, cu 5 tab-uri (Trezorerie, Bancă, Decontări, Valută, Planificare) și 11 secțiuni: cf-01 Tipuri de operațiuni · cf-02 Cheltuieli și diurnă · cf-03 Plafoane de numerar · cf-04 Profile de format bancar · cf-05 Rate de referință BNR · cf-06 Praguri · cf-07 Intervale de vechime (comune cu întârzierile din F5) · cf-08 Multivalută · cf-09 Reguli de aprobare · cf-10 Categorii de cash-flow · cf-11 Alerte de cash-flow | ✓ Validat (15.11.2026) |

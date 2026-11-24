# ERP · 02.02 Finanțe · Ce a rămas de făcut

Inventar consemnat la cererea lui Aurelian (24.11.2026), după F1–F5.2b, ca să revenim asupra lui. Sursele: notele de implementare din
[F1](F1-CASIERIE.md), [F2](F2-BANCA.md), [F3](F3-DECONTARI.md), [F4](F4-MULTIVALUTA.md), [F5](F5-PLANIFICARE-TREZORERIE.md), secțiunile
„Rămân de făcut” din F3 și F4 și criteriile de acceptare fără test, verificate în cod la aceeași dată. Când un punct se face, se taie aici
(`~~...~~`) și se scrie pasul în nota de implementare a fazei.

## 1. Treaba din Finanțe, fără dependențe de alte module

| # | Ce | Unde e descris | Observații |
| --- | --- | --- | --- |
| 1.1 | F5.3: ecranele Planificării — Propuneri de plată (f5-01), De aprobat (f5-02), Prognoză cash-flow (f5-03, grafic, tabel, scenariu prudent, instantanee, prognoză vs realizat), Sume planificate (f5-04) | F5 11 | Backend-ul e gata (F5.1, F5.2a, F5.2b) |
| 1.2 | F5.3: tab-urile din Configurare › Finanțe — Reguli de aprobare a plăților (cf-09), Intervale de întârziere și Categorii de cash-flow (cf-10), Alerte de cash-flow (cf-11), setările `Fin.Forecast.*` (CollectionMode, MinSampleInvoices, HistoryMonths, AutoSnapshot) | F5 11; nota F5.2a | Setările se citesc deja din `fin.CompanySetting` |
| 1.3 | Alegerea categoriei de cash-flow pe tipul de operațiune de casă și bancă (ecranul tipurilor de operațiuni) | Nota F5.2b | Coloana există; tipurile livrate au categoria din seed |
| 1.4 | Prognoză vs realizat: deschiderea abaterilor până la client, furnizor și document | F5 4.5 pasul 3 | Cu ecranul f5-03 |
| 1.5 | Scadențar (f3-04): vizualizarea Calendar, exportul Excel și „Trimite în propunere de plată” | F3 „Rămân de făcut” 1 | Propunerile există acum (F5.1) |
| 1.6 | Alocare (f3-03): propunerile pe combinații de facturi care dau suma plății (cel mult 5 facturi) | F3 4.3 pasul 3; F3 „Rămân de făcut” 3 | — |
| 1.7 | Potrivirea pe facturi a liniilor de extras: factura pe părțile liniei (alegerea din lista facturilor deschise), pasul 4 al potrivirii automate și validarea automată a încasărilor potrivite exact pe factură (`Fin.Bank.AutoValidateLevel = 2`) | F2 4.3 pasul 4; F3 „Rămân de făcut” 4 | Criteriul F2-A21 nu are încă test |
| 1.8 | Potrivirea încasării unui efect comercial din extras (F2 pasul 3a) și borderoul de depunere tipărit (opțional) | F2 4.3 pasul 3a; README F3.4b | — |
| 1.9 | Compensări: modificarea liniilor ciornei din ecran (acum PV-ul se reface din propunere) | README F3.4c | — |
| 1.10 | `Fin.Fx.RealizeOnTreasuryOutflow`: diferența realizată la ieșirea din contul în valută, la cursul mediu ponderat (`Source = 3`) | F4 3.4; F4 „Rămân de făcut” 2 | Oprită implicit; acum totul se regularizează la reevaluarea lunară |
| 1.11 | Setarea `Fin.Advance.SettlementWorkingDays` (termenul de decontare a avansului) din ecran; acum valoarea e fixă, 3 zile lucrătoare | Nota F1.5 | — |
| 1.12 | Setarea configurabilă a separării sarcinilor la aprobarea decontului; acum e mereu activă (51019) | F1 3.4 regula 5 | — |
| 1.13 | PDF-ul și tipărirea registrului de casă | README F1.4 | QuestPDF e deja folosit la confirmări și compensări |
| 1.14 | Importul regulilor de mapare din Excel (`ImportMappingRulesFromExcel`) | C1 6; nota C1.3 | Regulile se scriu din ecran; conturile planului alternativ se importă deja |
| 1.15 | Contarea reevaluării valutare (`ReevaluareValutaraValidata` / `Stornata`): evenimentul are doar totaluri fără partener, iar 4111 / 401 cer partenerul; adaptorul trebuie să citească liniile din `fin.FxRevaluationLine` | C2 nota C2.2 | Reevaluarea rămâne în F4, fără notă |
| 1.16 | Contarea efectelor comerciale (`EfectComercialModificat`) și a stornărilor de trezorerie (`DocumentTrezorerieStornat`, cu conturile notei inițiale) | C2 nota C2.2 | — |
| 1.17 | Diferențele de curs fără partener (schimb valutar, transferuri; sursele 2 și 3) | C2 nota C2.2 | — |
| 1.18 | Liniile de decont plătite pe o factură de furnizor (401 / 542) | C2 nota C2.2 | Restul decontului se contează |
| 1.19 | Notele recurente cu sume calculate dintr-un sold (de exemplu 1/12 din soldul `471`) | C2 3.8; nota C2.4b | Șabloanele au doar sume fixe |
| 1.20 | Asistentul „Împarte contul” (analitice noi sub un cont cu rulaj, nota de reclasificare; C1-A4) | C1 3.4; nota C1b | Analiticele se creează doar sub conturi fără rulaj |
| 1.21 | Recalcularea mapării ca job de fundal, vederea indexată a balanței alternative și testul de performanță C1-A10 | C1 4.4, 7; nota C1b | Recalcularea rulează sincron |
| 1.22 | Din machetele c2-01, c2-02, c2-04: registrul jurnal, tab-urile Document sursă / Planuri alternative / Note legate, partenerul pe linia notei manuale, efectul în planurile alternative în editor, setarea contului direct din coada De contat | C2 nota C2.5a | Contul se setează din Plan de conturi și Conturi implicite; reîncercarea automată îl preia |
| 1.23 | Din machetele c2-03, c2-05: variantele noi și condițiile lor din ecran, versiunile modelului cu data de la care se aplică, istoricul versiunilor, conturile pe grupe de articole și pe gestiuni, exportul Excel | C2 nota C2.5b | Liniile variantei se modifică pe loc; recontarea reface notele |
| 1.24 | Din machetele c5-01, c5-02: exportul Excel și PDF al balanței și fișei, ecranul cărții mari, deschiderea notei și a documentului sursă din fișă, balanța în valută | C5 nota C5a.2 | Datele sunt în API |
| 1.25 | Concordanțele rămase: băncile (soldul ultimului extras), avansurile spre decontare `542`, TVA neexigibilă `4428`, stocurile (cu modulul Stocuri, prin `ISubledgerBalanceProvider`); pașii `BANK_STATEMENTS`, `FX_REVALUATION`, pașii proprii ai clientului și închiderea operativă automată | C3 nota C3.1b | Concordanțele clienți, furnizori și casă rulează |
| 1.26 | Soldurile lunare materializate (`AccountPeriodBalance`) la închidere și ștergerea lor la redeschidere; nota de închidere rămasă fără solduri la reînchidere (de stornat) | C3 nota C3.2; C5 3.1 | Balanța citește din linii |
| 1.27 | Nucleul refuză atribuirea rolului „Deblocare an” unui utilizator cu alte roluri; șablonul „Repartizarea profitului” cu hotărârea AGA atașată; impozitul pe profit contat înainte de închiderea lui decembrie (cu C4) | C3 3.6, 8; nota C3.3 | Repartizarea se face cu o notă manuală |

## 2. Depinde de alte module sau de Nucleu

| # | Ce | Așteaptă | Unde e descris |
| --- | --- | --- | --- |
| 2.1 | Notificările: casierul și contabilul la ziua de casă lăsată deschisă, aprobatorii propunerilor (și reamintirea după 24 de ore), rolul din regula de alertă la `AlertaCashFlow`, Administratorul când rolul aprobator e gol | Notificările din Nucleu (00) | F1 4.2; F5 4.2, 4.6; nota F5.2b |
| 2.2 | PDF-ul semnat al registrului atașat zilei de casă; atașarea bonurilor la decont | Folosirea `IAttachmentStore` din Nucleu (există) | README F1.4, F1.5 |
| 2.3 | Raportul Z din POS: o singură dispoziție de încasare la evenimentul livrat de două ori (`ICashDocumentWriter`), criteriul F1-A14 | Modulul POS | Nota F1.3 |
| 2.4 | Plata prin angajat (partida de tip 10, din decont) | Facturile de furnizor din Achiziții | Nota F3 |
| 2.5 | Trecerea calculului de penalități în „Facturat” | Factura de penalități din Vânzări | README F3.4f |
| 2.6 | Rolul livrat „Agent de vânzări” | Modulul Vânzări | Nota F3.1 |
| 2.7 | Exportul Excel din Reevaluare valutară, Diferențe de curs, Poziția valutară (f4-01…f4-03) | Exporturile din Raportare | F4 „Rămân de făcut” 1 |
| 2.8 | Comparația automată a soldurilor inițiale importate cu balanța de pornire | Partea contabilă (C1–C5) | README F3 |
| 2.9 | Sumele planificate de salarii și taxe înlocuite de partide reale | HR și partea contabilă | F5 3.5 |
| 2.10 | Conectarea directă la API-urile băncilor (PSD2 / corporate) | Integrare 02.xx, ulterior | F2 |
| 2.11 | Partea contabilă a modulului: C1 Planuri de conturi, C2 Modele de contare și note, C3 Închideri, C4 Declarații, C5 Rapoarte | Fazele C1–C5 | [C1](C1-PLANURI-DE-CONTURI.md)…[C5](C5-RAPOARTE.md) |

## 3. Criterii de acceptare fără test

| # | Criteriu | Ce cere | Tip |
| --- | --- | --- | --- |
| 3.1 | F3-A1 | Soldul partenerului din partide = sold inițial + facturi − plăți, pe 10.000 de documente generate aleator | Test de proprietate |
| 3.2 | F3-A14 | Soldul unui partener pe 1.000.000 de partide în p95 sub 200 ms | Performanță |
| 3.3 | F4-A11 | Reevaluarea a 200.000 de partide: generare sub 2 minute, validare sub 30 de secunde | Performanță |
| 3.4 | F5-A13 | Prognoza zilnică pe 92 de zile, 100.000 de partide deschise, sub 3 secunde | Performanță |
| 3.5 | F3-A15, F4-A13, F5-A14 | Procedurile F3, F4, F5 pe baza case sensitive creată pe un server cu collation case insensitive (`TST-09`) | Collation |
| 3.6 | F4-A3 | Factura în valută plătită în trei tranșe, cu o reevaluare între ele, se închide cu `OpenAmountRon = 0`, iar diferențele realizate + reevaluarea dau diferența totală | Integrare (scenariul e acoperit parțial de testele F4, fără trăsătura criteriului) |
| 3.7 | F2-A21 | Validarea automată a încasării potrivite exact pe factură | Integrare, cu punctul 1.7 |
| 3.8 | F1-A14 | Raportul Z din POS, idempotent | Integrare, cu punctul 2.3 |
| 3.9 | F5-A2 | Factura într-o singură propunere activă, verificat și cu salvări simultane (acum secvențial, prin indexul unic) | Concurență |

## 4. Verificări externe

| # | Ce | Unde e descris |
| --- | --- | --- |
| 4.1 | Data și forma în vigoare a Legii 70/2015 (plafoanele de numerar) — de verificat pe legislatie.just.ro | F1, cerințe legale |
| 4.2 | Fișiere reale de extras (MT940, CAMT.053) de la bănci; testele folosesc fișiere construite după specificațiile publice | F2, TBD |

## 5. Corecturi de documentație făcute la inventar

- F4, nota F4.2b: `Fin.Fx.BlockAllocationsInRevaluedMonth` e implementată (în F4.4, din ecranul de setări cf-08), nu „încă neimplementată”.
- F3, nota F3.1: alocarea între monede diferite nu mai e refuzată din F4 (cursul încrucișat); codul 51418 rămâne pentru monede diferite fără curs încrucișat și pentru compensările cu partide în altă monedă decât procesul-verbal.

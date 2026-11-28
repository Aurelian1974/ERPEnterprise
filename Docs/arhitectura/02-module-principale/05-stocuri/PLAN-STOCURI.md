# ERP · 02.05 Stocuri · Planul de implementare (S1–S7)

Plan întocmit la cererea lui Aurelian (28.11.2026), din documentele validate [S1](S1-GESTIUNI-SI-SETARI.md) … [S7](S7-RAPOARTE-DECLARATII-CONFIGURARE.md).
Ca la Finanțe, fiecare pas e mic: commit, push pe `main`, CI verde, nota de implementare în documentul părții și rândul în [README](README.md).
Plaja de erori a modulului e `54000–54999`, împărțită pe părți (README). Ce rămâne nefăcut la sfârșitul unui pas se trece în nota pasului.

## 1. Ordinea

```
S1 Gestiuni și setări ──► S2 Mișcări (registrul, documentele, portul de stoc) ──► S3 Evaluare și cost ──► S4 Loturi și serii
                                                                                         │
                                                                    S5 Rezervări ◄───────┤
                                                                    S6 Inventariere ◄────┤
                                                                    S7 Rapoarte, D406, Intrastat, Configurare
```

Schimbarea metodei de evaluare (S1 4.5) transformă straturile de cost, deci se implementează după S3. Predarea gestiunii cu inventar (S1 4.2)
se leagă de inventarul din S6; până atunci predarea fără inventar obligatoriu funcționează complet.

## 2. Pașii părții S1

| Pas | Conținut | Criterii |
| --- | --- | --- |
| S1.1 | Proiectele `Erp.Inventory.*`; `inv.StockSettings`, `inv.Warehouse`; gestiunea ca valoare de dimensiune „gestiune” (`IDimensionLookup.EnsureAsync`, `SyncAsync` nou în Nucleu); tranzitul și setările create automat (`FirmaModificata`, idempotent); salvarea, blocarea, dezactivarea gestiunii; `GestiuneModificata`; permisiunile `Inv.*` și rolurile Șef depozit, Gestionar, Contabil stocuri; accesul pe gestiuni prin scope | S1-A1, A2, A3 (câmpurile blocate, cu marcajul primei mișcări), A4, A5 (listele), A12 (fără stoc încă), A13 |
| S1.2 | Gestionarii (titular, înlocuitori, perioade) și predarea-primirea; setările de stoc ale articolului pe firmă și pe gestiune; politica efectivă `IStockPolicyProvider` | S1-A6, A7 (fără inventar), A8, A15 (partea 1) |
| S1.3 | Depozitele, gestiunile din depozite, regulile de propunere, nivelul de evidență (firmă, punct de lucru, locație); `IWarehouseLookup` | S1-A15 (partea 2) |
| S1.4 | Ecranele Gestiuni, Gestionari și predare, Articol · tab Stoc, Depozite (s1-01, s1-02, s1-03, s1-05) | — |

## 3. Pașii părții S2

| Pas | Conținut | Criterii |
| --- | --- | --- |
| S2.1 | Registrul (`inv.StockMovement`, imuabil), soldul curent și soldurile pe zile, tipurile de mișcare și motivele livrate, `inv.usp_StockMovement_Post` (blocare, negativ pe toate datele ulterioare, idempotență pe sursă, cost mediu pe cheie), stornarea la ban, portul `IStockLedger` (Nucleu) implementat de Stocuri, evenimentele `MiscareStocValidata` și `StocNegativFortat`, data mișcării (`BackdatingDays`) | S2-A1, A2, A3, A4, A8, A9 (portul), A12, A14 |
| S2.2 | Documentele proprii: încărcare și descărcare cu motive și aprobare, bon de consum, custodie, notă de predare produse; numerotarea la validare, stornarea documentului | S2-A7, A11 |
| S2.3 | Nota de predare într-o etapă și în două etape (`TRANZIT`, `TransitShipment`), nota de primire cu diferențele și rezolvarea lor, avizul de însoțire | S2-A5, A6 |
| S2.4 | Consumatorul POS (`BonuriPOSSincronizate`), registrul de mișcări, ecranele s2-01…s2-05 | S2-A9, A13 |

## 4. Pașii părții S3

| Pas | Conținut | Criterii |
| --- | --- | --- |
| S3.1 | Motorul de cost (FIFO, LIFO, CMP, identificare directă pe lot), straturile și consumul lor, transferul care mută straturile cu data lor, stornarea care le reface, stratul negativ și regularizarea lui, coada de recalculare, recalcularea după mișcări retroactive (imediat sub 2.000 de mișcări, altfel jobul la 15 minute), `CostStocAjustat`, costul standard (coloana) | S3-A1, A2, A3, A4, A5, A7 |
| S3.2 | Costurile adăugate și diferențele de preț (documentul `CA`, `AdjustInboundCostAsync`) | S3-A6 |
| S3.3 | Gestiunile la preț cu amănuntul: `RetailAmount`, descărcarea lunară prin K (`DA`), modificarea prețului (`MP`), metoda 2 pe fiecare vânzare | S3-A8, A13 |
| S3.4 | Concordanța cu contabilitatea, pașii de închidere (`COST_RECALC_PENDING`, `RETAIL_DISCHARGE`, `NEGATIVE_STOCK`), blocul `Accounting` și modelele de contare ale Stocurilor | S3-A9, A10 |
| S3.5 | Ecranele s3-01…s3-05 și documentul de schimbare a metodei (S1 4.5, s1-04) | S1-A11 |

Pașii părților S4–S7 se detaliază la începutul fiecărei părți.

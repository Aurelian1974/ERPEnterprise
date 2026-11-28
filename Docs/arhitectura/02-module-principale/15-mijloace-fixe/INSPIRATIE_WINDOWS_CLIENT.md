# 02.15 Mijloace fixe și obiecte de inventar · Note de inspirație din „Windows Client”

Stare: **propunere, de discutat cu Aurelian** (27.11.2026). Nu e încă document de arhitectură.

Sursa: `D:\Projects\ERPEnterprise\ERPEnterprise\Docs\arhitectura\Windows Client` (calculatorul lui Aurelian, citit doar), același folder folosit la Achiziții și Vânzări. E o instalare de client Windows a unui ERP comercial românesc (Charisma Enterprise). Sursa lizibilă folosită aici e `Help.chm` (manualul de utilizare), extras cu [`_unelte/chm/chmx.py`](../../../_unelte/chm/chmx.py). Capitolele citite: „Mijloace fixe” (Fișa mijlocului fix, Bon de mișcare, Fișa de amortizare lunară, Procese verbale: recepție, punere în funcțiune, reevaluare, creștere/diminuare a valorii, corecție, punere în conservare, repunere în funcțiune, scoatere din uz, trecere în obiecte de inventar), „Dicționare” (Grupe, Clase, Motive de modificare a valorii, Moduri de obținere), „Facturi de vânzare a mijloacelor fixe”, „Articole” și „Gestiuni” (bifa „Obiecte de inventar în folosință”), „Rapoarte operaționale”.

Preluăm **idei de comportament**, nu cod, nume de tabele sau ecrane. Totul se reproiectează pe regulile noastre (monolit modular .NET + MediatR, SQL Server cu colația `Latin1_General_100_CS_AS_SC_UTF8`, Dapper + proceduri stocate, fără ORM, Bune practici), cu documentele create prin tranzacții ([Nucleu §3.6](../01-nucleu/NUCLEU.md)).

## 1. Ce acoperă modulul acolo

| Zonă | Ce face acolo |
| --- | --- |
| Fișa mijlocului fix | Document central: toate operațiile unui mijloc fix se văd pe fișă (tab „Operații”, cu deschiderea documentului). Filtre pe dată, locație, stare, denumire, număr de inventar, grupă, clasă |
| Crearea fișei | Opțional dintr-un **document sursă** (factură de aprovizionare, PV de recepție): alegi documentul, apoi linia (articolul), și dai numărul de inventar. Data recepției vine din documentul sursă |
| Multiplicare | Un buton creează fișe identice cu una existentă, diferite doar prin numărul de inventar |
| Date generale | Mod de obținere, centru de cost, departament, responsabil; tab „Date tehnice” cu caracteristici libere |
| Date de amortizare | Pe **sistem contabil** (pot fi mai multe în paralel, de ex. RAS și IAS): grupă → clasă → durata normală propusă, editabilă între minimul și maximul clasei; valoare și luni **inițial amortizate** (pentru un bun cumpărat la mâna a doua sau preluat la implementare); datele de început și sfârșit ale amortizării nu se scriu, le pune PIF-ul. Bifa „de natura obiectelor de inventar” |
| PV de punere în funcțiune | Se creează din fișă („Creare PV PIF”) și calculează cotele de amortizare pe toate lunile de funcționare |
| Fișa de amortizare lunară | Document lunar: „Preluare amortizare” aduce cotele lunii pentru toate mijloacele fixe; după validare stă la baza contării amortizării |
| PV de reevaluare | **Ocazională** (câteva mijloace fixe, pe fiecare: durată, valoare de inventar, diferență de amortizare) sau **periodică** (în masă, cu filtre pe data PIF, grupă, clasă): modificarea valorii de inventar procentual sau valoric, a valorii amortizate (procent ±), a duratei (± luni sau „=” valoare nouă), sau **capitalizarea cheltuielilor** unui centru de cost din luna curentă, repartizate proporțional cu valoarea rămasă de amortizat. Pe sistem contabil |
| PV de creștere / diminuare a valorii | Pe mijloc fix, cu motivul modificării (din dicționar), pe sistem contabil |
| PV de corecție | Schimbă proprietăți: centru de cost, departament, responsabil, mod de obținere, serie |
| Bon de mișcare | Sursă (departament, responsabil, centru de cost) → destinație, pentru unul sau mai multe mijloace fixe |
| Conservare și repunere în funcțiune | PV cu motiv, pe sistem contabil |
| Scoatere din uz | PV de casare, cu motiv, pe sistem contabil |
| Trecere în obiecte de inventar | PV care preia mijloacele fixe și le trece în obiecte de inventar |
| Vânzare | Factură de vânzare a mijloacelor fixe (în modulul Vânzări): pe linie se alege obligatoriu **numărul de inventar**, altfel bunul nu apare vândut și nu se calculează diferența dintre prețul de vânzare și valoarea rămasă |
| Obiecte de inventar | Sunt articole stocabile; „în folosință” e o gestiune bifată ca atare, deci folosința e urmărită ca stoc pe o gestiune specială |
| Dicționare | Grupe (cod, denumire, tip), clase (cod, denumire, grupă, durată minimă și maximă), motive de modificare a valorii, moduri de obținere |
| Rapoarte | Fișa mijlocului fix, balanța mijloacelor fixe pe grupe, registrul numerelor de inventar |

## 2. Ce lipsește acolo și trebuie la noi

Regimul fiscal separat de cel contabil (amortizare degresivă și accelerată doar fiscal, diferențe pentru D101), deprecierea (`291x`) distinctă de reevaluare, transferul surplusului din reevaluare (`105` → `1175`) pe măsura amortizării, componentele unui mijloc fix, investițiile în curs (`231`) cu decontarea lor, leasingul financiar, D406 active (SAF-T), inventarierea cu etichete și scanare, evidența obiectelor de inventar pe angajat (predare la plecare, din HR), închiderea lunii blocată până la contarea amortizării (C3, verificarea `DEPRECIATION`).

## 3. Ce propunem să preluăm

| # | Idee | Cum ar arăta la noi | Parte |
| --- | --- | --- | --- |
| P1 | Fișa mijlocului fix ca centru al tuturor operațiilor | Fișa (`fa.Asset`) cu tab „Istoric” care listează documentele, din legăturile Nucleu §3.6, și valorile după fiecare operație | M2 |
| P2 | Fișa creată din documentul sursă (factură, recepție), alegând linia | Intrarea se generează din linia de imobilizare a facturii (A5, `NonStockAssetKind = 1`) sau a NIR-ului, prin tranzacție de generare, cu cantitatea rămasă; o linie cu cantitate 5 dă 5 fișe | M2 |
| P3 | Multiplicarea fișelor identice | „Duplică” cu N copii și numere de inventar luate din seria de numere de inventar | M2 |
| P4 | Grupe și clase cu durata minimă și maximă; durata aleasă în interval | Catalogul HG 2139/2004 ca nomenclator separat, cu seed (grupă → subgrupă → clasă, DNU min–max); durata în afara intervalului doar cu motiv | M1 |
| P5 | Valoare și luni inițial amortizate | Preluarea inițială la implementare și bunurile la mâna a doua, cu valoarea amortizată anterior pe fiecare regim | M2 |
| P6 | PIF-ul pornește amortizarea | Amortizarea începe din luna următoare PIF-ului (regula românească), planul pe luni se generează la PIF și se recalculează la orice operație de valoare sau durată | M2, M3 |
| P7 | Mai multe sisteme contabile în paralel | Două regimuri fixe pe fiecare mijloc fix: **contabil** și **fiscal** (opțional al treilea, IFRS, legat de planurile alternative C1), fiecare cu metodă, durată, valoare și amortizare proprii | M1, M3 |
| P8 | Fișa de amortizare lunară ca document de contare | Calculul lunar e un document (`AMORT` pe lună și firmă), cu linii pe mijloc fix și regim, contat prin C2 (`6811` = `281x`); închiderea lunii (C3 `DEPRECIATION`) îl cere validat | M3 |
| P9 | Reevaluarea ocazională și periodică, cu cele patru metode | Reevaluare la valoare justă (raport de evaluator) pe mijloc fix sau în masă, cu metoda brută sau netă, surplusul în `105` și transferul lunar sau la ieșire în `1175`; capitalizarea cheltuielilor pe centru de cost rămâne ca „modernizare din cheltuieli” | M4 |
| P10 | Creștere / diminuare cu motiv din dicționar | Modernizare (crește valoarea, eventual durata) și depreciere (`6813` = `291x`, reversibilă), cu motive în nomenclator separat, cu seed | M4 |
| P11 | PV de corecție și bonul de mișcare | Un singur document de **transfer** (locație, responsabil, centru de cost, departament), cu PV de predare-primire tipărit; corecția doar pentru date descriptive | M4 |
| P12 | Conservare și repunere în funcțiune | Starea „în conservare” suspendă amortizarea pe regimul fiscal și, opțional, pe cel contabil (setare), cu PV | M4 |
| P13 | Scoatere din uz și vânzare cu numărul de inventar obligatoriu | Ieșirea (casare, vânzare, lipsă, donație, aport, transfer în grup) ca document al modulului; vânzarea pornește din Vânzări (factura cu linia legată de mijlocul fix) și se închide aici cu nota `6583` = `21x` și amortizarea `281x` = `21x`; materialele recuperate intră în stoc (Stocuri) | M5 |
| P14 | Trecerea în obiecte de inventar | Reclasificarea unui mijloc fix sub prag în obiect de inventar în folosință, cu PV | M5, M6 |
| P15 | Obiecte de inventar în folosință ca gestiune specială | La noi OB în folosință nu mai e stoc: bonul de consum (S2) scoate OB-ul pe cheltuială și deschide o **evidență de folosință** pe persoană și locație (extrabilanțier `8039`), cu restituire, transfer între persoane și casare | M6 |
| P16 | Dicționare: moduri de obținere, motive | Nomenclatoare separate cu seed: mod de obținere (achiziție, producție proprie, aport, donație, leasing, plus la inventar, schimb), motive de modificare, motive de ieșire | M1 |
| P17 | Rapoarte: fișa, balanța pe grupe, registrul numerelor de inventar | Plus: situația amortizării contabil vs fiscal, proiecția amortizării pe ani (pentru buget, F5), D406 active, listele de inventariere | M7 |
| P18 | Compunerea și descompunerea (cerută de Aurelian, 27.11.2026; nu există în Charisma) | **Compunere**: mai multe mijloace fixe (sau componente) se unesc într-unul nou, cu valoarea și amortizarea cumulate preluate. **Descompunere**: un mijloc fix se împarte în mai multe, cu valoarea și amortizarea repartizate (procentual, valoric sau pe cantitate). Ambele ca documente cu PV, fără efect în rezultat, cu nota de reclasificare între conturi `21x`/`281x` | M4 |

## 4. Idei de la alți producători

Aurelian a cerut (27.11.2026) inspirație și de la alți producători, nu doar din Charisma. Sursele sunt cunoștințe generale despre produse publice, nu documentație citită de pe calculatorul lui Aurelian; preluăm tot doar comportament.

| # | Producător | Idee | Cum ar arăta la noi | Parte |
| --- | --- | --- | --- | --- |
| P19 | SAP S/4HANA (Asset Accounting) | **Arii de amortizare** paralele pe același activ (contabil, fiscal, IFRS, grup), fiecare cu cheie de amortizare, durată și conturi; una singură e contată în registrul principal, celelalte doar raportate | Confirmă P7: regimurile `BOOK`, `TAX`, opțional `IFRS`; regimul fiscal nu generează note, doar diferențe pentru D101 | M1, M3 |
| P20 | SAP | **Subnumere de activ** pentru componente și modernizări ulterioare (aceeași fișă principală, valori separate) | Componentele ca subfișe (`ParentAssetId`), amortizate separat sau cu părintele (setare pe grupă) | M2, M4 |
| P21 | SAP | **Investiții în curs (AuC)** decontate printr-o regulă de repartizare pe unul sau mai multe active finale | Fișă de investiție în curs (`231`) care acumulează facturile și bonurile de consum, decontată pe mijloace fixe cu repartizare procentuală sau valorică | M2 |
| P22 | SAP, Dynamics 365 | **Simularea amortizării** înainte de contare și **proiecția** pe ani | Rulare „simulare” a documentului lunar, plus proiecție pe 5 ani pentru buget și F5 | M3, M7 |
| P23 | Microsoft Dynamics 365 Finance | **Propunerea de amortizare** generată ca jurnal, revizuită și apoi contată; **profiluri de amortizare** reutilizabile (metodă, convenție, perioadă) | Profilul de amortizare ca nomenclator legat de grupă; documentul lunar e propunere până la validare | M1, M3 |
| P24 | Dynamics 365 | **Convenția de amortizare** (luna următoare, luna întreagă, jumătate de lună) | La noi fixă pe regula românească (luna următoare PIF-ului și a ieșirii), dar păstrată ca setare pe profil pentru IFRS | M1 |
| P25 | Dynamics 365, Oracle | **Împărțirea (split) și unirea (merge)** activelor, cu păstrarea istoricului | Confirmă P18: compunerea și descompunerea, cu legături între fișe în istoric | M4 |
| P26 | Oracle Fusion Assets, NetSuite | **Adăugări în masă (mass additions)**: liniile de factură de imobilizări intră într-o coadă de „de preluat”, de unde se creează fișele | Coada „Imobilizări de preluat” din liniile A4/A5 cu `NonStockAssetKind = 1`, cu generare în masă | M2 |
| P27 | Oracle, SAP | **Transferuri și ieșiri în masă** pe filtre (locație, centru de cost, grupă) | Documentele de transfer și ieșire acceptă selecție în masă, ca reevaluarea | M4, M5 |
| P28 | Odoo | **Modele de active**: categoria aduce implicit metoda, durata și conturile; fișa se creează automat la validarea facturii dacă articolul are model | Grupa sau profilul aduce valorile implicite; crearea automată a fișei din factură e opțională, per firmă | M1, M2 |
| P29 | WinMentor, SAGA, Senior ERP (producători români) | **Fișa mijlocului fix** și **registrul numerelor de inventar** în formatele cerute de OMFP 2634/2015; **fișa de evidență a obiectelor de inventar în folosință**; **etichete cu cod de bare** și inventariere cu terminal | Formulare tipizate în M7 (fișa MF), M6 (fișa OB pe persoană) și M3 (fișa de amortizare); etichete și scanare în M7 | M3, M6, M7 |
| P30 | Senior ERP, Nexus ERP | **Pragul de încadrare** ca mijloc fix configurabil în timp (valoarea limită s-a schimbat de mai multe ori) | Prag pe firmă cu dată de la care se aplică; acum **5.000 lei** (precizare Aurelian, 27.11.2026); fișele existente își păstrează încadrarea | M1 |

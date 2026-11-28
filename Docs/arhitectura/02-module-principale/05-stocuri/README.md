# 02.05 · Stocuri

**✓ Modul validat complet de Aurelian pe 26.11.2026 (S1–S7, cu ecranele).** Modulul se documentează în 7 părți, câte un document separat, fiecare cu ecranele lui. Împărțirea a fost confirmată de Aurelian pe 26.11.2026, cu trei adăugiri: depozitul ca entitate cu mai multe gestiuni și bifa „stocul se ține pe gestiune / pe depozit” (pe firmă, punct de lucru sau locație); metodele de evaluare FIFO, CMP, LIFO și identificare directă; notele de mișcare (predare, primire, încărcare, descărcare) în S2. Fiecare parte se validează înainte de următoarea.

Schema SQL `inv`, prefix de cod `Erp.Inventory`, plaja de erori SQL `54000–54999`, împărțită pe părți: S1 `54000–54099`, S2 `54100–54299`, S3 `54300–54449`, S4 `54450–54549`, S5 `54550–54649`, S6 `54650–54799`, S7 `54800–54899`, rezervă `54900–54999`.

| # | Parte | Document | Stare |
| --- | --- | --- | --- |
| S1 | Gestiuni, depozite și setări: tipuri de gestiune, depozite și nivelul de evidență, gestionari și predare, setările firmei și ale articolului, stoc negativ, acces | [S1-GESTIUNI-SI-SETARI.md](S1-GESTIUNI-SI-SETARI.md) | ✓ Validat (26.11.2026) |
| S2 | Mișcări și documente: registrul de mișcări, note de predare și de primire (o etapă sau două, prin TRANZIT), încărcări și descărcări cu motive, bon de consum, notă de predare produse, aviz de însoțire, custodie, mișcările din alte module prin portul de stoc, stornare | [S2-MISCARI-SI-DOCUMENTE.md](S2-MISCARI-SI-DOCUMENTE.md) | ✓ Validat (26.11.2026) |
| S3 | Evaluare și cost: FIFO, CMP, LIFO, identificare directă, preț cu amănuntul, costuri adăugate, contare prin C2, concordanța 371 | [S3-EVALUARE-SI-COST.md](S3-EVALUARE-SI-COST.md) | ✓ Validat (26.11.2026) |
| S4 | Loturi, serii, termene de valabilitate, FEFO, carantină, trasabilitate | [S4-LOTURI-SERII-TERMENE.md](S4-LOTURI-SERII-TERMENE.md) | ✓ Validat (26.11.2026) |
| S5 | Rezervări și disponibil | [S5-REZERVARI-SI-DISPONIBIL.md](S5-REZERVARI-SI-DISPONIBIL.md) | ✓ Validat (26.11.2026) |
| S6 | Inventariere: liste, diferențe, perisabilități, compensări, proces-verbal | [S6-INVENTARIERE.md](S6-INVENTARIERE.md) | ✓ Validat (26.11.2026) |
| S7 | Rapoarte, declarații (D406 stocuri, Intrastat) și Configurare › Stocuri | [S7-RAPOARTE-DECLARATII-CONFIGURARE.md](S7-RAPOARTE-DECLARATII-CONFIGURARE.md) | ✓ Validat (26.11.2026) |

Vezi documentul principal: [MODULE_PRINCIPALE.md](../MODULE_PRINCIPALE.md).

## Implementare

Planul pașilor: [PLAN-STOCURI.md](PLAN-STOCURI.md). Detaliile fiecărui pas sunt în „Note de implementare” din documentul părții.

| Pas | Conținut | Stare |
| --- | --- | --- |
| S1.1 | Modulul `Erp.Inventory`, gestiunile (cod unic pe firmă, tipuri, locație, preț cu amănuntul, blocare, dezactivare, dimensiunea „gestiune”), tranzitul și setările de stoc ale firmei create automat, permisiunile și rolurile; testele S1-A1–A5, A12, A13 | Implementat (stocul la dezactivare cu S2) |
| S1.2 | Gestionarii (titular, înlocuitori, perioade), predarea-primirea cu blocarea gestiunii, setările de stoc ale articolului pe firmă și pe gestiune, politica efectivă `IStockPolicyProvider`; testele S1-A6, A7, A8, A15 (partea 1) | Implementat (inventarul de predare cu S6) |
| S1.3 | Depozitele (dimensiunea „depozit”), gestiunile din depozite, regulile de propunere a gestiunii liniei, nivelul de evidență pe firmă, punct de lucru sau locație, `IWarehouseLookup`; testele 3.8 și S1-A15 (legăturile care contrazic nivelul) | Implementat (stocul la schimbarea nivelului cu S2) |
| S1.4 | Ecranele Gestiuni (s1-01, cu tab-ul Gestionari și predarea s1-02), Depozite (s1-05), tab-ul Stoc pe fișa articolului (s1-03); meniul Stocuri | Implementat (s1-04 cu S3; tab-urile Contabilitate și Articole ale gestiunii, valorile de stoc din listă cu S2/S3) |

## Machete de ecran (`ecrane/`)

Același stil, aceeași bară de sus și aceleași date demo ca machetele Nucleu și Finanțe (Grupul Exemplu, firma curentă Exemplu Distribuție SRL). Generatoare: `/_unelte/machete-stocuri/` (`sh.py` + `sN.py`, `render.js`). Date demo noi: contabila de stocuri Simona Iordache, șeful de depozit Gheorghe Ilie, gestiunile DC-PL, AMB-PL, CUST-PL (Ploiești, nivel „depozit”, halele HALA-A și HALA-B), DEP-CJ (fără cont de stoc, cauza lui BC 10255), MAG-CJ (preț cu amănuntul), DEP-TM (blocată pentru predarea PG 0007), TRANZIT, LA-LOG; responsabilul de calitate Mihaela Dobre, loturile APA05 din DEP-CJ (L2609-11, L2610-18, L2611-07, L2611-15 în carantină), dozatoarele DZ20 pe serie; stoc propriu în 371: 4.215.500,00 lei.

| Lot | Ecrane | Stare |
| --- | --- | --- |
| S1 · [s1-gestiuni/](ecrane/s1-gestiuni/) | s1-01 Gestiuni · s1-02 Gestionari și predare · s1-03 Articol, tab Stoc · s1-04 Schimbare metodă de evaluare · s1-05 Depozite și nivelul de evidență | ✓ Validat (26.11.2026) |
| S2 · [s2-miscari/](ecrane/s2-miscari/) | s2-01 Note de predare · s2-02 Notă de primire · s2-03 Încărcări și descărcări · s2-04 Bon de consum · s2-05 Registrul de mișcări | ✓ Validat (26.11.2026) |
| S3 · [s3-evaluare/](ecrane/s3-evaluare/) | s3-01 Costuri adăugate · s3-02 Recalculare cost · s3-03 Descărcare la preț cu amănuntul · s3-04 Modificare de preț · s3-05 Straturi de cost | ✓ Validat (26.11.2026) |
| S4 · [s4-loturi-serii/](ecrane/s4-loturi-serii/) | s4-01 Loturi · s4-02 Expirări · s4-03 Alegerea loturilor (FEFO) · s4-04 Serii · s4-05 Trasabilitate și retragere | ✓ Validat (26.11.2026) |
| S5 · [s5-rezervari/](ecrane/s5-rezervari/) | s5-01 Disponibil · s5-02 Rezervări · s5-03 Cereri în așteptare · s5-04 Disponibil proiectat · s5-05 Preluarea unei rezervări | ✓ Validat (26.11.2026) |
| S6 · [s6-inventariere/](ecrane/s6-inventariere/) | s6-01 Inventare · s6-02 Listă de numărare · s6-03 Diferențe de inventar · s6-04 Perisabilități și compensări · s6-05 Finalizare și proces-verbal | ✓ Validat (26.11.2026) |
| S7 · [s7-rapoarte/](ecrane/s7-rapoarte/) | s7-01 Balanța de stocuri · s7-02 Vechimea stocului · s7-03 D406 stocuri · s7-04 Intrastat · s7-05 Configurare › Stocuri › Generale · s7-06 Configurare › Stocuri › Documente · s7-07 Stocul la o dată · s7-08 Stocul pe interval · s7-09 Fișa de magazie | ✓ Validat (26.11.2026) |

# 02.13 · Raportare și BI

**✓ Complet (27.11.2026), aprobat autonom.** Aurelian a cerut lucru autonom și, înainte de Raportare și BI, o analiză a felului în care lucrează alți producători. Analiza (Microsoft Power BI, Dynamics 365 Business Central, Odoo, Metabase, Apache Superset, plus Qlik și Tableau citite pe 28.11.2026 la cererea lui Aurelian; SSRS din cunoștințe generale) e în [INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), preluările P1–P30. Împărțirea BI1–BI5 și deciziile fiecărei părți au fost adoptate pe recomandare și marcate „✓ Aprobat autonom (27.11.2026)”.

Schema SQL `rpt`, prefix de cod `Erp.Reporting`, plaja de erori SQL `62000–62999`, împărțită pe părți: BI1 `62000–62099`, BI2 `62100–62249`, BI3 `62250–62449`, BI4 `62450–62599`, BI5 `62600–62749`, rezervă `62750–62999`. Fazele 1 → 4 din roadmap: BI1–BI3 intră în faza 1, BI4 și BI5 cresc odată cu modulele.

**Licență:** catalogul rapoartelor, rapoartele modulelor și tablourile livrate pe rol sunt în pachetul „Bază”. Generatorul de analize, tablourile proprii, alertele, ecranele mari, bugetarea și accesul extern sunt în pachetul „BI” (cere „Bază”).

**Ideea de bază:** BI-ul nu e un produs separat instalat lângă ERP. Motorul comun de rapoarte rămâne în [Nucleu](../01-nucleu/NUCLEU.md) §3.5 (`core.ReportView`, `core.ReportSchedule`). 02.13 adaugă:

- catalogul unic al rapoartelor;
- stratul semantic, cu măsurile definite o singură dată în cod;
- faptele publicate de module, ca vederi versionate;
- instantaneele nocturne;
- generatorul de analize fără SQL;
- tablourile, țintele și alertele;
- bugetarea cu plan vs realizat și control bugetar.

Schema `rpt` e singurul loc unde se fac join-uri între scheme, și doar peste vederile publicate. **Completare din 28.11.2026 (Qlik și Tableau, P25–P30):** filtrele afișează valorile fără date gri, cu istoric înapoi / înainte, iar vederile salvate au filtre dinamice și o vedere implicită (BI3 §3.6). KPI-urile se pot urmări, cu rezumat periodic și explicațiile „De ce?” calculate în SQL (BI4 §3.6). Alertele au abonare și suspendare la eroare, iar clicul într-un widget filtrează tabloul (BI4 §3.4, §3.7).

Securitatea pe rânduri vine din filtrele de vizibilitate ale modulelor (`IDataScopeResolver`) și se aplică în proceduri. Nu se folosesc componente plătite: Apache ECharts, ClosedXML și QuestPDF (licența Community, vezi mai jos).

| # | Parte | Document | Stare |
| --- | --- | --- | --- |
| BI1 | Configurare și catalog: catalogul `rpt.ReportCatalog` (coduri `<modul>.<raport>`, sincronizat din module prin `IReportCatalogContributor`), măsurile și dimensiunile (aditive, semi-aditive, neaditive; sensul „bine”; măsurile sensibile), filtrele de vizibilitate, rolurile livrate, setările | [BI1-CONFIGURARE-CATALOG.md](BI1-CONFIGURARE-CATALOG.md) | ✓ Aprobat autonom (27.11.2026) |
| BI2 | Depozitul de date: faptele publicate de module, columnstore, instantaneele nocturne (02:30) înghețate la închiderea lunii, moneda grupului și excluderea tranzacțiilor din grup, desfășurarea până la document, cache-ul, accesul extern prin `rpt.ext_*` și OData | [BI2-DEPOZIT-DATE.md](BI2-DEPOZIT-DATE.md) | ✓ Aprobat autonom (27.11.2026) |
| BI3 | Rapoarte și analize: generatorul fără SQL (rafturi, pivot, comparații, top N, grafice ECharts), exportul Excel cu pivot, rapoartele cu parametri (`rpt.ExecutiveMonthly`, `rpt.GroupKpis` etc.), pachetele de raportare într-un PDF, programarea extinsă | [BI3-RAPOARTE-ANALIZE.md](BI3-RAPOARTE-ANALIZE.md) | ✓ Aprobat autonom (27.11.2026) |
| BI4 | Tablouri, KPI și alerte: tablourile livrate pe rol și cele proprii, widget-urile, țintele proratate pe zile lucrătoare (manual sau din buget), alertele pe prag, variație, țintă și lipsă de date, ecranele mari (kiosk) | [BI4-TABLOURI-KPI-ALERTE.md](BI4-TABLOURI-KPI-ALERTE.md) | ✓ Aprobat autonom (27.11.2026) |
| BI5 | Bugetare: documentul `BUG` (tranzacții `BUG-BVC`, `BUG-VNZ`, `BUG-REV`), versiunile Inițial / Rectificat / Prognoză, secțiunile pe responsabilii centrelor de cost, plan vs realizat cu angajamente, controlul bugetar `IBudgetCheck` (avertisment sau aprobare, fără blocare), legăturile cu țintele, V1 și F5 | [BI5-BUGETARE.md](BI5-BUGETARE.md) | ✓ Aprobat autonom (27.11.2026) |

**Licența QuestPDF (BI3, decizia 8): ✓ decis de Aurelian pe 28.11.2026.** Rămâne QuestPDF cu licența Community, gratuită sub 1 milion USD venit anual brut. Licența comercială se cumpără doar dacă și când pragul e depășit. Decizia privește tot produsul, pentru că PDF-ul e generat de motorul comun.

## Cerințe primite de la alte module

- **Vânzări V7:** vederile publicate `sales.vw_SalesFact`, `sales.vw_OrderBook`, `sales.vw_OrderLineService`, `sales.vw_CommissionStatement` (contract stabil, versionat); Raportarea citește vânzările doar prin ele (precizare din Vânzări V7, 27.11.2026).
- **CRM CR5:** vederile `crm.vw_*Fact` și rapoartele CRM-R01 … R10, înregistrate în catalog.

**Precizări aplicate în celelalte documente (27.11.2026)**, fiecare într-o secțiune „Precizări din 02.13 Raportare și BI” spre finalul documentului:

- [Nucleu](../01-nucleu/NUCLEU.md): roluri, coloanele noi din `ReportSchedule`, seria `BUG`, cheia de dispozitiv „Ecran”, `GroupCompanyId`, `ReportingConnection`, `ResponsibleUserId`, `IHomeWidgetProvider`, pachetul de licență „BI”;
- [S7](../05-stocuri/S7-RAPOARTE-DECLARATII-CONFIGURARE.md): `inv.vw_StockMovementFact`, columnstore, tabloul „Depozitul”;
- [C2](../02-finante-contabilitate/C2-MODELE-DE-CONTARE-SI-NOTE.md): columnstore;
- [C5](../02-finante-contabilitate/C5-RAPOARTE.md): `fin.vw_GlLineFact`, coloana „Buget”;
- [F3](../02-finante-contabilitate/F3-DECONTARI.md): `fin.vw_OpenItemFact`;
- [F5](../02-finante-contabilitate/F5-PLANIFICARE-TREZORERIE.md): plățile estimate din buget;
- [M7](../15-mijloace-fixe/M7-INVENTARIERE-RAPOARTE.md): `fa.vw_AssetLedgerFact`;
- [V1](../03-vanzari/V1-CONFIGURARE.md): ținta agentului din buget;
- [V5](../03-vanzari/V5-FACTURARE.md): columnstore;
- [A3](../04-achizitii/A3-COMENZI.md): `purch.vw_OrderCommitment`, `IBudgetCheck`;
- [A5](../04-achizitii/A5-FACTURI.md): `IBudgetCheck`;
- [CR3](../12-crm/CR3-OPORTUNITATI-PIPELINE.md): `IKpiTargetQuery`.

[CR5](../12-crm/CR5-FISA360-RAPOARTE.md) a fost corectat: motorul de rapoarte e în Nucleu §3.5, iar catalogul e `rpt.ReportCatalog`. În [MODULE_PRINCIPALE.md](../MODULE_PRINCIPALE.md), pachetul de licență „BI avansat” a devenit „BI”.

Vezi documentul principal: [MODULE_PRINCIPALE.md](../MODULE_PRINCIPALE.md).

## Machete de ecran (`ecrane/`)

Același stil, aceeași bară de sus și aceleași date demo ca restul machetelor (Grupul Exemplu, firma curentă Exemplu Distribuție SRL). Generatoare: `/_unelte/machete-bi/` (`sh.py`, `doc.py`, `biN.py`, `render.js`; rulare `python3 biN.py | xargs node render.js`).

**Utilizatori demo:**

- George Marinescu (Director general);
- Ion Popescu (Director economic);
- Radu Stan (Șef vânzări);
- Gheorghe Ilie (responsabilul CC-LOG);
- Andrei Matei (Administrator).

**Date demo:**

- cifra de afaceri a grupului pe noiembrie, la 27.11: 5.602.900 lei, fără tranzacțiile din grup;
- tabloul Directorului general (galben la cifra de afaceri, roșu la creanțele restante peste 30 de zile, 418.300 lei);
- analiza lui Radu Stan „Marja pe categorie, Cluj și Alba” (1.441.000 lei, +6,1 %);
- raportul lunar al conducerii pe octombrie (6.322.800 lei, 101,2 % din buget) și pachetul „Închiderea lunii pentru conducere”;
- BVC 2026 (BUG 2026/02), cu transportul la +12,1 %;
- avertismentul la comanda CF 2412 (depășire 19.800 lei);
- BVC 2027 (BUG 2027/01), în întocmire pe secțiuni.

| Lot | Ecrane | Stare |
| --- | --- | --- |
| BI1 · [bi1-catalog/](ecrane/bi1-catalog/) | bi1-01 Toate rapoartele · bi1-02 Măsuri și dimensiuni · bi1-03 Configurare › Raportare | ✓ Aprobat autonom |
| BI2 · [bi2-depozit/](ecrane/bi2-depozit/) | bi2-01 Surse de date · bi2-02 Acces extern | ✓ Aprobat autonom |
| BI3 · [bi3-analize/](ecrane/bi3-analize/) | bi3-01 Generatorul de analize · bi3-02 Raportul lunar al conducerii · bi3-03 Pachete și programări | ✓ Aprobat autonom |
| BI4 · [bi4-tablouri/](ecrane/bi4-tablouri/) | bi4-01 Tabloul Directorului general · bi4-02 Editarea tabloului · bi4-03 Alerte · bi4-04 Ecranul mare din depozit · bi4-05 KPI urmărit (28.11) | ✓ Aprobat autonom |
| BI5 · [bi5-bugetare/](ecrane/bi5-bugetare/) | bi5-01 Bugetul în întocmire · bi5-02 Plan vs realizat · bi5-03 Avertismentul de control bugetar | ✓ Aprobat autonom |

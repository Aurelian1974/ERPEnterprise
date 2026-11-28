# 02.13 Raportare și BI · Cum fac alți producători (analiză înainte de documentare)

27 noiembrie 2026. Aurelian a cerut ca, înainte de Raportare și BI, să verific cum lucrează alți producători. Analiza de mai jos s-a făcut pe documentația publică a producătorilor. Am completat-o din ce știu despre produse, iar aceste completări sunt marcate ca atare. Fiecare idee preluată are un cod, P1–P24, folosit apoi în documentele BI1–BI5.

Surse consultate pe 27.11.2026:

- **Microsoft Power BI**:
  - [schema stea](https://learn.microsoft.com/en-nz/power-bi/guidance/star-schema)
  - [securitatea pe rânduri (RLS), îndrumări](https://learn.microsoft.com/power-bi/guidance/rls-guidance)
  - [implementarea RLS](https://learn.microsoft.com/en-gb/training/modules/enforce-semantic-model-security/2-row-level-security)
- **Dynamics 365 Business Central**:
  - [raportare și BI](https://learn.microsoft.com/en-us/Dynamics365/business-central/reports-bi-reporting)
  - [raportarea financiară pe categorii de conturi](https://learn.microsoft.com/en-ca/dynamics365/business-central/bi-how-work-account-schedule)
  - [aplicațiile Power BI pe arii funcționale](https://learn.microsoft.com/en-us/dynamics365/business-central/across-powerbi-apps-by-functional-area)
  - [comparația cu bugetul](https://learn.microsoft.com/hi-in/dynamics365/business-central/finance-powerbi-budget-comparison)
- **Odoo**:
  - [Spreadsheet](https://www.odoo.com/documentation/19.0/applications/productivity/spreadsheet.html)
  - [Dashboards](https://www.odoo.com/documentation/master/applications/productivity/dashboards.html)
  - [bugete](https://www.odoo.com/documentation/18.0/applications/finance/accounting/reporting/budget.html)
- **Metabase** (open source):
  - [concepte (întrebări, modele, metrici, tablouri, alerte, abonamente)](https://www.metabase.com/docs/latest/getting-started/concepts)
  - [modelarea datelor](https://www.metabase.com/docs/latest/data-modeling/start)
- **Apache Superset** (open source):
  - [stratul semantic](https://preset.io/blog/understanding-superset-semantic-layer/)
  - [securitatea](https://superset.apache.org/admin-docs/security/)
- **Qlik Sense** (citit pe 28.11.2026, la cererea lui Aurelian):
  - [modelul asociativ al selecțiilor](https://help.qlik.com/en-us/cloud-services/Subsystems/Hub/Content/Sense_Hub/Selections/associative-selection-model.htm)
  - [stările selecțiilor](https://help.qlik.com/en-US/cloud-services/Subsystems/Hub/Content/Sense_Hub/Selections/selection-states.htm)
  - [marcajele (bookmarks)](https://help.qlik.com/en-US/cloud-services/Subsystems/Hub/Content/Sense_Hub/Bookmarks/create-bookmark.htm)
- **Tableau** (citit pe 28.11.2026, la cererea lui Aurelian):
  - [Tableau Pulse, noutăți și funcții](https://help.tableau.com/current/online/en-us/pulse_intro.htm)
  - [tipurile de „insight” din Pulse](https://help.tableau.com/current/online/en-us/pulse_insights_platform_insight_types.htm)
  - [alertele pe date](https://help.tableau.com/current/pro/desktop/en-us/data_alerts.htm)
  - [acțiunile cu parametri](https://help.tableau.com/current/pro/desktop/en-us/actions_parameters.htm)
- **SSRS**: completat din ce știu despre produs (modelul de raport tipărit cu parametri și abonamente, P18).

## 1. Ce au în comun toți

| Temă | Ce fac producătorii | Concluzie pentru noi |
| --- | --- | --- |
| Modelul de date | Power BI recomandă schema stea: tabele de fapte (tranzacții la granularitate fixă) și dimensiuni conformate (dată, client, articol), relații unu-la-mulți | Vederi de fapte `vw_*Fact` publicate de fiecare modul (deja cerute de V7, A7, CR5) + dimensiuni conformate în `rpt` (dată, firmă, partener, articol, agent, dimensiunile Nucleu) (P1) |
| Stratul semantic | Power BI: măsuri DAX definite o dată în model. Metabase: „models” și „metrics” oficiale. Superset: „datasets” cu metrici și coloane calculate | Catalog central de măsuri și dimensiuni cu definiție, formulă SQL, unitate, proprietar și descriere în limbaj simplu, folosit de rapoarte, tablouri și export (P2) |
| Securitate pe rânduri | Power BI și Superset: RLS prin roluri și filtre dinamice pe utilizator | Filtrele de vizibilitate ale modulelor (firmă, portofoliu, `BuyerDataScope`, `SalesDataScope`, `CrmVisibility`) aplicate în procedurile de citire, nu în interfață (P3) |
| Întrebări fără SQL | Metabase: generatorul de întrebări (alegi tabelul, filtrezi, rezumi, grupezi), apoi vizualizare; Superset: Explore | Generator de analize pe stratul semantic: măsuri × dimensiuni × filtre, tabel pivot și grafic, salvate ca vizualizări (P4) |
| Tablouri de bord | Toți: cartonașe, grafice, filtre globale, desfășurare | Tablouri compuse din widget-uri pe stratul semantic, filtre globale, desfășurare până la document (P5) |
| Alerte și abonamente | Metabase: alerte când un rezultat trece un prag; abonamente pe e-mail. SSRS: abonamente | `core.ReportSchedule` (există) + alerte pe prag pentru măsuri, cu notificare în aplicație și pe e-mail (P6) |
| Rapoarte financiare | Business Central: „financial reporting” cu rânduri pe categorii de conturi și coloane pe perioade sau buget | Există deja în C5 (`fin.ReportDefinition`); 02.13 adaugă doar coloanele de buget și comparațiile (P7) |
| Bugete | Odoo: bugete pe conturi analitice cu realizat și procent. Business Central: bugete G/L cu comparație în Power BI | Bugete de venituri și cheltuieli pe cont, centru de cost și lună, cu versiuni și aprobare; bugetul de vânzări pe agent și articol; plan vs realizat (P8) |
| Foaie de calcul | Odoo Spreadsheet: tabele pivot legate de date vii, folosite pentru bugete și tablouri | Export Excel „viu” nu fără servicii plătite; în schimb export Excel cu tabele și pivot gata făcute (ClosedXML) și un endpoint OData doar citire pentru Excel Power Query (P9) |
| Performanță | Power BI: import în memorie sau DirectQuery. Metabase și Superset: cache pe întrebare | Instantanee periodice (solduri, stoc, creanțe pe lună), indecși columnstore pe faptele mari (disponibili în SQL Server Standard), cache al rezultatelor pe raport cu durată configurabilă (P10) |
| Acces din instrumente externe | Business Central: aplicații Power BI gata făcute peste API | Utilizator SQL doar citire pe schema `rpt` și endpoint OData, ca clientul să folosească, dacă vrea, Power BI Desktop (gratuit) sau Excel; nu e o dependență (P11) |

## 2. Ce e specific unui ERP on-premise

| # | Idee | De unde | Preluare |
| --- | --- | --- | --- |
| P12 | Rapoartele fiecărui modul rămân în modul; BI-ul le adaugă un catalog unic și meniul „Rapoarte” | Business Central (Report Selection), Odoo | Catalogul `rpt.ReportCatalog`, în care modulele își înregistrează rapoartele la instalare |
| P13 | Consolidarea de grup: rapoarte pe toate firmele din aceeași bază, cu conversie în moneda grupului | Business Central (Consolidation), SAP B1 | Dimensiunea „firmă” e mereu prezentă; măsurile monetare au varianta „în moneda grupului” la cursul BNR mediu sau de închidere |
| P14 | Instantanee lunare pentru istorie (stocul la sfârșit de lună, creanțele pe vechime) | Depozitele de date clasice | Joburi nocturne care scriu instantaneele în `rpt` |
| P15 | Desfășurarea până la document | Toți ERP-ii | Fiecare rând de fapt poartă `SourceModule`, `SourceDocType`, `SourceId`; clicul deschide documentul cu permisiunile modulului |
| P16 | Tablouri pe rol livrate gata | Business Central (Role Centers), Odoo | Tablourile livrate: Director general, Director economic, Director comercial, Șef depozit, Achiziții, Manager vânzări (CRM CR5) |
| P17 | Afișarea pe televizor (mod kiosk) în depozit și în biroul de vânzări | Metabase (full-screen), Grafana | Mod „ecran mare” cu reîmprospătare automată, autentificat cu o cheie de dispozitiv (Nucleu) |
| P18 | Trimiterea automată a raportului lunar către conducere sau către bancă | SSRS (abonamente) | Programarea existentă + „pachete de raportare” (mai multe rapoarte într-un PDF) |
| P19 | Explicarea abaterilor față de buget | Power BI („key influencers”), Tableau | Desfășurarea abaterii pe dimensiuni (centru de cost → cont → document), fără modele statistice externe |
| P20 | Ținte și semafoare pe KPI | Toți | Ținte pe măsură, perioadă și dimensiune, cu praguri verde / galben / roșu |
| P21 | Graficele | Superset și Metabase folosesc biblioteci open source | Apache ECharts (licență Apache 2.0) în interfață și în PDF (imagine SVG randată pe server). PDF-ul rămâne cu QuestPDF, ales deja în proiect, cu licența Community (gratuită sub 1 milion USD venit anual); licența comercială doar la depășirea pragului (decis de Aurelian pe 28.11.2026, BI3 decizia 8) |
| P22 | Datele personale în BI | GDPR | Faptele nu conțin nume de persoane fizice sau contacte; doar identificatori (CR5 a aplicat deja regula) |
| P23 | Rapoarte legale | Toate ERP-urile românești | Rămân în modulele lor (bilanț în C5, declarații în C4, D406 în C4 și M7); 02.13 nu le dublează |
| P24 | Fără componente plătite și fără nor obligatoriu | Cerința proiectului | Fără Power BI Report Server, Tableau Server sau SSAS ca dependență; motorul rulează în aplicație, pe SQL Server |

## 2b. Qlik și Tableau (adăugat pe 28.11.2026)

| # | Idee | De unde | Preluare |
| --- | --- | --- | --- |
| P25 | Filtre „asociative”: în lista de valori a unui filtru, valorile care nu mai au date sub celelalte filtre apar gri, nu dispar; selecțiile se văd într-o bară și au istoric înapoi / înainte | Qlik: stările selectat (verde), posibil (alb), alternativ (gri deschis), exclus (gri închis) | Filtrele din generator (BI3) și filtrele globale ale tablourilor (BI4) afișează valorile fără date gri și la final, cu numărul de rânduri; bara de filtre are „înapoi” și „înainte”. Calculul e o interogare `EXISTS` pe setul de date, cu aceleași drepturi; fără motor în memorie |
| P26 | Vederi salvate cu filtre dinamice, implicite pe utilizator | Qlik: bookmarks cu căutări dinamice (de exemplu „trimestrul curent”, „vânzări > 1 milion”), „Set as default bookmark” | Vederile salvate (`core.ReportView`) acceptă filtre relative pe dată și filtre pe măsură („clienții cu cifra de afaceri pe an > 100.000 lei”); o vedere poate fi marcată „implicită pentru mine” (BI3) |
| P27 | KPI-uri urmărite, cu rezumat periodic | Tableau Pulse: metrica guvernată separat de cine o urmărește; rezumat (digest) pe e-mail | Utilizatorul „urmărește” un KPI (măsură + filtre) și primește un rezumat zilnic sau săptămânal, pe e-mail și în notificări (BI4) |
| P28 | Explicații calculate statistic, nu scrise de un model de limbaj | Tableau Pulse: schimbarea față de perioada anterioară, contribuitorii principali, valori neașteptate (în afara benzii istorice), concentrarea (câțiva membri ≥ 50 %), ritmul față de țintă | Panoul „De ce?” al KPI-ului, cu 5 explicații calculate în SQL: variația, primii contribuitori, valoarea în afara benzii pe 12 luni, concentrarea, proiecția la final de perioadă față de țintă (BI4) |
| P29 | Abonarea la o alertă existentă și suspendarea alertelor care eșuează | Tableau: „Add Me”, alertele suspendate la eroare și reluate de proprietar („Resume Alert”) | Butonul „Abonează-mă” pe alertele partajate; după 3 evaluări eșuate la rând alerta trece în „Suspendată” și proprietarul o reia (BI4) |
| P30 | Clicul într-un widget filtrează restul tabloului | Tableau: acțiunile cu parametri și de filtrare (la selectare, la trecere cu mouse-ul, din meniu) | Pe tablou, clicul pe o bară sau un rând (de exemplu o firmă) devine filtru global temporar, afișat în bara de filtre; al doilea clic îl scoate. Desfășurarea până la document rămâne pe meniul contextual (BI4) |

## 3. Ce NU preluăm (cu motiv)

- **Un instrument BI separat inclus în produs** (Metabase sau Superset instalat lângă ERP): ar aduce alt mecanism de autentificare, altă securitate pe rânduri și altă tehnologie (Java sau Python) de instalat la fiecare client. Rămânem cu motorul din aplicație și lăsăm accesul extern prin SQL doar citire și OData (P11).
- **Cuburi OLAP (SSAS)**: SSAS e în SQL Server Standard, dar adaugă un server de administrat și cere licențe pe client. Schema stea în SQL, cu columnstore, ajunge pentru volumele unui ERP de distribuție și producție.
- **Limbaj de formule propriu, de tip DAX**: măsurile se scriu în SQL de dezvoltatori și se aleg din catalog de utilizatori. Utilizatorul nu scrie formule, ca rezultatele să fie aceleași peste tot.
- **Asistenți AI care răspund la întrebări în limbaj natural**: cer servicii externe. Se pot adăuga ulterior, ca integrare opțională.
- **Rezumatele scrise de un model de limbaj** (Tableau Pulse): cer un serviciu extern și pot inventa. Explicațiile noastre sunt calculate și au text fix pe tip (P28).
- **Motorul asociativ în memorie al Qlik**: ar cere încărcarea datelor în memoria serverului. Efectul util pentru utilizator (valorile fără date afișate gri) se obține cu interogări pe SQL Server (P25).

## 4. Împărțirea propusă (adoptată autonom pe 27.11.2026)

| Parte | Conținut | Preluări |
| --- | --- | --- |
| BI1 Configurare, catalog și strat semantic | Catalogul rapoartelor tuturor modulelor, meniul „Rapoarte”, catalogul măsurilor și dimensiunilor, securitatea pe rânduri, setările, rolurile | P2, P3, P12, P22, P24 |
| BI2 Depozitul de date și sursele | Faptele publicate de module, dimensiunile conformate, calendarul, instantaneele, consolidarea și moneda grupului, performanța, accesul extern doar citire | P1, P10, P11, P13, P14, P15 |
| BI3 Rapoarte și analize | Generatorul de analize (pivot și grafic), rapoartele cu parametri, pachetele de raportare, exportul, programarea, cache-ul | P4, P9, P18, P21, P25, P26 |
| BI4 Tablouri de bord, KPI și alerte | Tablourile și widget-urile, tablourile pe rol livrate, țintele și semafoarele, alertele pe prag, modul ecran mare | P5, P6, P16, P17, P20, P27–P30 |
| BI5 Bugetare și plan vs realizat | Bugetul de venituri și cheltuieli, bugetul de vânzări, versiunile și aprobarea, realizatul, abaterile cu desfășurare, coloanele de buget în rapoartele financiare C5 | P7, P8, P19 |

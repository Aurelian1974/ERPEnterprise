# 02.12 CRM · Cum fac alți producători (analiză înainte de documentare)

27 noiembrie 2026. Aurelian a cerut ca, înainte de CRM, să verific cum lucrează alți producători de CRM-uri. Analiza de mai jos a fost făcută pe documentația publică a producătorilor și pe comparații independente; fiecare idee preluată are un cod (P1–P24), folosit în documentele CR1–CR5.

Surse consultate (27.11.2026):

- Microsoft Dynamics 365 Sales: [calificarea lead-ului și convertirea în oportunitate](https://learn.microsoft.com/en-us/dynamics365/sales/qualify-lead-convert-opportunity-sales), [ciclul de viață al oportunității](https://learn.microsoft.com/en-us/training/modules/manage-opportunities-dynamics-365-sales/4-opportunity-management-lifecycle), [fluxurile de proces](https://docs.microsoft.com/en-gb/learn/modules/explore-dynamics-365-sales/10-describe-dynamics-365-sales-business-process-flows).
- Salesforce Sales Cloud: [crearea și convertirea lead-urilor](https://trailhead.salesforce.com/en/content/learn/modules/leads_opportunities_lightning_experience/create-and-convert-leads-lightning), [lead-uri și pipeline](https://trailhead.salesforce.com/fr/content/learn/modules/sales-for-small-business/manage-leads-and-sales-pipelines), [procesul de vânzare în Salesforce](https://www.cirrusinsight.com/blog/sales-process-in-salesforce) (blog de partener).
- Odoo CRM: [scorul predictiv al lead-urilor](https://www.odoo.com/documentation/19.0/applications/sales/crm/track_leads/lead_scoring.html) (pagina s-a încărcat incomplet; completat din cunoștințele despre produs, marcate ca atare).
- Pipedrive și HubSpot: [comparație independentă 2026](https://dev.to/konabayev/pipedrive-vs-hubspot-crm-in-2026-real-comparison-1bd3), [comparația HubSpot](https://www.hubspot.com/comparisons/pipedrive-vs-hubspot).
- ERP-ul folosit anterior (Charisma, Help.chm): CRM-ul e minimal, doar „Convorbiri cu clienții” și „Istoric comunicări cu clienții” (în modulul Leasing), cu tip de discuție, motive și legături spre dosare, contracte și partener. Confirmă că trebuie construit aproape totul de la zero.

## 1. Ce au în comun toți

| Temă | Ce fac producătorii | Concluzie pentru noi |
| --- | --- | --- |
| Lead vs client | Salesforce și Dynamics: lead-ul e un contact necalificat; la calificare devine cont (firmă) + persoană de contact + oportunitate. HubSpot: totul e contact, cu etapă de ciclu de viață (subscriber → lead → MQL → SQL → opportunity → customer). Pipedrive: centrat pe „deal” | Lead separat de partener; la calificare se creează (sau se leagă) partenerul din Nucleu, persoana de contact și oportunitatea (P1) |
| Calificare | Dynamics: butoanele Calificare / Descalificare, lead-ul descalificat rămâne cu motiv și poate fi reactivat; până la 5 oportunități dintr-un lead; legarea la cont / contact existent | Calificare cu deduplicare obligatorie față de parteneri (CUI, e-mail, telefon), descalificare cu motiv, reactivare (P2, P3) |
| Etape și probabilitate | Salesforce: etape mapate la probabilitate și la categorii de prognoză (Pipeline, Best Case, Commit, Closed); „Sales Path” cu îndrumări pe etapă. Dynamics: flux de proces Calificare → Dezvoltare → Propunere → Închidere | Pipeline-uri configurabile, etape cu probabilitate implicită, categorie de prognoză și criterii de ieșire (checklist) pe etapă (P4, P5) |
| Câștig și pierdere | Dynamics: stare Deschis / Câștigat / Pierdut cu motive (pierdut, anulat, câștigat de concurent), concurentul și venitul real, redeschidere. Salesforce: coduri de motiv | Motive de câștig și de pierdere obligatorii, concurentul, redeschidere controlată (P6) |
| Kanban | Pipedrive și Dynamics (vizualizarea pipeline, „deal tracker”, pâlnie): coloane pe etape, drag-and-drop | Ecran kanban pe etape, cu valoarea pe coloană (P7) |
| Oportunități „stagnante” | Pipedrive: „deal rotting”, alertă când o oportunitate stă prea mult într-o etapă. Salesforce: lead-uri fără activitate, fără pas următor, cu termen depășit | Zile maxime pe etapă și „fără pas următor” ca semnale vizuale și în tabloul de bord (P8) |
| Activități | Toți: apel, e-mail, întâlnire, sarcină, pe o cronologie a clientului; memento-uri | Activități cu tip, termen, rezultat, pe cronologia unificată a partenerului (P9) |
| Cadențe | Salesforce (Cadences), HubSpot și Pipedrive (Sequences): pași programați (e-mail, apel, sarcină) cu oprire la răspuns | Secvențe de activități pe șablon, oprite automat la răspuns sau la schimbarea etapei (P10) |
| E-mail și calendar | HubSpot și Pipedrive: sincronizare bidirecțională e-mail, urmărire deschideri | Sincronizare cu Microsoft 365 (Graph) și IMAP / SMTP (MailKit, open source), legarea automată după adresă; fără urmărire ascunsă a deschiderilor (GDPR) (P11) |
| Scor | Odoo: scor predictiv (probabilitate calculată din istoricul câștigat / pierdut pe câmpuri alese: țară, sursă, etapă, e-mail valid…). HubSpot: scor predictiv doar la Enterprise. Dynamics: scoruri pe lead | Scor pe reguli (configurabil) din prima versiune și probabilitate statistică din istoric (calcul Bayes simplu, ca Odoo), fără servicii externe (P12) |
| Atribuire | Odoo: echipe de vânzări cu reguli de atribuire și capacitate. Salesforce: reguli de atribuire, cozi | Echipe, teritorii pe județ / segment, atribuire automată round-robin cu capacitate (P13) |
| Prognoză | Salesforce: categorii de prognoză, ajustări ale managerului; Pipedrive: prognoză pe luna de închidere | Prognoză pe lună, agent și echipă: ponderată și pe categorii, cu „commit” al agentului și ajustarea managerului (P14) |
| Campanii | Salesforce și Dynamics: campanie cu membri, răspunsuri, cost, ROI; HubSpot: marketing integrat | Campanii cu liste țintă, trimitere de e-mail în masă cu dezabonare, atribuirea oportunităților și ROI (P15) |
| Formulare web | HubSpot, Odoo: formulare de site care creează lead-uri | Endpoint pentru formulare web (cu cheie API, Nucleu) și import CSV (P16) |
| Vedere 360 | Toți: fișa contului cu tot istoricul | Fișa 360 a partenerului: CRM + oferte, comenzi, facturi, încasări, sold, restanțe (din Vânzări și Finanțe, doar citire) (P17) |

## 2. Ce e specific unui ERP (nu unui CRM izolat)

| # | Idee | De unde | Preluare |
| --- | --- | --- | --- |
| P18 | Oportunitatea continuă în **oferta din Vânzări (V3)**, nu într-o ofertă CRM separată; valoarea și etapa se actualizează din ofertă | Dynamics (Quote din Opportunity), Odoo (Quotation din CRM) | Butonul „Ofertă” creează oferta V3 legată; câștigul se marchează automat la acceptarea ofertei / comanda confirmată |
| P19 | Clientul existent: limita de credit, soldul, restanțele vizibile pe oportunitate | ERP-urile (Odoo, Dynamics cu Finance) | Panoul financiar pe oportunitate, din F3 (doar citire) |
| P20 | Oportunități de reînnoire / vânzare repetată din contracte și facturare recurentă | Salesforce (renewals), Dynamics | Generare de oportunități de reînnoire din planurile de facturare V5 care expiră |
| P21 | Reclamații și cereri după vânzare (service de bază) | Dynamics Customer Service, Odoo Helpdesk | Cazuri simple (`Case`) legate de client, factură sau lot, cu SLA de răspuns; service complet rămâne în afara modulului |
| P22 | GDPR: temeiul prelucrării, consimțământul pe canal, dezabonarea, dreptul de a fi uitat | Toți producătorii din UE; HubSpot (subscription types) | Consimțăminte pe persoană și canal, cu istoric și sursă; anonimizarea persoanei la cerere |
| P23 | Agentul de teren: vizite planificate pe rute, check-in cu locația | Pipedrive (mobile), Salesforce Maps | Vizite ca activități cu locație și raport de vizită; rutele rămân în 02.07 TMS doar dacă e licențiat |
| P24 | Fără componente plătite și fără nor obligatoriu | Cerința proiectului | Toate funcțiile (scor, sincronizare, trimitere e-mail) rulează on-premise, cu biblioteci open source (MailKit, Quartz/BackgroundService) |

## 3. Ce NU preluăm (cu motiv)

- **Asistenți AI de rezumat sau scriere de e-mailuri** (Dynamics Copilot, Salesforce Einstein): cer servicii externe; se pot adăuga ulterior ca integrare opțională.
- **Urmărirea ascunsă a deschiderii e-mailurilor** (pixel): risc GDPR; se urmăresc doar răspunsurile și clicurile pe linkuri de dezabonare.
- **Automatizări generale „low-code”** (Power Automate, Salesforce Flow): în produs folosim reguli predefinite și evenimente; extinderile se fac prin integrări.
- **Ofertă CRM separată de oferta din Vânzări**: dublează prețurile, discounturile și aprobările deja definite în V2–V3.

## 4. Împărțirea propusă (adoptată autonom, 27.11.2026)

| Parte | Conținut | Preluări |
| --- | --- | --- |
| CR1 Configurare | Pipeline-uri și etape, surse, motive, categorii de prognoză, echipe și teritorii, reguli de atribuire și de scor, tipuri de activități, șabloane, tranzacții, setări | P4, P5, P6, P12, P13 |
| CR2 Lead-uri și contacte | Captarea (formular web, import, e-mail, manual), deduplicarea, scorul, atribuirea, calificarea și descalificarea, persoanele de contact, consimțămintele GDPR | P1–P3, P12, P13, P16, P22 |
| CR3 Oportunități și pipeline | Oportunitatea, etapele, kanban, legătura cu oferta V3, câștig / pierdere, reînnoiri, prognoza | P4–P8, P14, P18–P20 |
| CR4 Activități, comunicări și campanii | Activități, memento-uri, secvențe, vizite, sincronizarea e-mail și calendar, campanii și trimiteri în masă, cazurile după vânzare | P9–P11, P15, P21, P23 |
| CR5 Fișa 360 și rapoarte | Fișa 360 a clientului, pâlnia, conversia, prognoza, activitatea, motivele de pierdere, ROI campanii, tabloul de bord | P17, P14, P15 |

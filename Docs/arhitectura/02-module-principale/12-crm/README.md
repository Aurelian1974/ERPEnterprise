# 02.12 · CRM

**✓ Complet (27.11.2026), aprobat autonom.** Aurelian a cerut lucru autonom și, înainte de CRM, o analiză a felului în care lucrează alți producători. Analiza (Microsoft Dynamics 365 Sales, Salesforce Sales Cloud, Odoo CRM, Pipedrive, HubSpot, plus ERP-ul folosit anterior, unde CRM-ul e minimal) e în [INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), preluările P1–P24. Împărțirea CR1–CR5 și deciziile fiecărei părți au fost adoptate pe recomandare și marcate „✓ Aprobat autonom (27.11.2026)”.

Schema SQL `crm`, prefix de cod `Erp.Crm`, plaja de erori SQL `63000–63999`, împărțită pe părți: CR1 `63000–63099`, CR2 `63100–63249`, CR3 `63250–63449`, CR4 `63450–63649`, CR5 `63650–63749`, rezervă `63750–63999`. Pachetul de licență: „CRM” (cere „Bază”), faza 4 din roadmap.

**Ideea de bază:** CRM-ul nu dublează datele altor module. Clientul e partenerul din Nucleu, persoana de contact e `core.PartnerContact`, agenții și echipele sunt cei din Vânzări V1, oferta e cea din V3, motivele de pierdere sunt comune cu oferta. CRM-ul adaugă ce e înainte de vânzare (lead-uri, oportunități, activități, campanii), cazurile după vânzare și fișa 360, care citește restul. Vânzările funcționează și fără CRM.

| # | Parte | Document | Stare |
| --- | --- | --- | --- |
| CR1 | Configurare: pipeline-uri cu etape, probabilitate, categorie de prognoză, prag de stagnare și criterii de ieșire; surse, motive, concurenți, tipuri de activitate; cozi și reguli de atribuire cu round-robin și capacitate; reguli de scor și probabilitatea statistică locală; șabloane de e-mail, secvență și vizită; tranzacțiile `LD`, `OP`, `CMP`, `CAZ`; setări | [CR1-CONFIGURARE.md](CR1-CONFIGURARE.md) | ✓ Aprobat autonom (27.11.2026) |
| CR2 | Lead-uri și contacte: captarea (manual, formular web cu cheie API, import, e-mail, campanie, teren), deduplicarea, îmbogățirea din ANAF, scorul, atribuirea, calificarea în partener + contact + oportunitate, descalificarea, fuziunea, profilul CRM al persoanei, consimțămintele GDPR și anonimizarea | [CR2-LEADURI-CONTACTE.md](CR2-LEADURI-CONTACTE.md) | ✓ Aprobat autonom (27.11.2026) |
| CR3 | Oportunități și pipeline: kanbanul, criteriile de ieșire, stagnarea și pasul următor, oferta din V3 legată, câștigul automat la comanda confirmată, pierderea cu motiv și concurent, reînnoirile din planurile de facturare V5, prognoza cu commit și ajustarea șefului | [CR3-OPORTUNITATI-PIPELINE.md](CR3-OPORTUNITATI-PIPELINE.md) | ✓ Aprobat autonom (27.11.2026) |
| CR4 | Activități, comunicări și campanii: activitățile și calendarul, secvențele, vizitele de teren cu raport și comandă, sincronizarea e-mailului și a calendarului (Microsoft Graph sau IMAP / SMTP cu MailKit), campaniile cu liste, consimțământ, dezabonare și ROI, cazurile după vânzare cu SLA | [CR4-ACTIVITATI-CAMPANII.md](CR4-ACTIVITATI-CAMPANII.md) | ✓ Aprobat autonom (27.11.2026) |
| CR5 | Fișa 360 și rapoarte: fișa clientului (CRM, Vânzări, Finanțe, fără copii de date), rapoartele CRM-R01 … R10, tablourile de bord pe rol, vederile `crm.vw_*Fact` pentru 02.13 | [CR5-FISA360-RAPOARTE.md](CR5-FISA360-RAPOARTE.md) | ✓ Aprobat autonom (27.11.2026) |

**Precizări aplicate în celelalte documente (27.11.2026)**, fiecare într-o secțiune „Precizări din 02.12 CRM” spre finalul documentului: [Nucleu](../01-nucleu/NUCLEU.md) (serii `LD`, `OP`, `CMP`, `CAZ`, rolul `Prospect`, persoanele de contact și anonimizarea, cheia API „Formular web”, roluri, asistent), [V1](../03-vanzari/V1-CONFIGURARE.md) (agenți și ierarhie, ținta agentului `SalesAgentTarget`, canalul `Teren`, concurenți), [V3](../03-vanzari/V3-OFERTE-COMENZI.md) (`OpportunityId` pe ofertă și în evenimente, `CompetitorId`, `CloseQuoteLost`), [V5](../03-vanzari/V5-FACTURARE.md) (reînnoiri, „Prelungește planul”), [V6](../03-vanzari/V6-RETURURI-STORNO.md) (retur din caz), [V7](../03-vanzari/V7-RAPOARTE.md) (vederile citite de fișa 360), [F3](../02-finante-contabilitate/F3-DECONTARI.md) (`IPartnerBalanceQueries`), [A5](../04-achizitii/A5-FACTURI.md) și [S2](../05-stocuri/S2-MISCARI-SI-DOCUMENTE.md) (costul campaniilor), [02.14](../14-integrari-anaf-bnr/README.md) (`IAnafCompanyLookup`).

Vezi documentul principal: [MODULE_PRINCIPALE.md](../MODULE_PRINCIPALE.md).

## Machete de ecran (`ecrane/`)

Același stil, aceeași bară de sus și aceleași date demo ca restul machetelor (Grupul Exemplu, firma curentă Exemplu Distribuție SRL). Generatoare: `/_unelte/machete-crm/` (`sh.py`, `doc.py`, `crN.py`, `render.js`; rulare `python3 crN.py | xargs node render.js`). Utilizatori demo: echipa lui Radu Stan (Șef vânzări) din Vânzări, adică Cristina Marin (key account), Vlad Georgescu (agent de teren) și Ioana Rusu (telesales), plus Diana Florea (Marketing) și Elena Dobre (Relații clienți). Date demo: lead-ul LD 000318 Hotel Belvedere Cluj (formular web, scor 60) calificat în OP 000184 (86.400 lei pe an, oferta OV 0212, comanda CV 4471), pipeline-ul „HoReCa Cluj”, reînnoirea OP 000176 din planul PR 0007, prognoza pe decembrie 2026, vizita la Pizzeria Napoli (comanda CV 4468), campania CMP 000041 „Degustare HoReCa Cluj” (ROI 248 %), reclamația CAZ 000093 Market Turda.

| Lot | Ecrane | Stare |
| --- | --- | --- |
| CR1 · [cr1-configurare/](ecrane/cr1-configurare/) | cr1-01 Pipeline-uri și etape · cr1-02 Atribuire și scor · cr1-03 Configurare › CRM | ✓ Aprobat autonom |
| CR2 · [cr2-leaduri/](ecrane/cr2-leaduri/) | cr2-01 Lista lead-urilor · cr2-02 Formularul lead-ului · cr2-03 Calificarea · cr2-04 Persoana de contact și consimțăminte | ✓ Aprobat autonom |
| CR3 · [cr3-oportunitati/](ecrane/cr3-oportunitati/) | cr3-01 Kanbanul pipeline-ului · cr3-02 Formularul oportunității · cr3-03 Închiderea ca pierdută · cr3-04 Prognoza | ✓ Aprobat autonom |
| CR4 · [cr4-activitati/](ecrane/cr4-activitati/) | cr4-01 Activitățile mele și calendarul · cr4-02 Raportul de vizită · cr4-03 Campania · cr4-04 Cazul | ✓ Aprobat autonom |
| CR5 · [cr5-fisa360/](ecrane/cr5-fisa360/) | cr5-01 Fișa 360 · cr5-02 Tabloul de bord al șefului · cr5-03 Conversia lead-urilor · cr5-04 ROI campanii | ✓ Aprobat autonom |

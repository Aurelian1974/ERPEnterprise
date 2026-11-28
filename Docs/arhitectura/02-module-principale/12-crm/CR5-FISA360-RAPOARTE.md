# ERP · 02.12 CRM · CR5 Fișa 360 a clientului, rapoarte și tabloul de bord

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P14, P15, P17). Ultima din cele 5 părți ale modulului 02.12.

Plaja de erori CR5: `63650–63749`. Documente părinte: [CR1](CR1-CONFIGURARE.md)–[CR4](CR4-ACTIVITATI-CAMPANII.md), [Vânzări V7 Rapoarte](../03-vanzari/V7-RAPOARTE.md) (vederile `sales.vw_SalesFact`, `sales.vw_OrderBook`), [Finanțe F3](../02-finante-contabilitate/F3-DECONTARI.md) (sold și restanțe), [02.13 Raportare și BI](../13-raportare-bi/README.md) (catalogul rapoartelor și tablourile; motorul comun de rapoarte e în [Nucleu](../01-nucleu/NUCLEU.md) §3.5). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- **Fișa 360** e pagina unică a unui partener în CRM: cine e, cine se ocupă de el, ce oportunități are, ce a cumpărat, cât datorează, ce s-a discutat, ce reclamații are. **Nu copiază date**: citește prin contracte din Nucleu, Vânzări, Finanțe și Stocuri, cu o cerere per panou, încărcate în paralel.
- **Rapoartele CRM** folosesc motorul comun de rapoarte din Nucleu §3.5 (filtre, export XLSX cu ClosedXML și PDF, programare pe e-mail, drepturi pe rânduri) și apar în catalogul 02.13 (`rpt.ReportCatalog`, [BI1](../13-raportare-bi/BI1-CONFIGURARE-CATALOG.md)). CR5 definește doar **ce** rapoarte și vederi SQL publică CRM-ul.
- Toate cifrele de vânzări din CRM vin din Vânzări (facturi), nu din oportunități: oportunitatea câștigată dă „ce s-a promis”, factura dă „ce s-a vândut”.

## 1. Scop și perimetru

| Zonă | Ce face CR5 | Ce NU face |
| --- | --- | --- |
| Fișa 360 | Antet, panouri, cronologie unificată, acțiuni rapide | Editarea partenerului (Nucleu) |
| Rapoarte | Pâlnia, conversia lead → oportunitate → câștig, durata ciclului, prognoza vs realizat, activitate, motive de pierdere, ROI campanii, SLA cazuri | Raportarea de vânzări (V7), BI general (02.13) |
| Tablou de bord | Tabloul de bord CRM pe rol (agent, șef, marketing) | Motorul de tablouri (02.13) |
| Vederi SQL | `crm.vw_*` pentru 02.13 | — |

## 2. Fișa 360 (P17)

### 2.1 Antetul

Denumirea, CUI, rolul (Prospect / Client), segmentul, industria, agentul din portofoliu, etichetele, semnalele (restanță peste 30 de zile, limită de credit depășită, caz deschis cu SLA depășit, firmă inactivă fiscal), persoana de contact principală cu telefon și e-mail. Acțiuni rapide: Activitate nouă, E-mail, Oportunitate nouă, Ofertă nouă (V3), Comandă nouă (V3), Caz nou.

### 2.2 Panourile

| Panou | Sursa (contract) | Ce arată |
| --- | --- | --- |
| Indicatori | `sales.vw_SalesFact` (V7), `IPartnerBalanceQueries` (F3) | Cifra de afaceri pe 12 luni și față de anul anterior, marja (doar cu permisiunea `Sales.Margin.View`), ultima factură, frecvența medie a comenzilor, soldul, restanța, zilele medii de încasare |
| Oportunități | CR3 | Deschise (etapă, valoare, dată), ultimele câștigate și pierdute |
| Oferte și comenzi | `sales.vw_OrderBook` (V7) | Ofertele deschise, comenzile nelivrate |
| Ce cumpără | `sales.vw_SalesFact` | Primele 10 articole pe 12 luni, articolele care **nu** s-au mai cumpărat în ultimele 90 de zile deși se cumpărau lunar (semnal de pierdere, „Articole oprite”) |
| Persoane de contact | Nucleu + CR2 | Rol în decizie, consimțăminte, ultima activitate |
| Cazuri | CR4 | Deschise, ultimele închise, satisfacția medie |
| Planuri recurente | V5 | Planurile active, data de expirare, reînnoirea (CR3) |
| Campanii | CR4 | Campaniile la care a participat |

### 2.3 Cronologia unificată

Lista descrescătoare din `crm.TimelineEntry` și `crm.Activity`: activități, e-mailuri, schimbări de etapă, oferte trimise și închise, comenzi, facturi, încasări, retururi, cazuri. Filtre pe tip și pe persoană. Fiecare rând deschide documentul sursă (cu permisiunile modulului sursă; utilizatorul fără drept pe facturi vede rândul „Factură emisă” fără valoare, `63651` informativ).

### 2.4 Performanța

Fișa se încarcă în sub 1,5 secunde pentru un partener cu 5 ani de istoric (cerință de performanță, test de încărcare): antetul și indicatorii din procedura `crm.usp_Partner360_Header` (o singură trecere), panourile în paralel, cronologia paginată (50 de rânduri).

## 3. Rapoartele

| Cod | Raport | Măsuri și dimensiuni |
| --- | --- | --- |
| `CRM-R01` | Pâlnia pipeline-ului | Pe etapă: număr, valoare, ponderată, rata de trecere la etapa următoare, zilele medii în etapă; filtre pipeline, agent, perioadă |
| `CRM-R02` | Conversia lead-urilor | Pe sursă, campanie, agent, lună: lead-uri, calificate, descalificate (pe motiv), oportunități, câștigate, valoare câștigată, rata lead → câștig |
| `CRM-R03` | Durata ciclului de vânzare | Zile de la creare la câștig pe pipeline, segment, agent; mediana și percentila 80 |
| `CRM-R04` | Prognoza vs realizat | Pe lună și agent: ponderat la trimitere, commit, ajustarea șefului, câștigat (CR3), facturat (V7), ținta |
| `CRM-R05` | Activitatea echipei | Pe agent și săptămână: activități realizate pe tip, vizite planificate vs realizate, oportunități fără pas următor, lead-uri neatinse |
| `CRM-R06` | Motive de pierdere și concurenți | Pe motiv, concurent, pipeline, trimestru: număr și valoare pierdută; comune cu ofertele V3 pierdute |
| `CRM-R07` | ROI campanii | Pe campanie: membri, trimise, răspunsuri, înscrieri, lead-uri, oportunități, câștigate, venit atribuit, cost, ROI |
| `CRM-R08` | Cazuri și SLA | Pe categorie, prioritate, operator: cazuri, SLA respectat %, durata medie de rezolvare, satisfacția |
| `CRM-R09` | Clienți în risc | Clienții cu cifra de afaceri pe ultimele 3 luni sub 60 % din media lunară pe 12 luni, cu articole oprite, cu cazuri recente sau restanțe; agentul din portofoliu |
| `CRM-R10` | Câștigate vs facturate | Oportunitățile câștigate pe lună și facturarea reală a acelor clienți în următoarele 90 de zile |

Toate rapoartele respectă vizibilitatea din CR1 §3.7 (filtrul pe portofoliu se aplică în procedură, nu în interfață).

## 4. Tabloul de bord CRM

| Rol | Tablouri |
| --- | --- |
| Agent | Activitățile de azi și întârziate; oportunitățile mele pe etape; prognoza lunii față de țintă; lead-urile noi atribuite; clienții mei în risc |
| Șef vânzări | Pâlnia echipei; prognoza pe agenți (commit vs ponderat vs țintă); stagnante și fără pas următor; activitatea pe agent; motive de pierdere ale trimestrului |
| Marketing | Lead-uri pe sursă și săptămână; conversia pe campanie; ROI; dezabonări |
| Relații clienți | Cazuri deschise pe prioritate, SLA în pericol, satisfacția |

Tablourile se construiesc cu componentele din 02.13 (cartonașe de indicatori, grafice, liste) și se pot personaliza de utilizator în limitele permisiunilor.

## 5. Exemplu: tabloul lui Radu Stan pe 27.11.2026

- **Pâlnia „HoReCa Cluj”:** Calificat 18 oportunități / 412.600 lei · Degustare 11 / 298.300 · Ofertă trimisă 7 / 214.900 · Test 3 / 96.200. Rata Calificat → Câștig pe ultimele 6 luni: 22 %.
- **Prognoza decembrie:** Cristina Marin commit 118.000 (ajustat 110.000), ponderat 142.310, țintă 150.000; Vlad Georgescu commit 64.000, ponderat 71.450, țintă 80.000; Ioana Rusu commit 38.000, ponderat 41.200, țintă 45.000. Echipa: commit ajustat 212.000 față de ținta 275.000 (77 %).
- **Semnale:** 4 oportunități stagnante (2 ale lui Vlad), 3 fără pas următor, 6 lead-uri neatinse de peste 2 zile în coadă.
- **Pierderi T4:** Preț 9 (184.000 lei), Concurent 6 (AquaVita 4), Fără buget 3.
- **ROI CMP 000041** (până acum): cost 5.000; venit atribuit 1.450 × 12 = 17.400; ROI 248 %.

**Fișa 360 a Pizzeriei Napoli pe 27.11.2026:** cifra de afaceri pe 12 luni 48.360 lei (+6 % față de anul anterior), sold 3.120 lei (factura FV EXD 10231, scadentă 09.12) din care restanță 0, zilele medii de încasare 18; articol oprit: cafea boabe 1 kg (cumpărată lunar până în august, 7.920 lei pe 12 luni); ultima vizită 04.11, următoarea 02.12 (Vlad Georgescu, CR4 §4.1); oportunitatea deschisă OP 000172 „Dozator apă bucătărie” 1.200 lei pe an, etapa Degustare programată.

## 6. Vederi SQL publicate pentru 02.13

| Vedere | Granularitate | Coloane principale |
| --- | --- | --- |
| `crm.vw_LeadFact` | Un rând pe lead | Data creării, sursa, campania, proprietarul, starea, motivul de descalificare, data calificării, scorul la calificare |
| `crm.vw_OpportunityFact` | Un rând pe oportunitate | Partener, pipeline, etapa curentă, valoare, ponderată, data estimată, starea, motivul, concurentul, data închiderii, durata, campania, oferta, comanda |
| `crm.vw_OpportunityStageFact` | Un rând pe trecere de etapă | Etapa, intrarea, ieșirea, zilele |
| `crm.vw_ActivityFact` | Un rând pe activitate | Tip, rezultat, proprietar, subiect, partener, data |
| `crm.vw_CampaignFact` | Un rând pe membru de campanie | Starea, răspunsul, lead, oportunitate, venit atribuit |
| `crm.vw_CaseFact` | Un rând pe caz | Categoria, prioritatea, SLA respectat, durata, satisfacția |
| `crm.vw_ForecastFact` | Un rând pe agent și lună | Ponderat, commit, ajustare, țintă, câștigat |

Vederile nu conțin date personale ale persoanelor de contact (doar `PartnerContactId`), ca BI-ul să nu răspândească date personale (GDPR, minimizare).

## 7. Schema SQL și erori

Partea CR5 nu adaugă tabele de date, doar vederile din §6, procedurile `crm.usp_Partner360_*`, `crm.usp_Report_R01` … `R10` și rapoartele înregistrate în catalogul 02.13 (`rpt.ReportCatalog`, prin `IReportCatalogContributor`, BI1).

**Erorile CR5** (`63650–63749`): `63651` `CRM_360_SOURCE_FORBIDDEN` (informativ) · `63652` `CRM_360_MODULE_NOT_LICENSED` (informativ, panoul lipsește) · `63660` `CRM_REPORT_SCOPE_DENIED`.

## 8. Permisiuni

`Crm.Partner360.View`, `Crm.Reports.View` (pe portofoliu), `Crm.Reports.ViewTeam`, `Crm.Reports.ViewCompany`, `Crm.Dashboard.Customize`. Panourile din alte module cer permisiunile lor (`Sales.Invoice.View`, `Fin.Balance.View`, `Sales.Margin.View`).

## 9. Integrări

| Modul | Ce citește CR5 |
| --- | --- |
| Vânzări V7 | `sales.vw_SalesFact`, `sales.vw_OrderBook` |
| Vânzări V5 | Planurile recurente |
| Finanțe F3 | `IPartnerBalanceQueries` |
| 02.13 | Catalogul rapoartelor (BI1) și tablourile (BI4); motorul de rapoarte și programarea sunt în Nucleu §3.5 |

## 10. Rapoarte

Secțiunea 3.

## 11. Ecrane principale

Machetele sunt în [ecrane/cr5-fisa360/](ecrane/cr5-fisa360/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Fișa 360 (principal) | Pizzeria Napoli: antet, indicatori, oportunități, ce cumpără, cronologia | `cr5-01-fisa-360` |
| Tabloul de bord al șefului | Pâlnia, prognoza pe agenți, semnale, pierderi | `cr5-02-tablou-sef` |
| Raportul de conversie | `CRM-R02` pe surse, noiembrie 2026 | `cr5-03-raport-conversie` |
| ROI campanii | `CRM-R07` pe campaniile anului | `cr5-04-roi-campanii` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| CR5-A1 | Fișa 360 a Pizzeriei Napoli arată cifrele din §5 pe datele de test | Test de integrare |
| CR5-A2 | Fișa se încarcă sub 1,5 secunde pentru un partener cu 5 ani de istoric (20.000 de facturi) | Test de performanță |
| CR5-A3 | Utilizatorul fără drept pe marjă nu vede marja în indicatori | Test de integrare |
| CR5-A4 | Agentul vede în `CRM-R01` doar oportunitățile din portofoliu | Test de integrare |
| CR5-A5 | `CRM-R07` dă ROI 248 % pentru CMP 000041 | Test de integrare |
| CR5-A6 | Fără Finanțe licențiate, panoul de sold lipsește fără eroare (`63652`) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Fișa 360 | Citire în timp real prin contracte, fără copii de date | ✓ Aprobat autonom (27.11.2026) |
| 2 | Cifrele de vânzări | Din facturi (V7), nu din oportunități | ✓ Aprobat autonom |
| 3 | Motorul de rapoarte | Cel comun (Nucleu §3.5), cu catalogul și tablourile din 02.13 | ✓ Aprobat autonom |
| 4 | Vederile pentru BI | `crm.vw_*Fact`, fără date personale | ✓ Aprobat autonom |
| 5 | Clienții în risc | Reguli simple (scădere a cifrei de afaceri, articole oprite, cazuri, restanțe), fără modele externe | ✓ Aprobat autonom |

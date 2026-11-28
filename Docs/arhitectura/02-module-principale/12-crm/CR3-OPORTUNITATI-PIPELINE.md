# ERP · 02.12 CRM · CR3 Oportunități și pipeline: etape, kanban, oferta din Vânzări, câștig și pierdere, reînnoiri, prognoză

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P4–P8, P14, P18–P20). A treia din cele 5 părți ale modulului 02.12.

Plaja de erori CR3: `63250–63449`. Documente părinte: [CR1 Configurare](CR1-CONFIGURARE.md) (pipeline-uri, etape, motive, setări), [CR2 Lead-uri](CR2-LEADURI-CONTACTE.md) (calificarea creează oportunități), [Vânzări V3](../03-vanzari/V3-OFERTE-COMENZI.md) (oferta și comanda), [V5](../03-vanzari/V5-FACTURARE.md) (planurile de facturare recurentă), [Finanțe F3](../02-finante-contabilitate/F3-DECONTARI.md) (soldul și restanțele partenerului, doar citire). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- **Oportunitatea** (`crm.Opportunity`, document `OP`) e o vânzare posibilă către un partener din Nucleu (prospect sau client), cu valoare estimată, dată estimată de închidere, etapă în pipeline și proprietar (agent V1).
- **Oferta nu se face în CRM.** Butonul „Ofertă” deschide oferta din Vânzări V3, legată de oportunitate (`SalesQuote.OpportunityId`, precizare în V3). Prețurile, discounturile, aprobările și PDF-ul sunt cele din V2–V3. CRM-ul află ce se întâmplă cu oferta din evenimentele `OfertaTrimisa` și `OfertaInchisa`, iar cu comanda din `ComandaConfirmata`.
- **Câștigul se marchează automat** când comanda din oferta legată e confirmată (`AutoWinOnOrder = 1`), sau manual, cu motiv. **Pierderea** cere motivul (`ReasonCode` tip 1, comun cu V3) și, opțional, concurentul.
- **Reînnoirile** se generează din planurile de facturare recurentă V5 care se apropie de `EndDate` (P20).
- Toate valorile din CRM sunt **estimări fără efect contabil**. Nu se fac înregistrări contabile din CRM.

## 1. Scop și perimetru

| Zonă | Ce face CR3 | Ce NU face |
| --- | --- | --- |
| Oportunitatea | Creare (din lead, manuală, din fișa clientului, reînnoire), etapă, valoare, produse estimate, persoane implicate, concurenți | Prețul final (V2–V3) |
| Pipeline | Kanban pe etape cu drag-and-drop, lista, criteriile de ieșire, semnalele „stagnant” și „fără pas următor” | Configurarea etapelor (CR1) |
| Oferta | Crearea ofertei V3 din oportunitate, actualizarea valorii și etapei din evenimentele ofertei | Oferta propriu-zisă (V3) |
| Închidere | Câștig (automat sau manual), pierdere cu motiv și concurent, redeschidere controlată | Comanda (V3) |
| Reînnoiri | Oportunități `OP-REN` din planurile V5 care expiră | Planul (V5) |
| Prognoza | Prognoza pe lună, agent și echipă: ponderată și pe categorii, cu „commit” al agentului și ajustarea șefului | Bugetul de vânzări (V7 / 02.13) |

## 2. Model de domeniu

```
 crm.Opportunity (OP 000184)
   ├─ PartnerId (core.Partner) · PrimaryContactId (core.PartnerContact) · OwnerAgentId (sales.SalesAgent)
   ├─ PipelineId · StageId · StageEnteredAt · Probability · ForecastCategory
   ├─ EstimatedAmount (lei, fără TVA) · RecurringKind (0 unică · 1 lunară · 2 anuală) · EstimatedCloseDate
   ├─ LeadId? · CampaignId? (atribuirea la campanie, CR4) · ParentOpportunityId? (reînnoirea precedentă)
   ├─ Status: 1 Deschisă · 2 Câștigată · 3 Pierdută · 4 Anulată
   ├─ WinReasonId? · LostReasonId? (ReasonCode tip 1) · CompetitorId? · CloseNote · ClosedAt · ActualAmount
   ├─ crm.OpportunityLine × N        (articol sau familie, cantitate estimată, preț estimat; opțional)
   ├─ crm.OpportunityContact × N     (persoanele implicate și rolul lor în decizie)
   ├─ crm.OpportunityCompetitor × N  (concurenții în joc, punctele tari și slabe)
   ├─ crm.OpportunityCriterionCheck × N (bifele criteriilor de ieșire, cine și când)
   ├─ crm.OpportunityStageHistory × N   (fiecare schimbare de etapă, cu durata în etapa anterioară)
   └─ legături: sales.SalesQuote × N (OpportunityId), sales.SalesOrder (prin ofertă), sales.BillingPlan (reînnoire)

 crm.ForecastSubmission (lună, agent) ── crm.ForecastSubmissionLine × N (categorie, sumă commit, ajustarea șefului)
```

Reguli de business:

1. **Partenerul e obligatoriu** (`63251`); oportunitatea fără partener nu există (lead-ul se califică întâi, CR2).
2. **Etapa nu poate sări peste criteriile obligatorii** (`EnforceExitCriteria`, `63253`), iar etapa cu `RequiresQuote` cere o ofertă V3 legată, cel puțin trimisă (`63255`). Întoarcerea la o etapă anterioară e permisă și apare în istoric.
3. **Valoarea estimată** se completează manual sau din liniile estimate; când există o ofertă trimisă legată, valoarea devine valoarea netă a ultimei revizii trimise (`NetAmountRon` din `OfertaTrimisa`), iar câmpul manual se blochează (`63256`, se poate debloca prin desfacerea legăturii). Pentru `RecurringKind = 1`, valoarea pe pipeline e valoarea lunară × 12 (valoarea anuală), afișată separat.
4. **Pas următor obligatoriu:** oportunitatea deschisă fără activitate viitoare (CR4) primește semnalul „fără pas următor” și, cu `RequireNextStep = 1`, un avertisment la salvare (`63254`).
5. **Stagnarea:** zilele în etapă peste `MaxDaysInStage` colorează cardul în kanban (galben la 80 %, roșu peste prag) și o pun în „De rezolvat” (P8).
6. **Pierderea cere motivul** (`63260`); motivul „Concurent” cere concurentul (`63261`). **Câștigul manual** cere motivul de câștig (`63262`) și, dacă există o ofertă legată neacceptată, o confirmare.
7. **Redeschiderea** unei oportunități închise e permisă în 90 de zile, de proprietar sau de șef, cu motiv; după 90 de zile se creează una nouă cu `ParentOpportunityId` (`63263`).
8. **Oportunitățile închise nu se modifică** (`63264`), cu excepția notelor și a corectării motivului de către șef (audit).

## 3. Fluxuri detaliate

### 3.1 Crearea

| Cale | Tranzacția | Ce se completează |
| --- | --- | --- |
| Calificarea lead-ului (CR2 §3.5) | `OP-LD` | Partener, contact, sursă, campanie, valoare din lead |
| Manual, din kanban sau din fișa 360 | `OP-NOU` (prospect) sau `OP-SUP` (client existent, cu facturi) | Tranzacția se propune după rolul partenerului |
| Reînnoire (3.6) | `OP-REN` | Generată de job |

### 3.2 Kanbanul (P7)

Coloanele sunt etapele deschise ale pipeline-ului ales, plus două zone de plasare la marginea de jos, „Câștigată” și „Pierdută”. Pe coloană: numărul de oportunități, suma valorilor și suma ponderată (valoare × probabilitate). Cardul arată partenerul, numele, valoarea, data estimată de închidere, proprietarul (inițiale), pictograma pasului următor (sau semnul „fără pas următor”), zilele în etapă și culoarea de stagnare. Filtre: proprietar, echipă, lună de închidere, sursă, segment, „doar ale mele”. Tragerea cardului rulează aceleași verificări ca la salvare (criterii, ofertă); dacă lipsește ceva, se deschide fereastra cu bifele cerute.

### 3.3 Legătura cu oferta din Vânzări (P18)

1. Pe oportunitate, butonul **„Ofertă nouă”** deschide formularul de ofertă V3 cu partenerul, contactul, agentul, liniile estimate (articolele și cantitățile, prețurile se recalculează după V2) și `OpportunityId` completat. Folosește grupul de consum `QUOTE_FROM_OPP` (CR1 §3.6).
2. La `OfertaTrimisa`, CRM-ul: actualizează valoarea estimată (regula 3), mută oportunitatea în prima etapă cu `RequiresQuote` dacă e mai jos, scrie o activitate „Ofertă trimisă” pe cronologie.
3. La `OfertaInchisa`: acceptată → etapa „Negociere” sau mai sus (fără câștig încă); pierdută → propune închiderea oportunității ca pierdută cu **același motiv și concurent** (o notificare către proprietar, nu automat, pentru că oportunitatea poate avea și alte oferte); expirată → activitate „Ofertă expirată” și semnalul „fără pas următor”.
4. La `ComandaConfirmata` dintr-o ofertă legată, cu `AutoWinOnOrder = 1`: oportunitatea devine **Câștigată**, `ActualAmount` = valoarea netă a comenzii, motivul de câștig rămâne de completat de agent în 7 zile (sarcină automată), se publică `OportunitateCastigata`.

### 3.4 Panoul financiar al clientului (P19)

Pe formularul oportunității, pentru partenerii cu istoric: soldul, restanțele peste 30 de zile, limita de credit și expunerea (din `IPartnerBalanceQueries`, F3, și din limita de credit V1), ultimele 3 facturi, cifra de afaceri pe 12 luni (din `sales.vw_SalesFact`, V7). Doar citire; fără Finanțe licențiate panoul arată doar ce vine din Vânzări.

### 3.5 Închiderea

Fereastra „Închide ca pierdută”: motivul (lista `ReasonCode` tip 1), concurentul (`crm.Competitor`, cu adăugare rapidă), prețul concurentului (opțional), nota; ofertele V3 legate încă deschise se pot închide ca pierdute în același pas (comandă V3 `CloseQuoteLost`, același motiv). Fereastra „Închide ca câștigată” (manual): motivul de câștig, valoarea reală, data.

### 3.6 Reînnoirile (P20)

Jobul zilnic `CrmRenewalsDue` (06:30) caută planurile de facturare V5 active cu `EndDate` în următoarele `RenewalLeadDays` zile (60 implicit) și fără oportunitate de reînnoire pentru perioada respectivă (grupul de consum `RENEWAL`). Creează `OP-REN` în pipeline-ul de tip „Reînnoiri”, cu proprietarul = agentul din portofoliul clientului, valoarea = valoarea lunară a planului × 12, data estimată = `EndDate`, și o sarcină „Discută reînnoirea”. Câștigarea reînnoirii nu prelungește planul automat: agentul îl prelungește în V5 (butonul „Prelungește planul” deschide planul cu `EndDate` propus + 12 luni).

### 3.7 Prognoza (P14)

| Măsură | Calcul |
| --- | --- |
| Ponderată | Σ (valoare × probabilitate) pe oportunitățile deschise cu data estimată în lună |
| Recurentele | Oportunitățile cu `RecurringKind = 1` intră în prognoza lunii cu valoarea unei luni, nu cu valoarea anuală afișată pe pipeline |
| Pe categorii | `Closed` (câștigate în lună) · `Commit` · `BestCase` · `Pipeline` |
| Commit-ul agentului | Agentul trimite pentru luna următoare între ziua 25 a lunii curente și ziua 5 a lunii prognozate (setare) suma pe care se angajează; se poate retrimite în fereastră, ultima variantă contează; se salvează în `crm.ForecastSubmission` |
| Ajustarea șefului | Șeful poate corecta suma agentului, cu notă; vede ambele valori |
| Ținta | Ținta lunară a agentului (din V1, `SalesAgentTarget`, dacă există; altfel câmpul din prognoză) |

Prognoza e un instantaneu: la trimitere se salvează și lista oportunităților incluse, ca la sfârșitul lunii să se compare ce s-a promis cu ce s-a câștigat (CR5).

## 4. Exemplu complet: OP 000184 Hotel Belvedere

| Data | Ce se întâmplă | Etapa · probabilitate | Valoare |
| --- | --- | --- | --- |
| 27.11.2026 | Calificarea LD 000318 (CR2 §4) creează **OP 000184**, Cristina Marin, pipeline „HoReCa Cluj” | Calificat · 10 % | 86.400 (7.200 lunar, recurentă) |
| 28.11.2026 | Activitate „Degustare la hotel” programată pe 02.12 | Degustare programată · 30 % | 86.400 |
| 02.12.2026 | Degustarea realizată (Vizită, rezultat „Cere ofertă”); concurent în joc: „AquaVita Distribuție” | Degustare · 30 % | 86.400 |
| 03.12.2026 | Cristina creează din oportunitate oferta **OV 0212** (V3): apă plată și minerală 0,5 l și 19 l, cafea boabe 1 kg, chirie 8 dozatoare; trimisă după aprobare | Ofertă trimisă · 55 % | 6.980 lunar × 12 = **83.760** (din ofertă) |
| 09.12.2026 | Comanda de test de două săptămâni livrată; criteriul „Comanda de test livrată” bifat | Test 2 săptămâni · 75 % (`Commit`) | 83.760 |
| 18.12.2026 | Hotelul acceptă; comanda **CV 4471** confirmată din OV 0212 | **Câștigată** automat | Real: 6.980 lunar |

Cristina completează motivul de câștig „Termen de livrare” pe 18.12. `OportunitateCastigata` pleacă spre raportare și spre Vânzări (care poate propune crearea planului de facturare pentru chiria dozatoarelor).

**Reînnoire:** planul `PR 0007` al Biroului Notarial Pop (V5 §4.1) are `EndDate` = 31.01.2027. Pe 02.12.2026, jobul creează **OP 000176** „Birou Notarial Pop – reînnoire chirie dozatoare 2027”, pipeline „Reînnoiri”, etapa „De reînnoit” 60 %, 150 × 12 = 1.800 lei, proprietar Vlad Georgescu (portofoliu).

**Prognoza pe decembrie 2026, Cristina Marin** (trimisă pe 27.11): ponderat 142.310; `Commit` 118.000; `BestCase` 176.400; ținta 150.000. Radu Stan ajustează commit-ul la 110.000 („Hyper Nord poate întârzia”).

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `OportunitateCreata` v1 | Publicat | Partener, valoare, etapă, sursă, campanie | CR4, CR5 |
| `OportunitateEtapaSchimbata` v1 | Publicat | Etapa veche și nouă, zile în etapă | CR4 (oprirea secvențelor), CR5 |
| `OportunitateCastigata` v1 | Publicat (MODULE_PRINCIPALE) | Partener, valoare reală, ofertă, comandă, campanie, motiv | Vânzări (propunerea planului de facturare pentru linii recurente), CR4 (ROI campanie), Raportare |
| `OportunitatePierduta` v1 | Publicat | Motiv, concurent | CR5, Raportare |
| `OfertaTrimisa`, `OfertaInchisa` v1 | Consumate (V3) | `QuoteId`, `OpportunityId` (câmp nou, precizare), valori, motiv | 3.3 |
| `ComandaConfirmata` v1 | Consumat (V3) | `SourceQuoteId`, valoare | 3.3 pasul 4 |
| `PlanFacturareModificat` v1 | Consumat (V5) | `EndDate`, stare | 3.6 (planul încheiat sau prelungit anulează sau actualizează reînnoirea deschisă) |

## 6. Contracte publice

```csharp
public interface ICrmOpportunityQuery    // folosit de V3 (lista oportunităților deschise ale partenerului la crearea ofertei) și de 02.13
{
    Task<IReadOnlyList<OpenOpportunityDto>> GetOpenForPartnerAsync(int companyId, int partnerId, CancellationToken ct);
}
public sealed record OpenOpportunityDto(long OpportunityId, string Number, string Name, int OwnerAgentId,
    string StageName, decimal Probability, decimal EstimatedAmount, DateOnly EstimatedCloseDate);
```

Vânzările nu depind de CRM la compilare: V3 cheamă `ICrmOpportunityQuery` printr-un port opțional; fără pachetul CRM, câmpul `OpportunityId` rămâne gol și ascuns.

## 7. Schema SQL (partea CR3)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `crm.Opportunity` | `Id bigint` | UX `(CompanyId, DocumentNumber)`; IX `(CompanyId, PipelineId, Status, StageId)`; IX `(PartnerId, Status)`; IX `(OwnerAgentId, Status, EstimatedCloseDate)`; `CHECK (Status <> 3 OR LostReasonId IS NOT NULL)` |
| `crm.OpportunityLine`, `crm.OpportunityContact`, `crm.OpportunityCompetitor` | `Id bigint` | FK `OpportunityId` |
| `crm.OpportunityCriterionCheck` | `Id bigint` | UX `(OpportunityId, PipelineStageCriterionId)` |
| `crm.OpportunityStageHistory` | `Id bigint` | IX `(OpportunityId, ChangedAt)` |
| `crm.ForecastSubmission`, `crm.ForecastSubmissionLine`, `crm.ForecastSnapshotItem` | `Id bigint` | UX `(CompanyId, SalesAgentId, PeriodMonth)` |
| `sales.SalesQuote.OpportunityId` | — | Coloană nouă `bigint NULL`, fără FK între scheme (Bune practici: fără FK între module), IX filtrat `WHERE OpportunityId IS NOT NULL` |

Procedurile: `crm.usp_Opportunity_Save`, `crm.usp_Opportunity_MoveStage` (verificările regulii 2, istoricul), `crm.usp_Opportunity_Close`, `crm.usp_Opportunity_Reopen`, `crm.usp_Opportunity_OnQuoteEvent`, `crm.usp_Renewals_Due`, `crm.usp_Forecast_Submit`.

**Erorile CR3** (`63250–63449`): `63251` `CRM_OPP_PARTNER_REQUIRED` · `63252` `CRM_OPP_STAGE_INACTIVE` · `63253` `CRM_OPP_EXIT_CRITERIA_MISSING` · `63254` `CRM_OPP_NO_NEXT_STEP` (avertisment) · `63255` `CRM_OPP_QUOTE_REQUIRED` · `63256` `CRM_OPP_AMOUNT_FROM_QUOTE` · `63260` `CRM_OPP_LOST_REASON_REQUIRED` · `63261` `CRM_OPP_COMPETITOR_REQUIRED` · `63262` `CRM_OPP_WIN_REASON_REQUIRED` · `63263` `CRM_OPP_REOPEN_EXPIRED` · `63264` `CRM_OPP_CLOSED_READONLY` · `63270` `CRM_FORECAST_PERIOD_LOCKED`.

## 8. Permisiuni

`Crm.Opportunity.View`, `.Create`, `.Edit`, `.Close`, `.Reopen` (proprietar în 90 de zile, șef), `.Reassign` (șef), `Crm.Forecast.Submit` (agent), `Crm.Forecast.Adjust` (șef), `Crm.Forecast.ViewTeam`.

## 9. Cerințe legale și integrări

| Integrare | Ce se schimbă (precizări aplicate 27.11.2026) |
| --- | --- |
| V3 Oferte și comenzi | `SalesQuote.OpportunityId`; câmpul „Oportunitate” pe formularul ofertei; `OpportunityId` în `OfertaTrimisa` și `OfertaInchisa`; `CompetitorId` opțional; comanda `CloseQuoteLost` folosită și din CRM |
| V5 Facturare | Planurile care expiră generează reînnoiri; „Prelungește planul” |
| V1 Configurare | `SalesAgentTarget` (ținta lunară a agentului) folosit de prognoză, dacă există |
| F3 Creanțe | `IPartnerBalanceQueries` pentru panoul financiar |
| Legal | Nicio cerință fiscală: CRM-ul nu produce documente fiscale |

## 10. Rapoarte

Lista oportunităților (filtre, export), „Stagnante”, „Fără pas următor”, „Închise luna aceasta”. Pâlnia, conversia, prognoza vs realizat și motivele de pierdere sunt în CR5.

## 11. Ecrane principale

Machetele sunt în [ecrane/cr3-oportunitati/](ecrane/cr3-oportunitati/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Kanbanul pipeline-ului | „HoReCa Cluj”: coloanele cu sume, cardurile cu stagnare și pas următor | `cr3-01-kanban` |
| Formularul oportunității (principal) | OP 000184: etapele ca bară de progres, criteriile, oferta OV 0212 legată, panoul financiar, cronologia | `cr3-02-formular-oportunitate` |
| Închiderea ca pierdută | Fereastra cu motivul, concurentul și ofertele legate | `cr3-03-inchidere-pierduta` |
| Prognoza lunii | Decembrie 2026 pe agenți: ponderat, categorii, commit, ajustare, țintă | `cr3-04-prognoza` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| CR3-A1 | Mutarea OP 000184 în „Test 2 săptămâni” fără bifa criteriului e refuzată (`63253`) | Test de integrare |
| CR3-A2 | `OfertaTrimisa` pentru OV 0212 actualizează valoarea la 83.760 și etapa la „Ofertă trimisă” | Test de integrare pe handler |
| CR3-A3 | `ComandaConfirmata` CV 4471 marchează OP 000184 câștigată și publică `OportunitateCastigata` o singură dată (idempotent la reluarea evenimentului) | Test de integrare |
| CR3-A4 | Pierderea cu motivul „Concurent” fără concurent e refuzată (`63261`) | Test unitar |
| CR3-A5 | Jobul de reînnoiri creează OP 000176 o singură dată pentru PR 0007, chiar rulat de două ori | Test de integrare |
| CR3-A6 | Prognoza ponderată a Cristinei pe decembrie dă 142.310 pe datele de test | Test de integrare |
| CR3-A7 | Fără pachetul CRM, V3 funcționează și câmpul „Oportunitate” e ascuns | Test de integrare cu licența fără CRM |

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Ținta folosită de prognoză** se citește prin `IKpiTargetQuery` (BI4), aceeași cu ținta din `sales.SalesAgentTarget`; vederea `crm.vw_OpportunityFact` e sursa setului de date „CRM · oportunități” și a instantaneului săptămânal al pipeline-ului. [BI4 §6](../13-raportare-bi/BI4-TABLOURI-KPI-ALERTE.md)

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Oferta | Doar oferta din V3, legată prin `OpportunityId`; nu există ofertă CRM | ✓ Aprobat autonom (27.11.2026) |
| 2 | Câștigul | Automat la comanda confirmată din oferta legată (setare), manual cu motiv | ✓ Aprobat autonom |
| 3 | Oferta pierdută | Propune pierderea oportunității cu același motiv, nu o închide automat | ✓ Aprobat autonom |
| 4 | Valoarea | Din ultima revizie trimisă a ofertei, când există; altfel manuală; recurentele la valoare anuală | ✓ Aprobat autonom |
| 5 | Criteriile de ieșire | Obligatorii doar pe etapele marcate | ✓ Aprobat autonom |
| 6 | Reînnoirile | Generate din planurile V5 cu 60 de zile înainte; prelungirea planului rămâne manuală în V5 | ✓ Aprobat autonom |
| 7 | Prognoza | Ponderată + categorii + commit al agentului + ajustarea șefului, cu instantaneu lunar | ✓ Aprobat autonom |
| 8 | Redeschiderea | În 90 de zile, cu motiv; după, oportunitate nouă legată | ✓ Aprobat autonom |
| 9 | Dependența Vânzări → CRM | Doar prin port opțional și evenimente; Vânzările merg fără CRM | ✓ Aprobat autonom |

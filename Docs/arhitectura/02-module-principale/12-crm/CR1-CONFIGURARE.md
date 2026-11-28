# ERP · 02.12 CRM · CR1 Configurare: pipeline-uri, etape, surse, motive, echipe, atribuire, scor, tranzacții

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: Aurelian a cerut lucru autonom și analiza altor producători înainte de CRM ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P1–P24); împărțirea CR1–CR5 și deciziile 1–10 au fost adoptate pe recomandare. Prima din cele 5 părți ale modulului 02.12.

Modulul 02.12 are schema SQL `crm`, prefixul de cod `Erp.Crm` și plaja de erori `63000–63999` (Bune practici 8.3), împărțită pe părți: CR1 `63000–63099`, CR2 `63100–63249`, CR3 `63250–63449`, CR4 `63450–63649`, CR5 `63650–63749`, rezervă `63750–63999`. Faza 4 din roadmap, pachetul de licență „CRM” (cere „Bază”; legăturile cu Vânzări și Finanțe apar doar dacă și acelea sunt licențiate). Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [Nucleu](../01-nucleu/NUCLEU.md) (parteneri și persoane de contact, utilizatori, tranzacții §3.6, nomenclatoare §3.5, serii, chei API), [Vânzări V1](../03-vanzari/V1-CONFIGURARE.md) (agenți, echipe, zone, portofolii) și [V3](../03-vanzari/V3-OFERTE-COMENZI.md) (oferta, motivele de pierdere). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP on-premise, SQL Server case sensitive (`Latin1_General_100_CS_AS_SC_UTF8`, `SQL-21`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite și fără servicii în nor obligatorii.
- **CRM-ul nu dublează datele altor module.** Clientul e partenerul din Nucleu (`core.Partner`), persoana de contact e `core.PartnerContact`, agentul și echipa sunt cele din Vânzări V1 (`SalesAgent`, `ManagerAgentId`, `SalesZone`), oferta e cea din V3, motivele de pierdere sunt nomenclatorul `ReasonCode` de tip 1 din Vânzări. CRM-ul adaugă **ce e înainte de vânzare** (lead, oportunitate, activități, campanii) și **fișa 360**, care citește restul.
- Comunicarea cu Vânzările și Finanțele e prin **evenimente și contracte de citire** (MODULE_PRINCIPALE: „Nucleu, cu Vânzări doar prin evenimente”). CRM-ul publică `OportunitateCastigata` și consumă `OfertaTrimisa`, `OfertaInchisa`, `ComandaConfirmata`, `FacturaEmisa`, `IncasareInregistrata`.
- **Ce NU face CR1:** nu creează lead-uri (CR2), oportunități (CR3), activități sau campanii (CR4) și nu are rapoarte (CR5). Definește doar configurarea pe care o citesc celelalte părți.

## 1. Scop și perimetru

| Zonă | Ce face CR1 | Ce NU face (și cine face) |
| --- | --- | --- |
| Pipeline-uri | Unul sau mai multe pe firmă (de exemplu „Clienți noi HoReCa”, „Retail modern”, „Reînnoiri”), cu etape, probabilitate, categorie de prognoză, zile maxime și criterii de ieșire | Oportunitățile (CR3) |
| Nomenclatoare | Surse de lead, motive de descalificare, motive de câștig, concurenți, tipuri de activitate, industrii, segmente, rezultate de activitate | Motivele de pierdere (`ReasonCode` tip 1, Vânzări V1, folosite comun) |
| Echipe și atribuire | Folosește agenții și ierarhia din V1; adaugă cozile de lead-uri și regulile de atribuire (teritoriu, sursă, segment, round-robin cu capacitate) | Agenții și portofoliile (V1) |
| Scor | Reguli de scor pe lead (puncte pe câmpuri și activități) și setările scorului statistic | Calculul (CR2) |
| Șabloane | Șabloane de e-mail, de secvență și de raport de vizită | Trimiterea (CR4) |
| Tranzacții | Tipurile de documente și tranzacțiile livrate (Nucleu §3.6) | — |
| Setări | Setările CRM ale firmei (3.8) | — |

## 2. Model de domeniu

```
 core: Partner · PartnerContact · User · ApiKey · NumberSeries · TransactionType · ReasonCode (tip 1, din Vânzări)
 sales: SalesAgent (ManagerAgentId = echipa) · SalesZone · AgentPortfolio

 crm.Pipeline ── crm.PipelineStage × N        (ordine, probabilitate, ForecastCategory, MaxDaysInStage, criterii de ieșire)
 crm.LeadSource · crm.DisqualifyReason · crm.WinReason · crm.Competitor · crm.ActivityType · crm.ActivityOutcome
 crm.Industry · crm.Segment
 crm.LeadQueue ── crm.AssignmentRule × N      (condiții → agent / echipă / round-robin, capacitate)
 crm.ScoringRule × N                          (câmp sau activitate → puncte; prag „fierbinte”)
 crm.EmailTemplate · crm.SequenceTemplate ── crm.SequenceStep × N · crm.VisitReportTemplate
 crm.CompanySettings
```

Reguli de business:

1. **Un pipeline aparține unei firme** și are cel puțin o etapă deschisă și cele două etape finale fixe, `WON` și `LOST` (livrate, nu se șterg, `63001`).
2. **Probabilitatea vine din etapă** (implicit), iar agentul o poate suprascrie pe oportunitate doar dacă setarea `AllowProbabilityOverride` e activă; categoria de prognoză (`Pipeline`, `BestCase`, `Commit`, `Closed`, `Omitted`) vine tot din etapă și poate fi mutată de agent doar spre `Commit` / `BestCase` (P14).
3. **Etapele nu se șterg dacă au oportunități** (`63002`): se dezactivează, iar oportunitățile se mută la altă etapă printr-o acțiune în masă.
4. **Criteriile de ieșire** dintr-o etapă (checklist, P5) sunt obligatorii doar dacă etapa are `EnforceExitCriteria = 1`: oportunitatea nu trece mai departe fără bifele cerute (`63253`, CR3).
5. **Motivele de pierdere sunt comune cu oferta** (V3): o ofertă pierdută și oportunitatea ei folosesc același cod, ca raportul de pierderi să fie unul singur.

## 3. Entități

### 3.1 Pipeline-ul și etapele (`crm.Pipeline`, `crm.PipelineStage`)

| Câmp | Reguli |
| --- | --- |
| `Pipeline.Code`, `Name`, `CompanyId`, `IsDefault`, `Currency` | Unul implicit pe firmă; moneda de raportare (lei implicit) |
| `Pipeline.Kind` | 1 Clienți noi · 2 Vânzare suplimentară · 3 Reînnoiri (generate din CR3 §3.6) · 4 Proiecte |
| `Stage.SortOrder`, `Name`, `Probability` (0–100) | Ordinea pe kanban |
| `Stage.ForecastCategory` | `Pipeline` · `BestCase` · `Commit` · `Closed` (doar `WON`) · `Omitted` (doar `LOST`) |
| `Stage.MaxDaysInStage` | Peste prag, oportunitatea e „stagnantă” (P8) |
| `Stage.ExitCriteria` | Listă de bife (de exemplu „Decidentul identificat”, „Bugetul confirmat”, „Oferta trimisă”); `EnforceExitCriteria` |
| `Stage.RequiresQuote` | Etapa cere o ofertă V3 trimisă legată (de exemplu „Propunere”) |
| `Stage.IsSystem` | `WON`, `LOST` |

Pipeline-ul livrat „Vânzări B2B” (seed `MIG-06`): Calificat 10 % · Analiză nevoi 25 % · Propunere 50 % (cere ofertă) · Negociere 75 % (`Commit`) · Câștigat 100 % · Pierdut 0 %. Pipeline-ul livrat „Reînnoiri”: De reînnoit 60 % · Ofertă de reînnoire 80 % · Câștigat · Pierdut.

### 3.2 Nomenclatoarele

| Nomenclator | Valori livrate |
| --- | --- |
| `LeadSource` | Formular site · Telefon · E-mail · Târg sau eveniment · Recomandare · Campanie · Import listă · Agent de teren · Partener |
| `DisqualifyReason` | Fără nevoie · Fără buget · Contact invalid · Concurent · Duplicat · În afara zonei · Spam |
| `WinReason` | Preț · Disponibilitate stoc · Termen de livrare · Relație · Calitate · Condiții de plată |
| `Competitor` | Listă liberă, cu site și note (folosită și de V3 în câmpul `CompetitorName`, precizare) |
| `ActivityType` | Apel · E-mail · Întâlnire · Vizită (cu locație) · Sarcină · Demonstrație · Notă; fiecare cu durata implicită și dacă intră în calendar |
| `ActivityOutcome` | Realizată · Fără răspuns · Reprogramată · Interesat · Neinteresat · Cere ofertă |
| `Industry`, `Segment` | HoReCa, retail tradițional, retail modern, birouri, instituții, distribuitori; A / B / C |

### 3.3 Cozile și atribuirea (`crm.LeadQueue`, `crm.AssignmentRule`)

O coadă primește lead-urile nealocate (implicit „Lead-uri noi”). Regulile se evaluează în ordine: condiții (sursă, județ sau zonă V1, industrie, segment, scor minim, cuvinte în mesaj) → țintă (agent, echipă cu round-robin, coadă). Round-robin-ul ține cont de **capacitate** (`MaxOpenLeads` pe agent) și de absențe (calendarul utilizatorului). Clientul existent cu portofoliu (V1) merge **la agentul din portofoliu**, înainte de orice regulă (decizia 5).

### 3.4 Scorul (`crm.ScoringRule`, setări)

- **Scor pe reguli** (P12): puncte pe câmp (industrie = HoReCa +15, județ în zonele firmei +10, e-mail de firmă, nu gratuit, +5, CUI valid +10) și pe activitate (formular completat +20, răspuns la e-mail +15, întâlnire realizată +25, inactiv 30 de zile −20). Pragurile: „rece” < 30, „cald” 30–59, „fierbinte” ≥ 60.
- **Probabilitatea statistică** (opțională, setarea `PredictiveScoring`): calculată nocturn din lead-urile închise în ultimele 24 de luni (calificate vs descalificate) pe câmpurile alese (sursă, industrie, județ, segment, etapă), cu formula Bayes naivă cu netezire Laplace, ca în Odoo; nu are nevoie de servicii externe. Afișată lângă scorul pe reguli, nu îl înlocuiește.

### 3.5 Șabloanele

- **E-mail** (`crm.EmailTemplate`): subiect și corp HTML cu câmpuri (`{{Contact.FirstName}}`, `{{Opportunity.Name}}`, `{{Agent.Signature}}`), limba (ro / en), subsolul obligatoriu cu dezabonarea pentru e-mailurile de campanie.
- **Secvență** (`crm.SequenceTemplate`, P10): pași cu zi relativă, tip (e-mail automat, e-mail de trimis manual, apel, sarcină, vizită), șablon; condiții de oprire (răspuns primit, etapă schimbată, lead calificat sau descalificat, dezabonare).
- **Raport de vizită** (`crm.VisitReportTemplate`): întrebări (prezență produse la raft, concurenți văzuți, stoc, comandă luată), folosite de agentul de teren (CR4).

### 3.6 Tipurile de documente și tranzacțiile livrate

| Tip | Ce e | Prefix | Tranzacții | Sursă |
| --- | --- | --- | --- | --- |
| `Lead` | Lead | `LD` | `LD-MAN` manual · `LD-WEB` formular web · `LD-IMP` import · `LD-MAIL` e-mail primit pe adresa de lead-uri · `LD-CMP` răspuns la campanie | Generate sau manuale |
| `Opportunity` | Oportunitate | `OP` | `OP-NOU` clienți noi · `OP-SUP` vânzare suplimentară · `OP-REN` reînnoire (generată din planurile de facturare V5) · `OP-LD` din calificarea lead-ului | `OP-LD` consumă `Lead` (grupul `QUALIFY`) |
| `Campaign` | Campanie | `CMP` | `CMP-EMAIL` · `CMP-EVENT` (târg, eveniment) · `CMP-TEL` (telesales) | Manuale |
| `Case` | Caz după vânzare (reclamație, cerere) | `CAZ` | `CAZ-RECL` reclamație · `CAZ-CERERE` cerere de informații | Manuale sau din e-mail |

Activitățile nu sunt documente cu tranzacție (sunt înregistrări de lucru, fără număr). Grupurile de consum: `QUALIFY` (un lead → cel mult 5 oportunități, P2), `QUOTE_FROM_OPP` (oportunitate → oferte V3, folosit de V3 la crearea ofertei din oportunitate), `RENEWAL` (plan de facturare → oportunitate de reînnoire, o singură dată pe perioadă).

### 3.7 Permisiunile pe înregistrări

Vizibilitatea urmează portofoliile din V1: agentul vede lead-urile și oportunitățile lui și ale clienților din portofoliul lui; șeful de echipă vede echipa (pe ierarhia `ManagerAgentId`, cel mult 3 niveluri); rolul „Manager vânzări” vede firma. Setarea `CrmVisibility` = 1 Pe portofoliu (implicit) · 2 Toată firma.

### 3.8 Setările CRM ale firmei (`crm.CompanySettings`)

| Setare | Implicit | Ce face |
| --- | --- | --- |
| `CrmVisibility` | 1 | 3.7 |
| `LeadAutoAssign` | 1 | Atribuire automată la creare (3.3) |
| `DuplicateCheckMode` | 2 | 1 Avertisment · 2 Blocant pe CUI și e-mail exact, avertisment pe nume și telefon (CR2) |
| `AllowProbabilityOverride` | 0 | Regula 2 |
| `StaleWarningDays` | 14 | Zile fără activitate pe un lead sau o oportunitate deschisă |
| `RequireNextStep` | 1 | O oportunitate deschisă trebuie să aibă o activitate viitoare (`63254`, avertisment) |
| `AutoWinOnOrder` | 1 | Comanda confirmată din oferta legată marchează oportunitatea câștigată (CR3) |
| `RenewalLeadDays` | 60 | Cu câte zile înainte de expirarea planului de facturare se creează oportunitatea de reînnoire |
| `PredictiveScoring` | 0 | 3.4 |
| `EmailSyncProvider` | 0 | 0 Fără · 1 Microsoft 365 (Graph) · 2 IMAP / SMTP (CR4) |
| `EmailOpenTracking` | 0 | Fixat la 0 (decizia 8, GDPR) |
| `ConsentRequiredForMarketing` | 1 | Trimiterile de campanie doar către persoane cu consimțământ pe e-mail (CR2) |
| `CaseSlaHours` | 24 | Termenul de primă reacție la un caz (CR4) |

## 4. Fluxuri principale

### 4.1 Configurarea la activare

La activarea pachetului „CRM”, seed-ul creează pipeline-urile livrate, nomenclatoarele, coada „Lead-uri noi”, regulile de scor implicite și tranzacțiile. Asistentul de configurare are pasul „CRM”: alegerea pipeline-ului, a vizibilității, a furnizorului de e-mail.

### 4.2 Exemplu: pipeline-ul HoReCa al Exemplu Distribuție

Radu Stan (șef vânzări) creează pipeline-ul „HoReCa Cluj” cu etapele Calificat 10 % · Degustare programată 30 % (max. 10 zile) · Ofertă trimisă 55 % (cere ofertă V3) · Test 2 săptămâni 75 % (`Commit`, criteriu „Comanda de test livrată”) · Câștigat · Pierdut. Regula de atribuire: sursa „Formular site”, industria HoReCa, județul Cluj → round-robin între Cristina Marin și Vlad Georgescu, capacitate 40 de lead-uri deschise fiecare.

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `PipelineModificat` v1 | Publicat | Pipeline-ul, etapele | CR3 (recalcul probabilități pentru oportunitățile deschise, la cerere), CR5 |
| `PortofoliuAgentModificat` v1 | Consumat (V1) | Agentul, clientul | Reatribuirea oportunităților deschise ale clientului (opțional, cu confirmare) |
| `UtilizatorDezactivat` v1 | Consumat (Nucleu) | — | Lead-urile și oportunitățile deschise ale agentului apar în „De rezolvat” la șeful lui |

## 6. Contracte publice (`Erp.Crm.Contracts`, partea CR1)

```csharp
public interface ICrmPipelineQuery
{
    Task<IReadOnlyList<PipelineStageDto>> GetStagesAsync(int pipelineId, CancellationToken ct);
}
public sealed record PipelineStageDto(int StageId, string Name, int SortOrder, decimal Probability,
    string ForecastCategory, int? MaxDaysInStage, bool RequiresQuote, bool IsWon, bool IsLost);
```

## 7. Schema SQL `crm` (partea CR1)

| Tabel | Cheie primară | Chei unice / străine |
| --- | --- | --- |
| `crm.Pipeline` | `Id int` | UX `(CompanyId, Code)`; UX `(CompanyId) WHERE IsDefault = 1` |
| `crm.PipelineStage` | `Id int` | FK `PipelineId`; UX `(PipelineId, SortOrder)`; `CHECK (Probability BETWEEN 0 AND 100)`; `CHECK (ForecastCategory IN ('Pipeline','BestCase','Commit','Closed','Omitted'))` |
| `crm.PipelineStageCriterion` | `Id int` | FK `PipelineStageId` |
| `crm.LeadSource`, `crm.DisqualifyReason`, `crm.WinReason`, `crm.Competitor`, `crm.ActivityType`, `crm.ActivityOutcome`, `crm.Industry`, `crm.Segment` | `Id int` | UX `(CompanyId, Code)`; seed `MERGE` doar cu inserare (`MIG-06`) |
| `crm.LeadQueue`, `crm.AssignmentRule`, `crm.AgentCapacity` | `Id int` | FK `LeadQueueId`, `SalesAgentId` |
| `crm.ScoringRule` | `Id int` | `CHECK (RuleKind IN ('FIELD','ACTIVITY','DECAY'))` |
| `crm.EmailTemplate`, `crm.SequenceTemplate`, `crm.SequenceStep`, `crm.VisitReportTemplate` | `Id int` | UX `(CompanyId, Code)` |
| `crm.CompanySettings` | `CompanyId int` | FK `core.Company` |

**Erorile CR1** (`63000–63099`): `63001` `CRM_SYSTEM_STAGE_LOCKED` · `63002` `CRM_STAGE_IN_USE` · `63003` `CRM_PIPELINE_NO_OPEN_STAGE` · `63004` `CRM_DEFAULT_PIPELINE_REQUIRED` · `63005` `CRM_RULE_TARGET_INACTIVE` (agent inactiv în regula de atribuire) · `63006` `CRM_TEMPLATE_UNSUBSCRIBE_MISSING` (șablon de campanie fără linkul de dezabonare).

## 8. Permisiuni și roluri

Roluri livrate (noi, în Nucleu): **Manager vânzări** (toată configurarea CRM, vede firma), **Agent CRM** (lead-uri, oportunități, activități pe portofoliu; se combină cu rolurile de agent din V1), **Marketing** (campanii, liste, șabloane de e-mail, lead-urile din campanii, fără oportunități), **Operator relații clienți** (cazuri). Permisiunile CR1: `Crm.Pipeline.Manage`, `Crm.Lookups.Manage`, `Crm.Assignment.Manage`, `Crm.Scoring.Manage`, `Crm.Templates.Manage`, `Crm.Settings.Manage`.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru CR1 |
| --- | --- |
| GDPR (Regulamentul UE 2016/679) și Legea 506/2004 (comunicări electronice) | Șabloanele de campanie au obligatoriu dezabonarea (`63006`); trimiterea de marketing cere consimțământ (setare); fără urmărirea deschiderilor |
| Nucleu | Roluri noi, tipurile de serie `Lead`, `Opportunity`, `Campaign`, `Case`; pasul „CRM” în asistent |
| Vânzări V1, V3 | Agenții, echipele, zonele, portofoliile; `ReasonCode` tip 1 comun; `Competitor` folosit și pe ofertă |

**Precizări aplicate (27.11.2026):** Nucleu (roluri, serii, pas în asistent), V1 (folosirea agenților și a ierarhiei de către CRM), V3 (`CompetitorId` opțional lângă `CompetitorName`; câmpul `OpportunityId` pe ofertă, CR3).

## 10. Rapoarte

Configurarea are doar liste exportabile (pipeline-uri, reguli, șabloane). Rapoartele sunt în CR5.

## 11. Ecrane principale

Machetele sunt în [ecrane/cr1-configurare/](ecrane/cr1-configurare/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Pipeline-uri și etape | Pipeline-ul „HoReCa Cluj”: etapele cu probabilitate, categorie, zile maxime, criterii de ieșire | `cr1-01-pipeline-etape` |
| Atribuire și scor | Coada „Lead-uri noi”, regulile de atribuire în ordine, capacitatea agenților, regulile de scor | `cr1-02-atribuire-scor` |
| Configurare › CRM | Setările firmei și nomenclatoarele, în ecranul comun de configurare | `cr1-03-configurare-crm` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| CR1-A1 | Seed-ul creează pipeline-urile livrate cu `WON` și `LOST` nedeletabile (`63001`) | Test de integrare |
| CR1-A2 | Ștergerea unei etape cu oportunități e refuzată (`63002`) | Test de integrare |
| CR1-A3 | Regula de atribuire din 4.2 împarte 10 lead-uri 5 / 5; la capacitate plină, lead-ul rămâne în coadă | Test de integrare |
| CR1-A4 | Clientul din portofoliul Cristinei Marin merge la ea, indiferent de regulă | Test de integrare |
| CR1-A5 | Probabilitatea statistică pe datele de test dă valorile așteptate (Bayes naiv cu netezire) | Test unitar |
| CR1-A6 | Un șablon de campanie fără dezabonare nu se salvează (`63006`) | Test unitar |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Clientul și persoana de contact | Cele din Nucleu (`Partner`, `PartnerContact`); CRM-ul nu are cont sau contact propriu | ✓ Aprobat autonom (27.11.2026) |
| 2 | Agenții și echipele | Cele din Vânzări V1; CRM-ul adaugă doar cozi, capacitate și reguli | ✓ Aprobat autonom |
| 3 | Pipeline-uri | Mai multe pe firmă, cu etapele `WON` / `LOST` fixe | ✓ Aprobat autonom |
| 4 | Probabilitate și prognoză | Din etapă; agentul poate muta doar categoria spre `BestCase` / `Commit`; probabilitatea doar cu setare | ✓ Aprobat autonom |
| 5 | Atribuirea | Portofoliul V1 întâi, apoi regulile, cu round-robin și capacitate | ✓ Aprobat autonom |
| 6 | Scorul | Pe reguli din prima versiune; probabilitatea statistică opțională, calculată local | ✓ Aprobat autonom |
| 7 | Motivele de pierdere | Comune cu oferta V3 | ✓ Aprobat autonom |
| 8 | Urmărirea deschiderii e-mailurilor | Nu (GDPR); doar răspunsuri și dezabonări | ✓ Aprobat autonom |
| 9 | Vizibilitatea | Pe portofoliu implicit, pe firmă prin setare | ✓ Aprobat autonom |
| 10 | Tranzacțiile | `LD`, `OP`, `CMP`, `CAZ` prin Nucleu §3.6; activitățile fără număr | ✓ Aprobat autonom |

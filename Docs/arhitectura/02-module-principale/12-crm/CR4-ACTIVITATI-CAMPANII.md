# ERP · 02.12 CRM · CR4 Activități, comunicări și campanii: cronologie, secvențe, vizite, e-mail și calendar, campanii, cazuri

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P9–P11, P15, P21, P23). A patra din cele 5 părți ale modulului 02.12.

Plaja de erori CR4: `63450–63649`. Documente părinte: [CR1 Configurare](CR1-CONFIGURARE.md) (tipuri de activitate, rezultate, șabloane, setări), [CR2](CR2-LEADURI-CONTACTE.md) (lead-uri, consimțăminte), [CR3](CR3-OPORTUNITATI-PIPELINE.md) (oportunități), [Nucleu](../01-nucleu/NUCLEU.md) (utilizatori, notificări, joburi, atașamente, jurnal de audit), [Vânzări V5](../03-vanzari/V5-FACTURARE.md) și [V6](../03-vanzari/V6-RETURURI-STORNO.md) (factura și returul pe care le poate referi un caz). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- **Activitatea** (`crm.Activity`) e tot ce face un om cu un client: apel, e-mail, întâlnire, vizită, sarcină, demonstrație, notă. Are un subiect (lead, oportunitate, partener, persoană de contact, caz sau campanie), un responsabil, un termen sau un interval și un rezultat. Nu are număr de document.
- **Cronologia** clientului (P9) e lista unificată a activităților, a schimbărilor din CRM și a evenimentelor din alte module (oferte, comenzi, facturi, încasări, retururi), citită de fișa 360 (CR5).
- **E-mailul și calendarul** se sincronizează on-premise: Microsoft 365 prin Microsoft Graph (gratuit, aplicație înregistrată de client în Entra ID) sau orice server IMAP / SMTP cu **MailKit** (open source); calendarul prin Graph sau CalDAV. Nu se urmăresc deschiderile (decizia 8 din CR1).
- **Campaniile** trimit e-mailuri în masă prin serverul SMTP al clientului, în loturi, cu dezabonare obligatorie și doar către persoanele care au voie să fie contactate (`IContactConsentQuery`, CR2).
- **Cazurile** (`CAZ`) sunt reclamațiile și cererile după vânzare, cu termen de primă reacție (SLA). Service-ul complet (intervenții, piese, contracte de service) **nu** e în acest modul.

## 1. Scop și perimetru

| Zonă | Ce face CR4 | Ce NU face |
| --- | --- | --- |
| Activități | Planificare, memento-uri, rezultat, recurență, activități de echipă, „Activitățile mele” și calendarul | — |
| Secvențe | Rularea șabloanelor de secvență (CR1 §3.5) pe lead-uri, contacte, oportunități; oprirea automată | Definirea șabloanelor (CR1) |
| Vizite de teren | Vizita cu check-in (locație), raport de vizită pe șablon, comanda luată la vizită (deschide comanda V3) | Rutele (02.07 TMS) |
| E-mail și calendar | Sincronizarea căsuțelor agenților, legarea automată după adresă, trimiterea din CRM pe șablon, calendarul bidirecțional | Serverul de e-mail |
| Campanii | Liste țintă (statice și dinamice), trimitere de e-mail în masă, evenimente cu înscriere, campanii de telesales, cost, răspunsuri, atribuire | Publicitatea plătită |
| Cazuri | Reclamații și cereri, cu SLA, legate de factură, lot, retur; răspunsul pe e-mail | Service tehnic, garanții detaliate |

## 2. Model de domeniu

```
 crm.Activity
   ├─ ActivityTypeId · Subject · Body (HTML, fără scripturi) · Direction (intrare / ieșire)
   ├─ subiectul: LeadId? · OpportunityId? · PartnerId? · PartnerContactId? · CaseId? · CampaignId?
   ├─ OwnerUserId · AssignedUserIds (participanți interni) · DueAt | StartAt–EndAt · AllDay · ReminderMinutes
   ├─ Status: 1 Planificată · 2 Realizată · 3 Anulată · OutcomeId · CompletedAt
   ├─ Location? · CheckInLat/Lng/At? (vizite) · VisitReportJson? (răspunsurile la șablon)
   ├─ RecurrenceRule? (RFC 5545 RRULE, de exemplu vizită lunară)
   ├─ SequenceEnrollmentStepId? · ExternalMessageId? (e-mail sincronizat) · ExternalEventId? (calendar)
   └─ crm.ActivityAttachment × N (prin atașamentele Nucleu)

 crm.SequenceEnrollment (subiect × șablon) ── crm.SequenceEnrollmentStep × N (planificat, executat, oprit)
 crm.Mailbox (utilizator, furnizor, sincronizare) ── crm.EmailMessage × N (antet, corp, legături)
 crm.Campaign (CMP 000041) ── crm.CampaignList × N ── crm.CampaignMember × N (contact sau lead, stare, răspuns)
                          ├─ crm.CampaignCost × N
                          └─ crm.CampaignSend × N (lot, trimise, respinse, dezabonări)
 crm.Case (CAZ 000093) ── PartnerId · ContactId · Category · Priority · SlaDueAt · Status · legături (factură, lot, retur, comandă)
```

Reguli de business:

1. **Activitatea are cel puțin un subiect** (`63451`); la salvare, dacă subiectul e o oportunitate sau un lead, se completează automat partenerul și persoana, ca activitatea să apară pe cronologia clientului.
2. **Activitatea realizată cere rezultatul** pentru tipurile Apel, Vizită, Întâlnire (`63452`).
3. **Activitățile nu se șterg** după realizare (`63453`); se anulează cu motiv (audit). Notele se pot edita de autor 24 de ore.
4. **Trimiterea de marketing respectă consimțământul**: membrul de campanie fără voie pe canal e exclus la trimitere și marcat „Exclus – fără consimțământ” (`63520` informativ). E-mailul de la om la om (răspuns, ofertă, discuție de lucru) nu e marketing și nu are această verificare.
5. **Dezabonarea e imediată**: linkul din subsol scrie `Withdrawn` (CR2 §3.8) și oprește toate secvențele de marketing ale persoanei.
6. **Cazul are SLA**: termenul primei reacții = creare + `CaseSlaHours` (ore lucrătoare după calendarul firmei din Nucleu, sau după prioritate); cazul neatins la termen devine „Depășit” și urcă la șeful echipei de relații clienți.

## 3. Fluxuri detaliate

### 3.1 Activitățile și calendarul

- Ecranul **„Activitățile mele”**: întârziate, azi, săptămâna aceasta, cu acțiunile rapide „Realizată”, „Reprogramează”, „Apel” (deschide `tel:` pe telefon), „E-mail”.
- **Calendarul** (zi, săptămână, lună, echipă): activitățile cu interval; tragerea mută intervalul; vizitele afișează adresa; calendarul echipei arată disponibilitatea pentru programarea întâlnirilor.
- **Memento-urile** pleacă prin notificările Nucleu (în aplicație, e-mail) cu `ReminderMinutes` înainte.
- **După realizarea unei activități** se propune pasul următor (o activitate nouă pe același subiect), ca oportunitatea să nu rămână „fără pas următor” (CR3 regula 4).

### 3.2 Secvențele (P10)

Înscrierea unui subiect într-o secvență creează pașii cu datele calculate (zile lucrătoare). Jobul `CrmSequenceRunner` (la 15 minute): pașii „e-mail automat” se trimit prin căsuța proprietarului, în fereastra orară setată (08:00–18:00), cu maximum 200 pe oră pe căsuță; pașii „apel”, „sarcină”, „vizită”, „e-mail manual” devin activități planificate ale proprietarului. Oprirea: răspuns primit de la persoană (după firul de e-mail), etapă schimbată (CR3), lead calificat sau descalificat (CR2), dezabonare, oprire manuală. Secvența livrată „Întâmpinare lead web”: ziua 0 e-mail de mulțumire, ziua 1 apel, ziua 3 e-mail cu catalogul, ziua 7 apel, ziua 14 sarcină „Decide: califică sau descalifică”.

### 3.3 Vizitele de teren (P23)

Agentul de teren (Vlad Georgescu) are vizitele zilei pe telefon (interfața web adaptivă a ERP-ului). La sosire, „Check-in” salvează locația (doar cu permisiunea browserului și doar la acțiune, nu urmărire continuă, decizia 6) și ora. Raportul de vizită urmează șablonul (CR1 §3.5): prezența produselor la raft, concurenții văzuți, stocul la client, observații, fotografii (atașamente). „Ia comandă” deschide comanda V3 pentru client, cu `SalesChannel = Teren`; comanda apare pe cronologie legată de vizită. Vizitele recurente (lunare la clienții A) se generează din `RecurrenceRule`.

### 3.4 Sincronizarea e-mailului și a calendarului (P11)

| Furnizor | E-mail | Calendar | Autentificare |
| --- | --- | --- | --- |
| Microsoft 365 | Microsoft Graph (citire din Inbox / Sent pe dosarele alese, trimitere) | Graph, bidirecțional | Aplicație înregistrată de client în Entra ID, permisiuni delegate per utilizator (consimțământul utilizatorului) |
| IMAP / SMTP (orice server, inclusiv Exchange on-premise și Gmail) | MailKit, IMAP IDLE sau citire la 5 minute | CalDAV (opțional) sau fără | Utilizator și parolă de aplicație, salvate criptat (Nucleu, protecția datelor) |

**Legarea automată:** mesajul se leagă de persoana de contact și de lead după adresa expeditorului sau a destinatarilor; de oportunitate, dacă firul de discuție a pornit din CRM sau dacă persoana are o singură oportunitate deschisă. Mesajele cu adrese necunoscute nu se importă (protecția datelor), doar dacă agentul le leagă manual (butonul „Leagă de CRM”). Mesajele interne (doar domeniile firmei) se ignoră. Atașamentele se salvează la cerere.

**Trimiterea din CRM:** fereastra de e-mail cu șablon (CR1 §3.5), câmpurile completate, semnătura agentului; pleacă prin căsuța agentului (apare în „Trimise” ale lui), se salvează ca activitate.

### 3.5 Campaniile (P15)

| Tranzacție | Ce e | Ce are în plus |
| --- | --- | --- |
| `CMP-EMAIL` | Newsletter, ofertă promoțională | Șablon, trimitere programată în loturi, dezabonare, răspunsuri |
| `CMP-EVENT` | Târg, degustare, eveniment | Invitații, înscrieri (formular web cu `CampaignId`), prezență, lead-uri create la eveniment |
| `CMP-TEL` | Telesales | Lista de apelat împărțită pe agenți, rezultat pe apel |

**Listele țintă:** statice (selectate sau importate) sau dinamice (filtru salvat: industrie, județ, segment, scor, „a cumpărat articolul X în ultimele 6 luni” prin `sales.vw_SalesFact`, „fără comandă în 90 de zile”), înghețate la lansare. Membrii sunt persoane de contact sau lead-uri.

**Trimiterea:** jobul `CrmCampaignSender` trimite prin SMTP-ul configurat pentru campanii (poate fi altul decât al agenților, de exemplu un serviciu SMTP local), în loturi de 100 la 1 minut (setabil), cu antetul `List-Unsubscribe` (RFC 8058) și linkul din subsol. Respingerile permanente (bounce) marchează adresa „Invalidă” pe contact. Starea pe membru: Exclus · Programat · Trimis · Respins · Răspuns · Dezabonat · Înscris / Prezent (evenimente) · Lead creat · Oportunitate.

**Costul și atribuirea:** costurile campaniei (manuale sau din facturile de furnizor legate, A5: câmpul `CampaignId` opțional pe linie, precizare) și veniturile atribuite: oportunitățile câștigate cu `CampaignId` (din lead sau setat manual), atribuire la prima atingere. ROI = (venit câștigat − cost) / cost (CR5).

### 3.6 Cazurile după vânzare (P21)

| Câmp | Reguli |
| --- | --- |
| `Category` | Calitate produs · Livrare (întârziere, lipsă, deteriorare) · Factură · Cerere de informații · Altele |
| `Priority` | Scăzută · Normală · Ridicată · Critică (SLA: 48 / 24 / 8 / 2 ore lucrătoare, setabile) |
| Legături | Factură (V5), aviz (V4), lot (S4), comandă (V3), retur (V6) |
| `Status` | 1 Nou · 2 În lucru · 3 Așteaptă clientul · 4 Rezolvat · 5 Închis · 6 Anulat |
| `Resolution` | Text, plus acțiunea făcută: retur creat (deschide V6 cu factura legată), notă de credit, înlocuire, informare |

Cazul se creează manual, din e-mailul primit pe adresa de relații clienți (`CAZ-RECL` sau `CAZ-CERERE` după cuvinte cheie configurabile) sau din fișa 360. Răspunsurile pe e-mail păstrează numărul cazului în subiect („[CAZ 000093]”), ca răspunsurile clientului să se lege. Închiderea trimite opțional o întrebare de satisfacție (1–5) printr-un link.

## 4. Exemple

### 4.1 Vizita lui Vlad Georgescu la Pizzeria Napoli (02.12.2026)

Vizita lunară recurentă, 10:30–11:00. Check-in la 10:34 la adresa clientului. Raportul: apa 0,5 l prezentă, cafeaua lipsă din 28.11, concurentul „AquaVita” cu dozator nou. Vlad ia comanda **CV 4468** (V3, canal Teren): 20 bax apă 0,5 l, 4 kg cafea. Activitate următoare propusă: „Ofertă dozator” pe 09.12.

### 4.2 Campania „Degustare HoReCa Cluj – decembrie” (CMP 000041, `CMP-EVENT`)

Lista dinamică: contacte din industria HoReCa, județul Cluj, cu consimțământ pentru e-mail pe scopul `EVENTS` → 412 persoane, din care 37 excluse (fără consimțământ) și 6 adrese invalide; trimise 369. Rezultate până pe 27.11: 58 de înscrieri, 9 dezabonări, 3 respingeri. Cost: sala 2.500 lei, produse 1.800 lei, materiale 700 lei = 5.000 lei. Lead-uri create: 14; oportunități: 6, din care una câștigată (Cafe Unirii, al doilea punct de lucru, 1.450 lei pe lună). Hotel Belvedere (OP 000184) nu e atribuit campaniei, deși Andreea Pop s-a înscris la degustare: lead-ul a venit înainte, din formularul site-ului, iar atribuirea e la prima atingere.

### 4.3 Reclamația Market Turda (CAZ 000093)

24.11.2026 09:12, e-mail pe relatii.clienti@: „Am primit 6 baxuri cu ambalajul deteriorat pe avizul EXD 30632.” Cazul `CAZ-RECL`, categoria Livrare, prioritatea Ridicată (SLA 8 ore lucrătoare, termen 24.11 17:12), alocat Elenei Dobre (operator relații clienți). Prima reacție la 10:05 (e-mail de răspuns). Pe 25.11 se creează returul V6 legat de factura avizului; cazul trece în „Rezolvat”, închis pe 26.11 cu nota de satisfacție 4.

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `ActivitateRealizata` v1 | Publicat | Tip, rezultat, subiect | CR2 (scor), CR3 (pas următor), CR5 |
| `CampanieLansata` v1, `CampanieInchisa` v1 | Publicat | Membri, cost | CR5 |
| `CazCreat` v1, `CazInchis` v1 | Publicat | Categorie, SLA respectat, satisfacție | CR5, 02.13 |
| `ComandaConfirmata`, `FacturaEmisa`, `IncasareInregistrata`, `ReturCreat` v1 | Consumate (Vânzări, Finanțe) | Partener, document, valoare | Rânduri pe cronologie (fără copierea documentului, doar referința) |
| `OportunitateCastigata` v1 | Consumat (CR3) | `CampaignId` | Venitul atribuit campaniei |

## 6. Contracte publice

```csharp
public interface ICrmTimelineWriter     // folosit de modulele care vor să scrie pe cronologia clientului fără eveniment dedicat
{
    Task AddAsync(TimelineEntry entry, CancellationToken ct);
}
public interface ICrmActivityQuery      // folosit de CR5 și de 02.13
{
    Task<IReadOnlyList<ActivityDto>> GetForPartnerAsync(int partnerId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

## 7. Schema SQL (partea CR4)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `crm.Activity` | `Id bigint` | IX `(OwnerUserId, Status, DueAt)`; IX `(PartnerId, CompletedAt DESC)`; IX `(OpportunityId)`; IX `(LeadId)`; UX `(MailboxId, ExternalMessageId) WHERE ExternalMessageId IS NOT NULL`; `CHECK` cel puțin un subiect |
| `crm.TimelineEntry` | `Id bigint` | IX `(PartnerId, OccurredAt DESC)`; referințele la documente din alte module ca `(SourceModule, SourceDocType, SourceId)` fără FK |
| `crm.SequenceEnrollment`, `crm.SequenceEnrollmentStep` | `Id bigint` | UX `(SequenceTemplateId, SubjectKind, SubjectId) WHERE Status = 1` (o singură înscriere activă) |
| `crm.Mailbox` | `Id int` | UX `(UserId)`; parola sau tokenul criptate (Nucleu `IDataProtector`) |
| `crm.EmailMessage` | `Id bigint` | UX `(MailboxId, InternetMessageId)` |
| `crm.Campaign`, `crm.CampaignList`, `crm.CampaignMember`, `crm.CampaignCost`, `crm.CampaignSend` | `Id bigint` | UX `(CompanyId, DocumentNumber)`; UX `(CampaignId, PartnerContactId)`; UX `(CampaignId, LeadId)` |
| `crm.Case`, `crm.CaseLink` | `Id bigint` | UX `(CompanyId, DocumentNumber)`; IX `(Status, SlaDueAt)` |

**Erorile CR4** (`63450–63649`): `63451` `CRM_ACTIVITY_SUBJECT_REQUIRED` · `63452` `CRM_ACTIVITY_OUTCOME_REQUIRED` · `63453` `CRM_ACTIVITY_COMPLETED_LOCKED` · `63460` `CRM_SEQUENCE_ALREADY_ENROLLED` · `63470` `CRM_MAILBOX_AUTH_FAILED` · `63471` `CRM_MAIL_SEND_FAILED` · `63500` `CRM_CAMPAIGN_NO_TEMPLATE` · `63501` `CRM_CAMPAIGN_LIST_FROZEN` · `63520` `CRM_CAMPAIGN_MEMBER_NO_CONSENT` (informativ) · `63540` `CRM_CASE_LINK_INVALID` · `63541` `CRM_CASE_CLOSED_READONLY`.

## 8. Permisiuni

`Crm.Activity.*` (pe subiectele vizibile, CR1 §3.7), `Crm.Mailbox.Connect` (fiecare utilizator pe căsuța lui), `Crm.Campaign.View`, `.Manage`, `.Send` (Marketing, Manager vânzări), `Crm.Case.View`, `.Manage`, `.Close` (Operator relații clienți), `Crm.Sequence.Enroll`.

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă |
| --- | --- |
| Legea 506/2004, GDPR art. 21 | Consimțământ pentru marketing, dezabonare într-un clic, `List-Unsubscribe` |
| GDPR art. 5 (minimizare) | Doar mesajele cu persoane cunoscute se importă; fără urmărirea deschiderilor; locația doar la check-in |
| Codul muncii (monitorizarea angajaților) | Check-in-ul e la acțiunea agentului, nu urmărire continuă |
| Vânzări V3, V4, V5, V6 | `SalesChannel = Teren` pe comandă (există în V1 ca nomenclator, precizare), cazurile se leagă de documente, returul din caz |
| Achiziții A5 | `CampaignId` opțional pe linia facturii de furnizor, pentru costul campaniei (precizare) |
| Nucleu | Notificări, joburi, atașamente, protecția secretelor, calendarul lucrător |

## 10. Rapoarte

„Activitățile mele”, „Activități pe echipă și tip”, „Vizite realizate vs planificate”, lista campaniilor cu rezultate, „Cazuri cu SLA depășit”. Rapoartele de analiză sunt în CR5.

## 11. Ecrane principale

Machetele sunt în [ecrane/cr4-activitati/](ecrane/cr4-activitati/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Activitățile mele și calendarul | Săptămâna Cristinei Marin: întârziate, azi, calendarul cu întâlniri și vizite | `cr4-01-activitati-calendar` |
| Raportul de vizită | Vizita lui Vlad Georgescu la Pizzeria Napoli: check-in, întrebările, „Ia comandă” | `cr4-02-raport-vizita` |
| Formularul campaniei (principal) | CMP 000041: lista, excluderi, trimitere, rezultate, cost, ROI | `cr4-03-campanie` |
| Formularul cazului | CAZ 000093 Market Turda: SLA, legături, răspunsuri, rezolvare | `cr4-04-caz` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| CR4-A1 | Activitatea pe OP 000184 apare pe cronologia partenerului Hotel Belvedere | Test de integrare |
| CR4-A2 | Răspunsul persoanei oprește secvența „Întâmpinare lead web” înainte de pasul 3 | Test de integrare cu MailKit pe un server IMAP de test (GreenMail sau similar, open source) |
| CR4-A3 | CMP 000041 exclude cele 37 de persoane fără consimțământ și trimite 369 | Test de integrare |
| CR4-A4 | Dezabonarea din link scrie `Withdrawn` și scoate persoana din trimiterile programate | Test de integrare |
| CR4-A5 | CAZ 000093 neatins la 17:12 devine „Depășit” și notifică șeful | Test de integrare cu ceasul simulat |
| CR4-A6 | Mesajele cu adrese necunoscute nu se importă | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Sincronizarea e-mailului | Microsoft Graph și IMAP / SMTP cu MailKit; fără servicii plătite | ✓ Aprobat autonom (27.11.2026) |
| 2 | Ce mesaje se importă | Doar cele cu persoane cunoscute în CRM; restul la cerere | ✓ Aprobat autonom |
| 3 | Secvențele | Pe șabloane, cu oprire automată la răspuns, etapă sau dezabonare | ✓ Aprobat autonom |
| 4 | Campaniile | E-mail în masă prin SMTP-ul clientului, cu loturi, consimțământ, dezabonare, ROI la prima atingere | ✓ Aprobat autonom |
| 5 | Cazurile | Reclamații și cereri cu SLA, legate de documente; service complet în afara modulului | ✓ Aprobat autonom |
| 6 | Locația agenților | Doar la check-in, la acțiunea agentului | ✓ Aprobat autonom |
| 7 | Aplicația de teren | Interfața web adaptivă a ERP-ului, fără aplicație nativă în prima versiune | ✓ Aprobat autonom |

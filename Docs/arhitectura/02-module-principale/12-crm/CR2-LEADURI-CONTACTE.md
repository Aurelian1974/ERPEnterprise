# ERP · 02.12 CRM · CR2 Lead-uri și persoane de contact: captare, deduplicare, scor, atribuire, calificare, GDPR

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P1–P3, P12, P13, P16, P22). A doua din cele 5 părți ale modulului 02.12.

Plaja de erori CR2: `63100–63249`. Documente părinte: [CR1 Configurare](CR1-CONFIGURARE.md) (surse, motive, cozi, reguli de atribuire și de scor, setări), [Nucleu](../01-nucleu/NUCLEU.md) (`core.Partner`, `core.PartnerContact`, validarea CUI, chei API, importuri), [Vânzări V1](../03-vanzari/V1-CONFIGURARE.md) (agenți și portofolii). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- **Lead-ul** e o persoană sau o firmă care a arătat interes și nu e încă verificată. Stă în `crm.Lead`, **nu** în `core.Partner`, ca lista de parteneri (folosită de facturare, de SAF-T, de e-Factura) să nu se umple cu contacte necalificate.
- **La calificare** (P1, P2), lead-ul devine: partenerul din Nucleu (nou sau existent, legat după CUI), persoana de contact (`core.PartnerContact`) și una sau mai multe oportunități (CR3). Lead-ul rămâne, în starea „Calificat”, cu legăturile spre ce a creat.
- Persoana de contact a clientului existent e **tot** `core.PartnerContact`; CR2 îi adaugă câmpurile CRM (rol în decizie, consimțăminte, rețele) într-un tabel 1:1 `crm.ContactProfile`.
- Toate prelucrările rulează on-premise: formularul web trimite la un endpoint al ERP-ului (cu cheie API), e-mailurile se citesc cu MailKit (open source), scorul se calculează local.

## 1. Scop și perimetru

| Zonă | Ce face CR2 | Ce NU face |
| --- | --- | --- |
| Captare | Manual, formular web (endpoint REST), import CSV / XLSX, e-mail primit pe adresa de lead-uri, răspuns la campanie, agentul de teren pe mobil | Trimiterea campaniilor (CR4) |
| Deduplicare | Față de lead-urile deschise, de parteneri și de persoanele de contact, pe CUI, e-mail, telefon normalizat, nume | Fuziunea partenerilor (Nucleu) |
| Îmbogățire | Validarea CUI și preluarea datelor firmei prin serviciul ANAF de verificare a TVA (02.14, dacă e licențiat) | — |
| Scor și atribuire | Aplică regulile din CR1 la creare și la fiecare activitate | Definirea regulilor (CR1) |
| Calificare | Convertirea în partener + contact + oportunitate; descalificare cu motiv; reactivare; fuziunea a două lead-uri | Oportunitatea (CR3) |
| Contacte | Profilul CRM al persoanei de contact, rolul în decizie, consimțămintele GDPR, anonimizarea | Datele de bază ale persoanei (Nucleu) |

## 2. Model de domeniu

```
 crm.Lead (LD 000001…)
   ├─ date firmă: CompanyName, TaxId (CUI), County, City, Industry, Segment, Website, EmployeesBand
   ├─ date persoană: FirstName, LastName, JobTitle, Email, Phone (E.164), PreferredChannel
   ├─ LeadSourceId, CampaignId?, Message, UtmSource/Medium/Campaign, FormCode
   ├─ OwnerAgentId?, LeadQueueId?, Score, ScoreBand, PredictiveProbability?
   ├─ Status: 1 Nou · 2 În lucru · 3 Calificat · 4 Descalificat · 5 Fuzionat
   ├─ crm.LeadDuplicateCandidate × N           (ce s-a găsit la deduplicare și ce a decis operatorul)
   ├─ crm.LeadScoreEvent × N                   (fiecare punct adăugat sau scăzut, cu regula)
   └─ la calificare: PartnerId, PartnerContactId, crm.LeadOpportunity × N (cel mult 5)

 core.PartnerContact ── crm.ContactProfile (1:1)   (rol în decizie, interese, LinkedIn, ziua de naștere opțională)
                    └─ crm.Consent × N             (canal × scop, stare, temei, sursă, dată, dovadă)
 crm.Lead ── crm.Consent × N                       (consimțămintele date înainte de calificare, mutate pe contact)
```

Reguli de business:

1. **Un lead nu are număr fiscal obligatoriu.** Persoana fizică poate fi lead; la calificare devine partener persoană fizică (Nucleu), cu CNP doar dacă e nevoie pentru facturare.
2. **Deduplicarea rulează la fiecare creare și la fiecare modificare a CUI-ului, e-mailului sau telefonului** (3.2). În modul blocant (`DuplicateCheckMode = 2`), un CUI deja existent pe un lead deschis sau pe un partener activ cere legarea sau o confirmare cu motiv (`63101`).
3. **Calificarea cere** nume de firmă sau de persoană, cel puțin un canal de contact și, pentru firmă, CUI validat sau bifa „Firmă străină fără CUI românesc” (`63110`).
4. **Descalificarea cere motivul** din `crm.DisqualifyReason` (`63111`). Lead-ul descalificat poate fi **reactivat** (revine la „În lucru”, cu istoricul păstrat).
5. **Lead-ul calificat nu se mai modifică** (`63112`), cu excepția notelor; corecțiile se fac pe partener și pe contact.
6. **Marketingul nu trimite fără consimțământ** pe canalul respectiv dacă `ConsentRequiredForMarketing = 1` (CR4 verifică, CR2 ține datele).

## 3. Entități și fluxuri detaliate

### 3.1 Captarea

| Canal | Cum intră | Tranzacția | Detalii |
| --- | --- | --- | --- |
| Manual | Ecranul „Lead nou” sau din telefonul primit | `LD-MAN` | Agentul e proprietar implicit, dacă nu alege coada |
| Formular web | `POST /api/crm/leads/webform` cu cheie API (Nucleu, tip „Formular web”, legată de o firmă și de un `FormCode`) | `LD-WEB` | Câmpurile formularului se mapează pe câmpuri ale lead-ului; câmpul ascuns „capcană” (honeypot) și limita de 20 de cereri pe minut pe IP blochează spam-ul (`63120`); bifele de consimțământ ale formularului devin `crm.Consent` cu textul afișat salvat ca dovadă |
| Import | Fișier CSV sau XLSX (ClosedXML), cu maparea coloanelor salvată ca șablon | `LD-IMP` | Previzualizare, apoi import în lot; rândurile cu erori sau duplicate apar într-un raport descărcabil, cele bune se importă (`63121` pe rând). Lista importată cere sursa și temeiul GDPR pe lot (`63122`) |
| E-mail | Căsuța „lead-uri@…” citită la 5 minute (MailKit IMAP sau Microsoft Graph, CR4) | `LD-MAIL` | Expeditorul deja cunoscut ca persoană de contact duce la o activitate pe partener, nu la lead |
| Campanie | Răspunsul la o campanie (formular de eveniment, răspuns la e-mail) | `LD-CMP` | `CampaignId` completat; atribuirea oportunității la campanie (CR4) |
| Agent de teren | Aplicația mobilă (sau web pe telefon), cu locația opțională | `LD-MAN` | Sursa „Agent de teren” |

Contractul endpoint-ului (pentru integratori):

```json
POST /api/crm/leads/webform
X-Api-Key: <cheia formularului>
{
  "formCode": "SITE-CONTACT",
  "companyName": "Hotel Belvedere Cluj SRL",
  "taxId": "RO18234567",
  "firstName": "Andreea", "lastName": "Pop", "jobTitle": "Manager achiziții",
  "email": "andreea.pop@belvedere-cluj.ro", "phone": "0744 512 300",
  "county": "CJ", "message": "Aprovizionare lunară apă și cafea pentru 120 de camere și restaurant.",
  "consents": [{ "channel": "EMAIL", "purpose": "MARKETING", "granted": true, "text": "Sunt de acord să primesc oferte pe e-mail." }],
  "utm": { "source": "google", "medium": "cpc", "campaign": "horeca-2026" },
  "website": ""          // capcana: dacă vine completată, cererea e ignorată
}
→ 201 { "leadNumber": "LD 000318", "status": "Nou" }
```

### 3.2 Deduplicarea

| Cheie | Normalizare | Unde caută | Rezultat |
| --- | --- | --- | --- |
| CUI | fără „RO”, fără spații | Lead-uri deschise, parteneri | Potrivire exactă → blocant în modul 2 |
| E-mail | litere mici, fără spații | Lead-uri deschise, `PartnerContact`, `Partner.Email` | Potrivire exactă → blocant în modul 2 |
| Domeniul e-mailului | fără domeniile gratuite (listă livrată: gmail.com, yahoo.com, outlook.com…) | Parteneri cu același domeniu | Avertisment „posibil aceeași firmă” |
| Telefon | E.164 (`+40744512300`) | Lead-uri, contacte, parteneri | Avertisment |
| Nume firmă | fără forma juridică (SRL, SA, SNC), fără diacritice (colația CI_AI, `SQL-24`), distanța Levenshtein ≤ 2 | Lead-uri, parteneri | Avertisment |

Candidații se salvează în `crm.LeadDuplicateCandidate` cu decizia operatorului (Legat de partener, Fuzionat cu lead-ul X, Nu e duplicat). Pe formularul web și la import, potrivirea exactă cu un lead deschis **adaugă o activitate** pe lead-ul existent („Formular completat din nou”) în loc să creeze unul nou.

### 3.3 Îmbogățirea

La introducerea CUI-ului, butonul „Preia datele” (și automat la formularul web) cheamă `IAnafCompanyLookup` (02.14): denumirea, adresa, înregistrarea în scopuri de TVA, starea (activă, radiată, inactivă). Firma radiată sau inactivă fiscal pune un avertisment roșu pe lead și scade 50 de puncte (regula `DECAY` livrată „Firmă inactivă”). Fără 02.14 licențiat, butonul lipsește și CUI-ul se validează doar cu cifra de control (Nucleu).

### 3.4 Scorul și atribuirea

La creare și la fiecare activitate (CR4), `LeadScoringService` aplică regulile CR1 §3.4 și scrie `crm.LeadScoreEvent`. Scorul ajuns în banda „fierbinte” trimite o notificare proprietarului. Lead-ul fără proprietar trece prin atribuire (CR1 §3.3): portofoliul V1 (dacă CUI-ul aparține unui client existent cu agent), apoi regulile. Lead-ul nerezolvat în `StaleWarningDays` zile apare în „De rezolvat” la șeful echipei.

### 3.5 Calificarea (`QualifyLead`)

Ecranul de calificare are trei pași:

1. **Partenerul.** Dacă s-a găsit un partener după CUI, se propune legarea; altfel se creează unul nou cu datele din lead (și din ANAF), cu rolul „Client potențial” (`PartnerRole = Prospect`, precizare în Nucleu). Rolul devine „Client” la prima factură (eveniment `FacturaEmisa`).
2. **Persoana de contact.** Legarea la un `PartnerContact` existent după e-mail sau crearea unuia nou; consimțămintele lead-ului se mută pe contact.
3. **Oportunitatea.** Una implicit (pipeline-ul implicit, etapa întâi, valoarea estimată, data estimată de închidere), cu posibilitatea de a adăuga până la 5 (`63113` peste). Se poate califica și **fără oportunitate** (doar partener și contact), de exemplu pentru un partener care va primi doar campanii.

Totul rulează într-o singură tranzacție SQL; lead-ul trece în „Calificat”, se publică `LeadCalificat`.

### 3.6 Descalificarea, reactivarea și fuziunea

- **Descalificare:** motivul, nota, opțional „Nu mai contacta” (pune consimțămintele pe „Retras” pe toate canalele de marketing).
- **Reactivare:** lead-ul descalificat revine la „În lucru”; motivul anterior rămâne în istoric.
- **Fuziune:** două lead-uri duplicate devin unul; activitățile, consimțămintele și evenimentele de scor trec pe cel păstrat, celălalt trece în „Fuzionat” cu legătura (`63114` dacă unul e calificat).

### 3.7 Persoanele de contact și profilul CRM

`crm.ContactProfile`: `DecisionRole` (Decident · Influențator · Utilizator · Aprobator financiar · Gardian), `Interests` (etichete), `LinkedInUrl`, `BirthDate` opțional (cu temei; implicit ascuns), `PreferredChannel`, `DoNotCall`, `LastActivityAt` (calculat). Persoana de contact se vede în fișa 360 (CR5) cu toate activitățile ei.

### 3.8 Consimțămintele GDPR (`crm.Consent`, P22)

| Câmp | Reguli |
| --- | --- |
| `Channel` | `EMAIL` · `SMS` · `PHONE` · `POST` |
| `Purpose` | `MARKETING` (oferte, newsletter) · `EVENTS` (invitații) · `SURVEY` |
| `Status` | `Granted` · `Withdrawn` · `Unknown` |
| `LegalBasis` | Consimțământ · Interes legitim (relație contractuală existentă, art. 6(1)(f) GDPR, cu dezabonare oferită) · Contract |
| `Source`, `CapturedAt`, `CapturedByUserId`, `EvidenceText` | De unde vine (formular, import, telefon), textul afișat, cine l-a introdus |

Fiecare schimbare e un rând nou (istoric complet, nu se suprascrie). Dezabonarea din e-mail (CR4) scrie `Withdrawn` cu sursa „Link dezabonare”.

**Dreptul de a fi uitat:** acțiunea „Anonimizează persoana” (rolul Manager vânzări) înlocuiește numele, e-mailul, telefonul și notele cu „Anonimizat #id” pe lead, pe profil și pe `PartnerContact`, păstrează activitățile fără conținut personal și scrie jurnalul de audit (Nucleu). Persoana legată de documente fiscale (facturi) nu se anonimizează pe documente (obligația de păstrare 10 ani, Legea 82/1991), doar în CRM (`63130` informativ).

## 4. Exemplu complet: Hotel Belvedere Cluj

1. **26.11.2026 18:42**, formularul de pe site creează **LD 000318** „Hotel Belvedere Cluj SRL”, Andreea Pop, manager achiziții, CUI RO18234567, județul Cluj, sursa „Formular site”, mesajul „Aprovizionare lunară apă și cafea pentru 120 de camere și restaurant”. Consimțământ e-mail marketing: da.
2. Deduplicarea: niciun partener cu CUI-ul; domeniul `belvedere-cluj.ro` nu există. ANAF: firmă activă, plătitoare de TVA.
3. Scor: formular +20, HoReCa +15, Cluj în zone +10, e-mail de firmă +5, CUI valid +10 = **60, „fierbinte”**. Probabilitatea statistică: 41 %.
4. Atribuire: regula „HoReCa Cluj” → round-robin → **Cristina Marin** (12 lead-uri deschise din 40).
5. **27.11.2026 09:15**, Cristina sună (activitate „Apel”, rezultat „Interesat”, CR4), apoi califică: partener nou „Hotel Belvedere Cluj SRL” (rol Prospect), contactul Andreea Pop (Decident), oportunitatea **OP 000184** „Hotel Belvedere – aprovizionare anuală”, pipeline „HoReCa Cluj”, etapa „Calificat”, **86.400 lei pe an** (7.200 lei pe lună), data estimată de închidere 15.12.2026.

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `LeadCreat` v1 | Publicat | Lead, sursă, campanie, proprietar | CR4 (secvențe de întâmpinare), CR5 |
| `LeadCalificat` v1 | Publicat | Lead, partener, contact, oportunități | CR3, CR4 (oprirea secvențelor de lead), CR5 |
| `LeadDescalificat` v1 | Publicat | Motiv | CR4, CR5 |
| `ConsimtamantModificat` v1 | Publicat | Persoana, canal, scop, stare | CR4 (listele de campanie) |
| `PartenerCreat` v1 | Consumat (Nucleu) | — | Verificarea lead-urilor deschise cu același CUI (avertisment) |
| `FacturaEmisa` v1 | Consumat (Vânzări) | Partener | Rolul „Prospect” → „Client” (prin Nucleu); scorul nu se mai calculează pentru lead-urile acelui partener |

## 6. Contracte publice

```csharp
public interface ICrmLeadIntake        // folosit de endpoint-ul web, de importul Nucleu și de CR4 (campanii, e-mail)
{
    Task<LeadIntakeResult> CreateAsync(LeadIntakeRequest request, CancellationToken ct);
}
public interface IContactConsentQuery  // folosit de CR4 și de orice modul care trimite comunicări de marketing
{
    Task<bool> CanContactAsync(int partnerContactId, string channel, string purpose, CancellationToken ct);
}
```

## 7. Schema SQL (partea CR2)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `crm.Lead` | `Id bigint` | UX `(CompanyId, DocumentNumber)`; IX `(CompanyId, TaxIdNormalized) WHERE Status IN (1,2)`; IX `(EmailNormalized)`; IX `(PhoneE164)`; IX `(OwnerAgentId, Status)` |
| `crm.LeadDuplicateCandidate` | `Id bigint` | FK `LeadId`; `CHECK (Decision IN ('LINKED','MERGED','NOT_DUPLICATE','PENDING'))` |
| `crm.LeadScoreEvent` | `Id bigint` | FK `LeadId`, `ScoringRuleId` |
| `crm.LeadOpportunity` | `(LeadId, OpportunityId)` | Cel mult 5 pe lead (verificat în procedură, `63113`) |
| `crm.ContactProfile` | `PartnerContactId int` | FK `core.PartnerContact` |
| `crm.Consent` | `Id bigint` | IX `(PartnerContactId, Channel, Purpose, CapturedAt DESC)`; IX `(LeadId)`; `CHECK (PartnerContactId IS NOT NULL OR LeadId IS NOT NULL)` |
| `crm.ImportTemplate`, `crm.WebForm` | `Id int` | UX `(CompanyId, Code)` |

Câmpurile normalizate (`TaxIdNormalized`, `EmailNormalized`, `PhoneE164`) sunt coloane calculate persistente sau scrise de procedură; căutarea pe nume folosește coloana `NameSearch` cu colația `Latin1_General_100_CI_AI_SC_UTF8` (`SQL-24`).

**Erorile CR2** (`63100–63249`): `63101` `CRM_LEAD_DUPLICATE_BLOCKING` · `63102` `CRM_LEAD_NO_CHANNEL` · `63110` `CRM_QUALIFY_TAXID_REQUIRED` · `63111` `CRM_DISQUALIFY_REASON_REQUIRED` · `63112` `CRM_LEAD_QUALIFIED_READONLY` · `63113` `CRM_QUALIFY_MAX_OPPORTUNITIES` · `63114` `CRM_MERGE_QUALIFIED_LEAD` · `63120` `CRM_WEBFORM_REJECTED` · `63121` `CRM_IMPORT_ROW_INVALID` · `63122` `CRM_IMPORT_LEGAL_BASIS_REQUIRED` · `63130` `CRM_ANONYMIZE_FISCAL_RETAINED` (informativ).

## 8. Permisiuni

`Crm.Lead.View`, `Crm.Lead.Create`, `Crm.Lead.Edit` (pe proprietar sau echipă, CR1 §3.7), `Crm.Lead.Qualify`, `Crm.Lead.Disqualify`, `Crm.Lead.Merge`, `Crm.Lead.Import`, `Crm.Lead.Assign` (șef echipă), `Crm.Contact.Consent`, `Crm.Contact.Anonymize` (Manager vânzări).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă |
| --- | --- |
| GDPR art. 6, 7 (consimțământ demonstrabil), 17 (ștergere), 21 (opoziție la marketing) | `crm.Consent` cu istoric și dovadă; anonimizarea; „Nu mai contacta” |
| Legea 506/2004 art. 12 | Comunicările comerciale prin e-mail cer consimțământ prealabil, cu excepția clienților existenți pentru produse similare, cu posibilitate de opoziție |
| Legea 82/1991 | Datele de pe documentele contabile se păstrează; anonimizarea nu le atinge |
| 02.14 Integrări ANAF | `IAnafCompanyLookup` pentru îmbogățire (opțional) |
| Nucleu | `PartnerRole = Prospect` (precizare), cheie API de tip „Formular web” |

## 10. Rapoarte

Lista de lead-uri cu filtre (stare, sursă, proprietar, bandă de scor, vechime), exportabilă; „Lead-uri fără activitate”; „Duplicate în așteptare”. Rapoartele de conversie sunt în CR5.

## 11. Ecrane principale

Machetele sunt în [ecrane/cr2-leaduri/](ecrane/cr2-leaduri/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Lista lead-urilor | Lead-urile Cristinei Marin și ale cozii, cu scor, sursă, vechime, filtre | `cr2-01-lista-leaduri` |
| Formularul lead-ului (principal) | LD 000318 Hotel Belvedere: date firmă și persoană, scor cu explicație, duplicate, consimțăminte, activități | `cr2-02-formular-lead` |
| Calificarea | Cei trei pași: partener nou din ANAF, contact, oportunitatea OP 000184 | `cr2-03-calificare` |
| Persoana de contact și consimțăminte | Andreea Pop: profil, rol, istoricul consimțămintelor, anonimizare | `cr2-04-contact-consimtaminte` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| CR2-A1 | Formularul web din 3.1 creează LD 000318 cu consimțământul și dovada; cererea cu capcana completată e ignorată | Test de integrare pe endpoint |
| CR2-A2 | Al doilea formular cu același e-mail adaugă activitate pe lead-ul existent | Test de integrare |
| CR2-A3 | Scorul din exemplul 4 dă 60, „fierbinte”, cu 5 evenimente de scor | Test unitar |
| CR2-A4 | Calificarea creează partenerul, contactul și OP 000184 într-o singură tranzacție; la eroare nu rămâne nimic parțial | Test de integrare |
| CR2-A5 | Descalificarea fără motiv e refuzată (`63111`); reactivarea păstrează istoricul | Test de integrare |
| CR2-A6 | Anonimizarea șterge datele personale din CRM, păstrează facturile neatinse | Test de integrare |
| CR2-A7 | Importul cu 100 de rânduri, din care 3 invalide și 2 duplicate, importă 95 și produce raportul de erori | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Lead separat de partener? | Da, `crm.Lead`; partenerul se creează la calificare | ✓ Aprobat autonom (27.11.2026) |
| 2 | Câte oportunități dintr-un lead | Între 0 și 5 | ✓ Aprobat autonom |
| 3 | Deduplicarea | Blocantă pe CUI și e-mail exact, avertisment pe nume, telefon, domeniu | ✓ Aprobat autonom |
| 4 | Rolul partenerului nou | „Client potențial” (`Prospect`), devine „Client” la prima factură | ✓ Aprobat autonom |
| 5 | Formularul web | Endpoint REST cu cheie API, capcană anti-spam și limită de rată; fără serviciu extern | ✓ Aprobat autonom |
| 6 | Consimțămintele | Pe persoană, canal și scop, cu istoric și dovadă | ✓ Aprobat autonom |
| 7 | Dreptul de a fi uitat | Anonimizare în CRM; documentele fiscale rămân neatinse | ✓ Aprobat autonom |
| 8 | Profilul CRM al contactului | Tabel 1:1 `crm.ContactProfile`, fără câmpuri noi în Nucleu | ✓ Aprobat autonom |

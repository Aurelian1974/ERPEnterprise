# ERPEnterprise - instructiuni comune pentru agenti

## Surse si decizii

- Documentatia din [Docs/arhitectura](Docs/arhitectura/README.md) este sursa de adevar. Inspecteaza structura reala inainte de lucru; o structura descrisa in documentatie nu inseamna cod implementat.
- Respecta principiile P1-P10 si regulile obligatorii din [Bune practici](Docs/arhitectura/bune-practici/BUNE_PRACTICI.md). Pastreaza distinctia obligatoriu/recomandat si validat/propus/de facut.
- Deciziile explicite validate si specificatiile detaliate prevaleaza fata de schita initiala. Pentru contradictii ramase sau decizii nespecificate, cere clarificare inainte de implementarea afectata. Nu inventa aprobari; documenteaza exceptiile aprobate.
- Pentru legislatie, scheme si termene fiscale, verifica sursele oficiale actuale inainte de implementare; documentatia proiectului nu inlocuieste aceasta verificare.

## Invariante

- Monolit modular on-premise, Windows Server, o baza SQL Server per client/grup; izolare pe firma prin `CompanyId`, nu SaaS multi-tenant.
- .NET 10, MediatR, Dapper + proceduri stocate, DbUp, SQL Server 2022+, React + TypeScript ([Stack tehnologic](Docs/arhitectura/07-stack-tehnologic/STACK_TEHNOLOGIC.md)). Fara EF Core/ORM, Blazor, microservicii sau broker extern adaugat implicit.
- Modulele detin schemele SQL; acces intre module numai prin `.Contracts`/evenimente. Exceptie: raportare read-only prin view-uri dedicate `rpt`. Numai Finante genereaza note contabile.
- Document validat = imutabil; corectie prin storno. Respecta perioadele inchise, permisiunile si licenta pe server. Documentul, numerotarea fiscala, outbox-ul si auditul participa la aceeasi tranzactie; consumatorii sunt idempotenti.
- Bani/cantitati/cursuri: `decimal`, precizie prin `IPrecisionService`, nu `float`/`double`. Fiscalul este configurabil si versionat, nu hardcodat.
- Collation fix `Latin1_General_100_CS_AS_SC_UTF8`; identificatori si coduri tehnice cu majuscule exacte.
- ASP.NET Core Identity cu cookie in `Erp.Host`, store-uri Dapper, chei API pentru conturi tehnice/POS; fara Duende, OpenIddict sau server de identitate separat.

## Consum minim de tokeni

- Optimizeaza explicit pentru consumul minim de tokeni compatibil cu o solutie corecta si verificata. Nu elimina verificari, protectii sau cerinte pentru a economisi context.
- Incarca progresiv: cerinta -> fisiere relevante -> sectiuni necesare. Cauta intai titluri, simboluri sau ID-uri de reguli; citeste intervale delimitate, nu intregul depozit.
- Nu incarca preventiv toate documentele, instructiunile, skill-urile, imaginile sau machetele. Exceptie: specificatia installer-ului cere citire integrala inainte de codul installer-ului.
- Refoloseste contextul deja citit; reciteste numai dupa modificari sau pentru o verificare concreta. Grupeaza citirile independente; limiteaza rezultatele cautarilor si ale comenzilor.
- Lucreaza direct implicit. Subagenti doar pentru munca substantiala independenta, cu obiectiv limitat; fara delegari/revizuiri repetitive si fara modele impuse in configurari.
- Foloseste skill-ul potrivit numai la sarcina lui. Raspunde concis: schimbari, verificari efective, blocaje; fara reproducerea documentelor sau jurnalelor complete.

## Lucru si verificare

- Aplica numai instructiunile relevante din [ghidul de configurare](.github/README.md); daca mediul nu le incarca automat, citeste manual fisierul corespunzator. Nu urma recursiv toate linkurile.
- Modificari mici, reutilizare inainte de abstractizare, fara modificari colaterale. Pastreaza lucrul existent al utilizatorului; nu crea commit/push fara cerere.
- Nu expune secrete sau date personale in cod, jurnale ori rezultate. Nu inghiti erori si nu raporta succes neverificat.
- Ruleaza cea mai mica verificare existenta care acopera schimbarea; extinde numai justificat. Nu inventa comenzi, proiecte sau teste. Raporteaza explicit verificarile imposibile; nu instala infrastructura doar pentru a simula succesul.
- Actualizeaza documentatia direct afectata. Comunica in romana; UI in romana cu diacritice si resurse de traducere, identificatori conform documentatiei.

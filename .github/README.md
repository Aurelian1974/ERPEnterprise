# Configurarea agentilor ERPEnterprise

Configurari locale bazate pe documentatia din [Docs](../Docs/arhitectura/README.md). Regulile de proiect nu modifica permisiunile instrumentelor si nu inlocuiesc testele sau aprobarea operatiunilor administrative.

## Incarcare si utilizare

- [AGENTS.md](../AGENTS.md) este unica sursa a regulilor globale. [Copilot](copilot-instructions.md) si [Claude](../CLAUDE.md) trimit la el, fara copii ale regulilor.
- In VS Code/Copilot, instructiunile `applyTo` se aplica fisierelor potrivite, iar skill-urile sunt incarcate la cereri relevante sau prin `/nume-skill`. Descrierile raman scurte.
- In medii care nu descopera aceste directoare, agentul citeste manual numai instructiunea/skill-ul relevant; nu presupune ca o trimitere Markdown garanteaza incarcarea automata.
- [Setarile VS Code](../.vscode/settings.json) activeaza explicit `AGENTS.md`, fisierele de compatibilitate, instructiunile `applyTo` si skill-urile. Linkurile Markdown nu sunt incluse automat (`chat.includeReferencedInstructions: false`), iar output-ul mare din terminal este comprimat. Setarile se aplica numai intr-un workspace de incredere; agentii din `.github/agents` sunt descoperiti implicit.
- Deschide radacina depozitului. In VS Code: **Chat: Open Customizations** pentru verificarea listelor de Instructions, Agents si Skills. Verifica aplicarea intr-o conversatie noua; disponibilitatea nu garanteaza respectarea regulilor.

## Instructiuni pe domenii

| Fisier | Cand se foloseste |
| --- | --- |
| [backend](instructions/backend.instructions.md) | C#, proiecte .NET, contracte, pipeline, autentificare |
| [sql](instructions/sql.instructions.md) | SQL, proceduri, DbUp; citire manuala si la SQL construit din C# |
| [frontend](instructions/frontend.instructions.md) | TypeScript, React, CSS, Razor, machete HTML |
| [testing](instructions/testing.instructions.md) | Teste si selectarea verificarilor unei schimbari |
| [installer](instructions/installer.instructions.md) | Setup, build/kit, specificatia installer-ului |
| [documentation](instructions/documentation.instructions.md) | Docs si mentinerea personalizarilor |

Globurile reflecta extensiile si structura planificata, nu afirma ca proiectele sunt implementate. Cand structura reala se schimba, ajusteaza globurile si verifica un fisier pozitiv si unul negativ pentru fiecare.

## Agenti optionali

| Agent | Rol |
| --- | --- |
| [ERP Architect](agents/erp-architect.agent.md) | Analiza si plan, numai citire/cautare; fara modificari sau teste executate |
| [ERP Implementer](agents/erp-implementer.agent.md) | Implementare limitata la cerinta, teste si raportarea rezultatului |

Selecteaza un singur rol potrivit. Nu exista handoff sau echipa automata de agenti; niciun model nu este fixat. Agentul standard poate folosi aceleasi instructiuni si skill-uri.

## Skill-uri la nevoie

| Comanda | Scop | Exemplu |
| --- | --- | --- |
| [/erp-module-spec](skills/erp-module-spec/SKILL.md) | Specificatie noua/completata conform celor 12 sectiuni | „Completeaza contractele si criteriile modulului Vanzari; marcheaza deciziile lipsa.” |
| [/erp-vertical-slice](skills/erp-vertical-slice/SKILL.md) | Un caz de utilizare de business, cu teste | „Implementeaza cazul aprobat de modificare a partenerului.” |
| [/erp-installer](skills/erp-installer/SKILL.md) | O sarcina T1-T16, cu verificarile IA aferente | „Implementeaza T1; nu instala nimic pe acest calculator.” |

Pentru o editare triviala nu este necesar un skill. Imbunatateste skill-ul existent pentru un caz repetat in loc de a crea unul care dubleaza acelasi flux.

## Harta de lectura a surselor

| Necesitate | Document si sectiuni de pornire |
| --- | --- |
| Perimetru, P1-P10, cerinte nefunctionale | [Scop si principii](../Docs/arhitectura/01-scop-si-principii/SCOP_SI_PRINCIPII.md) |
| Module, scheme, dependente, sablon cu 12 sectiuni | [Module principale](../Docs/arhitectura/02-module-principale/MODULE_PRINCIPALE.md) |
| Conventii de cod | [Bune practici](../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md): 2-3 SQL, 4 backend, 5 UI, 6 teste, 7 CI/securitate, 8 checklist/plaje erori |
| Servicii comune | [Nucleu](../Docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md): 3 entitati, 4 fluxuri, 5 evenimente, 6 contracte, 7 SQL, 8 permisiuni, 11 ecrane, 12 criterii A1-A22 |
| Ecran concret | Numai HTML-ul relevant din [machete](../Docs/arhitectura/02-module-principale/01-nucleu/ecrane); PNG/browser daca trebuie verificat vizual |
| Installer/identitate | [Instalare si actualizare](../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md): 0 reguli agent, 5 installer (5.2 straturi, 5.10 administrator, 5.12 chei), 6 CLI, 8 autentificare, 10 upgrade, 12 IA, 13 sarcini si cod existent, 15 abateri |

Schita este context istoric, nu inlocuitorul specificatiilor detaliate. Paginile marcate „de facut” nu autorizeaza inventarea regulilor. Un statut general „validat” nu transforma automat fiecare propunere interna intr-o decizie aprobata.

## Buget de context si verificare

Obiectiv: **consum cat mai mic de tokeni fara pierderea corectitudinii**. Limite de mentenanta, nu estimari de tokeni:

- Nucleu global + cele doua fisiere de compatibilitate: maximum 650 de cuvinte.
- Fiecare instructiune pe domeniu: maximum 350 de cuvinte.
- Fiecare agent: maximum 200 de cuvinte; fiecare skill: maximum 500.
- Ghidul de fata si documentele-sursa nu se incarca integral la fiecare cerere. Exceptia de citire integrala pentru implementarea installer-ului este obligatorie.

La modificari verifica frontmatter-ul, numele skill-ului egal cu folderul, legaturile locale, globurile si limitele de mai sus. Numararea cuvintelor separate prin spatii este doar o masura reproductibila a dimensiunii; numarul real de tokeni depinde de model si de contextul incarcat.

Teste manuale in conversatii noi:

1. Cere analiza unui modul nedetaliat cu ERP Architect: asteptat plan/decizii lipsa, fara editari.
2. Cere un flux de business: asteptat numai documentul/sectiunile si instructiunile relevante, nu toate Docs sau toate skill-urile.
3. Cere implementarea installer-ului: asteptata citirea integrala a specificatiei, dar nicio operatie administrativa neautorizata.
4. Verifica References/Agent Debug Logs si fisierele modificate; separa descoperirea configurarii de executarea corecta a sarcinii.

Formate verificate in documentatia oficiala VS Code: [instructiuni](https://code.visualstudio.com/docs/agent-customization/custom-instructions), [agenti](https://code.visualstudio.com/docs/agent-customization/custom-agents), [skill-uri](https://code.visualstudio.com/docs/agent-customization/agent-skills).

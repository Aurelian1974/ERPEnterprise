---
name: erp-vertical-slice
description: 'Implementeaza sau modifica un flux ERP de business prin API, MediatR, SQL si UI, cu teste; foloseste pentru un caz de utilizare transversal, nu pentru installer sau o editare triviala.'
---
# Implementarea unui caz de utilizare

## Intrare si surse

Cerinta trebuie sa identifice modulul, comportamentul si criteriile de acceptare. Aplica [AGENTS.md](../../../AGENTS.md); citeste specificatia modulului si numai instructiunile atinse: [backend](../../instructions/backend.instructions.md), [SQL](../../instructions/sql.instructions.md), [frontend](../../instructions/frontend.instructions.md), [teste](../../instructions/testing.instructions.md). Nu reincarca fisiere deja citite.

## Pasi

1. Inspecteaza codul si testele existente, manifestele si CI. Identifica implementarea similara si interfetele reutilizabile inainte de a crea fisiere. Un modul „de facut” nu este o specificatie suficienta: clarifica regulile lipsa.
2. Stabileste traseul minim: contract/comanda/query -> validator -> handler -> store/query -> procedura/migrare -> endpoint -> UI, numai straturile necesare cerintei.
3. Pentru comenzi foloseste pipeline-ul [Nucleu 4.1](../../../Docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md). Verifica licenta, scope-ul firmei/permisiunile, perioada, starea si `RowVersion`; nu permite ocolirea prin alte puncte de intrare.
4. Foloseste `IDbSession`, `IPrecisionService`, serviciul de numerotare si outbox-ul existente. Salveaza atomic inclusiv auditul; probeaza rollback-ul si idempotenta pentru efectele introduse. Consumatorul nu produce efecte duplicate la reexecutare.
5. Pentru SQL separa migrarea noua de obiectele `CREATE OR ALTER`, foloseste plaja de erori a modulului si verifica regulile CS, `CodeKey`, `NameSearch` si `#temp` cand sunt aplicabile.
6. Pastreaza contractul API/ProblemDetails; regenereaza clientul OpenAPI cu generatorul proiectului daca se schimba contractul. UI respecta precizia, permisiunile, starea documentului si macheta relevanta, fara reguli financiare independente.
7. Adauga teste pentru criteriile afectate: succes/refuz, firme neautorizate, coliziune `RowVersion`, retrimitere, rollback/concurenta dupa caz. Nu crea teste nerelevante doar pentru acoperire.
8. Ruleaza build/type-check/lint/testele tintite deja disponibile, apoi verifica diff-ul si documentatia dependenta. Pentru SQL foloseste server real cu collation-ul clientului. Raporteaza comenzile si rezultatele reale.

## Iesire

Schimbarea persistenta, testele si un rezumat scurt al verificarilor/blocajelor. Nu crea un modul intreg pentru o singura actiune si nu rula suite globale fara motiv.

Exemplu: „Implementeaza validarea documentului conform criteriului aprobat” -> vertical slice si teste inclusiv refuz/rollback, nu CRUD generic care ignora starea fiscala.

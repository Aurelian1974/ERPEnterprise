---
name: erp-module-spec
description: 'Creeaza sau completeaza o specificatie de modul ERP in Docs; foloseste la cerinte de arhitectura, contracte si criterii de acceptare, nu la orice editare de cod.'
---
# Specificarea unui modul ERP

## Context minim

Aplica [AGENTS.md](../../../AGENTS.md) si [regulile de documentatie](../../instructions/documentation.instructions.md), fara recitire daca sunt deja incarcate.

Identifica modulul si scopul cerut. Citeste fisa lui, dependentele si sablonul din [Module principale](../../../Docs/arhitectura/02-module-principale/MODULE_PRINCIPALE.md), apoi documentul existent al modulului. Pentru servicii comune, citeste numai contractele relevante din [Nucleu](../../../Docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md), sectiunea 6.

## Pasi

1. Clasifica cerinta in produs/mai tarziu/in afara produsului conform [Scop si principii](../../../Docs/arhitectura/01-scop-si-principii/SCOP_SI_PRINCIPII.md). Noteaza dependentele obligatorii si deciziile lipsa.
2. Pastreaza cele 12 sectiuni standard: scop, domeniu, documente/stari, fluxuri, evenimente, contracte, SQL, permisiuni, legal/integrari, rapoarte, ecrane, acceptare/teste. La actualizare modifica numai sectiunile atinse.
3. Specifica proprietarul fiecarei date, scope-ul firmei, invarianti, tranzactii/outbox/idempotenta, precizie, erori stabile si permisiuni. Nu crea copii locale ale serviciilor Nucleului.
4. Refera regulile din [Bune practici](../../../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md) prin ID. Pentru SQL verifica inclusiv CS/CI, iar pentru fiscalitate cere surse oficiale actuale; marcheaza ceea ce nu este verificat.
5. Defineste criterii masurabile si teste pe drum bun, refuz, concurenta/rollback unde conteaza. Nu inventa praguri obligatorii pornind de la propuneri.
6. Foloseste checklist-ul 8.2 din Bune practici. Prezinta contradictiile cu surse, nu le rezolva prin presupuneri. Pastreaza documentul propus/nevalidat pana la aprobarea explicita.
7. Daca se cere editare, salveaza documentul in folderul modulului si actualizeaza indexul direct afectat; daca se cere numai analiza sau agentul este read-only, returneaza propunerea fara fisiere.

## Rezultat si limita

Livreaza documentul/sectiunile cerute, criteriile si lista scurta a deciziilor de confirmat. Nu incepe implementarea si nu genera toate celelalte module.

Exemplu: „Completeaza specificatia modulului Vanzari pentru validarea facturii” -> sectiunile afectate, contractele si criteriile propuse; nu o implementare dedusa dintr-un README gol.

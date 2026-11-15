---
description: 'Reguli pentru specificatii, decizii si mentinerea instructiunilor ERP.'
applyTo: 'Docs/**/*.md,AGENTS.md,CLAUDE.md,.github/**/*.md'
---
# Documentatie si personalizari

Surse: [index](../../Docs/arhitectura/README.md), [Module principale](../../Docs/arhitectura/02-module-principale/MODULE_PRINCIPALE.md), sectiunea Documente detaliate; [Bune practici](../../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md), sectiunile 1 si 8.2.

- Nu transforma o schita, ipoteza, propunere sau pagina „de facut” in decizie validata. Validarea apartine responsabilului proiectului; nu adauga automat date sau aprobari.
- Specificatiile de modul urmeaza cele 12 sectiuni comune. Citeaza ID-uri P/SQL/SP/MIG/NET/UI/TST/SEC/GIT si criterii A/IA in locul copierii regulilor.
- La contradictii, arata sursele si impactul; cere decizia necesara inainte de cod. Nu corecta tacut politici fiscale/comerciale sau exemple contradictorii.
- Modifica numai documentele/legaturile direct afectate, inclusiv indexul cand adaugi o specificatie. Pastreaza cai cu majusculele reale (`Docs`, nu `docs`).
- Machetele HTML/PNG completeaza specificatia; valorile demonstrative nu sunt configuratii obligatorii.
- Regulile globale se mentin numai in [AGENTS.md](../../AGENTS.md). Fisierele de compatibilitate doar trimit la acesta; instructiunile pe domenii nu repeta intregul nucleu.
- Instructiuni = reguli declarative; agenti = roluri; skill-uri = proceduri reutilizabile cu declansator precis. Imbunatateste un fisier existent inainte de a crea unul suprapus.
- Incarcare progresiva: `applyTo` tintit, descrieri scurte, skill-uri la nevoie; fara `applyTo: '**/*'`, copierea documentelor sau delegare automata.
- La schimbari de personalizari verifica frontmatter, cai relative, concordanta cu sursele si dimensiunea conform [ghidului](../README.md). Nu declara economii exacte de tokeni fara masurare cu tokenizer-ul modelului.

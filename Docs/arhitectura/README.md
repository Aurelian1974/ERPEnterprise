# Arhitectura ERP · index

Fiecare secțiune a arhitecturii are folderul ei, cu documentul Markdown și diagramele. Versiunea editabilă a fiecărui document e în Claude Docs (linkul din primul rând al fiecărui fișier).

Pentru lucru asistat de agenți: [instrucțiuni comune](../../AGENTS.md) și [ghidul agenților, instrucțiunilor și skill-urilor](../../.github/README.md). Configurarea folosește încărcare selectivă pentru consum minim de tokeni, fără a elimina verificările cerute de specificații.

```
arhitectura/
├── README.md                          ← acest index
├── bune-practici/                     ✓ validat · reguli SQL și cod, inclusiv SQL-21–25 bază case sensitive
│   └── BUNE_PRACTICI.md
├── 00-schita/                         schița de arhitectură (punctul de plecare)
│   ├── ARHITECTURA_ERP.md
│   ├── harta-contextelor.png
│   └── roadmap.png
├── 01-scop-si-principii/              ✓ validat
│   ├── SCOP_SI_PRINCIPII.md
│   └── context-sistem.png
├── 02-module-principale/              ✓ validat (documentul principal)
│   ├── MODULE_PRINCIPALE.md           documentul principal
│   ├── dependente-module.png
│   ├── 01-nucleu/                     ✓ 02.01 validat
│   │   ├── NUCLEU.md
│   │   ├── model-domeniu.png
│   │   ├── pipeline-comenzi.png
│   │   └── ecrane/                   ✓ machete 01–10 validate (14.11.2026)
│   ├── 02-finante-contabilitate/      02.02
│   ├── 03-vanzari/                    02.03
│   ├── 04-achizitii/                  02.04
│   ├── 05-stocuri/                    02.05
│   ├── 06-wms/                        02.06
│   ├── 07-transport-tms/              02.07
│   ├── 08-productie-mrp/              02.08
│   ├── 09-hr-salarizare/              02.09
│   ├── 10-retail-pos/                 02.10
│   ├── 11-constructii/                02.11
│   ├── 12-crm/                        02.12
│   ├── 13-raportare-bi/               02.13
│   └── 14-integrari-anaf-bnr/         02.14
├── 03-bounded-contexts-integrare/
├── 04-stil-arhitectural/
├── 05-date-multi-tenancy/
├── 06-securitate-permisiuni/
├── 07-stack-tehnologic/               ✓ validat (ST1–ST4) · stack-ul consolidat
│   └── STACK_TEHNOLOGIC.md
├── 08-instalare-actualizare/          ✓ validat · specificația installer-ului
│   ├── INSTALARE_ACTUALIZARE.md
│   ├── GHID_INSTALARE_ACTUALIZARE.md  ghid operațional (propus, de revizuit)
│   └── flux-actualizare.png
└── 09-roadmap/
```

| # | Secțiune | Stare | Document | Editabil |
| --- | --- | --- | --- | --- |
| — | Bune practici (SQL și cod) | Validat (14.11.2026), inclusiv regulile pentru baza case sensitive | [BUNE_PRACTICI.md](bune-practici/BUNE_PRACTICI.md) | [Claude Docs](https://claude.ai/artifact/JP7yeX8cA7AP8PRRMJGiTA) |
| 00 | Schița de arhitectură | Aprobată | [ARHITECTURA_ERP.md](00-schita/ARHITECTURA_ERP.md) | [Claude Docs](https://claude.ai/artifact/NNYXW6G9eaLaxHFHkCZAJQ) |
| 01 | Scop și principii | Validat (14.11.2026) | [SCOP_SI_PRINCIPII.md](01-scop-si-principii/SCOP_SI_PRINCIPII.md) | [Claude Docs](https://claude.ai/artifact/TR4QjvsTBdvETfGgozK4we) |
| 02 | Module principale | Validat (14.11.2026) | [MODULE_PRINCIPALE.md](02-module-principale/MODULE_PRINCIPALE.md) | [Claude Docs](https://claude.ai/artifact/6wZzKpXDTAbPBPmTwUZRSY) |
| 02.01 | Nucleu (Platform) | Validat, inclusiv machetele de ecran (14.11.2026); inclusiv baza case sensitive, administratorul de la instalare și autentificarea cu cookie | [NUCLEU.md](02-module-principale/01-nucleu/NUCLEU.md) | [Claude Docs](https://claude.ai/artifact/UE59RnaWRpVe5dfdJgzrqU) |
| 03 | Bounded contexts și integrare | De făcut | — | — |
| 04 | Stil arhitectural | De făcut | — | — |
| 05 | Date și multi-tenancy | De făcut | — | — |
| 06 | Securitate și permisiuni | De făcut | — | — |
| 07 | Stack tehnologic | Validat (15.11.2026) pentru ST1–ST4 (numai React, fără Blazor; `src/Web/erp-web`; Node.js ultima stabilă; componente Syncfusion pentru React); restul consolidat din sursele indicate, cu puncte de decis | [STACK_TEHNOLOGIC.md](07-stack-tehnologic/STACK_TEHNOLOGIC.md) | — |
| 08 | Instalare și actualizare (installer) | Validat (14.11.2026) | [INSTALARE_ACTUALIZARE.md](08-instalare-actualizare/INSTALARE_ACTUALIZARE.md) | — |
| 08 | Ghid operațional: construirea și livrarea kitului, instalarea, actualizarea | Propus (15.11.2026), de revizuit de Aurelian; canalul de livrare nedecis | [GHID_INSTALARE_ACTUALIZARE.md](08-instalare-actualizare/GHID_INSTALARE_ACTUALIZARE.md) | — |
| 09 | Roadmap pe faze | De făcut | — | — |

---
description: 'Criterii de acceptare, teste de business, SQL, arhitectura si E2E ERP.'
applyTo: '**/*Tests*/**/*,**/tests/**/*,**/*.Tests.csproj,**/*.test.ts,**/*.test.tsx,**/*.spec.ts,**/*.spec.tsx'
---
# Teste

Surse: [Bune practici](../../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md), sectiunea 6 (`TST-01`-`TST-09`); criteriile din documentul modulului.

- Leaga testele de criteriul atins, de exemplu `[Trait("Criteriu", "02.01-A2")]` sau `08-IA-04`. Adauga teste pentru comportamentul schimbat, nu pentru constante/framework.
- xUnit pentru reguli pure; SQL Server real cu Testcontainers pentru proceduri: drum bun si fiecare `THROW`. Baza CS `Latin1_General_100_CS_AS_SC_UTF8` pe server CI `SQL_Latin1_General_CP1_CI_AS`, aceleasi migratii ca la client.
- NetArchTest pentru granitele `.Contracts`, absenta SQL din Application si permisiuni declarate. Migrare pe baza goala si pe versiunea anterioara.
- Verifica pragul si scenariul exact, nu un substitut: [Nucleu, sectiunea 12](../../Docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md) A2 (concurenta, 1.000 documente), A3 (rollback fara gol), A8 (efect unic dupa oprire), A10 (precizie), A16 (retrimiteri), dupa caz.
- Pentru fiscalitate: fisiere de referinta si scheme/validatoare oficiale actuale. Playwright pentru fluxuri critice; E2E installer pe VM Windows dedicata, nu pe masina comuna.
- Ruleaza selectia relevanta cu runner-ul deja prezent; afla comenzile din proiect/CI, nu presupune ca exista. Lipsa SQL/Docker/Windows runner este un blocaj raportat, nu test trecut.
- Selectia locala economiseste tokeni/timp, dar nu inlocuieste portile CI/livrare din `GIT-04` si specificatie. Tinta propusa de acoperire Domain/Application ramane distincta de verificarea acestei schimbari.

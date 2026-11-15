---
description: 'Reguli pentru UI React/TypeScript, pagini Razor si machetele HTML ERP.'
applyTo: '**/*.ts,**/*.tsx,**/*.css,**/*.html,**/*.cshtml,**/*.razor'
---
# Frontend

Surse: [Bune practici](../../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md), sectiunea 5 (`UI-01`-`UI-10`); [Nucleu](../../Docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md), sectiunea 11; [Stack tehnologic](../../Docs/arhitectura/07-stack-tehnologic/STACK_TEHNOLOGIC.md).

- Interfata numai React + TypeScript in `src/Web/erp-web` (Vite, Node.js din `.nvmrc`); fara Blazor. Singura exceptie: paginile Razor `/account/*`.
- Componentele UI din Syncfusion pentru React (`@syncfusion/ej2-react-*`), folosite prin componente comune proprii. Cheia de licenta numai din `SYNCFUSION_LICENSE` (`npx syncfusion-license activate`); niciodata in cod, configurare sau Git.
- Pentru aplicatia React: TypeScript strict, fara `any`; TanStack Query, React Hook Form + Zod; client API si tipuri generate din OpenAPI.
- Organizare pe module/functionalitati, componente comune reutilizate; module nelicentiate neincarcate si absente din meniu. Nu duplica datele serverului in stare globala.
- Sume/preturi/cantitati in JSON ca string. Fara calcule monetare cu `number`; previzualizare de pe server sau biblioteca zecimala cu aceleasi reguli; valoarea salvata este calculata pe server.
- UI in romana cu diacritice, texte in resurse, format numeric romanesc, date `zz.ll.aaaa`, Europe/Bucharest. Erori actionable si erori 400 legate de campuri.
- Flux listare/filtrare -> fisa cu tab-uri -> istoric. Documente validate doar citire; actiuni dupa stare si permisiuni. Serverul ramane autoritatea pentru drepturi/licenta.
- Orice salvare, peste tot, are feedback: notificare de succes (`useNotifier`) sau de eroare plus erorile pe camp (`UI-11`).
- Flicker-ul la reincarcare e interzis (`UI-12`): fara golirea ecranului sau „Se incarca…” la refetch; proprietati stabile pentru componentele Syncfusion (date memorate, sabloane/handler-e create o data); selectia si starea arborilor/listelor raman dupa salvare.
- Pastreaza navigarea din tastatura, focusul, etichetele si contrastul. Liste mari paginate pe server/virtualizate, nu descarcate integral.
- Cookie same-origin si antiforgery pentru scrieri conform [autentificarii, 8.7](../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md). Login-ul ramane Razor `/account/*`, nu este inlocuit cu un flux React/JWT.
- La UI, citeste numai macheta HTML a ecranului vizat; deschide PNG-ul/browserul doar pentru verificari vizuale necesare. Machetele sunt referinte, nu cod de productie: nu copia scripturi inline, date demonstrative sau valori fixe ca reguli de business.
- Pentru modificari exclusiv de macheta pastreaza formatul existent; nu introduce React, generator OpenAPI sau infrastructura de build in documentatie.

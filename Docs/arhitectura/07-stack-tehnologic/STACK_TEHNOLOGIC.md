# ERP · 07 Stack tehnologic

15 noiembrie 2026 · Aurelian · **Validat (15.11.2026)** pentru deciziile ST1–ST4; restul tabelelor consolidează sursele indicate, cu starea lor · Documente legate: [01 Scop și principii](../01-scop-si-principii/SCOP_SI_PRINCIPII.md), [schița](../00-schita/ARHITECTURA_ERP.md), [Bune practici](../bune-practici/BUNE_PRACTICI.md), [08 Instalare și actualizare](../08-instalare-actualizare/INSTALARE_ACTUALIZARE.md)

Acest document strânge într-un singur loc tehnologiile folosite de ERP. Nu introduce alegeri noi în afara deciziilor ST1–ST4: fiecare rând trimite la documentul care l-a decis. La o contradicție, prevalează documentele validate (01, 08, Bune practici) față de schiță; o alegere doar „propusă” se validează înainte de folosire.

---

## 1. Decizii (Aurelian, 15.11.2026)

| # | Decizie | Consecință |
| --- | --- | --- |
| ST1 | **Interfața aplicației se face numai în React + TypeScript. Fără Blazor** (Server, WebAssembly, Hybrid sau United) | Ecranele ERP sunt componente React; serverul expune doar API. Excepție deja validată, compatibilă cu ST1: paginile de autentificare `/account/*` rămân **Razor Pages** (HTML generat pe server, fără Blazor), conform deciziei D10 din 08 și 02.01 secțiunea 11 |
| ST2 | **Aplicația React stă în `src/Web/erp-web`** | Construcția ei produce fișiere statice copiate în `wwwroot` al `Erp.Host` la construirea kitului (08, secțiunea 1) |
| ST3 | **Node.js: ultima versiune stabilă** (nu alpha, beta sau RC) | La 15.11.2026: **Node.js 26** (v26.13.1, linia LTS „Lithium”, publicată 10.11.2026, sursa: [nodejs.org/dist/index.json](https://nodejs.org/dist/index.json)). Versiunea se fixează în depozit (`.nvmrc` și `engines` în `package.json`) și se actualizează la verificarea lunară a dependențelor (`SEC-04`) |
| ST4 | **Componentele de interfață se iau din Syncfusion Essential JS 2 pentru React** (pachetele `@syncfusion/ej2-react-*`; licență comercială deținută de Aurelian) | Numai pachetele React, niciodată cele Blazor (ST1). Componentele Syncfusion se folosesc prin componente comune proprii (de exemplu o listă standard peste DataGrid), ca ecranele să păstreze același tipar și regulile `UI-*`. Cheia de licență nu stă în Git (secțiunea 4) |

**De reverificat la ST3:** în aprilie 2027 va apărea Node.js 27, versiune „Current”, fără suport pe termen lung (doar liniile pare devin LTS). Atunci „ultima stabilă” și „ultima LTS” nu vor mai coincide; Aurelian confirmă care se urmează.

---

## 2. Stack pe straturi

| Strat | Alegere | Sursă | Stare |
| --- | --- | --- | --- |
| Server | Windows Server 2019, 2022 sau 2025, IIS 10 | 08, secțiunea 1 | Validat |
| Runtime | .NET 10 (LTS), ASP.NET Core Hosting Bundle 10.x; SDK fixat în `global.json` (10.0.401) | 08, secțiunea 1; `global.json` | Validat |
| Stil | Monolit modular, un host `Erp.Host`; module în același proces, comunicare prin `.Contracts` și evenimente | 01 (P1); `AGENTS.md` | Validat |
| Aplicație | MediatR (pipeline validare → autorizare → tranzacție → audit); FluentValidation pentru validarea de formă | 01 (stack fix); `NET-11` | Validat |
| Versiunea MediatR | **12.5.0**, ultima versiune cu licență Apache 2.0. Versiunile 13 și următoare au licență comercială Lucky Penny Software (gratuită doar sub 5 mil. USD venit anual, cu înregistrare) | Verificat pe nuget.org și mediatr.io, 15.11.2026 | **De confirmat** de Aurelian: rămânem pe 12.5.0 sau cumpărăm licența |
| Mapare | Mapster, doar unde maparea e mecanică | `NET-20` | Recomandat |
| Acces date | Dapper + proceduri stocate, **fără ORM** | 01 (stack fix) | Validat |
| Migrații | DbUp, scripturi SQL versionate, rulate de installer | 08, secțiunea 8.5 | Validat |
| Bază de date | SQL Server 2022 sau mai nou, Standard sau Enterprise; o bază per client; o schemă per modul; collation `Latin1_General_100_CS_AS_SC_UTF8` | 08, secțiunile 1 și 9 | Validat (schița spunea doar 2022) |
| Mesagerie internă | Outbox în SQL + procesator în fundal; fără broker extern | 01 (P8); schița | Validat |
| Joburi | Hangfire 1.8, stocare în SQL Server (schema `hangfire`), în serviciul `Erp.Worker` | 08, secțiunile 1 și 8.5 | Validat |
| Identitate | ASP.NET Core Identity cu cookie, store-uri Dapper în schema `ids`, pagini Razor `/account/*`, 2FA, chei API pentru conturi tehnice și POS, autentificare Windows opțională | 08, secțiunea 8.7 (D10) | Validat |
| Frontend | React + TypeScript (strict), Vite, TanStack Query, React Hook Form + Zod | 01 (stack fix); schița; `UI-03` | Validat (React + TS); aprobat în schiță (restul) |
| Client API | Generat din OpenAPI, nu scris de mână | `UI-03` | Validat |
| Calcule zecimale în UI | Totaluri de la server sau `decimal.js` cu aceleași reguli; valorile circulă ca string | `UI-05` | Validat |
| Componente UI | Syncfusion Essential JS 2 pentru React (grile, formulare, calendare, grafice etc.) | ST4 | Validat |
| Tabele mari | Syncfusion DataGrid (paginare și filtrare pe server, `UI-*`) | ST4; schița propunea AG Grid sau TanStack Table | Propus (decurge din ST4), de confirmat |
| Livrarea UI | Fișiere statice în `wwwroot` al `Erp.Host`, același origin cu API-ul (cookie `SameSite=Strict`) | 08, secțiunile 1 și 8.7 | Validat |
| Rapoarte și documente | QuestPDF pentru facturi și state; ClosedXML pentru Excel; BI: Power BI Report Server sau Metabase, on-premise | Schița | Aprobat în schiță |
| Jurnale | Serilog, jurnale structurate fără date personale | `NET-18` | Validat |
| Observabilitate | OpenTelemetry, Seq local | Schița | Aprobat în schiță |
| Integrări România | Adaptoare ANAF (e-Factura, e-Transport, SPV), BNR, REVISAL, în modulul `Integrations` | Schița | Aprobat în schiță |
| POS | Client local cu SQLite și driver pentru casa de marcat; interfața: React în Electron sau WPF | Schița; 08, secțiunea 13 | **De decis** în 02.10 (vezi secțiunea 5) |
| Installer | Aplicație consolă .NET self-contained (`ErpSetup.exe`), fără MSI | 08, decizia D2 | Validat |
| Teste .NET | xUnit v3 pe Microsoft.Testing.Platform; Testcontainers cu SQL Server real; NetArchTest (fork-ul întreținut `NetArchTest.eNhancedEdition`, același API; originalul nu mai e actualizat din 2021) | `TST-02`–`TST-04`; `global.json` | Validat |
| Teste end-to-end | Playwright, doar pentru fluxurile critice ale fiecărui modul | `TST-08` | Recomandat |
| Teste de componente UI | — | — | **De decis** |
| Construire și CI | GitHub Actions; kit construit cu `build/package-kit.ps1`; teste Pester pentru scripturile de build | 08, secțiunile 4 și 5 | Validat |

---

## 3. Ce nu se folosește

| Exclus | Motiv | Sursă |
| --- | --- | --- |
| Blazor (orice variantă) | Interfața numai în React | ST1 |
| Entity Framework Core sau alt ORM | Acces date prin Dapper + proceduri stocate | 01; 08, secțiunea 5 |
| Duende IdentityServer, OpenIddict, server de identitate separat | Costuri de licență, complexitate inutilă | 08, D10 |
| Broker de mesaje extern, microservicii | O dependență în plus la client; echipă mică | 01 (P9); `AGENTS.md` |
| `float`, `double`, `money` pentru bani, cantități, cursuri | Precizie | `SQL-07`, `NET-14`, `UI-05` |
| Docker / Linux în producție | „Mai târziu”, nu în prima versiune | 01, perimetrul |

---

## 4. Versiuni și actualizări

- **.NET:** SDK fixat în `global.json`; pachetele NuGet cu versiuni centrale în `Directory.Packages.props`.
- **Node.js și npm:** versiunea din ST3, fixată în `.nvmrc` și `engines`; dependențele npm cu `package-lock.json` în depozit.
- **Actualizare:** dependențele NuGet și npm se verifică lunar; o vulnerabilitate critică cunoscută blochează livrarea (`SEC-04`).
- **Licența Syncfusion (ST4):** cheia **nu se scrie niciodată** în depozit, în cod (`registerLicense('...')`) sau în fișiere de configurare urmărite de Git; `syncfusion-license.txt` e în `.gitignore`. Se activează la build cu `npx syncfusion-license activate` (scriptul `postinstall` al aplicației), care citește variabila de mediu `SYNCFUSION_LICENSE`:
  - pe mașina de dezvoltare: variabilă de mediu a utilizatorului Windows (setată pentru Aurelian pe 15.11.2026);
  - în GitHub Actions și la construirea kitului: secretul de depozit `SYNCFUSION_LICENSE` (setat pe 15.11.2026).

  Validarea e offline, deci serverul clientului nu are nevoie de internet pentru ea. Cheia depinde de versiunea majoră Syncfusion: la trecerea la o versiune majoră nouă se generează o cheie nouă din contul Syncfusion și se înlocuiește în ambele locuri. Fără cheie validă, componentele afișează mesajul de versiune de evaluare. `THIRD-PARTY-NOTICES.txt` din kit (T16) menționează Syncfusion.

---

## 5. De decis

| Subiect | Opțiuni din documentație | Observație |
| --- | --- | --- |
| Tabele mari în UI | Syncfusion DataGrid (din ST4), în locul AG Grid sau TanStack Table din schiță | De confirmat de Aurelian |
| Teste de componente UI | — | Nicio sursă nu le numește |
| Interfața POS | React în Electron sau WPF | Schița lasă ambele variante. ST1 se referă la interfața aplicației ERP; dacă se aplică și la POS, rămâne varianta React în Electron. Se decide în 02.10 |
| Licența MediatR | 12.5.0 (Apache 2.0) sau 13+ cu licență comercială | Secțiunea 2 |
| Node.js 27 (aprilie 2027) | Ultima stabilă sau ultima LTS | ST3, secțiunea 1 |

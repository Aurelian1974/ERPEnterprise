# AGENTS.md — Copilot Coding Agent Instructions

> Acest fișier ghidează Copilot coding agent când implementează autonom
> issues asignate cu `@copilot` pe GitHub.

---

## Comenzi esențiale

### Build

```bash
# Backend
cd src/Api
dotnet build erp.sln --configuration Release

# Frontend
cd frontend
npm ci
npm run build

# Type check frontend (fără build)
npm run type-check

# Lint
npm run lint
```

### Teste

```bash
# Unit tests
dotnet test tests/Unit/ --configuration Release --no-build

# Integration tests (necesită SQL Server LocalDB)
dotnet test tests/Integration/ --configuration Release --no-build

# Frontend unit tests
cd frontend && npm run test:unit

# Toate testele backend
dotnet test --configuration Release --logger "console;verbosity=detailed"
```

### Formatare și analiză statică

```bash
# Verifică formatare C# (CSharpier)
dotnet csharpier --check .

# Aplică formatare C#
dotnet csharpier .

# Verifică formatare TS/TSX (Prettier)
cd frontend && npm run format:check

# Aplică formatare TS/TSX
cd frontend && npm run format
```

### Generare client API

```bash
# Pornește backend-ul local, apoi:
npx openapi-typescript http://localhost:5000/openapi/v1.json \
  -o frontend/src/api/generated/api.ts
```

---

## Reguli pentru agent

### Ce TREBUIE să faci

```
✅ Citește copilot-instructions.md înainte de orice implementare
✅ Citește skill-ul relevant din .github/skills/ pentru task-ul curent
✅ Rulează dotnet build după orice modificare C#
✅ Rulează npm run type-check după orice modificare TS
✅ Adaugă teste pentru orice feature nou (unit + integration)
✅ Verifică că tot SQL-ul e în fișiere .sql — niciodată inline în C#
✅ Folosește CREATE OR ALTER pe orice SP nou sau modificat
✅ Adaugă tenant_id în orice SP și query nou
✅ Urmează naming conventions din copilot-instructions.md
```

### Ce NU trebuie să faci

```
🚫 Nu modifica fișiere din Shared.Kernel fără să înțelegi impactul complet
🚫 Nu adăuga SQL inline în C# — indiferent de context sau urgență
🚫 Nu adăuga connection string în niciun fișier — folosește user-secrets
🚫 Nu adăuga referințe între module (Finance → HR, etc.)
🚫 Nu modifica migrări existente — adaugă migrare nouă
🚫 Nu șterge sau modifica SP-uri existente fără migration script de rollback
🚫 Nu comite appsettings.Development.json sau appsettings.Production.json
🚫 Nu instala pachete NuGet fără să verifici Directory.Packages.props
🚫 Nu crea fișiere index.ts cu re-exporturi în frontend
🚫 Nu pune JSX/logică proprie direct în RouteComponent din fișierele index.tsx de rută
   → MOTIV: @tanstack/router-generator suprascrie index.tsx cu Hello"%%tsrPath%%"! la prima rulare
            a watcher-ului dacă fișierul nu era deja în cache-ul intern al generatorului
   → CORECT: logica paginii în -page.tsx (prefix "-" = ignorat de router), importat în index.tsx
   → GREȘIT: function RouteComponent() { return <div>JSX propriu</div> }  ← va fi suprascris
   → PATTERN:
        // index.tsx — DOAR bridge
        import MyPage from './-page'
        export const Route = createFileRoute('/path')({ component: RouteComponent })
        function RouteComponent() { return <MyPage /> }

        // -page.tsx — conținut real
        export default function MyPage() { return <div>...</div> }
```

---

## Structura repo — navigare rapidă

```
src/
  Api/                          ← Host, Program.cs, middleware, DI
  Shared/
    Shared.Kernel/              ← Primitives: Entity, AggregateRoot, Result<T>, Error
    Shared.Infrastructure/      ← Behaviors, DbConnectionFactory, Auth, Audit
    Shared.Contracts/           ← IntegrationEvents (cross-module contracts)
  Modules/
    Finance/                    ← Modul Finance
      Finance.Domain/           ← Entități, ValueObjects, DomainEvents, Errors
      Finance.Application/      ← Features (VSA), Handlers, Validators
      Finance.Infrastructure/   ← Repositories (Dapper→SP), Migrations, StoredProcedures
      Finance.Api/              ← Controllers
    HR/ Inventory/ Purchasing/ Sales/ Administration/

frontend/
  src/
    api/generated/              ← Tipuri generate din OpenAPI — NU modifica manual
    features/                   ← Features FE organizate per modul/entitate
    components/                 ← Componente shared (ui/, common/, layout/)
    store/                      ← Zustand stores
    hooks/                      ← Custom hooks (usePermission, useCurrentUser)

tests/
  Unit/                         ← Domain + Application tests (NSubstitute)
  Integration/                  ← Handler + Repository tests (SQL Server LocalDB)
  E2E/                          ← Playwright

.github/
  copilot-instructions.md       ← Reguli și naming conventions
  skills/                       ← 14 skills specializate per task type
  prompts/                      ← Reusable prompts (slash commands)
```

---

## Workflow implementare feature nou

```
1. Citește issue-ul complet
2. Identifică modulul afectat (Finance, HR, Inventory...)
3. Citește .github/skills/new-vertical-slice/SKILL.md
4. Creează migration SQL dacă e necesar (nou tabel/coloană)
   → .github/skills/new-migration/SKILL.md
5. Creează SP-urile necesare în Infrastructure/Database/StoredProcedures/
   → .github/skills/sql-objects/SKILL.md
6. Creează Command/Query + Handler + Validator
7. Creează Repository method (apelează SP)
8. Creează Controller action cu [Authorize(Policy)]
9. Creează unit tests
   → .github/skills/unit-test/SKILL.md
10. Creează integration tests
    → .github/skills/integration-test/SKILL.md
11. Rulează dotnet build + dotnet test
12. Dacă feature are UI, citește .github/skills/frontend-feature/SKILL.md
13. Rulează npm run type-check + npm run lint
```

---

## Adăugare pachet NuGet — procedură

```bash
# 1. Adaugă în Directory.Packages.props (versiune centralizată)
# <PackageVersion Include="NumePachet" Version="x.y.z" />

# 2. Adaugă referința în .csproj (fără versiune — vine din CPM)
# <PackageReference Include="NumePachet" />

# 3. Verifică că nu există conflict cu pachete existente
dotnet restore
```

---

## Environment — development

```
SQL Server: SQL Server LocalDB sau instanță locală
            Connection string: dotnet user-secrets (nu în fișiere)
Redis:      instalat local pe Windows, port default 6379
Seq:        http://localhost:5341 (log viewer)
Frontend:   http://localhost:5173 (Vite dev server)
Backend:    http://localhost:5000 (Kestrel)
```

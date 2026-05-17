---
mode: agent
description: Face code review complet pe selecție sau PR — verifică BLOCKERS, naming, arhitectură, SQL, security.
---

Fă code review pe codul selectat sau pe fișierele modificate în PR, respectând skill-ul `code-review`.

**Verifică în ordine — oprește-te la primul BLOCKER găsit:**

### 🔴 BLOCKERS (raportează imediat)
- SQL inline în C# (`const string sql`, `$"SELECT..."`)
- Connection string hardcodat sau în fișiere comise în repo
- EF Core (`DbContext`, `DbSet`, `Include`, `IQueryable`)
- Referință directă între module (`using Finance.Domain` în `HR.Application`)
- `DEFAULT NEWID()` sau `DEFAULT NEWSEQUENTIALID()` pe PK aggregate root
- `SELECT *` în SP, View, sau TVF
- Concatenare string în SQL

### ⚠️ AVERTISMENTE
- SP fără `tenant_id = @TenantId` în WHERE
- SP fără `SET NOCOUNT ON`
- SP fără `CREATE OR ALTER` (folosește `CREATE` simplu)
- Handler care aruncă excepție pentru eroare business (în loc de `Result<T>`)
- Controller cu logică business (în loc de `ISender.Send()`)
- Repository cu SQL inline (în loc de apel SP)
- TypeScript cu `any`
- Barrel files (`index.ts` cu re-exporturi)
- Fetch direct în componentă React (în loc de TanStack Query)
- Naming incorect (SQL: `snake_case`, C#: `PascalCase`, TS: convențiile din copilot-instructions.md)
- `[Authorize(Policy)]` lipsă pe Controller action
- `CancellationToken` lipsă pe metodă async publică

**Format raport:**
```
🔴 BLOCKERS: [listă sau "Niciun blocker găsit"]
⚠️ AVERTISMENTE: [listă sau "Niciun avertisment"]
✅ BINE: [ce e corect implementat]
💡 SUGESTII: [îmbunătățiri opționale]
```

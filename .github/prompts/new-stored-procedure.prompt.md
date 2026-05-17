---
mode: agent
description: Generează un Stored Procedure complet — INSERT, SELECT, UPDATE, DELETE, raport cu CTE, paginat cu filtre.
---

Creează un Stored Procedure respectând skill-ul `sql-objects`.

**Informații necesare:**
- Tip operație: [INSERT / SELECT single / SELECT paginat / UPDATE / DELETE / Raport CTE]
- Modul și schemă: [finance / hr / inventory / ...]
- Entitate principală și tabelele implicate
- Parametri de filtrare (pentru SELECT paginat)
- Coloane de returnat

**Template obligatoriu:**
```sql
CREATE OR ALTER PROCEDURE {schema}.usp_{Action}{Entity}
    @TenantId  UNIQUEIDENTIFIER,
    -- restul parametrilor tipizați explicit
AS
BEGIN
    SET NOCOUNT ON;
    -- logică
END;
GO
```

**Reguli obligatorii:**
- `CREATE OR ALTER` — întotdeauna
- `SET NOCOUNT ON` — întotdeauna
- `@TenantId UNIQUEIDENTIFIER` — pe orice SP care accesează date
- `WHERE ... AND {table}.tenant_id = @TenantId` — pe fiecare tabel accesat
- `AND {joined_table}.tenant_id = @TenantId` — pe fiecare JOIN
- Coloane cu alias explicit `AS NumeAlias` — niciodată `SELECT *`
- Schema prefix pe toate tabelele și obiectele
- `SYSUTCDATETIME()` pentru datetime — niciodată `GETDATE()`
- INSERT: `@Id UNIQUEIDENTIFIER` vine din C# (UUIDv7), nu generat în SQL
- SELECT paginat: `COUNT(*) OVER () AS TotalCount` + `OFFSET/FETCH`
- UPDATE/DELETE: `SELECT @@ROWCOUNT AS AffectedRows` la final
- Niciodată concatenare string sau SQL dinamic necontrolat

---
name: dapper-repository
description: >-
  Generează metode Dapper corecte pentru repository-uri ERP.
  Regula fundamentală: ZERO SQL inline în C# — orice apel de date
  se face exclusiv prin Stored Procedures, Views sau TVF-uri.
---

# Dapper Repository

## Regula fundamentală

```
Repository C#  →  apelează SP/View/TVF  →  SQL trăiește în fișiere .sql
INTERZIS: const string sql = "SELECT..." în orice fișier C#
```

Orice metodă repository are exact același pattern:
1. `using var conn = _connectionFactory.Create()`
2. Apel Dapper cu `CommandDefinition` + `commandType: CommandType.StoredProcedure`
3. Parametri anonimi — fără string concatenation

## Setup obligatoriu

```csharp
internal sealed class {Entity}Repository : I{Entity}Repository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenant;

    public {Entity}Repository(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }
}
```

## Template INSERT — apelează SP

```csharp
// C# — apelează SP, zero SQL inline
public async Task InsertAsync({Entity} entity, CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    await conn.ExecuteAsync(new CommandDefinition(
        "usp_Create{Entity}",
        new
        {
            entity.Id,        // UUIDv7 generat în constructorul entității
            entity.TenantId,
            /* restul proprietăților */
            CreatedBy = _tenant.UserId
        },
        commandType: CommandType.StoredProcedure,
        cancellationToken: ct));
}
```

```sql
-- Database/StoredProcedures/usp_Create{Entity}.sql
CREATE OR ALTER PROCEDURE {schema}.usp_Create{Entity}
    @Id          UNIQUEIDENTIFIER,
    @TenantId    UNIQUEIDENTIFIER,
    @{Param1}    {DataType},
    @CreatedBy   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO {schema}.{table} (id, tenant_id, {col1}, created_at, created_by)
    VALUES (@Id, @TenantId, @{Param1}, SYSUTCDATETIME(), @CreatedBy);
END;
GO
```

## Template INSERT child table (BIGINT IDENTITY)

```csharp
public async Task InsertLinesAsync(
    IEnumerable<{Entity}Line> lines, CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    // Dapper acceptă IEnumerable — batch insert
    await conn.ExecuteAsync(new CommandDefinition(
        "usp_Create{Entity}Line",
        lines.Select(l => new
        {
            l.{ParentId},
            l.TenantId,
            l.{Col1},
            l.{Col2}
        }),
        commandType: CommandType.StoredProcedure,
        cancellationToken: ct));
}
```

```sql
CREATE OR ALTER PROCEDURE {schema}.usp_Create{Entity}Line
    @{ParentId}  UNIQUEIDENTIFIER,
    @TenantId    UNIQUEIDENTIFIER,
    @{Col1}      {DataType},
    @{Col2}      {DataType}
AS
BEGIN
    SET NOCOUNT ON;
    -- id BIGINT IDENTITY generat automat de SQL Server
    INSERT INTO {schema}.{child_table} ({parent_id}, tenant_id, {col1}, {col2})
    VALUES (@{ParentId}, @TenantId, @{Col1}, @{Col2});
END;
GO
```

## Template GET BY ID — apelează SP

```csharp
public async Task<{Entity}DetailDto?> GetByIdAsync(
    Guid id, CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    return await conn.QuerySingleOrDefaultAsync<{Entity}DetailDto>(
        new CommandDefinition(
            "usp_Get{Entity}ById",
            new { Id = id, TenantId = _tenant.TenantId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
}
```

```sql
CREATE OR ALTER PROCEDURE {schema}.usp_Get{Entity}ById
    @Id       UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.id         AS Id,
           e.tenant_id  AS TenantId,
           e.{col1}     AS {Col1},
           e.created_at AS CreatedAt
    FROM {schema}.{table} e
    WHERE e.id        = @Id
      AND e.tenant_id = @TenantId;
END;
GO
```

## Template LIST paginat — apelează SP

```csharp
public async Task<PagedResult<{Entity}ListDto>> ListAsync(
    List{Entity}Query query, CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    var rows = (await conn.QueryAsync<{Entity}ListDto>(
        new CommandDefinition(
            "usp_List{Entity}",
            new
            {
                TenantId = _tenant.TenantId,
                query.Search,
                query.Page,
                query.PageSize
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct))).ToList();

    return new PagedResult<{Entity}ListDto>(
        rows,
        rows.FirstOrDefault()?.TotalCount ?? 0,
        query.Page,
        query.PageSize);
}
```

```sql
CREATE OR ALTER PROCEDURE {schema}.usp_List{Entity}
    @TenantId UNIQUEIDENTIFIER,
    @Search   NVARCHAR(200) = NULL,
    @Page     INT           = 1,
    @PageSize INT           = 25
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) OVER ()  AS TotalCount,
           e.id              AS Id,
           e.{col1}          AS {Col1},
           e.created_at      AS CreatedAt
    FROM {schema}.{table} e
    WHERE e.tenant_id = @TenantId
      AND (@Search IS NULL OR e.{col} LIKE '%' + @Search + '%')
    ORDER BY e.created_at DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
GO
```

## Template UPDATE — apelează SP

```csharp
public async Task<bool> UpdateAsync({Entity} entity, CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    var affected = await conn.ExecuteAsync(new CommandDefinition(
        "usp_Update{Entity}",
        new
        {
            entity.Id,
            entity.TenantId,
            /* proprietăți modificate */
            UpdatedBy = _tenant.UserId
        },
        commandType: CommandType.StoredProcedure,
        cancellationToken: ct));
    return affected > 0;
}
```

```sql
CREATE OR ALTER PROCEDURE {schema}.usp_Update{Entity}
    @Id        UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER,
    @{Col1}    {DataType},
    @UpdatedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE {schema}.{table}
    SET {col1}     = @{Col1},
        updated_at = SYSUTCDATETIME(),
        updated_by = @UpdatedBy
    WHERE id        = @Id
      AND tenant_id = @TenantId;
    SELECT @@ROWCOUNT;
END;
GO
```

## Template VIEW (pentru read-side complex)

```csharp
// Repository — apelează View prin SELECT direct, parametri siguri
public async Task<IEnumerable<{Report}Dto>> GetAgingAsync(CancellationToken ct = default)
{
    using var conn = _connectionFactory.Create();
    return await conn.QueryAsync<{Report}Dto>(
        new CommandDefinition(
            "usp_Get{Report}",   // SP care citește din view
            new { TenantId = _tenant.TenantId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
}
```

```sql
-- View (creat prin migration)
CREATE OR ALTER VIEW {schema}.vw_{Description}
AS
    SELECT e.tenant_id,
           e.id,
           e.{col1},
           /* calcule complexe */
    FROM {schema}.{table} e
    WHERE /* filtre fixe */;
GO

-- SP care citește din view (parametri dinamici)
CREATE OR ALTER PROCEDURE {schema}.usp_Get{Report}
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT v.id, v.{col1}
    FROM {schema}.vw_{Description} v
    WHERE v.tenant_id = @TenantId;
END;
GO
```

## Reguli obligatorii
- ZERO SQL inline în C# — fără `const string sql`, fără string interpolation cu SQL
- `CommandDefinition` cu `commandType: CommandType.StoredProcedure` pe orice apel
- `using var conn` — nu uita să eliberezi conexiunea
- `tenant_id = @TenantId` în ORICE SP
- SP-uri: `CREATE OR ALTER` — niciodată `CREATE` simplu
- Coloane explicite în SELECT din SP — niciodată `SELECT *`
- Schema prefix obligatoriu în SP — niciodată `dbo`

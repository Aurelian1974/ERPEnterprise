---
name: new-vertical-slice
description: >-
  Scaffolează un feature complet VSA pentru ERP: Command/Query, Handler,
  Validator, Controller action, Repository (apelează SP), SP SQL.
  Respectă convențiile stricte: zero SQL în C#, Result<T>, tenant_id obligatoriu.
---

# New Vertical Slice

## Când se aplică
Când utilizatorul cere un feature nou, un endpoint nou, sau o operație nouă
(Create, Update, Delete, GetById, List) pentru orice modul ERP.

## Structura de fișiere de generat

```
src/Modules/{Module}/{Module}.Application/Features/{Entity}/{Action}/
  {Action}{Entity}Command.cs           ← sau Query
  {Action}{Entity}CommandHandler.cs
  {Action}{Entity}Validator.cs
  {Action}{Entity}Request.cs
  {Action}{Entity}Response.cs

src/Modules/{Module}/{Module}.Infrastructure/
  Repositories/{Entity}Repository.cs   ← metodă nouă
  StoredProcedures/
    usp_{Action}{Entity}.sql  ← SP NOU, CREATE OR ALTER

src/Modules/{Module}/{Module}.Api/
  Controllers/{Entity}Controller.cs    ← action adăugat
```

## 1. Command (operații de scriere)

```csharp
// {Action}{Entity}Command.cs
public sealed record {Action}{Entity}Command(
    /* parametri din request */
) : ICommand<Result<{ReturnType}>>;

// {Action}{Entity}CommandHandler.cs
internal sealed class {Action}{Entity}CommandHandler
    : IRequestHandler<{Action}{Entity}Command, Result<{ReturnType}>>
{
    private readonly I{Entity}Repository _repo;
    private readonly ICurrentUser _currentUser;

    public {Action}{Entity}CommandHandler(
        I{Entity}Repository repo,
        ICurrentUser currentUser)
    {
        _repo = repo;
        _currentUser = currentUser;
    }

    public async Task<Result<{ReturnType}>> Handle(
        {Action}{Entity}Command command,
        CancellationToken cancellationToken)
    {
        // 1. Validare business (nu input — asta face Validator-ul)
        // 2. Construiește entitatea cu ID UUIDv7 generat în constructor
        // 3. Persistă via repository → repository apelează SP
        // 4. Returnează Result<T> — NICIODATĂ throw pentru erori business
    }
}

// {Action}{Entity}Validator.cs
public sealed class {Action}{Entity}CommandValidator
    : AbstractValidator<{Action}{Entity}Command>
{
    public {Action}{Entity}CommandValidator()
    {
        RuleFor(x => x.{Property}).NotEmpty();
    }
}
```

## 2. Query (operații de citire)

```csharp
public sealed record Get{Entity}ByIdQuery(Guid Id)
    : IQuery<Result<{Entity}DetailDto>>;

public sealed record List{Entity}Query(
    int Page = 1,
    int PageSize = 25,
    string? Search = null
) : IQuery<Result<PagedResult<{Entity}ListDto>>>;

internal sealed class Get{Entity}ByIdQueryHandler
    : IRequestHandler<Get{Entity}ByIdQuery, Result<{Entity}DetailDto>>
{
    private readonly I{Entity}Repository _repo;

    public async Task<Result<{Entity}DetailDto>> Handle(
        Get{Entity}ByIdQuery query,
        CancellationToken cancellationToken)
    {
        var dto = await _repo.GetByIdAsync(query.Id, cancellationToken);
        if (dto is null)
            return Result<{Entity}DetailDto>.Failure(
                {Module}Errors.{Entity}NotFound(query.Id));

        return Result<{Entity}DetailDto>.Success(dto);
    }
}
```

## 3. Controller

```csharp
[ApiController]
[Route("api/v1/{module}/{entity}")]
public sealed class {Entity}Controller : ControllerBase
{
    private readonly ISender _sender;

    public {Entity}Controller(ISender sender) => _sender = sender;

    [HttpPost]
    [Authorize(Policy = "{module}.{entity}.create")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] Create{Entity}Request request,
        CancellationToken cancellationToken)
    {
        var command = new Create{Entity}Command(/* map din request */);
        var result = await _sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, result.Value)
            : result.Error.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "{module}.{entity}.view")]
    [ProducesResponseType<{Entity}DetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new Get{Entity}ByIdQuery(id), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }

    [HttpGet]
    [Authorize(Policy = "{module}.{entity}.view")]
    [ProducesResponseType<PagedResult<{Entity}ListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] List{Entity}Query query,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(query, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }
}
```

## 4. Repository — apelează SP, ZERO SQL inline

```csharp
// I{Entity}Repository.cs
public interface I{Entity}Repository
{
    Task InsertAsync({Entity} entity, CancellationToken ct = default);
    Task<{Entity}DetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<{Entity}ListDto>> ListAsync(
        List{Entity}Query query, CancellationToken ct = default);
}

// {Entity}Repository.cs
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

    public async Task InsertAsync({Entity} entity, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(new CommandDefinition(
            "usp_Create{Entity}",
            new
            {
                entity.Id,          // UUIDv7 generat în constructorul entității
                entity.TenantId,
                /* restul parametrilor */
                CreatedBy = _tenant.UserId
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

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
}
```

## 5. Stored Procedures SQL

```sql
-- usp_Create{Entity}.sql
CREATE OR ALTER PROCEDURE {schema}.usp_Create{Entity}
    @Id          UNIQUEIDENTIFIER,   -- vine din C# (UUIDv7)
    @TenantId    UNIQUEIDENTIFIER,
    @{Param1}    {DataType},
    @CreatedBy   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO {schema}.{table} (
        id, tenant_id, {col1}, created_at, created_by
    )
    VALUES (
        @Id, @TenantId, @{Param1}, SYSUTCDATETIME(), @CreatedBy
    );
END;
GO

-- usp_Get{Entity}ById.sql
CREATE OR ALTER PROCEDURE {schema}.usp_Get{Entity}ById
    @Id       UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.id          AS Id,
           e.tenant_id   AS TenantId,
           e.{col1}      AS {Col1},
           e.created_at  AS CreatedAt,
           e.created_by  AS CreatedBy
    FROM {schema}.{table} e
    WHERE e.id        = @Id
      AND e.tenant_id = @TenantId;
END;
GO

-- usp_List{Entity}.sql
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
      AND (@Search IS NULL
           OR e.{search_col} LIKE '%' + @Search + '%')
    ORDER BY e.created_at DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
GO
```

## Reguli obligatorii
- `tenant_id = @TenantId` în ORICE SP — fără excepție
- Zero SQL inline în C# — repository apelează exclusiv SP-uri
- Handler returnează `Result<T>` — niciodată `throw` pentru erori business
- ID aggregate root = `UNIQUEIDENTIFIER` UUIDv7 generat în constructorul entității
- ID child tables = `BIGINT IDENTITY` — niciodată expus în API
- SP-uri: `CREATE OR ALTER` — întotdeauna idempotente
- Controller action: `[Authorize(Policy = "...")]` pe fiecare metodă
- Connection string: NICIODATĂ hardcodat — vine din user-secrets / env vars

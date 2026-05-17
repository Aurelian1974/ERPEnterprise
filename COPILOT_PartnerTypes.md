# GitHub Copilot Instructions — Feature: Tipuri Parteneri (Partner Types)

## Context & Arhitectură

Acesta este modulul **Tipuri Parteneri** din **ValyanERP**.
Stack: **.NET 10 / C#**, **Dapper** (nu EF Core), **SQL Server 2022+**, **MediatR + CQRS**,
**React 19 / TypeScript / Vite**, **TanStack Query v5**, **Zustand**.
Pattern: **Clean Architecture** cu layerele: `Domain → Application → Infrastructure → API`.

---

## 1. DATABASE LAYER

### 1.1 Tabele

#### `dbo.PartnerTypes`

```sql
CREATE TABLE dbo.PartnerTypes
(
    PartnerTypeId   TINYINT         NOT NULL IDENTITY(1,1),
    Code            NVARCHAR(50)    NOT NULL,   -- 'CLIENT','VENDOR','INDIVIDUAL','BANK','NGO','PUBLIC_INSTITUTION'
    Name            NVARCHAR(100)   NOT NULL,   -- 'Client', 'Furnizor', 'Persoană Fizică' etc.
    Description     NVARCHAR(500)   NULL,
    IsSystem        BIT             NOT NULL CONSTRAINT DF_PartnerTypes_IsSystem        DEFAULT 0,  -- tipurile seed nu pot fi șterse
    IsActive        BIT             NOT NULL CONSTRAINT DF_PartnerTypes_IsActive        DEFAULT 1,
    AffectsIssuedInvoices   BIT     NOT NULL CONSTRAINT DF_PartnerTypes_AffectsIssued  DEFAULT 0,  -- true → apare în facturi emise
    AffectsReceivedInvoices BIT     NOT NULL CONSTRAINT DF_PartnerTypes_AffectsReceived DEFAULT 0, -- true → apare în facturi primite
    SortOrder       SMALLINT        NOT NULL CONSTRAINT DF_PartnerTypes_SortOrder       DEFAULT 0,
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_PartnerTypes_CreatedAt       DEFAULT SYSDATETIME(),
    CreatedBy       NVARCHAR(100)   NOT NULL CONSTRAINT DF_PartnerTypes_CreatedBy       DEFAULT SYSTEM_USER,
    UpdatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_PartnerTypes_UpdatedAt       DEFAULT SYSDATETIME(),
    UpdatedBy       NVARCHAR(100)   NOT NULL CONSTRAINT DF_PartnerTypes_UpdatedBy       DEFAULT SYSTEM_USER,

    CONSTRAINT PK_PartnerTypes  PRIMARY KEY CLUSTERED (PartnerTypeId),
    CONSTRAINT UQ_PartnerTypes_Code UNIQUE (Code)
);
```

#### `dbo.PartnerTypeAssignments`
> Many-to-many între Partners și PartnerTypes. Se va folosi când modulul Partners este gata.

```sql
CREATE TABLE dbo.PartnerTypeAssignments
(
    PartnerId       INT         NOT NULL,
    PartnerTypeId   TINYINT     NOT NULL,
    AssignedAt      DATETIME2(0) NOT NULL CONSTRAINT DF_PTA_AssignedAt DEFAULT SYSDATETIME(),
    AssignedBy      NVARCHAR(100) NOT NULL CONSTRAINT DF_PTA_AssignedBy DEFAULT SYSTEM_USER,

    CONSTRAINT PK_PartnerTypeAssignments PRIMARY KEY CLUSTERED (PartnerId, PartnerTypeId),
    CONSTRAINT FK_PTA_Partners     FOREIGN KEY (PartnerId)     REFERENCES dbo.Partners(PartnerId),
    CONSTRAINT FK_PTA_PartnerTypes FOREIGN KEY (PartnerTypeId) REFERENCES dbo.PartnerTypes(PartnerTypeId)
);
```

---

### 1.2 Seed Data (DbUp migration)

```sql
-- Migration: V003__SeedPartnerTypes.sql
SET IDENTITY_INSERT dbo.PartnerTypes ON;

INSERT INTO dbo.PartnerTypes
    (PartnerTypeId, Code, Name, Description, IsSystem, IsActive,
     AffectsIssuedInvoices, AffectsReceivedInvoices, SortOrder)
VALUES
    (1, 'CLIENT',             'Client',              'Partener căruia i se emit facturi.',                    1, 1, 1, 0, 10),
    (2, 'VENDOR',             'Furnizor',            'Partener de la care se primesc facturi.',               1, 1, 0, 1, 20),
    (3, 'INDIVIDUAL',         'Persoană Fizică',     'Persoană fizică (fără CUI). Poate fi Client/Furnizor.',1, 1, 0, 0, 30),
    (4, 'BANK',               'Bancă',               'Instituție bancară.',                                   1, 1, 0, 1, 40),
    (5, 'NGO',                'ONG',                 'Organizație non-guvernamentală.',                       1, 1, 1, 1, 50),
    (6, 'PUBLIC_INSTITUTION', 'Instituție Publică',  'Instituție publică (bugetară).',                        1, 1, 1, 1, 60);

SET IDENTITY_INSERT dbo.PartnerTypes OFF;
```

---

### 1.3 Stored Procedures

#### `dbo.usp_PartnerTypes_GetAll`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_PartnerTypes_GetAll
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        pt.PartnerTypeId,
        pt.Code,
        pt.Name,
        pt.Description,
        pt.IsSystem,
        pt.IsActive,
        pt.AffectsIssuedInvoices,
        pt.AffectsReceivedInvoices,
        pt.SortOrder,
        pt.CreatedAt,
        pt.CreatedBy,
        pt.UpdatedAt,
        pt.UpdatedBy,
        -- câți parteneri activi folosesc acest tip
        UsageCount = (
            SELECT COUNT(*)
            FROM dbo.PartnerTypeAssignments pta
            INNER JOIN dbo.Partners p ON p.PartnerId = pta.PartnerId AND p.IsActive = 1
            WHERE pta.PartnerTypeId = pt.PartnerTypeId
        )
    FROM dbo.PartnerTypes pt
    WHERE (@IncludeInactive = 1 OR pt.IsActive = 1)
    ORDER BY pt.SortOrder, pt.Name;
END;
```

#### `dbo.usp_PartnerTypes_GetById`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_PartnerTypes_GetById
    @PartnerTypeId TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        pt.PartnerTypeId, pt.Code, pt.Name, pt.Description,
        pt.IsSystem, pt.IsActive,
        pt.AffectsIssuedInvoices, pt.AffectsReceivedInvoices,
        pt.SortOrder, pt.CreatedAt, pt.CreatedBy, pt.UpdatedAt, pt.UpdatedBy
    FROM dbo.PartnerTypes pt
    WHERE pt.PartnerTypeId = @PartnerTypeId;
END;
```

#### `dbo.usp_PartnerTypes_Upsert`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_PartnerTypes_Upsert
    @PartnerTypeId          TINYINT         = NULL,  -- NULL = INSERT
    @Code                   NVARCHAR(50),
    @Name                   NVARCHAR(100),
    @Description            NVARCHAR(500)   = NULL,
    @IsActive               BIT             = 1,
    @AffectsIssuedInvoices  BIT             = 0,
    @AffectsReceivedInvoices BIT            = 0,
    @SortOrder              SMALLINT        = 0,
    @UpdatedBy              NVARCHAR(100),
    @NewPartnerTypeId       TINYINT         OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Validare duplicat Code
    IF EXISTS (
        SELECT 1 FROM dbo.PartnerTypes
        WHERE Code = @Code
          AND (@PartnerTypeId IS NULL OR PartnerTypeId <> @PartnerTypeId)
    )
    BEGIN
        RAISERROR('Codul ''%s'' există deja în nomenclatorul de tipuri parteneri.', 16, 1, @Code);
        RETURN;
    END;

    IF @PartnerTypeId IS NULL
    BEGIN
        INSERT INTO dbo.PartnerTypes
            (Code, Name, Description, IsSystem, IsActive,
             AffectsIssuedInvoices, AffectsReceivedInvoices,
             SortOrder, CreatedBy, UpdatedBy)
        VALUES
            (@Code, @Name, @Description, 0, @IsActive,
             @AffectsIssuedInvoices, @AffectsReceivedInvoices,
             @SortOrder, @UpdatedBy, @UpdatedBy);

        SET @NewPartnerTypeId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        -- Nu se modifică IsSystem, Code (dacă IsSystem = 1)
        IF EXISTS (SELECT 1 FROM dbo.PartnerTypes WHERE PartnerTypeId = @PartnerTypeId AND IsSystem = 1)
        BEGIN
            -- Tipurile sistem: se permite modificarea doar a Name, Description, SortOrder, IsActive
            UPDATE dbo.PartnerTypes
            SET
                Name        = @Name,
                Description = @Description,
                SortOrder   = @SortOrder,
                IsActive    = @IsActive,
                UpdatedAt   = SYSDATETIME(),
                UpdatedBy   = @UpdatedBy
            WHERE PartnerTypeId = @PartnerTypeId;
        END
        ELSE
        BEGIN
            UPDATE dbo.PartnerTypes
            SET
                Code                    = @Code,
                Name                    = @Name,
                Description             = @Description,
                IsActive                = @IsActive,
                AffectsIssuedInvoices   = @AffectsIssuedInvoices,
                AffectsReceivedInvoices = @AffectsReceivedInvoices,
                SortOrder               = @SortOrder,
                UpdatedAt               = SYSDATETIME(),
                UpdatedBy               = @UpdatedBy
            WHERE PartnerTypeId = @PartnerTypeId;
        END;

        SET @NewPartnerTypeId = @PartnerTypeId;
    END;
END;
```

#### `dbo.usp_PartnerTypes_Delete`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_PartnerTypes_Delete
    @PartnerTypeId TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.PartnerTypes WHERE PartnerTypeId = @PartnerTypeId AND IsSystem = 1)
    BEGIN
        RAISERROR('Tipurile de sistem nu pot fi șterse.', 16, 1);
        RETURN;
    END;

    IF EXISTS (SELECT 1 FROM dbo.PartnerTypeAssignments WHERE PartnerTypeId = @PartnerTypeId)
    BEGIN
        RAISERROR('Tipul nu poate fi șters — este asignat la unul sau mai mulți parteneri. Dezactivați-l în schimb.', 16, 1);
        RETURN;
    END;

    DELETE FROM dbo.PartnerTypes WHERE PartnerTypeId = @PartnerTypeId;
END;
```

---

## 2. DOMAIN LAYER (C#)

### `PartnerType.cs` — Entity

```csharp
namespace ValyanERP.Domain.Partners;

public sealed class PartnerType
{
    public byte PartnerTypeId { get; init; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; private set; }
    public bool AffectsIssuedInvoices { get; private set; }
    public bool AffectsReceivedInvoices { get; private set; }
    public short SortOrder { get; private set; }
    public int UsageCount { get; init; }   // read-only, din SP
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = default!;
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = default!;
}
```

### `IPartnerTypeRepository.cs`

```csharp
namespace ValyanERP.Domain.Partners;

public interface IPartnerTypeRepository
{
    Task<IReadOnlyList<PartnerType>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PartnerType?> GetByIdAsync(byte id, CancellationToken ct = default);
    Task<byte> UpsertAsync(PartnerTypeUpsertData data, CancellationToken ct = default);
    Task DeleteAsync(byte id, CancellationToken ct = default);
}

public sealed record PartnerTypeUpsertData(
    byte? PartnerTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    bool AffectsIssuedInvoices,
    bool AffectsReceivedInvoices,
    short SortOrder,
    string UpdatedBy
);
```

---

## 3. APPLICATION LAYER (CQRS / MediatR)

### Queries

#### `GetPartnerTypesQuery`

```csharp
namespace ValyanERP.Application.Partners.Queries;

public sealed record GetPartnerTypesQuery(bool IncludeInactive = false)
    : IRequest<IReadOnlyList<PartnerTypeDto>>;

public sealed class GetPartnerTypesHandler(IPartnerTypeRepository repo)
    : IRequestHandler<GetPartnerTypesQuery, IReadOnlyList<PartnerTypeDto>>
{
    public async Task<IReadOnlyList<PartnerTypeDto>> Handle(
        GetPartnerTypesQuery request, CancellationToken ct)
    {
        var types = await repo.GetAllAsync(request.IncludeInactive, ct);
        return types.Select(PartnerTypeDto.FromDomain).ToList();
    }
}
```

#### `GetPartnerTypeByIdQuery`

```csharp
public sealed record GetPartnerTypeByIdQuery(byte Id)
    : IRequest<PartnerTypeDto?>;

public sealed class GetPartnerTypeByIdHandler(IPartnerTypeRepository repo)
    : IRequestHandler<GetPartnerTypeByIdQuery, PartnerTypeDto?>
{
    public async Task<PartnerTypeDto?> Handle(
        GetPartnerTypeByIdQuery request, CancellationToken ct)
    {
        var type = await repo.GetByIdAsync(request.Id, ct);
        return type is null ? null : PartnerTypeDto.FromDomain(type);
    }
}
```

### Commands

#### `UpsertPartnerTypeCommand`

```csharp
namespace ValyanERP.Application.Partners.Commands;

public sealed record UpsertPartnerTypeCommand(
    byte? PartnerTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    bool AffectsIssuedInvoices,
    bool AffectsReceivedInvoices,
    short SortOrder
) : IRequest<byte>;

public sealed class UpsertPartnerTypeValidator : AbstractValidator<UpsertPartnerTypeCommand>
{
    public UpsertPartnerTypeValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().MaximumLength(50)
            .Matches(@"^[A-Z0-9_]+$").WithMessage("Codul trebuie să conțină doar litere mari, cifre și underscore.");

        RuleFor(x => x.Name)
            .NotEmpty().MaximumLength(100);

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class UpsertPartnerTypeHandler(
    IPartnerTypeRepository repo,
    ICurrentUserService currentUser)
    : IRequestHandler<UpsertPartnerTypeCommand, byte>
{
    public async Task<byte> Handle(UpsertPartnerTypeCommand cmd, CancellationToken ct)
    {
        var data = new PartnerTypeUpsertData(
            cmd.PartnerTypeId,
            cmd.Code.ToUpperInvariant(),
            cmd.Name,
            cmd.Description,
            cmd.IsActive,
            cmd.AffectsIssuedInvoices,
            cmd.AffectsReceivedInvoices,
            cmd.SortOrder,
            currentUser.UserName
        );

        return await repo.UpsertAsync(data, ct);
    }
}
```

#### `DeletePartnerTypeCommand`

```csharp
public sealed record DeletePartnerTypeCommand(byte PartnerTypeId) : IRequest;

public sealed class DeletePartnerTypeHandler(IPartnerTypeRepository repo)
    : IRequestHandler<DeletePartnerTypeCommand>
{
    public async Task Handle(DeletePartnerTypeCommand cmd, CancellationToken ct)
        => await repo.DeleteAsync(cmd.PartnerTypeId, ct);
}
```

### DTO

```csharp
namespace ValyanERP.Application.Partners;

public sealed record PartnerTypeDto(
    byte PartnerTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    bool AffectsIssuedInvoices,
    bool AffectsReceivedInvoices,
    short SortOrder,
    int UsageCount,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime UpdatedAt,
    string UpdatedBy
)
{
    public static PartnerTypeDto FromDomain(PartnerType pt) => new(
        pt.PartnerTypeId, pt.Code, pt.Name, pt.Description,
        pt.IsSystem, pt.IsActive,
        pt.AffectsIssuedInvoices, pt.AffectsReceivedInvoices,
        pt.SortOrder, pt.UsageCount,
        pt.CreatedAt, pt.CreatedBy, pt.UpdatedAt, pt.UpdatedBy
    );
}
```

---

## 4. INFRASTRUCTURE LAYER (Dapper)

### `PartnerTypeRepository.cs`

```csharp
namespace ValyanERP.Infrastructure.Repositories.Partners;

public sealed class PartnerTypeRepository(IDbConnectionFactory dbFactory)
    : IPartnerTypeRepository
{
    public async Task<IReadOnlyList<PartnerType>> GetAllAsync(
        bool includeInactive, CancellationToken ct)
    {
        using var conn = dbFactory.CreateConnection();
        var rows = await conn.QueryAsync<PartnerType>(
            "dbo.usp_PartnerTypes_GetAll",
            new { IncludeInactive = includeInactive },
            commandType: CommandType.StoredProcedure
        );
        return rows.ToList();
    }

    public async Task<PartnerType?> GetByIdAsync(byte id, CancellationToken ct)
    {
        using var conn = dbFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<PartnerType>(
            "dbo.usp_PartnerTypes_GetById",
            new { PartnerTypeId = id },
            commandType: CommandType.StoredProcedure
        );
    }

    public async Task<byte> UpsertAsync(PartnerTypeUpsertData data, CancellationToken ct)
    {
        using var conn = dbFactory.CreateConnection();
        var p = new DynamicParameters();
        p.Add("@PartnerTypeId",           data.PartnerTypeId,           DbType.Byte,    direction: ParameterDirection.Input);
        p.Add("@Code",                    data.Code,                    DbType.String);
        p.Add("@Name",                    data.Name,                    DbType.String);
        p.Add("@Description",             data.Description,             DbType.String);
        p.Add("@IsActive",                data.IsActive,                DbType.Boolean);
        p.Add("@AffectsIssuedInvoices",   data.AffectsIssuedInvoices,   DbType.Boolean);
        p.Add("@AffectsReceivedInvoices", data.AffectsReceivedInvoices, DbType.Boolean);
        p.Add("@SortOrder",               data.SortOrder,               DbType.Int16);
        p.Add("@UpdatedBy",               data.UpdatedBy,               DbType.String);
        p.Add("@NewPartnerTypeId",        dbType: DbType.Byte,          direction: ParameterDirection.Output);

        await conn.ExecuteAsync(
            "dbo.usp_PartnerTypes_Upsert", p,
            commandType: CommandType.StoredProcedure
        );

        return p.Get<byte>("@NewPartnerTypeId");
    }

    public async Task DeleteAsync(byte id, CancellationToken ct)
    {
        using var conn = dbFactory.CreateConnection();
        await conn.ExecuteAsync(
            "dbo.usp_PartnerTypes_Delete",
            new { PartnerTypeId = id },
            commandType: CommandType.StoredProcedure
        );
    }
}
```

---

## 5. API LAYER

### `PartnerTypesController.cs`

```csharp
[ApiController]
[Route("api/v1/partner-types")]
[Authorize]
public sealed class PartnerTypesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returnează lista tipurilor de parteneri.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PartnerTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetPartnerTypesQuery(includeInactive), ct);
        return Ok(result);
    }

    /// <summary>
    /// Returnează un tip de partener după ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<PartnerTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(byte id, CancellationToken ct)
    {
        var result = await sender.Send(new GetPartnerTypeByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Creează un tip de partener nou.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<byte>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertPartnerTypeCommand command,
        CancellationToken ct)
    {
        // Ensure PartnerTypeId is null for creation
        var cmd = command with { PartnerTypeId = null };
        var newId = await sender.Send(cmd, ct);
        return CreatedAtAction(nameof(GetById), new { id = newId }, newId);
    }

    /// <summary>
    /// Actualizează un tip de partener existent.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        byte id,
        [FromBody] UpsertPartnerTypeCommand command,
        CancellationToken ct)
    {
        var cmd = command with { PartnerTypeId = id };
        await sender.Send(cmd, ct);
        return NoContent();
    }

    /// <summary>
    /// Șterge un tip de partener (dacă nu este sistem și nu are parteneri asignați).
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(byte id, CancellationToken ct)
    {
        await sender.Send(new DeletePartnerTypeCommand(id), ct);
        return NoContent();
    }
}
```

---

## 6. FRONTEND — React 19 / TypeScript

### 6.1 Tip TypeScript

```typescript
// src/types/partnerType.ts

export interface PartnerType {
  partnerTypeId: number;
  code: string;
  name: string;
  description?: string;
  isSystem: boolean;
  isActive: boolean;
  affectsIssuedInvoices: boolean;
  affectsReceivedInvoices: boolean;
  sortOrder: number;
  usageCount: number;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
  updatedBy: string;
}

export interface UpsertPartnerTypeRequest {
  partnerTypeId?: number;
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
  affectsIssuedInvoices: boolean;
  affectsReceivedInvoices: boolean;
  sortOrder: number;
}
```

### 6.2 API Client + TanStack Query hooks

```typescript
// src/api/partnerTypesApi.ts
import { apiClient } from '@/lib/apiClient';
import type { PartnerType, UpsertPartnerTypeRequest } from '@/types/partnerType';

const BASE = '/api/v1/partner-types';

export const partnerTypesApi = {
  getAll: (includeInactive = false) =>
    apiClient.get<PartnerType[]>(`${BASE}?includeInactive=${includeInactive}`),

  getById: (id: number) =>
    apiClient.get<PartnerType>(`${BASE}/${id}`),

  create: (data: UpsertPartnerTypeRequest) =>
    apiClient.post<number>(BASE, data),

  update: (id: number, data: UpsertPartnerTypeRequest) =>
    apiClient.put<void>(`${BASE}/${id}`, data),

  delete: (id: number) =>
    apiClient.delete<void>(`${BASE}/${id}`),
};
```

```typescript
// src/hooks/usePartnerTypes.ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { partnerTypesApi } from '@/api/partnerTypesApi';
import type { UpsertPartnerTypeRequest } from '@/types/partnerType';

export const PARTNER_TYPES_KEY = ['partner-types'] as const;

export function usePartnerTypes(includeInactive = false) {
  return useQuery({
    queryKey: [...PARTNER_TYPES_KEY, { includeInactive }],
    queryFn: () => partnerTypesApi.getAll(includeInactive),
    staleTime: 5 * 60 * 1000, // 5 min — date relativ stabile
  });
}

export function usePartnerTypeById(id: number) {
  return useQuery({
    queryKey: [...PARTNER_TYPES_KEY, id],
    queryFn: () => partnerTypesApi.getById(id),
    enabled: id > 0,
  });
}

export function useUpsertPartnerType() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: ({ id, data }: { id?: number; data: UpsertPartnerTypeRequest }) =>
      id ? partnerTypesApi.update(id, data) : partnerTypesApi.create(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PARTNER_TYPES_KEY });
    },
  });
}

export function useDeletePartnerType() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: number) => partnerTypesApi.delete(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PARTNER_TYPES_KEY });
    },
  });
}
```

### 6.3 Pagina principală

```tsx
// src/pages/PartnerTypesPage.tsx
import { useState } from 'react';
import { usePartnerTypes, useDeletePartnerType } from '@/hooks/usePartnerTypes';
import { PartnerTypesTable } from '@/components/partners/PartnerTypesTable';
import { PartnerTypeModal } from '@/components/partners/PartnerTypeModal';
import type { PartnerType } from '@/types/partnerType';

export default function PartnerTypesPage() {
  const [includeInactive, setIncludeInactive] = useState(false);
  const [editTarget, setEditTarget] = useState<PartnerType | null>(null);
  const [modalOpen, setModalOpen] = useState(false);

  const { data: types = [], isLoading } = usePartnerTypes(includeInactive);
  const deleteMutation = useDeletePartnerType();

  const handleAdd = () => {
    setEditTarget(null);
    setModalOpen(true);
  };

  const handleEdit = (pt: PartnerType) => {
    setEditTarget(pt);
    setModalOpen(true);
  };

  const handleDelete = (pt: PartnerType) => {
    if (pt.isSystem) return; // UI guard — logica e și în SP
    if (!confirm(`Ștergeți tipul "${pt.name}"?`)) return;
    deleteMutation.mutate(pt.partnerTypeId);
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <h1>Tipuri Parteneri</h1>
        <div className="page-actions">
          <label className="toggle-label">
            <input
              type="checkbox"
              checked={includeInactive}
              onChange={e => setIncludeInactive(e.target.checked)}
            />
            Afișează inactive
          </label>
          <button className="btn btn-primary" onClick={handleAdd}>
            + Tip nou
          </button>
        </div>
      </div>

      <PartnerTypesTable
        data={types}
        isLoading={isLoading}
        onEdit={handleEdit}
        onDelete={handleDelete}
      />

      {modalOpen && (
        <PartnerTypeModal
          partnerType={editTarget}
          onClose={() => setModalOpen(false)}
        />
      )}
    </div>
  );
}
```

### 6.4 Tabel

```tsx
// src/components/partners/PartnerTypesTable.tsx
import type { PartnerType } from '@/types/partnerType';
import { Badge } from '@/components/ui/Badge';

interface Props {
  data: PartnerType[];
  isLoading: boolean;
  onEdit: (pt: PartnerType) => void;
  onDelete: (pt: PartnerType) => void;
}

export function PartnerTypesTable({ data, isLoading, onEdit, onDelete }: Props) {
  if (isLoading) return <div className="skeleton-table" />;

  return (
    <table className="erp-table">
      <thead>
        <tr>
          <th>Cod</th>
          <th>Denumire</th>
          <th>Descriere</th>
          <th title="Facturi Emise">F. Emise</th>
          <th title="Facturi Primite">F. Primite</th>
          <th>Parteneri</th>
          <th>Status</th>
          <th>Sistem</th>
          <th>Acțiuni</th>
        </tr>
      </thead>
      <tbody>
        {data.map(pt => (
          <tr key={pt.partnerTypeId} className={!pt.isActive ? 'row-inactive' : ''}>
            <td><code>{pt.code}</code></td>
            <td>{pt.name}</td>
            <td className="text-muted">{pt.description ?? '—'}</td>
            <td className="text-center">
              {pt.affectsIssuedInvoices
                ? <Badge variant="success">✓</Badge>
                : <span className="text-muted">—</span>}
            </td>
            <td className="text-center">
              {pt.affectsReceivedInvoices
                ? <Badge variant="info">✓</Badge>
                : <span className="text-muted">—</span>}
            </td>
            <td className="text-center">{pt.usageCount}</td>
            <td>
              <Badge variant={pt.isActive ? 'success' : 'secondary'}>
                {pt.isActive ? 'Activ' : 'Inactiv'}
              </Badge>
            </td>
            <td className="text-center">
              {pt.isSystem && <Badge variant="warning">Sistem</Badge>}
            </td>
            <td>
              <button
                className="btn btn-sm btn-secondary"
                onClick={() => onEdit(pt)}
              >
                Editează
              </button>
              {!pt.isSystem && (
                <button
                  className="btn btn-sm btn-danger"
                  onClick={() => onDelete(pt)}
                  disabled={pt.usageCount > 0}
                  title={pt.usageCount > 0 ? 'Are parteneri asignați' : undefined}
                >
                  Șterge
                </button>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
```

### 6.5 Modal Upsert

```tsx
// src/components/partners/PartnerTypeModal.tsx
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useUpsertPartnerType } from '@/hooks/usePartnerTypes';
import type { PartnerType } from '@/types/partnerType';
import { Modal } from '@/components/ui/Modal';
import { FormField } from '@/components/ui/FormField';

const schema = z.object({
  code: z
    .string()
    .min(1, 'Codul este obligatoriu')
    .max(50)
    .regex(/^[A-Z0-9_]+$/, 'Doar litere mari, cifre și underscore'),
  name: z.string().min(1, 'Denumirea este obligatorie').max(100),
  description: z.string().max(500).optional(),
  isActive: z.boolean(),
  affectsIssuedInvoices: z.boolean(),
  affectsReceivedInvoices: z.boolean(),
  sortOrder: z.number().int().min(0).max(9999),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  partnerType: PartnerType | null;
  onClose: () => void;
}

export function PartnerTypeModal({ partnerType, onClose }: Props) {
  const isEdit = partnerType !== null;
  const isSystem = partnerType?.isSystem ?? false;
  const upsert = useUpsertPartnerType();

  const { register, handleSubmit, reset, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: '',
      name: '',
      description: '',
      isActive: true,
      affectsIssuedInvoices: false,
      affectsReceivedInvoices: false,
      sortOrder: 0,
    },
  });

  useEffect(() => {
    if (partnerType) {
      reset({
        code: partnerType.code,
        name: partnerType.name,
        description: partnerType.description ?? '',
        isActive: partnerType.isActive,
        affectsIssuedInvoices: partnerType.affectsIssuedInvoices,
        affectsReceivedInvoices: partnerType.affectsReceivedInvoices,
        sortOrder: partnerType.sortOrder,
      });
    }
  }, [partnerType, reset]);

  const onSubmit = async (values: FormValues) => {
    await upsert.mutateAsync({
      id: partnerType?.partnerTypeId,
      data: values,
    });
    onClose();
  };

  return (
    <Modal
      title={isEdit ? `Editare: ${partnerType.name}` : 'Tip Partener Nou'}
      onClose={onClose}
    >
      <form onSubmit={handleSubmit(onSubmit)} noValidate>
        {/* Cod — disabled pentru tipuri sistem */}
        <FormField label="Cod" error={errors.code?.message} required>
          <input
            {...register('code')}
            className="input"
            disabled={isSystem}
            placeholder="ex: CUSTOM_TYPE"
          />
          {isSystem && (
            <small className="hint">Codul tipurilor de sistem nu poate fi modificat.</small>
          )}
        </FormField>

        <FormField label="Denumire" error={errors.name?.message} required>
          <input {...register('name')} className="input" />
        </FormField>

        <FormField label="Descriere" error={errors.description?.message}>
          <textarea {...register('description')} className="input" rows={2} />
        </FormField>

        {/* Flags facturi — disabled pentru tipuri sistem */}
        <fieldset disabled={isSystem} className="fieldset">
          <legend>Impact facturare</legend>
          <label className="checkbox-label">
            <input type="checkbox" {...register('affectsIssuedInvoices')} />
            Afectează facturi emise (Client)
          </label>
          <label className="checkbox-label">
            <input type="checkbox" {...register('affectsReceivedInvoices')} />
            Afectează facturi primite (Furnizor)
          </label>
          {isSystem && (
            <small className="hint">Flags-urile de facturare nu pot fi modificate pentru tipurile de sistem.</small>
          )}
        </fieldset>

        <FormField label="Ordine afișare" error={errors.sortOrder?.message}>
          <input type="number" {...register('sortOrder', { valueAsNumber: true })} className="input" />
        </FormField>

        <label className="checkbox-label">
          <input type="checkbox" {...register('isActive')} />
          Activ
        </label>

        <div className="modal-footer">
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Anulează
          </button>
          <button type="submit" className="btn btn-primary" disabled={upsert.isPending}>
            {upsert.isPending ? 'Se salvează...' : 'Salvează'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
```

---

## 7. REGULI DE BUSINESS (Business Rules Summary)

| Regulă | Unde se aplică |
|--------|---------------|
| Tipurile cu `IsSystem = true` nu pot fi șterse | SP + API Guard |
| Codul tipurilor sistem nu poate fi modificat | SP UPDATE branch |
| Flags-urile de facturare ale tipurilor sistem sunt imuabile | SP UPDATE branch |
| Un tip cu `UsageCount > 0` nu poate fi șters (dezactivare în schimb) | SP + UI hint |
| `Code` trebuie să fie unic în tabel | SP + Validator FluentValidation |
| Codul acceptă doar majuscule, cifre, underscore | Validator + FE regex |
| Un partener poate fi asignat la **N tipuri simultan** | `PartnerTypeAssignments` (many-to-many) |
| `AffectsIssuedInvoices = true` → partenerul apare în lista clienți la emitere facturi | Business logic invoicing module |
| `AffectsReceivedInvoices = true` → partenerul apare în lista furnizori la primire facturi | Business logic invoicing module |

---

## 8. TESTE

### Unit — Application Layer

```csharp
// Tests/Application/Partners/GetPartnerTypesHandlerTests.cs
public class GetPartnerTypesHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllActiveTypes_WhenIncludeInactiveFalse()
    {
        var repo = Substitute.For<IPartnerTypeRepository>();
        repo.GetAllAsync(false, default).Returns(PartnerTypeFixtures.ActiveTypes());
        var handler = new GetPartnerTypesHandler(repo);

        var result = await handler.Handle(new GetPartnerTypesQuery(false), default);

        result.Should().HaveCount(6); // cele 6 tipuri seed
        result.Should().OnlyContain(t => t.IsActive);
    }
}
```

### Integration — API

```csharp
// Tests/Api/PartnerTypesControllerTests.cs
public class PartnerTypesControllerTests(TestWebAppFactory factory)
    : IClassFixture<TestWebAppFactory>
{
    [Fact]
    public async Task GET_PartnerTypes_Returns200WithSeededTypes()
    {
        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/v1/partner-types");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var types = await response.Content.ReadFromJsonAsync<List<PartnerTypeDto>>();
        types.Should().HaveCountGreaterThanOrEqualTo(6);
        types.Should().Contain(t => t.Code == "CLIENT");
        types.Should().Contain(t => t.Code == "VENDOR");
    }

    [Fact]
    public async Task DELETE_SystemType_Returns400()
    {
        var client = factory.CreateAuthenticatedClient();
        // CLIENT are PartnerTypeId = 1 și IsSystem = true
        var response = await client.DeleteAsync("/api/v1/partner-types/1");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

---

## 9. ÎNREGISTRARE DI (Program.cs / ServiceExtensions.cs)

```csharp
// Infrastructure
services.AddScoped<IPartnerTypeRepository, PartnerTypeRepository>();

// MediatR — dacă nu e deja înregistrat global
services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(GetPartnerTypesQuery).Assembly));

// FluentValidation
services.AddValidatorsFromAssemblyContaining<UpsertPartnerTypeValidator>();
```

---

## 10. ROUTE FE (React Router)

```tsx
// src/router/index.tsx
{
  path: '/nomenclatoare/tipuri-parteneri',
  element: <PartnerTypesPage />,
  handle: { breadcrumb: 'Tipuri Parteneri', module: 'Nomenclatoare' }
}
```

---

## NOTĂ ARHITECTURALĂ — Combinații de tipuri

Un partener primește **N tipuri** prin `PartnerTypeAssignments`.
Nu există un tabel separat de "combinații" — combinațiile sunt implicite:

```
Partner X → [CLIENT, INDIVIDUAL]   → apare în facturi emise
Partner Y → [CLIENT, VENDOR]       → apare în AMBELE liste (emise + primite)
Partner Z → [VENDOR, BANK]         → apare în facturi primite
```

La query-urile din modulul de facturare se va filtra cu:

```sql
-- Clienți (facturi emise):
SELECT DISTINCT p.*
FROM dbo.Partners p
INNER JOIN dbo.PartnerTypeAssignments pta ON pta.PartnerId = p.PartnerId
INNER JOIN dbo.PartnerTypes pt ON pt.PartnerTypeId = pta.PartnerTypeId
WHERE pt.AffectsIssuedInvoices = 1 AND p.IsActive = 1;

-- Furnizori (facturi primite):
-- ... WHERE pt.AffectsReceivedInvoices = 1 ...
```

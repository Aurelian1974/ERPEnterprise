---
name: authorization
description: >-
  Generează RBAC complet pentru ERP: definire permisiuni, policy registration,
  [Authorize(Policy)] pe Controller, permission guard în React, seeding permisiuni,
  verificare în handler pentru resource-level authorization.
---

# Authorization

## Când se aplică
Când utilizatorul cere să adauge autorizare pe un endpoint, să definească permisiuni
noi, să verifice drepturi în handler sau să implementeze un permission guard în UI.

## Model RBAC

```
Tenant
  └── User → UserRoles (many-to-many)
              └── Role → RolePermissions (many-to-many)
                         └── Permission: "finance.invoices.approve"
                                         "hr.employees.view"
                                         "inventory.stock.adjust"

Format permisiune: "{module}.{entity}.{action}"
Acțiuni standard:  view, create, update, delete, approve, export, admin
Wildcard:          "*" = toate permisiunile (super admin)
```

---

## 1. Definire permisiuni — per modul

```csharp
// Finance.Domain/Authorization/FinancePermissions.cs
public static class FinancePermissions
{
    private const string Module = "finance";

    public static class Invoices
    {
        public const string View    = $"{Module}.invoices.view";
        public const string Create  = $"{Module}.invoices.create";
        public const string Update  = $"{Module}.invoices.update";
        public const string Delete  = $"{Module}.invoices.delete";
        public const string Approve = $"{Module}.invoices.approve";
        public const string Export  = $"{Module}.invoices.export";

        public static IEnumerable<string> All =>
        [
            View, Create, Update, Delete, Approve, Export
        ];
    }

    public static class Customers
    {
        public const string View   = $"{Module}.customers.view";
        public const string Create = $"{Module}.customers.create";
        public const string Update = $"{Module}.customers.update";
        public const string Delete = $"{Module}.customers.delete";

        public static IEnumerable<string> All =>
        [
            View, Create, Update, Delete
        ];
    }

    // Toate permisiunile modulului — pentru seeding
    public static IEnumerable<string> All =>
        Invoices.All.Concat(Customers.All);
}
```

## 2. Înregistrare Policies în Program.cs

```csharp
// Shared.Infrastructure/Extensions/AuthorizationExtensions.cs
public static IServiceCollection AddErpAuthorization(this IServiceCollection services)
{
    services.AddAuthorization(options =>
    {
        // Policy per permisiune — generată dinamic
        var allPermissions = new[]
        {
            FinancePermissions.All,
            HRPermissions.All,
            InventoryPermissions.All,
        }.SelectMany(p => p);

        foreach (var permission in allPermissions)
        {
            options.AddPolicy(permission, policy =>
                policy.Requirements.Add(new PermissionRequirement(permission)));
        }
    });

    services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

    return services;
}

// Shared.Infrastructure/Auth/PermissionRequirement.cs
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

// Shared.Infrastructure/Auth/PermissionAuthorizationHandler.cs
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Permisiunile sunt în JWT claims
        var permissions = context.User
            .FindAll("permission")
            .Select(c => c.Value)
            .ToHashSet();

        if (permissions.Contains("*") ||
            permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

## 3. Utilizare în Controller

```csharp
[ApiController]
[Route("api/v1/finance/invoices")]
public sealed class InvoiceController : ControllerBase
{
    private readonly ISender _sender;

    public InvoiceController(ISender sender) => _sender = sender;

    [HttpGet]
    [Authorize(Policy = FinancePermissions.Invoices.View)]
    public async Task<IActionResult> List(
        [FromQuery] ListInvoicesQuery query,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(query, cancellationToken));

    [HttpPost]
    [Authorize(Policy = FinancePermissions.Invoices.Create)]
    public async Task<IActionResult> Create(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateInvoiceCommand(request), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, result.Value)
            : result.Error.ToActionResult();
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = FinancePermissions.Invoices.Approve)]
    public async Task<IActionResult> Approve(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ApproveInvoiceCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
```

## 4. Resource-level Authorization în Handler

```csharp
// Când nu e suficient că userul are permisiunea — trebuie verificat și ownership
internal sealed class ApproveInvoiceCommandHandler
    : IRequestHandler<ApproveInvoiceCommand, Result>
{
    private readonly IInvoiceRepository _repo;
    private readonly ICurrentUser _currentUser;

    public async Task<Result> Handle(
        ApproveInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = await _repo.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
            return Result.Failure(FinanceErrors.InvoiceNotFound(command.InvoiceId));

        // Resource-level: invoice aparține tenant-ului curent?
        // (tenant_id din SP asigură asta, dar verificare explicită în handler)
        if (invoice.TenantId != _currentUser.TenantId)
            return Result.Failure(SharedErrors.Forbidden);

        invoice.Approve(_currentUser.UserId);
        await _repo.UpdateAsync(invoice, cancellationToken);

        return Result.Success();
    }
}
```

## 5. JWT Token cu permisiuni

```csharp
// Shared.Infrastructure/Auth/JwtTokenService.cs
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;

    public string Generate(UserDto user, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("tid",                         user.TenantId.ToString()),  // tenant claim
            new("name",                        user.Name),
        };

        // Permisiunile ca claims individuale
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:   _settings.Issuer,
            audience: _settings.Audience,
            claims:   claims,
            expires:  DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

## 6. ICurrentUser

```csharp
// Shared.Infrastructure/Auth/CurrentUserService.cs
public sealed class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public Guid UserId =>
        Guid.Parse(_httpContextAccessor.HttpContext!.User
            .FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public Guid TenantId =>
        Guid.Parse(_httpContextAccessor.HttpContext!.User
            .FindFirstValue("tid")!);

    public string Email =>
        _httpContextAccessor.HttpContext!.User
            .FindFirstValue(JwtRegisteredClaimNames.Email)!;

    public bool HasPermission(string permission)
    {
        var permissions = _httpContextAccessor.HttpContext!.User
            .FindAll("permission")
            .Select(c => c.Value)
            .ToHashSet();

        return permissions.Contains("*") || permissions.Contains(permission);
    }
}
```

## 7. Seeding permisiuni în DB

```sql
-- StoredProcedures/usp_SeedPermissions.sql
CREATE OR ALTER PROCEDURE administration.usp_SeedPermissions
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Insert permisiuni dacă nu există
    INSERT INTO administration.permissions (id, tenant_id, code, module, created_at)
    SELECT NEWID(), @TenantId, p.code, p.module, SYSUTCDATETIME()
    FROM (VALUES
        ('finance.invoices.view',    'finance'),
        ('finance.invoices.create',  'finance'),
        ('finance.invoices.update',  'finance'),
        ('finance.invoices.delete',  'finance'),
        ('finance.invoices.approve', 'finance'),
        ('finance.invoices.export',  'finance'),
        ('hr.employees.view',        'hr'),
        ('hr.employees.create',      'hr')
        -- ... restul permisiunilor
    ) AS p(code, module)
    WHERE NOT EXISTS (
        SELECT 1
        FROM administration.permissions existing
        WHERE existing.tenant_id = @TenantId
          AND existing.code      = p.code
    );
END;
GO
```

## 8. Frontend — usePermission hook

```typescript
// hooks/usePermission.ts
import { useAuthStore } from '@/store/auth.store';

export const usePermission = (permission: string): boolean => {
  const permissions = useAuthStore((s) => s.permissions);
  return permissions.includes('*') || permissions.includes(permission);
};

// Utilizare în componentă
export function InvoiceActions({ invoiceId }: { invoiceId: string }) {
  const canApprove = usePermission('finance.invoices.approve');
  const canDelete  = usePermission('finance.invoices.delete');

  return (
    <div>
      {canApprove && (
        <Button onClick={() => handleApprove(invoiceId)}>Approve</Button>
      )}
      {canDelete && (
        <Button variant="destructive" onClick={() => handleDelete(invoiceId)}>
          Delete
        </Button>
      )}
    </div>
  );
}
```

## Reguli obligatorii

```
Format permisiune  — "{module}.{entity}.{action}" — niciodată freeform strings
Constants class    — {Module}Permissions static class în Domain — nu strings inline
Policy name        — identic cu permission string — niciodată mapping manual
[Authorize(Policy)]— pe FIECARE Controller action — niciodată pe controller class
tenant_id în SP    — prima linie de apărare, resource-level în handler e a doua
ICurrentUser       — injectat în handler, niciodată HttpContext direct în handler
Frontend guard     — usePermission() pe ORICE buton destructiv sau acțiune sensibilă
JWT claim "tid"    — tenant_id în fiecare token — extras de TenantContext middleware
Seeding            — permisiunile sunt seeded în DB la creare tenant — nu hardcodate în code
```

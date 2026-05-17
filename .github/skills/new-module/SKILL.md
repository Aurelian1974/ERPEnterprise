---
name: new-module
description: >-
  Scaffolează un modul ERP nou: structura de foldere, proiecte .csproj,
  IModuleInstaller, schema SQL, migration script, folder StoredProcedures.
  Modulele nu se referențiază direct — comunică prin IntegrationEvents.
---

# New ERP Module

## Când se aplică
Când utilizatorul cere un modul business nou (ex: Logistics, CRM, Payroll).

## Structura completă de generat

```
src/Modules/{Module}/
├── {Module}.Domain/
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Enums/
│   ├── DomainEvents/
│   ├── Errors/
│   │   └── {Module}Errors.cs
│   └── {Module}.Domain.csproj
│
├── {Module}.Application/
│   ├── Features/                   ← câte un folder per entitate, vertical slices
│   ├── Abstractions/
│   │   └── I{Entity}Repository.cs
│   ├── EventHandlers/              ← handlers pentru IntegrationEvents din ALTE module
│   └── {Module}.Application.csproj
│
├── {Module}.Infrastructure/
│   ├── Repositories/
│   │   └── {Entity}Repository.cs  ← apelează exclusiv SP-uri
│   ├── Database/
│   │   ├── Migrations/             ← DDL versioned (journaled de DbUp)
│   │   │   └── YYYYMMDD_001_CreateSchema{Module}.sql
│   │   └── StoredProcedures/       ← always run de DbUp, CREATE OR ALTER
│   │       └── usp_*.sql
│   ├── {Module}Module.cs           ← IModuleInstaller
│   └── {Module}.Infrastructure.csproj
│
└── {Module}.Api/
    ├── Controllers/
    └── {Module}.Api.csproj
```

## Template IModuleInstaller

```csharp
// {Module}Module.cs
public sealed class {Module}Module : IModuleInstaller
{
    public IServiceCollection Install(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof({Module}Module).Assembly));

        services.AddValidatorsFromAssembly(
            typeof({Module}Module).Assembly, includeInternalTypes: true);

        services.AddScoped<I{Entity}Repository, {Entity}Repository>();

        // DbUp rulează la startup:
        // 1. Migrations/ — journaled (rulat o singură dată)
        // 2. StoredProcedures/ — always run (rulat la fiecare deploy)
        services.RunMigrations<{Module}Module>(configuration);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app)
    {
        // Controllers înregistrați global în Program.cs via AddControllers()
        return app;
    }
}
```

## Template {Module}Errors.cs

```csharp
// {Module}.Domain/Errors/{Module}Errors.cs
public static class {Module}Errors
{
    public static Error {Entity}NotFound(Guid id) =>
        new($"{module}.{entity}.not_found",
            $"{Entity} with id '{id}' was not found.");

    public static Error {Entity}AlreadyExists(string identifier) =>
        new($"{module}.{entity}.already_exists",
            $"{Entity} '{identifier}' already exists.");

    public static Error {Entity}InvalidState(string reason) =>
        new($"{module}.{entity}.invalid_state", reason);
}
```

## Template Migration — CREATE SCHEMA

```sql
-- YYYYMMDD_001_CreateSchema{Module}.sql
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '{module_lowercase}')
    EXEC('CREATE SCHEMA {module_lowercase}');
GO
```

## Template .csproj Domain

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Shared\Shared.Kernel\Shared.Kernel.csproj" />
  </ItemGroup>
</Project>
```

## Template .csproj Infrastructure

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\{Module}.Application\{Module}.Application.csproj" />
    <ProjectReference Include="..\..\Shared\Shared.Infrastructure\Shared.Infrastructure.csproj" />
  </ItemGroup>
  <!-- SQL embedded ca resurse pentru DbUp -->
  <ItemGroup>
    <EmbeddedResource Include="Database\Migrations\*.sql" />
    <EmbeddedResource Include="Database\StoredProcedures\*.sql" />
  </ItemGroup>
</Project>
```

## Înregistrare în Program.cs

```csharp
builder.Services
    .InstallModule<FinanceModule>(builder.Configuration)
    .InstallModule<{Module}Module>(builder.Configuration); // ← adaugă aici
```

## Reguli obligatorii
- Modulul NU referențiază alte module — zero `ProjectReference` cross-modul
- Comunicare inter-modul = exclusiv `IntegrationEvents` din `Shared.Contracts`
- Repository-uri apelează exclusiv SP-uri din `Database/StoredProcedures/` — zero SQL în C#
- Primul migration = doar `CREATE SCHEMA` — tabelele vin în scripturi separate
- Connection string: NICIODATĂ în cod sau fișiere comise — vine din user-secrets / env vars
- `{Module}Errors` static class în Domain — nu strings hardcodate în handlers

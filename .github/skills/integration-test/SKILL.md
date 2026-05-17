---
name: integration-test
description: >-
  Generează integration tests pentru ERP: SQL Server LocalDB cu DbUp migrations,
  fixture per modul, seed data, testare handler stack complet (DB real).
  xUnit, FluentAssertions, fără mocking pentru repository.
---

# Integration Test

## Când se aplică
Când utilizatorul cere teste care verifică stack-ul complet al unui feature:
handler → repository → SP → SQL Server LocalDB real (nu mockit).

## Diferența față de Unit Tests

```
Unit Test         — repository mockit (NSubstitute), zero IO, rapid
Integration Test  — repository real, SQL Server LocalDB, DbUp migrations, seed data
                    Testează că SP-urile și query-urile funcționează corect
```

## Structura proiecte

```
tests/Integration/
  {Module}.Integration.Tests/
    Fixtures/
      {Module}ModuleFixture.cs    ← IAsyncLifetime, LocalDB, DbUp, DI container
      SeedData.cs                  ← date de test predefinite
    Features/
      Invoices/
        CreateInvoiceTests.cs
        ApproveInvoiceTests.cs
        ListInvoicesTests.cs
    Shared/
      Builders/
        InvoiceBuilder.cs         ← același builder ca în Unit Tests
```

---

## 1. Module Fixture

```csharp
// {Module}.Integration.Tests/Fixtures/{Module}ModuleFixture.cs
public sealed class FinanceModuleFixture : IAsyncLifetime
{
    private readonly string _dbName = $"ErpIntTest_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=(localdb)\\mssqllocaldb;Database={_dbName};" +
        $"Trusted_Connection=True;MultipleActiveResultSets=True;";

    public ISender Sender { get; private set; } = null!;
    public IInvoiceRepository InvoiceRepository { get; private set; } = null!;
    public SeedData SeedData { get; private set; } = null!;
    public Guid DefaultTenantId { get; } = Guid.NewGuid();
    public Guid DefaultUserId   { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        // 1. Rulează DbUp — creează schema și SP-urile
        RunMigrations();

        // 2. Seed date de test
        SeedData = await SeedTestDataAsync();

        // 3. Build DI container cu implementări reale
        var services = new ServiceCollection();
        ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        Sender             = provider.GetRequiredService<ISender>();
        InvoiceRepository  = provider.GetRequiredService<IInvoiceRepository>();
    }

    private void RunMigrations()
    {
        // Migrations (DDL — journaled)
        var migrationUpgrader = DeployChanges.To
            .SqlDatabase(ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(FinanceModule).Assembly,
                s => s.Contains(".Migrations."))
            .WithTransaction()
            .LogToConsole()
            .Build();

        var result = migrationUpgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException(
                $"Migration failed: {result.Error}");

        // Stored Procedures (always run — idempotente)
        var spUpgrader = DeployChanges.To
            .SqlDatabase(ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(FinanceModule).Assembly,
                s => s.Contains(".StoredProcedures."))
            .JournalTo(new NullJournal())   // always run, nu journaled
            .WithTransaction()
            .LogToConsole()
            .Build();

        var spResult = spUpgrader.PerformUpgrade();
        if (!spResult.Successful)
            throw new InvalidOperationException(
                $"SP deployment failed: {spResult.Error}");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Tenant context fix pentru teste
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(DefaultTenantId);
        tenantContext.UserId.Returns(DefaultUserId);
        services.AddSingleton(tenantContext);

        // Connection factory cu LocalDB
        services.AddSingleton<IDbConnectionFactory>(
            new SqlConnectionFactory(ConnectionString));

        // Repository-uri reale
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        // MediatR
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(FinanceModule).Assembly));

        // FluentValidation
        services.AddValidatorsFromAssembly(
            typeof(FinanceModule).Assembly, includeInternalTypes: true);

        // Pipeline behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // Logging
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    }

    private async Task<SeedData> SeedTestDataAsync()
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Seed customer
        var customerId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "finance.usp_CreateCustomer",
            new
            {
                Id       = customerId,
                TenantId = DefaultTenantId,
                Name     = "Test Customer SRL",
                Cui      = "RO12345678",
                CreatedBy = DefaultUserId
            },
            commandType: CommandType.StoredProcedure);

        return new SeedData
        {
            CustomerId = customerId,
        };
    }

    public async Task DisposeAsync()
    {
        // Drop DB după fiecare test class
        using var conn = new SqlConnection(
            "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True;");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            $"IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '{_dbName}')" +
            $" BEGIN ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" +
            $" DROP DATABASE [{_dbName}]; END");
    }
}

// SeedData.cs
public sealed class SeedData
{
    public Guid CustomerId { get; init; }
}
```

## 2. Test Class cu Fixture

```csharp
// {Module}.Integration.Tests/Features/Invoices/CreateInvoiceTests.cs
// IClassFixture = fixture creat O DATĂ per test class, nu per test
public sealed class CreateInvoiceTests : IClassFixture<FinanceModuleFixture>
{
    private readonly FinanceModuleFixture _fixture;

    public CreateInvoiceTests(FinanceModuleFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Handle_ValidCommand_CreatesInvoiceInDb()
    {
        // Arrange
        var command = new CreateInvoiceCommand(
            CustomerId:    _fixture.SeedData.CustomerId,
            InvoiceNumber: $"INV-TEST-{Guid.NewGuid():N}"[..15],
            DueDate:       DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Lines: [new InvoiceLineDto(
                ProductId:   Guid.NewGuid(),
                Description: "Integration test product",
                Quantity:    2m,
                UnitPrice:   150m,
                VatRate:     0.19m)]);

        // Act
        var result = await _fixture.Sender.Send(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        // Verifică în DB că entitatea există
        var saved = await _fixture.InvoiceRepository
            .GetByIdAsync(result.Value, CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Status.Should().Be(1);           // Draft
        saved.TotalAmount.Should().Be(300m);    // 2 * 150
    }

    [Fact]
    public async Task Handle_DuplicateInvoiceNumber_ReturnsFailure()
    {
        // Arrange — creăm prima factură
        var invoiceNumber = $"DUP-{Guid.NewGuid():N}"[..15];
        var firstCommand  = BuildCommand(invoiceNumber);
        await _fixture.Sender.Send(firstCommand);

        // Act — a doua factură cu același număr
        var secondCommand = BuildCommand(invoiceNumber);
        var result = await _fixture.Sender.Send(secondCommand);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("already_exists");
    }

    [Fact]
    public async Task Handle_EmptyLines_ReturnsValidationError()
    {
        // Arrange
        var command = new CreateInvoiceCommand(
            CustomerId:    _fixture.SeedData.CustomerId,
            InvoiceNumber: "INV-NOLINES",
            DueDate:       DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Lines:         []);   // empty — validation trebuie să prindă

        // Act
        var act = () => _fixture.Sender.Send(command);

        // Assert — ValidationBehavior aruncă ValidationException
        await act.Should().ThrowAsync<ValidationException>();
    }

    private CreateInvoiceCommand BuildCommand(string invoiceNumber) =>
        new(
            CustomerId:    _fixture.SeedData.CustomerId,
            InvoiceNumber: invoiceNumber,
            DueDate:       DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Lines: [new InvoiceLineDto(
                ProductId:   Guid.NewGuid(),
                Description: "Test",
                Quantity:    1m,
                UnitPrice:   100m,
                VatRate:     0.19m)]);
}
```

## 3. Collection Fixture (fixture shared între mai multe test classes)

```csharp
// Shared între toate test classes din modul — DB creat O SINGURĂ DATĂ
[CollectionDefinition("Finance Integration")]
public sealed class FinanceIntegrationCollection
    : ICollectionFixture<FinanceModuleFixture> { }

// Test class cu [Collection]
[Collection("Finance Integration")]
public sealed class ApproveInvoiceTests
{
    private readonly FinanceModuleFixture _fixture;

    public ApproveInvoiceTests(FinanceModuleFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Handle_DraftInvoiceWithLines_ApprovesSuccessfully()
    {
        // Arrange — creăm o factură draft
        var createResult = await _fixture.Sender.Send(
            new CreateInvoiceCommand(/* ... */));

        createResult.IsSuccess.Should().BeTrue();

        // Act
        var approveResult = await _fixture.Sender.Send(
            new ApproveInvoiceCommand(createResult.Value));

        // Assert
        approveResult.IsSuccess.Should().BeTrue();

        var invoice = await _fixture.InvoiceRepository
            .GetByIdAsync(createResult.Value, CancellationToken.None);

        invoice!.Status.Should().Be(2);   // Approved
    }
}
```

## 4. xUnit.runner.json — configurare timeout și paralelism

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "methodDisplay": "classAndMethod",
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false,
  "maxParallelThreads": 1
}
```

> `parallelizeTestCollections: false` — evită conflicte pe LocalDB când
> mai multe fixtures rulează simultan.

## Reguli obligatorii

```
IClassFixture   — fixture per test class (DB creat o dată per class)
ICollectionFixture — fixture shared între mai multe test classes (DB creat o dată)
Fără mocking    — repository-uri reale în integration tests
DbUp în fixture — migrări DDL + SP-uri înainte de orice test
Dispose         — DROP DATABASE în DisposeAsync — nu lăsa DB-uri de test
Seed data       — date minimale necesare în SeedTestDataAsync
InvoiceNumber   — unic per test — folosește Guid în număr (evită conflicte)
Paralelism off  — LocalDB nu suportă bine accesul paralel de la mai multe fixture-uri
Assertions      — verifică în DB după operație, nu doar result.IsSuccess
```

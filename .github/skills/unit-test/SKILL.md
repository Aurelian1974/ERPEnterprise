---
name: unit-test
description: >-
  Generează unit tests pentru ERP: Domain layer (entități, value objects,
  invariante), Application layer (command/query handlers cu repository mockuit).
  xUnit, FluentAssertions, NSubstitute. Fără IO, fără DB, fără HTTP.
---

# Unit Test

## Când se aplică
Când utilizatorul cere teste pentru logică Domain sau Application:
entități, value objects, invariante business, command handlers, query handlers.

## Stack

```
xUnit               — test runner
FluentAssertions    — assertions expresive
NSubstitute         — mocking (preferabil Moq alternativă)
Bogus               — generare date de test realiste (opțional)
```

## Structura proiecte

```
tests/Unit/
  {Module}.Domain.Tests/
    Entities/
      InvoiceTests.cs
      InvoiceLineTests.cs
    ValueObjects/
      MoneyTests.cs
      InvoiceNumberTests.cs
  {Module}.Application.Tests/
    Features/
      Invoices/
        Create/
          CreateInvoiceCommandHandlerTests.cs
        Approve/
          ApproveInvoiceCommandHandlerTests.cs
        List/
          ListInvoicesQueryHandlerTests.cs
    Shared/
      Builders/                    ← Builder pattern pentru date de test
        InvoiceBuilder.cs
```

---

## Template — Domain Entity Tests

```csharp
// {Module}.Domain.Tests/Entities/InvoiceTests.cs
public sealed class InvoiceTests
{
    // Naming: {Method}_{Condition}_{ExpectedBehavior}
    [Fact]
    public void Create_ValidData_ReturnsInvoiceInDraftStatus()
    {
        // Arrange
        var tenantId    = Guid.NewGuid();
        var customerId  = Guid.NewGuid();
        var number      = InvoiceNumber.From("INV-2026-001");
        var dueDate     = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var createdBy   = Guid.NewGuid();

        // Act
        var invoice = Invoice.Create(tenantId, customerId, number, dueDate, createdBy);

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.TenantId.Should().Be(tenantId);
        invoice.CustomerId.Should().Be(customerId);
        invoice.TotalAmount.Should().Be(Money.Zero);
        invoice.Id.Should().NotBe(Guid.Empty);
        invoice.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Create_PastDueDate_ThrowsDomainException()
    {
        // Arrange
        var pastDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        // Act
        var act = () => Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            InvoiceNumber.From("INV-001"),
            pastDueDate,
            Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage(FinanceErrors.InvalidDueDate.Description);
    }

    [Fact]
    public void Approve_DraftInvoiceWithLines_ChangesStatusAndRaisesDomainEvent()
    {
        // Arrange
        var invoice = InvoiceBuilder.Default().Build();
        invoice.AddLine(InvoiceLineBuilder.Default().Build());

        // Act
        invoice.Approve(Guid.NewGuid());

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.Approved);
        invoice.DomainEvents.Should().ContainSingle()
               .Which.Should().BeOfType<InvoiceApprovedDomainEvent>();
    }

    [Fact]
    public void Approve_InvoiceWithNoLines_ThrowsDomainException()
    {
        // Arrange
        var invoice = InvoiceBuilder.Default().Build();

        // Act
        var act = () => invoice.Approve(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage(FinanceErrors.InvoiceHasNoLines.Description);
    }

    [Fact]
    public void Approve_AlreadyApprovedInvoice_ThrowsDomainException()
    {
        // Arrange
        var invoice = InvoiceBuilder.Default().WithStatus(InvoiceStatus.Approved).Build();

        // Act
        var act = () => invoice.Approve(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(InvoiceStatus.Approved)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void AddLine_NonDraftInvoice_ThrowsDomainException(InvoiceStatus status)
    {
        // Arrange
        var invoice = InvoiceBuilder.Default().WithStatus(status).Build();
        var line    = InvoiceLineBuilder.Default().Build();

        // Act
        var act = () => invoice.AddLine(line);

        // Assert
        act.Should().Throw<DomainException>()
           .WithMessage(FinanceErrors.InvoiceNotDraft.Description);
    }
}
```

## Template — Value Object Tests

```csharp
// {Module}.Domain.Tests/ValueObjects/MoneyTests.cs
public sealed class MoneyTests
{
    [Fact]
    public void From_ValidAmount_CreatesMoney()
    {
        var money = Money.From(100.50m, "RON");

        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("RON");
    }

    [Fact]
    public void From_NegativeAmount_ThrowsDomainException()
    {
        var act = () => Money.From(-1, "RON");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Add_SameCurrency_ReturnsSummedMoney()
    {
        var a = Money.From(100, "RON");
        var b = Money.From(50,  "RON");

        var result = a.Add(b);

        result.Amount.Should().Be(150);
        result.Currency.Should().Be("RON");
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsDomainException()
    {
        var ron = Money.From(100, "RON");
        var eur = Money.From(50,  "EUR");

        var act = () => ron.Add(eur);
        act.Should().Throw<DomainException>()
           .WithMessage(SharedErrors.CurrencyMismatch.Description);
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var a = Money.From(100, "RON");
        var b = Money.From(100, "RON");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentAmount_AreNotEqual()
    {
        var a = Money.From(100, "RON");
        var b = Money.From(200, "RON");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }
}
```

## Template — Command Handler Tests

```csharp
// {Module}.Application.Tests/Features/Invoices/Create/CreateInvoiceCommandHandlerTests.cs
public sealed class CreateInvoiceCommandHandlerTests
{
    // NSubstitute — mock repository și dependencies
    private readonly IInvoiceRepository _repo = Substitute.For<IInvoiceRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly CreateInvoiceCommandHandler _handler;

    public CreateInvoiceCommandHandlerTests()
    {
        _currentUser.TenantId.Returns(Guid.NewGuid());
        _currentUser.UserId.Returns(Guid.NewGuid());
        _handler = new CreateInvoiceCommandHandler(_repo, _currentUser);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithInvoiceId()
    {
        // Arrange
        var command = new CreateInvoiceCommand(
            CustomerId:    Guid.NewGuid(),
            InvoiceNumber: "INV-2026-001",
            DueDate:       DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Lines: [new InvoiceLineDto(
                ProductId:   Guid.NewGuid(),
                Description: "Product A",
                Quantity:    2,
                UnitPrice:   100m,
                VatRate:     0.19m)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        // Verifică că repository-ul a fost apelat cu entitatea corectă
        await _repo.Received(1).InsertAsync(
            Arg.Is<Invoice>(i =>
                i.TenantId == _currentUser.TenantId &&
                i.Status   == InvoiceStatus.Draft),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PastDueDate_ReturnsFailure()
    {
        // Arrange
        var command = new CreateInvoiceCommand(
            CustomerId:    Guid.NewGuid(),
            InvoiceNumber: "INV-2026-001",
            DueDate:       DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Lines: []);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("finance.invoice.invalid_due_date");

        // Repository NU trebuie apelat
        await _repo.DidNotReceive().InsertAsync(
            Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
    }
}
```

## Template — Query Handler Tests

```csharp
// {Module}.Application.Tests/Features/Invoices/List/ListInvoicesQueryHandlerTests.cs
public sealed class ListInvoicesQueryHandlerTests
{
    private readonly IInvoiceRepository _repo = Substitute.For<IInvoiceRepository>();
    private readonly ITenantContext _tenant    = Substitute.For<ITenantContext>();
    private readonly ListInvoicesQueryHandler _handler;

    public ListInvoicesQueryHandlerTests()
    {
        _tenant.TenantId.Returns(Guid.NewGuid());
        _handler = new ListInvoicesQueryHandler(_repo, _tenant);
    }

    [Fact]
    public async Task Handle_ReturnsPagedResult()
    {
        // Arrange
        var expectedItems = new List<InvoiceListDto>
        {
            new(Id: Guid.NewGuid(), InvoiceNumber: "INV-001",
                Status: 1, TotalCount: 2),
            new(Id: Guid.NewGuid(), InvoiceNumber: "INV-002",
                Status: 2, TotalCount: 2),
        };

        _repo.ListAsync(Arg.Any<ListInvoicesQuery>(), Arg.Any<CancellationToken>())
             .Returns(new PagedResult<InvoiceListDto>(expectedItems, 2, 1, 25));

        var query = new ListInvoicesQuery(Page: 1, PageSize: 25);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
        result.Value.Page.Should().Be(1);
    }
}
```

## Template — Builder pattern pentru test data

```csharp
// tests/Unit/{Module}.Domain.Tests/Shared/Builders/InvoiceBuilder.cs
public sealed class InvoiceBuilder
{
    private Guid           _tenantId   = Guid.NewGuid();
    private Guid           _customerId = Guid.NewGuid();
    private InvoiceNumber  _number     = InvoiceNumber.From("INV-TEST-001");
    private DateOnly       _dueDate    = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
    private Guid           _createdBy  = Guid.NewGuid();
    private InvoiceStatus  _status     = InvoiceStatus.Draft;

    public static InvoiceBuilder Default() => new();

    public InvoiceBuilder WithTenantId(Guid tenantId)
        { _tenantId = tenantId; return this; }

    public InvoiceBuilder WithCustomerId(Guid customerId)
        { _customerId = customerId; return this; }

    public InvoiceBuilder WithStatus(InvoiceStatus status)
        { _status = status; return this; }

    public Invoice Build()
    {
        var invoice = Invoice.Create(_tenantId, _customerId, _number, _dueDate, _createdBy);

        // Forțează status pentru teste (reflection dacă nu există setter intern)
        if (_status != InvoiceStatus.Draft)
        {
            typeof(Invoice)
                .GetProperty(nameof(Invoice.Status))!
                .SetValue(invoice, _status);
        }

        invoice.ClearDomainEvents();   // reset după Create
        return invoice;
    }
}
```

## Reguli obligatorii

```
Naming test     — {Method}_{Condition}_{ExpectedBehavior}
Structură       — Arrange / Act / Assert, separate vizibil
No IO           — niciun apel DB, HTTP, File System în unit tests
NSubstitute     — mock orice dependency externă (repository, current user)
FluentAssertions — nu Assert.Equal(), folosește .Should().Be()
Builder pattern — pentru entități complexe, nu new() inline cu 10 parametri
Domain tests    — testează invariante, nu getteri
Handler tests   — verifică că repository-ul e apelat corect (Received/DidNotReceive)
Result<T>       — testează atât IsSuccess cât și IsFailure + Error.Code
ClearDomainEvents() — în builder după construire, dacă testezi events separat
```

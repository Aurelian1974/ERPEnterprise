using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Tests;

// Cereri de test pentru pipeline: aceleasi atribute ca in modulele reale (NET-04).

[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
[Audited("Core.Test.Ping")]
public sealed record PingCommand(string Text, bool Fail = false) : ICommand<PingResult>;

public sealed record PingResult(Guid Id, string Text);

public sealed record PingHappened(string Text) : IntegrationEvent;

public sealed class PingCommandValidator : AbstractValidator<PingCommand>
{
    public PingCommandValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(20);
}

/// <summary>Scrie un eveniment in outbox si descrie audit-ul; cu Fail = true arunca dupa scriere (proba rollback-ului, P8).</summary>
public sealed class PingCommandHandler(IOutbox outbox, IAuditScope audit, HandlerCalls calls) : ICommandHandler<PingCommand, PingResult>
{
    public async Task<PingResult> Handle(PingCommand request, CancellationToken cancellationToken)
    {
        calls.Count++;
        await outbox.EnqueueAsync(new PingHappened(request.Text), cancellationToken);
        audit.Describe("Core.Test", 42, new { request.Text });
        if (request.Fail)
        {
            throw ErpException.Conflict("CORE_TEST_FAILED", "Eșec simulat după scriere.");
        }

        return new PingResult(Guid.NewGuid(), request.Text);
    }
}

[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
public sealed record OtherCommand : ICommand<int>;

public sealed class OtherCommandHandler : ICommandHandler<OtherCommand, int>
{
    public Task<int> Handle(OtherCommand request, CancellationToken cancellationToken) => Task.FromResult(7);
}

[Module("Core")]
[RequiresPermission("Core.Settings.Manage")]
public sealed record PostDocument(DateOnly AccountingDate) : ICommand<bool>, IAccountingDated;

public sealed class PostDocumentHandler(HandlerCalls calls) : ICommandHandler<PostDocument, bool>
{
    public Task<bool> Handle(PostDocument request, CancellationToken cancellationToken)
    {
        calls.Count++;
        return Task.FromResult(true);
    }
}

[Module("Sales")]
[RequiresPermission("Sales.Invoice.View")]
public sealed record GetInvoice(int Id) : IQuery<int>;

public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, int>
{
    public Task<int> Handle(GetInvoice request, CancellationToken cancellationToken) => Task.FromResult(request.Id);
}

[Module("Core")]
[RequiresPermission("Core.Partner.View")]
public sealed record ListThings : IQuery<int>;

public sealed class ListThingsHandler(HandlerCalls calls) : IQueryHandler<ListThings, int>
{
    public Task<int> Handle(ListThings request, CancellationToken cancellationToken)
    {
        calls.Count++;
        return Task.FromResult(3);
    }
}

/// <summary>Cerere fara permisiune declarata: refuzata (fail closed).</summary>
[Module("Core")]
public sealed record Undeclared : IQuery<int>;

public sealed class UndeclaredHandler : IQueryHandler<Undeclared, int>
{
    public Task<int> Handle(Undeclared request, CancellationToken cancellationToken) => Task.FromResult(1);
}

/// <summary>Numara apelurile handler-elor de test (scoped).</summary>
public sealed class HandlerCalls
{
    public int Count { get; set; }
}

public sealed class FakeContext : ICurrentContext, Erp.Core.Application.Me.ICurrentUserProfile
{
    public bool IsAuthenticated { get; set; } = true;

    public int UserId { get; set; } = 1;

    public int CompanyId { get; set; } = 1;

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public string? IdempotencyKey { get; set; }

    public string? IpAddress { get; set; } = "127.0.0.1";

    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "Core.Settings.Manage", "Core.Partner.View" };

    public bool HasPermission(string permission, DimensionRef? scope = null) => IsAuthenticated && Permissions.Contains(permission);

    public string UserName => "test";

    public string DisplayName => "Utilizator test";

    public string Language => "ro";

    IReadOnlyCollection<string> Erp.Core.Application.Me.ICurrentUserProfile.Permissions => Permissions;
}

public sealed class FakeLicense : ILicenseInfo
{
    public HashSet<string> Modules { get; } = new(StringComparer.Ordinal) { "Core" };

    public DateOnly ValidUntil { get; set; } = new(2099, 12, 31);

    public string? CustomerName { get; set; } = "Grupul Exemplu";

    public LicenseState State { get; set; } = LicenseState.Valid;

    public bool IsModuleActive(string moduleCode) => Modules.Contains(moduleCode);

    public IReadOnlyCollection<string> ActiveModules => Modules;

    public int? GetLimit(string limitCode) => null;
}

public sealed class FakePeriods : IPeriodLock
{
    public HashSet<DateOnly> ClosedMonths { get; } = [];

    public Task<bool> IsClosedAsync(int companyId, DateOnly date, CancellationToken ct) =>
        Task.FromResult(ClosedMonths.Contains(new DateOnly(date.Year, date.Month, 1)));
}

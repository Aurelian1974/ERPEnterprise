using Erp.Core.Application;
using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Pipeline;

/// <summary>Pipeline-ul comun (02.01, 4.1) cu dubluri in memorie: refuzurile se produc inainte de handler, in ordinea documentata.</summary>
public sealed class PipelineTests
{
    private readonly FakeContext context = new();
    private readonly FakeLicense license = new();
    private readonly FakePeriods periods = new();
    private readonly FakeTransient transient = new();
    private readonly FakeLicenseFeature licenseFeature = new();
    private readonly FakeUnitOfWork unitOfWork;

    public PipelineTests() => unitOfWork = new FakeUnitOfWork(transient);

    private FakeUnitOfWork audit => unitOfWork;

    private (ISender Sender, HandlerCalls Calls) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(PipelineTests).Assembly);
        services.AddSingleton<ICurrentContext>(context);
        services.AddSingleton<Erp.Core.Application.Me.ICurrentUserProfile>(context);
        services.AddSingleton<ILicenseInfo>(license);
        services.AddSingleton<Erp.Core.Application.Licensing.ILicenseSnapshot>(licenseFeature);
        services.AddSingleton<Erp.Core.Application.Licensing.ILicenseStore>(licenseFeature);
        services.AddSingleton<Erp.Core.Application.Licensing.ILicenseRefresh>(licenseFeature);
        services.AddSingleton<Erp.Licensing.ILicenseReader>(new Erp.Licensing.LicenseReader(new Erp.Licensing.LicenseKeyRing([]), TimeProvider.System));
        services.AddSingleton<Erp.Core.Application.Organization.IOrganizationStore>(new FakeOrganizationStore());
        services.AddFakePartnerServices();
        services.AddSingleton<IPeriodLock>(periods);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<IIdempotencyStore>(unitOfWork);
        services.AddSingleton<IOutbox>(unitOfWork);
        services.AddSingleton<IAuditWriter>(unitOfWork);
        services.AddSingleton<ITransientFailureDetector>(transient);
        services.AddSingleton<ISqlErrorTranslator, NoSqlErrors>();
        services.AddScoped<HandlerCalls>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<ISender>(), scope.ServiceProvider.GetRequiredService<HandlerCalls>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Criteriu", "02.01-A4")]
    public async Task Command_without_permission_is_refused_with_403_before_the_handler()
    {
        context.Permissions.Clear();
        var (sender, calls) = Build();

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PingCommand("x"), Ct));

        Assert.Equal(ErpErrorKind.Forbidden, ex.Kind);
        Assert.Equal(CoreErrorCodes.Forbidden, ex.Code);
        Assert.Equal(0, calls.Count);
        Assert.Equal(0, unitOfWork.Begun);
    }

    [Fact]
    public async Task Unauthenticated_request_is_refused_with_401_and_undeclared_permission_fails_closed()
    {
        var (sender, _) = Build();

        var undeclared = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new Undeclared(), Ct));
        context.IsAuthenticated = false;
        var anonymous = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new ListThings(), Ct));

        Assert.Equal(CoreErrorCodes.Forbidden, undeclared.Code);
        Assert.Equal(ErpErrorKind.Unauthenticated, anonymous.Kind);
    }

    [Fact]
    [Trait("Criteriu", "02.01-A5")]
    public async Task Command_dated_in_a_closed_period_is_refused_with_409_before_the_transaction()
    {
        periods.ClosedMonths.Add(new DateOnly(2026, 10, 1));
        var (sender, calls) = Build();

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PostDocument(new DateOnly(2026, 10, 31)), Ct));
        var open = await sender.Send(new PostDocument(new DateOnly(2026, 11, 1)), Ct);

        Assert.Equal(ErpErrorKind.Conflict, ex.Kind);
        Assert.Equal(CoreErrorCodes.PeriodClosed, ex.Code);
        Assert.True(open);
        Assert.Equal(1, calls.Count);
        Assert.Equal(1, unitOfWork.Begun);
    }

    [Fact]
    [Trait("Criteriu", "02.01-A17")]
    public async Task Read_only_license_refuses_commands_but_allows_queries()
    {
        license.State = LicenseState.ReadOnly;
        var (sender, calls) = Build();

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PingCommand("x"), Ct));
        var result = await sender.Send(new ListThings(), Ct);

        Assert.Equal(ErpErrorKind.Forbidden, ex.Kind);
        Assert.Equal(CoreErrorCodes.LicenseReadOnly, ex.Code);
        Assert.Equal(3, result);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task Command_allowed_when_read_only_runs_so_a_new_license_can_be_loaded()
    {
        license.State = LicenseState.ReadOnly;
        var (sender, _) = Build();

        Assert.Equal(11, await sender.Send(new UnlockCommand(), Ct));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Company_missing_from_the_license_is_read_only()
    {
        context.CompanyId = 5;
        license.UnlicensedCompanies.Add(5);
        var (sender, calls) = Build();

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PingCommand("x"), Ct));
        var result = await sender.Send(new ListThings(), Ct);

        Assert.Equal(ErpErrorKind.Forbidden, ex.Kind);
        Assert.Equal(CoreErrorCodes.CompanyNotLicensed, ex.Code);
        Assert.Equal(3, result);
        Assert.Equal(1, calls.Count);
    }

    [Fact]
    public async Task Request_of_an_unlicensed_module_is_refused_with_403()
    {
        context.Permissions.Add("Sales.Invoice.View");
        var (sender, _) = Build();

        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new GetInvoice(5), Ct));
        license.Modules.Add("Sales");
        var (licensed, _) = Build();

        Assert.Equal(CoreErrorCodes.ModuleNotLicensed, ex.Code);
        Assert.Equal(5, await licensed.Send(new GetInvoice(5), Ct));
    }

    [Fact]
    public async Task Invalid_command_returns_field_errors_in_camel_case_without_opening_a_transaction()
    {
        var (sender, calls) = Build();

        var ex = await Assert.ThrowsAsync<ErpValidationException>(() => sender.Send(new PingCommand(""), Ct));

        Assert.Equal(CoreErrorCodes.Validation, ex.Code);
        Assert.True(ex.Errors.ContainsKey("text"));
        Assert.Equal(0, calls.Count);
        Assert.Equal(0, unitOfWork.Begun);
    }

    [Fact]
    public async Task Successful_command_commits_once_with_audit_written_inside_the_transaction()
    {
        var (sender, _) = Build();

        await sender.Send(new PingCommand("ok"), Ct);

        Assert.Equal((1, 1, 0), (unitOfWork.Begun, unitOfWork.Committed, unitOfWork.RolledBack));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(("Core.Test.Ping", "Core.Test", 42L), (entry.Record.Action, entry.Record.EntityType, entry.Record.EntityId));
        Assert.True(entry.InTransaction);
    }

    [Fact]
    public async Task Failing_handler_rolls_back_and_writes_no_audit()
    {
        var (sender, _) = Build();

        await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PingCommand("x", Fail: true), Ct));

        Assert.Equal((1, 0, 1), (unitOfWork.Begun, unitOfWork.Committed, unitOfWork.RolledBack));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Deadlock_is_retried_three_times_then_reported_as_busy()
    {
        transient.FailuresLeft = 2;
        var (sender, calls) = Build();

        await sender.Send(new PingCommand("retry"), Ct);
        Assert.Equal(3, calls.Count);
        Assert.Single(audit.Entries);

        transient.FailuresLeft = 10;
        var ex = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new PingCommand("busy"), Ct));
        Assert.Equal(CoreErrorCodes.ResourceBusy, ex.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.01-A16")]
    public async Task Same_idempotency_key_returns_the_first_result_without_running_the_handler_again()
    {
        context.IdempotencyKey = "form-1";
        var (sender, calls) = Build();

        var first = await sender.Send(new PingCommand("once"), Ct);
        var second = await sender.Send(new PingCommand("once"), Ct);
        var reused = await Assert.ThrowsAsync<ErpException>(() => sender.Send(new OtherCommand(), Ct));

        Assert.Equal(first, second);
        Assert.Equal(1, calls.Count);
        Assert.Single(unitOfWork.Events);
        Assert.Equal(CoreErrorCodes.IdempotencyKeyReused, reused.Code);
    }

    internal sealed class FakeUnitOfWork(FakeTransient transient) : IUnitOfWork, IIdempotencyStore, IOutbox, IAuditWriter
    {
        private readonly Dictionary<string, IdempotentResult> committed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IdempotentResult> pending = new(StringComparer.Ordinal);
        private readonly List<IntegrationEvent> pendingEvents = [];
        private readonly List<(AuditRecord Record, bool InTransaction)> pendingAudit = [];

        public List<(AuditRecord Record, bool InTransaction)> Entries { get; } = [];

        public bool InTransaction { get; private set; }

        public int Begun { get; private set; }

        public int Committed { get; private set; }

        public int RolledBack { get; private set; }

        public List<IntegrationEvent> Events { get; } = [];

        public Task BeginAsync(CancellationToken ct)
        {
            Begun++;
            InTransaction = true;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct)
        {
            if (transient.FailuresLeft > 0)
            {
                transient.FailuresLeft--;
                throw new TransientException();
            }

            Committed++;
            InTransaction = false;
            foreach (var (key, value) in pending)
            {
                committed[key] = value;
            }

            Events.AddRange(pendingEvents);
            Entries.AddRange(pendingAudit);
            pending.Clear();
            pendingEvents.Clear();
            pendingAudit.Clear();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            if (InTransaction)
            {
                RolledBack++;
            }

            InTransaction = false;
            pending.Clear();
            pendingEvents.Clear();
            pendingAudit.Clear();
            return Task.CompletedTask;
        }

        public Task WriteAsync(AuditRecord record, CancellationToken ct)
        {
            pendingAudit.Add((record, InTransaction));
            return Task.CompletedTask;
        }

        public Task<IdempotentResult?> ClaimAsync(int userId, string key, string requestType, CancellationToken ct)
        {
            if (committed.TryGetValue(key, out var existing))
            {
                return Task.FromResult<IdempotentResult?>(existing);
            }

            pending[key] = new IdempotentResult(requestType, null);
            return Task.FromResult<IdempotentResult?>(null);
        }

        public Task CompleteAsync(int userId, string key, string resultJson, CancellationToken ct)
        {
            pending[key] = pending[key] with { ResultJson = resultJson };
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(IntegrationEvent evt, CancellationToken ct)
        {
            pendingEvents.Add(evt);
            return Task.CompletedTask;
        }
    }

    // FakeUnitOfWork arunca TransientException la commit de FailuresLeft ori (ca un deadlock).
    internal sealed class FakeTransient : ITransientFailureDetector
    {
        public int FailuresLeft { get; set; }

        public bool IsTransient(Exception exception) => exception is TransientException;
    }

    private sealed class TransientException : Exception;

    internal sealed class NoSqlErrors : ISqlErrorTranslator
    {
        public ErpException? Translate(Exception exception) => null;
    }
}

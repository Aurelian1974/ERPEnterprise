using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Erp.Finance.Infrastructure.Treasury;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.Finance.Tests.Sql;

/// <summary>Ceas fix pentru jobul de la miezul noptii (F1-A10).</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Registrul de casa pe SQL Server real (02.02, F1 3.4 si 4.2; criteriile F1-A6, A7, A10): inchiderea in ordine si fara ciorne, paginile
/// numerotate continuu, evenimentul RegistruCasaInchis, avertizarea de sold, redeschiderea cu motiv si jobul de inchidere automata.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CashRegisterIntegrationTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public CashRegisterIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.CashView, FinancePermissions.CashCreate, FinancePermissions.CashValidate, FinancePermissions.CashCloseDay]);
    }

    private async Task<int> NewCash(CashCloseMode mode = CashCloseMode.OnDemand, string? maxBalance = null) =>
        (await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Cash, Unique("CASA-"), "Casa registru", Database.HeadOfficeId, "RON", null, null,
            Database.CashierId, null, null, null, maxBalance, mode, null, true, [new TreasuryCashierInput(Database.CashierId, true, null, null)], null))).Id;

    private async Task<FinanceSaved> Draft(int account, DateOnly day, CashDocumentKind kind, string operation, string amount)
    {
        var type = await Query<int>("SELECT Id FROM fin.TreasuryOperationType WHERE Code = @Code", new { Code = operation });
        return await Send(new SaveCashDocument(null, account, kind, day, null, null, null, null, null, null,
            [new CashLineInput(type, amount, null, null, null, null, null)], null), Database.CashierId);
    }

    private async Task Validated(int account, DateOnly day, CashDocumentKind kind, string operation, string amount)
    {
        var draft = await Draft(account, day, kind, operation, amount);
        await Send(new ValidateCashDocument(draft.Id, draft.RowVersion), Database.CashierId);
    }

    private Task<CashDayClosedDto> Close(int account, DateOnly day) => Send(new CloseCashDay(account, day), Database.CashierId);

    [Fact]
    [Trait("Criteriu", "02.02-F1-A7")]
    public async Task A_day_closes_only_after_the_previous_day_and_without_drafts()
    {
        var account = await NewCash();
        await Validated(account, new DateOnly(2026, 11, 2), CashDocumentKind.CashIn, "INC-DIVERSE", "100");
        await Validated(account, new DateOnly(2026, 11, 3), CashDocumentKind.CashIn, "INC-DIVERSE", "50");

        var previousOpen = await Assert.ThrowsAsync<ErpException>(() => Close(account, new DateOnly(2026, 11, 3)));
        Assert.Equal("FIN_CASH_PREVIOUS_DAY_OPEN", previousOpen.Code);

        var draft = await Draft(account, new DateOnly(2026, 11, 2), CashDocumentKind.CashOut, "PLATA-DIVERSE", "10");
        var drafts = await Assert.ThrowsAsync<ErpException>(() => Close(account, new DateOnly(2026, 11, 2)));
        Assert.Equal("FIN_CASH_DAY_HAS_DRAFTS", drafts.Code);
        await Send(new DeleteCashDocument(draft.Id, draft.RowVersion), Database.CashierId);

        var first = await Close(account, new DateOnly(2026, 11, 2));
        var second = await Close(account, new DateOnly(2026, 11, 3));
        Assert.Equal((1, 1, 2, 2), (first.FirstPageNo, first.LastPageNo, second.FirstPageNo, second.LastPageNo));
        Assert.Equal("150.00", second.ClosingBalance);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%RegistruCasaInchis' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"treasuryAccountId\":{account},\"day\":\"2026-11-03\"%" }));
        var again = await Assert.ThrowsAsync<ErpException>(() => Close(account, new DateOnly(2026, 11, 3)));
        Assert.Equal("FIN_CASH_DAY_CLOSED", again.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A6")]
    public async Task Reopening_needs_the_permission_and_a_reason_reopens_the_later_days_and_is_audited()
    {
        var account = await NewCash();
        await Validated(account, new DateOnly(2026, 11, 9), CashDocumentKind.CashIn, "INC-DIVERSE", "100");
        await Validated(account, new DateOnly(2026, 11, 10), CashDocumentKind.CashIn, "INC-DIVERSE", "100");
        await Close(account, new DateOnly(2026, 11, 9));
        await Close(account, new DateOnly(2026, 11, 10));

        var forbidden = await Assert.ThrowsAsync<ErpException>(() => Send(new ReopenCashDay(account, new DateOnly(2026, 11, 9), "Chitanță omisă"),
            Database.CashierId));
        Assert.Equal(ErpErrorKind.Forbidden, forbidden.Kind);

        Permissions.Add(FinancePermissions.CashReopenDay);
        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new ReopenCashDay(account, new DateOnly(2026, 11, 9), " "),
            Database.CashierId));
        Assert.Contains("reason", noReason.Errors.Keys);

        var reopened = await Send(new ReopenCashDay(account, new DateOnly(2026, 11, 9), "Chitanță omisă"), Database.CashierId);
        Assert.Equal([new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 9)], reopened);
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Fin.Cash.ReopenDay' AND JSON_VALUE(Changes, '$.reason') = N'Chitanță omisă'
              AND JSON_VALUE(Changes, '$.treasuryAccountId') = @Account
            """, new { Account = account.ToString(System.Globalization.CultureInfo.InvariantCulture) }));

        // Dupa redeschidere ziua primeste documente; la reinchidere paginile alocate se pastreaza.
        await Validated(account, new DateOnly(2026, 11, 9), CashDocumentKind.CashIn, "INC-DIVERSE", "5");
        var reclosed = await Close(account, new DateOnly(2026, 11, 9));
        Assert.Equal((1, "105.00"), (reclosed.FirstPageNo, reclosed.ClosingBalance));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-4.2")]
    public async Task Closing_above_the_cash_limit_warns_and_the_register_shows_the_running_balance()
    {
        var account = await NewCash(maxBalance: "1000");
        var day = new DateOnly(2026, 11, 12);
        await Validated(account, day, CashDocumentKind.CashIn, "INC-DIVERSE", "1500");
        await Validated(account, day, CashDocumentKind.CashOut, "PLATA-DIVERSE", "200");

        var closed = await Close(account, day);
        Assert.Contains("1.000,00", closed.BalanceWarning, StringComparison.Ordinal);

        var register = await Send(new GetCashRegister(account, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), Database.CashierId);
        Assert.Equal(["1500.00", "1300.00"], register.Entries.Select(e => e.Balance));
        Assert.True(Assert.Single(register.Days).IsClosed);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A10")]
    public async Task The_midnight_job_closes_the_eligible_days_and_leaves_open_the_day_with_drafts()
    {
        var account = await NewCash(CashCloseMode.AutomaticAtMidnight);
        await Validated(account, new DateOnly(2026, 11, 16), CashDocumentKind.CashIn, "INC-DIVERSE", "70");
        await Validated(account, new DateOnly(2026, 11, 17), CashDocumentKind.CashIn, "INC-DIVERSE", "30");
        await Draft(account, new DateOnly(2026, 11, 17), CashDocumentKind.CashOut, "PLATA-DIVERSE", "10");
        await Validated(account, new DateOnly(2026, 11, 18), CashDocumentKind.CashIn, "INC-DIVERSE", "1");

        // 18.11.2026, 00:05 ora Romaniei (22:05 UTC in ziua precedenta): ziua precedenta e 17.11.
        var job = new CashDayAutoClose(new FinanceConnection(Database.ConnectionString), new FixedTime(new DateTimeOffset(2026, 11, 17, 22, 5, 0,
            TimeSpan.Zero)), NullLogger<CashDayAutoClose>.Instance);
        var result = await job.RunAsync(Ct);

        Assert.Contains((account, new DateOnly(2026, 11, 16)), result.Closed);
        Assert.Contains((account, new DateOnly(2026, 11, 17), "FIN_CASH_DAY_HAS_DRAFTS"), result.LeftOpen);
        Assert.DoesNotContain(result.Closed, c => c.TreasuryAccountId == account && c.Day >= new DateOnly(2026, 11, 17));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Fin.Cash.CloseDay' AND UserId = 1 AND EntityId = (
                SELECT Id FROM fin.CashRegisterDay WHERE TreasuryAccountId = @Account AND [Day] = '20261116')
            """, new { Account = account }));
    }
}

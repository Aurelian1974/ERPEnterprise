using System.Diagnostics;
using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Proprietatile registrului de casa pe SQL Server real (02.02, F1 3.4; criteriile F1-A8 si F1-A17): soldurile zilelor raman inlantuite
/// oricare ar fi ordinea validarilor, iar registrul unei zile cu 500 de documente se genereaza sub 3 secunde. Documentele se valideaza
/// direct prin fin.usp_CashDocument_Validate (aceeasi procedura ca pipeline-ul), ca testul sa ramana rapid.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CashBalancePropertyTests : FinanceTestBase, IClassFixture<FinanceDatabase>
{
    public CashBalancePropertyTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.CashView]);
    }

    private async Task<int> NewCash() =>
        (await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Cash, Unique("CASA-"), "Casa proprietati", Database.HeadOfficeId, "RON", null, null,
            Database.CashierId, null, null, null, null, CashCloseMode.OnDemand, null, true, [new TreasuryCashierInput(Database.CashierId, true, null, null)],
            null))).Id;

    /// <summary>
    /// O ciorna cu o linie si validarea ei, in tranzactia ei (ca TransactionBehavior: procedura nu isi deschide tranzactia, SP-03);
    /// intoarce false daca validarea e refuzata (sold negativ), cu rollback.
    /// </summary>
    private async Task<bool> Direct(SqlConnection connection, int account, DateOnly day, bool cashIn, decimal amount, int number)
    {
        var type = cashIn ? "INC-DIVERSE" : "PLATA-DIVERSE";
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Ct);
        var id = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO fin.CashDocument (CompanyId, TreasuryAccountId, Kind, DocumentDate, CurrencyCode, ExchangeRate, RateDate, TotalAmount, TotalAmountRon,
                CreatedBy)
            VALUES (@CompanyId, @Account, @Kind, @Day, 'RON', 1, @Day, @Amount, @Amount, 1);
            DECLARE @Id int = SCOPE_IDENTITY();
            INSERT INTO fin.CashDocumentLine (CashDocumentId, LineNumber, TreasuryOperationTypeId, Amount, AmountRon)
            SELECT @Id, 1, Id, @Amount, @Amount FROM fin.TreasuryOperationType WHERE Code = @Type;
            SELECT @Id;
            """, new { Database.CompanyId, Account = account, Kind = cashIn ? 2 : 3, Day = day.ToDateTime(TimeOnly.MinValue), Amount = amount, Type = type },
            transaction);
        var version = await connection.ExecuteScalarAsync<byte[]>("SELECT RowVersion FROM fin.CashDocument WHERE Id = @Id", new { Id = id }, transaction);
        try
        {
            await connection.ExecuteAsync("fin.usp_CashDocument_Validate", new
            {
                CashDocumentId = id, Database.CompanyId, Number = $"T-{account}-{number:D5}", RowVersion = version, UserId = 1,
            }, transaction, commandType: System.Data.CommandType.StoredProcedure);
            await transaction.CommitAsync(Ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 51005)
        {
            await transaction.RollbackAsync(Ct);
            return false;
        }
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A8")]
    public async Task The_opening_balance_of_each_day_is_the_closing_balance_of_the_previous_active_day_over_1000_random_days()
    {
        var account = await NewCash();
        var random = new Random(20261122);
        var start = new DateOnly(2021, 1, 1);
        var expected = 0m;
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);

        // 1.000 de zile in ordine aleatoare (si documente datate inapoi), incasari mari si plati mici: platile refuzate pentru sold negativ nu se numara.
        var days = Enumerable.Range(0, 1000).OrderBy(_ => random.Next()).ToList();
        var number = 0;
        var activeDays = new HashSet<int>();
        foreach (var offset in days)
        {
            var cashIn = random.Next(3) != 0;
            var amount = Math.Round((decimal)random.Next(100, 100_000) / 100m, 2);
            if (await Direct(connection, account, start.AddDays(offset), cashIn, amount, ++number))
            {
                expected += cashIn ? amount : -amount;
                activeDays.Add(offset);
            }
        }

        var rows = (await connection.QueryAsync<(DateTime Day, decimal Opening, decimal TotalIn, decimal TotalOut, decimal Closing)>("""
            SELECT [Day], OpeningBalance, TotalIn, TotalOut, ClosingBalance FROM fin.CashRegisterDay WHERE TreasuryAccountId = @Account ORDER BY [Day]
            """, new { Account = account })).ToList();
        Assert.Equal(activeDays.Count, rows.Count);
        Assert.True(rows.Count > 600);
        Assert.Equal(0m, rows[0].Opening);
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.Equal(rows[i - 1].Closing, rows[i].Opening);
        }

        Assert.All(rows, r => Assert.Equal(r.Opening + r.TotalIn - r.TotalOut, r.Closing));
        Assert.All(rows, r => Assert.True(r.Closing >= 0));
        Assert.Equal(expected, rows[^1].Closing);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A17")]
    public async Task The_register_of_a_day_with_500_documents_is_generated_in_under_3_seconds()
    {
        var account = await NewCash();
        var day = new DateOnly(2026, 11, 5);
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            for (var i = 1; i <= 500; i++)
            {
                Assert.True(await Direct(connection, account, day, i % 4 != 0, 10m + i, i));
            }
        }

        // Prima cerere incalzeste pipeline-ul; se masoara a doua, ca la un utilizator.
        await Send(new GetCashRegister(account, day, day), Database.CashierId);
        var watch = Stopwatch.StartNew();
        var register = await Send(new GetCashRegister(account, day, day), Database.CashierId);
        watch.Stop();

        Assert.Equal(500, register.Entries.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Registrul a durat {watch.Elapsed.TotalMilliseconds:N0} ms.");
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-11")]
    public async Task The_account_history_shows_its_audited_changes()
    {
        Permissions.UnionWith([FinancePermissions.TreasuryAccountView, FinancePermissions.TreasuryAccountManage]);
        var account = await NewCash();
        var history = await Send(new GetTreasuryAccountHistory(account));
        Assert.Equal("Fin.TreasuryAccount.Save", Assert.Single(history).Action);
        Assert.Empty(await Send(new GetTreasuryAccountHistory(account), companyId: Database.OtherCompanyId));
    }
}

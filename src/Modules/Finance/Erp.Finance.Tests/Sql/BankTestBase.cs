using Dapper;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Treasury;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>Testele bancare: fiecare lucreaza pe contul lui bancar, cu IBAN valid propriu, si importa extrase construite (StatementFiles).</summary>
public abstract class BankTestBase(FinanceDatabase database) : FinanceTestBase(database)
{
    protected static readonly DateOnly Day = new(2026, 11, 2);
    private static int accountSequence;

    /// <summary>Un cont bancar nou al firmei, cu IBAN valid propriu.</summary>
    protected async Task<(int Id, string Iban)> NewBank(string currency = "RON", int? companyId = null)
    {
        var iban = StatementFiles.RomanianIban("RNCB", $"{Interlocked.Increment(ref accountSequence):D4}{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var bankAccountId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.CompanyBankAccount (CompanyId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@CompanyId, @Iban, N'BCR', @Currency, 0, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { CompanyId = companyId ?? Database.CompanyId, Iban = iban, Currency = currency });
        var saved = await Send(new SaveTreasuryAccount(null, TreasuryAccountKind.Bank, Unique("BANCA-"), "Cont bancar test", null, currency, bankAccountId,
            null, null, null, null, null, null, CashCloseMode.OnDemand, null, true, [], null), companyId: companyId);
        return (saved.Id, iban);
    }

    protected Task<BankImportResultDto> Import(int? account, byte[] content, string name = "extras.xml", int? companyId = null) =>
        Send(new ImportBankStatement(account, name, Convert.ToBase64String(content)), companyId: companyId);

    protected Task<int> OperationType(string code) => Query<int>("SELECT Id FROM fin.TreasuryOperationType WHERE Code = @Code", new { Code = code });
}

namespace Erp.Finance.Contracts.Treasury;

/// <summary>Tipul contului de trezorerie (02.02, F1 3.1).</summary>
public enum TreasuryAccountKind : byte
{
    Bank = 1,
    Cash = 2,
    CompanyCard = 3,
    Transit = 4,
    Deposit = 5,
}

/// <summary>Un cont de trezorerie al firmei, pentru celelalte module (F1 6).</summary>
public sealed record TreasuryAccountDto(
    int Id, int CompanyId, int? BranchId, string Code, string Name,
    TreasuryAccountKind Kind, string CurrencyCode, int? CompanyBankAccountId,
    string? GlAccountCode, bool IsActive);

/// <summary>Conturile de trezorerie ale unei firme, citite de celelalte module (F1 6).</summary>
public interface ITreasuryAccountQueries
{
    Task<IReadOnlyList<TreasuryAccountDto>> GetByCompanyAsync(int companyId, TreasuryAccountKind? kind, CancellationToken ct);
}

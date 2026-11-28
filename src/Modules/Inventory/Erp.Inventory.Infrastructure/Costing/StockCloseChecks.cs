using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Contracts.Periods;

namespace Erp.Inventory.Infrastructure.Costing;

/// <summary>
/// Pasii Stocurilor din lista de verificari la inchiderea lunii (02.05, S1 3.6, S3 3.5, 3.7; C3 3.3), prin inv.usp_StockClose_Check:
/// COST_RECALC_PENDING (chei in coada de recalculare de la o data din luna sau dinainte; blocant, fara trecere, S3-A9), NEGATIVE_STOCK (chei pe
/// minus la sfarsitul lunii; blocant, cu trecere) si RETAIL_DISCHARGE (gestiunile la pret cu amanuntul cu metoda K si miscari fara documentul DA
/// al lunii; blocant, fara trecere: contabilul valideaza descarcarea din ecranul s3-03).
/// </summary>
public sealed class StockCloseCheck(IDbSession session, string code, CloseCheckSeverity severity, bool allowOverride, string okSummary, string failSummary)
    : IPeriodCloseCheck
{
    public static IEnumerable<(string Code, CloseCheckSeverity Severity, bool AllowOverride, string Ok, string Fail)> Shipped { get; } =
    [
        ("COST_RECALC_PENDING", CloseCheckSeverity.Blocking, false, "Coada de recalculare a costului e goală pentru lună.",
            "{0} chei de stoc așteaptă recalcularea costului."),
        ("NEGATIVE_STOCK", CloseCheckSeverity.Blocking, true, "Niciun stoc negativ la sfârșitul lunii.", "{0} stocuri negative la sfârșitul lunii."),
        ("RETAIL_DISCHARGE", CloseCheckSeverity.Blocking, false, "Descărcările la preț cu amănuntul ale lunii sunt validate.",
            "{0} gestiuni la preț cu amănuntul fără descărcarea (DA) lunii."),
        ("UNIDENTIFIED_LOT", CloseCheckSeverity.Blocking, true, "Niciun sold pe lotul NEIDENTIFICAT.",
            "{0} variante au sold pe lotul NEIDENTIFICAT; corectați-l cu documentul de schimbare a lotului (SL)."),
    ];

    public string Code => code;

    public CloseCheckSeverity Severity => severity;

    public bool AllowOverride => allowOverride;

    public async Task<CloseCheckResult> RunAsync(int companyId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var issues = (await session.Connection.QueryAsync<CloseCheckIssue>(new CommandDefinition("inv.usp_StockClose_Check", new
        {
            CompanyId = companyId, Code = code, Start = periodStart.ToDateTime(TimeOnly.MinValue), End = periodEnd.ToDateTime(TimeOnly.MinValue),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)).ToList();
        return issues.Count == 0
            ? new CloseCheckResult(CloseCheckOutcome.Ok, okSummary, [])
            : new CloseCheckResult(CloseCheckOutcome.Failed, string.Format(System.Globalization.CultureInfo.InvariantCulture, failSummary, issues.Count), issues);
    }
}

/// <summary>
/// Evidenta operativa a stocului pentru concordanta C3 (02.05, S3 3.6; zona 4, pe gestiune), prin inv.usp_StockSubledger_Balance: valoarea la
/// sfarsitul datei pe care trebuie sa o arate conturile de stoc, adaos si TVA neexigibila cu gestiunea (371 − 378 − 4428 net).
/// </summary>
public sealed class StockSubledgerBalanceProvider(IDbSession session) : ISubledgerBalanceProvider
{
    public byte Area => 4;

    public string AreaName => "Stocuri";

    public IReadOnlyList<string> LedgerRoles { get; } = ["StockAccount", "RetailMarkup", "VatPending"];

    public async Task<IReadOnlyList<SubledgerBalance>> GetBalancesAsync(int companyId, DateOnly asOf, CancellationToken ct) =>
        (await session.Connection.QueryAsync<SubledgerBalance>(new CommandDefinition("inv.usp_StockSubledger_Balance",
            new { CompanyId = companyId, AsOf = asOf.ToDateTime(TimeOnly.MinValue) }, session.Transaction, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false)).ToList();
}

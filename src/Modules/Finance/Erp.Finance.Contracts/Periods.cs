using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Periods;

/// <summary>Starea unei luni contabile (C3 3.2): deschisa, inchisa operativ, inchisa, blocata.</summary>
public enum PeriodState : byte
{
    Open = 1,
    SoftClosed = 2,
    Closed = 3,
    Locked = 4,
}

/// <summary>Ce se scrie cu data contabila in luna (C3 3.2, tabelul starilor).</summary>
public enum PeriodWriteKind : byte
{
    SourceDocument = 1,
    AutomaticEntry = 2,
    ManualEntry = 3,
    ClosingEntry = 4,
}

/// <summary>
/// Starea perioadelor pentru celelalte module si pentru C2 (C3 6): documentele modulelor doar in luna deschisa; notele automate, manuale si de
/// inchidere si in luna inchisa operativ. O firma fara an financiar pentru data nu are perioade inchise.
/// </summary>
public interface IAccountingPeriodGuard
{
    Task<PeriodState?> GetStateAsync(int companyId, DateOnly accountingDate, CancellationToken ct);

    Task<bool> CanWriteAsync(int companyId, DateOnly accountingDate, PeriodWriteKind kind, CancellationToken ct);
}

/// <summary>Inchiderea operativa a unei luni (C3 5.1). Consumatori: toate modulele (mesaj), POS.</summary>
public sealed record PerioadaContabilaInchisaOperativ(int Year, int PeriodNo, DateOnly StartDate, DateOnly EndDate) : IntegrationEvent;

/// <summary>Inchiderea unei luni (C3 5.1). Consumatori: C1 (inghetarea planurilor alternative), C4, Raportare.</summary>
public sealed record PerioadaContabilaInchisa(int Year, int PeriodNo, DateOnly StartDate, DateOnly EndDate) : IntegrationEvent;

/// <summary>Redeschiderea unei luni (C3 5.1). Consumatori: C2 (reincearca coada), C4 (declaratii de verificat), Raportare.</summary>
public sealed record PerioadaContabilaRedeschisa(int Year, int PeriodNo, DateOnly StartDate, DateOnly EndDate, PeriodState NewState, string Reason)
    : IntegrationEvent;

/// <summary>Cat de grav e un pas al listei de inchidere (C3 6): blocant sau avertisment.</summary>
public enum CloseCheckSeverity : byte
{
    Blocking = 1,
    Warning = 2,
}

/// <summary>Rezultatul unui pas (C3 6): in regula, esuat sau nu se aplica.</summary>
public enum CloseCheckOutcome : byte
{
    Ok = 1,
    Failed = 2,
    NotApplicable = 3,
}

/// <summary>O problema gasita de un pas: mesajul, documentul si suma, daca sunt.</summary>
public sealed record CloseCheckIssue(string Message, string? DocumentRef, decimal? Amount);

public sealed record CloseCheckResult(CloseCheckOutcome Outcome, string Summary, IReadOnlyList<CloseCheckIssue> Issues);

/// <summary>
/// Un pas al listei de verificari la inchiderea lunii (C3 3.3, 6), implementat de modulul care stie regula (F1 casa, C2 coada, C1 maparea...).
/// C3 le descopera prin IEnumerable (NET-05) si nu cunoaste regulile, doar rezultatul.
/// </summary>
public interface IPeriodCloseCheck
{
    string Code { get; }

    CloseCheckSeverity Severity { get; }

    bool AllowOverride { get; }

    Task<CloseCheckResult> RunAsync(int companyId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}

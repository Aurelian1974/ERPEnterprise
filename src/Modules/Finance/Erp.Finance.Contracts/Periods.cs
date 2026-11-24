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

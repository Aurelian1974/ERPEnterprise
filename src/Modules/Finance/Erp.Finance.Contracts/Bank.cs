using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Bank;

/// <summary>Un extras bancar importat (02.02, F2 5.1), pentru raportare si notificari. ContinuityStatus: 1 OK, 2 gol, 3 gol acceptat.</summary>
public sealed record ExtrasBancarImportat(int BankStatementId, int TreasuryAccountId, DateOnly StatementDate, int LineCount, decimal OpeningBalance,
    decimal ClosingBalance, byte ContinuityStatus) : IntegrationEvent;

/// <summary>Confirmarea unui OP pe extras (F2 5.1): linia validata care il contine (ultima, daca a fost executat in transe).</summary>
public sealed record OrdinPlataConfirmat(int PaymentOrderId, long BankStatementLineId, DateOnly BookingDate, decimal Amount) : IntegrationEvent;

/// <summary>Banca a refuzat sau returnat OP-ul (F2 5.1): linia de restituire legata de el.</summary>
public sealed record OrdinPlataRespins(int PaymentOrderId, long BankStatementLineId, string Reason) : IntegrationEvent;

/// <summary>Un OP dintr-un lot exportat (F2 5.1).</summary>
public sealed record OrdinPlataExportatLinie(int PaymentOrderId, int? PartnerId, decimal Amount);

/// <summary>Exportul unui lot de plati catre banca (F2 5.1), la fiecare export (si reexport). Consumator: F5.</summary>
public sealed record OrdinPlataExportat(int PaymentBatchId, int TreasuryAccountId, decimal TotalAmount, IReadOnlyList<OrdinPlataExportatLinie> Orders)
    : IntegrationEvent;

/// <summary>O decontare de card asteptata, din alt modul (F2 6): POS-ul la inchiderea zilei.</summary>
public sealed record CardSettlementRequest(int CompanyId, string AcquirerCode, DateOnly SalesDate, decimal GrossAmount, string SourceModule, string SourceRef);

/// <summary>Inregistrarea decontarilor de card asteptate (F2 6), in tranzactia apelantului.</summary>
public interface ICardSettlementWriter
{
    /// <summary>Idempotent pe (SourceModule, SourceRef): un raport Z livrat de doua ori creeaza o singura decontare.</summary>
    Task<int> RegisterExpectedAsync(CardSettlementRequest request, CancellationToken ct);
}

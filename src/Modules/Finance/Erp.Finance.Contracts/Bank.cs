using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Bank;

/// <summary>Un extras bancar importat (02.02, F2 5.1), pentru raportare si notificari. ContinuityStatus: 1 OK, 2 gol, 3 gol acceptat.</summary>
public sealed record ExtrasBancarImportat(int BankStatementId, int TreasuryAccountId, DateOnly StatementDate, int LineCount, decimal OpeningBalance,
    decimal ClosingBalance, byte ContinuityStatus) : IntegrationEvent;

/// <summary>Confirmarea unui OP pe extras (F2 5.1): linia validata care il contine (ultima, daca a fost executat in transe).</summary>
public sealed record OrdinPlataConfirmat(int PaymentOrderId, long BankStatementLineId, DateOnly BookingDate, decimal Amount) : IntegrationEvent;

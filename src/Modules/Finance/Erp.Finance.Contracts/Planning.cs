using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Planning;

/// <summary>Aprobarea finala a unei propuneri de plata (F5 4.2 pasul 3, 5.1). Consumatori: Raportare, notificari.</summary>
public sealed record PropunerePlataAprobata(int PaymentProposalId, string Number, DateOnly PlannedPaymentDate, decimal TotalAmountRon, int LineCount, int ApprovedBy)
    : IntegrationEvent;

/// <summary>
/// Soldul prognozat scade sub pragul unei reguli de alerta (F5 3.7, 5.1), o singura data pe zi si regula. TreasuryAccountId null = toata
/// firma; Date = prima zi sub prag; CauseCategoryCode = categoria cu cea mai mare iesire in acea zi. Consumatori: Notificari, Raportare.
/// </summary>
public sealed record AlertaCashFlow(int RuleId, int? TreasuryAccountId, DateOnly Date, decimal ForecastBalanceRon, decimal MinBalanceRon,
    string? CauseCategoryCode, int? NotifyRoleId) : IntegrationEvent;

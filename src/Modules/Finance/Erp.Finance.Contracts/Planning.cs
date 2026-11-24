using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Planning;

/// <summary>Aprobarea finala a unei propuneri de plata (F5 4.2 pasul 3, 5.1). Consumatori: Raportare, notificari.</summary>
public sealed record PropunerePlataAprobata(int PaymentProposalId, string Number, DateOnly PlannedPaymentDate, decimal TotalAmountRon, int LineCount, int ApprovedBy)
    : IntegrationEvent;

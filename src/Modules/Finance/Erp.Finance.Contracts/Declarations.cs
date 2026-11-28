using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Declarations;

/// <summary>Depunerea unei declaratii (C4 5.1): recipisa sau numarul de inregistrare. Consumatori: Raportare, calendarul fiscal.</summary>
public sealed record DeclaratieDepusa(int ReturnId, string FormCode, DateOnly PeriodFrom, DateOnly PeriodTo, string RegistrationNumber, decimal AmountDue,
    DateOnly? DueDate) : IntegrationEvent;

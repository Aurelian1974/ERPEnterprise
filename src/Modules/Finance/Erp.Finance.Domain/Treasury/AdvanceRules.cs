namespace Erp.Finance.Domain.Treasury;

/// <summary>Scopul avansului spre decontare (F1 3.5).</summary>
public enum AdvancePurpose : byte
{
    CurrentExpenses = 1,
    DomesticTrip = 2,
    ForeignTrip = 3,
    Purchase = 4,
}

/// <summary>Starea avansului (F1 3.5); 0 De platit e adaugata la implementare (dispozitia de plata e inca ciorna).</summary>
public enum AdvanceStatus : byte
{
    ToPay = 0,
    Granted = 1,
    PartiallySettled = 2,
    Settled = 3,
    Overdue = 4,
    Reversed = 5,
}

/// <summary>Starea decontului de cheltuieli (F1 3.7).</summary>
public enum ExpenseReportStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Validated = 4,
    Reversed = 5,
}

/// <summary>Felul justificativului unei linii de decont (F1 3.7).</summary>
public enum JustificationKind : byte
{
    CompanyInvoice = 1,
    Receipt = 2,
    PerDiem = 3,
    Other = 4,
}

/// <summary>Diurna unei linii: suma, partea neimpozabila si cea impozabila (transmisa la HR).</summary>
public sealed record PerDiemAmounts(decimal Amount, decimal NonTaxable, decimal Taxable);

/// <summary>Regulile avansurilor si deconturilor (02.02, F1 3.5 si 3.7), fara acces la date.</summary>
public static class AdvanceRules
{
    /// <summary>Zilele lucratoare de decontare (setarea Fin.Advance.SettlementWorkingDays, implicit 3).</summary>
    public const int DefaultSettlementWorkingDays = 3;

    /// <summary>
    /// Termenul de decontare (F1 3.5): data intoarcerii (sau a acordarii, la cheltuieli curente si achizitii) plus zilele lucratoare.
    /// Sambata si duminica nu se numara; sarbatorile legale nu sunt inca in calendar.
    /// </summary>
    public static DateOnly SettlementDueDate(DateOnly start, int workingDays)
    {
        var day = start;
        for (var added = 0; added < workingDays;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                added++;
            }
        }

        return day;
    }

    /// <summary>Diurna (F1 3.7 regula 3): zile × suma pe zi; neimpozabil = zile × min(suma pe zi, plafonul neimpozabil); restul impozabil.</summary>
    public static PerDiemAmounts PerDiem(decimal days, decimal dailyAmount, decimal nonTaxableDailyLimit, Func<decimal, decimal> round)
    {
        var amount = round(days * dailyAmount);
        var nonTaxable = round(days * Math.Min(dailyAmount, nonTaxableDailyLimit));
        return new PerDiemAmounts(amount, nonTaxable, amount - nonTaxable);
    }
}

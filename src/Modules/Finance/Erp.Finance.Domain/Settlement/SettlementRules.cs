namespace Erp.Finance.Domain.Settlement;

/// <summary>Tipul partidei deschise (02.02, F3 3.1).</summary>
public enum OpenItemKind : byte
{
    Invoice = 1,
    CreditNote = 2,
    AdvanceInvoice = 3,
    DebitNote = 4,
    Payment = 5,
    OpeningBalance = 6,
    Compensation = 7,
    CommercialPaper = 8,
    WriteOff = 9,
    EmployeePayment = 10,
}

/// <summary>De unde vine o alocare (F3 3.3).</summary>
public enum AllocationSource : byte
{
    AutomaticOnPayment = 1,
    Manual = 2,
    Compensation = 3,
    Reversal = 4,
    CommercialPaper = 5,
    WriteOff = 6,
    Import = 7,
}

/// <summary>Alocarea automata a platilor din profilul partenerului (F3 3.2); FIFO e implicit (decizia din 15.11.2026).</summary>
public enum AutoAllocationMode : byte
{
    IndicatedOnly = 1,
    IndicatedThenFifo = 2,
    None = 3,
}

/// <summary>Motivul ajustarii unei diferente mici (F3 3.6).</summary>
public enum WriteOffReason : byte
{
    Rounding = 1,
    FeeWithheldByPartner = 2,
    AcceptedDifference = 3,
}

/// <summary>O partida pe care se poate aloca, in ordinea in care se aloca; Open e restul in valoare absoluta.</summary>
public sealed record AllocationCandidate(long Id, decimal Open);

/// <summary>Regulile de alocare ale F3, fara acces la date.</summary>
public static class SettlementRules
{
    /// <summary>
    /// Imparte o suma pe partide, in ordinea data (FIFO sau ordinea facturilor indicate), pana la restul fiecareia (F3 4.2 pasii 2–3). Intoarce
    /// perechile (partida, suma) cu suma pozitiva; ce depaseste toate partidele ramane nealocat.
    /// </summary>
    public static IReadOnlyList<(long Id, decimal Amount)> Distribute(decimal available, IEnumerable<AllocationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var result = new List<(long, decimal)>();
        foreach (var candidate in candidates)
        {
            if (available <= 0)
            {
                break;
            }

            var amount = Math.Min(available, candidate.Open);
            if (amount > 0)
            {
                result.Add((candidate.Id, amount));
                available -= amount;
            }
        }

        return result;
    }

    /// <summary>
    /// Semnul partidei unei plati (F3 2, conventia de semn): incasarea de la client e credit, plata catre furnizor e debit; restituirea are
    /// semnul invers.
    /// </summary>
    public static decimal PaymentSign(bool isCustomer, bool isRefund) => isCustomer ^ isRefund ? -1m : 1m;

    /// <summary>Semnul unei facturi primite prin eveniment: factura emisa e debit la client, factura primita e credit la furnizor.</summary>
    public static decimal InvoiceSign(bool isCustomer) => isCustomer ? 1m : -1m;

    /// <summary>Tipul partidei unei facturi din eveniment (F3 4.1).</summary>
    public static OpenItemKind InvoiceKind(bool isReversal, bool isAdvance) =>
        isReversal ? OpenItemKind.CreditNote : isAdvance ? OpenItemKind.AdvanceInvoice : OpenItemKind.Invoice;

    /// <summary>Contul de referinta al facturii (F3 3.1): 4111 la client, 401 la furnizor; reclasificarea avansurilor e in partea contabila.</summary>
    public static string InvoiceGlAccount(bool isCustomer) => isCustomer ? "4111" : "401";
}

using Erp.Finance.Contracts.Settlement;

namespace Erp.Finance.Domain.Settlement;

/// <summary>Regula de curs pentru plata in alta moneda decat factura (F4 3.3), din profilul partenerului.</summary>
public enum CrossRateRule : byte
{
    BnrOnPaymentDate = 1,
    BnrOnPaymentDatePlusMarkup = 2,
    InvoiceRate = 3,
    Manual = 4,
}

/// <summary>Starea unei partide la alocare, citita sub blocare: restul in moneda ei si valoarea lui contabila in lei, ambele cu semnul partidei.</summary>
public sealed record FxItem(long Id, PartnerRole Role, OpenItemKind Kind, string CurrencyCode, decimal ExchangeRate, decimal OpenAmount, decimal OpenAmountRon);

/// <summary>
/// O alocare de calculat. Cu aceeasi moneda, Amount e in moneda comuna. Cu monede diferite, Amount e in moneda platii (partea de plata;
/// fara plata, partea de credit), iar CrossRate = unitati din moneda platii pentru o unitate din moneda facturii.
/// </summary>
public sealed record FxRequest(long DebitId, long CreditId, decimal Amount, decimal? CrossRate = null, CrossRateRule? CrossRateSource = null);

/// <summary>
/// Rezultatul pe alocare: sumele pe fiecare parte (in moneda ei), valorile contabile in lei alocate, cursul incrucisat si diferenta realizata
/// (pozitiv = castig), cu partida in valuta a carei valoare s-a realizat.
/// </summary>
public sealed record FxPlanRow(long DebitId, long CreditId, decimal DebitAmount, decimal CreditAmount, decimal AmountRonDebit, decimal AmountRonCredit,
    decimal? CrossRate, CrossRateRule? CrossRateSource, decimal DifferenceRon, long? FxOpenItemId);

/// <summary>
/// Diferentele de curs la alocare (02.02, F4 2 regulile 2–4, 3.3, 4.1): fiecare parte a alocarii ia din valoarea contabila in lei a restului
/// partidei proportional cu suma alocata, iar alocarea care inchide partida ia tot restul in lei (P7); diferenta realizata = valoarea in lei a
/// partii de credit − valoarea in lei a partii de debit, pozitiva pentru castig la client (incasare mai mare decat creanta) si la furnizor
/// (plata mai mica decat datoria). Partidele din lot se actualizeaza pe rand, in ordinea randurilor.
/// </summary>
public static class FxCalculator
{
    public const string GainAccount = "765";
    public const string LossAccount = "665";
    public const string IndexGainAccount = "768";
    public const string IndexLossAccount = "668";

    public static bool IsPayment(OpenItemKind kind) => kind is OpenItemKind.Payment or OpenItemKind.EmployeePayment;

    /// <summary>Valoarea contabila in lei a unei sume alocate dintr-un rest (valori absolute): proportional, iar la inchidere tot restul.</summary>
    public static decimal Carrying(decimal openAbs, decimal openRonAbs, decimal amount) =>
        amount >= openAbs ? openRonAbs : Math.Round(amount * openRonAbs / openAbs, 2, MidpointRounding.AwayFromZero);

    /// <summary>Partea „factura” a unei alocari intre monede diferite: cea care nu e plata (fara plata, debitul).</summary>
    public static bool InvoiceIsDebit(FxItem debit, FxItem credit) => !IsPayment(debit.Kind) || IsPayment(credit.Kind);

    public static IReadOnlyList<FxPlanRow> Plan(IReadOnlyDictionary<long, FxItem> items, IReadOnlyList<FxRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(requests);
        var open = items.ToDictionary(i => i.Key, i => (Amount: Math.Abs(i.Value.OpenAmount), Ron: Math.Abs(i.Value.OpenAmountRon)));
        var result = new List<FxPlanRow>(requests.Count);
        foreach (var r in requests)
        {
            var debit = items[r.DebitId];
            var credit = items[r.CreditId];
            decimal debitAmount, creditAmount;
            decimal? crossRate = null;
            if (debit.CurrencyCode == credit.CurrencyCode)
            {
                debitAmount = creditAmount = r.Amount;
            }
            else
            {
                var rate = r.CrossRate ?? throw new InvalidOperationException("Lipsește cursul încrucișat.");
                var invoiceIsDebit = InvoiceIsDebit(debit, credit);
                var invoiceOpen = open[invoiceIsDebit ? debit.Id : credit.Id].Amount;
                var invoiceAmount = Math.Round(r.Amount / rate, 2, MidpointRounding.AwayFromZero);
                var paymentAmount = r.Amount;
                if (invoiceAmount > invoiceOpen)
                {
                    invoiceAmount = invoiceOpen;
                    paymentAmount = Math.Min(r.Amount, Math.Round(invoiceOpen * rate, 2, MidpointRounding.AwayFromZero));
                }

                (debitAmount, creditAmount) = invoiceIsDebit ? (invoiceAmount, paymentAmount) : (paymentAmount, invoiceAmount);
                crossRate = rate;
            }

            var d = open[debit.Id];
            var c = open[credit.Id];
            var ronDebit = Carrying(d.Amount, d.Ron, debitAmount);
            var ronCredit = Carrying(c.Amount, c.Ron, creditAmount);
            open[debit.Id] = (Math.Max(0, d.Amount - debitAmount), Math.Max(0, d.Ron - ronDebit));
            open[credit.Id] = (Math.Max(0, c.Amount - creditAmount), Math.Max(0, c.Ron - ronCredit));
            var difference = debit.CurrencyCode == "RON" && credit.CurrencyCode == "RON" ? 0 : ronCredit - ronDebit;
            long? fxItem = difference == 0 ? null : InvoiceIsDebit(debit, credit) ? debit.Id : credit.Id;
            result.Add(new FxPlanRow(r.DebitId, r.CreditId, debitAmount, creditAmount, ronDebit, ronCredit, crossRate, crossRate is null ? null : r.CrossRateSource,
                difference, fxItem));
        }

        return result;
    }

    /// <summary>
    /// Cursul incrucisat propus (F4 3.3): unitati din moneda platii pentru o unitate din moneda facturii, din cursurile in lei ale celor doua
    /// monede (RON = 1) la data platii; cu marja, majorat cu procentul din profil; „cursul facturii”, cursul in lei al facturii impartit la
    /// cursul in lei al platii din aceeasi zi. Memorat cu 6 zecimale.
    /// </summary>
    public static decimal CrossRate(CrossRateRule rule, decimal invoicePerUnit, decimal paymentPerUnit, decimal markupPercent, decimal invoiceDocumentRate,
        decimal paymentPerUnitOnInvoiceDate) =>
        Math.Round(rule switch
        {
            CrossRateRule.BnrOnPaymentDatePlusMarkup => invoicePerUnit / paymentPerUnit * (1 + markupPercent / 100m),
            CrossRateRule.InvoiceRate => invoiceDocumentRate / paymentPerUnitOnInvoiceDate,
            _ => invoicePerUnit / paymentPerUnit,
        }, 6, MidpointRounding.AwayFromZero);
}

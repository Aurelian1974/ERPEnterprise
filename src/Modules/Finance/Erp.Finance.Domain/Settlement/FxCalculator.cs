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
public sealed record FxItem(long Id, PartnerRole Role, OpenItemKind Kind, string CurrencyCode, decimal ExchangeRate, decimal OpenAmount, decimal OpenAmountRon)
{
    /// <summary>Valuta de referinta a unei facturi in lei indexate (F4 4.2); null pentru celelalte partide.</summary>
    public string? IndexCurrencyCode { get; init; }

    /// <summary>Restul de referinta, cu semnul partidei.</summary>
    public decimal IndexedOpenAmount { get; init; }
}

/// <summary>
/// O alocare de calculat. Cu aceeasi moneda, Amount e in moneda comuna. Cu monede diferite, Amount e in moneda platii (partea de plata;
/// fara plata, partea de credit), iar CrossRate = unitati din moneda platii pentru o unitate din moneda facturii.
/// </summary>
public sealed record FxRequest(long DebitId, long CreditId, decimal Amount, decimal? CrossRate = null, CrossRateRule? CrossRateSource = null)
{
    /// <summary>Data alocarii; fata de ea se aleg rularile de reevaluare de dinaintea carora se masoara diferenta realizata (F4 4.6).</summary>
    public DateOnly? AllocationDate { get; init; }
}

/// <summary>
/// O rulare validata care a reevaluat partida (F4 4.6): data reevaluarii, diferenta aplicata valorii contabile (cu semnul ei) si restul in valuta
/// (valoare absoluta) pe care s-a aplicat.
/// </summary>
public sealed record FxRunShare(int RunId, DateOnly RevaluationDate, decimal AppliedDifferenceRon, decimal AppliedOn);

/// <summary>Corectia in asteptare a unei alocari cu data intr-o luna reevaluata: partida, rularea, suma stinsa si diferenta de anulat (F4 4.6).</summary>
public sealed record FxCorrection(long OpenItemId, int RunId, decimal ForeignAmount, decimal DifferenceRon);

/// <summary>
/// Rezultatul pe alocare: sumele pe fiecare parte (in moneda ei), valorile contabile in lei alocate, cursul incrucisat si diferenta realizata
/// (pozitiv = castig), cu partida in valuta a carei valoare s-a realizat.
/// </summary>
public sealed record FxPlanRow(long DebitId, long CreditId, decimal DebitAmount, decimal CreditAmount, decimal AmountRonDebit, decimal AmountRonCredit,
    decimal? CrossRate, CrossRateRule? CrossRateSource, decimal DifferenceRon, long? FxOpenItemId)
{
    /// <summary>Alocare pe o factura indexata (F4 4.2): diferenta merge pe 768 / 668, iar IndexedAmount e suma de referinta stinsa.</summary>
    public bool IsIndexed { get; init; }

    public decimal? IndexedAmount { get; init; }

    /// <summary>Corectiile in asteptare (F4 4.6), cand alocarea are data intr-o luna deja reevaluata a uneia dintre partide.</summary>
    public IReadOnlyList<FxCorrection> Corrections { get; init; } = [];
}

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

    /// <summary>O alocare intre o factura indexata si o partida neindexata, in aceeasi moneda (lei): se stinge suma de referinta (F4 4.2).</summary>
    public static bool IsIndexedPair(FxItem debit, FxItem credit) =>
        debit.CurrencyCode == credit.CurrencyCode && (debit.IndexCurrencyCode is null) != (credit.IndexCurrencyCode is null);

    /// <summary>Partea „factura” a unei alocari intre monede diferite: cea care nu e plata (fara plata, debitul).</summary>
    public static bool InvoiceIsDebit(FxItem debit, FxItem credit) => !IsPayment(debit.Kind) || IsPayment(credit.Kind);

    public static IReadOnlyList<FxPlanRow> Plan(IReadOnlyDictionary<long, FxItem> items, IReadOnlyList<FxRequest> requests,
        IReadOnlyDictionary<long, IReadOnlyList<FxRunShare>>? runs = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(requests);
        var open = items.ToDictionary(i => i.Key, i => (Amount: Math.Abs(i.Value.OpenAmount), Ron: Math.Abs(i.Value.OpenAmountRon)));
        var indexedOpen = items.Where(i => i.Value.IndexCurrencyCode is not null).ToDictionary(i => i.Key, i => Math.Abs(i.Value.IndexedOpenAmount));
        var result = new List<FxPlanRow>(requests.Count);
        foreach (var r in requests)
        {
            var debit = items[r.DebitId];
            var credit = items[r.CreditId];
            decimal debitAmount, creditAmount;
            decimal? crossRate = null;
            decimal? indexed = null;
            if (IsIndexedPair(debit, credit))
            {
                // Factura indexata: plata (in lei) / curs = suma de referinta stinsa; partea din factura in lei, proportionala cu ea, iar daca
                // se stinge tot restul de referinta, tot restul in lei; surplusul platii ramane nealocat.
                var rate = r.CrossRate ?? throw new InvalidOperationException("Lipsește cursul valutei de indexare.");
                var invoice = debit.IndexCurrencyCode is not null ? debit : credit;
                var reference = indexedOpen[invoice.Id];
                var invoiceOpen = open[invoice.Id].Amount;
                var stins = Math.Round(r.Amount / rate, 2, MidpointRounding.AwayFromZero);
                decimal invoiceAmount, paymentAmount = r.Amount;
                if (stins >= reference)
                {
                    stins = reference;
                    invoiceAmount = invoiceOpen;
                    paymentAmount = Math.Min(r.Amount, Math.Round(reference * rate, 2, MidpointRounding.AwayFromZero));
                }
                else
                {
                    invoiceAmount = Math.Round(invoiceOpen * stins / reference, 2, MidpointRounding.AwayFromZero);
                }

                indexedOpen[invoice.Id] = reference - stins;
                (debitAmount, creditAmount) = invoice == debit ? (invoiceAmount, paymentAmount) : (paymentAmount, invoiceAmount);
                crossRate = rate;
                indexed = stins;
            }
            else if (debit.CurrencyCode == credit.CurrencyCode)
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
            // F4 4.6: partea din reevaluarile de la data alocarii incolo nu intra in diferenta realizata (se masoara fata de valoarea de dinainte),
            // ci devine corectie in asteptare, anulata de rularea urmatoare. Pe debit reevaluarea marise valoarea, pe credit o scazuse (semnul ei).
            var corrections = new List<FxCorrection>();
            var revaluedDebit = Retro(runs, debit.Id, r.AllocationDate, debit.IndexCurrencyCode is null ? debitAmount : indexed ?? 0, corrections);
            var revaluedCredit = Retro(runs, credit.Id, r.AllocationDate, credit.IndexCurrencyCode is null ? creditAmount : indexed ?? 0, corrections);
            var difference = debit.CurrencyCode == "RON" && credit.CurrencyCode == "RON" && indexed is null
                ? 0
                : (ronCredit + revaluedCredit) - (ronDebit - revaluedDebit);
            long? fxItem = difference == 0 ? null
                : indexed is not null ? (debit.IndexCurrencyCode is not null ? debit.Id : credit.Id)
                : InvoiceIsDebit(debit, credit) ? debit.Id : credit.Id;
            result.Add(new FxPlanRow(r.DebitId, r.CreditId, debitAmount, creditAmount, ronDebit, ronCredit, crossRate, crossRate is null ? null : r.CrossRateSource,
                difference, fxItem) { Corrections = corrections, IsIndexed = indexed is not null, IndexedAmount = indexed });
        }

        return result;
    }

    /// <summary>
    /// Partea din reevaluarile validate ale partidei, cu data reevaluarii la sau dupa data alocarii, care revine sumei stinse: proportional cu
    /// restul pe care s-a aplicat fiecare (F4 4.6). Scrie corectiile (diferenta de anulat = −partea) si intoarce suma partilor, cu semnul lor.
    /// </summary>
    private static decimal Retro(IReadOnlyDictionary<long, IReadOnlyList<FxRunShare>>? runs, long itemId, DateOnly? date, decimal amount,
        List<FxCorrection> corrections)
    {
        if (runs is null || date is null || !runs.TryGetValue(itemId, out var shares))
        {
            return 0;
        }

        var total = 0m;
        foreach (var run in shares.Where(s => s.RevaluationDate >= date && s.AppliedOn > 0))
        {
            var part = Math.Round(run.AppliedDifferenceRon * Math.Min(amount, run.AppliedOn) / run.AppliedOn, 2, MidpointRounding.AwayFromZero);
            if (part != 0)
            {
                corrections.Add(new FxCorrection(itemId, run.RunId, amount, -part));
                total += part;
            }
        }

        return total;
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

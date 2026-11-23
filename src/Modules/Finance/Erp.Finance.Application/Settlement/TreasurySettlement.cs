using System.Text.RegularExpressions;
using Erp.Finance.Contracts.Settlement;

namespace Erp.Finance.Application.Settlement;

/// <summary>O linie a unui document de plata (casa, OP, parte de linie de extras), pentru alocare.</summary>
public sealed record TreasurySettlementLine(string Key, string OperationTypeCode, int? PartnerId, decimal Amount, decimal AmountRon, string? SettlementRef)
{
    /// <summary>Sensul banilor, cand nu il da tipul operatiunii (partea unei linii de extras: semnul sumei).</summary>
    public bool? Incoming { get; init; }
}

/// <summary>Un document de plata validat: sursa, numarul, data, moneda si liniile.</summary>
public sealed record TreasurySettlementDocument(int CompanyId, string SourceDocumentType, long SourceDocumentId, string DocumentNo, DateOnly DocumentDate,
    string CurrencyCode, decimal ExchangeRate, IReadOnlyList<TreasurySettlementLine> Lines, bool ConfirmWarnings);

/// <summary>
/// Puntea dintre documentele de plata F1 / F2 si alocarea F3 (F1 4.1 pasul 5, F2 4.4, F3 4.2), in tranzactia documentului: fiecare linie cu
/// partener si tip de decontare devine partida de plata. Tipul operatiunii da rolul (SettlementKind 1 / 5 client, 2 / 6 furnizor); sensul opus
/// rolului e o restituire; avansurile (5, 6) raman nealocate (le consuma factura urmatoare). Facturile indicate vin din SettlementRef
/// („OI:&lt;id partida&gt;”, una sau mai multe).
/// </summary>
public sealed partial class TreasurySettlement(ISettlementStore store, ISettlementService settlement)
{
    [GeneratedRegex(@"OI:(\d{1,18})", RegexOptions.CultureInvariant)]
    private static partial Regex OpenItemReference();

    /// <summary>Facturile indicate intr-o referinta de decontare, in ordinea scrisa.</summary>
    public static IReadOnlyList<long> Targets(string? settlementRef) =>
        settlementRef is null ? [] : OpenItemReference().Matches(settlementRef).Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().ToList();

    /// <summary>Referinta scrisa pe linia documentului pentru partidele alese din lista facturilor deschise.</summary>
    public static string Reference(IEnumerable<long> openItemIds) => string.Join(',', openItemIds.Select(id => $"OI:{id}"));

    public async Task RegisterAsync(TreasurySettlementDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        var kinds = await store.OperationTypeKindsAsync(ct).ConfigureAwait(false);
        // Un document revalidat (linia de extras dupa desfacerea validarii) primeste partide noi: cele vechi sunt stornate (cheia + „/n”).
        var (existing, _) = await store.PaymentItemsAsync(document.CompanyId, document.SourceDocumentType, document.SourceDocumentId, null, ct).ConfigureAwait(false);
        foreach (var line in document.Lines)
        {
            if (line.PartnerId is not { } partnerId || line.Amount == 0 || !kinds.TryGetValue(line.OperationTypeCode, out var type))
            {
                continue;
            }

            var (role, fifo) = type.SettlementKind switch
            {
                1 => (PartnerRole.Customer, true),
                2 => (PartnerRole.Supplier, true),
                5 => (PartnerRole.Customer, false),
                6 => (PartnerRole.Supplier, false),
                _ => ((PartnerRole?)null, false),
            };
            if (role is not { } partnerRole)
            {
                continue;
            }

            // Banii intra (sensul 1) sau ies (2): la client incasarea intra, la furnizor plata iese; altfel e o restituire.
            var incoming = line.Incoming ?? (type.Direction == 3 ? line.Amount > 0 : type.Direction == 1);
            var previous = existing.Where(i => i.SourceLineKey == line.Key || i.SourceLineKey.StartsWith(line.Key + "/", StringComparison.Ordinal)).ToList();
            if (previous.Any(i => !i.IsReversed))
            {
                continue; // deja inregistrata (reluare)
            }

            var key = previous.Count == 0 ? line.Key : $"{line.Key}/{previous.Count + 1}";
            var isRefund = partnerRole == PartnerRole.Customer ? !incoming : incoming;
            var targets = fifo ? Targets(line.SettlementRef).Select(id => new SettlementTarget(id, null)).ToList() : [];
            await settlement.RegisterPaymentAsync(new PaymentSettlementRequest(document.CompanyId, partnerId, partnerRole, document.SourceDocumentType,
                document.SourceDocumentId, key, document.DocumentNo, document.DocumentDate, document.CurrencyCode, document.ExchangeRate, Math.Abs(line.Amount),
                Math.Abs(line.AmountRon), isRefund, targets, document.ConfirmWarnings) { AllowFifo = fifo }, ct).ConfigureAwait(false);
        }
    }
}

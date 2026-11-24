using Erp.Finance.Contracts.Accounting;

namespace Erp.Finance.Domain.Accounting;

/// <summary>De unde vine contul unei linii de nota (C2 3.4): afisat pe linie, ca utilizatorul sa stie ce sa schimbe.</summary>
public enum ResolvedFrom : byte
{
    Override = 1,
    Item = 2,
    ItemGroup = 3,
    ItemType = 4,
    Partner = 5,
    Treasury = 6,
    OperationType = 7,
    Role = 8,
    Default = 9,
}

/// <summary>Motivul pentru care un eveniment intra in coada „De contat” (C2 3.7).</summary>
public enum PostingFailureReason : byte
{
    AccountNotFound = 1,
    AccountInvalid = 2,
    DimensionMissing = 3,
    ClosedPeriod = 4,
    ModelMissing = 5,
    Unbalanced = 6,
    Unexpected = 7,
}

/// <summary>O linie de model (C2 3.2): componenta, filtrul, partea, semnul, cheia de cont, contul implicit, dimensiunile copiate si gruparea.</summary>
public sealed record ModelLineDef(int LineNumber, string Component, byte Side, string AccountKey, string? DefaultAccountCode = null, int Sign = 1,
    string? FilterVatCategory = null, string? FilterOperationTypeCode = null, byte CopyDimensions = Dimensions.All, byte GroupBy = 1,
    string? DescriptionTemplate = null);

/// <summary>O varianta de model (C2 3.2): conditiile pe antet si liniile; prima potrivita in ordinea prioritatii castiga.</summary>
public sealed record VariantDef(int Id, int Priority, string Name, IReadOnlyList<string> RequiredFlags, IReadOnlyList<string> ForbiddenFlags,
    IReadOnlyList<ModelLineDef> Lines, DateOnly ValidFrom, DateOnly? ValidTo = null, byte? PartnerKind = null, string? DocumentType = null);

/// <summary>Modelul unui tip de eveniment (C2 3.2), cu jurnalul notei si variantele.</summary>
public sealed record PostingModelDef(string EventType, int JournalId, IReadOnlyList<VariantDef> Variants);

/// <summary>Dimensiunile copiate de o linie de model din componenta pe linia de nota (masca de biti).</summary>
public static class Dimensions
{
    public const byte Partner = 1;
    public const byte Employee = 2;
    public const byte CostCenter = 4;
    public const byte Project = 8;
    public const byte Warehouse = 16;
    public const byte Site = 32;
    public const byte All = 63;
}

/// <summary>
/// Datele de configurare din care se gasesc conturile (C2 3.3, 3.4), incarcate o data pe nota: rolurile (cu conditiile lor), contul fiecarui
/// cont de trezorerie, contul corespondent al tipurilor de operatiuni si de cheltuieli, tipul partenerilor si modul de moneda al conturilor.
/// </summary>
public sealed record ResolutionContext(Func<string, byte?, string?, string?> RoleAccount, IReadOnlyDictionary<int, string> TreasuryAccounts,
    IReadOnlyDictionary<string, string> OperationTypeAccounts, IReadOnlyDictionary<string, string> ExpenseTypeAccounts, Func<int, byte?> PartnerKind,
    Func<string, byte> AccountCurrencyMode, decimal MaxRoundingDiff = 0.05m);

/// <summary>O linie de nota rezultata din contare, inca pe cod de cont.</summary>
public sealed record PostingLine(string AccountCode, byte Side, decimal AmountRon, string? CurrencyCode, decimal? AmountCurrency, int? PartnerId, int? EmployeeId,
    int? CostCenterId, int? ProjectId, int? WarehouseId, int? SiteId, string? VatRateCode, string? VatCategory, string? Description, ResolvedFrom ResolvedFrom,
    string? SourceLineRef);

/// <summary>Rezultatul contarii: liniile si varianta, sau motivul de coada cu detaliile (cheia, lantul incercat).</summary>
public sealed record PostingResult(int? VariantId, IReadOnlyList<PostingLine> Lines, PostingFailureReason? Failure, string? Details)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Contarea unui eveniment dupa model (C2 4.1 pasii 2–4), ca functie pura: varianta dupa flaguri, tipul partenerului si documentul; fiecare
/// linie de model pe componentele potrivite, cu contul gasit pe lantul cheii; gruparea pe cont si dimensiuni; diferenta de rotunjire pe rolul
/// RoundingGain / RoundingLoss pana la prag, peste prag coada (Unbalanced). Simularea (IPostingPreview) si contarea reala folosesc aceeasi functie.
/// </summary>
public static class PostingEngine
{
    public const string RoundingGainRole = "RoundingGain";
    public const string RoundingLossRole = "RoundingLoss";

    public static PostingResult Build(PostingModelDef? model, AccountingPayload payload, ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(ctx);
        if (model is null)
        {
            return Fail(PostingFailureReason.ModelMissing, $"Nu există model de contare activ pentru {payload.DocumentType}.");
        }

        var kind = payload.PartnerId is { } p ? ctx.PartnerKind(p) : null;
        var variant = model.Variants.OrderBy(v => v.Priority).FirstOrDefault(v =>
            v.ValidFrom <= payload.AccountingDate && (v.ValidTo is null || v.ValidTo >= payload.AccountingDate)
            && v.RequiredFlags.All(f => payload.Flags.Contains(f, StringComparer.OrdinalIgnoreCase))
            && !v.ForbiddenFlags.Any(f => payload.Flags.Contains(f, StringComparer.OrdinalIgnoreCase))
            && (v.PartnerKind is null || v.PartnerKind == kind)
            && (v.DocumentType is null || string.Equals(v.DocumentType, payload.DocumentType, StringComparison.OrdinalIgnoreCase)));
        if (variant is null)
        {
            return Fail(PostingFailureReason.ModelMissing, $"Nicio variantă a modelului {model.EventType} nu se potrivește documentului.");
        }

        var raw = new List<PostingLine>();
        foreach (var line in variant.Lines.OrderBy(l => l.LineNumber))
        {
            foreach (var c in payload.Components.Where(c => string.Equals(c.Component, line.Component, StringComparison.OrdinalIgnoreCase)
                         && (line.FilterVatCategory is null || string.Equals(line.FilterVatCategory, c.VatCategory, StringComparison.OrdinalIgnoreCase))
                         && (line.FilterOperationTypeCode is null || string.Equals(line.FilterOperationTypeCode, c.OperationTypeCode, StringComparison.OrdinalIgnoreCase))))
            {
                var partner = c.PartnerId ?? payload.PartnerId;
                var (code, from, tried) = Resolve(line, c, payload, partner, ctx);
                if (code is null)
                {
                    return Fail(PostingFailureReason.AccountNotFound,
                        $"Cheia {line.AccountKey} pentru componenta {c.Component} nu a dat niciun cont (încercat: {string.Join(" → ", tried)}).");
                }

                var amount = c.AmountRon * line.Sign;
                if (amount == 0)
                {
                    continue;
                }

                var foreign = !string.Equals(payload.CurrencyCode, "RON", StringComparison.OrdinalIgnoreCase) && ctx.AccountCurrencyMode(code) != 0;
                var d = line.CopyDimensions;
                raw.Add(new PostingLine(code, line.Side, amount, foreign ? payload.CurrencyCode : null, foreign ? c.Amount * line.Sign : null,
                    (d & Dimensions.Partner) != 0 ? partner : null, (d & Dimensions.Employee) != 0 ? payload.EmployeeId : null,
                    (d & Dimensions.CostCenter) != 0 ? c.CostCenterId : null, (d & Dimensions.Project) != 0 ? c.ProjectId : null,
                    (d & Dimensions.Warehouse) != 0 ? c.WarehouseId : null, (d & Dimensions.Site) != 0 ? c.SiteId : null, c.VatRateCode, c.VatCategory,
                    Describe(line.DescriptionTemplate, payload), from, line.GroupBy == 0 ? c.SourceLineRef : null));
            }
        }

        var lines = Group(raw, variant.Lines).ToList();
        var difference = lines.Where(l => l.Side == 1).Sum(l => l.AmountRon) - lines.Where(l => l.Side == 2).Sum(l => l.AmountRon);
        if (difference != 0)
        {
            if (Math.Abs(difference) > ctx.MaxRoundingDiff)
            {
                return Fail(PostingFailureReason.Unbalanced, $"Nota ar fi dezechilibrată cu {difference:0.00} lei (o componentă fără linie de model?).");
            }

            var role = difference > 0 ? RoundingGainRole : RoundingLossRole;
            var account = ctx.RoleAccount(role, null, null);
            if (account is null)
            {
                return Fail(PostingFailureReason.AccountNotFound, $"Rolul {role} nu are cont pentru diferența de rotunjire.");
            }

            lines.Add(new PostingLine(account, difference > 0 ? (byte)2 : (byte)1, Math.Abs(difference), null, null, null, null, null, null, null, null, null, null,
                "Diferență de rotunjire", ResolvedFrom.Role, null));
        }

        return lines.Count == 0 ? Fail(PostingFailureReason.Unbalanced, "Documentul nu are nicio sumă de contat.")
            : new PostingResult(variant.Id, lines, null, null);
    }

    /// <summary>Lantul de cautare al unei chei (C2 3.3): primul nivel care da un cont castiga; intoarce si nivelurile incercate.</summary>
    public static (string? Code, ResolvedFrom From, IReadOnlyList<string> Tried) Resolve(ModelLineDef line, AccountingComponent c, AccountingPayload payload,
        int? partnerId, ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(ctx);
        var tried = new List<string>();
        var kind = partnerId is { } p ? ctx.PartnerKind(p) : null;
        string? Role(string role)
        {
            tried.Add($"rolul {role}");
            return ctx.RoleAccount(role, kind, payload.CurrencyCode);
        }

        (string?, ResolvedFrom) Override()
        {
            tried.Add("contul de pe linie");
            return (c.GlAccountOverride, ResolvedFrom.Override);
        }

        (string?, ResolvedFrom) Found = line.AccountKey switch
        {
            "Treasury" => (Lookup(ctx.TreasuryAccounts, c.TreasuryAccountId, tried, "contul de trezorerie"), ResolvedFrom.Treasury),
            "TreasuryCounter" => First(Override(), (Lookup(ctx.OperationTypeAccounts, c.OperationTypeCode, tried, "tipul de operațiune"), ResolvedFrom.OperationType)),
            "Expense" => First(Override(), (Lookup(ctx.ExpenseTypeAccounts, c.ExpenseTypeCode, tried, "tipul de cheltuială"), ResolvedFrom.OperationType),
                (Role("PurchaseExpense"), ResolvedFrom.Role)),
            "Revenue" => First(Override(), (Role("SalesRevenue"), ResolvedFrom.Role)),
            "CostOfSales" => First(Override(), (Role("CostOfGoodsSold"), ResolvedFrom.Role)),
            "PartnerReceivable" => First(Override(), (Role("CustomerReceivable"), ResolvedFrom.Role)),
            "PartnerPayable" => First(Override(), (Role("SupplierPayable"), ResolvedFrom.Role)),
            "CustomerAdvance" => First(Override(), (Role("CustomerAdvance"), ResolvedFrom.Role)),
            "SupplierAdvance" => First(Override(), (Role("SupplierAdvance"), ResolvedFrom.Role)),
            "VatCollected" or "VatDeductible" or "VatPending" or "FxGain" or "FxLoss" or "IndexGain" or "IndexLoss" or "CashInTransit"
                => First(Override(), (Role(line.AccountKey), ResolvedFrom.Role)),
            "EventAccount" => Override(),
            _ => (null, ResolvedFrom.Default),
        };
        if (Found.Item1 is { Length: > 0 } code)
        {
            return (code, Found.Item2, tried);
        }

        tried.Add("contul implicit al modelului");
        return (line.DefaultAccountCode, ResolvedFrom.Default, tried);
    }

    private static (string?, ResolvedFrom) First(params (string? Code, ResolvedFrom From)[] options) =>
        options.FirstOrDefault(o => !string.IsNullOrEmpty(o.Code)) is { Code: not null } hit ? hit : (null, ResolvedFrom.Default);

    private static string? Lookup(IReadOnlyDictionary<int, string> map, int? key, List<string> tried, string label)
    {
        tried.Add(label);
        return key is { } k && map.TryGetValue(k, out var code) ? code : null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> map, string? key, List<string> tried, string label)
    {
        tried.Add(label);
        return key is not null && map.TryGetValue(key, out var code) ? code : null;
    }

    private static IEnumerable<PostingLine> Group(IReadOnlyList<PostingLine> lines, IReadOnlyList<ModelLineDef> model)
    {
        // Gruparea implicita (GroupBy 1): pe cont, parte si toate dimensiunile; liniile cu GroupBy 0 au SourceLineRef si raman separate.
        return lines.GroupBy(l => (l.AccountCode, l.Side, l.CurrencyCode, l.PartnerId, l.EmployeeId, l.CostCenterId, l.ProjectId, l.WarehouseId, l.SiteId,
                l.VatRateCode, l.SourceLineRef))
            .Select(g => g.First() with
            {
                AmountRon = g.Sum(l => l.AmountRon), AmountCurrency = g.First().AmountCurrency is null ? null : g.Sum(l => l.AmountCurrency),
            })
            .Where(l => l.AmountRon != 0);
    }

    private static string? Describe(string? template, AccountingPayload payload) =>
        template?.Replace("{DocumentType}", payload.DocumentType, StringComparison.Ordinal)
            .Replace("{DocumentNumber}", payload.DocumentNumber ?? "", StringComparison.Ordinal).Trim();

    private static PostingResult Fail(PostingFailureReason reason, string details) => new(null, [], reason, details);
}

using Erp.Core.Application.Organization;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Precision;

/// <summary>Rotunjirea dupa precizie (P7), fara acces la date: folosita de IPrecisionService si testata direct (A10).</summary>
public static class PrecisionRounding
{
    public static decimal Price(decimal value, PrecisionInfo p) => decimal.Round(value, p.PriceDecimals, p.Rounding);

    public static decimal Quantity(decimal value, PrecisionInfo p) => decimal.Round(value, p.QuantityDecimals, p.Rounding);

    public static decimal Amount(decimal value, PrecisionInfo p) => decimal.Round(value, p.AmountDecimals, p.Rounding);

    /// <summary>Valoarea unei linii: pretul si cantitatea rotunjite intai, apoi produsul la zecimalele valorii (macheta 07d).</summary>
    public static decimal LineAmount(decimal quantity, decimal price, PrecisionInfo p) => Amount(Quantity(quantity, p) * Price(price, p), p);
}

/// <summary>Modurile de rotunjire acceptate (System.MidpointRounding); 1 = jumatate departe de zero, implicit.</summary>
public static class RoundingModes
{
    public static bool IsValid(byte mode) => Enum.IsDefined((MidpointRounding)mode);
}

public sealed record PrecisionPolicyItemDto(int Id, string Code, string Name, byte PricePrecision, byte QuantityPrecision, byte AmountPrecision,
    byte RoundingMode, bool IsActive, int CompanyCount, byte[] RowVersion);

public sealed record PrecisionUsageDto(int CompanyId, string CompanyName, int PolicyId);

/// <summary>Configurare › Precizie: politicile, firmele cu politica lor implicita si articolele cu zecimale proprii.</summary>
public sealed record PrecisionPageDto(IReadOnlyList<PrecisionPolicyItemDto> Policies, IReadOnlyList<PrecisionUsageDto> Companies, int ItemOverrides);

public interface IPrecisionStore
{
    Task<PrecisionPageDto> ListAsync(CancellationToken ct);

    Task<SavedEntity> SaveAsync(SavePrecisionPolicy policy, int userId, CancellationToken ct);
}

[Module("Core")]
[RequiresPermission(CorePermissions.PrecisionManage)]
public sealed record GetPrecisionPolicies : IQuery<PrecisionPageDto>;

/// <summary>Precizia unui articol pe firma curenta, pentru fise si documente (orice utilizator autentificat).</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record ResolvePrecision(int? ItemId = null) : IQuery<PrecisionInfo>;

/// <summary>Politica de zecimale (macheta 07d): valoarea ramane 2; afecteaza doar documentele noi.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PrecisionManage)]
[Audited("Core.Precision.Save")]
public sealed record SavePrecisionPolicy(int? Id, string Code, string Name, byte PricePrecision, byte QuantityPrecision, byte RoundingMode, bool IsActive,
    byte[]? RowVersion) : ICommand<SavedEntity>;

internal sealed class SavePrecisionPolicyValidator : AbstractValidator<SavePrecisionPolicy>
{
    public SavePrecisionPolicyValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$").WithMessage("Codul are 1–20 de caractere: litere, cifre, „.”, „_” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(100).WithMessage("Denumirea are cel mult 100 de caractere.");
        RuleFor(x => x.PricePrecision).InclusiveBetween((byte)0, (byte)6).WithMessage("Prețul are între 0 și 6 zecimale.");
        RuleFor(x => x.QuantityPrecision).InclusiveBetween((byte)0, (byte)6).WithMessage("Cantitatea are între 0 și 6 zecimale.");
        RuleFor(x => x.RoundingMode).Must(RoundingModes.IsValid).WithMessage("Alegeți modul de rotunjire.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class GetPrecisionPoliciesHandler(IPrecisionStore store) : IQueryHandler<GetPrecisionPolicies, PrecisionPageDto>
{
    public Task<PrecisionPageDto> Handle(GetPrecisionPolicies request, CancellationToken cancellationToken) => store.ListAsync(cancellationToken);
}

internal sealed class ResolvePrecisionHandler(IPrecisionService precision, ICurrentContext context) : IQueryHandler<ResolvePrecision, PrecisionInfo>
{
    public Task<PrecisionInfo> Handle(ResolvePrecision request, CancellationToken cancellationToken) =>
        context.CompanyId == 0
            ? throw new ErpValidationException(new Dictionary<string, string[]> { ["companyId"] = ["Alegeți firma curentă."] })
            : precision.ResolveAsync(context.CompanyId, request.ItemId, null, cancellationToken);
}

internal sealed class SavePrecisionPolicyHandler(IPrecisionStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SavePrecisionPolicy, SavedEntity>
{
    public async Task<SavedEntity> Handle(SavePrecisionPolicy request, CancellationToken cancellationToken)
    {
        var policy = request with { Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim() };
        var saved = await store.SaveAsync(policy, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.PrecisionPolicy", saved.Id, new
        {
            policy.Code, policy.Name, policy.PricePrecision, policy.QuantityPrecision, policy.RoundingMode, policy.IsActive,
        });
        return saved;
    }
}

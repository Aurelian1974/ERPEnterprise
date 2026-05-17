using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.AnafLookup;

public sealed record AnafAdresaSediuSocialDto(
    string? Strada,
    string? Numar,
    string? Localitate,
    string? Judet,
    string? CodPostal,
    string? Tara
);

public sealed record AnafLookupDto(
    string Denumire,
    bool IsVatPayer,
    string? NrRegCom,
    string? StareInregistrare,
    string? Adresa,
    string? Telefon,
    string? FormaJuridica,
    AnafAdresaSediuSocialDto? AdresaSediuSocial
);

public sealed record AnafLookupQuery(string Cui) : IQuery<AnafLookupDto>;

public sealed class AnafLookupQueryHandler(IAnafService anafService)
    : IQueryHandler<AnafLookupQuery, AnafLookupDto>
{
    public async Task<Result<AnafLookupDto>> Handle(
        AnafLookupQuery request,
        CancellationToken cancellationToken)
    {
        var result = await anafService.VerifyAsync(request.Cui, cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure<AnafLookupDto>(AdministrationErrors.AnafVerificationFailed(result.ErrorMessage!));

        if (string.IsNullOrWhiteSpace(result.Data!.Denumire))
            return Result.Failure<AnafLookupDto>(AdministrationErrors.AnafVerificationFailed(
                "CUI-ul nu a fost găsit în registrul ANAF."));

        var raw = result.Data.AdresaSediuSocial;
        var adresaDto = raw is null ? null : new AnafAdresaSediuSocialDto(
            raw.Strada,
            raw.Numar,
            raw.Localitate,
            raw.Judet,
            raw.CodPostal,
            raw.Tara
        );

        return Result.Success(new AnafLookupDto(
            result.Data.Denumire,
            result.Data.ScpTva,
            result.Data.NrRegCom,
            result.Data.StareInregistrare,
            result.Data.Adresa,
            result.Data.Telefon,
            result.Data.FormaJuridica,
            adresaDto
        ));
    }
}

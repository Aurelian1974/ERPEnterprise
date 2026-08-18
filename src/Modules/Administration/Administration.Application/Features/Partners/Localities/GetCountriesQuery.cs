using Administration.Application.Abstractions;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Localities;

public sealed record CountryDto(string Code, string Name);

public sealed record GetCountriesQuery : IQuery<List<CountryDto>>;

public sealed class GetCountriesQueryHandler : IQueryHandler<GetCountriesQuery, List<CountryDto>>
{
    public Task<Result<List<CountryDto>>> Handle(
        GetCountriesQuery request,
        CancellationToken cancellationToken)
    {
        var countries = new List<CountryDto>
        {
            new("RO", "România"),
            new("BG", "Bulgaria"),
            new("HU", "Ungaria"),
            new("DE", "Germania"),
            new("IT", "Italia"),
            new("ES", "Spania"),
            new("FR", "Franța"),
            new("AT", "Austria"),
            new("PL", "Polonia"),
            new("UK", "Regatul Unit"),
            new("US", "Statele Unite"),
            new("OTHER", "Altă țară"),
        };

        return Task.FromResult(Result.Success(countries));
    }
}

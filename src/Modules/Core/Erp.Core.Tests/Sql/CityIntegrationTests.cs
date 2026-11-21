using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Localitatile SIRUTA pe SQL Server real (02.01, 3.5): lista din formulare si legarea adreselor (cod ales sau nume unic in judet).</summary>
public sealed partial class PartnerIntegrationTests
{
    private SavePartner CityPartner(string city, string county, int? siruta = null) =>
        new(null, Code(), "Partener " + city, 4, null, true, false, null, false, false, false, null, "Str. Gării 1", city, county, "RO", null, null, null,
            null, true, null, CitySirutaCode: siruta);

    [Fact]
    [Trait("Criteriu", "02.01-3.5")]
    public async Task City_list_searches_without_diacritics_and_shows_the_commune_and_postal_code()
    {
        var cities = await Send(new SearchCities("AB", "garbova"));
        var village = Assert.Single(cities, c => c.Name == "Gârbova de Jos");
        Assert.Equal((1286, "Aiud", (byte?)4, "515204"), (village.SirutaCode, village.ParentName, village.ParentKind, village.PostalCode));
        Assert.Equal("Gârbova", cities[0].Name); // satul resedinta de comuna inaintea celorlalte
        Assert.Empty(await Send(new SearchCities("CJ", "garbova de jos")));
        Assert.Contains(await Send(new SearchCities("B", null)), c => c.Name == "Sectorul 1" && c.ParentName == "București");
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.5")]
    public async Task Address_takes_the_official_name_from_the_chosen_code_or_links_a_unique_name_in_the_county()
    {
        var chosen = await Send(new GetPartner((await Send(CityPartner("ceva", "AB", 1026))).Id));
        Assert.Equal(("Alba Iulia", (int?)1026), (chosen.City, chosen.CitySirutaCode));

        var byName = await Send(new GetPartner((await Send(CityPartner("brasov", "BV"))).Id));
        Assert.Equal(("Brașov", "BV"), (byName.City, byName.CountyCode));
        Assert.NotNull(byName.CitySirutaCode);

        var sector = await Send(new GetPartner((await Send(CityPartner("Sector 3", "B"))).Id));
        Assert.Equal(("Sectorul 3", (int?)179169), (sector.City, sector.CitySirutaCode));

        var ambiguous = await Send(new GetPartner((await Send(CityPartner("Avrămești", "AB"))).Id));
        Assert.Equal(("Avrămești", (int?)null), (ambiguous.City, ambiguous.CitySirutaCode));

        var unknown = await Send(new GetPartner((await Send(CityPartner("Localitate inventată", "AB"))).Id));
        Assert.Null(unknown.CitySirutaCode);

        var wrongCounty = await Assert.ThrowsAsync<ErpException>(() => Send(CityPartner("Alba Iulia", "CJ", 1026)));
        Assert.Equal("CORE_INVALID_ADDRESS", wrongCounty.Code);
    }
}

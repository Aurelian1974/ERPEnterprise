using Administration.Application.Features.Partners;
using Administration.Application.Features.Partners.AnafLookup;
using Administration.Application.Features.Partners.Create;
using Administration.Application.Features.Partners.GetById;
using Administration.Application.Features.Partners.GetNextCode;
using Administration.Application.Features.Partners.List;
using Administration.Application.Features.Partners.Localities;
using Administration.Application.Features.Partners.SubEntities;
using Administration.Application.Features.Partners.Update;
using Administration.Application.Features.Partners.VerifyAnaf;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;

namespace Administration.Api.Controllers;

[ApiController]
[Route("api/v1/administration/partners")]
[AllowAnonymous] // TODO: restore [Authorize] once authentication is implemented
public sealed class PartnersController(ISender sender) : ControllerBase
{
    [HttpGet("anaf-lookup")]
    [ProducesResponseType(typeof(AnafLookupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnafLookup([FromQuery] string cui, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cui))
            return BadRequest("CUI-ul este obligatoriu.");

        var result = await sender.Send(new AnafLookupQuery(cui), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    // ─── Localities (localitati.dev proxy) ────────────────────────────────────

    [HttpGet("localities/search")]
    [ProducesResponseType(typeof(List<LocalityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchLocalities(
        [FromQuery] string q,
        [FromQuery] string? county = null,
        [FromQuery] string? countryCode = null,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Termenul de căutare este obligatoriu.");

        var result = await sender.Send(new SearchLocalitiesQuery(q, county, countryCode, limit), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("localities/countries")]
    [ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCountries(CancellationToken ct = default)
    {
        var result = await sender.Send(new GetCountriesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("localities/counties")]
    [ProducesResponseType(typeof(List<CountyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCounties(
        [FromQuery] string? countryCode = null,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetCountiesQuery(countryCode), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("localities/validate")]
    [ProducesResponseType(typeof(LocalityValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateLocality(
        [FromQuery] string name,
        [FromQuery] string? county = null,
        [FromQuery] string? countryCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Numele localității este obligatoriu.");

        var result = await sender.Send(new ValidateLocalityQuery(name, county, countryCode), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("localities/streets")]
    [ProducesResponseType(typeof(List<NominatimStreetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchNominatimStreets(
        [FromQuery] string country,
        [FromQuery] string city,
        [FromQuery] string street,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(street))
            return BadRequest("Termenul de căutare este obligatoriu.");

        var result = await sender.Send(new SearchNominatimStreetsQuery(country, city, street, limit), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("next-code")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNextCode(CancellationToken ct = default)
    {
        var result = await sender.Send(new GetNextPartnerCodeQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PartnerListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListPartnersQuery(page, pageSize, search), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PartnerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await sender.Send(new GetPartnerByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(result.Error.Description);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePartnerCommand command,
        CancellationToken ct = default)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePartnerCommand command,
        CancellationToken ct = default)
    {
        var cmd = command with { Id = id };
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    // ─── Addresses ─────────────────────────────────────────────────────────────

    [HttpPost("{partnerId:guid}/addresses")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertAddress(
        Guid partnerId,
        [FromBody] UpsertPartnerAddressCommand command,
        CancellationToken ct = default)
    {
        var cmd = command with { PartnerId = partnerId };
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpDelete("{partnerId:guid}/addresses/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAddress(
        Guid partnerId, long id, CancellationToken ct = default)
    {
        var result = await sender.Send(new DeletePartnerAddressCommand(partnerId, id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    // ─── Contacts ──────────────────────────────────────────────────────────────

    [HttpPost("{partnerId:guid}/contacts")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertContact(
        Guid partnerId,
        [FromBody] UpsertPartnerContactCommand command,
        CancellationToken ct = default)
    {
        var cmd = command with { PartnerId = partnerId };
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpDelete("{partnerId:guid}/contacts/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteContact(
        Guid partnerId, long id, CancellationToken ct = default)
    {
        var result = await sender.Send(new DeletePartnerContactCommand(partnerId, id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    // ─── Bank Accounts ─────────────────────────────────────────────────────────

    [HttpPost("{partnerId:guid}/bank-accounts")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertBankAccount(
        Guid partnerId,
        [FromBody] UpsertPartnerBankAccountCommand command,
        CancellationToken ct = default)
    {
        var cmd = command with { PartnerId = partnerId };
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpDelete("{partnerId:guid}/bank-accounts/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteBankAccount(
        Guid partnerId, long id, CancellationToken ct = default)
    {
        var result = await sender.Send(new DeletePartnerBankAccountCommand(partnerId, id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    // ─── ANAF Verification ─────────────────────────────────────────────────────

    [HttpPost("{id:guid}/anaf-verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyAnaf(Guid id, CancellationToken ct = default)
    {
        var result = await sender.Send(new VerifyPartnerAnafCommand(id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }
}

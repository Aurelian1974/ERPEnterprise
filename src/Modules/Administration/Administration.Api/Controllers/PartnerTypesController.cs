using Administration.Application.Features.PartnerTypes.Delete;
using Administration.Application.Features.PartnerTypes.GetAll;
using Administration.Application.Features.PartnerTypes.GetById;
using Administration.Application.Features.PartnerTypes.Upsert;
using Administration.Application.Features.PartnerTypes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;

namespace Administration.Api.Controllers;

[ApiController]
[Route("api/v1/administration/partner-types")]
[AllowAnonymous] // TODO: restore [Authorize] once authentication is implemented
public sealed class PartnerTypesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PartnerTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        Result<IReadOnlyList<PartnerTypeDto>> result =
            await sender.Send(new GetAllPartnerTypesQuery(includeInactive), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PartnerTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(byte id, CancellationToken ct)
    {
        Result<PartnerTypeDto> result =
            await sender.Send(new GetPartnerTypeByIdQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(result.Error.Description);
    }

    [HttpPost]
    [ProducesResponseType(typeof(byte), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] UpsertPartnerTypeCommand command,
        CancellationToken ct)
    {
        var cmd = command with { PartnerTypeId = null };
        Result<byte> result = await sender.Send(cmd, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        byte id,
        [FromBody] UpsertPartnerTypeCommand command,
        CancellationToken ct)
    {
        var cmd = command with { PartnerTypeId = id };
        Result<byte> result = await sender.Send(cmd, ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(byte id, CancellationToken ct)
    {
        Result result = await sender.Send(new DeletePartnerTypeCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }
}

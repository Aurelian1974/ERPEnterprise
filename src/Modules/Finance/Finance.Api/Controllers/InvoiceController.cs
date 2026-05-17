using Finance.Application.Features.Invoices.Approve;
using Finance.Application.Features.Invoices.Create;
using Finance.Application.Features.Invoices.GetById;
using Finance.Application.Features.Invoices.List;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/v1/finance/invoices")]
[Authorize]
public sealed class InvoiceController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ITenantContext _tenant;

    public InvoiceController(ISender sender, ITenantContext tenant)
    {
        _sender = sender;
        _tenant = tenant;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InvoiceListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] InvoiceFilters filters,
        CancellationToken ct)
    {
        var query = new ListInvoicesQuery(filters, _tenant.TenantId);
        Result<IReadOnlyList<InvoiceListDto>> result = await _sender.Send(query, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var query = new GetInvoiceByIdQuery(id, _tenant.TenantId);
        Result<InvoiceDetailDto> result = await _sender.Send(query, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(result.Error.Description);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken ct)
    {
        var command = new CreateInvoiceCommand(
            _tenant.TenantId,
            request.CustomerId,
            request.Currency,
            request.DueDate,
            request.Lines.Select(l => new CreateInvoiceLineCommand(
                l.Description, l.Quantity, l.UnitPrice, l.VatRate))
                .ToList());

        Result<Guid> result = await _sender.Send(command, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, result.Value)
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var command = new ApproveInvoiceCommand(id, _tenant.TenantId);
        Result result = await _sender.Send(command, ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
    }
}

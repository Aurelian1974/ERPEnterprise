using Shared.Kernel.Abstractions;

namespace Finance.Application.Features.Invoices.Approve;

public sealed record ApproveInvoiceCommand(Guid InvoiceId, Guid TenantId) : ICommand;

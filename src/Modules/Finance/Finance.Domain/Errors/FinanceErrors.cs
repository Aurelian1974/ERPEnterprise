using Shared.Kernel.Errors;

namespace Finance.Domain.Errors;

public static class FinanceErrors
{
    public static class Invoices
    {
        public static readonly Error NotFound = new("Finance.Invoice.NotFound", "Invoice was not found.");
        public static Error CustomerNotFound(Guid customerId) =>
            new("Finance.Invoice.CustomerNotFound", $"Customer '{customerId}' was not found.");
        public static readonly Error AlreadyApproved = new("Finance.Invoice.AlreadyApproved", "Invoice is already approved.");
        public static readonly Error AlreadyPaid = new("Finance.Invoice.AlreadyPaid", "Invoice is already paid.");
        public static readonly Error CannotApproveNonSubmitted = new("Finance.Invoice.CannotApprove", "Only submitted invoices can be approved.");
        public static readonly Error NoLines = new("Finance.Invoice.NoLines", "Invoice must have at least one line.");
    }
}

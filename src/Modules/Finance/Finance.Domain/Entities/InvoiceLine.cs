using Finance.Domain.ValueObjects;
using Shared.Kernel.Primitives;
using UUIDNext;

namespace Finance.Domain.Entities;

public sealed class InvoiceLine : Entity
{
    private InvoiceLine(
        Guid id,
        Guid invoiceId,
        string description,
        decimal quantity,
        Money unitPrice,
        decimal vatRate) : base(id)
    {
        InvoiceId = invoiceId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        VatRate = vatRate;
    }

    public Guid InvoiceId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public decimal VatRate { get; private set; }

    public Money NetAmount => UnitPrice.Multiply(Quantity);
    public Money VatAmount => NetAmount.Multiply(VatRate / 100m);
    public Money GrossAmount => NetAmount.Add(VatAmount);

    public static InvoiceLine Create(
        Guid invoiceId,
        string description,
        decimal quantity,
        Money unitPrice,
        decimal vatRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (vatRate < 0) throw new ArgumentException("VAT rate cannot be negative.", nameof(vatRate));

        return new InvoiceLine(
            Uuid.NewDatabaseFriendly(Database.SqlServer),
            invoiceId,
            description,
            quantity,
            unitPrice,
            vatRate);
    }
}

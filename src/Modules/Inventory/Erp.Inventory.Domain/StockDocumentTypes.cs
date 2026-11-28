namespace Erp.Inventory.Domain;

/// <summary>Un tip de document de stoc (02.05, S2 3.3): sensul, tipul miscarii, seria de numerotare si permisiunile lui.</summary>
public sealed record StockDocumentKind(string Code, int Direction, string MovementTypeCode, string NumberSeriesType, string EditPermission,
    string ValidatePermission);

/// <summary>Documentele proprii ale Stocurilor (S2 3.3); NP si NR (transferurile) au regulile lor, in S2.3.</summary>
public static class StockDocumentTypes
{
    public const string Load = "NI";
    public const string Unload = "ND";
    public const string Consumption = "BC";
    public const string ProductionNote = "PF";
    public const string CustodyIn = "CI";
    public const string CustodyOut = "CE";
    public const string TransferNote = "NP";
    public const string ReceiptNote = "NR";

    private static readonly IReadOnlyDictionary<string, StockDocumentKind> Kinds = new Dictionary<string, StockDocumentKind>(StringComparer.Ordinal)
    {
        [Load] = new(Load, 1, "LOAD", "Inventory.Load", "Inv.LoadUnload.Edit", "Inv.LoadUnload.Validate"),
        [Unload] = new(Unload, -1, "UNLOAD", "Inventory.Unload", "Inv.LoadUnload.Edit", "Inv.LoadUnload.Validate"),
        [Consumption] = new(Consumption, -1, "CONSUMPTION", "Inventory.Consumption", "Inv.Consumption.Edit", "Inv.Consumption.Validate"),
        [ProductionNote] = new(ProductionNote, 1, "PRODUCTION_IN", "Inventory.ProductionNote", "Inv.ProductionNote.Edit", "Inv.ProductionNote.Validate"),
        [CustodyIn] = new(CustodyIn, 1, "CUSTODY_IN", "Inventory.CustodyIn", "Inv.Custody.Edit", "Inv.Custody.Validate"),
        [CustodyOut] = new(CustodyOut, -1, "CUSTODY_OUT", "Inventory.CustodyOut", "Inv.Custody.Edit", "Inv.Custody.Validate"),
        [TransferNote] = new(TransferNote, 0, "TRANSFER_OUT", "Inventory.TransferNote", "Inv.Transfer.Edit", "Inv.Transfer.Validate"),
        [ReceiptNote] = new(ReceiptNote, 0, "TRANSFER_IN", "Inventory.ReceiptNote", "Inv.Transfer.Edit", "Inv.Transfer.Validate"),
    };

    public static StockDocumentKind? Find(string code) => Kinds.GetValueOrDefault(code);

    public static bool IsKnown(string code) => Kinds.ContainsKey(code);

    /// <summary>Documentele cu o singura miscare pe linie (intrare sau iesire), fara transferuri.</summary>
    public static bool IsSimple(string code) => Kinds.TryGetValue(code, out var kind) && kind.Direction != 0;
}

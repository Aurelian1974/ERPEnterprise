-- Tipurile de documente numerotate ale Stocurilor (02.05, S2 3.3; Nucleu 4.2): fiecare document are seria lui pe firma si an. MERGE pe Code (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.DocumentType AS target
USING (VALUES
    (N'Inventory.TransferNote',   N'Inventory', N'Notă de predare',          0),
    (N'Inventory.ReceiptNote',    N'Inventory', N'Notă de primire',          0),
    (N'Inventory.Load',           N'Inventory', N'Încărcare',                0),
    (N'Inventory.Unload',         N'Inventory', N'Descărcare',               0),
    (N'Inventory.Consumption',    N'Inventory', N'Bon de consum',            0),
    (N'Inventory.ProductionNote', N'Inventory', N'Notă de predare produse',  0),
    (N'Inventory.CustodyIn',      N'Inventory', N'Intrare în custodie',      0),
    (N'Inventory.CustodyOut',     N'Inventory', N'Ieșire din custodie',      0)
) AS source (Code, ModuleCode, Name, IsFiscalDefault)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.IsFiscalDefault <> source.IsFiscalDefault) THEN
    UPDATE SET Name = source.Name, IsFiscalDefault = source.IsFiscalDefault, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ModuleCode, Name, IsFiscalDefault, CreatedBy)
    VALUES (source.Code, source.ModuleCode, source.Name, source.IsFiscalDefault, 1);

-- Tipurile de documente numerotate (02.01, 4.2), din modulele planificate. Codul e tehnic si comparat exact (SQL-21);
-- fiecare modul isi completeaza tipurile odata cu documentele lui. MERGE pe Code (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.DocumentType AS target
USING (VALUES
    (N'Sales.Invoice',            N'Sales',      N'Factură fiscală',     1),
    (N'Sales.CreditNote',         N'Sales',      N'Factură storno',      1),
    (N'Sales.Order',              N'Sales',      N'Comandă client',      0),
    (N'Sales.Quote',              N'Sales',      N'Ofertă',              0),
    (N'Inventory.DeliveryNote',   N'Inventory',  N'Aviz de însoțire',    0),
    (N'Inventory.GoodsReceipt',   N'Inventory',  N'Recepție (NIR)',      0),
    (N'Inventory.Transfer',       N'Inventory',  N'Transfer',            0),
    (N'Finance.Receipt',          N'Finance',    N'Chitanță',            1),
    (N'Finance.Payment',          N'Finance',    N'Dispoziție de plată', 0),
    (N'Finance.CashIn',           N'Finance',    N'Dispoziție de încasare', 0),
    (N'Finance.ExpenseReport',    N'Finance',    N'Decont de cheltuieli', 0),
    (N'Finance.PaymentOrder',     N'Finance',    N'Ordin de plată',      0),
    (N'Finance.JournalEntry',     N'Finance',    N'Notă contabilă',      0),
    (N'Purchasing.Order',         N'Purchasing', N'Comandă furnizor',    0),
    (N'Core.Partner',             N'Core',       N'Cod partener',        0),
    (N'Core.Item',                N'Core',       N'Cod articol',         0)
) AS source (Code, ModuleCode, Name, IsFiscalDefault)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.IsFiscalDefault <> source.IsFiscalDefault) THEN
    UPDATE SET Name = source.Name, IsFiscalDefault = source.IsFiscalDefault, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ModuleCode, Name, IsFiscalDefault, CreatedBy)
    VALUES (source.Code, source.ModuleCode, source.Name, source.IsFiscalDefault, 1);

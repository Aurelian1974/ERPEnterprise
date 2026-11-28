-- Tipurile de miscare livrate (02.05, S2 3.2): lista inchisa, cu semnul si tipul de document pentru contarea C2. MERGE pe Id (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE inv.MovementType AS target
USING (VALUES
    (1,  'RECEIPT_PURCHASE',     N'Recepție de la furnizor',        1, 'GoodsReceipt'),
    (2,  'RETURN_TO_SUPPLIER',   N'Retur la furnizor',             -1, 'SupplierReturn'),
    (3,  'ISSUE_SALE',           N'Ieșire la vânzare',             -1, 'StockIssueSale'),
    (4,  'RETURN_FROM_CUSTOMER', N'Retur de la client',             1, 'StockReturnSale'),
    (5,  'POS_SALE',             N'Vânzare POS',                   -1, 'StockIssuePos'),
    (6,  'TRANSFER_OUT',         N'Predare (transfer)',            -1, 'StockTransfer'),
    (7,  'TRANSFER_IN',          N'Primire (transfer)',             1, 'StockTransfer'),
    (8,  'LOAD',                 N'Încărcare',                      1, 'StockLoad'),
    (9,  'UNLOAD',               N'Descărcare',                    -1, 'StockUnload'),
    (10, 'CONSUMPTION',          N'Consum',                        -1, 'StockConsumption'),
    (11, 'PRODUCTION_IN',        N'Predare produse',                1, 'ProductionOutput'),
    (12, 'PRODUCTION_OUT',       N'Consum în producție',           -1, 'ProductionConsumption'),
    (13, 'COUNT_PLUS',           N'Plus la inventar',               1, 'InventoryDifference'),
    (14, 'COUNT_MINUS',          N'Minus la inventar',             -1, 'InventoryDifference'),
    (15, 'CUSTODY_IN',           N'Intrare în custodie',            1, 'CustodyMovement'),
    (16, 'CUSTODY_OUT',          N'Ieșire din custodie',           -1, 'CustodyMovement')
) AS source (Id, Code, Name, Direction, PostingDocumentType)
ON target.Id = source.Id
WHEN MATCHED AND (target.Code <> source.Code OR target.Name <> source.Name OR target.Direction <> source.Direction
                  OR ISNULL(target.PostingDocumentType, '') <> source.PostingDocumentType) THEN
    UPDATE SET Code = source.Code, Name = source.Name, Direction = source.Direction, PostingDocumentType = source.PostingDocumentType
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Name, Direction, PostingDocumentType) VALUES (source.Id, source.Code, source.Name, source.Direction, source.PostingDocumentType);

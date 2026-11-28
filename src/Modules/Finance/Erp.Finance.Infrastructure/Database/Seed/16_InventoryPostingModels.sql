-- Cheile, rolurile si modelele de contare ale Stocurilor (02.05, S3 3.6, 3.7; 02.02, C2 3.2, 3.3; MIG-06). Modulul Stocuri trimite blocul de
-- contare pe evenimentele lui (componente cu tipul miscarii ca OperationTypeCode si gestiunea); contul contrapartidei se alege pe tipul miscarii
-- (FilterOperationTypeCode). Un model se creeaza doar daca tipul lui de eveniment nu are inca model (clientul il poate schimba din ecran).
-- CopyDimensions: 4 centru de cost, 16 gestiune (20 = ambele).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.AccountKey AS target
USING (VALUES
    ('Stock',                    N'Contul de stoc',                     N'Override → rolul StockAccount → implicit'),
    ('RetailMarkup',             N'Adaosul comercial (378)',            N'Override → rolul RetailMarkup → implicit'),
    ('GoodsReceivedNotInvoiced', N'Furnizori - facturi nesosite (408)', N'Override → rolul SupplierInvoicesToReceive → implicit')
) AS source (Code, Name, Chain)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.Chain <> source.Chain) THEN UPDATE SET Name = source.Name, Chain = source.Chain
WHEN NOT MATCHED BY TARGET THEN INSERT (Code, Name, Chain) VALUES (source.Code, source.Name, source.Chain);

DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

INSERT INTO fin.GlAccountRoleAssignment (CompanyId, Role, AccountId, ValidFrom, CreatedBy)
SELECT NULL, v.Role, a.Id, '20000101', 1
FROM (VALUES ('StockAccount', N'371'), ('RetailMarkup', N'378')) AS v (Role, Code)
JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.Code = v.Code
WHERE NOT EXISTS (SELECT 1 FROM fin.GlAccountRoleAssignment AS r WHERE r.CompanyId IS NULL AND r.Role = v.Role AND r.PartnerKind IS NULL
                  AND r.CurrencyCode IS NULL);

DECLARE @Models TABLE (EventType varchar(80), Name nvarchar(200));
INSERT INTO @Models VALUES
    ('MiscareStocValidata', N'Mișcare de stoc'),
    ('CostStocAjustat', N'Ajustare a costului stocului'),
    ('DescarcareAmanuntValidata', N'Descărcarea lunară la preț cu amănuntul'),
    ('ModificarePretAmanuntValidata', N'Modificarea prețului cu amănuntul');

DECLARE @Variants TABLE (EventType varchar(80), Priority int, Name nvarchar(200), RequiredFlags nvarchar(200));
INSERT INTO @Variants VALUES
    ('MiscareStocValidata', 90, N'Standard', NULL),
    ('CostStocAjustat', 10, N'Cost adăugat sau diferență de preț', N'Landed'),
    ('CostStocAjustat', 90, N'Recalculare și regularizare', NULL),
    ('DescarcareAmanuntValidata', 90, N'Standard', NULL),
    ('ModificarePretAmanuntValidata', 90, N'Standard', NULL);

DECLARE @Lines TABLE (EventType varchar(80), Priority int, LineNumber int, Component varchar(40), FilterType nvarchar(30), Side tinyint,
    AccountKey varchar(40), DefaultCode nvarchar(20), CopyDimensions tinyint);
INSERT INTO @Lines VALUES
    -- Miscarile: stocul la cost sau, in gestiunile la pret cu amanuntul, la pret cu TVA, cu adaosul (378) si TVA-ul neexigibil (4428).
    ('MiscareStocValidata', 90, 1,  'StockIn',    NULL,                   1, 'Stock',                    N'371',  16),
    ('MiscareStocValidata', 90, 2,  'MarkupIn',   NULL,                   2, 'RetailMarkup',             N'378',  16),
    ('MiscareStocValidata', 90, 3,  'VatIn',      NULL,                   2, 'VatPending',               N'4428', 16),
    ('MiscareStocValidata', 90, 4,  'StockOut',   NULL,                   2, 'Stock',                    N'371',  16),
    ('MiscareStocValidata', 90, 5,  'MarkupOut',  NULL,                   1, 'RetailMarkup',             N'378',  16),
    ('MiscareStocValidata', 90, 6,  'VatOut',     NULL,                   1, 'VatPending',               N'4428', 16),
    ('MiscareStocValidata', 90, 10, 'CounterIn',  N'RECEIPT_PURCHASE',    2, 'GoodsReceivedNotInvoiced', N'408',  0),
    ('MiscareStocValidata', 90, 11, 'CounterIn',  N'RETURN_FROM_CUSTOMER', 2, 'CostOfSales',             N'607',  20),
    ('MiscareStocValidata', 90, 12, 'CounterIn',  N'LOAD',                2, 'Fixed',                    N'7588', 20),
    ('MiscareStocValidata', 90, 13, 'CounterIn',  N'PRODUCTION_IN',       2, 'Fixed',                    N'711',  20),
    ('MiscareStocValidata', 90, 14, 'CounterIn',  N'COUNT_PLUS',          2, 'CostOfSales',              N'607',  20),
    ('MiscareStocValidata', 90, 20, 'CounterOut', N'ISSUE_SALE',          1, 'CostOfSales',              N'607',  20),
    ('MiscareStocValidata', 90, 21, 'CounterOut', N'POS_SALE',            1, 'CostOfSales',              N'607',  20),
    ('MiscareStocValidata', 90, 22, 'CounterOut', N'RETURN_TO_SUPPLIER',  1, 'GoodsReceivedNotInvoiced', N'408',  0),
    ('MiscareStocValidata', 90, 23, 'CounterOut', N'UNLOAD',              1, 'Fixed',                    N'6588', 20),
    ('MiscareStocValidata', 90, 24, 'CounterOut', N'CONSUMPTION',         1, 'Fixed',                    N'6028', 20),
    ('MiscareStocValidata', 90, 25, 'CounterOut', N'PRODUCTION_OUT',      1, 'Fixed',                    N'601',  20),
    ('MiscareStocValidata', 90, 26, 'CounterOut', N'COUNT_MINUS',         1, 'CostOfSales',              N'607',  20),
    -- Costurile adaugate (371 / 607 = 408) si recalcularea sau regularizarea (607 = 371).
    ('CostStocAjustat', 10, 1, 'AdjStock',   NULL, 1, 'Stock',                    N'371', 16),
    ('CostStocAjustat', 10, 2, 'AdjExpense', NULL, 1, 'CostOfSales',              N'607', 20),
    ('CostStocAjustat', 10, 3, 'AdjStock',   NULL, 2, 'GoodsReceivedNotInvoiced', N'408', 0),
    ('CostStocAjustat', 10, 4, 'AdjExpense', NULL, 2, 'GoodsReceivedNotInvoiced', N'408', 0),
    ('CostStocAjustat', 90, 1, 'AdjExpense', NULL, 1, 'CostOfSales',              N'607', 20),
    ('CostStocAjustat', 90, 2, 'AdjExpense', NULL, 2, 'Stock',                    N'371', 16),
    ('CostStocAjustat', 90, 3, 'AdjStock',   NULL, 1, 'Stock',                    N'371', 16),
    ('CostStocAjustat', 90, 4, 'AdjStock',   NULL, 2, 'CostOfSales',              N'607', 20),
    -- Descarcarea lunara: 378 = 607 (adaosul), 4428 = 607 (TVA-ul vanzarilor).
    ('DescarcareAmanuntValidata', 90, 1, 'Markup', NULL, 1, 'RetailMarkup', N'378',  16),
    ('DescarcareAmanuntValidata', 90, 2, 'Markup', NULL, 2, 'CostOfSales',  N'607',  20),
    ('DescarcareAmanuntValidata', 90, 3, 'Vat',    NULL, 1, 'VatPending',   N'4428', 16),
    ('DescarcareAmanuntValidata', 90, 4, 'Vat',    NULL, 2, 'CostOfSales',  N'607',  20),
    -- Modificarea pretului: 371 = 378 (diferenta fara TVA) + 4428 (TVA-ul diferentei).
    ('ModificarePretAmanuntValidata', 90, 1, 'Diff',       NULL, 1, 'Stock',        N'371',  16),
    ('ModificarePretAmanuntValidata', 90, 2, 'DiffMarkup', NULL, 2, 'RetailMarkup', N'378',  16),
    ('ModificarePretAmanuntValidata', 90, 3, 'DiffVat',    NULL, 2, 'VatPending',   N'4428', 16);

DECLARE @New TABLE (EventType varchar(80));

INSERT INTO fin.PostingModel (EventType, Name, JournalId, IsSystem, CreatedBy)
OUTPUT inserted.EventType INTO @New
SELECT m.EventType, m.Name, j.Id, 1, 1
FROM @Models AS m
JOIN fin.Journal AS j ON j.CompanyId IS NULL AND j.CodeKey = N'ST'
WHERE NOT EXISTS (SELECT 1 FROM fin.PostingModel AS x WHERE x.EventType = m.EventType);

INSERT INTO fin.PostingModelVariant (ModelId, Priority, Name, RequiredFlags, ForbiddenFlags, ValidFrom, CreatedBy)
SELECT pm.Id, v.Priority, v.Name, v.RequiredFlags, NULL, '20000101', 1
FROM @Variants AS v
JOIN @New AS n ON n.EventType = v.EventType
JOIN fin.PostingModel AS pm ON pm.EventType = v.EventType;

INSERT INTO fin.PostingModelLine (VariantId, LineNumber, Component, FilterOperationTypeCode, Side, AccountKey, DefaultAccountId, CopyDimensions,
    DescriptionTemplate)
SELECT pv.Id, l.LineNumber, l.Component, l.FilterType, l.Side, l.AccountKey, a.Id, l.CopyDimensions, N'{DocumentNumber}'
FROM @Lines AS l
JOIN @New AS n ON n.EventType = l.EventType
JOIN fin.PostingModel AS pm ON pm.EventType = l.EventType
JOIN fin.PostingModelVariant AS pv ON pv.ModelId = pm.Id AND pv.Priority = l.Priority
LEFT JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.Code = l.DefaultCode;

-- Cheile de cont si modelele de contare livrate (02.02, C2 3.2, 3.3; MIG-06). Cheile se actualizeaza la fiecare versiune. Un model livrat se
-- creeaza doar daca tipul lui de eveniment nu are inca model (clientul il poate schimba din ecran, iar o rerulare nu ii suprascrie schimbarile).
-- CopyDimensions: 1 partener, 2 angajat, 4 centru de cost, 8 proiect, 16 gestiune, 32 santier.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.AccountKey AS target
USING (VALUES
    ('Revenue',           N'Venituri',                          N'Override → rolul SalesRevenue → implicit'),
    ('Expense',           N'Cheltuieli',                        N'Override → tipul de cheltuială → rolul PurchaseExpense → implicit'),
    ('CostOfSales',       N'Costul vânzărilor',                 N'Override → rolul CostOfGoodsSold → implicit'),
    ('PartnerReceivable', N'Creanțe față de clienți',           N'Override → rolul CustomerReceivable (tip partener, monedă) → implicit'),
    ('PartnerPayable',    N'Datorii față de furnizori',         N'Override → rolul SupplierPayable (tip partener, monedă) → implicit'),
    ('CustomerAdvance',   N'Avansuri de la clienți',            N'Override → rolul CustomerAdvance → implicit'),
    ('SupplierAdvance',   N'Avansuri către furnizori',          N'Override → rolul SupplierAdvance → implicit'),
    ('Treasury',          N'Contul de trezorerie',              N'Contul contului de trezorerie (F1), fără alt nivel'),
    ('TreasuryCounter',   N'Contul corespondent al trezoreriei', N'Override → tipul de operațiune → implicit'),
    ('VatCollected',      N'TVA colectată',                     N'Override → rolul VatCollected → implicit'),
    ('VatDeductible',     N'TVA deductibilă',                   N'Override → rolul VatDeductible → implicit'),
    ('VatPending',        N'TVA neexigibilă',                   N'Override → rolul VatPending → implicit'),
    ('FxGain',            N'Câștig din diferențe de curs',      N'Contul din eveniment → rolul FxGain → implicit'),
    ('FxLoss',            N'Pierdere din diferențe de curs',    N'Contul din eveniment → rolul FxLoss → implicit'),
    ('EventAccount',      N'Contul trimis de eveniment',        N'Contul de pe componentă → implicit'),
    ('Fixed',             N'Cont fix',                          N'Doar contul implicit al liniei de model')
) AS source (Code, Name, Chain)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.Chain <> source.Chain) THEN UPDATE SET Name = source.Name, Chain = source.Chain
WHEN NOT MATCHED BY TARGET THEN INSERT (Code, Name, Chain) VALUES (source.Code, source.Name, source.Chain);

DECLARE @Models TABLE (EventType varchar(80), Name nvarchar(200), Journal nvarchar(10));
INSERT INTO @Models VALUES
    ('IncasareInregistrata', N'Încasare (casă și bancă)', N'TR'),
    ('PlataInregistrata', N'Plată (casă și bancă)', N'TR'),
    ('FacturaEmisa', N'Factură de vânzare', N'VZ'),
    ('FacturaFurnizorValidata', N'Factură de furnizor', N'AC'),
    ('TvaLaIncasareExigibil', N'TVA la încasare devenită exigibilă', N'DC'),
    ('DiferentaCursInregistrata', N'Diferență de curs la decontare', N'VL'),
    ('ReevaluareValutaraValidata', N'Reevaluarea valutară lunară', N'VL'),
    ('CompensareValidata', N'Compensare', N'DC'),
    ('DecontCheltuieliValidat', N'Decont de cheltuieli', N'TR');

DECLARE @Variants TABLE (EventType varchar(80), Priority int, Name nvarchar(200), RequiredFlags nvarchar(200), ForbiddenFlags nvarchar(200));
INSERT INTO @Variants VALUES
    ('IncasareInregistrata', 90, N'Standard', NULL, NULL),
    ('PlataInregistrata', 90, N'Standard', NULL, NULL),
    ('FacturaEmisa', 10, N'TVA la încasare', N'VatOnCollection', NULL),
    ('FacturaEmisa', 90, N'Standard', NULL, NULL),
    ('FacturaFurnizorValidata', 10, N'TVA la încasare', N'VatOnCollection', NULL),
    ('FacturaFurnizorValidata', 20, N'Taxare inversă', N'ReverseCharge', NULL),
    ('FacturaFurnizorValidata', 90, N'Standard', NULL, NULL),
    ('TvaLaIncasareExigibil', 10, N'Client', N'Customer', NULL),
    ('TvaLaIncasareExigibil', 20, N'Furnizor', N'Supplier', NULL),
    ('DiferentaCursInregistrata', 10, N'Client, câștig', N'Customer,Gain', NULL),
    ('DiferentaCursInregistrata', 20, N'Client, pierdere', N'Customer,Loss', NULL),
    ('DiferentaCursInregistrata', 30, N'Furnizor, câștig', N'Supplier,Gain', NULL),
    ('DiferentaCursInregistrata', 40, N'Furnizor, pierdere', N'Supplier,Loss', NULL),
    ('ReevaluareValutaraValidata', 90, N'Standard', NULL, NULL),
    ('CompensareValidata', 90, N'Standard', NULL, NULL),
    ('DecontCheltuieliValidat', 90, N'Standard', NULL, NULL);

DECLARE @Lines TABLE (EventType varchar(80), Priority int, LineNumber int, Component varchar(40), Side tinyint, AccountKey varchar(40),
    DefaultCode nvarchar(20), CopyDimensions tinyint, DescriptionTemplate nvarchar(200));
INSERT INTO @Lines VALUES
    ('IncasareInregistrata', 90, 1, 'Amount', 1, 'Treasury', NULL, 0, N'{DocumentNumber}'),
    ('IncasareInregistrata', 90, 2, 'Amount', 2, 'TreasuryCounter', NULL, 63, N'{DocumentNumber}'),
    ('PlataInregistrata', 90, 1, 'Amount', 1, 'TreasuryCounter', NULL, 63, N'{DocumentNumber}'),
    ('PlataInregistrata', 90, 2, 'Amount', 2, 'Treasury', NULL, 0, N'{DocumentNumber}'),
    ('FacturaEmisa', 10, 1, 'Total', 1, 'PartnerReceivable', N'4111', 1, N'{DocumentNumber}'),
    ('FacturaEmisa', 10, 2, 'Net', 2, 'Revenue', N'707', 60, N'{DocumentNumber}'),
    ('FacturaEmisa', 10, 3, 'Vat', 2, 'VatPending', N'4428', 0, N'{DocumentNumber}'),
    ('FacturaEmisa', 90, 1, 'Total', 1, 'PartnerReceivable', N'4111', 1, N'{DocumentNumber}'),
    ('FacturaEmisa', 90, 2, 'Net', 2, 'Revenue', N'707', 60, N'{DocumentNumber}'),
    ('FacturaEmisa', 90, 3, 'Vat', 2, 'VatCollected', N'4427', 0, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 10, 1, 'Net', 1, 'Expense', N'628', 60, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 10, 2, 'Vat', 1, 'VatPending', N'4428', 0, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 10, 3, 'Total', 2, 'PartnerPayable', N'401', 1, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 20, 1, 'Net', 1, 'Expense', N'628', 60, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 20, 2, 'Total', 2, 'PartnerPayable', N'401', 1, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 20, 3, 'Vat', 1, 'VatDeductible', N'4426', 0, N'{DocumentNumber} taxare inversă'),
    ('FacturaFurnizorValidata', 20, 4, 'Vat', 2, 'VatCollected', N'4427', 0, N'{DocumentNumber} taxare inversă'),
    ('FacturaFurnizorValidata', 90, 1, 'Net', 1, 'Expense', N'628', 60, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 90, 2, 'Vat', 1, 'VatDeductible', N'4426', 0, N'{DocumentNumber}'),
    ('FacturaFurnizorValidata', 90, 3, 'Total', 2, 'PartnerPayable', N'401', 1, N'{DocumentNumber}'),
    ('TvaLaIncasareExigibil', 10, 1, 'Vat', 1, 'VatPending', N'4428', 0, N'{DocumentNumber}'),
    ('TvaLaIncasareExigibil', 10, 2, 'Vat', 2, 'VatCollected', N'4427', 0, N'{DocumentNumber}'),
    ('TvaLaIncasareExigibil', 20, 1, 'Vat', 1, 'VatDeductible', N'4426', 0, N'{DocumentNumber}'),
    ('TvaLaIncasareExigibil', 20, 2, 'Vat', 2, 'VatPending', N'4428', 0, N'{DocumentNumber}'),
    ('DiferentaCursInregistrata', 10, 1, 'Difference', 1, 'PartnerReceivable', N'4111', 1, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 10, 2, 'Difference', 2, 'EventAccount', N'765', 0, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 20, 1, 'Difference', 1, 'EventAccount', N'665', 60, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 20, 2, 'Difference', 2, 'PartnerReceivable', N'4111', 1, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 30, 1, 'Difference', 1, 'PartnerPayable', N'401', 1, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 30, 2, 'Difference', 2, 'EventAccount', N'765', 0, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 40, 1, 'Difference', 1, 'EventAccount', N'665', 60, N'Diferență de curs {DocumentNumber}'),
    ('DiferentaCursInregistrata', 40, 2, 'Difference', 2, 'PartnerPayable', N'401', 1, N'Diferență de curs {DocumentNumber}'),
    ('ReevaluareValutaraValidata', 90, 1, 'Debit', 1, 'EventAccount', NULL, 60, N'Reevaluare {DocumentNumber}'),
    ('ReevaluareValutaraValidata', 90, 2, 'Credit', 2, 'EventAccount', NULL, 60, N'Reevaluare {DocumentNumber}'),
    ('CompensareValidata', 90, 1, 'Payable', 1, 'PartnerPayable', N'401', 1, N'Compensare {DocumentNumber}'),
    ('CompensareValidata', 90, 2, 'Receivable', 2, 'PartnerReceivable', N'4111', 1, N'Compensare {DocumentNumber}'),
    ('DecontCheltuieliValidat', 90, 1, 'Net', 1, 'Expense', N'628', 62, N'Decont {DocumentNumber}'),
    ('DecontCheltuieliValidat', 90, 2, 'Vat', 1, 'VatDeductible', N'4426', 0, N'Decont {DocumentNumber}'),
    ('DecontCheltuieliValidat', 90, 3, 'Total', 2, 'Fixed', N'542', 2, N'Decont {DocumentNumber}');

DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
DECLARE @New TABLE (EventType varchar(80));

INSERT INTO fin.PostingModel (EventType, Name, JournalId, IsSystem, CreatedBy)
OUTPUT inserted.EventType INTO @New
SELECT m.EventType, m.Name, j.Id, 1, 1
FROM @Models AS m
JOIN fin.Journal AS j ON j.CompanyId IS NULL AND j.CodeKey = UPPER(m.Journal)
WHERE NOT EXISTS (SELECT 1 FROM fin.PostingModel AS x WHERE x.EventType = m.EventType);

INSERT INTO fin.PostingModelVariant (ModelId, Priority, Name, RequiredFlags, ForbiddenFlags, ValidFrom, CreatedBy)
SELECT pm.Id, v.Priority, v.Name, v.RequiredFlags, v.ForbiddenFlags, '20000101', 1
FROM @Variants AS v
JOIN @New AS n ON n.EventType = v.EventType
JOIN fin.PostingModel AS pm ON pm.EventType = v.EventType;

INSERT INTO fin.PostingModelLine (VariantId, LineNumber, Component, Side, AccountKey, DefaultAccountId, CopyDimensions, DescriptionTemplate)
SELECT pv.Id, l.LineNumber, l.Component, l.Side, l.AccountKey, a.Id, l.CopyDimensions, l.DescriptionTemplate
FROM @Lines AS l
JOIN @New AS n ON n.EventType = l.EventType
JOIN fin.PostingModel AS pm ON pm.EventType = l.EventType
JOIN fin.PostingModelVariant AS pv ON pv.ModelId = pm.Id AND pv.Priority = l.Priority
LEFT JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.Code = l.DefaultCode;

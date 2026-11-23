CREATE OR ALTER PROCEDURE fin.usp_Allocation_Undo
    @CompanyId           int,
    @AllocationIds       nvarchar(max),
    @Reason              nvarchar(500),
    @AllowDocumentSource bit,
    @Today               date,
    @UserId              int
AS
BEGIN
    -- Desfacerea alocarilor (02.02, F3 3.3, 3.7), in tranzactia apelantului: statusul 2, restul de plata revine pe ambele partide, randurile
    -- de TVA la incasare se scriu inverse (sume negative), cu data desfacerii (@Today, ziua Romaniei). Alocarile din compensari, efecte sau ajustari se desfac doar prin documentul lor
    -- (@AllowDocumentSource = 1; altfel 51404); o alocare deja desfacuta sau din alta firma: 51404. Perioada inchisa o verifica apelantul.
    -- Valoarea contabila in lei a restului revine pe ambele partide, iar diferentele de curs ale alocarilor se scriu inverse (F4 4.1 pasul 7).
    -- Intoarce: 1) alocarile desfacute; 2) partidele redeschise; 3) randurile de TVA inverse; 4) diferentele de curs inverse.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Ids TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Ids (Id) SELECT DISTINCT CAST(j.value AS bigint) FROM OPENJSON(@AllocationIds) AS j;

    DECLARE @Rows TABLE (Id bigint PRIMARY KEY, CompanyId int, DebitOpenItemId bigint, CreditOpenItemId bigint, DebitAmount decimal(19,2),
                         CreditAmount decimal(19,2), Status tinyint, Source tinyint, AmountRonDebit decimal(19,2), AmountRonCredit decimal(19,2));
    INSERT INTO @Rows
    SELECT a.Id, a.CompanyId, a.DebitOpenItemId, a.CreditOpenItemId, a.DebitAmount, a.CreditAmount, a.Status, a.Source, a.AmountRonDebit, a.AmountRonCredit
    FROM fin.Allocation AS a WITH (UPDLOCK, ROWLOCK)
    WHERE a.Id IN (SELECT Id FROM @Ids);

    IF (SELECT COUNT(*) FROM @Rows) <> (SELECT COUNT(*) FROM @Ids) OR EXISTS (SELECT 1 FROM @Rows WHERE CompanyId <> @CompanyId)
        THROW 51404, N'Alocarea nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Rows WHERE Status <> 1)
        THROW 51404, N'Alocarea este deja desfăcută.', 1;
    IF @AllowDocumentSource = 0 AND EXISTS (SELECT 1 FROM @Rows WHERE Source IN (3, 5, 6))
        THROW 51404, N'Alocarea vine dintr-o compensare, un efect comercial sau o ajustare; se stornează documentul acela.', 1;

    DECLARE @Items TABLE (Id bigint PRIMARY KEY, OpenAmount decimal(19,2));
    INSERT INTO @Items (Id, OpenAmount)
    SELECT o.Id, o.OpenAmount
    FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.Id IN (SELECT DebitOpenItemId FROM @Rows UNION SELECT CreditOpenItemId FROM @Rows)
    ORDER BY o.Id;

    UPDATE a SET Status = 2, UndoneAt = SYSUTCDATETIME(), UndoneBy = @UserId, UndoReason = @Reason
    FROM fin.Allocation AS a JOIN @Rows AS r ON r.Id = a.Id;

    UPDATE o
    SET OpenAmount = o.OpenAmount + x.Delta, OpenAmountRon = o.OpenAmountRon + x.DeltaRon, ClosedAt = NULL, ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = @UserId
    FROM fin.OpenItem AS o
    JOIN (SELECT ItemId, SUM(Delta) AS Delta, SUM(DeltaRon) AS DeltaRon
          FROM (SELECT DebitOpenItemId AS ItemId, DebitAmount AS Delta, AmountRonDebit AS DeltaRon FROM @Rows
                UNION ALL
                SELECT CreditOpenItemId, -CreditAmount, -AmountRonCredit FROM @Rows
                UNION ALL
                -- Partea din reevaluare care revenise alocarii (corectia F4 de la validarea rularii) se intoarce in valoarea contabila.
                SELECT x.OpenItemId, 0, -x.DifferenceRon FROM fin.FxDifference AS x
                WHERE x.AllocationId IN (SELECT Id FROM @Rows) AND x.RevaluationRunId IS NOT NULL AND x.IsReversal = 0
                  AND NOT EXISTS (SELECT 1 FROM fin.FxDifference AS r WHERE r.ReversalOfId = x.Id)) AS u
          GROUP BY ItemId) AS x ON x.ItemId = o.Id;

    DECLARE @Touched nvarchar(max) = (SELECT Id FROM @Items FOR JSON PATH);
    EXEC fin.usp_OpenItemSchedule_Refresh @Touched;

    DECLARE @FxOut TABLE (Id bigint);
    INSERT INTO fin.FxDifference (CompanyId, DifferenceDate, Source, AllocationId, PartnerId, Role, OpenItemId, CurrencyCode, ForeignAmount, CarryingAmountRon,
        SettlementAmountRon, DifferenceRon, IsIndexed, GlAccountCode, IsReversal, ReversalOfId, RevaluationRunId, CreatedBy)
    OUTPUT inserted.Id INTO @FxOut (Id)
    SELECT x.CompanyId, @Today, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, -x.ForeignAmount, -x.CarryingAmountRon,
        -x.SettlementAmountRon, -x.DifferenceRon, x.IsIndexed, x.GlAccountCode, 1, x.Id, x.RevaluationRunId, @UserId
    FROM fin.FxDifference AS x
    WHERE x.AllocationId IN (SELECT Id FROM @Rows) AND x.IsReversal = 0 AND NOT EXISTS (SELECT 1 FROM fin.FxDifference AS r WHERE r.ReversalOfId = x.Id);

    DECLARE @VatOut TABLE (Id bigint, OpenItemId bigint, Side tinyint, ExigibilityDate date, BaseAmountRon decimal(19,2), VatAmountRon decimal(19,2));
    INSERT INTO fin.VatCashEntry (CompanyId, OpenItemId, AllocationId, Side, ExigibilityDate, BaseAmountRon, VatAmountRon, IsReversal, CreatedBy)
    OUTPUT inserted.Id, inserted.OpenItemId, inserted.Side, inserted.ExigibilityDate, inserted.BaseAmountRon, inserted.VatAmountRon INTO @VatOut
    SELECT e.CompanyId, e.OpenItemId, e.AllocationId, e.Side, @Today, -e.BaseAmountRon, -e.VatAmountRon, 1, @UserId
    FROM fin.VatCashEntry AS e
    WHERE e.AllocationId IN (SELECT Id FROM @Rows) AND e.IsReversal = 0;

    SELECT a.Id AS AllocationId, CAST(0 AS int) AS RowNo, d.PartnerId, d.Role, a.DebitOpenItemId, d.SourceDocumentType AS DebitSourceDocumentType,
        d.SourceDocumentId AS DebitSourceDocumentId, d.CurrencyCode AS DebitCurrencyCode, d.ExchangeRate AS DebitExchangeRate, a.CreditOpenItemId,
        c.SourceDocumentType AS CreditSourceDocumentType, c.SourceDocumentId AS CreditSourceDocumentId, c.CurrencyCode AS CreditCurrencyCode,
        c.ExchangeRate AS CreditExchangeRate, a.DebitAmount AS Amount, a.AmountRonDebit, a.AmountRonCredit, a.AllocationDate, a.Source, a.CreditAmount
    FROM fin.Allocation AS a
    JOIN @Rows AS r ON r.Id = a.Id
    JOIN fin.OpenItem AS d ON d.Id = a.DebitOpenItemId
    JOIN fin.OpenItem AS c ON c.Id = a.CreditOpenItemId
    ORDER BY a.Id;

    SELECT o.Id, o.PartnerId, o.Role, o.Kind, o.SourceDocumentType, o.SourceDocumentId, CAST(NULL AS date) AS ClosedAt
    FROM fin.OpenItem AS o JOIN @Items AS i ON i.Id = o.Id
    WHERE i.OpenAmount = 0 AND o.OpenAmount <> 0;

    SELECT v.Id, v.OpenItemId, o.SourceDocumentType, o.SourceDocumentId, v.Side, v.ExigibilityDate, v.BaseAmountRon, v.VatAmountRon, CAST(1 AS bit) AS IsReversal
    FROM @VatOut AS v JOIN fin.OpenItem AS o ON o.Id = v.OpenItemId;

    SELECT x.Id, x.DifferenceDate, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed,
        x.GlAccountCode, x.IsReversal
    FROM fin.FxDifference AS x JOIN @FxOut AS f ON f.Id = x.Id;
END;

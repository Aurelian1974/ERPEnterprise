CREATE OR ALTER PROCEDURE inv.usp_StockDocument_MarkValidated
    @CompanyId int,
    @Id        int,
    @Number    nvarchar(40),
    @UserId    int
AS
BEGIN
    -- Sfarsitul validarii (02.05, S2 4.1): numarul, starea Validat, costul inregistrat pe fiecare linie si totalul, din miscarile documentului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE l
    SET UnitCostPosted = m.UnitCost, CostAmountPosted = m.CostAmount
    FROM inv.StockDocumentLine AS l
    CROSS APPLY (SELECT TOP (1) x.UnitCost, x.CostAmount FROM inv.StockMovement AS x
                 WHERE x.SourceModule = 'inv' AND x.SourceDocumentId = @Id AND x.SourceLineId = ISNULL(l.ReversedLineId, l.Id)
                 ORDER BY x.SourceRole) AS m
    WHERE l.DocumentId = @Id;

    UPDATE d
    SET Number = @Number, Status = 2, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId,
        TotalCost = (SELECT SUM(ABS(CostAmountPosted)) FROM inv.StockDocumentLine WHERE DocumentId = @Id),
        IsForcedNegative = CASE WHEN EXISTS (SELECT 1 FROM inv.StockMovement WHERE SourceModule = 'inv' AND SourceDocumentId = @Id AND IsForcedNegative = 1)
                                THEN 1 ELSE 0 END
    FROM inv.StockDocument AS d
    WHERE d.Id = @Id AND d.CompanyId = @CompanyId;

    SELECT Id, RowVersion FROM inv.StockDocument WHERE Id = @Id;
END;

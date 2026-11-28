CREATE OR ALTER PROCEDURE inv.usp_StockMovement_Reverse
    @CompanyId             int,
    @SourceModule          varchar(20),
    @SourceDocumentId      bigint,
    @SourceLineIds         inv.IdList READONLY,
    @MovementDate          date,
    @ReversalDocumentType  varchar(30),
    @ReversalDocumentId    bigint,
    @ReversalDocumentNumber nvarchar(40) = NULL,
    @UserId                int
AS
BEGIN
    -- Stornarea miscarilor unui document sursa (02.05, S2 4.6): toate liniile sau doar cele date (@SourceLineIds gol = toate). Fiecare miscare
    -- primeste una de semn opus, legata de ea (ReversalOfId), cu exact valoarea originalului, ca valoarea stocului sa revina la ban (S2-A8).
    -- Stornarea unei intrari trece prin verificarea de stoc negativ. Miscare deja stornata sau nimic de stornat: 54114.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Lines inv.tt_StockMovementRequest;
    INSERT INTO @Lines (RowNo, SourceLineId, SourceRole, MovementTypeCode, DepotId, WarehouseId, ItemVariantId, QtyBase, LotId, SerialId, CostCenterId,
        ProjectId, ReasonId, ReversalOfId, FixedCostAmount)
    SELECT ROW_NUMBER() OVER (ORDER BY m.PostingSeq DESC), m.SourceLineId, m.SourceRole, t.Code, m.DepotId, m.WarehouseId, m.ItemVariantId, -m.QtyBase,
        m.LotId, m.SerialId, m.CostCenterId, m.ProjectId, m.ReasonId, m.Id, -m.CostAmount
    FROM inv.StockMovement AS m
    JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
    WHERE m.CompanyId = @CompanyId AND m.SourceModule = @SourceModule AND m.SourceDocumentId = @SourceDocumentId AND m.ReversalOfId IS NULL
      AND (NOT EXISTS (SELECT 1 FROM @SourceLineIds) OR m.SourceLineId IN (SELECT Id FROM @SourceLineIds));

    IF NOT EXISTS (SELECT 1 FROM @Lines)
        THROW 54114, N'Documentul nu are mișcări de stornat.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l JOIN inv.StockMovement AS r ON r.ReversalOfId = l.ReversalOfId)
        THROW 54114, N'Mișcarea a fost deja stornată.', 1;

    -- Stornarea pastreaza tipul originalului, cu semn opus (storno pe acelasi document de contare).
    EXEC inv.usp_StockMovement_Post @CompanyId = @CompanyId, @SourceModule = @SourceModule, @SourceDocumentType = @ReversalDocumentType,
        @SourceDocumentId = @ReversalDocumentId, @SourceDocumentNumber = @ReversalDocumentNumber, @MovementDate = @MovementDate,
        @AccountingDate = @MovementDate, @Lines = @Lines, @IsExternalFact = 0, @UserId = @UserId;
END;

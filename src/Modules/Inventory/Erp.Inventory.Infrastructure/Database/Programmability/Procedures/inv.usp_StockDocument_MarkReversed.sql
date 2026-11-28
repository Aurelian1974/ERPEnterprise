CREATE OR ALTER PROCEDURE inv.usp_StockDocument_MarkReversed
    @CompanyId  int,
    @Id         int,
    @ReversalId int,
    @Number     nvarchar(40),
    @UserId     int
AS
BEGIN
    -- Sfarsitul stornarii (02.05, S2 4.6): stornarea validata, cu costul miscarilor ei, iar originalul Stornat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC inv.usp_StockDocument_MarkValidated @CompanyId = @CompanyId, @Id = @ReversalId, @Number = @Number, @UserId = @UserId;

    UPDATE inv.StockDocument
    SET Status = 3, ReversedByDocumentId = @ReversalId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId;

    UPDATE inv.TransitShipment SET Status = 5, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE NpDocumentId = @Id;
END;

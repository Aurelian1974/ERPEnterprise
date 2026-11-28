CREATE OR ALTER PROCEDURE inv.usp_Lot_ChangeStatus
    @CompanyId          int,
    @LotId              int,
    @ToStatus           tinyint,
    @Reason             nvarchar(400) = NULL,
    @RowVersion         binary(8) = NULL,
    @SourceDocumentType varchar(30) = NULL,
    @SourceDocumentId   bigint = NULL,
    @IsSystem           bit = 0,
    @UserId             int
AS
BEGIN
    -- Schimbarea starii unui lot (02.05, S4 3.3), pe firma (in toate gestiunile): carantina, eliberarea, blocarea si deblocarea intre Disponibil,
    -- Carantina si Blocat; Expirat (jobul) si Retras (documentul RL) doar de sistem; din Expirat sau Retras nu se mai iese (54464). Istoricul si
    -- datele evenimentului LotStareSchimbata.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @From tinyint, @Version binary(8), @Technical bit;
    SELECT @From = Status, @Version = RowVersion, @Technical = IsTechnical FROM inv.Lot WITH (UPDLOCK, ROWLOCK) WHERE Id = @LotId AND CompanyId = @CompanyId;
    IF @From IS NULL
        THROW 54454, N'Lotul nu există în firma curentă.', 1;
    IF @RowVersion IS NOT NULL AND @Version <> @RowVersion
        THROW 54014, N'Lotul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Technical = 1 OR @From = @ToStatus OR @From IN (4, 5) OR (@ToStatus IN (4, 5) AND @IsSystem = 0) OR @ToStatus NOT BETWEEN 1 AND 5
        THROW 54464, N'Schimbarea stării lotului nu e permisă (din Expirat sau Retras nu se mai iese; Expirat și Retras le pune doar sistemul).', 1;

    UPDATE inv.Lot SET Status = @ToStatus, StatusReason = @Reason, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @LotId;
    INSERT INTO inv.LotStatusHistory (LotId, FromStatus, ToStatus, Reason, SourceDocumentType, SourceDocumentId, ChangedBy)
    VALUES (@LotId, @From, @ToStatus, @Reason, @SourceDocumentType, @SourceDocumentId, @UserId);

    SELECT x.Id AS LotId, x.ItemVariantId, x.LotCode, @From AS FromStatus, x.Status AS ToStatus, x.SupplierPartnerId, x.RowVersion
    FROM inv.Lot AS x WHERE x.Id = @LotId;
END;

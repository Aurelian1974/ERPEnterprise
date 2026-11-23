CREATE OR ALTER PROCEDURE fin.usp_OpenItem_SetFlags
    @CompanyId           int,
    @Id                  bigint,
    @IsDisputed          bit = NULL,
    @DisputeNote         nvarchar(500) = NULL,
    @IsBlockedForPayment bit = NULL,
    @RowVersion          binary(8),
    @UserId              int
AS
BEGIN
    -- Litigiul (F3 2 regula 7) sau blocarea la plata (F3 3.1) unei partide; parametrul NULL nu schimba marcajul. Partida a firmei (51021),
    -- versiune diferita: 51417. Blocarea la plata e doar pentru partidele de furnizor (51021).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ItemCompanyId int, @Role tinyint, @Version binary(8);
    SELECT @ItemCompanyId = CompanyId, @Role = Role, @Version = RowVersion FROM fin.OpenItem WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @ItemCompanyId IS NULL OR @ItemCompanyId <> @CompanyId
        THROW 51021, N'Partida nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 51417, N'Partida a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @IsBlockedForPayment = 1 AND @Role <> 2
        THROW 51021, N'Blocarea la plată se aplică doar facturilor de furnizor.', 1;

    UPDATE fin.OpenItem
    SET IsDisputed = ISNULL(@IsDisputed, IsDisputed),
        DisputeNote = CASE WHEN @IsDisputed IS NULL THEN DisputeNote WHEN @IsDisputed = 1 THEN @DisputeNote END,
        IsBlockedForPayment = ISNULL(@IsBlockedForPayment, IsBlockedForPayment),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.RowVersion
    WHERE Id = @Id;
END;

CREATE OR ALTER PROCEDURE core.usp_NumberSeriesRule_Save
    @Id               int = NULL,
    @CompanyId        int,
    @DocumentTypeCode nvarchar(50),
    @Priority         int,
    @UserId           int = NULL,
    @BranchId         int = NULL,
    @LocationId       int = NULL,
    @PartnerScope     tinyint = NULL,
    @Prefix           nvarchar(20),
    @IsActive         bit,
    @RowVersion       binary(8) = NULL,
    @ModifiedBy       int
AS
BEGIN
    -- Regula de alegere a seriei (02.01, 3.6 punctul 16). Prefixul trebuie sa fie al unei serii a firmei pentru acelasi tip de
    -- document (din orice an), ca regula sa aiba o tinta. Punctul de lucru si locatia sunt ale firmei (cheile compuse).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @DocumentTypeId int = (SELECT Id FROM core.DocumentType WHERE Code = @DocumentTypeCode);
    IF @DocumentTypeId IS NULL
        THROW 50021, N'Tipul de document nu există.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.NumberSeries WHERE CompanyId = @CompanyId AND DocumentTypeId = @DocumentTypeId AND PrefixKey = UPPER(@Prefix))
        THROW 50021, N'Firma nu are o serie cu acest prefix pentru tipul de document ales.', 1;
    IF @BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId)
        THROW 50021, N'Punctul de lucru nu este al firmei.', 1;
    IF @LocationId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @LocationId AND CompanyId = @CompanyId)
        THROW 50021, N'Locația nu este a firmei.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.NumberSeriesRule (CompanyId, DocumentTypeId, Priority, UserId, BranchId, LocationId, PartnerScope, Prefix, IsActive, CreatedBy)
        VALUES (@CompanyId, @DocumentTypeId, @Priority, @UserId, @BranchId, @LocationId, @PartnerScope, @Prefix, @IsActive, @ModifiedBy);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.NumberSeriesRule
        SET DocumentTypeId = @DocumentTypeId, Priority = @Priority, UserId = @UserId, BranchId = @BranchId, LocationId = @LocationId,
            PartnerScope = @PartnerScope, Prefix = @Prefix, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.NumberSeriesRule WHERE Id = @Id AND CompanyId = @CompanyId)
                THROW 50027, N'Regula nu există.', 1;
            THROW 50006, N'Regula a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.NumberSeriesRule WHERE Id = @Id;
END;

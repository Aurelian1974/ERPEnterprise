CREATE OR ALTER PROCEDURE core.usp_NumberSeries_Save
    @Id               int = NULL,
    @CompanyId        int,
    @BranchId         int = NULL,
    @DocumentTypeCode nvarchar(50),
    @Prefix           nvarchar(20),
    @Year             smallint,
    @NextNumber       bigint,
    @Padding          tinyint,
    @IsFiscal         bit,
    @IsDefault        bit,
    @AutoRollover     bit,
    @RowVersion       binary(8) = NULL,
    @UserId           int
AS
BEGIN
    -- Seria de numerotare (02.01, 3.5 si 4.2): pe firma, punct de lucru optional, tip de document si an. Dupa prima
    -- folosire, o serie fiscala nu-si mai schimba tipul, punctul de lucru, prefixul, anul sau urmatorul numar; o serie
    -- nefiscala folosita poate doar sari inainte. O serie inchisa nu se mai modifica. Seria implicita noua o inlocuieste
    -- pe cea veche (aceeasi firma, punct de lucru, tip si an).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @DocumentTypeId int = (SELECT Id FROM core.DocumentType WHERE Code = @DocumentTypeCode);
    IF @DocumentTypeId IS NULL
        THROW 50021, N'Tipul de document nu există.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId AND IsActive = 1)
        THROW 50021, N'Firma nu există sau este inactivă.', 1;

    IF @BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId)
        THROW 50021, N'Punctul de lucru nu aparține firmei.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @Old TABLE (BranchId int NULL, DocumentTypeId int, PrefixKey nvarchar(20), [Year] smallint, NextNumber bigint,
            FirstNumber bigint, IsFiscal bit, ClosedAt datetime2(3) NULL);
        INSERT INTO @Old
        SELECT BranchId, DocumentTypeId, PrefixKey, [Year], NextNumber, FirstNumber, IsFiscal, ClosedAt
        FROM core.NumberSeries WITH (UPDLOCK)
        WHERE Id = @Id AND CompanyId = @CompanyId;

        IF @@ROWCOUNT = 0
            THROW 50027, N'Seria nu există în această firmă.', 1;

        IF EXISTS (SELECT 1 FROM @Old WHERE ClosedAt IS NOT NULL)
            THROW 50025, N'Seria este închisă și nu se mai modifică.', 1;

        IF EXISTS (SELECT 1 FROM @Old AS o WHERE o.NextNumber > o.FirstNumber AND o.IsFiscal = 1
                   AND (o.BranchId IS DISTINCT FROM @BranchId OR o.DocumentTypeId <> @DocumentTypeId OR o.PrefixKey <> UPPER(@Prefix)
                        OR o.[Year] <> @Year OR o.NextNumber <> @NextNumber OR @IsFiscal = 0))
            THROW 50024, N'Seria fiscală a fost folosită: tipul, punctul de lucru, prefixul, anul și următorul număr nu se mai schimbă. Închideți-o și deschideți alta.', 1;

        IF EXISTS (SELECT 1 FROM @Old AS o WHERE o.NextNumber > o.FirstNumber AND @NextNumber < o.NextNumber)
            THROW 50024, N'Seria a fost folosită: următorul număr nu poate coborî sub cel curent.', 1;
    END;

    IF EXISTS (SELECT 1 FROM core.NumberSeries WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND BranchId IS NOT DISTINCT FROM @BranchId AND DocumentTypeId = @DocumentTypeId
                 AND PrefixKey = UPPER(@Prefix) AND [Year] = @Year AND (@Id IS NULL OR Id <> @Id))
        THROW 50026, N'Există deja o serie cu acest prefix pentru tipul de document, punctul de lucru și anul ales.', 1;

    IF @IsDefault = 1
        UPDATE core.NumberSeries
        SET IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE CompanyId = @CompanyId AND BranchId IS NOT DISTINCT FROM @BranchId AND DocumentTypeId = @DocumentTypeId
          AND [Year] = @Year AND IsDefault = 1 AND ClosedAt IS NULL AND (@Id IS NULL OR Id <> @Id);

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.NumberSeries (CompanyId, BranchId, DocumentTypeId, Prefix, [Year], NextNumber, FirstNumber, Padding,
            IsFiscal, IsDefault, AutoRollover, CreatedBy)
        VALUES (@CompanyId, @BranchId, @DocumentTypeId, @Prefix, @Year, @NextNumber, @NextNumber, @Padding,
            @IsFiscal, @IsDefault, @AutoRollover, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.NumberSeries
        SET BranchId = @BranchId, DocumentTypeId = @DocumentTypeId, Prefix = @Prefix, [Year] = @Year,
            -- Inainte de prima folosire, numarul de pornire se poate schimba.
            FirstNumber = CASE WHEN NextNumber = FirstNumber THEN @NextNumber ELSE FirstNumber END,
            NextNumber = @NextNumber, Padding = @Padding, IsFiscal = @IsFiscal, IsDefault = @IsDefault,
            AutoRollover = @AutoRollover, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 50006, N'Seria a fost modificată între timp (de alt utilizator sau de o validare); reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM core.NumberSeries WHERE Id = @Id;
END;

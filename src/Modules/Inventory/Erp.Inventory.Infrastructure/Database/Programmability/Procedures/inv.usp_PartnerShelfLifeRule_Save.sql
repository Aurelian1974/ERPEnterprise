CREATE OR ALTER PROCEDURE inv.usp_PartnerShelfLifeRule_Save
    @CompanyId        int,
    @PartnerId        int,
    @ItemId           int = NULL,
    @ItemGroupId      int = NULL,
    @MinRemainingDays int = NULL,
    @UserId           int
AS
BEGIN
    -- Durata minima ramasa ceruta de un client (02.05, S4 3.2, decizia 4), pe model sau pe grupa de articole; fara zile, regula se sterge.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId) OR (@ItemId IS NULL AND @ItemGroupId IS NULL) OR (@ItemId IS NOT NULL AND @ItemGroupId IS NOT NULL)
        THROW 54018, N'Regula cere clientul și fie modelul, fie grupa de articole.', 1;

    IF @MinRemainingDays IS NULL
        DELETE FROM inv.PartnerShelfLifeRule
        WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND ItemKey = ISNULL(@ItemId, 0) AND ItemGroupKey = ISNULL(@ItemGroupId, 0);
    ELSE
        MERGE inv.PartnerShelfLifeRule WITH (HOLDLOCK) AS target
        USING (SELECT @CompanyId AS CompanyId, @PartnerId AS PartnerId, @ItemId AS ItemId, @ItemGroupId AS ItemGroupId) AS source
        ON target.CompanyId = source.CompanyId AND target.PartnerId = source.PartnerId AND target.ItemKey = ISNULL(source.ItemId, 0)
           AND target.ItemGroupKey = ISNULL(source.ItemGroupId, 0)
        WHEN MATCHED THEN UPDATE SET MinRemainingDays = @MinRemainingDays, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHEN NOT MATCHED THEN INSERT (CompanyId, PartnerId, ItemId, ItemGroupId, MinRemainingDays, CreatedBy)
            VALUES (@CompanyId, @PartnerId, @ItemId, @ItemGroupId, @MinRemainingDays, @UserId);

    SELECT r.Id, r.PartnerId, p.Name AS PartnerName, r.ItemId, i.Name AS ItemName, r.ItemGroupId, g.Name AS ItemGroupName, r.MinRemainingDays
    FROM inv.PartnerShelfLifeRule AS r
    JOIN core.Partner AS p ON p.Id = r.PartnerId
    LEFT JOIN core.Item AS i ON i.Id = r.ItemId
    LEFT JOIN core.ItemGroup AS g ON g.Id = r.ItemGroupId
    WHERE r.CompanyId = @CompanyId AND r.PartnerId = @PartnerId
    ORDER BY p.Name, i.Name, g.Name;
END;

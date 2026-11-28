CREATE OR ALTER PROCEDURE inv.usp_ItemSerialIdentifierRule_Save
    @ItemId            int = NULL,
    @ItemGroupId       int = NULL,
    @TypeCode          nvarchar(20),
    @SortOrder         int = 100,
    @RequiredAtReceipt bit = 0,
    @RequiredAtIssue   bit = 0,
    @IsUnique          bit = 0,
    @OnDocuments       bit = 0,
    @Delete            bit = 0,
    @UserId            int
AS
BEGIN
    -- Regula unui identificator pe model sau pe grupa de articole (02.05, S4 3.4, Inv.Settings.SerialIdentifiers): ordinea, obligativitatea la
    -- intrare sau la iesire, unicitatea, afisarea pe documente. Regulile modelului au prioritate fata de cele ale grupei.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Type int = (SELECT Id FROM inv.SerialIdentifierType WHERE CodeKey = UPPER(@TypeCode));
    IF @Type IS NULL
        THROW 54471, N'Tipul identificatorului nu există.', 1;
    IF (@ItemId IS NULL AND @ItemGroupId IS NULL) OR (@ItemId IS NOT NULL AND @ItemGroupId IS NOT NULL)
        THROW 54018, N'Regula se dă fie pe model, fie pe grupa de articole.', 1;

    IF @Delete = 1
        DELETE FROM inv.ItemSerialIdentifierRule WHERE ItemKey = ISNULL(@ItemId, 0) AND ItemGroupKey = ISNULL(@ItemGroupId, 0) AND IdentifierTypeId = @Type;
    ELSE
        MERGE inv.ItemSerialIdentifierRule WITH (HOLDLOCK) AS target
        USING (SELECT @ItemId AS ItemId, @ItemGroupId AS ItemGroupId, @Type AS IdentifierTypeId) AS source
        ON target.ItemKey = ISNULL(source.ItemId, 0) AND target.ItemGroupKey = ISNULL(source.ItemGroupId, 0) AND target.IdentifierTypeId = source.IdentifierTypeId
        WHEN MATCHED THEN UPDATE SET SortOrder = @SortOrder, RequiredAtReceipt = @RequiredAtReceipt, RequiredAtIssue = @RequiredAtIssue, IsUnique = @IsUnique,
            OnDocuments = @OnDocuments, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHEN NOT MATCHED THEN INSERT (ItemId, ItemGroupId, IdentifierTypeId, SortOrder, RequiredAtReceipt, RequiredAtIssue, IsUnique, OnDocuments, CreatedBy)
            VALUES (@ItemId, @ItemGroupId, @Type, @SortOrder, @RequiredAtReceipt, @RequiredAtIssue, @IsUnique, @OnDocuments, @UserId);

    SELECT r.Id, r.ItemId, r.ItemGroupId, t.Code AS TypeCode, r.SortOrder, r.RequiredAtReceipt, r.RequiredAtIssue, r.IsUnique, r.OnDocuments
    FROM inv.ItemSerialIdentifierRule AS r JOIN inv.SerialIdentifierType AS t ON t.Id = r.IdentifierTypeId
    WHERE r.ItemKey = ISNULL(@ItemId, 0) AND r.ItemGroupKey = ISNULL(@ItemGroupId, 0)
    ORDER BY r.SortOrder, t.Code;
END;

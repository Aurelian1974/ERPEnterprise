CREATE OR ALTER PROCEDURE core.usp_ItemBlock_Save
    @ItemId  int,
    @BlockedFor tinyint,
    @Reason     nvarchar(300) = NULL,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Blocarea partiala a articolului (masca: 1 vanzare, 2 achizitie). Motivul e obligatoriu la blocare (validat in
    -- handler); deblocarea (0) sterge motivul. Concurenta optimista pe RowVersion-ul blocarii (50006).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId)
        THROW 50027, N'Articolul nu există.', 1;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();
    IF @BlockedFor = 0
        SET @Reason = NULL;

    IF EXISTS (SELECT 1 FROM core.ItemBlock WITH (UPDLOCK, HOLDLOCK) WHERE ItemId = @ItemId)
    BEGIN
        UPDATE core.ItemBlock
        SET BlockedFor = @BlockedFor, Reason = @Reason,
            BlockedAt = CASE WHEN @BlockedFor = 0 THEN NULL WHEN BlockedFor = 0 OR BlockedAt IS NULL THEN @Now ELSE BlockedAt END,
            ModifiedAt = @Now, ModifiedBy = @UserId
        WHERE ItemId = @ItemId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 50006, N'Blocarea a fost modificată de alt utilizator. Reîncărcați fișa.', 1;
    END
    ELSE
        INSERT INTO core.ItemBlock (ItemId, BlockedFor, Reason, BlockedAt, ModifiedAt, ModifiedBy)
        VALUES (@ItemId, @BlockedFor, @Reason, CASE WHEN @BlockedFor = 0 THEN NULL ELSE @Now END, @Now, @UserId);

    SELECT @ItemId AS Id, RowVersion FROM core.ItemBlock WHERE ItemId = @ItemId;
END;

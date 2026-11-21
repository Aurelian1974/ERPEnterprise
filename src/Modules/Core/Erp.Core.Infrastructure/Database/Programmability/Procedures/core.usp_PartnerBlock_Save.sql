CREATE OR ALTER PROCEDURE core.usp_PartnerBlock_Save
    @PartnerId  int,
    @BlockedFor tinyint,
    @Reason     nvarchar(300) = NULL,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Blocarea partiala a partenerului (masca: 1 vanzare, 2 achizitie, 4 plati). Motivul e obligatoriu la blocare (validat in
    -- handler); deblocarea (0) sterge motivul. Concurenta optimista pe RowVersion-ul blocarii (50006).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 50027, N'Partenerul nu există.', 1;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();
    IF @BlockedFor = 0
        SET @Reason = NULL;

    IF EXISTS (SELECT 1 FROM core.PartnerBlock WITH (UPDLOCK, HOLDLOCK) WHERE PartnerId = @PartnerId)
    BEGIN
        UPDATE core.PartnerBlock
        SET BlockedFor = @BlockedFor, Reason = @Reason,
            BlockedAt = CASE WHEN @BlockedFor = 0 THEN NULL WHEN BlockedFor = 0 OR BlockedAt IS NULL THEN @Now ELSE BlockedAt END,
            ModifiedAt = @Now, ModifiedBy = @UserId
        WHERE PartnerId = @PartnerId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 50006, N'Blocarea a fost modificată de alt utilizator. Reîncărcați fișa.', 1;
    END
    ELSE
        INSERT INTO core.PartnerBlock (PartnerId, BlockedFor, Reason, BlockedAt, ModifiedAt, ModifiedBy)
        VALUES (@PartnerId, @BlockedFor, @Reason, CASE WHEN @BlockedFor = 0 THEN NULL ELSE @Now END, @Now, @UserId);

    SELECT @PartnerId AS Id, RowVersion FROM core.PartnerBlock WHERE PartnerId = @PartnerId;
END;

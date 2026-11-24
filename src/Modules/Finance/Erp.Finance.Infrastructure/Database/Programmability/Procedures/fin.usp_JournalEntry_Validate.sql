CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Validate
    @CompanyId   int,
    @Id          bigint,
    @RowVersion  binary(8),
    @FourEyes    bit,
    @UserId      int
AS
BEGIN
    -- Valideaza o nota din ciorna (C2 4.3): doar ciornele (51850), perioada care primeste note (deschisa sau inchisa operativ, 51848), patru
    -- ochi daca e pornit (autorul nu valideaza, 51853), verificarile din fin.usp_JournalEntry_Check, apoi numarul din secventa jurnalului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Status tinyint, @Version binary(8), @Date date, @Journal int, @Author int;
    SELECT @Status = Status, @Version = RowVersion, @Date = AccountingDate, @Journal = JournalId, @Author = CreatedBy
    FROM fin.JournalEntry WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;

    IF @Status IS NULL
        THROW 51841, N'Nota nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51842, N'Nota a fost modificată de altcineva între timp.', 1;
    IF @Status <> 1
        THROW 51850, N'Nota e deja validată.', 1;
    IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND StartDate <= @Date AND EndDate >= @Date AND Status >= 3)
        THROW 51848, N'Data contabilă e într-o lună închisă.', 1;
    IF @FourEyes = 1 AND @Author = @UserId
        THROW 51853, N'Nota se validează de alt utilizator decât autorul (patru ochi).', 1;

    EXEC fin.usp_JournalEntry_Check @EntryId = @Id;

    DECLARE @Number int, @Year smallint = YEAR(@Date);
    EXEC fin.usp_JournalSequence_Next @CompanyId = @CompanyId, @JournalId = @Journal, @Year = @Year, @Number = @Number OUTPUT;

    UPDATE fin.JournalEntry
    SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ApprovedBy = CASE WHEN @FourEyes = 1 THEN @UserId END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;
    EXEC fin.usp_AltMapping_Apply @EntryId = @Id;
    COMMIT TRANSACTION;

    EXEC fin.usp_JournalEntry_Header @Id = @Id;
END;

CREATE OR ALTER PROCEDURE fin.usp_RecurringEntryTemplate_Save
    @Id           int = NULL,
    @CompanyId    int,
    @Name         nvarchar(200),
    @JournalId    int,
    @Periodicity  tinyint,
    @NextDate     date,
    @EndDate      date = NULL,
    @Description  nvarchar(400) = NULL,
    @Lines        nvarchar(max),
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Sablonul unei note recurente (C2 3.8): jurnalul al instalarii sau al firmei (51021), RowVersion la modificare (51842).
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.Journal WHERE Id = @JournalId AND IsActive = 1 AND (CompanyId IS NULL OR CompanyId = @CompanyId))
        THROW 51021, N'Jurnalul nu există.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.RecurringEntryTemplate (CompanyId, Name, JournalId, Periodicity, NextDate, EndDate, Description, Lines, CreatedBy)
        VALUES (@CompanyId, @Name, @JournalId, @Periodicity, @NextDate, @EndDate, @Description, @Lines, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE fin.RecurringEntryTemplate
        SET Name = @Name, JournalId = @JournalId, Periodicity = @Periodicity, NextDate = @NextDate, EndDate = @EndDate, Description = @Description,
            Lines = @Lines, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 51842, N'Șablonul a fost modificat între timp sau nu există.', 1;
    END;

    SELECT Id, RowVersion FROM fin.RecurringEntryTemplate WHERE Id = @Id;
END;

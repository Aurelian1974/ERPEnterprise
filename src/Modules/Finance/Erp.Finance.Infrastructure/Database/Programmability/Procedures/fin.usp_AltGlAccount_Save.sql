CREATE OR ALTER PROCEDURE fin.usp_AltGlAccount_Save
    @Id          int = NULL,
    @ChartId     int,
    @ParentId    int = NULL,
    @Code        nvarchar(20),
    @Name        nvarchar(200),
    @IsActive    bit,
    @RowVersion  binary(8) = NULL,
    @UserId      int
AS
BEGIN
    -- Un cont al unui plan alternativ (C1 4.2): codul liber, unic in plan (51803), parintele din acelasi plan (51805), nivelul dupa parinte.
    -- Un cont folosit ca tinta de o regula activa nu se dezactiveaza (51810). Frunzele planului se recalculeaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.ChartOfAccounts WHERE Id = @ChartId AND Kind = 2)
        THROW 51812, N'Conturile planului românesc se modifică din Plan de conturi.', 1;
    IF @ParentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fin.GlAccount WHERE Id = @ParentId AND ChartId = @ChartId AND (@Id IS NULL OR Id <> @Id))
        THROW 51805, N'Contul părinte nu e în acest plan.', 1;
    IF EXISTS (SELECT 1 FROM fin.GlAccount WHERE ChartId = @ChartId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 51803, N'Codul există deja în plan.', 1;
    IF @Id IS NOT NULL AND @IsActive = 0 AND EXISTS (SELECT 1 FROM fin.AccountMappingTarget AS t JOIN fin.AccountMappingRule AS r ON r.Id = t.RuleId
                                                     WHERE t.TargetAccountId = @Id AND r.IsActive = 1)
        THROW 51810, N'Contul e folosit de o regulă de mapare activă; schimbați regula întâi.', 1;

    DECLARE @Level tinyint = ISNULL((SELECT Level + 1 FROM fin.GlAccount WHERE Id = @ParentId), 1);

    BEGIN TRANSACTION;
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.GlAccount (ChartId, Code, Name, ParentId, Level, IsActive, CreatedBy)
        VALUES (@ChartId, @Code, @Name, @ParentId, @Level, @IsActive, @UserId);
        SET @Id = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM fin.GlAccount WHERE Id = @Id AND Name <> @Name)
            INSERT INTO fin.GlAccountNameHistory (AccountId, Name, ValidFrom, ValidTo, ChangedBy)
            SELECT Id, Name, ISNULL((SELECT MAX(ValidTo) FROM fin.GlAccountNameHistory WHERE AccountId = @Id), CreatedAt), SYSUTCDATETIME(), @UserId
            FROM fin.GlAccount WHERE Id = @Id;

        UPDATE fin.GlAccount
        SET Code = @Code, Name = @Name, ParentId = @ParentId, Level = @Level, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND ChartId = @ChartId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 51802, N'Contul a fost modificat de altcineva între timp.', 1;
    END;
    EXEC fin.usp_GlAccount_RefreshPostable @ChartId = @ChartId;
    COMMIT TRANSACTION;

    SELECT Id, RowVersion FROM fin.GlAccount WHERE Id = @Id;
END;

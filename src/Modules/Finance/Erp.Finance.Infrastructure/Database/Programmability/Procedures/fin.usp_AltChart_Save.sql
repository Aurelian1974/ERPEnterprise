CREATE OR ALTER PROCEDURE fin.usp_AltChart_Save
    @Id                      int = NULL,
    @Code                    nvarchar(20),
    @Name                    nvarchar(200),
    @Purpose                 tinyint,
    @LanguageCode            char(2),
    @RequireCompleteMapping  bit,
    @IsActive                bit,
    @Companies               nvarchar(max),
    @RowVersion              binary(8) = NULL,
    @UserId                  int
AS
BEGIN
    -- Un plan alternativ (C1 3.1, 4.2): adaugare sau modificare cu RowVersion (51802), codul unic (51803), firmele pentru care se calculeaza
    -- maparea (JSON [id]; lista goala = toate). Planul romanesc nu se modifica de aici (51812).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM fin.ChartOfAccounts WHERE Id = @Id AND Kind = 1)
        THROW 51812, N'Planul românesc nu se modifică din planurile alternative.', 1;
    IF EXISTS (SELECT 1 FROM fin.ChartOfAccounts WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 51803, N'Există deja un plan cu acest cod.', 1;

    DECLARE @Firms TABLE (CompanyId int PRIMARY KEY);
    INSERT INTO @Firms (CompanyId) SELECT DISTINCT CAST(value AS int) FROM OPENJSON(@Companies);
    IF EXISTS (SELECT 1 FROM @Firms AS f WHERE NOT EXISTS (SELECT 1 FROM core.Company AS c WHERE c.Id = f.CompanyId))
        THROW 51021, N'Firma nu există.', 1;

    BEGIN TRANSACTION;
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.ChartOfAccounts (Code, Name, Kind, Purpose, LanguageCode, RequireCompleteMapping, IsActive, CreatedBy)
        VALUES (@Code, @Name, 2, @Purpose, @LanguageCode, @RequireCompleteMapping, @IsActive, @UserId);
        SET @Id = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
    BEGIN
        UPDATE fin.ChartOfAccounts
        SET Code = @Code, Name = @Name, Purpose = @Purpose, LanguageCode = @LanguageCode, RequireCompleteMapping = @RequireCompleteMapping,
            IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND Kind = 2 AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 51802, N'Planul a fost modificat de altcineva între timp.', 1;
    END;

    DELETE FROM fin.ChartOfAccountsCompany WHERE ChartId = @Id AND CompanyId NOT IN (SELECT CompanyId FROM @Firms);
    INSERT INTO fin.ChartOfAccountsCompany (ChartId, CompanyId)
    SELECT @Id, f.CompanyId FROM @Firms AS f WHERE NOT EXISTS (SELECT 1 FROM fin.ChartOfAccountsCompany AS x WHERE x.ChartId = @Id AND x.CompanyId = f.CompanyId);
    COMMIT TRANSACTION;

    SELECT Id, RowVersion FROM fin.ChartOfAccounts WHERE Id = @Id;
END;

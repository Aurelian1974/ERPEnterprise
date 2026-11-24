CREATE OR ALTER PROCEDURE fin.usp_FinancialStatement_Save
    @CompanyId   int,
    @Year        smallint,
    @Kind        tinyint,
    @Category    tinyint,
    @IsBalanced  bit,
    @Lines       nvarchar(max),
    @UserId      int
AS
BEGIN
    -- Calculul situatiilor (C5 3.6, 4.2): ciorna creata sau recalculata (inapoi in Ciorna, fisierul generat se sterge); depuse nu se mai
    -- recalculeaza (51978). Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @Id int, @Status tinyint;
    SELECT @Id = Id, @Status = Status FROM fin.FinancialStatement WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND Year = @Year AND Kind = @Kind;
    IF @Status = 4
        THROW 51978, N'Situațiile financiare au fost depuse; nu se mai modifică.', 1;
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.FinancialStatement (CompanyId, Year, Kind, EntityCategory, IsBalanced, CalculatedAt, CalculatedBy)
        VALUES (@CompanyId, @Year, @Kind, @Category, @IsBalanced, SYSUTCDATETIME(), @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.FinancialStatement SET EntityCategory = @Category, IsBalanced = @IsBalanced, Status = 1, CalculatedAt = SYSUTCDATETIME(), CalculatedBy = @UserId,
            GeneratedAt = NULL, FileContent = NULL, FileName = NULL
        WHERE Id = @Id;
    DELETE FROM fin.FinancialStatementLine WHERE StatementId = @Id;
    INSERT INTO fin.FinancialStatementLine (StatementId, FormCode, RowCode, Label, SortOrder, IsTotal, CurrentYear, PriorYear)
    SELECT @Id, FormCode, RowCode, Label, SortOrder, IsTotal, CurrentYear, PriorYear
    FROM OPENJSON(@Lines) WITH (FormCode varchar(10) '$.formCode', RowCode varchar(30) '$.rowCode', Label nvarchar(300) '$.label', SortOrder int '$.sortOrder',
        IsTotal bit '$.isTotal', CurrentYear decimal(19,2) '$.currentYear', PriorYear decimal(19,2) '$.priorYear');
    COMMIT;
    SELECT @Id;
END;

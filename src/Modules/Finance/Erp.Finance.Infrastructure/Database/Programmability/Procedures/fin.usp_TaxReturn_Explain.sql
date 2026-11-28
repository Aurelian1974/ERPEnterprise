CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_Explain
    @CompanyId    int,
    @Id           int,
    @RowCode      varchar(30),
    @ColumnCode   varchar(20),
    @Explanation  nvarchar(1000),
    @UserId       int
AS
BEGIN
    -- Explicatia unei diferente fata de P300 (C4 3.4, decizia 5); explicatia goala e refuzata (51946).
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@Explanation)), N'') IS NULL
        THROW 51946, N'Scrieți explicația diferenței.', 1;
    DECLARE @Status tinyint = (SELECT Status FROM fin.TaxReturn WHERE Id = @Id AND CompanyId = @CompanyId);
    IF @Status IS NULL
        THROW 51941, N'Declarația nu există.', 1;
    IF @Status >= 4
        THROW 51947, N'Declarația e depusă; o corecție se face prin rectificativă.', 1;
    UPDATE fin.TaxReturnComparison SET Explanation = @Explanation, ExplainedAt = SYSUTCDATETIME(), ExplainedBy = @UserId
    WHERE ReturnId = @Id AND RowCode = @RowCode AND ColumnCode = @ColumnCode;
    IF @@ROWCOUNT = 0
        THROW 51941, N'Rândul comparației nu există.', 1;
END;

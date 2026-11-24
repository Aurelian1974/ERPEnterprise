CREATE OR ALTER PROCEDURE fin.usp_ProfitTax_Save
    @CompanyId  int,
    @Year       smallint,
    @Quarter    tinyint,
    @Regime     tinyint,
    @TaxRate    decimal(5,2),
    @TaxDue     decimal(19,2),
    @Lines      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Fisa de calcul a trimestrului (C4 3.6, 4.4 pasul 1): creata sau recalculata; toate randurile se rescriu (cele manuale vin cu valoarea si
    -- motivul lor). Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @Id int = (SELECT Id FROM fin.ProfitTaxComputation WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND Year = @Year AND Quarter = @Quarter);
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.ProfitTaxComputation (CompanyId, Year, Quarter, Regime, TaxRate, TaxDue, CalculatedAt, CalculatedBy)
        VALUES (@CompanyId, @Year, @Quarter, @Regime, @TaxRate, @TaxDue, SYSUTCDATETIME(), @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.ProfitTaxComputation SET Regime = @Regime, TaxRate = @TaxRate, TaxDue = @TaxDue, CalculatedAt = SYSUTCDATETIME(), CalculatedBy = @UserId
        WHERE Id = @Id;
    DELETE FROM fin.ProfitTaxComputationLine WHERE ComputationId = @Id;
    INSERT INTO fin.ProfitTaxComputationLine (ComputationId, RowCode, Label, SortOrder, IsManual, Amount, Reason)
    SELECT @Id, RowCode, Label, SortOrder, IsManual, Amount, Reason
    FROM OPENJSON(@Lines) WITH (RowCode varchar(30) '$.rowCode', Label nvarchar(200) '$.label', SortOrder int '$.sortOrder', IsManual bit '$.isManual',
        Amount decimal(19,2) '$.amount', Reason nvarchar(300) '$.reason');
    COMMIT;
    SELECT @Id;
END;

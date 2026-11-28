CREATE OR ALTER PROCEDURE fin.usp_FinancialStatement_Transition
    @CompanyId           int,
    @Year                smallint,
    @Kind                tinyint,
    @RowVersion          binary(8),
    @Action              varchar(10),
    @FileName            nvarchar(260) = NULL,
    @FileContent         varbinary(max) = NULL,
    @RegistrationNumber  nvarchar(50) = NULL,
    @UserId              int
AS
BEGIN
    -- Generarea fisierului (decizia 5: doar pe anul inchis; raportarea semestriala doar cu iunie inchisa; 51976) si a bilantului echilibrat (51977),
    -- apoi depunerea cu numarul din recipisa. Intoarce RowVersion-ul nou.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @Id int, @Status tinyint, @Version binary(8), @Balanced bit;
    SELECT @Id = Id, @Status = Status, @Version = RowVersion, @Balanced = IsBalanced
    FROM fin.FinancialStatement WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND Year = @Year AND Kind = @Kind;
    IF @Id IS NULL
        THROW 51971, N'Situațiile financiare nu sunt calculate.', 1;
    IF @Version <> @RowVersion
        THROW 51972, N'Situațiile au fost modificate între timp.', 1;
    IF @Status = 4
        THROW 51978, N'Situațiile financiare au fost depuse; nu se mai modifică.', 1;
    IF @Action = 'Generate'
    BEGIN
        DECLARE @YearStatus tinyint, @YearStart date;
        SELECT @YearStatus = Status, @YearStart = StartDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND Year = @Year;
        IF (@Kind = 1 AND ISNULL(@YearStatus, 1) < 2)
            OR (@Kind = 2 AND NOT EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND EndDate = EOMONTH(DATEADD(MONTH, 5, @YearStart)) AND Status >= 3))
            THROW 51976, N'Fișierul se generează doar pe anul închis (raportarea semestrială, cu iunie închisă).', 1;
        IF @Balanced = 0
            THROW 51977, N'Bilanțul nu e echilibrat: totalul activelor diferă de capitaluri și datorii.', 1;
        UPDATE fin.FinancialStatement SET Status = 3, GeneratedAt = SYSUTCDATETIME(), FileName = @FileName, FileContent = @FileContent WHERE Id = @Id;
    END
    ELSE IF @Action = 'Submit'
    BEGIN
        IF @Status <> 3
            THROW 51981, N'Depunerea se înregistrează doar după generarea fișierului.', 1;
        UPDATE fin.FinancialStatement SET Status = 4, SubmittedAt = SYSUTCDATETIME(), RegistrationNumber = @RegistrationNumber WHERE Id = @Id;
    END
    ELSE
        THROW 51971, N'Pasul nu există.', 1;
    COMMIT;
    SELECT RowVersion FROM fin.FinancialStatement WHERE Id = @Id;
END;

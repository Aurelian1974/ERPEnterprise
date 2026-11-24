CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_Transition
    @CompanyId           int,
    @Id                  int,
    @RowVersion          binary(8),
    @Action              varchar(20),
    @Hash                int = NULL,
    @RegistrationNumber  nvarchar(50) = NULL,
    @UserId              int
AS
BEGIN
    -- Pasii declaratiei (C4 3.3): Verify (ciorna → verificata), Generate (verificata → generata: lunile perioadei inchise, 51943; datele
    -- neschimbate de la calcul, 51944), Submit (generata → depusa, cu numarul de inregistrare; 51948). RowVersion (51942); depusa nu se mai
    -- modifica (51947). La depunere, declaratia cu suma de plata creeaza obligatia si suma planificata in prognoza de trezorerie (F5).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Status tinyint, @Version binary(8), @From date, @To date, @Stored int, @Form varchar(10), @Due date, @Amount decimal(19,2);
    SELECT @Status = Status, @Version = RowVersion, @From = PeriodFrom, @To = PeriodTo, @Stored = CalculationHash, @Form = FormCode, @Due = DueDate, @Amount = AmountDue
    FROM fin.TaxReturn WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 51941, N'Declarația nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51942, N'Declarația a fost modificată între timp.', 1;
    IF @Status >= 4
        THROW 51947, N'Declarația e depusă; o corecție se face prin rectificativă.', 1;

    IF @Action = 'Verify'
    BEGIN
        IF EXISTS (SELECT 1 FROM fin.TaxReturnComparison WHERE ReturnId = @Id AND IsOverThreshold = 1 AND Explanation IS NULL)
            THROW 51946, N'Explicați diferențele față de decontul precompletat peste prag.', 1;
        UPDATE fin.TaxReturn SET Status = 2, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    END
    ELSE IF @Action = 'Generate'
    BEGIN
        IF @Status < 2
            THROW 51948, N'Verificați declarația înainte de generare.', 1;
        IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND StartDate <= @To AND EndDate >= @From AND Status < 3)
            OR NOT EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND StartDate <= @To AND EndDate >= @From)
            THROW 51943, N'Fișierul final se generează doar după închiderea lunilor perioadei.', 1;
        IF @Hash IS NULL OR @Hash <> @Stored
            THROW 51944, N'Datele s-au schimbat de la calcul; recalculați declarația.', 1;
        UPDATE fin.TaxReturn SET Status = 3, GeneratedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    END
    ELSE IF @Action = 'Submit'
    BEGIN
        IF @Status <> 3
            THROW 51948, N'Depunerea se înregistrează doar pentru o declarație generată.', 1;
        UPDATE fin.TaxReturn SET Status = 4, SubmittedAt = SYSUTCDATETIME(), SubmittedBy = @UserId, RegistrationNumber = @RegistrationNumber,
            NeedsReview = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DECLARE @Budget tinyint = (SELECT BudgetKind FROM fin.TaxForm WHERE Code = @Form);
        IF @Budget IS NOT NULL AND @Amount > 0
        BEGIN
            DECLARE @Category int = (SELECT Id FROM fin.CashFlowCategory WHERE CodeKey = N'TAXE');
            DECLARE @PlanId int = NULL;
            IF @Category IS NOT NULL
            BEGIN
                INSERT INTO fin.CashFlowPlanItem (CompanyId, CategoryId, Description, CurrencyCode, Amount, FirstDate, Recurrence, Probability, CreatedBy)
                VALUES (@CompanyId, @Category, CONCAT(@Form, N' ', CONVERT(char(7), @From, 126)), 'RON', @Amount, ISNULL(@Due, DATEADD(MONTH, 1, @To)), 0, 100, @UserId);
                SET @PlanId = SCOPE_IDENTITY();
            END;
            INSERT INTO fin.TaxObligation (CompanyId, ReturnId, BudgetKind, Amount, DueDate, PlanItemId)
            VALUES (@CompanyId, @Id, @Budget, @Amount, ISNULL(@Due, DATEADD(MONTH, 1, @To)), @PlanId);
        END;
    END
    ELSE
        THROW 51941, N'Pasul declarației nu există.', 1;
    COMMIT TRANSACTION;

    SELECT Id, FormCode, PeriodFrom, PeriodTo, Status, AmountDue, DueDate, RowVersion FROM fin.TaxReturn WHERE Id = @Id;
END;

CREATE OR ALTER PROCEDURE fin.usp_ClosingEntry_Generate
    @CompanyId   int,
    @PeriodId    int,
    @Kind        tinyint,
    @AnnualOnly  bit = 0,
    @Preview     bit = 0,
    @UserId      int
AS
BEGIN
    -- O nota de inchidere a lunii (C3 3.5), datata in ultima zi, jurnalul IN, SourceKind 4, fara dimensiuni pe linii. Soldurile se iau din anul
    -- financiar pana la sfarsitul lunii, fara nota de inchidere a acestei luni (la reinchidere). Kind 1, regularizarea TVA (doar in ultima luna a
    -- perioadei de TVA): 4427 = 4426, diferenta pe 4423 (rolul VatPayable) sau 4424 (VatReceivable). Kind 2, inchiderea claselor 6 si 7 in 121:
    -- conturile cu ClosingTarget 1 lunar (in decembrie si cele anuale, 2; cu @AnnualOnly toate doar in ultima luna a anului). Cu @Preview intoarce
    -- liniile; altfel scrie nota noua (validata, numerotata) sau rescrie nota existenta a lunii (acelasi numar, istoric) si intoarce
    -- (EntryId, IsNew, OldVersion, OldTotal). Fara solduri de inchis nu scrie nimic.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Start date, @End date, @PeriodNo tinyint, @YearStart date, @YearEnd date, @VatPeriodicity tinyint;
    SELECT @Start = p.StartDate, @End = p.EndDate, @PeriodNo = p.PeriodNo, @YearStart = y.StartDate, @YearEnd = y.EndDate, @VatPeriodicity = y.VatPeriodicity
    FROM fin.AccountingPeriod AS p JOIN fin.FiscalYear AS y ON y.Id = p.FiscalYearId WHERE p.Id = @PeriodId AND p.CompanyId = @CompanyId;
    IF @Start IS NULL
        THROW 51901, N'Perioada nu există.', 1;

    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    DECLARE @YearEndMonth bit = CASE WHEN @End = @YearEnd THEN 1 ELSE 0 END;
    DECLARE @Lines TABLE (LineNumber int IDENTITY(1,1), AccountId int, Side tinyint, AmountRon decimal(19,2));

    DECLARE @Net TABLE (AccountId int PRIMARY KEY, Code nvarchar(20), Net decimal(19,2), Target tinyint);
    INSERT INTO @Net
    SELECT a.Id, a.Code, SUM(l.DebitRon - l.CreditRon), ISNULL(NULLIF(a.ClosingTarget, 0), s.ClosingTarget)
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
    LEFT JOIN fin.GlAccount AS s ON s.ChartId = @Statutory AND s.CodeKey = UPPER(LEFT(a.Code, CHARINDEX(N'.', a.Code + N'.') - 1))
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @End
      AND NOT EXISTS (SELECT 1 FROM fin.PeriodClosingEntry AS pc WHERE pc.EntryId = e.Id AND pc.PeriodId = @PeriodId)
      AND ((@Kind = 1 AND (a.Code LIKE N'4427%' OR a.Code LIKE N'4426%')) OR (@Kind = 2 AND LEFT(a.Code, 1) IN (N'6', N'7')))
    GROUP BY a.Id, a.Code, ISNULL(NULLIF(a.ClosingTarget, 0), s.ClosingTarget)
    HAVING SUM(l.DebitRon - l.CreditRon) <> 0;

    IF @Kind = 1
    BEGIN
        IF @PeriodNo % @VatPeriodicity = 0
        BEGIN
            INSERT INTO @Lines (AccountId, Side, AmountRon)
            SELECT AccountId, CASE WHEN Net < 0 THEN 1 ELSE 2 END, ABS(Net) FROM @Net ORDER BY Code;
            DECLARE @Difference decimal(19,2) = -(SELECT ISNULL(SUM(Net), 0) FROM @Net);
            IF @Difference <> 0
            BEGIN
                DECLARE @Role varchar(40) = CASE WHEN @Difference > 0 THEN 'VatPayable' ELSE 'VatReceivable' END;
                DECLARE @Account int = (SELECT TOP (1) AccountId FROM fin.GlAccountRoleAssignment
                                        WHERE Role = @Role AND (CompanyId IS NULL OR CompanyId = @CompanyId) AND ValidFrom <= @End AND (ValidTo IS NULL OR ValidTo >= @End)
                                        ORDER BY CASE WHEN CompanyId IS NULL THEN 1 ELSE 0 END, ValidFrom DESC);
                IF @Account IS NULL
                BEGIN
                    DECLARE @NoRole nvarchar(200) = CONCAT(N'Rolul ', @Role, N' nu are cont pentru regularizarea TVA.');
                    THROW 51844, @NoRole, 1;
                END;
                INSERT INTO @Lines (AccountId, Side, AmountRon) VALUES (@Account, CASE WHEN @Difference > 0 THEN 2 ELSE 1 END, ABS(@Difference));
            END;
        END;
    END
    ELSE
    BEGIN
        DELETE FROM @Net WHERE NOT ((Target = 1 AND (@AnnualOnly = 0 OR @YearEndMonth = 1)) OR (Target = 2 AND @YearEndMonth = 1));
        DECLARE @Result int = (SELECT TOP (1) AccountId FROM fin.GlAccountRoleAssignment
                               WHERE Role = 'ProfitOrLoss' AND (CompanyId IS NULL OR CompanyId = @CompanyId) AND ValidFrom <= @End AND (ValidTo IS NULL OR ValidTo >= @End)
                               ORDER BY CASE WHEN CompanyId IS NULL THEN 1 ELSE 0 END, ValidFrom DESC);
        IF @Result IS NULL AND EXISTS (SELECT 1 FROM @Net)
            THROW 51844, N'Rolul ProfitOrLoss (121) nu are cont pentru închiderea lunii.', 1;
        DECLARE @Expenses decimal(19,2) = (SELECT ISNULL(SUM(Net), 0) FROM @Net WHERE Net > 0);
        DECLARE @Revenues decimal(19,2) = (SELECT ISNULL(SUM(-Net), 0) FROM @Net WHERE Net < 0);
        IF @Expenses <> 0
            INSERT INTO @Lines (AccountId, Side, AmountRon) VALUES (@Result, 1, @Expenses);
        INSERT INTO @Lines (AccountId, Side, AmountRon) SELECT AccountId, 2, Net FROM @Net WHERE Net > 0 ORDER BY Code;
        INSERT INTO @Lines (AccountId, Side, AmountRon) SELECT AccountId, 1, -Net FROM @Net WHERE Net < 0 ORDER BY Code;
        IF @Revenues <> 0
            INSERT INTO @Lines (AccountId, Side, AmountRon) VALUES (@Result, 2, @Revenues);
    END;

    IF @Preview = 1
    BEGIN
        SELECT a.Code AS AccountCode, a.Name AS AccountName, x.Side, x.AmountRon FROM @Lines AS x JOIN fin.GlAccount AS a ON a.Id = x.AccountId ORDER BY x.LineNumber;
        RETURN;
    END;

    IF NOT EXISTS (SELECT 1 FROM @Lines)
    BEGIN
        SELECT CAST(NULL AS bigint) AS EntryId, CAST(0 AS bit) AS IsNew, 0 AS OldVersion, CAST(0 AS decimal(19,2)) AS OldTotal;
        RETURN;
    END;

    DECLARE @Total decimal(19,2) = (SELECT SUM(AmountRon) FROM @Lines WHERE Side = 1);
    DECLARE @Description nvarchar(400) = CASE @Kind WHEN 1 THEN N'Regularizarea TVA' ELSE N'Închiderea veniturilor și cheltuielilor în 121' END;
    DECLARE @Outer int = @@TRANCOUNT;
    IF @Outer = 0 BEGIN TRANSACTION;
    DECLARE @EntryId bigint = (SELECT EntryId FROM fin.PeriodClosingEntry WITH (UPDLOCK, HOLDLOCK) WHERE PeriodId = @PeriodId AND Kind = @Kind);
    DECLARE @IsNew bit = CASE WHEN @EntryId IS NULL THEN 1 ELSE 0 END, @OldVersion int = 0, @OldTotal decimal(19,2) = 0;

    IF @EntryId IS NULL
    BEGIN
        DECLARE @Journal int = (SELECT Id FROM fin.Journal WHERE CompanyId IS NULL AND CodeKey = N'IN');
        INSERT INTO fin.JournalEntry (CompanyId, JournalId, EntryDate, AccountingDate, PeriodYear, PeriodMonth, SourceKind, SourceModule, SourceDocumentType,
            SourceDocumentId, Status, Description, TotalDebit, TotalCredit, CreatedBy)
        VALUES (@CompanyId, @Journal, @End, @End, YEAR(@End), MONTH(@End), 4, 'Finance', 'Finance.PeriodClosing', @PeriodId, 1, @Description, @Total, @Total, @UserId);
        SET @EntryId = SCOPE_IDENTITY();
        INSERT INTO fin.PeriodClosingEntry (PeriodId, Kind, CompanyId, EntryId) VALUES (@PeriodId, @Kind, @CompanyId, @EntryId);
    END
    ELSE
    BEGIN
        SELECT @OldTotal = TotalDebit FROM fin.JournalEntry WHERE Id = @EntryId;
        SET @OldVersion = ISNULL((SELECT MAX(Version) FROM fin.JournalEntryHistory WHERE EntryId = @EntryId), 0) + 1;
        INSERT INTO fin.JournalEntryHistory (EntryId, Version, Reason, TotalDebit, Lines, ChangedBy)
        SELECT @EntryId, @OldVersion, N'Reînchiderea lunii', @OldTotal,
            (SELECT a.Code AS accountCode, l.Side AS side, l.AmountRon AS amountRon FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
             WHERE l.EntryId = @EntryId ORDER BY l.LineNumber FOR JSON PATH), @UserId;
        DELETE FROM fin.JournalLine WHERE EntryId = @EntryId;
        UPDATE fin.JournalEntry SET TotalDebit = @Total, TotalCredit = @Total, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @EntryId;
    END;

    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, Description)
    SELECT @EntryId, @CompanyId, YEAR(@End), MONTH(@End), LineNumber, AccountId, Side, AmountRon, @Description FROM @Lines;
    EXEC fin.usp_JournalEntry_Check @EntryId = @EntryId;

    IF @IsNew = 1
    BEGIN
        DECLARE @Number int, @Year smallint = YEAR(@End), @JournalId int = (SELECT JournalId FROM fin.JournalEntry WHERE Id = @EntryId);
        EXEC fin.usp_JournalSequence_Next @CompanyId = @CompanyId, @JournalId = @JournalId, @Year = @Year, @Number = @Number OUTPUT;
        UPDATE fin.JournalEntry SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId WHERE Id = @EntryId;
    END;
    EXEC fin.usp_AltMapping_Apply @EntryId = @EntryId;
    IF @Outer = 0 COMMIT TRANSACTION;

    SELECT @EntryId AS EntryId, @IsNew AS IsNew, @OldVersion AS OldVersion, @OldTotal AS OldTotal;
END;

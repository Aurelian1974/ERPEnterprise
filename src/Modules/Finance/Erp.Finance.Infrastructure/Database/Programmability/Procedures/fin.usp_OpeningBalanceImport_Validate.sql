CREATE OR ALTER PROCEDURE fin.usp_OpeningBalanceImport_Validate
    @CompanyId  int,
    @Id         int,
    @UserId     int
AS
BEGIN
    -- Validarea importului (C3 3.7 pasii 3–4), intr-o tranzactie: nota de deschidere cu soldurile initiale ale anului (prima zi a anului) si nota
    -- de deschidere cu rulajele cumulate (ultima zi a lunii dinaintea startului, cand startul nu e in ianuarie); lunile anului de dinaintea
    -- startului se inchid ca importate; soldurile clientilor, furnizorilor si caselor trebuie sa fie egale cu evidenta operativa preluata
    -- (aceleasi concordante ca la inchidere, la ziua dinaintea startului; 51915). Anul financiar al startului trebuie sa existe (51901).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Start date, @Status tinyint;
    SELECT @Start = StartDate, @Status = Status FROM fin.OpeningBalanceImport WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL OR @Status <> 1
        THROW 51916, N'Importul nu există sau nu e o ciornă.', 1;
    DECLARE @YearId int, @YearStart date;
    SELECT @YearId = Id, @YearStart = StartDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate <= @Start AND EndDate >= @Start;
    IF @YearId IS NULL
        THROW 51901, N'Creați întâi anul financiar al lunii de start.', 1;

    DECLARE @Opening nvarchar(max) = (
        SELECT AccountId AS accountId, CASE WHEN OpeningDebit - OpeningCredit > 0 THEN 1 ELSE 2 END AS side, ABS(OpeningDebit - OpeningCredit) AS amountRon,
            PartnerId AS partnerId, CurrencyCode AS currencyCode, CASE WHEN CurrencyCode IS NOT NULL THEN ABS(AmountCurrency) END AS amountCurrency
        FROM fin.OpeningBalanceImportRow WHERE ImportId = @Id AND OpeningDebit <> OpeningCredit ORDER BY RowNumber FOR JSON PATH);
    DECLARE @Turnover nvarchar(max) = (
        SELECT x.AccountId AS accountId, x.Side AS side, x.Amount AS amountRon, x.PartnerId AS partnerId
        FROM (SELECT RowNumber, AccountId, PartnerId, 1 AS Side, TurnoverDebit AS Amount FROM fin.OpeningBalanceImportRow WHERE ImportId = @Id AND TurnoverDebit <> 0
              UNION ALL
              SELECT RowNumber, AccountId, PartnerId, 2, TurnoverCredit FROM fin.OpeningBalanceImportRow WHERE ImportId = @Id AND TurnoverCredit <> 0) AS x
        ORDER BY x.RowNumber, x.Side FOR JSON PATH);

    BEGIN TRANSACTION;
    DECLARE @Written TABLE (EntryId bigint, IsNew bit, OldVersion int, OldTotal decimal(19,2));
    DECLARE @OpeningEntry bigint, @TurnoverEntry bigint, @Before date = DATEADD(DAY, -1, @Start);
    IF @Opening IS NOT NULL
    BEGIN
        INSERT INTO @Written EXEC fin.usp_SystemEntry_Write @CompanyId = @CompanyId, @AccountingDate = @YearStart, @SourceKind = 5,
            @SourceDocumentType = 'Finance.OpeningBalanceImport', @SourceDocumentId = @Id, @Description = N'Solduri inițiale preluate', @Lines = @Opening,
            @Reason = N'', @UserId = @UserId;
        SET @OpeningEntry = (SELECT TOP (1) EntryId FROM @Written);
        DELETE FROM @Written;
    END;
    IF @Turnover IS NOT NULL AND @Start > @YearStart
    BEGIN
        INSERT INTO @Written EXEC fin.usp_SystemEntry_Write @CompanyId = @CompanyId, @AccountingDate = @Before, @SourceKind = 5,
            @SourceDocumentType = 'Finance.OpeningBalanceImport', @SourceDocumentId = @Id, @Description = N'Rulaje cumulate preluate', @Lines = @Turnover,
            @Reason = N'', @UserId = @UserId;
        SET @TurnoverEntry = (SELECT TOP (1) EntryId FROM @Written);
    END;

    UPDATE fin.AccountingPeriod SET Status = 3, IsImported = 1, ClosedAt = SYSUTCDATETIME(), ClosedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE FiscalYearId = @YearId AND EndDate < @Start AND Status < 3;

    DECLARE @Differences TABLE (Area tinyint, KeyId int, KeyName nvarchar(200), Ledger decimal(19,2), Subledger decimal(19,2), Difference decimal(19,2));
    INSERT INTO @Differences EXEC fin.usp_Reconciliation_Get @CompanyId = @CompanyId, @AsOf = @Before, @Explain = 0;
    DECLARE @Mismatch nvarchar(400) = (SELECT TOP (1) CONCAT(CASE Area WHEN 1 THEN N'Clienți' WHEN 2 THEN N'Furnizori' ELSE N'Casa' END, N' · ', ISNULL(KeyName, N'—'),
        N': soldul importat ', Ledger, N', evidența preluată ', Subledger, N'.') FROM @Differences ORDER BY Area, KeyName);
    IF @Mismatch IS NOT NULL
        THROW 51915, @Mismatch, 1;

    UPDATE fin.OpeningBalanceImport
    SET Status = 2, OpeningEntryId = @OpeningEntry, TurnoverEntryId = @TurnoverEntry, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;
    COMMIT TRANSACTION;
END;

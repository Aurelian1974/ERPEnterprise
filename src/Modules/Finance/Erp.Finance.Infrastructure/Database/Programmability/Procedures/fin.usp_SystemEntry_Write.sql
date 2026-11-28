CREATE OR ALTER PROCEDURE fin.usp_SystemEntry_Write
    @CompanyId           int,
    @EntryId             bigint = NULL,
    @AccountingDate      date,
    @SourceKind          tinyint,
    @SourceDocumentType  varchar(50),
    @SourceDocumentId    bigint,
    @Description         nvarchar(400),
    @Lines               nvarchar(max),
    @Reason              nvarchar(300),
    @UserId              int
AS
BEGIN
    -- O nota de sistem a inchiderilor (C3 3.5, 3.6) in jurnalul IN: noua (validata si numerotata) sau, cu @EntryId, rescrisa pe loc (acelasi numar,
    -- versiunea veche in fin.JournalEntryHistory cu @Reason). Liniile: JSON [{accountId, side, amountRon, partnerId, employeeId, currencyCode,
    -- amountCurrency}]. Luna datei trebuie sa primeasca note (51848); verificarile din fin.usp_JournalEntry_Check; maparea in planurile
    -- alternative. Intoarce (EntryId, IsNew, OldVersion, OldTotal).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @New TABLE (LineNumber int, AccountId int, Side tinyint, AmountRon decimal(19,2), PartnerId int, EmployeeId int, CurrencyCode char(3),
        AmountCurrency decimal(19,2));
    INSERT INTO @New
    SELECT CAST(x.[key] AS int) + 1, j.AccountId, j.Side, j.AmountRon, j.PartnerId, j.EmployeeId, j.CurrencyCode, j.AmountCurrency
    FROM OPENJSON(@Lines) AS x
    CROSS APPLY OPENJSON(x.value) WITH (AccountId int '$.accountId', Side tinyint '$.side', AmountRon decimal(19,2) '$.amountRon', PartnerId int '$.partnerId',
        EmployeeId int '$.employeeId', CurrencyCode char(3) '$.currencyCode', AmountCurrency decimal(19,2) '$.amountCurrency') AS j;
    DECLARE @Total decimal(19,2) = (SELECT ISNULL(SUM(AmountRon), 0) FROM @New WHERE Side = 1);

    DECLARE @Outer int = @@TRANCOUNT;
    IF @Outer = 0 BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND StartDate <= @AccountingDate AND EndDate >= @AccountingDate AND Status >= 3)
        THROW 51848, N'Luna notei de închidere e închisă.', 1;

    DECLARE @IsNew bit = CASE WHEN @EntryId IS NULL THEN 1 ELSE 0 END, @OldVersion int = 0, @OldTotal decimal(19,2) = 0;
    IF @EntryId IS NULL
    BEGIN
        DECLARE @Journal int = (SELECT Id FROM fin.Journal WHERE CompanyId IS NULL AND CodeKey = N'IN');
        INSERT INTO fin.JournalEntry (CompanyId, JournalId, EntryDate, AccountingDate, PeriodYear, PeriodMonth, SourceKind, SourceModule, SourceDocumentType,
            SourceDocumentId, Status, Description, TotalDebit, TotalCredit, CreatedBy)
        VALUES (@CompanyId, @Journal, @AccountingDate, @AccountingDate, YEAR(@AccountingDate), MONTH(@AccountingDate), @SourceKind, 'Finance', @SourceDocumentType,
            @SourceDocumentId, 1, @Description, @Total, @Total, @UserId);
        SET @EntryId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        SELECT @OldTotal = TotalDebit FROM fin.JournalEntry WITH (UPDLOCK) WHERE Id = @EntryId AND CompanyId = @CompanyId;
        SET @OldVersion = ISNULL((SELECT MAX(Version) FROM fin.JournalEntryHistory WHERE EntryId = @EntryId), 0) + 1;
        INSERT INTO fin.JournalEntryHistory (EntryId, Version, Reason, TotalDebit, Lines, ChangedBy)
        SELECT @EntryId, @OldVersion, @Reason, @OldTotal,
            (SELECT a.Code AS accountCode, l.Side AS side, l.AmountRon AS amountRon, l.PartnerId AS partnerId FROM fin.JournalLine AS l
             JOIN fin.GlAccount AS a ON a.Id = l.AccountId WHERE l.EntryId = @EntryId ORDER BY l.LineNumber FOR JSON PATH), @UserId;
        DELETE FROM fin.JournalLine WHERE EntryId = @EntryId;
        UPDATE fin.JournalEntry SET TotalDebit = @Total, TotalCredit = @Total, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @EntryId;
    END;

    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, Description)
    SELECT @EntryId, @CompanyId, YEAR(@AccountingDate), MONTH(@AccountingDate), LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, @Description
    FROM @New;
    EXEC fin.usp_JournalEntry_Check @EntryId = @EntryId;

    IF @IsNew = 1
    BEGIN
        DECLARE @Number int, @Year smallint = YEAR(@AccountingDate), @JournalId int = (SELECT JournalId FROM fin.JournalEntry WHERE Id = @EntryId);
        EXEC fin.usp_JournalSequence_Next @CompanyId = @CompanyId, @JournalId = @JournalId, @Year = @Year, @Number = @Number OUTPUT;
        UPDATE fin.JournalEntry SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId WHERE Id = @EntryId;
    END;
    EXEC fin.usp_AltMapping_Apply @EntryId = @EntryId;
    IF @Outer = 0 COMMIT TRANSACTION;

    SELECT @EntryId AS EntryId, @IsNew AS IsNew, @OldVersion AS OldVersion, @OldTotal AS OldTotal;
END;

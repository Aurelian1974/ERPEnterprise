CREATE OR ALTER PROCEDURE fin.usp_OpeningBalanceImport_Save
    @CompanyId  int,
    @StartDate  date,
    @FileName   nvarchar(260) = NULL,
    @Rows       nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Ciorna importului de solduri initiale (C3 3.7 pasii 1–3): randurile JSON [{rowNumber, accountCode, partnerId, currencyCode, amountCurrency,
    -- openingDebit, openingCredit, turnoverDebit, turnoverCredit, closingDebit, closingCredit}]. Verificari: un singur import validat pe firma
    -- (51916), conturi frunza active ale planului romanesc (51844), partenerul cerut de cont (51846), sold initial + rulaje = sold final pe rand si
    -- total debit = total credit pe fiecare coloana (51914). Intoarce Id-ul importului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM fin.OpeningBalanceImport WHERE CompanyId = @CompanyId AND Status = 2)
        THROW 51916, N'Firma are deja solduri inițiale validate; anulați importul înainte de a-l reface.', 1;

    DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    DECLARE @New TABLE (RowNumber int, AccountCode nvarchar(20), AccountId int, PartyRequirement tinyint, PartnerId int, CurrencyCode char(3), AmountCurrency decimal(19,2),
        OpeningDebit decimal(19,2), OpeningCredit decimal(19,2), TurnoverDebit decimal(19,2), TurnoverCredit decimal(19,2), ClosingDebit decimal(19,2),
        ClosingCredit decimal(19,2));
    INSERT INTO @New
    SELECT j.RowNumber, j.AccountCode, a.Id, a.PartyRequirement, j.PartnerId, NULLIF(j.CurrencyCode, ''), j.AmountCurrency, ISNULL(j.OpeningDebit, 0),
        ISNULL(j.OpeningCredit, 0), ISNULL(j.TurnoverDebit, 0), ISNULL(j.TurnoverCredit, 0), ISNULL(j.ClosingDebit, 0), ISNULL(j.ClosingCredit, 0)
    FROM OPENJSON(@Rows) WITH (RowNumber int '$.rowNumber', AccountCode nvarchar(20) '$.accountCode', PartnerId int '$.partnerId', CurrencyCode char(3) '$.currencyCode',
        AmountCurrency decimal(19,2) '$.amountCurrency', OpeningDebit decimal(19,2) '$.openingDebit', OpeningCredit decimal(19,2) '$.openingCredit',
        TurnoverDebit decimal(19,2) '$.turnoverDebit', TurnoverCredit decimal(19,2) '$.turnoverCredit', ClosingDebit decimal(19,2) '$.closingDebit',
        ClosingCredit decimal(19,2) '$.closingCredit') AS j
    LEFT JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.CodeKey = UPPER(j.AccountCode) AND a.IsPostable = 1 AND a.IsActive = 1
        AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId);

    DECLARE @Message nvarchar(400), @Row int;
    SELECT TOP (1) @Row = RowNumber, @Message = CONCAT(N'Rândul ', RowNumber, N': contul ', AccountCode, N' nu există, are analitice sau e inactiv.')
    FROM @New WHERE AccountId IS NULL ORDER BY RowNumber;
    IF @Row IS NOT NULL
        THROW 51844, @Message, 1;
    SELECT TOP (1) @Row = RowNumber, @Message = CONCAT(N'Rândul ', RowNumber, N': contul ', AccountCode, N' cere partenerul.')
    FROM @New WHERE PartyRequirement IN (1, 3) AND PartnerId IS NULL ORDER BY RowNumber;
    IF @Row IS NOT NULL
        THROW 51846, @Message, 1;
    SELECT TOP (1) @Row = RowNumber, @Message = CONCAT(N'Rândul ', RowNumber, N' (', AccountCode, N'): soldul inițial + rulajele nu dau soldul final.')
    FROM @New WHERE OpeningDebit - OpeningCredit + TurnoverDebit - TurnoverCredit <> ClosingDebit - ClosingCredit ORDER BY RowNumber;
    IF @Row IS NOT NULL
        THROW 51914, @Message, 1;
    IF (SELECT SUM(OpeningDebit) - SUM(OpeningCredit) FROM @New) <> 0 OR (SELECT SUM(TurnoverDebit) - SUM(TurnoverCredit) FROM @New) <> 0
        OR (SELECT SUM(ClosingDebit) - SUM(ClosingCredit) FROM @New) <> 0
        THROW 51914, N'Balanța importată nu e echilibrată: total debit diferă de total credit pe o coloană.', 1;

    BEGIN TRANSACTION;
    UPDATE fin.OpeningBalanceImport SET Status = 3, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE CompanyId = @CompanyId AND Status = 1;
    INSERT INTO fin.OpeningBalanceImport (CompanyId, StartDate, FileName, CreatedBy) VALUES (@CompanyId, @StartDate, @FileName, @UserId);
    DECLARE @Id int = SCOPE_IDENTITY();
    INSERT INTO fin.OpeningBalanceImportRow (ImportId, RowNumber, AccountId, PartnerId, CurrencyCode, AmountCurrency, OpeningDebit, OpeningCredit, TurnoverDebit,
        TurnoverCredit, ClosingDebit, ClosingCredit)
    SELECT @Id, RowNumber, AccountId, PartnerId, CurrencyCode, AmountCurrency, OpeningDebit, OpeningCredit, TurnoverDebit, TurnoverCredit, ClosingDebit, ClosingCredit
    FROM @New;
    COMMIT TRANSACTION;

    SELECT @Id;
END;

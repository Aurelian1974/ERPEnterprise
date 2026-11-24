CREATE OR ALTER PROCEDURE fin.usp_CashFlow_Actuals
    @CompanyId          int,
    @From               date,
    @To                 date,
    @TreasuryAccountId  int = NULL,
    @CurrencyCode       char(3) = NULL
AS
BEGIN
    -- Incasarile si platile reale (F5 4.5) din conturile incluse in prognoza: documentele de casa validate (si stornarile lor) si liniile
    -- de extras validate, pe parti, cu categoria tipului de operatiune (NULL = necategorizat). Casa are suma in lei; banca are suma in
    -- moneda contului (AmountRon NULL: se converteste in C#).
    SET NOCOUNT ON;

    SELECT d.DocumentDate AS [Date], t.CashFlowCategoryId AS CategoryId, a.CurrencyCode,
           CASE WHEN d.Kind = 3 THEN -l.Amount ELSE l.Amount END AS Amount,
           CAST(CASE WHEN d.Kind = 3 THEN -l.AmountRon ELSE l.AmountRon END AS decimal(19,2)) AS AmountRon
    FROM fin.CashDocument AS d
    JOIN fin.CashDocumentLine AS l ON l.CashDocumentId = d.Id
    JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
    JOIN fin.TreasuryAccount AS a ON a.Id = d.TreasuryAccountId
    WHERE d.CompanyId = @CompanyId AND d.Status IN (2, 3) AND d.Kind IN (1, 2, 3) AND d.DocumentDate BETWEEN @From AND @To
      AND a.IncludeInForecast = 1 AND (@TreasuryAccountId IS NULL OR a.Id = @TreasuryAccountId) AND (@CurrencyCode IS NULL OR a.CurrencyCode = @CurrencyCode)

    UNION ALL

    SELECT l.BookingDate, t.CashFlowCategoryId, a.CurrencyCode, p.Amount, CAST(NULL AS decimal(19,2))
    FROM fin.BankStatementLine AS l
    JOIN fin.BankStatementLinePart AS p ON p.BankStatementLineId = l.Id
    JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
    JOIN fin.TreasuryAccount AS a ON a.Id = l.TreasuryAccountId
    WHERE a.CompanyId = @CompanyId AND l.MatchStatus = 4 AND l.BookingDate BETWEEN @From AND @To
      AND a.IncludeInForecast = 1 AND (@TreasuryAccountId IS NULL OR a.Id = @TreasuryAccountId) AND (@CurrencyCode IS NULL OR a.CurrencyCode = @CurrencyCode);
END;

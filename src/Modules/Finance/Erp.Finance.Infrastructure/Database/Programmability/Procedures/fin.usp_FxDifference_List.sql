CREATE OR ALTER PROCEDURE fin.usp_FxDifference_List
    @CompanyId     int,
    @From          date,
    @To            date,
    @Source        tinyint = NULL,
    @CurrencyCode  char(3) = NULL,
    @PartnerId     int = NULL,
    @Search        nvarchar(100) = NULL
AS
BEGIN
    -- Diferentele de curs realizate pe un interval (F4 10; macheta f4-02), cele mai noi intai (cel mult 1000), cu originea: partida si
    -- documentul pereche al alocarii, sau conturile schimbului valutar; corectiile reevaluarii (F4 4.6) sunt marcate.
    SET NOCOUNT ON;

    SELECT TOP (1000) x.Id, x.DifferenceDate, x.Source, x.PartnerId, p.Name AS PartnerName, x.Role, x.OpenItemId, o.DocumentNo, x.AllocationId,
        other.DocumentNo AS CounterDocumentNo, ta.Code AS AccountCode, ca.Code AS CounterAccountCode, x.CurrencyCode, x.ForeignAmount, x.CarryingAmountRon,
        x.SettlementAmountRon, x.DifferenceRon, x.GlAccountCode, x.IsIndexed, x.IsReversal,
        CAST(CASE WHEN x.RevaluationRunId IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsCorrection
    FROM fin.FxDifference AS x
    LEFT JOIN core.Partner AS p ON p.Id = x.PartnerId
    LEFT JOIN fin.OpenItem AS o ON o.Id = x.OpenItemId
    LEFT JOIN fin.Allocation AS a ON a.Id = x.AllocationId
    LEFT JOIN fin.OpenItem AS other ON other.Id = CASE WHEN a.DebitOpenItemId = x.OpenItemId THEN a.CreditOpenItemId ELSE a.DebitOpenItemId END
    LEFT JOIN fin.BankStatementLinePart AS bp ON bp.Id = x.BankStatementLinePartId
    LEFT JOIN fin.BankStatementLine AS bl ON bl.Id = bp.BankStatementLineId
    LEFT JOIN fin.TreasuryAccount AS ta ON ta.Id = bl.TreasuryAccountId
    LEFT JOIN fin.TreasuryAccount AS ca ON ca.Id = bp.CounterTreasuryAccountId
    WHERE x.CompanyId = @CompanyId AND x.DifferenceDate BETWEEN @From AND @To
      AND (@Source IS NULL OR x.Source = @Source) AND (@CurrencyCode IS NULL OR x.CurrencyCode = @CurrencyCode)
      AND (@PartnerId IS NULL OR x.PartnerId = @PartnerId)
      AND (@Search IS NULL OR p.Name LIKE N'%' + @Search + N'%' OR o.DocumentNo LIKE N'%' + @Search + N'%' OR other.DocumentNo LIKE N'%' + @Search + N'%')
    ORDER BY x.DifferenceDate DESC, x.Id DESC;
END;

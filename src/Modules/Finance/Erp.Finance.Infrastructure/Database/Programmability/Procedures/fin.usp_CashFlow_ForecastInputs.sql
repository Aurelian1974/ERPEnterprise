CREATE OR ALTER PROCEDURE fin.usp_CashFlow_ForecastInputs
    @CompanyId          int,
    @Start              date,
    @To                 date,
    @TreasuryAccountId  int = NULL,
    @CurrencyCode       char(3) = NULL
AS
BEGIN
    -- Datele prognozei de cash-flow (F5 4.4), pe seturi; transele, recurentele si perioadele se calculeaza in C# (CashFlowRules).
    -- 1 conturile incluse cu soldul scriptic la zi: banca = ultimul extras minus OP-urile validate neconfirmate (F5-A9: nu se mai adauga),
    --   casa = ultima zi de registru; 2 partidele deschise (fara plati), fara cele din propuneri in aprobare sau aprobate si din OP-uri
    --   in ciorna; 3 liniile propunerilor in aprobare sau aprobate; 4 OP-urile in ciorna; 5 sumele planificate active; 6 decontarile de card
    --   asteptate; 7 istoricul de plata pe intervale (firma si clientii cu partide deschise). Partidele fara cont si decontarile de card intra
    --   doar in prognoza pe toata firma.
    SET NOCOUNT ON;

    SELECT a.Id AS TreasuryAccountId, a.Code, a.Name, a.Kind, a.CurrencyCode,
           CASE WHEN a.Kind IN (1, 3, 5) THEN ISNULL(s.ClosingBalance, 0) - ISNULL(u.Amount, 0) ELSE ISNULL(r.ClosingBalance, 0) END AS Balance
    FROM fin.TreasuryAccount AS a
    OUTER APPLY (SELECT TOP (1) x.ClosingBalance FROM fin.BankStatement AS x
                 WHERE x.TreasuryAccountId = a.Id AND x.StatementDate <= @Start ORDER BY x.StatementDate DESC, x.Id DESC) AS s
    OUTER APPLY (SELECT SUM(o.Amount) AS Amount FROM fin.PaymentOrder AS o
                 WHERE o.TreasuryAccountId = a.Id AND o.Status = 2 AND o.ConfirmationStatus = 1 AND o.ReversalOfId IS NULL) AS u
    OUTER APPLY (SELECT TOP (1) d.ClosingBalance FROM fin.CashRegisterDay AS d
                 WHERE d.TreasuryAccountId = a.Id AND d.[Day] <= @Start ORDER BY d.[Day] DESC) AS r
    WHERE a.CompanyId = @CompanyId AND a.IsActive = 1 AND a.IncludeInForecast = 1
      AND (@TreasuryAccountId IS NULL OR a.Id = @TreasuryAccountId) AND (@CurrencyCode IS NULL OR a.CurrencyCode = @CurrencyCode)
    ORDER BY a.Code;

    CREATE TABLE #Drafted (OpenItemId bigint NOT NULL PRIMARY KEY);
    INSERT INTO #Drafted (OpenItemId)
    SELECT DISTINCT TRY_CAST(SUBSTRING(LTRIM(v.value), 4, 20) AS bigint)
    FROM fin.PaymentOrder AS o
    JOIN fin.PaymentOrderLine AS l ON l.PaymentOrderId = o.Id
    CROSS APPLY STRING_SPLIT(l.SettlementRef, ',') AS v
    WHERE o.CompanyId = @CompanyId AND o.Status = 1 AND l.SettlementRef IS NOT NULL AND LTRIM(v.value) LIKE 'OI:%'
      AND TRY_CAST(SUBSTRING(LTRIM(v.value), 4, 20) AS bigint) IS NOT NULL;

    SELECT i.Id AS OpenItemId, i.PartnerId, i.Role, i.Kind, i.DocumentNo, i.DueDate, i.CurrencyCode, i.OpenAmount
    FROM fin.OpenItem AS i
    WHERE @TreasuryAccountId IS NULL AND i.CompanyId = @CompanyId AND i.OpenAmount <> 0 AND i.Kind <> 5
      AND (@CurrencyCode IS NULL OR i.CurrencyCode = @CurrencyCode)
      AND NOT EXISTS (SELECT 1 FROM fin.PaymentProposalLine AS l JOIN fin.PaymentProposal AS pp ON pp.Id = l.PaymentProposalId
                      WHERE l.OpenItemId = i.Id AND l.IsActive = 1 AND pp.Status IN (2, 3))
      AND NOT EXISTS (SELECT 1 FROM #Drafted AS d WHERE d.OpenItemId = i.Id);

    SELECT l.Id AS LineId, pp.Number, pp.PlannedPaymentDate, l.PartnerId, l.TreasuryAccountId, l.CurrencyCode, l.ProposedAmount
    FROM fin.PaymentProposal AS pp
    JOIN fin.PaymentProposalLine AS l ON l.PaymentProposalId = pp.Id
    WHERE pp.CompanyId = @CompanyId AND pp.Status IN (2, 3) AND l.IsActive = 1 AND pp.PlannedPaymentDate <= @To
      AND (@TreasuryAccountId IS NULL OR l.TreasuryAccountId = @TreasuryAccountId) AND (@CurrencyCode IS NULL OR l.CurrencyCode = @CurrencyCode);

    SELECT o.Id AS PaymentOrderId, o.RequestedExecutionDate, o.PartnerId, o.TreasuryAccountId, o.CurrencyCode, o.Amount
    FROM fin.PaymentOrder AS o
    WHERE o.CompanyId = @CompanyId AND o.Status = 1 AND o.RequestedExecutionDate <= @To
      AND (@TreasuryAccountId IS NULL OR o.TreasuryAccountId = @TreasuryAccountId) AND (@CurrencyCode IS NULL OR o.CurrencyCode = @CurrencyCode);

    SELECT p.Id, p.CategoryId, c.Direction, p.Description, p.CurrencyCode, p.Amount, p.TreasuryAccountId, p.FirstDate, p.Recurrence, p.RecurrenceDay,
           p.EndDate, p.Probability
    FROM fin.CashFlowPlanItem AS p
    JOIN fin.CashFlowCategory AS c ON c.Id = p.CategoryId
    WHERE p.CompanyId = @CompanyId AND p.IsActive = 1 AND p.Probability > 0 AND p.FirstDate <= @To AND (p.EndDate IS NULL OR p.EndDate >= @Start)
      AND (@TreasuryAccountId IS NULL OR p.TreasuryAccountId = @TreasuryAccountId) AND (@CurrencyCode IS NULL OR p.CurrencyCode = @CurrencyCode);

    SELECT e.Id, e.ExpectedSettlementDate, e.GrossAmount
    FROM fin.CardSettlementExpected AS e
    WHERE @TreasuryAccountId IS NULL AND (@CurrencyCode IS NULL OR @CurrencyCode = 'RON')
      AND e.CompanyId = @CompanyId AND e.Status = 1 AND e.ExpectedSettlementDate <= @To;

    SELECT h.PartnerId, h.SampleInvoices, b.DaysFrom, b.DaysTo, b.IsUncollectible, x.AmountShare, x.AvgDaysLate
    FROM fin.PartnerPaymentBehavior AS h
    JOIN fin.PartnerPaymentBehaviorBucket AS x ON x.BehaviorId = h.Id
    JOIN fin.PaymentDelayBucket AS b ON b.Id = x.BucketId
    WHERE h.CompanyId = @CompanyId
      AND (h.PartnerId IS NULL OR EXISTS (SELECT 1 FROM fin.OpenItem AS i
                                          WHERE i.CompanyId = @CompanyId AND i.PartnerId = h.PartnerId AND i.Role = 1 AND i.OpenAmount > 0))
    ORDER BY h.PartnerId, b.SortOrder;
END;

CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_Validate
    @CompanyId   int,
    @RunId       int,
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Validarea rularii (F4 4.4 pasul 3), in tranzactia apelantului. Refuzuri: inexistenta (51601), versiune diferita (51602), nu e ciorna
    -- (51603), luna are deja o rulare validata (51604), exista o rulare validata ulterioara (51606), luna anterioara nu e reevaluata desi firma
    -- are rulari validate (51609), restul unei partide s-a schimbat la sfarsitul lunii dupa generare (51610, se regenereaza).
    -- Pe fiecare partida: valoarea contabila creste cu diferenta, mai putin partea care revine alocarilor cu data dupa sfarsitul lunii, deja
    -- inregistrate; partea aceea corecteaza diferenta lor realizata (FxDifference legat de rulare, cu data alocarii), ca totalul pe factura sa fie
    -- acelasi. Cursul contabil devine cursul rularii. Intoarce: 1) rularea; 2) totalurile pe cont, contrapartida si moneda; 3) corectiile.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Year smallint, @Month tinyint, @Version binary(8), @RevaluationDate date;
    SELECT @Status = Status, @Year = PeriodYear, @Month = PeriodMonth, @Version = RowVersion, @RevaluationDate = RevaluationDate
    FROM fin.FxRevaluationRun WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @RunId AND CompanyId = @CompanyId;

    IF @Status IS NULL
        THROW 51601, N'Rularea de reevaluare nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51602, N'Rularea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 51603, N'Doar o rulare ciornă poate fi validată.', 1;
    IF EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND PeriodYear = @Year AND PeriodMonth = @Month AND Status = 2)
        THROW 51604, N'Luna are deja o reevaluare validată.', 1;
    IF EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND Status = 2 AND PeriodYear * 12 + PeriodMonth > @Year * 12 + @Month)
        THROW 51606, N'Există o reevaluare validată pentru o lună ulterioară.', 1;
    IF EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND Status = 2)
       AND NOT EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND Status = 2
                       AND PeriodYear * 12 + PeriodMonth = @Year * 12 + @Month - 1)
        THROW 51609, N'Luna anterioară nu are reevaluarea validată; reevaluați lunile în ordine.', 1;

    -- Partidele liniilor, blocate in ordinea Id-urilor (SP-16).
    DECLARE @Items TABLE (Id bigint PRIMARY KEY, Amount decimal(19,2));
    INSERT INTO @Items (Id, Amount)
    SELECT o.Id, o.Amount FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.Id IN (SELECT OpenItemId FROM fin.FxRevaluationLine WHERE RunId = @RunId AND IsCorrection = 0)
    ORDER BY o.Id;

    -- O corectie F4 4.6 aparuta dupa generare cere regenerarea.
    IF EXISTS (SELECT 1 FROM fin.FxRevaluationCorrection AS c JOIN fin.FxRevaluationRun AS cr ON cr.Id = c.RunId
               WHERE c.CompanyId = @CompanyId AND c.AppliedInRunId IS NULL AND cr.RevaluationDate < @RevaluationDate
                 AND NOT EXISTS (SELECT 1 FROM fin.FxRevaluationLine AS l WHERE l.RunId = @RunId AND l.CorrectionId = c.Id))
       OR EXISTS (SELECT 1 FROM fin.FxRevaluationLine AS l
                  WHERE l.RunId = @RunId AND l.IsCorrection = 1
                    AND NOT EXISTS (SELECT 1 FROM fin.FxRevaluationCorrection AS c WHERE c.Id = l.CorrectionId AND c.AppliedInRunId IS NULL))
        THROW 51610, N'Corecțiile de reevaluare s-au schimbat după generare; regenerați rularea.', 1;

    -- Alocarile active cu data dupa sfarsitul lunii, pe partea fiecarei partide din linii.
    DECLARE @After TABLE (LineId bigint, OpenItemId bigint, AllocationId bigint, AllocationDate date, Amount decimal(19,2), PRIMARY KEY (LineId, AllocationId));
    INSERT INTO @After (LineId, OpenItemId, AllocationId, AllocationDate, Amount)
    SELECT l.Id, l.OpenItemId, a.Id, a.AllocationDate,
        -- La o factura indexata suma se masoara in valuta de referinta (proportional cu suma in lei).
        CASE WHEN l.IsIndexed = 1 THEN ROUND(CASE WHEN a.DebitOpenItemId = l.OpenItemId THEN a.DebitAmount ELSE a.CreditAmount END * ABS(o.IndexedAmount) / ABS(o.Amount), 2)
             ELSE CASE WHEN a.DebitOpenItemId = l.OpenItemId THEN a.DebitAmount ELSE a.CreditAmount END END
    FROM fin.FxRevaluationLine AS l
    JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
    JOIN fin.Allocation AS a ON (a.DebitOpenItemId = l.OpenItemId OR a.CreditOpenItemId = l.OpenItemId) AND a.Status = 1 AND a.AllocationDate > @RevaluationDate
    WHERE l.RunId = @RunId AND l.IsCorrection = 0;

    IF EXISTS (SELECT 1 FROM fin.FxRevaluationLine AS l
               JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
               OUTER APPLY (SELECT SUM(x.Amount) AS Paid FROM @After AS x WHERE x.LineId = l.Id) AS p
               WHERE l.RunId = @RunId AND l.IsCorrection = 0
                 AND ((l.IsIndexed = 0 AND ABS(o.OpenAmount) + ISNULL(p.Paid, 0) <> ABS(l.ForeignAmount))
                      OR (l.IsIndexed = 1 AND ABS(ABS(o.IndexedOpenAmount) + ISNULL(p.Paid, 0) - ABS(l.ForeignAmount)) > 0.05)))
        THROW 51610, N'Au apărut alocări cu data în luna reevaluată după generare; regenerați rularea.', 1;

    -- Partea din diferenta care revine fiecarei alocari ulterioare: proportional cu suma ei, iar daca partida e inchisa, ultima ia restul.
    DECLARE @Corrections TABLE (LineId bigint, OpenItemId bigint, AllocationId bigint, AllocationDate date, Amount decimal(19,2), Share decimal(19,2));
    WITH Ordered AS (
        SELECT x.*, l.DifferenceRon, ABS(l.ForeignAmount) AS ForeignAbs,
            SUM(x.Amount) OVER (PARTITION BY x.LineId ORDER BY x.AllocationDate, x.AllocationId ROWS UNBOUNDED PRECEDING) AS Running
        FROM @After AS x JOIN fin.FxRevaluationLine AS l ON l.Id = x.LineId)
    INSERT INTO @Corrections (LineId, OpenItemId, AllocationId, AllocationDate, Amount, Share)
    SELECT LineId, OpenItemId, AllocationId, AllocationDate, Amount,
        CASE WHEN Running = ForeignAbs THEN DifferenceRon - ROUND(DifferenceRon * (Running - Amount) / ForeignAbs, 2)
             ELSE ROUND(DifferenceRon * Running / ForeignAbs, 2) - ROUND(DifferenceRon * (Running - Amount) / ForeignAbs, 2) END
    FROM Ordered;

    UPDATE l
    SET AppliedDifferenceRon = l.DifferenceRon - ISNULL(c.Share, 0), PreviousCarryingRate = o.CarryingRate, PreviousRunId = o.LastRevaluationRunId
    FROM fin.FxRevaluationLine AS l
    JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
    OUTER APPLY (SELECT SUM(Share) AS Share FROM @Corrections AS c WHERE c.LineId = l.Id) AS c
    WHERE l.RunId = @RunId AND l.IsCorrection = 0;

    UPDATE o
    SET OpenAmountRon = o.OpenAmountRon + l.AppliedDifferenceRon, CarryingRate = ROUND(l.Rate / r.Multiplier, 6), LastRevaluationRunId = @RunId,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.OpenItem AS o
    JOIN fin.FxRevaluationLine AS l ON l.OpenItemId = o.Id AND l.RunId = @RunId AND l.IsCorrection = 0
    JOIN fin.FxRevaluationRate AS r ON r.RunId = @RunId AND r.CurrencyCode = l.CurrencyCode;

    DECLARE @FxOut TABLE (Id bigint);
    INSERT INTO fin.FxDifference (CompanyId, DifferenceDate, Source, AllocationId, PartnerId, Role, OpenItemId, CurrencyCode, ForeignAmount, CarryingAmountRon,
        SettlementAmountRon, DifferenceRon, IsIndexed, GlAccountCode, RevaluationRunId, CreatedBy)
    OUTPUT inserted.Id INTO @FxOut (Id)
    SELECT @CompanyId, c.AllocationDate, CASE WHEN l.IsIndexed = 1 THEN 4 ELSE 1 END, c.AllocationId, l.PartnerId, l.Role, c.OpenItemId, l.CurrencyCode, c.Amount,
        c.Share, 0, -c.Share, l.IsIndexed,
        CASE WHEN l.IsIndexed = 1 THEN CASE WHEN c.Share < 0 THEN N'768' ELSE N'668' END ELSE CASE WHEN c.Share < 0 THEN N'765' ELSE N'665' END END, @RunId, @UserId
    FROM @Corrections AS c JOIN fin.FxRevaluationLine AS l ON l.Id = c.LineId
    WHERE c.Share <> 0;

    UPDATE c SET AppliedInRunId = @RunId
    FROM fin.FxRevaluationCorrection AS c JOIN fin.FxRevaluationLine AS l ON l.CorrectionId = c.Id AND l.RunId = @RunId;

    UPDATE fin.FxRevaluationRun
    SET Status = 2,
        TotalGainRon = (SELECT ISNULL(SUM(DifferenceRon), 0) FROM fin.FxRevaluationLine WHERE RunId = @RunId AND DifferenceRon > 0),
        TotalLossRon = (SELECT ISNULL(-SUM(DifferenceRon), 0) FROM fin.FxRevaluationLine WHERE RunId = @RunId AND DifferenceRon < 0),
        ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @RunId;

    SELECT Id, Number, PeriodYear, PeriodMonth, RevaluationDate, TotalGainRon, TotalLossRon, RowVersion,
        (SELECT COUNT(*) FROM fin.FxRevaluationLine WHERE RunId = @RunId) AS LineCount
    FROM fin.FxRevaluationRun WHERE Id = @RunId;

    SELECT GlAccountCode, CounterGlAccountCode, CurrencyCode, SUM(DifferenceRon) AS DifferenceRon
    FROM fin.FxRevaluationLine WHERE RunId = @RunId AND DifferenceRon <> 0
    GROUP BY GlAccountCode, CounterGlAccountCode, CurrencyCode
    ORDER BY GlAccountCode, CounterGlAccountCode, CurrencyCode;

    SELECT x.Id, x.DifferenceDate, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed,
        x.GlAccountCode, x.IsReversal
    FROM fin.FxDifference AS x JOIN @FxOut AS f ON f.Id = x.Id;
END;

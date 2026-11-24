CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_Generate
    @CompanyId        int,
    @Year             smallint,
    @Month            tinyint,
    @RevaluationDate  date,
    @RateDate         date,
    @IncludeAdvances  bit,
    @Rates            fin.FxRateList READONLY,
    @UserId           int
AS
BEGIN
    -- Ciorna reevaluarii unei luni (F4 3.6, 4.4 pasii 1–2), in tranzactia apelantului, pe seturi: cursurile copiate in rulare si o linie pe
    -- fiecare partida in valuta cu rest la sfarsitul lunii. Restul si valoarea lui contabila la data se refac din starea curenta plus alocarile
    -- active cu data dupa sfarsitul lunii (F4-A7: o rulare generata pe 5 ale lunii urmatoare foloseste resturile de la sfarsitul lunii).
    -- Valoarea reevaluata = rest × curs / multiplicator, rotunjita; diferenta = reevaluata − contabila (pozitiv = castig). O ciorna existenta a
    -- lunii se regenereaza (liniile si cursurile se refac). Refuzuri: luna are deja o rulare validata (51604), exista o rulare validata pentru o
    -- luna ulterioara (51606). Corectiile in asteptare ale lunilor anterioare (F4 4.6) devin linii cu IsCorrection = 1. Intoarce Id-ul rularii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND PeriodYear = @Year AND PeriodMonth = @Month AND Status = 2)
        THROW 51604, N'Luna are deja o reevaluare validată.', 1;
    IF EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND Status = 2 AND PeriodYear * 12 + PeriodMonth > @Year * 12 + @Month)
        THROW 51606, N'Există o reevaluare validată pentru o lună ulterioară.', 1;

    DECLARE @RunId int = (SELECT Id FROM fin.FxRevaluationRun WITH (UPDLOCK, HOLDLOCK)
                          WHERE CompanyId = @CompanyId AND PeriodYear = @Year AND PeriodMonth = @Month AND Status = 1);
    IF @RunId IS NULL
    BEGIN
        DECLARE @Number nvarchar(40) = CONCAT(N'RV ', @Year, N'-', RIGHT(CONCAT(N'0', @Month), 2), N'/',
            1 + (SELECT COUNT(*) FROM fin.FxRevaluationRun WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND PeriodYear = @Year AND PeriodMonth = @Month));
        INSERT INTO fin.FxRevaluationRun (CompanyId, PeriodYear, PeriodMonth, RevaluationDate, RateDate, Number, IncludesAdvances, GeneratedAt, CreatedBy)
        VALUES (@CompanyId, @Year, @Month, @RevaluationDate, @RateDate, @Number, @IncludeAdvances, SYSUTCDATETIME(), @UserId);
        SET @RunId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DELETE FROM fin.FxRevaluationLine WHERE RunId = @RunId;
        DELETE FROM fin.FxRevaluationRate WHERE RunId = @RunId;
        UPDATE fin.FxRevaluationRun
        SET RateDate = @RateDate, IncludesAdvances = @IncludeAdvances, GeneratedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @RunId;
    END;

    INSERT INTO fin.FxRevaluationRate (RunId, CurrencyCode, Rate, Multiplier, RateDate, Source)
    SELECT @RunId, CurrencyCode, Rate, Multiplier, RateDate, Source FROM @Rates;

    -- Facturile in lei indexate (F4 4.2 pasul 4): restul de referinta la data (proportional cu restul in lei), la cursul valutei de referinta,
    -- cu diferenta pe 768 / 668.
    INSERT INTO fin.FxRevaluationLine (RunId, ElementKind, OpenItemId, PartnerId, Role, CurrencyCode, IsIndexed, ForeignAmount, Rate, CarryingAmountRon,
        RevaluedAmountRon, DifferenceRon, GlAccountCode, CounterGlAccountCode, CreatedBy)
    SELECT @RunId, 1, x.Id, x.PartnerId, x.Role, x.CurrencyCode, x.IsIndexed, x.ForeignAsOf, r.Rate, x.Carrying, v.Revalued, v.Revalued - x.Carrying,
        CASE WHEN x.IsIndexed = 1 THEN CASE WHEN v.Revalued - x.Carrying >= 0 THEN N'768' ELSE N'668' END
             ELSE CASE WHEN v.Revalued - x.Carrying >= 0 THEN N'765' ELSE N'665' END END, x.CounterGl, @UserId
    FROM (SELECT o.Id, o.PartnerId, o.Role, COALESCE(o.IndexCurrencyCode, o.CurrencyCode) AS CurrencyCode,
                 CAST(CASE WHEN o.IndexCurrencyCode IS NULL THEN 0 ELSE 1 END AS bit) AS IsIndexed,
                 ISNULL(o.GlAccountCode, CASE WHEN o.Role = 1 THEN N'4111' ELSE N'401' END) AS CounterGl,
                 CASE WHEN o.IndexCurrencyCode IS NULL THEN o.OpenAmount + ISNULL(d.Amount, 0) - ISNULL(c.Amount, 0)
                      ELSE ROUND(o.IndexedAmount * (o.OpenAmount + ISNULL(d.Amount, 0) - ISNULL(c.Amount, 0)) / o.Amount, 2) END AS ForeignAsOf,
                 o.OpenAmountRon + ISNULL(d.Ron, 0) - ISNULL(c.Ron, 0) AS Carrying
          FROM fin.OpenItem AS o
          OUTER APPLY (SELECT SUM(a.DebitAmount) AS Amount, SUM(a.AmountRonDebit) AS Ron FROM fin.Allocation AS a
                       WHERE a.DebitOpenItemId = o.Id AND a.Status = 1 AND a.AllocationDate > @RevaluationDate) AS d
          OUTER APPLY (SELECT SUM(a.CreditAmount) AS Amount, SUM(a.AmountRonCredit) AS Ron FROM fin.Allocation AS a
                       WHERE a.CreditOpenItemId = o.Id AND a.Status = 1 AND a.AllocationDate > @RevaluationDate) AS c
          WHERE o.CompanyId = @CompanyId AND (o.CurrencyCode <> 'RON' OR o.IndexCurrencyCode IS NOT NULL) AND o.DocumentDate <= @RevaluationDate
            AND (o.IsMonetary = 1 OR @IncludeAdvances = 1)) AS x
    JOIN @Rates AS r ON r.CurrencyCode = x.CurrencyCode
    CROSS APPLY (SELECT ROUND(x.ForeignAsOf * r.Rate / r.Multiplier, 2) AS Revalued) AS v
    WHERE x.ForeignAsOf <> 0;

    -- Conturile de trezorerie in valuta (F4 3.4, 3.6): soldul la sfarsitul lunii, valoarea lui contabila in lei si diferenta pana la cursul rularii.
    DECLARE @Treasury TABLE (TreasuryAccountId int PRIMARY KEY, Code nvarchar(30), Kind tinyint, CurrencyCode char(3), GlAccountCode nvarchar(20),
                             Balance decimal(19,2), CarryingAmountRon decimal(19,2), MissingRates int);
    INSERT INTO @Treasury EXEC fin.usp_TreasuryFx_Values @CompanyId = @CompanyId, @AsOf = @RevaluationDate;
    IF EXISTS (SELECT 1 FROM @Treasury WHERE MissingRates > 0 AND (Balance <> 0 OR CarryingAmountRon <> 0))
        THROW 51605, N'Lipsește cursul BNR pentru o mișcare a unui cont de trezorerie în valută.', 1;
    INSERT INTO fin.FxRevaluationLine (RunId, ElementKind, TreasuryAccountId, CurrencyCode, ForeignAmount, Rate, CarryingAmountRon, RevaluedAmountRon,
        DifferenceRon, GlAccountCode, CounterGlAccountCode, CreatedBy)
    SELECT @RunId, 2, t.TreasuryAccountId, t.CurrencyCode, t.Balance, r.Rate, t.CarryingAmountRon, v.Revalued, v.Revalued - t.CarryingAmountRon,
        CASE WHEN v.Revalued - t.CarryingAmountRon >= 0 THEN N'765' ELSE N'665' END, t.GlAccountCode, @UserId
    FROM @Treasury AS t
    JOIN @Rates AS r ON r.CurrencyCode = t.CurrencyCode
    CROSS APPLY (SELECT ROUND(t.Balance * r.Rate / r.Multiplier, 2) AS Revalued) AS v
    WHERE t.Balance <> 0 OR t.CarryingAmountRon <> 0;

    -- Corectiile in asteptare ale lunilor anterioare (F4 4.6 pasul 4): anuleaza reevaluarea de prisos a partilor stinse retroactiv.
    INSERT INTO fin.FxRevaluationLine (RunId, ElementKind, OpenItemId, PartnerId, Role, CurrencyCode, ForeignAmount, Rate, CarryingAmountRon, RevaluedAmountRon,
        DifferenceRon, GlAccountCode, CounterGlAccountCode, IsCorrection, CorrectionId, CorrectedRunId, CreatedBy)
    SELECT @RunId, 1, c.OpenItemId, o.PartnerId, o.Role, o.CurrencyCode, c.ForeignAmount, 0, 0, c.DifferenceRon, c.DifferenceRon,
        CASE WHEN c.DifferenceRon >= 0 THEN N'765' ELSE N'665' END, ISNULL(o.GlAccountCode, CASE WHEN o.Role = 1 THEN N'4111' ELSE N'401' END), 1, c.Id, c.RunId,
        @UserId
    FROM fin.FxRevaluationCorrection AS c
    JOIN fin.OpenItem AS o ON o.Id = c.OpenItemId
    JOIN fin.FxRevaluationRun AS cr ON cr.Id = c.RunId
    WHERE c.CompanyId = @CompanyId AND c.AppliedInRunId IS NULL AND cr.RevaluationDate < @RevaluationDate;

    SELECT @RunId AS RunId;
END;

CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_Reverse
    @CompanyId   int,
    @RunId       int,
    @RowVersion  binary(8),
    @Today       date,
    @UserId      int
AS
BEGIN
    -- Stornarea unei rulari validate (F4 3.6, 4.5), in tranzactia apelantului: valorile contabile si cursurile partidelor revin la cele dinaintea
    -- rularii (din linii), corectiile diferentelor realizate se scriu inverse (cu data stornarii) si rularea trece in Stornat. Refuzuri:
    -- inexistenta (51601), versiune diferita (51602), nu e validata (51603), exista o rulare validata ulterioara (51606), o partida a rularii a
    -- fost alocata sau desfacuta dupa validare (51611: desfaceti intai alocarea). Perioada o verifica apelantul. Intoarce: 1) rularea;
    -- 2) corectiile inverse.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Year smallint, @Month tinyint, @Version binary(8), @ValidatedAt datetime2(3);
    SELECT @Status = Status, @Year = PeriodYear, @Month = PeriodMonth, @Version = RowVersion, @ValidatedAt = ValidatedAt
    FROM fin.FxRevaluationRun WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @RunId AND CompanyId = @CompanyId;

    IF @Status IS NULL
        THROW 51601, N'Rularea de reevaluare nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51602, N'Rularea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 2
        THROW 51603, N'Doar o rulare validată poate fi stornată.', 1;
    IF EXISTS (SELECT 1 FROM fin.FxRevaluationRun WHERE CompanyId = @CompanyId AND Status = 2 AND PeriodYear * 12 + PeriodMonth > @Year * 12 + @Month)
        THROW 51606, N'Există o reevaluare validată pentru o lună ulterioară; stornați întâi luna aceea.', 1;

    DECLARE @Items TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Items (Id)
    SELECT o.Id FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.Id IN (SELECT OpenItemId FROM fin.FxRevaluationLine WHERE RunId = @RunId)
    ORDER BY o.Id;

    IF EXISTS (SELECT 1 FROM fin.Allocation AS a JOIN @Items AS i ON i.Id IN (a.DebitOpenItemId, a.CreditOpenItemId)
               WHERE a.CreatedAt > @ValidatedAt OR a.UndoneAt > @ValidatedAt)
        THROW 51611, N'O partidă a rulării a fost alocată sau desfăcută după validare; desfaceți întâi acele alocări.', 1;

    UPDATE o
    SET OpenAmountRon = o.OpenAmountRon - l.AppliedDifferenceRon, CarryingRate = ISNULL(l.PreviousCarryingRate, o.ExchangeRate),
        LastRevaluationRunId = l.PreviousRunId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.OpenItem AS o
    JOIN fin.FxRevaluationLine AS l ON l.OpenItemId = o.Id AND l.RunId = @RunId;

    DECLARE @FxOut TABLE (Id bigint);
    INSERT INTO fin.FxDifference (CompanyId, DifferenceDate, Source, AllocationId, PartnerId, Role, OpenItemId, CurrencyCode, ForeignAmount, CarryingAmountRon,
        SettlementAmountRon, DifferenceRon, IsIndexed, GlAccountCode, IsReversal, ReversalOfId, RevaluationRunId, CreatedBy)
    OUTPUT inserted.Id INTO @FxOut (Id)
    SELECT x.CompanyId, @Today, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, -x.ForeignAmount, -x.CarryingAmountRon,
        -x.SettlementAmountRon, -x.DifferenceRon, x.IsIndexed, x.GlAccountCode, 1, x.Id, @RunId, @UserId
    FROM fin.FxDifference AS x
    WHERE x.RevaluationRunId = @RunId AND x.IsReversal = 0 AND NOT EXISTS (SELECT 1 FROM fin.FxDifference AS r WHERE r.ReversalOfId = x.Id);

    UPDATE fin.FxRevaluationRun
    SET Status = 3, ReversedAt = SYSUTCDATETIME(), ReversedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @RunId;

    SELECT Id, Number, PeriodYear, PeriodMonth, RevaluationDate, TotalGainRon, TotalLossRon, RowVersion,
        (SELECT COUNT(*) FROM fin.FxRevaluationLine WHERE RunId = @RunId) AS LineCount
    FROM fin.FxRevaluationRun WHERE Id = @RunId;

    SELECT x.Id, x.DifferenceDate, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed,
        x.GlAccountCode, x.IsReversal
    FROM fin.FxDifference AS x JOIN @FxOut AS f ON f.Id = x.Id;
END;

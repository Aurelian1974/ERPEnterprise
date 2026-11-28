CREATE OR ALTER PROCEDURE fin.usp_Balance_Get
    @CompanyId       int,
    @ChartId         int = NULL,
    @From            date,
    @To              date,
    @IncludeClosing  bit = 1,
    @AccountFilter   nvarchar(20) = NULL,
    @ByPartner       bit = 0,
    @ByCostCenter    bit = 0,
    @ByProject       bit = 0
AS
BEGIN
    -- Balanta de verificare pe cerere (C5 3.2), pe set, la nivel de cont frunza: anul financiar al perioadei (o perioada nu trece peste doi ani,
    -- 51973), apoi pe cont (si pe partener, centru de cost, proiect, la cerere) soldurile initiale ale anului (notele de deschidere, SourceKind 5, din prima zi a anului; rulajele importate la punerea in functiune sunt tot note de deschidere,
    -- datate mai tarziu, deci intra la rulaje precedente),
    -- rulajele precedente (de la inceputul anului pana inainte de @From) si rulajele perioadei, din notele validate (si cele stornate: storno-ul
    -- are nota lui). Notele de inchidere (SourceKind 4, fara nota impozitului pe profit, care e o cheltuiala a perioadei) se pot exclude (C5 regula 3). Planul romanesc citeste liniile; un plan alternativ citeste
    -- maparea (C1 3.6), cu „Nemapat” pe cont NULL. La sfarsit, conturile planului (cod, denumire, tip), pentru nivelurile sintetice.
    -- Soldurile materializate ale lunilor inchise vin cu inchiderile (C3); acum totul se citeste din linii.
    SET NOCOUNT ON;

    DECLARE @YearStart date, @YearEnd date;
    SELECT @YearStart = StartDate, @YearEnd = EndDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate <= @From AND EndDate >= @From;
    IF @YearStart IS NULL
        SELECT @YearStart = DATEFROMPARTS(YEAR(@From), 1, 1), @YearEnd = DATEFROMPARTS(YEAR(@From), 12, 31);
    IF @To < @From OR @To > @YearEnd
        THROW 51973, N'Balanța e pe un singur an financiar: intervalul trece în anul următor.', 1;

    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    SET @ChartId = ISNULL(@ChartId, @Statutory);

    SELECT @YearStart AS YearStart, @YearEnd AS YearEnd, CAST(CASE WHEN @ChartId = @Statutory THEN 0 ELSE 1 END AS bit) AS IsAlternative;

    IF @ChartId = @Statutory
        SELECT a.Code AS AccountCode,
            CASE WHEN @ByPartner = 1 THEN l.PartnerId END AS PartnerId, CASE WHEN @ByCostCenter = 1 THEN l.CostCenterId END AS CostCenterId,
            CASE WHEN @ByProject = 1 THEN l.ProjectId END AS ProjectId,
            SUM(CASE WHEN (e.SourceKind = 5 AND e.AccountingDate = @YearStart) THEN l.DebitRon ELSE 0 END) AS OpeningDebit, SUM(CASE WHEN (e.SourceKind = 5 AND e.AccountingDate = @YearStart) THEN l.CreditRon ELSE 0 END) AS OpeningCredit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate < @From THEN l.DebitRon ELSE 0 END) AS PriorDebit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate < @From THEN l.CreditRon ELSE 0 END) AS PriorCredit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate >= @From THEN l.DebitRon ELSE 0 END) AS PeriodDebit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate >= @From THEN l.CreditRon ELSE 0 END) AS PeriodCredit
        FROM fin.JournalLine AS l
        JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
        JOIN fin.GlAccount AS a ON a.Id = l.AccountId
        WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @To AND (@IncludeClosing = 1 OR e.SourceKind <> 4 OR e.SourceDocumentType = 'Finance.ProfitTax')
          AND (@AccountFilter IS NULL OR a.Code LIKE @AccountFilter + N'%')
        GROUP BY a.Code, CASE WHEN @ByPartner = 1 THEN l.PartnerId END, CASE WHEN @ByCostCenter = 1 THEN l.CostCenterId END,
            CASE WHEN @ByProject = 1 THEN l.ProjectId END;
    ELSE
        SELECT t.Code AS AccountCode,
            CASE WHEN @ByPartner = 1 THEN l.PartnerId END AS PartnerId, CASE WHEN @ByCostCenter = 1 THEN l.CostCenterId END AS CostCenterId,
            CASE WHEN @ByProject = 1 THEN l.ProjectId END AS ProjectId,
            SUM(CASE WHEN (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND m.Side = 1 THEN m.AmountRon ELSE 0 END) AS OpeningDebit,
            SUM(CASE WHEN (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND m.Side = 2 THEN m.AmountRon ELSE 0 END) AS OpeningCredit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate < @From AND m.Side = 1 THEN m.AmountRon ELSE 0 END) AS PriorDebit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate < @From AND m.Side = 2 THEN m.AmountRon ELSE 0 END) AS PriorCredit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate >= @From AND m.Side = 1 THEN m.AmountRon ELSE 0 END) AS PeriodDebit,
            SUM(CASE WHEN NOT (e.SourceKind = 5 AND e.AccountingDate = @YearStart) AND e.AccountingDate >= @From AND m.Side = 2 THEN m.AmountRon ELSE 0 END) AS PeriodCredit
        FROM fin.JournalLineAltAccount AS m
        JOIN fin.JournalLine AS l ON l.Id = m.JournalLineId
        JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
        LEFT JOIN fin.GlAccount AS t ON t.Id = m.TargetAccountId
        WHERE m.ChartId = @ChartId AND m.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @To
          AND (@IncludeClosing = 1 OR e.SourceKind <> 4 OR e.SourceDocumentType = 'Finance.ProfitTax') AND (@AccountFilter IS NULL OR t.Code LIKE @AccountFilter + N'%')
        GROUP BY t.Code, CASE WHEN @ByPartner = 1 THEN l.PartnerId END, CASE WHEN @ByCostCenter = 1 THEN l.CostCenterId END,
            CASE WHEN @ByProject = 1 THEN l.ProjectId END;

    SELECT Code, Name, AccountType FROM fin.GlAccount WHERE ChartId = @ChartId;
END;

CREATE OR ALTER PROCEDURE fin.usp_AccountingPeriod_SetStatus
    @CompanyId   int,
    @Id          int,
    @Status      tinyint,
    @Reason      nvarchar(500) = NULL,
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Schimba starea unei luni (C3 3.2, 4.1, 4.2): inchiderea (operativa sau completa) cere luna anterioara cel putin la fel de inchisa (51904);
    -- inchiderea completa cere maparea completa in planurile care o cer (51815, C1 4.3);
    -- redeschiderea cere lunile urmatoare cel mult la fel de deschise (51905) si motiv (51907); o luna blocata nu se schimba (51906).
    -- Concurenta optimista pe RowVersion (51902), cu blocare pe rand. Intoarce starea veche si noua, pentru evenimente.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Current tinyint, @Start date, @End date, @Version binary(8);
    SELECT @Current = Status, @Start = StartDate, @End = EndDate, @Version = RowVersion
    FROM fin.AccountingPeriod WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;

    IF @Current IS NULL
        THROW 51901, N'Perioada nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51902, N'Perioada a fost modificată de altcineva între timp.', 1;
    IF @Current = 4 OR @Status = 4
        THROW 51906, N'Luna e blocată odată cu anul financiar.', 1;

    IF @Status > @Current AND EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND EndDate < @Start AND Status < @Status)
        THROW 51904, N'O lună anterioară e mai deschisă; închideți lunile în ordine.', 1;
    -- Pasii fara trecere, re-verificati in tranzactia inchiderii, cu luna blocata (C3 4.1 pasul 5): nimic nu intra intre lista si inchidere.
    IF @Status = 3 AND @Current < 3 AND EXISTS (SELECT 1 FROM fin.PostingQueue WHERE CompanyId = @CompanyId AND Status = 1 AND AccountingDate BETWEEN @Start AND @End)
        THROW 51903, N'Coada „De contat” are documente datate în lună; luna nu se închide până nu sunt contate.', 1;
    IF @Status = 3 AND @Current < 3 AND EXISTS (SELECT 1 FROM core.Outbox AS o JOIN fin.PostingModel AS m ON m.IsActive = 1 AND o.EventType LIKE N'%.' + m.EventType
                                                WHERE o.CompanyId = @CompanyId AND o.ProcessedAt IS NULL)
        THROW 51908, N'Există documente validate încă necontate (evenimente în tranzit); reîncercați după ce sunt tratate.', 1;
    IF @Status = 3 AND @Current < 3 AND EXISTS (
        SELECT 1 FROM fin.JournalLineAltAccount AS m JOIN fin.ChartOfAccounts AS c ON c.Id = m.ChartId AND c.RequireCompleteMapping = 1 AND c.IsActive = 1
        WHERE m.CompanyId = @CompanyId AND m.PeriodYear = YEAR(@Start) AND m.PeriodMonth = MONTH(@Start) AND m.TargetAccountId IS NULL)
        THROW 51815, N'Un plan alternativ cu mapare completă are linii nemapate în această lună (C1-A11); completați regulile și recalculați.', 1;
    IF @Status < @Current
    BEGIN
        IF NULLIF(LTRIM(@Reason), N'') IS NULL
            THROW 51907, N'Redeschiderea cere un motiv.', 1;
        IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND StartDate > @End AND Status > @Status)
            THROW 51905, N'O lună ulterioară e închisă; redeschideți întâi lunile de după ea.', 1;
        IF EXISTS (SELECT 1 FROM fin.FiscalYear AS y JOIN fin.AccountingPeriod AS p ON p.FiscalYearId = y.Id WHERE p.Id = @Id AND y.Status = 3)
            THROW 51912, N'Anul financiar e blocat.', 1;
    END;

    IF @Status <> @Current
    BEGIN
        UPDATE fin.AccountingPeriod
        SET Status = @Status,
            SoftClosedAt = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() ELSE SoftClosedAt END,
            SoftClosedBy = CASE WHEN @Status = 2 THEN @UserId ELSE SoftClosedBy END,
            ClosedAt = CASE WHEN @Status = 3 THEN SYSUTCDATETIME() ELSE ClosedAt END,
            ClosedBy = CASE WHEN @Status = 3 THEN @UserId ELSE ClosedBy END,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

        INSERT INTO fin.PeriodStatusHistory (PeriodId, FromStatus, ToStatus, Reason, ChangedBy)
        VALUES (@Id, @Current, @Status, NULLIF(LTRIM(@Reason), N''), @UserId);

        -- Redeschiderea unei luni dintr-un an inchis redeschide anul (C3 4.3): soldurile reportate se recalculeaza la reinchidere (C3-A9).
        IF @Status < @Current
            UPDATE y SET Status = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
            FROM fin.FiscalYear AS y JOIN fin.AccountingPeriod AS p ON p.FiscalYearId = y.Id WHERE p.Id = @Id AND y.Status = 2;
    END;
    COMMIT TRANSACTION;

    SELECT p.Id, y.[Year], p.PeriodNo, p.StartDate, p.EndDate, @Current AS FromStatus, p.Status, p.RowVersion
    FROM fin.AccountingPeriod AS p JOIN fin.FiscalYear AS y ON y.Id = p.FiscalYearId WHERE p.Id = @Id;
END;

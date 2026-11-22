CREATE OR ALTER PROCEDURE fin.usp_BankStatement_Import
    @CompanyId           int,
    @TreasuryAccountId   int,
    @BankStatementFileId int = NULL,
    @StatementNo         nvarchar(35) = NULL,
    @StatementDate       date,
    @PeriodFrom          date,
    @PeriodTo            date,
    @CurrencyCode        char(3),
    @OpeningBalance      decimal(19,2),
    @ClosingBalance      decimal(19,2),
    @Source              tinyint,
    @Lines               fin.BankStatementLineList READONLY,
    @UserId              int
AS
BEGIN
    -- Salveaza un extras cu liniile lui (02.02, F2 4.1, SP-07), in tranzactia apelantului (SP-03). Contul e al firmei (51023), activ, de
    -- tip Banca, Card sau Depozit (51021), in moneda extrasului (51209); soldul final − soldul initial = suma liniilor din fisier (51208).
    -- Liniile cu BankReference existent pe cont se sar. Continuitatea: gol (2) cand soldul initial difera de soldul final al extrasului
    -- anterior; extrasul urmator isi reface continuitatea fata de acesta (un extras lipsa importat tarziu inchide golul), afara de un
    -- gol deja acceptat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AccountCompanyId int, @Kind tinyint, @Active bit, @AccountCurrency char(3);
    SELECT @AccountCompanyId = CompanyId, @Kind = Kind, @Active = IsActive, @AccountCurrency = CurrencyCode
    FROM fin.TreasuryAccount WITH (UPDLOCK, ROWLOCK) WHERE Id = @TreasuryAccountId;

    IF @AccountCompanyId IS NULL OR @AccountCompanyId <> @CompanyId
        THROW 51023, N'Contul bancar nu există în firma curentă.', 1;
    IF @Kind NOT IN (1, 3, 5) OR @Active = 0
        THROW 51021, N'Extrasele se importă doar în conturi active de tip bancă, card sau depozit.', 1;
    IF @CurrencyCode <> @AccountCurrency
        THROW 51209, N'Moneda extrasului diferă de moneda contului.', 1;
    IF @ClosingBalance - @OpeningBalance <> ISNULL((SELECT SUM(Amount) FROM @Lines), 0)
        THROW 51208, N'Soldul final minus soldul inițial nu este egal cu suma liniilor extrasului.', 1;
    IF EXISTS (SELECT 1 FROM @Lines WHERE Amount = 0)
        THROW 51210, N'Extrasul are o linie cu suma zero.', 1;

    DECLARE @PreviousClosing decimal(19,2), @HasPrevious bit = 0;
    SELECT TOP (1) @PreviousClosing = ClosingBalance, @HasPrevious = 1
    FROM fin.BankStatement
    WHERE TreasuryAccountId = @TreasuryAccountId AND PeriodTo < @PeriodFrom
    ORDER BY PeriodTo DESC, Id DESC;

    DECLARE @Continuity tinyint = CASE WHEN @HasPrevious = 1 AND @PreviousClosing <> @OpeningBalance THEN 2 ELSE 1 END;

    INSERT INTO fin.BankStatement (CompanyId, TreasuryAccountId, StatementNo, StatementDate, PeriodFrom, PeriodTo, CurrencyCode, OpeningBalance,
        ClosingBalance, Source, BankStatementFileId, ContinuityStatus, CreatedBy)
    VALUES (@CompanyId, @TreasuryAccountId, @StatementNo, @StatementDate, @PeriodFrom, @PeriodTo, @CurrencyCode, @OpeningBalance, @ClosingBalance,
        @Source, @BankStatementFileId, @Continuity, @UserId);
    DECLARE @Id int = SCOPE_IDENTITY();

    INSERT INTO fin.BankStatementLine (BankStatementId, TreasuryAccountId, LineNumber, BookingDate, ValueDate, Amount, CounterpartyName, CounterpartyIban,
        CounterpartyBic, RemittanceInfo, EndToEndId, BankReference, BankTransactionCode, ForeignAmount, ForeignCurrency, ExtractedFiscalCode,
        ExtractedDocumentNo, CreatedBy)
    SELECT @Id, @TreasuryAccountId, l.LineNumber, l.BookingDate, l.ValueDate, l.Amount, l.CounterpartyName, l.CounterpartyIban, l.CounterpartyBic,
        l.RemittanceInfo, l.EndToEndId, l.BankReference, l.BankTransactionCode, l.ForeignAmount, l.ForeignCurrency, l.ExtractedFiscalCode,
        l.ExtractedDocumentNo, @UserId
    FROM @Lines AS l
    WHERE NOT EXISTS (SELECT 1 FROM fin.BankStatementLine AS x WHERE x.TreasuryAccountId = @TreasuryAccountId AND x.BankReference = l.BankReference);
    DECLARE @Inserted int = @@ROWCOUNT;

    -- Un extras fara linii noi (sold initial sau fisier deja acoperit) nu are nimic de reconciliat.
    IF @Inserted = 0
        UPDATE fin.BankStatement SET Status = 3 WHERE Id = @Id;

    UPDATE s
    SET ContinuityStatus = CASE WHEN s.OpeningBalance = @ClosingBalance THEN 1 ELSE 2 END, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.BankStatement AS s
    WHERE s.Id = (SELECT TOP (1) n.Id FROM fin.BankStatement AS n
                  WHERE n.TreasuryAccountId = @TreasuryAccountId AND n.PeriodFrom > @PeriodTo AND n.Id <> @Id
                  ORDER BY n.PeriodFrom, n.Id)
      AND s.ContinuityStatus <> 3;

    UPDATE fin.TreasuryAccount
    SET LastStatementDate = CASE WHEN LastStatementDate IS NULL OR LastStatementDate < @StatementDate THEN @StatementDate ELSE LastStatementDate END
    WHERE Id = @TreasuryAccountId;

    SELECT @Id AS BankStatementId, @Inserted AS LineCount, (SELECT COUNT(*) FROM @Lines) - @Inserted AS SkippedCount, @Continuity AS ContinuityStatus;
END;

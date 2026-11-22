CREATE OR ALTER PROCEDURE fin.usp_BankStatementLine_Validate
    @CompanyId int,
    @Lines     fin.BankLineVersionList READONLY,
    @UserId    int
AS
BEGIN
    -- Validarea unui grup de linii de extras, pe seturi (02.02, F2 7; SP-08), in tranzactia apelantului (SP-03). Handler-ul a verificat
    -- perioadele si construieste evenimentele. Blocare in ordinea Id-urilor (SP-16). Liniile sunt ale firmei (51201), cu versiunea vazuta
    -- (51202), explicate (51203), pe un extras fara gol neacceptat (51204), cu partile egale cu suma (51205); o plata catre partener se
    -- face prin ordin de plata (51223). Extrasul cu toate liniile validate se inchide.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Locked TABLE (Id bigint PRIMARY KEY, BankStatementId int, MatchStatus tinyint, Amount decimal(19,2), RowVersion binary(8),
                           ContinuityStatus tinyint);
    INSERT INTO @Locked (Id, BankStatementId, MatchStatus, Amount, RowVersion, ContinuityStatus)
    SELECT l.Id, l.BankStatementId, l.MatchStatus, l.Amount, l.RowVersion, s.ContinuityStatus
    FROM fin.BankStatementLine AS l WITH (UPDLOCK, ROWLOCK)
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId AND s.CompanyId = @CompanyId
    WHERE l.Id IN (SELECT Id FROM @Lines)
    ORDER BY l.Id;

    IF (SELECT COUNT(*) FROM @Locked) <> (SELECT COUNT(*) FROM @Lines)
        THROW 51201, N'Una sau mai multe linii de extras nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Locked AS k JOIN @Lines AS p ON p.Id = k.Id WHERE p.RowVersion IS NULL OR k.RowVersion <> p.RowVersion)
        THROW 51202, N'O linie de extras a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF EXISTS (SELECT 1 FROM @Locked WHERE MatchStatus <> 3)
        THROW 51203, N'Doar liniile explicate pot fi validate.', 1;
    IF EXISTS (SELECT 1 FROM @Locked WHERE ContinuityStatus = 2)
        THROW 51204, N'Extrasul are un gol față de extrasul anterior; importați extrasul lipsă sau acceptați golul.', 1;
    IF EXISTS (SELECT 1 FROM @Locked AS k
               OUTER APPLY (SELECT SUM(p.Amount) AS PartsTotal FROM fin.BankStatementLinePart AS p WHERE p.BankStatementLineId = k.Id) AS t
               WHERE ISNULL(t.PartsTotal, 0) <> k.Amount)
        THROW 51205, N'Suma părților nu este egală cu suma liniei.', 1;
    IF EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS p
               JOIN @Locked AS k ON k.Id = p.BankStatementLineId
               JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
               WHERE p.Amount < 0 AND t.RequiresPartner = 1 AND p.PaymentOrderId IS NULL)
        THROW 51223, N'O plată către partener se validează prin ordin de plată („OP nou” din linia de extras).', 1;

    UPDATE l
    SET MatchStatus = 4, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ReversedAt = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.BankStatementLine AS l
    JOIN @Locked AS k ON k.Id = l.Id;

    UPDATE s
    SET Status = CASE WHEN NOT EXISTS (SELECT 1 FROM fin.BankStatementLine AS x WHERE x.BankStatementId = s.Id AND x.MatchStatus <> 4) THEN 3 ELSE 2 END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.BankStatement AS s
    WHERE s.Id IN (SELECT BankStatementId FROM @Locked);

    SELECT l.Id, l.RowVersion FROM fin.BankStatementLine AS l JOIN @Locked AS k ON k.Id = l.Id;
END;

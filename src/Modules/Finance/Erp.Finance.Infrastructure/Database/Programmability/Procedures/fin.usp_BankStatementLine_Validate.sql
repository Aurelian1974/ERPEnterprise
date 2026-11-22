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

    -- Confirmarea OP-urilor: Confirmat cand liniile validate legate de OP acopera suma lui (si in transe); linia de confirmare e ultima.
    DECLARE @Confirmed TABLE (PaymentOrderId int PRIMARY KEY, BankStatementLineId bigint, Amount decimal(19,2));
    UPDATE po
    SET ConfirmationStatus = 2, BankStatementLineId = c.LastLineId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.Id, inserted.BankStatementLineId, inserted.Amount INTO @Confirmed (PaymentOrderId, BankStatementLineId, Amount)
    FROM fin.PaymentOrder AS po
    JOIN (SELECT p.PaymentOrderId, -SUM(p.Amount) AS ConfirmedAmount, MAX(p.BankStatementLineId) AS LastLineId
          FROM fin.BankStatementLinePart AS p
          JOIN fin.BankStatementLine AS l ON l.Id = p.BankStatementLineId AND l.MatchStatus = 4
          WHERE p.PaymentOrderId IN (SELECT x.PaymentOrderId FROM fin.BankStatementLinePart AS x JOIN @Locked AS k ON k.Id = x.BankStatementLineId
                                     WHERE x.PaymentOrderId IS NOT NULL)
            AND p.Amount < 0
          GROUP BY p.PaymentOrderId) AS c ON c.PaymentOrderId = po.Id
    WHERE po.Status = 2 AND po.ConfirmationStatus = 1 AND c.ConfirmedAmount = po.Amount;

    -- Restituirea bancii (parte BANK-RESTITUIRE pe OP) il trece in Respins (F2 4.2 pasul 8); storno-ul il face utilizatorul.
    DECLARE @Rejected TABLE (PaymentOrderId int PRIMARY KEY, BankStatementLineId bigint);
    UPDATE po
    SET ConfirmationStatus = 3, BankStatementLineId = r.LineId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.Id, inserted.BankStatementLineId INTO @Rejected (PaymentOrderId, BankStatementLineId)
    FROM fin.PaymentOrder AS po
    JOIN (SELECT p.PaymentOrderId, MAX(p.BankStatementLineId) AS LineId
          FROM fin.BankStatementLinePart AS p
          JOIN @Locked AS k ON k.Id = p.BankStatementLineId
          JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId AND t.Code = N'BANK-RESTITUIRE'
          WHERE p.PaymentOrderId IS NOT NULL AND p.Amount > 0
          GROUP BY p.PaymentOrderId) AS r ON r.PaymentOrderId = po.Id
    WHERE po.Status = 2 AND po.ConfirmationStatus IN (1, 2);

    SELECT l.Id, l.RowVersion FROM fin.BankStatementLine AS l JOIN @Locked AS k ON k.Id = l.Id;
    SELECT c.PaymentOrderId, c.BankStatementLineId, c.Amount, l.BookingDate
    FROM @Confirmed AS c JOIN fin.BankStatementLine AS l ON l.Id = c.BankStatementLineId
    WHERE c.PaymentOrderId NOT IN (SELECT PaymentOrderId FROM @Rejected);
    SELECT PaymentOrderId, BankStatementLineId FROM @Rejected;
END;

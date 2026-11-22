CREATE OR ALTER PROCEDURE fin.usp_BankStatementLine_Unvalidate
    @CompanyId           int,
    @BankStatementLineId bigint,
    @RowVersion          binary(8),
    @UserId              int
AS
BEGIN
    -- Desfacerea validarii unei linii de extras (02.02, F2 3.4): linia revine la Explicata, cu partile ei, ca sa poata primi alta
    -- explicatie; datele bancii raman neatinse. Handler-ul a verificat perioada si publica storno-ul. Doar o linie validata (51203).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StatementId int, @Status tinyint, @Version binary(8), @StatementCompanyId int;
    SELECT @StatementId = l.BankStatementId, @Status = l.MatchStatus, @Version = l.RowVersion, @StatementCompanyId = s.CompanyId
    FROM fin.BankStatementLine AS l WITH (UPDLOCK, ROWLOCK)
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId
    WHERE l.Id = @BankStatementLineId;

    IF @StatementId IS NULL OR @StatementCompanyId <> @CompanyId
        THROW 51201, N'Linia de extras nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Linia de extras a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 4
        THROW 51203, N'Doar o linie validată își poate desface validarea.', 1;

    UPDATE fin.BankStatementLine
    SET MatchStatus = 3, ValidatedAt = NULL, ValidatedBy = NULL, ReversedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @BankStatementLineId;
    -- OP-urile legate de linie isi recalculeaza confirmarea din liniile validate ramase (F2 3.6: Confirmat → Neconfirmat doar prin
    -- desfacerea potrivirii pe extras; o restituire desfacuta scoate OP-ul din Respins).
    UPDATE po
    SET ConfirmationStatus = CASE WHEN x.Returned > 0 THEN 3 WHEN x.Paid = po.Amount THEN 2 ELSE 1 END,
        BankStatementLineId = CASE WHEN x.Returned > 0 OR x.Paid = po.Amount THEN x.LastLineId END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.PaymentOrder AS po
    CROSS APPLY (SELECT ISNULL(-SUM(CASE WHEN p.Amount < 0 THEN p.Amount END), 0) AS Paid,
                        COUNT(CASE WHEN p.Amount > 0 THEN 1 END) AS Returned, MAX(p.BankStatementLineId) AS LastLineId
                 FROM fin.BankStatementLinePart AS p
                 JOIN fin.BankStatementLine AS l ON l.Id = p.BankStatementLineId AND l.MatchStatus = 4
                 WHERE p.PaymentOrderId = po.Id) AS x
    WHERE po.Status = 2 AND po.ConfirmationStatus IS NOT NULL
      AND po.Id IN (SELECT PaymentOrderId FROM fin.BankStatementLinePart WHERE BankStatementLineId = @BankStatementLineId AND PaymentOrderId IS NOT NULL);
    UPDATE fin.CardSettlementExpected
    SET Status = 1, SettledAmount = NULL, FeeAmount = NULL, BankStatementLineId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id IN (SELECT CardSettlementExpectedId FROM fin.BankStatementLinePart WHERE BankStatementLineId = @BankStatementLineId AND CardSettlementExpectedId IS NOT NULL);
    UPDATE fin.BankStatement SET Status = 2, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @StatementId AND Status = 3;

    SELECT RowVersion FROM fin.BankStatementLine WHERE Id = @BankStatementLineId;
END;

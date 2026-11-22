CREATE OR ALTER PROCEDURE fin.usp_BankStatementLine_Explain
    @CompanyId           int,
    @BankStatementLineId bigint,
    @Parts               fin.BankStatementLinePartList READONLY,
    @RowVersion          binary(8),
    @UserId              int
AS
BEGIN
    -- Explicarea unei linii de extras (02.02, F2 3.4 si 4.4): partile o inlocuiesc pe cea anterioara; datele bancii nu se ating. Fara
    -- parti, linia revine la Nepotrivita. Partile acopera exact suma liniei (51205); tipurile sunt bancare, active, cu sensul partii
    -- (51021) si cu ce cer: partener, angajat, cont pereche al firmei, referinta obligatiei la buget, text la sumele de clarificat,
    -- documentul de casa validat la depuneri si ridicari, nefolosit de alta linie (51220). O linie validata nu se mai explica (51211).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StatementId int, @AccountId int, @Amount decimal(19,2), @Status tinyint, @Version binary(8), @StatementCompanyId int;
    SELECT @StatementId = l.BankStatementId, @AccountId = l.TreasuryAccountId, @Amount = l.Amount, @Status = l.MatchStatus, @Version = l.RowVersion,
           @StatementCompanyId = s.CompanyId
    FROM fin.BankStatementLine AS l WITH (UPDLOCK, ROWLOCK)
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId
    WHERE l.Id = @BankStatementLineId;

    IF @StatementId IS NULL OR @StatementCompanyId <> @CompanyId
        THROW 51201, N'Linia de extras nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Linia de extras a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status = 4
        THROW 51211, N'O linie validată nu se mai modifică; desfaceți întâi validarea.', 1;

    IF EXISTS (SELECT 1 FROM @Parts)
    BEGIN
        IF (SELECT SUM(Amount) FROM @Parts) <> @Amount
            THROW 51205, N'Suma părților nu este egală cu suma liniei.', 1;
        IF EXISTS (SELECT 1 FROM @Parts WHERE Amount = 0)
            THROW 51205, N'O parte are suma zero.', 1;
        IF EXISTS (SELECT 1 FROM @Parts AS p LEFT JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
                   WHERE t.Id IS NULL OR t.IsActive = 0 OR t.AppliesToBank = 0)
            THROW 51021, N'Tipul de operațiune nu există, este inactiv sau nu se folosește la bancă.', 1;
        IF EXISTS (SELECT 1 FROM @Parts AS p JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
                   WHERE (t.Direction = 1 AND p.Amount < 0) OR (t.Direction = 2 AND p.Amount > 0))
            THROW 51021, N'Sensul părții nu se potrivește tipului de operațiune (intrare cu sumă pozitivă, ieșire cu sumă negativă).', 1;
        IF EXISTS (SELECT 1 FROM @Parts AS p JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
                   WHERE (t.RequiresPartner = 1 AND p.PartnerId IS NULL) OR (t.RequiresEmployee = 1 AND p.EmployeeId IS NULL)
                      OR (t.RequiresCounterAccount = 1 AND p.CounterTreasuryAccountId IS NULL)
                      OR (t.Code = N'BANK-PLATA-BUGET' AND p.BudgetObligationRef IS NULL)
                      OR (t.Code = N'BANK-NEIDENTIFICAT' AND p.Note IS NULL)
                      OR (t.Code IN (N'BANK-DEPUNERE-NUMERAR', N'BANK-RIDICARE-NUMERAR') AND p.CashDocumentId IS NULL))
            THROW 51021, N'O parte nu are ce cere tipul ei de operațiune (partener, angajat, cont pereche, obligație, explicație sau document de casă).', 1;
        IF EXISTS (SELECT 1 FROM @Parts AS p LEFT JOIN core.Partner AS x ON x.Id = p.PartnerId WHERE p.PartnerId IS NOT NULL AND x.Id IS NULL)
           OR EXISTS (SELECT 1 FROM @Parts AS p LEFT JOIN core.Employee AS e ON e.Id = p.EmployeeId AND e.CompanyId = @CompanyId
                      WHERE p.EmployeeId IS NOT NULL AND e.Id IS NULL)
           OR EXISTS (SELECT 1 FROM @Parts AS p LEFT JOIN fin.TreasuryAccount AS a ON a.Id = p.CounterTreasuryAccountId AND a.CompanyId = @CompanyId
                      WHERE p.CounterTreasuryAccountId IS NOT NULL AND (a.Id IS NULL OR a.Id = @AccountId))
            THROW 51021, N'Partenerul, angajatul sau contul pereche nu există în firma curentă.', 1;
        -- Depunerea si ridicarea de numerar: perechea e documentul de casa validat cu operatiunea corespunzatoare (F1 3.2).
        IF EXISTS (SELECT 1 FROM @Parts AS p JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
                   WHERE p.CashDocumentId IS NOT NULL AND NOT EXISTS (
                       SELECT 1 FROM fin.CashDocument AS d
                       JOIN fin.CashDocumentLine AS dl ON dl.CashDocumentId = d.Id
                       JOIN fin.TreasuryOperationType AS ct ON ct.Id = dl.TreasuryOperationTypeId
                       WHERE d.Id = p.CashDocumentId AND d.CompanyId = @CompanyId AND d.Status = 2
                         AND ct.Code = CASE t.Code WHEN N'BANK-DEPUNERE-NUMERAR' THEN N'PLATA-DEPUNERE-BANCA'
                                                   WHEN N'BANK-RIDICARE-NUMERAR' THEN N'INC-RIDICARE-BANCA' ELSE ct.Code END))
            THROW 51021, N'Documentul de casă nu este o depunere sau ridicare de numerar validată a firmei.', 1;
        IF EXISTS (SELECT 1 FROM @Parts AS p JOIN fin.BankStatementLinePart AS x ON x.CashDocumentId = p.CashDocumentId
                   WHERE p.CashDocumentId IS NOT NULL AND x.BankStatementLineId <> @BankStatementLineId)
            THROW 51220, N'Documentul de casă este deja potrivit cu altă linie de extras.', 1;
    END;

    DELETE FROM fin.BankStatementLinePart WHERE BankStatementLineId = @BankStatementLineId;
    INSERT INTO fin.BankStatementLinePart (BankStatementLineId, PartNumber, TreasuryOperationTypeId, Amount, PartnerId, EmployeeId, SettlementRef,
        CashDocumentId, CounterTreasuryAccountId, BudgetObligationRef, CostCenterId, ProjectId, Note)
    SELECT @BankStatementLineId, PartNumber, TreasuryOperationTypeId, Amount, PartnerId, EmployeeId, SettlementRef, CashDocumentId,
        CounterTreasuryAccountId, BudgetObligationRef, CostCenterId, ProjectId, Note
    FROM @Parts;

    DECLARE @HasParts bit = CASE WHEN EXISTS (SELECT 1 FROM @Parts) THEN 1 ELSE 0 END;
    DECLARE @Partner int = (SELECT CASE WHEN COUNT(DISTINCT PartnerId) = 1 THEN MAX(PartnerId) END FROM @Parts WHERE PartnerId IS NOT NULL);
    UPDATE fin.BankStatementLine
    SET MatchStatus = CASE WHEN @HasParts = 1 THEN 3 ELSE 1 END, MatchSource = CASE WHEN @HasParts = 1 THEN 6 END,
        PartnerId = COALESCE(@Partner, PartnerId), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @BankStatementLineId;

    UPDATE fin.BankStatement SET Status = 2, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @StatementId AND Status = 1;

    SELECT RowVersion FROM fin.BankStatementLine WHERE Id = @BankStatementLineId;
END;

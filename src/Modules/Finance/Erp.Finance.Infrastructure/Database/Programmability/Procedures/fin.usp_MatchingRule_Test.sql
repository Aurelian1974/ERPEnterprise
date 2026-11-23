CREATE OR ALTER PROCEDURE fin.usp_MatchingRule_Test
    @CompanyId              int,
    @Id                     int = NULL,
    @Priority               int,
    @TreasuryAccountId      int = NULL,
    @Direction              tinyint = NULL,
    @CounterpartyIban       varchar(34) = NULL,
    @CounterpartyFiscalCode varchar(20) = NULL,
    @RemittanceContains     nvarchar(200) = NULL,
    @BankTransactionCode    varchar(35) = NULL,
    @AmountFrom             decimal(19,2) = NULL,
    @AmountTo               decimal(19,2) = NULL,
    @TreasuryOperationTypeId int,
    @PartnerId              int = NULL,
    @NoteTemplate           nvarchar(500) = NULL,
    @FromDate               date
AS
BEGIN
    -- „Testeaza pe 30 de zile” (02.02, F2 3.5; macheta f2-04): aplica regula asa cum e in fisa (chiar nesalvata) pe liniile de extras ale
    -- firmei din @FromDate incoace, fara nicio modificare. Conditiile sunt cele ale pasului 5 din fin.usp_BankMatching_Run; regula testata
    -- concureaza cu celelalte reguli active (versiunea salvata a ei e inlocuita de cea din fisa), ca sa se vada liniile prinse inaintea ei.
    -- Intoarce: 1) rezumatul (linii, deja explicate altfel, prinse de alta regula); 2) totalurile pe moneda; 3) primele 200 de linii.
    SET NOCOUNT ON;

    DECLARE @Candidate int = -1;
    DECLARE @Rules TABLE (RuleId int PRIMARY KEY, SortId int NOT NULL, Priority int NOT NULL, Name nvarchar(200) NULL, TreasuryAccountId int NULL,
        Direction tinyint NULL, Iban varchar(34) NULL, FiscalCode varchar(20) NULL, RemittanceSearch nvarchar(200) COLLATE Latin1_General_100_CI_AI_SC_UTF8 NULL,
        Code varchar(35) NULL, AmountFrom decimal(19,2) NULL, AmountTo decimal(19,2) NULL, TypeId int NOT NULL, PartnerId int NULL, NoteTemplate nvarchar(500) NULL);

    INSERT INTO @Rules VALUES (@Candidate, ISNULL(@Id, 2147483647), @Priority, NULL, @TreasuryAccountId, @Direction, @CounterpartyIban, @CounterpartyFiscalCode,
        @RemittanceContains, @BankTransactionCode, @AmountFrom, @AmountTo, @TreasuryOperationTypeId, @PartnerId, @NoteTemplate);
    INSERT INTO @Rules
    SELECT r.Id, r.Id, r.Priority, r.Name, r.TreasuryAccountId, r.Direction, r.CounterpartyIban, r.CounterpartyFiscalCode, r.RemittanceContains,
        r.BankTransactionCode, r.AmountFrom, r.AmountTo, r.TreasuryOperationTypeId, r.PartnerId, r.NoteTemplate
    FROM fin.MatchingRule AS r
    WHERE r.CompanyId = @CompanyId AND r.IsActive = 1 AND r.Id <> ISNULL(@Id, 0);

    -- Toate potrivirile regula / linie (conditiile pasului 5), apoi, pe liniile prinse de regula testata, prima regula dupa prioritate.
    DECLARE @Matches TABLE (LineId bigint NOT NULL, RuleId int NOT NULL, Priority int NOT NULL, SortId int NOT NULL, PRIMARY KEY (LineId, RuleId));
    INSERT INTO @Matches (LineId, RuleId, Priority, SortId)
    SELECT l.Id, r.RuleId, r.Priority, r.SortId
    FROM fin.BankStatementLine AS l
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId AND s.CompanyId = @CompanyId
    CROSS JOIN @Rules AS r
    JOIN fin.TreasuryOperationType AS t ON t.Id = r.TypeId AND t.IsActive = 1 AND t.AppliesToBank = 1
    WHERE (r.TreasuryAccountId IS NULL OR r.TreasuryAccountId = l.TreasuryAccountId)
      AND (r.Direction IS NULL OR (r.Direction = 1 AND l.Amount > 0) OR (r.Direction = 2 AND l.Amount < 0))
      AND (t.Direction = 3 OR (t.Direction = 1 AND l.Amount > 0) OR (t.Direction = 2 AND l.Amount < 0))
      AND (r.Iban IS NULL OR r.Iban = l.CounterpartyIban)
      AND (r.FiscalCode IS NULL OR r.FiscalCode = l.ExtractedFiscalCode)
      AND (r.RemittanceSearch IS NULL OR l.RemittanceSearch LIKE N'%' + r.RemittanceSearch + N'%')
      AND (r.Code IS NULL OR r.Code = l.BankTransactionCode)
      AND (r.AmountFrom IS NULL OR ABS(l.Amount) >= r.AmountFrom) AND (r.AmountTo IS NULL OR ABS(l.Amount) <= r.AmountTo)
      AND (t.RequiresPartner = 0 OR (l.Amount > 0 AND COALESCE(r.PartnerId, l.PartnerId) IS NOT NULL))
      AND t.RequiresEmployee = 0 AND t.RequiresCounterAccount = 0
      AND t.Code NOT IN (N'BANK-PLATA-BUGET', N'BANK-DEPUNERE-NUMERAR', N'BANK-RIDICARE-NUMERAR', N'BANK-DECONTARE-CARD', N'BANK-RESTITUIRE')
      AND (t.Code <> N'BANK-NEIDENTIFICAT' OR r.NoteTemplate IS NOT NULL)
      AND l.BookingDate >= @FromDate;

    DECLARE @Hits TABLE (LineId bigint PRIMARY KEY, WinnerId int NOT NULL);
    INSERT INTO @Hits (LineId, WinnerId)
    SELECT m.LineId, w.RuleId
    FROM @Matches AS m
    CROSS APPLY (SELECT TOP (1) x.RuleId FROM @Matches AS x WHERE x.LineId = m.LineId ORDER BY x.Priority, x.SortId) AS w
    WHERE m.RuleId = @Candidate;

    SELECT COUNT(*) AS Lines,
        ISNULL(SUM(CASE WHEN l.MatchStatus IN (3, 4) AND (l.MatchingRuleId IS NULL OR l.MatchingRuleId <> ISNULL(@Id, 0)) THEN 1 ELSE 0 END), 0) AS AlreadyExplained,
        ISNULL(SUM(CASE WHEN h.WinnerId <> @Candidate THEN 1 ELSE 0 END), 0) AS TakenByOtherRule
    FROM @Hits AS h JOIN fin.BankStatementLine AS l ON l.Id = h.LineId;

    SELECT a.CurrencyCode, COUNT(*) AS Lines, SUM(CASE WHEN l.Amount > 0 THEN l.Amount ELSE 0 END) AS TotalIn,
        SUM(CASE WHEN l.Amount < 0 THEN -l.Amount ELSE 0 END) AS TotalOut
    FROM @Hits AS h JOIN fin.BankStatementLine AS l ON l.Id = h.LineId JOIN fin.TreasuryAccount AS a ON a.Id = l.TreasuryAccountId
    GROUP BY a.CurrencyCode
    ORDER BY a.CurrencyCode;

    SELECT TOP (200) l.Id, l.BankStatementId, l.BookingDate, a.Code AS AccountCode, a.CurrencyCode, l.Amount, l.CounterpartyName, l.RemittanceInfo,
        l.MatchStatus, t.Code AS ExplainedAs, er.Name AS ExplainedByRuleName,
        CASE WHEN h.WinnerId <> @Candidate THEN wr.Name END AS TakenByRuleName
    FROM @Hits AS h
    JOIN fin.BankStatementLine AS l ON l.Id = h.LineId
    JOIN fin.TreasuryAccount AS a ON a.Id = l.TreasuryAccountId
    LEFT JOIN fin.BankStatementLinePart AS p ON p.BankStatementLineId = l.Id AND p.PartNumber = 1 AND l.MatchStatus IN (3, 4)
    LEFT JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId
    LEFT JOIN fin.MatchingRule AS er ON er.Id = l.MatchingRuleId
    LEFT JOIN @Rules AS wr ON wr.RuleId = h.WinnerId
    ORDER BY l.BookingDate DESC, l.Id DESC;
END;

CREATE OR ALTER PROCEDURE fin.usp_BankMatching_Run
    @CompanyId           int,
    @BankStatementId     int = NULL,
    @TreasuryAccountId   int = NULL,
    @FeeTolerancePercent decimal(5,2),
    @UserId              int
AS
BEGIN
    -- Potrivirea automata a liniilor Nepotrivite (02.02, F2 4.3), pe seturi, in tranzactia apelantului (SP-03). Identifica partenerul (IBAN-ul
    -- memorat in core.PartnerBankAccount, apoi CUI-ul gasit in detalii), apoi, in ordine, se opreste la prima potrivire sigura:
    -- 1 OP validat si neconfirmat (EndToEndId identic; sau IBAN + suma identice in ±5 zile de la data ceruta, unic in ambele sensuri);
    -- 2 pereche interna (depunere / ridicare de numerar din casa; virament intern cu partea din celalalt cont; suma identica, ±3 zile);
    -- 3 decontare de card (IBAN-ul procesatorului, brutul asteptat minus comisionul in toleranta); 5 regula clientului (prima, dupa prioritate);
    -- 6 propuneri pentru partenerul identificat (OP-urile lui neconfirmate). Pasii 3a si 4 (efecte, facturi) vin cu F3. Intoarce liniile
    -- explicate (sursa, daca regula cere validare automata, versiunea, data, continuitatea), pentru validarea automata facuta de handler.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Lines TABLE (Id bigint PRIMARY KEY, AccountId int, Amount decimal(19,2), BookingDate date, Iban varchar(34), EndToEndId varchar(35),
                          FiscalCode varchar(20), PartnerId int, Code varchar(35));
    INSERT INTO @Lines (Id, AccountId, Amount, BookingDate, Iban, EndToEndId, FiscalCode, PartnerId, Code)
    SELECT l.Id, l.TreasuryAccountId, l.Amount, l.BookingDate, l.CounterpartyIban, l.EndToEndId, l.ExtractedFiscalCode, l.PartnerId, l.BankTransactionCode
    FROM fin.BankStatementLine AS l WITH (UPDLOCK, ROWLOCK)
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId AND s.CompanyId = @CompanyId
    WHERE l.MatchStatus IN (1, 2)
      AND (@BankStatementId IS NULL OR l.BankStatementId = @BankStatementId)
      AND (@TreasuryAccountId IS NULL OR l.TreasuryAccountId = @TreasuryAccountId);

    -- Partenerul: IBAN-ul memorat (un singur partener), apoi CUI-ul din detalii.
    UPDATE k SET PartnerId = x.PartnerId
    FROM @Lines AS k
    CROSS APPLY (SELECT MIN(b.PartnerId) AS PartnerId, COUNT(DISTINCT b.PartnerId) AS Partners
                 FROM core.PartnerBankAccount AS b JOIN core.Partner AS p ON p.Id = b.PartnerId AND p.IsActive = 1
                 WHERE b.Iban = k.Iban AND b.IsActive = 1) AS x
    WHERE k.PartnerId IS NULL AND k.Iban IS NOT NULL AND x.Partners = 1;
    UPDATE k SET PartnerId = p.Id
    FROM @Lines AS k JOIN core.Partner AS p ON p.FiscalCode = k.FiscalCode AND p.IsActive = 1
    WHERE k.PartnerId IS NULL AND k.FiscalCode IS NOT NULL;

    DECLARE @Matched TABLE (LineId bigint PRIMARY KEY, Source tinyint, AutoValidate bit);
    DECLARE @Parts TABLE (LineId bigint, PartNumber smallint, TypeId int, Amount decimal(19,2), PartnerId int, EmployeeId int, SettlementRef nvarchar(100),
                          PaymentOrderId int, CashDocumentId int, CounterAccountId int, CardId int, PairedLineId bigint, CostCenterId int, ProjectId int,
                          Note nvarchar(500), PRIMARY KEY (LineId, PartNumber));

    -- Pasul 1: OP validat si neconfirmat, nelegat inca de vreo linie.
    DECLARE @Op TABLE (LineId bigint PRIMARY KEY, OrderId int);
    INSERT INTO @Op (LineId, OrderId)
    SELECT k.Id, MIN(o.Id)
    FROM @Lines AS k
    JOIN fin.PaymentOrder AS o ON o.EndToEndId = k.EndToEndId AND o.CompanyId = @CompanyId AND o.TreasuryAccountId = k.AccountId AND o.Status = 2
         AND o.ReversalOfId IS NULL AND o.ConfirmationStatus = 1 AND o.Amount = -k.Amount
    WHERE k.Amount < 0 AND k.EndToEndId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS x WHERE x.PaymentOrderId = o.Id)
    GROUP BY k.Id;
    DELETE op FROM @Op AS op WHERE EXISTS (SELECT 1 FROM @Op AS o2 WHERE o2.OrderId = op.OrderId AND o2.LineId < op.LineId);

    INSERT INTO @Op (LineId, OrderId)
    SELECT c.LineId, c.OrderId
    FROM (SELECT k.Id AS LineId, o.Id AS OrderId, COUNT(*) OVER (PARTITION BY k.Id) AS ForLine, COUNT(*) OVER (PARTITION BY o.Id) AS ForOrder
          FROM @Lines AS k
          JOIN fin.PaymentOrder AS o ON o.BeneficiaryIban = k.Iban AND o.Amount = -k.Amount AND o.CompanyId = @CompanyId AND o.TreasuryAccountId = k.AccountId
               AND o.Status = 2 AND o.ReversalOfId IS NULL AND o.ConfirmationStatus = 1
               AND k.BookingDate BETWEEN DATEADD(day, -5, o.RequestedExecutionDate) AND DATEADD(day, 5, o.RequestedExecutionDate)
          WHERE k.Amount < 0 AND k.Iban IS NOT NULL AND k.Id NOT IN (SELECT LineId FROM @Op) AND o.Id NOT IN (SELECT OrderId FROM @Op)
            AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS x WHERE x.PaymentOrderId = o.Id)) AS c
    WHERE c.ForLine = 1 AND c.ForOrder = 1;

    INSERT INTO @Parts (LineId, PartNumber, TypeId, Amount, PartnerId, EmployeeId, SettlementRef, PaymentOrderId, CostCenterId, ProjectId)
    SELECT op.LineId, ol.LineNumber, ol.TreasuryOperationTypeId, -ol.Amount, o.PartnerId, o.EmployeeId, ol.SettlementRef, o.Id, ol.CostCenterId, ol.ProjectId
    FROM @Op AS op JOIN fin.PaymentOrder AS o ON o.Id = op.OrderId JOIN fin.PaymentOrderLine AS ol ON ol.PaymentOrderId = o.Id;
    INSERT INTO @Matched (LineId, Source, AutoValidate) SELECT LineId, 1, 0 FROM @Op;

    -- Pasul 2a: depunerea / ridicarea de numerar din casa, catre acest cont.
    INSERT INTO @Parts (LineId, PartNumber, TypeId, Amount, CashDocumentId)
    SELECT c.LineId, 1, t.Id, c.Amount, c.DocId
    FROM (SELECT k.Id AS LineId, k.Amount, d.Id AS DocId, CASE WHEN k.Amount > 0 THEN N'BANK-DEPUNERE-NUMERAR' ELSE N'BANK-RIDICARE-NUMERAR' END AS TypeCode,
                 ROW_NUMBER() OVER (PARTITION BY k.Id ORDER BY ABS(DATEDIFF(day, d.DocumentDate, k.BookingDate)), d.Id) AS ForLine,
                 ROW_NUMBER() OVER (PARTITION BY d.Id ORDER BY ABS(DATEDIFF(day, d.DocumentDate, k.BookingDate)), k.Id) AS ForDocument
          FROM @Lines AS k
          JOIN fin.CashDocumentLine AS dl ON dl.CounterTreasuryAccountId = k.AccountId AND dl.Amount = ABS(k.Amount)
          JOIN fin.CashDocument AS d ON d.Id = dl.CashDocumentId AND d.CompanyId = @CompanyId AND d.Status = 2 AND d.ReversalOfId IS NULL
          JOIN fin.TreasuryOperationType AS ct ON ct.Id = dl.TreasuryOperationTypeId
               AND ct.Code = CASE WHEN k.Amount > 0 THEN N'PLATA-DEPUNERE-BANCA' ELSE N'INC-RIDICARE-BANCA' END
          WHERE k.Id NOT IN (SELECT LineId FROM @Matched) AND ABS(DATEDIFF(day, d.DocumentDate, k.BookingDate)) <= 3
            AND NOT EXISTS (SELECT 1 FROM fin.CashDocument AS r WHERE r.ReversalOfId = d.Id)
            AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS x WHERE x.CashDocumentId = d.Id)) AS c
    JOIN fin.TreasuryOperationType AS t ON t.Code = c.TypeCode
    WHERE c.ForLine = 1 AND c.ForDocument = 1;
    INSERT INTO @Matched (LineId, Source, AutoValidate) SELECT DISTINCT LineId, 3, 0 FROM @Parts WHERE CashDocumentId IS NOT NULL;

    -- Pasul 2b: viramentul intern, cu partea din celalalt cont (explicata sau validata), nepereche.
    INSERT INTO @Parts (LineId, PartNumber, TypeId, Amount, CounterAccountId, PairedLineId)
    SELECT c.LineId, 1, c.TypeId, c.Amount, c.OtherAccountId, c.OtherLineId
    FROM (SELECT k.Id AS LineId, k.Amount, op.TreasuryOperationTypeId AS TypeId, ol.TreasuryAccountId AS OtherAccountId, ol.Id AS OtherLineId,
                 ROW_NUMBER() OVER (PARTITION BY k.Id ORDER BY ABS(DATEDIFF(day, ol.BookingDate, k.BookingDate)), ol.Id) AS ForLine,
                 ROW_NUMBER() OVER (PARTITION BY ol.Id ORDER BY ABS(DATEDIFF(day, ol.BookingDate, k.BookingDate)), k.Id) AS ForOther
          FROM @Lines AS k
          JOIN fin.BankStatementLinePart AS op ON op.CounterTreasuryAccountId = k.AccountId AND op.Amount = -k.Amount AND op.PairedBankStatementLineId IS NULL
          JOIN fin.TreasuryOperationType AS t ON t.Id = op.TreasuryOperationTypeId AND t.Code = N'BANK-TRANSFER-INTERN'
          JOIN fin.BankStatementLine AS ol ON ol.Id = op.BankStatementLineId AND ol.MatchStatus IN (3, 4)
          JOIN fin.BankStatement AS os ON os.Id = ol.BankStatementId AND os.CompanyId = @CompanyId
          WHERE k.Id NOT IN (SELECT LineId FROM @Matched) AND ABS(DATEDIFF(day, ol.BookingDate, k.BookingDate)) <= 3
            AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS q WHERE q.PairedBankStatementLineId = ol.Id)) AS c
    WHERE c.ForLine = 1 AND c.ForOther = 1;
    INSERT INTO @Matched (LineId, Source, AutoValidate) SELECT DISTINCT LineId, 3, 0 FROM @Parts WHERE PairedLineId IS NOT NULL;

    -- Pasul 3: decontarea de card de la procesatorul cu IBAN-ul liniei: brut + comision (diferenta), comisionul in toleranta.
    DECLARE @Card TABLE (LineId bigint PRIMARY KEY, CardId int, Gross decimal(19,2), Net decimal(19,2));
    INSERT INTO @Card (LineId, CardId, Gross, Net)
    SELECT c.LineId, c.CardId, c.Gross, c.Net
    FROM (SELECT k.Id AS LineId, e.Id AS CardId, e.GrossAmount AS Gross, k.Amount AS Net,
                 ROW_NUMBER() OVER (PARTITION BY k.Id ORDER BY e.ExpectedSettlementDate, e.Id) AS ForLine,
                 ROW_NUMBER() OVER (PARTITION BY e.Id ORDER BY k.BookingDate, k.Id) AS ForSettlement
          FROM @Lines AS k
          JOIN fin.CardAcquirer AS a ON a.CompanyId = @CompanyId AND a.IsActive = 1 AND a.CounterpartyIban = k.Iban
          JOIN fin.CardSettlementExpected AS e ON e.CompanyId = @CompanyId AND e.AcquirerKey = a.CodeKey AND e.Status = 1
          WHERE k.Amount > 0 AND k.Id NOT IN (SELECT LineId FROM @Matched) AND k.BookingDate >= e.SalesDate AND k.Amount <= e.GrossAmount
            AND e.GrossAmount - k.Amount <= ROUND(e.GrossAmount * @FeeTolerancePercent / 100, 2)
            AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLinePart AS x WHERE x.CardSettlementExpectedId = e.Id)) AS c
    WHERE c.ForLine = 1 AND c.ForSettlement = 1;
    INSERT INTO @Parts (LineId, PartNumber, TypeId, Amount, CardId)
    SELECT c.LineId, 1, t.Id, c.Gross, c.CardId FROM @Card AS c JOIN fin.TreasuryOperationType AS t ON t.Code = N'BANK-DECONTARE-CARD';
    INSERT INTO @Parts (LineId, PartNumber, TypeId, Amount)
    SELECT c.LineId, 2, t.Id, c.Net - c.Gross FROM @Card AS c JOIN fin.TreasuryOperationType AS t ON t.Code = N'BANK-COMISION' WHERE c.Gross > c.Net;
    INSERT INTO @Matched (LineId, Source, AutoValidate) SELECT LineId, 4, 0 FROM @Card;

    -- Pasul 5: prima regula a clientului (prioritate), cu tipul potrivit sensului liniei; fara plati catre parteneri (cer OP).
    DECLARE @Rule TABLE (LineId bigint PRIMARY KEY, RuleId int);
    INSERT INTO @Rule (LineId, RuleId)
    SELECT k.Id, x.Id
    FROM @Lines AS k
    JOIN fin.BankStatementLine AS l ON l.Id = k.Id
    CROSS APPLY (SELECT TOP (1) r.Id
                 FROM fin.MatchingRule AS r
                 JOIN fin.TreasuryOperationType AS t ON t.Id = r.TreasuryOperationTypeId AND t.IsActive = 1 AND t.AppliesToBank = 1
                 WHERE r.CompanyId = @CompanyId AND r.IsActive = 1 AND (r.TreasuryAccountId IS NULL OR r.TreasuryAccountId = k.AccountId)
                   AND (r.Direction IS NULL OR (r.Direction = 1 AND k.Amount > 0) OR (r.Direction = 2 AND k.Amount < 0))
                   AND (t.Direction = 3 OR (t.Direction = 1 AND k.Amount > 0) OR (t.Direction = 2 AND k.Amount < 0))
                   AND (r.CounterpartyIban IS NULL OR r.CounterpartyIban = k.Iban)
                   AND (r.CounterpartyFiscalCode IS NULL OR r.CounterpartyFiscalCode = k.FiscalCode)
                   AND (r.RemittanceSearch IS NULL OR l.RemittanceSearch LIKE N'%' + r.RemittanceSearch + N'%')
                   AND (r.BankTransactionCode IS NULL OR r.BankTransactionCode = k.Code)
                   AND (r.AmountFrom IS NULL OR ABS(k.Amount) >= r.AmountFrom) AND (r.AmountTo IS NULL OR ABS(k.Amount) <= r.AmountTo)
                   AND (t.RequiresPartner = 0 OR (k.Amount > 0 AND COALESCE(r.PartnerId, k.PartnerId) IS NOT NULL))
                   AND t.RequiresEmployee = 0 AND t.RequiresCounterAccount = 0
                   AND t.Code NOT IN (N'BANK-PLATA-BUGET', N'BANK-DEPUNERE-NUMERAR', N'BANK-RIDICARE-NUMERAR', N'BANK-DECONTARE-CARD', N'BANK-RESTITUIRE')
                   AND (t.Code <> N'BANK-NEIDENTIFICAT' OR r.NoteTemplate IS NOT NULL)
                 ORDER BY r.Priority, r.Id) AS x
    WHERE k.Id NOT IN (SELECT LineId FROM @Matched);
    INSERT INTO @Parts (LineId, PartNumber, TypeId, Amount, PartnerId, CostCenterId, ProjectId, Note)
    SELECT k.Id, 1, r.TreasuryOperationTypeId, k.Amount, CASE WHEN t.RequiresPartner = 1 OR r.PartnerId IS NOT NULL THEN COALESCE(r.PartnerId, k.PartnerId) END,
        r.CostCenterId, r.ProjectId, r.NoteTemplate
    FROM @Rule AS x JOIN @Lines AS k ON k.Id = x.LineId JOIN fin.MatchingRule AS r ON r.Id = x.RuleId
    JOIN fin.TreasuryOperationType AS t ON t.Id = r.TreasuryOperationTypeId;
    INSERT INTO @Matched (LineId, Source, AutoValidate)
    SELECT x.LineId, 5, CASE WHEN r.AutoValidate = 1 AND t.RequiresPartner = 0 THEN 1 ELSE 0 END
    FROM @Rule AS x JOIN fin.MatchingRule AS r ON r.Id = x.RuleId JOIN fin.TreasuryOperationType AS t ON t.Id = r.TreasuryOperationTypeId;

    -- Salvarea explicatiilor si a partenerilor identificati.
    DELETE FROM fin.BankStatementLinePart WHERE BankStatementLineId IN (SELECT LineId FROM @Matched);
    INSERT INTO fin.BankStatementLinePart (BankStatementLineId, PartNumber, TreasuryOperationTypeId, Amount, PartnerId, EmployeeId, SettlementRef, PaymentOrderId,
        CashDocumentId, CounterTreasuryAccountId, CardSettlementExpectedId, PairedBankStatementLineId, CostCenterId, ProjectId, Note)
    SELECT LineId, PartNumber, TypeId, Amount, PartnerId, EmployeeId, SettlementRef, PaymentOrderId, CashDocumentId, CounterAccountId, CardId, PairedLineId,
        CostCenterId, ProjectId, Note
    FROM @Parts;
    DELETE FROM fin.MatchSuggestion WHERE BankStatementLineId IN (SELECT Id FROM @Lines);

    UPDATE l
    SET MatchStatus = CASE WHEN m.LineId IS NOT NULL THEN 3 ELSE 1 END, MatchSource = m.Source,
        PartnerId = COALESCE((SELECT CASE WHEN COUNT(DISTINCT p.PartnerId) = 1 THEN MAX(p.PartnerId) END FROM @Parts AS p WHERE p.LineId = l.Id), k.PartnerId),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.BankStatementLine AS l
    JOIN @Lines AS k ON k.Id = l.Id
    LEFT JOIN @Matched AS m ON m.LineId = l.Id;

    -- Pasul 6: propuneri pentru partenerul identificat al liniilor ramase (OP-urile lui neconfirmate pe cont), cel mult 5, dupa apropierea sumei.
    INSERT INTO fin.MatchSuggestion (BankStatementLineId, Kind, PaymentOrderId, PartnerId, Amount, Score, Description)
    SELECT c.LineId, 1, c.OrderId, c.PartnerId, c.Amount,
        CAST(CASE WHEN c.Gap >= 100 THEN 0 ELSE 100 - c.Gap END AS tinyint), CONCAT(N'OP ', c.Number, N' din ', CONVERT(char(10), c.DocumentDate, 104))
    FROM (SELECT k.Id AS LineId, o.Id AS OrderId, o.PartnerId, o.Amount, o.Number, o.DocumentDate,
                 CAST(ABS(o.Amount + k.Amount) * 100 / NULLIF(ABS(k.Amount), 0) AS decimal(19,2)) AS Gap,
                 ROW_NUMBER() OVER (PARTITION BY k.Id ORDER BY ABS(o.Amount + k.Amount), o.Id) AS Rank
          FROM @Lines AS k
          JOIN fin.PaymentOrder AS o ON o.PartnerId = k.PartnerId AND o.CompanyId = @CompanyId AND o.TreasuryAccountId = k.AccountId AND o.Status = 2
               AND o.ReversalOfId IS NULL AND o.ConfirmationStatus = 1
          WHERE k.Amount < 0 AND k.PartnerId IS NOT NULL AND k.Id NOT IN (SELECT LineId FROM @Matched)) AS c
    WHERE c.Rank <= 5;
    UPDATE l SET MatchStatus = 2
    FROM fin.BankStatementLine AS l
    WHERE l.Id IN (SELECT BankStatementLineId FROM fin.MatchSuggestion) AND l.Id IN (SELECT Id FROM @Lines) AND l.MatchStatus = 1;

    SELECT m.LineId AS Id, m.Source, m.AutoValidate, l.RowVersion, l.BookingDate, s.ContinuityStatus
    FROM @Matched AS m
    JOIN fin.BankStatementLine AS l ON l.Id = m.LineId
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId;
    SELECT (SELECT COUNT(*) FROM @Lines) AS Considered,
           (SELECT COUNT(*) FROM fin.BankStatementLine WHERE MatchStatus = 2 AND Id IN (SELECT Id FROM @Lines)) AS Proposed;
END;

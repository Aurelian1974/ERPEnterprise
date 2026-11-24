CREATE OR ALTER PROCEDURE fin.usp_OpenItem_LockForAllocation
    @CompanyId int,
    @Ids       nvarchar(max)
AS
BEGIN
    -- Partidele unui lot de alocari, blocate in ordinea Id-urilor (SP-16) in tranzactia apelantului, cu restul si valoarea lui contabila in
    -- lei, pentru calculul diferentelor de curs (F4 4.1), si regula de curs incrucisat din profilul partenerului (F4 3.3). Al doilea set:
    -- rularile validate care au reevaluat partidele (F4 4.6), cu partea aplicata valorii contabile si restul in valuta pe care s-a aplicat
    -- (restul de la sfarsitul lunii minus platile de dupa, corectate la validare).
    SET NOCOUNT ON;

    SELECT o.Id, o.PartnerId, o.Role, o.Kind, o.CurrencyCode, o.ExchangeRate, o.OpenAmount, o.OpenAmountRon, o.DocumentDate,
        p.CrossCurrencyRule, p.CrossCurrencyMarkupPercent,
        o.IndexCurrencyCode, o.IndexedOpenAmount, CASE WHEN o.IndexedAmount IS NULL THEN NULL ELSE ROUND(o.Amount / o.IndexedAmount, 6) END AS IndexRate
    FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    LEFT JOIN fin.PartnerFinanceProfile AS p ON p.CompanyId = o.CompanyId AND p.PartnerId = o.PartnerId AND p.Role = o.Role
    WHERE o.CompanyId = @CompanyId AND o.Id IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@Ids) AS j)
    ORDER BY o.Id;

    SELECT l.OpenItemId, l.RunId, r.RevaluationDate, l.AppliedDifferenceRon,
        ABS(l.ForeignAmount) - ISNULL((SELECT SUM(x.ForeignAmount) FROM fin.FxDifference AS x
                                       WHERE x.RevaluationRunId = l.RunId AND x.OpenItemId = l.OpenItemId AND x.AllocationId IS NOT NULL), 0) AS AppliedOn
    FROM fin.FxRevaluationLine AS l
    JOIN fin.FxRevaluationRun AS r ON r.Id = l.RunId AND r.Status = 2 AND r.CompanyId = @CompanyId
    WHERE l.IsCorrection = 0 AND l.AppliedDifferenceRon <> 0 AND l.OpenItemId IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@Ids) AS j)
    ORDER BY l.OpenItemId, r.RevaluationDate;
END;

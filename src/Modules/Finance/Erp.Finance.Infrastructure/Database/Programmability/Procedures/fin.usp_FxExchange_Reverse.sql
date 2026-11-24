CREATE OR ALTER PROCEDURE fin.usp_FxExchange_Reverse
    @CompanyId  int,
    @LineId     bigint,
    @Today      date,
    @UserId     int
AS
BEGIN
    -- Desfacerea validarii unei linii de schimb valutar (F4 4.3): diferenta perechii ei se scrie inversa, cu data desfacerii. Intoarce randurile.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Parts TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Parts (Id)
    SELECT p.Id FROM fin.BankStatementLinePart AS p WHERE p.BankStatementLineId = @LineId
    UNION
    SELECT q.Id FROM fin.BankStatementLinePart AS p
    JOIN fin.BankStatementLinePart AS q ON q.BankStatementLineId = p.PairedBankStatementLineId OR q.PairedBankStatementLineId = p.BankStatementLineId
    WHERE p.BankStatementLineId = @LineId;

    DECLARE @FxOut TABLE (Id bigint);
    INSERT INTO fin.FxDifference (CompanyId, DifferenceDate, Source, BankStatementLinePartId, CurrencyCode, ForeignAmount, CarryingAmountRon,
        SettlementAmountRon, DifferenceRon, GlAccountCode, IsReversal, ReversalOfId, CreatedBy)
    OUTPUT inserted.Id INTO @FxOut (Id)
    SELECT x.CompanyId, @Today, x.Source, x.BankStatementLinePartId, x.CurrencyCode, -x.ForeignAmount, -x.CarryingAmountRon, -x.SettlementAmountRon,
        -x.DifferenceRon, x.GlAccountCode, 1, x.Id, @UserId
    FROM fin.FxDifference AS x
    WHERE x.CompanyId = @CompanyId AND x.Source = 2 AND x.IsReversal = 0 AND x.BankStatementLinePartId IN (SELECT Id FROM @Parts)
      AND NOT EXISTS (SELECT 1 FROM fin.FxDifference AS r WHERE r.ReversalOfId = x.Id);

    SELECT x.Id, x.DifferenceDate, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed,
        x.GlAccountCode, x.IsReversal
    FROM fin.FxDifference AS x JOIN @FxOut AS f ON f.Id = x.Id;
END;

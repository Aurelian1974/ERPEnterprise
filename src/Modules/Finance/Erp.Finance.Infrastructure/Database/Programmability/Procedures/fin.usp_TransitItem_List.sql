CREATE OR ALTER PROCEDURE fin.usp_TransitItem_List
    @CompanyId         int,
    @TreasuryAccountId int = NULL,
    @AsOf              date
AS
BEGIN
    -- Sumele in tranzit ale firmei (02.02, F2 3.7), pe vechime, din fin.v_TransitItem.
    SET NOCOUNT ON;

    SELECT v.Kind, v.TreasuryAccountId, v.SourceAccountId, v.ItemDate, v.Amount, v.Reference, v.SourceId, DATEDIFF(day, v.ItemDate, @AsOf) AS AgeDays
    FROM fin.v_TransitItem AS v
    WHERE v.CompanyId = @CompanyId AND (@TreasuryAccountId IS NULL OR v.TreasuryAccountId = @TreasuryAccountId OR v.SourceAccountId = @TreasuryAccountId)
    ORDER BY v.ItemDate, v.Kind;
END;

CREATE OR ALTER PROCEDURE fin.usp_AltMapping_Unmapped
    @ChartId    int,
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Nemapatele unui plan alternativ (C1 4.5): conturile romanesti cu linii fara tinta in interval, cu rulajul debit, credit si numarul de linii.
    SET NOCOUNT ON;

    SELECT a.Id AS AccountId, a.Code, a.Name, SUM(CASE WHEN m.Side = 1 THEN m.AmountRon ELSE 0 END) AS Debit,
        SUM(CASE WHEN m.Side = 2 THEN m.AmountRon ELSE 0 END) AS Credit, COUNT(*) AS LineCount
    FROM fin.JournalLineAltAccount AS m
    JOIN fin.JournalLine AS l ON l.Id = m.JournalLineId
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    WHERE m.ChartId = @ChartId AND m.CompanyId = @CompanyId AND m.TargetAccountId IS NULL AND e.AccountingDate BETWEEN @From AND @To
    GROUP BY a.Id, a.Code, a.Name
    ORDER BY a.Code;
END;

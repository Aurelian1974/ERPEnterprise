CREATE OR ALTER PROCEDURE fin.usp_DueSchedule_Report
    @CompanyId int,
    @From      date,
    @To        date
AS
BEGIN
    -- Scadentarul (02.02, F3 10, macheta f3-04): incasarile si platile scadente pe zile intre @From si @To, plus restantele (scadenta inainte
    -- de @From), in numar de partide si lei (cursul partidei). Doar resturile de sensul rolului (facturi de incasat de la clienti, de platit
    -- furnizorilor); ratele (OpenItemSchedule) au scadenta lor. Intoarce cate un rand pe zi cu scadente, DueDay NULL = restante.
    SET NOCOUNT ON;

    WITH Due AS (
        SELECT o.Role, ISNULL(s.DueDate, o.DueDate) AS DueDate, ROUND(ISNULL(s.OpenAmount, o.OpenAmount) * o.ExchangeRate, 2) AS OpenRon
        FROM fin.OpenItem AS o
        LEFT JOIN fin.OpenItemSchedule AS s ON s.OpenItemId = o.Id AND s.OpenAmount <> 0
        WHERE o.CompanyId = @CompanyId AND o.OpenAmount <> 0 AND o.Kind IN (1, 3, 4, 6)
          AND ((o.Role = 1 AND o.Amount > 0) OR (o.Role = 2 AND o.Amount < 0)))
    SELECT CASE WHEN d.DueDate < @From THEN NULL ELSE d.DueDate END AS DueDay,
        SUM(CASE WHEN d.Role = 1 THEN 1 ELSE 0 END) AS ReceivableCount,
        ISNULL(SUM(CASE WHEN d.Role = 1 THEN d.OpenRon END), 0) AS ReceivableRon,
        SUM(CASE WHEN d.Role = 2 THEN 1 ELSE 0 END) AS PayableCount,
        ISNULL(-SUM(CASE WHEN d.Role = 2 THEN d.OpenRon END), 0) AS PayableRon
    FROM Due AS d
    WHERE d.DueDate <= @To
    GROUP BY CASE WHEN d.DueDate < @From THEN NULL ELSE d.DueDate END
    ORDER BY DueDay;
END;

CREATE OR ALTER PROCEDURE fin.usp_OpenItemSchedule_Refresh
    @Items nvarchar(max)
AS
BEGIN
    -- Restul de plata al ratelor (02.02, F3 3.1), dupa o alocare sau o desfacere: ce s-a stins din partida (valoarea minus restul) acopera
    -- ratele in ordinea scadentei. @Items: lista JSON [{"Id":...}]; partidele fara rate sunt ignorate. In tranzactia apelantului.
    SET NOCOUNT ON;

    DECLARE @Calc TABLE (Id bigint PRIMARY KEY, OpenAmount decimal(19,2));
    INSERT INTO @Calc (Id, OpenAmount)
    SELECT x.Id, SIGN(x.Amount) * (ABS(x.Amount) - CASE WHEN x.Covered < 0 THEN 0 WHEN x.Covered > ABS(x.Amount) THEN ABS(x.Amount) ELSE x.Covered END)
    FROM (SELECT s.Id, s.Amount,
                 ABS(o.Amount - o.OpenAmount)
                 - (SUM(ABS(s.Amount)) OVER (PARTITION BY s.OpenItemId ORDER BY s.DueDate, s.Id ROWS UNBOUNDED PRECEDING) - ABS(s.Amount)) AS Covered
          FROM fin.OpenItemSchedule AS s
          JOIN fin.OpenItem AS o ON o.Id = s.OpenItemId
          WHERE s.OpenItemId IN (SELECT j.Id FROM OPENJSON(@Items) WITH (Id bigint '$.Id') AS j)) AS x;

    UPDATE s SET OpenAmount = c.OpenAmount
    FROM fin.OpenItemSchedule AS s JOIN @Calc AS c ON c.Id = s.Id
    WHERE s.OpenAmount <> c.OpenAmount;
END;

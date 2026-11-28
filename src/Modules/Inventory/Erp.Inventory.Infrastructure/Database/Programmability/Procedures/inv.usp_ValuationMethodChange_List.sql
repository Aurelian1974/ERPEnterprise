CREATE OR ALTER PROCEDURE inv.usp_ValuationMethodChange_List
    @CompanyId int,
    @Status    tinyint = NULL,
    @Top       int = 200
AS
BEGIN
    -- Lista documentelor SM (macheta s1-04), cele mai noi intai, cu modelele (primele) si metoda veche a primului.
    SET NOCOUNT ON;

    SELECT TOP (@Top) d.Id, d.Number, d.NewMethod, d.EffectiveDate, d.Status,
        (SELECT COUNT(*) FROM inv.ValuationMethodChangeLine AS l WHERE l.ChangeId = d.Id) AS ItemCount,
        (SELECT TOP (1) i.Name FROM inv.ValuationMethodChangeLine AS l JOIN core.Item AS i ON i.Id = l.ItemId WHERE l.ChangeId = d.Id ORDER BY i.Code) AS FirstItemName
    FROM inv.ValuationMethodChange AS d
    WHERE d.CompanyId = @CompanyId AND (@Status IS NULL OR d.Status = @Status)
    ORDER BY d.EffectiveDate DESC, d.Id DESC;
END;

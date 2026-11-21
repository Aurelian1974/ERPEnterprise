CREATE OR ALTER PROCEDURE core.usp_ItemBlock_Get
    @ItemId int
AS
BEGIN
    -- Blocarea partiala a articolului (02.01, 3.6 punctul 6): 1 vanzare, 2 achizitie. Fara rand = neblocat.
    SET NOCOUNT ON;

    SELECT ISNULL(b.BlockedFor, 0) AS BlockedFor, b.Reason, b.BlockedAt, u.DisplayName AS BlockedByName, b.RowVersion
    FROM core.Item AS i
    LEFT JOIN core.ItemBlock AS b ON b.ItemId = i.Id
    LEFT JOIN core.[User] AS u ON u.Id = b.ModifiedBy AND b.BlockedFor > 0
    WHERE i.Id = @ItemId;
END;

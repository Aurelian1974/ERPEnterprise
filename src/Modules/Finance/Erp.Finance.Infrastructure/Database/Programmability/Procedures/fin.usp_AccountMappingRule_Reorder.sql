CREATE OR ALTER PROCEDURE fin.usp_AccountMappingRule_Reorder
    @ChartId  int,
    @Order    nvarchar(max),
    @UserId   int
AS
BEGIN
    -- Renumeroteaza prioritatile regulilor unui plan (C1 3.5, macheta c1-03: mutarea unui rand): JSON [{id, priority}] pentru toate regulile
    -- planului, cu prioritati unice (51809). Intai se muta temporar pe valori negative, ca indexul unic sa nu se loveasca la schimb.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @New TABLE (Id int PRIMARY KEY, Priority int);
    INSERT INTO @New (Id, Priority) SELECT Id, Priority FROM OPENJSON(@Order) WITH (Id int '$.id', Priority int '$.priority');

    IF (SELECT COUNT(DISTINCT Priority) FROM @New) <> (SELECT COUNT(*) FROM @New) OR EXISTS (SELECT 1 FROM @New WHERE Priority <= 0)
       OR (SELECT COUNT(*) FROM @New) <> (SELECT COUNT(*) FROM fin.AccountMappingRule WHERE TargetChartId = @ChartId)
       OR EXISTS (SELECT 1 FROM @New AS n WHERE NOT EXISTS (SELECT 1 FROM fin.AccountMappingRule AS r WHERE r.Id = n.Id AND r.TargetChartId = @ChartId))
        THROW 51809, N'Prioritățile trebuie să fie pozitive, unice și pentru toate regulile planului.', 1;

    BEGIN TRANSACTION;
    UPDATE fin.AccountMappingRule SET Priority = -Priority WHERE TargetChartId = @ChartId;
    UPDATE r SET Priority = n.Priority, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.AccountMappingRule AS r JOIN @New AS n ON n.Id = r.Id;
    COMMIT TRANSACTION;
END;

CREATE OR ALTER PROCEDURE core.usp_DimensionValue_Get
    @Id int
AS
BEGIN
    -- O valoare de dimensiune pentru module (IDimensionLookup, 02.01, 6).
    SET NOCOUNT ON;

    SELECT v.Id, v.CompanyId, v.DimensionType, v.Code, v.Name, v.ParentId, v.ValidFrom, v.ValidTo, v.IsActive
    FROM core.DimensionValue AS v
    WHERE v.Id = @Id;
END;

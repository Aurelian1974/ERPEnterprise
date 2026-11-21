CREATE OR ALTER FUNCTION core.fn_AuditRecordLabel
(
    @EntityType nvarchar(100),
    @EntityId   bigint,
    @Changes    nvarchar(max)
)
RETURNS TABLE
AS
RETURN
    -- Textul coloanei „Inregistrare” din Configurare › Audit: denumirea de azi a inregistrarii pentru entitatile principale,
    -- altfel campurile obisnuite din Changes (JSON cu nume camelCase). NULL cand nu se poate spune nimic.
    SELECT COALESCE(
        CASE @EntityType
            WHEN N'Core.Partner' THEN (SELECT x.Name FROM core.Partner AS x WHERE x.Id = @EntityId)
            WHEN N'Core.Item' THEN (SELECT x.Name FROM core.Item AS x WHERE x.Id = @EntityId)
            WHEN N'Core.User' THEN (SELECT x.DisplayName FROM core.[User] AS x WHERE x.Id = @EntityId)
            WHEN N'Core.Employee' THEN (SELECT x.LastName + N' ' + x.FirstName FROM core.Employee AS x WHERE x.Id = @EntityId)
            WHEN N'Core.Company' THEN (SELECT x.Name FROM core.Company AS x WHERE x.Id = @EntityId)
            WHEN N'Core.CompanyGroup' THEN (SELECT x.Name FROM core.CompanyGroup AS x WHERE x.Id = @EntityId)
            WHEN N'Core.Branch' THEN (SELECT x.Name FROM core.Branch AS x WHERE x.Id = @EntityId)
            WHEN N'Core.Location' THEN (SELECT x.Name FROM core.Location AS x WHERE x.Id = @EntityId)
            WHEN N'Core.Role' THEN (SELECT x.Name FROM core.Role AS x WHERE x.Id = @EntityId)
            WHEN N'Core.DimensionValue' THEN (SELECT x.Code + N' · ' + x.Name FROM core.DimensionValue AS x WHERE x.Id = @EntityId)
            WHEN N'Core.NumberSeries' THEN (SELECT x.Prefix + N' ' + CAST(x.[Year] AS nvarchar(4)) FROM core.NumberSeries AS x WHERE x.Id = @EntityId)
            WHEN N'Core.ExchangeRate' THEN JSON_VALUE(@Changes, N'$.currencyCode') + N' ' + JSON_VALUE(@Changes, N'$.rateDate')
        END,
        JSON_VALUE(@Changes, N'$.name'),
        JSON_VALUE(@Changes, N'$.code'),
        JSON_VALUE(@Changes, N'$.userName'),
        JSON_VALUE(@Changes, N'$.key'),
        JSON_VALUE(@Changes, N'$.fileName')) AS RecordLabel;

-- Proprietatile partenerilor livrate ca model (02.01, 3.2; macheta 04b): se adauga doar daca lipsesc, ca administratorul
-- sa le poata modifica sau dezactiva fara ca actualizarea sa le readuca (MIG-06). DataType: 1 text, 2 numar, 3 data,
-- 4 da/nu, 5 lista, 6 lista multipla, 7 utilizator. AppliesTo: 1 client, 2 furnizor, 3 ambele.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.PropertyDefinition AS target
USING (VALUES
    ('Partner', N'SEGMENT',         N'Segment',                       5, 1, N'Comercial', 10, 1, 1),
    ('Partner', N'SALES_AGENT',     N'Agent de vânzări',              7, 1, N'Comercial', 20, 0, 0),
    ('Partner', N'DELIVERY_ZONE',   N'Zonă de livrare',               5, 1, N'Comercial', 30, 0, 1),
    ('Partner', N'DELIVERY_DAYS',   N'Zile de livrare',               6, 1, N'Comercial', 40, 0, 0),
    ('Partner', N'SALES_CHANNEL',   N'Canal de vânzare',              5, 1, N'Comercial', 50, 0, 1),
    ('Partner', N'GDPR_MARKETING',  N'Acord marketing (GDPR)',        4, 1, N'Comercial', 60, 0, 1),
    ('Partner', N'SUPPLIER_RATING', N'Rating furnizor',               2, 2, N'Calitate',  10, 0, 0),
    ('Partner', N'ISO_9001',        N'Certificat ISO 9001',           4, 2, N'Calitate',  20, 0, 1),
    ('Partner', N'ISO_9001_UNTIL',  N'ISO 9001 valabil până la',      3, 2, N'Calitate',  30, 0, 0),
    ('Partner', N'CUSTOMER_CODE',   N'Cod client la furnizor',        1, 2, N'Calitate',  40, 0, 0),
    ('Partner', N'LEAD_TIME_DAYS',  N'Termen mediu de livrare (zile)', 2, 2, N'Calitate', 50, 0, 0),
    ('Partner', N'APPROVED',        N'Furnizor agreat',               4, 2, N'Calitate',  60, 0, 1),
    ('Partner', N'INTERNAL_NOTES',  N'Observații interne',            1, 3, N'Altele',    10, 0, 0)
) AS source (EntityType, Code, Name, DataType, AppliesTo, Section, SortOrder, IsRequired, IsFilterable)
ON target.EntityType = source.EntityType AND target.CompanyId IS NULL AND target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (EntityType, Code, Name, DataType, AppliesTo, Section, SortOrder, IsRequired, IsFilterable, CreatedBy)
    VALUES (source.EntityType, source.Code, source.Name, source.DataType, source.AppliesTo, source.Section, source.SortOrder,
        source.IsRequired, source.IsFilterable, 1);

MERGE core.PropertyOption AS target
USING (
    SELECT d.Id, o.Code, o.Label, o.SortOrder
    FROM (VALUES
        (N'SEGMENT',       N'DISTRIBUTION', N'Distribuție',  10),
        (N'SEGMENT',       N'RETAIL',       N'Retail',       20),
        (N'SEGMENT',       N'HORECA',       N'HoReCa',       30),
        (N'SEGMENT',       N'SERVICES',     N'Servicii',     40),
        (N'DELIVERY_ZONE', N'NORTH',        N'Moldova Nord', 10),
        (N'DELIVERY_ZONE', N'SOUTH',        N'Muntenia',     20),
        (N'DELIVERY_ZONE', N'WEST',         N'Banat',        30),
        (N'DELIVERY_ZONE', N'CENTER',       N'Transilvania', 40),
        (N'DELIVERY_DAYS', N'MON',          N'Luni',         10),
        (N'DELIVERY_DAYS', N'TUE',          N'Marți',        20),
        (N'DELIVERY_DAYS', N'WED',          N'Miercuri',     30),
        (N'DELIVERY_DAYS', N'THU',          N'Joi',          40),
        (N'DELIVERY_DAYS', N'FRI',          N'Vineri',       50),
        (N'SALES_CHANNEL', N'B2B',          N'B2B direct',   10),
        (N'SALES_CHANNEL', N'DISTRIBUTOR',  N'Prin distribuitor', 20),
        (N'SALES_CHANNEL', N'ONLINE',       N'Online',       30)
    ) AS o (DefinitionCode, Code, Label, SortOrder)
    JOIN core.PropertyDefinition AS d ON d.EntityType = 'Partner' AND d.CompanyId IS NULL AND d.Code = o.DefinitionCode
) AS source (PropertyDefinitionId, Code, Label, SortOrder)
ON target.PropertyDefinitionId = source.PropertyDefinitionId AND target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (PropertyDefinitionId, Code, Label, SortOrder, CreatedBy)
    VALUES (source.PropertyDefinitionId, source.Code, source.Label, source.SortOrder, 1);

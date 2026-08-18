IF NOT EXISTS (
    SELECT 1
FROM sys.columns
WHERE object_id = OBJECT_ID(N'administration.partner_addresses')
    AND name = N'street_number'
)
BEGIN
    ALTER TABLE administration.partner_addresses
        ADD street_number NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
FROM sys.columns
WHERE object_id = OBJECT_ID(N'administration.partner_addresses')
    AND name = N'block'
)
BEGIN
    ALTER TABLE administration.partner_addresses
        ADD block NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
FROM sys.columns
WHERE object_id = OBJECT_ID(N'administration.partner_addresses')
    AND name = N'staircase'
)
BEGIN
    ALTER TABLE administration.partner_addresses
        ADD staircase NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
FROM sys.columns
WHERE object_id = OBJECT_ID(N'administration.partner_addresses')
    AND name = N'floor'
)
BEGIN
    ALTER TABLE administration.partner_addresses
        ADD floor NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
FROM sys.columns
WHERE object_id = OBJECT_ID(N'administration.partner_addresses')
    AND name = N'apartment'
)
BEGIN
    ALTER TABLE administration.partner_addresses
        ADD apartment NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
FROM sys.columns
WHERE object_id = OBJECT_ID(N'administration.partner_addresses')
    AND name = N'building'
)
BEGIN
    ALTER TABLE administration.partner_addresses
        ADD building NVARCHAR(50) NULL;
END
GO

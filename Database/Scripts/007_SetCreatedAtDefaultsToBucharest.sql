-- 007_SetCreatedAtDefaultsToBucharest.sql
-- Update default constraints for CreatedAt columns to use Romania time (E. Europe Standard Time)

GO

SET NOCOUNT ON;
GO

-- Helper to replace default for a table/column
CREATE PROCEDURE dbo.SetDefaultToBucharest
    @SchemaName SYSNAME,
    @TableName SYSNAME,
    @ColumnName SYSNAME,
    @ConstraintName SYSNAME
AS
BEGIN
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @existing NVARCHAR(128);

    SELECT @existing = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = @TableName AND c.name = @ColumnName AND SCHEMA_NAME(t.schema_id) = @SchemaName;

    IF @existing IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName) + ' DROP CONSTRAINT ' + QUOTENAME(@existing) + ';';
        EXEC sp_executesql @sql;
    END

    -- Add new default using SYSUTCDATETIME() AT TIME ZONE 'E. Europe Standard Time' converted to datetime2
    SET @sql = N'ALTER TABLE ' + QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName) + ' ADD CONSTRAINT ' + QUOTENAME(@ConstraintName) + ' DEFAULT (CAST(SYSUTCDATETIME() AT TIME ZONE ''E. Europe Standard Time'' AS datetime2)) FOR ' + QUOTENAME(@ColumnName) + ';';
    EXEC sp_executesql @sql;
END
GO

-- Apply to Users.CreatedAt
EXEC dbo.SetDefaultToBucharest 'dbo','Users','CreatedAt','DF_Users_CreatedAt';
GO
-- Apply to Persoane.CreatedAt
EXEC dbo.SetDefaultToBucharest 'dbo','Persoane','CreatedAt','DF_Persoane_CreatedAt';
GO
-- Apply to Sessions.CreatedAt
EXEC dbo.SetDefaultToBucharest 'dbo','Sessions','CreatedAt','DF_Sessions_CreatedAt';
GO
-- Clean up helper
DROP PROCEDURE dbo.SetDefaultToBucharest;
GO

-- Verify results
SELECT t.name AS TableName, c.name AS ColumnName, dc.name AS DefaultConstraint, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
JOIN sys.tables t ON t.object_id = c.object_id
WHERE c.name = 'CreatedAt' AND t.name IN ('Users','Persoane','Sessions');
GO
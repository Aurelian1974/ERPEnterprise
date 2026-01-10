-- 005_AddConstraints.sql
-- Add the foreign key constraints that depend on both tables existing
-- Add FK from Users.PersoanaId -> Persoane(Id)
IF OBJECT_ID('dbo.FK_Users_Persoane') IS NULL
BEGIN
    ALTER TABLE dbo.Users
    ADD CONSTRAINT FK_Users_Persoane FOREIGN KEY (PersoanaId) REFERENCES dbo.Persoane(Id);
END
GO

-- Add FK from Persoane.CreatedBy -> Users(Id)
IF OBJECT_ID('dbo.FK_Persoane_CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.Persoane
    ADD CONSTRAINT FK_Persoane_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);
END
GO

-- Add FK from Persoane.UpdatedBy -> Users(Id)
IF OBJECT_ID('dbo.FK_Persoane_UpdatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.Persoane
    ADD CONSTRAINT FK_Persoane_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users(Id);
END
GO

PRINT 'Constraints added.';
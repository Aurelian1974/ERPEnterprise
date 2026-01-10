-- Create Sessions table for server-side session management
IF OBJECT_ID('dbo.Sessions', 'U') IS NOT NULL
    DROP TABLE dbo.Sessions;
GO

CREATE TABLE dbo.Sessions (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    UserId UNIQUEIDENTIFIER NOT NULL,
    Token UNIQUEIDENTIFIER NOT NULL,
    CircuitId NVARCHAR(200) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    LastHeartbeat DATETIME2 NULL,
    ExpiresAt DATETIME2 NULL,
    Metadata NVARCHAR(MAX) NULL
);

CREATE INDEX IX_Sessions_Token ON dbo.Sessions (Token);
CREATE INDEX IX_Sessions_UserId ON dbo.Sessions (UserId);
GO

-- Keep historical sessions for troubleshooting; cleanup will be handled by a background job

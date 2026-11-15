-- Migrare ids (08, 8.7): sesiunile active, codurile de rezerva 2FA si istoricul parolelor, folosite de store-urile Dapper
-- din Erp.Auth. Legatura cu contul Windows (ids.UserExternalLogin) si cheile API (ids.ApiKey) vin odata cu acele functii.

-- Sesiunile (8.7): cookie-ul poarta SessionId (claim sid); o sesiune revocata respinge cookie-ul in cel mult un minut.
CREATE TABLE ids.UserSession (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserSession PRIMARY KEY,
    SessionId     uniqueidentifier     NOT NULL,
    UserAccountId int                  NOT NULL CONSTRAINT FK_UserSession_UserAccount REFERENCES ids.UserAccount (Id),
    CreatedAt     datetime2(3)         NOT NULL CONSTRAINT DF_UserSession_CreatedAt DEFAULT (sysutcdatetime()),
    LastSeenAt    datetime2(3)         NOT NULL CONSTRAINT DF_UserSession_LastSeenAt DEFAULT (sysutcdatetime()),
    IpAddress     varchar(45)          NULL,
    UserAgent     nvarchar(300)        NULL,
    RevokedAt     datetime2(3)         NULL,
    CONSTRAINT UQ_UserSession_SessionId UNIQUE (SessionId)
);
GO

CREATE INDEX IX_UserSession_UserAccountId_Active ON ids.UserSession (UserAccountId) WHERE RevokedAt IS NULL;
GO

-- Codurile de rezerva 2FA (02.01, 11.2): doar hash-ul SHA-256, fiecare cod se foloseste o singura data.
CREATE TABLE ids.UserRecoveryCode (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserRecoveryCode PRIMARY KEY,
    UserAccountId int               NOT NULL CONSTRAINT FK_UserRecoveryCode_UserAccount REFERENCES ids.UserAccount (Id),
    CodeHash      char(64)          NOT NULL,
    CreatedAt     datetime2(3)      NOT NULL CONSTRAINT DF_UserRecoveryCode_CreatedAt DEFAULT (sysutcdatetime()),
    UsedAt        datetime2(3)      NULL,
    CONSTRAINT UQ_UserRecoveryCode_UserAccountId_CodeHash UNIQUE (UserAccountId, CodeHash)
);
GO

-- Istoricul parolelor (02.01, 11.2: parola noua diferita de ultimele 5).
CREATE TABLE ids.PasswordHistory (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PasswordHistory PRIMARY KEY,
    UserAccountId int                  NOT NULL CONSTRAINT FK_PasswordHistory_UserAccount REFERENCES ids.UserAccount (Id),
    PasswordHash  nvarchar(500)        NOT NULL,
    CreatedAt     datetime2(3)         NOT NULL CONSTRAINT DF_PasswordHistory_CreatedAt DEFAULT (sysutcdatetime())
);
GO

CREATE INDEX IX_PasswordHistory_UserAccountId_CreatedAt ON ids.PasswordHistory (UserAccountId, CreatedAt DESC);
GO

-- Lista de coduri de rezerva pentru ids.usp_UserRecoveryCode_Replace (SP-07).
CREATE TYPE ids.tt_RecoveryCode AS TABLE (
    CodeHash char(64) NOT NULL PRIMARY KEY
);

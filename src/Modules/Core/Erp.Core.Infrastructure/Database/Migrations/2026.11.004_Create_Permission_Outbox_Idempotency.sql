-- Nucleu (02.01, 4.1, 4.5, 7 si 8): catalogul de permisiuni, legatura rol-permisiune, outbox-ul, inbox-ul consumatorilor
-- si cheile de idempotenta folosite de pipeline-ul comun al comenzilor.

-- Permisiunile au format Modul.Resursa.Actiune si se compara exact (SQL-21, NET-21).
CREATE TABLE core.Permission (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permission PRIMARY KEY,
    Code        nvarchar(100)     NOT NULL,
    ModuleCode  nvarchar(30)      NOT NULL,
    Description nvarchar(300)     NOT NULL,
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_Permission_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT UQ_Permission_Code UNIQUE (Code),
    CONSTRAINT CK_Permission_CodeFormat CHECK (Code LIKE N'%_.%_.%_' AND Code LIKE ModuleCode + N'.%')
);
GO

CREATE INDEX IX_Permission_ModuleCode ON core.Permission (ModuleCode);
GO

-- Abatere permanenta de la SQL-13 (02.01, 7): tabela de legatura pura, cheie compusa.
CREATE TABLE core.RolePermission (
    RoleId       int NOT NULL CONSTRAINT FK_RolePermission_Role REFERENCES core.Role (Id),
    PermissionId int NOT NULL CONSTRAINT FK_RolePermission_Permission REFERENCES core.Permission (Id),
    CONSTRAINT PK_RolePermission PRIMARY KEY (RoleId, PermissionId)
);
GO

CREATE INDEX IX_RolePermission_PermissionId ON core.RolePermission (PermissionId);
GO

-- Outbox (P8, 4.5): evenimentul se scrie in aceeasi tranzactie cu documentul; procesatorul il livreaza dupa commit.
-- Status: 0 = in asteptare, 1 = procesat, 2 = esuat definitiv.
CREATE TABLE core.Outbox (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Outbox PRIMARY KEY,
    EventId       uniqueidentifier     NOT NULL,
    EventType     nvarchar(200)        NOT NULL,
    EventVersion  int                  NOT NULL,
    CompanyId     int                  NULL,
    CorrelationId uniqueidentifier     NOT NULL,
    Payload       nvarchar(max)        NOT NULL,
    OccurredAt    datetime2(3)         NOT NULL,
    Status        tinyint              NOT NULL CONSTRAINT DF_Outbox_Status DEFAULT (0),
    Attempts      int                  NOT NULL CONSTRAINT DF_Outbox_Attempts DEFAULT (0),
    NextAttemptAt datetime2(3)         NULL,
    ProcessedAt   datetime2(3)         NULL,
    LastError     nvarchar(2000)       NULL,
    CreatedAt     datetime2(3)         NOT NULL CONSTRAINT DF_Outbox_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT UQ_Outbox_EventId UNIQUE (EventId),
    CONSTRAINT CK_Outbox_PayloadJson CHECK (ISJSON(Payload) = 1),
    CONSTRAINT CK_Outbox_Status CHECK (Status IN (0, 1, 2)),
    CONSTRAINT CK_Outbox_EventVersion CHECK (EventVersion >= 1)
);
GO

CREATE INDEX IX_Outbox_Pending ON core.Outbox (Id) INCLUDE (NextAttemptAt) WHERE ProcessedAt IS NULL;
GO

-- Inbox (4.5): fiecare consumator noteaza evenimentul procesat, ca acelasi mesaj sa nu produca efecte de doua ori.
CREATE TABLE core.InboxProcessed (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InboxProcessed PRIMARY KEY,
    EventId     uniqueidentifier     NOT NULL,
    Consumer    nvarchar(200)        NOT NULL,
    ProcessedAt datetime2(3)         NOT NULL CONSTRAINT DF_InboxProcessed_ProcessedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT UQ_InboxProcessed_EventId_Consumer UNIQUE (EventId, Consumer)
);
GO

-- Idempotenta (4.1): cheia din antetul Idempotency-Key si rezultatul primei cereri, pastrate 24 de ore (4.9).
CREATE TABLE core.IdempotencyKey (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IdempotencyKey PRIMARY KEY,
    UserId      int                  NOT NULL CONSTRAINT FK_IdempotencyKey_User REFERENCES core.[User] (Id),
    [Key]       nvarchar(100)        NOT NULL,
    RequestType nvarchar(300)        NOT NULL,
    ResultJson  nvarchar(max)        NULL,
    CreatedAt   datetime2(3)         NOT NULL CONSTRAINT DF_IdempotencyKey_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT UQ_IdempotencyKey_UserId_Key UNIQUE (UserId, [Key]),
    CONSTRAINT CK_IdempotencyKey_ResultJson CHECK (ResultJson IS NULL OR ISJSON(ResultJson) = 1)
);
GO

CREATE INDEX IX_IdempotencyKey_CreatedAt ON core.IdempotencyKey (CreatedAt);

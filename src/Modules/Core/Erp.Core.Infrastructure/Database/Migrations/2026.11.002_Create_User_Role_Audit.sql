-- Nucleu (02.01, 3.4, 3.5 si 7): utilizatori, roluri, atribuiri si jurnalul de audit, in forma necesara administratorului
-- initial (08, 8.8). Cheile straine spre core.Company, core.Employee si core.Partner (LastCompanyId, EmployeeId,
-- ExternalPartnerId, UserRoleAssignment.CompanyId, AuditEntry.CompanyId) se adauga de migrarile care creeaza acele tabele.
CREATE TABLE core.[User] (
    Id                      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_User PRIMARY KEY,
    SubjectId               uniqueidentifier  NOT NULL,
    UserName                nvarchar(50)      NOT NULL,
    DisplayName             nvarchar(200)     NOT NULL,
    FirstName               nvarchar(100)     NULL,
    LastName                nvarchar(100)     NULL,
    Email                   nvarchar(256)     NULL,
    IsActive                bit               NOT NULL CONSTRAINT DF_User_IsActive DEFAULT (1),
    IsSystem                bit               NOT NULL CONSTRAINT DF_User_IsSystem DEFAULT (0),
    IsEmployee              bit               NOT NULL CONSTRAINT DF_User_IsEmployee DEFAULT (0),
    EmployeeId              int               NULL,
    ExternalPartnerId       int               NULL,
    SponsorUserId           int               NULL CONSTRAINT FK_User_User_Sponsor REFERENCES core.[User] (Id),
    AccessReason            nvarchar(500)     NULL,
    ValidFrom               date              NULL,
    ValidUntil              date              NULL,
    LastLoginAt             datetime2(3)      NULL,
    LastCompanyId           int               NULL,
    Language                varchar(10)       NOT NULL CONSTRAINT DF_User_Language DEFAULT ('ro'),
    NotificationPreferences nvarchar(max)     NULL,
    CreatedAt               datetime2(3)      NOT NULL CONSTRAINT DF_User_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy               int               NOT NULL,
    ModifiedAt              datetime2(3)      NULL,
    ModifiedBy              int               NULL,
    RowVersion              rowversion        NOT NULL,
    CONSTRAINT UQ_User_SubjectId UNIQUE (SubjectId),
    CONSTRAINT UQ_User_UserName UNIQUE (UserName),
    CONSTRAINT CK_User_UserNameLower CHECK (UserName = LOWER(UserName)),
    CONSTRAINT CK_User_NotificationPreferencesJson CHECK (NotificationPreferences IS NULL OR ISJSON(NotificationPreferences) = 1),
    -- Trei tipuri de cont (3.4): de sistem, angajat sau extern.
    CONSTRAINT CK_User_Kind CHECK (
        (IsSystem = 1 AND IsEmployee = 0 AND EmployeeId IS NULL AND ExternalPartnerId IS NULL AND SponsorUserId IS NULL
            AND AccessReason IS NULL AND ValidUntil IS NULL)
        OR (IsSystem = 0 AND IsEmployee = 1 AND EmployeeId IS NOT NULL)
        OR (IsSystem = 0 AND IsEmployee = 0 AND EmployeeId IS NULL AND ValidUntil IS NOT NULL AND SponsorUserId IS NOT NULL
            AND AccessReason IS NOT NULL)
    )
);
GO

CREATE UNIQUE INDEX UX_User_EmployeeId ON core.[User] (EmployeeId) WHERE EmployeeId IS NOT NULL;
CREATE INDEX IX_User_SponsorUserId ON core.[User] (SponsorUserId) WHERE SponsorUserId IS NOT NULL;
CREATE INDEX IX_User_ValidUntil ON core.[User] (ValidUntil) WHERE IsEmployee = 0 AND IsSystem = 0;
GO

CREATE TABLE core.Role (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
    Code        nvarchar(50)      NOT NULL,
    Name        nvarchar(200)     NOT NULL,
    Description nvarchar(500)     NULL,
    IsSystem    bit               NOT NULL CONSTRAINT DF_Role_IsSystem DEFAULT (0),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_Role_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT UQ_Role_Code UNIQUE (Code)
);
GO

CREATE TABLE core.UserRoleAssignment (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserRoleAssignment PRIMARY KEY,
    UserId         int               NOT NULL CONSTRAINT FK_UserRoleAssignment_User REFERENCES core.[User] (Id),
    RoleId         int               NOT NULL CONSTRAINT FK_UserRoleAssignment_Role REFERENCES core.Role (Id),
    CompanyId      int               NULL,      -- NULL = toata instalarea, doar pentru rolul ADMIN (3.4)
    DimensionScope nvarchar(max)     NULL,
    CreatedAt      datetime2(3)      NOT NULL CONSTRAINT DF_UserRoleAssignment_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int               NOT NULL,
    ModifiedAt     datetime2(3)      NULL,
    ModifiedBy     int               NULL,
    RowVersion     rowversion        NOT NULL,
    CONSTRAINT CK_UserRoleAssignment_DimensionScopeJson CHECK (DimensionScope IS NULL OR ISJSON(DimensionScope) = 1)
);
GO

CREATE INDEX IX_UserRoleAssignment_UserId_CompanyId ON core.UserRoleAssignment (UserId, CompanyId);
CREATE INDEX IX_UserRoleAssignment_RoleId ON core.UserRoleAssignment (RoleId);
GO

CREATE TABLE core.AuditEntry (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEntry PRIMARY KEY,
    OccurredAt    datetime2(3)      NOT NULL,
    UserId        int               NOT NULL CONSTRAINT FK_AuditEntry_User REFERENCES core.[User] (Id),
    CompanyId     int               NULL,
    Action        nvarchar(100)     NOT NULL,
    EntityType    nvarchar(100)     NOT NULL,
    EntityId      bigint            NULL,
    Changes       nvarchar(max)     NULL,
    IpAddress     varchar(45)       NULL,
    CorrelationId uniqueidentifier  NOT NULL,
    CONSTRAINT CK_AuditEntry_ChangesJson CHECK (Changes IS NULL OR ISJSON(Changes) = 1)
);
GO

CREATE INDEX IX_AuditEntry_EntityType_EntityId ON core.AuditEntry (EntityType, EntityId);
CREATE INDEX IX_AuditEntry_UserId_OccurredAt ON core.AuditEntry (UserId, OccurredAt);
GO

-- SP-21: jurnalul de audit nu se modifica si nu se sterge (50009).
CREATE TRIGGER core.tr_AuditEntry_Immutable ON core.AuditEntry
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50009, N'Jurnalul de audit nu poate fi modificat sau șters.', 1;
END;

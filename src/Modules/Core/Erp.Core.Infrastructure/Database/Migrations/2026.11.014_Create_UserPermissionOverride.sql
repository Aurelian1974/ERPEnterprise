-- Exceptiile de drepturi pe utilizator (02.01, 3.4 si 8; deciziile lui Aurelian din 21.11.2026): peste drepturile mostenite din
-- rolurile lui, un utilizator poate primi un drept in plus (G) sau i se poate revoca unul (R). Exceptia e pe firma (CompanyId) sau pe
-- toate firmele (CompanyId NULL); revocarea castiga. Un drept adaugat cere motivul.
CREATE TABLE core.UserPermissionOverride (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserPermissionOverride PRIMARY KEY,
    UserId       int               NOT NULL CONSTRAINT FK_UserPermissionOverride_User REFERENCES core.[User] (Id),
    PermissionId int               NOT NULL CONSTRAINT FK_UserPermissionOverride_Permission REFERENCES core.Permission (Id),
    CompanyId    int               NULL CONSTRAINT FK_UserPermissionOverride_Company REFERENCES core.Company (Id),
    Effect       char(1)           NOT NULL CONSTRAINT CK_UserPermissionOverride_Effect CHECK (Effect IN ('G', 'R')),
    Reason       nvarchar(500)     NULL,
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_UserPermissionOverride_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    CONSTRAINT CK_UserPermissionOverride_GrantReason CHECK (Effect = 'R' OR (Reason IS NOT NULL AND LEN(Reason) > 0))
);
GO

-- O singura exceptie pe utilizator, drept si firma (sau pe toate firmele, CompanyId NULL).
CREATE UNIQUE INDEX UX_UserPermissionOverride_UserId_PermissionId_CompanyId ON core.UserPermissionOverride (UserId, PermissionId, CompanyId);
CREATE INDEX IX_UserPermissionOverride_PermissionId ON core.UserPermissionOverride (PermissionId);
CREATE INDEX IX_UserPermissionOverride_CompanyId ON core.UserPermissionOverride (CompanyId) WHERE CompanyId IS NOT NULL;
GO

-- Permisiunile unui rol, salvate impreuna (core.usp_Role_Save, SP-07).
CREATE TYPE core.PermissionIdList AS TABLE (
    PermissionId int NOT NULL PRIMARY KEY
);
GO

-- Exceptiile utilizatorilor dintr-o salvare a matricei (core.usp_UserPermissionOverride_Save): lista inlocuieste exceptiile
-- fiecarui utilizator din @UserIds pe firma aleasa.
CREATE TYPE core.UserPermissionOverrideList AS TABLE (
    UserId       int           NOT NULL,
    PermissionId int           NOT NULL,
    Effect       char(1)       NOT NULL,
    Reason       nvarchar(500) NULL,
    PRIMARY KEY (UserId, PermissionId)
);
GO

CREATE TYPE core.IdList AS TABLE (
    Id int NOT NULL PRIMARY KEY
);
GO

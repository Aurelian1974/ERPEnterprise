-- Domeniul drepturilor si proprietarul articolelor si partenerilor (02.01, 2, 3.2-3.4; deciziile lui Aurelian din 21.11.2026).
-- 1. Un rol se atribuie pe instalare (toate campurile NULL, doar ADMIN), pe un grup de firme (CompanyGroupId: firmele grupului si
--    ale subgrupurilor), pe o firma (CompanyId), pe un punct de lucru (CompanyId + BranchId) sau pe o locatie (CompanyId + LocationId).
--    Orice rol se poate atribui pe grup.
-- 2. Un articol sau un partener e comun grupului (fara proprietar, ca pana acum) sau propriu unei firme, unui punct de lucru ori unei
--    locatii (OwnerCompanyId + optional OwnerBranchId sau OwnerLocationId). Codul si CUI-ul raman unice in toata baza.

-- Locatia in firma ei, pentru cheile compuse de mai jos (ca UQ_Branch_Id_CompanyId).
ALTER TABLE core.Location ADD CONSTRAINT UQ_Location_Id_CompanyId UNIQUE (Id, CompanyId);
GO

ALTER TABLE core.UserRoleAssignment ADD
    CompanyGroupId int NULL CONSTRAINT FK_UserRoleAssignment_CompanyGroup REFERENCES core.CompanyGroup (Id),
    BranchId       int NULL,
    LocationId     int NULL;
GO

ALTER TABLE core.UserRoleAssignment ADD
    CONSTRAINT FK_UserRoleAssignment_Branch FOREIGN KEY (BranchId, CompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT FK_UserRoleAssignment_Location FOREIGN KEY (LocationId, CompanyId) REFERENCES core.Location (Id, CompanyId),
    CONSTRAINT CK_UserRoleAssignment_Scope CHECK (
        (CompanyGroupId IS NULL OR (CompanyId IS NULL AND BranchId IS NULL AND LocationId IS NULL))
        AND ((BranchId IS NULL AND LocationId IS NULL) OR CompanyId IS NOT NULL)
        AND (BranchId IS NULL OR LocationId IS NULL));
GO

-- Acelasi rol o singura data pe acelasi nod (instalare, grup, firma, punct de lucru sau locatie).
DROP INDEX UX_UserRoleAssignment_UserId_RoleId_CompanyId ON core.UserRoleAssignment;
CREATE UNIQUE INDEX UX_UserRoleAssignment_UserId_RoleId_Scope
    ON core.UserRoleAssignment (UserId, RoleId, CompanyGroupId, CompanyId, BranchId, LocationId);
CREATE INDEX IX_UserRoleAssignment_CompanyGroupId ON core.UserRoleAssignment (CompanyGroupId) WHERE CompanyGroupId IS NOT NULL;
GO

-- Tipul listei de atribuiri primeste nodul; procedura care il foloseste se recreeaza din Programmability.
DROP PROCEDURE IF EXISTS core.usp_UserRoleAssignment_Save;
DROP TYPE core.UserRoleAssignmentList;
GO

CREATE TYPE core.UserRoleAssignmentList AS TABLE (
    RoleId         int NOT NULL,
    CompanyGroupId int NULL,
    CompanyId      int NULL,
    BranchId       int NULL,
    LocationId     int NULL
);
GO

ALTER TABLE core.Item ADD
    OwnerCompanyId  int NULL CONSTRAINT FK_Item_OwnerCompany REFERENCES core.Company (Id),
    OwnerBranchId   int NULL,
    OwnerLocationId int NULL;
GO

ALTER TABLE core.Item ADD
    CONSTRAINT FK_Item_OwnerBranch FOREIGN KEY (OwnerBranchId, OwnerCompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT FK_Item_OwnerLocation FOREIGN KEY (OwnerLocationId, OwnerCompanyId) REFERENCES core.Location (Id, CompanyId),
    CONSTRAINT CK_Item_Owner CHECK (
        ((OwnerBranchId IS NULL AND OwnerLocationId IS NULL) OR OwnerCompanyId IS NOT NULL)
        AND (OwnerBranchId IS NULL OR OwnerLocationId IS NULL));
GO

CREATE INDEX IX_Item_OwnerCompanyId ON core.Item (OwnerCompanyId, OwnerBranchId, OwnerLocationId) WHERE OwnerCompanyId IS NOT NULL;
GO

ALTER TABLE core.Partner ADD
    OwnerCompanyId  int NULL CONSTRAINT FK_Partner_OwnerCompany REFERENCES core.Company (Id),
    OwnerBranchId   int NULL,
    OwnerLocationId int NULL;
GO

ALTER TABLE core.Partner ADD
    CONSTRAINT FK_Partner_OwnerBranch FOREIGN KEY (OwnerBranchId, OwnerCompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT FK_Partner_OwnerLocation FOREIGN KEY (OwnerLocationId, OwnerCompanyId) REFERENCES core.Location (Id, CompanyId),
    CONSTRAINT CK_Partner_Owner CHECK (
        ((OwnerBranchId IS NULL AND OwnerLocationId IS NULL) OR OwnerCompanyId IS NOT NULL)
        AND (OwnerBranchId IS NULL OR OwnerLocationId IS NULL));
GO

CREATE INDEX IX_Partner_OwnerCompanyId ON core.Partner (OwnerCompanyId, OwnerBranchId, OwnerLocationId) WHERE OwnerCompanyId IS NOT NULL;
GO

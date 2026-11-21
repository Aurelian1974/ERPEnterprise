-- Administrarea utilizatorilor (02.01, 3.4 si 11; machetele 06, 06b, 06c): fisa minima de angajat in Nucleu, legata de
-- utilizatorii angajati, si campurile utilizatorului extern din macheta 06c (tipul accesului, telefonul).

-- Fisa minima de angajat (3.4): in Nucleu, ca utilizatorii sa se poata lega de ea si fara modulul HR, care o extinde in schema hr.
-- Marca (EmployeeNo) e unica pe firma fara diferenta de majuscule (SQL-24); cautarea dupa nume fara majuscule si diacritice (SQL-25),
-- dupa prenume sau dupa nume.
CREATE TABLE core.Employee (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employee PRIMARY KEY,
    CompanyId      int               NOT NULL CONSTRAINT FK_Employee_Company REFERENCES core.Company (Id),
    BranchId       int               NULL,
    EmployeeNo     nvarchar(20)      NOT NULL,
    FirstName      nvarchar(100)     NOT NULL,
    LastName       nvarchar(100)     NOT NULL,
    Department     nvarchar(100)     NULL,
    JobTitle       nvarchar(100)     NULL,
    WorkEmail      nvarchar(256)     NULL,
    Phone          nvarchar(30)      NULL,
    HireDate       date              NULL,
    LeaveDate      date              NULL,
    IsActive       bit               NOT NULL CONSTRAINT DF_Employee_IsActive DEFAULT (1),
    CreatedAt      datetime2(3)      NOT NULL CONSTRAINT DF_Employee_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int               NOT NULL,
    ModifiedAt     datetime2(3)      NULL,
    ModifiedBy     int               NULL,
    RowVersion     rowversion        NOT NULL,
    EmployeeNoKey  AS UPPER(EmployeeNo) PERSISTED,
    NameSearch     AS ((FirstName + N' ' + LastName) COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,
    LastNameSearch AS ((LastName + N' ' + FirstName) COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,
    -- Punctul de lucru apartine firmei angajatului (cheia compusa, ca la locatii si serii).
    CONSTRAINT FK_Employee_Branch FOREIGN KEY (BranchId, CompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT CK_Employee_Dates CHECK (LeaveDate IS NULL OR HireDate IS NULL OR LeaveDate >= HireDate)
);
GO

CREATE UNIQUE INDEX UX_Employee_CompanyId_EmployeeNoKey ON core.Employee (CompanyId, EmployeeNoKey);
CREATE INDEX IX_Employee_BranchId ON core.Employee (BranchId) WHERE BranchId IS NOT NULL;
CREATE INDEX IX_Employee_NameSearch ON core.Employee (NameSearch) INCLUDE (CompanyId, EmployeeNo, IsActive);
CREATE INDEX IX_Employee_LastNameSearch ON core.Employee (LastNameSearch) INCLUDE (CompanyId, EmployeeNo, IsActive);
CREATE INDEX IX_Employee_LeaveDate ON core.Employee (LeaveDate) WHERE LeaveDate IS NOT NULL;
GO

ALTER TABLE core.[User] ADD CONSTRAINT FK_User_Employee FOREIGN KEY (EmployeeId) REFERENCES core.Employee (Id);
GO

-- Utilizatorul extern (macheta 06c): tipul accesului (lista fixa in cod: AUDITOR, CONSULTANT, ACCOUNTANT, TECHNICAL = cont tehnic
-- fara parola interactiva) si telefonul. Cautarea dupa numele afisat, fara majuscule si diacritice (SQL-25).
ALTER TABLE core.[User] ADD
    ExternalAccessType varchar(20)   NULL,
    Phone              nvarchar(30)  NULL,
    NameSearch         AS (DisplayName COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED;
GO

ALTER TABLE core.[User] ADD
    CONSTRAINT CK_User_ExternalAccessType CHECK (
        ExternalAccessType IS NULL
        OR (IsSystem = 0 AND IsEmployee = 0 AND ExternalAccessType IN ('AUDITOR', 'CONSULTANT', 'ACCOUNTANT', 'TECHNICAL')));
GO

CREATE INDEX IX_User_NameSearch ON core.[User] (NameSearch) INCLUDE (UserName, IsActive);
CREATE INDEX IX_User_ExternalPartnerId ON core.[User] (ExternalPartnerId) WHERE ExternalPartnerId IS NOT NULL;
GO

-- Acelasi rol nu se atribuie de doua ori pe aceeasi firma (sau de doua ori la nivelul instalarii, CompanyId NULL).
CREATE UNIQUE INDEX UX_UserRoleAssignment_UserId_RoleId_CompanyId ON core.UserRoleAssignment (UserId, RoleId, CompanyId);
CREATE INDEX IX_UserRoleAssignment_CompanyId ON core.UserRoleAssignment (CompanyId) WHERE CompanyId IS NOT NULL;
GO

-- Atribuirile de roluri ale unui utilizator, salvate impreuna (core.usp_UserRoleAssignment_Save, SP-07).
CREATE TYPE core.UserRoleAssignmentList AS TABLE (
    RoleId    int NOT NULL,
    CompanyId int NULL
);
GO

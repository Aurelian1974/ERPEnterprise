-- Licenta instalarii (02.01 InstallationLicense; INSTALARE_ACTUALIZARE.md 8.9, pasul I-12): copie pentru afisare si istoric.
-- Sursa de adevar la pornire ramane fisierul license\erp.lic. Licenta curenta are IsCurrent = 1; randurile vechi raman (SQL-20).
CREATE TABLE core.InstallationLicense (
    Id                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InstallationLicense PRIMARY KEY,
    LicenseId             nvarchar(50)      NOT NULL,
    KeyId                 nvarchar(50)      NOT NULL,
    CustomerName          nvarchar(200)     NOT NULL,
    FiscalCodes           nvarchar(max)     NOT NULL,
    Modules               nvarchar(max)     NOT NULL,
    LimitCompanies        int               NOT NULL,
    LimitUsers            int               NOT NULL,
    LimitPayrollEmployees int               NOT NULL,
    LimitPosTerminals     int               NOT NULL,
    IssuedDate            date              NOT NULL,
    ValidUntil            date              NOT NULL,
    Signature             varchar(200)      NOT NULL,
    IsCurrent             bit               NOT NULL CONSTRAINT DF_InstallationLicense_IsCurrent DEFAULT (0),
    CreatedAt             datetime2(3)      NOT NULL CONSTRAINT DF_InstallationLicense_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy             int               NOT NULL,
    ModifiedAt            datetime2(3)      NULL,
    ModifiedBy            int               NULL,
    RowVersion            rowversion        NOT NULL,
    CONSTRAINT CK_InstallationLicense_FiscalCodesJson CHECK (ISJSON(FiscalCodes) = 1),
    CONSTRAINT CK_InstallationLicense_ModulesJson CHECK (ISJSON(Modules) = 1),
    CONSTRAINT CK_InstallationLicense_Limits CHECK (LimitCompanies >= 0 AND LimitUsers >= 0 AND LimitPayrollEmployees >= 0 AND LimitPosTerminals >= 0),
    CONSTRAINT CK_InstallationLicense_Validity CHECK (ValidUntil >= IssuedDate)
);
GO

CREATE UNIQUE INDEX UX_InstallationLicense_IsCurrent ON core.InstallationLicense (IsCurrent) WHERE IsCurrent = 1;

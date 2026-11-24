-- Situatiile financiare (02.02, C5 3.6): pe firma, an si tip (1 anuale, 2 raportarea semestriala), categoria entitatii propusa din criteriile
-- legale, starea (1 ciorna, 2 verificata, 3 generata, 4 depusa) si recipisa; liniile formularelor (F10 bilant, F20 cont de profit si pierdere)
-- cu anul curent si cel precedent, calculate din definitiile livrate (decizia 2).
CREATE TABLE fin.FinancialStatement (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialStatement PRIMARY KEY,
    CompanyId           int               NOT NULL CONSTRAINT FK_FinancialStatement_Company REFERENCES core.Company (Id),
    Year                smallint          NOT NULL,
    Kind                tinyint           NOT NULL,
    EntityCategory      tinyint           NOT NULL,
    Status              tinyint           NOT NULL CONSTRAINT DF_FinancialStatement_Status DEFAULT (1),
    IsBalanced          bit               NOT NULL,
    CalculatedAt        datetime2(3)      NOT NULL,
    CalculatedBy        int               NOT NULL,
    GeneratedAt         datetime2(3)      NULL,
    SubmittedAt         datetime2(3)      NULL,
    RegistrationNumber  nvarchar(50)      NULL,
    FileContent         varbinary(max)    NULL,
    FileName            nvarchar(260)     NULL,
    RowVersion          rowversion        NOT NULL,
    CONSTRAINT CK_FinancialStatement_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_FinancialStatement_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_FinancialStatement_Category CHECK (EntityCategory BETWEEN 1 AND 4)
);

CREATE UNIQUE INDEX UX_FinancialStatement_Company_Year_Kind ON fin.FinancialStatement (CompanyId, Year, Kind);

CREATE TABLE fin.FinancialStatementLine (
    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialStatementLine PRIMARY KEY,
    StatementId  int               NOT NULL CONSTRAINT FK_FinancialStatementLine_Statement REFERENCES fin.FinancialStatement (Id),
    FormCode     varchar(10)       NOT NULL,
    RowCode      varchar(30)       NOT NULL,
    Label        nvarchar(300)     NOT NULL,
    SortOrder    int               NOT NULL,
    IsTotal      bit               NOT NULL,
    CurrentYear  decimal(19,2)     NULL,
    PriorYear    decimal(19,2)     NULL,
    CONSTRAINT UQ_FinancialStatementLine UNIQUE (StatementId, FormCode, RowCode)
);

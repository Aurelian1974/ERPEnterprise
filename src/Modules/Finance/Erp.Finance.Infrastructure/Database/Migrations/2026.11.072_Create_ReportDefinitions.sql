-- Rapoartele definite (02.02, C5 3.5, decizia 4): acelasi motor pentru situatiile livrate (Kind 1, needitabile, versionate pe data) si pentru
-- rapoartele clientului (Kind 2, ale firmei); randurile au formule pe conturi, dimensiuni si alte randuri, interpretate in .NET (SEC-03).
CREATE TABLE fin.ReportDefinition (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReportDefinition PRIMARY KEY,
    Code        varchar(30)       NOT NULL,
    Name        nvarchar(200)     NOT NULL,
    Kind        tinyint           NOT NULL,
    CompanyId   int               NULL CONSTRAINT FK_ReportDefinition_Company REFERENCES core.Company (Id),
    ChartId     int               NULL CONSTRAINT FK_ReportDefinition_Chart REFERENCES fin.ChartOfAccounts (Id),
    Version     smallint          NOT NULL CONSTRAINT DF_ReportDefinition_Version DEFAULT (1),
    ValidFrom   date              NULL,
    ValidTo     date              NULL,
    IsActive    bit               NOT NULL CONSTRAINT DF_ReportDefinition_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_ReportDefinition_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT CK_ReportDefinition_Kind CHECK ((Kind = 1 AND CompanyId IS NULL) OR (Kind = 2 AND CompanyId IS NOT NULL))
);

CREATE UNIQUE INDEX UX_ReportDefinition_Code_Version ON fin.ReportDefinition (Code, Version, CompanyId);

CREATE TABLE fin.ReportRow (
    DefinitionId  int               NOT NULL CONSTRAINT FK_ReportRow_Definition REFERENCES fin.ReportDefinition (Id),
    RowCode       varchar(30)       NOT NULL,
    Label         nvarchar(300)     NOT NULL,
    Formula       nvarchar(1000)    NULL,
    IsTotal       bit               NOT NULL CONSTRAINT DF_ReportRow_IsTotal DEFAULT (0),
    Indent        tinyint           NOT NULL CONSTRAINT DF_ReportRow_Indent DEFAULT (0),
    SortOrder     int               NOT NULL,
    CONSTRAINT PK_ReportRow PRIMARY KEY (DefinitionId, RowCode)
);

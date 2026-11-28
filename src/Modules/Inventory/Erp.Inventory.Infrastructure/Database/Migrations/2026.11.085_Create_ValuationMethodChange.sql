-- Stocuri S3.5 (02.05, S1 4.5; macheta s1-04): documentul de schimbare a metodei de evaluare (SM). Modelele, metoda noua, prima zi a lunii de la
-- care se aplica si motivul; la validare metoda modelelor se schimba, iar recalcularea (CostEngine) aplica vechea metoda inainte de data si
-- conversia straturilor la data (valoarea stocului nu se schimba).
CREATE TABLE inv.ValuationMethodChange (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ValuationMethodChange PRIMARY KEY,
    CompanyId     int            NOT NULL CONSTRAINT FK_ValuationMethodChange_Company REFERENCES core.Company (Id),
    Number        nvarchar(40)   NULL,
    NewMethod     tinyint        NOT NULL,
    EffectiveDate date           NOT NULL,
    Reason        nvarchar(1000) NOT NULL,
    Status        tinyint        NOT NULL CONSTRAINT DF_ValuationMethodChange_Status DEFAULT (1),   -- 1 Ciorna, 2 Validat, 4 Anulat
    ValidatedAt   datetime2(3)   NULL,
    ValidatedBy   int            NULL CONSTRAINT FK_ValuationMethodChange_User_ValidatedBy REFERENCES core.[User] (Id),
    CreatedAt     datetime2(3)   NOT NULL CONSTRAINT DF_ValuationMethodChange_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy     int            NOT NULL,
    ModifiedAt    datetime2(3)   NULL,
    ModifiedBy    int            NULL,
    RowVersion    rowversion     NOT NULL,
    CONSTRAINT CK_ValuationMethodChange_Method CHECK (NewMethod BETWEEN 1 AND 4),
    CONSTRAINT CK_ValuationMethodChange_Status CHECK (Status IN (1, 2, 4)),
    CONSTRAINT CK_ValuationMethodChange_Date CHECK (DAY(EffectiveDate) = 1),
    CONSTRAINT CK_ValuationMethodChange_Number CHECK (Status <> 2 OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_ValuationMethodChange_Number ON inv.ValuationMethodChange (CompanyId, Number) WHERE Number IS NOT NULL;
GO

CREATE TABLE inv.ValuationMethodChangeLine (
    Id        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ValuationMethodChangeLine PRIMARY KEY,
    ChangeId  int      NOT NULL CONSTRAINT FK_ValuationMethodChangeLine_Change REFERENCES inv.ValuationMethodChange (Id) ON DELETE CASCADE,
    ItemId    int      NOT NULL CONSTRAINT FK_ValuationMethodChangeLine_Item REFERENCES core.Item (Id),
    OldMethod tinyint  NULL,   -- la validare
    CONSTRAINT UX_ValuationMethodChangeLine_Item UNIQUE (ChangeId, ItemId)
);
CREATE INDEX IX_ValuationMethodChangeLine_ItemId ON inv.ValuationMethodChangeLine (ItemId);
GO

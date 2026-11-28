-- 02.02 Contabilitate, C4.1: registrul de TVA (C4 3.1) si tipurile de operatiuni fiscale (C4 3.2). Randurile se scriu odata cu nota contabila a
-- documentului (decizia 2), cu datele partenerului copiate la data documentului; TVA la incasare: randul facturii e neexigibil (fara perioada),
-- iar fiecare incasare sau plata adauga un rand copil cu partea exigibila, in perioada ei.
CREATE TABLE fin.VatOperationType (
    Code        nvarchar(20)   NOT NULL,
    ValidFrom   date           NOT NULL,
    ValidTo     date           NULL,
    Name        nvarchar(200)  NOT NULL,
    Direction   tinyint        NOT NULL,
    IsDeductible bit           NOT NULL CONSTRAINT DF_VatOperationType_IsDeductible DEFAULT (1),
    CONSTRAINT PK_VatOperationType PRIMARY KEY (Code, ValidFrom),
    CONSTRAINT CK_VatOperationType_Direction CHECK (Direction IN (1, 2))
);

CREATE TABLE fin.VatEntry (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_VatEntry PRIMARY KEY,
    CompanyId           int               NOT NULL CONSTRAINT FK_VatEntry_Company REFERENCES core.Company (Id),
    EntryId             bigint            NOT NULL CONSTRAINT FK_VatEntry_JournalEntry REFERENCES fin.JournalEntry (Id),
    SourceDocumentType  varchar(50)       NOT NULL,
    SourceDocumentId    bigint            NOT NULL,
    DocumentNumber      nvarchar(50)      NOT NULL,
    DocumentDate        date              NOT NULL,
    Direction           tinyint           NOT NULL,
    PartnerId           int               NULL CONSTRAINT FK_VatEntry_Partner REFERENCES core.Partner (Id),
    PartnerFiscalCode   nvarchar(30)      NULL,
    PartnerCountryCode  char(2)           NULL,
    PartnerName         nvarchar(200)     NULL,
    OperationTypeCode   nvarchar(20)      NOT NULL,
    VatRateCode         nvarchar(10)      NOT NULL,
    VatRatePercent      decimal(5,2)      NOT NULL,
    BaseRon             decimal(19,2)     NOT NULL,
    VatRon              decimal(19,2)     NOT NULL,
    ExigibilityDate     date              NULL,
    PeriodYear          smallint          NULL,
    PeriodMonth         tinyint           NULL,
    IsVatOnCollection   bit               NOT NULL CONSTRAINT DF_VatEntry_IsVatOnCollection DEFAULT (0),
    ParentVatEntryId    bigint            NULL CONSTRAINT FK_VatEntry_Parent REFERENCES fin.VatEntry (Id),
    DeductionPercent    decimal(5,2)      NOT NULL CONSTRAINT DF_VatEntry_DeductionPercent DEFAULT (100),
    IsReversal          bit               NOT NULL CONSTRAINT DF_VatEntry_IsReversal DEFAULT (0),
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_VatEntry_CreatedAt DEFAULT (sysutcdatetime()),
    -- Direction: 1 livrare, 2 achizitie. Perioada lipseste doar pe randul neexigibil al unei facturi cu TVA la incasare.
    CONSTRAINT CK_VatEntry_Direction CHECK (Direction IN (1, 2)),
    CONSTRAINT CK_VatEntry_Period CHECK ((PeriodYear IS NULL AND PeriodMonth IS NULL AND ExigibilityDate IS NULL)
        OR (PeriodYear = YEAR(ExigibilityDate) AND PeriodMonth = MONTH(ExigibilityDate)))
);
CREATE INDEX IX_VatEntry_Period ON fin.VatEntry (CompanyId, PeriodYear, PeriodMonth, Direction) INCLUDE (OperationTypeCode, VatRateCode, BaseRon, VatRon);
CREATE INDEX IX_VatEntry_Source ON fin.VatEntry (SourceDocumentType, SourceDocumentId);
CREATE INDEX IX_VatEntry_EntryId ON fin.VatEntry (EntryId);
CREATE INDEX IX_VatEntry_PartnerId ON fin.VatEntry (PartnerId) WHERE PartnerId IS NOT NULL;
CREATE INDEX IX_VatEntry_ParentVatEntryId ON fin.VatEntry (ParentVatEntryId) WHERE ParentVatEntryId IS NOT NULL;

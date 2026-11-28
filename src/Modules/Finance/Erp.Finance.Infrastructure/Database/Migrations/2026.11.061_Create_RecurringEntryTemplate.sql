-- 02.02 Contabilitate, C2.4b: notele recurente (C2 3.8): un sablon de nota manuala cu periodicitate, generat ca ciorna la data scadenta si
-- validat de om. Liniile se tin ca JSON (aceeasi forma ca la ciorna), fiindca se citesc si se copiaza intregi; sumele calculate din sold vin
-- cu balanta (C5a).
CREATE TABLE fin.RecurringEntryTemplate (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecurringEntryTemplate PRIMARY KEY,
    CompanyId    int               NOT NULL CONSTRAINT FK_RecurringEntryTemplate_Company REFERENCES core.Company (Id),
    Name         nvarchar(200)     NOT NULL,
    JournalId    int               NOT NULL CONSTRAINT FK_RecurringEntryTemplate_Journal REFERENCES fin.Journal (Id),
    Periodicity  tinyint           NOT NULL,
    NextDate     date              NOT NULL,
    EndDate      date              NULL,
    Description  nvarchar(400)     NULL,
    Lines        nvarchar(max)     NOT NULL,
    IsActive     bit               NOT NULL CONSTRAINT DF_RecurringEntryTemplate_IsActive DEFAULT (1),
    LastEntryId  bigint            NULL CONSTRAINT FK_RecurringEntryTemplate_LastEntry REFERENCES fin.JournalEntry (Id),
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_RecurringEntryTemplate_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    ModifiedAt   datetime2(3)      NULL,
    ModifiedBy   int               NULL,
    RowVersion   rowversion        NOT NULL,
    -- Periodicity: luni intre doua note (1 lunar, 3 trimestrial, 6 semestrial, 12 anual).
    CONSTRAINT CK_RecurringEntryTemplate_Periodicity CHECK (Periodicity IN (1, 3, 6, 12)),
    CONSTRAINT CK_RecurringEntryTemplate_Lines CHECK (ISJSON(Lines) = 1)
);
CREATE INDEX IX_RecurringEntryTemplate_Company_Active_NextDate ON fin.RecurringEntryTemplate (CompanyId, IsActive, NextDate);
CREATE INDEX IX_RecurringEntryTemplate_JournalId ON fin.RecurringEntryTemplate (JournalId);
CREATE INDEX IX_RecurringEntryTemplate_LastEntryId ON fin.RecurringEntryTemplate (LastEntryId) WHERE LastEntryId IS NOT NULL;

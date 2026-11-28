-- 02.02 Contabilitate, C2.4: contractul de contare din care s-a facut fiecare nota automata (pentru recontare si stornarea la sursa, C2 4.2,
-- 4.6) si istoricul versiunilor unei note recontate (C2 3.1 regula 4). Liniile vechi se pastreaza ca JSON: istoricul se citeste, nu se agrega.
CREATE TABLE fin.JournalEntrySource (
    EntryId    bigint        NOT NULL CONSTRAINT PK_JournalEntrySource PRIMARY KEY CONSTRAINT FK_JournalEntrySource_Entry REFERENCES fin.JournalEntry (Id),
    EventType  varchar(80)   NOT NULL,
    Payload    nvarchar(max) NOT NULL
);

CREATE TABLE fin.JournalEntryHistory (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalEntryHistory PRIMARY KEY,
    EntryId     bigint            NOT NULL CONSTRAINT FK_JournalEntryHistory_Entry REFERENCES fin.JournalEntry (Id),
    Version     int               NOT NULL,
    Reason      nvarchar(300)     NOT NULL,
    TotalDebit  decimal(19,2)     NOT NULL,
    Lines       nvarchar(max)     NOT NULL,
    ChangedAt   datetime2(3)      NOT NULL CONSTRAINT DF_JournalEntryHistory_ChangedAt DEFAULT (sysutcdatetime()),
    ChangedBy   int               NOT NULL,
    CONSTRAINT UQ_JournalEntryHistory_Entry_Version UNIQUE (EntryId, Version)
);

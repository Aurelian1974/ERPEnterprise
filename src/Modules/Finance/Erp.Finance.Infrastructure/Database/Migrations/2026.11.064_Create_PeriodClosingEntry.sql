-- 02.02 Contabilitate, C3.2: notele de inchidere ale unei luni (C3 3.5): regularizarea TVA (Kind 1) si inchiderea veniturilor si cheltuielilor
-- in 121 (Kind 2). O nota pe luna si fel: la reinchidere se rescrie aceeasi nota (acelasi numar, versiunea veche in fin.JournalEntryHistory).
CREATE TABLE fin.PeriodClosingEntry (
    PeriodId   int     NOT NULL CONSTRAINT FK_PeriodClosingEntry_Period REFERENCES fin.AccountingPeriod (Id),
    Kind       tinyint NOT NULL,
    CompanyId  int     NOT NULL CONSTRAINT FK_PeriodClosingEntry_Company REFERENCES core.Company (Id),
    EntryId    bigint  NOT NULL CONSTRAINT FK_PeriodClosingEntry_Entry REFERENCES fin.JournalEntry (Id),
    CONSTRAINT PK_PeriodClosingEntry PRIMARY KEY (PeriodId, Kind),
    CONSTRAINT UQ_PeriodClosingEntry_EntryId UNIQUE (EntryId),
    CONSTRAINT CK_PeriodClosingEntry_Kind CHECK (Kind IN (1, 2))
);
CREATE INDEX IX_PeriodClosingEntry_CompanyId ON fin.PeriodClosingEntry (CompanyId);

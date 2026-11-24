-- 02.02 Contabilitate, C3.3: notele inchiderii anului (C3 3.6): soldurile reportate (Kind 1) si preluarea rezultatului (Kind 2), datate in prima
-- zi a anului urmator, si rezerva legala (Kind 3), datata in ultima zi a anului. O nota pe an si fel: la reinchidere se rescrie aceeasi nota.
-- Abatere de la C3 3.6: soldurile reportate sunt o nota de deschidere (SourceKind 5), nu tabelul AccountOpeningBalance; balanta (C5a) le
-- citeste ca solduri initiale ale anului, iar reinchiderea anului precedent le rescrie.
CREATE TABLE fin.FiscalYearCloseEntry (
    FiscalYearId  int     NOT NULL CONSTRAINT FK_FiscalYearCloseEntry_Year REFERENCES fin.FiscalYear (Id),
    Kind          tinyint NOT NULL,
    CompanyId     int     NOT NULL CONSTRAINT FK_FiscalYearCloseEntry_Company REFERENCES core.Company (Id),
    EntryId       bigint  NOT NULL CONSTRAINT FK_FiscalYearCloseEntry_Entry REFERENCES fin.JournalEntry (Id),
    CONSTRAINT PK_FiscalYearCloseEntry PRIMARY KEY (FiscalYearId, Kind),
    CONSTRAINT UQ_FiscalYearCloseEntry_EntryId UNIQUE (EntryId),
    CONSTRAINT CK_FiscalYearCloseEntry_Kind CHECK (Kind IN (1, 2, 3))
);
CREATE INDEX IX_FiscalYearCloseEntry_CompanyId ON fin.FiscalYearCloseEntry (CompanyId);

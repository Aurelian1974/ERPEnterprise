-- Comparatia decontului de TVA cu decontul precompletat P300 (02.02, C4 3.4, decizia 5): pe randurile formularului, valoarea declarata,
-- cea precompletata, marcajul peste prag si explicatia contabilului (pentru eventuala notificare de conformare).
CREATE TABLE fin.TaxReturnComparison (
    ReturnId           int               NOT NULL CONSTRAINT FK_TaxReturnComparison_Return REFERENCES fin.TaxReturn (Id),
    RowCode            varchar(30)       NOT NULL,
    ColumnCode         varchar(20)       NOT NULL,
    DeclaredValue      decimal(19,2)     NOT NULL,
    PrecompletedValue  decimal(19,2)     NOT NULL,
    IsOverThreshold    bit               NOT NULL,
    Explanation        nvarchar(1000)    NULL,
    ExplainedAt        datetime2(3)      NULL,
    ExplainedBy        int               NULL,
    CONSTRAINT PK_TaxReturnComparison PRIMARY KEY (ReturnId, RowCode, ColumnCode)
);

ALTER TABLE fin.TaxReturn ADD PrecompletedAt datetime2(3) NULL;

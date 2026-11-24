-- Alocarile cu data intr-o luna deja reevaluata (02.02, F4 4.6, F4-A14): diferenta realizata se calculeaza fata de valoarea contabila de
-- dinaintea rularilor validate de la data alocarii incolo, iar partea din reevaluarea lor care revine sumei stinse devine o corectie in
-- asteptare, preluata ca linie a rularii urmatoare (IsCorrection = 1). Desfacerea alocarii scrie corectia inversa.
CREATE TABLE fin.FxRevaluationCorrection (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FxRevaluationCorrection PRIMARY KEY,
    CompanyId       int               NOT NULL CONSTRAINT FK_FxRevaluationCorrection_Company REFERENCES core.Company (Id),
    OpenItemId      bigint            NOT NULL CONSTRAINT FK_FxRevaluationCorrection_OpenItem REFERENCES fin.OpenItem (Id),
    RunId           int               NOT NULL CONSTRAINT FK_FxRevaluationCorrection_Run REFERENCES fin.FxRevaluationRun (Id),
    AllocationId    bigint            NOT NULL CONSTRAINT FK_FxRevaluationCorrection_Allocation REFERENCES fin.Allocation (Id),
    ForeignAmount   decimal(19,2)     NOT NULL,
    DifferenceRon   decimal(19,2)     NOT NULL,
    ReversalOfId    bigint            NULL CONSTRAINT FK_FxRevaluationCorrection_ReversalOf REFERENCES fin.FxRevaluationCorrection (Id),
    AppliedInRunId  int               NULL CONSTRAINT FK_FxRevaluationCorrection_AppliedInRun REFERENCES fin.FxRevaluationRun (Id),
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_FxRevaluationCorrection_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy       int               NOT NULL,
    CONSTRAINT CK_FxRevaluationCorrection_NotZero CHECK (DifferenceRon <> 0)
);
CREATE INDEX IX_FxRevaluationCorrection_Pending ON fin.FxRevaluationCorrection (CompanyId, AppliedInRunId) WHERE AppliedInRunId IS NULL;
CREATE INDEX IX_FxRevaluationCorrection_AllocationId ON fin.FxRevaluationCorrection (AllocationId);

ALTER TABLE fin.FxRevaluationLine ADD
    IsCorrection    bit     NOT NULL CONSTRAINT DF_FxRevaluationLine_IsCorrection DEFAULT (0),
    CorrectionId    bigint  NULL CONSTRAINT FK_FxRevaluationLine_Correction REFERENCES fin.FxRevaluationCorrection (Id),
    CorrectedRunId  int     NULL CONSTRAINT FK_FxRevaluationLine_CorrectedRun REFERENCES fin.FxRevaluationRun (Id);

-- Corectiile calculate de FxCalculator pentru un lot de alocari, pe randul alocarii.
DROP PROCEDURE IF EXISTS fin.usp_Allocation_CreateBatch;
CREATE TYPE fin.FxCorrectionList AS TABLE (
    RowNo          int            NOT NULL,
    OpenItemId     bigint         NOT NULL,
    RunId          int            NOT NULL,
    ForeignAmount  decimal(19,2)  NOT NULL,
    DifferenceRon  decimal(19,2)  NOT NULL,
    PRIMARY KEY (RowNo, OpenItemId, RunId)
);

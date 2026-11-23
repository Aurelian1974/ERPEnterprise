-- Ajustarea diferentelor mici (02.02, F3 3.6, 4.8): documentul de ajustare pe o partida, partida lui de tip 9 si alocarea cu Source = 6.
-- Reason: 1 Rotunjire, 2 Comision retinut de partener, 3 Diferenta acceptata. Amount e pozitiv, in moneda partidei ajustate.
CREATE TABLE fin.WriteOff (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WriteOff PRIMARY KEY,
    CompanyId            int            NOT NULL CONSTRAINT FK_WriteOff_Company REFERENCES core.Company (Id),
    OpenItemId           bigint         NOT NULL CONSTRAINT FK_WriteOff_OpenItem REFERENCES fin.OpenItem (Id),
    WriteOffOpenItemId   bigint         NULL CONSTRAINT FK_WriteOff_OpenItem_WriteOff REFERENCES fin.OpenItem (Id),
    Amount               decimal(19,2)  NOT NULL,
    AmountRon            decimal(19,2)  NOT NULL,
    Reason               tinyint        NOT NULL,
    Note                 nvarchar(500)  NULL,
    DocumentDate         date           NOT NULL,
    AboveThreshold       bit            NOT NULL CONSTRAINT DF_WriteOff_AboveThreshold DEFAULT (0),
    CreatedAt            datetime2(3)   NOT NULL CONSTRAINT DF_WriteOff_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int            NOT NULL,
    CONSTRAINT CK_WriteOff_Amount CHECK (Amount > 0),
    CONSTRAINT CK_WriteOff_Reason CHECK (Reason IN (1, 2, 3))
);
CREATE INDEX IX_WriteOff_CompanyId_DocumentDate ON fin.WriteOff (CompanyId, DocumentDate);
CREATE INDEX IX_WriteOff_OpenItemId ON fin.WriteOff (OpenItemId);

ALTER TABLE fin.Allocation ADD CONSTRAINT FK_Allocation_WriteOff FOREIGN KEY (WriteOffId) REFERENCES fin.WriteOff (Id);

-- Lotul de alocari primeste documentul F3 care le creeaza (ajustare acum; compensare si efect comercial cand apar tabelele lor).
DROP PROCEDURE IF EXISTS fin.usp_Allocation_CreateBatch;
DROP TYPE fin.AllocationList;
CREATE TYPE fin.AllocationList AS TABLE (
    RowNo             int            NOT NULL PRIMARY KEY,
    DebitOpenItemId   bigint         NOT NULL,
    CreditOpenItemId  bigint         NOT NULL,
    Amount            decimal(19,2)  NOT NULL,
    AllocationDate    date           NULL,
    Source            tinyint        NOT NULL,
    CompensationId    int            NULL,
    WriteOffId        int            NULL,
    CommercialPaperId int            NULL
);

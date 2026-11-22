-- Loturile de export ale ordinelor de plata (02.02, F2 3.6): un fisier pentru internet banking cu mai multe OP-uri ale aceluiasi cont.
-- Status: 1 Deschis, 2 Exportat, 3 Anulat. Format: formatul de plati al profilului bancar la export (1 pain.001.001.03, 2 pain.001.001.09,
-- 3 CSV). Fisierul exportat se pastreaza provizoriu aici, cu hash, pana la atasamentele Nucleului (02.01).
CREATE TABLE fin.PaymentBatch (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentBatch PRIMARY KEY,
    CompanyId         int               NOT NULL CONSTRAINT FK_PaymentBatch_Company REFERENCES core.Company (Id),
    TreasuryAccountId int               NOT NULL CONSTRAINT FK_PaymentBatch_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    Number            nvarchar(30)      NULL,
    Format            tinyint           NULL,
    OrderCount        int               NOT NULL CONSTRAINT DF_PaymentBatch_OrderCount DEFAULT (0),
    TotalAmount       decimal(19,2)     NOT NULL CONSTRAINT DF_PaymentBatch_TotalAmount DEFAULT (0),
    Status            tinyint           NOT NULL CONSTRAINT DF_PaymentBatch_Status DEFAULT (1),
    ExportCount       int               NOT NULL CONSTRAINT DF_PaymentBatch_ExportCount DEFAULT (0),
    ExportedAt        datetime2(3)      NULL,
    ExportedBy        int               NULL,
    FileName          nvarchar(260)     NULL,
    FileSha256        char(64)          NULL,
    FileContent       varbinary(max)    NULL,
    LastReExportReason nvarchar(500)    NULL,
    CancelledAt       datetime2(3)      NULL,
    CancelledBy       int               NULL,
    CancelReason      nvarchar(500)     NULL,
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_PaymentBatch_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT CK_PaymentBatch_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_PaymentBatch_Format CHECK (Format IS NULL OR Format BETWEEN 1 AND 3),
    CONSTRAINT CK_PaymentBatch_Exported CHECK (Status <> 2 OR (ExportedAt IS NOT NULL AND FileSha256 IS NOT NULL))
);
GO

CREATE UNIQUE INDEX UX_PaymentBatch_CompanyId_Number ON fin.PaymentBatch (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE INDEX IX_PaymentBatch_TreasuryAccountId_Status ON fin.PaymentBatch (TreasuryAccountId, Status);
GO

ALTER TABLE fin.PaymentOrder ADD CONSTRAINT FK_PaymentOrder_PaymentBatch FOREIGN KEY (PaymentBatchId) REFERENCES fin.PaymentBatch (Id);
CREATE INDEX IX_PaymentOrder_PaymentBatchId ON fin.PaymentOrder (PaymentBatchId) WHERE PaymentBatchId IS NOT NULL;
GO

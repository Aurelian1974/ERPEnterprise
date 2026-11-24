-- Propunerile de plata si aprobarea lor pe praguri (02.02, F5 3.1–3.3, 7). Propunerea: 1 Ciorna, 2 In aprobare, 3 Aprobata, 4 Generata,
-- 5 Respinsa. Linia: 1 Propusa, 2 Respinsa, 3 Generata, 4 Sarita la generare (factura schimbata intre timp). O factura intr-o singura
-- propunere activa: indexul unic filtrat pe linia propusa a unei propuneri deschise (IsActive), SQL-17. Aprobarile sunt doar cu adaugare
-- (trigger, SP-21); SubmissionNo separa aprobarile unei retrimiteri de cele vechi (F5 3.2 regula 3).
ALTER TABLE fin.TreasuryAccount ADD
    OverdraftLimit     decimal(19,2)  NULL,
    IncludeInForecast  bit            NOT NULL CONSTRAINT DF_TreasuryAccount_IncludeInForecast DEFAULT (1);

ALTER TABLE fin.PartnerFinanceProfile ADD
    PaymentPriority             tinyint  NOT NULL CONSTRAINT DF_PartnerFinanceProfile_PaymentPriority DEFAULT (3),
    PreferredTreasuryAccountId  int      NULL CONSTRAINT FK_PartnerFinanceProfile_PreferredTreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    PaymentDayOfWeek            tinyint  NULL;
GO

ALTER TABLE fin.PartnerFinanceProfile ADD CONSTRAINT CK_PartnerFinanceProfile_PaymentPriority CHECK (PaymentPriority BETWEEN 1 AND 4
    AND (PaymentDayOfWeek IS NULL OR PaymentDayOfWeek BETWEEN 1 AND 7));

CREATE TABLE fin.PaymentProposal (
    Id                        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentProposal PRIMARY KEY,
    CompanyId                 int               NOT NULL CONSTRAINT FK_PaymentProposal_Company REFERENCES core.Company (Id),
    Number                    nvarchar(40)      NOT NULL,
    Name                      nvarchar(200)     NULL,
    ProposalDate              date              NOT NULL,
    PlannedPaymentDate        date              NOT NULL,
    DueDateUntil              date              NOT NULL,
    DefaultTreasuryAccountId  int               NOT NULL CONSTRAINT FK_PaymentProposal_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    TotalAmountRon            decimal(19,2)     NOT NULL CONSTRAINT DF_PaymentProposal_TotalAmountRon DEFAULT (0),
    RequiredApprovalLevel     tinyint           NULL,
    CurrentApprovalLevel      tinyint           NOT NULL CONSTRAINT DF_PaymentProposal_CurrentApprovalLevel DEFAULT (0),
    SubmissionNo              int               NOT NULL CONSTRAINT DF_PaymentProposal_SubmissionNo DEFAULT (0),
    Status                    tinyint           NOT NULL CONSTRAINT DF_PaymentProposal_Status DEFAULT (1),
    LastApproverId            int               NULL,
    ReturnReason              nvarchar(500)     NULL,
    SubmittedAt               datetime2(3)      NULL,
    ApprovedAt                datetime2(3)      NULL,
    GeneratedAt               datetime2(3)      NULL,
    GeneratedBy               int               NULL,
    CreatedAt                 datetime2(3)      NOT NULL CONSTRAINT DF_PaymentProposal_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                 int               NOT NULL,
    ModifiedAt                datetime2(3)      NULL,
    ModifiedBy                int               NULL,
    RowVersion                rowversion        NOT NULL,
    CONSTRAINT UQ_PaymentProposal_Number UNIQUE (CompanyId, Number),
    CONSTRAINT CK_PaymentProposal_Status CHECK (Status BETWEEN 1 AND 5),
    CONSTRAINT CK_PaymentProposal_Levels CHECK (CurrentApprovalLevel BETWEEN 0 AND 3 AND (RequiredApprovalLevel IS NULL OR RequiredApprovalLevel BETWEEN 1 AND 3))
);
CREATE INDEX IX_PaymentProposal_CompanyId_Status_PlannedPaymentDate ON fin.PaymentProposal (CompanyId, Status, PlannedPaymentDate);

CREATE TABLE fin.PaymentProposalLine (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentProposalLine PRIMARY KEY,
    PaymentProposalId  int               NOT NULL CONSTRAINT FK_PaymentProposalLine_Proposal REFERENCES fin.PaymentProposal (Id),
    LineNumber         int               NOT NULL,
    OpenItemId         bigint            NOT NULL CONSTRAINT FK_PaymentProposalLine_OpenItem REFERENCES fin.OpenItem (Id),
    PartnerId          int               NOT NULL CONSTRAINT FK_PaymentProposalLine_Partner REFERENCES core.Partner (Id),
    DueDate            date              NOT NULL,
    CurrencyCode       char(3)           NOT NULL,
    OpenAmount         decimal(19,2)     NOT NULL,
    ProposedAmount     decimal(19,2)     NOT NULL,
    ProposedAmountRon  decimal(19,2)     NOT NULL,
    TreasuryAccountId  int               NOT NULL CONSTRAINT FK_PaymentProposalLine_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    Priority           tinyint           NOT NULL,
    Warnings           nvarchar(max)     NULL,
    LineStatus         tinyint           NOT NULL CONSTRAINT DF_PaymentProposalLine_LineStatus DEFAULT (1),
    RejectReason       nvarchar(500)     NULL,
    PaymentOrderId     int               NULL CONSTRAINT FK_PaymentProposalLine_PaymentOrder REFERENCES fin.PaymentOrder (Id),
    ProposalIsOpen     bit               NOT NULL CONSTRAINT DF_PaymentProposalLine_ProposalIsOpen DEFAULT (1),
    IsActive           AS (CAST(CASE WHEN LineStatus = 1 AND ProposalIsOpen = 1 THEN 1 ELSE 0 END AS bit)) PERSISTED,
    CONSTRAINT UQ_PaymentProposalLine_LineNumber UNIQUE (PaymentProposalId, LineNumber),
    CONSTRAINT CK_PaymentProposalLine_LineStatus CHECK (LineStatus BETWEEN 1 AND 4),
    CONSTRAINT CK_PaymentProposalLine_Amount CHECK (ProposedAmount > 0 AND ProposedAmount <= OpenAmount),
    CONSTRAINT CK_PaymentProposalLine_WarningsJson CHECK (Warnings IS NULL OR ISJSON(Warnings) = 1)
);
CREATE UNIQUE INDEX UX_PaymentProposalLine_OpenItemId_Active ON fin.PaymentProposalLine (OpenItemId) WHERE LineStatus = 1 AND ProposalIsOpen = 1;
CREATE INDEX IX_PaymentProposalLine_PaymentProposalId ON fin.PaymentProposalLine (PaymentProposalId);

CREATE TABLE fin.PaymentApprovalRule (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentApprovalRule PRIMARY KEY,
    CompanyId       int               NOT NULL CONSTRAINT FK_PaymentApprovalRule_Company REFERENCES core.Company (Id),
    Level           tinyint           NOT NULL,
    ThresholdRon    decimal(19,2)     NOT NULL,
    Basis           tinyint           NOT NULL CONSTRAINT DF_PaymentApprovalRule_Basis DEFAULT (1),
    ApproverRoleId  int               NOT NULL CONSTRAINT FK_PaymentApprovalRule_Role REFERENCES core.Role (Id),
    MinApprovers    tinyint           NOT NULL CONSTRAINT DF_PaymentApprovalRule_MinApprovers DEFAULT (1),
    IsActive        bit               NOT NULL CONSTRAINT DF_PaymentApprovalRule_IsActive DEFAULT (1),
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_PaymentApprovalRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy       int               NOT NULL,
    ModifiedAt      datetime2(3)      NULL,
    ModifiedBy      int               NULL,
    RowVersion      rowversion        NOT NULL,
    CONSTRAINT CK_PaymentApprovalRule_Level CHECK (Level BETWEEN 1 AND 3),
    CONSTRAINT CK_PaymentApprovalRule_Basis CHECK (Basis IN (1, 2)),
    CONSTRAINT CK_PaymentApprovalRule_Threshold CHECK (ThresholdRon >= 0 AND MinApprovers = 1)
);
CREATE UNIQUE INDEX UX_PaymentApprovalRule_CompanyId_Level_Active ON fin.PaymentApprovalRule (CompanyId, Level) WHERE IsActive = 1;

CREATE TABLE fin.PaymentApproval (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentApproval PRIMARY KEY,
    PaymentProposalId  int               NOT NULL CONSTRAINT FK_PaymentApproval_Proposal REFERENCES fin.PaymentProposal (Id),
    SubmissionNo       int               NOT NULL,
    Level              tinyint           NOT NULL,
    UserId             int               NOT NULL CONSTRAINT FK_PaymentApproval_User REFERENCES core.[User] (Id),
    Decision           tinyint           NOT NULL,
    DecidedAt          datetime2(3)      NOT NULL CONSTRAINT DF_PaymentApproval_DecidedAt DEFAULT (sysutcdatetime()),
    Comment            nvarchar(500)     NULL,
    RejectedLineIds    nvarchar(max)     NULL,
    CONSTRAINT CK_PaymentApproval_Decision CHECK (Decision IN (1, 2, 3)),
    CONSTRAINT CK_PaymentApproval_RejectedLineIdsJson CHECK (RejectedLineIds IS NULL OR ISJSON(RejectedLineIds) = 1)
);
CREATE INDEX IX_PaymentApproval_PaymentProposalId_Level ON fin.PaymentApproval (PaymentProposalId, Level);
GO

CREATE TRIGGER fin.trg_PaymentApproval_AppendOnly ON fin.PaymentApproval AFTER UPDATE, DELETE
AS
BEGIN
    -- Pista aprobarilor e doar cu adaugare (F5 3.2, SP-21).
    SET NOCOUNT ON;
    THROW 51702, N'Deciziile de aprobare nu se modifică și nu se șterg.', 1;
END;
GO

CREATE TYPE fin.PaymentProposalLineList AS TABLE (
    LineNumber         int            NOT NULL PRIMARY KEY,
    OpenItemId         bigint         NOT NULL,
    PartnerId          int            NOT NULL,
    DueDate            date           NOT NULL,
    CurrencyCode       char(3)        NOT NULL,
    OpenAmount         decimal(19,2)  NOT NULL,
    ProposedAmount     decimal(19,2)  NOT NULL,
    ProposedAmountRon  decimal(19,2)  NOT NULL,
    TreasuryAccountId  int            NOT NULL,
    Priority           tinyint        NOT NULL,
    Warnings           nvarchar(max)  NULL,
    LineStatus         tinyint        NOT NULL,
    RejectReason       nvarchar(500)  NULL
);

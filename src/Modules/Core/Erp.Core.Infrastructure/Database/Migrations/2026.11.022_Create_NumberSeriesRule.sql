-- Serii alternative si reguli de alegere (02.01, 3.6 punctul 16; decizia lui Aurelian din 21.11.2026). Un tip de document poate avea
-- mai multe serii deschise pe firma si an; utilizatorul alege una pe document sau seria vine din reguli. O regula se potriveste cand
-- toate conditiile ei completate se potrivesc (utilizatorul, punctul de lucru, locatia, tipul partenerului: 1 Romania, 2 UE, 3 non-UE)
-- si trimite la seria cu prefixul dat din anul documentului; cea cu Priority mai mic castiga. Fara regula, seria implicita (4.2).
CREATE TABLE core.NumberSeriesRule (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NumberSeriesRule PRIMARY KEY,
    CompanyId      int               NOT NULL CONSTRAINT FK_NumberSeriesRule_Company REFERENCES core.Company (Id),
    DocumentTypeId int               NOT NULL CONSTRAINT FK_NumberSeriesRule_DocumentType REFERENCES core.DocumentType (Id),
    Priority       int               NOT NULL,
    UserId         int               NULL CONSTRAINT FK_NumberSeriesRule_User REFERENCES core.[User] (Id),
    BranchId       int               NULL,
    LocationId     int               NULL,
    PartnerScope   tinyint           NULL CONSTRAINT CK_NumberSeriesRule_PartnerScope CHECK (PartnerScope IN (1, 2, 3)),
    Prefix         nvarchar(20)      NOT NULL,
    PrefixKey      AS UPPER(Prefix) PERSISTED,
    IsActive       bit               NOT NULL CONSTRAINT DF_NumberSeriesRule_IsActive DEFAULT (1),
    CreatedAt      datetime2(3)      NOT NULL CONSTRAINT DF_NumberSeriesRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int               NOT NULL,
    ModifiedAt     datetime2(3)      NULL,
    ModifiedBy     int               NULL,
    RowVersion     rowversion        NOT NULL,
    CONSTRAINT FK_NumberSeriesRule_Branch FOREIGN KEY (BranchId, CompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT FK_NumberSeriesRule_Location FOREIGN KEY (LocationId, CompanyId) REFERENCES core.Location (Id, CompanyId)
);
CREATE INDEX IX_NumberSeriesRule_Company ON core.NumberSeriesRule (CompanyId, DocumentTypeId, Priority);
GO

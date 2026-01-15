PRINT '=== Începere migrare 043_Document ===';

IF OBJECT_ID('dbo.Document', 'U') IS NOT NULL
    DROP TABLE dbo.Document;
GO

CREATE TABLE dbo.Document (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    DocumentDate DATE NOT NULL,
    DueDate DATE NULL,
    DocumentNumber NVARCHAR(50) NOT NULL,
    DocumentTypeCode NVARCHAR(10) NOT NULL,
    DocumentStateId UNIQUEIDENTIFIER NOT NULL,
    Observations NVARCHAR(500) NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    EntityIntroduced UNIQUEIDENTIFIER NULL, -- Entity where document was introduced
    IntroductionDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2 NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,
    OwnerCompanyId UNIQUEIDENTIFIER NULL,
    OwnerWorkPlaceId UNIQUEIDENTIFIER NULL,
    OwnerLocationId UNIQUEIDENTIFIER NULL
);

-- Add unique constraint on DocumentNumber (within same type and company)
ALTER TABLE dbo.Document
ADD CONSTRAINT UQ_Document_Number_Type_Company UNIQUE (DocumentNumber, DocumentTypeCode, OwnerCompanyId);

-- Add foreign key constraints
ALTER TABLE dbo.Document
ADD CONSTRAINT FK_Document_DocumentState FOREIGN KEY (DocumentStateId) REFERENCES dbo.DocumentState(Id);

ALTER TABLE dbo.Document
ADD CONSTRAINT FK_Document_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.Document
ADD CONSTRAINT FK_Document_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(Id);

ALTER TABLE dbo.Document
ADD CONSTRAINT FK_Document_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users(Id);

-- Add indexes for performance
CREATE INDEX IX_Document_DocumentStateId ON dbo.Document(DocumentStateId);
CREATE INDEX IX_Document_UserId ON dbo.Document(UserId);
CREATE INDEX IX_Document_DocumentTypeCode ON dbo.Document(DocumentTypeCode);
CREATE INDEX IX_Document_DocumentDate ON dbo.Document(DocumentDate);
CREATE INDEX IX_Document_IsActive ON dbo.Document(IsActive);
CREATE INDEX IX_Document_OwnerCompanyId ON dbo.Document(OwnerCompanyId);

-- Add check constraints
ALTER TABLE dbo.Document
ADD CONSTRAINT CK_Document_DueDate_After_DocumentDate CHECK (DueDate IS NULL OR DueDate >= DocumentDate);

PRINT '✅ Tabela Document creată';

PRINT '=== Migrare 043_Document completă ===';
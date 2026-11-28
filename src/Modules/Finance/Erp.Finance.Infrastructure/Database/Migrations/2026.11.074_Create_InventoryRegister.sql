-- Registrul inventar anual (02.02, C5 3.4; Legea 82/1991 art. 20, OMFP 2861/2009): pe firma si an, elementele fiecarui cont de bilant la
-- sfarsitul anului (pe partener), cu valoarea contabila, valoarea de inventar si diferenta; PDF-ul generat si daca e final (an inchis).
CREATE TABLE fin.InventoryRegister (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryRegister PRIMARY KEY,
    CompanyId    int               NOT NULL CONSTRAINT FK_InventoryRegister_Company REFERENCES core.Company (Id),
    Year         smallint          NOT NULL,
    IsFinal      bit               NOT NULL,
    GeneratedAt  datetime2(3)      NOT NULL,
    GeneratedBy  int               NOT NULL,
    Pdf          varbinary(max)    NOT NULL,
    RowVersion   rowversion        NOT NULL
);

CREATE UNIQUE INDEX UX_InventoryRegister_Company_Year ON fin.InventoryRegister (CompanyId, Year);

CREATE TABLE fin.InventoryRegisterLine (
    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryRegisterLine PRIMARY KEY,
    RegisterId      int               NOT NULL CONSTRAINT FK_InventoryRegisterLine_Register REFERENCES fin.InventoryRegister (Id),
    AccountCode     nvarchar(20)      NOT NULL,
    AccountName     nvarchar(200)     NOT NULL,
    PartnerId       int               NULL,
    PartnerName     nvarchar(200)     NULL,
    Side            tinyint           NOT NULL,
    BookValue       decimal(19,2)     NOT NULL,
    InventoryValue  decimal(19,2)     NOT NULL,
    Difference      decimal(19,2)     NOT NULL
);

CREATE INDEX IX_InventoryRegisterLine_Register ON fin.InventoryRegisterLine (RegisterId);

-- Stocuri S4.2 (02.05, S4 3.2, 3.5): durata minima ramasa ceruta de un client (pe model sau pe grupa de articole) si motivul alegerii altui lot
-- decat primul propus FEFO, scris pe miscare (tipul tabela al miscarilor costate primeste coloana; procedura se recreeaza din programabilitate).

CREATE TABLE inv.PartnerShelfLifeRule (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerShelfLifeRule PRIMARY KEY,
    CompanyId        int            NOT NULL CONSTRAINT FK_PartnerShelfLifeRule_Company REFERENCES core.Company (Id),
    PartnerId        int            NOT NULL CONSTRAINT FK_PartnerShelfLifeRule_Partner REFERENCES core.Partner (Id),
    ItemId           int            NULL CONSTRAINT FK_PartnerShelfLifeRule_Item REFERENCES core.Item (Id),
    ItemGroupId      int            NULL CONSTRAINT FK_PartnerShelfLifeRule_ItemGroup REFERENCES core.ItemGroup (Id),
    ItemKey          AS ISNULL(ItemId, 0) PERSISTED,
    ItemGroupKey     AS ISNULL(ItemGroupId, 0) PERSISTED,
    MinRemainingDays int            NOT NULL,
    CreatedAt        datetime2(3)   NOT NULL CONSTRAINT DF_PartnerShelfLifeRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int            NOT NULL,
    ModifiedAt       datetime2(3)   NULL,
    ModifiedBy       int            NULL,
    RowVersion       rowversion     NOT NULL,
    CONSTRAINT UX_PartnerShelfLifeRule UNIQUE (CompanyId, PartnerId, ItemKey, ItemGroupKey),
    CONSTRAINT CK_PartnerShelfLifeRule_Target CHECK (ItemId IS NULL OR ItemGroupId IS NULL),
    CONSTRAINT CK_PartnerShelfLifeRule_Days CHECK (MinRemainingDays >= 0)
);
CREATE INDEX IX_PartnerShelfLifeRule_Partner ON inv.PartnerShelfLifeRule (PartnerId);
GO

DROP PROCEDURE IF EXISTS inv.usp_StockMovement_Post;
DROP TYPE inv.tt_StockMovementCosted;
GO
CREATE TYPE inv.tt_StockMovementCosted AS TABLE (
    RowNo             int            NOT NULL PRIMARY KEY,
    SourceLineId      bigint         NOT NULL,
    SourceRole        tinyint        NOT NULL,
    MovementTypeCode  varchar(30)    NOT NULL,
    DepotId           int            NULL,
    WarehouseId       int            NOT NULL,
    ItemVariantId     int            NOT NULL,
    QtyBase           decimal(19,6)  NOT NULL,
    UnitCost          decimal(19,6)  NOT NULL,
    CostAmount        decimal(19,2)  NOT NULL,
    LotId             int            NULL,
    SerialId          int            NULL,
    CostCenterId      int            NULL,
    ProjectId         int            NULL,
    ReasonId          int            NULL,
    CounterRowNo      int            NULL,
    ReversalOfId      bigint         NULL,
    LotOverrideReason tinyint        NULL
);
GO

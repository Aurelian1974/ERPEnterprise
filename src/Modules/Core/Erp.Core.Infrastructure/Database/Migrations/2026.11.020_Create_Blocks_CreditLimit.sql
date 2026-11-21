-- Blocarea partiala a partenerilor si articolelor si limita de credit (02.01, 3.6 punctul 6; decizia lui Aurelian din 21.11.2026).
-- Stau in tabele separate, ca blocarea sa nu schimbe RowVersion-ul fisei deschise de altcineva. BlockedFor e o masca de biti:
-- partener 1 vanzare, 2 achizitie, 4 plati (7 = tot); articol 1 vanzare, 2 achizitie. 0 = deblocat (randul ramane, istoricul e in
-- audit). Limita de credit e pe firma, pentru client: OverLimitAction 1 = avertizare, 2 = blocare la depasire.
CREATE TABLE core.PartnerBlock (
    PartnerId  int           NOT NULL CONSTRAINT PK_PartnerBlock PRIMARY KEY CONSTRAINT FK_PartnerBlock_Partner REFERENCES core.Partner (Id),
    BlockedFor tinyint       NOT NULL CONSTRAINT CK_PartnerBlock_BlockedFor CHECK (BlockedFor BETWEEN 0 AND 7),
    Reason     nvarchar(300) NULL,
    BlockedAt  datetime2(3)  NULL,
    ModifiedAt datetime2(3)  NOT NULL,
    ModifiedBy int           NOT NULL CONSTRAINT FK_PartnerBlock_User REFERENCES core.[User] (Id),
    RowVersion rowversion    NOT NULL,
    CONSTRAINT CK_PartnerBlock_Reason CHECK ((BlockedFor = 0 AND Reason IS NULL AND BlockedAt IS NULL)
                                          OR (BlockedFor > 0 AND Reason IS NOT NULL AND BlockedAt IS NOT NULL))
);
GO

CREATE TABLE core.ItemBlock (
    ItemId     int           NOT NULL CONSTRAINT PK_ItemBlock PRIMARY KEY CONSTRAINT FK_ItemBlock_Item REFERENCES core.Item (Id),
    BlockedFor tinyint       NOT NULL CONSTRAINT CK_ItemBlock_BlockedFor CHECK (BlockedFor BETWEEN 0 AND 3),
    Reason     nvarchar(300) NULL,
    BlockedAt  datetime2(3)  NULL,
    ModifiedAt datetime2(3)  NOT NULL,
    ModifiedBy int           NOT NULL CONSTRAINT FK_ItemBlock_User REFERENCES core.[User] (Id),
    RowVersion rowversion    NOT NULL,
    CONSTRAINT CK_ItemBlock_Reason CHECK ((BlockedFor = 0 AND Reason IS NULL AND BlockedAt IS NULL)
                                       OR (BlockedFor > 0 AND Reason IS NOT NULL AND BlockedAt IS NOT NULL))
);
GO

CREATE TABLE core.PartnerCredit (
    PartnerId       int            NOT NULL CONSTRAINT FK_PartnerCredit_Partner REFERENCES core.Partner (Id),
    CompanyId       int            NOT NULL CONSTRAINT FK_PartnerCredit_Company REFERENCES core.Company (Id),
    CreditLimit     decimal(19,2)  NULL CONSTRAINT CK_PartnerCredit_Limit CHECK (CreditLimit >= 0),
    OverLimitAction tinyint        NOT NULL CONSTRAINT CK_PartnerCredit_Action CHECK (OverLimitAction IN (1, 2)),
    ModifiedAt      datetime2(3)   NOT NULL,
    ModifiedBy      int            NOT NULL CONSTRAINT FK_PartnerCredit_User REFERENCES core.[User] (Id),
    RowVersion      rowversion     NOT NULL,
    CONSTRAINT PK_PartnerCredit PRIMARY KEY (PartnerId, CompanyId)
);
GO

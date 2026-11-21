-- Detectarea si unirea dublurilor (02.01, 3.6 punctul 7; decizia lui Aurelian din 21.11.2026). Inregistrarea unita in alta nu se
-- sterge (documentele modulelor o pot referi): se dezactiveaza si pastreaza MergedIntoId; copiii ei se muta pe inregistrarea pastrata,
-- iar modulele primesc evenimentul PartenerUnificat / ArticolUnificat. Perechile marcate „nu sunt dubluri” nu se mai propun.
ALTER TABLE core.Partner ADD MergedIntoId int NULL CONSTRAINT FK_Partner_MergedInto REFERENCES core.Partner (Id);
ALTER TABLE core.Item ADD MergedIntoId int NULL CONSTRAINT FK_Item_MergedInto REFERENCES core.Item (Id);
GO

CREATE TABLE core.DuplicateIgnore (
    EntityType tinyint      NOT NULL CONSTRAINT CK_DuplicateIgnore_EntityType CHECK (EntityType IN (1, 2)),
    FirstId    int          NOT NULL,
    SecondId   int          NOT NULL,
    CreatedAt  datetime2(3) NOT NULL CONSTRAINT DF_DuplicateIgnore_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int          NOT NULL CONSTRAINT FK_DuplicateIgnore_User REFERENCES core.[User] (Id),
    CONSTRAINT PK_DuplicateIgnore PRIMARY KEY (EntityType, FirstId, SecondId),
    CONSTRAINT CK_DuplicateIgnore_Order CHECK (FirstId < SecondId)
);
GO

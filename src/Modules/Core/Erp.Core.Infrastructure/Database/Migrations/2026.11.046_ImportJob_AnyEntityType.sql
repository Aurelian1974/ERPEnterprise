-- Joburile de import servesc si importurile altor module (02.01, 4.8; soldurile initiale din 02.02, F3 4.9, „FinOpeningItem”): tipul entitatii
-- nu mai e limitat la Partner si Item, doar nevid.
ALTER TABLE core.ImportJob DROP CONSTRAINT CK_ImportJob_EntityType;
ALTER TABLE core.ImportJob ADD CONSTRAINT CK_ImportJob_EntityType CHECK (LEN(EntityType) > 0);

CREATE OR ALTER PROCEDURE core.usp_Partner_Merge
    @KeepId   int,
    @RemoveId int,
    @UserId   int
AS
BEGIN
    -- Unirea a doi parteneri (02.01, 3.6 punctul 7), in tranzactia comenzii: adresele, conturile (fara IBAN-urile pe care pastratul le
    -- are deja), contactele, proprietatile (doar cele pe care pastratul nu le are), codurile articolelor la partener (fara cele care
    -- s-ar dubla), utilizatorii externi legati si CUI-ul (daca pastratul nu are) trec pe partenerul pastrat. Implicitele adreselor si
    -- conturilor celui unit se scot, ca sa ramana cele ale pastratului. Blocarea, limita de credit si dimensiunile implicite raman
    -- ale pastratului. Partenerul unit se dezactiveaza cu MergedIntoId. Modulele isi muta documentele la evenimentul PartenerUnificat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @KeepId = @RemoveId
        THROW 50021, N'Alegeți doi parteneri diferiți.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Partner WITH (UPDLOCK, HOLDLOCK) WHERE Id = @KeepId AND IsActive = 1 AND MergedIntoId IS NULL)
       OR NOT EXISTS (SELECT 1 FROM core.Partner WITH (UPDLOCK, HOLDLOCK) WHERE Id = @RemoveId AND MergedIntoId IS NULL)
        THROW 50021, N'Unul dintre parteneri nu există, e inactiv sau a fost deja unit cu altul.', 1;

    UPDATE core.PartnerAddress SET PartnerId = @KeepId, IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE PartnerId = @RemoveId;

    UPDATE b SET PartnerId = @KeepId, IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM core.PartnerBankAccount AS b
    WHERE b.PartnerId = @RemoveId AND NOT EXISTS (SELECT 1 FROM core.PartnerBankAccount AS k WHERE k.PartnerId = @KeepId AND k.Iban = b.Iban);

    UPDATE core.PartnerContact SET PartnerId = @KeepId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE PartnerId = @RemoveId;

    UPDATE v SET EntityId = @KeepId
    FROM core.PropertyValue AS v
    JOIN core.PropertyDefinition AS d ON d.Id = v.PropertyDefinitionId AND d.EntityType = 'Partner'
    WHERE v.EntityId = @RemoveId
      AND NOT EXISTS (SELECT 1 FROM core.PropertyValue AS k WHERE k.EntityId = @KeepId AND k.PropertyDefinitionId = v.PropertyDefinitionId);

    UPDATE r SET PartnerId = @KeepId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM core.ItemPartnerReference AS r
    WHERE r.PartnerId = @RemoveId AND NOT EXISTS (SELECT 1 FROM core.ItemPartnerReference AS k
        WHERE k.PartnerId = @KeepId AND k.ReferenceType = r.ReferenceType AND k.CodeKey = r.CodeKey);
    UPDATE core.ItemPartnerReference SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE PartnerId = @RemoveId;

    UPDATE core.[User] SET ExternalPartnerId = @KeepId WHERE ExternalPartnerId = @RemoveId;

    DECLARE @MovedFiscalCode nvarchar(30) = (SELECT FiscalCode FROM core.Partner WHERE Id = @RemoveId);
    IF @MovedFiscalCode IS NOT NULL AND EXISTS (SELECT 1 FROM core.Partner WHERE Id = @KeepId AND FiscalCode IS NULL)
    BEGIN
        UPDATE core.Partner SET FiscalCode = NULL WHERE Id = @RemoveId;
        UPDATE core.Partner SET FiscalCode = @MovedFiscalCode, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @KeepId;
    END;

    UPDATE core.Partner SET IsActive = 0, MergedIntoId = @KeepId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @RemoveId;
    UPDATE core.Partner SET ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @KeepId;

    SELECT Id, RowVersion FROM core.Partner WHERE Id = @KeepId;
END;

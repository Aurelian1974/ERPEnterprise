CREATE OR ALTER PROCEDURE fin.usp_OpeningBalance_Import
    @CompanyId int,
    @Rows      fin.OpeningItemList READONLY,
    @UserId    int
AS
BEGIN
    -- Un lot din importul soldurilor initiale (F3 4.9; 02.01, 4.8 pasul 4), in tranzactia apelantului, pe seturi: partidele noi (cele deja
    -- importate se sar, deci reluarea nu dubleaza nimic, F3-A13) si profilul financiar al partenerilor fara profil. Restul de plata e valoarea
    -- importata (restul de la data pornirii). Intoarce cate partide s-au creat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Created TABLE (Id bigint);
    INSERT INTO fin.OpenItem (CompanyId, PartnerId, Role, Kind, SourceModule, SourceDocumentType, SourceDocumentId, SourceLineKey, DocumentNo,
        DocumentDate, AccountingDate, DueDate, CurrencyCode, ExchangeRate, Amount, AmountRon, OpenAmount, VatAmount, VatAmountRon, IsVatOnCollection,
        GlAccountCode, EInvoiceStatus, CreatedBy)
    OUTPUT inserted.Id INTO @Created (Id)
    SELECT @CompanyId, r.PartnerId, r.Role, r.Kind, 'Finance', 'Finance.OpeningBalance', r.PartnerId, r.LineKey, r.DocumentNo,
        r.DocumentDate, r.DocumentDate, r.DueDate, r.CurrencyCode, r.ExchangeRate, r.Amount, r.AmountRon, r.Amount, 0, r.VatAmountRon, r.IsVatOnCollection,
        r.GlAccountCode, 0, @UserId
    FROM @Rows AS r
    WHERE NOT EXISTS (SELECT 1 FROM fin.OpenItem AS o WITH (UPDLOCK, HOLDLOCK)
                      WHERE o.SourceDocumentType = 'Finance.OpeningBalance' AND o.SourceDocumentId = r.PartnerId AND o.SourceLineKey = r.LineKey
                        AND o.Role = r.Role);

    INSERT INTO fin.PartnerFinanceProfile (CompanyId, PartnerId, Role, CreatedBy)
    SELECT DISTINCT @CompanyId, r.PartnerId, r.Role, @UserId
    FROM @Rows AS r
    WHERE NOT EXISTS (SELECT 1 FROM fin.PartnerFinanceProfile AS p WITH (UPDLOCK, HOLDLOCK)
                      WHERE p.CompanyId = @CompanyId AND p.PartnerId = r.PartnerId AND p.Role = r.Role);

    SELECT COUNT(*) FROM @Created;
END;

CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_Lookups
    @CompanyId int
AS
BEGIN
    -- Listele fisei contului de trezorerie (macheta f1-01), din Nucleu: punctele de lucru active ale firmei, conturile bancare active,
    -- valutele active, utilizatorii activi (fara cei de sistem) si seriile deschise ale documentelor de casa.
    SET NOCOUNT ON;

    SELECT b.Id, b.Code, b.Name
    FROM core.Branch AS b
    WHERE b.CompanyId = @CompanyId AND b.IsActive = 1
    ORDER BY b.Name;

    SELECT ba.Id, ba.Iban, ba.BankName, ba.Currency AS CurrencyCode
    FROM core.CompanyBankAccount AS ba
    WHERE ba.CompanyId = @CompanyId AND ba.IsActive = 1
    ORDER BY ba.BankName, ba.Iban;

    SELECT c.Code
    FROM core.Currency AS c
    WHERE c.IsActive = 1 OR c.Code = 'RON'
    ORDER BY CASE WHEN c.Code = 'RON' THEN 0 ELSE 1 END, c.Code;

    SELECT u.Id, u.UserName AS Code, u.DisplayName AS Name
    FROM core.[User] AS u
    WHERE u.IsActive = 1 AND u.IsSystem = 0
    ORDER BY u.DisplayName;

    SELECT s.Id, t.Code AS DocumentType, s.Prefix, s.[Year], s.BranchId
    FROM core.NumberSeries AS s
    JOIN core.DocumentType AS t ON t.Id = s.DocumentTypeId
    WHERE s.CompanyId = @CompanyId AND s.ClosedAt IS NULL
      AND t.Code IN (N'Finance.Receipt', N'Finance.CashIn', N'Finance.Payment')
    ORDER BY t.Code, s.[Year] DESC, s.Prefix;
END;

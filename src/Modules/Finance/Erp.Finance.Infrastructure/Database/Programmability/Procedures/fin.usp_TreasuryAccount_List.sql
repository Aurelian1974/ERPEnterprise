CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_List
    @CompanyId       int,
    @Search          nvarchar(100) = NULL,
    @IncludeInactive bit = 1
AS
BEGIN
    -- Lista ecranului Conturi de trezorerie (02.02, F1 11; macheta f1-01), pe firma, ordonata pe tip si cod. Cautarea e pe prefixul
    -- codului, pe denumire sau pe IBAN (sirul vine cu caracterele LIKE deja protejate).
    SET NOCOUNT ON;

    SELECT a.Id, a.Kind, a.Code, a.Name, a.BranchId, b.Name AS BranchName, a.CurrencyCode, a.GlAccountCode, a.CompanyBankAccountId, ba.BankName, ba.Iban, a.IsActive,
           (SELECT COUNT(*) FROM fin.TreasuryAccountUser AS u WHERE u.TreasuryAccountId = a.Id) AS CashierCount
    FROM fin.TreasuryAccount AS a
    LEFT JOIN core.Branch AS b ON b.Id = a.BranchId
    LEFT JOIN core.CompanyBankAccount AS ba ON ba.Id = a.CompanyBankAccountId
    WHERE a.CompanyId = @CompanyId
      AND (@IncludeInactive = 1 OR a.IsActive = 1)
      AND (@Search IS NULL
           OR a.CodeKey LIKE UPPER(@Search) + N'%'
           OR a.Name LIKE N'%' + @Search + N'%'
           OR ba.Iban LIKE N'%' + UPPER(REPLACE(@Search, N' ', N'')) + N'%')
    ORDER BY a.Kind, a.CodeKey;
END;

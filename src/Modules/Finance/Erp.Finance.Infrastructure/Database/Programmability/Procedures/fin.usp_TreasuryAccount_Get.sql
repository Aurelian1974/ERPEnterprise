CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Fisa unui cont de trezorerie al firmei (macheta f1-01) si casierii lui. Un cont al altei firme nu se intoarce.
    SET NOCOUNT ON;

    SELECT a.Id, a.CompanyId, a.Kind, a.Code, a.Name, a.BranchId, a.CurrencyCode, a.CompanyBankAccountId, a.GlAccountCode, a.ResponsibleUserId,
           a.ReceiptSeriesId, a.CashInSeriesId, a.CashOutSeriesId, a.MaxClosingBalance, a.CloseMode, a.OpeningDate, a.IsActive,
           CAST(0 AS bit) AS HasDocuments, a.CreatedAt, a.ModifiedAt, m.DisplayName AS ModifiedByName, a.RowVersion
    FROM fin.TreasuryAccount AS a
    LEFT JOIN core.[User] AS m ON m.Id = ISNULL(a.ModifiedBy, a.CreatedBy)
    WHERE a.Id = @Id AND a.CompanyId = @CompanyId;

    SELECT u.UserId, us.DisplayName, u.CanValidate, u.ValidFrom, u.ValidTo
    FROM fin.TreasuryAccountUser AS u
    JOIN fin.TreasuryAccount AS a ON a.Id = u.TreasuryAccountId
    JOIN core.[User] AS us ON us.Id = u.UserId
    WHERE u.TreasuryAccountId = @Id AND a.CompanyId = @CompanyId
    ORDER BY us.DisplayName;
END;

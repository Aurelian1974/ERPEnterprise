CREATE OR ALTER PROCEDURE fin.usp_GlAccount_FindByRole
    @CompanyId    int,
    @Role         varchar(40),
    @PartnerKind  tinyint = NULL,
    @CurrencyCode char(3) = NULL,
    @OnDate       date
AS
BEGIN
    -- Contul pe rol la o data (C1 3.2, IChartOfAccountsQueries): atribuirea firmei inaintea celei a instalarii, conditia exacta pe tipul
    -- partenerului si pe moneda inaintea celei generale, cea mai recenta in vigoare; contul trebuie sa poata primi note.
    SET NOCOUNT ON;

    SELECT TOP (1) a.Id, a.Code, a.Name, a.AccountType, a.IsPostable, a.CurrencyMode, a.CurrencyCode, a.PartyRequirement, a.CompanyId
    FROM fin.GlAccountRoleAssignment AS r
    JOIN fin.GlAccount AS a ON a.Id = r.AccountId
    WHERE r.Role = @Role AND (r.CompanyId IS NULL OR r.CompanyId = @CompanyId)
      AND (r.PartnerKind IS NULL OR r.PartnerKind = @PartnerKind) AND (r.CurrencyCode IS NULL OR r.CurrencyCode = @CurrencyCode)
      AND r.ValidFrom <= @OnDate AND (r.ValidTo IS NULL OR r.ValidTo >= @OnDate)
      AND a.IsPostable = 1 AND a.IsActive = 1 AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId)
    ORDER BY CASE WHEN r.CompanyId IS NULL THEN 1 ELSE 0 END, CASE WHEN r.PartnerKind IS NULL THEN 1 ELSE 0 END,
             CASE WHEN r.CurrencyCode IS NULL THEN 1 ELSE 0 END, r.ValidFrom DESC;
END;

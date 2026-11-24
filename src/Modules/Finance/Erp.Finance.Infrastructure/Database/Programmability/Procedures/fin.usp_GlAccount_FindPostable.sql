CREATE OR ALTER PROCEDURE fin.usp_GlAccount_FindPostable
    @CompanyId  int,
    @Code       nvarchar(20),
    @OnDate     date
AS
BEGIN
    -- Contul care poate primi note la o data (C1 6, IChartOfAccountsQueries): al planului romanesc, frunza, activ, in vigoare, comun sau al
    -- firmei. Fara rand daca nu exista; al doilea set spune de ce (pentru mesajul validarii).
    SET NOCOUNT ON;

    DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

    SELECT a.Id, a.Code, a.Name, a.AccountType, a.IsPostable, a.CurrencyMode, a.CurrencyCode, a.PartyRequirement, a.CompanyId
    FROM fin.GlAccount AS a
    WHERE a.ChartId = @Chart AND a.CodeKey = UPPER(@Code) AND a.IsPostable = 1 AND a.IsActive = 1
      AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId)
      AND (a.ValidFrom IS NULL OR a.ValidFrom <= @OnDate) AND (a.ValidTo IS NULL OR a.ValidTo >= @OnDate);

    SELECT CASE
               WHEN a.Id IS NULL THEN 1
               WHEN a.CompanyId IS NOT NULL AND a.CompanyId <> @CompanyId THEN 1
               WHEN a.IsActive = 0 OR (a.ValidFrom IS NOT NULL AND a.ValidFrom > @OnDate) OR (a.ValidTo IS NOT NULL AND a.ValidTo < @OnDate) THEN 2
               WHEN a.IsPostable = 0 THEN 3
               ELSE 0 END AS Reason
    FROM (SELECT 1 AS One) AS d
    LEFT JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.CodeKey = UPPER(@Code);
END;

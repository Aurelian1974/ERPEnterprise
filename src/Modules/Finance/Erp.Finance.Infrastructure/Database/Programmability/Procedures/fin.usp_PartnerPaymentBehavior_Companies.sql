CREATE OR ALTER PROCEDURE fin.usp_PartnerPaymentBehavior_Companies
AS
BEGIN
    -- Firmele pentru jobul lunar al istoricului de plata (F5 3.4): cele cu partide de client, cu Fin.Forecast.HistoryMonths (implicit 12).
    SET NOCOUNT ON;

    SELECT c.Id AS CompanyId, ISNULL(TRY_CAST(s.Value AS int), 12) AS HistoryMonths
    FROM core.Company AS c
    LEFT JOIN fin.CompanySetting AS s ON s.CompanyId = c.Id AND s.Code = 'Fin.Forecast.HistoryMonths'
    WHERE EXISTS (SELECT 1 FROM fin.OpenItem AS i WHERE i.CompanyId = c.Id AND i.Role = 1)
    ORDER BY c.Id;
END;

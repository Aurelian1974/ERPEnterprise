CREATE OR ALTER PROCEDURE core.usp_PartnerBlock_Get
    @PartnerId int,
    @CompanyId int
AS
BEGIN
    -- Tab-ul „Blocare și credit” al partenerului (02.01, 3.6 punctul 6): blocarea (comuna tuturor firmelor) si limita de credit a
    -- firmei curente. Fara rand = neblocat / fara limita (RowVersion NULL la prima salvare).
    SET NOCOUNT ON;

    SELECT ISNULL(b.BlockedFor, 0) AS BlockedFor, b.Reason, b.BlockedAt, u.DisplayName AS BlockedByName, b.RowVersion AS BlockRowVersion,
           c.CreditLimit, ISNULL(c.OverLimitAction, 1) AS OverLimitAction, c.RowVersion AS CreditRowVersion
    FROM core.Partner AS p
    LEFT JOIN core.PartnerBlock AS b ON b.PartnerId = p.Id
    LEFT JOIN core.[User] AS u ON u.Id = b.ModifiedBy AND b.BlockedFor > 0
    LEFT JOIN core.PartnerCredit AS c ON c.PartnerId = p.Id AND c.CompanyId = @CompanyId
    WHERE p.Id = @PartnerId;
END;

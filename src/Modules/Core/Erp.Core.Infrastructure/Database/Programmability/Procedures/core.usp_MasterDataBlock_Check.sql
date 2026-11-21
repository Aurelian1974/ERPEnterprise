CREATE OR ALTER PROCEDURE core.usp_MasterDataBlock_Check
    @CompanyId int,
    @PartnerId int = NULL,
    @ItemId    int = NULL
AS
BEGIN
    -- Verificarea folosita de module pe documente (IMasterDataBlockCheck, 02.01, 6): blocarea partenerului sau a articolului,
    -- activ / inactiv si limita de credit a clientului pe firma. Soldul clientului il da modulul care il tine (IPartnerExposureProvider).
    SET NOCOUNT ON;

    IF @PartnerId IS NOT NULL
        SELECT p.Name, p.IsActive, ISNULL(b.BlockedFor, 0) AS BlockedFor, b.Reason, c.CreditLimit, ISNULL(c.OverLimitAction, 1) AS OverLimitAction
        FROM core.Partner AS p
        LEFT JOIN core.PartnerBlock AS b ON b.PartnerId = p.Id
        LEFT JOIN core.PartnerCredit AS c ON c.PartnerId = p.Id AND c.CompanyId = @CompanyId
        WHERE p.Id = @PartnerId;
    ELSE
        SELECT i.Name, i.IsActive, ISNULL(b.BlockedFor, 0) AS BlockedFor, b.Reason, CAST(NULL AS decimal(19,2)) AS CreditLimit,
               CAST(1 AS tinyint) AS OverLimitAction
        FROM core.Item AS i
        LEFT JOIN core.ItemBlock AS b ON b.ItemId = i.Id
        WHERE i.Id = @ItemId;
END;

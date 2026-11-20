CREATE OR ALTER PROCEDURE core.usp_Partner_FindForImport
    @Keys core.PartnerKeyList READONLY
AS
BEGIN
    -- Partenerii existenti cu acelasi cod (fara majuscule) sau acelasi CUI ca randurile importate (02.01, 4.8 pasul 5: reluarea
    -- importului actualizeaza, nu dubleaza). Datele pe care importul nu le contine (firma din grup, data verificarii ANAF) raman.
    SET NOCOUNT ON;

    -- Doua cautari pe indecsii unici (CodeKey, FiscalCode), unite.
    SELECT p.Id, p.Code, p.FiscalCode, p.Name, p.IntraGroupCompanyId, p.AnafCheckedAt, p.RowVersion
    FROM core.Partner AS p
    JOIN (SELECT DISTINCT UPPER(Code) AS CodeKey FROM @Keys WHERE Code IS NOT NULL) AS k ON k.CodeKey = p.CodeKey
    UNION
    SELECT p.Id, p.Code, p.FiscalCode, p.Name, p.IntraGroupCompanyId, p.AnafCheckedAt, p.RowVersion
    FROM core.Partner AS p
    JOIN (SELECT DISTINCT FiscalCode FROM @Keys WHERE FiscalCode IS NOT NULL) AS k ON k.FiscalCode = p.FiscalCode;
END;

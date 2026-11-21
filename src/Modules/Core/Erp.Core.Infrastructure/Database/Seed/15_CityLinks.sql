-- Leaga adresele existente de localitatile SIRUTA (02.01, 3.5), dupa livrarea 14_Cities.sql: pentru adresele din Romania fara cod,
-- localitatea cu acelasi nume din judet (core.fn_CityMatch, doar daca e unica). Textul localitatii ramane cum l-a scris utilizatorul;
-- la urmatoarea salvare devine numele oficial. Ruleaza la fiecare livrare; atinge doar adresele inca nelegate.
SET NOCOUNT ON;
SET XACT_ABORT ON;

UPDATE a
SET CitySirutaCode = m.SirutaCode
FROM core.Company AS a
CROSS APPLY core.fn_CityMatch(a.CountyCode, a.City) AS m
WHERE a.CitySirutaCode IS NULL AND a.CountryCode = 'RO' AND a.CountyCode IS NOT NULL AND a.City IS NOT NULL;

UPDATE a
SET CitySirutaCode = m.SirutaCode
FROM core.Branch AS a
CROSS APPLY core.fn_CityMatch(a.CountyCode, a.City) AS m
WHERE a.CitySirutaCode IS NULL AND a.CountryCode = 'RO' AND a.CountyCode IS NOT NULL AND a.City IS NOT NULL;

UPDATE a
SET CitySirutaCode = m.SirutaCode
FROM core.Location AS a
CROSS APPLY core.fn_CityMatch(a.CountyCode, a.City) AS m
WHERE a.CitySirutaCode IS NULL AND a.CountryCode = 'RO' AND a.CountyCode IS NOT NULL AND a.City IS NOT NULL;

UPDATE a
SET CitySirutaCode = m.SirutaCode
FROM core.CompanyAddress AS a
CROSS APPLY core.fn_CityMatch(a.CountyCode, a.City) AS m
WHERE a.CitySirutaCode IS NULL AND a.CountryCode = 'RO' AND a.CountyCode IS NOT NULL AND a.City IS NOT NULL;

UPDATE a
SET CitySirutaCode = m.SirutaCode
FROM core.Partner AS a
CROSS APPLY core.fn_CityMatch(a.CountyCode, a.City) AS m
WHERE a.CitySirutaCode IS NULL AND a.CountryCode = 'RO' AND a.CountyCode IS NOT NULL AND a.City IS NOT NULL;

UPDATE a
SET CitySirutaCode = m.SirutaCode
FROM core.PartnerAddress AS a
CROSS APPLY core.fn_CityMatch(a.CountyCode, a.City) AS m
WHERE a.CitySirutaCode IS NULL AND a.CountryCode = 'RO' AND a.CountyCode IS NOT NULL AND a.City IS NOT NULL;

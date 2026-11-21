-- Nomenclatorul de valute (02.01, 3.5): ISO 4217, cu monedele publicate de BNR. MERGE pe Code (MIG-06): denumirea si zecimalele
-- se actualizeaza, starea activ / inactiv ramane alegerea clientului. Active implicit, la prima livrare: RON (baza), EUR, USD, GBP, CHF.
-- XAU (aurul, gram) si XDR (DST, FMI) sunt publicate de BNR si au coduri ISO 4217, dar nu se folosesc pe documente.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Currency AS target
USING (VALUES
    ('RON', N'Leu românesc', 2, 1),
    ('EUR', N'Euro', 2, 1),
    ('USD', N'Dolar american', 2, 1),
    ('GBP', N'Liră sterlină', 2, 1),
    ('CHF', N'Franc elvețian', 2, 1),
    ('AED', N'Dirham Emiratele Arabe Unite', 2, 0),
    ('AUD', N'Dolar australian', 2, 0),
    ('BGN', N'Leva bulgărească', 2, 0),
    ('BRL', N'Real brazilian', 2, 0),
    ('CAD', N'Dolar canadian', 2, 0),
    ('CNY', N'Yuan renminbi chinezesc', 2, 0),
    ('CZK', N'Coroană cehă', 2, 0),
    ('DKK', N'Coroană daneză', 2, 0),
    ('EGP', N'Liră egipteană', 2, 0),
    ('HKD', N'Dolar Hong Kong', 2, 0),
    ('HUF', N'Forint maghiar', 2, 0),
    ('IDR', N'Rupie indoneziană', 2, 0),
    ('ILS', N'Shekel israelian', 2, 0),
    ('INR', N'Rupie indiană', 2, 0),
    ('ISK', N'Coroană islandeză', 0, 0),
    ('JPY', N'Yen japonez', 0, 0),
    ('KRW', N'Won sud-coreean', 0, 0),
    ('MDL', N'Leu moldovenesc', 2, 0),
    ('MXN', N'Peso mexican', 2, 0),
    ('MYR', N'Ringgit malaiezian', 2, 0),
    ('NOK', N'Coroană norvegiană', 2, 0),
    ('NZD', N'Dolar neozeelandez', 2, 0),
    ('PHP', N'Peso filipinez', 2, 0),
    ('PLN', N'Zlot polonez', 2, 0),
    ('RSD', N'Dinar sârbesc', 2, 0),
    ('RUB', N'Rublă rusească', 2, 0),
    ('SEK', N'Coroană suedeză', 2, 0),
    ('SGD', N'Dolar Singapore', 2, 0),
    ('THB', N'Baht thailandez', 2, 0),
    ('TRY', N'Liră turcească', 2, 0),
    ('UAH', N'Hryvna ucraineană', 2, 0),
    ('XAU', N'Aur (gram)', 2, 0),
    ('XDR', N'Drepturi speciale de tragere (FMI)', 2, 0),
    ('ZAR', N'Rand sud-african', 2, 0)
) AS source (Code, Name, Decimals, IsActive)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.Decimals <> source.Decimals) THEN
    UPDATE SET Name = source.Name, Decimals = source.Decimals, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Decimals, IsActive, CreatedBy) VALUES (source.Code, source.Name, source.Decimals, source.IsActive, 1);

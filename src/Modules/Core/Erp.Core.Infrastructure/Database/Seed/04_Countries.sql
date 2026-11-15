-- Tarile folosite in adresele firmelor (02.01, 3.5): Romania, statele UE si cateva frecvente. Lista completa ISO 3166-1
-- vine cu nomenclatoarele (etapa 5). MERGE pe Code (MIG-06); denumirile modificate de client nu se suprascriu.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Country AS target
USING (VALUES
    ('RO', N'România', 1),
    ('AT', N'Austria', 1),
    ('BE', N'Belgia', 1),
    ('BG', N'Bulgaria', 1),
    ('HR', N'Croația', 1),
    ('CY', N'Cipru', 1),
    ('CZ', N'Cehia', 1),
    ('DK', N'Danemarca', 1),
    ('EE', N'Estonia', 1),
    ('FI', N'Finlanda', 1),
    ('FR', N'Franța', 1),
    ('DE', N'Germania', 1),
    ('GR', N'Grecia', 1),
    ('HU', N'Ungaria', 1),
    ('IE', N'Irlanda', 1),
    ('IT', N'Italia', 1),
    ('LV', N'Letonia', 1),
    ('LT', N'Lituania', 1),
    ('LU', N'Luxemburg', 1),
    ('MT', N'Malta', 1),
    ('NL', N'Țările de Jos', 1),
    ('PL', N'Polonia', 1),
    ('PT', N'Portugalia', 1),
    ('SK', N'Slovacia', 1),
    ('SI', N'Slovenia', 1),
    ('ES', N'Spania', 1),
    ('SE', N'Suedia', 1),
    ('MD', N'Republica Moldova', 0),
    ('UA', N'Ucraina', 0),
    ('RS', N'Serbia', 0),
    ('TR', N'Turcia', 0),
    ('GB', N'Regatul Unit', 0),
    ('CH', N'Elveția', 0),
    ('NO', N'Norvegia', 0),
    ('US', N'Statele Unite ale Americii', 0),
    ('CN', N'China', 0)
) AS source (Code, Name, IsEu)
ON target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, IsEu, CreatedBy) VALUES (source.Code, source.Name, source.IsEu, 1);

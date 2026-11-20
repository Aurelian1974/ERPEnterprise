-- Unitatile de masura uzuale, cu codurile UN/ECE (Recomandarea 20, unitati; Recomandarea 21, ambalaje, prefix X) cerute de
-- e-Factura. Se adauga doar daca lipsesc; clientul adauga altele din Configurare › Nomenclatoare.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.UnitOfMeasure AS target
USING (VALUES
    (N'buc',   N'bucată',          'H87'),
    (N'unit',  N'unitate',         'C62'),
    (N'kg',    N'kilogram',        'KGM'),
    (N'g',     N'gram',            'GRM'),
    (N't',     N'tonă',            'TNE'),
    (N'l',     N'litru',           'LTR'),
    (N'ml',    N'mililitru',       'MLT'),
    (N'm',     N'metru',           'MTR'),
    (N'cm',    N'centimetru',      'CMT'),
    (N'km',    N'kilometru',       'KMT'),
    (N'mp',    N'metru pătrat',    'MTK'),
    (N'mc',    N'metru cub',       'MTQ'),
    (N'kWh',   N'kilowatt-oră',    'KWH'),
    (N'h',     N'oră',             'HUR'),
    (N'zi',    N'zi',              'DAY'),
    (N'luna',  N'lună',            'MON'),
    (N'set',   N'set',             'SET'),
    (N'per',   N'pereche',         'PR'),
    (N'bax',   N'bax',             'XBX'),
    (N'cut',   N'cutie',           'XCT'),
    (N'lad',   N'lădiță',          'XCR'),
    (N'pal',   N'palet',           'XPX'),
    (N'pac',   N'pachet',          'XPK'),
    (N'sac',   N'sac',             'XSA'),
    (N'rola',  N'rolă',            'XRO'),
    (N'sticla', N'sticlă',         'XBO')
) AS source (Code, Name, UneceCode)
ON target.UneceCode = source.UneceCode
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, UneceCode, CreatedBy)
    VALUES (source.Code, source.Name, source.UneceCode, 1);

-- Judetele Romaniei si municipiul Bucuresti (02.01, 3.5): indicativul ISO 3166-2:RO, folosit si in e-Factura. MERGE pe Code.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.County AS target
USING (VALUES
    (N'AB', N'Alba'), (N'AR', N'Arad'), (N'AG', N'Argeș'), (N'BC', N'Bacău'), (N'BH', N'Bihor'),
    (N'BN', N'Bistrița-Năsăud'), (N'BT', N'Botoșani'), (N'BV', N'Brașov'), (N'BR', N'Brăila'), (N'B', N'București'),
    (N'BZ', N'Buzău'), (N'CS', N'Caraș-Severin'), (N'CL', N'Călărași'), (N'CJ', N'Cluj'), (N'CT', N'Constanța'),
    (N'CV', N'Covasna'), (N'DB', N'Dâmbovița'), (N'DJ', N'Dolj'), (N'GL', N'Galați'), (N'GR', N'Giurgiu'),
    (N'GJ', N'Gorj'), (N'HR', N'Harghita'), (N'HD', N'Hunedoara'), (N'IL', N'Ialomița'), (N'IS', N'Iași'),
    (N'IF', N'Ilfov'), (N'MM', N'Maramureș'), (N'MH', N'Mehedinți'), (N'MS', N'Mureș'), (N'NT', N'Neamț'),
    (N'OT', N'Olt'), (N'PH', N'Prahova'), (N'SM', N'Satu Mare'), (N'SJ', N'Sălaj'), (N'SB', N'Sibiu'),
    (N'SV', N'Suceava'), (N'TR', N'Teleorman'), (N'TM', N'Timiș'), (N'TL', N'Tulcea'), (N'VS', N'Vaslui'),
    (N'VL', N'Vâlcea'), (N'VN', N'Vrancea')
) AS source (Code, Name)
ON target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, CountryCode, Name, CreatedBy) VALUES (source.Code, 'RO', source.Name, 1);

-- Politica de zecimale implicita STANDARD (02.01, 3.5): pret 4, cantitate 3, valoare 2 zecimale, rotunjire la jumatate
-- departe de zero (MidpointRounding.AwayFromZero = 1). Administrarea politicilor vine in etapa 9. MERGE pe Code.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.PrecisionPolicy AS target
USING (VALUES (N'STANDARD', N'Standard (preț 4, cantitate 3)', 4, 3, 2, 1)) AS source (Code, Name, PricePrecision, QuantityPrecision, AmountPrecision, RoundingMode)
ON target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, PricePrecision, QuantityPrecision, AmountPrecision, RoundingMode, CreatedBy)
    VALUES (source.Code, source.Name, source.PricePrecision, source.QuantityPrecision, source.AmountPrecision, source.RoundingMode, 1);

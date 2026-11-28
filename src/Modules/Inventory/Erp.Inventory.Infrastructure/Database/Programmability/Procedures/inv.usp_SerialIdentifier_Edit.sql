CREATE OR ALTER PROCEDURE inv.usp_SerialIdentifier_Edit
    @CompanyId int,
    @SerialId  int,
    @TypeCode  nvarchar(20),
    @Value     nvarchar(100) = NULL,
    @Reason    nvarchar(400),
    @UserId    int
AS
BEGIN
    -- Corectarea unui identificator al bucatii (02.05, S4 3.4, Inv.Serial.EditIdentifiers), cu motiv si istoric; fara valoare, identificatorul
    -- se sterge. Unicitatea pe tip ramane (54472); formatul il verifica apelantul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Type int = (SELECT Id FROM inv.SerialIdentifierType WHERE CodeKey = UPPER(@TypeCode));
    IF @Type IS NULL
        THROW 54471, N'Tipul identificatorului nu există.', 1;
    IF NOT EXISTS (SELECT 1 FROM inv.Serial WHERE Id = @SerialId AND CompanyId = @CompanyId)
        THROW 54461, N'Seria nu există în firma curentă.', 1;

    DECLARE @Old nvarchar(100), @Unique bit;
    SELECT @Old = Value, @Unique = IsUnique FROM inv.SerialIdentifier WITH (UPDLOCK, HOLDLOCK) WHERE SerialId = @SerialId AND IdentifierTypeId = @Type;
    SET @Value = NULLIF(LTRIM(RTRIM(@Value)), N'');
    SET @Unique = COALESCE(@Unique, (SELECT TOP (1) r.IsUnique FROM inv.ItemSerialIdentifierRule AS r
        JOIN core.ItemVariant AS v ON v.Id = (SELECT ItemVariantId FROM inv.Serial WHERE Id = @SerialId)
        JOIN core.Item AS i ON i.Id = v.ItemId
        WHERE r.IdentifierTypeId = @Type AND (r.ItemId = i.Id OR r.ItemGroupId = i.ItemGroupId) ORDER BY CASE WHEN r.ItemId IS NULL THEN 1 ELSE 0 END), 0);
    IF @Value IS NOT NULL AND @Unique = 1 AND EXISTS (SELECT 1 FROM inv.SerialIdentifier WHERE IdentifierTypeId = @Type AND Value = @Value AND IsUnique = 1
                                                      AND SerialId <> @SerialId)
        THROW 54472, N'Identificatorul este deja folosit pe altă bucată.', 1;

    IF @Value IS NULL
        DELETE FROM inv.SerialIdentifier WHERE SerialId = @SerialId AND IdentifierTypeId = @Type;
    ELSE IF @Old IS NULL
        INSERT INTO inv.SerialIdentifier (SerialId, IdentifierTypeId, Value, IsUnique, CreatedBy) VALUES (@SerialId, @Type, @Value, @Unique, @UserId);
    ELSE
        UPDATE inv.SerialIdentifier SET Value = @Value WHERE SerialId = @SerialId AND IdentifierTypeId = @Type;

    INSERT INTO inv.SerialIdentifierHistory (SerialId, IdentifierTypeId, OldValue, NewValue, Reason, ChangedBy)
    VALUES (@SerialId, @Type, @Old, @Value, @Reason, @UserId);
END;

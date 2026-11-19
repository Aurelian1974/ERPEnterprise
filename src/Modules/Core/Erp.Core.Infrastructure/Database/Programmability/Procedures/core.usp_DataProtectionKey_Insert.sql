CREATE OR ALTER PROCEDURE core.usp_DataProtectionKey_Insert
    @FriendlyName nvarchar(200),
    @Xml          nvarchar(max)
AS
BEGIN
    -- O cheie noua a inelului (08, 5.12): Data Protection o creeaza la prima pornire si la rotire (90 de zile).
    -- Cheile vechi raman: datele protejate cu ele trebuie sa ramana lizibile.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO core.DataProtectionKey (FriendlyName, Xml)
    VALUES (@FriendlyName, @Xml);
END;

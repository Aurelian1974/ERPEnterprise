CREATE OR ALTER PROCEDURE core.usp_Currency_SetActive
    @Code       char(3),
    @IsActive   bit,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Activarea unei monede (macheta 07b, „+ monedă”): doar monedele active se aleg pe conturi, documente si cursuri manuale.
    -- RON e moneda de baza si ramane activa (50021); cursurile salvate raman si dupa dezactivare.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Code = 'RON' AND @IsActive = 0
        THROW 50021, N'Leul e moneda de bază și rămâne activ.', 1;

    UPDATE core.Currency
    SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Code = @Code AND RowVersion = @RowVersion;

    IF @@ROWCOUNT = 0
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @Code)
            THROW 50027, N'Moneda nu există.', 1;
        THROW 50006, N'Moneda a fost modificată de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT RowVersion FROM core.Currency WHERE Code = @Code;
END;

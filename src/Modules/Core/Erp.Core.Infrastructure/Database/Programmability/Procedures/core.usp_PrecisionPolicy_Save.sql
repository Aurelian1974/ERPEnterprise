CREATE OR ALTER PROCEDURE core.usp_PrecisionPolicy_Save
    @Id                int = NULL,
    @Code              nvarchar(20),
    @Name              nvarchar(100),
    @PricePrecision    tinyint,
    @QuantityPrecision tinyint,
    @RoundingMode      tinyint,
    @IsActive          bit,
    @RowVersion        binary(8) = NULL,
    @UserId            int
AS
BEGIN
    -- Politica de zecimale (02.01, 3.5; macheta 07d): pret si cantitate 0–6, valoarea mereu 2 (cerinta fiscala), rotunjirea ca
    -- System.MidpointRounding. Cod unic fara diferenta de majuscule (50018). O politica folosita de o firma nu se dezactiveaza (50010).
    -- Schimbarea afecteaza doar documentele noi: documentele isi memoreaza valorile.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM core.PrecisionPolicy WITH (UPDLOCK, HOLDLOCK) WHERE UPPER(Code) = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul politicii există deja.', 1;
    IF @Id IS NOT NULL AND @IsActive = 0 AND EXISTS (SELECT 1 FROM core.Company WHERE DefaultPrecisionPolicyId = @Id)
        THROW 50010, N'Politica e implicită pentru o firmă; alegeți întâi altă politică pentru firmă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.PrecisionPolicy (Code, Name, PricePrecision, QuantityPrecision, AmountPrecision, RoundingMode, IsActive, CreatedBy)
        VALUES (@Code, @Name, @PricePrecision, @QuantityPrecision, 2, @RoundingMode, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.PrecisionPolicy
        SET Code = @Code, Name = @Name, PricePrecision = @PricePrecision, QuantityPrecision = @QuantityPrecision, RoundingMode = @RoundingMode,
            IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.PrecisionPolicy WHERE Id = @Id)
                THROW 50027, N'Politica nu există.', 1;
            THROW 50006, N'Politica a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.PrecisionPolicy WHERE Id = @Id;
END;

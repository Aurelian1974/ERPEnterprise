CREATE OR ALTER PROCEDURE fin.usp_BankFormatProfile_Save
    @Id                  int = NULL,
    @Code                nvarchar(30),
    @Name                nvarchar(200),
    @BankBic             varchar(11) = NULL,
    @StatementFormat     tinyint,
    @CsvMapping          nvarchar(max) = NULL,
    @PaymentFormat       tinyint,
    @PaymentMapping      nvarchar(max) = NULL,
    @MaxRemittanceLength smallint,
    @IsActive            bit,
    @RowVersion          binary(8) = NULL,
    @UserId              int
AS
BEGIN
    -- Salveaza un profil de format bancar (02.02, F2 3.1). Codul e unic pe instalare (51022). Profilele livrate isi pastreaza codul si
    -- formatele (51021). Versiune diferita: 51002.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @System bit, @Version binary(8), @OldCode nvarchar(30), @OldStatement tinyint, @OldPayment tinyint;
        SELECT @System = IsSystem, @Version = RowVersion, @OldCode = Code, @OldStatement = StatementFormat, @OldPayment = PaymentFormat
        FROM fin.BankFormatProfile WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @System IS NULL
            THROW 51021, N'Profilul bancar nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51002, N'Profilul bancar a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @System = 1 AND (@OldCode <> @Code OR @OldStatement <> @StatementFormat OR @OldPayment <> @PaymentFormat)
            THROW 51021, N'Un profil livrat își păstrează codul și formatele; se poate redenumi sau dezactiva.', 1;
    END;

    IF EXISTS (SELECT 1 FROM fin.BankFormatProfile WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 51022, N'Codul profilului bancar există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.BankFormatProfile (Code, Name, BankBic, StatementFormat, CsvMapping, PaymentFormat, PaymentMapping, MaxRemittanceLength, IsActive,
            CreatedBy)
        VALUES (@Code, @Name, @BankBic, @StatementFormat, @CsvMapping, @PaymentFormat, @PaymentMapping, @MaxRemittanceLength, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.BankFormatProfile
        SET Code = @Code, Name = @Name, BankBic = @BankBic, StatementFormat = @StatementFormat, CsvMapping = @CsvMapping, PaymentFormat = @PaymentFormat,
            PaymentMapping = @PaymentMapping, MaxRemittanceLength = @MaxRemittanceLength, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(),
            ModifiedBy = @UserId
        WHERE Id = @Id;

    SELECT Id, RowVersion FROM fin.BankFormatProfile WHERE Id = @Id;
END;

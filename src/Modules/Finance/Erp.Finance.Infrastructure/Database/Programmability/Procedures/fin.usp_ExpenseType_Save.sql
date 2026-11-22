CREATE OR ALTER PROCEDURE fin.usp_ExpenseType_Save
    @Id                     int = NULL,
    @Code                   nvarchar(30),
    @Name                   nvarchar(200),
    @IsPerDiem              bit,
    @DailyAmount            decimal(19,2) = NULL,
    @NonTaxableDailyLimit   decimal(19,2) = NULL,
    @VatDeductibleOnReceipt bit,
    @ValidFrom              date,
    @ValidTo                date = NULL,
    @CounterGlAccountCode   nvarchar(20) = NULL,
    @IsActive               bit,
    @RowVersion             binary(8) = NULL,
    @UserId                 int
AS
BEGIN
    -- Un tip de cheltuiala sau o versiune noua a lui (02.02, F1 3.7; macheta cf-02; P6): o schimbare de suma se face ca rand nou cu
    -- alt ValidFrom (acelasi cod), ca deconturile vechi sa pastreze sumele lor. Pe un rand existent se schimba doar denumirea, contul,
    -- deductibilitatea, sfarsitul valabilitatii si activarea; sumele unui rand livrat raman ale versiunii (51021). Cod + data existente: 51022.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @IsPerDiem = 1 AND (@DailyAmount IS NULL OR @NonTaxableDailyLimit IS NULL)
        THROW 51021, N'Diurna cere suma pe zi și plafonul neimpozabil.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @Version binary(8), @OldCode nvarchar(30), @OldFrom date, @OldPerDiem bit, @OldDaily decimal(19,2), @OldLimit decimal(19,2), @System bit;
        SELECT @Version = RowVersion, @OldCode = Code, @OldFrom = ValidFrom, @OldPerDiem = IsPerDiem, @OldDaily = DailyAmount,
               @OldLimit = NonTaxableDailyLimit, @System = IsSystem
        FROM fin.ExpenseType WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @Version IS NULL
            THROW 51021, N'Tipul de cheltuială nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51002, N'Tipul de cheltuială a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF UPPER(@OldCode) <> UPPER(@Code) OR @OldFrom <> @ValidFrom OR @OldPerDiem <> @IsPerDiem
           OR (@System = 1 AND (ISNULL(@OldDaily, -1) <> ISNULL(@DailyAmount, -1) OR ISNULL(@OldLimit, -1) <> ISNULL(@NonTaxableDailyLimit, -1)))
            THROW 51021, N'Codul, data de vigoare și sumele unei versiuni nu se schimbă; adăugați o versiune nouă cu altă dată.', 1;
    END;

    IF EXISTS (SELECT 1 FROM fin.ExpenseType WITH (UPDLOCK, HOLDLOCK)
               WHERE CodeKey = UPPER(@Code) AND ValidFrom = @ValidFrom AND (@Id IS NULL OR Id <> @Id))
        THROW 51022, N'Tipul de cheltuială are deja o versiune cu această dată de vigoare.', 1;
    IF @Id IS NULL AND EXISTS (SELECT 1 FROM fin.ExpenseType WHERE CodeKey = UPPER(@Code) AND IsPerDiem <> @IsPerDiem)
        THROW 51021, N'Versiunile aceluiași cod sunt toate diurnă sau toate cheltuieli obișnuite.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.ExpenseType (Code, Name, IsPerDiem, DailyAmount, NonTaxableDailyLimit, CurrencyCode, CountryCode, VatDeductibleOnReceipt, ValidFrom,
            ValidTo, CounterGlAccountCode, IsSystem, IsActive, CreatedBy)
        VALUES (@Code, @Name, @IsPerDiem, @DailyAmount, @NonTaxableDailyLimit, CASE WHEN @IsPerDiem = 1 THEN 'RON' END, CASE WHEN @IsPerDiem = 1 THEN 'RO' END,
            @VatDeductibleOnReceipt, @ValidFrom, @ValidTo, @CounterGlAccountCode, 0, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.ExpenseType
        SET Name = @Name, DailyAmount = @DailyAmount, NonTaxableDailyLimit = @NonTaxableDailyLimit, VatDeductibleOnReceipt = @VatDeductibleOnReceipt,
            ValidTo = @ValidTo, CounterGlAccountCode = @CounterGlAccountCode, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

    SELECT Id, RowVersion FROM fin.ExpenseType WHERE Id = @Id;
END;

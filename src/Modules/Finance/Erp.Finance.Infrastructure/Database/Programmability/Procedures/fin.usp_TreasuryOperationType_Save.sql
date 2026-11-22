CREATE OR ALTER PROCEDURE fin.usp_TreasuryOperationType_Save
    @Id                     int = NULL,
    @Code                   nvarchar(30),
    @Name                   nvarchar(200),
    @Direction              tinyint,
    @RequiresPartner        bit,
    @RequiresEmployee       bit,
    @RequiresCounterAccount bit,
    @SettlementKind         tinyint,
    @CounterGlAccountCode   nvarchar(20) = NULL,
    @CashLimitCategory      varchar(20) = NULL,
    @AppliesToCash          bit,
    @AppliesToBank          bit,
    @IsActive               bit,
    @RowVersion             binary(8) = NULL,
    @UserId                 int
AS
BEGIN
    -- Un tip de operatiune de trezorerie (02.02, F1 3.2; macheta cf-01). Tipurile livrate (IsSystem) nu se sterg si nu isi schimba
    -- codul, sensul, cerintele sau decontarea: se pot redenumi, dezactiva si li se poate schimba contul de referinta (51021). Cod
    -- duplicat: 51022. Versiune diferita: 51002.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @CashLimitCategory IS NOT NULL AND @CashLimitCategory NOT IN ('Incasare', 'Plata', 'AvansPersoanaZi')
        THROW 51021, N'Plafonul de numerar ales nu există.', 1;
    IF @CashLimitCategory = 'Incasare' AND @Direction <> 1 OR @CashLimitCategory IN ('Plata', 'AvansPersoanaZi') AND @Direction <> 2
        THROW 51021, N'Plafonul de numerar nu se potrivește sensului operațiunii.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @System bit, @Version binary(8), @OldCode nvarchar(30), @OldDirection tinyint, @OldPartner bit, @OldEmployee bit, @OldCounter bit,
                @OldSettlement tinyint;
        SELECT @System = IsSystem, @Version = RowVersion, @OldCode = Code, @OldDirection = Direction, @OldPartner = RequiresPartner,
               @OldEmployee = RequiresEmployee, @OldCounter = RequiresCounterAccount, @OldSettlement = SettlementKind
        FROM fin.TreasuryOperationType WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @System IS NULL
            THROW 51021, N'Tipul de operațiune nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51002, N'Tipul de operațiune a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @System = 1 AND (@OldCode <> @Code OR @OldDirection <> @Direction OR @OldPartner <> @RequiresPartner OR @OldEmployee <> @RequiresEmployee
                            OR @OldCounter <> @RequiresCounterAccount OR @OldSettlement <> @SettlementKind)
            THROW 51021, N'Un tip livrat își păstrează codul, sensul, cerințele și decontarea; se poate redenumi sau dezactiva.', 1;
    END;

    IF EXISTS (SELECT 1 FROM fin.TreasuryOperationType WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 51022, N'Codul tipului de operațiune există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.TreasuryOperationType (Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount, SettlementKind,
            CounterGlAccountCode, CashLimitCategory, AppliesToCash, AppliesToBank, IsSystem, IsActive, CreatedBy)
        VALUES (@Code, @Name, @Direction, @RequiresPartner, @RequiresEmployee, @RequiresCounterAccount, @SettlementKind, @CounterGlAccountCode,
            @CashLimitCategory, @AppliesToCash, @AppliesToBank, 0, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.TreasuryOperationType
        SET Code = @Code, Name = @Name, Direction = @Direction, RequiresPartner = @RequiresPartner, RequiresEmployee = @RequiresEmployee,
            RequiresCounterAccount = @RequiresCounterAccount, SettlementKind = @SettlementKind, CounterGlAccountCode = @CounterGlAccountCode,
            CashLimitCategory = @CashLimitCategory, AppliesToCash = @AppliesToCash, AppliesToBank = @AppliesToBank, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

    SELECT Id, RowVersion FROM fin.TreasuryOperationType WHERE Id = @Id;
END;

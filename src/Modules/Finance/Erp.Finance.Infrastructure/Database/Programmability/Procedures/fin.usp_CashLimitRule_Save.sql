CREATE OR ALTER PROCEDURE fin.usp_CashLimitRule_Save
    @Id         int = NULL,
    @CompanyId  int,
    @Category   varchar(20),
    @Amount     decimal(19,2),
    @Action     tinyint,
    @ValidFrom  date,
    @ValidTo    date = NULL,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Un plafon de numerar propriu al firmei curente (02.02, F1 3.6; macheta cf-03), verificat impreuna cu plafonul legal (cel mai strict
    -- castiga). Plafoanele legale nu se modifica din ecran: se actualizeaza doar prin versiune (51021). Aceeasi categorie si data: 51022.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Category NOT IN ('IncasarePJ', 'IncasarePF', 'PlataPJ', 'PlataPF', 'AvansPersoanaZi', 'SoldCasa')
        THROW 51021, N'Categoria de plafon nu există.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @RuleCompanyId int, @Version binary(8), @Exists bit = 0;
        SELECT @RuleCompanyId = CompanyId, @Version = RowVersion, @Exists = 1 FROM fin.CashLimitRule WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @Exists = 0 OR @RuleCompanyId IS NULL OR @RuleCompanyId <> @CompanyId
            THROW 51021, N'Plafoanele legale se actualizează doar prin versiune; se pot modifica doar plafoanele proprii ale firmei.', 1;
        IF @Version <> @RowVersion
            THROW 51002, N'Plafonul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    END;

    IF EXISTS (SELECT 1 FROM fin.CashLimitRule WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND Category = @Category AND CurrencyCode = 'RON' AND ValidFrom = @ValidFrom AND (@Id IS NULL OR Id <> @Id))
        THROW 51022, N'Firma are deja un plafon propriu pentru această categorie de la aceeași dată.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.CashLimitRule (CompanyId, Category, CurrencyCode, Amount, Action, ValidFrom, ValidTo, CreatedBy)
        VALUES (@CompanyId, @Category, 'RON', @Amount, @Action, @ValidFrom, @ValidTo, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.CashLimitRule
        SET Category = @Category, Amount = @Amount, Action = @Action, ValidFrom = @ValidFrom, ValidTo = @ValidTo, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

    SELECT Id, RowVersion FROM fin.CashLimitRule WHERE Id = @Id;
END;

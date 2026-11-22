CREATE OR ALTER PROCEDURE fin.usp_CardAcquirer_Save
    @Id               int = NULL,
    @CompanyId        int,
    @Code             nvarchar(30),
    @Name             nvarchar(200),
    @CounterpartyIban varchar(34) = NULL,
    @IsActive         bit,
    @RowVersion       binary(8) = NULL,
    @UserId           int
AS
BEGIN
    -- Procesatorul de carduri al firmei (02.02, F2 3.7): codul unic pe firma (51022) si IBAN-ul de pe care vin decontarile.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM fin.CardAcquirer WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 51022, N'Codul procesatorului există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.CardAcquirer (CompanyId, Code, Name, CounterpartyIban, IsActive, CreatedBy)
        VALUES (@CompanyId, @Code, @Name, @CounterpartyIban, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @AcquirerCompanyId int, @Version binary(8);
        SELECT @AcquirerCompanyId = CompanyId, @Version = RowVersion FROM fin.CardAcquirer WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @AcquirerCompanyId IS NULL OR @AcquirerCompanyId <> @CompanyId
            THROW 51021, N'Procesatorul nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51202, N'Procesatorul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        UPDATE fin.CardAcquirer
        SET Code = @Code, Name = @Name, CounterpartyIban = @CounterpartyIban, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END;

    SELECT Id, RowVersion FROM fin.CardAcquirer WHERE Id = @Id;
END;

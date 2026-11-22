CREATE OR ALTER PROCEDURE fin.usp_CashAdvance_Create
    @CompanyId        int,
    @EmployeeId       int,
    @Purpose          tinyint,
    @CurrencyCode     char(3),
    @Amount           decimal(19,2),
    @Description      nvarchar(500) = NULL,
    @TripStartDate    date = NULL,
    @TripEndDate      date = NULL,
    @SettlementDueDate date,
    @OverdueConfirmed bit,
    @UserId           int
AS
BEGIN
    -- Avansul spre decontare al unui angajat (02.02, F1 3.5 si 4.4 pasul 1), in starea De platit; dispozitia de plata (ciorna) se face
    -- in aceeasi tranzactie (C#). Angajatul e al firmei si activ; valuta doar la deplasari externe (51021). Verificarea avansului depasit
    -- (51018) si calculul termenului sunt in C#.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @EmployeeId AND CompanyId = @CompanyId AND IsActive = 1)
        THROW 51021, N'Angajatul nu aparține firmei sau este inactiv.', 1;
    IF @CurrencyCode <> 'RON' AND @Purpose <> 3
        THROW 51021, N'Avansul în valută se acordă doar pentru deplasări externe.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @CurrencyCode AND (IsActive = 1 OR Code = 'RON'))
        THROW 51021, N'Moneda nu există sau nu este activă.', 1;

    INSERT INTO fin.CashAdvance (CompanyId, EmployeeId, Purpose, CurrencyCode, Amount, Description, TripStartDate, TripEndDate, SettlementDueDate,
        OverdueConfirmed, CreatedBy)
    VALUES (@CompanyId, @EmployeeId, @Purpose, @CurrencyCode, @Amount, @Description, @TripStartDate, @TripEndDate, @SettlementDueDate,
        @OverdueConfirmed, @UserId);

    SELECT CAST(SCOPE_IDENTITY() AS int) AS Id;
END;

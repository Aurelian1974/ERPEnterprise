CREATE OR ALTER PROCEDURE fin.usp_CashLimitCheck_Log
    @CompanyId      int,
    @CashDocumentId int = NULL,
    @UserId         int,
    @PartnerId      int = NULL,
    @EmployeeId     int = NULL,
    @PersonName     nvarchar(200) = NULL,
    @Category       varchar(20),
    @DocumentDate   date,
    @Amount         decimal(19,2),
    @DayTotal       decimal(19,2),
    @LimitAmount    decimal(19,2),
    @Action         tinyint,
    @Confirmed      bit
AS
BEGIN
    -- O verificare de plafon refuzata sau o avertizare confirmata (02.02, F1 3.6 regula 4). Refuzurile se scriu pe o conexiune proprie,
    -- in afara tranzactiei comenzii (care se anuleaza), ca jurnalul incercarilor de depasire sa ramana.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO fin.CashLimitCheck (CompanyId, CashDocumentId, UserId, PartnerId, EmployeeId, PersonName, Category, DocumentDate, Amount, DayTotal,
        LimitAmount, Action, Confirmed)
    VALUES (@CompanyId, @CashDocumentId, @UserId, @PartnerId, @EmployeeId, @PersonName, @Category, @DocumentDate, @Amount, @DayTotal,
        @LimitAmount, @Action, @Confirmed);
END;

CREATE OR ALTER PROCEDURE fin.usp_RecurringEntryTemplate_Advance
    @CompanyId    int,
    @Id           int,
    @GeneratedFor date,
    @EntryId      bigint
AS
BEGIN
    -- Dupa generarea ciornei pentru @GeneratedFor: urmatoarea data = @GeneratedFor + periodicitatea (in luni; ultima zi ramane ultima zi).
    -- Doar daca data scadenta e inca @GeneratedFor, ca doua generari simultane sa nu sara o perioada.
    SET NOCOUNT ON;

    UPDATE fin.RecurringEntryTemplate
    SET NextDate = CASE WHEN @GeneratedFor = EOMONTH(@GeneratedFor) THEN EOMONTH(DATEADD(MONTH, Periodicity, @GeneratedFor))
                        ELSE DATEADD(MONTH, Periodicity, @GeneratedFor) END,
        LastEntryId = @EntryId, ModifiedAt = SYSUTCDATETIME()
    WHERE Id = @Id AND CompanyId = @CompanyId AND NextDate = @GeneratedFor;
    SELECT @@ROWCOUNT;
END;

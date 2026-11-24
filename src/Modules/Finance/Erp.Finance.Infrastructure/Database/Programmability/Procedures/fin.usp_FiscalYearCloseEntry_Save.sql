CREATE OR ALTER PROCEDURE fin.usp_FiscalYearCloseEntry_Save
    @CompanyId  int,
    @YearId     int,
    @Kind       tinyint,
    @EntryId    bigint
AS
BEGIN
    -- Leaga nota scrisa de inchiderea anului (C3 3.6), o data, la prima scriere.
    SET NOCOUNT ON;
    INSERT INTO fin.FiscalYearCloseEntry (FiscalYearId, Kind, CompanyId, EntryId)
    SELECT @YearId, @Kind, @CompanyId, @EntryId
    WHERE NOT EXISTS (SELECT 1 FROM fin.FiscalYearCloseEntry WHERE FiscalYearId = @YearId AND Kind = @Kind);
END;

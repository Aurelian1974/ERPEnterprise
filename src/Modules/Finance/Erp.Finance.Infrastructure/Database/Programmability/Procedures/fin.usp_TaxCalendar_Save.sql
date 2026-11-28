CREATE OR ALTER PROCEDURE fin.usp_TaxCalendar_Save
    @CompanyId  int,
    @Items      nvarchar(max)
AS
BEGIN
    -- Calendarul fiscal al firmei (C4 3.7): termenele noi se adauga, cele existente isi actualizeaza scadenta si sfarsitul perioadei.
    SET NOCOUNT ON;
    MERGE fin.TaxCalendarItem AS target
    USING (SELECT FormCode, PeriodFrom, PeriodTo, DueDate FROM OPENJSON(@Items) WITH (FormCode varchar(10) '$.formCode', PeriodFrom date '$.periodFrom',
        PeriodTo date '$.periodTo', DueDate date '$.dueDate')) AS source
    ON target.CompanyId = @CompanyId AND target.FormCode = source.FormCode AND target.PeriodFrom = source.PeriodFrom
    WHEN MATCHED AND (target.DueDate <> source.DueDate OR target.PeriodTo <> source.PeriodTo) THEN UPDATE SET DueDate = source.DueDate, PeriodTo = source.PeriodTo
    WHEN NOT MATCHED BY TARGET THEN INSERT (CompanyId, FormCode, PeriodFrom, PeriodTo, DueDate) VALUES (@CompanyId, source.FormCode, source.PeriodFrom, source.PeriodTo, source.DueDate);
END;

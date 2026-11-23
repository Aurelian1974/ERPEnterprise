CREATE OR ALTER PROCEDURE fin.usp_ReferenceRate_List
AS
BEGIN
    -- Ratele de referinta BNR, cele mai noi intai (F3 3.10).
    SET NOCOUNT ON;

    SELECT ValidFrom, Rate FROM fin.ReferenceRate ORDER BY ValidFrom DESC;
END;

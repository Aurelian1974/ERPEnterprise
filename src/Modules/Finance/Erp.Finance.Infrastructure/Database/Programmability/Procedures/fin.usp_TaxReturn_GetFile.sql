CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_GetFile
    @CompanyId  int,
    @FileId     int
AS
BEGIN
    -- Continutul unui fisier al declaratiei, pentru descarcare.
    SET NOCOUNT ON;
    SELECT f.FileName, f.Content FROM fin.TaxReturnFile AS f JOIN fin.TaxReturn AS r ON r.Id = f.ReturnId WHERE f.Id = @FileId AND r.CompanyId = @CompanyId;
END;

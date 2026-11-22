CREATE OR ALTER PROCEDURE fin.usp_PaymentBatch_File
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Fisierul ultimului export al unui lot (02.02, F2 4.2 pasul 4), pentru descarcare.
    SET NOCOUNT ON;
    SELECT FileName, FileSha256, FileContent FROM fin.PaymentBatch WHERE Id = @Id AND CompanyId = @CompanyId AND FileContent IS NOT NULL;
END;

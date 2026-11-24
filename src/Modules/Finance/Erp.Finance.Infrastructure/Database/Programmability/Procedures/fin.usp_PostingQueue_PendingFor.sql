CREATE OR ALTER PROCEDURE fin.usp_PostingQueue_PendingFor
    @CompanyId           int,
    @SourceDocumentType  varchar(50),
    @SourceDocumentId    bigint
AS
BEGIN
    -- Motivul pentru care documentul original al unei stornari asteapta in coada (C2 4.2); fara rand daca nu asteapta.
    SET NOCOUNT ON;

    SELECT TOP (1) Reason FROM fin.PostingQueue
    WHERE CompanyId = @CompanyId AND Status = 1 AND SourceDocumentType = @SourceDocumentType AND SourceDocumentId = @SourceDocumentId
    ORDER BY Id;
END;

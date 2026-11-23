CREATE OR ALTER PROCEDURE fin.usp_OpenItem_FindBySource
    @SourceDocumentType varchar(50),
    @SourceDocumentId   bigint,
    @Role               tinyint
AS
BEGIN
    -- Partida unei facturi dupa documentul sursa (F3 4.1 pasul 4: factura stornata), fara parte de document.
    SET NOCOUNT ON;

    SELECT o.Id FROM fin.OpenItem AS o
    WHERE o.SourceDocumentType = @SourceDocumentType AND o.SourceDocumentId = @SourceDocumentId AND o.SourceLineKey = N'' AND o.Role = @Role;
END;

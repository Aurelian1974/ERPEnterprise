CREATE OR ALTER PROCEDURE core.usp_ImportJob_Create
    @EntityType  varchar(20),
    @CompanyId   int = NULL,
    @FileName    nvarchar(260),
    @AnafCheck   bit,
    @TotalRows   int,
    @ValidRows   int,
    @ErrorRows   int,
    @WarningRows int,
    @ErrorReport nvarchar(max) = NULL,
    @Payload     nvarchar(max) = NULL,
    @UserId      int
AS
BEGIN
    -- Jobul unui import validat (02.01, 4.8 pasii 2-3), in asteptarea confirmarii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO core.ImportJob (EntityType, CompanyId, FileName, Status, AnafCheck, TotalRows, ValidRows, ErrorRows, WarningRows, ErrorReport,
        Payload, CreatedBy)
    VALUES (@EntityType, NULLIF(@CompanyId, 0), @FileName, 1, @AnafCheck, @TotalRows, @ValidRows, @ErrorRows, @WarningRows, @ErrorReport,
        @Payload, @UserId);

    SELECT Id, RowVersion FROM core.ImportJob WHERE Id = SCOPE_IDENTITY();
END;

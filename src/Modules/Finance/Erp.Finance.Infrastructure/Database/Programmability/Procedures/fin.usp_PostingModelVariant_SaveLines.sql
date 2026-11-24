CREATE OR ALTER PROCEDURE fin.usp_PostingModelVariant_SaveLines
    @VariantId   int,
    @Lines       nvarchar(max),
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Inlocuieste liniile unei variante (C2 3.2): JSON [{component, filterVatCategory, filterOperationTypeCode, side, sign, accountKey,
    -- defaultAccountCode, copyDimensions, groupBy, descriptionTemplate}], numerotate in ordine. Contul implicit e un cont frunza al planului
    -- romanesc (51844); RowVersion pe varianta (51842). Notele scrise raman cum au fost; recontarea le reface la cerere (C2 4.6).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    DECLARE @New TABLE (LineNumber int, Component varchar(40), FilterVatCategory nvarchar(4), FilterOperationTypeCode nvarchar(30), Side tinyint, Sign smallint,
        AccountKey varchar(40), DefaultCode nvarchar(20), DefaultAccountId int, CopyDimensions tinyint, GroupBy tinyint, DescriptionTemplate nvarchar(200));
    INSERT INTO @New
    SELECT CAST(x.[key] AS int) + 1, j.Component, NULLIF(j.FilterVatCategory, N''), NULLIF(j.FilterOperationTypeCode, N''), j.Side, ISNULL(j.Sign, 1), j.AccountKey,
        NULLIF(j.DefaultAccountCode, N''), a.Id, ISNULL(j.CopyDimensions, 63), ISNULL(j.GroupBy, 1), NULLIF(j.DescriptionTemplate, N'')
    FROM OPENJSON(@Lines) AS x
    CROSS APPLY OPENJSON(x.value) WITH (Component varchar(40) '$.component', FilterVatCategory nvarchar(4) '$.filterVatCategory',
        FilterOperationTypeCode nvarchar(30) '$.filterOperationTypeCode', Side tinyint '$.side', Sign smallint '$.sign', AccountKey varchar(40) '$.accountKey',
        DefaultAccountCode nvarchar(20) '$.defaultAccountCode', CopyDimensions tinyint '$.copyDimensions', GroupBy tinyint '$.groupBy',
        DescriptionTemplate nvarchar(200) '$.descriptionTemplate') AS j
    LEFT JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.CodeKey = UPPER(j.DefaultAccountCode) AND a.IsPostable = 1;

    DECLARE @Bad nvarchar(20) = (SELECT TOP (1) DefaultCode FROM @New WHERE DefaultCode IS NOT NULL AND DefaultAccountId IS NULL ORDER BY LineNumber);
    IF @Bad IS NOT NULL
    BEGIN
        DECLARE @Message nvarchar(200) = CONCAT(N'Contul implicit ', @Bad, N' nu există sau are analitice.');
        THROW 51844, @Message, 1;
    END;

    BEGIN TRANSACTION;
    UPDATE fin.PostingModelVariant SET ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @VariantId AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 51842, N'Varianta a fost modificată între timp sau nu există.', 1;
    DELETE FROM fin.PostingModelLine WHERE VariantId = @VariantId;
    INSERT INTO fin.PostingModelLine (VariantId, LineNumber, Component, FilterVatCategory, FilterOperationTypeCode, Side, Sign, AccountKey, DefaultAccountId,
        CopyDimensions, GroupBy, DescriptionTemplate)
    SELECT @VariantId, LineNumber, Component, FilterVatCategory, FilterOperationTypeCode, Side, Sign, AccountKey, DefaultAccountId, CopyDimensions, GroupBy,
        DescriptionTemplate
    FROM @New;
    COMMIT TRANSACTION;

    SELECT RowVersion FROM fin.PostingModelVariant WHERE Id = @VariantId;
END;

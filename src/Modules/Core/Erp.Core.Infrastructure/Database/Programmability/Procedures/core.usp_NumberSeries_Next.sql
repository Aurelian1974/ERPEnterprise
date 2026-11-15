CREATE OR ALTER PROCEDURE core.usp_NumberSeries_Next
    @CompanyId        int,
    @BranchId         int = NULL,
    @DocumentTypeCode nvarchar(50),
    @DocumentDate     date
AS
BEGIN
    -- Alocarea numarului la validare (02.01, 4.2; publica pentru module). Se apeleaza in tranzactia documentului:
    -- randul seriei ramane blocat pana la commit, deci validarile simultane se serializeaza (A2), iar un rollback anuleaza
    -- si incrementarea, fara goluri (A3). Seria implicita a punctului de lucru are prioritate fata de cea a firmei.
    -- Prima validare dintr-un an nou creeaza seria anului, cu acelasi prefix, daca seria anului anterior are AutoRollover.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @@TRANCOUNT = 0
        THROW 50028, N'core.usp_NumberSeries_Next se apelează doar în tranzacția documentului (4.2).', 1;

    DECLARE @Year smallint = YEAR(@DocumentDate);
    DECLARE @DocumentTypeId int = (SELECT Id FROM core.DocumentType WHERE Code = @DocumentTypeCode);
    DECLARE @SeriesId int;

    SELECT TOP (1) @SeriesId = s.Id
    FROM core.NumberSeries AS s WITH (UPDLOCK, HOLDLOCK)
    WHERE s.CompanyId = @CompanyId AND s.DocumentTypeId = @DocumentTypeId AND s.[Year] = @Year
      AND s.IsDefault = 1 AND s.ClosedAt IS NULL
      AND (s.BranchId = @BranchId OR s.BranchId IS NULL)
    ORDER BY CASE WHEN s.BranchId = @BranchId THEN 0 ELSE 1 END;

    IF @SeriesId IS NULL
    BEGIN
        INSERT INTO core.NumberSeries (CompanyId, BranchId, DocumentTypeId, Prefix, [Year], NextNumber, FirstNumber, Padding,
            IsFiscal, IsDefault, AutoRollover, CreatedBy)
        SELECT TOP (1) p.CompanyId, p.BranchId, p.DocumentTypeId, p.Prefix, @Year, 1, 1, p.Padding, p.IsFiscal, 1, 1, 1
        FROM core.NumberSeries AS p WITH (UPDLOCK, HOLDLOCK)
        WHERE p.CompanyId = @CompanyId AND p.DocumentTypeId = @DocumentTypeId AND p.[Year] = @Year - 1
          AND p.AutoRollover = 1 AND (p.IsDefault = 1 OR p.ClosedAt IS NOT NULL)
          AND (p.BranchId = @BranchId OR p.BranchId IS NULL)
          AND NOT EXISTS (SELECT 1 FROM core.NumberSeries AS n
                          WHERE n.CompanyId = p.CompanyId AND n.BranchId IS NOT DISTINCT FROM p.BranchId
                            AND n.DocumentTypeId = p.DocumentTypeId AND n.PrefixKey = p.PrefixKey AND n.[Year] = @Year)
        ORDER BY CASE WHEN p.BranchId = @BranchId THEN 0 ELSE 1 END, p.IsDefault DESC, p.Id DESC;

        IF @@ROWCOUNT = 0
            THROW 50001, N'Nu există o serie de numerotare implicită pentru acest tip de document și an. Definiți una în Configurare › Serii.', 1;

        SET @SeriesId = SCOPE_IDENTITY();
    END;

    UPDATE core.NumberSeries
    SET NextNumber = NextNumber + 1, LastUsedAt = SYSUTCDATETIME()
    OUTPUT inserted.Id AS SeriesId, inserted.Prefix, inserted.[Year], inserted.NextNumber - 1 AS Number, inserted.Padding
    WHERE Id = @SeriesId;
END;

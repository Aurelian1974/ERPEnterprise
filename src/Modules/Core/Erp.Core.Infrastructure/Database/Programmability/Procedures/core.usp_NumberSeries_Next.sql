CREATE OR ALTER PROCEDURE core.usp_NumberSeries_Next
    @CompanyId        int,
    @BranchId         int = NULL,
    @DocumentTypeCode nvarchar(50),
    @DocumentDate     date,
    @SeriesId         int = NULL,
    @UserId           int = NULL,
    @LocationId       int = NULL,
    @PartnerScope     tinyint = NULL
AS
BEGIN
    -- Alocarea numarului la validare (02.01, 4.2; publica pentru module). Se apeleaza in tranzactia documentului:
    -- randul seriei ramane blocat pana la commit, deci validarile simultane se serializeaza (A2), iar un rollback anuleaza
    -- si incrementarea, fara goluri (A3).
    -- Alegerea seriei (3.6 punctul 16): seria aleasa de utilizator (@SeriesId), altfel seria cu prefixul din prima regula potrivita
    -- (core.fn_NumberSeriesRuleMatch), altfel seria implicita, a punctului de lucru inaintea celei a firmei.
    -- Prima validare dintr-un an nou creeaza seria anului, cu acelasi prefix, daca seria anului anterior are AutoRollover.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @@TRANCOUNT = 0
        THROW 50028, N'core.usp_NumberSeries_Next se apelează doar în tranzacția documentului (4.2).', 1;

    DECLARE @Year smallint = YEAR(@DocumentDate);
    DECLARE @DocumentTypeId int = (SELECT Id FROM core.DocumentType WHERE Code = @DocumentTypeCode);
    DECLARE @Chosen int;
    DECLARE @TargetPrefix nvarchar(20);

    IF @SeriesId IS NOT NULL
    BEGIN
        SELECT @Chosen = s.Id
        FROM core.NumberSeries AS s WITH (UPDLOCK, HOLDLOCK)
        WHERE s.Id = @SeriesId AND s.CompanyId = @CompanyId AND s.DocumentTypeId = @DocumentTypeId AND s.[Year] = @Year
          AND s.ClosedAt IS NULL AND (s.BranchId IS NULL OR s.BranchId = @BranchId);
        IF @Chosen IS NULL
            THROW 50021, N'Seria aleasă nu se poate folosi: e închisă sau e a altui an, tip de document ori punct de lucru.', 1;
    END
    ELSE
    BEGIN
        SELECT @TargetPrefix = m.PrefixKey
        FROM core.fn_NumberSeriesRuleMatch(@CompanyId, @DocumentTypeId, @UserId, @BranchId, @LocationId, @PartnerScope) AS m;

        SELECT TOP (1) @Chosen = s.Id
        FROM core.NumberSeries AS s WITH (UPDLOCK, HOLDLOCK)
        WHERE s.CompanyId = @CompanyId AND s.DocumentTypeId = @DocumentTypeId AND s.[Year] = @Year AND s.ClosedAt IS NULL
          AND (s.BranchId = @BranchId OR s.BranchId IS NULL)
          AND ((@TargetPrefix IS NULL AND s.IsDefault = 1) OR s.PrefixKey = @TargetPrefix)
        ORDER BY CASE WHEN s.BranchId = @BranchId THEN 0 ELSE 1 END;
    END;

    IF @Chosen IS NULL
    BEGIN
        INSERT INTO core.NumberSeries (CompanyId, BranchId, DocumentTypeId, Prefix, [Year], NextNumber, FirstNumber, Padding,
            IsFiscal, IsDefault, AutoRollover, CreatedBy)
        SELECT TOP (1) p.CompanyId, p.BranchId, p.DocumentTypeId, p.Prefix, @Year, 1, 1, p.Padding, p.IsFiscal,
               CASE WHEN @TargetPrefix IS NULL THEN 1 ELSE 0 END, 1, 1
        FROM core.NumberSeries AS p WITH (UPDLOCK, HOLDLOCK)
        WHERE p.CompanyId = @CompanyId AND p.DocumentTypeId = @DocumentTypeId AND p.[Year] = @Year - 1 AND p.AutoRollover = 1
          AND ((@TargetPrefix IS NULL AND (p.IsDefault = 1 OR p.ClosedAt IS NOT NULL)) OR p.PrefixKey = @TargetPrefix)
          AND (p.BranchId = @BranchId OR p.BranchId IS NULL)
          AND NOT EXISTS (SELECT 1 FROM core.NumberSeries AS n
                          WHERE n.CompanyId = p.CompanyId AND n.BranchId IS NOT DISTINCT FROM p.BranchId
                            AND n.DocumentTypeId = p.DocumentTypeId AND n.PrefixKey = p.PrefixKey AND n.[Year] = @Year)
        ORDER BY CASE WHEN p.BranchId = @BranchId THEN 0 ELSE 1 END, p.IsDefault DESC, p.Id DESC;

        IF @@ROWCOUNT = 0
        BEGIN
            IF @TargetPrefix IS NOT NULL
            BEGIN
                DECLARE @Message nvarchar(300) = CONCAT(N'Regula de alegere cere seria ', @TargetPrefix,
                    N', dar ea nu există (sau e închisă) pentru acest an. Definiți-o în Configurare › Serii.');
                THROW 50001, @Message, 1;
            END;
            THROW 50001, N'Nu există o serie de numerotare implicită pentru acest tip de document și an. Definiți una în Configurare › Serii.', 1;
        END;

        SET @Chosen = SCOPE_IDENTITY();
    END;

    UPDATE core.NumberSeries
    SET NextNumber = NextNumber + 1, LastUsedAt = SYSUTCDATETIME()
    OUTPUT inserted.Id AS SeriesId, inserted.Prefix, inserted.[Year], inserted.NextNumber - 1 AS Number, inserted.Padding
    WHERE Id = @Chosen;
END;

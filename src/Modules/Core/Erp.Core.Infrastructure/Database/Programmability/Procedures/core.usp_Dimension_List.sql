CREATE OR ALTER PROCEDURE core.usp_Dimension_List
    @UserId          int,
    @DimensionType   varchar(20),
    @CompanyId       int = NULL,
    @Search          nvarchar(100) = NULL,
    @IncludeInactive bit = 0
AS
BEGIN
    -- Configurare › Dimensiuni (macheta 07k): doar firmele pe care utilizatorul are Core.Dimension.Manage (@CompanyId NULL = toate).
    -- Intoarce, in ordine: tipurile cu numarul de valori; valorile tipului ales (cu firma, parintele, responsabilul); firmele;
    -- utilizatorii activi (responsabili posibili).
    SET NOCOUNT ON;

    CREATE TABLE #Companies (Id int NOT NULL PRIMARY KEY);
    INSERT INTO #Companies (Id)
    SELECT f.CompanyId FROM core.fn_UserCompanies(@UserId, N'Core.Dimension.Manage') AS f
    WHERE @CompanyId IS NULL OR f.CompanyId = @CompanyId;

    SELECT t.Code, t.Name,
           (SELECT COUNT(*) FROM core.DimensionValue AS v JOIN #Companies AS c ON c.Id = v.CompanyId
            WHERE v.DimensionType = t.Code AND (@IncludeInactive = 1 OR v.IsActive = 1)) AS ValueCount
    FROM core.DimensionType AS t
    ORDER BY t.SortOrder;

    SELECT v.Id, v.CompanyId, co.Name AS CompanyName, v.DimensionType, v.Code, v.Name, v.ParentId, p.Code AS ParentCode, p.Name AS ParentName,
           v.ResponsibleUserId, u.DisplayName AS ResponsibleName, v.ValidFrom, v.ValidTo, v.IsActive,
           (SELECT COUNT(*) FROM core.DimensionValue AS ch WHERE ch.ParentId = v.Id AND ch.IsActive = 1) AS ActiveChildren,
           v.RowVersion
    FROM core.DimensionValue AS v
    JOIN #Companies AS c ON c.Id = v.CompanyId
    JOIN core.Company AS co ON co.Id = v.CompanyId
    LEFT JOIN core.DimensionValue AS p ON p.Id = v.ParentId
    LEFT JOIN core.[User] AS u ON u.Id = v.ResponsibleUserId
    WHERE v.DimensionType = @DimensionType
      AND (@IncludeInactive = 1 OR v.IsActive = 1)
      AND (@Search IS NULL OR v.CodeKey LIKE UPPER(@Search) + N'%' OR v.Name LIKE N'%' + @Search + N'%')
    ORDER BY co.Name, v.CodeKey;

    SELECT co.Id, co.Code, co.Name
    FROM core.Company AS co
    JOIN core.fn_UserCompanies(@UserId, N'Core.Dimension.Manage') AS f ON f.CompanyId = co.Id
    WHERE co.IsActive = 1
    ORDER BY co.Name;

    SELECT u.Id, u.DisplayName
    FROM core.[User] AS u
    WHERE u.IsActive = 1 AND u.Id <> 1
    ORDER BY u.NameSearch, u.Id;
END;

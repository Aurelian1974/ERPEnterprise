CREATE OR ALTER PROCEDURE fin.usp_MatchingRule_Reorder
    @CompanyId int,
    @RuleIds   nvarchar(max),
    @UserId    int
AS
BEGIN
    -- Noua ordine a regulilor de potrivire (02.02, F2 3.5; macheta f2-04, reordonarea prin tragere): @RuleIds e lista JSON a Id-urilor, in
    -- ordinea noua; prioritatile devin 10, 20, 30... Regulile sunt ale firmei (51021); cele nelistate isi pastreaza prioritatea.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISJSON(@RuleIds) <> 1
        THROW 51021, N'Lista regulilor nu este validă.', 1;

    DECLARE @Order TABLE (RuleId int PRIMARY KEY, Position int NOT NULL);
    INSERT INTO @Order (RuleId, Position)
    SELECT CAST(j.value AS int), CAST(j.[key] AS int) FROM OPENJSON(@RuleIds) AS j;

    IF EXISTS (SELECT 1 FROM @Order AS o LEFT JOIN fin.MatchingRule AS r ON r.Id = o.RuleId AND r.CompanyId = @CompanyId WHERE r.Id IS NULL)
        THROW 51021, N'O regulă din listă nu există în firma curentă.', 1;

    UPDATE r
    SET Priority = (o.Position + 1) * 10, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.MatchingRule AS r WITH (UPDLOCK)
    JOIN @Order AS o ON o.RuleId = r.Id;
END;

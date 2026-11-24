CREATE OR ALTER PROCEDURE fin.usp_GlAccount_SetActive
    @Id          int,
    @IsActive    bit,
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Activarea sau dezactivarea unui cont (C1 2, regula 9; 3.3): un cont nu se sterge. Nu se dezactiveaza un cont cu analitice active
    -- (51814); activarea cere parintele activ. Verificarea rulajului din anul deschis (51804) vine cu notele contabile (C2).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Chart int, @Parent int, @Version binary(8);
    SELECT @Chart = ChartId, @Parent = ParentId, @Version = RowVersion FROM fin.GlAccount WHERE Id = @Id;
    IF @Chart IS NULL
        THROW 51801, N'Contul nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51802, N'Contul a fost modificat de altcineva între timp.', 1;
    IF @IsActive = 0 AND EXISTS (SELECT 1 FROM fin.GlAccount WHERE ParentId = @Id AND IsActive = 1)
        THROW 51814, N'Contul are analitice active; dezactivați-le întâi.', 1;
    IF @IsActive = 1 AND EXISTS (SELECT 1 FROM fin.GlAccount WHERE Id = @Parent AND IsActive = 0)
        THROW 51814, N'Contul părinte e inactiv; activați-l întâi.', 1;

    BEGIN TRANSACTION;
    UPDATE fin.GlAccount SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    EXEC fin.usp_GlAccount_RefreshPostable @ChartId = @Chart;
    COMMIT TRANSACTION;

    SELECT RowVersion FROM fin.GlAccount WHERE Id = @Id;
END;

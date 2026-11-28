CREATE OR ALTER PROCEDURE fin.usp_IntercompanyPair_Save
    @ChartId          int,
    @Name             nvarchar(100),
    @FirstAccountId   int,
    @SecondAccountId  int,
    @UserId           int
AS
BEGIN
    -- O pereche de eliminare intra-grup (C5 3.7): primul cont e cel cu sold debitor (creanta, cheltuiala), al doilea cel cu sold creditor (datoria,
    -- venitul); amandoua se marcheaza pentru eliminare. Conturile trebuie sa fie ale planului alternativ (51980). Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF (SELECT COUNT(*) FROM fin.GlAccount WHERE Id IN (@FirstAccountId, @SecondAccountId) AND ChartId = @ChartId) <> 2
        THROW 51980, N'Conturile perechii trebuie să fie ale planului alternativ.', 1;
    BEGIN TRANSACTION;
    UPDATE fin.GlAccount SET EliminateIntercompany = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id IN (@FirstAccountId, @SecondAccountId);
    DECLARE @Id int = (SELECT Id FROM fin.IntercompanyPair WHERE ChartId = @ChartId AND FirstAccountId = @FirstAccountId AND SecondAccountId = @SecondAccountId);
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.IntercompanyPair (ChartId, Name, FirstAccountId, SecondAccountId, CreatedBy) VALUES (@ChartId, @Name, @FirstAccountId, @SecondAccountId, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE fin.IntercompanyPair SET Name = @Name WHERE Id = @Id;
    COMMIT;
    SELECT @Id;
END;

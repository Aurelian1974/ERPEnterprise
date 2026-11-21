CREATE OR ALTER PROCEDURE core.usp_DuplicateIgnore_Save
    @EntityType tinyint,
    @FirstId    int,
    @SecondId   int,
    @UserId     int
AS
BEGIN
    -- „Nu sunt dubluri” (02.01, 3.6 punctul 7): perechea nu se mai propune.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @A int = IIF(@FirstId < @SecondId, @FirstId, @SecondId), @B int = IIF(@FirstId < @SecondId, @SecondId, @FirstId);
    IF NOT EXISTS (SELECT 1 FROM core.DuplicateIgnore WHERE EntityType = @EntityType AND FirstId = @A AND SecondId = @B)
        INSERT INTO core.DuplicateIgnore (EntityType, FirstId, SecondId, CreatedBy) VALUES (@EntityType, @A, @B, @UserId);
END;

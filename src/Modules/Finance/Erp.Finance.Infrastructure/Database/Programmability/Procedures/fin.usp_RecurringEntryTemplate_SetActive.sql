CREATE OR ALTER PROCEDURE fin.usp_RecurringEntryTemplate_SetActive
    @CompanyId   int,
    @Id          int,
    @IsActive    bit,
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Opreste sau reporneste generarea unui sablon (C2 3.8); RowVersion (51842).
    SET NOCOUNT ON;

    UPDATE fin.RecurringEntryTemplate SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 51842, N'Șablonul a fost modificat între timp sau nu există.', 1;
END;

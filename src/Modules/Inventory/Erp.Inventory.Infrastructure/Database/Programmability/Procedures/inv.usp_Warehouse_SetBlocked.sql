CREATE OR ALTER PROCEDURE inv.usp_Warehouse_SetBlocked
    @CompanyId  int,
    @Id         int,
    @IsBlocked  bit,
    @Reason     nvarchar(250) = NULL,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Blocarea temporara a gestiunii (02.05, S1 4.3): manuala, cu motiv, sau din inventar (S6) si predare (4.2). @RowVersion NULL = apel intern
    -- (inventar, predare), fara verificarea versiunii. Gestiune inexistenta: 54001; versiune diferita: 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId)
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;

    UPDATE inv.Warehouse
    SET IsBlocked = @IsBlocked,
        BlockReason = CASE WHEN @IsBlocked = 1 THEN @Reason END,
        BlockedAt = CASE WHEN @IsBlocked = 1 THEN SYSUTCDATETIME() END,
        BlockedBy = CASE WHEN @IsBlocked = 1 THEN @UserId END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND (@RowVersion IS NULL OR RowVersion = @RowVersion);

    IF @@ROWCOUNT = 0
        THROW 54014, N'Gestiunea a fost modificată de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion, DimensionValueId, IsActive FROM inv.Warehouse WHERE Id = @Id;
END;

CREATE OR ALTER PROCEDURE inv.usp_Depot_SetBlocked
    @CompanyId  int,
    @Id         int,
    @IsBlocked  bit,
    @Reason     nvarchar(250) = NULL,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Blocarea depozitului (02.05, S1 3.8): blocheaza toate gestiunile din el pentru miscarile validate (verificat de S2). Coduri: 54014, 54023.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Depot WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId)
        THROW 54023, N'Depozitul nu există în firma curentă.', 1;

    UPDATE inv.Depot
    SET IsBlocked = @IsBlocked, BlockReason = CASE WHEN @IsBlocked = 1 THEN @Reason END, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54014, N'Depozitul a fost modificat de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion, DimensionValueId, IsActive FROM inv.Depot WHERE Id = @Id;
END;

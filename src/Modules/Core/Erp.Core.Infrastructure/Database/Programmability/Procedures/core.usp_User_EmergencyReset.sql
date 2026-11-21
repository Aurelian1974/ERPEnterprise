CREATE OR ALTER PROCEDURE core.usp_User_EmergencyReset
    @SubjectId     uniqueidentifier,
    @SetupVersion  nvarchar(20),
    @MachineName   nvarchar(128)
AS
BEGIN
    -- „Resetează accesul administratorului” (INSTALARE_ACTUALIZARE.md 11.3): utilizatorul redevine activ, primeste din nou rolul
    -- ADMIN la nivel de instalare daca il pierduse, iar resetarea ajunge in audit. Tranzactia e deschisa de installer (SP-03).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @UserId int, @AdminRoleId int;
    SELECT @UserId = Id FROM core.[User] WHERE SubjectId = @SubjectId;
    SELECT @AdminRoleId = Id FROM core.Role WHERE Code = N'ADMIN' AND IsSystem = 1;   -- codul exact (SQL-21)

    IF @UserId IS NULL
        THROW 50018, N'Utilizatorul administratorului nu există.', 1;
    IF @AdminRoleId IS NULL
        THROW 50016, N'Rolul de sistem ADMIN lipsește; rulați din nou datele livrate.', 1;

    UPDATE core.[User] SET IsActive = 1, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1 WHERE Id = @UserId AND IsActive = 0;

    IF NOT EXISTS (SELECT 1 FROM core.UserRoleAssignment WHERE UserId = @UserId AND RoleId = @AdminRoleId AND CompanyId IS NULL AND CompanyGroupId IS NULL)
        INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, DimensionScope, CreatedBy)
        VALUES (@UserId, @AdminRoleId, NULL /* toată instalarea */, NULL, 1);

    INSERT INTO core.AuditEntry (OccurredAt, UserId, CompanyId, Action, EntityType, EntityId, Changes, IpAddress, CorrelationId)
    VALUES (SYSUTCDATETIME(), 1, NULL, N'Core.User.EmergencyReset', N'Core.User', @UserId,
            (SELECT @SetupVersion AS setupVersion, @MachineName AS machine FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            NULL, NEWID());
END;

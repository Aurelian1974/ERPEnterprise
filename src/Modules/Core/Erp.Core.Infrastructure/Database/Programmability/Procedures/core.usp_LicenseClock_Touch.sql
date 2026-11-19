CREATE OR ALTER PROCEDURE core.usp_LicenseClock_Touch
    @Today  date,
    @UserId int
AS
BEGIN
    -- Data de referinta pentru expirarea licentei (08, 4.4, decizia D20): cea mai mare data vazuta de aplicatie, in
    -- core.Setting (License.HighestSeenDate). Doar creste. Intoarce valoarea dinainte si cea de dupa.
    -- Tranzactia e deschisa de apelant (SP-03).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Key varchar(100) = 'License.HighestSeenDate';
    DECLARE @Exists bit = 0, @Previous date;

    SELECT @Exists = 1, @Previous = TRY_CONVERT(date, s.Value, 23)
    FROM core.Setting AS s WITH (UPDLOCK, HOLDLOCK)
    WHERE s.Scope = 'Installation' AND s.ScopeId IS NULL AND s.[Key] = @Key;

    IF @Exists = 0
        INSERT INTO core.Setting (Scope, ScopeId, [Key], Value, CreatedBy)
        VALUES ('Installation', NULL, @Key, CONVERT(nvarchar(10), @Today, 23), @UserId);
    ELSE IF @Previous IS NULL OR @Today > @Previous
        UPDATE core.Setting
        SET Value = CONVERT(nvarchar(10), @Today, 23), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Scope = 'Installation' AND ScopeId IS NULL AND [Key] = @Key;

    SELECT @Previous AS PreviousHighest,
           CASE WHEN @Previous IS NULL OR @Today > @Previous THEN @Today ELSE @Previous END AS Highest;
END;

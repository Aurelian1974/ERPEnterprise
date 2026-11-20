CREATE OR ALTER PROCEDURE core.usp_Setting_Get
    @Scope   varchar(20),
    @ScopeId int = NULL,
    @Key     varchar(100)
AS
BEGIN
    -- O setare (02.01, 3.5) cu versiunea ei, pentru ecranul Configurare › Setari si pentru serviciile care o folosesc.
    -- Fara rand: setarea nu exista inca (aplicatia foloseste valoarea implicita).
    SET NOCOUNT ON;

    SELECT Value, RowVersion
    FROM core.Setting
    WHERE Scope = @Scope AND ScopeId IS NOT DISTINCT FROM @ScopeId AND [Key] = @Key;
END;

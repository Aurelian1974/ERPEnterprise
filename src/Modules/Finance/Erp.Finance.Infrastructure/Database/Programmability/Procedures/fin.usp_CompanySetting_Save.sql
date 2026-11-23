CREATE OR ALTER PROCEDURE fin.usp_CompanySetting_Save
    @CompanyId int,
    @Code      varchar(60),
    @Value     nvarchar(200),
    @UserId    int
AS
BEGIN
    -- O setare a Finantelor pe firma (02.02, F2: patru ochi, validarea automata, toleranta si zilele decontarii de card), inlocuita la salvare.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    MERGE fin.CompanySetting WITH (HOLDLOCK) AS target
    USING (SELECT @CompanyId AS CompanyId, @Code AS Code) AS source
    ON target.CompanyId = source.CompanyId AND target.Code = source.Code
    WHEN MATCHED THEN UPDATE SET Value = @Value, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHEN NOT MATCHED THEN INSERT (CompanyId, Code, Value, ModifiedBy) VALUES (@CompanyId, @Code, @Value, @UserId);
END;

CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_SaveFile
    @CompanyId  int,
    @Id         int,
    @Kind       tinyint,
    @FileName   nvarchar(260),
    @Content    varbinary(max),
    @UserId     int
AS
BEGIN
    -- Un fisier al declaratiei (C4 3.3): fisierul generat inlocuieste generarea anterioara; recipisa se adauga.
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM fin.TaxReturn WHERE Id = @Id AND CompanyId = @CompanyId)
        THROW 51941, N'Declarația nu există.', 1;
    IF @Kind IN (1, 2, 3)
        DELETE FROM fin.TaxReturnFile WHERE ReturnId = @Id AND Kind = @Kind;
    INSERT INTO fin.TaxReturnFile (ReturnId, Kind, FileName, Content, CreatedBy) VALUES (@Id, @Kind, @FileName, @Content, @UserId);
END;

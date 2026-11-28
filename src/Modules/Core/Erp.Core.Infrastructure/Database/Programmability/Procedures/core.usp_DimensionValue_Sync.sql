CREATE OR ALTER PROCEDURE core.usp_DimensionValue_Sync
    @Id       int,
    @Code     nvarchar(30),
    @Name     nvarchar(200),
    @IsActive bit,
    @UserId   int
AS
BEGIN
    -- Valoarea detinuta de un modul (IDimensionLookup.SyncAsync, 02.01, 6): Stocurile isi aliniaza gestiunea (cod, denumire, stare; 02.05 S1 4.4),
    -- in tranzactia comenzii modulului. Valoare inexistenta: 50027. Codul altei valori de acelasi tip din firma: 50018.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CompanyId int, @DimensionType varchar(20);
    SELECT @CompanyId = CompanyId, @DimensionType = DimensionType FROM core.DimensionValue WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @CompanyId IS NULL
        THROW 50027, N'Valoarea de dimensiune nu există.', 1;
    IF EXISTS (SELECT 1 FROM core.DimensionValue WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND DimensionType = @DimensionType AND CodeKey = UPPER(@Code) AND Id <> @Id)
        THROW 50018, N'Codul există deja la altă valoare a dimensiunii.', 1;

    UPDATE core.DimensionValue
    SET Code = @Code, Name = @Name, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND (Code <> @Code OR Name <> @Name OR IsActive <> @IsActive);
END;

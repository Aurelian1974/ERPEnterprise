CREATE OR ALTER PROCEDURE core.usp_OwnerAccess_Check
    @EntityType      varchar(10),
    @Id              int = NULL,
    @CheckNewOwner   bit = 0,
    @OwnerCompanyId  int = NULL,
    @OwnerBranchId   int = NULL,
    @OwnerLocationId int = NULL,
    @Permission      nvarchar(100),
    @Manage          bit,
    @UserId          int,
    @CompanyId       int
AS
BEGIN
    -- Accesul pe proprietarul unui articol sau partener (02.01, 3.2-3.4; deciziile din 21.11.2026). Cu @Id: inregistrarea existenta
    -- trebuie sa fie vizibila (altfel 50027, ca si cum n-ar exista) si, la modificare, in nodul utilizatorului sau dedesubt (50037).
    -- Cu @CheckNewOwner: proprietarul ales (NULL = comun) e in firma curenta, iar utilizatorul are dreptul pe el (50037).
    -- Un articol sau partener comun se modifica doar cu dreptul pe toata firma.
    SET NOCOUNT ON;

    DECLARE @Exists bit = 0, @C int, @B int, @L int;
    IF @Id IS NOT NULL
    BEGIN
        IF @EntityType = 'Item'
            SELECT @Exists = 1, @C = OwnerCompanyId, @B = OwnerBranchId, @L = OwnerLocationId FROM core.Item WHERE Id = @Id;
        ELSE IF @EntityType = 'Partner'
            SELECT @Exists = 1, @C = OwnerCompanyId, @B = OwnerBranchId, @L = OwnerLocationId FROM core.Partner WHERE Id = @Id;
        ELSE
            THROW 50021, N'Tip de entitate necunoscut.', 1;

        IF @Exists = 0 OR NOT EXISTS (SELECT 1 FROM core.fn_UserOwnerAccess(@UserId, @CompanyId, @Permission, 0, @C, @B, @L))
            THROW 50027, N'Înregistrarea nu există sau nu aveți acces la ea.', 1;
        IF @Manage = 1 AND NOT EXISTS (SELECT 1 FROM core.fn_UserOwnerAccess(@UserId, @CompanyId, @Permission, 1, @C, @B, @L))
            THROW 50037, N'Înregistrarea aparține altui nod (comună, firmă, punct de lucru sau locație) decât cele pe care aveți drept de modificare.', 1;
    END;

    IF @CheckNewOwner = 1
    BEGIN
        IF @OwnerCompanyId IS NOT NULL AND @OwnerCompanyId <> @CompanyId
            THROW 50037, N'Proprietarul trebuie să fie firma curentă, un punct de lucru sau o locație a ei.', 1;
        IF NOT EXISTS (SELECT 1 FROM core.fn_UserOwnerAccess(@UserId, @CompanyId, @Permission, 1, @OwnerCompanyId, @OwnerBranchId, @OwnerLocationId))
            THROW 50037, N'Nu aveți drept de adăugare sau modificare pe nodul ales; alegeți unul dintre nodurile dumneavoastră.', 1;
    END;
END;

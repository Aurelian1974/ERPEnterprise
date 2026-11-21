CREATE OR ALTER FUNCTION core.fn_UserOwnerAccess
(
    @UserId          int,
    @CompanyId       int,
    @Permission      nvarchar(100),
    @Manage          bit,
    @OwnerCompanyId  int,
    @OwnerBranchId   int,
    @OwnerLocationId int
)
RETURNS TABLE
AS
RETURN
    -- Un rand daca utilizatorul are acces, pe firma curenta, la o inregistrare cu proprietarul dat (articol, partener; 21.11.2026).
    -- Vizualizare (@Manage = 0): comunele, cele ale firmei si ale nodurilor de deasupra lui (punctul de lucru al locatiei lui), plus
    -- tot ce e in nodul lui si dedesubt. Modificare (@Manage = 1): doar nodul lui si dedesubt; comunele cer dreptul pe toata firma.
    SELECT 1 AS Allowed
    WHERE EXISTS (
        SELECT 1
        FROM core.fn_UserNodeAccess(@UserId, @CompanyId, @Permission) AS n
        LEFT JOIN core.Location AS ol ON ol.Id = @OwnerLocationId
        WHERE (@OwnerCompanyId IS NULL AND (@Manage = 0 OR n.WholeCompany = 1))
           OR (@OwnerCompanyId = @CompanyId AND (
                  n.WholeCompany = 1
               OR (@Manage = 0 AND @OwnerBranchId IS NULL AND @OwnerLocationId IS NULL)
               OR (@OwnerBranchId IS NOT NULL AND (n.BranchId = @OwnerBranchId OR (@Manage = 0 AND n.LocationBranchId = @OwnerBranchId)))
               OR (@OwnerLocationId IS NOT NULL AND (n.LocationId = @OwnerLocationId OR n.BranchId = ol.BranchId)))));

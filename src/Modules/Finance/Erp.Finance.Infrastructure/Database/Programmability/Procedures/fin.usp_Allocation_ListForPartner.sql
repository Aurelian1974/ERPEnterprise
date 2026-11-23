CREATE OR ALTER PROCEDURE fin.usp_Allocation_ListForPartner
    @CompanyId int,
    @PartnerId int,
    @Role      tinyint = NULL
AS
BEGIN
    -- Alocarile unui partener (F3 3.3; tab-ul „Alocari” din f3-02 si „Desfa o alocare” din f3-03), cele mai noi intai, cel mult 500: partidele
    -- legate, suma, data, sursa, starea si motivul desfacerii.
    SET NOCOUNT ON;

    SELECT TOP (500) a.Id, d.Role, a.AllocationDate, a.Source, a.Status, a.DebitAmount AS Amount, d.CurrencyCode, a.DebitOpenItemId, d.DocumentNo AS DebitDocumentNo,
        d.Kind AS DebitKind, a.CreditOpenItemId, c.DocumentNo AS CreditDocumentNo, c.Kind AS CreditKind, a.UndoneAt, a.UndoReason, a.CreatedAt,
        u.DisplayName AS CreatedByName
    FROM fin.Allocation AS a
    JOIN fin.OpenItem AS d ON d.Id = a.DebitOpenItemId
    JOIN fin.OpenItem AS c ON c.Id = a.CreditOpenItemId
    LEFT JOIN core.[User] AS u ON u.Id = a.CreatedBy
    WHERE a.CompanyId = @CompanyId AND (d.PartnerId = @PartnerId OR c.PartnerId = @PartnerId) AND (@Role IS NULL OR d.Role = @Role)
    ORDER BY a.Id DESC;
END;

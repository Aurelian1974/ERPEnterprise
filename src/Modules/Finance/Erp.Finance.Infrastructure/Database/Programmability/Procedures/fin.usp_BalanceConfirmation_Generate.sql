CREATE OR ALTER PROCEDURE fin.usp_BalanceConfirmation_Generate
    @CompanyId   int,
    @Number      nvarchar(40),
    @AsOf        date,
    @Role        tinyint = NULL,
    @MinBalance  decimal(19,2),
    @PartnerIds  nvarchar(max) = NULL,
    @UserId      int
AS
BEGIN
    -- Un lot de confirmari de sold (02.02, F3 3.9, 4.6 pasul 1), in tranzactia apelantului: pentru fiecare partener si rol (optional doar
    -- partenerii dati) cu soldul in lei la @AsOf, in valoare absoluta, de cel putin @MinBalance, o confirmare numerotata Lot/nnnn. PDF-urile
    -- le genereaza apelantul. Intoarce lotul si confirmarile lui (fin.usp_BalanceConfirmation_Get).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO fin.BalanceConfirmationBatch (CompanyId, Number, AsOfDate, Role, MinBalance, CreatedBy)
    VALUES (@CompanyId, @Number, @AsOf, @Role, @MinBalance, @UserId);
    DECLARE @BatchId int = SCOPE_IDENTITY();

    WITH Items AS (
        SELECT o.PartnerId, o.Role, ROUND((o.Amount - ISNULL(d.Allocated, 0) + ISNULL(c.Allocated, 0)) * o.ExchangeRate, 2) AS OpenRon
        FROM fin.OpenItem AS o
        OUTER APPLY (SELECT SUM(a.DebitAmount) AS Allocated FROM fin.Allocation AS a
                     WHERE a.DebitOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS d
        OUTER APPLY (SELECT SUM(a.CreditAmount) AS Allocated FROM fin.Allocation AS a
                     WHERE a.CreditOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS c
        WHERE o.CompanyId = @CompanyId AND o.DocumentDate <= @AsOf AND (@Role IS NULL OR o.Role = @Role)
          AND (@PartnerIds IS NULL OR o.PartnerId IN (SELECT CAST(j.value AS int) FROM OPENJSON(@PartnerIds) AS j))),
    Balances AS (
        SELECT PartnerId, Role, SUM(OpenRon) AS Balance FROM Items GROUP BY PartnerId, Role HAVING ABS(SUM(OpenRon)) >= @MinBalance AND SUM(OpenRon) <> 0)
    INSERT INTO fin.BalanceConfirmation (CompanyId, BatchId, PartnerId, Role, AsOfDate, Number, OurBalance)
    SELECT @CompanyId, @BatchId, b.PartnerId, b.Role, @AsOf,
        CONCAT(@Number, N'/', RIGHT(CONCAT(N'0000', ROW_NUMBER() OVER (ORDER BY p.Name, b.Role)), 4)), b.Balance
    FROM Balances AS b JOIN core.Partner AS p ON p.Id = b.PartnerId;

    SELECT @BatchId AS BatchId;
    EXEC fin.usp_BalanceConfirmation_Get @CompanyId = @CompanyId, @BatchId = @BatchId;
END;

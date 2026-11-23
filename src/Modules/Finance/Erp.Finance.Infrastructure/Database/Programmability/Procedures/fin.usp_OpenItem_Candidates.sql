CREATE OR ALTER PROCEDURE fin.usp_OpenItem_Candidates
    @CompanyId    int,
    @PartnerId    int,
    @Role         tinyint,
    @CurrencyCode char(3),
    @Side         tinyint,
    @Ids          nvarchar(max) = NULL
AS
BEGIN
    -- Partidele deschise ale partenerului pe o parte (1 debit, 2 credit), in moneda data, blocate pentru alocare (02.02, F3 4.1, 4.2; SP-16:
    -- blocarea in ordinea Id-urilor), intoarse in ordinea FIFO: scadenta, data documentului, Id. @Ids (lista JSON) restrange la partidele date
    -- (facturile indicate), oricare ar fi moneda sau partea, ca apelantul sa poata explica refuzul. SpvExempt: furnizor din afara Romaniei
    -- sau persoana fizica (F3 3.8).
    SET NOCOUNT ON;

    DECLARE @Locked TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Locked (Id)
    SELECT o.Id
    FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.CompanyId = @CompanyId AND o.PartnerId = @PartnerId AND o.Role = @Role
      AND ((@Ids IS NULL AND o.OpenAmount <> 0 AND o.CurrencyCode = @CurrencyCode AND ((@Side = 1 AND o.Amount > 0) OR (@Side = 2 AND o.Amount < 0)))
           OR (@Ids IS NOT NULL AND o.Id IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@Ids) AS j)))
    ORDER BY o.Id;

    SELECT o.Id, o.Kind, o.DocumentNo, o.DocumentDate, o.DueDate, o.CurrencyCode, o.Amount, o.OpenAmount, o.IsDisputed, o.IsBlockedForPayment,
        o.EInvoiceStatus, CAST(CASE WHEN p.CountryCode <> 'RO' OR p.PartnerType = 4 THEN 1 ELSE 0 END AS bit) AS SpvExempt
    FROM @Locked AS l
    JOIN fin.OpenItem AS o ON o.Id = l.Id
    JOIN core.Partner AS p ON p.Id = o.PartnerId
    ORDER BY o.DueDate, o.DocumentDate, o.Id;
END;

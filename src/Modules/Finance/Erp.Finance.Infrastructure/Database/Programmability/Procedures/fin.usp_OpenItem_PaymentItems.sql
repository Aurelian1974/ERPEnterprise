CREATE OR ALTER PROCEDURE fin.usp_OpenItem_PaymentItems
    @CompanyId          int,
    @SourceDocumentType varchar(50),
    @SourceDocumentId   bigint,
    @SourceLineKey      nvarchar(50) = NULL
AS
BEGIN
    -- Partidele unui document de plata (02.02, F3 4.2 pasul 6), pentru stornare: partida (blocata), daca a fost deja stornata (are o partida
    -- inversa) si alocarile ei active. @SourceLineKey NULL = toate partile documentului; partidele inverse ('...#S') nu se intorc.
    SET NOCOUNT ON;

    DECLARE @Items TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Items (Id)
    SELECT o.Id FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.CompanyId = @CompanyId AND o.SourceDocumentType = @SourceDocumentType AND o.SourceDocumentId = @SourceDocumentId
      AND (@SourceLineKey IS NULL OR o.SourceLineKey = @SourceLineKey) AND o.ReversalOfId IS NULL
    ORDER BY o.Id;

    SELECT o.Id, o.PartnerId, o.Role, o.Kind, o.SourceModule, o.SourceLineKey, o.DocumentNo, o.DocumentDate, o.AccountingDate, o.CurrencyCode,
        o.ExchangeRate, o.Amount, o.AmountRon,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.OpenItem AS r WHERE r.ReversalOfId = o.Id) THEN 1 ELSE 0 END AS bit) AS IsReversed
    FROM @Items AS i JOIN fin.OpenItem AS o ON o.Id = i.Id
    ORDER BY o.Id;

    SELECT a.Id, a.DebitOpenItemId, a.CreditOpenItemId, a.AllocationDate, a.Source
    FROM fin.Allocation AS a
    WHERE a.Status = 1 AND (a.DebitOpenItemId IN (SELECT Id FROM @Items) OR a.CreditOpenItemId IN (SELECT Id FROM @Items))
    ORDER BY a.Id;
END;

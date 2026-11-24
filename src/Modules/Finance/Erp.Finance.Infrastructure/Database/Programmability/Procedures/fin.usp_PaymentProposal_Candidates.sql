CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_Candidates
    @CompanyId           int,
    @DueUntil            date,
    @PartnerIds          nvarchar(max) = NULL,
    @Currencies          nvarchar(max) = NULL,
    @MaxPriority         tinyint = NULL,
    @ExcludeProposalId   int = NULL
AS
BEGIN
    -- Partidele de furnizor de propus la plata (F5 4.1 pasii 2–3): datoriile deschise scadente pana la @DueUntil, fara cele in litigiu, blocate
    -- la plata, ale furnizorilor blocati si cele aflate deja intr-o propunere activa (alta decat @ExcludeProposalId); restul pozitiv, prioritatea
    -- si contul preferat din profil, contul bancar al furnizorului in moneda facturii, inactivitatea ANAF si starea e-Factura. Ordinea: prioritate,
    -- scadenta (FIFO). Partea acoperita de OP-uri validate e deja scazuta din rest (F3).
    SET NOCOUNT ON;

    SELECT o.Id AS OpenItemId, o.PartnerId, p.Name AS PartnerName, o.DocumentNo, o.DueDate, o.CurrencyCode, -o.OpenAmount AS OpenAmount, o.ExchangeRate,
        ISNULL(pf.PaymentPriority, 3) AS PaymentPriority, pf.PreferredTreasuryAccountId, CAST(ISNULL(p.AnafInactive, 0) AS bit) AS AnafInactive, o.EInvoiceStatus,
        CAST(CASE WHEN p.CountryCode <> 'RO' OR p.PartnerType = 4 THEN 1 ELSE 0 END AS bit) AS SpvExempt,
        (SELECT TOP (1) b.Id FROM core.PartnerBankAccount AS b WHERE b.PartnerId = o.PartnerId AND b.IsActive = 1 AND b.Currency = o.CurrencyCode
         ORDER BY b.IsDefault DESC, b.Id) AS PartnerBankAccountId
    FROM fin.OpenItem AS o
    JOIN core.Partner AS p ON p.Id = o.PartnerId
    LEFT JOIN fin.PartnerFinanceProfile AS pf ON pf.CompanyId = o.CompanyId AND pf.PartnerId = o.PartnerId AND pf.Role = o.Role
    WHERE o.CompanyId = @CompanyId AND o.Role = 2 AND o.Amount < 0 AND o.OpenAmount < 0 AND o.Kind IN (1, 4, 6) AND o.DueDate <= @DueUntil
      AND o.IsDisputed = 0 AND o.IsBlockedForPayment = 0 AND ISNULL(pf.IsPaymentBlocked, 0) = 0
      AND (@MaxPriority IS NULL OR ISNULL(pf.PaymentPriority, 3) <= @MaxPriority)
      AND (@PartnerIds IS NULL OR o.PartnerId IN (SELECT CAST(j.value AS int) FROM OPENJSON(@PartnerIds) AS j))
      AND (@Currencies IS NULL OR o.CurrencyCode IN (SELECT CAST(j.value AS char(3)) FROM OPENJSON(@Currencies) AS j))
      AND NOT EXISTS (SELECT 1 FROM fin.PaymentProposalLine AS l
                      WHERE l.OpenItemId = o.Id AND l.IsActive = 1 AND (@ExcludeProposalId IS NULL OR l.PaymentProposalId <> @ExcludeProposalId))
    ORDER BY ISNULL(pf.PaymentPriority, 3), o.DueDate, o.DocumentDate, o.Id;
END;

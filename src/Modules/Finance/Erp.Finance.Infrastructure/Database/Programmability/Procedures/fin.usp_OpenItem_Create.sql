CREATE OR ALTER PROCEDURE fin.usp_OpenItem_Create
    @CompanyId          int,
    @PartnerId          int,
    @Role               tinyint,
    @Kind               tinyint,
    @SourceModule       varchar(20),
    @SourceDocumentType varchar(50),
    @SourceDocumentId   bigint,
    @SourceLineKey      nvarchar(50),
    @DocumentNo         nvarchar(50),
    @DocumentDate       date,
    @AccountingDate     date,
    @DueDate            date,
    @CurrencyCode       char(3),
    @ExchangeRate       decimal(19,6),
    @Amount             decimal(19,2),
    @AmountRon          decimal(19,2),
    @VatAmount          decimal(19,2),
    @VatAmountRon       decimal(19,2),
    @IsVatOnCollection  bit,
    @GlAccountCode      nvarchar(20) = NULL,
    @EInvoiceStatus     tinyint,
    @EInvoiceIndexNo    varchar(30) = NULL,
    @CostCenterId       int = NULL,
    @ProjectId          int = NULL,
    @ReversalOfId       bigint = NULL,
    @Schedules          fin.OpenItemScheduleList READONLY,
    @UserId             int
AS
BEGIN
    -- Partida deschisa a unui document (02.02, F3 3.1, 4.1, 4.2), idempotenta pe documentul sursa: a doua livrare intoarce partida existenta
    -- (Created = 0). Creeaza profilul financiar al partenerului la prima partida (3.2). Partenerul si moneda exista (51021); scadentele, daca
    -- exista, dau exact valoarea (51021). Intoarce partida si modul de alocare automata al partenerului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id bigint, @Created bit = 0;
    SELECT @Id = Id FROM fin.OpenItem WITH (UPDLOCK, HOLDLOCK)
    WHERE SourceDocumentType = @SourceDocumentType AND SourceDocumentId = @SourceDocumentId AND SourceLineKey = @SourceLineKey AND Role = @Role;

    IF @Id IS NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
            THROW 51021, N'Partenerul nu există.', 1;
        IF NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @CurrencyCode)
            THROW 51021, N'Moneda nu există.', 1;
        IF EXISTS (SELECT 1 FROM @Schedules) AND (SELECT SUM(Amount) FROM @Schedules) <> @Amount
            THROW 51021, N'Scadențele nu dau valoarea documentului.', 1;

        INSERT INTO fin.OpenItem (CompanyId, PartnerId, Role, Kind, SourceModule, SourceDocumentType, SourceDocumentId, SourceLineKey, DocumentNo,
            DocumentDate, AccountingDate, DueDate, CurrencyCode, ExchangeRate, Amount, AmountRon, OpenAmount, VatAmount, VatAmountRon, IsVatOnCollection,
            GlAccountCode, EInvoiceStatus, EInvoiceIndexNo, CostCenterId, ProjectId, ReversalOfId, CreatedBy)
        VALUES (@CompanyId, @PartnerId, @Role, @Kind, @SourceModule, @SourceDocumentType, @SourceDocumentId, @SourceLineKey, @DocumentNo,
            @DocumentDate, @AccountingDate, @DueDate, @CurrencyCode, @ExchangeRate, @Amount, @AmountRon, @Amount, @VatAmount, @VatAmountRon, @IsVatOnCollection,
            @GlAccountCode, @EInvoiceStatus, @EInvoiceIndexNo, @CostCenterId, @ProjectId, @ReversalOfId, @UserId);
        SET @Id = SCOPE_IDENTITY();
        SET @Created = 1;

        INSERT INTO fin.OpenItemSchedule (OpenItemId, DueDate, Amount, OpenAmount)
        SELECT @Id, s.DueDate, s.Amount, s.Amount FROM @Schedules AS s
        WHERE (SELECT COUNT(*) FROM @Schedules) > 1;

        INSERT INTO fin.PartnerFinanceProfile (CompanyId, PartnerId, Role, CreatedBy)
        SELECT @CompanyId, @PartnerId, @Role, @UserId
        WHERE NOT EXISTS (SELECT 1 FROM fin.PartnerFinanceProfile WITH (UPDLOCK, HOLDLOCK)
                          WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND Role = @Role);
    END

    SELECT o.Id, @Created AS Created, ISNULL(p.AutoAllocationMode, 2) AS AutoAllocationMode, ISNULL(p.IsPaymentBlocked, 0) AS IsPartnerPaymentBlocked
    FROM fin.OpenItem AS o
    LEFT JOIN fin.PartnerFinanceProfile AS p ON p.CompanyId = o.CompanyId AND p.PartnerId = o.PartnerId AND p.Role = o.Role
    WHERE o.Id = @Id;
END;

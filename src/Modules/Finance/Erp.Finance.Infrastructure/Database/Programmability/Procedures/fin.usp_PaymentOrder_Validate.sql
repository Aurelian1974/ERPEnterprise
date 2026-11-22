CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_Validate
    @Id                    int,
    @CompanyId             int,
    @Number                nvarchar(30),
    @AnafInactiveConfirmed bit,
    @RowVersion            binary(8),
    @UserId                int
AS
BEGIN
    -- Validarea unui OP la legarea de obiectul platii (02.02, F2 3.6 regula 2), in tranzactia apelantului (SP-03): liniile acopera suma si
    -- fiecare are obiectul ei (factura la plata furnizorului, partenerul la avans, angajatul la salarii, obligatia la buget; 51221); plata
    -- catre buget are CUI-ul platitorului si referinta obligatiei (51216); IBAN-ul exista (51215). Primeste numarul (alocat de handler),
    -- devine Neconfirmat si imutabil.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @OrderCompanyId int, @Version binary(8), @Amount decimal(19,2), @Purpose tinyint, @PartnerId int, @EmployeeId int,
            @FiscalCode varchar(20), @BudgetRef nvarchar(50), @Iban varchar(34);
    SELECT @Status = Status, @OrderCompanyId = CompanyId, @Version = RowVersion, @Amount = Amount, @Purpose = Purpose, @PartnerId = PartnerId,
           @EmployeeId = EmployeeId, @FiscalCode = BeneficiaryFiscalCode, @BudgetRef = BudgetObligationRef, @Iban = BeneficiaryIban
    FROM fin.PaymentOrder WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;

    IF @Status IS NULL OR @OrderCompanyId <> @CompanyId
        THROW 51225, N'Ordinul de plată nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Ordinul de plată a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 51212, N'Ordinul de plată este deja validat.', 1;
    IF ISNULL((SELECT SUM(Amount) FROM fin.PaymentOrderLine WHERE PaymentOrderId = @Id), 0) <> @Amount
        THROW 51221, N'Liniile ordinului de plată nu acoperă suma lui.', 1;
    IF EXISTS (SELECT 1 FROM fin.PaymentOrderLine AS l JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
               WHERE l.PaymentOrderId = @Id
                 AND ((t.SettlementKind = 2 AND l.SettlementRef IS NULL) OR (t.RequiresPartner = 1 AND @PartnerId IS NULL)
                      OR ((t.RequiresEmployee = 1 OR t.Code = N'BANK-PLATA-SALARII') AND @EmployeeId IS NULL AND l.SettlementRef IS NULL)))
        THROW 51221, N'O linie nu are obiectul plății: factura, partenerul sau angajatul (ori lotul de salarii).', 1;
    IF (@Purpose = 3 OR EXISTS (SELECT 1 FROM fin.PaymentOrderLine AS l JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
                                WHERE l.PaymentOrderId = @Id AND t.Code = N'BANK-PLATA-BUGET'))
       AND (@FiscalCode IS NULL OR @BudgetRef IS NULL)
        THROW 51216, N'Plata către buget cere CUI-ul plătitorului și referința obligației.', 1;
    IF @Iban IS NULL
        THROW 51215, N'Lipsește IBAN-ul beneficiarului.', 1;

    UPDATE fin.PaymentOrder
    SET Number = @Number, Status = 2, ConfirmationStatus = 1, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId,
        AnafInactiveConfirmed = @AnafInactiveConfirmed, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT RowVersion FROM fin.PaymentOrder WHERE Id = @Id;
END;

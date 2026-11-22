CREATE OR ALTER PROCEDURE fin.usp_CashDocument_Validate
    @CashDocumentId int,
    @CompanyId      int,
    @Number         nvarchar(30) = NULL,
    @RowVersion     binary(8),
    @UserId         int
AS
BEGIN
    -- Validarea unui document de casa (02.02, F1 4.1 pasul 4; tranzactia e a pipeline-ului, SP-03). Ordinea blocarilor: documentul, contul
    -- de trezorerie (serializeaza validarile pe aceeasi casa, SP-16), apoi zilele. Actualizeaza registrul zilei documentului si soldul de
    -- inceput al zilelor deschise ulterioare; refuza ziua inchisa (51004), soldul negativ in orice zi de la data documentului (51005) si
    -- soldul initial dupa alte documente (51017). Numarul vine din serie (C#); soldul initial nu are numar.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Kind tinyint, @AccountId int, @Day date, @Amount decimal(19,2), @CurrentVersion binary(8), @DocCompanyId int,
            @DayId int, @DayStatus tinyint, @ReversalOfId int;

    SELECT @Status = d.Status, @Kind = d.Kind, @AccountId = d.TreasuryAccountId, @Day = d.DocumentDate, @Amount = d.TotalAmount,
           @CurrentVersion = d.RowVersion, @DocCompanyId = d.CompanyId, @ReversalOfId = d.ReversalOfId
    FROM fin.CashDocument AS d WITH (UPDLOCK, ROWLOCK)
    WHERE d.Id = @CashDocumentId;

    IF @Status IS NULL OR @DocCompanyId <> @CompanyId
        THROW 51001, N'Documentul de casă nu există.', 1;
    IF @CurrentVersion <> @RowVersion
        THROW 51002, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 51003, N'Doar o ciornă poate fi validată.', 1;
    IF @Kind <> 4 AND @Number IS NULL
        THROW 51021, N'Documentul de casă primește numărul din serie la validare.', 1;

    -- Serializare pe casa (SP-16): orice validare pe aceeasi casa asteapta aici.
    UPDATE fin.TreasuryAccount SET ModifiedAt = ModifiedAt WHERE Id = @AccountId;

    IF @Kind = 4 AND @ReversalOfId IS NULL AND EXISTS (SELECT 1 FROM fin.CashDocument
                                                      WHERE TreasuryAccountId = @AccountId AND Status IN (2, 3) AND Id <> @CashDocumentId)
        THROW 51017, N'Soldul inițial se introduce o singură dată, înaintea oricărui alt document al casei.', 1;
    IF @Kind <> 4 AND EXISTS (SELECT 1 FROM fin.CashDocument AS o
                              WHERE o.TreasuryAccountId = @AccountId AND o.Kind = 4 AND o.Status = 2 AND o.ReversalOfId IS NULL
                                AND o.DocumentDate > @Day)
        THROW 51004, N'Documentul este datat înaintea soldului inițial al casei.', 1;

    SELECT @DayId = Id, @DayStatus = Status
    FROM fin.CashRegisterDay WITH (UPDLOCK, ROWLOCK)
    WHERE TreasuryAccountId = @AccountId AND [Day] = @Day;

    IF @DayStatus = 2
        THROW 51004, N'Ziua de casă este închisă.', 1;
    IF EXISTS (SELECT 1 FROM fin.CashRegisterDay WITH (UPDLOCK) WHERE TreasuryAccountId = @AccountId AND [Day] > @Day AND Status = 2)
        THROW 51004, N'O zi ulterioară a casei este închisă; documentul nu mai poate fi datat în această zi.', 1;

    IF @DayId IS NULL
    BEGIN
        INSERT INTO fin.CashRegisterDay (TreasuryAccountId, [Day], OpeningBalance, TotalIn, TotalOut, Status, CloseMode, CreatedBy)
        SELECT @AccountId, @Day,
               ISNULL((SELECT TOP (1) p.ClosingBalance FROM fin.CashRegisterDay AS p
                       WHERE p.TreasuryAccountId = @AccountId AND p.[Day] < @Day ORDER BY p.[Day] DESC), 0),
               0, 0, 1, a.CloseMode, @UserId
        FROM fin.TreasuryAccount AS a WHERE a.Id = @AccountId;
        SET @DayId = SCOPE_IDENTITY();
    END;

    DECLARE @In decimal(19,2) = CASE WHEN @Kind IN (1, 2, 4) THEN @Amount ELSE 0 END;
    DECLARE @Out decimal(19,2) = CASE WHEN @Kind = 3 THEN @Amount ELSE 0 END;

    UPDATE fin.CashRegisterDay
    SET TotalIn  = TotalIn  + CASE WHEN Id = @DayId THEN @In  ELSE 0 END,
        TotalOut = TotalOut + CASE WHEN Id = @DayId THEN @Out ELSE 0 END,
        OpeningBalance = OpeningBalance + CASE WHEN Id = @DayId THEN 0 ELSE @In - @Out END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE TreasuryAccountId = @AccountId AND [Day] >= @Day;

    DECLARE @NegativeDay date, @NegativeBalance decimal(19,2);
    SELECT TOP (1) @NegativeDay = [Day], @NegativeBalance = ClosingBalance
    FROM fin.CashRegisterDay
    WHERE TreasuryAccountId = @AccountId AND [Day] >= @Day AND ClosingBalance < 0
    ORDER BY [Day];
    IF @NegativeDay IS NOT NULL
    BEGIN
        DECLARE @Message nvarchar(400) = CONCAT(N'Soldul casei ar deveni negativ în ziua ', FORMAT(@NegativeDay, 'dd.MM.yyyy'), N': ',
            FORMAT(@NegativeBalance, 'N2', 'ro-RO'), N'.');
        THROW 51005, @Message, 1;
    END;

    UPDATE fin.CashDocument
    SET Status = 2, Number = @Number, CashRegisterDayId = @DayId, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @CashDocumentId;

    SELECT d.Id AS CashDocumentId, d.Number, r.ClosingBalance AS DayClosingBalance, d.RowVersion
    FROM fin.CashDocument AS d
    JOIN fin.CashRegisterDay AS r ON r.Id = d.CashRegisterDayId
    WHERE d.Id = @CashDocumentId;
END;

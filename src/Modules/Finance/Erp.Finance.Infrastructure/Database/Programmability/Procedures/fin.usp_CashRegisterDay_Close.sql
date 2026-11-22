CREATE OR ALTER PROCEDURE fin.usp_CashRegisterDay_Close
    @TreasuryAccountId int,
    @CompanyId         int,
    @Day               date,
    @UserId            int
AS
BEGIN
    -- Inchiderea zilei de casa (02.02, F1 4.2), la cerere sau din jobul de la miezul noptii. Conditii: ziua anterioara cu activitate e
    -- inchisa (51012), nu exista ciorne datate in ziua respectiva (51013), ziua nu e deja inchisa (51004). O zi fara activitate se poate
    -- inchide explicit (randul se creeaza cu soldul zilei anterioare). Paginile registrului se numeroteaza continuu pe casa si an.
    -- Avertizarea de sold (plafonul SoldCasa si plafonul propriu) se calculeaza in C# din valorile intoarse.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId AND Kind = 2)
        THROW 51023, N'Casa nu există în firma curentă.', 1;

    -- Aceeasi ordine de blocare ca validarea (SP-16): contul, apoi zilele.
    UPDATE fin.TreasuryAccount SET ModifiedAt = ModifiedAt WHERE Id = @TreasuryAccountId;

    DECLARE @DayId int, @Status tinyint;
    SELECT @DayId = Id, @Status = Status FROM fin.CashRegisterDay WITH (UPDLOCK, ROWLOCK)
    WHERE TreasuryAccountId = @TreasuryAccountId AND [Day] = @Day;

    IF @Status = 2
        THROW 51004, N'Ziua de casă este deja închisă.', 1;
    IF EXISTS (SELECT 1 FROM fin.CashRegisterDay WHERE TreasuryAccountId = @TreasuryAccountId AND [Day] < @Day AND Status = 1)
        THROW 51012, N'O zi anterioară a casei este încă deschisă; închideți zilele în ordine.', 1;
    IF EXISTS (SELECT 1 FROM fin.CashDocument WHERE TreasuryAccountId = @TreasuryAccountId AND DocumentDate = @Day AND Status = 1)
        THROW 51013, N'Ziua are ciorne; validați-le, mutați-le în altă zi sau ștergeți-le înainte de închidere.', 1;

    IF @DayId IS NULL
    BEGIN
        INSERT INTO fin.CashRegisterDay (TreasuryAccountId, [Day], OpeningBalance, TotalIn, TotalOut, Status, CloseMode, CreatedBy)
        SELECT @TreasuryAccountId, @Day,
               ISNULL((SELECT TOP (1) p.ClosingBalance FROM fin.CashRegisterDay AS p
                       WHERE p.TreasuryAccountId = @TreasuryAccountId AND p.[Day] < @Day ORDER BY p.[Day] DESC), 0),
               0, 0, 1, a.CloseMode, @UserId
        FROM fin.TreasuryAccount AS a WHERE a.Id = @TreasuryAccountId;
        SET @DayId = SCOPE_IDENTITY();
    END;

    IF EXISTS (SELECT 1 FROM fin.CashRegisterDay WHERE Id = @DayId AND ClosingBalance < 0)
        THROW 51005, N'Soldul casei este negativ; ziua nu se poate închide.', 1;

    DECLARE @Documents int = (SELECT COUNT(*) FROM fin.CashDocument WHERE TreasuryAccountId = @TreasuryAccountId AND DocumentDate = @Day AND Status <> 1);
    DECLARE @Pages int = CASE WHEN @Documents <= 25 THEN 1 ELSE (@Documents + 24) / 25 END;
    DECLARE @NextPage int = ISNULL((SELECT MAX(LastPageNo) FROM fin.CashRegisterDay
                                    WHERE TreasuryAccountId = @TreasuryAccountId AND YEAR([Day]) = YEAR(@Day) AND Id <> @DayId), 0) + 1;

    UPDATE fin.CashRegisterDay
    SET Status = 2, ClosedAt = SYSUTCDATETIME(), ClosedBy = @UserId,
        FirstPageNo = ISNULL(FirstPageNo, @NextPage),
        LastPageNo = CASE WHEN FirstPageNo IS NULL THEN @NextPage + @Pages - 1
                          ELSE CASE WHEN LastPageNo > FirstPageNo + @Pages - 1 THEN LastPageNo ELSE FirstPageNo + @Pages - 1 END END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @DayId;

    SELECT r.Id AS CashRegisterDayId, r.[Day], r.OpeningBalance, r.TotalIn, r.TotalOut, r.ClosingBalance, r.FirstPageNo, r.LastPageNo,
           a.MaxClosingBalance,
           (SELECT TOP (1) c.Amount FROM fin.CashLimitRule AS c
            WHERE c.Category = 'SoldCasa' AND c.CurrencyCode = 'RON' AND (c.CompanyId IS NULL OR c.CompanyId = @CompanyId)
              AND c.ValidFrom <= @Day AND (c.ValidTo IS NULL OR c.ValidTo >= @Day)
            ORDER BY c.Amount) AS LegalLimit,
           a.CurrencyCode
    FROM fin.CashRegisterDay AS r JOIN fin.TreasuryAccount AS a ON a.Id = r.TreasuryAccountId
    WHERE r.Id = @DayId;
END;

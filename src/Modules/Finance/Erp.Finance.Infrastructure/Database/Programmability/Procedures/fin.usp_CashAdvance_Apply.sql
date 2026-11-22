CREATE OR ALTER PROCEDURE fin.usp_CashAdvance_Apply
    @CashDocumentId int,
    @UserId         int
AS
BEGIN
    -- Efectul unui document de casa validat asupra avansului lui (02.02, F1 4.4; apelat din fin.usp_CashDocument_Validate, deci si la
    -- stornari, cu sume negative): plata avansului il trece in Acordat (stornarea ei, in Stornat); restituirea si diferenta de plata din
    -- decont modifica soldul. Dupa decontul validat, avansul e Decontat cand soldul ajunge la zero, altfel Decontat partial.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AdvanceId int = (SELECT CashAdvanceId FROM fin.CashDocument WHERE Id = @CashDocumentId);
    IF @AdvanceId IS NULL
        RETURN;

    DECLARE @Granted decimal(19,2), @Returned decimal(19,2), @Paid decimal(19,2);
    SELECT @Granted = SUM(CASE WHEN t.Code = N'PLATA-AVANS-DECONTARE' THEN l.Amount ELSE 0 END),
           @Returned = SUM(CASE WHEN t.Code = N'INC-RESTITUIRE-AVANS' THEN l.Amount ELSE 0 END),
           @Paid = SUM(CASE WHEN t.Code = N'PLATA-DIFERENTA-DECONT' THEN l.Amount ELSE 0 END)
    FROM fin.CashDocumentLine AS l JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
    WHERE l.CashDocumentId = @CashDocumentId;

    UPDATE fin.CashAdvance WITH (UPDLOCK)
    SET Status = CASE WHEN @Granted > 0 AND Status = 0 THEN 1 WHEN @Granted < 0 THEN 5 ELSE Status END,
        GrantedByCashDocumentId = CASE WHEN @Granted > 0 THEN @CashDocumentId ELSE GrantedByCashDocumentId END,
        ReturnedAmount = ReturnedAmount + ISNULL(@Returned, 0),
        PaidDifference = PaidDifference + ISNULL(@Paid, 0),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @AdvanceId;

    EXEC fin.usp_CashAdvance_RefreshStatus @CashAdvanceId = @AdvanceId, @UserId = @UserId;
END;

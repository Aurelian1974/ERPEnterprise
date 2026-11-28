CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_MarkReview
    @CompanyId  int,
    @From       date,
    @To         date,
    @Reopened   bit
AS
BEGIN
    -- Redeschiderea unei luni (C4 4.5, C4-A11): declaratiile depuse care o cuprind devin „De verificat” si intorc perioadele lor; la reinchidere,
    -- se intorc cele marcate, ca aplicatia sa compare amprenta datelor si sa stearga marcajul cand nu s-a schimbat nimic.
    SET NOCOUNT ON;
    IF @Reopened = 1
        UPDATE fin.TaxReturn SET NeedsReview = 1 WHERE CompanyId = @CompanyId AND Status = 4 AND PeriodFrom <= @To AND PeriodTo >= @From;
    SELECT Id, FormCode, PeriodFrom, PeriodTo, CalculationHash FROM fin.TaxReturn
    WHERE CompanyId = @CompanyId AND Status = 4 AND NeedsReview = 1 AND PeriodFrom <= @To AND PeriodTo >= @From;
END;

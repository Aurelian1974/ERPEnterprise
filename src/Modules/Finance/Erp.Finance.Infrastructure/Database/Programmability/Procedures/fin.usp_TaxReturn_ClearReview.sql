CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_ClearReview
    @CompanyId  int,
    @Id         int
AS
BEGIN
    -- Sterge marcajul „De verificat” cand datele declaratiei nu s-au schimbat (C4-A11).
    SET NOCOUNT ON;
    UPDATE fin.TaxReturn SET NeedsReview = 0 WHERE Id = @Id AND CompanyId = @CompanyId;
END;

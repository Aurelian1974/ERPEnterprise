CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_ApproveFromProposal
    @CompanyId   int,
    @Ids         nvarchar(max),
    @ApproverId  int
AS
BEGIN
    -- OP-urile generate dintr-o propunere aprobata sunt considerate aprobate pentru patru ochi de ultimul ei aprobator (F5 2 regula 5), cand
    -- acesta nu e cel care le genereaza (verificat de apelant).
    SET NOCOUNT ON;

    UPDATE fin.PaymentOrder SET ApprovedAt = SYSUTCDATETIME(), ApprovedBy = @ApproverId
    WHERE CompanyId = @CompanyId AND Id IN (SELECT CAST(j.value AS int) FROM OPENJSON(@Ids) AS j) AND ApprovedAt IS NULL;
END;

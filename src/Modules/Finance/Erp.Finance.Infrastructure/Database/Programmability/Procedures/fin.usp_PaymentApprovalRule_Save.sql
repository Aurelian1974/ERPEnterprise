CREATE OR ALTER PROCEDURE fin.usp_PaymentApprovalRule_Save
    @CompanyId  int,
    @Rules      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Regulile de aprobare a platilor ale firmei (F5 3.2), inlocuite in bloc: JSON [{level, thresholdRon, basis, approverRoleId}], niveluri 1..n
    -- consecutive, cu praguri crescatoare (51021). Regulile vechi raman inactive (istoric). Propunerile aflate in aprobare isi pastreaza nivelul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @New TABLE (Level tinyint PRIMARY KEY, ThresholdRon decimal(19,2), Basis tinyint, ApproverRoleId int);
    INSERT INTO @New (Level, ThresholdRon, Basis, ApproverRoleId)
    SELECT Level, ThresholdRon, Basis, ApproverRoleId
    FROM OPENJSON(@Rules) WITH (Level tinyint '$.level', ThresholdRon decimal(19,2) '$.thresholdRon', Basis tinyint '$.basis', ApproverRoleId int '$.approverRoleId');

    IF EXISTS (SELECT 1 FROM @New AS n WHERE n.Level <> (SELECT COUNT(*) FROM @New AS x WHERE x.Level <= n.Level))
       OR EXISTS (SELECT 1 FROM @New AS a JOIN @New AS b ON b.Level = a.Level + 1 WHERE b.ThresholdRon <= a.ThresholdRon)
       OR EXISTS (SELECT 1 FROM @New AS n WHERE NOT EXISTS (SELECT 1 FROM core.Role AS r WHERE r.Id = n.ApproverRoleId))
        THROW 51021, N'Nivelurile sunt 1, 2, 3, consecutive, cu praguri crescătoare și cu un rol aprobator existent.', 1;

    UPDATE fin.PaymentApprovalRule SET IsActive = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE CompanyId = @CompanyId AND IsActive = 1;
    INSERT INTO fin.PaymentApprovalRule (CompanyId, Level, ThresholdRon, Basis, ApproverRoleId, CreatedBy)
    SELECT @CompanyId, Level, ThresholdRon, Basis, ApproverRoleId, @UserId FROM @New;
END;

CREATE OR ALTER PROCEDURE fin.usp_CashFlowAlertRule_Save
    @CompanyId  int,
    @Rules      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Regulile de alerta ale firmei (F5 3.7), salvate in bloc: JSON [{id, treasuryAccountId, minBalanceRon, horizonDays, notifyRoleId,
    -- isActive}]. Cele existente se modifica (ziua ultimei alerte ramane), cele noi se adauga, cele lipsa se sterg. Contul e al firmei,
    -- rolul exista (51021).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @New TABLE (Id int NULL, TreasuryAccountId int NULL, MinBalanceRon decimal(19,2), HorizonDays smallint, NotifyRoleId int NULL, IsActive bit);
    INSERT INTO @New (Id, TreasuryAccountId, MinBalanceRon, HorizonDays, NotifyRoleId, IsActive)
    SELECT Id, TreasuryAccountId, MinBalanceRon, HorizonDays, NotifyRoleId, IsActive
    FROM OPENJSON(@Rules) WITH (Id int '$.id', TreasuryAccountId int '$.treasuryAccountId', MinBalanceRon decimal(19,2) '$.minBalanceRon',
        HorizonDays smallint '$.horizonDays', NotifyRoleId int '$.notifyRoleId', IsActive bit '$.isActive');

    IF EXISTS (SELECT 1 FROM @New AS n WHERE n.TreasuryAccountId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount AS a WHERE a.Id = n.TreasuryAccountId AND a.CompanyId = @CompanyId))
       OR EXISTS (SELECT 1 FROM @New AS n WHERE n.NotifyRoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Role AS r WHERE r.Id = n.NotifyRoleId))
       OR EXISTS (SELECT 1 FROM @New AS n WHERE n.Id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM fin.CashFlowAlertRule AS r WHERE r.Id = n.Id AND r.CompanyId = @CompanyId))
        THROW 51021, N'Contul, rolul sau regula nu există.', 1;

    BEGIN TRANSACTION;
    DELETE r FROM fin.CashFlowAlertRule AS r
    WHERE r.CompanyId = @CompanyId AND NOT EXISTS (SELECT 1 FROM @New AS n WHERE n.Id = r.Id);

    UPDATE r SET TreasuryAccountId = n.TreasuryAccountId, MinBalanceRon = n.MinBalanceRon, HorizonDays = n.HorizonDays, NotifyRoleId = n.NotifyRoleId,
        IsActive = n.IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.CashFlowAlertRule AS r
    JOIN @New AS n ON n.Id = r.Id;

    INSERT INTO fin.CashFlowAlertRule (CompanyId, TreasuryAccountId, MinBalanceRon, HorizonDays, NotifyRoleId, IsActive, CreatedBy)
    SELECT @CompanyId, TreasuryAccountId, MinBalanceRon, HorizonDays, NotifyRoleId, IsActive, @UserId FROM @New WHERE Id IS NULL;
    COMMIT TRANSACTION;
END;

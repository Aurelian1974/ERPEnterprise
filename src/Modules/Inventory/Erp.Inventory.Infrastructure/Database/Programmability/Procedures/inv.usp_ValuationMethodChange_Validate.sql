CREATE OR ALTER PROCEDURE inv.usp_ValuationMethodChange_Validate
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @Number     nvarchar(40),
    @Today      date,
    @UserId     int
AS
BEGIN
    -- Validarea documentului SM (02.05, S1 4.5), in tranzactia apelantului: documentul blocat, ciorna, cu versiunea vazuta; data cel mai devreme
    -- prima zi a lunii curente (54011); metoda veche pe fiecare linie, metoda noua pe model (randul de setari creat daca lipseste). Intoarce
    -- liniile (pentru eveniment) si cheile de stoc ale modelelor, de refacut de apelant cu motorul de cost (conversia straturilor la data).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8), @NewMethod tinyint, @Date date;
    SELECT @Status = Status, @Version = RowVersion, @NewMethod = NewMethod, @Date = EffectiveDate
    FROM inv.ValuationMethodChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 54118, N'Documentul nu mai este ciornă.', 1;
    IF @Date < DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1)
        THROW 54011, N'Schimbarea metodei se aplică cel mai devreme de la începutul lunii curente.', 1;
    IF @NewMethod = 4 AND EXISTS (SELECT 1 FROM inv.ValuationMethodChangeLine AS l
                                  LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = l.ItemId
                                  WHERE l.ChangeId = @Id AND ISNULL(s.TrackingMode, 0) = 0)
        THROW 54015, N'Identificarea directă cere urmărirea pe lot sau pe serie.', 1;

    INSERT INTO inv.ItemStockSettings (CompanyId, ItemId, ValuationMethod, CreatedBy)
    SELECT @CompanyId, l.ItemId, ss.DefaultValuationMethod, @UserId
    FROM inv.ValuationMethodChangeLine AS l
    JOIN inv.StockSettings AS ss ON ss.CompanyId = @CompanyId
    WHERE l.ChangeId = @Id AND NOT EXISTS (SELECT 1 FROM inv.ItemStockSettings AS s WITH (UPDLOCK, HOLDLOCK) WHERE s.CompanyId = @CompanyId AND s.ItemId = l.ItemId);

    UPDATE l SET OldMethod = s.ValuationMethod
    FROM inv.ValuationMethodChangeLine AS l
    JOIN inv.ItemStockSettings AS s WITH (UPDLOCK) ON s.CompanyId = @CompanyId AND s.ItemId = l.ItemId
    WHERE l.ChangeId = @Id;

    UPDATE s SET ValuationMethod = @NewMethod, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM inv.ItemStockSettings AS s
    JOIN inv.ValuationMethodChangeLine AS l ON l.ItemId = s.ItemId AND l.ChangeId = @Id
    WHERE s.CompanyId = @CompanyId;

    UPDATE inv.ValuationMethodChange
    SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT Id, RowVersion FROM inv.ValuationMethodChange WHERE Id = @Id;
    SELECT ItemId, OldMethod FROM inv.ValuationMethodChangeLine WHERE ChangeId = @Id;
    SELECT DISTINCT k.WarehouseId, k.DepotId, k.ItemVariantId, k.LotId
    FROM inv.Stock AS k
    JOIN core.ItemVariant AS v ON v.Id = k.ItemVariantId
    JOIN inv.ValuationMethodChangeLine AS l ON l.ItemId = v.ItemId AND l.ChangeId = @Id
    JOIN inv.Warehouse AS w ON w.Id = k.WarehouseId AND w.CompanyId = @CompanyId;
END;

CREATE OR ALTER PROCEDURE inv.usp_StockDocument_Save
    @Id              int = NULL,
    @CompanyId       int,
    @DocumentType    varchar(2),
    @DocumentDate    date,
    @AccountingDate  date,
    @FromDepotId     int = NULL,
    @FromWarehouseId int = NULL,
    @ToDepotId       int = NULL,
    @ToWarehouseId   int = NULL,
    @ReasonId        int = NULL,
    @PartnerId       int = NULL,
    @CostCenterId    int = NULL,
    @ProjectId       int = NULL,
    @EmployeeId      int = NULL,
    @Notes           nvarchar(1000) = NULL,
    @Lines           inv.tt_StockDocumentLine READONLY,
    @RowVersion      binary(8) = NULL,
    @UserId          int
AS
BEGIN
    -- Ciorna unui document de stoc (02.05, S2 3.3, 3.5–3.9): gestiunile dupa tip (NI, PF, CI intra; ND, BC, CE ies; NP are ambele), motivul la
    -- incarcare si descarcare (54110), centrul de cost la bonul de consum (54115), custodia doar pe gestiunile de custodie (54116), niciun document
    -- direct pe tranzit (54108), liniile cu cantitate pozitiva (54104) pe variante stocabile (54105), costul introdus unde e cerut (54119).
    -- Descarcarea peste pragul motivului (valoarea estimata la costul mediu curent) trece in „De aprobat” (S2 3.6). Liniile inlocuiesc liniile
    -- ciornei. Coduri: 54001, 54117 versiune, 54118 documentul nu e ciorna.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @CurrentStatus tinyint, @CurrentType varchar(2);
        SELECT @CurrentStatus = Status, @CurrentType = DocumentType FROM inv.StockDocument WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @CurrentStatus IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @CurrentStatus <> 1 OR @CurrentType <> @DocumentType
            THROW 54118, N'Doar ciornele se modifică, fără schimbarea tipului.', 1;
    END;

    DECLARE @In bit = CASE WHEN @DocumentType IN ('NI', 'PF', 'CI') THEN 1 ELSE 0 END,
            @Out bit = CASE WHEN @DocumentType IN ('ND', 'BC', 'CE') THEN 1 ELSE 0 END;
    IF @In = 1 SELECT @FromWarehouseId = NULL, @FromDepotId = NULL;
    IF @Out = 1 SELECT @ToWarehouseId = NULL, @ToDepotId = NULL;
    IF (@In = 1 AND @ToWarehouseId IS NULL) OR (@Out = 1 AND @FromWarehouseId IS NULL)
       OR (@DocumentType IN ('NP', 'NR') AND (@FromWarehouseId IS NULL OR @ToWarehouseId IS NULL))
        THROW 54001, N'Alegeți gestiunea documentului.', 1;
    IF EXISTS (SELECT 1 FROM (VALUES (@FromWarehouseId), (@ToWarehouseId)) AS v (Id)
               WHERE v.Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.Warehouse AS w WHERE w.Id = v.Id AND w.CompanyId = @CompanyId AND w.IsActive = 1))
        THROW 54001, N'Gestiunea nu există în firmă sau este inactivă.', 1;
    IF @DocumentType <> 'NR' AND EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id IN (@FromWarehouseId, @ToWarehouseId) AND Kind = 3)
        THROW 54108, N'Pe gestiunea de tranzit nu se fac documente directe.', 1;
    IF (@DocumentType IN ('CI', 'CE') AND EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id IN (@FromWarehouseId, @ToWarehouseId) AND Kind <> 4))
       OR (@DocumentType IN ('NI', 'ND', 'BC', 'PF') AND EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id IN (@FromWarehouseId, @ToWarehouseId) AND Kind = 4))
        THROW 54116, N'Custodia are doar intrări și ieșiri de custodie, pe gestiunile de custodie.', 1;
    IF @DocumentType IN ('NI', 'ND') AND NOT EXISTS (SELECT 1 FROM inv.MovementReason WHERE Id = @ReasonId AND IsActive = 1
        AND (CompanyId IS NULL OR CompanyId = @CompanyId) AND Direction = CASE WHEN @DocumentType = 'NI' THEN 1 ELSE -1 END)
        THROW 54110, N'Alegeți motivul încărcării sau al descărcării, de direcția potrivită.', 1;
    IF @DocumentType NOT IN ('NI', 'ND') SET @ReasonId = NULL;
    IF @DocumentType = 'BC' AND NOT EXISTS (SELECT 1 FROM core.DimensionValue
        WHERE Id = @CostCenterId AND CompanyId = @CompanyId AND DimensionType = 'COST_CENTER' AND IsActive = 1)
        THROW 54115, N'Bonul de consum cere un centru de cost activ al firmei.', 1;
    IF @EmployeeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @EmployeeId AND CompanyId = @CompanyId)
        THROW 54018, N'Angajatul nu aparține firmei.', 1;

    IF NOT EXISTS (SELECT 1 FROM @Lines) OR EXISTS (SELECT 1 FROM @Lines WHERE Qty <= 0 OR UomFactor <= 0)
        THROW 54104, N'Documentul are cel puțin o linie, cu cantitate pozitivă.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l LEFT JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId LEFT JOIN core.Item AS i ON i.Id = v.ItemId
               WHERE v.Id IS NULL OR v.IsActive = 0 OR i.IsStockable = 0)
        THROW 54105, N'O linie are o variantă inactivă sau un articol nestocabil.', 1;
    IF @DocumentType IN ('PF', 'CI') AND EXISTS (SELECT 1 FROM @Lines WHERE UnitCost IS NULL)
        THROW 54119, N'Linia cere costul (costul standard la predarea produselor, valoarea declarată de proprietar la custodie).', 1;
    IF @DocumentType = 'NI' AND EXISTS (SELECT 1 FROM inv.MovementReason WHERE Id = @ReasonId AND CostSource = 1)
       AND EXISTS (SELECT 1 FROM @Lines WHERE UnitCost IS NULL)
        THROW 54119, N'Motivul încărcării cere costul pe fiecare linie.', 1;

    -- Aprobarea descarcarii peste pragul motivului: valoarea estimata la costul mediu curent al gestiunii (S2 3.6).
    DECLARE @Approval tinyint = 0;
    IF @DocumentType = 'ND'
    BEGIN
        DECLARE @Threshold decimal(19,2) = (SELECT ApprovalThreshold FROM inv.MovementReason WHERE Id = @ReasonId);
        DECLARE @Estimate decimal(19,2) = (
            SELECT ISNULL(SUM(ROUND(l.Qty * l.UomFactor * ISNULL(c.UnitCost, 0), 2)), 0)
            FROM @Lines AS l
            OUTER APPLY (SELECT CASE WHEN SUM(s.QtyOnHand) > 0 THEN SUM(s.CostOnHand) / SUM(s.QtyOnHand) ELSE MAX(s.LastUnitCost) END AS UnitCost
                         FROM inv.Stock AS s WHERE s.WarehouseId = ISNULL(l.FromWarehouseId, @FromWarehouseId) AND s.ItemVariantId = l.ItemVariantId) AS c);
        IF @Threshold IS NOT NULL AND @Estimate >= @Threshold
            SET @Approval = 1;
    END;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.StockDocument (CompanyId, DocumentType, DocumentDate, AccountingDate, FromDepotId, FromWarehouseId, ToDepotId, ToWarehouseId,
            ReasonId, PartnerId, CostCenterId, ProjectId, EmployeeId, ApprovalStatus, Notes, CreatedBy)
        VALUES (@CompanyId, @DocumentType, @DocumentDate, @AccountingDate, @FromDepotId, @FromWarehouseId, @ToDepotId, @ToWarehouseId,
            @ReasonId, @PartnerId, @CostCenterId, @ProjectId, @EmployeeId, @Approval, @Notes, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE inv.StockDocument
        SET DocumentDate = @DocumentDate, AccountingDate = @AccountingDate, FromDepotId = @FromDepotId, FromWarehouseId = @FromWarehouseId,
            ToDepotId = @ToDepotId, ToWarehouseId = @ToWarehouseId, ReasonId = @ReasonId, PartnerId = @PartnerId, CostCenterId = @CostCenterId,
            ProjectId = @ProjectId, EmployeeId = @EmployeeId, Notes = @Notes, ApprovalStatus = @Approval, ApprovalNote = NULL, ApprovedBy = NULL,
            ApprovedAt = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        DELETE FROM inv.StockDocumentLine WHERE DocumentId = @Id;
    END;

    INSERT INTO inv.StockDocumentLine (DocumentId, LineNumber, ItemVariantId, ItemNameSnapshot, UomId, Qty, UomFactor, QtyBase, UnitCost, FromDepotId,
        FromWarehouseId, ToDepotId, ToWarehouseId, CostCenterId, ProjectId)
    SELECT @Id, l.LineNumber, l.ItemVariantId, v.Name, l.UomId, l.Qty, l.UomFactor, ROUND(l.Qty * l.UomFactor, 6), l.UnitCost,
        CASE WHEN @In = 1 THEN NULL ELSE l.FromDepotId END, CASE WHEN @In = 1 THEN NULL ELSE l.FromWarehouseId END,
        CASE WHEN @Out = 1 THEN NULL ELSE l.ToDepotId END, CASE WHEN @Out = 1 THEN NULL ELSE l.ToWarehouseId END, l.CostCenterId, l.ProjectId
    FROM @Lines AS l
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId;

    SELECT Id, RowVersion FROM inv.StockDocument WHERE Id = @Id;
END;

CREATE OR ALTER PROCEDURE inv.usp_Serial_ResolveForMovements
    @CompanyId      int,
    @IsExternalFact bit = 0,
    @Lines          inv.tt_SerialRequest READONLY,
    @Identifiers    inv.tt_SerialIdentifierValue READONLY,
    @UserId         int
AS
BEGIN
    -- Seriile unui apel al portului de stoc (02.05, S4 3.4), in tranzactia apelantului, inaintea loturilor si a costului. Pentru modelele urmarite
    -- pe serie (3, 4), fiecare linie e o bucata (±1):
    --   intrarea (citire la intrare): seria noua se creeaza in stoc; o serie vanduta revine doar prin retur de la client (54462); o serie deja in
    --   stoc sau casata e refuzata (54463, 54462), cu exceptia unei serii scoase prin stornarea intrarii ei;
    --   iesirea: seria trebuie sa fie in stoc in gestiunea sursa (54461); la citirea doar la iesire, seria se creeaza acum (IsCapturedAtIssue);
    --   identificatorii: cei obligatorii dupa regula modelului sau a grupei (54473), unicitatea pe tip (54472), tipurile cunoscute (54471).
    -- Intoarce seria fiecarei linii si lotul ei (la urmarirea pe serie si lot). Starea finala a seriei o scrie inv.usp_StockMovement_Post.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Rows TABLE (
        RowNo int PRIMARY KEY, ItemVariantId int, ItemId int, ItemGroupId int, Tracking tinyint, CaptureMode tinyint, MovementTypeCode varchar(30) COLLATE DATABASE_DEFAULT,
        WarehouseId int, DepotId int, QtyBase decimal(19,6), SerialId int, SerialNo nvarchar(60) COLLATE DATABASE_DEFAULT, Kind tinyint, IsNew bit NOT NULL DEFAULT (0));
    INSERT INTO @Rows (RowNo, ItemVariantId, ItemId, ItemGroupId, Tracking, CaptureMode, MovementTypeCode, WarehouseId, DepotId, QtyBase, SerialId, SerialNo, Kind)
    SELECT l.RowNo, l.ItemVariantId, i.Id, i.ItemGroupId, ISNULL(s.TrackingMode, 0), ISNULL(s.SerialCaptureMode, 1), l.MovementTypeCode, l.WarehouseId, l.DepotId,
        l.QtyBase, l.SerialId, NULLIF(LTRIM(RTRIM(l.SerialNo)), N''),
        CASE WHEN ISNULL(s.TrackingMode, 0) NOT IN (3, 4) THEN 0 WHEN l.IsCounter = 1 OR l.ReversalOfId IS NOT NULL THEN 3 WHEN l.QtyBase > 0 THEN 1 ELSE 2 END
    FROM @Lines AS l
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId
    JOIN core.Item AS i ON i.Id = v.ItemId
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = i.Id;

    UPDATE @Rows SET SerialId = NULL, SerialNo = NULL WHERE Kind = 0 OR (Kind = 1 AND CaptureMode = 2);

    -- Seria data prin numar.
    UPDATE r SET SerialId = x.Id
    FROM @Rows AS r JOIN inv.Serial AS x ON x.ItemVariantId = r.ItemVariantId AND x.SerialNo = r.SerialNo
    WHERE r.Kind IN (1, 2) AND r.SerialId IS NULL AND r.SerialNo IS NOT NULL;
    UPDATE r SET SerialNo = x.SerialNo
    FROM @Rows AS r JOIN inv.Serial AS x ON x.Id = r.SerialId
    WHERE r.Kind IN (1, 2) AND r.SerialNo IS NULL;

    IF @IsExternalFact = 0 AND EXISTS (SELECT 1 FROM @Rows
        WHERE (Kind = 1 AND CaptureMode = 1 AND (ABS(QtyBase) <> 1 OR SerialNo IS NULL)) OR (Kind = 2 AND (ABS(QtyBase) <> 1 OR SerialNo IS NULL)))
        THROW 54460, N'Articolul este urmărit pe serie: fiecare bucată are linia ei, cu seria.', 1;

    IF EXISTS (SELECT 1 FROM @Rows WHERE Kind IN (1, 2) AND SerialNo IS NOT NULL GROUP BY ItemVariantId, SerialNo HAVING COUNT(*) > 1)
        THROW 54463, N'Aceeași serie apare de două ori în document.', 1;

    -- Intrarile: seria existenta se poate primi doar ca retur de la client (vanduta de firma) sau dupa stornarea intrarii ei.
    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.Serial AS x ON x.Id = r.SerialId
               WHERE r.Kind = 1 AND x.Status IN (1, 2, 4, 6))
        THROW 54463, N'Seria este deja în stoc.', 1;
    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.Serial AS x ON x.Id = r.SerialId
               WHERE r.Kind = 1
                 AND NOT (x.Status = 3 AND x.CompanyId = @CompanyId AND r.MovementTypeCode = 'RETURN_FROM_CUSTOMER')
                 AND NOT (x.Status = 5 AND EXISTS (SELECT 1 FROM inv.StockMovement AS m WHERE m.SerialId = x.Id AND m.ReversalOfId IS NOT NULL
                                                       AND m.PostingSeq = (SELECT MAX(z.PostingSeq) FROM inv.StockMovement AS z WHERE z.SerialId = x.Id))))
        THROW 54462, N'Seria a fost vândută, casată sau este a altei firme; nu se poate primi decât prin returul clientului care a cumpărat-o.', 1;
    IF EXISTS (SELECT 1 FROM @Rows WHERE Kind = 1 AND SerialId IS NULL AND MovementTypeCode = 'RETURN_FROM_CUSTOMER' AND SerialNo IS NOT NULL)
        THROW 54462, N'Returul unei serii necunoscute nu e permis.', 1;

    -- Iesirile cu citire la intrare: seria trebuie sa fie in stoc in gestiunea sursa.
    IF @IsExternalFact = 0 AND EXISTS (SELECT 1 FROM @Rows AS r LEFT JOIN inv.Serial AS x ON x.Id = r.SerialId
               WHERE r.Kind = 2 AND r.CaptureMode = 1
                 AND (x.Id IS NULL OR x.CompanyId <> @CompanyId OR x.Status NOT IN (1, 4) OR x.WarehouseId <> r.WarehouseId))
        THROW 54461, N'Seria nu este în stoc în gestiunea din care iese.', 1;
    -- Iesirile cu citire doar la iesire: seria se citeste acum si nu trebuie sa existe.
    IF EXISTS (SELECT 1 FROM @Rows WHERE Kind = 2 AND CaptureMode = 2 AND SerialId IS NOT NULL)
        THROW 54463, N'Seria există deja.', 1;

    -- Seriile noi: la intrare (citire la intrare) si la iesire (citire doar la iesire).
    DECLARE @New TABLE (Id int, ItemVariantId int, SerialNo nvarchar(60) COLLATE DATABASE_DEFAULT);
    INSERT INTO inv.Serial (CompanyId, ItemVariantId, SerialNo, Status, WarehouseId, DepotId, IsCapturedAtIssue, CreatedBy)
    OUTPUT inserted.Id, inserted.ItemVariantId, inserted.SerialNo INTO @New
    SELECT @CompanyId, r.ItemVariantId, r.SerialNo, 1, r.WarehouseId, r.DepotId, CASE WHEN r.Kind = 2 THEN 1 ELSE 0 END, @UserId
    FROM @Rows AS r
    WHERE r.Kind IN (1, 2) AND r.SerialId IS NULL AND r.SerialNo IS NOT NULL AND (r.Kind = 1 OR r.CaptureMode = 2);
    UPDATE r SET SerialId = n.Id, IsNew = 1
    FROM @Rows AS r JOIN @New AS n ON n.ItemVariantId = r.ItemVariantId AND n.SerialNo = r.SerialNo;

    -- Identificatorii: tipurile cunoscute, regulile modelului (sau, fara ele, ale grupei), cei obligatorii, unicitatea, apoi valorile noi.
    IF EXISTS (SELECT 1 FROM @Identifiers AS i LEFT JOIN inv.SerialIdentifierType AS t ON t.CodeKey = UPPER(i.TypeCode) AND t.IsActive = 1 WHERE t.Id IS NULL)
        THROW 54471, N'Tipul unui identificator nu există.', 1;

    DECLARE @Rules TABLE (RowNo int, IdentifierTypeId int, RequiredAtReceipt bit, RequiredAtIssue bit, IsUnique bit, PRIMARY KEY (RowNo, IdentifierTypeId));
    INSERT INTO @Rules
    SELECT r.RowNo, x.IdentifierTypeId, x.RequiredAtReceipt, x.RequiredAtIssue, x.IsUnique
    FROM @Rows AS r
    JOIN inv.ItemSerialIdentifierRule AS x
        ON (x.ItemId = r.ItemId) OR (x.ItemGroupId = r.ItemGroupId AND NOT EXISTS (SELECT 1 FROM inv.ItemSerialIdentifierRule AS y WHERE y.ItemId = r.ItemId))
    WHERE r.Kind IN (1, 2) AND r.SerialId IS NOT NULL;

    DECLARE @Values TABLE (RowNo int, SerialId int, IdentifierTypeId int, Value nvarchar(100) COLLATE DATABASE_DEFAULT, IsUnique bit);
    INSERT INTO @Values
    SELECT r.RowNo, r.SerialId, t.Id, LTRIM(RTRIM(i.Value)), ISNULL(u.IsUnique, 0)
    FROM @Identifiers AS i
    JOIN @Rows AS r ON r.RowNo = i.RowNo AND r.Kind IN (1, 2) AND r.SerialId IS NOT NULL
    JOIN inv.SerialIdentifierType AS t ON t.CodeKey = UPPER(i.TypeCode)
    LEFT JOIN @Rules AS u ON u.RowNo = r.RowNo AND u.IdentifierTypeId = t.Id
    WHERE LTRIM(RTRIM(i.Value)) <> N'';

    IF EXISTS (SELECT 1 FROM @Rules AS u JOIN @Rows AS r ON r.RowNo = u.RowNo
               WHERE ((r.Kind = 1 AND u.RequiredAtReceipt = 1) OR (r.Kind = 2 AND (u.RequiredAtIssue = 1 OR (r.IsNew = 1 AND u.RequiredAtReceipt = 1))))
                 AND NOT EXISTS (SELECT 1 FROM @Values AS v WHERE v.RowNo = u.RowNo AND v.IdentifierTypeId = u.IdentifierTypeId)
                 AND NOT EXISTS (SELECT 1 FROM inv.SerialIdentifier AS s WHERE s.SerialId = r.SerialId AND s.IdentifierTypeId = u.IdentifierTypeId))
        THROW 54473, N'Lipsește un identificator obligatoriu al bucății (de exemplu IMEI).', 1;

    IF EXISTS (SELECT 1 FROM @Values WHERE IsUnique = 1 GROUP BY IdentifierTypeId, Value HAVING COUNT(*) > 1)
       OR EXISTS (SELECT 1 FROM @Values AS v JOIN inv.SerialIdentifier AS s ON s.IdentifierTypeId = v.IdentifierTypeId AND s.Value = v.Value
                  WHERE v.IsUnique = 1 AND s.IsUnique = 1 AND s.SerialId <> v.SerialId)
        THROW 54472, N'Identificatorul este deja folosit pe altă bucată.', 1;

    INSERT INTO inv.SerialIdentifier (SerialId, IdentifierTypeId, Value, IsUnique, CreatedBy)
    SELECT v.SerialId, v.IdentifierTypeId, v.Value, v.IsUnique, @UserId
    FROM @Values AS v
    WHERE NOT EXISTS (SELECT 1 FROM inv.SerialIdentifier AS s WHERE s.SerialId = v.SerialId AND s.IdentifierTypeId = v.IdentifierTypeId);

    SELECT r.RowNo, r.SerialId, x.LotId
    FROM @Rows AS r LEFT JOIN inv.Serial AS x ON x.Id = r.SerialId
    WHERE r.Kind IN (1, 2)
    ORDER BY r.RowNo;
END;

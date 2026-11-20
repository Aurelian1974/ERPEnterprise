CREATE OR ALTER PROCEDURE core.usp_UnitOfMeasure_SaveBatch
    @Rows   core.UnitOfMeasureList READONLY,
    @UserId int
AS
BEGIN
    -- Unitatile de masura editate in tabel (macheta 07). Codul si codul UN/ECE sunt unice; la o UM folosita (in seturi,
    -- exceptii sau coduri de bare) nu se mai schimba, doar denumirea si starea.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN core.UnitOfMeasure AS u WITH (UPDLOCK, HOLDLOCK) ON u.Id = r.Id WHERE u.RowVersion <> r.RowVersion OR r.RowVersion IS NULL)
        THROW 50006, N'O unitate de măsură a fost modificată de alt utilizator; reîncărcați datele.', 1;

    IF EXISTS (SELECT 1 FROM @Rows AS r WHERE r.Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.UnitOfMeasure AS u WHERE u.Id = r.Id))
        THROW 50027, N'O unitate de măsură nu există.', 1;

    IF EXISTS (
        SELECT 1 FROM @Rows AS r JOIN core.UnitOfMeasure AS u ON u.Id = r.Id
        WHERE (r.Code <> u.Code OR r.UneceCode <> u.UneceCode)
          AND (EXISTS (SELECT 1 FROM core.UomSetLine AS l WHERE l.UomId = u.Id) OR EXISTS (SELECT 1 FROM core.ItemUom AS x WHERE x.UomId = u.Id)
               OR EXISTS (SELECT 1 FROM core.ItemBarcode AS b WHERE b.UomId = u.Id)))
        THROW 50010, N'O unitate de măsură folosită nu își mai schimbă codul.', 1;

    UPDATE u
    SET Code = r.Code, Name = r.Name, UneceCode = r.UneceCode, IsActive = r.IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM core.UnitOfMeasure AS u
    JOIN @Rows AS r ON r.Id = u.Id;

    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN core.UnitOfMeasure AS u ON (u.CodeKey = UPPER(r.Code) OR u.UneceCode = r.UneceCode) WHERE r.Id IS NULL)
       OR EXISTS (SELECT UPPER(Code) FROM @Rows GROUP BY UPPER(Code) HAVING COUNT(*) > 1)
       OR EXISTS (SELECT UneceCode FROM @Rows GROUP BY UneceCode HAVING COUNT(*) > 1)
        THROW 50018, N'Codul sau codul UN/ECE există deja la altă unitate de măsură.', 1;

    INSERT INTO core.UnitOfMeasure (Code, Name, UneceCode, IsActive, CreatedBy)
    SELECT r.Code, r.Name, r.UneceCode, r.IsActive, @UserId
    FROM @Rows AS r
    WHERE r.Id IS NULL;

    SELECT COUNT(*) FROM @Rows;
END;

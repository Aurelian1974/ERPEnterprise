CREATE OR ALTER PROCEDURE core.usp_UomSet_Save
    @Id         int = NULL,
    @Code       nvarchar(20),
    @Name       nvarchar(100),
    @IsActive   bit,
    @Lines      core.UomLineList READONLY,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Setul de UM (02.01, 3.3): o singura linie de baza cu factor 1, factori pozitivi, cel mult o linie implicita pe circuit.
    -- Liniile se inlocuiesc; UM de baza a articolelor din set urmeaza linia de baza. Restrictia „factorii nu se schimba daca
    -- articolele au stoc sau documente” se aplica odata cu modulele Stocuri si Vanzari.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (SELECT COUNT(*) FROM @Lines WHERE IsBase = 1) <> 1 OR EXISTS (SELECT 1 FROM @Lines WHERE IsBase = 1 AND FactorToBase <> 1)
        THROW 50021, N'Setul are exact o UM de bază, cu factorul 1.', 1;

    IF EXISTS (SELECT 1 FROM @Lines WHERE FactorToBase <= 0)
       OR (SELECT COUNT(*) FROM @Lines WHERE IsDefaultSales = 1) > 1
       OR (SELECT COUNT(*) FROM @Lines WHERE IsDefaultPurchase = 1) > 1
       OR (SELECT COUNT(*) FROM @Lines WHERE IsDefaultPos = 1) > 1
       OR EXISTS (SELECT UomId FROM @Lines GROUP BY UomId HAVING COUNT(*) > 1)
        THROW 50021, N'Factorii sunt pozitivi, fiecare UM apare o dată și există cel mult o UM implicită pe circuit.', 1;

    IF EXISTS (SELECT 1 FROM @Lines AS l WHERE NOT EXISTS (SELECT 1 FROM core.UnitOfMeasure AS u WHERE u.Id = l.UomId))
        THROW 50021, N'O unitate de măsură aleasă nu există.', 1;

    IF EXISTS (SELECT 1 FROM core.UomSet WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul setului există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.UomSet (Code, Name, IsActive, CreatedBy) VALUES (@Code, @Name, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.Item WHERE UomSetId = @Id AND IsActive = 1)
            THROW 50010, N'Setul e folosit de articole active.', 1;

        UPDATE core.UomSet
        SET Code = @Code, Name = @Name, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.UomSet WHERE Id = @Id)
                THROW 50027, N'Setul de UM nu există.', 1;
            THROW 50006, N'Setul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;

        DELETE FROM core.UomSetLine WHERE UomSetId = @Id;
    END;

    INSERT INTO core.UomSetLine (UomSetId, UomId, FactorToBase, IsBase, IsDefaultSales, IsDefaultPurchase, IsDefaultPos, SortOrder)
    SELECT @Id, UomId, FactorToBase, IsBase, IsDefaultSales, IsDefaultPurchase, IsDefaultPos, SortOrder FROM @Lines;

    UPDATE core.Item
    SET BaseUomId = (SELECT UomId FROM @Lines WHERE IsBase = 1), ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE UomSetId = @Id AND BaseUomId <> (SELECT UomId FROM @Lines WHERE IsBase = 1);

    SELECT Id, RowVersion FROM core.UomSet WHERE Id = @Id;
END;

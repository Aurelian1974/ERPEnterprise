CREATE OR ALTER PROCEDURE core.usp_Duplicate_Find
    @EntityType tinyint,
    @Top        int = 200
AS
BEGIN
    -- Perechi posibile de dubluri intre inregistrarile active (02.01, 3.6 punctul 7). Parteneri: aceeasi denumire (fara diacritice,
    -- punctuatie si forma juridica), acelasi e-mail, acelasi telefon (doar cifrele, minim 9) sau acelasi IBAN; articole: aceeasi
    -- denumire. Reasons = motivele (Name, Email, Phone, Iban), separate prin virgula. Perechile ignorate nu apar.
    SET NOCOUNT ON;

    CREATE TABLE #K (
        Id      int NOT NULL PRIMARY KEY,
        NameKey nvarchar(260) COLLATE Latin1_General_100_CI_AI_SC_UTF8 NULL,
        Email   nvarchar(254) COLLATE Latin1_General_100_CI_AI_SC_UTF8 NULL,
        Phone   varchar(30) COLLATE DATABASE_DEFAULT NULL
    );

    IF @EntityType = 1
        INSERT INTO #K (Id, NameKey, Email, Phone)
        SELECT p.Id, k.NameKey, NULLIF(LTRIM(RTRIM(p.Email)), N''),
               NULLIF(TRANSLATE(ISNULL(p.Phone, N''), N'+-./() ', N'       '), N'')
        FROM core.Partner AS p
        CROSS APPLY core.fn_NameKey(p.Name) AS k
        WHERE p.IsActive = 1 AND p.MergedIntoId IS NULL;
    ELSE
        INSERT INTO #K (Id, NameKey)
        SELECT i.Id, k.NameKey
        FROM core.Item AS i
        CROSS APPLY core.fn_NameKey(i.Name) AS k
        WHERE i.IsActive = 1 AND i.MergedIntoId IS NULL;

    UPDATE #K SET Phone = REPLACE(Phone, ' ', '');
    UPDATE #K SET Phone = NULL WHERE LEN(Phone) < 9;

    CREATE TABLE #Pairs (FirstId int NOT NULL, SecondId int NOT NULL, Reason varchar(10) NOT NULL);
    INSERT INTO #Pairs (FirstId, SecondId, Reason)
    SELECT a.Id, b.Id, 'Name' FROM #K AS a JOIN #K AS b ON b.NameKey = a.NameKey AND b.Id > a.Id WHERE a.NameKey <> N''
    UNION ALL
    SELECT a.Id, b.Id, 'Email' FROM #K AS a JOIN #K AS b ON b.Email = a.Email AND b.Id > a.Id
    UNION ALL
    SELECT a.Id, b.Id, 'Phone' FROM #K AS a JOIN #K AS b ON b.Phone = a.Phone AND b.Id > a.Id;

    IF @EntityType = 1
        INSERT INTO #Pairs (FirstId, SecondId, Reason)
        SELECT DISTINCT x.PartnerId, y.PartnerId, 'Iban'
        FROM core.PartnerBankAccount AS x
        JOIN core.PartnerBankAccount AS y ON y.Iban = x.Iban AND y.PartnerId > x.PartnerId
        JOIN #K AS kx ON kx.Id = x.PartnerId
        JOIN #K AS ky ON ky.Id = y.PartnerId;

    WITH Grouped AS (
        SELECT p.FirstId, p.SecondId, STRING_AGG(p.Reason, ',') WITHIN GROUP (ORDER BY p.Reason) AS Reasons, COUNT(*) AS Score
        FROM (SELECT DISTINCT FirstId, SecondId, Reason FROM #Pairs) AS p
        WHERE NOT EXISTS (SELECT 1 FROM core.DuplicateIgnore AS g WHERE g.EntityType = @EntityType AND g.FirstId = p.FirstId AND g.SecondId = p.SecondId)
        GROUP BY p.FirstId, p.SecondId
    )
    SELECT TOP (@Top) g.FirstId, g.SecondId, g.Reasons,
           COALESCE(pa.Code, ia.Code) AS FirstCode, COALESCE(pa.Name, ia.Name) AS FirstName, pa.FiscalCode AS FirstFiscalCode,
           COALESCE(pb.Code, ib.Code) AS SecondCode, COALESCE(pb.Name, ib.Name) AS SecondName, pb.FiscalCode AS SecondFiscalCode
    FROM Grouped AS g
    LEFT JOIN core.Partner AS pa ON @EntityType = 1 AND pa.Id = g.FirstId
    LEFT JOIN core.Partner AS pb ON @EntityType = 1 AND pb.Id = g.SecondId
    LEFT JOIN core.Item AS ia ON @EntityType = 2 AND ia.Id = g.FirstId
    LEFT JOIN core.Item AS ib ON @EntityType = 2 AND ib.Id = g.SecondId
    ORDER BY g.Score DESC, g.FirstId, g.SecondId;
END;

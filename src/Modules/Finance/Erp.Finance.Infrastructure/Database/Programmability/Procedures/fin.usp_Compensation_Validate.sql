CREATE OR ALTER PROCEDURE fin.usp_Compensation_Validate
    @Id         int,
    @CompanyId  int,
    @Number     nvarchar(40),
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Valideaza un PV de compensare (02.02, F3 3.4, 4.4 pasul 3), in tranzactia apelantului: ciorna (51411), aceeasi versiune (51417), cu
    -- linii, echilibrat (suma liniilor = 0, 51410); partidele (blocate) inca acopera sumele (51403). Creeaza cate o partida tehnica de tip 7 pe
    -- partener si rol (suma opusa liniilor lor), ca alocarea sa ramana debit <-> credit la acelasi partener si rol; alocarile le face apelantul.
    -- Intoarce liniile cu partida tehnica a grupului lor.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @HeaderCompanyId int, @Status tinyint, @Version binary(8), @Date date, @Currency char(3);
    SELECT @HeaderCompanyId = CompanyId, @Status = Status, @Version = RowVersion, @Date = DocumentDate, @Currency = CurrencyCode
    FROM fin.Compensation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @HeaderCompanyId IS NULL OR @HeaderCompanyId <> @CompanyId
        THROW 51021, N'Procesul-verbal nu există.', 1;
    IF @Status <> 1
        THROW 51411, N'Procesul-verbal este deja validat sau stornat.', 1;
    IF @Version <> @RowVersion
        THROW 51417, N'Procesul-verbal a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF NOT EXISTS (SELECT 1 FROM fin.CompensationLine WHERE CompensationId = @Id)
        THROW 51410, N'Procesul-verbal nu are linii.', 1;
    IF (SELECT SUM(Amount) FROM fin.CompensationLine WHERE CompensationId = @Id) <> 0
        THROW 51410, N'Ce se stinge ca datorie trebuie să fie egal cu ce se stinge ca creanță (suma liniilor = 0).', 1;

    DECLARE @Items TABLE (Id bigint PRIMARY KEY, PartnerId int, Role tinyint, Amount decimal(19,2), OpenAmount decimal(19,2), ExchangeRate decimal(19,6));
    INSERT INTO @Items
    SELECT o.Id, o.PartnerId, o.Role, o.Amount, o.OpenAmount, o.ExchangeRate
    FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.Id IN (SELECT OpenItemId FROM fin.CompensationLine WHERE CompensationId = @Id)
    ORDER BY o.Id;
    IF EXISTS (SELECT 1 FROM fin.CompensationLine AS l JOIN @Items AS i ON i.Id = l.OpenItemId
               WHERE l.CompensationId = @Id AND (SIGN(l.Amount) <> SIGN(i.Amount) OR ABS(l.Amount) > ABS(i.OpenAmount)))
        THROW 51403, N'O partidă din procesul-verbal nu mai are restul compensat (a fost între timp plătită).', 1;

    -- Partida tehnica pe partener si rol: suma opusa liniilor grupului, la cursul mediu ponderat al partidelor lui.
    DECLARE @Groups TABLE (PartnerId int, Role tinyint, Amount decimal(19,2), Rate decimal(19,6), TechId bigint NULL, PRIMARY KEY (PartnerId, Role));
    INSERT INTO @Groups (PartnerId, Role, Amount, Rate)
    SELECT i.PartnerId, i.Role, -SUM(l.Amount), ROUND(SUM(ABS(l.Amount) * i.ExchangeRate) / SUM(ABS(l.Amount)), 6)
    FROM fin.CompensationLine AS l JOIN @Items AS i ON i.Id = l.OpenItemId
    WHERE l.CompensationId = @Id
    GROUP BY i.PartnerId, i.Role;
    IF EXISTS (SELECT 1 FROM @Groups WHERE Amount = 0)
        THROW 51410, N'Pe un partener și rol liniile se anulează între ele; folosiți alocarea, nu compensarea.', 1;

    DECLARE @Number2 nvarchar(50) = LEFT(N'PVC ' + @Number, 50);
    INSERT INTO fin.OpenItem (CompanyId, PartnerId, Role, Kind, SourceModule, SourceDocumentType, SourceDocumentId, SourceLineKey, DocumentNo, DocumentDate,
        AccountingDate, DueDate, CurrencyCode, ExchangeRate, Amount, AmountRon, OpenAmount, OpenAmountRon, CarryingRate, CreatedBy)
    SELECT @CompanyId, g.PartnerId, g.Role, 7, 'Finance', 'Finance.Compensation', @Id, CONCAT(N'P', g.PartnerId, N'R', g.Role), @Number2, @Date, @Date, @Date,
        @Currency, g.Rate, g.Amount, ROUND(g.Amount * g.Rate, 2), g.Amount, ROUND(g.Amount * g.Rate, 2), g.Rate, @UserId
    FROM @Groups AS g;

    UPDATE g SET TechId = o.Id
    FROM @Groups AS g
    JOIN fin.OpenItem AS o ON o.SourceDocumentType = 'Finance.Compensation' AND o.SourceDocumentId = @Id
        AND o.SourceLineKey = CONCAT(N'P', g.PartnerId, N'R', g.Role) AND o.Role = g.Role;

    UPDATE fin.Compensation
    SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT l.LineNumber, l.OpenItemId, l.Amount, g.TechId AS TechnicalOpenItemId, i.PartnerId, i.Role
    FROM fin.CompensationLine AS l
    JOIN @Items AS i ON i.Id = l.OpenItemId
    JOIN @Groups AS g ON g.PartnerId = i.PartnerId AND g.Role = i.Role
    WHERE l.CompensationId = @Id
    ORDER BY l.LineNumber;
END;

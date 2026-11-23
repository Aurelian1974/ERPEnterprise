CREATE OR ALTER PROCEDURE fin.usp_Compensation_Save
    @Id           int = NULL,
    @CompanyId    int,
    @DocumentDate date,
    @Kind         tinyint,
    @ExternalRef  nvarchar(100) = NULL,
    @CurrencyCode char(3),
    @Note         nvarchar(500) = NULL,
    @Lines        fin.CompensationLineList READONLY,
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Salveaza un PV de compensare ciorna (02.02, F3 3.4, 4.4 pasul 1): partidele sunt ale firmei, deschise, in moneda PV-ului, iar suma
    -- liniei are semnul partidei si nu depaseste restul (51021, 51418, 51403). La compensarea bilaterala (Kind 1) toate partidele sunt ale
    -- aceluiasi partener (51402). Echilibrul (suma liniilor = 0) se cere la validare. PV validat: 51411; alta versiune: 51417.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @Lines AS l LEFT JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId AND o.CompanyId = @CompanyId WHERE o.Id IS NULL)
        THROW 51021, N'O partidă nu există în firma curentă.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId WHERE o.CurrencyCode <> @CurrencyCode)
        THROW 51418, N'Toate partidele compensate sunt în moneda procesului-verbal.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
               WHERE SIGN(l.Amount) <> SIGN(o.Amount) OR ABS(l.Amount) > ABS(o.OpenAmount))
        THROW 51403, N'Suma compensată are semnul partidei și nu depășește restul ei de plată.', 1;
    IF @Kind = 1 AND (SELECT COUNT(DISTINCT o.PartnerId) FROM @Lines AS l JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId) > 1
        THROW 51402, N'Compensarea bilaterală la același partener cuprinde partidele unui singur partener.', 1;

    DECLARE @Total decimal(19,2) = ISNULL((SELECT SUM(Amount) FROM @Lines WHERE Amount > 0), 0);

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.Compensation (CompanyId, DocumentDate, Kind, ExternalRef, CurrencyCode, TotalAmount, Note, CreatedBy)
        VALUES (@CompanyId, @DocumentDate, @Kind, @ExternalRef, @CurrencyCode, @Total, @Note, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @HeaderCompanyId int, @Status tinyint, @Version binary(8);
        SELECT @HeaderCompanyId = CompanyId, @Status = Status, @Version = RowVersion FROM fin.Compensation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @HeaderCompanyId IS NULL OR @HeaderCompanyId <> @CompanyId
            THROW 51021, N'Procesul-verbal nu există.', 1;
        IF @Status <> 1
            THROW 51411, N'Doar o ciornă se poate modifica.', 1;
        IF @Version <> @RowVersion
            THROW 51417, N'Procesul-verbal a fost modificat de alt utilizator; reîncărcați datele.', 1;

        UPDATE fin.Compensation
        SET DocumentDate = @DocumentDate, Kind = @Kind, ExternalRef = @ExternalRef, CurrencyCode = @CurrencyCode, TotalAmount = @Total, Note = @Note,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM fin.CompensationLine WHERE CompensationId = @Id;
    END

    INSERT INTO fin.CompensationLine (CompensationId, LineNumber, OpenItemId, Amount)
    SELECT @Id, l.LineNumber, l.OpenItemId, l.Amount FROM @Lines AS l;

    SELECT Id, RowVersion FROM fin.Compensation WHERE Id = @Id;
END;

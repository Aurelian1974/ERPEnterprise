CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_FirstError
    @EntryId       bigint,
    @ErrorCode     int OUTPUT,
    @ErrorMessage  nvarchar(400) OUTPUT
AS
BEGIN
    -- Verificarile unei note inainte de validare (C2 3.1 regulile 2 si 8), pe set, fara sa arunce: prima eroare (codul si mesajul, cu contul
    -- liniei) sau 0. Echilibrul pe lei (51843), conturile (frunza activa a planului romanesc, permisa firmei, in vigoare la data contabila; 51844),
    -- dimensiunile cerute sau interzise de cont (51845; nu si pe notele de inchidere si de deschidere, fara dimensiuni prin definitie, C3 3.5), partenerul sau angajatul cerut (51846), moneda permisa (51847). Contarea automata o
    -- foloseste ca sa puna nota in coada fara sa anuleze tranzactia; validarea manuala, prin fin.usp_JournalEntry_Check, care arunca.
    SET NOCOUNT ON;
    SELECT @ErrorCode = 0, @ErrorMessage = NULL;

    DECLARE @Company int, @Date date, @Message nvarchar(400), @Code nvarchar(20);
    SELECT @Company = CompanyId, @Date = AccountingDate FROM fin.JournalEntry WHERE Id = @EntryId;
    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

    IF NOT EXISTS (SELECT 1 FROM fin.JournalLine WHERE EntryId = @EntryId)
        OR (SELECT SUM(DebitRon) FROM fin.JournalLine WHERE EntryId = @EntryId) <> (SELECT SUM(CreditRon) FROM fin.JournalLine WHERE EntryId = @EntryId)
        BEGIN SELECT @ErrorCode = 51843, @ErrorMessage = N'Nota nu e echilibrată: totalul debit diferă de totalul credit.'; RETURN; END;

    SELECT TOP (1) @Code = a.Code
    FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    WHERE l.EntryId = @EntryId AND (a.ChartId <> @Statutory OR a.IsPostable = 0 OR a.IsActive = 0 OR (a.CompanyId IS NOT NULL AND a.CompanyId <> @Company)
          OR (a.ValidFrom IS NOT NULL AND a.ValidFrom > @Date) OR (a.ValidTo IS NOT NULL AND a.ValidTo < @Date))
    ORDER BY l.LineNumber;
    IF @Code IS NOT NULL
    BEGIN
        SET @Message = CONCAT(N'Contul ', @Code, N' nu poate primi note: are analitice, e inactiv, nu e în vigoare sau nu e al firmei.');
        BEGIN SELECT @ErrorCode = 51844, @ErrorMessage = @Message; RETURN; END;
    END;

    SELECT TOP (1) @Code = a.Code, @Message = m.DimensionType
    FROM fin.JournalLine AS l
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    CROSS APPLY fin.ufn_GlAccount_DimensionModes(l.AccountId) AS m
    CROSS APPLY (SELECT CASE m.DimensionType WHEN 'COST_CENTER' THEN l.CostCenterId WHEN 'PROJECT' THEN l.ProjectId WHEN 'WAREHOUSE' THEN l.WarehouseId
                        WHEN 'SITE' THEN l.SiteId END AS Value) AS v
    WHERE l.EntryId = @EntryId AND (SELECT SourceKind FROM fin.JournalEntry WHERE Id = @EntryId) NOT IN (4, 5)
      AND ((m.Mode = 2 AND v.Value IS NULL) OR (m.Mode = 0 AND v.Value IS NOT NULL))
    ORDER BY l.LineNumber;
    IF @Code IS NOT NULL
    BEGIN
        SET @Message = CONCAT(N'Pe contul ', @Code, N' dimensiunea ', @Message, N' e obligatorie sau interzisă și nu respectă regula contului.');
        BEGIN SELECT @ErrorCode = 51845, @ErrorMessage = @Message; RETURN; END;
    END;

    SELECT TOP (1) @Code = a.Code
    FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    WHERE l.EntryId = @EntryId AND ((a.PartyRequirement = 1 AND l.PartnerId IS NULL) OR (a.PartyRequirement = 2 AND l.EmployeeId IS NULL)
          OR (a.PartyRequirement = 3 AND l.PartnerId IS NULL AND l.EmployeeId IS NULL))
    ORDER BY l.LineNumber;
    IF @Code IS NOT NULL
    BEGIN
        SET @Message = CONCAT(N'Contul ', @Code, N' cere partenerul sau angajatul pe linie.');
        BEGIN SELECT @ErrorCode = 51846, @ErrorMessage = @Message; RETURN; END;
    END;

    SELECT TOP (1) @Code = a.Code
    FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    WHERE l.EntryId = @EntryId AND l.CurrencyCode IS NOT NULL AND l.CurrencyCode <> 'RON'
      AND (a.CurrencyMode = 0 OR (a.CurrencyMode = 2 AND l.CurrencyCode <> a.CurrencyCode))
    ORDER BY l.LineNumber;
    IF @Code IS NOT NULL
    BEGIN
        SET @Message = CONCAT(N'Contul ', @Code, N' nu ține sume în moneda liniei.');
        BEGIN SELECT @ErrorCode = 51847, @ErrorMessage = @Message; RETURN; END;
    END;
END;

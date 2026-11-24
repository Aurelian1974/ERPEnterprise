CREATE OR ALTER PROCEDURE fin.usp_InventoryRegister_Save
    @CompanyId  int,
    @Year       smallint,
    @Lines      nvarchar(max),
    @Pdf        varbinary(max),
    @UserId     int
AS
BEGIN
    -- Registrul inventar al anului (C5 3.4), creat sau regenerat; pe anul blocat nu se mai regenereaza (51906). Final cand anul e inchis.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @YearStatus tinyint = (SELECT Status FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND Year = @Year);
    IF @YearStatus IS NULL
        THROW 51901, N'Anul financiar nu există.', 1;
    IF @YearStatus = 3
        THROW 51906, N'Anul e blocat; registrul inventar nu se mai regenerează.', 1;
    BEGIN TRANSACTION;
    DECLARE @Id int = (SELECT Id FROM fin.InventoryRegister WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND Year = @Year);
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.InventoryRegister (CompanyId, Year, IsFinal, GeneratedAt, GeneratedBy, Pdf)
        VALUES (@CompanyId, @Year, CASE WHEN @YearStatus >= 2 THEN 1 ELSE 0 END, SYSUTCDATETIME(), @UserId, @Pdf);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE fin.InventoryRegister SET IsFinal = CASE WHEN @YearStatus >= 2 THEN 1 ELSE 0 END, GeneratedAt = SYSUTCDATETIME(), GeneratedBy = @UserId, Pdf = @Pdf
        WHERE Id = @Id;
        DELETE FROM fin.InventoryRegisterLine WHERE RegisterId = @Id;
    END;
    INSERT INTO fin.InventoryRegisterLine (RegisterId, AccountCode, AccountName, PartnerId, PartnerName, Side, BookValue, InventoryValue, Difference)
    SELECT @Id, AccountCode, AccountName, PartnerId, PartnerName, Side, BookValue, InventoryValue, InventoryValue - BookValue
    FROM OPENJSON(@Lines) WITH (AccountCode nvarchar(20) '$.accountCode', AccountName nvarchar(200) '$.accountName', PartnerId int '$.partnerId',
        PartnerName nvarchar(200) '$.partnerName', Side tinyint '$.side', BookValue decimal(19,2) '$.bookValue', InventoryValue decimal(19,2) '$.inventoryValue');
    COMMIT;
    SELECT @Id;
END;

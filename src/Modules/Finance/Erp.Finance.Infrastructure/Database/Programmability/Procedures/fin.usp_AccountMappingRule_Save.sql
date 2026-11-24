CREATE OR ALTER PROCEDURE fin.usp_AccountMappingRule_Save
    @Id                  int = NULL,
    @ChartId             int,
    @Priority            int,
    @SourceKind          tinyint,
    @SourceAccountId     int = NULL,
    @SourceFrom          nvarchar(20) = NULL,
    @SourceTo            nvarchar(20) = NULL,
    @SourceAccounts      nvarchar(max) = NULL,
    @CompanyId           int = NULL,
    @Side                tinyint,
    @PartnerKind         tinyint = NULL,
    @PartnerGroupId      int = NULL,
    @PartnerId           int = NULL,
    @DimensionType       varchar(20) = NULL,
    @DimensionCondition  tinyint,
    @DimensionValueId    int = NULL,
    @ItemGroupId         int = NULL,
    @CurrencyCode        char(3) = NULL,
    @BalanceSign         tinyint,
    @ValidFrom           date,
    @ValidTo             date = NULL,
    @IsActive            bit,
    @Note                nvarchar(500) = NULL,
    @Targets             nvarchar(max),
    @RowVersion          binary(8) = NULL,
    @UserId              int
AS
BEGIN
    -- O regula de mapare (C1 3.5): prioritatea unica in plan (51809); sursa din planul romanesc (un cont, interval normalizat in C#, lista
    -- JSON [accountId]); tintele JSON [{accountId, percent}] din planul alternativ, conturi frunza active, cu procente care fac 100 (51808);
    -- valoarea dimensiunii de tipul cerut (51021). Modificare cu RowVersion (51802); sursele si tintele se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    IF NOT EXISTS (SELECT 1 FROM fin.ChartOfAccounts WHERE Id = @ChartId AND Kind = 2)
        THROW 51812, N'Regulile de mapare sunt doar pentru planurile alternative.', 1;
    IF EXISTS (SELECT 1 FROM fin.AccountMappingRule WHERE TargetChartId = @ChartId AND Priority = @Priority AND (@Id IS NULL OR Id <> @Id))
        THROW 51809, N'Există deja o regulă cu această prioritate în plan.', 1;

    DECLARE @Sources TABLE (AccountId int PRIMARY KEY);
    IF @SourceKind = 3
        INSERT INTO @Sources (AccountId) SELECT DISTINCT CAST(value AS int) FROM OPENJSON(@SourceAccounts);
    DECLARE @Goals TABLE (AccountId int PRIMARY KEY, PercentShare decimal(9,6), SortOrder tinyint);
    INSERT INTO @Goals (AccountId, PercentShare, SortOrder)
    SELECT AccountId, PercentShare, CAST([key] AS int) + 1
    FROM OPENJSON(@Targets) AS j CROSS APPLY OPENJSON(j.value) WITH (AccountId int '$.accountId', PercentShare decimal(9,6) '$.percent');

    IF (@SourceKind = 1 AND NOT EXISTS (SELECT 1 FROM fin.GlAccount WHERE Id = @SourceAccountId AND ChartId = @Statutory))
       OR (@SourceKind = 3 AND (NOT EXISTS (SELECT 1 FROM @Sources)
           OR EXISTS (SELECT 1 FROM @Sources AS s WHERE NOT EXISTS (SELECT 1 FROM fin.GlAccount AS a WHERE a.Id = s.AccountId AND a.ChartId = @Statutory))))
        THROW 51021, N'Sursa regulii trebuie să fie din planul românesc.', 1;
    IF NOT EXISTS (SELECT 1 FROM @Goals) OR (SELECT SUM(PercentShare) FROM @Goals) <> 100 OR EXISTS (SELECT 1 FROM @Goals WHERE PercentShare <= 0)
        THROW 51808, N'Țintele regulii trebuie să facă împreună 100%.', 1;
    IF EXISTS (SELECT 1 FROM @Goals AS g WHERE NOT EXISTS (SELECT 1 FROM fin.GlAccount AS a WHERE a.Id = g.AccountId AND a.ChartId = @ChartId
                                                           AND a.IsPostable = 1 AND a.IsActive = 1))
        THROW 51806, N'Țintele sunt conturi active, fără conturi copil, ale planului alternativ.', 1;
    IF @DimensionCondition = 1 AND NOT EXISTS (SELECT 1 FROM core.DimensionValue WHERE Id = @DimensionValueId AND DimensionType = @DimensionType)
        THROW 51021, N'Valoarea aleasă nu e de tipul de dimensiune al condiției.', 1;

    BEGIN TRANSACTION;
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.AccountMappingRule (TargetChartId, Priority, SourceKind, SourceAccountId, SourceFrom, SourceTo, CompanyId, Side, PartnerKind,
            PartnerGroupId, PartnerId, DimensionType, DimensionCondition, DimensionValueId, ItemGroupId, CurrencyCode, BalanceSign, ValidFrom, ValidTo,
            IsActive, Note, CreatedBy)
        VALUES (@ChartId, @Priority, @SourceKind, @SourceAccountId, @SourceFrom, @SourceTo, @CompanyId, @Side, @PartnerKind, @PartnerGroupId, @PartnerId,
            @DimensionType, @DimensionCondition, @DimensionValueId, @ItemGroupId, @CurrencyCode, @BalanceSign, @ValidFrom, @ValidTo, @IsActive, @Note, @UserId);
        SET @Id = CAST(SCOPE_IDENTITY() AS int);
    END
    ELSE
    BEGIN
        UPDATE fin.AccountMappingRule
        SET Priority = @Priority, SourceKind = @SourceKind, SourceAccountId = @SourceAccountId, SourceFrom = @SourceFrom, SourceTo = @SourceTo,
            CompanyId = @CompanyId, Side = @Side, PartnerKind = @PartnerKind, PartnerGroupId = @PartnerGroupId, PartnerId = @PartnerId,
            DimensionType = @DimensionType, DimensionCondition = @DimensionCondition, DimensionValueId = @DimensionValueId, ItemGroupId = @ItemGroupId,
            CurrencyCode = @CurrencyCode, BalanceSign = @BalanceSign, ValidFrom = @ValidFrom, ValidTo = @ValidTo, IsActive = @IsActive, Note = @Note,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND TargetChartId = @ChartId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 51802, N'Regula a fost modificată de altcineva între timp.', 1;
        DELETE FROM fin.AccountMappingSource WHERE RuleId = @Id;
        DELETE FROM fin.AccountMappingTarget WHERE RuleId = @Id;
    END;

    INSERT INTO fin.AccountMappingSource (RuleId, AccountId) SELECT @Id, AccountId FROM @Sources;
    INSERT INTO fin.AccountMappingTarget (RuleId, TargetAccountId, PercentShare, SortOrder) SELECT @Id, AccountId, PercentShare, SortOrder FROM @Goals;
    COMMIT TRANSACTION;

    SELECT Id, RowVersion FROM fin.AccountMappingRule WHERE Id = @Id;
END;

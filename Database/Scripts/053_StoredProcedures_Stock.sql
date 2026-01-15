PRINT '=== Începere migrare 053_StoredProcedures_Stock ===';

-- =============================================
-- Stock Stored Procedures
-- =============================================

-- sp_Stock_UpdateQuantity
IF OBJECT_ID('dbo.sp_Stock_UpdateQuantity', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Stock_UpdateQuantity;
GO

CREATE PROCEDURE dbo.sp_Stock_UpdateQuantity
    @ItemId UNIQUEIDENTIFIER,
    @LocationId UNIQUEIDENTIFIER,
    @QuantityChange DECIMAL(18,2),
    @UserId UNIQUEIDENTIFIER,
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Check if stock record exists
    IF EXISTS (SELECT 1 FROM dbo.Stock WHERE ItemId = @ItemId AND LocationId = @LocationId)
    BEGIN
        -- Update existing stock
        UPDATE dbo.Stock
        SET Quantity = Quantity + @QuantityChange,
            UpdatedAt = GETDATE(),
            UpdatedBy = @UserId
        WHERE ItemId = @ItemId AND LocationId = @LocationId;
    END
    ELSE
    BEGIN
        -- Insert new stock record
        INSERT INTO dbo.Stock (
            Id, ItemId, LocationId, Quantity,
            CreatedBy, UpdatedBy,
            OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
        ) VALUES (
            NEWID(), @ItemId, @LocationId, @QuantityChange,
            @UserId, @UserId,
            @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
        );
    END
END
GO

-- sp_Stock_GetByItemAndLocation
IF OBJECT_ID('dbo.sp_Stock_GetByItemAndLocation', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Stock_GetByItemAndLocation;
GO

CREATE PROCEDURE dbo.sp_Stock_GetByItemAndLocation
    @ItemId UNIQUEIDENTIFIER,
    @LocationId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        ItemId,
        LocationId,
        Quantity,
        ReservedQuantity,
        IsActive,
        CreatedAt,
        CreatedBy,
        UpdatedAt,
        UpdatedBy,
        OwnerCompanyId,
        OwnerWorkPlaceId,
        OwnerLocationId
    FROM dbo.Stock
    WHERE ItemId = @ItemId
    AND LocationId = @LocationId
    AND IsActive = 1;
END
GO

-- sp_Stock_GetAll
IF OBJECT_ID('dbo.sp_Stock_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Stock_GetAll;
GO

CREATE PROCEDURE dbo.sp_Stock_GetAll
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.Id,
        s.ItemId,
        a.ArticolName as ItemName,
        s.LocationId,
        l.Name as LocationName,
        s.Quantity,
        s.ReservedQuantity,
        s.Quantity - s.ReservedQuantity as AvailableQuantity,
        s.IsActive,
        s.CreatedAt,
        s.CreatedBy,
        s.UpdatedAt,
        s.UpdatedBy
    FROM dbo.Stock s
    INNER JOIN dbo.Articole a ON s.ItemId = a.Id
    INNER JOIN dbo.Locations l ON s.LocationId = l.Id
    WHERE s.IsActive = 1
    AND (@OwnerCompanyId IS NULL OR s.OwnerCompanyId = @OwnerCompanyId)
    ORDER BY a.ArticolName, l.Name;
END
GO

PRINT '✅ Stored procedures pentru Stock create';

PRINT '=== Migrare 053_StoredProcedures_Stock completă ===';
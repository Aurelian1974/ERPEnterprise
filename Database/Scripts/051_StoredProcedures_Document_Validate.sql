-- =============================================
-- Document Validation Stored Procedure
-- =============================================

-- sp_Document_Validate
IF OBJECT_ID('dbo.sp_Document_Validate', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Document_Validate;
GO

CREATE PROCEDURE dbo.sp_Document_Validate
    @DocumentId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Check if document exists and is in draft state
        DECLARE @CurrentStateCode NVARCHAR(10);
        DECLARE @DocumentExists INT;

        SELECT @CurrentStateCode = ds.CodStare,
               @DocumentExists = 1
        FROM dbo.Document d
        INNER JOIN dbo.DocumentState ds ON d.DocumentStateId = ds.Id
        WHERE d.Id = @DocumentId AND d.IsActive = 1;

        IF @DocumentExists IS NULL
        BEGIN
            RAISERROR('Document not found or inactive', 16, 1);
            RETURN;
        END

        IF @CurrentStateCode != 'C'
        BEGIN
            RAISERROR('Document can only be validated from Draft state', 16, 1);
            RETURN;
        END

        -- Get the Valid state ID
        DECLARE @ValidStateId UNIQUEIDENTIFIER;
        SELECT @ValidStateId = Id FROM dbo.DocumentState WHERE CodStare = 'V' AND IsActive = 1;

        IF @ValidStateId IS NULL
        BEGIN
            RAISERROR('Valid state not found in DocumentState table', 16, 1);
            RETURN;
        END

        -- Update document state to Valid
        UPDATE dbo.Document
        SET DocumentStateId = @ValidStateId,
            UpdatedAt = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @DocumentId;

        -- Update stock for all stockable items in this document
        DECLARE @ItemId UNIQUEIDENTIFIER, @Quantity DECIMAL(18,2);

        DECLARE stock_cursor CURSOR FOR
        SELECT dd.ItemId, dd.Quantity
        FROM dbo.DocumentDetail dd
        INNER JOIN dbo.Articole a ON dd.ItemId = a.Id
        WHERE dd.DocumentId = @DocumentId AND a.IsStockable = 1;

        OPEN stock_cursor;
        FETCH NEXT FROM stock_cursor INTO @ItemId, @Quantity;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- Update stock quantity
            EXEC dbo.sp_Stock_UpdateQuantity
                @ItemId = @ItemId,
                @LocationId = @OwnerLocationId,
                @QuantityChange = @Quantity,  -- Positive for purchases (adding to stock)
                @UserId = @UserId,
                @OwnerCompanyId = @OwnerCompanyId,
                @OwnerWorkPlaceId = @OwnerWorkPlaceId,
                @OwnerLocationId = @OwnerLocationId;

            FETCH NEXT FROM stock_cursor INTO @ItemId, @Quantity;
        END

        CLOSE stock_cursor;
        DEALLOCATE stock_cursor;

        COMMIT TRANSACTION;

        -- Return success
        SELECT 1 as Success, 'Document validated successfully' as Message;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- Return error
        SELECT 0 as Success, ERROR_MESSAGE() as Message;

        THROW;
    END CATCH
END
GO
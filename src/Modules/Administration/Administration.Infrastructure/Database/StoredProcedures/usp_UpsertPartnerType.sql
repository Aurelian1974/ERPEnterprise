CREATE OR ALTER PROCEDURE administration.usp_UpsertPartnerType
    @PartnerTypeId           TINYINT         = NULL,
    @Code                    NVARCHAR(50),
    @Name                    NVARCHAR(100),
    @Description             NVARCHAR(500)   = NULL,
    @IsActive                BIT             = 1,
    @AffectsIssuedInvoices   BIT             = 0,
    @AffectsReceivedInvoices BIT             = 0,
    @SortOrder               SMALLINT        = 0,
    @UpdatedBy               NVARCHAR(100),
    @NewPartnerTypeId        TINYINT         OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Validare duplicat code
    IF EXISTS (
        SELECT 1
        FROM administration.partner_types
        WHERE code = @Code
          AND (@PartnerTypeId IS NULL OR partner_type_id <> @PartnerTypeId)
    )
    BEGIN
        RAISERROR(N'Codul ''%s'' există deja în nomenclatorul de tipuri parteneri.', 16, 1, @Code);
        RETURN;
    END;

    IF @PartnerTypeId IS NULL
    BEGIN
        INSERT INTO administration.partner_types
            (code, name, description, is_system, is_active,
             affects_issued_invoices, affects_received_invoices,
             sort_order, created_by, updated_by)
        VALUES
            (@Code, @Name, @Description, 0, @IsActive,
             @AffectsIssuedInvoices, @AffectsReceivedInvoices,
             @SortOrder, @UpdatedBy, @UpdatedBy);

        SET @NewPartnerTypeId = CAST(SCOPE_IDENTITY() AS TINYINT);
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM administration.partner_types WHERE partner_type_id = @PartnerTypeId AND is_system = 1)
        BEGIN
            -- Tipurile sistem: se permite modificarea doar a Name, Description, SortOrder, IsActive
            UPDATE administration.partner_types
            SET
                name        = @Name,
                description = @Description,
                is_active   = @IsActive,
                sort_order  = @SortOrder,
                updated_at  = SYSDATETIME(),
                updated_by  = @UpdatedBy
            WHERE partner_type_id = @PartnerTypeId;
        END
        ELSE
        BEGIN
            UPDATE administration.partner_types
            SET
                code                     = @Code,
                name                     = @Name,
                description              = @Description,
                is_active                = @IsActive,
                affects_issued_invoices  = @AffectsIssuedInvoices,
                affects_received_invoices = @AffectsReceivedInvoices,
                sort_order               = @SortOrder,
                updated_at               = SYSDATETIME(),
                updated_by               = @UpdatedBy
            WHERE partner_type_id = @PartnerTypeId;
        END;

        SET @NewPartnerTypeId = @PartnerTypeId;
    END;
END;

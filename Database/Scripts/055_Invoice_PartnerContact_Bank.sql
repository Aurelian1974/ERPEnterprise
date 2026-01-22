-- 055_Invoice_PartnerContact_Bank.sql
-- Migration: add PartnerContactId and PartnerBankAccountId to dbo.Invoice
-- Adds helper stored procedure to set those fields and provides templates to extend
-- existing stored procedures (sp_Document_InsertPurchaseInvoice, sp_Invoice_UpdateComplete)

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT 'Adding nullable columns PartnerContactId and PartnerBankAccountId to dbo.Invoice (if missing)';

IF COL_LENGTH('dbo.Invoice', 'PartnerContactId') IS NULL
BEGIN
    ALTER TABLE dbo.Invoice
    ADD PartnerContactId UNIQUEIDENTIFIER NULL;
    PRINT 'Added column PartnerContactId';
END
ELSE
    PRINT 'Column PartnerContactId already exists';

IF COL_LENGTH('dbo.Invoice', 'PartnerBankAccountId') IS NULL
BEGIN
    ALTER TABLE dbo.Invoice
    ADD PartnerBankAccountId UNIQUEIDENTIFIER NULL;
    PRINT 'Added column PartnerBankAccountId';
END
ELSE
    PRINT 'Column PartnerBankAccountId already exists';

-- Optional: create nonclustered indexes to speed lookups (skip if you prefer DBA migration policies)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Invoice') AND name = 'IX_Invoice_PartnerContact')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Invoice_PartnerContact ON dbo.Invoice(PartnerContactId);
    PRINT 'Created index IX_Invoice_PartnerContact';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Invoice') AND name = 'IX_Invoice_PartnerBankAccount')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Invoice_PartnerBankAccount ON dbo.Invoice(PartnerBankAccountId);
    PRINT 'Created index IX_Invoice_PartnerBankAccount';
END

GO

-- Helper stored procedure: update partner contact + bank account on an invoice
IF OBJECT_ID('dbo.sp_Invoice_SetPartnerContactAndBankAccount', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Invoice_SetPartnerContactAndBankAccount;
GO

CREATE PROCEDURE dbo.sp_Invoice_SetPartnerContactAndBankAccount
    @InvoiceId UNIQUEIDENTIFIER,
    @PartnerContactId UNIQUEIDENTIFIER = NULL,
    @PartnerBankAccountId UNIQUEIDENTIFIER = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Invoice
    SET
        PartnerContactId = @PartnerContactId,
        PartnerBankAccountId = @PartnerBankAccountId,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @InvoiceId;

    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT 'Created helper procedure dbo.sp_Invoice_SetPartnerContactAndBankAccount';

/*
  NOTE TO DBA/DEVELOPER:
  -----------------------
  The application code now passes @PartnerContactId and @PartnerBankAccountId to
  sp_Document_InsertPurchaseInvoice and to sp_Invoice_UpdateComplete. You must
  update those stored procedures to accept the new parameters and persist them
  into the dbo.Invoice table at insert/update time.

  Example parameter additions (add to the parameter list):
    @PartnerContactId UNIQUEIDENTIFIER = NULL,
    @PartnerBankAccountId UNIQUEIDENTIFIER = NULL,

  Example snippet to include when inserting into dbo.Invoice (add the columns to the insert list):

    -- columns list: ..., PartnerAddressFacturareId, PartnerContactId, PartnerBankAccountId, ...
    INSERT INTO dbo.Invoice (..., PartnerAddressFacturareId, PartnerContactId, PartnerBankAccountId, ...)
    VALUES (..., @PartnerAddressFacturareId, @PartnerContactId, @PartnerBankAccountId, ...);

  OR, if the master SP cannot be modified in place, you can call the helper proc after creating the invoice:

    DECLARE @NewInvoiceId UNIQUEIDENTIFIER = -- the invoice id returned/created by the SP;
    EXEC dbo.sp_Invoice_SetPartnerContactAndBankAccount @NewInvoiceId, @PartnerContactId, @PartnerBankAccountId, @UserId;

  Example for sp_Invoice_UpdateComplete: ensure the procedure accepts the parameters and when updating the invoice include
  PartnerContactId = @PartnerContactId, PartnerBankAccountId = @PartnerBankAccountId in the UPDATE statement.

*/

PRINT 'Migration script 055 completed. Review templates above and update master SPs accordingly.';
GO

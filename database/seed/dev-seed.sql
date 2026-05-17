-- Development seed data
-- Run AFTER schemas.sql and all migrations

USE ERPEnterprise;
GO

DECLARE @TenantId UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @CustomerId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @InvoiceId UNIQUEIDENTIFIER = '33333333-3333-3333-3333-333333333333';

-- Sample invoice (status: Draft)
INSERT INTO finance.invoices (id, tenant_id, customer_id, invoice_number, currency, status, due_date, created_at_utc)
VALUES (@InvoiceId, @TenantId, @CustomerId, 'INV-2026-000001', 'RON', 'Draft', '2026-06-30', GETUTCDATE());

INSERT INTO finance.invoice_lines (invoice_id, description, quantity, unit_price, vat_rate)
VALUES
    (@InvoiceId, 'Software Development Services', 10, 500.00, 19.00),
    (@InvoiceId, 'Technical Consulting', 5, 300.00, 19.00);

PRINT 'Dev seed data inserted.';

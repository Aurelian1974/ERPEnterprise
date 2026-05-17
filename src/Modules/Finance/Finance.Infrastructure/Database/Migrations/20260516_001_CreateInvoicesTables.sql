CREATE TABLE finance.invoices
(
    id               UNIQUEIDENTIFIER NOT NULL,
    tenant_id        UNIQUEIDENTIFIER NOT NULL,
    customer_id      UNIQUEIDENTIFIER NOT NULL,
    invoice_number   NVARCHAR(50)     NOT NULL,
    currency         CHAR(3)          NOT NULL,
    status           NVARCHAR(20)     NOT NULL,
    due_date         DATE             NOT NULL,
    created_at_utc   DATETIME2(7)     NOT NULL,
    approved_at_utc  DATETIME2(7)     NULL,
    paid_at_utc      DATETIME2(7)     NULL,
    CONSTRAINT PK_invoices PRIMARY KEY CLUSTERED (id),
    CONSTRAINT UQ_invoices_tenant_number UNIQUE (tenant_id, invoice_number)
);

CREATE INDEX IX_invoices_tenant_status
    ON finance.invoices (tenant_id, status);

CREATE INDEX IX_invoices_tenant_customer
    ON finance.invoices (tenant_id, customer_id);

CREATE TABLE finance.invoice_lines
(
    id           BIGINT IDENTITY(1,1) NOT NULL,
    invoice_id   UNIQUEIDENTIFIER     NOT NULL,
    description  NVARCHAR(500)        NOT NULL,
    quantity     DECIMAL(18,6)        NOT NULL,
    unit_price   DECIMAL(18,4)        NOT NULL,
    vat_rate     DECIMAL(5,2)         NOT NULL,
    CONSTRAINT PK_invoice_lines PRIMARY KEY CLUSTERED (id),
    CONSTRAINT FK_invoice_lines_invoices
        FOREIGN KEY (invoice_id) REFERENCES finance.invoices (id)
        ON DELETE CASCADE
);

CREATE INDEX IX_invoice_lines_invoice
    ON finance.invoice_lines (invoice_id);

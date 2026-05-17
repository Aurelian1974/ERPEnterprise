CREATE TABLE administration.partner_bank_accounts
(
    id BIGINT IDENTITY(1,1) NOT NULL,
    partner_id UNIQUEIDENTIFIER NOT NULL,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    iban NVARCHAR(34) NOT NULL,
    bank_name NVARCHAR(200) NOT NULL,
    currency NVARCHAR(3) NOT NULL CONSTRAINT DF_partner_bank_accounts_currency DEFAULT N'RON',
    is_default BIT NOT NULL CONSTRAINT DF_partner_bank_accounts_is_default DEFAULT 0,

    CONSTRAINT PK_partner_bank_accounts             PRIMARY KEY CLUSTERED (id),
    CONSTRAINT UQ_partner_bank_accounts_iban        UNIQUE (iban),
    CONSTRAINT FK_partner_bank_accounts_partners    FOREIGN KEY (partner_id)
        REFERENCES administration.partners (id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_partner_bank_accounts_partner_id
    ON administration.partner_bank_accounts (tenant_id, partner_id);
GO

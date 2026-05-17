CREATE TABLE administration.partner_addresses
(
    id BIGINT IDENTITY(1,1) NOT NULL,
    partner_id UNIQUEIDENTIFIER NOT NULL,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    address_type NVARCHAR(50) NOT NULL,
    street NVARCHAR(300) NOT NULL,
    city NVARCHAR(100) NOT NULL,
    county NVARCHAR(100) NULL,
    postal_code NVARCHAR(20) NULL,
    country NVARCHAR(100) NOT NULL CONSTRAINT DF_partner_addresses_country DEFAULT N'România',
    is_primary BIT NOT NULL CONSTRAINT DF_partner_addresses_is_primary DEFAULT 0,

    CONSTRAINT PK_partner_addresses                 PRIMARY KEY CLUSTERED (id),
    CONSTRAINT FK_partner_addresses_partners        FOREIGN KEY (partner_id)
        REFERENCES administration.partners (id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_partner_addresses_partner_id
    ON administration.partner_addresses (tenant_id, partner_id);
GO

CREATE TABLE administration.partner_contacts
(
    id BIGINT IDENTITY(1,1) NOT NULL,
    partner_id UNIQUEIDENTIFIER NOT NULL,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    full_name NVARCHAR(200) NOT NULL,
    position NVARCHAR(100) NULL,
    phone NVARCHAR(30) NULL,
    email NVARCHAR(200) NULL,
    is_primary BIT NOT NULL CONSTRAINT DF_partner_contacts_is_primary DEFAULT 0,

    CONSTRAINT PK_partner_contacts                  PRIMARY KEY CLUSTERED (id),
    CONSTRAINT FK_partner_contacts_partners         FOREIGN KEY (partner_id)
        REFERENCES administration.partners (id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_partner_contacts_partner_id
    ON administration.partner_contacts (tenant_id, partner_id);
GO

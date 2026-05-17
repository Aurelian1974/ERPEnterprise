CREATE TABLE administration.partners
(
    id UNIQUEIDENTIFIER NOT NULL,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    code NVARCHAR(20) NOT NULL,
    name NVARCHAR(200) NOT NULL,
    cui NVARCHAR(20) NULL,
    registration_number NVARCHAR(50) NULL,
    legal_form NVARCHAR(50) NULL,
    partner_type_id TINYINT NULL,
    is_vat_payer BIT NOT NULL CONSTRAINT DF_partners_is_vat_payer         DEFAULT 0,
    phone NVARCHAR(30) NULL,
    email NVARCHAR(200) NULL,
    is_active BIT NOT NULL CONSTRAINT DF_partners_is_active             DEFAULT 1,
    notes NVARCHAR(1000) NULL,
    created_at DATETIME2(7) NOT NULL CONSTRAINT DF_partners_created_at            DEFAULT SYSUTCDATETIME(),
    created_by UNIQUEIDENTIFIER NOT NULL,
    updated_at DATETIME2(7) NULL,
    updated_by UNIQUEIDENTIFIER NULL,

    CONSTRAINT PK_partners                  PRIMARY KEY CLUSTERED (id),
    CONSTRAINT UQ_partners_tenant_code      UNIQUE (tenant_id, code),
    CONSTRAINT FK_partners_partner_types    FOREIGN KEY (partner_type_id)
        REFERENCES administration.partner_types (partner_type_id)
);
GO

CREATE INDEX IX_partners_tenant_is_active
    ON administration.partners (tenant_id, is_active)
    INCLUDE (code, name, cui);
GO

CREATE INDEX IX_partners_tenant_name
    ON administration.partners (tenant_id, name);
GO

CREATE INDEX IX_partners_tenant_cui
    ON administration.partners (tenant_id, cui);
GO

CREATE TABLE administration.partner_types
(
    partner_type_id TINYINT NOT NULL IDENTITY(1,1),
    code NVARCHAR(50) NOT NULL,
    name NVARCHAR(100) NOT NULL,
    description NVARCHAR(500) NULL,
    is_system BIT NOT NULL CONSTRAINT DF_partner_types_is_system                  DEFAULT 0,
    is_active BIT NOT NULL CONSTRAINT DF_partner_types_is_active                  DEFAULT 1,
    affects_issued_invoices BIT NOT NULL CONSTRAINT DF_partner_types_affects_issued_invoices    DEFAULT 0,
    affects_received_invoices BIT NOT NULL CONSTRAINT DF_partner_types_affects_received_invoices  DEFAULT 0,
    sort_order SMALLINT NOT NULL CONSTRAINT DF_partner_types_sort_order                 DEFAULT 0,
    created_at DATETIME2(0) NOT NULL CONSTRAINT DF_partner_types_created_at                 DEFAULT SYSDATETIME(),
    created_by NVARCHAR(100) NOT NULL CONSTRAINT DF_partner_types_created_by                 DEFAULT SYSTEM_USER,
    updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_partner_types_updated_at                 DEFAULT SYSDATETIME(),
    updated_by NVARCHAR(100) NOT NULL CONSTRAINT DF_partner_types_updated_by                 DEFAULT SYSTEM_USER,

    CONSTRAINT PK_partner_types        PRIMARY KEY CLUSTERED (partner_type_id),
    CONSTRAINT UQ_partner_types_code   UNIQUE (code)
);

SET IDENTITY_INSERT administration.partner_types ON;

INSERT INTO administration.partner_types
    (partner_type_id, code, name, description, is_system, is_active,
    affects_issued_invoices, affects_received_invoices, sort_order)
VALUES
    (1, 'CLIENT', N'Client', N'Partener căruia i se emit facturi.', 1, 1, 1, 0, 10),
    (2, 'VENDOR', N'Furnizor', N'Partener de la care se primesc facturi.', 1, 1, 0, 1, 20),
    (3, 'INDIVIDUAL', N'Persoană Fizică', N'Persoană fizică (fără CUI). Poate fi Client/Furnizor.', 1, 1, 0, 0, 30),
    (4, 'BANK', N'Bancă', N'Instituție bancară.', 1, 1, 0, 1, 40),
    (5, 'NGO', N'ONG', N'Organizație non-guvernamentală.', 1, 1, 1, 1, 50),
    (6, 'PUBLIC_INSTITUTION', N'Instituție Publică', N'Instituție publică (bugetară).', 1, 1, 1, 1, 60);

SET IDENTITY_INSERT administration.partner_types OFF;

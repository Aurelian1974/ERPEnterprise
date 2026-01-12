-- =============================================
-- ValyanERP - Partners Tables
-- Script: 020_Partners.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Tabele pentru gestionarea partenerilor de afaceri
--            (Furnizori, Clienți, PF, PJ, Instituții, etc.)
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 020_Partners ===';
GO

-- =============================================
-- 1. TABEL PRINCIPAL: Partners
-- =============================================
IF OBJECT_ID('dbo.Partners', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Partners] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        
        -- Cod intern unic (auto-generat: P-000001, P-000002...)
        [Cod] NVARCHAR(20) NOT NULL,
        
        -- Clasificare
        [Categoria] TINYINT NOT NULL, -- 0=PF, 1=PFA, 2=SC, 3=NP, 4=IP, 5=DIP, 6=OI, 7=STR, 8=SP, 9=ALT
        [TipEntitate] NVARCHAR(20) NOT NULL, -- Subtip detaliat: SRL, PFA_STD, PF_RO, etc.
        [RolPartener] INT NOT NULL DEFAULT 0, -- FLAGS: 1=Furnizor, 2=Client, 4=Colaborator, 8=Angajat, 16=Asociat
        
        -- Identificare (unul din acestea obligatoriu)
        [Denumire] NVARCHAR(200) NULL, -- Pentru PJ
        [DenumireScurta] NVARCHAR(50) NULL,
        [Nume] NVARCHAR(100) NULL, -- Pentru PF
        [Prenume] NVARCHAR(100) NULL, -- Pentru PF
        
        -- Identificatori fiscali (mutual exclusive după tip)
        [CNP] NVARCHAR(13) NULL,
        [CUI] NVARCHAR(20) NULL, -- Include prefix RO pentru plătitori TVA
        [CIF] NVARCHAR(20) NULL, -- Cod Identificare Fiscală (instituții, non-profit)
        [VATID] NVARCHAR(20) NULL, -- Pentru entități străine UE
        [CodFiscalStrain] NVARCHAR(50) NULL, -- Pentru entități non-UE
        [RegCom] NVARCHAR(50) NULL, -- J../.../.....
        [NrAutorizatie] NVARCHAR(50) NULL, -- Pentru PFA, cabinete
        [Pasaport] NVARCHAR(50) NULL, -- Pentru PF nerezidente
        
        -- Date specifice
        [DataInregistrare] DATE NULL, -- Data înființării/înregistrării
        [DataRadiere] DATE NULL, -- Dacă e radiat/inactiv
        [TaraOrigine] NVARCHAR(3) NOT NULL DEFAULT N'RO', -- Cod ISO (RO, DE, US, etc.)
        [CAENPrincipal] NVARCHAR(10) NULL,
        [CapitalSocial] DECIMAL(18,2) NULL,
        
        -- Contact
        [Email] NVARCHAR(256) NULL,
        [Telefon] NVARCHAR(20) NULL,
        [TelefonSecundar] NVARCHAR(20) NULL,
        [Website] NVARCHAR(200) NULL,
        [LogoUrl] NVARCHAR(500) NULL,
        
        -- Status TVA
        [EstePlătitorTVA] BIT NOT NULL DEFAULT 0,
        [DataInregistrareTVA] DATE NULL,
        [StatusSplitTVA] BIT NOT NULL DEFAULT 0,
        
        -- Status operațional
        [PartnerStatus] VARCHAR(20) NOT NULL DEFAULT 'Activ',
        [EsteActiv] BIT NOT NULL DEFAULT 1,
        [BlocatFacturare] BIT NOT NULL DEFAULT 0,
        [BlocatLivrare] BIT NOT NULL DEFAULT 0,
        [MotivBlocare] NVARCHAR(500) NULL,
        
        -- Verificare ANAF (cu referință la cache)
        [EsteVerificat] BIT NOT NULL DEFAULT 0,
        [AnafStatus] VARCHAR(20) NULL, -- 'valid', 'inactiv', 'radiat'
        [AnafVerifiedAt] DATETIME2 NULL,
        [AnafVerifiedBy] UNIQUEIDENTIFIER NULL,
        
        -- Credit și clasificare comercială
        [LimitaCredit] DECIMAL(18,2) NULL,
        [TermenPlataDef] INT NULL DEFAULT 30, -- Zile
        [CategorieComercială] VARCHAR(20) NULL, -- 'Standard', 'Premium', 'VIP'
        
        -- SAF-T Romania (obligatoriu pentru export)
        [CodPartenerSAFT] VARCHAR(35) NULL,
        [TipPartenerSAFT] VARCHAR(10) NULL, -- 'C'=Client, 'S'=Supplier, 'O'=Other, 'CS'=Both
        [SupplierID] VARCHAR(35) NULL,
        [CustomerID] VARCHAR(35) NULL,
        
        -- Identificator temporar (pentru entități fără CUI/CNP)
        [IdentificatorTemp] VARCHAR(50) NULL,
        [MotivLipsaIdentificator] NVARCHAR(200) NULL,
        
        -- Adresa principală (denormalizat pentru performanță)
        [AdresaPrincipalaId] UNIQUEIDENTIFIER NULL,
        
        -- Note
        [Observatii] NVARCHAR(2000) NULL,
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [UQ_Partners_Cod] UNIQUE ([Cod]),
        CONSTRAINT [FK_Partners_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_Partners_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_Partners_AnafVerifiedBy] FOREIGN KEY ([AnafVerifiedBy]) REFERENCES [dbo].[Users]([Id]),
        
        -- Validare status
        CONSTRAINT [CK_Partners_Status] CHECK ([PartnerStatus] IN ('Activ', 'Inactiv', 'Blocat', 'Suspendat', 'Radiat')),
        
        -- Validare categorie comercială
        CONSTRAINT [CK_Partners_CategorieComercială] CHECK ([CategorieComercială] IS NULL OR [CategorieComercială] IN ('Standard', 'Premium', 'VIP')),
        
        -- Cel puțin un identificator trebuie să existe
        CONSTRAINT [CK_Partners_HasIdentifier] CHECK (
            [Denumire] IS NOT NULL OR 
            [Nume] IS NOT NULL OR 
            [CUI] IS NOT NULL OR 
            [CIF] IS NOT NULL OR 
            [CNP] IS NOT NULL OR 
            [VATID] IS NOT NULL OR 
            [CodFiscalStrain] IS NOT NULL OR
            [IdentificatorTemp] IS NOT NULL
        )
    );
    
    PRINT '✅ Tabel Partners creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel Partners există deja';
END
GO

-- =============================================
-- 1.1 Indexuri pentru Partners
-- =============================================

-- Unicitate CUI (dacă e completat)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Partners_CUI')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CUI] 
    ON [Partners] ([CUI]) WHERE [CUI] IS NOT NULL;
    PRINT '✅ Index UQ_Partners_CUI creat';
END
GO

-- Unicitate CNP (dacă e completat)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Partners_CNP')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CNP] 
    ON [Partners] ([CNP]) WHERE [CNP] IS NOT NULL;
    PRINT '✅ Index UQ_Partners_CNP creat';
END
GO

-- Unicitate CIF (dacă e completat)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Partners_CIF')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CIF] 
    ON [Partners] ([CIF]) WHERE [CIF] IS NOT NULL;
    PRINT '✅ Index UQ_Partners_CIF creat';
END
GO

-- Unicitate VAT ID (dacă e completat)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Partners_VATID')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_VATID] 
    ON [Partners] ([VATID]) WHERE [VATID] IS NOT NULL;
    PRINT '✅ Index UQ_Partners_VATID creat';
END
GO

-- Indexuri pentru căutare
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_Categoria')
BEGIN
    CREATE INDEX [IX_Partners_Categoria] ON [Partners] ([Categoria]) 
    INCLUDE ([Denumire], [Nume], [Prenume], [RolPartener], [EsteActiv]);
    PRINT '✅ Index IX_Partners_Categoria creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_TipEntitate')
BEGIN
    CREATE INDEX [IX_Partners_TipEntitate] ON [Partners] ([TipEntitate]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_Partners_TipEntitate creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_RolPartener')
BEGIN
    CREATE INDEX [IX_Partners_RolPartener] ON [Partners] ([RolPartener]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_Partners_RolPartener creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_Denumire')
BEGIN
    CREATE INDEX [IX_Partners_Denumire] ON [Partners] ([Denumire]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_Partners_Denumire creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_NumePrenume')
BEGIN
    CREATE INDEX [IX_Partners_NumePrenume] ON [Partners] ([Nume], [Prenume]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_Partners_NumePrenume creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_Status')
BEGIN
    CREATE INDEX [IX_Partners_Status] ON [Partners] ([PartnerStatus], [EsteActiv]) 
    INCLUDE ([Denumire], [Nume], [Prenume], [CUI]);
    PRINT '✅ Index IX_Partners_Status creat';
END
GO

-- Indexuri pentru căutări exacte (identificatori)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_CUI_Search')
BEGIN
    CREATE INDEX [IX_Partners_CUI_Search] ON [Partners] ([CUI]) 
    INCLUDE ([Id], [Denumire]) WHERE [IsActive] = 1;
    PRINT '✅ Index IX_Partners_CUI_Search creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_CNP_Search')
BEGIN
    CREATE INDEX [IX_Partners_CNP_Search] ON [Partners] ([CNP]) 
    INCLUDE ([Id], [Nume], [Prenume]) WHERE [IsActive] = 1;
    PRINT '✅ Index IX_Partners_CNP_Search creat';
END
GO

-- Index pentru ANAF verification status
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_AnafStatus')
BEGIN
    CREATE INDEX [IX_Partners_AnafStatus] ON [Partners] ([EsteVerificat], [AnafVerifiedAt]) 
    INCLUDE ([CUI], [AnafStatus]);
    PRINT '✅ Index IX_Partners_AnafStatus creat';
END
GO

-- Index pentru IsActive cu date comune
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_IsActive')
BEGIN
    CREATE INDEX [IX_Partners_IsActive] ON [Partners] ([IsActive]) 
    INCLUDE ([Cod], [Categoria], [TipEntitate], [Denumire], [Nume], [Prenume], [CUI], [RolPartener]);
    PRINT '✅ Index IX_Partners_IsActive creat';
END
GO

-- Index pentru SAF-T
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_SAFT')
BEGIN
    CREATE INDEX [IX_Partners_SAFT] ON [Partners] ([CodPartenerSAFT]) 
    WHERE [CodPartenerSAFT] IS NOT NULL;
    PRINT '✅ Index IX_Partners_SAFT creat';
END
GO

-- =============================================
-- 2. TABEL: PartnerAddresses (Adrese multiple)
-- =============================================
IF OBJECT_ID('dbo.PartnerAddresses', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PartnerAddresses] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [PartnerId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Tip adresă
        [TipAdresa] TINYINT NOT NULL DEFAULT 0, -- 0=Sediu, 1=Corespondenta, 2=Livrare, 3=Facturare
        [Denumire] NVARCHAR(100) NULL, -- Denumire opțională (ex: "Depozit București")
        
        -- Adresa completă
        [Adresa] NVARCHAR(500) NOT NULL, -- Stradă, nr, bloc, scara, ap
        [Localitate] NVARCHAR(100) NOT NULL,
        [Judet] NVARCHAR(50) NOT NULL,
        [CodPostal] NVARCHAR(10) NULL,
        [Tara] NVARCHAR(50) NOT NULL DEFAULT N'România',
        [CodTaraISO] NVARCHAR(3) NOT NULL DEFAULT N'RO',
        
        -- Coordonate GPS (opțional, pentru livrări)
        [Latitudine] DECIMAL(10, 8) NULL,
        [Longitudine] DECIMAL(11, 8) NULL,
        
        -- Contact la adresă
        [Telefon] NVARCHAR(20) NULL,
        [Email] NVARCHAR(256) NULL,
        [PersoanaContact] NVARCHAR(200) NULL,
        
        -- Flags
        [EstePrincipala] BIT NOT NULL DEFAULT 0, -- Adresa principală afișată în grid
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_PartnerAddresses_Partner] FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[Partners]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PartnerAddresses_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_PartnerAddresses_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
        
        -- Validare tip adresă
        CONSTRAINT [CK_PartnerAddresses_TipAdresa] CHECK ([TipAdresa] IN (0, 1, 2, 3))
    );
    
    PRINT '✅ Tabel PartnerAddresses creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel PartnerAddresses există deja';
END
GO

-- Adaugă FK pentru AdresaPrincipalaId în Partners
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Partners_AdresaPrincipala')
BEGIN
    ALTER TABLE [dbo].[Partners] ADD CONSTRAINT [FK_Partners_AdresaPrincipala] 
    FOREIGN KEY ([AdresaPrincipalaId]) REFERENCES [dbo].[PartnerAddresses]([Id]);
    PRINT '✅ FK Partners.AdresaPrincipalaId adăugat';
END
GO

-- Indexuri pentru PartnerAddresses
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerAddresses_PartnerId')
BEGIN
    CREATE INDEX [IX_PartnerAddresses_PartnerId] ON [PartnerAddresses] ([PartnerId]) 
    INCLUDE ([TipAdresa], [Adresa], [Localitate], [Judet], [EstePrincipala], [IsActive]);
    PRINT '✅ Index IX_PartnerAddresses_PartnerId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerAddresses_Principal')
BEGIN
    CREATE INDEX [IX_PartnerAddresses_Principal] ON [PartnerAddresses] ([PartnerId], [EstePrincipala]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_PartnerAddresses_Principal creat';
END
GO

-- =============================================
-- 3. TABEL: PartnerContacts (Persoane de contact)
-- =============================================
IF OBJECT_ID('dbo.PartnerContacts', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PartnerContacts] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [PartnerId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Date contact
        [Nume] NVARCHAR(100) NOT NULL,
        [Prenume] NVARCHAR(100) NULL,
        [Functie] NVARCHAR(100) NULL, -- Director, Manager, Contabil, etc.
        [Departament] NVARCHAR(100) NULL,
        
        -- Contact
        [Email] NVARCHAR(256) NULL,
        [Telefon] NVARCHAR(20) NULL,
        [TelefonMobil] NVARCHAR(20) NULL,
        
        -- Flags
        [EsteDecident] BIT NOT NULL DEFAULT 0, -- Persoană cu putere de decizie
        [EstePrincipal] BIT NOT NULL DEFAULT 0, -- Contact principal
        
        -- Note
        [Observatii] NVARCHAR(500) NULL,
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_PartnerContacts_Partner] FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[Partners]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PartnerContacts_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_PartnerContacts_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id])
    );
    
    PRINT '✅ Tabel PartnerContacts creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel PartnerContacts există deja';
END
GO

-- Indexuri pentru PartnerContacts
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerContacts_PartnerId')
BEGIN
    CREATE INDEX [IX_PartnerContacts_PartnerId] ON [PartnerContacts] ([PartnerId]) 
    INCLUDE ([Nume], [Prenume], [Functie], [Email], [Telefon], [EstePrincipal], [IsActive]);
    PRINT '✅ Index IX_PartnerContacts_PartnerId creat';
END
GO

-- =============================================
-- 4. TABEL: PartnerBankAccounts (Conturi bancare)
-- =============================================
IF OBJECT_ID('dbo.PartnerBankAccounts', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PartnerBankAccounts] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [PartnerId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Date cont
        [IBAN] NVARCHAR(34) NOT NULL, -- Format IBAN internațional (max 34 caractere)
        [BIC] NVARCHAR(11) NULL, -- SWIFT/BIC code
        [NumeBanca] NVARCHAR(100) NOT NULL,
        [Sucursala] NVARCHAR(100) NULL,
        [Moneda] NVARCHAR(3) NOT NULL DEFAULT N'RON', -- ISO 4217
        
        -- Titular (poate diferi de partener)
        [TitularCont] NVARCHAR(200) NULL,
        
        -- Flags
        [EstePrincipal] BIT NOT NULL DEFAULT 0, -- Contul principal pentru plăți
        
        -- Note
        [Observatii] NVARCHAR(500) NULL,
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_PartnerBankAccounts_Partner] FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[Partners]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PartnerBankAccounts_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_PartnerBankAccounts_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id])
    );
    
    PRINT '✅ Tabel PartnerBankAccounts creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel PartnerBankAccounts există deja';
END
GO

-- Indexuri pentru PartnerBankAccounts
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerBankAccounts_PartnerId')
BEGIN
    CREATE INDEX [IX_PartnerBankAccounts_PartnerId] ON [PartnerBankAccounts] ([PartnerId]) 
    INCLUDE ([IBAN], [NumeBanca], [Moneda], [EstePrincipal], [IsActive]);
    PRINT '✅ Index IX_PartnerBankAccounts_PartnerId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerBankAccounts_IBAN')
BEGIN
    CREATE INDEX [IX_PartnerBankAccounts_IBAN] ON [PartnerBankAccounts] ([IBAN]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_PartnerBankAccounts_IBAN creat';
END
GO

-- =============================================
-- 5. TABEL: PartnerRepresentatives (Reprezentanți legali/administratori)
-- =============================================
IF OBJECT_ID('dbo.PartnerRepresentatives', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PartnerRepresentatives] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [PartnerId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Legătură cu Persoane (opțional - dacă e înregistrat în sistem)
        [PersoanaId] UNIQUEIDENTIFIER NULL,
        
        -- Date reprezentant (obligatorii dacă nu e legat de Persoane)
        [Nume] NVARCHAR(100) NOT NULL,
        [Prenume] NVARCHAR(100) NULL,
        [CNP] NVARCHAR(13) NULL,
        
        -- Funcție și tip
        [Functie] NVARCHAR(100) NOT NULL, -- Administrator, Asociat, Director, etc.
        [TipReprezentant] TINYINT NOT NULL DEFAULT 0, -- 0=Administrator, 1=Asociat, 2=Împuternicit, 3=ReprezentantLegal
        
        -- Putere de semnătură (pentru documente)
        [ArePutereSemnatura] BIT NOT NULL DEFAULT 0,
        [LimitaSemnatura] DECIMAL(18,2) NULL, -- Limită valorică pentru semnătură
        
        -- Perioada
        [DataNumire] DATE NULL,
        [DataExpirare] DATE NULL,
        
        -- Contact
        [Email] NVARCHAR(256) NULL,
        [Telefon] NVARCHAR(20) NULL,
        
        -- Note
        [Observatii] NVARCHAR(500) NULL,
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_PartnerRepresentatives_Partner] FOREIGN KEY ([PartnerId]) REFERENCES [dbo].[Partners]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PartnerRepresentatives_Persoana] FOREIGN KEY ([PersoanaId]) REFERENCES [dbo].[Persoane]([Id]),
        CONSTRAINT [FK_PartnerRepresentatives_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_PartnerRepresentatives_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
        
        -- Validare tip reprezentant
        CONSTRAINT [CK_PartnerRepresentatives_TipReprezentant] CHECK ([TipReprezentant] IN (0, 1, 2, 3))
    );
    
    PRINT '✅ Tabel PartnerRepresentatives creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel PartnerRepresentatives există deja';
END
GO

-- Indexuri pentru PartnerRepresentatives
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerRepresentatives_PartnerId')
BEGIN
    CREATE INDEX [IX_PartnerRepresentatives_PartnerId] ON [PartnerRepresentatives] ([PartnerId]) 
    INCLUDE ([Nume], [Prenume], [Functie], [TipReprezentant], [IsActive]);
    PRINT '✅ Index IX_PartnerRepresentatives_PartnerId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartnerRepresentatives_PersoanaId')
BEGIN
    CREATE INDEX [IX_PartnerRepresentatives_PersoanaId] ON [PartnerRepresentatives] ([PersoanaId]) 
    WHERE [PersoanaId] IS NOT NULL;
    PRINT '✅ Index IX_PartnerRepresentatives_PersoanaId creat';
END
GO

-- =============================================
-- 6. TABEL: AnafVerificationCache (Cache verificări ANAF)
-- =============================================
IF OBJECT_ID('dbo.AnafVerificationCache', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AnafVerificationCache] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [CUI] NVARCHAR(20) NOT NULL,
        
        -- Rezultat verificare
        [Denumire] NVARCHAR(200) NULL,
        [Adresa] NVARCHAR(500) NULL,
        [NrRegCom] NVARCHAR(50) NULL,
        [CodPostal] NVARCHAR(10) NULL,
        [Telefon] NVARCHAR(50) NULL,
        [Fax] NVARCHAR(50) NULL,
        
        -- Status TVA
        [ScpTVA] BIT NOT NULL DEFAULT 0, -- Înregistrat în scopuri TVA
        [DataInregistrareTVA] DATE NULL,
        [DataAnulareTVA] DATE NULL,
        [StatusTVA] VARCHAR(50) NULL,
        [StatusTVAMessage] NVARCHAR(500) NULL,
        
        -- Status Split TVA
        [StatusSplitTVA] BIT NOT NULL DEFAULT 0,
        [DataInceputSplitTVA] DATE NULL,
        [DataAnulareSplitTVA] DATE NULL,
        
        -- Status Inactivi
        [StatusInactivi] BIT NOT NULL DEFAULT 0, -- TRUE = inactiv fiscal
        [DataInactivare] DATE NULL,
        [DataReactivare] DATE NULL,
        [StatusInactiviMessage] NVARCHAR(500) NULL,
        
        -- Status RO e-Factura
        [StatusRoEFactura] BIT NOT NULL DEFAULT 0,
        [DataInceputRoEFactura] DATE NULL,
        
        -- Raw response (pentru debugging)
        [RawResponse] NVARCHAR(MAX) NULL,
        
        -- Metadata
        [DataInterogare] DATE NOT NULL, -- Data pentru care s-a făcut verificarea
        [VerifiedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [ExpiresAt] DATETIME2 NOT NULL, -- Cache expiration (default 24h)
        [Source] VARCHAR(20) NOT NULL DEFAULT 'ANAF', -- ANAF, VIES, Manual
        
        -- Audit
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_AnafVerificationCache_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id])
    );
    
    PRINT '✅ Tabel AnafVerificationCache creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel AnafVerificationCache există deja';
END
GO

-- Indexuri pentru AnafVerificationCache
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnafVerificationCache_CUI')
BEGIN
    CREATE INDEX [IX_AnafVerificationCache_CUI] ON [AnafVerificationCache] ([CUI], [DataInterogare]) 
    INCLUDE ([ScpTVA], [StatusInactivi], [ExpiresAt]);
    PRINT '✅ Index IX_AnafVerificationCache_CUI creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnafVerificationCache_ExpiresAt')
BEGIN
    -- Index non-filtrat pentru cleanup job (filtrul se aplică în query)
    CREATE INDEX [IX_AnafVerificationCache_ExpiresAt] ON [AnafVerificationCache] ([ExpiresAt]);
    PRINT '✅ Index IX_AnafVerificationCache_ExpiresAt creat';
END
GO

-- =============================================
-- 7. VIEW: vw_Partners (Pentru grid și căutări)
-- =============================================
IF OBJECT_ID('dbo.vw_Partners', 'V') IS NOT NULL
    DROP VIEW dbo.vw_Partners;
GO

CREATE VIEW [dbo].[vw_Partners] AS
SELECT 
    p.Id,
    p.Cod,
    p.Categoria,
    p.TipEntitate,
    p.RolPartener,
    p.PartnerStatus,
    
    -- Denumire afișare (PJ sau PF)
    CASE 
        WHEN p.Denumire IS NOT NULL THEN p.Denumire
        ELSE CONCAT(p.Nume, ' ', p.Prenume)
    END AS DenumireAfisare,
    p.Denumire,
    p.DenumireScurta,
    p.Nume,
    p.Prenume,
    
    -- Identificator fiscal principal
    COALESCE(p.CUI, p.CIF, p.CNP, p.VATID, p.CodFiscalStrain, p.IdentificatorTemp) AS IdentificatorFiscal,
    p.CUI,
    p.CIF,
    p.CNP,
    p.VATID,
    p.RegCom,
    
    -- Contact
    p.Email,
    p.Telefon,
    p.Website,
    p.TaraOrigine,
    
    -- Status
    p.EstePlătitorTVA,
    p.EsteActiv,
    p.EsteVerificat,
    p.AnafStatus,
    p.AnafVerifiedAt,
    p.BlocatFacturare,
    p.BlocatLivrare,
    
    -- Credit
    p.LimitaCredit,
    p.TermenPlataDef,
    p.CategorieComercială,
    
    -- SAF-T
    p.CodPartenerSAFT,
    p.TipPartenerSAFT,
    
    -- Audit
    p.IsActive,
    p.CreatedAt,
    p.UpdatedAt,
    
    -- Adresa principală cu FALLBACK logic:
    -- Principală → Sediu → Facturare → Corespondență → Prima adresă
    addr.Adresa AS AdresaPrincipala,
    addr.Localitate,
    addr.Judet,
    addr.CodPostal,
    addr.Tara,
    
    -- Statistici (pentru afișare în grid)
    (SELECT COUNT(*) FROM PartnerAddresses WHERE PartnerId = p.Id AND IsActive = 1) AS NrAdrese,
    (SELECT COUNT(*) FROM PartnerContacts WHERE PartnerId = p.Id AND IsActive = 1) AS NrContacte,
    (SELECT COUNT(*) FROM PartnerBankAccounts WHERE PartnerId = p.Id AND IsActive = 1) AS NrConturi,
    (SELECT COUNT(*) FROM PartnerRepresentatives WHERE PartnerId = p.Id AND IsActive = 1) AS NrReprezentanti,
    
    -- User care a creat
    uc.UserName AS CreatedByUserName
FROM [dbo].[Partners] p

-- Fallback logic pentru adresa principală
LEFT JOIN [dbo].[PartnerAddresses] addr ON addr.PartnerId = p.Id 
    AND addr.IsActive = 1
    AND addr.Id = (
        SELECT TOP 1 a.Id 
        FROM [dbo].[PartnerAddresses] a
        WHERE a.PartnerId = p.Id AND a.IsActive = 1
        ORDER BY 
            a.EstePrincipala DESC,         -- 1. Cea marcată ca principală
            CASE a.TipAdresa 
                WHEN 0 THEN 1              -- 2. Sediu
                WHEN 3 THEN 2              -- 3. Facturare
                WHEN 1 THEN 3              -- 4. Corespondență
                WHEN 2 THEN 4              -- 5. Livrare
                ELSE 5 
            END,
            a.CreatedAt ASC                 -- 6. Prima adăugată
    )

LEFT JOIN [dbo].[Users] uc ON p.CreatedBy = uc.Id;
GO

PRINT '✅ View vw_Partners creat cu succes';
GO

-- =============================================
-- 8. SEQUENCE pentru generare cod partener (P-000001)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'seq_PartnerCode')
BEGIN
    CREATE SEQUENCE [dbo].[seq_PartnerCode]
    AS INT
    START WITH 1
    INCREMENT BY 1
    MINVALUE 1
    NO MAXVALUE
    NO CYCLE
    CACHE 10;
    
    PRINT '✅ Sequence seq_PartnerCode creată';
END
GO

-- =============================================
-- 9. PROCEDURĂ STORED pentru generare cod partener
-- (NEXT VALUE FOR nu poate fi folosit în funcții scalare)
-- =============================================
IF OBJECT_ID('dbo.sp_Partners_GenerateCode', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Partners_GenerateCode;
GO

CREATE PROCEDURE [dbo].[sp_Partners_GenerateCode]
    @Code NVARCHAR(20) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NextVal INT;
    
    -- Obține următoarea valoare din secvență
    SET @NextVal = NEXT VALUE FOR [dbo].[seq_PartnerCode];
    
    -- Formatează codul: P-000001
    SET @Code = 'P-' + RIGHT('000000' + CAST(@NextVal AS NVARCHAR(10)), 6);
END;
GO

PRINT '✅ Procedură sp_Partners_GenerateCode creată';
GO

-- =============================================
-- 10. FULL-TEXT SEARCH (pentru text liber - NU identificatori!)
-- =============================================
-- Verificăm dacă Full-Text Search e disponibil
IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'PartnersCatalog')
BEGIN
    PRINT '⏭️ Full-Text Catalog PartnersCatalog există deja';
END
ELSE
BEGIN
    -- Creează catalog doar dacă Full-Text e instalat
    IF (SELECT SERVERPROPERTY('IsFullTextInstalled')) = 1
    BEGIN
        CREATE FULLTEXT CATALOG [PartnersCatalog] AS DEFAULT;
        PRINT '✅ Full-Text Catalog PartnersCatalog creat';
    END
    ELSE
    BEGIN
        PRINT '⚠️ Full-Text Search nu este instalat pe acest server';
    END
END
GO

-- Full-text index pe Partners (doar text liber, NU identificatori!)
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Partners'))
BEGIN
    IF (SELECT SERVERPROPERTY('IsFullTextInstalled')) = 1
    BEGIN
        -- Găsim primary key index
        DECLARE @PKIndexName NVARCHAR(128);
        SELECT @PKIndexName = i.name
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID('dbo.Partners') AND i.is_primary_key = 1;
        
        IF @PKIndexName IS NOT NULL
        BEGIN
            EXEC('CREATE FULLTEXT INDEX ON [dbo].[Partners] ([Denumire], [DenumireScurta], [Nume], [Prenume], [Observatii]) KEY INDEX [' + @PKIndexName + '] ON [PartnersCatalog]');
            PRINT '✅ Full-Text Index pe Partners creat';
        END
    END
END
GO

-- =============================================
-- FINALIZARE
-- =============================================
PRINT '';
PRINT '=== Migrare 020_Partners finalizată cu succes! ===';
PRINT '';
PRINT 'Tabele create:';
PRINT '  ✅ Partners (tabel principal)';
PRINT '  ✅ PartnerAddresses (adrese multiple)';
PRINT '  ✅ PartnerContacts (persoane de contact)';
PRINT '  ✅ PartnerBankAccounts (conturi bancare)';
PRINT '  ✅ PartnerRepresentatives (reprezentanți legali)';
PRINT '  ✅ AnafVerificationCache (cache verificări ANAF)';
PRINT '';
PRINT 'View-uri create:';
PRINT '  ✅ vw_Partners (pentru grid cu fallback adresă)';
PRINT '';
PRINT 'Funcții create:';
PRINT '  ✅ fn_GeneratePartnerCode (generare cod P-000001)';
PRINT '';
GO

-- =============================================
-- ValyanERP - Update vw_Partners cu Owner Names
-- Script: 029_Partners_AddOwnerNames_View.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Adaugă OwnerCompanyName și OwnerLocationName în vw_Partners
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 029_Partners_AddOwnerNames_View ===';
GO

-- =============================================
-- 1. Recreate vw_Partners cu Owner Names
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
    p.EstePlatitorTVA,
    p.EsteActiv,
    p.EsteVerificat,
    p.AnafStatus,
    p.AnafVerifiedAt,
    p.BlocatFacturare,
    p.BlocatLivrare,
    
    -- Credit
    p.LimitaCredit,
    p.TermenPlataDef,
    p.CategorieComercialaTxt,
    
    -- SAF-T
    p.CodPartenerSAFT,
    p.TipPartenerSAFT,
    
    -- Audit
    p.IsActive,
    p.CreatedAt,
    p.UpdatedAt,
    
    -- Ownership - Owner entity IDs
    p.OwnerCompanyId,
    p.OwnerWorkPlaceId,
    p.OwnerLocationId,
    
    -- Ownership - Owner entity names (populated by JOINs)
    oc.Denumire AS OwnerCompanyName,
    ol.Denumire AS OwnerLocationName,
    
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

-- Join pentru Owner Company (entitatea care deține partenerul)
LEFT JOIN [dbo].[Companies] oc ON p.OwnerCompanyId = oc.Id

-- Join pentru Owner Location (locația care deține partenerul)
LEFT JOIN [dbo].[Locations] ol ON p.OwnerLocationId = ol.Id

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

PRINT '✅ View vw_Partners actualizat cu OwnerCompanyName și OwnerLocationName';
GO

PRINT '=== Migrare 029_Partners_AddOwnerNames_View completă ===';
GO

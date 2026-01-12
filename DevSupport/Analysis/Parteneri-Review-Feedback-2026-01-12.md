# 📝 Review și Feedback: Propunere Implementare Parteneri

**Data Review:** 12 Ianuarie 2026  
**Document Revizuit:** Parteneri-Implementation-Proposal-2026-01-12.md  
**Status:** ✅ INTEGRAT în documentul principal (v2.0)  

---

## 1. EVALUARE GENERALĂ

### ✅ Puncte Forte

| Aspect | Observație |
|--------|------------|
| **Abordare MVP** | Corect să începi cu SC + PF + PFA, restul vin în V2 |
| **Tabel unic Partners** | Simplitate maximă, o singură interogare pentru grid |
| **Formular dinamic** | Reduce codul și menține UX consistent |
| **Integrări ANAF/VIES** | Esențiale pentru un ERP serios în România |
| **Import/Export Excel** | Necesar la migrarea inițială de date |
| **Matricea câmpuri per categorie** | Foarte utilă pentru formularul dinamic |
| **View-ul vw_Partners** | Optimizat pentru grid, include adresa principală și statistici |
| **Indexuri filtered** | `WHERE IsActive = 1` – performanță bună |
| **Structura Vertical Slices** | Consistentă cu restul aplicației |
| **Riscuri identificate** | Realiste și cu mitigări concrete |

### 📊 Verdict

**Propunerea este solidă și gata de implementare** cu câteva ajustări recomandate mai jos.

---

## 2. SUGESTII ȘI COMPLETĂRI

### 2.1 RolPartener – Folosește Flags în loc de Enum Simplu

**Problema:** În propunerea actuală, `RolPartener TINYINT` are valori 0-3 (Furnizor, Client, Ambele, Altele). Dar un partener poate fi **simultan** Client + Furnizor + Colaborator (ex: o firmă de IT care îți vinde și de la care cumperi).

**Propunere – Flags cu bitwise:**

```csharp
// Features/Administrare/Parteneri/Models/Enums/RolPartener.cs
[Flags]
public enum RolPartener
{
    [Display(Name = "Nedefinit")]
    None = 0,
    
    [Display(Name = "Furnizor")]
    Furnizor = 1,       // 0001
    
    [Display(Name = "Client")]
    Client = 2,         // 0010
    
    [Display(Name = "Colaborator")]
    Colaborator = 4,    // 0100
    
    [Display(Name = "Angajat")]
    Angajat = 8,        // 1000
    
    [Display(Name = "Asociat")]
    Asociat = 16        // 10000
}
```

**Utilizare în C#:**

```csharp
// Setare roluri multiple
partner.RolPartener = RolPartener.Furnizor | RolPartener.Client; // = 3

// Verificare rol specific
bool esteFurnizor = partner.RolPartener.HasFlag(RolPartener.Furnizor);
bool esteClient = partner.RolPartener.HasFlag(RolPartener.Client);

// Afișare roluri
string roluriText = string.Join(", ", 
    Enum.GetValues<RolPartener>()
        .Where(r => r != RolPartener.None && partner.RolPartener.HasFlag(r))
        .Select(r => r.GetDisplayName()));
```

**În SQL rămâne `TINYINT`**, filtrarea devine:

```sql
-- Toți furnizorii (inclusiv cei care sunt și clienți)
WHERE (RolPartener & 1) = 1

-- Toți clienții
WHERE (RolPartener & 2) = 2

-- Doar furnizorii (nu și clienți)
WHERE RolPartener = 1

-- Furnizori SAU Clienți
WHERE (RolPartener & 3) > 0
```

**Impact UI:** În loc de dropdown simplu, folosești checkboxes multiple în formular.

---

### 2.2 Câmpuri Lipsă pentru SAF-T

Pentru conformitate SAF-T Romania, adaugă în tabelul `Partners`:

```sql
-- Câmpuri SAF-T obligatorii
ALTER TABLE Partners ADD
    TipPartenerSAFT VARCHAR(10) NULL,      -- 'C' (Client), 'S' (Supplier), 'O' (Other), 'CS' (Both)
    CodPartenerSAFT VARCHAR(35) NULL,      -- Cod intern unic pentru export SAF-T
    SupplierID VARCHAR(35) NULL,           -- ID furnizor pentru SAF-T (dacă diferit de CodPartenerSAFT)
    CustomerID VARCHAR(35) NULL;           -- ID client pentru SAF-T (dacă diferit de CodPartenerSAFT)

-- În PartnerBankAccounts
ALTER TABLE PartnerBankAccounts ADD
    TipCont VARCHAR(10) NULL DEFAULT 'IBAN',  -- 'IBAN', 'Altul'
    IBANValid BIT NULL;                        -- Rezultat validare IBAN
```

**Generare automată CodPartenerSAFT:**

```sql
-- Trigger pentru generare automată
CREATE TRIGGER TR_Partners_GenerateSAFTCode
ON Partners
AFTER INSERT
AS
BEGIN
    UPDATE p
    SET CodPartenerSAFT = CONCAT('P', FORMAT(p.CreatedAt, 'yyyyMMdd'), '-', RIGHT('00000' + CAST(p.Id AS VARCHAR), 5))
    FROM Partners p
    INNER JOIN inserted i ON p.Id = i.Id
    WHERE p.CodPartenerSAFT IS NULL;
END
```

---

### 2.3 Adresă Principală – Logică de Fallback

**Problema:** În view-ul `vw_Partners` ai `WHERE addr.EstePrincipala = 1`. Ce se întâmplă dacă nicio adresă nu e marcată principală?

**Soluție – Fallback inteligent:**

```sql
-- Modificare în vw_Partners
LEFT JOIN PartnerAddresses addr ON addr.PartnerId = p.Id 
    AND addr.IsActive = 1
    AND addr.Id = (
        SELECT TOP 1 Id 
        FROM PartnerAddresses 
        WHERE PartnerId = p.Id AND IsActive = 1
        ORDER BY 
            EstePrincipala DESC,           -- Prioritate: principală
            CASE TipAdresa 
                WHEN 0 THEN 1              -- Apoi: Sediu
                WHEN 3 THEN 2              -- Facturare
                WHEN 1 THEN 3              -- Corespondență
                WHEN 2 THEN 4              -- Livrare
                ELSE 5 
            END,
            CreatedAt ASC                  -- Prima adăugată
    )
```

**Alternativ – Computed column în Partners:**

```sql
ALTER TABLE Partners ADD
    AdresaPrincipalaId UNIQUEIDENTIFIER NULL;

-- Update periodic sau via trigger
UPDATE p
SET AdresaPrincipalaId = (
    SELECT TOP 1 Id FROM PartnerAddresses 
    WHERE PartnerId = p.Id AND IsActive = 1
    ORDER BY EstePrincipala DESC, TipAdresa ASC, CreatedAt ASC
)
FROM Partners p;
```

---

### 2.4 Full-Text Search – Optimizare

**Problema:** Full-text pe CNP/CUI nu e ideal – sunt căutări exacte sau prefix.

**Propunere:**

```sql
-- Full-text DOAR pentru text liber (căutare fuzzy)
CREATE FULLTEXT CATALOG PartnersCatalog AS DEFAULT;

CREATE FULLTEXT INDEX ON [Partners] 
    ([Denumire], [DenumireScurta], [Nume], [Prenume], [Observatii]) 
KEY INDEX [PK_Partners]
ON PartnersCatalog;

-- Index normal pentru identificatori (căutări exacte/prefix)
CREATE INDEX [IX_Partners_CUI_Search] ON [Partners] ([CUI]) 
    INCLUDE ([Id], [Denumire]) WHERE [IsActive] = 1;
    
CREATE INDEX [IX_Partners_CNP_Search] ON [Partners] ([CNP]) 
    INCLUDE ([Id], [Nume], [Prenume]) WHERE [IsActive] = 1;
    
CREATE INDEX [IX_Partners_CIF_Search] ON [Partners] ([CIF]) 
    INCLUDE ([Id], [Denumire]) WHERE [IsActive] = 1;
```

**Căutare în C#:**

```csharp
public async Task<List<PartnerListDto>> SearchAsync(string searchTerm)
{
    // Detectează tipul de căutare
    if (Regex.IsMatch(searchTerm, @"^\d{13}$")) // CNP
        return await SearchByCnpAsync(searchTerm);
    
    if (Regex.IsMatch(searchTerm, @"^(RO)?\d{1,10}$")) // CUI
        return await SearchByCuiAsync(searchTerm.Replace("RO", ""));
    
    // Altfel, full-text search
    return await FullTextSearchAsync(searchTerm);
}
```

---

### 2.5 Cache ANAF – Structură Propusă

```sql
CREATE TABLE AnafVerificationCache (
    CUI VARCHAR(20) NOT NULL PRIMARY KEY,
    
    -- Date returnate de ANAF
    Denumire NVARCHAR(200) NULL,
    Adresa NVARCHAR(500) NULL,
    NrRegCom VARCHAR(50) NULL,
    CodPostal VARCHAR(10) NULL,
    
    -- Status TVA
    ScpTVA BIT NULL,                       -- Înregistrat în scopuri de TVA
    DataInregistrareTVA DATE NULL,
    DataAnulareTVA DATE NULL,
    StatusTVA VARCHAR(50) NULL,            -- 'activ', 'inactiv', 'radiat'
    
    -- Status Split TVA
    StatusSplitTVA BIT NULL,
    DataSplitTVA DATE NULL,
    
    -- Status Inactivi
    StatusInactivi BIT NULL,
    DataInactivare DATE NULL,
    DataReactivare DATE NULL,
    
    -- Status Insolvență
    StatusInsolventa BIT NULL,
    DataInsolventa DATE NULL,
    
    -- Meta
    ResponseJson NVARCHAR(MAX) NULL,       -- JSON complet pentru debugging
    VerifiedAt DATETIME2 NOT NULL,
    ExpiresAt DATETIME2 NOT NULL,          -- Cache 24h default
    
    -- Index
    INDEX IX_AnafCache_ExpiresAt (ExpiresAt)
);
```

**Service pentru cache:**

```csharp
public class AnafVerificationService : IAnafVerificationService
{
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(24);
    
    public async Task<AnafVerificationResult> VerifyAsync(string cui)
    {
        // 1. Check cache
        var cached = await GetFromCacheAsync(cui);
        if (cached != null && cached.ExpiresAt > DateTime.UtcNow)
            return cached;
        
        // 2. Call ANAF API
        var result = await CallAnafApiAsync(cui);
        
        // 3. Update cache
        await SaveToCacheAsync(cui, result);
        
        // 4. Update Partner record
        await UpdatePartnerVerificationAsync(cui, result);
        
        return result;
    }
}
```

---

### 2.6 Status Partener – Câmpuri Extinse

**Propunere:** Adaugă câmpuri pentru status detaliat:

```sql
ALTER TABLE Partners ADD
    -- Status operațional
    PartnerStatus VARCHAR(20) NOT NULL DEFAULT 'Activ',  
    -- Valori: 'Activ', 'Inactiv', 'Blocat', 'Suspendat', 'Radiat'
    
    -- Status ANAF (sincronizat automat)
    AnafStatus VARCHAR(20) NULL,           -- 'valid', 'inactiv', 'radiat'
    AnafVerifiedAt DATETIME2 NULL,
    AnafVerifiedBy UNIQUEIDENTIFIER NULL,
    
    -- Blocări comerciale
    BlocatFacturare BIT NOT NULL DEFAULT 0,
    BlocatLivrare BIT NOT NULL DEFAULT 0,
    MotivBlocare NVARCHAR(500) NULL,
    DataBlocare DATETIME2 NULL,
    
    -- Credit
    LimitaCredit DECIMAL(18,2) NULL,
    TermenPlataDef INT NULL DEFAULT 30,    -- Zile
    
    -- Clasificare internă
    CategorieComercială VARCHAR(20) NULL,  -- 'Standard', 'Premium', 'VIP', 'Risc'
    ScorCredit INT NULL;                   -- 0-100
```

---

### 2.7 Audit History (Opțional – V2)

Pentru păstrarea istoricului modificărilor:

```sql
CREATE TABLE PartnerHistory (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    PartnerId UNIQUEIDENTIFIER NOT NULL,
    
    ChangeType VARCHAR(10) NOT NULL,       -- 'INSERT', 'UPDATE', 'DELETE'
    ChangedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ChangedBy UNIQUEIDENTIFIER NULL,
    
    -- Snapshot valori
    OldValues NVARCHAR(MAX) NULL,          -- JSON cu valorile vechi
    NewValues NVARCHAR(MAX) NULL,          -- JSON cu valorile noi
    ChangedFields NVARCHAR(500) NULL,      -- Lista câmpurilor modificate
    
    -- Indexuri
    INDEX IX_PartnerHistory_PartnerId (PartnerId),
    INDEX IX_PartnerHistory_ChangedAt (ChangedAt)
);
```

**Trigger pentru audit:**

```sql
CREATE TRIGGER TR_Partners_Audit
ON Partners
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO PartnerHistory (PartnerId, ChangeType, ChangedBy, OldValues, NewValues)
    SELECT 
        COALESCE(i.Id, d.Id),
        CASE 
            WHEN i.Id IS NULL THEN 'DELETE'
            ELSE 'UPDATE'
        END,
        COALESCE(i.UpdatedBy, d.UpdatedBy),
        (SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    FROM deleted d
    FULL OUTER JOIN inserted i ON d.Id = i.Id;
END
```

---

### 2.8 Unicitate Identificator – Edge Cases

**Problema:** Ce faci cu:
- Persoană fizică străină fără CNP/NIF cunoscut?
- Entități noi unde încă nu știi CUI-ul?

**Soluție:**

```sql
ALTER TABLE Partners ADD
    IdentificatorTemp VARCHAR(50) NULL,    -- Auto-generat pentru entități fără identificator
    MotivLipsaIdentificator NVARCHAR(200) NULL;

-- Constraint: cel puțin un identificator obligatoriu
ALTER TABLE Partners ADD CONSTRAINT CK_Partners_HasIdentifier
CHECK (
    CNP IS NOT NULL OR 
    CUI IS NOT NULL OR 
    CIF IS NOT NULL OR 
    VATID IS NOT NULL OR 
    CodFiscalStrain IS NOT NULL OR
    Pasaport IS NOT NULL OR
    IdentificatorTemp IS NOT NULL
);
```

**Generare IdentificatorTemp:**

```csharp
public string GenerateTemporaryId(CategoriePartener categorie)
{
    var prefix = categorie switch
    {
        CategoriePartener.PF => "PF",
        CategoriePartener.STR => "STR",
        CategoriePartener.DIP => "DIP",
        _ => "TMP"
    };
    
    return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
    // Ex: "PF-20260112-A1B2C3D4"
}
```

---

## 3. SCRIPT SQL COMPLET CU MODIFICĂRI

### 3.1 Tabel Partners (Versiune Revizuită)

```sql
-- =============================================
-- PARTNERS - Tabel Principal (Versiune Revizuită)
-- =============================================
CREATE TABLE [dbo].[Partners] (
    -- PK
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    
    -- Identificare internă
    [Cod] NVARCHAR(20) NOT NULL,                    -- Cod intern unic (auto-generat)
    [CodPartenerSAFT] VARCHAR(35) NULL,             -- Cod pentru export SAF-T
    
    -- Clasificare
    [Categoria] NVARCHAR(10) NOT NULL,              -- PF, PFA, SC, NP, IP, DIP, OI, STR, SP, ALT
    [TipEntitate] NVARCHAR(20) NOT NULL,            -- Subtip detaliat (SRL, PFA_STD, etc.)
    [RolPartener] TINYINT NOT NULL DEFAULT 0,       -- FLAGS: 1=Furnizor, 2=Client, 4=Colaborator, etc.
    
    -- Denumire (pentru PJ)
    [Denumire] NVARCHAR(200) NULL,
    [DenumireScurta] NVARCHAR(50) NULL,
    
    -- Nume (pentru PF)
    [Nume] NVARCHAR(100) NULL,
    [Prenume] NVARCHAR(100) NULL,
    
    -- Identificatori fiscali
    [CNP] NVARCHAR(13) NULL,
    [CUI] NVARCHAR(20) NULL,                        -- Include prefix RO pentru plătitori TVA
    [CIF] NVARCHAR(20) NULL,
    [VATID] NVARCHAR(20) NULL,                      -- Pentru entități străine UE
    [CodFiscalStrain] NVARCHAR(50) NULL,            -- Pentru entități non-UE
    [RegCom] NVARCHAR(50) NULL,                     -- J../.../.....
    [NrAutorizatie] NVARCHAR(50) NULL,              -- Pentru PFA, cabinete
    [Pasaport] NVARCHAR(50) NULL,                   -- Pentru PF nerezidente
    [IdentificatorTemp] VARCHAR(50) NULL,           -- Pentru entități fără identificator oficial
    [MotivLipsaIdentificator] NVARCHAR(200) NULL,
    
    -- Date specifice
    [DataInregistrare] DATE NULL,
    [DataRadiere] DATE NULL,
    [TaraOrigine] NVARCHAR(3) NULL DEFAULT 'RO',    -- Cod ISO
    [CAENPrincipal] NVARCHAR(10) NULL,
    [CapitalSocial] DECIMAL(18,2) NULL,
    
    -- Contact principal
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
    [DataBlocare] DATETIME2 NULL,
    
    -- Verificare ANAF
    [EsteVerificat] BIT NOT NULL DEFAULT 0,
    [AnafStatus] VARCHAR(20) NULL,
    [AnafVerifiedAt] DATETIME2 NULL,
    [AnafVerifiedBy] UNIQUEIDENTIFIER NULL,
    
    -- Credit și clasificare comercială
    [LimitaCredit] DECIMAL(18,2) NULL,
    [TermenPlataDef] INT NULL DEFAULT 30,
    [CategorieComercială] VARCHAR(20) NULL,
    [ScorCredit] INT NULL,
    
    -- SAF-T
    [TipPartenerSAFT] VARCHAR(10) NULL,
    [SupplierID] VARCHAR(35) NULL,
    [CustomerID] VARCHAR(35) NULL,
    
    -- Note
    [Observatii] NVARCHAR(2000) NULL,
    
    -- Adresa principală (denormalizat pentru performanță)
    [AdresaPrincipalaId] UNIQUEIDENTIFIER NULL,
    
    -- Audit
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    
    -- Constraints
    CONSTRAINT [PK_Partners] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Partners_Cod] UNIQUE ([Cod]),
    CONSTRAINT [CK_Partners_HasIdentifier] CHECK (
        [CNP] IS NOT NULL OR 
        [CUI] IS NOT NULL OR 
        [CIF] IS NOT NULL OR 
        [VATID] IS NOT NULL OR 
        [CodFiscalStrain] IS NOT NULL OR
        [Pasaport] IS NOT NULL OR
        [IdentificatorTemp] IS NOT NULL
    ),
    CONSTRAINT [CK_Partners_Status] CHECK ([PartnerStatus] IN ('Activ', 'Inactiv', 'Blocat', 'Suspendat', 'Radiat'))
);

-- =============================================
-- INDEXURI
-- =============================================

-- Unicitate identificatori (filtered indexes)
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CUI] 
ON [Partners] ([CUI]) WHERE [CUI] IS NOT NULL;

CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CNP] 
ON [Partners] ([CNP]) WHERE [CNP] IS NOT NULL;

CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CIF] 
ON [Partners] ([CIF]) WHERE [CIF] IS NOT NULL;

CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_VATID] 
ON [Partners] ([VATID]) WHERE [VATID] IS NOT NULL;

-- Căutare
CREATE INDEX [IX_Partners_Categoria] ON [Partners] ([Categoria]) 
    INCLUDE ([Denumire], [RolPartener]) WHERE [IsActive] = 1;

CREATE INDEX [IX_Partners_TipEntitate] ON [Partners] ([TipEntitate]) 
    WHERE [IsActive] = 1;

CREATE INDEX [IX_Partners_RolPartener] ON [Partners] ([RolPartener]) 
    WHERE [IsActive] = 1;

CREATE INDEX [IX_Partners_Denumire] ON [Partners] ([Denumire]) 
    WHERE [IsActive] = 1;

CREATE INDEX [IX_Partners_NumePrenume] ON [Partners] ([Nume], [Prenume]) 
    WHERE [IsActive] = 1;

CREATE INDEX [IX_Partners_Status] ON [Partners] ([PartnerStatus], [EsteActiv]) 
    INCLUDE ([Denumire], [CUI]);

-- Full-text (doar pentru text liber)
CREATE FULLTEXT CATALOG PartnersCatalog AS DEFAULT;

CREATE FULLTEXT INDEX ON [Partners] 
    ([Denumire], [DenumireScurta], [Nume], [Prenume], [Observatii]) 
KEY INDEX [PK_Partners]
ON PartnersCatalog;
```

---

## 4. CHECKLIST PRE-IMPLEMENTARE

### 4.1 Decizii de Luat

| # | Decizie | Opțiuni | Recomandare |
|---|---------|---------|-------------|
| 1 | RolPartener | Enum simplu vs. Flags | ✅ **Flags** (flexibilitate) |
| 2 | Storicizare date ANAF | Suprascrie vs. Versiuni | 🔸 Suprascrie + Cache (MVP), Versiuni în V2 |
| 3 | Număr adrese | Limitat (1-3) vs. Nelimitat | ✅ **Nelimitat** cu UI primele 3 |
| 4 | Full-text search | Pe toate câmpurile vs. Selectiv | ✅ **Selectiv** (doar text liber) |
| 5 | Audit history | Acum vs. V2 | 🔸 **V2** (complexitate) |
| 6 | Cache ANAF | In-memory vs. Tabel | ✅ **Tabel** (persistență, debugging) |

### 4.2 Înainte de Faza 1

- [ ] Decide asupra RolPartener (flags sau enum simplu)
- [ ] Confirmă câmpurile SAF-T necesare
- [ ] Stabilește regula pentru IdentificatorTemp
- [ ] Verifică endpoint-ul ANAF activ (v8)
- [ ] Obține acces la VIES API

---

## 5. FIȘIERE DE GENERAT

După aprobare, pot genera:

1. **Script SQL complet** – toate tabelele, indexurile, view-urile, trigger-ele
2. **Modele C# actualizate** – cu enum flags și câmpurile noi
3. **AnafVerificationService.cs** – implementare completă cu cache
4. **Validators actualizați** – CNP, CUI, IBAN cu logica de fallback

---

**Autor Review:** Claude  
**Data:** 12 Ianuarie 2026  
**Versiune:** 1.0

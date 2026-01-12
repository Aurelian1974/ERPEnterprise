# 🏢 User Organizational Access System
## Design Document - Ierarhie și Permisiuni Organizaționale

**Data:** 12 Ianuarie 2026  
**Status:** ⬜ PENDING REVIEW  
**Autor:** GitHub Copilot

---

## 📋 Sumar Executiv

Acest document definește arhitectura pentru sistemul de **acces organizațional granular** în ValyanERP, care permite:
- Acces la entități din orice nivel al ierarhiei (Grup → Societate → Punct Lucru → Locație)
- Moștenire automată a accesului către entitățile subordonate
- Acces granular (ex: doar 2 locații din puncte de lucru diferite)
- Filtrare automată a datelor bazată pe perimetrul vizibil al userului

---

## 🏗️ Ierarhia Organizațională (EXISTENTĂ)

```
┌─────────────────────────────────────────────────────────────────────┐
│ NIVEL 1: GRUP (CompanyGroups)                                       │
│ - Holding, grup de companii                                         │
│ - Ex: "UTI GRUP"                                                    │
├─────────────────────────────────────────────────────────────────────┤
│ NIVEL 2: SOCIETATE (Companies)                                      │
│ - Entitate juridică cu CUI                                          │
│ - Ex: "UTI SYSTEMS SA - RO16326010"                                 │
├─────────────────────────────────────────────────────────────────────┤
│ NIVEL 3: PUNCT DE LUCRU (WorkPlaces)                                │
│ - Filială, sediu, sucursală                                         │
│ - Ex: "Sediu Central", "Filiala Cluj"                               │
├─────────────────────────────────────────────────────────────────────┤
│ NIVEL 4: LOCAȚIE (Locations)                                        │
│ - Spațiu fizic specific                                             │
│ - Ex: "Depozit Principal", "Showroom", "Service Auto"               │
└─────────────────────────────────────────────────────────────────────┘
```

### Tabele Existente (din 017_SocietateaProprie.sql):

| Tabel | Descriere | Parent FK |
|-------|-----------|-----------|
| `CompanyGroups` | Grupuri de companii | - |
| `Companies` | Societăți/Companii | `GroupId`, `ParentCompanyId` |
| `WorkPlaces` | Puncte de lucru | `CompanyId` |
| `Locations` | Locații | `CompanyId`, `WorkPlaceId` |

---

## 🎯 Cerințe Funcționale

### R1: Acces Multi-Nivel
Un utilizator poate avea acces la **una sau mai multe entități** din **orice nivel**:
- ✅ Acces la un Grup → vede TOATE companiile, punctele de lucru, locațiile din acel grup
- ✅ Acces la o Societate → vede TOATE punctele de lucru și locațiile din ea
- ✅ Acces la un Punct de Lucru → vede TOATE locațiile din acel punct
- ✅ Acces la o Locație → vede DOAR acea locație

### R2: Moștenire Implicită
Accesul la un nivel superior **implică automat** acces la toate entitățile subordonate.

### R3: Acces Granular
Un user poate avea acces **doar la entități specifice**, fără moștenire:
- Ex: Acces doar la Locația X din Punctul A și Locația Y din Punctul B
- NU are acces la celelalte locații din aceste puncte

### R4: Perimetru la Autentificare
La login, sistemul determină **perimetrul vizibil** al userului și îl stochează în sesiune.

### R5: Filtrare Automată
Toate query-urile pe date operaționale TREBUIE filtrate automat după perimetru.

---

## 💾 Design Baza de Date

### Opțiunea A: Tabel Unic cu EntityType (RECOMANDATĂ ✅)

```sql
-- =============================================
-- USER ORGANIZATIONAL ACCESS
-- Un singur tabel pentru toate nivelurile
-- =============================================
CREATE TABLE [dbo].[UserOrganizationalAccess] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Tipul entității la care are acces
    [EntityType] VARCHAR(20) NOT NULL, -- 'GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION'
    [EntityId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Tip acces (pentru extensibilitate viitoare)
    [AccessLevel] TINYINT NOT NULL DEFAULT 1, 
    -- 0 = NoAccess (explicit denied)
    -- 1 = Read
    -- 2 = Write
    -- 3 = Full (include delete)
    -- 4 = Admin (poate administra access-ul altora)
    
    -- Moștenire
    [InheritToChildren] BIT NOT NULL DEFAULT 1,
    -- TRUE = accesul se propagă la toate entitățile subordonate
    -- FALSE = acces DOAR la această entitate
    
    -- Valabilitate (opțional, pentru acces temporar)
    [ValidFrom] DATETIME2 NULL,
    [ValidTo] DATETIME2 NULL,
    
    -- Audit
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [Notes] NVARCHAR(500) NULL,
    
    -- Constraints
    CONSTRAINT [FK_UOA_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UOA_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UOA_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [CK_UOA_EntityType] CHECK ([EntityType] IN ('GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION')),
    CONSTRAINT [CK_UOA_AccessLevel] CHECK ([AccessLevel] BETWEEN 0 AND 4),
    CONSTRAINT [CK_UOA_ValidDates] CHECK ([ValidTo] IS NULL OR [ValidFrom] IS NULL OR [ValidTo] >= [ValidFrom])
);

-- Un user nu poate avea duplicate pentru aceeași entitate
CREATE UNIQUE INDEX [UQ_UOA_User_Entity] 
ON [UserOrganizationalAccess] ([UserId], [EntityType], [EntityId]) 
WHERE [IsActive] = 1;

-- Indexuri pentru query-uri rapide
CREATE INDEX [IX_UOA_UserId] ON [UserOrganizationalAccess] ([UserId]) INCLUDE ([EntityType], [EntityId], [AccessLevel], [InheritToChildren]);
CREATE INDEX [IX_UOA_EntityType_EntityId] ON [UserOrganizationalAccess] ([EntityType], [EntityId]) INCLUDE ([UserId], [AccessLevel]);
CREATE INDEX [IX_UOA_ValidDates] ON [UserOrganizationalAccess] ([ValidFrom], [ValidTo]) WHERE [IsActive] = 1;
```

### Avantaje Opțiunea A:
- ✅ Un singur tabel de gestionat
- ✅ Query-uri simple pentru aflarea perimetrului
- ✅ Flexibilitate maximă
- ✅ Extensibil (AccessLevel, ValidFrom/To)
- ✅ Permite și acces granular (InheritToChildren = 0)

---

### Opțiunea B: Tabele Separate per Nivel (NU RECOMANDATĂ)

```sql
-- Ar necesita 4 tabele:
-- UserGroupAccess, UserCompanyAccess, UserWorkPlaceAccess, UserLocationAccess
```

### Dezavantaje Opțiunea B:
- ❌ 4 tabele de sincronizat
- ❌ Query-uri complexe pentru aflarea perimetrului total
- ❌ Logică duplicată în stored procedures

---

## 🔍 Calculul Perimetrului Vizibil

### Stored Procedure: Returnează tot ce vede un user

```sql
-- =============================================
-- FUNCȚIE: Returnează toate Location IDs vizibile pentru un user
-- Aceasta e "cheia" pentru filtrare automată
-- =============================================
CREATE FUNCTION [dbo].[fn_GetUserVisibleLocations](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    -- 1. Locații cu acces DIRECT
    SELECT DISTINCT l.Id AS LocationId
    FROM Locations l
    INNER JOIN UserOrganizationalAccess uoa ON uoa.EntityType = 'LOCATION' AND uoa.EntityId = l.Id
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 2. Locații din WORKPLACE cu acces
    SELECT l.Id AS LocationId
    FROM Locations l
    INNER JOIN UserOrganizationalAccess uoa ON uoa.EntityType = 'WORKPLACE' AND uoa.EntityId = l.WorkPlaceId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 3. Locații din COMPANY cu acces
    SELECT l.Id AS LocationId
    FROM Locations l
    INNER JOIN UserOrganizationalAccess uoa ON uoa.EntityType = 'COMPANY' AND uoa.EntityId = l.CompanyId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 4. Locații din GRUP cu acces
    SELECT l.Id AS LocationId
    FROM Locations l
    INNER JOIN Companies c ON c.Id = l.CompanyId
    INNER JOIN UserOrganizationalAccess uoa ON uoa.EntityType = 'GROUP' AND uoa.EntityId = c.GroupId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
);
GO

-- Similar pentru WorkPlaces, Companies vizibile...
```

---

## 🧠 Strategii de Cache pentru Perimetru

### Strategie A: Cache la Nivel de Sesiune (RECOMANDATĂ)

```csharp
// La login, calculăm perimetrul și îl stocăm în Session table
public class UserPerimeter
{
    public Guid UserId { get; set; }
    
    // ID-uri ale tuturor entităților la care are acces DIRECT
    public HashSet<Guid> DirectGroupIds { get; set; }
    public HashSet<Guid> DirectCompanyIds { get; set; }
    public HashSet<Guid> DirectWorkPlaceIds { get; set; }
    public HashSet<Guid> DirectLocationIds { get; set; }
    
    // ID-uri ale tuturor entităților VIZIBILE (calculate cu moștenire)
    public HashSet<Guid> VisibleCompanyIds { get; set; }
    public HashSet<Guid> VisibleWorkPlaceIds { get; set; }
    public HashSet<Guid> VisibleLocationIds { get; set; }
    
    // Flag pentru admin care vede tot
    public bool HasFullAccess { get; set; }
    
    // Data calculării (pentru invalidare)
    public DateTime CalculatedAt { get; set; }
}
```

### Cum se stochează în Session:

```sql
-- Adăugăm coloană în Sessions pentru perimetru serializat
ALTER TABLE Sessions ADD
    [UserPerimeterJson] NVARCHAR(MAX) NULL,
    [PerimeterCalculatedAt] DATETIME2 NULL;
```

### Când se invalidează:
- ❌ User-ul își schimbă permisiunile (admin modifică UserOrganizationalAccess)
- ❌ Structura organizațională se schimbă (adăugare/ștergere locații)
- ❌ User-ul se deloghează și reloghează

---

## 🔐 Integrare cu Filtrarea Datelor

### Pattern 1: JOIN Direct în Query (pentru Dapper)

```csharp
// În Repository - toate query-urile pe date operaționale
public async Task<IEnumerable<StockItem>> GetStockAsync(Guid userId)
{
    const string sql = @"
        SELECT s.*
        FROM StockItems s
        INNER JOIN dbo.fn_GetUserVisibleLocations(@UserId) uvl ON s.LocationId = uvl.LocationId
        WHERE s.IsActive = 1";
    
    using var connection = _context.CreateConnection();
    return await connection.QueryAsync<StockItem>(sql, new { UserId = userId });
}
```

### Pattern 2: Middleware pentru Filtrare Automată

```csharp
// IOrganizationalFilterService - injectat în toate Repository-urile
public interface IOrganizationalFilterService
{
    Task<HashSet<Guid>> GetVisibleLocationIdsAsync();
    Task<HashSet<Guid>> GetVisibleCompanyIdsAsync();
    Task<HashSet<Guid>> GetVisibleWorkPlaceIdsAsync();
    
    // Helper pentru query building
    string GetLocationFilterSql(string locationIdColumn = "LocationId");
    string GetCompanyFilterSql(string companyIdColumn = "CompanyId");
}
```

### Pattern 3: Scoped Service cu Cache

```csharp
// UserPerimeterProvider - Scoped, calculat o dată per request
public class UserPerimeterProvider : IUserPerimeterProvider
{
    private UserPerimeter? _cachedPerimeter;
    private readonly ISessionService _sessionService;
    
    public async Task<UserPerimeter> GetPerimeterAsync()
    {
        if (_cachedPerimeter != null)
            return _cachedPerimeter;
        
        // Load from session or recalculate
        _cachedPerimeter = await LoadOrCalculatePerimeterAsync();
        return _cachedPerimeter;
    }
}
```

---

## 📂 Structura Cod (Vertical Slices)

```
Features/
├── Infrastructure/
│   └── OrganizationalAccess/
│       ├── Models/
│       │   ├── UserOrganizationalAccess.cs
│       │   ├── UserPerimeter.cs
│       │   └── AccessLevel.cs (enum)
│       ├── Repositories/
│       │   ├── IOrganizationalAccessRepository.cs
│       │   └── OrganizationalAccessRepository.cs
│       ├── Services/
│       │   ├── IOrganizationalAccessService.cs
│       │   ├── OrganizationalAccessService.cs
│       │   ├── IUserPerimeterProvider.cs
│       │   └── UserPerimeterProvider.cs
│       └── OrganizationalAccessAdaptor.cs (pentru SfDataGrid)

├── Administrare/
│   └── PermisiuniOrganizationale/
│       └── Pages/
│           ├── PermisiuniOrganizationale.razor
│           ├── PermisiuniOrganizationale.razor.cs
│           └── PermisiuniOrganizationale.razor.css

Database/
└── Scripts/
    ├── 022_UserOrganizationalAccess.sql
    └── 023_StoredProcedures_OrganizationalAccess.sql
```

---

## 🎨 UI: Administrare Permisiuni

### Pagina: `/administrare/permisiuni-organizationale`

```
┌─────────────────────────────────────────────────────────────────────┐
│ 🏢 PERMISIUNI ORGANIZAȚIONALE                                       │
├─────────────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌──────────────────────────────────────────────┐│
│ │ Select User ▼   │ │ 🌳 Ierarhie Organizațională                  ││
│ │ Ion Popescu     │ │                                              ││
│ └─────────────────┘ │ ☑️ [GRUP] UTI GRUP                           ││
│                     │   ├── ☑️ [COMP] UTI SYSTEMS SA (inherit)     ││
│ Legendă:            │   │   ├── ☑️ [PL] Sediu Central (inherit)    ││
│ ☑️ Acces direct     │   │   │   ├── ☑️ [LOC] Depozit (inherit)     ││
│ ☑️ Moștenit         │   │   │   └── ☑️ [LOC] Showroom (inherit)    ││
│ ⬜ Fără acces       │   │   └── ⬜ [PL] Filiala Cluj               ││
│                     │   └── ⬜ [COMP] UTI TELECOM SRL              ││
│ ┌─────────────────┐ │                                              ││
│ │ 💾 Salvează     │ │ ☐ [GRUP] ALTA HOLDINGURI SA                  ││
│ └─────────────────┘ │   └── ☐ [COMP] Alta SRL                      ││
│                     └──────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────┘
```

### Funcționalități UI:
- TreeView cu toate entitățile organizaționale
- Checkbox la fiecare nod
- Când selectezi un nod părinte → copiii se marchează automat (grayed out = moștenit)
- Poți selecta individual locații fără a selecta părinții
- Dropdown pentru AccessLevel per nod (Read/Write/Full)

---

## 📊 Scenarii de Acces

### Scenariul 1: Administrator de Grup
```
User: admin.uti@gmail.com
Access:
  - EntityType: GROUP, EntityId: [UTI_GRUP_ID], AccessLevel: 4 (Admin)
  
Perimeter calculat:
  - Vede TOATE companiile din UTI GRUP
  - Vede TOATE punctele de lucru
  - Vede TOATE locațiile
```

### Scenariul 2: Manager Societate
```
User: manager.systems@uti.com
Access:
  - EntityType: COMPANY, EntityId: [UTI_SYSTEMS_ID], AccessLevel: 3 (Full)
  
Perimeter calculat:
  - Vede doar UTI SYSTEMS SA
  - Vede toate punctele de lucru din UTI SYSTEMS
  - Vede toate locațiile din UTI SYSTEMS
  - NU vede UTI TELECOM sau alte companii
```

### Scenariul 3: Operator Locație
```
User: operator.depozit@uti.com
Access:
  - EntityType: LOCATION, EntityId: [DEPOZIT_CENTRAL_ID], AccessLevel: 2 (Write)
  - EntityType: LOCATION, EntityId: [DEPOZIT_CLUJ_ID], AccessLevel: 2 (Write)
  
Perimeter calculat:
  - Vede DOAR aceste 2 locații
  - NU vede celelalte locații din aceleași puncte de lucru
```

### Scenariul 4: Acces Temporar (Contractor)
```
User: contractor@extern.com
Access:
  - EntityType: WORKPLACE, EntityId: [SANTIER_X_ID], AccessLevel: 1 (Read)
    ValidFrom: 2026-01-01, ValidTo: 2026-03-31
  
Perimeter calculat:
  - Vede santierul X și toate locațiile din el
  - Accesul expiră automat pe 31 martie 2026
```

---

## 🔒 Considerații de Securitate

### 1. Validare la Fiecare Request
```csharp
// NU te baza doar pe UI - validează în backend
public async Task<StockItem?> GetStockItemAsync(Guid itemId)
{
    var perimeter = await _perimeterProvider.GetPerimeterAsync();
    var item = await _repository.GetByIdAsync(itemId);
    
    // Security check
    if (item != null && !perimeter.VisibleLocationIds.Contains(item.LocationId))
        throw new UnauthorizedAccessException("Access denied to this location");
    
    return item;
}
```

### 2. Audit Trail
Toate modificările la `UserOrganizationalAccess` trebuie auditate:
- Cine a dat/revocat accesul
- Când
- Ce tip de acces

### 3. Admin Override
Rolul `Admin` (din Identity) ar trebui să bypaseze filtrarea:
```csharp
if (user.IsInRole("Admin"))
    return new UserPerimeter { HasFullAccess = true };
```

---

## 📋 Plan de Implementare

### FAZA 1: Baza de Date (Estimare: 2-3 ore)
- [ ] Creare `022_UserOrganizationalAccess.sql`
- [ ] Creare `023_StoredProcedures_OrganizationalAccess.sql`
- [ ] Funcții: `fn_GetUserVisibleLocations`, `fn_GetUserVisibleCompanies`, etc.
- [ ] Stored procedures CRUD pentru `UserOrganizationalAccess`

### FAZA 2: Backend (Estimare: 4-5 ore)
- [ ] Model: `UserOrganizationalAccess.cs`
- [ ] Model: `UserPerimeter.cs`
- [ ] Repository: `OrganizationalAccessRepository.cs`
- [ ] Service: `OrganizationalAccessService.cs`
- [ ] Provider: `UserPerimeterProvider.cs` (Scoped)
- [ ] Integrare în `SessionService` (cache perimeter în sesiune)

### FAZA 3: Integrare Filtrare (Estimare: 3-4 ore)
- [ ] `IOrganizationalFilterService` pentru query building
- [ ] Modificare repositories existente să folosească filtrul
- [ ] Test cu date reale

### FAZA 4: UI Administrare (Estimare: 5-6 ore)
- [ ] Pagină `/administrare/permisiuni-organizationale`
- [ ] TreeView cu Syncfusion SfTreeView
- [ ] Dialog pentru configurare acces
- [ ] Integrare cu SfDataGrid pentru lista utilizatori

### FAZA 5: Testing (Estimare: 2-3 ore)
- [ ] Unit tests pentru calculul perimetrului
- [ ] Integration tests pentru filtrare
- [ ] Manual testing cu scenariile de mai sus

---

---

## 🗑️ Data Cleanup & Ownership Tracking

### Cerință: Cleanup Complet + Tracking Ownership

La implementare:
1. **Ștergem TOATE datele** din DB EXCEPTÂND:
   - User-ul `admin@valyanerp.ro`
   - Persoana asociată acestui user
2. **Admin devine admin pe grup** - va crea toate celelalte date
3. **Toate tabelele de date vor avea coloane de ownership** care indică în ce entitate organizațională a fost creată înregistrarea

### Coloane de Ownership (de adăugat în tabele)

Fiecare tabel cu date operaționale va avea:

```sql
-- Ownership organizațional (entitatea în care a fost creată înregistrarea)
[OwnerCompanyId] UNIQUEIDENTIFIER NULL,      -- Compania care "deține" înregistrarea
[OwnerWorkPlaceId] UNIQUEIDENTIFIER NULL,    -- Punctul de lucru (opțional, pentru granularitate)
[OwnerLocationId] UNIQUEIDENTIFIER NULL,     -- Locația (opțional, pentru granularitate maximă)
```

### Tabele Afectate

| Tabel | Adaugă Ownership | Notă |
|-------|------------------|------|
| `Persoane` | ✅ Da | O persoană aparține unei companii |
| `Users` | ✅ Da | Un user este creat în contextul unei companii |
| `Partners` | ✅ Da | Un partener este asociat unei companii |
| `CompanyGroups` | ❌ Nu | E root, nu are owner |
| `Companies` | ❌ Nu | Aparține unui Group, nu altei companii |
| `WorkPlaces` | ❌ Nu | Aparține unei Companies |
| `Locations` | ❌ Nu | Aparține unui WorkPlace/Company |
| `Sessions` | ❌ Nu | E de audit, nu operațional |
| `SystemParameters` | ❌ Nu | E globală, nu per companie |
| `AuditLogs` | ❌ Nu | E de audit, nu operațional |

### Regulă de Business: Inserare cu Ownership

Când un user creează o înregistrare nouă:
1. Sistemul determină **contextul curent** al userului (compania/locația selectată)
2. Se populează automat `OwnerCompanyId` (și opțional `OwnerWorkPlaceId`, `OwnerLocationId`)
3. La query, datele se filtrează după perimetrul userului **SAU** după ownership

### Vizibilitate Date

Un user vede o înregistrare dacă:
1. **Are acces la entitatea owner** (OwnerCompanyId e în perimetrul său), SAU
2. **A creat-o el însuși** (CreatedBy = userId)

```sql
-- Exemplu query filtrat
SELECT * FROM Persoane p
WHERE p.IsActive = 1
AND (
    -- Vede datele din companiile la care are acces
    p.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@UserId))
    OR
    -- Sau datele pe care le-a creat el
    p.CreatedBy = @UserId
);
```

---

## ⚠️ Întrebări pentru Clarificare

1. **Admin Override:** Rolul `Admin` ar trebui să vadă TOTUL indiferent de permisiuni?
2. **Acces Negativ:** Vrem să putem bloca explicit un user de la o entitate? (AccessLevel = 0)
3. **Valabilitate:** Este necesar acces temporar (ValidFrom/ValidTo)?
4. **Multi-Companie:** Un user poate avea acces la companii din grupuri DIFERITE?
5. **Cache Invalidation:** Cum notificăm userul când i s-au schimbat permisiunile?

---

## ✅ Decizie Arhitecturală

**RECOMANDARE:** Opțiunea A (Tabel Unic cu EntityType)

**Argumente:**
1. ✅ Simplitate în gestionare
2. ✅ Query-uri eficiente cu funcții inline
3. ✅ Extensibilitate (AccessLevel, ValidFrom/ValidTo)
4. ✅ Un singur set de stored procedures
5. ✅ Audit centralizat

---

## 📂 Scripturi SQL Create

| Script | Descriere |
|--------|-----------|
| `022_UserOrganizationalAccess.sql` | Tabel principal + funcții perimetru |
| `023_StoredProcedures_OrganizationalAccess.sql` | CRUD + operații avansate |
| `024_AddOwnershipColumns.sql` | Adaugă coloane OwnerCompanyId în Persoane, Users, Partners |
| `025_CleanupAndSetupAdmin.sql` | ⚠️ DESTRUCTIV: Șterge tot, păstrează admin, setează admin pe grup |

### Ordinea de Rulare:
```powershell
# 1. Mai întâi coloanele de ownership (non-destructiv)
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "024_AddOwnershipColumns.sql"

# 2. Apoi tabelul de acces organizațional
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "022_UserOrganizationalAccess.sql"

# 3. Stored procedures
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "023_StoredProcedures_OrganizationalAccess.sql"

# 4. ⚠️ DOAR DACĂ vrei să ștergi toate datele:
# Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "025_CleanupAndSetupAdmin.sql"
```

---

**Status Document:** ⬜ PENDING REVIEW  
**Următorul Pas:** Validare design cu stakeholders, apoi implementare FAZA 1


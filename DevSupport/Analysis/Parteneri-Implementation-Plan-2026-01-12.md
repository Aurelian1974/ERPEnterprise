# 📋 PLAN IMPLEMENTARE: Pagina Parteneri

**Data:** 12 Ianuarie 2026  
**Versiune:** 1.0  
**Status:** ⬜ În așteptare  
**Estimare Totală:** ~12.5 zile lucrătoare

---

## 📊 OVERVIEW FAZE

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         PLAN IMPLEMENTARE PARTENERI                         │
├─────────┬─────────┬─────────┬─────────┬─────────┬─────────────────────────┤
│  FAZA 1 │  FAZA 2 │  FAZA 3 │  FAZA 4 │  FAZA 5 │      CONȚINUT           │
│ 3 zile  │ 3 zile  │ 3 zile  │ 2 zile  │ 1.5 zile│                         │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────────────────────┤
│ DB      │ Grid    │ Dialog  │ ANAF+   │ Testing │                         │
│ Backend │ Import  │ Forms   │ VIES    │ Polish  │                         │
│ Models  │ Export  │ Tabs    │ Valid.  │         │                         │
├─────────┴─────────┴─────────┴─────────┴─────────┴─────────────────────────┤
│                    TOTAL: ~12.5 zile                                        │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 🔷 FAZA 1: INFRASTRUCTURĂ (3 zile)

### Obiectiv
Crearea bazei de date și a backend-ului complet, testabil independent de UI.

### 1.1 Script SQL - Tabele Principale (~4h)

**Fișier:** `Database/Scripts/020_Partners.sql`

**Tabele de creat:**
| Tabel | Descriere | Coloane Cheie |
|-------|-----------|---------------|
| `Partners` | Tabel principal parteneri | Id, Cod, Categoria, TipEntitate, RolPartener, Denumire/Nume, CUI/CNP, etc. |
| `PartnerAddresses` | Adrese multiple | Id, PartnerId, TipAdresa, Adresa, Localitate, Judet |
| `PartnerContacts` | Persoane de contact | Id, PartnerId, Nume, Prenume, Functie, Email, Telefon |
| `PartnerBankAccounts` | Conturi bancare | Id, PartnerId, IBAN, BIC, Banca, Moneda |
| `PartnerRepresentatives` | Reprezentanți legali | Id, PartnerId, Nume, Functie, TipReprezentant |
| `AnafVerificationCache` | Cache răspunsuri ANAF | CUI, Date ANAF, ExpiresAt |

**Indexuri și Constraints:**
- Unicitate: CUI, CNP, CIF, VATID (filtered indexes)
- Căutare: Categoria, TipEntitate, RolPartener, Denumire
- Full-text: Denumire, Nume, Prenume, Observatii

**Livrabil:** Script SQL executabil, testat pe DB

---

### 1.2 Script SQL - Stored Procedures (~4h)

**Fișier:** `Database/Scripts/021_StoredProcedures_Partners.sql`

**Proceduri de creat:**
```
Partners:
├── sp_Partners_GetAll          -- Cu paginare, filtre, sortare
├── sp_Partners_GetById         -- Detalii complete + relații
├── sp_Partners_Create          -- Insert cu auto-generate Cod
├── sp_Partners_Update          -- Update principal
├── sp_Partners_Delete          -- Soft delete
├── sp_Partners_Search          -- Căutare text/identificator
└── sp_Partners_GetForExport    -- Pentru export Excel

PartnerAddresses:
├── sp_PartnerAddresses_GetByPartnerId
├── sp_PartnerAddresses_Upsert
└── sp_PartnerAddresses_Delete

PartnerContacts:
├── sp_PartnerContacts_GetByPartnerId
├── sp_PartnerContacts_Upsert
└── sp_PartnerContacts_Delete

PartnerBankAccounts:
├── sp_PartnerBankAccounts_GetByPartnerId
├── sp_PartnerBankAccounts_Upsert
└── sp_PartnerBankAccounts_Delete

PartnerRepresentatives:
├── sp_PartnerRepresentatives_GetByPartnerId
├── sp_PartnerRepresentatives_Upsert
└── sp_PartnerRepresentatives_Delete

AnafCache:
├── sp_AnafCache_Get
├── sp_AnafCache_Upsert
└── sp_AnafCache_CleanExpired
```

**Livrabil:** Stored procedures create și testate în SSMS

---

### 1.3 Modele C# și Enum-uri (~6h)

**Structură:**
```
Features/Administrare/Parteneri/
├── Models/
│   ├── Partner.cs
│   ├── PartnerAddress.cs
│   ├── PartnerContact.cs
│   ├── PartnerBankAccount.cs
│   ├── PartnerRepresentative.cs
│   ├── AnafCacheEntry.cs
│   ├── Enums/
│   │   ├── CategoriePartener.cs
│   │   ├── RolPartener.cs         -- [Flags]
│   │   ├── TipAdresa.cs
│   │   ├── TipReprezentant.cs
│   │   └── PartnerStatus.cs
│   └── DTOs/
│       ├── PartnerListDto.cs      -- Pentru grid
│       ├── PartnerDetailDto.cs    -- Pentru view/edit
│       ├── CreatePartnerDto.cs
│       ├── UpdatePartnerDto.cs
│       └── PartnerExportDto.cs    -- Pentru Excel
```

**Livrabil:** Modele compilabile cu DataAnnotations

---

### 1.4 Repository + Interface (~4h)

**Fișiere:**
```
Features/Administrare/Parteneri/Repositories/
├── IPartnerRepository.cs
├── PartnerRepository.cs
├── IPartnerAddressRepository.cs
├── PartnerAddressRepository.cs
├── IPartnerContactRepository.cs
├── PartnerContactRepository.cs
├── IPartnerBankAccountRepository.cs
├── PartnerBankAccountRepository.cs
├── IPartnerRepresentativeRepository.cs
├── PartnerRepresentativeRepository.cs
├── IAnafCacheRepository.cs
└── AnafCacheRepository.cs
```

**Pattern:** Dapper + Stored Procedures (conform project standards)

**Livrabil:** Repositories funcționale, testabile cu date mock

---

### 1.5 Service + Business Logic (~4h)

**Fișiere:**
```
Features/Administrare/Parteneri/Services/
├── IPartnerService.cs
├── PartnerService.cs
│   ├── GetAllAsync(filters, paging)
│   ├── GetByIdAsync(id)
│   ├── CreateAsync(dto) → auto-generate Cod
│   ├── UpdateAsync(dto)
│   ├── DeleteAsync(id) → soft delete
│   ├── SearchAsync(term)
│   └── GetForExportAsync(filters)
```

**Business Logic:**
- Generare automată Cod partener: `P-{YYYYMMDD}-{XXXXX}`
- Generare CodPartenerSAFT
- Validare cel puțin un identificator (CUI/CNP/etc)
- Setare automată TipPartenerSAFT din RolPartener

**Livrabil:** Service cu logică de business funcțională

---

### 1.6 Validators (~4h)

**Fișiere:**
```
Features/Administrare/Parteneri/Validators/
├── CnpValidator.cs          -- Algoritm validare CNP
├── CuiValidator.cs          -- Algoritm validare CUI
├── IbanValidator.cs         -- Validare IBAN (RO + internațional)
├── VatIdValidator.cs        -- Validare VAT ID UE
└── PartnerValidator.cs      -- FluentValidation pentru Partner
```

**Algoritmi:**
- CNP: cifră de control, validare dată naștere
- CUI: cifră de control
- IBAN: MOD 97, prefix țară
- VAT ID: format per țară UE

**Livrabil:** Validators cu unit tests

---

### 1.7 DI Registration (~2h)

**Fișier:** `Program.cs`

```csharp
// Partners Feature
builder.Services.AddScoped<IPartnerRepository, PartnerRepository>();
builder.Services.AddScoped<IPartnerAddressRepository, PartnerAddressRepository>();
builder.Services.AddScoped<IPartnerContactRepository, PartnerContactRepository>();
builder.Services.AddScoped<IPartnerBankAccountRepository, PartnerBankAccountRepository>();
builder.Services.AddScoped<IPartnerRepresentativeRepository, PartnerRepresentativeRepository>();
builder.Services.AddScoped<IAnafCacheRepository, AnafCacheRepository>();
builder.Services.AddScoped<IPartnerService, PartnerService>();
builder.Services.AddScoped<IAnafVerificationService, AnafVerificationService>();
builder.Services.AddScoped<IViesVerificationService, ViesVerificationService>();
builder.Services.AddScoped<PartnersAdaptor>();
```

**Livrabil:** Aplicația pornește fără erori DI

---

## 🔷 FAZA 2: UI GRID + IMPORT/EXPORT (3 zile)

### Obiectiv
Pagină funcțională cu grid, export și import Excel.

### 2.1 Parteneri.razor Layout (~4h)

**Fișiere:**
```
Components/Pages/Administrare/
├── Parteneri.razor
├── Parteneri.razor.cs
└── Parteneri.razor.css
```

**Elemente UI:**
- Page header cu titlu și buton "Adaugă Partener"
- Toolbar cu filtre: Categorie, Rol, TipEntitate, Căutare
- Checkbox "Doar activi"
- Butoane: Export Excel, Export PDF, Import

**Livrabil:** Layout vizual conform mockup

---

### 2.2 PartnersAdaptor Server-Side (~6h)

**Fișier:** `Features/Administrare/Parteneri/PartnersAdaptor.cs`

**Funcționalități:**
- ReadAsync: paginare, sortare, filtrare server-side
- FilterChoiceRequest: pentru Excel filter dropdowns
- LazyLoad Grouping: dacă e necesar

**Livrabil:** Grid cu date din DB, performant pe 2000+ înregistrări

---

### 2.3 Grid Coloane, Filtre, Toolbar (~4h)

**Coloane SfGrid:**
| Coloană | Tip | Width | Features |
|---------|-----|-------|----------|
| Cod | Text | 100 | Sort, Filter |
| Denumire | Text | 250 | Sort, Filter, Search |
| Categoria | Template | 120 | Filter dropdown |
| TipEntitate | Text | 100 | Filter |
| Rol | Template + Icon | 100 | Filter (multi-select pentru flags) |
| Identificator | Text | 120 | Filter, Search |
| Telefon | Text | 120 | - |
| Email | Text | 180 | - |
| Status | Template + Badge | 80 | Filter |
| Acțiuni | Template | 100 | View, Edit, Delete, ANAF |

**Livrabil:** Grid complet funcțional cu toate coloanele

---

### 2.4 Export Excel (~4h)

**Implementare:**
- Export manual cu Syncfusion.XlsIO (nu grid built-in)
- Coloane conform cerință: Denumire, Adresă, Localitate, Județ, CUI, RegCom, Reprezentant
- Format: .xlsx cu header styling

**Livrabil:** Export funcțional, fișier descărcat corect

---

### 2.5 Import Excel/CSV (~6h)

**Funcționalități:**
1. Upload fișier (.xlsx, .csv)
2. Parsare și validare
3. Preview date cu erori highlighted
4. Confirmare import
5. Raport final (X importate, Y erori)

**Template descărcabil:** `/templates/import-parteneri.xlsx`

**Livrabil:** Import funcțional cu validare și feedback

---

## 🔷 FAZA 3: DIALOG ADD/EDIT (3 zile)

### Obiectiv
Dialog complet cu tabs pentru CRUD pe toate entitățile.

### 3.1 PartnerFormDialog - Tab General (~6h)

**Fișiere:**
```
Components/Pages/Administrare/ParteneriComponents/
├── PartnerFormDialog.razor
└── PartnerFormDialog.razor.cs
```

**Formular dinamic:**
- Selectare Categorie → schimbă câmpurile afișate
- Selectare TipEntitate → validări specifice
- Câmpuri comune: Contact, Observații
- Checkboxes pentru RolPartener (flags)

**Livrabil:** Tab General funcțional cu formular dinamic

---

### 3.2 AddressesPanel CRUD (~4h)

**Fișiere:**
```
Components/Pages/Administrare/ParteneriComponents/
├── AddressesPanel.razor
└── AddressesPanel.razor.cs
```

**Funcționalități:**
- Grid inline cu adrese
- Add/Edit/Delete
- Marcare adresă principală
- Tip adresă: Sediu, Corespondență, Livrare, Facturare

**Livrabil:** Panel adrese funcțional

---

### 3.3 ContactsPanel + BankAccountsPanel (~4h)

**Fișiere:**
```
├── ContactsPanel.razor
├── ContactsPanel.razor.cs
├── BankAccountsPanel.razor
└── BankAccountsPanel.razor.cs
```

**Funcționalități:**
- Grid inline pentru fiecare
- CRUD complet
- Validare IBAN în timp real

**Livrabil:** Panels funcționale

---

### 3.4 RepresentativesPanel (~4h)

**Fișiere:**
```
├── RepresentativesPanel.razor
└── RepresentativesPanel.razor.cs
```

**Funcționalități:**
- Grid cu reprezentanți
- Tip: Administrator, Asociat, Împuternicit, Reprezentant Legal
- Link opțional cu Persoane

**Livrabil:** Panel reprezentanți funcțional

---

### 3.5 PartnerViewDialog Read-Only (~4h)

**Fișiere:**
```
├── PartnerViewDialog.razor
└── PartnerViewDialog.razor.cs
```

**Funcționalități:**
- View-only cu toate datele
- Tabs similare cu Edit
- Buton "Editează" → deschide PartnerFormDialog

**Livrabil:** Dialog view funcțional

---

## 🔷 FAZA 4: INTEGRĂRI ANAF + VIES (2 zile)

### Obiectiv
Verificare automată CUI și VAT ID în timp real.

### 4.1 AnafVerificationService + Cache (~6h)

**Fișiere:**
```
Features/Administrare/Parteneri/Services/
├── IAnafVerificationService.cs
└── AnafVerificationService.cs
```

**Implementare:**
1. Check cache (24h expiry)
2. Call API ANAF v8
3. Parse response
4. Save to cache
5. Update Partner record

**Endpoint:** `https://webservicesp.anaf.ro/PlatitorTvaRest/api/v8/ws/tva`

**Livrabil:** Verificare ANAF funcțională cu cache

---

### 4.2 ViesVerificationService (~4h)

**Fișiere:**
```
Features/Administrare/Parteneri/Services/
├── IViesVerificationService.cs
└── ViesVerificationService.cs
```

**Implementare:**
- Call VIES REST API
- Validare VAT ID format per țară
- Parse response (valid/invalid, name, address)

**Endpoint:** `https://ec.europa.eu/taxation_customs/vies/rest-api/...`

**Livrabil:** Verificare VIES funcțională

---

### 4.3 UI Verificare (Buton, Status) (~4h)

**Funcționalități:**
- Buton "Verifică ANAF" lângă câmp CUI
- Buton "Verifică VIES" lângă câmp VAT ID
- Status vizual: ✅ Valid, ❌ Invalid, ⏳ Verificare...
- Populare automată date din răspuns (denumire, adresă, etc.)

**Livrabil:** UI complet pentru verificare

---

## 🔷 FAZA 5: TESTING + POLISH (1.5 zile)

### Obiectiv
Calitate și stabilitate înainte de production.

### 5.1 Unit Tests Validators (~4h)

**Fișier:** `Tests/ValyanERP.Web.Tests/Parteneri/ValidatorTests.cs`

**Teste:**
- CNP valid/invalid (multiple scenarii)
- CUI valid/invalid
- IBAN valid/invalid (RO, DE, etc.)
- VAT ID valid/invalid

**Coverage target:** 100% pentru algoritmi validare

---

### 5.2 Integration Tests ANAF/VIES (~4h)

**Fișier:** `Tests/ValyanERP.Web.Tests/Parteneri/IntegrationTests.cs`

**Teste:**
- ANAF mock responses
- VIES mock responses
- Cache expiry behavior
- Error handling (API down)

---

### 5.3 E2E Test Flow Complet (~4h)

**Scenarii Playwright:**
1. Create partner SC (SRL) cu toate datele
2. Create partner PF cu CNP
3. Edit partner - schimbă adresă
4. Delete partner (soft)
5. Import Excel cu 10 parteneri
6. Export Excel și verifică conținut
7. Verificare ANAF (mock)

---

## 📋 CHECKLIST FINAL

### Pre-Deploy

- [ ] Build fără erori sau warnings
- [ ] Toate unit tests pass
- [ ] Integration tests pass
- [ ] E2E tests pass
- [ ] Manual testing pe scenarii principale
- [ ] Styling consistent cu design system
- [ ] Responsive pe mobile/tablet
- [ ] Performance OK pe 2000+ records
- [ ] Error handling complet
- [ ] Logging pentru debugging

### Documente de Actualizat

- [ ] `DevSupport/Analysis/Parteneri-Implementation-Proposal-2026-01-12.md` → marcat COMPLET
- [ ] `README.md` → secțiune Parteneri
- [ ] `DevSupport/SystemParameters-Documentation.md` → dacă sunt parametri noi

---

## 🚀 NEXT STEPS

**Când ești gata, confirmă pentru a începe cu:**

1. **FAZA 1.1** - Script SQL pentru tabele principale

Voi crea fișierul `Database/Scripts/020_Partners.sql` cu toate tabelele.

---

**Status:** ⏳ PLAN CREAT - Aștept confirmare pentru start  
**Data:** 12 Ianuarie 2026

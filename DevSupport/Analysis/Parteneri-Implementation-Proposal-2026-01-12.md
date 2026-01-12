# 🤝 Propunere Implementare: Pagina Parteneri

**Data:** 12 Ianuarie 2026  
**Versiune:** 1.0  
**Status:** Propunere de arhitectură  

**Referință:** Bazat pe implementarea Societatea Proprie

---

## 📋 Cuprins

1. [Cerințe de Business](#1-cerințe-de-business)
2. [Analiză Tipuri Entități](#2-analiză-tipuri-entități)
3. [Arhitectura Bazei de Date](#3-arhitectura-bazei-de-date)
4. [Modele C# (Vertical Slices)](#4-modele-c-vertical-slices)
5. [Arhitectura UI/UX](#5-arhitectura-uiux)
6. [Diferențe față de Societatea Proprie](#6-diferențe-față-de-societatea-proprie)
7. [Plan de Implementare](#7-plan-de-implementare)
8. [Estimări și Riscuri](#8-estimări-și-riscuri)

---

## 1. CERINȚE DE BUSINESS

### 1.1 Scop

Pagina Parteneri gestionează **toate entitățile externe** cu care compania interacționează:
- Furnizori
- Clienți
- Ambele (Furnizor + Client)
- Parteneri generici (colaboratori, subcontractori, etc.)

### 1.2 Diferențe cheie față de Societatea Proprie

| Aspect | Societatea Proprie | Parteneri |
|--------|-------------------|-----------|
| **Scop** | Companii proprii (1-10 entități) | Mii de parteneri externi |
| **Structură** | Arbore ierarhic (Grup → Companie → WP → Locație) | Lista plată cu filtre și categorii |
| **UI Pattern** | TreeView + Detail Panel | SfDataGrid cu filtre avansate |
| **Tipuri entități** | 1 tip (companie proprie) | 50+ tipuri (PF, PJ, instituții, etc.) |
| **Validări** | CUI/RegCom obligatorii | Depinde de tipul entității |
| **Adrese** | Multiple locații per companie | 1-3 adrese per partener (sediu, livrare, facturare) |

### 1.3 Cazuri de Utilizare

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           PARTENERI DE AFACERI                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐   ┌─────────────┐     │
│  │  FURNIZOR   │   │   CLIENT    │   │   AMBELE    │   │   ALTELE    │     │
│  │ (Purchases) │   │   (Sales)   │   │(Both roles) │   │(Colaborator)│     │
│  └──────┬──────┘   └──────┬──────┘   └──────┬──────┘   └──────┬──────┘     │
│         │                 │                 │                 │             │
│         └────────────┬────┴────────┬────────┴────────┬────────┘             │
│                      │             │                 │                       │
│               ┌──────▼──────┐ ┌────▼────┐ ┌─────────▼─────────┐             │
│               │ PERSOANĂ    │ │SOCIETATE│ │    INSTITUȚIE     │             │
│               │   FIZICĂ    │ │COMERCIALĂ│ │  PUBLICĂ/ALTELE  │             │
│               └─────────────┘ └─────────┘ └───────────────────┘             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. ANALIZĂ TIPURI ENTITĂȚI

### 2.1 Categorii Principale

Am organizat cele 50+ tipuri de entități în **categorii logice** pentru management simplificat:

| Categoria | Cod | Descriere | Nr. Subtipuri |
|-----------|-----|-----------|---------------|
| **Persoană Fizică** | `PF` | Persoane fizice rezidente/nerezidente | 3 |
| **PFA / Profesii Liberale** | `PFA` | Activități independente | 8 |
| **Societate Comercială** | `SC` | SRL, SA, SNC, etc. | 6 |
| **Entitate Non-Profit** | `NP` | Asociații, fundații, sindicate | 7 |
| **Instituție Publică RO** | `IP` | Ministere, primării, ANAF | 7 |
| **Entitate Diplomatică** | `DIP` | Ambasade, consulate | 4 |
| **Organizație Internațională** | `OI` | ONU, UE, NATO, FMI | 5 |
| **Entitate Străină** | `STR` | Companii și instituții străine | 4 |
| **Entitate Specială** | `SP` | Consorții, fonduri, cooperative | 8 |
| **Altele** | `ALT` | Asociații proprietari, succesiuni | 4 |

**Total: 10 categorii, 56 subtipuri**

---

### 2.2 Detaliere Tipuri Entități

#### 2.2.1 Persoane Fizice (`PF`)

| Cod | Denumire | Identificator | Validări Speciale |
|-----|----------|---------------|-------------------|
| `PF_RO` | Persoană fizică rezidentă | CNP (13 cifre) | Validare algoritm CNP |
| `PF_NR` | Persoană fizică nerezidentă | Pașaport / NIF străin | Format liber, țară obligatorie |
| `PF_MIN` | Minor (reprezentat legal) | CNP | + CNP/Nume reprezentant legal |

**Câmpuri specifice:**
- Nume, Prenume (obligatorii)
- CNP sau Pașaport/NIF
- Data nașterii (calculabilă din CNP)
- Reprezentant legal (pentru minori)

---

#### 2.2.2 PFA / Profesii Liberale (`PFA`)

| Cod | Denumire | Identificator | Note |
|-----|----------|---------------|------|
| `PFA_STD` | PFA - Persoană Fizică Autorizată | CUI (RO + cifre) | ONRC, autorizație |
| `II` | Întreprindere Individuală | CUI | Registrul comerțului |
| `IF` | Întreprindere Familială | CUI | Maximum 3 membri familie |
| `CAB_AV` | Cabinet individual avocat | Nr. înregistrare Barou | Nu are CUI |
| `CAB_NOT` | Cabinet notarial | Nr. înregistrare UNNPR | |
| `CAB_MED` | Cabinet medical | Nr. aviz DSP | + Cod parafă |
| `CAB_ARH` | Cabinet arhitectură | Nr. OAR | |
| `CAB_EXC` | Cabinet executor | Nr. înregistrare UNEJ | |
| `CEXP` | Expert contabil | Nr. înregistrare CECCAR | |
| `PINS` | Practician în insolvență | Nr. autorizație UNPIR | |

**Câmpuri specifice:**
- CUI sau Nr. autorizație/înregistrare (după tip)
- Organism de reglementare
- Nr. autorizație
- Data expirare autorizație

---

#### 2.2.3 Societăți Comerciale (`SC`)

| Cod | Denumire | Identificator | Note |
|-----|----------|---------------|------|
| `SRL` | Societate cu Răspundere Limitată | CUI + J../.../..... | Standard |
| `SRL_D` | SRL Debutant (Start-up Nation) | CUI + J../.../..... | Facilități fiscale |
| `SA` | Societate pe Acțiuni | CUI + J../.../..... | Capital social > 90.000 RON |
| `SNC` | Societate în Nume Colectiv | CUI + J../.../..... | Răspundere nelimitată |
| `SCS` | Societate în Comandită Simplă | CUI + J../.../..... | Comanditari + Comanditați |
| `SCA` | Societate în Comandită pe Acțiuni | CUI + J../.../..... | Hibrid SCS + SA |
| `SUC` | Sucursală societate străină | CUI + J../.../..... | + Societate-mamă străină |
| `FIL` | Filială | CUI + J../.../..... | + Societate-mamă română |

**Câmpuri specifice (comune):**
- CUI (obligatoriu, format RO + max 10 cifre)
- Reg. Com. (J../.../....)
- Denumire (obligatorie)
- Sediu social (adresă completă)
- Capital social
- CAEN principal + secundare

---

#### 2.2.4 Entități Non-Profit (`NP`)

| Cod | Denumire | Identificator | Registru |
|-----|----------|---------------|----------|
| `ASOC` | Asociație | CIF + Nr. registru | Registrul Asociațiilor și Fundațiilor |
| `FUND` | Fundație | CIF + Nr. registru | Registrul Asociațiilor și Fundațiilor |
| `FED` | Federație | CIF + Nr. registru | Federație de asociații |
| `SIND` | Sindicat | CIF | Registrul sindicatelor |
| `PATR` | Patronat | CIF | Registrul patronatelor |
| `PART` | Partid politic | CIF | Registrul partidelor |
| `CULT` | Cult religios / Unitate de cult | CIF | Registrul cultelor |

**Câmpuri specifice:**
- CIF (Cod Identificare Fiscală)
- Nr. înregistrare în registru specific
- Data înregistrării
- Scop/Obiect de activitate

---

#### 2.2.5 Instituții Publice Românești (`IP`)

| Cod | Denumire | Identificator | Exemple |
|-----|----------|---------------|---------|
| `IP_CEN` | Instituție publică centrală | CIF | Ministere, agenții naționale |
| `IP_LOC` | Instituție publică locală | CIF | Primării, consilii județene |
| `RA` | Regie autonomă | CUI | RADET, Metrorex |
| `CN` | Companie națională | CUI | CFR, Poșta Română |
| `INV_PUB` | Instituție de învățământ public | CIF | Universități, școli |
| `SPIT_PUB` | Spital public | CIF | Spitale, clinici |
| `ANAF` | Autoritate fiscală | CIF | ANAF, DITL, etc. |

**Câmpuri specifice:**
- CIF (obligatoriu)
- Cod SIRUTA (pentru instituții locale)
- Ordonator de credite
- Clasificare bugetară

---

#### 2.2.6 Entități Diplomatice (`DIP`)

| Cod | Denumire | Identificator | Note |
|-----|----------|---------------|------|
| `AMB` | Ambasadă | Cod țară | Reprezentanță diplomatică |
| `CONS` | Consulat | Cod țară + Oraș | Servicii consulare |
| `REP_DIP` | Reprezentanță diplomatică | Cod intern | Alte misiuni |
| `MIS_DIP` | Misiune diplomatică | Cod intern | Misiuni temporare |

**Câmpuri specifice:**
- Țara reprezentată
- Cod diplomatic
- Adresa în România
- Șef de misiune

---

#### 2.2.7 Organizații Internaționale (`OI`)

| Cod | Denumire | Identificator | Exemple |
|-----|----------|---------------|---------|
| `ONU` | ONU și agenții | Cod ONU | UNICEF, UNESCO, OMS |
| `UE` | Instituții Uniunea Europeană | - | Comisia, Parlamentul |
| `NATO` | NATO | - | Alianța Nord-Atlantică |
| `IFI` | Instituții financiare int. | - | Banca Mondială, FMI, BERD |
| `OI_ALT` | Alte organizații internat. | - | OCDE, Consiliul Europei |

**Câmpuri specifice:**
- Denumire oficială
- Acronim
- Țara sediului central
- Reprezentanță în România (dacă există)

---

#### 2.2.8 Entități Străine (`STR`)

| Cod | Denumire | Identificator | Note |
|-----|----------|---------------|------|
| `SC_UE` | Societate comercială UE | VAT ID (format EU) | Intracomunitar |
| `SC_NONUE` | Societate comercială non-UE | Cod fiscal local | Extra-comunitar |
| `IP_STR` | Instituție publică străină | Cod național | Ministere, agenții străine |
| `ONG_STR` | ONG străin | Cod național | Fundații, asociații străine |

**Câmpuri specifice:**
- VAT ID sau Cod fiscal național
- Țara de origine (obligatorie)
- Adresa în țara de origine
- Reprezentanță în România (opțional)

---

#### 2.2.9 Entități Speciale (`SP`)

| Cod | Denumire | Identificator | Note |
|-----|----------|---------------|------|
| `CONS_R` | Consorțiu | CUI | Grupare temporară |
| `GIE` | Grup de interes economic | CUI | Cooperare între companii |
| `SE` | Societate europeană | CUI | Societas Europaea |
| `SCE` | Societate cooperativă europeană | CUI | Cooperativă transfrontalieră |
| `COOP` | Cooperativă | CUI | Agricolă, de credit, consum |
| `CAR` | Casa de Ajutor Reciproc | CIF | CAR-uri |
| `FOND_INV` | Fond de investiții | Cod ASF | Reglementat ASF |
| `FOND_PENS` | Fond de pensii | Cod ASF | Pilonul II, III |

**Câmpuri specifice:**
- Identificator specific tipului
- Organism de reglementare
- Data constituirii
- Scop/Strategie (pentru fonduri)

---

#### 2.2.10 Alte Entități (`ALT`)

| Cod | Denumire | Identificator | Note |
|-----|----------|---------------|------|
| `ASOC_PROP` | Asociație de proprietari | CIF | Condominii |
| `HOA` | Asociație locatari | CIF | Similar cu ASOC_PROP |
| `MOST` | Moștenire neacceptată | Nr. dosar succesiune | În curs de succesiune |
| `INSOLV` | Entitate în insolvență | CUI + Nr. dosar | Sub administrare judiciară |

**Câmpuri specifice:**
- Identificator original
- Stare juridică actuală
- Practician în insolvență (dacă e cazul)
- Nr. dosar instanță

---

### 2.3 Matrice Câmpuri per Categorie

| Câmp | PF | PFA | SC | NP | IP | DIP | OI | STR | SP | ALT |
|------|:--:|:---:|:--:|:--:|:--:|:---:|:--:|:---:|:--:|:---:|
| **Nume/Prenume** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Denumire** | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **CNP** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **CUI** | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ |
| **CIF** | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ |
| **VAT ID** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Reg. Com.** | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| **Nr. Autorizație** | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Țara origine** | 🔸 | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **Sediu social** | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Adresă** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🔸 | ✅ | ✅ | ✅ |
| **IBAN** | ✅ | ✅ | ✅ | ✅ | ✅ | 🔸 | 🔸 | ✅ | ✅ | ✅ |
| **Reprezentant** | ❌ | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ | 🔸 |

Legendă: ✅ = Obligatoriu | 🔸 = Opțional | ❌ = Nu se aplică

---

## 3. ARHITECTURA BAZEI DE DATE

### 3.1 Diagrama ERD

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              PARTNERS                                        │
│                    (Tabel central pentru toți partenerii)                    │
├──────────────────────────────────────────────────────────────────────────────┤
│ Id (PK, GUID)                                                                │
│ Cod NVARCHAR(20) NOT NULL UNIQUE -- Cod intern unic (auto-generat)          │
│ Categoria NVARCHAR(10) NOT NULL -- PF, PFA, SC, NP, IP, DIP, OI, STR, SP, ALT│
│ TipEntitate NVARCHAR(20) NOT NULL -- Subtip detaliat (SRL, PFA_STD, etc.)   │
│ RolPartener TINYINT NOT NULL -- 0=Furnizor, 1=Client, 2=Ambele, 3=Altele    │
│                                                                              │
│ -- Identificare universală (unul din acestea obligatoriu)                    │
│ Denumire NVARCHAR(200) NULL -- Pentru PJ                                     │
│ DenumireScurta NVARCHAR(50) NULL                                             │
│ Nume NVARCHAR(100) NULL -- Pentru PF                                         │
│ Prenume NVARCHAR(100) NULL -- Pentru PF                                      │
│                                                                              │
│ -- Identificatori fiscali (mutual exclusive după tip)                        │
│ CNP NVARCHAR(13) NULL                                                        │
│ CUI NVARCHAR(20) NULL  -- Include prefix RO pentru plătitori TVA            │
│ CIF NVARCHAR(20) NULL  -- Cod Identificare Fiscală (instituții, non-profit) │
│ VATID NVARCHAR(20) NULL -- Pentru entități străine UE                        │
│ CodFiscalStrain NVARCHAR(50) NULL -- Pentru entități non-UE                  │
│ RegCom NVARCHAR(50) NULL -- J../.../.....                                    │
│ NrAutorizatie NVARCHAR(50) NULL -- Pentru PFA, cabinete                      │
│ Pasaport NVARCHAR(50) NULL -- Pentru PF nerezidente                          │
│                                                                              │
│ -- Date specifice                                                            │
│ DataInregistrare DATE NULL -- Data înființării/înregistrării                 │
│ DataRadiere DATE NULL -- Dacă e radiat/inactiv                               │
│ TaraOrigine NVARCHAR(3) NULL -- Cod ISO (RO, DE, US, etc.)                   │
│ CAENPrincipal NVARCHAR(10) NULL                                              │
│ CapitalSocial DECIMAL(18,2) NULL                                             │
│                                                                              │
│ -- Contact                                                                   │
│ Email NVARCHAR(256) NULL                                                     │
│ Telefon NVARCHAR(20) NULL                                                    │
│ TelefonSecundar NVARCHAR(20) NULL                                            │
│ Website NVARCHAR(200) NULL                                                   │
│ LogoUrl NVARCHAR(500) NULL                                                   │
│                                                                              │
│ -- Status TVA                                                                │
│ EstePlătitorTVA BIT NOT NULL DEFAULT 0                                       │
│ DataInregistrareTVA DATE NULL                                                │
│ StatusSplitTVA BIT NOT NULL DEFAULT 0                                        │
│                                                                              │
│ -- Status operațional (EXTINS)                                               │
│ PartnerStatus VARCHAR(20) NOT NULL DEFAULT 'Activ'                           │
│   -- Valori: 'Activ', 'Inactiv', 'Blocat', 'Suspendat', 'Radiat'            │
│ EsteActiv BIT NOT NULL DEFAULT 1                                             │
│ BlocatFacturare BIT NOT NULL DEFAULT 0                                       │
│ BlocatLivrare BIT NOT NULL DEFAULT 0                                         │
│ MotivBlocare NVARCHAR(500) NULL                                              │
│                                                                              │
│ -- Verificare ANAF (cu cache)                                                │
│ EsteVerificat BIT NOT NULL DEFAULT 0                                         │
│ AnafStatus VARCHAR(20) NULL  -- 'valid', 'inactiv', 'radiat'                │
│ AnafVerifiedAt DATETIME2 NULL                                                │
│ AnafVerifiedBy UNIQUEIDENTIFIER NULL                                         │
│                                                                              │
│ -- Credit și clasificare comercială                                          │
│ LimitaCredit DECIMAL(18,2) NULL                                              │
│ TermenPlataDef INT NULL DEFAULT 30  -- Zile                                  │
│ CategorieComercială VARCHAR(20) NULL  -- 'Standard', 'Premium', 'VIP'       │
│                                                                              │
│ -- SAF-T Romania (obligatoriu pentru export)                                 │
│ CodPartenerSAFT VARCHAR(35) NULL  -- Auto-generat                           │
│ TipPartenerSAFT VARCHAR(10) NULL  -- 'C', 'S', 'O', 'CS'                    │
│ SupplierID VARCHAR(35) NULL                                                  │
│ CustomerID VARCHAR(35) NULL                                                  │
│                                                                              │
│ -- Identificator temporar (pentru entități fără CUI/CNP)                     │
│ IdentificatorTemp VARCHAR(50) NULL                                           │
│ MotivLipsaIdentificator NVARCHAR(200) NULL                                   │
│                                                                              │
│ -- Adresa principală (denormalizat pentru performanță)                       │
│ AdresaPrincipalaId UNIQUEIDENTIFIER NULL                                     │
│                                                                              │
│ -- Note                                                                      │
│ Observatii NVARCHAR(2000) NULL                                               │
│                                                                              │
│ -- Audit                                                                     │
│ IsActive BIT NOT NULL DEFAULT 1                                              │
│ CreatedAt, UpdatedAt, CreatedBy, UpdatedBy                                   │
│                                                                              │
│ -- Constraints                                                               │
│ CK_Partners_HasIdentifier: cel puțin un identificator obligatoriu           │
│ CK_Partners_Status: validare valori PartnerStatus                            │
└──────────────────────────────────────────────────────────────────────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │ 1:N                         │ 1:N                         │ 1:N
        ▼                             ▼                             ▼
┌─────────────────────┐    ┌─────────────────────┐    ┌─────────────────────┐
│  PARTNER_ADDRESSES  │    │   PARTNER_CONTACTS  │    │  PARTNER_BANK_ACCTS │
│   (Adrese multiple) │    │(Persoane de contact)│    │   (Conturi bancare) │
├─────────────────────┤    ├─────────────────────┤    ├─────────────────────┤
│ Id (PK, GUID)       │    │ Id (PK, GUID)       │    │ Id (PK, GUID)       │
│ PartnerId (FK)      │    │ PartnerId (FK)      │    │ PartnerId (FK)      │
│ TipAdresa TINYINT   │    │ Nume, Prenume       │    │ IBAN NVARCHAR(34)   │
│  0=Sediu            │    │ Functie             │    │ BIC NVARCHAR(11)    │
│  1=Corespondenta    │    │ Email, Telefon      │    │ Banca NVARCHAR(100) │
│  2=Livrare          │    │ EsteDecident BIT    │    │ Moneda NVARCHAR(3)  │
│  3=Facturare        │    │ Observatii          │    │ EstePrincipal BIT   │
│ Adresa NVARCHAR(500)│    │ IsActive, SortOrder │    │ IsActive            │
│ Localitate, Judet   │    │ CreatedAt, ...      │    │ CreatedAt, ...      │
│ CodPostal, Tara     │    └─────────────────────┘    └─────────────────────┘
│ EstePrincipala BIT  │
│ IsActive            │
│ CreatedAt, ...      │
└─────────────────────┘
                                      │
                                      │ 1:N
                                      ▼
                    ┌─────────────────────────────────┐
                    │    PARTNER_REPRESENTATIVES      │
                    │   (Reprezentanți legali/admin)  │
                    ├─────────────────────────────────┤
                    │ Id (PK, GUID)                   │
                    │ PartnerId (FK)                  │
                    │ PersoanaId (FK → Persoane) NULL │
                    │ Nume, Prenume (dacă nu e în Persoane) │
                    │ CNP NVARCHAR(13) NULL           │
                    │ Functie NVARCHAR(100)           │
                    │ TipReprezentant TINYINT         │
                    │   0=Administrator               │
                    │   1=Asociat                     │
                    │   2=Împuternicit                │
                    │   3=ReprezentantLegal           │
                    │ DataNumire DATE                 │
                    │ DataExpirare DATE NULL          │
                    │ IsActive, SortOrder             │
                    │ CreatedAt, ...                  │
                    └─────────────────────────────────┘
```

---

### 3.2 Indexuri și Constraints

```sql
-- =============================================
-- Constraints principale
-- =============================================

-- Unicitate CUI (dacă e completat)
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CUI] 
ON [Partners] ([CUI]) WHERE [CUI] IS NOT NULL;

-- Unicitate CNP (dacă e completat)
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CNP] 
ON [Partners] ([CNP]) WHERE [CNP] IS NOT NULL;

-- Unicitate CIF (dacă e completat)
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_CIF] 
ON [Partners] ([CIF]) WHERE [CIF] IS NOT NULL;

-- Unicitate VAT ID (dacă e completat)
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Partners_VATID] 
ON [Partners] ([VATID]) WHERE [VATID] IS NOT NULL;

-- =============================================
-- Indexuri pentru căutare
-- =============================================

CREATE INDEX [IX_Partners_Categoria] ON [Partners] ([Categoria]) INCLUDE ([Denumire], [RolPartener]);
CREATE INDEX [IX_Partners_TipEntitate] ON [Partners] ([TipEntitate]) WHERE [IsActive] = 1;
CREATE INDEX [IX_Partners_RolPartener] ON [Partners] ([RolPartener]) WHERE [IsActive] = 1;
CREATE INDEX [IX_Partners_Denumire] ON [Partners] ([Denumire]) WHERE [IsActive] = 1;
CREATE INDEX [IX_Partners_NumePrenume] ON [Partners] ([Nume], [Prenume]) WHERE [IsActive] = 1;
CREATE INDEX [IX_Partners_Status] ON [Partners] ([PartnerStatus], [EsteActiv]) INCLUDE ([Denumire], [CUI]);

-- Indexuri pentru căutări exacte (identificatori)
CREATE INDEX [IX_Partners_CUI_Search] ON [Partners] ([CUI]) INCLUDE ([Id], [Denumire]) WHERE [IsActive] = 1;
CREATE INDEX [IX_Partners_CNP_Search] ON [Partners] ([CNP]) INCLUDE ([Id], [Nume], [Prenume]) WHERE [IsActive] = 1;

-- Full-text search DOAR pentru text liber (nu identificatori!)
CREATE FULLTEXT CATALOG PartnersCatalog AS DEFAULT;
CREATE FULLTEXT INDEX ON [Partners] ([Denumire], [DenumireScurta], [Nume], [Prenume], [Observatii]) 
KEY INDEX [PK_Partners] ON PartnersCatalog;
```

---

### 3.3 View-uri Utile

```sql
-- View pentru listă parteneri (optimizat pentru grid)
-- ✅ ACTUALIZAT: Fallback inteligent pentru adresa principală
CREATE VIEW [vw_Partners] AS
SELECT 
    p.Id,
    p.Cod,
    p.Categoria,
    p.TipEntitate,
    p.RolPartener,
    p.PartnerStatus,
    CASE 
        WHEN p.Denumire IS NOT NULL THEN p.Denumire
        ELSE CONCAT(p.Nume, ' ', p.Prenume)
    END AS DenumireAfisare,
    p.DenumireScurta,
    COALESCE(p.CUI, p.CIF, p.CNP, p.VATID, p.CodFiscalStrain, p.IdentificatorTemp) AS IdentificatorFiscal,
    p.Email,
    p.Telefon,
    p.TaraOrigine,
    p.EstePlătitorTVA,
    p.EsteActiv,
    p.EsteVerificat,
    p.AnafStatus,
    p.AnafVerifiedAt,
    p.BlocatFacturare,
    p.BlocatLivrare,
    p.LimitaCredit,
    p.CategorieComercială,
    p.IsActive,
    -- Adresa principală cu FALLBACK logic
    addr.Adresa AS AdresaPrincipala,
    addr.Localitate,
    addr.Judet,
    addr.CodPostal,
    addr.Tara,
    -- Statistici
    (SELECT COUNT(*) FROM PartnerAddresses WHERE PartnerId = p.Id AND IsActive = 1) AS NrAdrese,
    (SELECT COUNT(*) FROM PartnerContacts WHERE PartnerId = p.Id AND IsActive = 1) AS NrContacte,
    (SELECT COUNT(*) FROM PartnerBankAccounts WHERE PartnerId = p.Id AND IsActive = 1) AS NrConturi,
    -- Audit
    p.CreatedAt,
    uc.UserName AS CreatedByUserName
FROM Partners p
-- Fallback: Principală → Sediu → Facturare → Corespondență → Prima adăugată
LEFT JOIN PartnerAddresses addr ON addr.PartnerId = p.Id 
    AND addr.IsActive = 1
    AND addr.Id = (
        SELECT TOP 1 Id 
        FROM PartnerAddresses 
        WHERE PartnerId = p.Id AND IsActive = 1
        ORDER BY 
            EstePrincipala DESC,
            CASE TipAdresa 
                WHEN 0 THEN 1  -- Sediu
                WHEN 3 THEN 2  -- Facturare
                WHEN 1 THEN 3  -- Corespondență
                WHEN 2 THEN 4  -- Livrare
                ELSE 5 
            END,
            CreatedAt ASC
    )
LEFT JOIN Users uc ON p.CreatedBy = uc.Id;
GO
```

---

## 4. MODELE C# (VERTICAL SLICES)

### 4.1 Structura Foldere

```
Features/
└── Administrare/
    └── Parteneri/
        ├── Models/
        │   ├── Partner.cs
        │   ├── PartnerAddress.cs
        │   ├── PartnerContact.cs
        │   ├── PartnerBankAccount.cs
        │   ├── PartnerRepresentative.cs
        │   ├── Enums/
        │   │   ├── CategoriePartener.cs
        │   │   ├── TipEntitate.cs
        │   │   ├── RolPartener.cs
        │   │   ├── TipAdresa.cs
        │   │   └── TipReprezentant.cs
        │   └── DTOs/
        │       ├── PartnerListDto.cs
        │       ├── PartnerDetailDto.cs
        │       ├── CreatePartnerDto.cs
        │       └── UpdatePartnerDto.cs
        ├── Repositories/
        │   ├── IPartnerRepository.cs
        │   └── PartnerRepository.cs
        ├── Services/
        │   ├── IPartnerService.cs
        │   ├── PartnerService.cs
        │   ├── IPartnerValidationService.cs
        │   ├── PartnerValidationService.cs
        │   ├── IAnafVerificationService.cs  ← Integrare ANAF
        │   └── AnafVerificationService.cs
        ├── Validators/
        │   ├── PartnerValidator.cs
        │   ├── CuiValidator.cs
        │   ├── CnpValidator.cs
        │   └── IbanValidator.cs
        └── PartnersAdaptor.cs

Components/
└── Pages/
    └── Administrare/
        ├── Parteneri.razor
        ├── Parteneri.razor.cs
        ├── Parteneri.razor.css
        └── ParteneriComponents/
            ├── PartnerFormDialog.razor       ← Dialog Add/Edit principal
            ├── PartnerFormDialog.razor.cs
            ├── PartnerViewDialog.razor       ← Dialog View detalii
            ├── PartnerViewDialog.razor.cs
            ├── AddressesPanel.razor          ← Tab adrese
            ├── ContactsPanel.razor           ← Tab contacte
            ├── BankAccountsPanel.razor       ← Tab conturi bancare
            ├── RepresentativesPanel.razor    ← Tab reprezentanți
            └── AnafVerificationPanel.razor   ← Verificare ANAF
```

---

### 4.2 Modele Principale

```csharp
// Features/Administrare/Parteneri/Models/Enums/CategoriePartener.cs
public enum CategoriePartener
{
    [Display(Name = "Persoană Fizică")]
    PF = 0,
    
    [Display(Name = "PFA / Profesii Liberale")]
    PFA = 1,
    
    [Display(Name = "Societate Comercială")]
    SC = 2,
    
    [Display(Name = "Entitate Non-Profit")]
    NP = 3,
    
    [Display(Name = "Instituție Publică RO")]
    IP = 4,
    
    [Display(Name = "Entitate Diplomatică")]
    DIP = 5,
    
    [Display(Name = "Organizație Internațională")]
    OI = 6,
    
    [Display(Name = "Entitate Străină")]
    STR = 7,
    
    [Display(Name = "Entitate Specială")]
    SP = 8,
    
    [Display(Name = "Altele")]
    ALT = 9
}

// Features/Administrare/Parteneri/Models/Enums/RolPartener.cs
// ✅ ACTUALIZAT: Folosim Flags pentru roluri multiple simultane
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

// Utilizare:
// partner.RolPartener = RolPartener.Furnizor | RolPartener.Client; // = 3
// bool esteFurnizor = partner.RolPartener.HasFlag(RolPartener.Furnizor);
// SQL: WHERE (RolPartener & 1) = 1  -- toți furnizorii

// Features/Administrare/Parteneri/Models/Partner.cs
public class Partner
{
    public Guid Id { get; set; }
    
    [Required, StringLength(20)]
    public string Cod { get; set; } = string.Empty;
    
    [Required]
    public CategoriePartener Categoria { get; set; }
    
    [Required, StringLength(20)]
    public string TipEntitate { get; set; } = string.Empty;
    
    public RolPartener RolPartener { get; set; }
    
    // Identificare
    [StringLength(200)]
    public string? Denumire { get; set; }
    
    [StringLength(50)]
    public string? DenumireScurta { get; set; }
    
    [StringLength(100)]
    public string? Nume { get; set; }
    
    [StringLength(100)]
    public string? Prenume { get; set; }
    
    // Identificatori fiscali
    [StringLength(13)]
    public string? CNP { get; set; }
    
    [StringLength(20)]
    public string? CUI { get; set; }
    
    [StringLength(20)]
    public string? CIF { get; set; }
    
    [StringLength(20)]
    public string? VATID { get; set; }
    
    [StringLength(50)]
    public string? CodFiscalStrain { get; set; }
    
    [StringLength(50)]
    public string? RegCom { get; set; }
    
    [StringLength(50)]
    public string? NrAutorizatie { get; set; }
    
    [StringLength(50)]
    public string? Pasaport { get; set; }
    
    // Date specifice
    public DateTime? DataInregistrare { get; set; }
    public DateTime? DataRadiere { get; set; }
    
    [StringLength(3)]
    public string? TaraOrigine { get; set; }
    
    [StringLength(10)]
    public string? CAENPrincipal { get; set; }
    
    public decimal? CapitalSocial { get; set; }
    
    // Contact
    [StringLength(256), EmailAddress]
    public string? Email { get; set; }
    
    [StringLength(20)]
    public string? Telefon { get; set; }
    
    [StringLength(20)]
    public string? TelefonSecundar { get; set; }
    
    [StringLength(200)]
    public string? Website { get; set; }
    
    [StringLength(500)]
    public string? LogoUrl { get; set; }
    
    // Flags
    public bool EstePlătitorTVA { get; set; }
    public DateTime? DataInregistrareTVA { get; set; }
    public bool StatusSplitTVA { get; set; }
    
    // ✅ Status operațional (EXTINS)
    [StringLength(20)]
    public string PartnerStatus { get; set; } = "Activ";
    public bool EsteActiv { get; set; } = true;
    public bool BlocatFacturare { get; set; }
    public bool BlocatLivrare { get; set; }
    [StringLength(500)]
    public string? MotivBlocare { get; set; }
    
    // ✅ Verificare ANAF (cu cache)
    public bool EsteVerificat { get; set; }
    [StringLength(20)]
    public string? AnafStatus { get; set; }
    public DateTime? AnafVerifiedAt { get; set; }
    public Guid? AnafVerifiedBy { get; set; }
    
    // ✅ Credit și clasificare comercială
    public decimal? LimitaCredit { get; set; }
    public int? TermenPlataDef { get; set; } = 30;
    [StringLength(20)]
    public string? CategorieComercială { get; set; }
    
    // ✅ SAF-T Romania
    [StringLength(35)]
    public string? CodPartenerSAFT { get; set; }
    [StringLength(10)]
    public string? TipPartenerSAFT { get; set; }
    [StringLength(35)]
    public string? SupplierID { get; set; }
    [StringLength(35)]
    public string? CustomerID { get; set; }
    
    // ✅ Identificator temporar (pentru entități fără CUI/CNP)
    [StringLength(50)]
    public string? IdentificatorTemp { get; set; }
    [StringLength(200)]
    public string? MotivLipsaIdentificator { get; set; }
    
    // Adresa principală (denormalizat)
    public Guid? AdresaPrincipalaId { get; set; }
    
    [StringLength(2000)]
    public string? Observatii { get; set; }
    
    // Audit
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    
    // Navigation
    public List<PartnerAddress> Addresses { get; set; } = new();
    public List<PartnerContact> Contacts { get; set; } = new();
    public List<PartnerBankAccount> BankAccounts { get; set; } = new();
    public List<PartnerRepresentative> Representatives { get; set; } = new();
    
    // Computed
    public string DenumireAfisare => Denumire ?? $"{Nume} {Prenume}".Trim();
    public string IdentificatorFiscal => CUI ?? CIF ?? CNP ?? VATID ?? CodFiscalStrain ?? IdentificatorTemp ?? string.Empty;
    
    // ✅ Helper pentru roluri (flags)
    public bool EsteFurnizor => RolPartener.HasFlag(RolPartener.Furnizor);
    public bool EsteClient => RolPartener.HasFlag(RolPartener.Client);
}
```

---

## 5. ARHITECTURA UI/UX

### 5.1 Layout Principal

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 🤝 PARTENERI                                           [+ Adaugă Partener]  │
├─────────────────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ Filtre:  [Categorie ▼] [Rol ▼] [Tip Entitate ▼] [🔍 Căutare...]        │ │
│ │          [x] Doar activi    [Verificare ANAF] [Export Excel] [Export PDF] │
│ └─────────────────────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────────────────┐ │
│ │ SfDataGrid cu:                                                          │ │
│ │  • Coloane: Cod, Denumire, Tip, Rol, CUI/CNP, Email, Telefon, Status   │ │
│ │  • Server-side: Paging, Sorting, Filtering, Grouping                   │ │
│ │  • Lazy Load Grouping                                                   │ │
│ │  • Row Template cu iconițe per tip entitate                            │ │
│ │  • Context Menu: View, Edit, Delete, Verifică ANAF                     │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Dialog Add/Edit (Multi-Tab)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ✏️ Editare Partener: SC Example SRL                              [X]    │
├──────────────────────────────────────────────────────────────────────────┤
│ ┌──────────┬──────────┬──────────┬──────────┬──────────┬──────────┐     │
│ │📋 General│📍 Adrese │👥Contacte│🏦 Conturi│👔Reprez. │📊 ANAF   │     │
│ └──────────┴──────────┴──────────┴──────────┴──────────┴──────────┘     │
│                                                                          │
│  [Tab General - Date principale]                                         │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ Categorie: [Societate Comercială ▼]  Tip: [SRL ▼]  Rol: [Ambele ▼] │ │
│  ├─────────────────────────────────────────────────────────────────────┤ │
│  │ Denumire*: [________________________________]                       │ │
│  │ Den. Scurtă: [______________]                                       │ │
│  │                                                                     │ │
│  │ CUI*: [RO12345678    ] [🔍 Verifică ANAF]  ✅ Valid                 │ │
│  │ Reg. Com.: [J40/1234/2020  ]                                        │ │
│  │ CAEN Principal: [4711 - Comerț cu amănuntul...]                    │ │
│  │ Capital Social: [1.000.000,00] RON                                  │ │
│  │                                                                     │ │
│  │ [x] Plătitor TVA   [ ] Verificat ANAF   [x] Activ                  │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ Contact                                                             │ │
│  │ Email: [office@example.com         ]                                │ │
│  │ Telefon: [0212345678  ] Secundar: [            ]                   │ │
│  │ Website: [www.example.com          ]                                │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│                                      [Anulează]  [💾 Salvează]          │
└──────────────────────────────────────────────────────────────────────────┘
```

### 5.3 Formular Dinamic per Tip Entitate

**Principiu:** Câmpurile afișate se schimbă dinamic în funcție de `Categoria` și `TipEntitate`:

| Selecție | Câmpuri afișate |
|----------|-----------------|
| `PF → PF_RO` | Nume, Prenume, CNP, Data Nașterii |
| `PF → PF_NR` | Nume, Prenume, Pașaport, Țara |
| `SC → SRL` | Denumire, CUI, Reg.Com, CAEN, Capital |
| `NP → ASOC` | Denumire, CIF, Nr.Registru, Scop |
| `STR → SC_UE` | Denumire, VAT ID, Țara, Adresa străină |

---

## 6. DIFERENȚE FAȚĂ DE SOCIETATEA PROPRIE

### 6.1 Comparație Arhitecturală

| Aspect | Societatea Proprie | Parteneri |
|--------|-------------------|-----------|
| **UI Pattern** | TreeView + Detail Panel | SfDataGrid cu tabs |
| **Nr. entități** | 1-10 (companii proprii) | 1.000-100.000+ |
| **Structură date** | Ierarhică (4 nivele) | Plată cu relații 1:N |
| **Complexitate model** | 4 entități separate | 1 entitate cu 4 sub-entități |
| **Validări** | CUI/RegCom standard | Dinamice per tip (56 tipuri) |
| **Performance** | Load la start | Lazy load, paginare server |
| **Integrări** | - | ANAF, VIES, Registre |

### 6.2 Ce Reutilizăm din Societatea Proprie

- ✅ Design System (gradiente, culori, tipografie)
- ✅ Pattern-uri de dialog (SfDialog)
- ✅ Structură Vertical Slices
- ✅ Repository Pattern cu Dapper + SP
- ✅ Audit fields (CreatedAt, UpdatedAt, etc.)
- ✅ Soft delete (IsActive)

### 6.3 Ce Este Diferit

- ❌ NU folosim TreeView (nu e ierarhic)
- ❌ NU avem WorkPlace/Location separate (avem PartnerAddress)
- ❌ NU avem grupuri de companii (dar avem categorii)
- ➕ Formular dinamic per tip entitate
- ➕ Validări multiple (CNP, CUI, IBAN, VAT ID)
- ➕ Integrare API-uri externe (ANAF)
- ➕ Full-text search pentru mii de înregistrări

---

## 7. PLAN DE IMPLEMENTARE

### 7.1 Faze de Dezvoltare

#### **FAZA 1: Infrastructură (3-4 zile)**

| Task | Estimare | Descriere |
|------|----------|-----------|
| 1.1 | 0.5 zile | Creare script SQL tabele + indexuri |
| 1.2 | 0.5 zile | Creare stored procedures CRUD |
| 1.3 | 1 zi | Modele C# + Enum-uri + DTOs |
| 1.4 | 0.5 zile | Repository + Interface |
| 1.5 | 0.5 zile | Service + Business Logic |
| 1.6 | 0.5 zile | Validators (CNP, CUI, IBAN) |
| 1.7 | 0.5 zile | Înregistrare DI în Program.cs |

**Livrabil:** Backend funcțional, testabil via Swagger

---

#### **FAZA 2: UI Grid (2-3 zile)**

| Task | Estimare | Descriere |
|------|----------|-----------|
| 2.1 | 0.5 zile | Parteneri.razor + .razor.cs layout |
| 2.2 | 1 zi | PartnersAdaptor (server-side ops) |
| 2.3 | 0.5 zile | Configurare coloane, filtre, toolbar |
| 2.4 | 0.5 zile | Export Excel/PDF |
| 2.5 | 0.5 zile | Styling + responsive |

**Livrabil:** Grid funcțional cu CRUD de bază

---

#### **FAZA 3: Dialog Add/Edit (3-4 zile)**

| Task | Estimare | Descriere |
|------|----------|-----------|
| 3.1 | 1 zi | PartnerFormDialog (tab General) |
| 3.2 | 0.5 zile | Formular dinamic per tip entitate |
| 3.3 | 0.5 zile | AddressesPanel (CRUD adrese) |
| 3.4 | 0.5 zile | ContactsPanel (CRUD contacte) |
| 3.5 | 0.5 zile | BankAccountsPanel (CRUD conturi) |
| 3.6 | 0.5 zile | RepresentativesPanel |
| 3.7 | 0.5 zile | PartnerViewDialog (read-only) |

**Livrabil:** Dialog complet cu toate tab-urile

---

#### **FAZA 4: Validări și Integrări (2-3 zile)**

| Task | Estimare | Descriere |
|------|----------|-----------|
| 4.1 | 1 zi | Validare completă formular |
| 4.2 | 1 zi | Integrare API ANAF (verificare CUI) |
| 4.3 | 0.5 zile | AnafVerificationPanel |
| 4.4 | 0.5 zile | Error handling + notificări |

**Livrabil:** Validări complete, verificare ANAF

---

#### **FAZA 5: Testing și Polish (2 zile)**

| Task | Estimare | Descriere |
|------|----------|-----------|
| 5.1 | 0.5 zile | Unit tests pentru validators |
| 5.2 | 0.5 zile | Unit tests pentru service |
| 5.3 | 0.5 zile | Playwright E2E pentru flow complet |
| 5.4 | 0.5 zile | Performance testing (10k+ records) |

**Livrabil:** Aplicație testată și production-ready

---

### 7.2 Timeline Sumar

```
┌─────────┬─────────┬─────────┬─────────┬─────────┐
│  FAZA 1 │  FAZA 2 │  FAZA 3 │  FAZA 4 │  FAZA 5 │
│ Infra.  │  Grid   │ Dialog  │ Valid.  │ Testing │
│ 3-4 zile│ 2-3 zile│ 3-4 zile│ 2-3 zile│  2 zile │
├─────────┴─────────┴─────────┴─────────┴─────────┤
│                  TOTAL: 12-16 zile               │
└──────────────────────────────────────────────────┘
```

---

## 8. ESTIMĂRI ȘI RISCURI

### 8.1 Estimare Efort

| Componentă | Ore | Complexitate |
|------------|-----|--------------|
| Database (tabele, SP, views) | 16h | Medie |
| Backend (modele, repo, service) | 24h | Medie |
| Validators | 8h | Medie |
| UI Grid | 16h | Medie |
| UI Dialog (6 tabs) | 32h | Înaltă |
| Integrare ANAF | 8h | Medie |
| Testing | 16h | Medie |
| **TOTAL** | **120h** (~15 zile) | - |

### 8.2 Riscuri și Mitigări

| Risc | Probabilitate | Impact | Mitigare |
|------|--------------|--------|----------|
| Complexitate formular dinamic | Înaltă | Mediu | Folosim pattern cu componente reutilizabile |
| Performance 100k+ records | Medie | Înalt | Server-side paging obligatoriu, lazy load |
| API ANAF indisponibil | Medie | Scăzut | Graceful degradation, cache răspunsuri |
| 56 tipuri entități | Medie | Mediu | Grupăm în 10 categorii, UI adaptiv |
| Validări complexe (CNP, CUI, IBAN) | Scăzută | Scăzut | Biblioteci existente, teste unitare |

### 8.3 Dependențe

| Dependență | Status | Note |
|------------|--------|------|
| Syncfusion Blazor | ✅ Instalat | SfGrid, SfDialog, SfTab |
| Dapper | ✅ Instalat | Repository pattern |
| FluentValidation | ✅ Instalat | Validări |
| API ANAF | 🔸 De integrat | Public, gratuit, limită 1000 req/zi |
| VIES (VAT EU) | 🔸 Opțional | Pentru validare VAT ID UE |

---

## 9. RECOMANDĂRI

### 9.1 Implementare Incrementală

**Recomandare:** Începem cu un MVP care suportă doar cele mai comune tipuri:
1. **MVP:** SC (SRL, SA) + PF (rezident) + Furnizor/Client
2. **V1.1:** PFA, Non-profit
3. **V1.2:** Instituții publice, străine
4. **V2.0:** Toate celelalte tipuri + integrări complete

### 9.2 Decizii Arhitecturale Propuse

1. **Tabel unic `Partners`** vs. tabele separate per tip
   - ✅ Recomandare: Tabel unic cu câmpuri nullable
   - Avantaj: Simplitate, o singură interogare pentru grid

2. **Formular dinamic** vs. formulare separate
   - ✅ Recomandare: Un dialog cu secțiuni dinamice
   - Avantaj: Cod mai puțin, UX consistent

3. **Categorii în DB** vs. în cod
   - ✅ Recomandare: Enum în cod, persistat ca string în DB
   - Avantaj: Flexibilitate, type safety în C#

4. **Validare ANAF** sincronă vs. asincronă
   - ✅ Recomandare: Asincronă cu feedback vizual
   - Avantaj: UX fluid, nu blochează salvarea

---

## 10. RĂSPUNSURI LA ÎNTREBĂRI ✅

| Întrebare | Răspuns | Impact Implementare |
|-----------|---------|---------------------|
| **📊 Volum estimat** | 1.000 - 2.000 parteneri | Server-side paging recomandat, nu critic |
| **🔗 ANAF verificare CUI** | ✅ **DA - Obligatoriu** | Implementare Faza 4 |
| **🔗 VIES verificare VAT** | ✅ **DA - Obligatoriu** | Implementare Faza 4 |
| **🔗 Import Excel/CSV** | ✅ **DA - Necesar la start** | ➕ Adăugat în Faza 2 |
| **📋 Tipuri prioritare** | **SC** (Societăți) + **PF** (Persoane) | MVP focusat pe acestea |
| **🔒 Permisiuni** | Toți utilizatorii pot adăuga | Fără workflow special |
| **🔒 Aprobare** | ❌ Nu există | Simplificare UI |
| **📎 Atașamente** | ❌ Nu se atașează documente | Eliminat din scope |
| **🔄 Sincronizare** | ❌ Nu, deocamdată | Eliminat din scope |
| **🔄 Export necesar** | Denumire, Adresă, CUI, RegCom, Reprezentant | Export Excel simplu |

---

## 11. PLAN REVIZUIT (POST-FEEDBACK)

### 11.1 Scope Simplificat

**✅ În Scope (MVP):**
- Societăți Comerciale (SRL, SA, SNC, etc.)
- Persoane Fizice (rezidente, nerezidente)
- PFA și profesii liberale
- CRUD complet cu grid și dialog
- Verificare ANAF (CUI) - obligatoriu
- Verificare VIES (VAT ID UE) - obligatoriu
- Import Excel/CSV
- Export Excel (câmpuri: denumire, adresă, CUI, RegCom, reprezentant)

**❌ Eliminat din Scope:**
- Atașamente documente
- Workflow de aprobare
- Sincronizare cu sisteme externe
- Tipuri rare (diplomatic, organizații internaționale) - V2.0

### 11.2 Timeline Revizuit

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         PLAN IMPLEMENTARE REVIZUIT                          │
├─────────┬─────────┬─────────┬─────────┬─────────┬─────────────────────────┤
│  FAZA 1 │  FAZA 2 │  FAZA 3 │  FAZA 4 │  FAZA 5 │      CONȚINUT           │
│ 3 zile  │ 3 zile  │ 3 zile  │ 2 zile  │ 1.5 zile│                         │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────────────────────┤
│ DB      │ Grid    │ Dialog  │ ANAF+   │ Testing │                         │
│ Backend │ Import  │ Forms   │ VIES    │ Polish  │                         │
│ Models  │ Export  │ Tabs    │ Valid.  │         │                         │
├─────────┴─────────┴─────────┴─────────┴─────────┴─────────────────────────┤
│                    TOTAL REVIZUIT: ~12.5 zile                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 11.3 Detalii per Fază

#### **FAZA 1: Infrastructură (3 zile)**
| Task | Ore | Descriere |
|------|-----|-----------|
| 1.1 | 4h | Script SQL tabele Partners + sub-entități |
| 1.2 | 4h | Stored procedures CRUD |
| 1.3 | 6h | Modele C#: Partner, PartnerAddress, PartnerContact, PartnerBankAccount |
| 1.4 | 4h | Enum-uri: CategoriePartener, RolPartener, TipAdresa |
| 1.5 | 4h | Repository + Interface |
| 1.6 | 2h | Service + Business Logic |

#### **FAZA 2: UI Grid + Import/Export (3 zile)** ➕ EXTINS
| Task | Ore | Descriere |
|------|-----|-----------|
| 2.1 | 4h | Parteneri.razor layout |
| 2.2 | 6h | PartnersAdaptor (server-side ops) |
| 2.3 | 4h | Coloane, filtre, toolbar |
| 2.4 | 4h | **Export Excel** (denumire, adresă, CUI, RegCom, reprezentant) |
| 2.5 | 6h | **Import Excel/CSV** cu validare |

#### **FAZA 3: Dialog Add/Edit (3 zile)**
| Task | Ore | Descriere |
|------|-----|-----------|
| 3.1 | 6h | PartnerFormDialog (tab General) - formular dinamic |
| 3.2 | 4h | AddressesPanel (CRUD adrese) |
| 3.3 | 4h | ContactsPanel + BankAccountsPanel |
| 3.4 | 4h | RepresentativesPanel |
| 3.5 | 4h | PartnerViewDialog (read-only) |
| 3.6 | 2h | Styling + responsive |

#### **FAZA 4: Integrări ANAF + VIES (2 zile)** ⚠️ OBLIGATORIU
| Task | Ore | Descriere |
|------|-----|-----------|
| 4.1 | 6h | **AnafVerificationService** - API ANAF |
| 4.2 | 4h | **ViesVerificationService** - API VIES (UE) |
| 4.3 | 4h | UI: buton verificare, status, feedback |
| 4.4 | 2h | Cache răspunsuri (evită re-verificări) |

#### **FAZA 5: Testing + Polish (1.5 zile)**
| Task | Ore | Descriere |
|------|-----|-----------|
| 5.1 | 4h | Unit tests validators (CNP, CUI, IBAN) |
| 5.2 | 4h | Integration tests ANAF/VIES mock |
| 5.3 | 4h | E2E test flow complet |

---

## 12. SPECIFICAȚII TEHNICE ADIȚIONALE

### 12.1 Format Import Excel/CSV

**Coloane acceptate:**

| Coloană | Obligatoriu | Validare |
|---------|-------------|----------|
| Tip* | Da | SC/PF/PFA |
| Denumire/Nume* | Da | Max 200 chars |
| Prenume | Doar PF | Max 100 chars |
| CUI/CNP* | Da | Algoritm validare |
| RegCom | SC doar | Format J../.../..... |
| Adresa | Nu | Max 500 chars |
| Localitate | Nu | Max 100 chars |
| Judet | Nu | Max 50 chars |
| Email | Nu | Format email |
| Telefon | Nu | Max 20 chars |
| Reprezentant | Nu | Nume complet |

**Template Excel descărcabil:** `/templates/import-parteneri.xlsx`

### 12.2 Format Export Excel

**Coloane exportate (conform cerință):**

```
| Denumire | Adresa | Localitate | Judet | CUI | RegCom | Reprezentant |
|----------|--------|------------|-------|-----|--------|--------------|
| SC Example SRL | Str. X nr. 10 | București | S1 | RO12345678 | J40/1234/2020 | Ion Popescu |
```

### 12.3 Integrare API ANAF

**Endpoint:** `https://webservicesp.anaf.ro/PlatitorTvaRest/api/v8/ws/tva`

**Request:**
```json
[{"cui": 12345678, "data": "2026-01-12"}]
```

**Response utilizat:**
```json
{
  "denumire": "SC EXAMPLE SRL",
  "adresa": "Str. Exemplu nr. 10",
  "cui": "12345678",
  "nrRegCom": "J40/1234/2020",
  "scpTVA": true,
  "statusInactivi": false
}
```

### 12.4 Integrare API VIES (EU VAT)

**Endpoint:** `https://ec.europa.eu/taxation_customs/vies/rest-api/ms/{countryCode}/vat/{vatNumber}`

**Response utilizat:**
```json
{
  "isValid": true,
  "name": "EXAMPLE GMBH",
  "address": "Musterstraße 10, 10115 Berlin"
}
```

---

## 13. TABEL CACHE ANAF (ADĂUGAT DIN REVIEW)

### 13.1 Structura Tabelului

```sql
CREATE TABLE [dbo].[AnafVerificationCache] (
    [CUI] VARCHAR(20) NOT NULL PRIMARY KEY,
    
    -- Date returnate de ANAF
    [Denumire] NVARCHAR(200) NULL,
    [Adresa] NVARCHAR(500) NULL,
    [NrRegCom] VARCHAR(50) NULL,
    [CodPostal] VARCHAR(10) NULL,
    
    -- Status TVA
    [ScpTVA] BIT NULL,                         -- Înregistrat în scopuri de TVA
    [DataInregistrareTVA] DATE NULL,
    [DataAnulareTVA] DATE NULL,
    [StatusTVA] VARCHAR(50) NULL,              -- 'activ', 'inactiv', 'radiat'
    
    -- Status Split TVA
    [StatusSplitTVA] BIT NULL,
    [DataSplitTVA] DATE NULL,
    
    -- Status Inactivi
    [StatusInactivi] BIT NULL,
    [DataInactivare] DATE NULL,
    [DataReactivare] DATE NULL,
    
    -- Status Insolvență
    [StatusInsolventa] BIT NULL,
    [DataInsolventa] DATE NULL,
    
    -- Meta
    [ResponseJson] NVARCHAR(MAX) NULL,         -- JSON complet pentru debugging
    [VerifiedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,            -- Cache 24h default
    
    -- Index
    INDEX IX_AnafCache_ExpiresAt ([ExpiresAt])
);
```

### 13.2 Service cu Cache

```csharp
public class AnafVerificationService : IAnafVerificationService
{
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(24);
    private readonly IAnafCacheRepository _cacheRepository;
    private readonly HttpClient _httpClient;
    
    public async Task<AnafVerificationResult> VerifyAsync(string cui)
    {
        // 1. Check cache
        var cached = await _cacheRepository.GetByCuiAsync(cui);
        if (cached != null && cached.ExpiresAt > DateTime.UtcNow)
        {
            return new AnafVerificationResult
            {
                Success = true,
                FromCache = true,
                Data = cached
            };
        }
        
        // 2. Call ANAF API
        var response = await CallAnafApiAsync(cui);
        
        // 3. Save to cache
        await _cacheRepository.UpsertAsync(cui, response, _cacheExpiry);
        
        // 4. Return result
        return new AnafVerificationResult
        {
            Success = response.Found,
            FromCache = false,
            Data = response
        };
    }
}
```

---

## 14. DECIZII CONFIRMATE DIN REVIEW

| # | Decizie | Valoare | Rationale |
|---|---------|---------|-----------|
| 1 | **RolPartener** | ✅ Flags (bitwise) | Permite roluri multiple simultane |
| 2 | **Storicizare ANAF** | Suprascrie + Cache 24h | Simplu în MVP, versiuni în V2 |
| 3 | **Nr. adrese** | Nelimitat | Cu UI primele 3 afișate implicit |
| 4 | **Full-text search** | Selectiv (doar text liber) | CUI/CNP cu index normal pentru căutări exacte |
| 5 | **Audit history** | V2 | Complexitate prea mare pentru MVP |
| 6 | **Cache ANAF** | Tabel SQL | Persistență, ușor de debug |
| 7 | **Câmpuri SAF-T** | ✅ Adăugate | Conformitate export obligatoriu |
| 8 | **IdentificatorTemp** | ✅ Adăugat | Edge case: entități străine fără cod cunoscut |
| 9 | **Status extins** | ✅ Adăugat | Blocări, limită credit, clasificare |
| 10 | **Fallback adresă** | ✅ Implementat | Principală → Sediu → Facturare → Prima |

---

## 15. CHECKLIST PRE-IMPLEMENTARE

### 15.1 Verificări Tehnice

- [x] RolPartener cu Flags aprobat
- [x] Câmpuri SAF-T definite
- [x] Cache ANAF structurat
- [x] Fallback adresă principală definit
- [x] Full-text search optimizat
- [x] Status extins definit
- [ ] Endpoint ANAF v8 verificat activ
- [ ] VIES API access testat

### 15.2 Înainte de Faza 1

- [ ] Verifică endpoint ANAF (https://webservicesp.anaf.ro)
- [ ] Testează VIES API (https://ec.europa.eu/taxation_customs/vies)
- [ ] Creează template Excel pentru import
- [ ] Pregătește date de test (10-20 parteneri reali)

---

**Status:** ✅ **APROBAT - GATA DE IMPLEMENTARE**  
**Versiune Document:** 2.0 (cu review integrat)  
**Următorul pas:** Începere FAZA 1 - Infrastructură DB + Backend  
**Estimare finală:** ~12.5 zile lucrătoare  

**Data actualizare:** 12 Ianuarie 2026

# Implementare pagină Parteneri — ValyanClinic

## Instrucțiuni pentru GitHub Copilot

> **Citește skill-urile relevante înainte de a genera orice cod.**
> Skill-urile din `.github/skills/` definesc convențiile, pattern-urile și constrângerile
> specifice proiectului și au prioritate față de orice convenție generică pe care o cunoști.
> Consultă și `.github/copilot-instructions.md` și `.github/ERP_Architecture.md` pentru context global.

### Skill-uri obligatorii pentru această funcționalitate

| Skill | Fișier | Când se aplică |
|---|---|---|
| Arhitectură modul | `.github/ERP_Architecture.md` | Înainte de orice — context global |
| Instrucțiuni generale | `.github/copilot-instructions.md` | Convenții globale, reguli de cod |
| Vertical slice nou | `.github/skills/new-vertical-slice/` | Structura fișierelor pentru feature Partners |
| Modul nou | `.github/skills/new-module/` | Dacă Partners e un modul separat |
| Entitate de domeniu | `.github/skills/domain-entity/` | Entitățile `Partner`, `PartnerAddress` etc. |
| Migrație nouă | `.github/skills/new-migration/` | Migrațiile DbUp pentru cele 4 tabele |
| Obiecte SQL | `.github/skills/sql-objects/` | Stored procedures `sp_GetPartnersList`, `sp_GetPartnerById` |
| Repository Dapper | `.github/skills/dapper-repository/` | Implementarea repository cu Dapper |
| Gestionare erori | `.github/skills/error-handling/` | Error handling în service și controller |
| Feature frontend | `.github/skills/frontend-feature/` | Structura componentelor React |
| Split layout | `.github/skills/ui-split-layout/` | Layout master-detail stânga/dreapta |
| Tabel de date | `.github/skills/ui-data-table/` | Tabelul din panel stânga + sub-tabele |
| Secțiune formular | `.github/skills/ui-form-section/` | Câmpurile din „Date generale" |
| Grid editabil | `.github/skills/ui-editable-grid/` | Sub-tabelele colapsabile cu editare inline |
| Select căutabil | `.github/skills/ui-searchable-select/` | Dropdown Formă juridică / Tip partener |
| Badge status | `.github/skills/ui-status-badge/` | Badge-ul `Activ` din header partener |
| Input românesc | `.github/skills/ui-romanian-inputs/` | Formatare CUI, IBAN, telefon RO |
| Unit teste | `.github/skills/unit-test/` | Teste pentru `AnafService`, validators |
| Teste integrare | `.github/skills/integration-test/` | Teste endpoint-uri parteneri |

### Prompturi recomandate pentru generare cod

Folosește prompturile din `.github/prompts/` în această ordine:

1. `.github/prompts/new-migration.prompt.md` → migrații tabele
2. `.github/prompts/new-stored-procedure.prompt.md` → stored procedures
3. `.github/prompts/new-feature.prompt.md` → backend vertical slice
4. `.github/prompts/new-frontend-feature.prompt.md` → componente React
5. `.github/prompts/write-tests.prompt.md` → teste unitare și integrare
6. `.github/prompts/review-code.prompt.md` → review final înainte de PR

### Reguli stricte

- Nu genera migrații fără să fi citit skill-ul `new-migration` — convenția de versionare e critică
- Nu crea stored procedures fără să fi citit skill-ul `sql-objects` — există template și convenții de denumire
- Nu folosi `useEffect` pentru fetch — toate apelurile API trec prin TanStack Query (vezi `frontend-feature`)
- Nu folosi `int` ca tip de cheie primară — proiectul folosește `UNIQUEIDENTIFIER` cu `NEWSEQUENTIALID()` (vezi secțiunea 2.0)
- Orice componentă UI nouă se verifică mai întâi în skill-urile `ui-*` — nu reinventa ce există deja

---

## Cuprins

1. [Prezentare generală](#1-prezentare-generală)
2. [Structura bazei de date](#2-structura-bazei-de-date)
3. [Backend — Entități, Comenzi, Interogări](#3-backend--entități-comenzi-interogări)
4. [Integrare ANAF](#4-integrare-anaf)
5. [API Endpoints](#5-api-endpoints)
6. [Frontend — Componente React](#6-frontend--componente-react)
7. [State management](#7-state-management)
8. [Validări](#8-validări)
9. [Ordine de implementare](#9-ordine-de-implementare)

---

## 1. Prezentare generală

Pagina **Parteneri** este un master-detail split-panel:

- **Panel stânga (340px fix)** — tabel paginated cu `Cod`, `Denumire`, `CUI`; search live; buton `Nou`
- **Panel dreapta (fluid)** — detalii partener selectat, navigare prin 4 tab-uri, mod view/edit controlat de butonul `Modifică`

### Tab-uri panel dreapta

| Tab | Conținut |
|---|---|
| Date generale | Câmpuri fiscale + 3 sub-tabele colapsabile |
| Adrese | Tabel complet adrese |
| Persoane contact | Tabel complet persoane |
| Conturi bancare | Tabel complet conturi |

### Comportament editare

Pagina se deschide în **mod view** (read-only). Butonul `Modifică` (poziționat în header-ul partenerului, dreapta) comută în **mod edit**:
- câmpurile din „Date generale" devin editabile
- apare banner informativ albastru sub tab-uri
- butoanele `Salvează` / `Anulează` devin vizibile în footer
- butonul `Modifică` se transformă în `Închide`

Sub-tabelele (adrese, persoane, conturi) au editare independentă per rând, indiferent de modul paginii principale.

---

## 2. Structura bazei de date

### 2.0 Convenție chei primare — `UNIQUEIDENTIFIER` cu `NEWSEQUENTIALID()`

Toate tabelele din modulul Parteneri folosesc `UNIQUEIDENTIFIER` ca cheie primară, generat cu `NEWSEQUENTIALID()`.

**De ce `NEWSEQUENTIALID()` și nu `NEWID()` sau `INT IDENTITY`:**

| Criteriu | `INT IDENTITY` | `NEWID()` | `NEWSEQUENTIALID()` ✅ |
|---|---|---|---|
| Fragmentare index clustered | Zero | Severă — GUID aleatoriu = insert pe pagini random | Minimă — valori crescătoare secvențial |
| Predictabilitate ID în URL | Ușor de enumerat | OK | OK |
| Portabilitate / merge multi-tenant | Imposibil fără conflict | OK | OK |
| Scalare spre SaaS multi-clinic | Problematică | OK | OK |
| Generare ID pe client (optimistic UI) | Imposibil | `crypto.randomUUID()` | `crypto.randomUUID()` |

> `NEWSEQUENTIALID()` este funcție SQL Server — nu poate fi apelată în afara unui `DEFAULT` pe coloană.
> Pe client (React) și în C# (când e nevoie de ID înainte de insert), se folosește `Guid.NewGuid()` sau `crypto.randomUUID()`.
> Fragmentarea rămâne neglijabilă față de `NEWID()` deoarece inserțiile din aplicație sunt aproape întotdeauna în ordine cronologică.

**Mapping Dapper:** se folosește `Guid` în C# — Dapper mapează nativ `UNIQUEIDENTIFIER` ↔ `Guid`.

### 2.1 Tabela `Partners`

```sql
CREATE TABLE Partners (
    Id            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    Code          NVARCHAR(20)     NOT NULL,
    Name          NVARCHAR(200)    NOT NULL,
    CUI           NVARCHAR(20)     NULL,           -- nullable: persoane fizice
    LegalForm     NVARCHAR(20)     NOT NULL,        -- SRL | SA | PFA | RA | PF
    PartnerType   NVARCHAR(50)     NOT NULL,        -- Client | Furnizor | Client/Furnizor | Asigurator | Angajator
    IsVATPayer    BIT              NOT NULL DEFAULT 0,
    InvoiceEmail  NVARCHAR(150)    NULL,
    Phone         NVARCHAR(30)     NULL,
    IsActive      BIT              NOT NULL DEFAULT 1,
    AnafVerifiedAt DATETIME2       NULL,            -- timestamp ultima verificare ANAF reușită
    CreatedAt     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_Partners         PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Partners_Code    UNIQUE (Code),
    CONSTRAINT UQ_Partners_CUI     UNIQUE (CUI)
);
```

### 2.2 Tabela `PartnerAddresses`

```sql
CREATE TABLE PartnerAddresses (
    Id          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    PartnerId   UNIQUEIDENTIFIER NOT NULL REFERENCES Partners(Id) ON DELETE CASCADE,
    AddressType NVARCHAR(30)     NOT NULL,   -- SediuSocial | PunctLucru | Corespondenta | Alta
    Street      NVARCHAR(200)    NOT NULL,
    City        NVARCHAR(100)    NOT NULL,
    County      NVARCHAR(50)     NOT NULL,
    PostalCode  NVARCHAR(10)     NULL,
    Country     NVARCHAR(50)     NOT NULL DEFAULT 'România',
    IsPrimary   BIT              NOT NULL DEFAULT 0,
    CONSTRAINT PK_PartnerAddresses PRIMARY KEY CLUSTERED (Id)
);
```

### 2.3 Tabela `PartnerContacts`

```sql
CREATE TABLE PartnerContacts (
    Id          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    PartnerId   UNIQUEIDENTIFIER NOT NULL REFERENCES Partners(Id) ON DELETE CASCADE,
    FullName    NVARCHAR(150)    NOT NULL,
    Position    NVARCHAR(100)    NULL,
    Phone       NVARCHAR(30)     NULL,
    Email       NVARCHAR(150)    NULL,
    IsPrimary   BIT              NOT NULL DEFAULT 0,
    CONSTRAINT PK_PartnerContacts PRIMARY KEY CLUSTERED (Id)
);
```

### 2.4 Tabela `PartnerBankAccounts`

```sql
CREATE TABLE PartnerBankAccounts (
    Id          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
    PartnerId   UNIQUEIDENTIFIER NOT NULL REFERENCES Partners(Id) ON DELETE CASCADE,
    IBAN        NVARCHAR(34)     NOT NULL,
    BankName    NVARCHAR(100)    NOT NULL,
    Currency    NCHAR(3)         NOT NULL DEFAULT 'RON',
    IsDefault   BIT              NOT NULL DEFAULT 0,
    CONSTRAINT PK_PartnerBankAccounts PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_BankAccounts_IBAN   UNIQUE (IBAN)
);
```

### 2.5 Index-uri recomandate

```sql
CREATE INDEX IX_Partners_Name    ON Partners (Name);
CREATE INDEX IX_Partners_CUI     ON Partners (CUI);
CREATE INDEX IX_Partners_IsActive ON Partners (IsActive);
CREATE INDEX IX_PartnerAddresses_PartnerId ON PartnerAddresses (PartnerId);
CREATE INDEX IX_PartnerContacts_PartnerId  ON PartnerContacts (PartnerId);
CREATE INDEX IX_PartnerBankAccounts_PartnerId ON PartnerBankAccounts (PartnerId);
```

---

## 3. Backend — Entități, Comenzi, Interogări

### 3.1 Structura fișiere (Clean Architecture)

```
src/
  ValyanClinic.Domain/
    Entities/
      Partner.cs
      PartnerAddress.cs
      PartnerContact.cs
      PartnerBankAccount.cs
    Enums/
      LegalForm.cs
      PartnerType.cs
      AddressType.cs

  ValyanClinic.Application/
    Partners/
      Queries/
        GetPartnersList/
          GetPartnersListQuery.cs
          GetPartnersListQueryHandler.cs
          PartnerListItemDto.cs
        GetPartnerById/
          GetPartnerByIdQuery.cs
          GetPartnerByIdQueryHandler.cs
          PartnerDetailDto.cs
      Commands/
        CreatePartner/
          CreatePartnerCommand.cs
          CreatePartnerCommandHandler.cs
          CreatePartnerCommandValidator.cs
        UpdatePartner/
          UpdatePartnerCommand.cs
          UpdatePartnerCommandHandler.cs
          UpdatePartnerCommandValidator.cs
        UpsertPartnerAddress/
          UpsertPartnerAddressCommand.cs
          UpsertPartnerAddressCommandHandler.cs
        UpsertPartnerContact/
          UpsertPartnerContactCommand.cs
          UpsertPartnerContactCommandHandler.cs
        UpsertPartnerBankAccount/
          UpsertPartnerBankAccountCommand.cs
          UpsertPartnerBankAccountCommandHandler.cs
        DeletePartnerSubEntity/
          DeletePartnerAddressCommand.cs
          DeletePartnerContactCommand.cs
          DeletePartnerBankAccountCommand.cs

  ValyanClinic.Infrastructure/
    Services/
      AnafService.cs
      IAnafService.cs

  ValyanClinic.API/
    Controllers/
      PartnersController.cs
```

### 3.2 DTO-uri esențiale

```csharp
// Listă stânga — compact
public record PartnerListItemDto(
    Guid Id,
    string Code,
    string Name,
    string? CUI,
    bool IsActive
);

// Detalii dreapta — complet
public record PartnerDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? CUI,
    string LegalForm,
    string PartnerType,
    bool IsVATPayer,
    string? InvoiceEmail,
    string? Phone,
    bool IsActive,
    DateTime? AnafVerifiedAt,
    List<PartnerAddressDto> Addresses,
    List<PartnerContactDto> Contacts,
    List<PartnerBankAccountDto> BankAccounts
);

public record PartnerAddressDto(
    Guid Id, string AddressType, string Street,
    string City, string County, string? PostalCode,
    string Country, bool IsPrimary
);

public record PartnerContactDto(
    Guid Id, string FullName, string? Position,
    string? Phone, string? Email, bool IsPrimary
);

public record PartnerBankAccountDto(
    Guid Id, string IBAN, string BankName,
    string Currency, bool IsDefault
);
```

### 3.3 Stored procedures (Dapper)

```sql
-- GetPartnersList
CREATE PROCEDURE sp_GetPartnersList
    @Search     NVARCHAR(100) = NULL,
    @PageNumber INT = 1,
    @PageSize   INT = 50
AS
BEGIN
    SELECT Id, Code, Name, CUI, IsActive
    FROM Partners
    WHERE IsActive = 1
      AND (@Search IS NULL
           OR Name LIKE '%' + @Search + '%'
           OR CUI  LIKE '%' + @Search + '%'
           OR Code LIKE '%' + @Search + '%')
    ORDER BY Name
    OFFSET (@PageNumber - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END

-- GetPartnerById — returnează partenerul + sub-entitățile cu FOR JSON
CREATE PROCEDURE sp_GetPartnerById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SELECT p.*,
           (SELECT * FROM PartnerAddresses    WHERE PartnerId = p.Id FOR JSON PATH) AS Addresses,
           (SELECT * FROM PartnerContacts     WHERE PartnerId = p.Id FOR JSON PATH) AS Contacts,
           (SELECT * FROM PartnerBankAccounts WHERE PartnerId = p.Id FOR JSON PATH) AS BankAccounts
    FROM Partners p
    WHERE p.Id = @Id;
END
```

---

## 4. Integrare ANAF

### 4.1 Servicii disponibile

| Serviciu | URL | Mod |
|---|---|---|
| Sincron v9 | `https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva` | POST, răspuns imediat |
| Asincron v8 | `https://webservicesp.anaf.ro/AsynchWebService/api/v8/ws/tva` | POST → polling |

**Decizie arhitecturală:** se folosesc **ambele**, cu fallback automat:

1. Se încearcă mai întâi **sincronul v9** (timeout 5s)
2. Dacă serviciul sincron returnează eroare sau timeout → se escaladează la **asincronul v8**
3. Frontend-ul primește un `correlationId` și face polling până la rezoluție

### 4.2 Structuri cerere/răspuns ANAF

```csharp
// Cerere — același format pentru ambele servicii
public record AnafRequest(
    [JsonPropertyName("cui")]  int    cui,
    [JsonPropertyName("data")] string data  // format: "yyyy-MM-dd"
);

// Răspuns sincron v9
public record AnafSyncResponse(
    int cod,          // 200 = succes
    string message,
    List<AnafCompanyData> found
);

public record AnafCompanyData(
    [JsonPropertyName("denumire")]         string Denumire,
    [JsonPropertyName("cui")]              int    Cui,
    [JsonPropertyName("nrRegCom")]         string NrRegCom,
    [JsonPropertyName("adresa")]           string Adresa,
    [JsonPropertyName("scpTVA")]           bool   ScpTVA,      // plătitor TVA curent
    [JsonPropertyName("dataInregistrarii")]string DataInreg,
    [JsonPropertyName("stare_inregistrare")] string StareInregistrare
);

// Răspuns asincron v8 — faza 1 (acceptare)
public record AnafAsyncAcceptResponse(
    string correlationId,
    string status   // "accepted"
);

// Răspuns asincron v8 — faza 2 (polling rezultat)
public record AnafAsyncResultResponse(
    string status,          // "done" | "processing" | "error"
    List<AnafCompanyData> found
);
```

### 4.3 Implementare `AnafService`

```csharp
public interface IAnafService
{
    Task<AnafVerificationResult> VerifyAsync(string cui, CancellationToken ct = default);
}

public class AnafService : IAnafService
{
    private readonly HttpClient _http;
    private readonly ILogger<AnafService> _logger;

    private const string SyncUrl  = "https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva";
    private const string AsyncUrl = "https://webservicesp.anaf.ro/AsynchWebService/api/v8/ws/tva";

    public AnafService(HttpClient http, ILogger<AnafService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task<AnafVerificationResult> VerifyAsync(string cui, CancellationToken ct = default)
    {
        // Normalizare CUI — eliminăm prefixul "RO" dacă există
        var cuiNumeric = int.Parse(cui.Replace("RO", "").Trim());
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var payload = new[] { new AnafRequest(cuiNumeric, today) };

        // 1. Încearcă sincron v9
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var syncResp = await _http.PostAsJsonAsync(SyncUrl, payload, cts.Token);
            if (syncResp.IsSuccessStatusCode)
            {
                var data = await syncResp.Content.ReadFromJsonAsync<AnafSyncResponse>(cancellationToken: ct);
                if (data?.found?.Count > 0)
                    return AnafVerificationResult.Success(data.found[0]);
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            _logger.LogWarning("ANAF sync v9 indisponibil, fallback la async v8: {Message}", ex.Message);
        }

        // 2. Fallback asincron v8
        return await VerifyAsyncV8Async(payload, ct);
    }

    private async Task<AnafVerificationResult> VerifyAsyncV8Async(
        object payload, CancellationToken ct)
    {
        var acceptResp = await _http.PostAsJsonAsync(AsyncUrl, payload, ct);
        acceptResp.EnsureSuccessStatusCode();

        var accepted = await acceptResp.Content
            .ReadFromJsonAsync<AnafAsyncAcceptResponse>(cancellationToken: ct);

        // Polling cu back-off: 1s, 2s, 3s, 3s, 3s
        var delays = new[] { 1000, 2000, 3000, 3000, 3000 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay, ct);

            var pollResp = await _http.GetAsync(
                $"{AsyncUrl}/{accepted!.correlationId}", ct);

            if (!pollResp.IsSuccessStatusCode) continue;

            var result = await pollResp.Content
                .ReadFromJsonAsync<AnafAsyncResultResponse>(cancellationToken: ct);

            if (result?.status == "done" && result.found?.Count > 0)
                return AnafVerificationResult.Success(result.found[0]);

            if (result?.status == "error")
                return AnafVerificationResult.Failure("ANAF a returnat eroare.");
        }

        return AnafVerificationResult.Failure("Timeout polling ANAF.");
    }
}

// Result object
public class AnafVerificationResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public AnafCompanyData? Data { get; private init; }

    public static AnafVerificationResult Success(AnafCompanyData data) =>
        new() { IsSuccess = true, Data = data };

    public static AnafVerificationResult Failure(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}
```

### 4.4 Înregistrare în DI

```csharp
// Program.cs
builder.Services.AddHttpClient<IAnafService, AnafService>(client =>
{
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(30); // timeout global — polling gestionat intern
});
```

---

## 5. API Endpoints

```csharp
[ApiController]
[Route("api/partners")]
[Authorize]
public class PartnersController : ControllerBase
{
    // GET  api/partners?search=&page=1&pageSize=50
    [HttpGet]
    public Task<IActionResult> GetList([FromQuery] GetPartnersListQuery query, ...)

    // GET  api/partners/{id}
    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, ...)

    // POST api/partners
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreatePartnerCommand cmd, ...)

    // PUT  api/partners/{id}
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdatePartnerCommand cmd, ...)

    // POST api/partners/{id}/addresses
    [HttpPost("{id:guid}/addresses")]
    public Task<IActionResult> UpsertAddress(Guid id, [FromBody] UpsertPartnerAddressCommand cmd, ...)

    // DELETE api/partners/{id}/addresses/{addressId}
    [HttpDelete("{id:guid}/addresses/{addressId:guid}")]
    public Task<IActionResult> DeleteAddress(Guid id, Guid addressId, ...)

    // POST api/partners/{id}/contacts
    [HttpPost("{id:guid}/contacts")]
    public Task<IActionResult> UpsertContact(Guid id, [FromBody] UpsertPartnerContactCommand cmd, ...)

    // DELETE api/partners/{id}/contacts/{contactId}
    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    public Task<IActionResult> DeleteContact(Guid id, Guid contactId, ...)

    // POST api/partners/{id}/bank-accounts
    [HttpPost("{id:guid}/bank-accounts")]
    public Task<IActionResult> UpsertBankAccount(Guid id, [FromBody] UpsertPartnerBankAccountCommand cmd, ...)

    // DELETE api/partners/{id}/bank-accounts/{accountId}
    [HttpDelete("{id:guid}/bank-accounts/{accountId:guid}")]
    public Task<IActionResult> DeleteBankAccount(Guid id, Guid accountId, ...)

    // POST api/partners/anaf-verify?cui=RO23456789
    [HttpPost("anaf-verify")]
    public Task<IActionResult> AnafVerify([FromQuery] string cui, ...)
}
```

### 5.1 Răspuns endpoint ANAF verify

```json
// 200 OK — CUI găsit
{
  "isSuccess": true,
  "data": {
    "denumire": "CLINICA SF. LUCA SRL",
    "cui": 23456789,
    "nrRegCom": "J08/1234/2010",
    "adresa": "BRASOV, STR. MIHAI VITEAZU NR. 12",
    "scpTVA": true,
    "dataInregistrarii": "2010-05-15",
    "stareInregistrare": "INREGISTRAT din data 15.05.2010"
  }
}

// 200 OK — CUI negăsit sau eroare ANAF
{
  "isSuccess": false,
  "errorMessage": "CUI-ul nu a fost găsit în evidențele ANAF."
}
```

---

## 6. Frontend — Componente React

### 6.1 Structura fișiere

```
src/
  pages/
    Partners/
      PartnersPage.tsx          ← pagina principală, layout split
  features/
    partners/
      components/
        PartnerList.tsx          ← panel stânga: tabel + search
        PartnerDetail.tsx        ← panel dreapta: header + tab-uri
        PartnerGeneralTab.tsx    ← tab „Date generale"
        PartnerAddressesTab.tsx  ← tab „Adrese" (full tabel)
        PartnerContactsTab.tsx   ← tab „Persoane contact" (full tabel)
        PartnerBankAccountsTab.tsx ← tab „Conturi bancare" (full tabel)
        CollapsibleSection.tsx   ← wrapper colapsabil generic cu animație
        AnafVerifyButton.tsx     ← buton verificare ANAF cu state intern
        SubEntityModal.tsx       ← modal adăugare/editare sub-entitate
      hooks/
        usePartners.ts           ← TanStack Query: list + detail
        usePartnerMutations.ts   ← create, update, delete
        useAnafVerify.ts         ← logica verificare ANAF + auto-completare
      store/
        partnersUiStore.ts       ← Zustand: selectedId, editMode, activeTab
      api/
        partnersApi.ts           ← axios calls
      types/
        partner.types.ts
```

### 6.2 Componenta `AnafVerifyButton`

```tsx
// features/partners/components/AnafVerifyButton.tsx

interface Props {
  cui: string;
  onVerified: (data: AnafCompanyData) => void;
}

type VerifyStatus = 'idle' | 'loading' | 'success' | 'error';

export function AnafVerifyButton({ cui, onVerified }: Props) {
  const [status, setStatus] = useState<VerifyStatus>('idle');
  const [errorMsg, setErrorMsg] = useState('');

  // Butonul e activ doar dacă CUI-ul are minim 6 caractere
  const isDisabled = cui.replace('RO', '').trim().length < 6 || status === 'loading';

  const handleVerify = async () => {
    setStatus('loading');
    setErrorMsg('');
    try {
      const result = await partnersApi.verifyAnaf(cui);
      if (result.isSuccess) {
        setStatus('success');
        onVerified(result.data);
      } else {
        setStatus('error');
        setErrorMsg(result.errorMessage ?? 'Eroare necunoscută ANAF');
      }
    } catch {
      setStatus('error');
      setErrorMsg('Serviciul ANAF este indisponibil momentan.');
    }
  };

  return (
    <div className="anaf-verify-wrapper">
      <button
        onClick={handleVerify}
        disabled={isDisabled}
        className={`btn-anaf btn-anaf--${status}`}
        title={isDisabled ? 'Completați CUI-ul pentru a verifica' : 'Verificare ANAF'}
      >
        {status === 'loading' && <Spinner size={14} />}
        {status === 'success' && <CheckIcon size={14} />}
        {status !== 'loading' && status !== 'success' && <ShieldCheckIcon size={14} />}
        {status === 'loading' ? 'Se verifică...' : 'Verificare ANAF'}
      </button>
      {status === 'error' && (
        <span className="anaf-error">{errorMsg}</span>
      )}
      {status === 'success' && (
        <span className="anaf-success">Date completate din ANAF</span>
      )}
    </div>
  );
}
```

### 6.3 Hook `useAnafVerify` — auto-completare câmpuri

```ts
// features/partners/hooks/useAnafVerify.ts

export function useAnafVerify(
  setValue: UseFormSetValue<PartnerFormValues>
) {
  const handleVerified = useCallback((data: AnafCompanyData) => {
    // Auto-completează câmpurile formularului cu datele din ANAF
    setValue('name',        data.denumire,  { shouldDirty: true });
    setValue('nrRegCom',    data.nrRegCom,  { shouldDirty: true });
    setValue('isVATPayer',  data.scpTVA,    { shouldDirty: true });
    // Parsare formă juridică din denumire (SRL, SA, PFA etc.)
    const detectedForm = detectLegalForm(data.denumire);
    if (detectedForm) setValue('legalForm', detectedForm, { shouldDirty: true });
  }, [setValue]);

  return { handleVerified };
}

function detectLegalForm(denumire: string): LegalForm | null {
  const upper = denumire.toUpperCase();
  if (upper.includes('SRL'))  return 'SRL';
  if (upper.includes(' SA ') || upper.endsWith(' SA')) return 'SA';
  if (upper.includes('PFA'))  return 'PFA';
  if (upper.includes(' RA'))  return 'RA';
  return null;
}
```

### 6.4 Layout `PartnerGeneralTab` — plasare buton ANAF

```tsx
// În formularul „Date generale", câmpul CUI are butonul ANAF inline

<div className="info-item cui-field">
  <label>CUI / CIF</label>
  <div className="cui-input-row">
    <input
      {...register('cui')}
      readOnly={!editMode}
      placeholder="ex: RO12345678"
    />
    {editMode && (
      <AnafVerifyButton
        cui={watchCui}
        onVerified={handleVerified}
      />
    )}
  </div>
</div>
```

---

## 7. State management

### 7.1 Zustand store — UI state

```ts
// features/partners/store/partnersUiStore.ts

interface PartnersUiState {
  selectedPartnerId: string | null;   // Guid ca string în TypeScript
  editMode: boolean;
  activeTab: 'general' | 'adrese' | 'contacte' | 'conturi';

  setSelectedPartner: (id: string | null) => void;
  setEditMode: (on: boolean) => void;
  setActiveTab: (tab: PartnersUiState['activeTab']) => void;
}

export const usePartnersUiStore = create<PartnersUiState>((set) => ({
  selectedPartnerId: null,
  editMode: false,
  activeTab: 'general',

  setSelectedPartner: (id) => set({ selectedPartnerId: id, editMode: false, activeTab: 'general' }),
  setEditMode: (on) => set({ editMode: on }),
  setActiveTab: (tab) => set({ tab }),
}));
```

> **Notă:** la schimbarea partenerului selectat (`setSelectedPartner`), `editMode` se resetează automat pe `false` și tab-ul activ revine la `general`.

### 7.2 TanStack Query

```ts
// features/partners/hooks/usePartners.ts

export function usePartnersList(search: string) {
  return useQuery({
    queryKey: ['partners', 'list', search],
    queryFn: () => partnersApi.getList({ search }),
    staleTime: 1000 * 30,
  });
}

export function usePartnerDetail(id: string | null) {
  return useQuery({
    queryKey: ['partners', 'detail', id],
    queryFn: () => partnersApi.getById(id!),
    enabled: id !== null,
  });
}
```

---

## 8. Validări

### 8.1 Frontend (React Hook Form + Zod)

```ts
const partnerSchema = z.object({
  code:         z.string().min(1).max(20),
  name:         z.string().min(2).max(200),
  cui:          z.string().regex(/^(RO)?\d{6,10}$/, 'Format CUI invalid').optional().or(z.literal('')),
  legalForm:    z.enum(['SRL', 'SA', 'PFA', 'RA', 'PF']),
  partnerType:  z.enum(['Client', 'Furnizor', 'Client/Furnizor', 'Asigurator', 'Angajator']),
  isVATPayer:   z.boolean(),
  invoiceEmail: z.string().email('Email invalid').optional().or(z.literal('')),
  phone:        z.string().max(30).optional(),
});
```

### 8.2 Backend (FluentValidation)

```csharp
public class CreatePartnerCommandValidator : AbstractValidator<CreatePartnerCommand>
{
    public CreatePartnerCommandValidator(IPartnersRepository repo)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .MustAsync(async (code, ct) => !await repo.CodeExistsAsync(code, ct))
            .WithMessage("Codul este deja utilizat.");

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        RuleFor(x => x.CUI)
            .Matches(@"^(RO)?\d{6,10}$").When(x => !string.IsNullOrEmpty(x.CUI))
            .WithMessage("Format CUI invalid.");

        RuleFor(x => x.LegalForm).IsInEnum();
        RuleFor(x => x.PartnerType).IsInEnum();

        RuleFor(x => x.InvoiceEmail)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.InvoiceEmail));
    }
}
```

### 8.3 Validare IBAN

```csharp
// Validare IBAN Romania: RO + 2 cifre control + 4 litere bancă + 16 alfanumerice
RuleFor(x => x.IBAN)
    .Matches(@"^RO\d{2}[A-Z]{4}[A-Z0-9]{16}$")
    .WithMessage("IBAN invalid. Format așteptat: RO49 AAAA 1B31 0075 9384 0000");
```

---

## 9. Ordine de implementare

### Sprint 1 — Infrastructură și liste

- [ ] Migrații DbUp: `Partners`, `PartnerAddresses`, `PartnerContacts`, `PartnerBankAccounts`
- [ ] Stored procedures: `sp_GetPartnersList`, `sp_GetPartnerById`
- [ ] `GetPartnersListQueryHandler` + `GetPartnerByIdQueryHandler`
- [ ] `PartnersController` — GET endpoints
- [ ] `PartnerList.tsx` — panel stânga funcțional cu search debounced
- [ ] `usePartnersList` hook

### Sprint 2 — CRUD parteneri

- [ ] `CreatePartnerCommand` + validator + stored procedure
- [ ] `UpdatePartnerCommand` + validator + stored procedure
- [ ] `PartnerDetail.tsx` + `PartnerGeneralTab.tsx` — view mode
- [ ] Buton `Modifică` → edit mode cu câmpuri editabile
- [ ] `usePartnerMutations` hook
- [ ] Invalidare cache TanStack Query după mutații

### Sprint 3 — Sub-entități

- [ ] Upsert + Delete pentru Adrese, Contacte, Conturi bancare
- [ ] `CollapsibleSection.tsx` — component generic
- [ ] `SubEntityModal.tsx` — modal pentru adăugare/editare
- [ ] Tab-uri individuale: `PartnerAddressesTab`, `PartnerContactsTab`, `PartnerBankAccountsTab`

### Sprint 4 — Integrare ANAF

- [ ] `AnafService` — implementare cu fallback sync→async
- [ ] Endpoint `POST api/partners/anaf-verify`
- [ ] `AnafVerifyButton.tsx` — buton cu state (idle/loading/success/error)
- [ ] `useAnafVerify` hook — auto-completare câmpuri după verificare
- [ ] Persistare `AnafVerifiedAt` la salvare partener
- [ ] Afișare timestamp ultimei verificări în view mode

---

## Note implementare

- **`NEWSEQUENTIALID()` vs `NEWID()`** — `DEFAULT NEWSEQUENTIALID()` pe coloana `Id` garantează inserții secvențiale în index-ul clustered, eliminând fragmentarea. Pe client se folosește `crypto.randomUUID()` pentru ID optimistic înainte de confirmarea serverului; după răspuns, cache-ul TanStack Query se invalidează și UI-ul primește GUID-ul real din DB.
- **CUI cu prefix `RO`** — se stochează cu prefix în DB, se normalizează (eliminare `RO`) doar la apelul ANAF
- **Partener nou fără CUI** — câmpul CUI este opțional (persoane fizice); butonul ANAF se ascunde complet dacă `legalForm = 'PF'`
- **Unicitate IBAN** — constrângere la nivel de DB; eroarea 2627 din SQL Server se traduce în `400 Bad Request` cu mesaj explicit
- **Polling asincron ANAF** — maxim 5 încercări (total ~12s); dacă expiră, se afișează eroare cu sugestia de a reîncerca manual
- **`AnafVerifiedAt`** — se actualizează doar la o verificare reușită; se afișează în UI ca `Verificat ANAF: 15.05.2025`

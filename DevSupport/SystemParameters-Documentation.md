# 📋 Documentație Parametri Sistem - ValyanERP

## 🎯 Scopul Documentației

Această documentație oferă o privire completă asupra tuturor parametrilor de sistem din ValyanERP. **OBLIGATORIU: Orice adăugare, modificare sau ștergere de parametru TREBUIE documentată aici.**

---

## 📊 Structură Generală

### Categorii Disponibile
| Categorie | Scop | Parametri Activi |
|-----------|------|------------------|
| **Cache** | Configurare sistem de caching | 3 |
| **Validation** | Reguli de validare date | 3 |
| **Business** | Logică de business și workflow | 3 |
| **Session** | Gestionare sesiuni utilizatori | 2 |
| **Performance** | Optimizare performanță | 2 |
| **Enum** | Valori enumerare (JSON) | 1 |

---

## 📖 CATEGORIE: Cache

### 🔹 Cache.Persoane.DurationMinutes
- **Tip Date:** `int`
- **Valoare Curentă:** `5`
- **Valoare Implicită:** `5`
- **Interval Valid:** 1 - 60 minute
- **Descriere:** Durata de caching pentru lista de persoane în dropdown-uri
- **Impact Modificare:** 
  - ⬆️ Valoare mai mare = mai puține query-uri DB, risc date vechi
  - ⬇️ Valoare mai mică = date fresh, mai multe query-uri DB
- **Folosit În:**
  - `PersoaneService.GetAllSimpleAsync()`
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)

---

### 🔹 Cache.SizeLimit
- **Tip Date:** `int`
- **Valoare Curentă:** `1024`
- **Valoare Implicită:** `1024`
- **Interval Valid:** 256 - 10000 entries
- **Descriere:** Numărul maxim de intrări în cache-ul aplicației
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = mai multă memorie consumată, mai multe obiecte cached
  - ⬇️ Valoare mai mică = mai puțină memorie, compactare mai frecventă
- **Folosit În:**
  - `Program.cs` - IMemoryCache configuration (NOTĂ: citit la startup)
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)

---

### 🔹 Cache.CompactionPercentage
- **Tip Date:** `decimal`
- **Valoare Curentă:** `0.25`
- **Valoare Implicită:** `0.25`
- **Interval Valid:** 0.10 - 0.50 (10% - 50%)
- **Descriere:** Procentul de intrări eliminate când cache-ul atinge limita
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = eliminare agresivă, mai mult spațiu eliberat
  - ⬇️ Valoare mai mică = eliminare graduală, mai puțin spațiu eliberat
- **Folosit În:**
  - `Program.cs` - IMemoryCache configuration (NOTĂ: citit la startup)
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)

---

## 📖 CATEGORIE: Validation

### 🔹 Validation.Password.MinLength
- **Tip Date:** `int`
- **Valoare Curentă:** `8`
- **Valoare Implicită:** `8`
- **Interval Valid:** 6 - 32 caractere
- **Descriere:** Lungimea minimă obligatorie pentru parole utilizatori
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = securitate crescută, UX mai dificil
  - ⬇️ Valoare mai mică = risc securitate, UX mai ușor
- **Folosit În:**
  - `Program.cs` - Identity options (NOTĂ: citit la startup)
  - `ResetPasswordValidator.cs` - validare parole noi
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **⚠️ ATENȚIE:** Modificarea acestui parametru NU afectează parole existente, doar validarea pentru parole noi!

---

### 🔹 Validation.Password.RequireSpecialChar
- **Tip Date:** `bool`
- **Valoare Curentă:** `true`
- **Valoare Implicită:** `true`
- **Interval Valid:** true/false
- **Descriere:** Cere caracter special în parole (!@#$%^&*()_+-=[]{}|;:,.<>?)
- **Impact Modificare:**
  - `true` = securitate crescută, parole mai puternice
  - `false` = UX mai ușor, dar securitate scăzută
- **Folosit În:**
  - `ResetPasswordValidator.cs` - validare parole noi
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-13 (adăugat pentru FluentValidation)
- **Modificat De:** GitHub Copilot
- **⚠️ ATENȚIE:** Modificarea afectează doar validarea parolelor noi, nu cele existente!

---

### 🔹 Validation.Nume.MaxLength
- **Tip Date:** `int`
- **Valoare Curentă:** `100`
- **Valoare Implicită:** `100`
- **Interval Valid:** 50 - 200 caractere
- **Descriere:** Lungimea maximă permisă pentru câmpul Nume în entitatea Persoana
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = permite nume mai lungi (ex: nume compuse)
  - ⬇️ Valoare mai mică = risc trunchiere date
- **Folosit În:**
  - Validări FluentValidation pentru Persoana (poate fi integrat)
  - UI constraints în formulare
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **⚠️ ATENȚIE:** Schema bazei de date are NVARCHAR(100). Dacă crești valoarea peste 100, trebuie migrat schema!

---

### 🔹 Validation.Email.MaxLength
- **Tip Date:** `int`
- **Valoare Curentă:** `256`
- **Valoare Implicită:** `256`
- **Interval Valid:** 100 - 320 caractere
- **Descriere:** Lungimea maximă permisă pentru adrese email (RFC 5321 standard)
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = suport email-uri foarte lungi (rar necesar)
  - ⬇️ Valoare mai mică = risc respingere email-uri valide
- **Folosit În:**
  - Validări email în Persoana, User, etc.
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **📚 Referință:** RFC 5321 specifică 320 caractere (64@255 + 1)

---

## 📖 CATEGORIE: Business

### 🔹 Business.DefaultCountry
- **Tip Date:** `string`
- **Valoare Curentă:** `Romania`
- **Valoare Implicită:** `Romania`
- **Interval Valid:** Orice țară validă
- **Descriere:** Țara implicită pentru persoane noi când nu este specificată
- **Impact Modificare:**
  - Toate persoanele noi create fără țară vor primi această valoare
- **Folosit În:**
  - `PersoaneService.CreateAsync()`
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)

---

### 🔹 Business.Pagination.DefaultPageSize
- **Tip Date:** `int`
- **Valoare Curentă:** `20`
- **Valoare Implicită:** `20`
- **Interval Valid:** 5 - 200 înregistrări (multipli de 5)
- **Descriere:** Numărul implicit de înregistrări per pagină în grid-uri (trebuie să fie multiplu de 5)
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = mai puține request-uri, mai mult trafic per request
  - ⬇️ Valoare mai mică = mai multe request-uri, navigare mai frecventă
- **Folosit În:**
  - Toate grid-urile Syncfusion (Persoane, Utilizatori, etc.)
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-13 (actualizare interval și validare)
- **Modificat De:** GitHub Copilot

---

### 🔹 Business.Pagination.MaxPageSize
- **Tip Date:** `int`
- **Valoare Curentă:** `1000`
- **Valoare Implicită:** `1000`
- **Interval Valid:** 100 - 5000 înregistrări
- **Descriere:** Numărul maxim de înregistrări permis per pagină (protecție DoS)
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = risc performanță, risc timeout
  - ⬇️ Valoare mai mică = protecție mai strictă
- **Folosit În:**
  - Validare request-uri grid cu page size custom
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **⚠️ ATENȚIE:** Valori peste 5000 pot cauza timeout-uri și probleme de memorie!

---

## 📖 CATEGORIE: Session

### 🔹 Session.TimeoutMinutes
- **Tip Date:** `int`
- **Valoare Curentă:** `30`
- **Valoare Implicită:** `30`
- **Interval Valid:** 5 - 480 minute (8 ore)
- **Descriere:** Durata de inactivitate după care sesiunea utilizatorului expiră
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = UX mai bun, risc securitate crescut
  - ⬇️ Valoare mai mică = securitate crescută, UX mai dificil
- **Folosit În:**
  - `SessionService` - gestionare sesiuni
  - `SessionCleanupService` - curățare sesiuni expirate
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **🔒 Securitate:** Valori peste 120 minute (2 ore) nu sunt recomandate pentru aplicații sensibile

---

### 🔹 Session.CleanupIntervalMinutes
- **Tip Date:** `int`
- **Valoare Curentă:** `5`
- **Valoare Implicită:** `5`
- **Interval Valid:** 1 - 60 minute
- **Descriere:** Intervalul la care background job-ul curăță sesiuni expirate din DB
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = mai puține query-uri cleanup, date vechi mai persistente
  - ⬇️ Valoare mai mică = DB mai curat, mai multe query-uri
- **Folosit În:**
  - `SessionCleanupService.ExecuteAsync()` (NOTĂ: citit dinamic la fiecare iterație)
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)

---

## 📖 CATEGORIE: Performance

### 🔹 Performance.QueryTimeout.Seconds
- **Tip Date:** `int`
- **Valoare Curentă:** `30`
- **Valoare Implicită:** `30`
- **Interval Valid:** 10 - 300 secunde
- **Descriere:** Timpul maxim de așteptare pentru un query SQL înainte de timeout
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = query-uri lente mai tolerante, risc blocare resurse
  - ⬇️ Valoare mai mică = fail-fast, risc timeout pentru query-uri complexe
- **Folosit În:**
  - Connection string (NOTĂ: citit la startup)
  - Poate fi folosit în Dapper CommandTimeout
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)

---

### 🔹 Performance.SlowQueryThreshold.Milliseconds
- **Tip Date:** `int`
- **Valoare Curentă:** `500`
- **Valoare Implicită:** `500`
- **Interval Valid:** 100 - 5000 ms
- **Descriere:** Pragul peste care un query este considerat "lent" și logat pentru monitoring
- **Impact Modificare:**
  - ⬆️ Valoare mai mare = mai puține warning-uri, detectare tardivă probleme
  - ⬇️ Valoare mai mică = detectare agresivă, mai multe warning-uri
- **Folosit În:**
  - Repositories cu Stopwatch timing
  - Logging și monitoring
- **Read-Only:** ❌ Nu
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **📊 Best Practice:** Query-uri peste 500ms trebuie optimizate (indexuri, refactoring)

---

## 📖 CATEGORIE: Enum

### 🔹 Enum.DataType.Values
- **Tip Date:** `json`
- **Valoare Curentă:** `["int","string","bool","decimal","json","enum"]`
- **Valoare Implicită:** (aceeași)
- **Descriere:** Lista validă de tipuri de date pentru parametri sistem
- **Impact Modificare:**
  - Adăugare tip nou = trebuie implementat în `SystemParametersService`
  - Ștergere tip = risc invalidare parametri existenți
- **Folosit În:**
  - Validare `SystemParameter.DataType`
  - UI dropdown pentru selectare tip
- **Read-Only:** ⚠️ **DA** (CRITIC SISTEM)
- **Ultima Modificare:** 2026-01-09 (creare inițială)
- **Modificat De:** System (seed data)
- **❌ ATENȚIE:** NU modifica fără consultare echipă dev! Risc crash aplicație!

---

## 🔧 Ghid Adăugare Parametru Nou

### Checklist Obligatoriu

1. **Definire Parametru**
   ```sql
   -- 1. Adaugă în 011_SystemParameters.sql
   INSERT INTO [dbo].[SystemParameters] (
       [ParameterKey], [Category], [SubCategory], 
       [ParameterValue], [DataType], [DefaultValue],
       [Description], [DisplayName], [IsReadOnly]
   ) VALUES (
       'Category.SubCategory.ParameterName',
       'Category',
       'SubCategory',
       'DefaultValue',
       'DataType',
       'DefaultValue',
       'Descriere detaliată cu impact modificare',
       'Nume Afișat',
       0 -- sau 1 pentru read-only
   );
   ```

2. **Documentare Aici**
   - Adaugă secțiune în categoria corespunzătoare
   - Include: Tip, Valoare, Interval, Descriere, Impact, Folosit În
   - Marchează Read-Only și warnings dacă e cazul

3. **Cod C#**
   ```csharp
   // Folosire în cod:
   var value = await _parametersService.GetIntAsync("Category.SubCategory.ParameterName", defaultValue);
   ```

4. **Testing**
   - Testează valoare implicită
   - Testează limite (min/max)
   - Testează invalidare cache după update

5. **Comunicare Echipă**
   - Anunță în stand-up/slack
   - Actualizează acest document
   - Fă commit cu mesaj descriptiv

---

## 🚨 Parametri Read-Only (CRITICI SISTEM)

| Parametru | Motiv Read-Only | Impact Modificare |
|-----------|-----------------|-------------------|
| `Enum.DataType.Values` | Validare tip date core | 🔴 CRASH aplicație |

**⚠️ Regula de Aur:** Dacă un parametru este marcat Read-Only, NU poate fi modificat sau șters din UI! Necesită modificare cod + migration DB.

---

## 📈 Best Practices

### ✅ DO (Recomandări)

1. **Naming Convention:**
   - Format: `Category.SubCategory.ParameterName`
   - Evită abrevieri: `DurationMinutes` ✅ nu `DurMin` ❌

2. **Validare:**
   - Folosește `MinValue`/`MaxValue` pentru numeric
   - Folosește `ValidationRegex` pentru string-uri cu format specific
   - Setează `DefaultValue` întotdeauna

3. **Descriere:**
   - Explică IMPACT modificării, nu doar ce face
   - Include range-uri valide
   - Menționează unde este folosit în cod

4. **Testing:**
   - Testează edge cases (min, max, invalid)
   - Verifică invalidare cache
   - Testează cu valoare implicită

### ❌ DON'T (Evită)

1. **NU** hardcoda valori în cod când există parametru
2. **NU** șterge parametri fără verificare dependențe
3. **NU** modifica `ParameterKey` (e immutable identifier)
4. **NU** seta `IsReadOnly=true` fără justificare solidă
5. **NU** lăsa `Description` gol sau generic

---

## 🔄 Procedură Modificare Parametru

### În Producție

1. **Backup Valoare Veche**
   ```sql
   SELECT * FROM [dbo].[SystemParameters] 
   WHERE [ParameterKey] = 'Key.To.Change';
   ```

2. **Modificare prin UI Admin**
   - Accesează `/administrare/parametri-sistem`
   - Editează parametrul dorit
   - Verifică validare (min/max, regex)
   - Salvează

3. **Verificare Efect**
   - Monitorizează log-uri pentru erori
   - Verifică comportament aplicație
   - Rollback dacă e necesar

4. **Documentare**
   - Actualizează acest document cu noua valoare
   - Notează motiv modificare și data
   - Commit changes

### În Development

- Modifică direct în seed data (011_SystemParameters.sql)
- Rulează migration pentru actualizare DB
- Testează comportament nou

---

## 📊 Istoric Modificări Parametri

| Data | Parametru | Valoare Veche | Valoare Nouă | Modificat De | Motiv |
|------|-----------|---------------|--------------|--------------|-------|
| 2026-01-09 | (seed initial) | - | - | System | Creare inițială 14 parametri |

**NOTĂ:** Această secțiune trebuie actualizată MANUAL la fiecare modificare în producție!

---

## 🔍 Audit & Monitoring

### Query-uri Utile

```sql
-- Parametri modificați recent (7 zile)
SELECT * FROM [dbo].[SystemParameters]
WHERE [UpdatedAt] >= DATEADD(DAY, -7, GETDATE())
ORDER BY [UpdatedAt] DESC;

-- Parametri read-only
SELECT [ParameterKey], [DisplayName], [Category]
FROM [dbo].[SystemParameters]
WHERE [IsReadOnly] = 1 AND [IsActive] = 1;

-- Parametri cu validare regex
SELECT [ParameterKey], [ValidationRegex], [ParameterValue]
FROM [dbo].[SystemParameters]
WHERE [ValidationRegex] IS NOT NULL AND [IsActive] = 1;

-- Statistici per categorie
SELECT [Category], COUNT(*) as Total, 
       SUM(CASE WHEN [IsReadOnly] = 1 THEN 1 ELSE 0 END) as ReadOnly
FROM [dbo].[SystemParameters]
WHERE [IsActive] = 1
GROUP BY [Category]
ORDER BY [Category];
```

---

## 🆘 Troubleshooting

### Problema: Modificarea parametrului nu are efect

**Cauze Posibile:**
1. **Cache nu invalidat:** Unii parametri sunt cached indefinit
   - **Soluție:** Restart aplicație sau clear cache manual

2. **Parametru citit la startup:** Unii parametri (ex: Identity options) se citesc o singură dată
   - **Soluție:** Restart aplicație necesară

3. **Parametru read-only:** Nu poate fi modificat din UI
   - **Soluție:** Modifică în cod + migration DB

### Problema: Eroare validare la salvare

**Cauze Posibile:**
1. **Valoare în afara intervalului Min/Max**
   - **Soluție:** Verifică constraints și ajustează valoarea

2. **Regex validation failed**
   - **Soluție:** Verifică `ValidationRegex` și formatul valorii

3. **Tip date incompatibil**
   - **Soluție:** Verifică `DataType` și formatul valorii (ex: "true" pentru bool)

---

## 📞 Contact & Suport

**Pentru modificări critice sau adăugare parametri noi:**
- Consultă arhitectul soluției
- Review obligatoriu în PR
- Testing extensiv înainte de producție

**În caz de incident:**
1. Rollback imediat la valoare anterioară
2. Verifică log-uri pentru root cause
3. Documentează incident și soluție

---

**Ultima Actualizare:** 2026-01-09  
**Versiune Document:** 1.0  
**Responsabil:** Development Team  
**Review Necesar:** La fiecare adăugare/modificare parametru

# ERP · Bune practici (SQL și cod)

14 noiembrie 2026 · Aurelian · **Validat (14.11.2026); completat pe 14.11.2026 cu regulile pentru baza case sensitive (`SQL-21`–`SQL-25`, `NET-21`, `TST-09`), validat 14.11.2026; completat pe 20.11.2026 cu regulile temei vizuale (`UI-13`–`UI-17`), validat 20.11.2026, și cu garda modificărilor nesalvate (`UI-18`), validată 20.11.2026; precizări la `MIG-02` și `MIG-04` despre versionarea automată a scripturilor, 20.11.2026** · Versiune editabilă: https://claude.ai/artifact/JP7yeX8cA7AP8PRRMJGiTA

Acest document strânge regulile de scriere pentru tot codul ERP-ului: baza de date SQL Server, backend-ul .NET, frontend-ul React, testele și fluxul de lucru Git. E un document transversal: nu aparține unei secțiuni, ci se aplică tuturor. Pornește de la principiile P1–P10 din [01 Scop și principii](../01-scop-si-principii/SCOP_SI_PRINCIPII.md) și de la deciziile deja luate: Dapper și proceduri stocate fără ORM, SQL Server 2022, .NET 10, modular monolith, o bază de date per grup de client.

## 1. Cum se aplică

**Fiecare regulă are un identificator** (de exemplu `SQL-07`, `NET-12`, `UI-03`, `TST-04`), ca documentele de arhitectură, revizuirile de cod și mesajele de commit să o poată referi scurt.

| Prefix | Domeniu |
| --- | --- |
| `SQL-` | Schemă, tipuri de date, chei, indecși, constrângeri |
| `SP-` | Proceduri stocate, tranzacții, concurență, performanță |
| `MIG-` | Migrații DbUp și versionarea bazei |
| `NET-` | Backend .NET: structură, MediatR, Dapper, erori, jurnale |
| `UI-` | Frontend React și TypeScript |
| `TST-` | Teste |
| `GIT-` | Git, revizuire, integrare continuă |
| `SEC-` | Securitate și GDPR în cod |

**Două niveluri:**

- **Obligatoriu**: se respectă întotdeauna. O abatere se acceptă doar scrisă, cu motiv, în documentul modulului (vezi mai jos).
- **Recomandat**: implicit se respectă; se poate ignora când există un motiv bun, fără să fie nevoie de justificare scrisă.

În tabelele de mai jos nivelul e marcat cu **O** (obligatoriu) sau **R** (recomandat).

**Cum ține cont de el fiecare document de arhitectură:**

1. Documentele detaliate (02.01–02.14 și secțiunile 03–09) nu repetă aceste reguli; le referă prin identificator acolo unde contează (de exemplu: „schema respectă `SQL-01`–`SQL-20`”).
2. Exemplele de cod și SQL din documente respectă regulile; un exemplu care nu le respectă e o greșeală de corectat.
3. Orice abatere apare în documentul modulului, în secțiunea 7 (Schema SQL) sau 6 (Contracte), într-un rând „Abatere de la `SQL-xx`: ce, de ce, până când”.
4. La validarea fiecărui document verific și conformitatea cu acest document (checklist-ul din secțiunea 8).
5. Acest document se modifică doar prin revizuire; o regulă nouă primește următorul număr liber, iar o regulă retrasă își păstrează numărul, marcat „retrasă”, ca referințele vechi să rămână corecte.

## 2. SQL Server: schemă și date

### 2.1 Denumiri

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SQL-01` | O | Fiecare modul are schema lui (`core`, `fin`, `sales`, `purch`, `inv`, `wms`, `tms`, `mfg`, `hr`, `pos`, `cons`, `crm`, `rpt`, `intg`), plus `ids` (autentificarea: ASP.NET Core Identity, sesiuni, chei API) și schema tehnică `hangfire`; nimic în `dbo`. Toate referințele sunt calificate cu schema: `core.Partner`, nu `Partner`. |
| `SQL-02` | O | Tabele în PascalCase, la singular, în engleză: `SalesInvoice`, `SalesInvoiceLine`. Coloane în PascalCase: `DocumentDate`, `PartnerId`. Fără prefixe de tip (`tblX`, `strName`). |
| `SQL-03` | O | Cheia primară se numește `Id`; cheia străină e `<Entitate>Id` (`PartnerId`, `CompanyId`). Două chei străine spre aceeași entitate primesc rolul în față: `DeliveryPartnerId`, `InvoicePartnerId`. |
| `SQL-04` | O | Constrângeri și indecși cu nume explicite: `PK_<Tabel>`, `FK_<Tabel>_<TabelReferit>[_<Rol>]`, `UQ_<Tabel>_<Coloane>`, `CK_<Tabel>_<Regulă>`, `DF_<Tabel>_<Coloană>`, `IX_<Tabel>_<Coloane>`, `UX_` pentru index unic filtrat. Niciun nume generat automat de SQL Server. |
| `SQL-05` | O | Proceduri: `<schemă>.usp_<Entitate>_<Acțiune>` (`sales.usp_Invoice_Validate`). View-uri: `<schemă>.v_<Nume>`. Funcții: `<schemă>.fn_<Nume>`. Tipuri tabelă (TVP): `<schemă>.tt_<Nume>`. |
| `SQL-06` | R | Coloanele booleene încep cu `Is`, `Has`, `Can` (`IsActive`, `HasAttachments`). Datele se termină în `Date` (zi) sau `At` (moment): `DocumentDate`, `ValidatedAt`. |

### 2.2 Tipuri de date

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SQL-07` | O | **Prețuri și cantități:** `decimal(19,6)`; afișarea și rotunjirea se fac după politica de precizie (P7, `IPrecisionService`). **Valori și TVA:** `decimal(19,2)` în moneda documentului și în RON. **Cursuri:** `decimal(19,6)`. Niciodată `float`, `real` sau `money`. |
| `SQL-08` | O | **Zi fără oră** (dată document, dată contabilă, scadență): `date`. **Moment** (creare, validare, sincronizare): `datetime2(3)` în UTC, cu sufixul `At`. Niciodată `datetime` sau `smalldatetime`. Ora locală se calculează doar la afișare (Europe/Bucharest). |
| `SQL-09` | O | Text: `nvarchar(n)` cu lungime realistă (cod 20–50, denumire 200–250, adresă 500). `nvarchar(max)` doar pentru conținut lung (note, XML, JSON). `varchar` doar pentru coduri ASCII fixe (`char(3)` monedă, `char(2)` țară). |
| `SQL-10` | O | Enumerări: `tinyint` sau `smallint` cu `CHECK` pe valorile permise și enum C# cu aceleași valori explicite; nu se renumerotează niciodată. Pentru liste pe care clientul le extinde: tabelă nomenclator, nu enum. |
| `SQL-11` | O | Boolean: `bit NOT NULL` cu valoare implicită. Concurență optimistă: coloană `RowVersion rowversion` pe orice tabelă editabilă din interfață. |
| `SQL-12` | R | JSON în `nvarchar(max)` cu `CHECK (ISJSON(...) = 1)`, doar pentru date fără căutări frecvente (scope de drepturi, setări, atribute libere). Ce se filtrează sau se agregă stă în coloane. |

### 2.3 Chei, constrângeri și indecși

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SQL-13` | O | **Chei primare:** `int IDENTITY` pentru nomenclatoare și antete de documente, `bigint IDENTITY` pentru tabelele care cresc mult (linii, mișcări de stoc, note contabile, audit, outbox). Cheia primară e și indexul clustered. |
| `SQL-14` | O | **`uniqueidentifier`** doar pentru date născute în afara bazei (bonuri POS offline, `EventId`, importări și sincronizări), ca o coloană `UNIQUE` separată (`ExternalId`); niciodată cheie clustered. Opțional `PublicId` pe documentele expuse în URL sau API. |
| `SQL-15` | O | Orice relație are `FOREIGN KEY` declarat; fără `ON DELETE CASCADE` pe date de business (ștergerea e o decizie explicită în procedură). Excepție: liniile unei ciorne pot avea cascadă spre antetul ciornei. |
| `SQL-16` | O | Fiecare cheie străină are index (sau e prima coloană a unui index). Regulile de business simple stau în bază: `NOT NULL`, `CHECK` (cantitate > 0, stare permisă), `UNIQUE`. |
| `SQL-17` | O | Unicitatea condiționată se face cu index unic filtrat (`WHERE FiscalCode IS NOT NULL`, `WHERE IsActive = 1`), nu în cod. |
| `SQL-18` | R | Indecșii acoperă căutările din ecrane (`INCLUDE` pentru coloanele afișate în listă). Un index nou se justifică printr-o interogare concretă; fără indecși „de rezervă”. Tabelele de documente au index pe `(CompanyId, DocumentDate)`. |
| `SQL-19` | O | Coloanele de audit pe orice tabelă de business: `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` (și `RowVersion`, `SQL-11`). Documentele validate au și `ValidatedAt`, `ValidatedBy`. |
| `SQL-20` | O | Datele de business nu se șterg fizic după ce au fost folosite: nomenclatoarele se dezactivează, documentele validate se stornează (P3). Ștergerea fizică e permisă doar pentru ciorne și date tehnice (outbox procesat după retenție). |

### 2.4 Collation și majuscule (baza este case sensitive)

Decizia lui Aurelian (14.11.2026): baza de date a clientului este **case sensitive**. Installer-ul o creează cu collation-ul de mai jos ([08 Instalare și actualizare](../08-instalare-actualizare/INSTALARE_ACTUALIZARE.md), secțiunea 9).

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SQL-21` | O | Baza are collation **`Latin1_General_100_CS_AS_SC_UTF8`** (case și accent sensitive, UTF-8 pentru `varchar`; ales de Aurelian 14.11.2026), setat la `CREATE DATABASE` de installer și verificat la fiecare actualizare. Nu se schimbă niciodată după creare. Coloanele nu primesc alt `COLLATE`, cu excepția coloanelor calculate de căutare (`SQL-25`). |
| `SQL-22` | O | Orice identificator se scrie exact ca în declarație: scheme cu litere mici (`core`), tabele și coloane în PascalCase (`core.Partner.FiscalCode`), parametri și variabile cu aceleași majuscule ca la declarare (`@InvoiceId`), vederile de sistem cu litere mici (`sys.objects`). `core.[User]` se scrie între paranteze drepte (cuvânt rezervat). Valorile codurilor tehnice (`N'ADMIN'`, `N'Sales.Invoice.Validate'`) se scriu exact. |
| `SQL-23` | O | În tabelele temporare (`#t`) coloanele text se declară `COLLATE DATABASE_DEFAULT`: `tempdb` are collation-ul serverului, de obicei case insensitive, iar fără clauză join-urile cu tabelele ERP dau „Cannot resolve the collation conflict”. Se preferă parametrii tabelă (`tt_*`, `SP-07`) și variabilele tabelă. |
| `SQL-24` | O | Codurile introduse de utilizatori (partener, articol, gestiune, dimensiune, serie) sunt unice **fără diferență de majuscule**: coloană calculată persistată `CodeKey AS UPPER(Code) PERSISTED` cu index unic `UX_<Tabel>_CodeKey` (în locul UQ pe `Code`); valoarea afișată rămâne cum a scris-o utilizatorul. Numele de utilizator se salvează doar cu litere mici (`CHECK (UserName = LOWER(UserName))`); autentificarea caută după forma normalizată. CUI, IBAN, coduri ISO se normalizează la salvare (majuscule, fără spații). Codurile tehnice livrate (permisiuni, roluri, chei de setări) se compară exact. |
| `SQL-25` | O | Căutarea după denumire din ecrane nu ține cont de majuscule și diacritice și rămâne SARGable: coloană calculată persistată `NameSearch AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED`, cu index (`INCLUDE` coloanele listei), folosită ca `WHERE NameSearch LIKE @Term + N'%'`. Fără `UPPER()`/`LOWER()` sau `COLLATE` pe coloană în `WHERE` (`SP-09`). |

## 3. SQL Server: proceduri stocate și migrații

### 3.1 Proceduri stocate

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SP-01` | O | Orice scriere de date de business trece printr-o procedură stocată a modulului care deține tabelul (P1). Citirile simple pot fi SQL parametrizat în clasa de interogări a modulului; citirile complexe (rapoarte, căutări cu multe filtre) sunt tot proceduri. |
| `SP-02` | O | Fiecare procedură începe cu `SET NOCOUNT ON; SET XACT_ABORT ON;`. |
| `SP-03` | O | Procedurile nu deschid tranzacții proprii când sunt apelate din aplicație: tranzacția o deține `TransactionBehavior` din pipeline (02.01, flux 4.1). Procedurile apelate și din joburi SQL folosesc tiparul `@@TRANCOUNT` (tranzacție proprie doar dacă nu există una). |
| `SP-04` | O | Erorile de business se semnalează cu `THROW 50xxx, N'mesaj', 1;` folosind un cod din plaja modulului (`core` 50000–50999, `fin` 51000–51999, `sales` 52000–52999 ș.a.m.d.). Codul e mapat în C# la o eroare cu cod stabil (`NET-09`). Fără `RAISERROR`, fără coduri de retur pentru erori. |
| `SP-05` | O | Fără `SELECT *`; coloanele se listează explicit, în ordinea așteptată de DTO. |
| `SP-06` | O | SQL dinamic doar cu `sp_executesql` și parametri; niciodată valori concatenate în text. Numele de obiecte dinamice (rar) trec prin `QUOTENAME` și o listă permisă. |
| `SP-07` | O | Listele (linii de document, ID-uri) se trimit ca parametru tabelă (`tt_*`, TVP), nu ca text separat prin virgulă și nu prin apeluri repetate. |
| `SP-08` | O | Fără cursoare și bucle `WHILE` pe rânduri pentru prelucrări de date; se lucrează pe seturi. Excepție scrisă: algoritmi care chiar sunt secvențiali (de exemplu descărcarea FIFO, dacă nu se poate pe seturi). |
| `SP-09` | O | Condiții SARGable: fără funcții pe coloana filtrată (`WHERE DocumentDate >= @From AND DocumentDate < @To`, nu `YEAR(DocumentDate) = 2026`); tipul parametrului egal cu tipul coloanei (fără conversii implicite `nvarchar`/`varchar`). |
| `SP-10` | O | Paginare: keyset (`WHERE Id > @AfterId ORDER BY Id`) pentru liste mari și exporturi; `OFFSET/FETCH` doar pentru ecrane cu număr de pagină, cu limită maximă de 500 de rânduri pe pagină. |
| `SP-11` | R | Procedurile de căutare cu multe filtre opționale folosesc `OPTION (RECOMPILE)` sau SQL dinamic parametrizat (`SP-06`), ca să evite planuri proaste din parameter sniffing. |
| `SP-12` | R | O procedură face un singur lucru. Logica reutilizată din mai multe proceduri stă într-o procedură internă sau o funcție inline (`RETURNS TABLE`); fără funcții scalare în interogări pe multe rânduri. |
| `SP-13` | O | Calculele fiscale și contabile (TVA, rotunjiri, totaluri) se fac într-un singur loc: în C#, în handler, cu `IPrecisionService`. Procedura stochează și verifică (de exemplu `CHECK` că totalul = suma liniilor), nu recalculează cu alte reguli. |

### 3.2 Tranzacții și concurență

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SP-14` | O | Baza de date rulează cu `READ_COMMITTED_SNAPSHOT ON`: citirile nu blochează scrierile și nu sunt blocate de ele. Se setează la instalare. |
| `SP-15` | O | Tranzacțiile sunt scurte: fără apeluri HTTP, fișiere sau așteptări de utilizator în interiorul lor. Ce iese în afară (e-Factura, email) pleacă prin outbox după commit (P8). |
| `SP-16` | O | Citirea care precede o actualizare a aceluiași rând (sold, stoc, serie) folosește `WITH (UPDLOCK, ROWLOCK)` sau se face direct prin `UPDATE ... OUTPUT`. Ordinea de blocare e mereu aceeași (de exemplu articole sortate după `ItemId`), ca să evităm deadlock-urile. |
| `SP-17` | O | Deadlock-ul (eroarea 1205) și expirarea de blocare se reîncearcă automat de 3 ori în pipeline, cu așteptare scurtă; după aceea eroarea ajunge la utilizator ca „resursă ocupată, reîncercați”. |
| `SP-18` | O | Concurența la editare se verifică prin `RowVersion`: procedura de salvare primește versiunea citită și refuză (cod 409) dacă rândul s-a schimbat între timp. |

### 3.3 Securitate în baza de date

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SP-19` | O | Aplicația se conectează cu un login dedicat, fără `sysadmin` și fără `db_owner`; drepturi prin roluri de bază de date per modul (02.01, secțiunea 7). Migrațiile rulează cu o identitate separată, folosită doar la instalare și actualizare: contul Windows al operatorului installer-ului (08, secțiunea 5.8). |
| `SP-20` | O | Datele personale sensibile (CNP, salarii unde e cazul) se criptează la nivel de coloană; cheile nu stau în baza de date în clar. Detaliile vin în secțiunea 06 Securitate. |
| `SP-21` | O | Tabelele de audit și jurnalele fiscale au trigger care refuză `UPDATE` și `DELETE`. |

### 3.4 Migrații DbUp

| ID | Nivel | Regulă |
| --- | --- | --- |
| `MIG-01` | O | Toate modificările de schemă sunt scripturi DbUp în proiectul modulului: `Migrations/<modul>/<AAAA.MM.NNN>_<Descriere>.sql` (de exemplu `2026.11.001_Create_Partner.sql`). Ordinea e dată de nume. |
| `MIG-02` | O | Un script deja livrat la un client nu se modifică niciodată. O corectură e un script nou. Până la primul tag de versiune, o migrare încă nelivrată se poate corecta: baza de dezvoltare se reconstruiește singură ([07, „Mediul de dezvoltare”](../07-stack-tehnologic/STACK_TEHNOLOGIC.md)), iar verificarea din build (08, 5.9) o blochează după tag. |
| `MIG-03` | O | Migrațiile merg doar înainte. Pentru fiecare livrare există un backup automat înainte de actualizare (secțiunea 08), nu scripturi de întoarcere. |
| `MIG-04` | O | Obiectele programabile (proceduri, view-uri, funcții) stau în fișiere separate, rulate la fiecare actualizare cu `CREATE OR ALTER`, în folderul `Programmability/`; tabelele și datele, în scripturi de migrare rulate o singură dată. Un singur fișier per obiect, fără numere de versiune în nume: versiunea e automată (numărul de commituri care au schimbat fișierul) și se vede în `core.v_ScriptVersionCurrent` (08, 5.9). |
| `MIG-05` | O | Modificările pe tabele mari sunt compatibile în urmă: coloană nouă nullable sau cu valoare implicită, apoi completare în loturi, apoi `NOT NULL` într-o versiune ulterioară. Fără redenumiri de coloane folosite; se adaugă coloana nouă și se retrage cea veche. |
| `MIG-06` | O | Datele livrate cu produsul (cote TVA, UM, țări, județe, permisiuni) se încarcă idempotent cu `MERGE` pe cheia naturală (`Code`), ca rularea repetată să nu dubleze nimic. |
| `MIG-07` | O | Fiecare migrare rulează în CI pe o bază goală și pe o copie cu date din versiunea anterioară (test de actualizare). |

### 3.5 Șablon de procedură

```sql
CREATE OR ALTER PROCEDURE sales.usp_Invoice_Validate
    @InvoiceId      int,
    @RowVersion     binary(8),
    @UserId         int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Tranzacția e deschisă de TransactionBehavior (SP-03)
    DECLARE @Status tinyint, @CompanyId int, @CurrentVersion binary(8);

    SELECT @Status = Status, @CompanyId = CompanyId, @CurrentVersion = RowVersion
    FROM sales.Invoice WITH (UPDLOCK, ROWLOCK)               -- SP-16
    WHERE Id = @InvoiceId;

    IF @Status IS NULL
        THROW 52001, N'Factura nu există.', 1;              -- SP-04
    IF @CurrentVersion <> @RowVersion
        THROW 52002, N'Factura a fost modificată de alt utilizator.', 1;  -- SP-18
    IF @Status <> 1 /* Draft */
        THROW 52003, N'Doar o ciornă poate fi validată.', 1;

    UPDATE sales.Invoice
    SET Status      = 2 /* Posted */,
        ValidatedAt = SYSUTCDATETIME(),
        ValidatedBy = @UserId,
        ModifiedAt  = SYSUTCDATETIME(),
        ModifiedBy  = @UserId
    WHERE Id = @InvoiceId;

    SELECT Id, Number, Status, RowVersion                     -- SP-05
    FROM sales.Invoice
    WHERE Id = @InvoiceId;
END;
```

Numărul se alocă înainte, în aceeași tranzacție, prin `core.usp_NumberSeries_Next` (02.01, flux 4.2); evenimentul se scrie în outbox de handler, tot în aceeași tranzacție.

## 4. Backend .NET

### 4.1 Structură și dependențe

| ID | Nivel | Regulă |
| --- | --- | --- |
| `NET-01` | O | Fiecare modul are proiectele `Erp.<Modul>.Contracts` (interfețe publice, DTO-uri, evenimente), `Erp.<Modul>.Domain` (entități, reguli de business și stări, fără acces la date), `Erp.<Modul>.Application` (comenzi, interogări, handler-e, validatori), `Erp.<Modul>.Infrastructure` (Dapper, joburi, integrări) și `Erp.<Modul>.Api` (endpoint-uri). Un modul vede din altul doar `.Contracts` (P1); regula e verificată de teste NetArchTest. |
| `NET-02` | O | Organizare pe funcționalități (vertical slice): un folder per caz de utilizare cu comanda, validatorul, handler-ul și endpoint-ul (`Invoices/Validate/ValidateInvoice.cs`), nu foldere tehnice globale (`Commands/`, `Handlers/`). |
| `NET-03` | O | Endpoint-urile sunt subțiri: primesc cererea, trimit comanda prin MediatR, întorc rezultatul. Nicio regulă de business și niciun acces la date în endpoint. |
| `NET-04` | O | Fiecare comandă declară modulul și permisiunea (`[Module("Sales")]`, `[RequiresPermission("Sales.Invoice.Validate")]`) și, dacă schimbă date de business, `[Audited]`. O comandă fără permisiune declarată e refuzată la pornire (test automat). |
| `NET-05` | O | Comenzile și interogările sunt `record`-uri imutabile; numele spune intenția la imperativ: `ValidateInvoice`, `GetInvoiceById`, `SearchPartners`. |

### 4.2 Acces la date cu Dapper

| ID | Nivel | Regulă |
| --- | --- | --- |
| `NET-06` | O | Accesul la date stă în clase de acces ale modulului (`InvoiceStore`, `InvoiceQueries`) din `Infrastructure`; handler-ul folosește interfața lor, nu Dapper direct. Textul SQL nu apare în `Application`. |
| `NET-07` | O | Toate apelurile folosesc conexiunea și tranzacția curentă din `IDbSession` (furnizat de `TransactionBehavior`); nimeni nu deschide `SqlConnection` pe cont propriu în handler. Mereu `CommandType.StoredProcedure` pentru proceduri și parametri Dapper pentru valori (niciodată concatenare). |
| `NET-08` | O | Parametrii au tipuri explicite când contează: `DbType.String` cu lungime pentru `nvarchar`, `DbType.AnsiString` pentru `varchar`, `decimal` cu precizie și scală (`SP-09`). TVP-urile se construiesc cu un helper comun, nu manual în fiecare modul. |

### 4.3 Erori, validare și răspunsuri

| ID | Nivel | Regulă |
| --- | --- | --- |
| `NET-09` | O | Erorile de business au un cod stabil, în format `<MODUL>_<EROARE>` (`SALES_INVOICE_NOT_DRAFT`), și un mesaj în română. Codurile `THROW 5xxxx` din SQL se mapează la aceste coduri într-un singur tabel per modul. |
| `NET-10` | O | API-ul întoarce erorile ca ProblemDetails (RFC 9457): 400 validare (cu erorile pe câmp), 403 drepturi sau licență, 404 inexistent, 409 conflict (perioadă închisă, `RowVersion`, stare greșită), 500 doar pentru erori neprevăzute, fără detalii tehnice către client. |
| `NET-11` | O | Validarea de formă (obligatoriu, lungime, format CUI, IBAN) e în FluentValidation; regulile care cer date (stoc disponibil, limită de credit) sunt în handler; constrângerile de integritate sunt și în bază (`SQL-16`). |
| `NET-12` | O | Excepțiile nu se folosesc pentru fluxul normal și nu se înghit: fără `catch {}` gol, fără `catch (Exception)` care continuă ca și cum nimic nu s-ar fi întâmplat. |

### 4.4 Cod C#

| ID | Nivel | Regulă |
| --- | --- | --- |
| `NET-13` | O | `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, analizoare .NET la nivel recomandat și un `.editorconfig` comun pentru toată soluția. |
| `NET-14` | O | Bani, prețuri, cantități, cursuri: doar `decimal`. Rotunjirea doar prin `IPrecisionService` (P7); niciun `Math.Round` împrăștiat prin cod. |
| `NET-15` | O | Timpul vine din `TimeProvider` injectat, niciodată din `DateTime.Now`. Momentele în UTC (`DateTime` cu `Kind.Utc` sau `DateTimeOffset`), zilele ca `DateOnly`. |
| `NET-16` | O | Tot I/O-ul e asincron, cu `CancellationToken` transmis până la Dapper; fără `.Result`, `.Wait()` sau `async void`. |
| `NET-17` | O | Configurarea prin Options pattern, validată la pornire (`ValidateOnStart`). Fără secrete în `appsettings.json`: parolele și cheile stau criptate cu ASP.NET Core Data Protection, cu inelul de chei în bază, protejat cu certificatul instalării și cu cheie de recuperare (08, secțiunea 5.12). |
| `NET-18` | O | Jurnale structurate (Serilog) cu `CorrelationId`, utilizator, firmă și modul. Fără date personale (CNP, IBAN complet, salarii) și fără tokenuri în jurnale. |
| `NET-19` | R | Cod și identificatori în engleză; termenii fiscali românești fără echivalent păstrează numele românesc din glosarul din 01 (`Saft`, `Revisal`, `D394`). Mesajele pentru utilizator în română, în fișiere de resurse. |
| `NET-20` | R | Mapster pentru mapare DTO ↔ obiecte doar unde maparea e mecanică; logica de calcul nu se ascunde în configurări de mapare. |
| `NET-21` | O | Comparațiile de text în C# urmează regulile bazei (`SQL-24`): codurile tehnice (permisiuni, roluri, chei de setări) cu `StringComparer.Ordinal`; codurile introduse de utilizatori după normalizare, cu `StringComparer.OrdinalIgnoreCase`. Niciodată comparații dependente de cultura curentă pentru coduri. |

### 4.5 Exemplu de caz de utilizare complet

```csharp
// Sales/Invoices/Validate/ValidateInvoice.cs
[Module("Sales")]
[RequiresPermission("Sales.Invoice.Validate")]
[Audited("Sales.Invoice.Validate")]
public sealed record ValidateInvoice(int InvoiceId, byte[] RowVersion) : ICommand<ValidateInvoiceResult>;

public sealed record ValidateInvoiceResult(int InvoiceId, string Number, byte[] RowVersion);

public sealed class ValidateInvoiceValidator : AbstractValidator<ValidateInvoice>
{
    public ValidateInvoiceValidator()
    {
        RuleFor(x => x.InvoiceId).GreaterThan(0);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class ValidateInvoiceHandler(
    IInvoiceStore invoices,
    INumberSeriesService numbers,
    IOutbox outbox,
    ICurrentContext context) : ICommandHandler<ValidateInvoice, ValidateInvoiceResult>
{
    public async Task<ValidateInvoiceResult> Handle(ValidateInvoice cmd, CancellationToken ct)
    {
        var invoice = await invoices.GetForValidationAsync(cmd.InvoiceId, ct)
            ?? throw new NotFoundError("SALES_INVOICE_NOT_FOUND");

        invoice.EnsureCanValidate();                       // reguli de business, în domeniu

        var number = await numbers.NextAsync(
            context.CompanyId, DocumentTypes.SalesInvoice, invoice.DocumentDate, ct);

        var result = await invoices.ValidateAsync(cmd.InvoiceId, number, cmd.RowVersion, ct);

        await outbox.EnqueueAsync(new FacturaValidata
        {
            CompanyId = context.CompanyId,
            CorrelationId = context.CorrelationId,
            InvoiceId = result.InvoiceId,
            Number = result.Number
        }, ct);                                            // aceeași tranzacție (P8)

        return result;
    }
}

// Endpoint subțire (NET-03)
app.MapPost("/api/sales/invoices/{id:int}/validate",
    (int id, ValidateRequest body, ISender sender, CancellationToken ct) =>
        sender.Send(new ValidateInvoice(id, body.RowVersion), ct));
```

## 5. Frontend React și TypeScript

| ID | Nivel | Regulă |
| --- | --- | --- |
| `UI-01` | O | TypeScript cu `strict: true`; fără `any` (în loc: `unknown` și verificare). ESLint și Prettier cu configurare comună, rulate în CI. |
| `UI-02` | O | Organizare pe module și funcționalități, oglindă a backend-ului (`src/modules/sales/invoices/...`); componentele comune (grilă, câmp numeric, selector partener) în `src/shared`. Un modul nelicențiat nu apare în meniu și nu se încarcă (lazy loading pe modul). |
| `UI-03` | O | Datele de pe server prin TanStack Query (cache, reîncărcare, invalidare după comenzi); fără copii ale datelor de server în stare globală. Clientul API și tipurile se generează din OpenAPI, nu se scriu de mână. |
| `UI-04` | O | Formulare cu React Hook Form și Zod. Validarea din interfață e doar pentru confort; regula adevărată e pe server (`NET-11`), iar erorile 400 de la server se afișează pe câmpul corespunzător. |
| `UI-05` | O | Sume, prețuri, cantități: nu se calculează în JavaScript cu `number`. Totalurile afișate în timpul editării vin de la server (endpoint de calcul) sau dintr-o bibliotecă zecimală (`decimal.js`) cu aceleași reguli de rotunjire; valoarea salvată e mereu cea calculată de server. Valorile circulă în JSON ca string. |
| `UI-06` | O | Format românesc la afișare: `1.234,567890` (zecimale după politica de precizie), date `zz.ll.aaaa`, fus orar Europe/Bucharest. Toate textele prin fișierul de traduceri (română implicit), nu scrise direct în componente. |
| `UI-07` | O | Fiecare buton de acțiune se ascunde sau se dezactivează după permisiunile utilizatorului (din `/api/me/permissions`), dar serverul verifică oricum (02.01, pipeline). |
| `UI-08` | O | Ecranele urmează același tipar (P10): listă cu filtre → fișă cu tab-uri → istoric. Documentele validate se deschid doar în citire; acțiunile disponibile depind de stare. |
| `UI-09` | R | Utilizabil complet din tastatură pe ecranele de introducere masivă (facturi, recepții, note contabile): Enter/Tab între câmpuri, scurtături pentru salvare și linie nouă. Contrast și etichete conforme WCAG 2.1 AA. |
| `UI-10` | R | Grilele mari folosesc paginare pe server sau virtualizare; nicio listă nu încarcă toate înregistrările în browser. |
| `UI-11` | O | Fiecare salvare, în orice ecran, dă feedback vizibil: notificare de succes după reușită (componenta comună `Notifier`, Toast Syncfusion, anunțată și prin `role="status"`); la eșec, notificare de eroare plus erorile pe câmp (`UI-04`). Nicio salvare nu se termină „în tăcere” (decizia lui Aurelian, 15.11.2026). |
| `UI-12` | O | Fără flicker la reîncărcarea datelor (interzis, decizia lui Aurelian, 15.11.2026): după o salvare sau o reîncărcare, ecranul nu se golește, nu trece prin „Se încarcă…” și nu redesenează componentele ale căror date nu s-au schimbat. Componentele Syncfusion primesc proprietăți stabile (date memorate, șabloane și handler-e create o singură dată); selecția, nodurile strânse și poziția rămân după reîncărcare. |
| `UI-13` | O | Orice culoare vine din tokenii definiți în [10 Interfață și temă vizuală](../10-interfata-tema/INTERFATA_TEMA.md), folosiți ca `var(--…)`. Hex, `rgb()`, `hsl()` sau nume de culoare scrise direct în `*.tsx`, în paginile Razor sau în foile de stil ale ecranelor sunt interzise; tokeni noi se adaugă numai în documentul 10 și în `theme/tokens.css`. |
| `UI-14` | O | **Fără fundal alb** (decizia lui Aurelian, 20.11.2026): nicio suprafață — pagină, card, fișă, câmp, grilă, meniu, popup, fereastră de dialog, toast — nu folosește `#fff`, `#ffffff`, `white`, `#fefefe` sau `rgb(255,255,255)`. Albul rămâne permis numai ca text sau iconiță pe zonele închise (`--navy-600`…`--navy-900`). Regula e verificată automat de testul care citește foile de stil. Excepție aprobată de Aurelian (20.11.2026): fereastra installer-ului (`ErpSetup.exe`), care nu face parte din interfața ERP (10, secțiunea 9). |
| `UI-15` | O | Zonele aplicației se diferențiază vizual după harta din 10, secțiunea 4: antet și meniu lateral închise, canvas pal, fișă mai deschisă decât canvasul, câmp mai deschis decât fișa, bară de filtre și antet de grilă mai închise. Fiecare modul își poartă accentul (10, secțiunea 5); două suprafețe vecine nu au niciodată aceeași treaptă fără o linie între ele. |
| `UI-16` | O | Contrast WCAG 2.1 AA, verificat prin calcul, nu din ochi: text ≥4,5:1, conturul controalelor și limitele componentelor ≥3:1. Se folosesc numai combinațiile permise din 10, secțiunile 5 și 6. O stare (succes, avertizare, eroare) nu se transmite niciodată numai prin fundal: are obligatoriu bandă colorată și text sau iconiță în culoarea stării (WCAG 1.4.1). |
| `UI-17` | O | Componentele Syncfusion se temizează într-un singur fișier, `theme/syncfusion.css`, prin variabilele temei. În ecrane nu se suprascriu clase `.e-*` și nu se folosește `!important`. |
| `UI-18` | O | Modificările nesalvate dintr-o fișă nu se pierd fără confirmare (decizia lui Aurelian, 20.11.2026): nici la schimbarea tab-ului, a înregistrării alese, a ecranului sau a firmei curente, nici la reîmprospătarea sau închiderea paginii. Tab-urile unei fișe nu se demontează la schimbare, iar orice altă ieșire trece prin garda comună `UnsavedChanges` („Rămân” e alegerea implicită). După o salvare reușită formularul nu mai are modificări, deci garda nu se declanșează. |

## 6. Teste

| ID | Nivel | Regulă |
| --- | --- | --- |
| `TST-01` | O | Fiecare criteriu de acceptare dintr-un document de modul (de exemplu A1–A15 din 02.01) are cel puțin un test automat care îl numește (`[Trait("Criteriu", "02.01-A2")]`). |
| `TST-02` | O | Teste unitare (xUnit) pentru calcule și reguli pure: rotunjiri, TVA, validatori, conversii UM, reguli de stare. Rapide, fără bază de date. |
| `TST-03` | O | Teste de integrare pe SQL Server real în container (Testcontainers), cu migrațiile aplicate: fiecare procedură stocată are cel puțin un test pe drumul bun și unul pe fiecare eroare `THROW`. Nu se folosesc baze în memorie în locul SQL Server. |
| `TST-04` | O | Teste de arhitectură (NetArchTest): modulele nu se referențiază decât prin `.Contracts`, `Application` nu conține SQL, toate comenzile au permisiune declarată. |
| `TST-05` | O | Teste de concurență pentru zonele critice: numerotare, stoc, solduri, outbox (de exemplu 02.01-A2, A8). |
| `TST-06` | O | Teste de conformitate fiscală pe fișiere de referință: XML-urile generate (e-Factura, SAF-T, declarații) se validează cu schemele și validatoarele oficiale ANAF. |
| `TST-07` | R | Datele de test se construiesc cu builder-e (`APartner().WithVatPayer().Build()`), nu cu scripturi SQL lungi copiate. Numele testului spune scenariul: `Validate_WhenPeriodClosed_Returns409`. |
| `TST-08` | R | Teste end-to-end (Playwright) doar pentru fluxurile critice ale fiecărui modul (de exemplu: facturare cu trimitere la e-Factura simulată), nu pentru fiecare ecran. |
| `TST-09` | O | Testele de integrare rulează pe o bază creată exact ca la client (`Latin1_General_100_CS_AS_SC_UTF8`, de installer sau de același cod), pe un server SQL cu collation implicit case insensitive (`SQL_Latin1_General_CP1_CI_AS`, cel din imaginea Docker), ca greșelile de majuscule și conflictele cu `tempdb` să apară în CI. |

## 7. Git, integrare continuă și securitate

### 7.1 Git și revizuire

| ID | Nivel | Regulă |
| --- | --- | --- |
| `GIT-01` | O | Un singur depozit (monorepo) pentru backend, frontend, migrații și installer. Ramura principală `main` e mereu livrabilă; lucrul se face pe ramuri scurte (`feat/sales-invoice-validate`), unite prin pull request. |
| `GIT-02` | O | Mesajele de commit în format Conventional Commits, cu modulul ca scop: `feat(sales): validare factură`, `fix(core): gol în serie la rollback`. Din ele se generează nota de versiune pentru clienți. |
| `GIT-03` | O | Orice pull request trece prin revizuire (și în echipa de un om plus Claude: Claude revizuiește codul scris de om și invers) folosind checklist-ul din secțiunea 8. |
| `GIT-04` | O | CI la fiecare pull request: compilare fără avertismente, teste unitare, de integrare și de arhitectură, migrații pe bază goală și pe bază din versiunea anterioară (`MIG-07`), lint frontend, scanare de dependențe vulnerabile. Fără CI verde nu se unește. |
| `GIT-05` | O | Versiunea produsului are 5 componente: `AAAA.LL.Major.Release.ServicePack` (de exemplu `2026.11.8.2.3`); fiecare versiune livrată are tag în Git și pachet de instalare reproductibil. Formele mai scurte se compară completate cu zero. |
| `GIT-06` | O | La fiecare commit trimis pe GitHub se construiește un kit nou și commitul primește tag cu versiunea kitului (decizia lui Aurelian, 20.11.2026). Procedura: [ghidul de instalare, 3.2 și 3.5](../08-instalare-actualizare/GHID_INSTALARE_ACTUALIZARE.md). |

### 7.2 Securitate și GDPR în cod

| ID | Nivel | Regulă |
| --- | --- | --- |
| `SEC-01` | O | Orice intrare de la utilizator sau din fișiere importate e tratată ca nesigură: validată (`NET-11`), parametrizată în SQL (`SP-06`, `NET-07`), codificată la afișare (React o face implicit; fără `dangerouslySetInnerHTML`). |
| `SEC-02` | O | Secretele (parole SQL, certificatul ANAF, cheile de criptare) nu apar niciodată în cod, în Git, în jurnale sau în mesaje de eroare. |
| `SEC-03` | O | Datele personale se citesc doar cu permisiunea dedicată, iar accesul se jurnalizează (de exemplu `Core.Partner.ViewPersonalData`). Exporturile de date personale sunt acțiuni auditate. |
| `SEC-04` | O | Dependențele (NuGet, npm) se actualizează lunar; o vulnerabilitate critică cunoscută blochează livrarea. |
| `SEC-05` | O | Fișierele încărcate (atașamente, importuri) au limită de dimensiune, listă de tipuri permise și nu se execută niciodată; numele de fișier se generează de sistem. |

## 8. Checklist de revizuire

### 8.1 Pentru fiecare pull request

- [ ] Schema nouă respectă denumirile, tipurile și cheile (`SQL-01`–`SQL-20`).
- [ ] SQL-ul e scris pentru baza case sensitive: identificatori exacți, `COLLATE DATABASE_DEFAULT` în `#t`, unicitate pe `CodeKey`, căutare pe `NameSearch` (`SQL-21`–`SQL-25`, `NET-21`).
- [ ] Procedurile au `NOCOUNT`, `XACT_ABORT`, erori cu `THROW` din plaja modulului, fără `SELECT *`, condiții SARGable (`SP-02`, `SP-04`, `SP-05`, `SP-09`).
- [ ] Migrația e nouă, nu modifică una livrată, și e compatibilă în urmă (`MIG-02`, `MIG-05`).
- [ ] Comanda are modul, permisiune și audit declarate; endpoint-ul e subțire (`NET-03`, `NET-04`).
- [ ] Banii și cantitățile sunt `decimal`, rotunjite doar prin `IPrecisionService` (`NET-14`, `UI-05`).
- [ ] Evenimentele pleacă prin outbox, în aceeași tranzacție; nimic extern în tranzacție (`SP-15`).
- [ ] Există teste pentru drumul bun, pentru fiecare eroare și pentru criteriile de acceptare atinse (`TST-01`, `TST-03`).
- [ ] Nicio dată personală sau secret în jurnale, cod sau mesaje de eroare (`NET-18`, `SEC-02`).
- [ ] Culorile vin din tokeni, nicio suprafață nu e albă, zonele se diferențiază și contrastul e calculat (`UI-13`–`UI-17`).
- [ ] Fișele nu pierd modificările nesalvate fără confirmare (`UI-18`).

### 8.2 Pentru fiecare document de arhitectură

- [ ] Tabelele din secțiunea 7 (Schema SQL) respectă `SQL-01`–`SQL-25`; exemplele `CREATE TABLE` și procedurile respectă șablonul din 3.5.
- [ ] Contractele din secțiunea 6 respectă `NET-01`, `NET-05`, `NET-14`, `NET-15`.
- [ ] Erorile de business ale modulului au plaja de coduri SQL și codurile stabile listate (`SP-04`, `NET-09`).
- [ ] Criteriile de acceptare pot fi testate automat (`TST-01`).
- [ ] Abaterile de la acest document sunt scrise explicit, cu motiv (secțiunea 1).

### 8.3 Plaje de coduri de eroare SQL per modul

| Modul | Plajă | Modul | Plajă |
| --- | --- | --- | --- |
| `core` | 50000–50999 | `wms` | 56000–56999 |
| `fin` | 51000–51999 | `tms` | 57000–57999 |
| `sales` | 52000–52999 | `mfg` | 58000–58999 |
| `purch` | 53000–53999 | `hr` | 59000–59999 |
| `inv` | 54000–54999 | `pos` | 60000–60999 |
| `intg` | 55000–55999 | `cons` | 61000–61999 |
| `rpt` | 62000–62999 | `crm` | 63000–63999 |
| `ids` | 64000–64999 | | |

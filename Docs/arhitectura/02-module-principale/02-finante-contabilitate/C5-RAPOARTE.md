# ERP · 02.02 Contabilitate · C5 Situații financiare și rapoarte

15 noiembrie 2026 · Aurelian · **✓ Validat (15.11.2026)**, cu ecranele și toate cele 5 decizii. Ultima din cele 5 sub-părți ale părții contabile. Pornește de la deciziile deja luate în C1–C4: notele se fac doar pe planul românesc, iar planurile alternative se calculează pe fiecare linie (C1); notele de închidere au `SourceKind` 4 (C3); soldurile de deschidere ale anului stau în `AccountOpeningBalance` (C3); declarațiile se generează în ERP și se depun de utilizator (C4, decizia 1).

Modulul 02.02 Finanțe și contabilitate (schema SQL `fin`, prefix de cod `Erp.Finance`). C5 acoperă balanța de verificare, fișa contului și cartea mare, registrul inventar, situațiile financiare anuale și raportările semestriale, balanța și situațiile pe planurile alternative (inclusiv balanța de grup, cu eliminările dintre firme) și motorul de rapoarte pe rânduri cu formule pe conturi, folosit și pentru rapoartele proprii ale clientului. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), [C1](C1-PLANURI-DE-CONTURI.md), [C2](C2-MODELE-DE-CONTARE-SI-NOTE.md), [C3](C3-INCHIDERI.md), [C4](C4-DECLARATII.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- C5 **doar citește**. Nu scrie note și nu schimbă solduri; orice corecție se face prin C2 (notă) sau C3 (redeschidere).
- Sursele sunt `fin.JournalLine` (C2), `fin.JournalLineAltAccount` (C1), `fin.AccountOpeningBalance` (C3) și atributele conturilor (C1). Soldurile lunilor închise se materializează la închidere (3.1), cele ale lunilor deschise se calculează la cerere.
- Rapoartele care adună date din mai multe module (dashboard-uri, rapoarte programate pe email) sunt în modulul Raportare (02.13); C5 îi dă contractul de citire a soldurilor (6).
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`): rapoartele contabile la 2 zecimale; situațiile financiare la leu întreg, după regula formularului.
- **Ce NU face C5**: nu face note (C2), nu închide perioade (C3), nu calculează declarațiile fiscale (C4), nu face dashboard-uri de management (02.13), nu face consolidarea completă după IFRS (decizia 3).

## 1. Scop și perimetru

| Zonă | Ce face C5 | Ce NU face (și cine face) |
| --- | --- | --- |
| Balanța de verificare | Pe firmă, perioadă, nivel (sintetic de gradul I, II, analitic), plan (românesc sau alternativ), dimensiune; cu verificarea egalităților | Dashboard-uri (02.13) |
| Fișa contului și cartea mare | Mișcările unui cont, cu soldul după fiecare notă, filtrabile pe partener și dimensiuni; registrul cartea mare pe lună | Fișa financiară a partenerului (F3) |
| Registrul inventar | Anual, din balanță și listele de inventariere | Inventarierea fizică (Stocuri, Mijloace fixe) |
| Situații financiare | Bilanțul, contul de profit și pierdere, datele informative, situația activelor imobilizate, pe categoria entității; raportările semestriale; fișierul pentru ANAF | Semnarea și depunerea (utilizatorul, ca în C4) |
| Planuri alternative și grup | Balanța și rapoartele pe planul alternativ, pe firmă și pe grup, cu eliminarea soldurilor și rulajelor dintre firmele grupului | Consolidarea IFRS completă (participații, interese minoritare, fond comercial) |
| Rapoarte definite | Motorul de rânduri cu formule pe conturi și dimensiuni, pentru situațiile livrate și pentru rapoartele clientului | Rapoartele din alte module (02.13) |

## 2. Model de domeniu

```
 fin.JournalLine (C2) ─┬─► AccountPeriodBalance (pe lună închisă, materializat de C3 la închidere)
 fin.JournalLineAltAccount (C1) ─┘          │
 fin.AccountOpeningBalance (C3) ─────────────┤
                                             ▼
                               IBalanceQuery (sold inițial, rulaje, sold final, pe orice nivel și dimensiune)
                                             │
            ┌──────────────────┬─────────────┼───────────────────┬──────────────────────┐
            ▼                  ▼             ▼                   ▼                      ▼
   Balanța de verificare   Fișa contului   ReportDefinition    Balanța de grup      Registrul inventar
                           Cartea mare     (rânduri + formule)  (plan alternativ +
                                             │                  eliminări)
                                             ▼
                                FinancialStatement (an, formular, rânduri, fișier, depunere)
```

Reguli de business ale modelului:

1. **Un singur calcul de sold.** Toate rapoartele (balanța, fișa, situațiile, rapoartele clientului, contractul pentru 02.13) citesc soldurile prin același serviciu, `IBalanceQuery`. Nu există două formule pentru „soldul contului”.
2. **Balanța e egală cu ea însăși.** Total debit = total credit pe fiecare pereche de coloane; soldul final = sold inițial + rulaje pe fiecare cont; suma analiticelor = sinteticul. Balanța afișează rezultatul acestor verificări, nu doar cifrele.
3. **Notele de închidere se pot ascunde.** Rapoartele de gestiune (pe centre de cost, pe proiecte, contul de profit și pierdere al lunii) exclud implicit notele cu `SourceKind` 4, altfel conturile 6 și 7 ar fi zero; balanța legală le include.
4. **Situațiile financiare se calculează din balanță, prin rânduri livrate.** Fiecare rând al formularului are o formulă pe conturi (sold debitor, sold creditor, rulaj), livrată și versionată pe dată, needitabilă de client (decizia 2). Corecțiile se fac prin note contabile.
5. **Fișierul final al situațiilor se generează doar pe un an închis** (C3); raportarea semestrială, doar cu iunie închisă (decizia 5).
6. **Balanța de grup elimină doar ce e sigur.** Se elimină automat soldurile și rulajele dintre firmele grupului (partenerul liniei e altă firmă din grup), pe conturile marcate pentru eliminare în planul alternativ; diferențele dintre cele două părți se raportează, nu se ajustează singure (decizia 3).

## 3. Entități, documente și ciclul lor de viață

Convențiile de coloane, tipuri și chei sunt cele din F1, secțiunea 3.

### 3.1 Soldul lunar materializat (`AccountPeriodBalance`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `PeriodYear`, `PeriodMonth`, `ChartId` | Planul românesc sau un plan alternativ |
| `AccountId`, `PartnerId`, `CostCenterId`, `ProjectId`, `CurrencyCode` | Granularitatea; `NULL` unde nu se aplică |
| `DebitRon`, `CreditRon`, `ClosingDebitRon`, `ClosingCreditRon` | Rulajele lunii, separat cele din notele de închidere (regula 3) |
| `DebitCurrency`, `CreditCurrency` | Pentru conturile în valută |
| `BuiltAt`, `BuiltFromRunId` | Rularea de închidere (C3) care a construit rândurile |

- Se construiește la închiderea lunii (C3, după notele de închidere) și se șterge la redeschidere; un plan alternativ se reconstruiește și la recalcularea mapării (C1).
- Lunile deschise se calculează direct din `JournalLine` (indexul columnstore din C2), deci rapoartele arată mereu datele curente.

### 3.2 Balanța de verificare

Formatul implicit (decizia 1) e balanța cu solduri inițiale, rulaje precedente, rulaje curente, sume totale și solduri finale:

| Coloană | Calcul |
| --- | --- |
| Solduri inițiale an (D, C) | `AccountOpeningBalance` al anului |
| Rulaje precedente (D, C) | De la începutul anului până la luna dinaintea perioadei |
| Rulaje curente (D, C) | Perioada aleasă (o lună sau un interval) |
| Sume totale (D, C) | Solduri inițiale + rulaje precedente + rulaje curente |
| Solduri finale (D, C) | Diferența sumelor totale, pe partea cu sold, după tipul contului (activ, pasiv, bifuncțional) |

Variante: balanța compactă (sold inițial al perioadei, rulaje, sold final), balanța analitică pe partener (`401*`, `4111*`), balanța pe dimensiune (centru de cost, proiect), balanța în valută (conturile cu `CurrencyMode` în valută). Conturile bifuncționale se pot afișa cu sold net sau cu ambele solduri (setare pe raport).

### 3.3 Fișa contului și cartea mare

- **Fișa contului**: soldul inițial al perioadei, apoi fiecare linie de notă (data, nota, jurnalul, documentul sursă, explicația, partenerul, dimensiunile, debit, credit, sold curent) și soldul final; filtrabilă pe partener, centru de cost, proiect, monedă; din fiecare linie se deschide nota (C2) și documentul sursă.
- **Cartea mare** (registru obligatoriu): pe fiecare cont sintetic, pe lună, soldul inițial, rulajele pe conturi corespondente (din notele cu o singură contrapartidă sau pe linii de tip „%”), soldul final. Se generează PDF pe lună și se arhivează la închiderea anului.

### 3.4 Registrul inventar (`InventoryRegister`)

Anual, la închiderea anului (C3), pe firmă: pentru fiecare cont de bilanț, soldul la 31 decembrie și elementele lui (pe partener, pe gestiune, pe mijloc fix), cu valoarea contabilă, valoarea de inventar și diferențele, din listele de inventariere ale modulelor (Stocuri, Mijloace fixe) și din confirmările de sold (F3). Se generează PDF, numerotat și arhivat; diferențele de inventar se înregistrează prin note (C2) înainte de închiderea lui decembrie.

### 3.5 Definiții de raport (`ReportDefinition` + `ReportRow`)

Același motor pentru situațiile livrate și pentru rapoartele clientului (decizia 4):

| Câmp | Reguli |
| --- | --- |
| `Code`, `Name`, `Kind`, `ChartId` | 1 Livrat (formular legal, needitabil), 2 Al clientului; planul pe care se calculează |
| `ValidFrom`, `ValidTo`, `Version` | Versiunea formularului legal (P6) |
| `Columns` | Coloanele: perioade (an curent, an precedent, luni), dimensiuni (centru de cost, proiect, firmă) |

**Rândul** (`ReportRow`): `RowCode` (de exemplu `F20.01`), `Label`, `Formula`, `Sign`, `IsTotal`, `Indent`, `Format`.

Gramatica formulei (interpretată în .NET, nu SQL dinamic din text liber, ca să nu existe injecție, `SEC-03`):

| Element | Exemplu | Semnificație |
| --- | --- | --- |
| `SD(cont)`, `SC(cont)` | `SD(5121)` | Sold final debitor, creditor (cu analiticele) |
| `SN(cont)` | `SN(4423)` | Sold net (debit − credit) |
| `RD(cont)`, `RC(cont)` | `RC(707)` | Rulaj debitor, creditor în perioadă, fără notele de închidere |
| Interval și listă | `RC(701..708)`, `SD(201,203,205)` | Mai multe conturi |
| Filtru de dimensiune | `RD(628 | CC=LOG)` | Doar centrul de cost `LOG` |
| Rânduri | `[F20.05] - [F20.12]` | Alte rânduri ale aceluiași raport |
| Funcții | `MAX(0, ...)`, `ABS(...)` | Pentru rândurile care cer sold doar dacă e pozitiv |

Fiecare celulă calculată se poate detalia: conturile care au contribuit și, mai departe, fișa contului.

### 3.6 Situații financiare (`FinancialStatement`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Year`, `Kind` | 1 Anuale, 2 Raportare semestrială |
| `EntityCategory` | Microentitate, entitate mică, mijlocie sau mare: calculată din criteriile legale (active, cifră de afaceri, număr mediu de salariați) pe ultimii doi ani, propusă și confirmată de utilizator |
| `Forms` | Formularele cerute de categorie: bilanț (F10), cont de profit și pierdere (F20), date informative (F30), situația activelor imobilizate (F40) |
| `Status` | 1 Ciornă, 2 Verificată, 3 Generată, 4 Depusă |
| `RegistrationNumber`, `SubmittedAt` | Din recipisă |

- Rândurile se calculează din definițiile livrate (3.5) pentru anul curent și anul precedent (din situațiile depuse anterior sau recalculate).
- Verificări: bilanțul e echilibrat (activ = capitaluri + datorii), profitul din F20 = soldul `121` înainte de preluare, corelațiile dintre formulare (cele publicate de Ministerul Finanțelor, livrate ca reguli).
- Datele informative fără sursă contabilă (număr mediu de salariați, din HR dacă e licențiat; date despre participații) se completează manual, cu motiv.
- Fișierul se generează prin Integrări (02.14), ca la C4, se semnează și se depune de utilizator; la înregistrarea recipisei se publică `SituatiiFinanciareDepuse`, pe care C3 îl folosește pentru a propune blocarea anului.

### 3.7 Balanța de grup (`GroupBalanceRun`)

- Pe un plan alternativ (de exemplu `IFRS-GRUP`) și o perioadă: coloanele sunt firmele grupului, eliminările și totalul consolidat.
- **Eliminările**: pe fiecare cont alternativ marcat `EliminateIntercompany` (creanțe și datorii între firme, venituri și cheltuieli între firme), liniile al căror partener e altă firmă din grup se elimină din ambele părți. Perechile de eliminare (creanță ↔ datorie, venit ↔ cheltuială) se configurează pe planul alternativ.
- **Diferențele** dintre părți (factură în tranzit, curs diferit) apar într-un raport separat, pe pereche de firme, cu documentele care diferă; nu se ajustează automat.
- Rularea se salvează (`GroupBalanceRun`, rândurile ei), ca rezultatul raportat către compania-mamă să poată fi refăcut exact.

## 4. Fluxuri principale

### 4.1 Balanța lunară

1. Contabilul alege firma, perioada, nivelul și planul; sistemul citește lunile închise din `AccountPeriodBalance` și lunile deschise din `JournalLine`.
2. Balanța se afișează cu verificările (regula 2); orice verificare greșită e o eroare de sistem, raportată ca incident (nu ar trebui să apară niciodată).
3. Export Excel (cu formule de total) și PDF; balanța lunii închise se arhivează automat la închidere.

### 4.2 Situațiile financiare anuale

1. După închiderea anului (C3), Contabilul șef deschide situațiile pentru an; sistemul propune categoria entității și formularele.
2. Calculul rândurilor; verificările și corelațiile; completarea datelor informative manuale.
3. „Generează”: fișierul prin Integrări, validat cu validatorul ANAF; utilizatorul semnează și depune, apoi încarcă recipisa.
4. `SituatiiFinanciareDepuse` → C3 propune blocarea anului.

### 4.3 Balanța de grup lunară

1. Controllerul de grup alege planul alternativ și luna; sistemul verifică faptul că luna e închisă la toate firmele (sau arată care nu sunt) și că maparea e completă (C1).
2. Rulează eliminările; afișează coloanele pe firme, eliminările, consolidatul și diferențele intra-grup.
3. Exportă în formatul cerut de compania-mamă (Excel sau un raport definit, 3.5).

### 4.4 Raport definit de client

1. Contabilul șef copiază un raport livrat sau pornește unul gol; adaugă rânduri cu formule și coloane.
2. Previzualizarea arată valorile pe o perioadă aleasă și erorile de formulă (cont inexistent, referință circulară).
3. Raportul salvat apare în meniu pentru utilizatorii cu dreptul de citire; 02.13 îl poate programa pe email.

### 4.5 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Lună redeschisă (C3) | Soldurile materializate ale lunii se șterg; balanța citește din nou din linii până la reînchidere |
| Balanța pe un interval care traversează doi ani | Refuzată (`51973`): balanța e pe un an financiar |
| Cont analitic nou în timpul anului | Apare cu sold inițial zero; sinteticul lui rămâne egal cu suma analiticelor |
| Formular legal nou pentru situațiile anuale | Livrat printr-o actualizare, cu dată de vigoare; anii depuși rămân pe versiunea lor |
| Firmă din grup cu exercițiu financiar diferit | Balanța de grup e pe luni calendaristice; anul financiar al fiecărei firme nu contează |
| Partener intra-grup fără firmă asociată | Nu se elimină; apare în raportul de diferențe ca „partener din grup neidentificat” |
| Fișa unui cont cu 500.000 de linii pe an | Paginată pe server (`SP-12`), cu soldul curent calculat pe server (funcție fereastră) |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Payload | Consumatori |
| --- | --- | --- | --- |
| `SituatiiFinanciareDepuse` v1 | Recipisa situațiilor anuale înregistrată | `CompanyId`, `Year`, `RegistrationNumber` | C3 (propune blocarea anului), Raportare |
| `BalantaDeGrupRulata` v1 | O rulare a balanței de grup salvată | `RunId`, `ChartId`, perioada | Raportare |

### 5.2 Consumate

`PerioadaContabilaInchisa` și `PerioadaContabilaRedeschisa` (C3: construirea și ștergerea soldurilor materializate), `AnFinanciarInchis` (C3: registrul inventar, propunerea situațiilor), `MapareRecalculata` (C1: reconstruirea planului alternativ).

## 6. Contracte

```csharp
namespace Erp.Finance.Contracts.Reporting;

public enum BalanceLevel : byte { Synthetic1 = 1, Synthetic2 = 2, Analytic = 3 }

public sealed record BalanceRequest(
    int CompanyId, int ChartId, DateOnly From, DateOnly To, BalanceLevel Level,
    bool IncludeClosingEntries, string? AccountFilter,
    bool ByPartner, bool ByCostCenter, bool ByProject, string? CurrencyCode);

public sealed record BalanceRow(
    string AccountCode, string AccountName, int? PartnerId, int? CostCenterId, int? ProjectId,
    decimal OpeningDebit, decimal OpeningCredit, decimal PriorDebit, decimal PriorCredit,
    decimal PeriodDebit, decimal PeriodCredit, decimal ClosingDebit, decimal ClosingCredit);

// Singurul mod de a citi solduri, pentru C5 și pentru Raportare (02.13)
public interface IBalanceQuery
{
    IAsyncEnumerable<BalanceRow> GetBalanceAsync(BalanceRequest request, CancellationToken ct);
    Task<decimal> EvaluateAsync(int companyId, int chartId, DateOnly from, DateOnly to, string formula, CancellationToken ct);
}
```

Comenzi interne C5: `RunTrialBalance`, `GetAccountLedger`, `ExportGeneralLedgerBook`, `BuildPeriodBalances`, `DropPeriodBalances`, `PrepareFinancialStatements`, `SetStatementManualValue`, `GenerateFinancialStatementsFile`, `RegisterStatementsSubmission`, `RunGroupBalance`, `SaveReportDefinition`, `PreviewReport`, `GenerateInventoryRegister`.

## 7. Schema SQL `fin` (partea C5)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `fin.AccountPeriodBalance` | `Id bigint` | UX `(CompanyId, ChartId, PeriodYear, PeriodMonth, AccountId, PartnerId, CostCenterId, ProjectId, CurrencyCode)` (cu `NULL` tratat prin coloane calculate) | columnstore clustered (`SQL-16`) |
| `fin.ReportDefinition`, `fin.ReportColumn`, `fin.ReportRow` | `Id int` | UQ `(Code, Version)`; UQ `(DefinitionId, RowCode)` | — |
| `fin.FinancialStatement`, `fin.FinancialStatementLine` | `Id int`, `Id bigint` | UQ `(CompanyId, Year, Kind)`; FK `ReportDefinitionId` | — |
| `fin.GroupBalanceRun`, `fin.GroupBalanceRunLine` | `Id int`, `Id bigint` | FK `ChartId` | `(ChartId, PeriodYear, PeriodMonth)` |
| `fin.IntercompanyPair` | `Id int` | UQ `(ChartId, ReceivableAccountId, PayableAccountId)` | — |
| `fin.InventoryRegister` | `Id int` | UQ `(CompanyId, Year)`; FK `AttachmentId` | — |

Exemplu de tabel complet:

```sql
CREATE TABLE fin.AccountPeriodBalance (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountPeriodBalance PRIMARY KEY NONCLUSTERED,
    CompanyId          int               NOT NULL CONSTRAINT FK_AccountPeriodBalance_Company REFERENCES core.Company(Id),
    ChartId            int               NOT NULL CONSTRAINT FK_AccountPeriodBalance_Chart REFERENCES fin.Chart(Id),
    PeriodYear         smallint          NOT NULL,
    PeriodMonth        tinyint           NOT NULL,
    AccountId          int               NOT NULL,          -- cont RO sau alternativ, după ChartId
    PartnerId          int               NULL,
    CostCenterId       int               NULL,
    ProjectId          int               NULL,
    CurrencyCode       char(3)           NULL,
    DebitRon           decimal(19,2)     NOT NULL,
    CreditRon          decimal(19,2)     NOT NULL,
    ClosingDebitRon    decimal(19,2)     NOT NULL,          -- din notele de închidere (SourceKind 4)
    ClosingCreditRon   decimal(19,2)     NOT NULL,
    DebitCurrency      decimal(19,2)     NULL,
    CreditCurrency     decimal(19,2)     NULL,
    BuiltAt            datetime2(3)      NOT NULL CONSTRAINT DF_AccountPeriodBalance_BuiltAt DEFAULT (sysutcdatetime()),
    BuiltFromRunId     bigint            NULL,
    CONSTRAINT CK_AccountPeriodBalance_Month CHECK (PeriodMonth BETWEEN 1 AND 12)
);
CREATE CLUSTERED COLUMNSTORE INDEX CCI_AccountPeriodBalance ON fin.AccountPeriodBalance;
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `fin.usp_Balance_Get` | Balanța pe cerere (3.2): materializat pentru lunile închise + linii pentru cele deschise, pe set | Nu |
| `fin.usp_AccountLedger_Get` | Fișa contului, paginată, cu soldul curent calculat cu funcție fereastră | Nu |
| `fin.usp_PeriodBalance_Build`, `_Drop` | Construirea și ștergerea soldurilor materializate ale unei luni | Nu |
| `fin.usp_GroupBalance_Run` | Balanța de grup și eliminările | Nu |
| `fin.usp_Intercompany_Differences` | Diferențele dintre firmele grupului | Nu |

Formulele rapoartelor (3.5) se evaluează în .NET peste rezultatul lui `fin.usp_Balance_Get` (o singură citire pe raport), nu prin SQL generat.

Coduri de eroare (plaja C5 `51970–51999`, prefix API `FIN_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 51971 | `FIN_REPORT_NOT_FOUND` | 404 | Definiție de raport sau situație inexistentă |
| 51972 | `FIN_REPORT_CONCURRENCY_CONFLICT` | 409 | `RowVersion` diferit (`SP-18`) |
| 51973 | `FIN_BALANCE_CROSSES_YEARS` | 422 | Interval de balanță peste doi ani financiari |
| 51974 | `FIN_REPORT_FORMULA_INVALID` | 422 | Formulă cu cont inexistent, sintaxă greșită sau referință circulară |
| 51975 | `FIN_REPORT_DEFINITION_READ_ONLY` | 403 | Modificarea unui raport livrat |
| 51976 | `FIN_STATEMENTS_YEAR_NOT_CLOSED` | 409 | Generarea situațiilor pe un an neînchis sau a raportării semestriale cu iunie deschisă |
| 51977 | `FIN_STATEMENTS_UNBALANCED` | 409 | Bilanț dezechilibrat sau corelații încălcate |
| 51978 | `FIN_STATEMENTS_ALREADY_SUBMITTED` | 409 | Modificarea situațiilor depuse |
| 51979 | `FIN_GROUP_BALANCE_PERIOD_OPEN` | 409 | Rularea balanței de grup „finale” cu o firmă cu luna deschisă (rularea de previzualizare e permisă) |
| 51980 | `FIN_GROUP_BALANCE_MAPPING_INCOMPLETE` | 409 | Linii nemapate în planul alternativ, la o firmă |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterile:

- Abatere de la `SQL-19` și `SQL-11` pentru `fin.AccountPeriodBalance`: fără `ModifiedAt`, `ModifiedBy`, `RowVersion`, `CreatedBy`. Motiv: tabel derivat, reconstruit integral la fiecare închidere. Permanentă.
- Abatere de la `SQL-03` (cheie primară clustered) pentru `fin.AccountPeriodBalance`: indexul clustered e columnstore. Motiv: tabelul se citește doar agregat, pe milioane de rânduri. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Fin.Report.TrialBalance` | Balanța, fișa contului, cartea mare |
| `Fin.Report.Statements`, `.StatementsSubmit` | Situațiile financiare; generarea și recipisa |
| `Fin.Report.Group` | Balanța de grup și diferențele intra-grup (pe toate firmele grupului) |
| `Fin.Report.Define` | Rapoartele clientului |
| `Fin.Report.InventoryRegister` | Registrul inventar |

Roluri livrate: Contabil șef (toate), Contabil (balanță, fișe, situații fără depunere), Auditor (citire pe toate), Director financiar (citire pe toate), Controller de grup (rol nou: `Fin.Report.Group` și citirea balanțelor pe planurile alternative ale tuturor firmelor).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru C5 |
| --- | --- |
| Legea contabilității 82/1991 (art. 20, registrele; art. 28, situațiile financiare) | Registrul jurnal (C2), registrul inventar, cartea mare; situațiile financiare anuale |
| OMFP 1802/2014 | Formatul bilanțului și al contului de profit și pierdere, pe categorii de entități; criteriile de mărime |
| OMFP 2634/2015 | Registrele contabile, forma și păstrarea lor |
| OMFP anual privind raportările semestriale și anuale | Formularele și corelațiile anului; livrate ca versiuni de definiții |
| OMFP 2861/2009 | Registrul inventar și înregistrarea diferențelor de inventar |
| Păstrarea | Balanțele lunare, cartea mare, registrul inventar și situațiile depuse se arhivează PDF, 10 ani (02.01) |

Integrări: Integrări (02.14) pentru fișierul situațiilor financiare și validatorul ANAF. Interne: C1 (planuri, mapare), C2 (linii), C3 (solduri de deschidere, perioade, blocare), F3 (confirmări de sold), Stocuri și Mijloace fixe (liste de inventariere), HR (număr mediu de salariați), 02.13 (prin `IBalanceQuery`).

## 10. Rapoarte

Rapoartele sunt subiectul acestui document; lista lor:

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Balanța de verificare | 3.2, cu variantele ei | Contabil, Contabil șef, Auditor |
| Fișa contului, cartea mare | 3.3 | Contabil, Auditor |
| Registrul inventar | 3.4 | Contabil șef, Auditor |
| Situații financiare, raportări semestriale | 3.6 | Contabil șef, Director financiar |
| Balanța de grup, diferențe intra-grup | 3.7 | Controller de grup, Director financiar |
| Contul de profit și pierdere de gestiune | Raport definit livrat ca exemplu, pe centre de cost, fără notele de închidere | Director financiar, Director general |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/c5-rapoarte/](ecrane/c5-rapoarte/), în meniul Finanțe și contabilitate › Rapoarte:

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Balanță de verificare | Filtrele (firmă, perioadă, nivel, plan, dimensiune), cele 10 coloane, verificările egalităților | `c5-01-balanta-de-verificare` |
| Fișa contului | Soldul inițial, liniile cu sold curent, filtrele pe partener și dimensiuni, legătura spre notă și document | `c5-02-fisa-contului` |
| Situații financiare | Anii și formularele, rândurile cu anul curent și precedent, detalierea formulei, verificările, depunerea | `c5-03-situatii-financiare` |
| Balanța de grup | Plan alternativ × firme, eliminările, consolidatul și diferențele intra-grup | `c5-04-balanta-de-grup` |
| Rapoarte definite | Rândurile cu formule și previzualizarea pe coloane (exemplu: contul de profit și pierdere de gestiune pe centre de cost) | `c5-05-rapoarte-definite` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.02-C5-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| C5-A1 | Pe orice bază de test, balanța are total debit = total credit pe fiecare pereche de coloane și sinteticul = suma analiticelor | Test de proprietate |
| C5-A2 | Balanța unei luni închise citită din soldurile materializate e identică cu cea calculată direct din linii | Test de proprietate |
| C5-A3 | Redeschiderea unei luni șterge soldurile ei materializate, iar balanța reflectă imediat o notă nouă | Test de integrare (cu C3) |
| C5-A4 | Fișa contului `628` pe octombrie 2026 se termină cu sold zero după nota IN 00021, iar fără notele de închidere are sold debitor 38.940,00 | Test de integrare |
| C5-A5 | Contul de profit și pierdere 2025 pentru Exemplu Distribuție dă profit brut 1.640.200 și profit net 1.391.570, egale cu C3 | Test de integrare |
| C5-A6 | Bilanțul generat e echilibrat, iar fișierul trece validatorul ANAF | Test de integrare (cu Integrări) |
| C5-A7 | Balanța de grup pe `IFRS-GRUP` elimină facturile de transport dintre Exemplu Logistic și Exemplu Distribuție și raportează diferența de 2.500,00 (factură în tranzit) | Test de integrare |
| C5-A8 | O formulă cu referință circulară sau cu cont inexistent e refuzată la salvare (`51974`) | Test unitar |
| C5-A9 | Balanța anuală analitică pe o firmă cu 50 de milioane de linii se calculează în sub 10 secunde | Test de performanță |
| C5-A10 | Toate procedurile C5 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile C5-A1, C5-A2 și C5-A5 sunt critice: balanța e baza tuturor celorlalte raportări și a auditului.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **Situația activelor imobilizate (F40)** se completează din `IFixedAssetStatementQuery` ([M7 §6](../15-mijloace-fixe/M7-INVENTARIERE-RAPOARTE.md)), pe categoriile formularului.
- **Registrul-inventar** primește elementele conturilor de imobilizări și ale `8039` din `IAssetCountResultQuery` (inventarierea M7), cu valoarea contabilă și de inventar.

## Precizări din 02.13 Raportare și BI (aplicate pe 27.11.2026)

Modulul 02.13 Raportare și BI ([README](../13-raportare-bi/README.md), părțile BI1–BI5) a fost documentat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate. Vederile publicate sunt doar citire și sunt contracte versionate: o schimbare incompatibilă cere o versiune nouă a vederii (`_v2`), cu vechea păstrată o versiune de produs.

- **Vederea publicată** `fin.vw_GlLineFact` (v1): linia de notă validată, contul, dimensiunile analitice, debit și credit în lei și valută, jurnalul, documentul sursă. Sursa setului „Note contabile” și a realizatului din bugetare. [BI2 §9](../13-raportare-bi/BI2-DEPOZIT-DATE.md)
- **Tipul de coloană „Buget”** în `fin.ReportColumn` (cu versiunea bugetului aleasă), pentru rapoartele pe rânduri „Realizat vs Buget”, de exemplu contul de profit și pierdere pe centre de cost. [BI5 §3.2](../13-raportare-bi/BI5-BUGETARE.md)
- **Balanța sintetică** și **contul de profit și pierdere pe centre de cost** intră în pachetul livrat „Închiderea lunii pentru conducere” (BI3).

## Decizii de validare

Toate deciziile au fost confirmate de Aurelian pe 15.11.2026:

| # | Întrebare | Decizia | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Formatul implicit al balanței | Cu solduri inițiale an, rulaje precedente, rulaje curente, sume totale și solduri finale (10 coloane); varianta compactă la alegere | 3.2 | Decis de Aurelian (15.11.2026) |
| 2 | Rândurile situațiilor financiare | Livrate și versionate, needitabile de client; corecțiile prin note contabile; doar datele informative fără sursă contabilă se completează manual | 2 (regula 4), 3.6 | Decis de Aurelian (15.11.2026) |
| 3 | Consolidarea de grup în faza 1 | Balanța de grup pe planul alternativ, cu eliminarea automată a soldurilor și rulajelor dintre firme și raportul de diferențe; consolidarea IFRS completă (participații, interese minoritare) rămâne în afara fazei 1 | 2 (regula 6), 3.7 | Decis de Aurelian (15.11.2026) |
| 4 | Rapoarte proprii ale clientului | Același motor ca situațiile livrate (rânduri cu formule pe conturi și dimensiuni), cu drept separat | 3.5 | Decis de Aurelian (15.11.2026) |
| 5 | Când se generează situațiile | Fișierul final doar pe an închis (C3); raportarea semestrială doar cu iunie închisă; ciorna oricând | 2 (regula 5) | Decis de Aurelian (15.11.2026) |

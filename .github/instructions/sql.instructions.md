---
description: 'Reguli SQL Server case sensitive, proceduri stocate si migratii DbUp.'
applyTo: '**/*.sql'
---
# SQL si DbUp

Sursa: [Bune practici](../../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md), sectiunile 2-3, 8.3 (`SQL-*`, `SP-*`, `MIG-*`). Citeste regulile atinse, inclusiv nivelul O/R.

- Schema modulului obligatorie; nimic propriu in `dbo`. Tabele singular/PascalCase, coloane PascalCase, PK `Id`, FK `<Entitate>Id`; constrangeri/indecsi numiti explicit.
- Preturi/cantitati/cursuri `decimal(19,6)`, valori/TVA `decimal(19,2)`; zile `date`, momente `datetime2(3)` UTC. Audit si `RowVersion` pe tabelele aplicabile.
- PK clustered `int`/`bigint IDENTITY` dupa volum; GUID extern ca identificator unic separat, nu clustered. FK declarate/indexate; fara cascada pe date business, exceptia ciornelor conform `SQL-15`.
- Baza: `Latin1_General_100_CS_AS_SC_UTF8`, neschimbabil dupa creare; `READ_COMMITTED_SNAPSHOT ON`. Respecta exact majusculele, inclusiv `core.[User]` si parametrii.
- Text in `#temp`: `COLLATE DATABASE_DEFAULT`. Coduri utilizator: `CodeKey AS UPPER(Code) PERSISTED` cu index unic; nume utilizator lowercase; coduri tehnice comparate exact.
- Cautare nume: `NameSearch` calculat/persistat cu collation CI_AI si index, cautare prefix SARGable. Nu aplica functii/`COLLATE` coloanei in `WHERE`.
- Scrieri prin `schema.usp_Entitate_Actiune`; `SET NOCOUNT ON; SET XACT_ABORT ON;`, tranzactia aplicatiei, coloane explicite, TVP pentru liste, SQL parametrizat si operatii pe seturi.
- `THROW` din plaja exacta a modulului din 8.3; nu deduce plajele din ordinea modulelor. Verifica `RowVersion` si blocarea pentru actualizari concurente. Fara apeluri externe in tranzactie.
- Calculele fiscale sunt in C# cu `IPrecisionService`; SQL stocheaza/verifica, nu implementeaza alte rotunjiri.
- Migrare noua, ordonata, forward-only; nu edita scripturi livrate. `Programmability` separat cu `CREATE OR ALTER`; seed idempotent; modificari compatibile cu datele existente.
- Valideaza pe SQL Server real: baza CS pe server CI, instalare curata si upgrade. Nu substitui SQLite/in-memory.
- Exceptiile installer-ului pentru DDL si obiectele bibliotecilor sunt strict cele din [sectiunea 15](../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md); nu le generaliza la business.

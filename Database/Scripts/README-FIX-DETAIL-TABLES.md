# Fix pentru salvarea în tabelele de detalii (DocumentDetail și InvoiceDetail)

## Problema

Facturile nu salvează liniile de detalii în tabelele `DocumentDetail` și `InvoiceDetail` nici la creare, nici la update.

### Cauza
Procedurile stocate `sp_InvoiceDetail_UpdateLineItems` și `sp_Invoice_UpdateComplete` aveau bug-uri:
- Încercau să insereze direct în `InvoiceDetail` cu coloane care nu există (`ItemId`, `Quantity`, `UnitMeasure`)
- Nu ștergeau și `DocumentDetail` la update
- Nu transmitea `DocumentId` și owner fields către procedura de update detalii

## Soluția

Au fost corectate procedurile stocate pentru a:
1. Insera mai întâi în `DocumentDetail` (cu ItemId, Quantity, UnitMeasure)
2. Apoi insera în `InvoiceDetail` (cu DocumentDetailId, UnitPrice, VATRate, etc.)
3. Transmite corect DocumentId și owner fields

## Cum să aplici fix-ul

### Opțiunea 1: Script Batch (Windows Command Prompt) - CEL MAI SIMPLU

```cmd
cd Database\Scripts
Deploy-Fix-DetailTables.cmd
```

### Opțiunea 2: PowerShell

```powershell
cd Database\Scripts
.\Deploy-Fix-DetailTables.ps1
```

### Opțiunea 3: SQL Server Management Studio

1. Deschide SQL Server Management Studio
2. Conectează-te la server-ul `TS1828\ERP`
3. Selectează database-ul `ValyanERP`
4. Execută în ordine următoarele scripturi:
   - `054_StoredProcedures_Invoice_Update.sql`
   - `056_StoredProcedures_Invoice_ExtendParams.sql`

### Opțiunea 4: sqlcmd command line

```cmd
cd Database\Scripts
sqlcmd -S TS1828\ERP -d ValyanERP -E -i 054_StoredProcedures_Invoice_Update.sql
sqlcmd -S TS1828\ERP -d ValyanERP -E -i 056_StoredProcedures_Invoice_ExtendParams.sql
```

### Opțiunea 5: Folosind SqlRunner tool (dacă .NET este instalat)

```cmd
cd Tools\SqlRunner
dotnet run -- ../../Database/Scripts/054_StoredProcedures_Invoice_Update.sql
dotnet run -- ../../Database/Scripts/056_StoredProcedures_Invoice_ExtendParams.sql
```

## Verificare

După aplicarea fix-ului, verifică că:

1. Poți crea o factură nouă și liniile de detalii se salvează
2. Poți edita o factură existentă și liniile de detalii se actualizează corect

Interogări SQL pentru verificare:

```sql
-- Verifică numărul de detalii pentru ultima factură creată
SELECT TOP 1
    i.Id as InvoiceId,
    d.DocumentNumber,
    COUNT(dd.Id) as DocumentDetailCount,
    COUNT(id.Id) as InvoiceDetailCount
FROM Invoice i
INNER JOIN Document d ON i.DocumentId = d.Id
LEFT JOIN DocumentDetail dd ON dd.DocumentId = d.Id
LEFT JOIN InvoiceDetail id ON id.InvoiceId = i.Id
WHERE i.IsActive = 1
GROUP BY i.Id, d.DocumentNumber, i.CreatedAt
ORDER BY i.CreatedAt DESC
```

Ar trebui să vezi count-uri > 0 pentru ambele tipuri de detalii.

## Fișiere modificate

- `Database/Scripts/054_StoredProcedures_Invoice_Update.sql` - Proceduri actualizate
- `Database/Scripts/056_StoredProcedures_Invoice_ExtendParams.sql` - Proceduri actualizate cu parametri noi
- `Database/Scripts/057_Fix_InvoiceDetail_UpdateLineItems.sql` - Script nou (opțional, consolidat)
- `ValyanERP.Web/Features/Achizitii/Repositories/AchizitiiRepository.cs` - Repository actualizat

## Suport

Dacă întâmpini probleme la aplicarea fix-ului, verifică:

1. Ai permisiuni de modificare a procedurilor stocate în database
2. Connection string-ul din `appsettings.json` este corect
3. SQL Server este pornit și accesibil

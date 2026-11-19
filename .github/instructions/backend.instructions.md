---
description: 'Reguli pentru backend .NET, contracte, pipeline si autentificare ERP.'
applyTo: '**/*.cs,**/*.csproj,**/*.props,**/*.razor,**/*.cshtml'
---
# Backend

Surse: [Bune practici](../../Docs/arhitectura/bune-practici/BUNE_PRACTICI.md), sectiunea 4 (`NET-01`-`NET-21`); [Nucleu](../../Docs/arhitectura/02-module-principale/01-nucleu/NUCLEU.md), sectiunile 4-6.

- Proiecte `Erp.<Modul>.Contracts`, `.Domain`, `.Application`, `.Infrastructure`, `.Api`; organizare vertical slice. Nu aplica mecanic acest sablon proiectelor tehnice Auth/Setup/Shared.
- Endpoint-uri subtiri -> MediatR. `Application` nu contine SQL/Dapper; handler-ele folosesc interfete de store/query, implementate in `Infrastructure`, cu `IDbSession` comun.
- Comenzi/query/DTO-uri imutabile (`record`). Comenzile declara modul, permisiune si audit pentru modificari de business. Foloseste pipeline-ul comun, nu verificari duplicate in controllere.
- Pastreaza ordinea din Nucleu 4.1: corelare, licenta, autorizare, validare, perioada, tranzactie, handler, audit, commit. Query-urile trec prin primii trei pasi, fara tranzactie de scriere.
- `TransactionBehavior` detine tranzactia si idempotenta; document, numar, outbox si audit se salveaza atomic. Fara HTTP/fisiere in tranzactie. Consumatori cu inbox/idempotenta, retry si esec vizibil.
- FluentValidation pentru forma; handler pentru reguli cu date; integritate si in SQL. ProblemDetails: 400/403/404/409 conform `NET-10`, 500 numai neprevazut. Mapeaza erorile SQL la coduri stabile per modul.
- `Nullable` si warnings-as-errors; I/O async cu `CancellationToken`; `TimeProvider`, UTC si `DateOnly`; rotunjire numai prin `IPrecisionService`.
- Compara coduri tehnice cu `Ordinal`, coduri utilizator normalizate cu `OrdinalIgnoreCase`. Options validate la pornire; Serilog cu corelare, fara date sensibile.

## Autentificare

La modificari Auth, citeste [specificatia](../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md), sectiunea 8 si 5.12 (cheile Data Protection).

- Identity cu Dapper in `ids`, pagini Razor `/account/*`, cookie securizat; nu `MapIdentityApi` sau tokenuri browser in locul cookie-ului.
- Pastreaza antiforgery pentru scrieri autentificate cu cookie, 401/403 fara redirect pe API, revocarea sesiunilor si verificarea `SecurityStamp`.
- Installer-ul creeaza exact un administrator; upgrade/repair/recover nu modifica utilizatori. Pastreaza schimbarea parolei, 2FA si protectia ultimului administrator.

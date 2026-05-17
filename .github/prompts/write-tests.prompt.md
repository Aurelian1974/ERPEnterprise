---
mode: agent
description: Generează unit tests și/sau integration tests pentru codul selectat sau specificat.
---

Generează teste pentru codul selectat respectând skill-ul `unit-test` și/sau `integration-test`.

**Determină automat tipul de test necesar:**
- Dacă e logică Domain (entitate, value object, invariante) → **Unit Test** (Domain.Tests)
- Dacă e Application handler → **Unit Test** (Application.Tests) cu repository mockit (NSubstitute)
- Dacă e Repository sau feature complet → **Integration Test** (LocalDB, DbUp fixture)
- Dacă e componentă React → **Vitest unit test**

**Reguli unit tests (C#):**
- Naming: `{Method}_{Condition}_{ExpectedBehavior}`
- Structură: `// Arrange`, `// Act`, `// Assert` vizibil separate
- FluentAssertions: `.Should().Be()`, `.Should().Throw<>()`
- NSubstitute: `Substitute.For<IRepo>()`, `_repo.Received(1).MethodAsync(...)`
- Builder pattern pentru entități complexe
- `[Theory]` + `[InlineData]` pentru scenarii multiple similare
- Testează atât `IsSuccess` cât și `IsFailure` + `Error.Code`

**Reguli integration tests (C#):**
- `IClassFixture<{Module}ModuleFixture>` sau `[Collection]` pentru shared fixture
- SQL Server LocalDB — DbUp rulează migrările + SP-urile
- Repository și handler reale (fără mock)
- Verifică în DB după operație (`GetByIdAsync` după `InsertAsync`)
- Date de test unice per test (Guid în InvoiceNumber etc.)

**Generează pentru fiecare caz:**
1. Scenariul happy path (success)
2. Scenariile de eroare business (failure / exception)
3. Edge cases relevante (null, empty, boundary values)

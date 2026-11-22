using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Explicarea, validarea si desfacerea validarii liniilor de extras pe SQL Server real (02.02, F2 3.4 si 4.4; criteriile F2-A4, A5, A11,
/// A12, A14, A19): partile acopera exact suma, evenimentele in aceeasi tranzactie, datele bancii neschimbate, perioada inchisa refuzata.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BankLineIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    public BankLineIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementReconcile,
            FinancePermissions.BankStatementValidate, FinancePermissions.BankStatementUnvalidate, FinancePermissions.BankStatementAcceptGap]);
    }

    /// <summary>Un extras importat cu liniile date; intoarce liniile, in ordinea lor.</summary>
    private async Task<IReadOnlyList<BankStatementLineDto>> Statement(params TestEntry[] entries)
    {
        var (account, iban) = await NewBank();
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 50_000m, entries, "1"))).Statements);
        return (await Send(new GetBankStatement(imported.BankStatementId))).Lines;
    }

    private async Task<BankLinePartInput> Part(string code, string amount, int? partnerId = null, string? note = null) =>
        new(await OperationType(code), amount, partnerId, null, null, null, null, null, null, null, note);

    private Task<IReadOnlyList<BankLineSaved>> Validate(params (long Id, byte[] RowVersion)[] lines) =>
        Send(new ValidateBankStatementLines(lines.Select(l => new BankLineVersion(l.Id, l.RowVersion)).ToList()));

    [Fact]
    [Trait("Criteriu", "02.02-F2-A5")]
    public async Task A_line_is_validated_only_when_its_parts_cover_exactly_its_amount()
    {
        var line = (await Statement(new TestEntry(Day, -12.40m, Unique("A5"), null, null, "Comision"))).Single();

        var unexplained = await Assert.ThrowsAsync<ErpException>(() => Validate((line.Id, line.RowVersion)));
        Assert.Equal("FIN_BANK_LINE_NOT_EXPLAINED", unexplained.Code);
        var partial = await Assert.ThrowsAsync<ErpException>(async () =>
            await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-COMISION", "-10.00")], line.RowVersion)));
        Assert.Equal("FIN_BANK_LINE_PARTS_MISMATCH", partial.Code);
        var wrongSign = await Assert.ThrowsAsync<ErpException>(async () =>
            await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-DOBANDA-INC", "-12.40")], line.RowVersion)));
        Assert.Equal("FIN_INVALID_REFERENCE", wrongSign.Code);

        var explained = await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-COMISION", "-12.40")], line.RowVersion));
        var detail = await Send(new GetBankStatementLine(line.Id));
        Assert.Equal(BankLineMatchStatus.Explained, detail.MatchStatus);
        Assert.Equal("-12.40", Assert.Single(detail.Parts).Amount);

        var saved = Assert.Single(await Validate((line.Id, explained.RowVersion)));
        Assert.Equal(BankLineMatchStatus.Validated, (await Send(new GetBankStatementLine(line.Id))).MatchStatus);
        Assert.Equal(BankStatementStatus.Closed, (await Send(new GetBankStatement(detail.BankStatementId))).Statement.Status);
        var immutable = await Assert.ThrowsAsync<ErpException>(async () =>
            await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-COMISION", "-12.40")], saved.RowVersion)));
        Assert.Equal("FIN_BANK_LINE_IMMUTABLE", immutable.Code);
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%PlataInregistrata' AND Payload LIKE @Pattern
            """, new { Pattern = $"%\"documentType\":\"Finance.BankStatementLine\",\"documentId\":{line.Id},%" }));
    }

    [Fact]
    public async Task A_card_settlement_is_explained_as_gross_plus_fee_and_published_as_one_receipt_with_two_lines()
    {
        var line = (await Statement(new TestEntry(Day, 9_850m, Unique("CARD"), "Procesator Carduri SA", null, "Decontare POS 01.11"))).Single();
        var explained = await Send(new ExplainBankStatementLine(line.Id,
            [await Part("BANK-DECONTARE-CARD", "10000"), await Part("BANK-COMISION", "-150")], line.RowVersion));
        await Validate((line.Id, explained.RowVersion));

        var payload = await Query<string>("SELECT Payload FROM core.Outbox WHERE EventType LIKE N'%IncasareInregistrata' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"documentId\":{line.Id},%" });
        Assert.Contains("\"treasuryKind\":1", payload, StringComparison.Ordinal);
        Assert.Contains("\"totalAmount\":9850", payload, StringComparison.Ordinal);
        Assert.Contains("\"operationTypeCode\":\"BANK-DECONTARE-CARD\",\"amount\":10000", payload, StringComparison.Ordinal);
        Assert.Contains("\"operationTypeCode\":\"BANK-COMISION\",\"amount\":-150", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parts_must_have_what_their_type_requires_and_no_parts_resets_the_line()
    {
        var line = (await Statement(new TestEntry(Day, 500m, Unique("REQ"), null, null, "?"))).Single();
        foreach (var code in new[] { "BANK-NEIDENTIFICAT", "BANK-TRANSFER-INTERN", "BANK-DEPUNERE-NUMERAR", "INC-CLIENT" })
        {
            var ex = await Assert.ThrowsAsync<ErpException>(async () =>
                await Send(new ExplainBankStatementLine(line.Id, [await Part(code, "500")], line.RowVersion)));
            Assert.Equal("FIN_INVALID_REFERENCE", ex.Code);
        }

        var unknown = await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-NEIDENTIFICAT", "500", note: "De clarificat cu banca")], line.RowVersion));
        var client = await Send(new ExplainBankStatementLine(line.Id, [await Part("INC-CLIENT", "500", Database.LegalPartnerId)], unknown.RowVersion));
        var detail = await Send(new GetBankStatementLine(line.Id));
        Assert.Equal((BankLineMatchStatus.Explained, Database.LegalPartnerId), (detail.MatchStatus, detail.PartnerId));
        await Send(new ExplainBankStatementLine(line.Id, [], client.RowVersion));
        var reset = await Send(new GetBankStatementLine(line.Id));
        Assert.Equal(BankLineMatchStatus.Unmatched, reset.MatchStatus);
        Assert.Empty(reset.Parts);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A19")]
    public async Task A_partner_payment_on_the_statement_cannot_be_validated_without_a_payment_order()
    {
        var line = (await Statement(new TestEntry(Day, -300m, Unique("A19"), "Furnizor Energie SA", null, "OP 77"))).Single();
        var explained = await Send(new ExplainBankStatementLine(line.Id, [await Part("PLATA-FURNIZOR", "-300", Database.LegalPartnerId)], line.RowVersion));

        var ex = await Assert.ThrowsAsync<ErpException>(() => Validate((line.Id, explained.RowVersion)));
        Assert.Equal("FIN_BANK_PARTNER_PAYMENT_WITHOUT_OP", ex.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A11")]
    public async Task Unvalidating_a_line_publishes_the_reversal_and_brings_it_back_to_explained_with_the_bank_data_unchanged()
    {
        var line = (await Statement(new TestEntry(Day, 75.20m, Unique("A11"), "Banca", null, "Dobanda octombrie"))).Single();
        var explained = await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-DOBANDA-INC", "75.20")], line.RowVersion));
        var validated = Assert.Single(await Validate((line.Id, explained.RowVersion)));

        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new UnvalidateBankStatementLine(line.Id, "", validated.RowVersion)));
        Assert.Contains("reason", noReason.Errors.Keys);
        await Send(new UnvalidateBankStatementLine(line.Id, "Dobânda era pentru alt cont.", validated.RowVersion));

        var after = await Send(new GetBankStatementLine(line.Id));
        Assert.Equal(BankLineMatchStatus.Explained, after.MatchStatus);
        Assert.Null(after.ValidatedAt);
        Assert.Equal((line.Amount, line.BankReference, line.CounterpartyName, line.RemittanceInfo, line.BookingDate),
            (after.Amount, after.BankReference, after.CounterpartyName, after.RemittanceInfo, after.BookingDate));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%DocumentTrezorerieStornat' AND Payload LIKE @Pattern AND Payload LIKE N'%Finance.BankStatementLine%'
            """, new { Pattern = $"%\"originalDocumentId\":{line.Id},%" }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Fin.BankStatementLine.Unvalidate' AND EntityId = @Id",
            new { line.Id }));
        var again = await Assert.ThrowsAsync<ErpException>(() => Send(new UnvalidateBankStatementLine(line.Id, "din nou", after.RowVersion)));
        Assert.Equal("FIN_BANK_LINE_NOT_EXPLAINED", again.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A12")]
    public async Task Validating_or_unvalidating_a_line_in_a_closed_period_is_refused()
    {
        var line = (await Statement(new TestEntry(Day, 10m, Unique("A12"), null, null, "Dobanda"))).Single();
        var explained = await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-DOBANDA-INC", "10")], line.RowVersion));
        ClosedDays.Add(Day);
        var closed = await Assert.ThrowsAsync<ErpException>(() => Validate((line.Id, explained.RowVersion)));
        Assert.Equal(CoreErrorCodes.PeriodClosed, closed.Code);

        ClosedDays.Clear();
        var validated = Assert.Single(await Validate((line.Id, explained.RowVersion)));
        ClosedDays.Add(Day);
        var undo = await Assert.ThrowsAsync<ErpException>(() => Send(new UnvalidateBankStatementLine(line.Id, "corectie", validated.RowVersion)));
        Assert.Equal(CoreErrorCodes.PeriodClosed, undo.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A4")]
    public async Task The_lines_after_a_continuity_gap_are_validated_only_after_the_gap_is_accepted()
    {
        var (account, iban) = await NewBank();
        var prefix = Unique("GAP");
        await Import(account, StatementFiles.Camt053(iban, "RON", 100m, [new TestEntry(Day, 1m, $"{prefix}-1")], "1"));
        var gap = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 500m, [new TestEntry(Day.AddDays(3), 2m, $"{prefix}-2")], "2")))
            .Statements);
        var line = (await Send(new GetBankStatement(gap.BankStatementId))).Lines.Single();
        var explained = await Send(new ExplainBankStatementLine(line.Id, [await Part("BANK-DOBANDA-INC", "2")], line.RowVersion));

        var blocked = await Assert.ThrowsAsync<ErpException>(() => Validate((line.Id, explained.RowVersion)));
        Assert.Equal("FIN_BANK_STATEMENT_GAP", blocked.Code);

        var statement = await Send(new GetBankStatement(gap.BankStatementId));
        await Send(new AcceptStatementGap(gap.BankStatementId, "Extrasul intermediar e pe hârtie, arhivat.", statement.Statement.RowVersion));
        Assert.Single(await Validate((line.Id, explained.RowVersion)));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A14")]
    public async Task Validating_500_lines_in_one_command_publishes_500_events_in_the_same_transaction()
    {
        var prefix = Unique("BULK");
        var entries = Enumerable.Range(1, 500).Select(i => new TestEntry(Day, i % 5 == 0 ? -(1m + i / 100m) : 10m + i, $"{prefix}-{i}")).ToArray();
        var lines = await Statement(entries);
        var interest = await OperationType("BANK-DOBANDA-INC");
        var fee = await OperationType("BANK-COMISION");
        var statementId = (await Send(new GetBankStatementLine(lines[0].Id))).BankStatementId;
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            // Explicatia direct in baza (aceeasi forma ca fin.usp_BankStatementLine_Explain), ca testul sa masoare validarea.
            await connection.ExecuteAsync("""
                INSERT INTO fin.BankStatementLinePart (BankStatementLineId, PartNumber, TreasuryOperationTypeId, Amount)
                SELECT Id, 1, CASE WHEN Amount > 0 THEN @Interest ELSE @Fee END, Amount FROM fin.BankStatementLine WHERE BankStatementId = @Statement;
                UPDATE fin.BankStatementLine SET MatchStatus = 3, MatchSource = 6 WHERE BankStatementId = @Statement;
                """, new { Interest = interest, Fee = fee, Statement = statementId });
        }

        var current = (await Send(new GetBankStatement(statementId))).Lines;
        var before = await Query<int>("SELECT COUNT(*) FROM core.Outbox");
        var saved = await Validate(current.Select(l => (l.Id, l.RowVersion)).ToArray());

        Assert.Equal(500, saved.Count);
        Assert.Equal(500, await Query<int>("SELECT COUNT(*) FROM core.Outbox") - before);
        Assert.Equal(100, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%PlataInregistrata' AND Payload LIKE N'%Finance.BankStatementLine%'
              AND JSON_VALUE(Payload, '$.documentId') IN (SELECT CAST(Id AS nvarchar(20)) FROM fin.BankStatementLine WHERE BankStatementId = @Statement)
            """, new { Statement = statementId }));
        Assert.Equal(BankStatementStatus.Closed, (await Send(new GetBankStatement(statementId))).Statement.Status);
    }
}

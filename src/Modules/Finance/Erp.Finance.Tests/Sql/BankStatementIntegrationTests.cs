using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Domain.Treasury;
using Erp.Finance.Tests.Bank;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Importul extraselor pe SQL Server real, pe baza case sensitive (02.02, F2 4.1; criteriile F2-A1–A4 si F2-A16): liniile si referintele
/// salvate, fisierul refuzat a doua oara, liniile suprapuse sarite, soldurile si moneda verificate, continuitatea si acceptarea golului.
/// Fiecare test lucreaza pe contul lui bancar, cu IBAN propriu.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BankStatementIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    public BankStatementIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementAcceptGap,
            FinancePermissions.BankFormatManage]);
    }

    private static TestEntry[] Day1(string prefix) =>
    [
        new(Day, 1250.50m, $"{prefix}-1", "Agro Nord Distribuție SRL", StatementFiles.RomanianIban("BTRL", "0130120245678901"), "FV 1234", "E2E-1"),
        new(Day, -300m, $"{prefix}-2", "Furnizor Energie SA", null, "OP 77"),
        new(Day, -12.40m, null, null, null, "Comision"),
    ];

    [Fact]
    [Trait("Criteriu", "02.02-F2-A1")]
    [Trait("Criteriu", "02.02-F2-A16")]
    public async Task A_camt053_file_is_imported_with_all_lines_balances_and_references()
    {
        var (account, iban) = await NewBank();
        var result = await Import(account, StatementFiles.Camt053(iban, "RON", 10_000m, Day1(Unique("R")), "101"));

        var imported = Assert.Single(result.Statements);
        Assert.Equal((3, 0, ContinuityStatus.Ok), (imported.LineCount, imported.SkippedCount, imported.ContinuityStatus));
        var detail = await Send(new GetBankStatement(imported.BankStatementId));
        Assert.Equal(("10000.00", "10938.10", "101"), (detail.Statement.OpeningBalance, detail.Statement.ClosingBalance, detail.Statement.StatementNo));
        Assert.Equal((StatementSource.Camt053, BankStatementStatus.Imported), (detail.Statement.Source, detail.Statement.Status));
        Assert.Equal(["1250.50", "-300.00", "-12.40"], detail.Lines.Select(l => l.Amount));
        Assert.All(detail.Lines, l => Assert.Equal(BankLineMatchStatus.Unmatched, l.MatchStatus));
        Assert.Equal("E2E-1", detail.Lines[0].EndToEndId);
        Assert.StartsWith("H:", detail.Lines[2].BankReference, StringComparison.Ordinal);
        Assert.Equal("extras.xml", detail.FileName);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%ExtrasBancarImportat' AND Payload LIKE @Pattern",
            new { Pattern = $"%\"bankStatementId\":{imported.BankStatementId},%" }));
        Assert.Equal(Day, DateOnly.FromDateTime(await Query<DateTime>("SELECT LastStatementDate FROM fin.TreasuryAccount WHERE Id = @Id", new { Id = account })));
        Assert.Contains(await Send(new GetBankStatements(account)), s => s.Id == imported.BankStatementId && s.LineCount == 3);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A1")]
    public async Task An_mt940_file_is_imported_into_the_account_found_by_its_iban()
    {
        var (account, iban) = await NewBank();
        var result = await Import(null, StatementFiles.Mt940(iban, "RON", 0m, Day1(Unique("M"))), "extras.sta");

        var imported = Assert.Single(result.Statements);
        Assert.Equal((account, 3), (imported.TreasuryAccountId, imported.LineCount));
        Assert.Equal(StatementSource.Mt940, (await Send(new GetBankStatement(imported.BankStatementId))).Statement.Source);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A2")]
    public async Task The_same_file_is_refused_the_second_time_and_an_overlapping_file_adds_only_the_new_lines()
    {
        var (account, iban) = await NewBank();
        var prefix = Unique("D");
        var file = StatementFiles.Camt053(iban, "RON", 1_000m, Day1(prefix), "1");
        await Import(account, file);

        var again = await Assert.ThrowsAsync<ErpException>(() => Import(account, file, "alt-nume.xml"));
        Assert.Equal("FIN_BANK_FILE_ALREADY_IMPORTED", again.Code);

        // Extrasul pe doua zile repeta liniile primei zile (aceleasi referinte, si cea calculata) si aduce una noua.
        var twoDays = Day1(prefix).Append(new TestEntry(Day.AddDays(1), 500m, $"{prefix}-4", "Client Nou", null, "FV 9")).ToArray();
        var overlap = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 1_000m, twoDays, "2"))).Statements);
        Assert.Equal((1, 3), (overlap.LineCount, overlap.SkippedCount));
        Assert.Equal(4, await Query<int>("SELECT COUNT(*) FROM fin.BankStatementLine WHERE TreasuryAccountId = @Id", new { Id = account }));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A3")]
    public async Task A_statement_whose_balances_do_not_match_its_lines_is_refused_with_51208()
    {
        var (account, iban) = await NewBank();
        var xml = System.Text.Encoding.UTF8.GetString(StatementFiles.Camt053(iban, "RON", 100m, [new TestEntry(Day, 50m, Unique("B"))]))
            .Replace(">150.00</Amt>", ">151.00</Amt>", StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<ErpException>(() => Import(account, System.Text.Encoding.UTF8.GetBytes(xml)));
        Assert.Equal("FIN_BANK_STATEMENT_BALANCE_MISMATCH", ex.Code);
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM fin.BankStatementFile WHERE TreasuryAccountId = @Id", new { Id = account }));
    }

    [Fact]
    public async Task Currency_iban_and_company_are_checked()
    {
        var (account, iban) = await NewBank();
        var euro = await Assert.ThrowsAsync<ErpException>(() => Import(account, StatementFiles.Camt053(iban, "EUR", 0m, [new TestEntry(Day, 5m, Unique("C"))])));
        Assert.Equal("FIN_BANK_STATEMENT_CURRENCY_MISMATCH", euro.Code);

        var (_, otherIban) = await NewBank();
        var wrongIban = await Assert.ThrowsAsync<ErpException>(() => Import(account, StatementFiles.Camt053(otherIban, "RON", 0m, [new TestEntry(Day, 5m, "X")])));
        Assert.Equal("FIN_BANK_ACCOUNT_NOT_FOUND", wrongIban.Code);

        // IBAN-ul unui cont al altei firme nu se cauta acolo: refuz, fara import.
        var (_, foreignIban) = await NewBank(companyId: Database.OtherCompanyId);
        var foreign = await Assert.ThrowsAsync<ErpException>(() => Import(null, StatementFiles.Camt053(foreignIban, "RON", 0m, [new TestEntry(Day, 5m, "Y")])));
        Assert.Equal("FIN_BANK_ACCOUNT_NOT_FOUND", foreign.Code);

        var garbage = await Assert.ThrowsAsync<ErpException>(() => Import(account, "<Document><x/></Document>"u8.ToArray()));
        Assert.Equal("FIN_BANK_FILE_INVALID", garbage.Code);
        var dtd = await Assert.ThrowsAsync<ErpException>(() => Import(account,
            """<?xml version="1.0"?><!DOCTYPE d [<!ENTITY x SYSTEM "file:///etc/hosts">]><d>&x;</d>"""u8.ToArray()));
        Assert.Equal("FIN_BANK_FILE_INVALID", dtd.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A4")]
    public async Task A_missing_statement_marks_a_gap_that_closes_when_it_is_imported_or_is_accepted_with_a_reason()
    {
        var (account, iban) = await NewBank();
        var prefix = Unique("G");
        await Import(account, StatementFiles.Camt053(iban, "RON", 1_000m, [new TestEntry(Day, 100m, $"{prefix}-1")], "1"));
        // Extrasul din 4 noiembrie porneste de la 1.300, dar 2 noiembrie s-a inchis cu 1.100: lipseste extrasul din 3 noiembrie.
        var later = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 1_300m, [new TestEntry(Day.AddDays(2), -50m, $"{prefix}-3")], "3")))
            .Statements);
        Assert.Equal(ContinuityStatus.Gap, later.ContinuityStatus);

        var missing = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 1_100m, [new TestEntry(Day.AddDays(1), 200m, $"{prefix}-2")], "2")))
            .Statements);
        Assert.Equal(ContinuityStatus.Ok, missing.ContinuityStatus);
        Assert.Equal(ContinuityStatus.Ok, (await Send(new GetBankStatement(later.BankStatementId))).Statement.ContinuityStatus);

        // Un gol care nu se mai inchide (extrasul nu mai poate fi obtinut) se accepta cu motiv.
        var gap = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", 9_999m, [new TestEntry(Day.AddDays(5), 1m, $"{prefix}-5")], "5")))
            .Statements);
        var detail = await Send(new GetBankStatement(gap.BankStatementId));
        Assert.Equal((ContinuityStatus.Gap, "1250.00"), (detail.Statement.ContinuityStatus, detail.PreviousClosingBalance));
        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new AcceptStatementGap(gap.BankStatementId, " ", detail.Statement.RowVersion)));
        Assert.Contains("reason", noReason.Errors.Keys);
        await Send(new AcceptStatementGap(gap.BankStatementId, "Banca a corectat soldul; extrasul intermediar nu mai poate fi generat.", detail.Statement.RowVersion));
        var accepted = await Send(new GetBankStatement(gap.BankStatementId));
        Assert.Equal(ContinuityStatus.GapAccepted, accepted.Statement.ContinuityStatus);
        Assert.NotNull(accepted.GapAcceptedAt);
        var twice = await Assert.ThrowsAsync<ErpException>(() => Send(new AcceptStatementGap(gap.BankStatementId, "din nou", accepted.Statement.RowVersion)));
        Assert.Equal("FIN_INVALID_REFERENCE", twice.Code);
        var stale = await Assert.ThrowsAsync<ErpException>(() => Send(new AcceptStatementGap(gap.BankStatementId, "vechi", detail.Statement.RowVersion)));
        Assert.Equal("FIN_CONCURRENCY_CONFLICT", stale.Code);
    }

    [Fact]
    public async Task A_csv_profile_maps_the_bank_columns_and_takes_the_opening_balance_from_the_previous_statement()
    {
        var (account, iban) = await NewBank();
        var invalid = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SaveBankFormatProfile(null, Unique("CSV-"), "Banca CSV", null,
            StatementFormat.Csv, """{"columns":{"bookingDate":"Data"}}""", PaymentFileFormat.None, null, 140, true, null)));
        Assert.Contains("csvMapping", invalid.Errors.Keys);

        var profile = await Send(new SaveBankFormatProfile(null, Unique("CSV-"), "Banca CSV", "rncbrobu", StatementFormat.Csv,
            """{"separator":",","decimalSeparator":".","dateFormat":"yyyy-MM-dd","columns":{"bookingDate":"Date","amount":"Amount","bankReference":"Ref","remittanceInfo":"Details"}}""",
            PaymentFileFormat.None, null, 140, true, null));
        var prefix = Unique("V");
        await Import(account, StatementFiles.Camt053(iban, "RON", 0m, [new TestEntry(Day, 700m, $"{prefix}-0")], "1"));
        await Send(new SetTreasuryAccountBankFormat(account, profile.Id, null));
        Assert.Contains(await Send(new GetBankFormatProfiles()), p => p.Id == profile.Id && p.BankBic == "RNCBROBU" && p.AccountCount == 1);

        // Cu profilul CSV, un CAMT.053 nu se mai citeste.
        Assert.Equal("FIN_BANK_FILE_INVALID", (await Assert.ThrowsAsync<ErpException>(() => Import(account,
            StatementFiles.Camt053(iban, "RON", 700m, [new TestEntry(Day.AddDays(1), 1m, $"{prefix}-x")], "2")))).Code);

        var imported = Assert.Single((await Import(account, System.Text.Encoding.UTF8.GetBytes(
            $"Date,Amount,Ref,Details\n2026-11-03,-200.00,{prefix}-1,\"Chirie, noiembrie\"\n2026-11-03,50.25,{prefix}-2,Dobanda\n"), "extras.csv")).Statements);
        var detail = await Send(new GetBankStatement(imported.BankStatementId));
        Assert.Equal(("700.00", "550.25", ContinuityStatus.Ok), (detail.Statement.OpeningBalance, detail.Statement.ClosingBalance, detail.Statement.ContinuityStatus));
        Assert.Equal("Chirie, noiembrie", detail.Lines[0].RemittanceInfo);
    }

    [Fact]
    public async Task Shipped_profiles_keep_their_formats_and_bank_operation_types_are_shipped()
    {
        var shipped = (await Send(new GetBankFormatProfiles())).Single(p => p.Code == "CAMT053-STD");
        var changed = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveBankFormatProfile(shipped.Id, shipped.Code, shipped.Name, null, StatementFormat.Mt940,
            null, shipped.PaymentFormat, null, 140, true, shipped.RowVersion)));
        Assert.Equal("FIN_INVALID_REFERENCE", changed.Code);
        await Send(new SaveBankFormatProfile(shipped.Id, shipped.Code, "CAMT.053 standard", "BTRLRO22", shipped.StatementFormat, null, shipped.PaymentFormat,
            null, 140, true, shipped.RowVersion));

        Permissions.Add(FinancePermissions.TreasuryConfigure);
        var types = await Send(new GetOperationTypes());
        Assert.Contains(types, t => t.Code == "BANK-TRANSFER-INTERN" && t.Direction == CashDirection.Both && t.AppliesToBank && !t.AppliesToCash);
        Assert.Contains(types, t => t.Code == "INC-CLIENT" && t.AppliesToBank && t.AppliesToCash);
        var both = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SaveOperationType(null, Unique("X-"), "Ambele la casa", CashDirection.Both,
            false, false, false, 0, null, null, true, false, true, null)));
        Assert.Contains("direction", both.Errors.Keys);
    }
}

using System.Diagnostics;
using Dapper;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Potrivirea automata pe SQL Server real (02.02, F2 4.3, 4.5, 4.6; criteriile F2-A6 automat, A9, A10, A13): OP-urile dupa EndToEndId sau
/// IBAN + suma, depunerile de numerar din casa, viramentele interne, decontarile de card cu comision, regulile clientului, partenerul
/// identificat si propunerile, validarea automata dupa nivelul setat.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BankMatchingIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    public BankMatchingIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankStatementImport, FinancePermissions.BankStatementReconcile,
            FinancePermissions.BankStatementValidate, FinancePermissions.BankRuleManage, FinancePermissions.BankFormatManage,
            FinancePermissions.PaymentOrderCreate, FinancePermissions.PaymentOrderValidate]);
    }

    private async Task<IReadOnlyList<BankStatementLineDto>> ImportAndMatch(int account, string iban, decimal opening, params TestEntry[] entries)
    {
        var imported = Assert.Single((await Import(account, StatementFiles.Camt053(iban, "RON", opening, entries, Unique("S")))).Statements);
        await Send(new RunAutoMatching(imported.BankStatementId));
        return (await Send(new GetBankStatement(imported.BankStatementId))).Lines;
    }

    private async Task<(int PartnerId, int BankAccountId, string Iban)> Partner(string? fiscalCode = null)
    {
        var iban = StatementFiles.RomanianIban("BTRL", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var partner = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Partener Potrivire SRL', 1, @FiscalCode, 1, 1, 1, N'Str. Lungă 3', N'Iași', N'IS', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("PM-"), FiscalCode = fiscalCode ?? Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var account = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
            VALUES (@Partner, @Iban, N'Banca Transilvania', 'RON', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Partner = partner, Iban = iban });
        return (partner, account, iban);
    }

    private async Task<PaymentOrderDetailDto> ValidatedOrder(int account, int partner, int bankAccount, string amount)
    {
        var draft = await Send(new SavePaymentOrder(null, account, Day, Day, false, PaymentPurpose.Suppliers, partner, null, bankAccount, null, null, null, null,
            null, null, [new PaymentOrderLineInput(await OperationType("PLATA-FURNIZOR"), amount, "FA-9", null, null)], null));
        await Send(new ValidatePaymentOrder(draft.Id, draft.RowVersion));
        return await Send(new GetPaymentOrder(draft.Id));
    }

    private Task<int> Events(string type, string pattern) =>
        Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE @Type AND Payload LIKE @Pattern", new { Type = "%" + type, Pattern = pattern });

    [Fact]
    [Trait("Criteriu", "02.02-F2-A6")]
    public async Task Orders_are_matched_by_end_to_end_id_or_by_iban_and_amount_and_confirmed_automatically()
    {
        var (account, iban) = await NewBank();
        var (partner, bankAccount, partnerIban) = await Partner();
        var byId = await ValidatedOrder(account, partner, bankAccount, "1000");
        var byIban = await ValidatedOrder(account, partner, bankAccount, "777.70");

        var lines = await ImportAndMatch(account, iban, 10_000m,
            new TestEntry(Day.AddDays(1), -1000m, Unique("E"), "Partener", partnerIban, "plata", byId.EndToEndId),
            new TestEntry(Day.AddDays(3), -777.70m, Unique("I"), "Partener", partnerIban, "plata fara referinta"));

        Assert.All(lines, l => Assert.Equal(BankLineMatchStatus.Validated, l.MatchStatus));
        Assert.Equal(PaymentConfirmationStatus.Confirmed, (await Send(new GetPaymentOrder(byId.Id))).ConfirmationStatus);
        Assert.Equal(PaymentConfirmationStatus.Confirmed, (await Send(new GetPaymentOrder(byIban.Id))).ConfirmationStatus);
        Assert.Equal(1, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.PaymentOrder\",\"documentId\":{byId.Id},%"));
        Assert.Equal(0, await Events("PlataInregistrata", $"%\"documentType\":\"Finance.BankStatementLine\",\"documentId\":{lines[0].Id},%"));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A9")]
    public async Task A_cash_deposit_from_the_cash_desk_and_its_statement_line_are_matched_and_leave_transit()
    {
        var (account, iban) = await NewBank();
        var cash = (await Send(new Application.Treasury.SaveTreasuryAccount(null, Contracts.Treasury.TreasuryAccountKind.Cash, Unique("CASA-"), "Casa depuneri",
            Database.HeadOfficeId, "RON", null, null, Database.CashierId, null, null, null, null, Erp.Finance.Domain.Treasury.CashCloseMode.OnDemand, null, true,
            [new Application.Treasury.TreasuryCashierInput(Database.CashierId, true, null, null)], null))).Id;
        int cashDocument;
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            // Dispozitia de plata validata din casa (F1): numerar depus la banca, catre acest cont.
            cashDocument = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO fin.CashDocument (CompanyId, TreasuryAccountId, Kind, DocumentDate, Number, Status, CurrencyCode, ExchangeRate, RateDate, TotalAmount,
                    TotalAmountRon, ValidatedAt, ValidatedBy, CreatedBy)
                VALUES (@CompanyId, @Cash, 3, @Day, @Number, 2, 'RON', 1, @Day, 4200, 4200, SYSUTCDATETIME(), 1, 1);
                DECLARE @Id int = SCOPE_IDENTITY();
                INSERT INTO fin.CashDocumentLine (CashDocumentId, LineNumber, TreasuryOperationTypeId, Amount, AmountRon, CounterTreasuryAccountId)
                SELECT @Id, 1, Id, 4200, 4200, @Bank FROM fin.TreasuryOperationType WHERE Code = N'PLATA-DEPUNERE-BANCA';
                SELECT @Id;
                """, new { Database.CompanyId, Cash = cash, Day = Day.ToDateTime(TimeOnly.MinValue), Number = Unique("DP-"), Bank = account });
        }

        Assert.Contains(await Send(new GetTransitItems(account)), t => t.Kind == TransitKind.Cash && t.SourceId == cashDocument && t.Amount == "4200.00");
        var line = (await ImportAndMatch(account, iban, 0m, new TestEntry(Day.AddDays(1), 4200m, Unique("DEP"), null, null, "Depunere numerar"))).Single();

        Assert.Equal(BankLineMatchStatus.Validated, line.MatchStatus);
        var detail = await Send(new GetBankStatementLine(line.Id));
        Assert.Equal(("BANK-DEPUNERE-NUMERAR", cashDocument), (Assert.Single(detail.Parts).OperationTypeCode, detail.Parts[0].CashDocumentId));
        Assert.DoesNotContain(await Send(new GetTransitItems(account)), t => t.Kind == TransitKind.Cash && t.SourceId == cashDocument);
    }

    [Fact]
    public async Task An_internal_transfer_is_paired_with_the_line_of_the_other_account_and_leaves_transit()
    {
        Permissions.Add(FinancePermissions.BankStatementValidate);
        var (from, fromIban) = await NewBank();
        var (to, toIban) = await NewBank();
        var outgoing = (await ImportAndMatch(from, fromIban, 5_000m, new TestEntry(Day, -1500m, Unique("TO"), null, toIban, "Alimentare cont"))).Single();
        var explained = await Send(new ExplainBankStatementLine(outgoing.Id,
            [new BankLinePartInput(await OperationType("BANK-TRANSFER-INTERN"), "-1500", null, null, null, null, to, null, null, null, null)], outgoing.RowVersion));
        await Send(new ValidateBankStatementLines([new BankLineVersion(outgoing.Id, explained.RowVersion)]));
        Assert.Contains(await Send(new GetTransitItems(to)), t => t.Kind == TransitKind.InternalTransfer && t.SourceId == outgoing.Id);

        var incoming = (await ImportAndMatch(to, toIban, 0m, new TestEntry(Day.AddDays(1), 1500m, Unique("TI"), null, fromIban, "Alimentare cont"))).Single();

        Assert.Equal(BankLineMatchStatus.Validated, incoming.MatchStatus);
        var part = Assert.Single((await Send(new GetBankStatementLine(incoming.Id))).Parts);
        Assert.Equal(("BANK-TRANSFER-INTERN", from), (part.OperationTypeCode, part.CounterTreasuryAccountId));
        Assert.DoesNotContain(await Send(new GetTransitItems(to)), t => t.Kind == TransitKind.InternalTransfer && t.SourceId == outgoing.Id);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A10")]
    public async Task A_card_settlement_with_a_fee_within_tolerance_is_explained_as_gross_plus_fee()
    {
        var (account, iban) = await NewBank();
        var acquirerIban = StatementFiles.RomanianIban("INGB", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        var code = Unique("ACQ");
        await Send(new SaveCardAcquirer(null, code, "Procesator Carduri SA", acquirerIban, true, null));
        var settlement = await Send(new RegisterCardSettlement(code, Day, "10000", Unique("ONLINE-")));
        var far = await Send(new RegisterCardSettlement(code, Day, "1000", Unique("ONLINE-")));

        var lines = await ImportAndMatch(account, iban, 0m,
            new TestEntry(Day.AddDays(2), 9850m, Unique("C1"), "Procesator Carduri SA", acquirerIban, "Decontare"),
            new TestEntry(Day.AddDays(2), 800m, Unique("C2"), "Procesator Carduri SA", acquirerIban, "Decontare cu retur"));

        Assert.Equal(BankLineMatchStatus.Validated, lines[0].MatchStatus);
        var parts = (await Send(new GetBankStatementLine(lines[0].Id))).Parts;
        Assert.Equal([("BANK-DECONTARE-CARD", "10000.00"), ("BANK-COMISION", "-150.00")], parts.Select(p => (p.OperationTypeCode, p.Amount)));
        var settled = (await Send(new GetCardSettlements())).Single(c => c.Id == settlement);
        Assert.Equal((CardSettlementStatus.Settled, "9850.00", "150.00"), (settled.Status, settled.SettledAmount, settled.FeeAmount));
        // 800 din 1.000 = comision de 20%, peste toleranta: linia ramane de explicat, decontarea in asteptare.
        Assert.NotEqual(BankLineMatchStatus.Validated, lines[1].MatchStatus);
        Assert.Equal(CardSettlementStatus.Pending, (await Send(new GetCardSettlements())).Single(c => c.Id == far).Status);
        Assert.Equal(settlement, await Send(new RegisterCardSettlement(code, Day, "10000", (await Send(new GetCardSettlements())).Single(c => c.Id == settlement).SourceRef)));
    }

    [Fact]
    public async Task Client_rules_explain_lines_case_and_accent_insensitively_and_validate_only_when_asked()
    {
        var (account, iban) = await NewBank();
        await Send(new SaveMatchingRule(null, 10, "Comisioane", account, 2, null, null, "comision administrare", null, null, null,
            await OperationType("BANK-COMISION"), null, null, null, null, true, true, null));
        await Send(new SaveMatchingRule(null, 20, "Dobânzi", account, 1, null, null, "dobanda", null, null, "1000", await OperationType("BANK-DOBANDA-INC"),
            null, null, null, null, false, true, null));
        var noCondition = await Assert.ThrowsAsync<Erp.Core.Contracts.ErpValidationException>(async () => await Send(new SaveMatchingRule(null, 1, "Fără", null,
            null, null, null, null, null, null, null, await OperationType("BANK-COMISION"), null, null, null, null, false, true, null)));
        Assert.Contains("counterpartyIban", noCondition.Errors.Keys);

        var lines = await ImportAndMatch(account, iban, 1_000m,
            new TestEntry(Day, -12.40m, Unique("R1"), null, null, "COMISION ADMINISTRARE CONT"),
            new TestEntry(Day, 3.25m, Unique("R2"), null, null, "Dobândă lunară"),
            new TestEntry(Day, 5_000m, Unique("R3"), null, null, "Dobanda depozit"));

        Assert.Equal(BankLineMatchStatus.Validated, lines[0].MatchStatus);
        Assert.Equal(BankLineMatchStatus.Explained, lines[1].MatchStatus);
        Assert.Equal(BankLineMatchStatus.Unmatched, lines[2].MatchStatus);
        Assert.Contains(await Send(new GetMatchingRules()), r => r.Name == "Comisioane" && r.UsageCount == 1);

        // Reordonarea prin tragere: „Dobânzi” trece prima; prioritatile devin 10, 20.
        var rules = (await Send(new GetMatchingRules())).Where(r => r.TreasuryAccountId == account).ToList();
        var interestRule = rules.Single(r => r.Name == "Dobânzi").Id;
        var feeRule = rules.Single(r => r.Name == "Comisioane").Id;
        await Send(new ReorderMatchingRules([interestRule, feeRule]));
        var reordered = (await Send(new GetMatchingRules())).Where(r => r.TreasuryAccountId == account).ToDictionary(r => r.Id, r => r.Priority);
        Assert.Equal((10, 20), (reordered[interestRule], reordered[feeRule]));
        var foreign = await Assert.ThrowsAsync<Erp.Core.Contracts.ErpException>(() => Send(new ReorderMatchingRules([interestRule]), companyId: Database.OtherCompanyId));
        Assert.Equal("FIN_INVALID_REFERENCE", foreign.Code);
        Assert.Contains((await Send(new GetBankLookups())).OperationTypes, t => t.Code == "BANK-COMISION");

        // „Tine minte”: regula din explicatia manuala a liniei, dupa textul dat.
        var explained = await Send(new ExplainBankStatementLine(lines[2].Id,
            [new BankLinePartInput(await OperationType("BANK-DOBANDA-INC"), "5000", null, null, null, null, null, null, null, null, null)], lines[2].RowVersion));
        var remembered = await Send(new RememberMatchingRule(lines[2].Id, "depozit"));
        Assert.Contains(await Send(new GetMatchingRules()), r => r.Id == remembered.Id && r.CreatedFromLineId == lines[2].Id && r.RemittanceContains == "depozit");
        _ = explained;
    }

    [Fact]
    public async Task Testing_a_rule_on_the_last_30_days_reports_the_lines_it_would_explain_without_changing_them()
    {
        var (account, iban) = await NewBank();
        var fee = await OperationType("BANK-COMISION");
        var saved = await Send(new SaveMatchingRule(null, 10, "Comisioane test", account, 2, null, null, "comision", null, null, null, fee, null, null, null, null,
            false, true, null));
        var today = Application.Treasury.BusinessDay.Today(TimeProvider.System);
        var lines = await ImportAndMatch(account, iban, 1_000m,
            new TestEntry(today.AddDays(-40), -7m, Unique("T0"), null, null, "Comision SEPA vechi"),
            new TestEntry(today.AddDays(-2), -5m, Unique("T1"), null, null, "COMISIÓN lunar"),
            new TestEntry(today.AddDays(-1), -3m, Unique("T2"), null, null, "taxa"));
        Assert.Equal(BankLineMatchStatus.Explained, lines[1].MatchStatus);

        TestMatchingRule Candidate(int? id, int priority) =>
            new(id, priority, account, 2, null, null, "comision", null, null, null, fee, null, null);

        // O regula noua, inaintea celei salvate: prinde doar linia din ultimele 30 de zile, deja explicata de cealalta regula.
        var first = await Send(Candidate(null, 5));
        Assert.Equal((1, 1, 0), (first.Lines, first.AlreadyExplained, first.TakenByOtherRule));
        Assert.Equal(("RON", "0.00", "5.00"), (first.Totals.Single().CurrencyCode, first.Totals.Single().TotalIn, first.Totals.Single().TotalOut));
        var item = Assert.Single(first.Items);
        Assert.Equal((lines[1].Id, "BANK-COMISION", "Comisioane test", (string?)null), (item.Id, item.ExplainedAs, item.ExplainedByRuleName, item.TakenByRuleName));

        // Dupa regula salvata: linia o prinde aceea.
        var after = await Send(Candidate(null, 50));
        Assert.Equal((1, "Comisioane test"), (after.TakenByOtherRule, after.Items.Single().TakenByRuleName));

        // Testul regulii salvate (versiunea din fisa): liniile explicate chiar de ea nu sunt „explicate altfel”.
        var itself = await Send(Candidate(saved.Id, 10));
        Assert.Equal((1, 0, 0), (itself.Lines, itself.AlreadyExplained, itself.TakenByOtherRule));

        // Nimic nu s-a schimbat pe linie.
        var line = await Send(new GetBankStatementLine(lines[1].Id));
        Assert.Equal((Convert.ToBase64String(lines[1].RowVersion), BankLineMatchStatus.Explained), (Convert.ToBase64String(line.RowVersion), line.MatchStatus));
        await Assert.ThrowsAsync<Erp.Core.Contracts.ErpValidationException>(async () =>
            await Send(new TestMatchingRule(null, 5, account, 2, null, null, null, null, null, null, fee, null, null)));
    }

    [Fact]
    public async Task The_partner_is_identified_by_iban_or_fiscal_code_and_partial_payments_become_proposals()
    {
        var (account, iban) = await NewBank();
        var (partner, bankAccount, partnerIban) = await Partner();
        var (byCode, _, _) = await Partner("18547290");
        var order = await ValidatedOrder(account, partner, bankAccount, "3000");

        var lines = await ImportAndMatch(account, iban, 10_000m,
            new TestEntry(Day, -1000m, Unique("P1"), "Partener", partnerIban, "avans partial"),
            new TestEntry(Day, 250m, Unique("P2"), "Client", null, "Plata fact 77 CIF RO18547290"));

        Assert.Equal((BankLineMatchStatus.Proposed, partner), (lines[0].MatchStatus, lines[0].PartnerId));
        Assert.Equal((BankLineMatchStatus.Unmatched, byCode), (lines[1].MatchStatus, lines[1].PartnerId));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM fin.MatchSuggestion WHERE BankStatementLineId = @Id AND PaymentOrderId = @Order",
            new { lines[0].Id, Order = order.Id }));
        Assert.Equal(order.Id, Assert.Single((await Send(new GetBankStatementLine(lines[0].Id))).Suggestions).PaymentOrderId);

        // Ecranul Extras de cont: liniile nevalidate, OP-ul de confirmat si soldul scriptic = soldul extrasului − OP-urile neconfirmate.
        Assert.Contains(await Send(new GetBankAccounts()), a => a.Id == account && a.Kind == 1);
        var screen = await Send(new GetBankReconciliation(account));
        Assert.Equal(2, screen.Lines.Count);
        Assert.Equal(order.Id, Assert.Single(screen.UnconfirmedOrders).Id);
        Assert.Equal(("9250.00", "-3000.00", "6250.00"), (screen.StatementBalance, screen.UnconfirmedAmount, screen.BookBalance));
    }

    [Fact]
    public async Task With_auto_validation_off_matched_lines_stay_explained()
    {
        var (account, iban) = await NewBank();
        await Send(new SaveMatchingRule(null, 10, "Comisioane", account, 2, null, null, "comision", null, null, null, await OperationType("BANK-COMISION"), null,
            null, null, null, true, true, null));
        await using (var connection = new SqlConnection(Database.ConnectionString))
        {
            await connection.ExecuteAsync("INSERT INTO fin.CompanySetting (CompanyId, Code, Value, ModifiedBy) VALUES (@Company, 'Fin.Bank.AutoValidateLevel', N'0', 1)",
                new { Company = Database.CompanyId });
        }

        try
        {
            var line = (await ImportAndMatch(account, iban, 100m, new TestEntry(Day, -2m, Unique("L0"), null, null, "comision"))).Single();
            Assert.Equal(BankLineMatchStatus.Explained, line.MatchStatus);
        }
        finally
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.ExecuteAsync("DELETE FROM fin.CompanySetting WHERE CompanyId = @Company", new { Company = Database.CompanyId });
        }
    }

    [Fact]
    public async Task The_card_fee_tolerance_is_configurable_per_company_and_drives_the_matching()
    {
        Permissions.Add(FinancePermissions.TreasuryConfigure);
        var defaults = await Send(new GetFinanceSettings());
        Assert.Equal((true, 2, "3.00", 2), (defaults.RequireApproval, defaults.AutoValidateLevel, defaults.CardFeeTolerancePercent, defaults.CardSettlementDays));
        var invalid = await Assert.ThrowsAsync<Erp.Core.Contracts.ErpValidationException>(() => Send(new SaveFinanceSettings(true, 2, "25", 2)));
        Assert.Contains("cardFeeTolerancePercent", invalid.Errors.Keys);

        try
        {
            Assert.Equal("1.00", (await Send(new SaveFinanceSettings(true, 2, "1", 2))).CardFeeTolerancePercent);
            Assert.Equal("1.00", (await Send(new GetFinanceSettings())).CardFeeTolerancePercent);

            var (account, iban) = await NewBank();
            var acquirerIban = StatementFiles.RomanianIban("INGB", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
            var code = Unique("ACQ");
            await Send(new SaveCardAcquirer(null, code, "Procesator", acquirerIban, true, null));
            await Send(new RegisterCardSettlement(code, Day, "10000", Unique("ONLINE-")));

            // Comision de 1,5% peste toleranta de 1%: linia nu se potriveste automat.
            var line = (await ImportAndMatch(account, iban, 0m, new TestEntry(Day.AddDays(2), 9850m, Unique("TOL"), "Procesator", acquirerIban, "Decontare"))).Single();
            Assert.Equal(BankLineMatchStatus.Unmatched, line.MatchStatus);
        }
        finally
        {
            await using var connection = new SqlConnection(Database.ConnectionString);
            await connection.ExecuteAsync("DELETE FROM fin.CompanySetting WHERE CompanyId = @Company", new { Company = Database.CompanyId });
        }
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-A13")]
    public async Task Importing_and_matching_a_statement_of_10000_lines_takes_under_60_seconds()
    {
        var (account, iban) = await NewBank();
        await Send(new SaveMatchingRule(null, 10, "Comisioane", account, 2, null, null, "comision", null, null, null, await OperationType("BANK-COMISION"), null,
            null, null, null, true, true, null));
        var prefix = Unique("BIG");
        var random = new Random(13);
        var entries = Enumerable.Range(1, 10_000).Select(i => i % 4 == 0
            ? new TestEntry(Day.AddDays(i % 20), -(1m + i % 7), $"{prefix}-{i}", null, null, "Comision operatiune")
            : new TestEntry(Day.AddDays(i % 20), Math.Round((decimal)random.Next(100, 900_000) / 100m, 2), $"{prefix}-{i}", $"Client {i % 300}",
                null, $"Plata fact {i}")).ToArray();
        var file = StatementFiles.Camt053(iban, "RON", 0m, entries, "1");

        var watch = Stopwatch.StartNew();
        var imported = Assert.Single((await Import(account, file)).Statements);
        var result = await Send(new RunAutoMatching(imported.BankStatementId));
        watch.Stop();

        Assert.Equal(10_000, imported.LineCount);
        Assert.Equal((10_000, 2_500, 2_500), (result.Considered, result.Explained, result.AutoValidated));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"Importul și potrivirea au durat {watch.Elapsed.TotalSeconds:N1} s.");
    }
}

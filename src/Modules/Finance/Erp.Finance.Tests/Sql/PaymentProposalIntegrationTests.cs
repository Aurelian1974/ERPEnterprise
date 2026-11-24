using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Planning;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Tests.Bank;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>Propunerile de plata (02.02, F5 3.1–3.3, 4.1–4.3): construirea cu excluderi si bani disponibili, aprobarea pe trei niveluri, generarea OP-urilor.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PaymentProposalIntegrationTests : BankTestBase, IClassFixture<FinanceDatabase>
{
    private static long invoiceSequence = DateTime.UtcNow.Ticks % 1_000_000_000;

    public PaymentProposalIntegrationTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.PaymentProposalView, FinancePermissions.PaymentProposalCreate, FinancePermissions.PaymentProposalApprove,
            FinancePermissions.PaymentProposalGenerateOrders, FinancePermissions.PaymentApprovalRuleManage, FinancePermissions.BankView,
            FinancePermissions.BankStatementImport, FinancePermissions.PaymentOrderCreate, FinancePermissions.PaymentOrderValidate]);
    }

    private int Company => Database.PlanningCompanyId;

    private Task<T> As<T>(int user, MediatR.IRequest<T> request) => Send(request, user, Company);

    [Fact]
    [Trait("Criteriu", "02.02-F5-A1")]
    [Trait("Criteriu", "02.02-F5-A2")]
    [Trait("Criteriu", "02.02-F5-A3")]
    [Trait("Criteriu", "02.02-F5-A4")]
    [Trait("Criteriu", "02.02-F5-A5")]
    [Trait("Criteriu", "02.02-F5-A6")]
    [Trait("Criteriu", "02.02-F5-A7")]
    [Trait("Criteriu", "02.02-F5-A8")]
    public async Task A_proposal_excludes_blocked_invoices_needs_three_ordered_approvers_and_generates_validated_orders()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (account, iban) = await NewBank("RON", Company);
        await Import(account, StatementFiles.Camt053(iban, "RON", 250_000m, [new TestEntry(today.AddDays(-1), 0.01m, Unique("SOLD"))], "1"), companyId: Company);
        var (author, chief, financial, general, stranger) = (await NewUser("autor"), await NewUser("sef", "CONTABIL_SEF"), await NewUser("df", "DIRECTOR_FINANCIAR"),
            await NewUser("dg", "DIRECTOR_GENERAL"), await NewUser("fara-rol"));
        await As(author, new SavePaymentApprovalRules([new ApprovalRuleInput(1, "0", 1, await RoleId("CONTABIL_SEF")),
            new ApprovalRuleInput(2, "50000", 1, await RoleId("DIRECTOR_FINANCIAR")), new ApprovalRuleInput(3, "250000", 1, await RoleId("DIRECTOR_GENERAL"))]));

        var supplier = await Supplier();
        var big = await Invoice(supplier, 200_000m, today.AddDays(-5));
        var second = await Invoice(supplier, 100_000m, today.AddDays(-3));
        var disputed = await Invoice(supplier, 5_000m, today.AddDays(-2));
        var blocked = await Invoice(supplier, 10_000m, today.AddDays(-2));
        await Execute("UPDATE fin.OpenItem SET IsDisputed = 1 WHERE Id = @Id; UPDATE fin.OpenItem SET IsBlockedForPayment = 1 WHERE Id = @Blocked",
            new { Id = disputed, Blocked = blocked });

        // A3: cu bani disponibili (250.000), prima factura intra, a doua ramane respinsa „fonduri insuficiente”.
        var limited = await As(author, new BuildPaymentProposal("Limitat", today, today, account, [supplier], LimitToAvailableFunds: true));
        var limitedDetail = await As(author, new GetPaymentProposal(limited.Id));
        Assert.Equal([(big, ProposalLineStatus.Proposed), (second, ProposalLineStatus.Rejected)],
            limitedDetail.Lines.Select(l => (l.OpenItemId, l.LineStatus)));
        Assert.Equal("Fonduri insuficiente", limitedDetail.Lines[1].RejectReason);
        await As(author, new DeletePaymentProposal(limited.Id, limited.RowVersion));

        // A1: litigiul si blocarea se exclud; A2: o factura nu intra in doua propuneri active.
        var proposal = await As(author, new BuildPaymentProposal("Plati luna", today, today, account, [supplier]));
        var detail = await As(author, new GetPaymentProposal(proposal.Id));
        Assert.Equal([big, second], detail.Lines.Select(l => l.OpenItemId));
        Assert.Empty((await As(author, new GetPaymentProposal((await As(author, new BuildPaymentProposal(null, today, today, account, [supplier]))).Id))).Lines);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InTransaction(sp => sp.GetRequiredService<IPaymentProposalStore>().SaveAsync(Company, null, null,
            today, today, today, account, [new ProposalLineRecord(1, big, supplier, today, "RON", 200_000m, 1m, 1m, account, 3, [], ProposalLineStatus.Proposed, null)],
            null, author, Ct), Company));
        Assert.Equal(51707, duplicate.Number);

        // A4: 300.000 lei cer trei niveluri, in ordine, de trei utilizatori diferiti, niciunul autorul.
        var submitted = await As(author, new SubmitPaymentProposal(proposal.Id, detail.RowVersion));
        Assert.Equal(3, (await As(author, new GetPaymentProposal(proposal.Id))).RequiredApprovalLevel);
        Assert.Equal("FIN_PROPOSAL_APPROVER_NOT_ALLOWED", (await Refused(() => As(financial, Decide(proposal.Id, submitted.RowVersion)))).Code);
        var first = await As(chief, Decide(proposal.Id, submitted.RowVersion));
        Assert.Equal("FIN_PROPOSAL_SAME_APPROVER_TWICE", (await Refused(() => As(chief, Decide(proposal.Id, first.RowVersion)))).Code);
        var secondLevel = await As(financial, Decide(proposal.Id, first.RowVersion));
        Assert.Equal("FIN_PROPOSAL_SELF_APPROVAL", (await Refused(() => As(author, Decide(proposal.Id, secondLevel.RowVersion)))).Code);
        Assert.Equal("FIN_PROPOSAL_APPROVER_NOT_ALLOWED", (await Refused(() => As(stranger, Decide(proposal.Id, secondLevel.RowVersion)))).Code);
        var approved = await As(general, Decide(proposal.Id, secondLevel.RowVersion));
        Assert.Equal(ProposalStatus.Approved, (await As(author, new GetPaymentProposal(proposal.Id))).Status);

        // A5: retragerea anuleaza aprobarile; retrimiterea porneste de la nivelul 1 (sefului i se cere din nou).
        var withdrawn = await As(author, new WithdrawPaymentProposal(proposal.Id, "Schimbare de data", approved.RowVersion));
        var resubmitted = await As(author, new SubmitPaymentProposal(proposal.Id, withdrawn.RowVersion));
        var again = await As(author, new GetPaymentProposal(proposal.Id));
        Assert.Equal((ProposalStatus.InApproval, 0, 2), (again.Status, again.CurrentApprovalLevel, again.SubmissionNo));
        Assert.Equal("FIN_PROPOSAL_APPROVER_NOT_ALLOWED", (await Refused(() => As(general, Decide(proposal.Id, resubmitted.RowVersion)))).Code);
        var v1 = await As(chief, Decide(proposal.Id, resubmitted.RowVersion));
        var v2 = await As(financial, Decide(proposal.Id, v1.RowVersion));
        await As(general, Decide(proposal.Id, v2.RowVersion));

        // A7: a doua factura e platita intre aprobare si generare; A6, A8: un OP validat pentru furnizor, factura stinsa, aprobat de ultimul aprobator.
        await InTransaction(sp => sp.GetRequiredService<ISettlementService>().RegisterPaymentAsync(new PaymentSettlementRequest(Company, supplier, PartnerRole.Supplier,
            "Finance.CashDocument", Interlocked.Increment(ref invoiceSequence), "", "PLATA-MANUALA", today, "RON", 1m, 100_000m, 100_000m, false,
            [new SettlementTarget(second, null)], false), Ct), Company);
        var generated = await As(chief, new GeneratePaymentOrders(proposal.Id));
        var order = Assert.Single(generated.PaymentOrderIds);
        Assert.Contains(generated.Skipped, s => s.Contains("plătită", StringComparison.Ordinal));
        Assert.True(generated.ApprovalSatisfied);
        Assert.Equal(((byte)2, (byte)3, general, proposal.Id), await Query<(byte, byte, int, int)>(
            "SELECT Status, Origin, ApprovedBy, SourceProposalId FROM fin.PaymentOrder WHERE Id = @Id", new { Id = order }));
        Assert.Equal(0m, await Query<decimal>("SELECT OpenAmount FROM fin.OpenItem WHERE Id = @Id", new { Id = big }));
        var final = await As(author, new GetPaymentProposal(proposal.Id));
        Assert.Equal((ProposalStatus.Generated, ProposalLineStatus.Generated, ProposalLineStatus.Skipped),
            (final.Status, final.Lines[0].LineStatus, final.Lines[1].LineStatus));

        // A8: cand ultimul aprobator genereaza el insusi, OP-ul ramane de aprobat in F2.
        var small = await Invoice(supplier, 1_000m, today.AddDays(-1));
        var own = await As(author, new BuildPaymentProposal(null, today, today, account, [supplier]));
        var ownSubmitted = await As(author, new SubmitPaymentProposal(own.Id, (await As(author, new GetPaymentProposal(own.Id))).RowVersion));
        await As(chief, Decide(own.Id, ownSubmitted.RowVersion));
        var ownOrders = await As(chief, new GeneratePaymentOrders(own.Id));
        Assert.False(ownOrders.ApprovalSatisfied);
        Assert.Null(await Query<int?>("SELECT ApprovedBy FROM fin.PaymentOrder WHERE Id = @Id", new { Id = Assert.Single(ownOrders.PaymentOrderIds) }));
        _ = small;
    }

    private static DecidePaymentProposal Decide(int id, byte[] version) => new(id, ApprovalDecision.Approve, null, null, version);

    private static async Task<ErpException> Refused(Func<Task> action) => await Assert.ThrowsAsync<ErpException>(action);

    private Task<int> RoleId(string code) => Query<int>("SELECT Id FROM core.Role WHERE Code = @Code", new { Code = code });

    private async Task<int> NewUser(string name, string? role = null)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        var id = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy) VALUES (NEWID(), @Name, @Name, 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Name = Unique(name + ".").ToLowerInvariant() });
        if (role is not null)
        {
            await connection.ExecuteAsync("""
                INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, CreatedBy) SELECT @User, Id, @Company, 1 FROM core.Role WHERE Code = @Role
                """, new { User = id, Role = role, Company = Company });
        }

        return id;
    }

    private async Task<int> Supplier()
    {
        var iban = StatementFiles.RomanianIban("BTRL", $"{Random.Shared.NextInt64(1_000_000_000_000_000, 9_999_999_999_999_999)}");
        await using var connection = new SqlConnection(Database.ConnectionString);
        var partner = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, IsCustomer, IsSupplier, VatPayer, Address, City, CountyCode, CountryCode, IsActive, CreatedBy)
            VALUES (@Code, N'Furnizor Propuneri SRL', 1, @FiscalCode, 0, 1, 1, N'Str. 1', N'Brașov', N'BV', 'RO', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("FP-"), FiscalCode = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await connection.ExecuteAsync("""
            INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy) VALUES (@Partner, @Iban, N'BT', 'RON', 1, 1, 1)
            """, new { Partner = partner, Iban = iban });
        return partner;
    }

    private Task<long> Invoice(int partner, decimal total, DateOnly due) =>
        InTransaction(sp => sp.GetRequiredService<InvoiceOpenItems>().RegisterAsync(new FacturaFurnizorValidata(Interlocked.Increment(ref invoiceSequence), partner,
            $"FF-{invoiceSequence}", due.AddDays(-30), due.AddDays(-30), "RON", 1m, total, total, 0, 0, false, false, null, false, [new InvoiceDueDate(due, total)], null,
            null, EInvoiceStatus.ReceivedFromSpv, null) { CompanyId = Company }, Ct), Company);

    private async Task Execute(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }
}

using Erp.Core.Application;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Detectarea si unirea dublurilor pe SQL Server real (02.01, 3.6 punctul 7).</summary>
public sealed partial class PartnerIntegrationTests
{
    private SavePartner Person(string name, string email) =>
        new(null, Code(), name, 4, null, true, false, null, false, false, false, null, "Str. Gării 1", "Alba Iulia", "AB", "RO", null, null, email,
            null, true, null);

    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Duplicate_partners_are_proposed_merged_with_their_children_and_ignored_pairs_disappear()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var keep = await Send(Person($"Dubla Zeta {tag} SRL", $"z{tag}@exemplu.ro"));
        var remove = await Send(Person($"DUBLĂ ZETA {tag} S.R.L.", $"Z{tag}@EXEMPLU.RO"));
        var other = await Send(Person($"Dubla Zeta {tag}", $"altul{tag}@exemplu.ro"));
        await Send(new SavePartnerContact(null, remove.Id, "Ion Pop", "Achiziții", "0722 111 222", null, null, true, null));
        await Send(new SavePartnerBankAccount(null, remove.Id, "RO49AAAA1B31007593840000", "Banca", "RON", true, true, null));

        var pairs = await Send(new FindDuplicates(DuplicateEntity.Partner));
        var pair = Assert.Single(pairs, p => p.FirstId == keep.Id && p.SecondId == remove.Id);
        Assert.Equal("Email,Name", pair.Reasons);
        Assert.Contains(pairs, p => p.FirstId == keep.Id && p.SecondId == other.Id && p.Reasons == "Name");

        await Send(new IgnorePartnerDuplicate(other.Id, keep.Id));
        Assert.DoesNotContain(await Send(new FindDuplicates(DuplicateEntity.Partner)), p => p.FirstId == keep.Id && p.SecondId == other.Id);

        await Send(new MergePartners(keep.Id, remove.Id));
        var kept = await Send(new GetPartner(keep.Id));
        Assert.Equal(("Ion Pop", "RO49AAAA1B31007593840000"), (Assert.Single(kept.Contacts).Name, Assert.Single(kept.BankAccounts).Iban));
        var merged = await Send(new GetPartner(remove.Id));
        Assert.False(merged.IsActive);
        Assert.DoesNotContain(await Send(new FindDuplicates(DuplicateEntity.Partner)), p => p.SecondId == remove.Id || p.FirstId == remove.Id);
        var again = await Assert.ThrowsAsync<ErpException>(() => Send(new MergePartners(keep.Id, remove.Id)));
        Assert.Equal("CORE_INVALID_REFERENCE", again.Code);
        var noRight = await Assert.ThrowsAsync<ErpException>(() => Send(new FindDuplicates(DuplicateEntity.Partner), without: CorePermissions.PartnerManage));
        Assert.Equal(ErpErrorKind.Forbidden, noRight.Kind);
    }
}

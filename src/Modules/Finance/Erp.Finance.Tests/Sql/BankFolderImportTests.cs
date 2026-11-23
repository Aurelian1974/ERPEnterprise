using Erp.Core.Application;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Core.Infrastructure.Messaging;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Infrastructure;
using Erp.Finance.Infrastructure.Bank;
using Erp.Finance.Tests.Bank;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Tests.Sql;

/// <summary>
/// Importul automat al extraselor din folder (02.02, F2 4.1 pasul 1), compus ca in Erp.Worker: pipeline-ul comenzilor cu contextul de sistem.
/// Fisierul bun se importa (si se potriveste) si se muta in „importate”; cel refuzat in „erori”, cu motivul; cel abia scris asteapta.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BankFolderImportTests : BankTestBase, IClassFixture<FinanceDatabase>, IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erp-extrase-" + Guid.NewGuid().ToString("N"));

    public BankFolderImportTests(FinanceDatabase database) : base(database)
    {
        Permissions.UnionWith([FinancePermissions.BankView, FinancePermissions.BankFormatManage, FinancePermissions.BankStatementImport]);
        Directory.CreateDirectory(folder);
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private BankFolderImport Job()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(FinanceApplication.Assembly);
        services.AddCoreInfrastructure(Database.ConnectionString, new LicenseOptions(Development: true, LoadFromDatabase: false));
        services.AddFinanceInfrastructure(Database.ConnectionString);
        services.AddOutboxDispatcher(Database.ConnectionString);
        services.AddSingleton<ILicenseInfo>(new FakeLicense());
        return services.BuildServiceProvider().GetRequiredService<BankFolderImport>();
    }

    private string Write(string name, byte[] content, bool settled = true)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, content);
        if (settled)
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        }

        return path;
    }

    [Fact]
    [Trait("Criteriu", "02.02-F2-4.1")]
    public async Task Statements_dropped_in_the_folder_are_imported_and_refused_files_are_set_aside_with_the_reason()
    {
        var (account, iban) = await NewBank();
        await Send(new SetTreasuryAccountBankFormat(account, null, folder));
        var good = StatementFiles.Camt053(iban, "RON", 100m, [new TestEntry(Day, 25m, Unique("F"))], "1");
        Write("extras-1.xml", good);
        Write("stricat.xml", "<Document><nimic/></Document>"u8.ToArray());
        Write("in-copiere.xml", good, settled: false);

        var result = await Job().RunAsync(Ct);

        Assert.Equal((1, 1, 1), (result.Imported, result.Failed, result.Postponed));
        Assert.True(File.Exists(Path.Combine(folder, BankFolderImport.ImportedFolder, "extras-1.xml")));
        Assert.True(File.Exists(Path.Combine(folder, BankFolderImport.ErrorFolder, "stricat.xml")));
        Assert.StartsWith("FIN_BANK_FILE_INVALID", await File.ReadAllTextAsync(Path.Combine(folder, BankFolderImport.ErrorFolder, "stricat.xml.eroare.txt"), Ct),
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "in-copiere.xml")));
        var statement = Assert.Single(await Send(new GetBankStatements(account)));
        Assert.Equal(StatementSource.Camt053, statement.Source);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Fin.BankStatement.Import' AND UserId = 1 AND EntityId = @Id",
            new { statement.Id }));

        // Acelasi fisier, din nou: refuzat ca dublura, fara al doilea extras.
        File.SetLastWriteTimeUtc(Path.Combine(folder, "in-copiere.xml"), DateTime.UtcNow.AddMinutes(-5));
        var again = await Job().RunAsync(Ct);
        Assert.Equal((0, 1, 0), (again.Imported, again.Failed, again.Postponed));
        Assert.StartsWith("FIN_BANK_FILE_ALREADY_IMPORTED", await File.ReadAllTextAsync(Path.Combine(folder, BankFolderImport.ErrorFolder, "in-copiere.xml.eroare.txt"), Ct),
            StringComparison.Ordinal);
        Assert.Single(await Send(new GetBankStatements(account)));
    }
}

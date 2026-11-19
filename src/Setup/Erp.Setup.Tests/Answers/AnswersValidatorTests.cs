using System.Text.Json.Nodes;
using Erp.Setup.Core.Answers;

namespace Erp.Setup.Tests.Answers;

/// <summary>Fisierul de raspunsuri, formatul 1 (08, 6.2).</summary>
public sealed class AnswersValidatorTests
{
    private const string MinimalJson = """
        {
          "formatVersion": 1,
          "sql": { "server": "SQL01", "database": "ERP_PROD" },
          "site": { "hostName": "srv-erp.grupulexemplu.local" },
          "license": { "file": "C:\\Kit\\grupul-exemplu.lic" }
        }
        """;

    private static AnswersLoadResult Validate(string json) => AnswersValidator.Validate(AnswersFile.Parse(json), dataRoot: @"C:\ProgramData\Erp");

    private static AnswersLoadResult ValidateWith(string path, JsonNode? value)
    {
        var root = JsonNode.Parse(MinimalJson)!.AsObject();
        var segments = path.Split('.');
        var target = root;
        foreach (var segment in segments[..^1])
        {
            target = (target[segment] ??= new JsonObject()).AsObject();
        }

        if (value is null)
        {
            target.Remove(segments[^1]);
        }
        else
        {
            target[segments[^1]] = value;
        }

        return Validate(root.ToJsonString());
    }

    private static void AssertError(AnswersLoadResult result, string field)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Fact]
    public void Minimal_file_gets_the_documented_defaults()
    {
        var result = Validate(MinimalJson);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Field}: {e.Message}")));
        var r = result.Request!;
        Assert.Equal("PROD", r.Instance);
        Assert.Equal(@"C:\Program Files\Erp", r.InstallRoot);
        Assert.Equal(@"C:\ProgramData\Erp", r.DataRoot);
        Assert.Equal(Path.Combine(@"C:\ProgramData\Erp", "PROD"), r.InstanceDirectory);
        Assert.Equal("ERP-PROD", r.SiteName);
        Assert.Equal("ErpWorker-PROD", r.ServiceName);
        Assert.Equal(new SqlRequest("SQL01", "ERP_PROD", null, null, AppIdentity.VirtualAccounts, null), r.Sql);
        Assert.Equal(new SiteRequest("srv-erp.grupulexemplu.local", 443, true, CertificateKind.Internal, null), r.Site);
        Assert.Equal(new BackupRequest(null, new TimeOnly(1, 0), 15, 14, DayOfWeek.Saturday, new TimeOnly(2, 0)), r.Backup);
        Assert.Empty(r.AcceptedWarnings);
    }

    [Fact]
    public void The_documented_example_file_is_valid()
    {
        var path = Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup", "Kit", "templates", "answers.example.json");

        var result = AnswersValidator.Load(path);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Field}: {e.Message}")));
        Assert.Equal(AppIdentity.VirtualAccounts, result.Request!.Sql.AppIdentity);
        Assert.Equal("srv-erp.firma.local", result.Request.Site.HostName);
        Assert.Empty(result.Request.AcceptedWarnings);
    }

    [Theory]
    [InlineData("formatVersion", null)]
    [InlineData("formatVersion", 2)]
    [InlineData("sql.server", null)]
    [InlineData("sql.database", null)]
    [InlineData("site.hostName", null)]
    [InlineData("license.file", null)]
    public void Mandatory_fields_and_an_unknown_format_are_refused_with_the_field_name(string field, int? value)
    {
        AssertError(ValidateWith(field, value is null ? null : JsonValue.Create(value.Value)), field);
    }

    [Theory]
    [InlineData("instance", "prod")]
    [InlineData("instance", "P")]
    [InlineData("instance", "PRODUCTIE123")]
    [InlineData("sql.server", "SQL01; DROP")]
    [InlineData("sql.database", "1ERP")]
    [InlineData("sql.database", "ERP-PROD")]
    [InlineData("sql.dataFolder", "relativ\\date")]
    [InlineData("sql.logFolder", "D:\\Log'--")]
    [InlineData("sql.appIdentity", "Sql")]
    [InlineData("backup.folder", "B:\\x;y")]
    [InlineData("backup.fullBackupTime", "25:00")]
    [InlineData("backup.integrityCheckDay", "Sambata")]
    [InlineData("site.hostName", "erp_server")]
    [InlineData("site.certificate.kind", "SelfSigned")]
    public void Invalid_values_are_refused_with_the_field_name(string field, string value)
    {
        AssertError(ValidateWith(field, JsonValue.Create(value)), field);
    }

    [Theory]
    [InlineData("backup.logBackupEveryMinutes", 4)]
    [InlineData("backup.logBackupEveryMinutes", 61)]
    [InlineData("backup.retentionDays", 2)]
    [InlineData("backup.retentionDays", 91)]
    [InlineData("site.httpsPort", 0)]
    [InlineData("site.httpsPort", 70000)]
    public void Numbers_outside_the_allowed_range_are_refused(string field, int value)
    {
        AssertError(ValidateWith(field, JsonValue.Create(value)), field);
    }

    [Theory]
    [InlineData("SQL01")]
    [InlineData("SQL01\\ERP")]
    [InlineData("sql01.grupulexemplu.local,1433")]
    public void Sql_server_accepts_server_instance_and_port_forms(string server)
    {
        Assert.True(ValidateWith("sql.server", server).IsValid);
    }

    [Fact]
    public void Gmsa_identity_requires_a_DOMAIN_name_dollar_account()
    {
        AssertError(ValidateWith("sql.appIdentity", "Gmsa"), "sql.gmsaAccount");

        var root = JsonNode.Parse(MinimalJson)!.AsObject();
        root["sql"]!["appIdentity"] = "Gmsa";
        root["sql"]!["gmsaAccount"] = @"GRUPULEXEMPLU\erp-prod$";
        var result = Validate(root.ToJsonString());

        Assert.True(result.IsValid);
        Assert.Equal(@"GRUPULEXEMPLU\erp-prod$", result.Request!.Sql.GmsaAccount);
    }

    [Fact]
    public void Gmsa_account_is_ignored_for_other_identities()
    {
        var root = JsonNode.Parse(MinimalJson)!.AsObject();
        root["sql"]!["appIdentity"] = "ComputerAccount";
        root["sql"]!["gmsaAccount"] = @"GRUPULEXEMPLU\erp-prod$";

        var result = Validate(root.ToJsonString());

        Assert.Equal(AppIdentity.ComputerAccount, result.Request!.Sql.AppIdentity);
        Assert.Null(result.Request.Sql.GmsaAccount);
    }

    [Fact]
    public void Existing_certificate_requires_a_40_character_thumbprint_normalized_to_upper_case()
    {
        AssertError(ValidateWith("site.certificate", new JsonObject { ["kind"] = "Existing" }), "site.certificate.thumbprint");

        var result = ValidateWith("site.certificate", new JsonObject { ["kind"] = "Existing", ["thumbprint"] = "3f 2a9c00112233445566778899aabbccddeeffe1" });

        Assert.Equal(CertificateKind.Existing, result.Request!.Site.Certificate);
        Assert.Equal("3F2A9C00112233445566778899AABBCCDDEEFFE1", result.Request.Site.CertificateThumbprint);
    }

    [Fact]
    public void Accepted_warnings_must_be_check_ids()
    {
        AssertError(ValidateWith("acceptedWarnings", new JsonArray("C-11", "toate")), "acceptedWarnings");

        var result = ValidateWith("acceptedWarnings", new JsonArray("C-11", "C-19"));
        Assert.True(result.Request!.Accepts("C-19"));
        Assert.False(result.Request.Accepts("C-18"));
    }

    [Fact]
    public void Missing_or_malformed_file_is_reported()
    {
        Assert.False(AnswersValidator.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).IsValid);

        var path = Path.Combine(Path.GetTempPath(), $"erp-answers-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ nu e json");
        try
        {
            Assert.False(AnswersValidator.Load(path).IsValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Written_file_round_trips_to_the_same_request_without_secrets()
    {
        var original = Validate(MinimalJson).Request! with
        {
            Sql = new SqlRequest("SQL01", "ERP_PROD", @"D:\Date", @"L:\Log", AppIdentity.Gmsa, @"GRUPULEXEMPLU\erp-prod$"),
            Backup = new BackupRequest(@"B:\SQLBackup\ERP_PROD", new TimeOnly(0, 30), 10, 21, DayOfWeek.Sunday, new TimeOnly(3, 15)),
            AcceptedWarnings = new HashSet<string>(["C-11"]),
        };
        var path = Path.Combine(Path.GetTempPath(), $"erp-answers-{Guid.NewGuid():N}.json");
        try
        {
            AnswersFile.Write(path, original);
            var text = File.ReadAllText(path);
            var reloaded = AnswersValidator.Validate(AnswersFile.Parse(text), original.DataRoot).Request!;

            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(original.Sql, reloaded.Sql);
            Assert.Equal(original.Site, reloaded.Site);
            Assert.Equal(original.Backup, reloaded.Backup);
            Assert.Equal(original.LicenseFile, reloaded.LicenseFile);
            Assert.Equal(original.AcceptedWarnings, reloaded.AcceptedWarnings);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using System.Text.Json.Nodes;
using Erp.Setup.Core.Answers;

namespace Erp.Setup.Tests.Answers;

public sealed class AnswersValidatorTests
{
    private const string ValidJson = """
        {
          "instance": "PROD",
          "site": { "hostName": "erp.grupulexemplu.local", "certificateThumbprint": "3F2A9C00112233445566778899AABBCCDDEEFFE1" },
          "sql": { "server": "SQL01\\ERP" },
          "backup": { "backupRoot": "B:\\SQLBackup" },
          "license": { "filePath": "C:\\Kit\\grupul-exemplu.lic" }
        }
        """;

    private static AnswersValidationResult Validate(string json) => AnswersValidator.Parse(json, _ => true);

    private static AnswersValidationResult ValidateWith(string path, JsonNode? value)
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
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

    [Fact]
    public void Minimal_file_gets_documented_defaults()
    {
        var result = Validate(ValidJson);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var a = result.Answers!;
        Assert.Equal(@"C:\Program Files\Erp", a.InstallRoot);
        Assert.Equal(@"C:\ProgramData\Erp", a.DataRoot);
        Assert.Equal(443, a.Site.HttpsPort);
        Assert.True(a.Site.HttpRedirect);
        Assert.False(a.Site.AllowSelfSignedCertificate);
        Assert.Equal("ERP_PROD", a.Sql.DatabaseName);
        Assert.Equal(SqlAuthentication.Windows, a.Sql.OperatorAuthentication);
        Assert.Equal(SqlAuthentication.Sql, a.Sql.AppAuthentication);
        Assert.Equal(1024, a.Sql.InitialDataSizeMb);
        Assert.Equal(512, a.Sql.InitialLogSizeMb);
        Assert.Equal(SqlEncrypt.Mandatory, a.Sql.Encrypt);
        Assert.False(a.Sql.TrustServerCertificate);
        Assert.Equal(new TimeOnly(1, 0), a.Backup.FullBackupTime);
        Assert.Equal(15, a.Backup.LogBackupEveryMinutes);
        Assert.Equal(14, a.Backup.RetentionDays);
        Assert.Equal(DayOfWeek.Sunday, a.Backup.IntegrityCheckDay);
        Assert.Equal(new TimeOnly(3, 0), a.Backup.IntegrityCheckTime);
        Assert.Equal("administrator", a.Administrator.UserName);
        Assert.Equal("Administrator ERP", a.Administrator.DisplayName);
        Assert.Null(a.Administrator.Email);
        Assert.True(a.Prerequisites.InstallIisFeatures);
        Assert.True(a.Prerequisites.InstallHostingBundle);
    }

    [Fact]
    public void Instance_defaults_to_PROD_and_database_name_follows_instance()
    {
        var withoutInstance = ValidateWith("instance", null);
        var test = ValidateWith("instance", "TEST");

        Assert.Equal("PROD", withoutInstance.Answers!.Instance);
        Assert.Equal("ERP_TEST", test.Answers!.Sql.DatabaseName);
    }

    public static TheoryData<string, string> InvalidValues => new()
    {
        { "instance", "\"prod\"" },
        { "instance", "\"P\"" },
        { "instance", "\"PRODUCTION1\"" },
        { "instance", "\"1PROD\"" },
        { "installRoot", "\"Program Files\\\\Erp\"" },
        { "installRoot", "\"\\\\\\\\server\\\\share\\\\Erp\"" },
        { "installRoot", "\"C:\\\\\"" },
        { "dataRoot", "\"D:\"" },
        { "dataRoot", "\"C:\\\\Data|Erp\"" },
        { "site.hostName", "\"erp_server.local\"" },
        { "site.hostName", "\"-erp.local\"" },
        { "site.httpsPort", "0" },
        { "site.httpsPort", "65536" },
        { "site.certificateThumbprint", "\"XYZ\"" },
        { "sql.server", "\"SQL01\\\\ERP,1433\"" },
        { "sql.server", "\"SQL01,70000\"" },
        { "sql.server", "\"SQL01;Database=x\"" },
        { "sql.databaseName", "\"1ERP\"" },
        { "sql.databaseName", "\"ER\"" },
        { "sql.databaseName", "\"ERP-PROD\"" },
        { "sql.operatorAuthentication", "\"windows\"" },
        { "sql.operatorAuthentication", "\"Kerberos\"" },
        { "sql.appAuthentication", "\"0\"" },
        { "sql.dataFilePath", "\"D:\\\\SQL'Data\"" },
        { "sql.dataFilePath", "\"D:\\\\SQL;Data\"" },
        { "sql.logFilePath", "\"L:\\\\SQL--Log\"" },
        { "sql.logFilePath", "\"SQLLog\"" },
        { "sql.initialDataSizeMb", "255" },
        { "sql.initialLogSizeMb", "100" },
        { "sql.encrypt", "\"True\"" },
        { "backup.backupRoot", "\"\"" },
        { "backup.backupRoot", "\"B:\\\\Backup\\u0001\"" },
        { "backup.fullBackupTime", "\"1:00\"" },
        { "backup.fullBackupTime", "\"25:00\"" },
        { "backup.logBackupEveryMinutes", "4" },
        { "backup.logBackupEveryMinutes", "61" },
        { "backup.retentionDays", "2" },
        { "backup.retentionDays", "91" },
        { "backup.integrityCheckDay", "\"Duminică\"" },
        { "backup.integrityCheckDay", "\"sunday\"" },
        { "backup.integrityCheckTime", "\"03:00:00\"" },
        { "administrator.userName", "\"Administrator\"" },
        { "administrator.userName", "\"ab\"" },
        { "administrator.userName", "\"1admin\"" },
        { "administrator.displayName", "\"\"" },
        { "administrator.email", "\"it@\"" },
        { "administrator.email", "\"it at firma.ro\"" },
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void Invalid_value_is_reported_on_its_field(string field, string jsonValue)
    {
        var result = ValidateWith(field, JsonNode.Parse(jsonValue));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    public static TheoryData<string> RequiredFields => new()
    {
        "site.hostName",
        "sql.server",
        "backup.backupRoot",
        "license.filePath",
    };

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void Required_field_is_reported_when_missing(string field)
    {
        var result = ValidateWith(field, null);

        Assert.Contains(result.Errors, e => e.Field == field && e.Message == "Câmpul este obligatoriu.");
    }

    [Fact]
    public void Thumbprint_is_required_unless_self_signed_is_allowed()
    {
        var withoutThumbprint = ValidateWith("site.certificateThumbprint", null);
        Assert.Contains(withoutThumbprint.Errors, e => e.Field == "site.certificateThumbprint");

        var root = JsonNode.Parse(ValidJson)!.AsObject();
        root["site"]!.AsObject().Remove("certificateThumbprint");
        root["site"]!["allowSelfSignedCertificate"] = true;
        var selfSigned = Validate(root.ToJsonString());

        Assert.True(selfSigned.IsValid);
        Assert.Contains(selfSigned.Warnings, w => w.Field == "site.allowSelfSignedCertificate");
    }

    [Fact]
    public void Thumbprint_spaces_are_removed_and_uppercased()
    {
        var result = ValidateWith("site.certificateThumbprint", "3f 2a 9c 00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff e1");

        Assert.Equal("3F2A9C00112233445566778899AABBCCDDEEFFE1", result.Answers!.Site.CertificateThumbprint);
    }

    [Fact]
    public void Operator_sql_login_is_required_for_sql_authentication()
    {
        var result = ValidateWith("sql.operatorAuthentication", "Sql");

        Assert.Contains(result.Errors, e => e.Field == "sql.operatorSqlLogin");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("svc-erp", false)]
    [InlineData("CONTOSO\\svc-erp", true)]
    [InlineData("CONTOSO\\svc-erp$", true)]
    public void App_windows_account_is_required_and_validated_for_windows_authentication(string? account, bool valid)
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        root["sql"]!["appAuthentication"] = "Windows";
        if (account is not null)
        {
            root["sql"]!["appWindowsAccount"] = account;
        }

        var result = Validate(root.ToJsonString());

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Contains(result.Errors, e => e.Field == "sql.appWindowsAccount");
        }
    }

    [Theory]
    [InlineData("SQL01")]
    [InlineData("SQL01\\ERP")]
    [InlineData("sql01.firma.local,1433")]
    [InlineData(".")]
    public void Supported_sql_server_forms_are_accepted(string server)
    {
        Assert.True(ValidateWith("sql.server", server).IsValid);
    }

    [Fact]
    public void Missing_license_file_is_reported()
    {
        var result = AnswersValidator.Parse(ValidJson, _ => false);

        Assert.Contains(result.Errors, e => e.Field == "license.filePath" && e.Message.StartsWith("Fișierul de licență nu există", StringComparison.Ordinal));
    }

    [Fact]
    public void Backup_on_the_same_drive_as_data_is_a_warning()
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        root["sql"]!["dataFilePath"] = @"D:\SQLData";
        root["backup"]!["backupRoot"] = @"d:\SQLBackup";

        var result = Validate(root.ToJsonString());

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Field == "backup.backupRoot");
    }

    [Fact]
    public void Trusting_the_server_certificate_is_a_warning()
    {
        var result = ValidateWith("sql.trustServerCertificate", true);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Field == "sql.trustServerCertificate");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("sqlPassword")]
    public void Unknown_fields_such_as_passwords_are_rejected(string field)
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        root["sql"]![field] = "Secret123!";

        var result = Validate(root.ToJsonString());

        var issue = Assert.Single(result.Errors);
        Assert.Equal($"sql.{field}", issue.Field);
        Assert.DoesNotContain("Secret123!", issue.Message);
    }

    [Fact]
    public void Wrong_type_is_reported_on_its_field()
    {
        var result = ValidateWith("site.httpsPort", "443");

        Assert.Equal("site.httpsPort", Assert.Single(result.Errors).Field);
    }

    [Fact]
    public void Missing_answers_file_is_reported()
    {
        var result = AnswersValidator.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));

        Assert.Equal("--answers", Assert.Single(result.Errors).Field);
    }

    [Fact]
    public void Example_answers_file_from_the_kit_is_valid()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Setup", "Erp.Setup", "Kit", "templates", "answers.example.json");

        var result = AnswersValidator.Parse(File.ReadAllText(path), _ => true);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Erp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Rădăcina depozitului nu a fost găsită.");
    }
}

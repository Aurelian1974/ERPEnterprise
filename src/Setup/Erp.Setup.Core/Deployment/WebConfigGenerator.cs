using System.Xml.Linq;

namespace Erp.Setup.Core.Deployment;

/// <summary>web.config pentru Erp.Host (8.10): ANCM in-process, ERP_INSTANCE_DIR, autentificare Windows doar pe /account/windows.</summary>
public static class WebConfigGenerator
{
    public static string Generate(string instanceDirectory)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("configuration",
                new XElement("location",
                    new XAttribute("path", "."),
                    new XAttribute("inheritInChildApplications", "false"),
                    new XElement("system.webServer",
                        new XElement("handlers",
                            new XElement("add",
                                new XAttribute("name", "aspNetCore"),
                                new XAttribute("path", "*"),
                                new XAttribute("verb", "*"),
                                new XAttribute("modules", "AspNetCoreModuleV2"),
                                new XAttribute("resourceType", "Unspecified"))),
                        new XElement("aspNetCore",
                            new XAttribute("processPath", @".\Erp.Host.exe"),
                            new XAttribute("stdoutLogEnabled", "false"),
                            new XAttribute("hostingModel", "inprocess"),
                            new XElement("environmentVariables",
                                Variable("ASPNETCORE_ENVIRONMENT", "Production"),
                                Variable("ERP_INSTANCE_DIR", instanceDirectory))),
                        new XElement("security",
                            new XElement("requestFiltering",
                                new XAttribute("removeServerHeader", "true"),
                                new XElement("requestLimits", new XAttribute("maxAllowedContentLength", "52428800")))),
                        new XElement("httpProtocol",
                            new XElement("customHeaders",
                                new XElement("remove", new XAttribute("name", "X-Powered-By")))))),
                new XElement("location",
                    new XAttribute("path", "account/windows"),
                    new XElement("system.webServer",
                        new XElement("security",
                            new XElement("authentication",
                                new XElement("anonymousAuthentication", new XAttribute("enabled", "false")),
                                new XElement("windowsAuthentication", new XAttribute("enabled", "true"))))))));

        using var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private static XElement Variable(string name, string value) =>
        new("environmentVariable", new XAttribute("name", name), new XAttribute("value", value));

    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
    }
}

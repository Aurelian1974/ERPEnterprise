using Erp.Setup.Windows.Processes;
using Erp.Setup.Windows.Services;

namespace Erp.Setup.Windows.Iis;

/// <summary>
/// Pasul I-15 (5.5): regula de intrare „ERP &lt;Instanta&gt; HTTPS” pe TCP &lt;port&gt;, profilurile Domain si Private, si
/// „ERP &lt;Instanta&gt; HTTP” pe 80 doar cu redirect. Idempotent: regula cu acelasi nume se sterge si se recreeaza.
/// </summary>
public sealed class FirewallRules(IProcessRunner processes)
{
    public static string HttpsRuleName(string instance) => $"ERP {instance} HTTPS";

    public static string HttpRuleName(string instance) => $"ERP {instance} HTTP";

    public static IReadOnlyList<string> AddRule(string name, int port) =>
        ["advfirewall", "firewall", "add", "rule", $"name={name}", "dir=in", "action=allow", "protocol=TCP", $"localport={port}", "profile=domain,private"];

    public static IReadOnlyList<string> DeleteRule(string name) =>
        ["advfirewall", "firewall", "delete", "rule", $"name={name}"];

    public void Apply(string instance, int httpsPort, bool httpRedirect)
    {
        Replace(HttpsRuleName(instance), httpsPort);
        if (httpRedirect)
        {
            Replace(HttpRuleName(instance), 80);
        }
        else
        {
            processes.Run(Netsh, DeleteRule(HttpRuleName(instance)));
        }
    }

    public void Remove(string instance)
    {
        processes.Run(Netsh, DeleteRule(HttpsRuleName(instance)));
        processes.Run(Netsh, DeleteRule(HttpRuleName(instance)));
    }

    private static string Netsh => ProcessRunner.System32("netsh.exe");

    private void Replace(string name, int port)
    {
        // Codul de iesire al stergerii nu conteaza: regula poate sa nu existe.
        processes.Run(Netsh, DeleteRule(name));
        var result = processes.Run(Netsh, AddRule(name, port));
        if (result.ExitCode != 0)
        {
            throw new WindowsSetupException($"Regula de firewall „{name}” nu a putut fi creată (cod {result.ExitCode}): {result.Output}");
        }
    }
}

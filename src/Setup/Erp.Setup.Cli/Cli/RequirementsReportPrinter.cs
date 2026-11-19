using Erp.Setup.Core.Checks;

namespace Erp.Setup.Cli;

/// <summary>Tabelul verificarilor C-xx pentru operator (7).</summary>
public static class RequirementsReportPrinter
{
    public static void Print(OperatorConsole console, RequirementsReport report)
    {
        console.Info(string.Empty);
        console.Info("Verificarea cerințelor:");
        var titleWidth = report.Results.Max(r => r.Title.Length);
        foreach (var result in report.Results)
        {
            var line = $"  [{result.Id}] {Label(result.Status),-9} {result.Title.PadRight(titleWidth)}  {result.Message}";
            switch (result.Status)
            {
                case CheckStatus.Failed:
                    console.Failure(line);
                    break;
                case CheckStatus.Warning:
                    console.Warning(line);
                    break;
                default:
                    console.Info(line);
                    break;
            }
        }

        var failed = report.Results.Count(r => r.Status == CheckStatus.Failed);
        var warnings = report.Results.Count(r => r.Status == CheckStatus.Warning);
        console.Info(string.Empty);
        if (failed > 0)
        {
            console.Error($"Cerințe neîndeplinite: {failed} blocante, {warnings} avertismente.");
        }
        else if (warnings > 0)
        {
            console.Warning($"Cerințele sunt îndeplinite, cu {warnings} avertismente.");
        }
        else
        {
            console.Info("Toate cerințele sunt îndeplinite.");
        }
    }

    public static string Label(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "OK",
        CheckStatus.Warning => "ATENȚIE",
        CheckStatus.Failed => "BLOCANT",
        _ => "—",
    };
}

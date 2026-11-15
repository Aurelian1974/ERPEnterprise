using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace Erp.Setup.Cli;

/// <summary>Ajutorul in romana (T1); inlocuieste textul implicit, in engleza, al System.CommandLine.</summary>
internal sealed class RomanianHelpAction(SetupCommandLine owner) : SynchronousCommandLineAction
{
    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        Write(parseResult.CommandResult.Command);
        return 0;
    }

    public void Write(Command command)
    {
        var w = owner.Output;
        var root = owner.Root;
        w.WriteLine(root.Description);
        w.WriteLine();

        if (command == root)
        {
            w.WriteLine("Utilizare:");
            w.WriteLine($"  {SetupCommandLine.ExecutableName} <comandă> [opțiuni]");
            w.WriteLine();
            w.WriteLine("Comenzi:");
            WriteRows(w, root.Subcommands.Select(c => (c.Name, c.Description ?? string.Empty)));
        }
        else
        {
            w.WriteLine($"{command.Name}: {command.Description}");
            w.WriteLine();
            w.WriteLine("Utilizare:");
            w.WriteLine($"  {Usage(command)}");
            w.WriteLine();
            w.WriteLine("Opțiuni:");
            WriteRows(w, command.Options.Select(o => (OptionLabel(o), o.Description ?? string.Empty)));
        }

        w.WriteLine();
        w.WriteLine("Opțiuni comune:");
        WriteRows(w, root.Options.Select(o => (OptionLabel(o), o.Description ?? string.Empty)));

        if (command == root)
        {
            w.WriteLine();
            w.WriteLine($"Detalii pentru o comandă: {SetupCommandLine.ExecutableName} <comandă> --help");
        }
    }

    private string Usage(Command command)
    {
        var required = owner.RequiredOptionsOf(command);
        var parts = new List<string> { SetupCommandLine.ExecutableName, command.Name };
        parts.AddRange(required.Select(OptionLabel));
        parts.AddRange(command.Options.Where(o => !required.Contains(o)).Select(o => $"[{OptionLabel(o)}]"));
        return string.Join(' ', parts);
    }

    private static string OptionLabel(Option option)
    {
        var names = string.Join(", ", option.Aliases.Where(a => a.StartsWith('-')).Prepend(option.Name).Distinct());
        return option.ValueType == typeof(bool) || option is HelpOption or VersionOption
            ? names
            : $"{names} <{option.HelpName ?? "valoare"}>";
    }

    private static void WriteRows(TextWriter w, IEnumerable<(string Label, string Description)> rows)
    {
        var list = rows.ToList();
        var width = list.Count == 0 ? 0 : list.Max(r => r.Label.Length);
        foreach (var (label, description) in list)
        {
            w.WriteLine($"  {label.PadRight(width)}  {description}");
        }
    }
}

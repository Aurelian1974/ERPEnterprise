using System.CommandLine;
using System.CommandLine.Help;
using System.Text.RegularExpressions;
using Erp.Setup.Core;

namespace Erp.Setup.Cli;

/// <summary>Comenzile din INSTALARE_ACTUALIZARE.md, 6.1, cu ajutor si mesaje in romana.</summary>
public sealed partial class SetupCommandLine
{
    public const string ExecutableName = "ErpSetup.Cli.exe";

    private readonly Func<SetupInvocation, int> run;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly Dictionary<Command, List<Option>> requiredOptions = [];

    private readonly Option<string> logLevel = new("--log-level")
    {
        Description = "Nivelul jurnalului: Information sau Debug.",
        HelpName = "Information|Debug",
        Recursive = true,
        DefaultValueFactory = _ => "Information",
    };

    private readonly Option<bool> noColor = new("--no-color")
    {
        Description = "Afișare fără culori.",
        Recursive = true,
    };

#if DEBUG
    private readonly Option<bool> devSkipSignature = new("--dev-skip-signature")
    {
        Description = "Numai în build-urile Debug: kit de test fără semnătură (sare verificarea Authenticode).",
        Recursive = true,
    };
#endif

    public SetupCommandLine(Func<SetupInvocation, int> run, TextWriter output, TextWriter error)
    {
        this.run = run;
        this.output = output;
        this.error = error;
        Root = BuildRoot();
    }

    public RootCommand Root { get; }

    public IReadOnlyList<Option> RequiredOptionsOf(Command command) =>
        requiredOptions.TryGetValue(command, out var list) ? list : [];

    public ParseResult Parse(params string[] args) => Root.Parse(args);

    public int Invoke(params string[] args)
    {
        var result = Parse(args);
        if (result.Action is RomanianHelpAction)
        {
            return result.Invoke(Configuration());
        }

        if (result.CommandResult.Command == Root)
        {
            error.WriteLine("Specificați o comandă.");
            new RomanianHelpAction(this).Write(Root);
            return ExitCodes.InvalidCommandLine;
        }

        if (result.Errors.Count > 0)
        {
            error.WriteLine("Linia de comandă este invalidă:");
            foreach (var parseError in result.Errors)
            {
                error.WriteLine($"  - {Translate(parseError.Message)}");
            }

            error.WriteLine($"Folosiți „{ExecutableName} {result.CommandResult.Command.Name} --help” pentru ajutor.");
            return ExitCodes.InvalidCommandLine;
        }

        return result.Invoke(Configuration());
    }

    internal TextWriter Output => output;

    private InvocationConfiguration Configuration() => new()
    {
        Output = output,
        Error = error,
        EnableDefaultExceptionHandler = false,
    };

    private RootCommand BuildRoot()
    {
        var root = new RootCommand("ErpSetup.Cli – installer-ul ERP în linia de comandă: instalare, actualizare, reparare, recuperare și diagnostic.");
        root.Options.Add(logLevel);
        root.Options.Add(noColor);
#if DEBUG
        root.Options.Add(devSkipSignature);
#endif
        logLevel.Validators.Add(r =>
        {
            var value = r.GetValueOrDefault<string>();
            if (value is not ("Information" or "Debug"))
            {
                r.AddError($"Valoarea „{value}” pentru --log-level nu este permisă; folosiți Information sau Debug.");
            }
        });

        foreach (var option in root.Options)
        {
            if (option is HelpOption help)
            {
                help.Description = "Afișează ajutorul.";
                help.Action = new RomanianHelpAction(this);
            }
            else if (option is VersionOption version)
            {
                version.Description = "Afișează versiunea installer-ului.";
            }
        }

        root.Subcommands.Add(Check());
        root.Subcommands.Add(Install());
        root.Subcommands.Add(InstanceCommand("upgrade", "Actualizare cu kitul curent, cu revenire automată la eșec.", unattended: true));
        root.Subcommands.Add(InstanceCommand("rollback", "Revenire după o actualizare întreruptă."));
        root.Subcommands.Add(Repair());
        root.Subcommands.Add(Recover());
        root.Subcommands.Add(InstanceCommand("reset-admin", "Parolă temporară nouă pentru administratorul integrat (afișată o singură dată)."));
        root.Subcommands.Add(ExportRecoveryKey());
        root.Subcommands.Add(VerifyRecoveryKey());
        root.Subcommands.Add(Diagnose());
        root.Subcommands.Add(Uninstall());
        root.Subcommands.Add(InstanceCommand("status", "Versiunea, starea, ultimul backup, licența și certificatele instalării."));
        return root;
    }

    private Command Check()
    {
        var command = new Command("check", "Rulează verificările (nu modifică nimic).");
        var answers = AnswersOption(command);
        Bind(command, r => Base(r, "check") with { AnswersPath = r.GetValue(answers) });
        return command;
    }

    private Command Install()
    {
        var command = new Command("install", "Instalare nouă din fișierul de răspunsuri.");
        var answers = AnswersOption(command);
        var unattended = UnattendedOption(command);
        var recoveryKeyOut = new Option<string>("--recovery-key-out")
        {
            Description = "Fișierul .pfx în care se exportă cheia de recuperare; obligatoriu cu --unattended (parola din ERP_RECOVERY_KEY_PASSWORD).",
            HelpName = "fișier.pfx",
        };
        command.Options.Add(recoveryKeyOut);
        Bind(command, r => Base(r, "install") with
        {
            AnswersPath = r.GetValue(answers),
            Unattended = r.GetValue(unattended),
            RecoveryKeyOut = r.GetValue(recoveryKeyOut),
        });
        return command;
    }

    private Command Repair()
    {
        var command = InstanceCommand("repair", "Refacere IIS, serviciu, configurare, drepturi și obiecte programabile (aceeași versiune).");
        var license = new Option<string>("--license")
        {
            Description = "Înlocuiește licența cu fișierul dat.",
            HelpName = "fișier.lic",
        };
        command.Options.Add(license);
        Rebind(command, (r, invocation) => invocation with { LicensePath = r.GetValue(license) });
        return command;
    }

    private Command Recover()
    {
        var command = new Command("recover", "Recuperează o instalare existentă pe un calculator nou (baza și cheia de recuperare).");
        var answers = AnswersOption(command);
        var key = RecoveryKeyOption(command);
        Bind(command, r => Base(r, "recover") with { AnswersPath = r.GetValue(answers), RecoveryKeyPath = r.GetValue(key) });
        return command;
    }

    private Command ExportRecoveryKey()
    {
        var command = InstanceCommand("export-recovery-key", "Exportă din nou cheia de recuperare.");
        var output = new Option<string>("--out")
        {
            Description = "Fișierul .pfx creat.",
            HelpName = "fișier.pfx",
        };
        AddRequired(command, output);
        Rebind(command, (r, invocation) => invocation with { OutPath = r.GetValue(output) });
        return command;
    }

    private Command VerifyRecoveryKey()
    {
        var command = InstanceCommand("verify-recovery-key", "Verifică o cheie de recuperare existentă (nu modifică nimic).");
        var key = RecoveryKeyOption(command);
        Rebind(command, (r, invocation) => invocation with { RecoveryKeyPath = r.GetValue(key) });
        return command;
    }

    private Command Diagnose()
    {
        var command = InstanceCommand("diagnose", "Pachet ZIP de diagnostic, fără secrete.");
        var output = new Option<string>("--out")
        {
            Description = "Folderul în care se creează pachetul (implicit folderul curent).",
            HelpName = "folder",
        };
        command.Options.Add(output);
        Rebind(command, (r, invocation) => invocation with { OutPath = r.GetValue(output) });
        return command;
    }

    private Command Uninstall()
    {
        var command = InstanceCommand("uninstall", "Elimină site-ul, serviciul și programul; păstrează baza, backup-urile și certificatul de protecție a datelor.");
        var confirm = new Option<string>("--confirm-instance")
        {
            Description = "Confirmarea numelui instanței.",
            HelpName = "INSTANȚĂ",
        };
        AddRequired(command, confirm);
        Rebind(command, (r, invocation) => invocation with { ConfirmInstance = r.GetValue(confirm) });
        return command;
    }

    private Command InstanceCommand(string name, string description, bool unattended = false)
    {
        var command = new Command(name, description);
        var instance = InstanceOption(command);
        var unattendedOption = unattended ? UnattendedOption(command) : null;
        Bind(command, r => Base(r, name) with
        {
            Instance = r.GetValue(instance),
            Unattended = unattendedOption is not null && r.GetValue(unattendedOption),
        });
        return command;
    }

    private Option<string> AnswersOption(Command command)
    {
        var option = new Option<string>("--answers")
        {
            Description = "Fișierul de răspunsuri (JSON, fără parole).",
            HelpName = "fișier",
        };
        AddRequired(command, option);
        return option;
    }

    private Option<string> RecoveryKeyOption(Command command)
    {
        var option = new Option<string>("--recovery-key")
        {
            Description = "Cheia de recuperare (.pfx); parola se cere cu ecou ascuns.",
            HelpName = "fișier.pfx",
        };
        AddRequired(command, option);
        return option;
    }

    private Option<string> InstanceOption(Command command)
    {
        var option = new Option<string>("--instance")
        {
            Description = "Numele instalării (de exemplu PROD).",
            HelpName = "INSTANȚĂ",
        };
        option.Validators.Add(r =>
        {
            var value = r.GetValueOrDefault<string>();
            if (value is null || !InstanceName().IsMatch(value))
            {
                r.AddError($"Numele instalării „{value}” este invalid: 2–10 caractere, majuscule și cifre, începând cu o literă.");
            }
        });
        AddRequired(command, option);
        return option;
    }

    private static Option<bool> UnattendedOption(Command command)
    {
        var option = new Option<bool>("--unattended")
        {
            Description = "Fără întrebări; avertismentele trebuie acceptate în fișierul de răspunsuri (acceptedWarnings).",
        };
        command.Options.Add(option);
        return option;
    }

    private void AddRequired(Command command, Option option)
    {
        command.Options.Add(option);
        if (!requiredOptions.TryGetValue(command, out var list))
        {
            requiredOptions[command] = list = [];
        }

        list.Add(option);
        command.Validators.Add(r =>
        {
            if (r.GetResult(option) is null)
            {
                r.AddError($"Opțiunea {option.Name} este obligatorie pentru comanda {command.Name}.");
            }
        });
    }

    private readonly Dictionary<Command, Func<ParseResult, SetupInvocation>> bindings = [];

    private void Bind(Command command, Func<ParseResult, SetupInvocation> invocation)
    {
        bindings[command] = invocation;
        command.SetAction(r => run(bindings[command](r)));
    }

    /// <summary>Adauga optiunile unei comenzi peste legarea comuna a comenzilor cu --instance.</summary>
    private void Rebind(Command command, Func<ParseResult, SetupInvocation, SetupInvocation> extra)
    {
        var previous = bindings[command];
        bindings[command] = r => extra(r, previous(r));
    }

    private SetupInvocation Base(ParseResult result, string command) => new()
    {
        Command = command,
        LogLevel = result.GetValue(logLevel) ?? "Information",
        NoColor = result.GetValue(noColor),
#if DEBUG
        DevSkipSignature = result.GetValue(devSkipSignature),
#endif
    };

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$", RegexOptions.CultureInvariant)]
    public static partial Regex InstanceName();

    // Mesajele System.CommandLine nu au traducere in romana; le traducem pe cele uzuale.
    internal static string Translate(string message)
    {
        var match = UnrecognizedToken().Match(message);
        if (match.Success)
        {
            return $"Comandă sau argument necunoscut: „{match.Groups[1].Value}”.";
        }

        match = MissingArgument().Match(message);
        if (match.Success)
        {
            return $"Lipsește valoarea pentru opțiunea {match.Groups[1].Value}.";
        }

        match = TooManyArguments().Match(message);
        return match.Success
            ? $"Opțiunea {match.Groups[1].Value} acceptă o singură valoare."
            : message;
    }

    [GeneratedRegex("^Unrecognized command or argument '(.*)'\\.$", RegexOptions.CultureInvariant)]
    private static partial Regex UnrecognizedToken();

    [GeneratedRegex("^Required argument missing for option: '(.*)'\\.$", RegexOptions.CultureInvariant)]
    private static partial Regex MissingArgument();

    [GeneratedRegex("^Option '(.*)' expects a single argument but \\d+ were provided\\.$", RegexOptions.CultureInvariant)]
    private static partial Regex TooManyArguments();
}

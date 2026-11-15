using System.CommandLine;
using System.CommandLine.Help;
using System.Text.RegularExpressions;
using Erp.Setup.Core;

namespace Erp.Setup.Cli;

/// <summary>Comenzile din INSTALARE_ACTUALIZARE.md, 6.1, cu ajutor si mesaje in romana.</summary>
public sealed partial class SetupCommandLine
{
    public const string ExecutableName = "ErpSetup.exe";

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

    private readonly Option<bool> yes = new("--yes")
    {
        Description = "Răspunde „da” la confirmările care nu sunt distructive.",
        Recursive = true,
    };

    private readonly Option<bool> noColor = new("--no-color")
    {
        Description = "Afișare fără culori.",
        Recursive = true,
    };

#if DEBUG
    private readonly Option<bool> devSkipSignature = new("--dev-skip-signature")
    {
        Description = "Numai în build-urile Debug: sare verificarea Authenticode a installer-ului.",
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
        var root = new RootCommand("ErpSetup – installer-ul ERP: instalare, actualizare, reparare și diagnostic.");
        root.Options.Add(logLevel);
        root.Options.Add(yes);
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
        root.Subcommands.Add(Upgrade());
        root.Subcommands.Add(Repair());
        root.Subcommands.Add(ResetAdmin());
        root.Subcommands.Add(Rollback());
        root.Subcommands.Add(Uninstall());
        root.Subcommands.Add(Diagnose());
        root.Subcommands.Add(Status());
        return root;
    }

    private Command Check()
    {
        var command = new Command("check", "Verifică doar cerințele (nu modifică nimic).");
        var answers = AnswersOption(command);
        Bind(command, r => Base(r, "check") with { AnswersPath = r.GetValue(answers) });
        return command;
    }

    private Command Install()
    {
        var command = new Command("install", "Instalare nouă.");
        var answers = AnswersOption(command);
        var unattended = UnattendedOption(command);
        Bind(command, r => Base(r, "install") with
        {
            AnswersPath = r.GetValue(answers),
            Unattended = r.GetValue(unattended),
        });
        return command;
    }

    private Command Upgrade()
    {
        var command = new Command("upgrade", "Actualizare cu kitul curent, cu rollback automat la eșec.");
        var instance = InstanceOption(command);
        var unattended = UnattendedOption(command);
        Bind(command, r => Base(r, "upgrade") with
        {
            Instance = r.GetValue(instance),
            Unattended = r.GetValue(unattended),
        });
        return command;
    }

    private Command Repair()
    {
        var command = new Command("repair", "Refacere IIS, serviciu, configurare, drepturi și obiecte programabile.");
        var instance = InstanceOption(command);
        var rotateSecrets = new Option<bool>("--rotate-secrets")
        {
            Description = "Generează o parolă nouă pentru loginul aplicației și rescrie secretele.",
        };
        var license = new Option<string>("--license")
        {
            Description = "Înlocuiește licența cu fișierul dat.",
            HelpName = "fișier",
        };
        command.Options.Add(rotateSecrets);
        command.Options.Add(license);
        Bind(command, r => Base(r, "repair") with
        {
            Instance = r.GetValue(instance),
            RotateSecrets = r.GetValue(rotateSecrets),
            LicensePath = r.GetValue(license),
        });
        return command;
    }

    private Command ResetAdmin()
    {
        var command = new Command("reset-admin", "Acces de urgență pentru administrator.");
        var instance = InstanceOption(command);
        var user = new Option<string>("--user")
        {
            Description = "Contul resetat; implicit administratorul integrat.",
            HelpName = "nume",
        };
        command.Options.Add(user);
        Bind(command, r => Base(r, "reset-admin") with
        {
            Instance = r.GetValue(instance),
            User = r.GetValue(user),
        });
        return command;
    }

    private Command Rollback()
    {
        var command = new Command("rollback", "Revenire manuală la o actualizare întreruptă.");
        var instance = InstanceOption(command);
        Bind(command, r => Base(r, "rollback") with { Instance = r.GetValue(instance) });
        return command;
    }

    private Command Uninstall()
    {
        var command = new Command("uninstall", "Elimină site-ul, serviciul și binarele; păstrează baza și backup-urile.");
        var instance = InstanceOption(command);
        var unattended = UnattendedOption(command);
        var confirmInstance = new Option<string>("--confirm-instance")
        {
            Description = "Confirmarea numelui instanței, obligatorie cu --unattended.",
            HelpName = "INSTANȚĂ",
        };
        var removeJobs = new Option<bool>("--remove-jobs")
        {
            Description = "Șterge și joburile SQL Agent ale instalării.",
        };
        var removeLogins = new Option<bool>("--remove-logins")
        {
            Description = "Șterge loginurile SQL în loc să le dezactiveze.",
        };
        command.Options.Add(confirmInstance);
        command.Options.Add(removeJobs);
        command.Options.Add(removeLogins);
        command.Validators.Add(r =>
        {
            if (r.GetValue(unattended) && r.GetResult(confirmInstance) is null)
            {
                r.AddError("Cu --unattended, opțiunea --confirm-instance este obligatorie.");
            }
        });
        Bind(command, r => Base(r, "uninstall") with
        {
            Instance = r.GetValue(instance),
            Unattended = r.GetValue(unattended),
            ConfirmInstance = r.GetValue(confirmInstance),
            RemoveJobs = r.GetValue(removeJobs),
            RemoveLogins = r.GetValue(removeLogins),
        });
        return command;
    }

    private Command Diagnose()
    {
        var command = new Command("diagnose", "Pachet ZIP de diagnostic, fără secrete.");
        var instance = InstanceOption(command);
        Bind(command, r => Base(r, "diagnose") with { Instance = r.GetValue(instance) });
        return command;
    }

    private Command Status()
    {
        var command = new Command("status", "Versiune, stare, ultimul backup, starea health-check.");
        var instance = InstanceOption(command);
        Bind(command, r => Base(r, "status") with { Instance = r.GetValue(instance) });
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

    private Option<string> InstanceOption(Command command)
    {
        var option = new Option<string>("--instance")
        {
            Description = "Numele instanței (de exemplu PROD).",
            HelpName = "INSTANȚĂ",
        };
        option.Validators.Add(r =>
        {
            var value = r.GetValueOrDefault<string>();
            if (value is null || !InstanceName().IsMatch(value))
            {
                r.AddError($"Numele instanței „{value}” este invalid: 2–10 caractere, majuscule și cifre, începând cu o literă.");
            }
        });
        AddRequired(command, option);
        return option;
    }

    private static Option<bool> UnattendedOption(Command command)
    {
        var option = new Option<bool>("--unattended")
        {
            Description = "Fără întrebări; parolele se citesc din variabilele de mediu (6.3).",
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

    private void Bind(Command command, Func<ParseResult, SetupInvocation> invocation) =>
        command.SetAction(r => run(invocation(r)));

    private SetupInvocation Base(ParseResult result, string command) => new()
    {
        Command = command,
        LogLevel = result.GetValue(logLevel) ?? "Information",
        Yes = result.GetValue(yes),
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

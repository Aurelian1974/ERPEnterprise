using System.Runtime.Versioning;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.State;
using Erp.Setup.Engine;
using Erp.Setup.Windows.Certificates;

namespace Erp.Setup.Cli;

/// <summary>Comenzile scurte pe o instalare existenta: status (11.7), export-recovery-key si verify-recovery-key (11.4).</summary>
[SupportedOSPlatform("windows")]
internal static class InstanceCommands
{
    public static int Status(CommandContext context)
    {
        if (Read(context) is not { } state)
        {
            return ExitCodes.InvalidCommandLine;
        }

        var console = context.Console;
        console.Info($"Instalarea {state.Instance}: {state.State}, versiunea {state.CurrentVersion ?? "—"} (kitul: {context.Manifest.Version})");
        console.Info($"  Adresa: https://{state.HostName}:{state.HttpsPort}/  ({state.SiteName}, {state.ServiceName})");
        console.Info($"  Baza: {state.DatabaseName} pe {state.SqlServer}; identitatea aplicației: {state.AppIdentity} {state.AppIdentityAccount}");
        console.Info($"  Ultima migrare: {state.LastMigration ?? "—"}");
        console.Info($"  Backup: {(state.BackupEdition == BackupEdition.None ? "fără backup automat (SQL Express)" : state.BackupFolder)}");
        console.Info($"  Certificat HTTPS: {state.CertificateKind} {state.CertificateThumbprint}");
        if (state.RecoveryKeyExportedAt is { } exported)
        {
            console.Info($"  Cheia de recuperare exportată la {exported.ToLocalTime():dd.MM.yyyy HH:mm}");
        }
        else
        {
            console.Warning("  Cheia de recuperare nu a fost exportată: rulați export-recovery-key.");
        }

        return ExitCodes.Success;
    }

    public static int ExportRecoveryKey(CommandContext context)
    {
        if (Read(context) is null)
        {
            return ExitCodes.InvalidCommandLine;
        }

        var password = ReadNewPassword(context.Console);
        if (password is null)
        {
            return ExitCodes.CancelledByOperator;
        }

        var path = Path.GetFullPath(context.Invocation.OutPath!);
        var state = InstanceStateStore.Read(StatePath(context))!;
        var sql = new Erp.Setup.Sql.SqlOperator(new Core.Checks.SqlTarget(state.SqlServer!, state.DatabaseName!, state.Instance, state.TrustServerCertificate),
            context.Log);
        RecoveryKeys.Export(new WindowsSetup(context.Log, context.Time), StatePath(context), path, password, context.Time, sql);
        context.Console.Info($"Cheia de recuperare a fost salvată în {path}.");
        return ExitCodes.Success;
    }

    public static int VerifyRecoveryKey(CommandContext context)
    {
        if (Read(context) is not { } state)
        {
            return ExitCodes.InvalidCommandLine;
        }

        var password = new ConsoleSecretInput().ReadHidden("Parola cheii de recuperare: ");
        if (password is null)
        {
            return ExitCodes.CancelledByOperator;
        }

        var check = RecoveryKeys.Verify(new WindowsSetup(context.Log, context.Time), state, context.Invocation.RecoveryKeyPath!, password);
        if (check == RecoveryKeyCheck.Valid)
        {
            context.Console.Info("Cheia de recuperare e validă și aparține acestei instalări.");
            return ExitCodes.Success;
        }

        context.Console.Error($"{RecoveryKeyPolicy.Message(check)} (cod {ExitCodes.RecoveryKeyInvalid})");
        return ExitCodes.RecoveryKeyInvalid;
    }

    private static string StatePath(CommandContext context) => InstanceStateStore.PathFor(InstallRequest.DefaultDataRoot, context.Invocation.Instance!);

    private static InstanceState? Read(CommandContext context)
    {
        var state = InstanceStateStore.Read(StatePath(context));
        if (state is null)
        {
            context.Console.Error($"Instalarea {context.Invocation.Instance} nu există pe acest calculator.");
        }

        return state;
    }

    private static string? ReadNewPassword(OperatorConsole console)
    {
        var input = new ConsoleSecretInput();
        while (true)
        {
            var first = input.ReadHidden($"Parola cheii de recuperare (minimum {RecoveryKeyPolicy.MinimumPasswordLength} caractere): ");
            if (first is null)
            {
                return null;
            }

            if (first.Length >= RecoveryKeyPolicy.MinimumPasswordLength && input.ReadHidden("Repetați parola: ") == first)
            {
                return first;
            }

            console.Warning("Parola e prea scurtă sau parolele diferă.");
        }
    }
}

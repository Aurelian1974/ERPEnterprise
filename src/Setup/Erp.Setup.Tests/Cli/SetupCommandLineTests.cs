using Erp.Setup.Cli;
using Erp.Setup.Core;

namespace Erp.Setup.Tests.Cli;

public sealed class SetupCommandLineTests
{
    private readonly List<SetupInvocation> invocations = [];
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private SetupCommandLine CreateCli() => new(i =>
    {
        invocations.Add(i);
        return ExitCodes.Success;
    }, output, error);

    [Fact]
    public void Help_lists_all_commands_in_romanian()
    {
        var exitCode = CreateCli().Invoke("--help");

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("Comenzi:", text);
        Assert.Contains("Opțiuni comune:", text);
        foreach (var command in new[]
        {
            "check", "install", "upgrade", "rollback", "repair", "recover", "reset-admin", "export-recovery-key", "verify-recovery-key",
            "diagnose", "uninstall", "status",
        })
        {
            Assert.Contains($"  {command} ", text);
        }

        Assert.Empty(invocations);
    }

    [Fact]
    public void Command_help_is_shown_even_without_required_options()
    {
        var exitCode = CreateCli().Invoke("install", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("ErpSetup.Cli.exe install --answers <fișier> [--unattended] [--recovery-key-out <fișier.pfx>]", output.ToString());
        Assert.Empty(invocations);
    }

    [Theory]
    [InlineData("check --answers a.json", "check")]
    [InlineData("install --answers a.json --unattended --recovery-key-out E:\\cheie.pfx", "install")]
    [InlineData("upgrade --instance PROD --unattended", "upgrade")]
    [InlineData("rollback --instance PROD", "rollback")]
    [InlineData("repair --instance PROD --license x.lic", "repair")]
    [InlineData("recover --answers a.json --recovery-key E:\\cheie.pfx", "recover")]
    [InlineData("reset-admin --instance PROD", "reset-admin")]
    [InlineData("export-recovery-key --instance PROD --out E:\\cheie.pfx", "export-recovery-key")]
    [InlineData("verify-recovery-key --instance PROD --recovery-key E:\\cheie.pfx", "verify-recovery-key")]
    [InlineData("diagnose --instance TEST1 --out C:\\Temp", "diagnose")]
    [InlineData("uninstall --instance PROD --confirm-instance PROD", "uninstall")]
    [InlineData("status --instance PROD --log-level Debug --no-color", "status")]
    public void Valid_command_lines_are_parsed(string commandLine, string command)
    {
        var exitCode = CreateCli().Invoke(commandLine.Split(' '));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(command, Assert.Single(invocations).Command);
    }

    [Fact]
    public void Options_are_mapped_to_the_invocation()
    {
        CreateCli().Invoke("uninstall", "--instance", "PROD", "--confirm-instance", "PROD", "--log-level", "Debug", "--no-color");
        CreateCli().Invoke("install", "--answers", "a.json", "--unattended", "--recovery-key-out", "cheie.pfx");
        CreateCli().Invoke("repair", "--instance", "TEST", "--license", "x.lic");

        var uninstall = invocations[0];
        Assert.Equal("PROD", uninstall.Instance);
        Assert.Equal("PROD", uninstall.ConfirmInstance);
        Assert.Equal("Debug", uninstall.LogLevel);
        Assert.True(uninstall.NoColor);
        Assert.Equal(new SetupInvocation { Command = "install", AnswersPath = "a.json", Unattended = true, RecoveryKeyOut = "cheie.pfx" }, invocations[1]);
        Assert.Equal("x.lic", invocations[2].LicensePath);
        Assert.Equal("TEST", invocations[2].Instance);
    }

    [Fact]
    public void Passwords_cannot_be_given_as_arguments()
    {
        var exitCode = CreateCli().Invoke("install", "--answers", "a.json", "--password", "secret");

        Assert.Equal(ExitCodes.InvalidCommandLine, exitCode);
        Assert.Empty(invocations);
    }

    [Theory]
    [InlineData("install", "Opțiunea --answers este obligatorie pentru comanda install.")]
    [InlineData("upgrade", "Opțiunea --instance este obligatorie pentru comanda upgrade.")]
    [InlineData("upgrade --instance prod", "Numele instalării „prod” este invalid")]
    [InlineData("status --instance P", "Numele instalării „P” este invalid")]
    [InlineData("uninstall --instance PROD", "Opțiunea --confirm-instance este obligatorie pentru comanda uninstall.")]
    [InlineData("recover --answers a.json", "Opțiunea --recovery-key este obligatorie pentru comanda recover.")]
    [InlineData("export-recovery-key --instance PROD", "Opțiunea --out este obligatorie pentru comanda export-recovery-key.")]
    [InlineData("status --instance PROD --log-level Trace", "Valoarea „Trace” pentru --log-level nu este permisă")]
    [InlineData("status --instance PROD --bogus", "Comandă sau argument necunoscut: „--bogus”.")]
    [InlineData("install --answers", "Lipsește valoarea pentru opțiunea --answers.")]
    public void Invalid_command_lines_exit_with_code_3_and_romanian_message(string commandLine, string expectedMessage)
    {
        var exitCode = CreateCli().Invoke(commandLine.Split(' '));

        Assert.Equal(ExitCodes.InvalidCommandLine, exitCode);
        Assert.Contains(expectedMessage, error.ToString());
        Assert.Empty(invocations);
    }

    [Fact]
    public void Missing_command_exits_with_code_3()
    {
        var exitCode = CreateCli().Invoke();

        Assert.Equal(ExitCodes.InvalidCommandLine, exitCode);
        Assert.Contains("Specificați o comandă.", error.ToString());
    }

    [Theory]
    [InlineData(true, false, new string[0], true)]
    [InlineData(true, false, new[] { "check", "--answers", "a.json" }, true)]
    [InlineData(true, false, new[] { "install", "--answers", "a.json", "--unattended" }, false)]
    [InlineData(true, true, new[] { "check", "--answers", "a.json" }, false)]
    [InlineData(false, false, new string[0], false)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Own_console_window_waits_for_enter_except_unattended_or_redirected(bool ownWindow, bool redirected, string[] args, bool expected)
    {
        Assert.Equal(expected, ConsoleWindow.ShouldPause(ownWindow, redirected, args));
    }
}

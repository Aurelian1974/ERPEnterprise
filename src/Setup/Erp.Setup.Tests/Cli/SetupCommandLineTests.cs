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
        foreach (var command in new[] { "check", "install", "upgrade", "repair", "reset-admin", "rollback", "uninstall", "diagnose", "status" })
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
        Assert.Contains("ErpSetup.exe install --answers <fișier> [--unattended]", output.ToString());
        Assert.Empty(invocations);
    }

    [Theory]
    [InlineData("check --answers a.json", "check")]
    [InlineData("install --answers a.json --unattended", "install")]
    [InlineData("upgrade --instance PROD --unattended", "upgrade")]
    [InlineData("repair --instance PROD --rotate-secrets --license x.lic", "repair")]
    [InlineData("reset-admin --instance PROD --user ion.pop", "reset-admin")]
    [InlineData("rollback --instance PROD", "rollback")]
    [InlineData("uninstall --instance PROD --unattended --confirm-instance PROD --remove-jobs --remove-logins", "uninstall")]
    [InlineData("diagnose --instance TEST1", "diagnose")]
    [InlineData("status --instance PROD --log-level Debug --yes --no-color", "status")]
    public void Valid_command_lines_are_parsed(string commandLine, string command)
    {
        var exitCode = CreateCli().Invoke(commandLine.Split(' '));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(command, Assert.Single(invocations).Command);
    }

    [Fact]
    public void Options_are_mapped_to_the_invocation()
    {
        CreateCli().Invoke("uninstall", "--instance", "PROD", "--unattended", "--confirm-instance", "PROD", "--remove-jobs", "--log-level", "Debug", "--no-color");

        var invocation = Assert.Single(invocations);
        Assert.Equal("PROD", invocation.Instance);
        Assert.True(invocation.Unattended);
        Assert.Equal("PROD", invocation.ConfirmInstance);
        Assert.True(invocation.RemoveJobs);
        Assert.False(invocation.RemoveLogins);
        Assert.Equal("Debug", invocation.LogLevel);
        Assert.True(invocation.NoColor);
    }

    [Theory]
    [InlineData("install", "Opțiunea --answers este obligatorie pentru comanda install.")]
    [InlineData("upgrade", "Opțiunea --instance este obligatorie pentru comanda upgrade.")]
    [InlineData("upgrade --instance prod", "Numele instanței „prod” este invalid")]
    [InlineData("status --instance P", "Numele instanței „P” este invalid")]
    [InlineData("uninstall --instance PROD --unattended", "Cu --unattended, opțiunea --confirm-instance este obligatorie.")]
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

using Erp.Setup.Core;
using Erp.Setup.Core.Secrets;

namespace Erp.Setup.Tests.Answers;

public sealed class SecretReaderTests
{
    private const string StrongPassword = "Wq7#Hn2p+Kd9!mXa";

    private sealed class FakeInput(params string?[] answers) : ISecretInput
    {
        private readonly Queue<string?> answers = new(answers);

        public List<string> Lines { get; } = [];

        public string? ReadHidden(string prompt) => answers.Dequeue();

        public void WriteLine(string message) => Lines.Add(message);
    }

    private static Func<string, string?> Env(string? name = null, string? value = null) =>
        n => n == name ? value : null;

    [Fact]
    public void Unattended_operator_password_comes_from_the_environment()
    {
        var result = new SecretReader(Env(SecretReader.OperatorPasswordVariable, "p@ss"), null).ReadOperatorSqlPassword(unattended: true);

        Assert.Equal(SecretOutcome.Provided, result.Outcome);
        Assert.Equal("p@ss", result.Value);
    }

    [Fact]
    public void Unattended_operator_password_missing_exits_with_code_11()
    {
        var result = new SecretReader(Env(), null).ReadOperatorSqlPassword(unattended: true);

        Assert.Equal(SecretOutcome.Missing, result.Outcome);
        Assert.Equal(ExitCodes.InvalidAnswersFile, result.ExitCode);
        Assert.Contains(SecretReader.OperatorPasswordVariable, result.Message);
    }

    [Fact]
    public void Interactive_operator_password_is_asked_and_cancel_exits_with_code_2()
    {
        Assert.Equal("x", new SecretReader(Env(), new FakeInput("", "x")).ReadOperatorSqlPassword(false).Value);
        Assert.Equal(ExitCodes.CancelledByOperator, new SecretReader(Env(), new FakeInput([null])).ReadOperatorSqlPassword(false).ExitCode);
    }

    [Fact]
    public void Unattended_admin_password_without_variable_is_generated()
    {
        var result = new SecretReader(Env(), null).ReadInitialAdminPassword(unattended: true, "administrator");

        Assert.Equal(SecretOutcome.Generate, result.Outcome);
    }

    [Fact]
    public void Unattended_admin_password_from_variable_is_validated()
    {
        var accepted = new SecretReader(Env(SecretReader.AdminPasswordVariable, StrongPassword), null).ReadInitialAdminPassword(true, "administrator");
        var rejected = new SecretReader(Env(SecretReader.AdminPasswordVariable, "short"), null).ReadInitialAdminPassword(true, "administrator");

        Assert.Equal(SecretOutcome.Provided, accepted.Outcome);
        Assert.True(accepted.FromEnvironment);
        Assert.Equal(SecretOutcome.Rejected, rejected.Outcome);
        Assert.Equal(ExitCodes.InvalidAnswersFile, rejected.ExitCode);
        Assert.DoesNotContain("short", rejected.Message);
    }

    [Fact]
    public void Interactive_admin_password_is_asked_again_when_rejected_or_not_confirmed()
    {
        var input = new FakeInput("weak", StrongPassword, "different", StrongPassword, StrongPassword);

        var result = new SecretReader(Env(), input).ReadInitialAdminPassword(false, "administrator");

        Assert.Equal(SecretOutcome.Provided, result.Outcome);
        Assert.Equal(StrongPassword, result.Value);
        Assert.False(result.FromEnvironment);
        Assert.Contains("Parolele nu coincid.", input.Lines);
    }

    [Fact]
    public void Interactive_empty_admin_password_means_generated()
    {
        var result = new SecretReader(Env(), new FakeInput("")).ReadInitialAdminPassword(false, "administrator");

        Assert.Equal(SecretOutcome.Generate, result.Outcome);
    }

    [Theory]
    [InlineData("Wq7#Hn2p+Kd9", true)]
    [InlineData("Wq7#Hn2p+Kd", false)]
    [InlineData("wq7#hn2p+kd9!", false)]
    [InlineData("WQ7#HN2P+KD9!", false)]
    [InlineData("Wqx#Hnyp+Kdz!", false)]
    [InlineData("Wq7aHn2pbKd9c", false)]
    [InlineData("xAdministrator7#", false)]
    public void Initial_password_policy(string password, bool valid)
    {
        Assert.Equal(valid, InitialPasswordPolicy.Validate(password, "administrator").Count == 0);
    }
}

namespace Erp.Setup.Cli;

/// <summary>Afisarea pentru operator (6.6): avertismente in galben, erori in rosu, respecta --no-color.</summary>
public sealed class OperatorConsole(TextWriter output, TextWriter error, bool useColor)
{
    public void Info(string message) => output.WriteLine(message);

    public void Warning(string message) => Write(output, ConsoleColor.Yellow, message);

    public void Error(string message) => Write(error, ConsoleColor.Red, message);

    private void Write(TextWriter writer, ConsoleColor color, string message)
    {
        if (!useColor)
        {
            writer.WriteLine(message);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try
        {
            writer.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}

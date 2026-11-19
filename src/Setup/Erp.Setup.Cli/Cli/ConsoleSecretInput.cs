using System.Text;

namespace Erp.Setup.Cli;

/// <summary>Citirea parolelor fara ecou (08, 6.1): parolele nu se dau niciodata ca argumente.</summary>
public interface ISecretInput
{
    /// <returns>null daca operatorul a apasat Esc.</returns>
    string? ReadHidden(string prompt);

    void WriteLine(string message);
}

/// <summary>Citire de la consola cu ecou ascuns (08, 6.1).</summary>
public sealed class ConsoleSecretInput : ISecretInput
{
    public string? ReadHidden(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine();
            Console.WriteLine();
            return line;
        }

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return buffer.ToString();
                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return null;
                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                    }

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Append(key.KeyChar);
                    }

                    break;
            }
        }
    }

    public void WriteLine(string message) => Console.WriteLine(message);
}

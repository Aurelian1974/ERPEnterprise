using System.Text;
using Erp.Setup.Core.Secrets;

namespace Erp.Setup.Cli;

/// <summary>Citire de la consola cu ecou ascuns (6.3).</summary>
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

using System.Security.Cryptography;

namespace Erp.Setup.Core.Secrets;

/// <summary>
/// Parola temporara a administratorului (8.8, punctul 2): 20 de caractere fara caractere usor de confundat,
/// cel putin 2 din fiecare categorie, cu RandomNumberGenerator.
/// </summary>
public static class InitialPasswordGenerator
{
    public const int Length = 20;

    private static readonly string[] Categories =
    [
        "ABCDEFGHJKLMNPQRSTUVWXYZ",
        "abcdefghijkmnpqrstuvwxyz",
        "23456789",
        "!#$%&*+-=?@_",
    ];

    public static readonly string Alphabet = string.Concat(Categories);

    public static string Generate()
    {
        var characters = new char[Length];
        var position = 0;
        foreach (var category in Categories)
        {
            for (var i = 0; i < 2; i++)
            {
                characters[position++] = category[RandomNumberGenerator.GetInt32(category.Length)];
            }
        }

        while (position < Length)
        {
            characters[position++] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }
}

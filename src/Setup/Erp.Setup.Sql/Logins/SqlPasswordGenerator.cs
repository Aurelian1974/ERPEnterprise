using System.Security.Cryptography;

namespace Erp.Setup.Sql.Logins;

/// <summary>
/// Parolele loginurilor SQL (8.4): 32 de caractere din A–Z a–z 0–9 !#%*+-=?@_ (fara apostrof, ghilimele, punct si virgula),
/// cel putin 2 din fiecare categorie, cu RandomNumberGenerator. Nu se afiseaza si nu se jurnalizeaza.
/// </summary>
public static class SqlPasswordGenerator
{
    public const int Length = 32;

    private static readonly string[] Categories =
    [
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "abcdefghijklmnopqrstuvwxyz",
        "0123456789",
        "!#%*+-=?@_",
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

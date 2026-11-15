namespace Erp.Setup.Sql;

/// <summary>
/// Citarea identificatorilor si literalilor pentru textul SQL construit in C# (8.2, abaterea de la SP-06 din 17).
/// Valorile trebuie validate inainte (expresii regulate / liste permise); citarea nu inlocuieste validarea.
/// </summary>
public static class SqlText
{
    /// <summary>Echivalentul QUOTENAME(): intre paranteze drepte, cu „]” dublat.</summary>
    public static string QuoteName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Length > 128)
        {
            throw new ArgumentException("Un identificator SQL are cel mult 128 de caractere.", nameof(name));
        }

        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <summary>Literal Unicode: prefix N, apostroful dublat.</summary>
    public static string SqlLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}

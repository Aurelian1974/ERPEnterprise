using System.Globalization;
using System.Text;

namespace Erp.Finance.Domain.Reporting;

/// <summary>Functia pe conturi a unei formule: SD, SC sold final debitor / creditor, SN sold net, RD, RC rulaj debitor / creditor (fara inchideri).</summary>
public enum AccountMeasure : byte
{
    ClosingDebit = 1,
    ClosingCredit = 2,
    ClosingNet = 3,
    TurnoverDebit = 4,
    TurnoverCredit = 5,
}

/// <summary>Un interval de conturi (cu analiticele): „5121” sau „701..708”.</summary>
public sealed record AccountRange(string From, string To)
{
    /// <summary>Contul (sau analiticul lui) cade in interval: primele cifre ale codului sintetic, comparate ca text de aceeasi lungime.</summary>
    public bool Matches(string accountCode)
    {
        ArgumentNullException.ThrowIfNull(accountCode);
        var synthetic = accountCode.Split('.')[0];
        if (synthetic.Length < From.Length)
        {
            return false;
        }

        var head = synthetic[..From.Length];
        return string.CompareOrdinal(head, From) >= 0 && string.CompareOrdinal(head, To) <= 0;
    }
}

public abstract record FormulaNode;

public sealed record NumberNode(decimal Value) : FormulaNode;

public sealed record NegateNode(FormulaNode Operand) : FormulaNode;

public sealed record BinaryNode(char Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

public sealed record RowNode(string RowCode) : FormulaNode;

/// <summary>SD/SC/SN/RD/RC pe conturi, cu filtrul optional de dimensiune („CC=LOG”, „PR=P1”).</summary>
public sealed record AccountNode(AccountMeasure Measure, IReadOnlyList<AccountRange> Accounts, string? DimensionKind, string? DimensionCode) : FormulaNode;

public sealed record FunctionNode(string Name, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>Formula gresita (sintaxa, cont inexistent, referinta circulara): codul 51974, FIN_REPORT_FORMULA_INVALID.</summary>
public sealed class ReportFormulaException(string message) : Exception(message);

/// <summary>
/// Formulele rapoartelor definite (C5 3.5, SEC-03): o gramatica mica, interpretata in .NET, nu SQL din text liber. Expresii cu + − * /, paranteze,
/// numere, referinte la randuri [F20.05], functiile pe conturi SD SC SN RD RC (cont, interval 701..708, lista, filtru „| CC=LOG”) si
/// MAX, MIN, ABS.
/// </summary>
public static class ReportFormula
{
    private static readonly Dictionary<string, AccountMeasure> Measures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SD"] = AccountMeasure.ClosingDebit, ["SC"] = AccountMeasure.ClosingCredit, ["SN"] = AccountMeasure.ClosingNet,
        ["RD"] = AccountMeasure.TurnoverDebit, ["RC"] = AccountMeasure.TurnoverCredit,
    };

    private static readonly string[] MathFunctions = ["MAX", "MIN", "ABS"];

    public static FormulaNode Parse(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        var parser = new Parser(formula);
        var node = parser.Expression();
        parser.SkipSpaces();
        if (!parser.AtEnd)
        {
            throw new ReportFormulaException($"Formula „{formula}”: caracter neașteptat la poziția {parser.Position + 1}.");
        }

        return node;
    }

    /// <summary>Randurile si conturile la care se refera o formula.</summary>
    public static (IReadOnlySet<string> Rows, IReadOnlyList<AccountRange> Accounts) References(FormulaNode node)
    {
        var rows = new HashSet<string>(StringComparer.Ordinal);
        var accounts = new List<AccountRange>();
        void Walk(FormulaNode n)
        {
            switch (n)
            {
                case RowNode r: rows.Add(r.RowCode); break;
                case AccountNode a: accounts.AddRange(a.Accounts); break;
                case NegateNode g: Walk(g.Operand); break;
                case BinaryNode b: Walk(b.Left); Walk(b.Right); break;
                case FunctionNode f: f.Arguments.ToList().ForEach(Walk); break;
            }
        }

        Walk(node);
        return (rows, accounts);
    }

    /// <summary>
    /// Validarea definitiei la salvare (C5-A8): fiecare formula se parseaza, fiecare interval de conturi are cel putin un cont in plan, fiecare
    /// rand referit exista, iar randurile nu formeaza un ciclu. Intoarce formulele parsate, pe cod de rand.
    /// </summary>
    public static IReadOnlyDictionary<string, FormulaNode> Validate(IReadOnlyDictionary<string, string> formulas, IReadOnlyCollection<string> accountCodes)
    {
        ArgumentNullException.ThrowIfNull(formulas);
        ArgumentNullException.ThrowIfNull(accountCodes);
        var parsed = formulas.ToDictionary(f => f.Key, f => Parse(f.Value), StringComparer.Ordinal);
        foreach (var (row, node) in parsed)
        {
            var (rows, accounts) = References(node);
            if (accounts.FirstOrDefault(a => !accountCodes.Any(a.Matches)) is { } missing)
            {
                throw new ReportFormulaException($"Rândul {row}: contul {missing.From}{(missing.To != missing.From ? ".." + missing.To : string.Empty)} nu există în plan.");
            }

            if (rows.FirstOrDefault(r => !parsed.ContainsKey(r)) is { } unknown)
            {
                throw new ReportFormulaException($"Rândul {row}: rândul [{unknown}] nu există în raport.");
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        void Visit(string row, Stack<string> path)
        {
            if (state.GetValueOrDefault(row) == 2)
            {
                return;
            }

            if (state.GetValueOrDefault(row) == 1)
            {
                throw new ReportFormulaException($"Referință circulară: {string.Join(" → ", path.Reverse().SkipWhile(r => r != row))} → {row}.");
            }

            state[row] = 1;
            path.Push(row);
            foreach (var next in References(parsed[row]).Rows)
            {
                Visit(next, path);
            }

            path.Pop();
            state[row] = 2;
        }

        foreach (var row in parsed.Keys)
        {
            Visit(row, new Stack<string>());
        }

        return parsed;
    }

    /// <summary>Calculeaza randurile (validate) cu valorile pe conturi date de <paramref name="measure"/>; randurile referite se calculeaza o data.</summary>
    public static IReadOnlyDictionary<string, decimal> Evaluate(IReadOnlyDictionary<string, FormulaNode> rows, Func<AccountNode, decimal> measure)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(measure);
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        decimal Row(string code)
        {
            if (!values.TryGetValue(code, out var v))
            {
                v = decimal.Round(Eval(rows[code]), 2);
                values[code] = v;
            }

            return v;
        }

        decimal Eval(FormulaNode n) => n switch
        {
            NumberNode x => x.Value,
            NegateNode g => -Eval(g.Operand),
            RowNode r => Row(r.RowCode),
            AccountNode a => measure(a),
            BinaryNode b => b.Operator switch
            {
                '+' => Eval(b.Left) + Eval(b.Right),
                '-' => Eval(b.Left) - Eval(b.Right),
                '*' => Eval(b.Left) * Eval(b.Right),
                _ => Eval(b.Right) is var d && d == 0 ? 0 : Eval(b.Left) / d,
            },
            FunctionNode f => f.Name switch
            {
                "MAX" => f.Arguments.Select(Eval).Max(),
                "MIN" => f.Arguments.Select(Eval).Min(),
                _ => Math.Abs(Eval(f.Arguments[0])),
            },
            _ => 0,
        };

        foreach (var code in rows.Keys)
        {
            Row(code);
        }

        return values;
    }

    private sealed class Parser(string text)
    {
        public int Position { get; private set; }

        public bool AtEnd => Position >= text.Length;

        public void SkipSpaces()
        {
            while (!AtEnd && char.IsWhiteSpace(text[Position]))
            {
                Position++;
            }
        }

        public FormulaNode Expression()
        {
            var left = Term();
            while (Peek() is '+' or '-')
            {
                var op = text[Position++];
                left = new BinaryNode(op, left, Term());
            }

            return left;
        }

        private FormulaNode Term()
        {
            var left = Factor();
            while (Peek() is '*' or '/')
            {
                var op = text[Position++];
                left = new BinaryNode(op, left, Factor());
            }

            return left;
        }

        private FormulaNode Factor()
        {
            var c = Peek();
            if (c == '-')
            {
                Position++;
                return new NegateNode(Factor());
            }

            if (c == '(')
            {
                Position++;
                var inner = Expression();
                Expect(')');
                return inner;
            }

            if (c == '[')
            {
                Position++;
                var start = Position;
                while (!AtEnd && text[Position] != ']')
                {
                    Position++;
                }

                var code = text[start..Position].Trim();
                Expect(']');
                return code.Length == 0 ? throw Error("rând gol") : new RowNode(code);
            }

            if (c is >= '0' and <= '9')
            {
                return new NumberNode(decimal.Parse(Read(ch => char.IsDigit(ch) || ch == '.'), CultureInfo.InvariantCulture));
            }

            if (c is not null && char.IsLetter(c.Value))
            {
                var name = Read(char.IsLetter).ToUpperInvariant();
                Expect('(');
                if (Measures.TryGetValue(name, out var measure))
                {
                    return Accounts(measure);
                }

                if (!MathFunctions.Contains(name))
                {
                    throw Error($"funcția {name} nu există");
                }

                var args = new List<FormulaNode> { Expression() };
                while (Peek() == ',')
                {
                    Position++;
                    args.Add(Expression());
                }

                Expect(')');
                return name == "ABS" && args.Count != 1 ? throw Error("ABS primește un singur argument") : new FunctionNode(name, args);
            }

            throw Error(AtEnd ? "formula se termină prea devreme" : $"„{text[Position]}” neașteptat");
        }

        private AccountNode Accounts(AccountMeasure measure)
        {
            var ranges = new List<AccountRange>();
            do
            {
                if (Peek() == ',')
                {
                    Position++;
                }

                SkipSpaces();
                var from = Read(ch => char.IsLetterOrDigit(ch) || ch == '.' && Position + 1 < text.Length && text[Position + 1] != '.');
                var to = from;
                if (text.AsSpan(Position).StartsWith(".."))
                {
                    Position += 2;
                    to = Read(char.IsLetterOrDigit);
                }

                if (from.Length == 0 || to.Length != from.Length)
                {
                    throw Error("cont sau interval de conturi greșit");
                }

                ranges.Add(new AccountRange(from, to));
            }
            while (Peek() == ',');

            string? kind = null, code = null;
            if (Peek() == '|')
            {
                Position++;
                SkipSpaces();
                kind = Read(char.IsLetter).ToUpperInvariant();
                Expect('=');
                SkipSpaces();
                code = Read(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
                if (kind is not ("CC" or "PR") || code.Length == 0)
                {
                    throw Error("filtrul de dimensiune e CC=cod sau PR=cod");
                }
            }

            Expect(')');
            return new AccountNode(measure, ranges, kind, code);
        }

        private string Read(Func<char, bool> accept)
        {
            SkipSpaces();
            var sb = new StringBuilder();
            while (!AtEnd && accept(text[Position]))
            {
                sb.Append(text[Position++]);
            }

            return sb.ToString();
        }

        private char? Peek()
        {
            SkipSpaces();
            return AtEnd ? null : text[Position];
        }

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw Error($"lipsește „{c}”");
            }

            Position++;
        }

        private ReportFormulaException Error(string what) => new($"Formula „{text}”: {what} (poziția {Position + 1}).");
    }
}

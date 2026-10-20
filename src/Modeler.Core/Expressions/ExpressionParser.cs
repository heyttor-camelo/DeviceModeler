namespace Modeler.Core.Expressions;

/// <summary>ExpressionParser descendente recursivo. Precedência (menor → maior):
/// <c>ou</c>, <c>e</c>, <c>nao</c>, comparações, <c>+ -</c>, <c>* / %</c>, unário <c>-</c>, <c>^</c>
/// (associativo à direita), pós-fixos (<c>.campo</c>, <c>[i]</c>, chamada).</summary>
internal sealed class ExpressionParser
{
    private static readonly HashSet<string> OrWords = new(StringComparer.Ordinal) { "ou", "or" };
    private static readonly HashSet<string> AndWords = new(StringComparer.Ordinal) { "e", "and" };
    private static readonly HashSet<string> NotWords = new(StringComparer.Ordinal) { "nao", "não", "not" };
    private static readonly HashSet<string> TrueWords = new(StringComparer.Ordinal) { "verdadeiro", "true" };
    private static readonly HashSet<string> FalseWords = new(StringComparer.Ordinal) { "falso", "false" };
    private static readonly HashSet<string> Comparisons = new(StringComparer.Ordinal) { "==", "!=", "<>", "<", "<=", ">", ">=" };

    /// <summary>Palavras que não podem ser usadas como nome de parâmetro/variável.</summary>
    public static readonly IReadOnlySet<string> Keywords = new HashSet<string>(
        OrWords.Concat(AndWords).Concat(NotWords).Concat(TrueWords).Concat(FalseWords), StringComparer.Ordinal);

    private readonly List<Token> _tokens;
    private int _index;

    private ExpressionParser(string text)
    {
        _tokens = Lexer.Tokenize(text);
    }

    private Token Current => _tokens[_index];

    private Token Peek => _tokens[Math.Min(_index + 1, _tokens.Count - 1)];

    public static Node Parse(string text)
    {
        var ExpressionParser = new ExpressionParser(text);
        if (ExpressionParser.Current.Kind == TokenKind.End)
        {
            throw new ExpressionException("expressão vazia", 0);
        }

        var node = ExpressionParser.ParseOr();
        if (ExpressionParser.Current.Kind != TokenKind.End)
        {
            throw ExpressionParser.Unexpected();
        }

        return node;
    }

    private Node ParseOr()
    {
        var left = ParseAnd();
        while (IsWord(OrWords) || IsSymbol("||"))
        {
            var pos = Advance().Position;
            left = new BinaryNode("or", left, ParseAnd(), pos);
        }

        return left;
    }

    private Node ParseAnd()
    {
        var left = ParseNot();
        while (IsWord(AndWords) || IsSymbol("&&"))
        {
            var pos = Advance().Position;
            left = new BinaryNode("and", left, ParseNot(), pos);
        }

        return left;
    }

    private Node ParseNot()
    {
        if (IsWord(NotWords) || IsSymbol("!"))
        {
            var pos = Advance().Position;
            return new UnaryNode("not", ParseNot(), pos);
        }

        return ParseComparison();
    }

    private Node ParseComparison()
    {
        var left = ParseAdditive();
        if (IsSymbol("="))
        {
            throw new ExpressionException("use '==' para comparar ('=' só vale em atribuições e argumentos nomeados)", Current.Position);
        }

        if (Current.Kind == TokenKind.Symbol && Comparisons.Contains(Current.Text))
        {
            var op = Advance();
            var right = ParseAdditive();
            if (Current.Kind == TokenKind.Symbol && Comparisons.Contains(Current.Text))
            {
                throw new ExpressionException("comparações encadeadas não são permitidas; use 'e' (ex.: a < b e b < c)", Current.Position);
            }

            return new BinaryNode(op.Text == "<>" ? "!=" : op.Text, left, right, op.Position);
        }

        return left;
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (IsSymbol("+") || IsSymbol("-"))
        {
            var op = Advance();
            left = new BinaryNode(op.Text, left, ParseMultiplicative(), op.Position);
        }

        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParseUnary();
        while (IsSymbol("*") || IsSymbol("/") || IsSymbol("%"))
        {
            var op = Advance();
            left = new BinaryNode(op.Text, left, ParseUnary(), op.Position);
        }

        return left;
    }

    private Node ParseUnary()
    {
        if (IsSymbol("-") || IsSymbol("+"))
        {
            var op = Advance();
            return new UnaryNode(op.Text, ParseUnary(), op.Position);
        }

        return ParsePower();
    }

    private Node ParsePower()
    {
        var baseNode = ParsePostfix();
        if (IsSymbol("^"))
        {
            var pos = Advance().Position;
            // Expoente pode ter sinal (2^-1) e é associativo à direita (2^3^2 = 2^9).
            return new BinaryNode("^", baseNode, ParseUnary(), pos);
        }

        return baseNode;
    }

    private Node ParsePostfix()
    {
        var node = ParsePrimary();
        while (true)
        {
            if (IsSymbol("."))
            {
                Advance();
                if (Current.Kind != TokenKind.Identifier)
                {
                    throw new ExpressionException("esperado nome de campo após '.'", Current.Position);
                }

                var member = Advance();
                node = new MemberNode(node, member.Text, member.Position);
            }
            else if (IsSymbol("["))
            {
                var pos = Advance().Position;
                var index = ParseOr();
                Expect("]");
                node = new IndexNode(node, index, pos);
            }
            else if (IsSymbol("("))
            {
                if (node is not IdentifierNode function)
                {
                    throw new ExpressionException("só é possível chamar funções pelo nome", Current.Position);
                }

                Advance();
                node = new CallNode(function.Name, ParseArguments(), function.Position);
            }
            else
            {
                return node;
            }
        }
    }

    private List<Argument> ParseArguments()
    {
        var args = new List<Argument>();
        if (IsSymbol(")"))
        {
            Advance();
            return args;
        }

        while (true)
        {
            string? name = null;
            if (Current.Kind == TokenKind.Identifier && Peek is { Kind: TokenKind.Symbol, Text: "=" })
            {
                name = Advance().Text;
                Advance();
            }
            else if (args.Any(a => a.Name is not null))
            {
                throw new ExpressionException("argumentos posicionais não podem vir depois de argumentos nomeados", Current.Position);
            }

            args.Add(new Argument(name, ParseOr()));
            if (IsSymbol(","))
            {
                Advance();
                continue;
            }

            Expect(")");
            return args;
        }
    }

    private Node ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Advance();
                return new NumberNode(token.Number, token.Position);
            case TokenKind.String:
                Advance();
                return new StringNode(token.Text, token.Position);
            case TokenKind.Identifier:
                Advance();
                if (TrueWords.Contains(token.Text))
                {
                    return new BoolNode(true, token.Position);
                }

                if (FalseWords.Contains(token.Text))
                {
                    return new BoolNode(false, token.Position);
                }

                if (Keywords.Contains(token.Text))
                {
                    throw new ExpressionException($"palavra reservada '{token.Text}' fora de lugar", token.Position);
                }

                return new IdentifierNode(token.Text, token.Position);
            case TokenKind.Symbol when token.Text == "(":
            {
                Advance();
                var inner = ParseOr();
                Expect(")");
                return inner;
            }

            case TokenKind.Symbol when token.Text == "[":
            {
                Advance();
                var items = new List<Node>();
                if (!IsSymbol("]"))
                {
                    do
                    {
                        items.Add(ParseOr());
                    }
                    while (TryConsume(","));
                }

                Expect("]");
                return new ListNode(items, token.Position);
            }

            case TokenKind.End:
                throw new ExpressionException("expressão incompleta: esperado um valor", token.Position);
            default:
                throw Unexpected();
        }
    }

    private bool IsSymbol(string symbol) => Current.Kind == TokenKind.Symbol && Current.Text == symbol;

    private bool IsWord(HashSet<string> words) => Current.Kind == TokenKind.Identifier && words.Contains(Current.Text);

    private Token Advance() => _tokens[_index++];

    private bool TryConsume(string symbol)
    {
        if (!IsSymbol(symbol))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void Expect(string symbol)
    {
        if (!TryConsume(symbol))
        {
            throw Current.Kind == TokenKind.End
                ? new ExpressionException($"expressão incompleta: esperado '{symbol}'", Current.Position)
                : new ExpressionException($"esperado '{symbol}', encontrado '{Current.Text}'", Current.Position);
        }
    }

    private ExpressionException Unexpected() =>
        Current.Kind == TokenKind.End
            ? new ExpressionException("expressão incompleta", Current.Position)
            : new ExpressionException($"'{Current.Text}' inesperado", Current.Position);
}

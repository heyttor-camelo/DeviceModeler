namespace Modeler.Core.Expressions;

/// <summary>Erro de léxico, sintaxe ou avaliação dentro de uma expressão.
/// <see cref="Position"/> é o deslocamento (base 0) no texto da expressão.</summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(string message, int position) : base(message)
    {
        Position = position;
    }

    public int Position { get; }
}

namespace Hydro.Core.Expressions;

/// <summary>Erro de léxico, sintaxe ou avaliação dentro de uma expressão.
/// <see cref="Position"/> é o deslocamento (base 0) no texto da expressão.</summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(string message, int position, bool missing = false) : base(message)
    {
        Position = position;
        Missing = missing;
    }

    public int Position { get; }

    /// <summary>O valor pedido não existe (campo ausente num registro, índice fora da lista ou
    /// <c>nulo</c>): é o único erro que <c>existe()</c> e <c>ou()</c> absorvem.</summary>
    public bool Missing { get; }
}

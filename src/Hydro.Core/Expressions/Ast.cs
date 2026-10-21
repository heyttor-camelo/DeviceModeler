namespace Hydro.Core.Expressions;

/// <summary>Nó da árvore sintática de uma expressão. <c>Position</c> é o deslocamento no texto.</summary>
public abstract record Node(int Position);

public sealed record NumberNode(double Value, int Position) : Node(Position);

public sealed record StringNode(string Value, int Position) : Node(Position);

public sealed record BoolNode(bool Value, int Position) : Node(Position);

/// <summary>Literal <c>nulo</c> (ausência de valor, ver <c>existe()</c> e <c>ou()</c>).</summary>
public sealed record NullNode(int Position) : Node(Position);

public sealed record ListNode(IReadOnlyList<Node> Items, int Position) : Node(Position);

public sealed record IdentifierNode(string Name, int Position) : Node(Position);

/// <summary>Acesso a campo: <c>c.diametro</c>.</summary>
public sealed record MemberNode(Node Target, string Member, int Position) : Node(Position);

/// <summary>Índice em lista: <c>lista[0]</c>.</summary>
public sealed record IndexNode(Node Target, Node Index, int Position) : Node(Position);

/// <summary>Operador unário: <c>-</c>, <c>+</c> ou <c>not</c>.</summary>
public sealed record UnaryNode(string Operator, Node Operand, int Position) : Node(Position);

/// <summary>Operador binário. Operadores lógicos são normalizados para <c>and</c>/<c>or</c>;
/// diferente para <c>!=</c>.</summary>
public sealed record BinaryNode(string Operator, Node Left, Node Right, int Position) : Node(Position);

public sealed record Argument(string? Name, Node Value);

public sealed record CallNode(string Name, IReadOnlyList<Argument> Arguments, int Position) : Node(Position);

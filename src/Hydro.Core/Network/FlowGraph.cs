namespace Hydro.Core.Network;

/// <summary>Nó do grafo: um dispositivo (pontual).</summary>
public sealed record FlowNode(string Id, string Name, double X, double Y);

/// <summary>Trecho entre dois dispositivos: um tubo, com as elevações de fundo nas duas pontas. O
/// fluxo vai da ponta mais alta para a mais baixa (empate: de <see cref="From"/> para <see cref="To"/>).</summary>
public sealed record FlowEdge(string Id, string From, string To, double FromInvert, double ToInvert, double Length)
{
    public (string Up, string Down) Direction => ToInvert > FromInvert + 1e-9 ? (To, From) : (From, To);
}

public enum NumberingMode
{
    /// <summary>Da cabeceira mais distante para a descarga: o fluxo vai sempre do menor para o maior número.</summary>
    Global,

    /// <summary>Ao contrário: a descarga é 1.</summary>
    Invertido,
}

/// <summary>Rede como grafo dirigido pelo fluxo (estudo C §1.4): componentes conexos (as redes),
/// montante e jusante de cada nó, ciclos (que uma rede de gravidade não pode ter) e numeração.</summary>
public sealed class FlowGraph
{
    private readonly Dictionary<string, FlowNode> _nodes;
    private readonly List<FlowEdge> _edges;
    private readonly Dictionary<string, List<FlowEdge>> _out = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FlowEdge>> _in = new(StringComparer.Ordinal);

    public FlowGraph(IEnumerable<FlowNode> nodes, IEnumerable<FlowEdge> edges)
    {
        _nodes = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _edges = edges.Where(e => _nodes.ContainsKey(e.From) && _nodes.ContainsKey(e.To) && e.From != e.To).ToList();
        foreach (var node in _nodes.Keys)
        {
            _out[node] = new List<FlowEdge>();
            _in[node] = new List<FlowEdge>();
        }

        foreach (var edge in _edges)
        {
            var (up, down) = edge.Direction;
            _out[up].Add(edge);
            _in[down].Add(edge);
        }
    }

    public IReadOnlyCollection<FlowNode> Nodes => _nodes.Values;

    public IReadOnlyList<FlowEdge> Edges => _edges;

    public IReadOnlyList<FlowEdge> Downstream(string node) => _out[node];

    public IReadOnlyList<FlowEdge> Upstream(string node) => _in[node];

    /// <summary>Componentes conexos (ignorando o sentido), do maior para o menor.</summary>
    public List<List<FlowNode>> Components()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<List<FlowNode>>();
        foreach (var start in _nodes.Keys.OrderBy(k => _nodes[k].Name, StringComparer.Ordinal))
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var component = new List<FlowNode>();
            var stack = new Stack<string>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                component.Add(_nodes[id]);
                foreach (var e in _out[id].Concat(_in[id]))
                {
                    var other = e.From == id ? e.To : e.From;
                    if (seen.Add(other))
                    {
                        stack.Push(other);
                    }
                }
            }

            result.Add(component);
        }

        return result.OrderByDescending(c => c.Count).ToList();
    }

    /// <summary>Nós que fazem parte de um ciclo (vazio numa rede de gravidade correta).</summary>
    public HashSet<string> CycleNodes()
    {
        var indegree = _nodes.Keys.ToDictionary(k => k, k => _in[k].Count, StringComparer.Ordinal);
        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var removed = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            removed.Add(id);
            foreach (var e in _out[id])
            {
                var down = e.Direction.Down;
                if (--indegree[down] == 0)
                {
                    queue.Enqueue(down);
                }
            }
        }

        return _nodes.Keys.Where(k => !removed.Contains(k)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Número de cada nó (a partir de <paramref name="start"/>). Global: em ordem topológica,
    /// pegando primeiro a cabeceira com o caminho mais longo até a descarga, e seguindo cada ramal até
    /// a junção; assim o fluxo vai sempre do menor para o maior número. Nós em ciclo ficam por último.</summary>
    public Dictionary<string, int> Number(NumberingMode mode = NumberingMode.Global, int start = 1)
    {
        var depth = new Dictionary<string, double>(StringComparer.Ordinal);
        double Depth(string id, HashSet<string> path)
        {
            if (depth.TryGetValue(id, out var known))
            {
                return known;
            }

            if (!path.Add(id))
            {
                return 0;
            }

            var value = _out[id].Select(e => e.Length + Depth(e.Direction.Down, path)).DefaultIfEmpty(0).Max();
            path.Remove(id);
            depth[id] = value;
            return value;
        }

        foreach (var id in _nodes.Keys)
        {
            Depth(id, new HashSet<string>(StringComparer.Ordinal));
        }

        var indegree = _nodes.Keys.ToDictionary(k => k, k => _in[k].Count, StringComparer.Ordinal);
        var ready = new List<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var next = ready.OrderByDescending(id => depth[id]).ThenBy(id => _nodes[id].Name, StringComparer.Ordinal).First();
            ready.Remove(next);
            order.Add(next);
            foreach (var e in _out[next])
            {
                var down = e.Direction.Down;
                if (--indegree[down] == 0)
                {
                    ready.Add(down);
                }
            }
        }

        order.AddRange(_nodes.Keys.Where(k => !order.Contains(k)).OrderBy(k => _nodes[k].Name, StringComparer.Ordinal));
        if (mode == NumberingMode.Invertido)
        {
            order.Reverse();
        }

        return order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => start + x.i, StringComparer.Ordinal);
    }
}

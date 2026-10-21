using Hydro.Core.Csg;
using Hydro.Core.Flow;
using Hydro.Core.Results;

namespace Hydro.Core.Expressions;

internal static class BuiltinFunctions
{
    /// <summary>Limite de elementos gerados por intervalo()/array() — evita travar o CAD com um erro de digitação.</summary>
    public const int MaxGenerated = 10_000;

    public static void RegisterMath(FunctionRegistry r)
    {
        r.Register(new FunctionDef("min", new[] { "valores" }, 1, c => Aggregate(c, Math.Min), variadic: true, "menor valor (aceita lista)"));
        r.Register(new FunctionDef("max", new[] { "valores" }, 1, c => Aggregate(c, Math.Max), variadic: true, "maior valor (aceita lista)"));
        r.Register(new FunctionDef("soma", new[] { "valores" }, 1, c => Aggregate(c, (a, b) => a + b), variadic: true, "soma (aceita lista)"), "sum");
        r.Register(Unary("abs", Math.Abs, "valor absoluto"));
        r.Register(new FunctionDef("sqrt", new[] { "x" }, 1, c =>
        {
            var x = c.Number(0);
            return x >= 0 ? Math.Sqrt(x) : throw c.Error($"raiz de número negativo ({Values.FormatNumber(x)})", 0);
        }, description: "raiz quadrada"), "raiz");
        r.Register(new FunctionDef("round", new[] { "x", "casas" }, 1, c =>
        {
            var digits = c.Has(1) ? c.Integer(1) : 0;
            return digits is < 0 or > 15
                ? throw c.Error("'casas' deve estar entre 0 e 15", 1)
                : Math.Round(c.Number(0), digits, MidpointRounding.AwayFromZero);
        }, description: "arredonda (meio para longe de zero)"), "arred");
        r.Register(Unary("ceil", Math.Ceiling, "arredonda para cima"), "teto");
        r.Register(Unary("floor", Math.Floor, "arredonda para baixo"), "piso");
        r.Register(Unary("sin", Math.Sin, "seno (radianos)"), "sen");
        r.Register(Unary("cos", Math.Cos, "cosseno (radianos)"));
        r.Register(Unary("tan", Math.Tan, "tangente (radianos)"));
        r.Register(Unary("rad", g => g * Math.PI / 180, "graus → radianos"));
        r.Register(Unary("graus", a => a * 180 / Math.PI, "radianos → graus"), "deg");

        r.Register(new FunctionDef("tamanho", new[] { "lista" }, 1, c => (double)c.List(0).Count, description: "número de itens"), "len");
        r.Register(new FunctionDef("intervalo", new[] { "inicio", "fim", "passo" }, 2, Range,
            description: "lista [inicio, inicio+passo, ...) sem incluir fim"), "range");
        r.Register(new FunctionDef("ponto", new[] { "x", "y", "z" }, 3, c => Values.FromVec3(new Vec3(c.Number(0), c.Number(1), c.Number(2))),
            description: "ponto/vetor [x, y, z]"), "vetor");
        r.Register(new FunctionDef("polar", new[] { "angulo", "distancia", "z" }, 2, c =>
        {
            var a = c.Number(0) * Math.PI / 180;
            var d = c.Number(1);
            return Values.FromVec3(new Vec3(d * Math.Cos(a), d * Math.Sin(a), c.Number(2, 0)));
        }, description: "ponto a 'distancia' da origem no ângulo (graus, a partir de +X, anti-horário)"));
    }

    public static void RegisterGeometry(FunctionRegistry r)
    {
        r.Register(new FunctionDef("prisma", new[] { "L", "B", "H", "base" }, 3, c =>
            new PrismNode(c.Positive(0), c.Positive(1), c.Positive(2), c.Vector(3, Vec3.Zero)),
            description: "caixa L (X) × B (Y) × H (Z); base = centro da face inferior"), "prism");
        r.Register(new FunctionDef("cilindro", new[] { "d", "h", "base", "eixo" }, 2, c =>
            new CylinderNode(c.Positive(0), c.Positive(1), c.Vector(2, Vec3.Zero), c.Axis(3, Vec3.UnitZ)),
            description: "cilindro de diâmetro d e comprimento h; base = centro da face inicial"), "cylinder");
        r.Register(new FunctionDef("vazio", Array.Empty<string>(), 0, _ => EmptyNode.Instance, description: "sólido vazio"), "empty");

        r.Register(new FunctionDef("unir", new[] { "solidos" }, 1, c => Union(Solids(c, 0)), variadic: true, "união"), "union");
        r.Register(new FunctionDef("subtrair", new[] { "solido", "ferramentas" }, 2, c =>
        {
            var source = c.Solid(0);
            var tools = Solids(c, 1);
            return source is EmptyNode ? source : tools.Count == 0 ? source : new SubtractNode(source, tools);
        }, variadic: true, "primeiro sólido menos os demais"), "subtract");
        r.Register(new FunctionDef("intersectar", new[] { "solidos" }, 2, c =>
        {
            var all = Solids(c, 0, skipEmpty: false);
            return all.Any(s => s is EmptyNode) ? EmptyNode.Instance : new IntersectNode(all);
        }, variadic: true, "interseção"), "intersect");

        r.Register(new FunctionDef("mover", new[] { "solido", "dx", "dy", "dz" }, 2, c =>
        {
            var offset = c.Value(1) is IReadOnlyList<object>
                ? c.Vector(1)
                : new Vec3(c.Number(1), c.Number(2, 0), c.Number(3, 0));
            return Wrap(c.Solid(0), s => new TranslateNode(s, offset));
        }, description: "translada por [dx, dy, dz] ou (dx, dy, dz)"), "move");
        r.Register(new FunctionDef("rotacionar", new[] { "solido", "angulo", "eixo", "centro" }, 2, c =>
            Wrap(c.Solid(0), s => new RotateNode(s, c.Number(1), c.Axis(2, Vec3.UnitZ), c.Vector(3, Vec3.Zero))),
            description: "gira 'angulo' graus em torno do eixo (padrão z) que passa por centro"), "rotate");
        r.Register(new FunctionDef("array_z", new[] { "solido", "n", "passo" }, 3, c =>
            MakeArray(c, c.Solid(0), c.Integer(1), new Vec3(0, 0, c.Number(2))),
            description: "n cópias empilhadas a cada 'passo' em Z"));
        r.Register(new FunctionDef("array", new[] { "solido", "n", "passo" }, 3, c =>
            MakeArray(c, c.Solid(0), c.Integer(1), c.Vector(2)),
            description: "n cópias deslocadas de passo = [dx, dy, dz]"));

        RegisterProfiles(r);

        r.Register(new FunctionDef("expor_solido", new[] { "nome", "solido", "material" }, 2, c =>
        {
            var solid = c.Solid(1);
            Sink(c).AddSolid(new NamedSolid(c.Text(0), solid, c.Has(2) ? c.Text(2) : null), c);
            return solid;
        }, description: "publica um sólido de saída (somente no fluxo)"));
        r.Register(new FunctionDef("conector", new[] { "nome", "posicao", "direcao", "diametro", "filtro" }, 4, c =>
        {
            var direction = c.Vector(2);
            if (direction.Length < 1e-12)
            {
                throw c.Error("'direcao' não pode ser o vetor nulo", 2);
            }

            var connector = new Connector(c.Text(0), c.Vector(1), direction.Normalized(), c.Positive(3), c.Has(4) ? c.Text(4) : null);
            Sink(c).AddConnector(connector, c);
            return true;
        }, description: "publica um conector de tubulação (somente no fluxo)"), "connector");
    }

    /// <summary>Perfis, extrusão, planos e corte (linguagem v2), mais vetores e <c>editado()</c>.</summary>
    private static void RegisterProfiles(FunctionRegistry r)
    {
        r.Register(new FunctionDef("polilinha", new[] { "pontos", "fechada", "convexidades", "z" }, 1, c =>
        {
            var z = c.Number(3, 0);
            var points = c.List(0).Select((p, i) => Values.TryToPoint(p, z, out var v)
                ? v
                : throw c.Error($"vértice {i + 1} deve ser [x, y] ou [x, y, z], mas é {Values.TypeName(p)}", 0)).ToList();
            var bulges = c.Has(2) ? c.List(2).Select((b, i) => b as double? ?? throw c.Error($"convexidade {i + 1} deve ser número", 2)).ToList() : new List<double>();
            return MakeProfile(c, points, bulges, !c.Has(1) || Bool(c, 1));
        }, description: "perfil plano pelos vértices (convexidades = bulge por trecho, como no AutoCAD)"), "polyline");
        r.Register(new FunctionDef("retangulo", new[] { "L", "B", "centro", "rotacao" }, 2, c =>
        {
            var (l, b) = (c.Positive(0) / 2, c.Positive(1) / 2);
            var center = c.Vector(2, Vec3.Zero);
            var rotation = Affine.Rotation(c.Number(3, 0), Vec3.UnitZ, center);
            var corners = new[] { new Vec3(-l, -b, 0), new Vec3(l, -b, 0), new Vec3(l, b, 0), new Vec3(-l, b, 0) }
                .Select(p => rotation.Apply(center + p)).ToList();
            return new Profile(corners, Array.Empty<double>(), true);
        }, description: "perfil retangular L (X) × B (Y) centrado, girado 'rotacao' graus"), "rectangle");
        r.Register(new FunctionDef("circulo", new[] { "centro", "raio" }, 2, c =>
        {
            var center = c.Vector(0);
            var radius = c.Positive(1);
            return new Profile(new[] { center + new Vec3(-radius, 0, 0), center + new Vec3(radius, 0, 0) }, new[] { 1.0, 1.0 }, true);
        }, description: "perfil circular em planta (dois arcos)"), "circle");
        r.Register(new FunctionDef("extrudar", new[] { "perfil", "altura", "direcao" }, 2, c =>
        {
            var profile = c.Value(0) as Profile ?? throw c.Error($"'perfil' deve ser perfil (polilinha, retangulo...), mas é {Values.TypeName(c.Value(0))}", 0);
            if (!profile.Closed)
            {
                throw c.Error("o perfil precisa ser fechado", 0);
            }

            var direction = c.Axis(2, Vec3.UnitZ);
            if (Math.Abs(Affine.Dot(direction, profile.Normal)) < 1e-6)
            {
                throw c.Error("a direção da extrusão está no plano do perfil", 2);
            }

            return new ExtrudeNode(profile, direction * c.Positive(1));
        }, description: "sólido pela extrusão de um perfil fechado (direção padrão +Z)"), "extrude");
        r.Register(new FunctionDef("plano", new[] { "origem", "normal" }, 2, c => new Plane(c.Vector(0), c.Axis(1, Vec3.UnitZ)),
            description: "plano por um ponto e a normal"), "plane");
        r.Register(new FunctionDef("plano3", new[] { "p1", "p2", "p3" }, 3, c =>
        {
            var (a, b, d) = (c.Vector(0), c.Vector(1), c.Vector(2));
            var normal = Affine.Cross(b - a, d - a);
            return normal.Length < 1e-12
                ? throw c.Error("os três pontos estão alinhados (não definem um plano)")
                : new Plane(a, normal.Normalized());
        }, description: "plano pelos três pontos; normal = (p2 − p1) × (p3 − p1)"), "plane3");
        r.Register(new FunctionDef("cortar", new[] { "solido", "plano" }, 2, c =>
        {
            var plane = c.Value(1) as Plane ?? throw c.Error($"'plano' deve ser plano (plano, plano3), mas é {Values.TypeName(c.Value(1))}", 1);
            return Wrap(c.Solid(0), s => new CutNode(s, plane));
        }, description: "corta o sólido pelo plano; fica a parte do lado da normal"), "cut");

        r.Register(new FunctionDef("distancia", new[] { "p", "q" }, 2, c => (c.Vector(1) - c.Vector(0)).Length,
            description: "distância 3D entre dois pontos"), "distance");
        r.Register(new FunctionDef("distancia2d", new[] { "p", "q" }, 2, c =>
        {
            var d = c.Vector(1) - c.Vector(0);
            return Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
        }, description: "distância em planta"));
        r.Register(new FunctionDef("normalizar", new[] { "v" }, 1, c =>
        {
            var v = c.Vector(0);
            return v.Length < 1e-12 ? throw c.Error("vetor nulo não pode ser normalizado", 0) : Values.FromVec3(v.Normalized());
        }, description: "vetor unitário"), "normalize");
        r.Register(new FunctionDef("cruz", new[] { "a", "b" }, 2, c => Values.FromVec3(Affine.Cross(c.Vector(0), c.Vector(1))),
            description: "produto vetorial"), "cross");
        r.Register(new FunctionDef("escalar", new[] { "a", "b" }, 2, c => Affine.Dot(c.Vector(0), c.Vector(1)),
            description: "produto escalar"), "dot");
        r.Register(new FunctionDef("editado", Array.Empty<string>(), 0, c => c.Context.Edited ?? "",
            description: "nome do último parâmetro editado (paleta ou grip); texto vazio se nenhum"), "edited");
    }

    private static bool Bool(FunctionCall c, int index) => c.Value(index) is bool b ? b : throw c.Error("deve ser verdadeiro ou falso", index);

    private static Profile MakeProfile(FunctionCall c, List<Vec3> points, List<double> bulges, bool closed)
    {
        if (points.Count < 2)
        {
            throw c.Error("o perfil precisa de ao menos 2 vértices", 0);
        }

        // fechado: o último vértice igual ao primeiro é descartado (o fechamento é implícito)
        if (closed && points.Count > 2 && (points[^1] - points[0]).Length < 1e-9)
        {
            points.RemoveAt(points.Count - 1);
        }

        var segments = closed ? points.Count : points.Count - 1;
        if (bulges.Count > segments)
        {
            throw c.Error($"{bulges.Count} convexidades para {segments} trecho(s)", 2);
        }

        for (var s = 0; s < segments; s++)
        {
            if ((points[(s + 1) % points.Count] - points[s]).Length < 1e-9)
            {
                throw c.Error($"trecho {s + 1} do perfil tem comprimento zero (vértices repetidos)", 0);
            }
        }

        if (closed && points.Count < 3 && bulges.All(b => Math.Abs(b) < 1e-12))
        {
            throw c.Error("perfil fechado sem arcos precisa de ao menos 3 vértices", 0);
        }

        var profile = new Profile(points, bulges, closed);
        var normal = profile.Normal;
        if (points.Any(p => Math.Abs(Affine.Dot(p - points[0], normal)) > 1e-6))
        {
            throw c.Error("os vértices do perfil não estão num mesmo plano", 0);
        }

        return profile;
    }

    private static FunctionDef Unary(string name, Func<double, double> f, string description) =>
        new(name, new[] { "x" }, 1, c =>
        {
            var result = f(c.Number(0));
            return double.IsFinite(result) ? result : throw c.Error("resultado não é um número finito", 0);
        }, description: description);

    private static object Aggregate(FunctionCall c, Func<double, double, double> f)
    {
        var numbers = new List<double>();
        for (var i = 0; i < c.Count; i++)
        {
            if (c.Value(i) is IReadOnlyList<object> list)
            {
                foreach (var item in list)
                {
                    numbers.Add(item is double d ? d : throw c.Error($"lista contém {Values.TypeName(item)}, esperado número", i));
                }
            }
            else
            {
                numbers.Add(c.Number(i));
            }
        }

        if (numbers.Count == 0)
        {
            return c.Function.Name == "soma" ? 0.0 : throw c.Error("lista vazia");
        }

        return numbers.Skip(1).Aggregate(numbers[0], f);
    }

    private static object Range(FunctionCall c)
    {
        var start = c.Number(0);
        var end = c.Number(1);
        var step = c.Number(2, 1);
        if (step == 0)
        {
            throw c.Error("'passo' não pode ser zero", 2);
        }

        var count = Math.Max(0, (int)Math.Ceiling(Math.Round((end - start) / step, 9)));
        if (count > MaxGenerated)
        {
            throw c.Error($"intervalo gera {count} itens (máximo {MaxGenerated})");
        }

        return Enumerable.Range(0, count).Select(i => (object)(start + (i * step))).ToList();
    }

    private static List<CsgNode> Solids(FunctionCall c, int from, bool skipEmpty = true)
    {
        var result = new List<CsgNode>();
        for (var i = from; i < c.Count; i++)
        {
            if (c.Value(i) is IReadOnlyList<object> list)
            {
                foreach (var item in list)
                {
                    result.Add(item as CsgNode ?? throw c.Error($"lista contém {Values.TypeName(item)}, esperado sólido", i));
                }
            }
            else
            {
                result.Add(c.Solid(i));
            }
        }

        return skipEmpty ? result.Where(s => s is not EmptyNode).ToList() : result;
    }

    /// <summary>Uniões aninhadas são achatadas: unir(unir(a, b), c) = unir(a, b, c) — na Fase 2
    /// isso vira uma única sequência de BooleanOperation.</summary>
    private static CsgNode Union(List<CsgNode> solids)
    {
        var flat = solids.SelectMany(s => s is UnionNode u ? u.Children : new[] { s }).ToList();
        return flat.Count switch
        {
            0 => EmptyNode.Instance,
            1 => flat[0],
            _ => new UnionNode(flat),
        };
    }

    private static CsgNode Wrap(CsgNode solid, Func<CsgNode, CsgNode> transform) =>
        solid is EmptyNode ? solid : transform(solid);

    private static CsgNode MakeArray(FunctionCall c, CsgNode solid, int count, Vec3 step)
    {
        if (count < 0 || count > MaxGenerated)
        {
            throw c.Error($"'n' deve estar entre 0 e {MaxGenerated} (recebido {count})", 1);
        }

        return count == 0 || solid is EmptyNode ? EmptyNode.Instance : new ArrayNode(solid, count, step);
    }

    private static IOutputSink Sink(FunctionCall c) =>
        c.Context.Host as IOutputSink ?? throw c.Error("só pode ser usada no 'fluxo'");
}

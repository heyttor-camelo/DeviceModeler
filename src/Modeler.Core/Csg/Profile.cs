namespace Modeler.Core.Csg;

/// <summary>Perfil plano (polilinha) no sistema local: vértices 3D coplanares e, por trecho, a
/// convexidade (bulge, como no AutoCAD: tan(ângulo/4), positiva = arco anti-horário visto da normal).
/// Fechado, é a seção de <c>extrudar</c>. Valor de expressão do tipo "perfil".</summary>
public sealed record Profile(IReadOnlyList<Vec3> Vertices, IReadOnlyList<double> Bulges, bool Closed)
{
    /// <summary>Normal unitária do plano do perfil (Newell). +Z para perfis em planta anti-horários;
    /// perfis em planta horários dão −Z.</summary>
    public Vec3 Normal
    {
        get
        {
            var n = Vec3.Zero;
            for (var i = 0; i < Vertices.Count; i++)
            {
                var a = Vertices[i];
                var b = Vertices[(i + 1) % Vertices.Count];
                n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }

            // perfis só com arcos (círculo de dois vértices) ou degenerados: plano XY
            return n.Length < 1e-12 ? Vec3.UnitZ : n.Normalized();
        }
    }

    public int SegmentCount => Closed ? Vertices.Count : Vertices.Count - 1;

    public double Bulge(int segment) => segment < Bulges.Count ? Bulges[segment] : 0;

    /// <summary>Normal de referência dos arcos e do sentido: a do perfil, virada para cima (Z &gt; 0)
    /// ou, em perfis verticais, para +Y/+X. Assim a convexidade de um perfil em planta segue a
    /// convenção do AutoCAD (positiva = anti-horária vista de cima), qualquer que seja a ordem dos
    /// vértices.</summary>
    public Vec3 ArcNormal
    {
        get
        {
            var n = Normal;
            var flip = n.Z < -1e-9 || (Math.Abs(n.Z) <= 1e-9 && (n.Y < -1e-9 || (Math.Abs(n.Y) <= 1e-9 && n.X < 0)));
            return flip ? n * -1 : n;
        }
    }

    /// <summary>Ponto médio do arco do trecho (para montar o arco por três pontos).</summary>
    public Vec3 ArcMidpoint(int segment)
    {
        var a = Vertices[segment];
        var b = Vertices[(segment + 1) % Vertices.Count];
        var chord = b - a;
        var length = chord.Length;
        var right = Affine.Cross(chord * (1 / length), ArcNormal);
        return a + (chord * 0.5) + (right * (Bulge(segment) * length / 2));
    }

    /// <summary>Pontos ao longo do perfil, com cada arco dividido em trechos de no máximo 10°.</summary>
    public List<Vec3> Sample()
    {
        var points = new List<Vec3>();
        for (var s = 0; s < SegmentCount; s++)
        {
            var a = Vertices[s];
            var b = Vertices[(s + 1) % Vertices.Count];
            points.Add(a);
            var bulge = Bulge(s);
            if (Math.Abs(bulge) < 1e-12)
            {
                continue;
            }

            var theta = 4 * Math.Atan(bulge);
            var chord = b - a;
            var half = chord.Length / 2;
            var radius = half / Math.Abs(Math.Sin(theta / 2));
            var mid = a + (chord * 0.5);
            var right = Affine.Cross(chord * (1 / chord.Length), ArcNormal);
            // centro do lado oposto à flecha; distância do centro à corda = r·cos(θ/2) (com sinal)
            var center = mid - (right * (Math.Sign(bulge) * radius * Math.Cos(theta / 2)));
            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 18)));
            var start = a - center;
            for (var k = 1; k < steps; k++)
            {
                points.Add(center + Affine.Rotation(theta * k / steps * 180 / Math.PI, ArcNormal, Vec3.Zero).ApplyVector(start));
            }
        }

        if (!Closed)
        {
            points.Add(Vertices[^1]);
        }

        return points;
    }

    /// <summary>Área do perfil fechado (m²), contando os arcos.</summary>
    public double Area()
    {
        var n = ArcNormal;
        var polygon = 0.0;
        for (var i = 0; i < Vertices.Count; i++)
        {
            polygon += Affine.Dot(Affine.Cross(Vertices[i], Vertices[(i + 1) % Vertices.Count]), n);
        }

        var total = polygon / 2;
        for (var s = 0; s < SegmentCount; s++)
        {
            var bulge = Bulge(s);
            if (Math.Abs(bulge) < 1e-12)
            {
                continue;
            }

            var theta = 4 * Math.Atan(bulge);
            var half = (Vertices[(s + 1) % Vertices.Count] - Vertices[s]).Length / 2;
            var radius = half / Math.Abs(Math.Sin(theta / 2));
            // segmento circular, com o sinal da convexidade (o perfil pode estar em qualquer sentido)
            total += (theta - Math.Sin(theta)) * radius * radius / 2;
        }

        return Math.Abs(total);
    }
}

/// <summary>Plano por um ponto e uma normal unitária. Em <c>cortar</c>, fica a parte do sólido do
/// lado para onde a normal aponta.</summary>
public sealed record Plane(Vec3 Origin, Vec3 Normal)
{
    public double SignedDistance(Vec3 p) => Affine.Dot(p - Origin, Normal);
}

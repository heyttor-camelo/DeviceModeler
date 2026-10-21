using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Editing;
using Hydro.Core.Geometry;
using Hydro.Core.Persistence;
using Hydro.Core.Results;

namespace Hydro.Core.Tests;

/// <summary>Peças do Core usadas pelo plugin do Civil 3D (sem AutoCAD).</summary>
public class Phase2SupportTests
{
    private static readonly DeviceDefinition Pv =
        DeviceDefinition.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "exemplos", "pv_retangular.yaml"));

    [Fact]
    public void Placement_ida_e_volta()
    {
        var p = new Placement(new Vec3(1000, 2000, 0), 90);
        var world = p.ToWorld(new Vec3(1, 0, 100));
        Assert.Equal(1000, world.X, 9);
        Assert.Equal(2001, world.Y, 9);
        Assert.Equal(100, world.Z, 9);

        var back = p.ToLocal(world);
        Assert.Equal(1, back.X, 9);
        Assert.Equal(0, back.Y, 9);
        Assert.Equal(-1, p.DirectionToWorld(Vec3.UnitY).X, 9);
        Assert.Equal(1, p.DirectionToLocal(new Vec3(0, 1, 0)).X, 9);
        Assert.Equal(270, Placement.NormalizeAngle(-90), 9);
    }

    [Fact]
    public void Tubo_vira_conexao_no_sistema_local()
    {
        // PV em (100, 100) girado 90°; tubo saindo para +Y do desenho = +X local
        var p = new Placement(new Vec3(100, 100, 0), 90);
        var c = ConnectionGeometry.FromPipe(p, new Vec3(100, 100.75, 100.30), new Vec3(100, 150, 100.00), 0.60, 0.72, "T1", "2A3");

        Assert.Equal(0, c.Angle, 6);
        Assert.Equal(100.00, c.InvertElevation, 9); // centro 100.30 − 0.30
        Assert.Equal(0.60, c.Diameter);
        Assert.Equal(0.72, c.OuterDiameter);
        Assert.Equal("2A3", c.Reference);

        // a extremidade mais próxima define a ligação, mesmo com o tubo "ao contrário"
        var reversed = ConnectionGeometry.FromPipe(p, new Vec3(100, 50, 101), new Vec3(100, 99.25, 100.5), 0.4, null, "T2");
        Assert.Equal(180, reversed.Angle, 6);
        Assert.Equal(100.3, reversed.InvertElevation, 9);
        Assert.Equal(0.4, reversed.OuterDiameter);
    }

    [Fact]
    public void Conexao_do_tubo_alimenta_o_PV()
    {
        var c = ConnectionGeometry.FromPipe(Placement.Identity, new Vec3(0.75, 0, 100.3), new Vec3(30, 0, 100), 0.6, 0.72, "saida");
        var inst = Pv.Evaluate(new Dictionary<string, object> { ["Conexoes"] = new[] { c } });

        var hole = Assert.IsType<RotateNode>(Assert.IsType<SubtractNode>(inst.Solids["concreto"].Node).Tools[0]);
        var cylinder = Assert.IsType<CylinderNode>(hole.Child);
        Assert.Equal(0.82, cylinder.Diameter, 9);       // externo + 0,10
        Assert.Equal(100.30, cylinder.Base.Z, 9);       // eixo do tubo
        Assert.Equal(0.75, inst.Connectors["saida"].Position.X, 9);
    }

    [Fact]
    public void Codec_preserva_todos_os_tipos()
    {
        var inst = Pv.Evaluate(new Dictionary<string, object>
        {
            ["tampa"] = "T900",
            ["Conexoes"] = new[] { new ConnectionInput("e", 45, 100.1, 0.4, 0.5, "1F") },
        });

        var json = ParameterCodec.Serialize(inst.Parameters);
        var back = ParameterCodec.Deserialize(json);
        Assert.Equal(inst.Parameters.Keys.OrderBy(k => k), back.Keys.OrderBy(k => k));
        Assert.Equal("T900", back["tampa"]);
        Assert.Equal(1.2, back["L"]);

        // os valores lidos voltam a avaliar igual
        var again = Pv.Evaluate(back);
        Assert.Equal(inst.Quantities["concreto_m3"].Value, again.Quantities["concreto_m3"].Value, 12);
        Assert.Equal("1F", ((IReadOnlyDictionary<string, object>)((IReadOnlyList<object>)again.Parameters["Conexoes"])[0])["ref"]);
    }

    [Theory]
    [InlineData(0.10, 1.40)]  // fator 2
    [InlineData(5.00, 3.00)]  // limitado ao max
    [InlineData(-5.0, 0.80)]  // limitado ao min
    public void Grip_respeita_fator_e_limites(double dx, double expected)
    {
        var l = Pv.FindParameter("L")!;
        Assert.Equal(expected, DeviceEditing.GripValue(l, 1.20, new Vec3(dx, 7, 7)), 9);
    }

    [Fact]
    public void Reconciliar_descarta_parametro_antigo_e_valor_invalido()
    {
        var stored = new Dictionary<string, object> { ["L"] = 9.0, ["B"] = 1.5, ["Antigo"] = 1.0 };
        var values = DeviceEditing.Reconcile(Pv, stored, out var problems);
        Assert.Equal(new[] { "B" }, values.Keys);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("'Antigo' não existe mais"));
        Assert.Contains(problems, p => p.Contains("fora do intervalo") && p.Contains("padrão 1.2"));
    }

    [Fact]
    public void Deslocar_cotas_move_parametros_cota_e_conexoes()
    {
        var values = new Dictionary<string, object>
        {
            ["CotaFundo"] = 100.0,
            ["Conexoes"] = new List<object> { new Dictionary<string, object> { ["angulo"] = 0.0, ["cota"] = 100.2, ["diametro"] = 0.4 } },
        };
        var shifted = DeviceEditing.ShiftElevations(Pv, values, 1.5);
        Assert.Equal(101.5, shifted["CotaFundo"]);
        Assert.Equal(103.0, shifted["CotaTampa"]);   // padrão 101.5 + 1.5
        var c = (IReadOnlyDictionary<string, object>)((IReadOnlyList<object>)shifted["Conexoes"])[0];
        Assert.Equal(101.7, (double)c["cota"], 9);
        Assert.Equal(1.2, Pv.Evaluate(shifted).Parameters["L"]);
    }
}

using Hydro.Core.Csg;
using Hydro.Core.Definition;
using Hydro.Core.Geometry;

namespace Hydro.Core.Tests;

public class PlanOutlineTests
{
    [Fact]
    public void Rotacao_de_90_graus_em_z_leva_x_em_y()
    {
        var r = Affine.Rotation(90, Vec3.UnitZ, Vec3.Zero);
        var p = r.Apply(new Vec3(1, 0, 5));
        Assert.Equal(0, p.X, 9);
        Assert.Equal(1, p.Y, 9);
        Assert.Equal(5, p.Z, 9);
    }

    [Fact]
    public void Rotacao_em_torno_de_um_centro()
    {
        var r = Affine.Rotation(180, Vec3.UnitZ, new Vec3(1, 1, 0));
        var p = r.Apply(new Vec3(2, 1, 0));
        Assert.Equal(0, p.X, 9);
        Assert.Equal(1, p.Y, 9);
    }

    [Fact]
    public void Placement_como_affine_coincide_com_ToWorld()
    {
        var placement = new Placement(new Vec3(10, 20, 0), 30);
        var local = new Vec3(1.5, -0.7, 101);
        var a = placement.ToWorld(local);
        var b = placement.ToAffine().Apply(local);
        Assert.Equal(a.X, b.X, 9);
        Assert.Equal(a.Y, b.Y, 9);
        Assert.Equal(a.Z, b.Z, 9);
    }

    [Fact]
    public void Prisma_vira_retangulo_e_furo_vira_peca_removida()
    {
        var corpo = new SubtractNode(new PrismNode(2, 1, 1, Vec3.Zero), new CsgNode[] { new CylinderNode(0.5, 1, Vec3.Zero, Vec3.UnitZ) });
        var parts = PlanOutline.Of(corpo);
        Assert.Equal(2, parts.Count);
        Assert.False(parts[0].Removed);
        Assert.Equal(4, parts[0].Points.Count);
        Assert.Equal(-1, parts[0].Points.Min(p => p.X), 9);
        Assert.Equal(1, parts[0].Points.Max(p => p.X), 9);
        Assert.True(parts[1].Removed);
        Assert.Equal(0.25, parts[1].Points.Max(p => p.X), 6);
    }

    [Fact]
    public void Cilindro_horizontal_vira_retangulo_na_planta()
    {
        var parts = PlanOutline.Of(new CylinderNode(0.6, 2, Vec3.Zero, Vec3.UnitX));
        var part = Assert.Single(parts);
        Assert.Equal(0, part.Points.Min(p => p.X), 6);
        Assert.Equal(2, part.Points.Max(p => p.X), 6);
        Assert.Equal(0.3, part.Points.Max(p => p.Y), 6);
        Assert.Equal(-0.3, part.ZMin, 6);
    }

    [Fact]
    public void Array_e_rotacao_sao_aplicados()
    {
        var node = new RotateNode(new ArrayNode(new PrismNode(1, 1, 1, Vec3.Zero), 3, new Vec3(2, 0, 0)), 90, Vec3.UnitZ, Vec3.Zero);
        var parts = PlanOutline.Of(node);
        Assert.Equal(3, parts.Count);
        Assert.Equal(4.5, parts[2].Points.Max(p => p.Y), 6);
    }

    [Fact]
    public void Pv_do_catalogo_tem_planta()
    {
        var def = DeviceDefinition.Load(Path.Combine("catalog", "exemplos", "pv_retangular.yaml"));
        var inst = def.Evaluate();
        var parts = inst.Solids.SelectMany(s => PlanOutline.Of(s.Node)).ToList();
        Assert.Contains(parts, p => !p.Removed && Math.Abs(p.Points.Max(q => q.X) - 0.75) < 1e-9);
        Assert.Contains(parts, p => p.Removed);
    }
}

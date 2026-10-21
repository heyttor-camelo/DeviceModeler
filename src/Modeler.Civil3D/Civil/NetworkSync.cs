using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Devices;
using Modeler.Civil3D.Geometry;
using Modeler.Civil3D.Ui.Common;
using Modeler.Core.Csg;
using Modeler.Core.Definition;
using Modeler.Core.Network;
using CorePlacement = Modeler.Core.Geometry.Placement;

namespace Modeler.Civil3D.Civil;

/// <summary>Mantém dispositivo, estrutura nula e pontas dos tubos de acordo (técnica 11 do estudo E
/// e estudo C §1.3): a estrutura acompanha posição e elevações do dispositivo (e o Civil leva as
/// pontas dos tubos ligados a ela); tubos soltos têm a ponta levada ao conector pelo modo de
/// recálculo (fixo, ajustar posição, melhor conector em 3D ou em planta × corrigir ou manter a
/// elevação).</summary>
internal static class NetworkSync
{
    public const string DefaultModeText = "ajustar_corrigir";

    /// <summary>Modo padrão das pontas (Configuração do painel), para dispositivos sem modo próprio.</summary>
    public static string DefaultMode
    {
        get => Preferences.Get("modo_ponta") ?? DefaultModeText;
        set => Preferences.Set("modo_ponta", value);
    }

    /// <summary>Elevação da tampa e do fundo do dispositivo: os parâmetros com <c>alvo: superficie</c> e
    /// <c>alvo: tubo</c>; sem eles, o topo e a base dos sólidos.</summary>
    public static (double Rim, double Sump) Elevations(InstanceCache.Entry entry)
    {
        double? Target(TargetKind kind) => entry.Definition.Parameters
            .Where(p => p.Target == kind && p.IsNumeric)
            .Select(p => entry.Instance.Parameters[p.Name] as double?)
            .FirstOrDefault(v => v is not null);

        var z = entry.Instance.Solids.SelectMany(s => Wireframe.Of(s.Node)).SelectMany(l => l.Points).Select(p => p.Z).DefaultIfEmpty(0).ToList();
        return (Target(TargetKind.Superficie) ?? z.Max(), Target(TargetKind.Tubo) ?? z.Min());
    }

    /// <summary>Leva a estrutura nula do dispositivo para a posição e as elevações dele.</summary>
    public static bool SyncStructure(Transaction tr, Database db, BlockReference reference, InstanceCache.Entry entry)
    {
        if (!CivilLinks.TryGet<Structure>(tr, db, entry.Record.StructureHandle, out var structure))
        {
            return false;
        }

        var (rim, sump) = Elevations(entry);
        var location = new Point3d(reference.Position.X, reference.Position.Y, rim);
        if (structure.Location.DistanceTo(location) < 1e-6 && Math.Abs(structure.SumpElevation - sump) < 1e-6 && Math.Abs(structure.Rotation - reference.Rotation) < 1e-9)
        {
            return false;
        }

        PipeNetworkLink.Sync(tr, structure.ObjectId, location, reference.Rotation, rim, sump);
        return true;
    }

    /// <summary>Leva as pontas soltas (sem estrutura) dos tubos ligados ao conector escolhido pelo
    /// modo. Devolve verdadeiro se alguma ponta andou.</summary>
    public static bool AdjustEnds(Transaction tr, Database db, BlockReference reference, InstanceCache.Entry entry, List<string> messages)
    {
        var (choice, elevation) = ConnectorMatch.ParseMode(entry.Record.EndMode.Length > 0 ? entry.Record.EndMode : DefaultMode);
        var placement = DeviceService.PlacementOf(reference);
        var connectors = entry.Instance.Connectors
            .Select(c => c with { Position = placement.ToWorld(c.Position), Direction = placement.DirectionToWorld(c.Direction) })
            .ToList();
        if (connectors.Count == 0)
        {
            return false;
        }

        var conexoes = entry.Definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes);
        var connections = conexoes is not null && entry.Instance.Parameters[conexoes.Name] is IReadOnlyList<object> list
            ? list.OfType<IReadOnlyDictionary<string, object>>().ToList()
            : new List<IReadOnlyDictionary<string, object>>();
        var moved = false;
        foreach (var connection in connections)
        {
            if (connection.GetValueOrDefault("ref") is not string handle || !CivilLinks.TryGet<Pipe>(tr, db, handle, out var pipe))
            {
                continue;
            }

            var startIsNear = Plan(pipe.StartPoint, placement) <= Plan(pipe.EndPoint, placement);
            var structureAtEnd = startIsNear ? pipe.StartStructureId : pipe.EndStructureId;
            if (!structureAtEnd.IsNull)
            {
                continue; // a estrutura leva a ponta
            }

            var end = Solid3dBuilder.ToVec3(startIsNear ? pipe.StartPoint : pipe.EndPoint);
            var other = Solid3dBuilder.ToVec3(startIsNear ? pipe.EndPoint : pipe.StartPoint);
            var match = ConnectorMatch.Choose(connectors, end, other, "tubo", choice, elevation, connection.GetValueOrDefault("nome") as string);
            if (match is null || match.Distance < 1e-4)
            {
                continue;
            }

            pipe.Writable();
            if (startIsNear)
            {
                pipe.StartPoint = Solid3dBuilder.ToPoint(match.End);
            }
            else
            {
                pipe.EndPoint = Solid3dBuilder.ToPoint(match.End);
            }

            messages.Add($"ponta do tubo {pipe.Name} levada ao conector {match.Connector.Name} ({match.Distance:0.###} m)");
            moved = true;
        }

        return moved;
    }

    private static double Plan(Point3d p, CorePlacement placement) =>
        Math.Sqrt(Math.Pow(p.X - placement.Origin.X, 2) + Math.Pow(p.Y - placement.Origin.Y, 2));
}

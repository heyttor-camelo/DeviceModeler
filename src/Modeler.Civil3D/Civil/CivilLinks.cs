using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Modeler.Civil3D.Geometry;
using Modeler.Core.Definition;
using Modeler.Core.Geometry;
using Modeler.Core.Results;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Modeler.Civil3D.Civil;

/// <summary>Ligação com objetos do Civil 3D: tubos de Pipe Network (conexões e cota de fundo)
/// e superfícies (cota da tampa). Preenche os parâmetros conforme o <c>alvo</c> da definição.</summary>
internal static class CivilLinks
{
    /// <summary>Conexões a partir de tubos. O nome é o do tubo; a referência é o handle,
    /// que permite reler o tubo depois (DM_ATUALIZAR).</summary>
    public static List<ConnectionInput> ConnectionsFromPipes(Transaction tr, IEnumerable<ObjectId> pipeIds, Placement placement)
    {
        var result = new List<ConnectionInput>();
        foreach (var id in pipeIds)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Pipe pipe)
            {
                continue;
            }

            result.Add(FromPipe(pipe, placement));
        }

        return result;
    }

    public static ConnectionInput FromPipe(Pipe pipe, Placement placement)
    {
        var inner = pipe.InnerDiameterOrWidth;
        var outer = pipe.OuterDiameterOrWidth > inner ? pipe.OuterDiameterOrWidth : inner;
        return ConnectionGeometry.FromPipe(placement,
            Solid3dBuilder.ToVec3(pipe.StartPoint), Solid3dBuilder.ToVec3(pipe.EndPoint),
            inner, outer, string.IsNullOrWhiteSpace(pipe.Name) ? pipe.Handle.ToString() : pipe.Name, pipe.Handle.ToString());
    }

    /// <summary>Relê os tubos referenciados nas conexões (pelo handle) e atualiza ângulo, cota e
    /// diâmetros. Conexões cujo tubo não existe mais são mantidas como estavam.</summary>
    public static IReadOnlyList<object> RefreshConnections(Transaction tr, Database db, IReadOnlyList<object> connections, Placement placement, List<string> messages)
    {
        var result = new List<object>();
        foreach (var item in connections)
        {
            if (item is IReadOnlyDictionary<string, object> record && record.GetValueOrDefault("ref") is string handle && handle.Length > 0 &&
                TryGetPipe(tr, db, handle, out var pipe))
            {
                var fresh = FromPipe(pipe, placement);
                result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["nome"] = fresh.Name, ["angulo"] = fresh.Angle, ["cota"] = fresh.InvertElevation,
                    ["diametro"] = fresh.Diameter, ["diametro_externo"] = fresh.OuterDiameter ?? fresh.Diameter, ["ref"] = handle,
                });
            }
            else
            {
                if (item is IReadOnlyDictionary<string, object> r && r.GetValueOrDefault("ref") is string h && h.Length > 0)
                {
                    messages.Add($"tubo {h} não encontrado: conexão '{r.GetValueOrDefault("nome")}' mantida");
                }

                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>Aplica as conexões ao primeiro parâmetro do tipo <c>conexoes</c> e, nos parâmetros de
    /// cota com <c>alvo: tubo</c>, a menor cota de fundo entre os tubos.</summary>
    public static void ApplyConnections(DeviceDefinition definition, Dictionary<string, object> values, IReadOnlyList<object> connections, List<string> messages)
    {
        var parameter = definition.Parameters.FirstOrDefault(p => p.Type == ParameterType.Conexoes);
        if (parameter is null)
        {
            messages.Add($"a definição {definition.Id} não tem parâmetro do tipo 'conexoes'");
            return;
        }

        values[parameter.Name] = connections;
        var inverts = connections.OfType<IReadOnlyDictionary<string, object>>().Select(c => c["cota"]).OfType<double>().ToList();
        if (inverts.Count == 0)
        {
            return;
        }

        foreach (var target in definition.Parameters.Where(p => p.Target == TargetKind.Tubo && p.IsNumeric))
        {
            values[target.Name] = inverts.Min();
            messages.Add($"{target.Name} = {inverts.Min():0.###} (menor cota de fundo dos tubos)");
        }
    }

    /// <summary>Cota da superfície no ponto de inserção para os parâmetros com <c>alvo: superficie</c>.</summary>
    public static bool ApplySurface(DeviceDefinition definition, Dictionary<string, object> values, CivilSurface surface, Placement placement, List<string> messages)
    {
        double elevation;
        try
        {
            elevation = surface.FindElevationAtXY(placement.Origin.X, placement.Origin.Y);
        }
        catch (Exception)
        {
            messages.Add($"ponto ({placement.Origin.X:0.###}; {placement.Origin.Y:0.###}) fora da superfície {surface.Name}");
            return false;
        }

        var changed = false;
        foreach (var target in definition.Parameters.Where(p => p.Target == TargetKind.Superficie && p.IsNumeric))
        {
            values[target.Name] = elevation;
            changed = true;
        }

        if (!changed)
        {
            messages.Add($"a definição {definition.Id} não tem parâmetro com 'alvo: superficie'");
        }

        return changed;
    }

    private static bool TryGetPipe(Transaction tr, Database db, string handle, out Pipe pipe)
    {
        pipe = null!;
        try
        {
            var id = db.GetObjectId(false, new Handle(Convert.ToInt64(handle, 16)), 0);
            if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Pipe found)
            {
                return false;
            }

            pipe = found;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

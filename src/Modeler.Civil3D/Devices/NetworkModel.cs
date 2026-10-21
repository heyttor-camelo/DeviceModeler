using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Modeler.Civil3D.Civil;
using Modeler.Civil3D.Persistence;
using Modeler.Core.Definition;
using Modeler.Core.Network;

namespace Modeler.Civil3D.Devices;

/// <summary>Uma rede do desenho: dispositivos com o mesmo nome de rede (ou ligados entre si, se não
/// têm nome) e os tubos que ligam dois deles.</summary>
internal sealed record DeviceNetwork(string Name, Dictionary<string, (BlockReference Reference, InstanceCache.Entry Entry)> Members, FlowGraph Graph, double Length);

/// <summary>Monta as redes do desenho a partir das conexões dos dispositivos: um tubo (handle) que
/// aparece em dois dispositivos é um trecho entre eles, com as elevações de fundo de cada lado. A
/// numeração segue o fluxo (<see cref="FlowGraph.Number"/>).</summary>
internal static class NetworkModel
{
    public static List<DeviceNetwork> Build(Transaction tr, Database db, IEnumerable<BlockReference>? only = null)
    {
        var references = (only ?? DeviceService.AllDevices(tr, db)).ToList();
        var entries = new List<(BlockReference Reference, InstanceCache.Entry Entry)>();
        foreach (var reference in references)
        {
            if (InstanceCache.Get(tr, reference) is { } entry)
            {
                entries.Add((reference, entry));
            }
        }

        // tubo → (dispositivo, elevação de fundo naquele lado)
        var ends = new Dictionary<string, List<(string Device, double Invert)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (reference, entry) in entries)
        {
            foreach (var c in Connections(entry))
            {
                if (c.GetValueOrDefault("ref") is string pipe && pipe.Length > 0 && c.GetValueOrDefault("cota") is double invert)
                {
                    if (!ends.TryGetValue(pipe, out var list))
                    {
                        ends[pipe] = list = new List<(string, double)>();
                    }

                    list.Add((reference.Handle.ToString(), invert));
                }
            }
        }

        var nodes = entries.Select(e => new FlowNode(e.Reference.Handle.ToString(), e.Entry.Record.Name, e.Reference.Position.X, e.Reference.Position.Y)).ToList();
        var byId = entries.ToDictionary(e => e.Reference.Handle.ToString(), StringComparer.Ordinal);
        var edges = new List<FlowEdge>();
        foreach (var (pipe, list) in ends.Where(kv => kv.Value.Count == 2))
        {
            var (a, b) = (list[0], list[1]);
            var length = byId[a.Device].Reference.Position.DistanceTo(byId[b.Device].Reference.Position);
            edges.Add(new FlowEdge(pipe, a.Device, b.Device, a.Invert, b.Invert, length));
        }

        var graph = new FlowGraph(nodes, edges);
        var result = new List<DeviceNetwork>();
        foreach (var component in graph.Components())
        {
            // nome: o mais comum entre os dispositivos; sem nome, "(sem rede) n"
            var name = component.Select(n => byId[n.Id].Entry.Record.Network).Where(n => n.Length > 0)
                .GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var ids = component.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var sub = new FlowGraph(component, edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)));
            result.Add(new DeviceNetwork(
                name ?? (component.Count == 1 ? $"(isolado) {component[0].Name}" : $"(sem nome) {component[0].Name}"),
                component.ToDictionary(n => n.Id, n => byId[n.Id], StringComparer.Ordinal),
                sub,
                sub.Edges.Sum(e => e.Length)));
        }

        return result;
    }

    public static IEnumerable<IReadOnlyDictionary<string, object>> Connections(InstanceCache.Entry entry) =>
        entry.Definition.Parameters.Where(p => p.Type == ParameterType.Conexoes)
            .SelectMany(p => entry.Instance.Parameters[p.Name] as IReadOnlyList<object> ?? Array.Empty<object>())
            .OfType<IReadOnlyDictionary<string, object>>();

    /// <summary>Numera a rede pelo fluxo e renomeia os dispositivos pelo <c>modelos.nome</c> (com
    /// <c>numero</c>); a estrutura nula ganha o mesmo nome. Nomes de fora da rede não são repetidos.</summary>
    public static List<string> Number(Transaction tr, Database db, DeviceNetwork network, NumberingMode mode, int start)
    {
        var messages = new List<string>();
        var numbers = network.Graph.Number(mode, start);
        var outside = new HashSet<string>(DeviceService.AllDevices(tr, db)
            .Where(r => !network.Members.ContainsKey(r.Handle.ToString()))
            .Select(r => DeviceRecord.TryRead(r, tr)!.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, number) in numbers.OrderBy(kv => kv.Value))
        {
            var (reference, entry) = network.Members[id];
            var record = DeviceRecord.TryRead(reference, tr)!;
            var name = entry.Definition.Templates.Name is { } template
                ? entry.Instance.Render(template, new Dictionary<string, object> { ["numero"] = (double)number, ["nome"] = record.Name })
                : $"{entry.Definition.Id.Split('_')[0].ToUpperInvariant()}-{number:00}";
            if (outside.Contains(name))
            {
                name = $"{name}-{network.Name}";
            }

            record.Number = number;
            record.Name = name;
            reference.UpgradeOpen();
            record.Write(reference, tr);
            if (CivilLinks.TryGet<Autodesk.Civil.DatabaseServices.Structure>(tr, db, record.StructureHandle, out var structure))
            {
                try
                {
                    structure.UpgradeOpen();
                    structure.Name = name;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // nome já usado por outra estrutura da rede: fica o do Civil
                }
            }

            messages.Add($"{number}: {name}");
        }

        return messages;
    }
}

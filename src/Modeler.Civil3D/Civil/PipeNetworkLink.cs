using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Modeler.Civil3D.Infra;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace Modeler.Civil3D.Civil;

/// <summary>Ligação com a Pipe Network do Civil 3D (estudo C §1.4): os tubos são tubos nativos e cada
/// dispositivo pode ter uma <b>estrutura nula</b> da rede no mesmo ponto, à qual os tubos se ligam. Os
/// tubos nativos mantêm dimensionamento, rótulos e perfis do Civil; o dispositivo (bloco com sólidos)
/// é sincronizado com a estrutura: mover o dispositivo move a estrutura e as pontas dos tubos.</summary>
internal static class PipeNetworkLink
{
    public static List<(ObjectId Id, string Name)> Networks(Transaction tr, Database db)
    {
        var civil = CivilDocument.GetCivilDocument(db);
        var result = new List<(ObjectId, string)>();
        foreach (ObjectId id in civil.GetPipeNetworkIds())
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Network network)
            {
                result.Add((id, network.Name));
            }
        }

        return result;
    }

    /// <summary>Rede pelo nome; se não existir, cria uma nova com a lista de peças pedida (ou a primeira
    /// do desenho).</summary>
    public static Network GetOrCreate(Transaction tr, Database db, string name, ObjectId partsListId)
    {
        var existing = Networks(tr, db).FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!existing.Id.IsNull)
        {
            return (Network)tr.GetObject(existing.Id, OpenMode.ForWrite);
        }

        var civil = CivilDocument.GetCivilDocument(db);
        var newName = name;
        var id = Network.Create(civil, ref newName);
        var network = (Network)tr.GetObject(id, OpenMode.ForWrite);
        if (partsListId.IsNull)
        {
            partsListId = PartsLists(tr, db).Select(p => p.Id).FirstOrDefault();
        }

        if (!partsListId.IsNull)
        {
            network.PartsListId = partsListId;
        }

        HydroLog.Info("Rede", $"rede de tubos '{newName}' criada");
        return network;
    }

    public static List<(ObjectId Id, string Name)> PartsLists(Transaction tr, Database db)
    {
        var result = new List<(ObjectId, string)>();
        foreach (ObjectId id in CivilDocument.GetCivilDocument(db).Styles.PartsListSet)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is PartsList list)
            {
                result.Add((id, list.Name));
            }
        }

        return result;
    }

    /// <summary>Tamanhos de tubo da lista de peças da rede: família, tamanho, descrição e diâmetro interno (m).</summary>
    public static List<PipeSize> PipeSizes(Transaction tr, Network network)
    {
        var result = new List<PipeSize>();
        if (network.PartsListId.IsNull || tr.GetObject(network.PartsListId, OpenMode.ForRead) is not PartsList list)
        {
            return result;
        }

        foreach (ObjectId familyId in list.GetPartFamilyIdsByDomain(DomainType.Pipe))
        {
            var family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
            for (var i = 0; i < family.PartSizeCount; i++)
            {
                var sizeId = family[i];
                var size = (PartSize)tr.GetObject(sizeId, OpenMode.ForRead);
                var diameter = Number(size, PartContextType.PipeInnerDiameter);
                result.Add(new PipeSize(familyId, sizeId, $"{family.Description} — {size.Name}", diameter));
            }
        }

        return result;
    }

    /// <summary>Primeira família de estrutura nula da lista de peças (e o primeiro tamanho dela).</summary>
    public static (ObjectId Family, ObjectId Size)? NullStructure(Transaction tr, Network network)
    {
        if (network.PartsListId.IsNull || tr.GetObject(network.PartsListId, OpenMode.ForRead) is not PartsList list)
        {
            return null;
        }

        foreach (ObjectId familyId in list.GetPartFamilyIdsByDomain(DomainType.Structure))
        {
            var family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
            if (family.PartType == PartType.StructNull && family.PartSizeCount > 0)
            {
                return (familyId, family[0]);
            }
        }

        return null;
    }

    /// <summary>Estrutura nula no ponto do dispositivo, com tampa e fundo nas elevações dele. Devolve
    /// nulo (e uma mensagem) se a lista de peças não tiver estrutura nula.</summary>
    public static ObjectId AddNullStructure(Transaction tr, Network network, Point3d location, double rotation, double rim, double sump, List<string> messages)
    {
        if (NullStructure(tr, network) is not { } part)
        {
            messages.Add($"a lista de peças da rede '{network.Name}' não tem estrutura nula: os tubos ficam sem estrutura nas pontas");
            return ObjectId.Null;
        }

        var id = ObjectId.Null;
        network.AddStructure(part.Family, part.Size, new Point3d(location.X, location.Y, rim), rotation, ref id, false);
        Sync(tr, id, location, rotation, rim, sump);
        return id;
    }

    /// <summary>Leva a estrutura para a posição e as elevações do dispositivo (os tubos ligados acompanham).</summary>
    public static void Sync(Transaction tr, ObjectId structureId, Point3d location, double rotation, double rim, double sump)
    {
        if (structureId.IsNull || structureId.IsErased || tr.GetObject(structureId, OpenMode.ForWrite) is not Structure structure)
        {
            return;
        }

        structure.Location = new Point3d(location.X, location.Y, rim);
        structure.Rotation = rotation;
        structure.RimElevation = rim;
        if (sump < rim)
        {
            structure.SumpElevation = sump;
        }
    }

    /// <summary>Tubo reto entre dois pontos (centro das pontas), ligado às estruturas quando houver.</summary>
    public static Pipe AddPipe(Transaction tr, Network network, PipeSize size, Point3d start, Point3d end, ObjectId startStructure, ObjectId endStructure)
    {
        var id = ObjectId.Null;
        network.AddLinePipe(size.Family, size.Size, new LineSegment3d(start, end), ref id, false);
        var pipe = (Pipe)tr.GetObject(id, OpenMode.ForWrite);
        if (!startStructure.IsNull)
        {
            pipe.ConnectToStructure(ConnectorPositionType.Start, startStructure, true);
        }

        if (!endStructure.IsNull)
        {
            pipe.ConnectToStructure(ConnectorPositionType.End, endStructure, true);
        }

        // ligar à estrutura pode mexer nas pontas: devolve às posições pedidas
        pipe.StartPoint = start;
        pipe.EndPoint = end;
        return pipe;
    }

    private static double Number(PartSize size, PartContextType context)
    {
        try
        {
            using var record = size.SizeDataRecord;
            var field = record.GetDataFieldBy(context);
            return field?.Value is double d ? d : Convert.ToDouble(field?.Value ?? 0.0, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}

/// <summary>Tamanho de tubo da lista de peças.</summary>
internal sealed record PipeSize(ObjectId Family, ObjectId Size, string Description, double InnerDiameter);

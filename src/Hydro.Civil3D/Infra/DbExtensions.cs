namespace Hydro.Civil3D;

/// <summary>Apoio ao banco do desenho.</summary>
internal static class DbExtensions
{
    /// <summary>Abre para escrita só se ainda não estiver: <c>UpgradeOpen</c> num objeto já aberto para
    /// escrita (recém-criado na mesma transação, por exemplo) lança <c>eWasOpenForWrite</c>.</summary>
    public static void Writable(this Autodesk.AutoCAD.DatabaseServices.DBObject obj)
    {
        if (!obj.IsWriteEnabled)
        {
            obj.Writable();
        }
    }
}

namespace Hydro.Civil3D;

/// <summary>Apoio ao banco do desenho.</summary>
internal static class DbExtensions
{
    /// <summary>Abre para escrita só se ainda não estiver: <c>UpgradeOpen</c> num objeto já aberto para
    /// escrita (recém-criado na mesma transação, por exemplo) lança <c>eWasOpenForWrite</c>.
    /// Atenção: aqui dentro é <c>UpgradeOpen()</c>. Uma troca em massa de UpgradeOpen por Writable já
    /// fez este método chamar a si mesmo (estouro de pilha, o Civil 3D fechava no HY_REDE), e o teste
    /// <c>PluginSourceTests</c> confere isso.</summary>
    public static void Writable(this Autodesk.AutoCAD.DatabaseServices.DBObject obj)
    {
        if (!obj.IsWriteEnabled)
        {
            obj.UpgradeOpen();
        }
    }
}

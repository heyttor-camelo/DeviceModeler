using Autodesk.AutoCAD.DatabaseServices;
using Hydro.Civil3D.Infra;
using Hydro.Civil3D.Persistence;

namespace Hydro.Civil3D.Devices;

/// <summary>Cópias e exclusões de dispositivos (técnica 4 do estudo E). <c>DeepClone</c> (COPY,
/// ARRAY, MIRROR...) só enfileira a cópia: no fim do comando o <see cref="DeviceWatcher"/> dá a ela um
/// bloco anônimo próprio, um id e um nome novos — antes, a cópia compartilhava o bloco até a
/// primeira edição. <c>Erase</c> avisa a fila para limpar quem depende do dispositivo (rótulos e
/// projeções). Nada é refeito dentro do overrule. <c>WblockClone</c> (copiar para outro desenho)
/// fica com o comportamento padrão: o registro vai junto e, no outro desenho, o dispositivo continua
/// sendo dispositivo se a definição estiver no catálogo. Não há recursão: o overrule chama o
/// <c>base.DeepClone</c> uma vez e só enfileira; a fila do fim do comando não clona nada.</summary>
internal sealed class DeviceObjectOverrule : ObjectOverrule
{
    private static DeviceObjectOverrule? _instance;

    public static void Register()
    {
        if (_instance is not null)
        {
            return;
        }

        _instance = new DeviceObjectOverrule();
        _instance.SetExtensionDictionaryEntryFilter(DeviceRecord.Key);
        AddOverrule(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), _instance, false);
        Overruling = true;
    }

    public static void Unregister()
    {
        if (_instance is null)
        {
            return;
        }

        RemoveOverrule(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)), _instance);
        _instance.Dispose();
        _instance = null;
    }

    public override DBObject DeepClone(DBObject dbObject, DBObject ownerObject, IdMapping idMap, bool isPrimary)
    {
        var clone = base.DeepClone(dbObject, ownerObject, idMap, isPrimary);
        // só cópias no mesmo desenho; dentro de definições de bloco (BEDIT, bloco nomeado) não
        Guard.Run("Cópia", () =>
        {
            if (clone is BlockReference && isPrimary && clone.Database == dbObject.Database && ownerObject is BlockTableRecord { IsLayout: true })
            {
                DeviceWatcher.QueueClone(clone);
            }
        });

        return clone;
    }

    public override void Erase(DBObject dbObject, bool erasing)
    {
        base.Erase(dbObject, erasing);
        if (erasing)
        {
            Guard.Run("Exclusão", () => DeviceWatcher.QueueErasedDevice(dbObject));
        }
    }
}

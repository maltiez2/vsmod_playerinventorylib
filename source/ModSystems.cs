using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Server;

namespace PlayerInventoryLib;

public sealed class PlayerInventoryLibSystem : ModSystem
{
    public override void StartPre(ICoreAPI api)
    {
        HarmonyPatchesManager.Patch(api);

        _clientApi = api as ICoreClientAPI;

        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(CharacterInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(CharacterInventory));

        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(BackpackInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(BackpackInventory));
    }

    public override void Dispose()
    {
        HarmonyPatchesManager.Unpatch();
    }

    public void RegisterSlotIcon(AssetLocation path)
    {
        if (_clientApi == null)
        {
            return;
        }
        _clientApi.Gui.Icons.CustomIcons[path.ToString()] = _clientApi.Gui.Icons.SvgIconSource(path);
    }


    private ICoreClientAPI? _clientApi;
}

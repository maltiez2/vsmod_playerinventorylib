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

        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(CharacterInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(CharacterInventory));
    }

    public override void Dispose()
    {
        HarmonyPatchesManager.Unpatch();
    }
}

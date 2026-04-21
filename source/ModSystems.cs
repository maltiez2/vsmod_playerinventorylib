using HarmonyLib;
using PlayerInventoryLib.Backpacks;
using PlayerInventoryLib.GUI;
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

        if (api is ICoreClientAPI clientApi)
        {
            ReplaceGuiDialog(clientApi.World as ClientMain ?? throw new Exception(), clientApi);
        }

        _clientApi = api as ICoreClientAPI;

        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(CharacterInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(CharacterInventory));

        (api as ServerCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(BackpackInventory));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.backpackInvClassName, typeof(BackpackInventory));
    }
    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass("PlayerInventoryLib:BackpackBehavior", typeof(BackpackBehavior));
        api.RegisterCollectibleBehaviorClass("PlayerInventoryLib:EnableSlotsBehavior", typeof(EnableSlotsBehavior));
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


    private static void ReplaceGuiDialog(ClientMain client, ICoreClientAPI api)
    {
        List<GuiDialog> loadedGuis = AccessTools.FieldRefAccess<ClientMain, List<GuiDialog>>(AccessTools.Field(typeof(ClientMain), "LoadedGuis")).Invoke(client);

        for (int i = 0; i < loadedGuis.Count; i++)
        {
            if (loadedGuis[i] is GuiDialogCharacter && loadedGuis[i].GetType() == typeof(GuiDialogCharacter))
            {
                loadedGuis[i].Dispose();
                loadedGuis[i] = new CustomGuiDialogCharacter(api);
            }
        }

        client.RegisterDialog(new GuiDialogCreativeInventory(api));
        client.RegisterDialog(new GuiDialogSurvivalInventory(api));

        api.Input.RegisterHotKey("inventorydialog-creative", Lang.Get("Open Creative Inventory"), GlKeys.E, HotkeyType.CharacterControls, shiftPressed: true);
    }
}

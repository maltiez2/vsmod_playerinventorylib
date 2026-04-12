using HarmonyLib;
using PlayerInventoryLib.GUI;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace PlayerInventoryLib.Integration;

public static class GuiDialogPatches
{
    public static GuiDialogInventory? GuiDialogInventoryInstance { get; set; }

    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        Api = api;

        ReplaceGuiDialog(api.World as ClientMain, api);

        /*new Harmony(harmonyId).Patch(
                typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CharacterTabPatch), nameof(CharacterTabPatch.GuiDialogCharacter_ComposeCharacterTab)))
            );*/
    }

    public static void Unpatch(string harmonyId)
    {
        //new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);

        Api = null;
    }

    public static ICoreClientAPI? Api { get; set; }

    static void ReplaceGuiDialog(ClientMain client, ICoreClientAPI api)
    {
        List<GuiDialog> loadedGuis = AccessTools.FieldRefAccess<ClientMain, List<GuiDialog>>(AccessTools.Field(typeof(ClientMain), "LoadedGuis")).Invoke(client);

        for (int i = 0; i < loadedGuis.Count; i++)
        {
            if (loadedGuis[i] is GuiDialogCharacter && loadedGuis[i].GetType() == typeof(GuiDialogCharacter))
            {
                loadedGuis[i].Dispose();
                loadedGuis[i] = new CustomGuiDialogCharacter(api);
                break;
            }
        }
    }
}
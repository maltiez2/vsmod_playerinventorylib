using HarmonyLib;
using PlayerInventoryLib.Integration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PlayerInventoryLib;

internal static class HarmonyPatchesManager
{
    public static void Patch(ICoreAPI api)
    {
        _api = api;

        PatchUniversalSide(api);

        if (api is ICoreClientAPI clientApi)
        {
            PatchClientSide(clientApi);
        }
    }
    public static void Unpatch()
    {
        UnpatchUniversalSide();
        UnpatchClientSide();
        _api = null;
    }


    private const string _harmonyId = "PlayerInventoryLib:";
    private const string _harmonyIdGuiDialog = _harmonyId + "GuiDialog";
    private const string _harmonyIdTranspilers = _harmonyId + "Transpilers";
    private const string _harmonyIdGeneral = _harmonyId + "General";

    private static ICoreAPI? _api;
    private static bool _patchedUniversalSide = false;
    private static bool _patchedClientSide = false;


    private static void PatchClientSide(ICoreClientAPI api)
    {
        if (_patchedClientSide)
        {
            return;
        }
        _patchedClientSide = true;

        GuiDialogPatches.Patch(_harmonyIdGuiDialog, api);
    }
    private static void UnpatchClientSide()
    {
        if (!_patchedClientSide)
        {
            return;
        }
        _patchedClientSide = false;

        GuiDialogPatches.Unpatch(_harmonyIdGuiDialog);
    }

    private static void PatchUniversalSide(ICoreAPI api)
    {
        if (_patchedUniversalSide)
        {
            return;
        }
        _patchedUniversalSide = true;

        new Harmony(_harmonyIdTranspilers).PatchAll();
    }
    private static void UnpatchUniversalSide()
    {
        if (!_patchedUniversalSide)
        {
            return;
        }
        _patchedUniversalSide = false;

       new Harmony(_harmonyIdTranspilers).UnpatchAll();
    }
}

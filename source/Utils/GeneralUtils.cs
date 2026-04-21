using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PlayerInventoryLib;

public static class GeneralUtils
{
    public static BackpackInventory? GetBackpackInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as BackpackInventory;
    }

    public static CharacterInventory? GetCharacterInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) as CharacterInventory;
    }
}

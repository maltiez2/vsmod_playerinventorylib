using PlayerInventoryLib.Vanity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace PlayerInventoryLib;

public static class GeneralUtils
{
    public static InventoryPlayerBackpacks? GetBackpackInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackpacks;
    }

    public static InventoryCharacter? GetCharacterInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) as InventoryCharacter;
    }

    public static VanityInventory? GetVanityInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(PlayerInventoryLibSystem.VanityInventoryCode) as VanityInventory;
    }
}

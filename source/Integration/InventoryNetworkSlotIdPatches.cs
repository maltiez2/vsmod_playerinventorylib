using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace PlayerInventoryLib.Integration;

/// <summary>
/// Patches the static getDoubleUpdatePacket to inject string slot IDs
/// when either inventory involved is a BackpackInventory.
/// This covers cross-inventory moves where the OTHER inventory's
/// network util generates the double update packet.
/// </summary>
[HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getDoubleUpdatePacket))]
public static class GetDoubleUpdatePacketPatch
{
    [HarmonyPostfix]
    public static void Postfix(Packet_Server __result, IPlayer player, string[] invIds, int[] slotIds)
    {
        Packet_InventoryDoubleUpdate doubleUpdate = __result.InventoryDoubleUpdate;

        IInventory inv1 = player.InventoryManager.GetInventory(invIds[0]);
        if (inv1 is BackpackInventory && doubleUpdate.ItemStack1 != null)
        {
            BackpackInventoryNetworkUtil.InjectStringSlotIdPublic(
                doubleUpdate.ItemStack1, inv1[slotIds[0]]);
        }

        IInventory inv2 = player.InventoryManager.GetInventory(invIds[1]);
        if (inv2 is BackpackInventory && doubleUpdate.ItemStack2 != null)
        {
            BackpackInventoryNetworkUtil.InjectStringSlotIdPublic(
                doubleUpdate.ItemStack2, inv2[slotIds[1]]);
        }
    }
}
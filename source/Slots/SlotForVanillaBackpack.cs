using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

public class SlotForVanillaBackpack : ItemSlotBackpack, IPlayerInventorySlot
{
    public SlotForVanillaBackpack(string playerUid, string slotId, InventoryBase inventory) : base(inventory)
    {
        PlayerUid = playerUid;
        SlotId = slotId;
    }

    public bool Enabled { get; set; } = true;

    public string PlayerUid { get; }

    public string SlotId { get; }

    public ComplexTagCondition<TagSet>? Tags => null;

    public override ItemStack TakeOutWhole()
    {
        if (inventory is IPlayerInventory playerInventory)
        {
            playerInventory.BeforeTakeOutWhole(this);
        }

        return base.TakeOutWhole();
    }
}
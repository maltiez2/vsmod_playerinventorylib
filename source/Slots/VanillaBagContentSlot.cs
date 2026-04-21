using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

public class VanillaBagContentSlot : ItemSlotBagContent, IBackpackSlot
{
    public VanillaBagContentSlot(ItemSlotBagContent slot, string backpackSlotId, IBackpack backpack, string playerUid, string slotId) : base(slot.Inventory, slot.BagIndex, slot.SlotIndex, slot.StorageType)
    {
        OriginalSlot = slot;
        Backpack = backpack;
        BackpackSlotId = backpackSlotId;
        PlayerUid = playerUid;
        SlotId = slotId;
        itemstack = slot.Itemstack;
    }

    public ItemSlotBagContent OriginalSlot { get; set; }

    public string BackpackSlotId { get; set; }

    public IBackpack Backpack { get; set; }

    public BackpackSlotConfig BackpackSlotConfig { get; set; } = new();

    public bool Enabled { get; set; }

    public string PlayerUid { get; set; }

    public string SlotId { get; set; }

    public ComplexTagCondition<TagSet>? Tags { get; set; }

    public string? FullSlotId { get; set; }
}
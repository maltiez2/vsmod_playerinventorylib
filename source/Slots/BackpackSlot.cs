using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

public class BackpackSlotConfig : SlotConfig
{
    public float Priority { get; set; } = 0;
    public string? BackpackCategory { get; set; }
    public float BackpackCategoryPriority { get; set; } = 0;
}

public class BackpackSlot : PlayerInventorySlot, IBackpackSlot
{
    public BackpackSlot(string backpackSlotId, IBackpack backpack, string slotId, InventoryBase inventory, BackpackSlotConfig config, string playerUid) : base(TagSet.Empty, slotId, inventory, config, playerUid)
    {
        BackpackSlotConfig = config;
        BackpackSlotId = backpackSlotId;
        Backpack = backpack;
    }

    public int SlotIndex { get; set; }
    public string BackpackSlotId { get; }
    public string? FullSlotId { get; set; }
    public IBackpack Backpack { get; }
    public BackpackSlotConfig BackpackSlotConfig { get; }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) // From ItemSlotSurvival to restrict not empty held bags
    {
        IHeldBag? bag = sourceSlot.Itemstack?.Collectible.GetCollectibleInterface<IHeldBag>();

        if (bag != null && !bag.IsEmpty(sourceSlot.Itemstack))
        {
            return false;
        }
        return base.CanTakeFrom(sourceSlot, priority);
    }

    public override bool CanHold(ItemSlot sourceSlot) // From ItemSlotSurvival to restrict not empty held bags
    {
        IHeldBag? bag = sourceSlot.Itemstack?.Collectible.GetCollectibleInterface<IHeldBag>();

        return base.CanHold(sourceSlot) && (bag == null || bag.IsEmpty(sourceSlot.Itemstack)) && inventory.CanContain(this, sourceSlot);
    }
}
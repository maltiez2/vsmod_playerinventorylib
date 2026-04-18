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
    public IBackpack Backpack { get; }
    public BackpackSlotConfig BackpackSlotConfig { get; }
}
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace PlayerInventoryLib;

public class PlaceholderItemSlot : ItemSlot, IPlayerInventorySlot
{
    public PlaceholderItemSlot(string playerUid, string slotId, InventoryBase inventory) : base(inventory)
    {
        PlayerUid = playerUid;
        SlotId = slotId;
    }

    public bool Enabled => false;

    public string PlayerUid { get; }

    public string SlotId { get; }

    public ComplexTagCondition<TagSet>? Tags => null;

    public override bool CanHold(ItemSlot sourceSlot) => false;

    public override bool CanTake() => false;

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // do nothing
    }
}
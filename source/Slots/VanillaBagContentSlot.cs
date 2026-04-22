using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

public class VanillaBagContentSlot : ItemSlotBagContent, IBackpackSlot, IClickableSlot
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

    public bool Enabled { get; set; } = true;

    public string PlayerUid { get; set; }

    public string SlotId { get; set; }

    public ComplexTagCondition<TagSet>? Tags { get; set; }

    public string? FullSlotId { get; set; }

    public TagSet ExcludeTags { get; set; }

    public TagSet RequiredTags { get; set; }


    public event IClickableSlot.SlotClickedDelegate? OnSlotClicked;

    public virtual bool FitsSlot(ItemStack stack)
    {
        if (stack.Collectible == null)
        {
            return false;
        }

        IBackpack? backpack = stack.Collectible.GetCollectibleInterface<IBackpack>();

        return Enabled
            && (backpack == null || !ExcludeTags.Overlaps(backpack.GetAdditionalTags(stack)))
            && (RequiredTags.IsEmpty || stack.Collectible.Tags.Overlaps(RequiredTags))
            && !stack.Collectible.Tags.Overlaps(ExcludeTags)
            && (Tags == null || Tags.Value.Matches(stack.Collectible.Tags));
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        IClickableSlot.EnumHandled handled = IClickableSlot.EnumHandled.Handled;
        OnSlotClicked?.Invoke(this, sourceSlot, ref op, ref handled);
        if (handled == IClickableSlot.EnumHandled.PreventAction)
        {
            return;
        }

        base.ActivateSlot(sourceSlot, ref op);
    }
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (sourceSlot?.Itemstack?.Collectible != null && !FitsSlot(sourceSlot.Itemstack))
        {
            return false;
        }

        return base.CanTakeFrom(sourceSlot, priority);
    }
    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot?.Itemstack?.Collectible != null && !FitsSlot(sourceSlot.Itemstack))
        {
            return false;
        }

        return base.CanHold(sourceSlot);
    }
}
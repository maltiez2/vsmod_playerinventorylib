using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;


public interface IClickableSlot
{
    enum EnumHandled
    {
        Handled,
        PreventAction
    }

    delegate bool SlotClickedDelegate(ItemSlot thisSlot, ItemSlot sourceSlot, ref ItemStackMoveOperation operation, ref EnumHandled handled);

    event SlotClickedDelegate? OnSlotClicked;
}


public class CharacterInventorySlot : ItemSlotCharacter, IClickableSlot
{
    public CharacterInventorySlot(TagSet tag, string slotId, InventoryBase inventory) : base(EnumCharacterDressType.Unknown, inventory)
    {
        SlotId = slotId;
        SlotIdTag = tag;
    }


    public IWorldAccessor? World { get; set; }
    public string? OwnerUUID { get; set; }

    public string SlotId { get; set; }
    public TagSet SlotIdTag { get; set; }
    public TagSet ExcludeTags { get; set; }

    public override EnumItemStorageFlags StorageType => EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit;

    public event IClickableSlot.SlotClickedDelegate? OnSlotClicked;


    public virtual bool FitsSlot(ItemStack stack)
    {
        return stack.Collectible.Tags.Overlaps(SlotIdTag) && !stack.Collectible.Tags.Overlaps(ExcludeTags);
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
    
    

    protected override bool CheckDressType(IItemStack itemstack, EnumCharacterDressType dressType) => true;
}

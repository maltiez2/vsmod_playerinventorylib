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

public interface IPlayerInventorySlot
{
    bool Enabled { get; }
    string PlayerUid { get; }
    string SlotId { get; }
    public ComplexTagCondition<TagSet>? Tags { get; }
}

public class PlayerInventorySlot : ItemSlot, IClickableSlot, IPlayerInventorySlot
{
    public PlayerInventorySlot(TagSet slotTag, string slotId, InventoryBase inventory, SlotConfig config, string playerUid) : base(inventory)
    {
        SlotId = slotId;
        SlotIdTag = slotTag;
        Config = config;

        Tags = config.Tags;
        HexBackgroundColor = config.Color;
        BackgroundIcon = config.Icon;
        PlayerUid = playerUid;
    }


    public bool Enabled { get; set; } = true;
    public string SlotId { get; set; }
    public TagSet SlotIdTag { get; set; }
    public TagSet ExcludeTags { get; set; }
    public ComplexTagCondition<TagSet>? Tags { get; set; }
    public SlotConfig Config { get; }
    public string PlayerUid { get; }

    public override EnumItemStorageFlags StorageType => EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit;

    public event IClickableSlot.SlotClickedDelegate? OnSlotClicked;


    public virtual bool FitsSlot(ItemStack stack)
    {
        return Enabled
            && (SlotIdTag.IsEmpty || stack.Collectible.Tags.Overlaps(SlotIdTag))
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

    public override ItemStack TakeOutWhole()
    {
        if (inventory is IPlayerInventory playerInventory)
        {
            playerInventory.BeforeTakeOutWhole(this);
        }

        return base.TakeOutWhole();
    }
}

public class ClothesSlot : ItemSlotCharacter, IClickableSlot, IPlayerInventorySlot
{
    public ClothesSlot(TagSet slotTag, string slotId, InventoryBase inventory, CharacterSlotConfig config, string playerUid, EnumCharacterDressType dressType) : base(dressType, inventory)
    {
        SlotId = slotId;
        SlotIdTag = slotTag;
        Config = config;

        Tags = config.Tags;
        HexBackgroundColor = config.Color;
        BackgroundIcon = config.Icon;
        PlayerUid = playerUid;

        if (dressType != EnumCharacterDressType.Unknown)
        {
            BackgroundIcon = IconByDressType[dressType];
        }
    }


    public bool Enabled { get; set; } = true;
    public string SlotId { get; set; }
    public TagSet SlotIdTag { get; set; }
    public TagSet ExcludeTags { get; set; }
    public ComplexTagCondition<TagSet>? Tags { get; set; }
    public CharacterSlotConfig Config { get; }
    public string PlayerUid { get; }

    public override EnumItemStorageFlags StorageType => EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit;

    public event IClickableSlot.SlotClickedDelegate? OnSlotClicked;


    public virtual bool FitsSlot(ItemStack stack)
    {
        return Enabled
            && stack.Collectible.Tags.Overlaps(SlotIdTag)
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
    public override ItemStack TakeOutWhole()
    {
        if (inventory is IPlayerInventory playerInventory)
        {
            playerInventory.BeforeTakeOutWhole(this);
        }

        return base.TakeOutWhole();
    }



    protected override bool CheckDressType(IItemStack itemstack, EnumCharacterDressType dressType) => true;

    protected readonly Dictionary<EnumCharacterDressType, string> IconByDressType = new()
    {
        { EnumCharacterDressType.Foot, "boots" },
        { EnumCharacterDressType.Hand, "gloves" },
        { EnumCharacterDressType.Shoulder, "cape" },
        { EnumCharacterDressType.Head, "hat" },
        { EnumCharacterDressType.LowerBody, "trousers" },
        { EnumCharacterDressType.UpperBody, "shirt" },
        { EnumCharacterDressType.UpperBodyOver, "pullover" },
        { EnumCharacterDressType.Neck, "necklace" },
        { EnumCharacterDressType.Arm, "bracers" },
        { EnumCharacterDressType.Waist, "belt" },
        { EnumCharacterDressType.Emblem, "medal" },
        { EnumCharacterDressType.Face, "mask" },

        { EnumCharacterDressType.ArmorHead, "armorhead" },
        { EnumCharacterDressType.ArmorBody, "armorbody" },
        { EnumCharacterDressType.ArmorLegs, "armorlegs" },
    };
}

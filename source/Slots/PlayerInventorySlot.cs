using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

public class PlayerInventorySlot : ItemSlot, IClickableSlot, IPlayerInventorySlot, IConfigurableSlot
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
    public SlotConfig Config { get; set; }
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

    public SlotConfig GetConfig() => Config;
    public void OverrideConfig(SlotConfig config)
    {
        ConfigBackup ??= Config;

        Config = config;
        Tags = config.Tags;
        HexBackgroundColor = config.Color;
        BackgroundIcon = config.Icon;
    }
    public void ResetConfig()
    {
        if (ConfigBackup == null) return;

        Config = ConfigBackup;
        Tags = Config.Tags;
        HexBackgroundColor = Config.Color;
        BackgroundIcon = Config.Icon;
        ConfigBackup = null;
    }

    public override ItemStack TakeOutWhole()
    {
        if (inventory is IPlayerInventory playerInventory)
        {
            playerInventory.BeforeTakeOutWhole(this);
        }

        return base.TakeOutWhole();
    }

    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        if (itemSlot.StackSize > MaxSlotStackSize) return false;

        bool canHoldHis = itemSlot.Empty || CanHold(itemSlot);
        bool canIExchange = canHoldHis && (Empty || CanTake());

        bool canHoldMine = Empty || itemSlot.CanHold(this);
        bool canHeExchange = canHoldMine && (itemSlot.Empty || itemSlot.CanTake());

        if (canIExchange && canHeExchange)
        {
            if (inventory is IPlayerInventory playerInventory)
            {
                playerInventory.BeforeTakeOutWhole(this);
            }

            return base.TryFlipWith(itemSlot);
        }

        return false;
    }


    protected SlotConfig? ConfigBackup;

    protected override void FlipWith(ItemSlot withSlot)
    {
        if (inventory is IPlayerInventory playerInventory)
        {
            playerInventory.BeforeTakeOutWhole(this);
        }

        base.FlipWith(withSlot);
    }
}

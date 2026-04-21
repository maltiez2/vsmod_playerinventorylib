using System.Diagnostics;
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

    public TagSet ExcludeTags { get; set; }

    public TagSet RequiredTags { get; set; }

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

    protected override void FlipWith(ItemSlot withSlot)
    {
        if (inventory is IPlayerInventory playerInventory)
        {
            playerInventory.BeforeTakeOutWhole(this);
        }

        base.FlipWith(withSlot);
    }
    

    /*public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        Debug.WriteLine($"{SlotId}: CanTakeFrom");
        return base.CanTakeFrom(sourceSlot, priority);
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        Debug.WriteLine($"{SlotId}: CanHold");
        return base.CanHold(sourceSlot);
    }

    public override bool CanTake()
    {
        Debug.WriteLine($"{SlotId}: CanTake");
        return base.CanTake();
    }

    public override ItemStack TakeOut(int quantity)
    {
        Debug.WriteLine($"{SlotId}: TakeOut");
        return base.TakeOut(quantity);
    }

    public override int TryPutInto(IWorldAccessor world, ItemSlot sinkSlot, int quantity = 1)
    {
        Debug.WriteLine($"{SlotId}: TryPutInto");
        return base.TryPutInto(world, sinkSlot, quantity);
    }

    public override int TryPutInto(ItemSlot sinkSlot, ref ItemStackMoveOperation op)
    {
        Debug.WriteLine($"{SlotId}: TryPutInto");
        return base.TryPutInto(sinkSlot, ref op);
    }

    

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        Debug.WriteLine($"{SlotId}: ActivateSlot");
        base.ActivateSlot(sourceSlot, ref op);
    }*/
}
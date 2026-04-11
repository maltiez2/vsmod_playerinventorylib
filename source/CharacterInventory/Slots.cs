using PlayerInventoryLib.Armor;
using PlayerInventoryLib.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace PlayerInventoryLib;


public class ClothesSlot : ItemSlotCharacter, IClickableSlot
{
    public IWorldAccessor? World { get; set; }
    public string? OwnerUUID { get; set; }
    public bool PreviouslyHeldBag { get; set; } = false;
    public int PreviousItemId { get; set; } = 0;
    public int PreviousDurability { get; set; } = 0;
    public string? PreviousColor { get; set; }
    public bool PreviousEmpty { get; set; } = false;

    public IClickableSlot.SlotClickedDelegate? OnSlotClicked { get; set; }

    public ClothesSlot(EnumCharacterDressType type, InventoryBase inventory) : base(type, inventory)
    {
        MaxSlotStackSize = 1;
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (OnSlotClicked?.Invoke(this, sourceSlot, ref op) == true) return;

        try
        {
            base.ActivateSlot(sourceSlot, ref op);
            OnItemSlotModified(null);
        }
        catch (Exception exception)
        {
            LoggerUtil.Debug(World?.Api, this, $"(ActivateSlot) Exception: {exception}");
        }
    }

    public override ItemStack? TakeOutWhole()
    {
        if (!CanTake())
        {
            return null;
        }

        ItemStack itemStack = base.TakeOutWhole();

        if (itemStack != null) EmptyBag(itemStack);

        return itemStack;
    }

    public override ItemStack? TakeOut(int quantity)
    {
        if (!CanTake())
        {
            return null;
        }

        ItemStack stack = base.TakeOut(quantity);

        EmptyBag(stack);

        return stack;
    }

    public override bool CanTake()
    {
        IHeldBag? bag = itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>();

        if (bag != null && !bag.IsEmpty(itemstack))
        {
            ((inventory as InventoryBasePlayer)?.Player?.Entity?.Api as ICoreClientAPI)?.TriggerIngameError(this, "canttakeout", "Cannot take out item. Empty its contents before removing it.");
            return false;
        }

        return base.CanTake();
    }

    protected override void FlipWith(ItemSlot itemSlot)
    {
        if (!CanTake())
        {
            return;
        }

        base.FlipWith(itemSlot);

        ItemStack stack = itemSlot.Itemstack;

        if (stack != null) EmptyBag(stack);
    }

    protected void EmptyBag(ItemStack stack)
    {
        IHeldBag? bag = stack?.Item?.GetCollectibleInterface<IHeldBag>();

        try
        {
            if (bag != null && World != null && World.PlayerByUid(OwnerUUID)?.Entity != null)
            {
                ItemStack?[] bagContent = bag.GetContents(stack, World);
                if (bagContent != null)
                {
                    foreach (ItemStack? bagContentStack in bagContent)
                    {
                        if (bagContentStack != null) World.SpawnItemEntity(bagContentStack, World.PlayerByUid(OwnerUUID)?.Entity?.SidedPos.AsBlockPos);
                    }
                }

                bag.Clear(stack);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(World?.Api, this, $"Error on emptying bag '{stack?.Collectible?.Code}': \n{exception}");
        }
    }

    protected void ModifyBackpackSlot()
    {
        InventoryPlayerBackpacks? backpack = GetBackpackInventory();
        if (backpack != null)
        {
            backpack[0].MarkDirty();
        }
    }

    protected InventoryPlayerBackpacks? GetBackpackInventory()
    {
        return World?.PlayerByUid(OwnerUUID)?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackpacks;
    }
}

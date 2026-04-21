using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib.Backpacks;

public class BackpackConfig
{
    public Dictionary<string, BackpackSlotGroupData> SlotGroups { get; set; } = [];
}

public class BackpackSlotGroupData : BackpackSlotConfig
{
    public int Number { get; set; } = 1;
    public bool DisplayInToolSelection { get; set; } = false;
}

public class BackpackBehavior : CollectibleBehavior, IBackpack
{
    public BackpackBehavior(CollectibleObject collObj) : base(collObj)
    {
    }


    public string BackpackId => collObj.Code;
    public BackpackConfig Config { get; set; } = new();


    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Config = properties.AsObject<BackpackConfig>() ?? new();
    }

    public virtual Dictionary<string, ItemSlot> GenerateSlots(ItemStack stack, IPlayerInventorySlot slotBackpackIsIn, string playerUid, InventoryBase inventory)
    {
        ItemSlot backpackSlot = slotBackpackIsIn as ItemSlot ?? throw new Exception("slotBackpackIsIn is not ItemSlot");


        Dictionary<string, ItemSlot> result = [];
        foreach ((string groupId, BackpackSlotGroupData config) in Config.SlotGroups)
        {
            for (int slotIndex = 0; slotIndex < config.Number; slotIndex++)
            {
                string slotId = $"{groupId}-{slotIndex}";
                BackpackSlot slot = new(slotBackpackIsIn.SlotId, this, slotId, inventory, config, slotBackpackIsIn.PlayerUid);
                result.Add(slotId, slot);
            }
        }

        ReadContentFromAttributes(stack, result);

        return result;
    }
    public virtual void OnBackpackSlotModified(IBackpackSlot backpackSlot)
    {

    }
    public virtual bool RequiresSlotsReload(IPlayerInventorySlot slotBackpackIsIn) => false;
    public virtual void StoreSlots(ItemStack stack, IPlayerInventorySlot slot, Dictionary<string, ItemSlot> slots)
    {
        WriteContentToAttributes(stack, slots);
    }
    


    protected virtual void ReadContentFromAttributes(ItemStack stack, Dictionary<string, ItemSlot> slots)
    {
        if (stack?.Attributes == null) return;

        ITreeAttribute backpackTree = stack.Attributes.GetTreeAttribute("backpackSlots");
        if (backpackTree == null) return;

        foreach ((string slotId, ItemSlot slot) in slots)
        {
            ItemStack storedStack = backpackTree.GetItemstack(slotId);
            if (storedStack == null) continue;

            storedStack.ResolveBlockOrItem(slot.Inventory.Api.World);
            slot.Itemstack = storedStack;
        }
    }

    protected virtual void WriteContentToAttributes(ItemStack stack, Dictionary<string, ItemSlot> slots)
    {
        if (stack?.Attributes == null) return;

        TreeAttribute backpackTree = new();

        foreach ((string slotId, ItemSlot slot) in slots)
        {
            if (slot.Itemstack != null)
            {
                backpackTree.SetItemstack(slotId, slot.Itemstack.Clone());
            }
        }

        stack.Attributes["backpackSlots"] = backpackTree;
    }
}

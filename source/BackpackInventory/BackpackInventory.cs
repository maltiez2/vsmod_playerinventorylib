using Cairo;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace PlayerInventoryLib;

public class BackpackSlotsSystem : ModSystem
{
    public int BackpackSlotsCount { get; set; } = 4;
}

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
}

public class BackpackInventory : InventoryPlayerBackpacks
{
    public BackpackInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        SlotsSystem = api.ModLoader.GetModSystem<BackpackSlotsSystem>() ?? throw new InvalidOperationException("Unable to find BackpackSlotsSystem");
        PlaceholderSlot = new(playerUID, "placeholder", this);
    }

    public BackpackInventory(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        SlotsSystem = api.ModLoader.GetModSystem<BackpackSlotsSystem>() ?? throw new InvalidOperationException("Unable to find BackpackSlotsSystem");
        PlaceholderSlot = new(playerUID, "placeholder", this);
    }

    public void ReloadBagInventory()
    {
        bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));
    }

    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        base.AfterBlocksLoaded(world);

    }

    public override void OnItemSlotModified(ItemSlot slot)
    {

    }

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {

    }

    public override void DiscardAll()
    {

    }

    public override void DropAll(Vec3d pos, int maxStackSize = 0)
    {

    }



    protected readonly BackpackSlotsSystem SlotsSystem;
    protected readonly Dictionary<string, ItemSlot> SlotsBySlotId = [];
    protected readonly Dictionary<string, List<ItemSlot>> SlotsByBagSlotId = [];
    protected readonly List<ItemSlot> SlotsByIndex = [];
    protected readonly PlaceholderItemSlot PlaceholderSlot;

    protected void AddSlots(string bagId, List<ItemSlot> slots)
    {
        if (SlotsByBagSlotId.TryGetValue(bagId, out List<ItemSlot>? value))
        {
            value.Clear();
        }
        else
        {
            SlotsByBagSlotId[bagId] = [];
        }

        int lastAvailableIndex = 0;
        foreach (ItemSlot slot in slots)
        {
            if (slot is not IPlayerInventorySlot playerSlot)
            {
                continue;
            }

            if (lastAvailableIndex >= SlotsByIndex.Count)
            {
                SlotsByIndex.Add(slot);
            }
            else
            {
                for (int slotIndex = lastAvailableIndex + 1; slotIndex < SlotsByIndex.Count; slotIndex++)
                {
                    lastAvailableIndex = slotIndex;
                    if (SlotsByIndex[slotIndex] == PlaceholderSlot)
                    {
                        SlotsByIndex[slotIndex] = slot;

                    }
                }
            }
        }
    }
}
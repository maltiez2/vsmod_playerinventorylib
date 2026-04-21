using System.Diagnostics;
using Vintagestory.API.Common;

namespace PlayerInventoryLib;


public class HeldBagBackpack : IBackpack
{
    public HeldBagBackpack(string backpackId, IHeldBag bag, int bagIndex, BackpackInventory backpackInventory)
    {
        BackpackId = backpackId;
        Bag = bag;
        BagIndex = bagIndex;
        BackpackInventory = backpackInventory;
    }


    public IHeldBag Bag { get; set; }
    public string BackpackId { get; set; }
    public int BagIndex { get; set; }
    public BackpackInventory BackpackInventory { get; set; }

    public Dictionary<string, ItemSlot> GenerateSlots(ItemStack stack, IPlayerInventorySlot slotBackpackIsIn, string playerUid, InventoryBase inventory)
    {
        List<ItemSlotBagContent> slots = Bag.GetOrCreateSlots(stack, inventory, BagIndex, BackpackInventory.Api.World);
        Dictionary<string, ItemSlot> result = [];
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            result.Add($"{slotIndex}", new VanillaBagContentSlot(slots[slotIndex], slotBackpackIsIn.SlotId, this, BackpackInventory.PlayerUID, $"{slotIndex}"));
        }

        return result;
    }

    public void OnBackpackSlotModified(IBackpackSlot backpackSlot)
    {

    }
    public bool RequiresSlotsReload(IPlayerInventorySlot slotBackpackIsIn) => false;
    public void StoreSlots(ItemStack stack, IPlayerInventorySlot slot, Dictionary<string, ItemSlot> slots)
    {
        foreach ((string id, ItemSlot bagSlot) in slots)
        {
            if (bagSlot is not VanillaBagContentSlot vanillaBagSlot)
            {
                continue;
            }

            vanillaBagSlot.OriginalSlot.Itemstack = vanillaBagSlot.Itemstack;
            Bag.Store(stack, vanillaBagSlot);

            if (stack != vanillaBagSlot.Itemstack)
            {
                Debug.WriteLine($"({bagSlot.Inventory.Api.Side}: {BackpackId}@{slot.SlotId}) Stored from {id}: {vanillaBagSlot.Itemstack?.Collectible?.Code}");
            }
        }
    }
}
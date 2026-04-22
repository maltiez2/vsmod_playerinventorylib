using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

public interface IBackpack
{
    string BackpackId { get; }

    Dictionary<string, ItemSlot> GenerateSlots(ItemStack stack, IPlayerInventorySlot slotBackpackIsIn, string playerUid, InventoryBase backpackInventory);
    void StoreSlots(ItemStack stack, IPlayerInventorySlot slot, Dictionary<string, ItemSlot> slots);
    void OnBackpackSlotModified(IBackpackSlot backpackSlot);
    /// <summary>
    /// If it is required to regenerate slots, when slot is modified.
    /// </summary>
    /// <param name="slotBackpackIsIn">Slot current backpack is in</param>
    /// <returns>if 'true', BackpackInventory.RemoveSlots and BackpackInventory.AddSlots will be called</returns>
    bool RequiresSlotsReload(IPlayerInventorySlot slotBackpackIsIn);
    TagSet GetAdditionalTags(ItemStack stack);
}
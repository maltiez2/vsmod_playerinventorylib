using Vintagestory.API.Common;

namespace PlayerInventoryLib;

public interface IPlayerInventory : IOwnedInventory
{
    void BeforeTakeOutWhole(ItemSlot slot);
    void OnItemSlotModified(ItemSlot slot);
    string PlayerUID { get; }
    void DropSlot(ItemSlot slot);
}
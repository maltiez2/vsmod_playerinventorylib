using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib;

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
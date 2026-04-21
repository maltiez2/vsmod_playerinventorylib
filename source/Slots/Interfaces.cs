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

public interface IConfigurableSlot
{
    SlotConfig GetConfig();

    void OverrideConfig(SlotConfig config);

    void ResetConfig();
}

public interface IPlayerInventorySlot
{
    bool Enabled { get; set; }
    string PlayerUid { get; }
    string SlotId { get; }
    public ComplexTagCondition<TagSet>? Tags { get; }
}

public interface IBackpackSlot : IPlayerInventorySlot
{
    int SlotIndex { get; set; }
    string BackpackSlotId { get; }
    IBackpack Backpack { get; }
    BackpackSlotConfig BackpackSlotConfig { get; }
}

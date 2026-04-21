using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PlayerInventoryLib.Backpacks;

public class EnableSlotsConfig
{
    public string[] SlotsToEnable { get; set; } = [];

    public Dictionary<string, SlotConfig> ConfigOverride { get; set; } = [];
}

public class EnableSlotsBehavior : CollectibleBehavior, IEnableSlots
{
    public EnableSlotsBehavior(CollectibleObject collObj) : base(collObj)
    {
    }


    public EnableSlotsConfig Config { get; set; } = new();


    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Config = properties.AsObject<EnableSlotsConfig>() ?? Config;
    }


    public Dictionary<string, SlotConfig> GetConfigOverrides(ItemSlot inSlot) => Config.ConfigOverride;
    public string[] GetSlotsToEnable(ItemSlot inSlot) => Config.SlotsToEnable;
    public void OnBeforeTakenOut(ItemSlot fromSlot, List<ItemSlot> enabledSlots)
    {
        foreach (ItemSlot slot in enabledSlots)
        {
            if (slot.Inventory is IPlayerInventory playerInventory)
            {
                playerInventory.DropSlot(slot);
            }
        }
    }
}

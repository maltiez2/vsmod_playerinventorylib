using PlayerInventoryLib.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace PlayerInventoryLib;

public class CharacterInventory : InventoryCharacter
{
    public CharacterInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        SlotsSystem = Api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new Exception("Unable to find 'CharacterSlotsSystem' when creating 'CharacterInventory'");
    }
    public CharacterInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        SlotsSystem = Api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new Exception("Unable to find 'CharacterSlotsSystem' when creating 'CharacterInventory'");
    }



    public override ItemSlot this[int slotId] { get => GetSlotByIndex(slotId); set => LoggerUtil.Warn(Api, this, "CharacterInventory slots cannot be set"); }
    public override int Count => SlotsById.Count;

    public event Action<CharacterInventory, ItemSlot, string, int>? OnSlotModified;



    public virtual ItemSlot GetSlot(string id) => SlotsById[id];

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        IPlayer? player = Api.World.PlayerByUid(playerUID);
        if (player == null)
        {
            throw new InvalidOperationException($"Failed to get player with UID '{playerUID}' when 'CharacterInventory.FromTreeAttributes'");
        }

        PreviousVanillaSerializedData = tree.GetTreeAttribute("slots");
        VanillaSlotsCount = tree.GetInt("qslots", VanillaSlotsCount);

        ITreeAttribute slotsTree = tree.GetTreeAttribute(SlotsDataAttributeName) ?? new TreeAttribute();

        int version = slotsTree.GetInt("version");
        if (version < CurrentImplementationVersion)
        {
            // process version changes
        }

        foreach (string slotId in SlotsSystem.SlotIndexToId)
        {
            ItemStack? itemStack = slotsTree.GetItemstack(slotId);
            SlotsById.Add(slotId, SlotsSystem.CreateSlot(slotId, out _, this, itemStack, player));
        }

        if (version == 0 && PreviousVanillaSerializedData != null) // Vanilla
        {
            for (int slotIndex = 0; slotIndex < SlotsSystem.DefaultVanillaSlotsOrder.Count; slotIndex++)
            {
                string slotId = SlotsSystem.DefaultVanillaSlotsOrder[slotIndex];

                ItemStack? itemStack = PreviousVanillaSerializedData.GetItemstack(slotIndex.ToString());
                if (SlotsById.ContainsKey(slotId))
                {
                    SlotsById[slotId].Itemstack = itemStack;
                }
            }
        }

        PreviousSerializedData = slotsTree;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        foreach ((string id, ItemSlot slot) in SlotsById)
        {
            PreviousSerializedData.SetItemstack(id, slot.Itemstack);
        }

        tree[SlotsDataAttributeName] = PreviousSerializedData;
        tree.SetInt("qslots", VanillaSlotsCount);
        tree["slots"] = PreviousVanillaSerializedData;
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        foreach ((string id, ItemSlot existingSlot) in SlotsById)
        {
            if (existingSlot == slot)
            {
                OnSlotModified?.Invoke(this, slot, id, SlotsSystem.SlotIdToIndex[id]);
                break;
            }
        }

        base.OnItemSlotModified(slot);
    }

    public override void OnOwningEntityDeath(Vec3d pos)
    {
        foreach (string slotId in SlotsSystem.SlotsThatDropItemsOnDeath)
        {
            ItemSlot slot = SlotsById[slotId];

            if (slot.Itemstack == null) continue;

            Api.World.SpawnItemEntity(slot.Itemstack, pos);

            slot.Itemstack = null;
            slot.MarkDirty();
        }
    }



    protected readonly CharacterSlotsSystem SlotsSystem;
    protected const int CurrentImplementationVersion = 1;
    protected readonly Dictionary<string, ItemSlot> SlotsById = [];
    protected ITreeAttribute PreviousSerializedData = new TreeAttribute();
    protected ITreeAttribute? PreviousVanillaSerializedData;
    protected int VanillaSlotsCount = 13;
    protected const string SlotsDataAttributeName = "plrinvlib:slots";


    protected virtual ItemSlot GetSlotByIndex(int index)
    {
        return SlotsById[SlotsSystem.SlotIndexToId[index]]; // @TODO: optimize later
    }
}

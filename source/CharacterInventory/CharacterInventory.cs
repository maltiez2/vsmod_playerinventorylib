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
        for (int slotIndex = 0; slotIndex < VanillaSlotsCount; slotIndex++)
        {
            DummySlots.Add(new ItemSlot(this));
            DummySlots[slotIndex].Itemstack = null;
        }
        if (SlotsSystem.Ready)
        {
            GenerateEmptySlots();
        }
        else
        {
            SlotsSystem.OnReady += GenerateEmptySlots;
        }
    }
    public CharacterInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        SlotsSystem = Api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new Exception("Unable to find 'CharacterSlotsSystem' when creating 'CharacterInventory'");
        for (int slotIndex = 0; slotIndex < VanillaSlotsCount; slotIndex++)
        {
            DummySlots.Add(new ItemSlot(this));
            DummySlots[slotIndex].Itemstack = null;
        }
        if (SlotsSystem.Ready)
        {
            GenerateEmptySlots();
        }
        else
        {
            SlotsSystem.OnReady += GenerateEmptySlots;
        }
    }



    public override ItemSlot this[int slotIndex] { get => GetSlotByIndex(slotIndex); set => LoggerUtil.Warn(Api, this, "CharacterInventory slots cannot be set"); }
    public override int Count => Math.Max(SlotsById.Count, DummySlots.Count);

    public event Action<CharacterInventory, ItemSlot, string, int>? OnSlotModified;

    public string OwnerUid => playerUID;


    public virtual ItemSlot GetSlot(string id) => SlotsById[id];

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        PreviousVanillaSerializedData = tree.GetTreeAttribute("slots") ?? new TreeAttribute();
        VanillaSlotsCount = tree.GetInt("qslots", VanillaSlotsCount);

        ITreeAttribute slotsTree = tree.GetTreeAttribute(SlotsDataAttributeName) ?? new TreeAttribute();

        int version = slotsTree.GetInt("version");
        if (version < CurrentImplementationVersion)
        {
            // process version changes
        }

        SlotsById.Clear();
        foreach (string slotId in SlotsSystem.SlotIndexToId)
        {
            ItemStack? itemStack = slotsTree.GetItemstack(slotId);
            SlotsById.Add(slotId, SlotsSystem.CreateSlot(slotId, out _, this, itemStack, playerUID));
        }

        if (version == 0) // Vanilla
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
        tree.SetInt("version", CurrentImplementationVersion);
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
        IPlayer? player = Api.World.PlayerByUid(playerUID);
        BackpackInventory? backpackInventory = GeneralUtils.GetBackpackInventory(player);

        foreach (string slotId in SlotsSystem.SlotsThatDropItemsOnDeath)
        {
            ItemSlot slot = SlotsById[slotId];

            if (slot.Itemstack == null) continue;

            IBackpack? backpack = slot.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
            if (backpackInventory != null && backpack != null && slot is IPlayerInventorySlot playerSlot && slot.Itemstack != null)
            {
                backpackInventory.RemoveSlots(backpack, slot.Itemstack, playerSlot);
            }

            Api.World.SpawnItemEntity(slot.Itemstack, pos);

            slot.Itemstack = null;
            slot.MarkDirty();
        }
    }

    public override int GetSlotId(ItemSlot slot)
    {
        for (int i = 0; i < Count; i++)
        {
            if (this[i] == slot)
            {
                return i;
            }
        }

        for (int i = 0; i < DummySlots.Count; i++)
        {
            if (DummySlots[i] == slot)
            {
                return i;
            }
        }

        return -1;
    }



    protected readonly CharacterSlotsSystem SlotsSystem;
    protected const int CurrentImplementationVersion = 1;
    protected readonly Dictionary<string, ItemSlot> SlotsById = [];
    protected readonly List<ItemSlot> DummySlots = [];
    protected ITreeAttribute PreviousSerializedData = new TreeAttribute();
    protected ITreeAttribute PreviousVanillaSerializedData = new TreeAttribute();
    protected int VanillaSlotsCount = 15;
    protected const string SlotsDataAttributeName = "plrinvlib:slots";
    protected readonly CharacterInventorySlotsState SlotsState = new();


    protected virtual ItemSlot GetSlotByIndex(int index)
    {
        if (SlotsById.Count == 0)
        {
            return DummySlots[index];
        }
        return SlotsById[SlotsSystem.SlotIndexToId[index]]; // @TODO: optimize later
    }

    protected void GenerateEmptySlots()
    {
        if (SlotsById.Count != 0)
        {
            return;
        }
        foreach (string slotId in SlotsSystem.SlotIndexToId)
        {
            SlotsById.Add(slotId, SlotsSystem.CreateSlot(slotId, out _, this, null, playerUID));
        }
    }
}

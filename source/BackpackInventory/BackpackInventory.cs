using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

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

public class BackpackSlot : PlayerInventorySlot, IBackpackSlot
{
    public BackpackSlot(string backpackSlotId, IBackpack backpack, string slotId, InventoryBase inventory, BackpackSlotConfig config, string playerUid) : base(TagSet.Empty, slotId, inventory, config, playerUid)
    {
        BackpackSlotConfig = config;
        BackpackSlotId = backpackSlotId;
        Backpack = backpack;
    }

    public int SlotIndex { get; set; }
    public string BackpackSlotId { get; }
    public IBackpack Backpack { get; }
    public BackpackSlotConfig BackpackSlotConfig { get; }
}

public class SlotForVanillaBackpack : ItemSlotBackpack, IPlayerInventorySlot
{
    public SlotForVanillaBackpack(string playerUid, string slotId, InventoryBase inventory) : base(inventory)
    {
        PlayerUid = playerUid;
        SlotId = slotId;
    }

    public bool Enabled { get; set; } = true;

    public string PlayerUid { get; }

    public string SlotId { get; }

    public ComplexTagCondition<TagSet>? Tags => null;
}

public interface IBackpackSlot : IPlayerInventorySlot
{
    int SlotIndex { get; }
    string BackpackSlotId { get; }
    IBackpack Backpack { get; }
    BackpackSlotConfig BackpackSlotConfig { get; }
}

public interface IBackpack
{
    string BackpackId { get; }

    Dictionary<string, BackpackSlotConfig> GetSlotsConfigs(ItemStack stack, IPlayerInventorySlot slotBackpackIsIn);
    void StoreSlots(ItemStack stack, IPlayerInventorySlot slot, Dictionary<string, ItemSlot> slots);
    void OnBackpackSlotModified(ItemSlot slot);
    bool RequiresSlotsReload(ItemStack previous, ItemStack current, IPlayerInventorySlot slotBackpackIsIn);
}

public class BackpackSlotConfig : SlotConfig
{
    public float Priority { get; set; } = 0;
    public string? BackpackCategory { get; set; }
    public float BackpackCategoryPriority { get; set; } = 0;
}


public class BackpackInventory : InventoryPlayerBackpacks
{
    public BackpackInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        SlotsSystem = api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new InvalidOperationException("Unable to find CharacterSlotsSystem");
        PlaceholderSlot = new(playerUID, "placeholder", this);
        SlotsByBackpackSlotId["vanilla"] = [];
        SlotsForBackpacksCount = SlotsSystem.BackpackSlotsCount;
        for (int slotIndex = 0; slotIndex < SlotsForBackpacksCount; slotIndex++)
        {
            SlotForVanillaBackpack slotForBackpack = new(playerUID, $"{slotIndex}", this);
            SlotsByIndex.Add(slotForBackpack);
            SlotsBySlotId.Add($"vanilla@self{slotIndex}@{slotIndex}", slotForBackpack);
            SlotsByBackpackSlotId["vanilla"].Add($"{slotIndex}", slotForBackpack);
        }
    }
    public BackpackInventory(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        SlotsSystem = api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new InvalidOperationException("Unable to find CharacterSlotsSystem");
        PlaceholderSlot = new(playerUID, "placeholder", this);
        SlotsByBackpackSlotId["vanilla"] = [];
        SlotsForBackpacksCount = SlotsSystem.BackpackSlotsCount;
        for (int slotIndex = 0; slotIndex < SlotsForBackpacksCount; slotIndex++)
        {
            SlotForVanillaBackpack slotForBackpack = new(playerUID, $"{slotIndex}", this);
            SlotsByIndex.Add(slotForBackpack);
            SlotsBySlotId.Add($"vanilla@self{slotIndex}@{slotIndex}", slotForBackpack);
            SlotsByBackpackSlotId["vanilla"].Add($"{slotIndex}", slotForBackpack);
        }
    }


    public override ItemSlot this[int slotId]
    {
        get
        {
            return SlotsByIndex[slotId];
        }
        set
        {
            throw new InvalidOperationException("BackpackInventory slots can not be set");
        }
    }
    public override int Count => SlotsByIndex.Count;
    public override int CountForNetworkPacket => Count;

    // Make inventories that contain backpakcs add those backpacks
    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        ResolveBlocksOrItems();

    }
    public override void OnItemSlotModified(ItemSlot slot)
    {
        if (slot is IBackpackSlot backpackSlot)
        {
            backpackSlot.Backpack.OnBackpackSlotModified(slot);
        }
    }
    public override void DiscardAll()
    {
        for (int slotBackpackIsInIndex = 0; slotBackpackIsInIndex < SlotsForBackpacksCount; slotBackpackIsInIndex++)
        {
            ItemSlot slotByIndex = SlotsByIndex[slotBackpackIsInIndex];
            if (slotByIndex is not SlotForVanillaBackpack slotForBackpack || slotByIndex.Itemstack == null)
            {
                continue;
            }

            IBackpack? backpack = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
            if (backpack != null)
            {
                RemoveSlots(backpack, slotByIndex.Itemstack, slotForBackpack);
            }
        }

        base.DiscardAll();
    }
    public override void DropAll(Vec3d pos, int maxStackSize = 0) // TODO: maxStackSize is ignored, may require fix
    {
        for (int slotBackpackIsInIndex = 0; slotBackpackIsInIndex < SlotsForBackpacksCount; slotBackpackIsInIndex++)
        {
            ItemSlot slotByIndex = SlotsByIndex[slotBackpackIsInIndex];
            if (slotByIndex is not SlotForVanillaBackpack slotForBackpack || slotByIndex.Itemstack == null)
            {
                continue;
            }

            EnumHandling handling = EnumHandling.PassThrough;
            slotByIndex.Itemstack.Collectible.OnHeldDropped(Api.World, Player, slotByIndex, slotByIndex.StackSize, ref handling);
            if (handling != EnumHandling.PassThrough)
            {
                continue;
            }

            IBackpack? backpack = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
            if (backpack != null)
            {
                RemoveSlots(backpack, slotByIndex.Itemstack, slotForBackpack);
            }

            Api.World.SpawnItemEntity(slotByIndex.Itemstack, pos);

            slotByIndex.Itemstack = null;
            slotByIndex.MarkDirty();
        }

        if (SlotsSystem.DropBackpackContent)
        {
            for (int slotBackpackIsInIndex = SlotsForBackpacksCount; slotBackpackIsInIndex < Count; slotBackpackIsInIndex++)
            {
                ItemSlot slotByIndex = SlotsByIndex[slotBackpackIsInIndex];
                if (slotByIndex is SlotForVanillaBackpack || slotByIndex.Itemstack == null)
                {
                    continue;
                }

                Api.World.SpawnItemEntity(slotByIndex.Itemstack, pos);

                slotByIndex.Itemstack = null;
                slotByIndex.MarkDirty();
            }
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        PreviousVanillaSerializedData = tree.GetTreeAttribute("slots") ?? new TreeAttribute();
        VanillaSlotsCount = tree.GetInt("qslots", VanillaSlotsCount);

        ITreeAttribute slotsTree = tree.GetTreeAttribute(SlotsDataAttributeName) ?? new TreeAttribute();
        PreviousSerializedData = slotsTree;

        int version = slotsTree.GetInt("version");
        if (version < CurrentImplementationVersion)
        {
            // process version changes
        }

        DeserializedSlotsContent.Clear();
        foreach (string slotId in SlotsSystem.SlotIndexToId)
        {
            ItemStack? itemStack = slotsTree.GetItemstack(slotId);
            DeserializedSlotsContent.Add(slotId, itemStack);
        }

        if (version == 0) // Vanilla
        {
            for (int slotIndex = 0; slotIndex < SlotsSystem.DefaultVanillaSlotsOrder.Count; slotIndex++)
            {
                ItemStack? itemStack = PreviousVanillaSerializedData.GetItemstack(slotIndex.ToString());
                DeserializedSlotsContent[$"vanilla@self{slotIndex}@{slotIndex}"] = itemStack;
            }
        }


        UpdateSlotsForVanillaBackpackSlots();
        SyncronizeSlotsContentFromDeserizliedData();
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        foreach ((string id, ItemSlot slot) in SlotsBySlotId)
        {
            PreviousSerializedData.SetItemstack(id, slot.Itemstack);
        }

        tree[SlotsDataAttributeName] = PreviousSerializedData;
        tree.SetInt("qslots", VanillaSlotsCount);
        tree["slots"] = PreviousVanillaSerializedData;
        tree.SetInt("version", CurrentImplementationVersion);
    }


    public virtual bool AddSlots(IBackpack backpack, ItemStack backpackStack, IPlayerInventorySlot slotBackpackIsIn, bool setFromDeserialized = true)
    {
        string backpackId = $"{slotBackpackIsIn.SlotId}@{backpack.BackpackId}";

        if (SlotsByBackpackSlotId.ContainsKey(backpackId))
        {
            return false;
        }
        SlotsByBackpackSlotId[backpackId] = [];

        Dictionary<string, BackpackSlotConfig> backpackSlots = backpack.GetSlotsConfigs(backpackStack, slotBackpackIsIn);
        foreach ((string slotId, BackpackSlotConfig config) in backpackSlots)
        {
            string backpackSlotId = $"{backpackId}@{slotId}";

            BackpackSlot slot = new(backpackSlotId, backpack, slotId, this, config, playerUID);

            SlotsBySlotId[backpackSlotId] = slot;
            SlotsByBackpackSlotId[backpackId].Add(slotId, slot);
            slot.SlotIndex = AddSlot(slot);

            if (setFromDeserialized && DeserializedSlotsContent.TryGetValue(backpackSlotId, out ItemStack? backpackSlotStack))
            {
                slot.Itemstack = backpackSlotStack;
                DeserializedSlotsContent.Remove(backpackSlotId);
            }
        }

        return true;
    }
    public virtual bool RemoveSlots(IBackpack backpack, ItemStack backpackStack, IPlayerInventorySlot slotBackpackIsIn, bool storeSlots = true)
    {
        string backpackId = $"{slotBackpackIsIn.SlotId}@{backpack.BackpackId}";

        if (!SlotsByBackpackSlotId.TryGetValue(backpackId, out Dictionary<string, ItemSlot>? backpackSlots))
        {
            return false;
        }
        SlotsByBackpackSlotId.Remove(backpackId);

        foreach ((string slotId, ItemSlot backpackSlot) in backpackSlots)
        {
            foreach ((string backpackSlotId, ItemSlot slot) in SlotsBySlotId)
            {
                if (slot == backpackSlot)
                {
                    SlotsBySlotId.Remove(backpackSlotId);
                    DeserializedSlotsContent.Remove(backpackSlotId);
                    break;
                }
            }

            RemoveSlot(backpackSlot);
        }

        if (storeSlots)
        {
            backpack.StoreSlots(backpackStack, slotBackpackIsIn, backpackSlots);
        }

        return true;
    }
    public virtual bool GetBackpackSlots(IBackpack backpack, PlayerInventorySlot slotBackpackIsIn, [NotNullWhen(true)] out IReadOnlyDictionary<string, ItemSlot>? backpackSlots)
    {
        string backpackId = $"{slotBackpackIsIn.SlotId}@{backpack.BackpackId}";

        if (!SlotsByBackpackSlotId.TryGetValue(backpackId, out Dictionary<string, ItemSlot>? backpackSlotsValue))
        {
            backpackSlots = null;
            return false;
        }

        backpackSlots = new ReadOnlyDictionary<string, ItemSlot>(backpackSlotsValue);
        return true;
    }
    public virtual ItemSlot GetSlotByBackpackId(string backpackId)
    {
        return SlotsBySlotId[backpackId];
    }



    protected const int CurrentImplementationVersion = 1;
    protected readonly CharacterSlotsSystem SlotsSystem;
    protected readonly Dictionary<string, ItemSlot> SlotsBySlotId = [];
    protected readonly Dictionary<string, Dictionary<string, ItemSlot>> SlotsByBackpackSlotId = [];
    protected readonly List<ItemSlot> SlotsByIndex = [];
    protected readonly PlaceholderItemSlot PlaceholderSlot;
    protected readonly Dictionary<string, ItemStack> DeserializedSlotsContent = [];
    protected ITreeAttribute PreviousSerializedData = new TreeAttribute();
    protected ITreeAttribute PreviousVanillaSerializedData = new TreeAttribute();
    protected readonly int SlotsForBackpacksCount;
    protected int VanillaSlotsCount = 4;
    protected const string SlotsDataAttributeName = "plrinvlib:slots";


    protected virtual int AddSlot(ItemSlot slot)
    {
        for (int slotIndex = 0; slotIndex < SlotsByIndex.Count; slotIndex++)
        {
            if (SlotsByIndex[slotIndex] == PlaceholderSlot)
            {
                SlotsByIndex[slotIndex] = slot;
                return slotIndex;
            }
        }

        SlotsByIndex.Add(slot);
        return SlotsByIndex.Count - 1;
    }
    protected virtual void RemoveSlot(ItemSlot slot)
    {
        for (int slotIndex = 0; slotIndex < SlotsByIndex.Count; slotIndex++)
        {
            if (SlotsByIndex[slotIndex] == slot)
            {
                SlotsByIndex[slotIndex] = PlaceholderSlot;
                return;
            }
        }
    }
    protected virtual void SyncronizeSlotsContentFromDeserizliedData()
    {
        foreach ((string backpackSlotId, ItemSlot bakpackSlot) in SlotsBySlotId)
        {
            if (DeserializedSlotsContent.TryGetValue(backpackSlotId, out ItemStack? deserializedStack))
            {
                bakpackSlot.Itemstack = deserializedStack;
                DeserializedSlotsContent.Remove(backpackSlotId);
            }
            else
            {
                bakpackSlot.Itemstack = null;
            }
        }
    }
    protected virtual void UpdateSlotsForVanillaBackpackSlots()
    {
        for (int slotBackpackIsInIndex = 0; slotBackpackIsInIndex < SlotsForBackpacksCount; slotBackpackIsInIndex++)
        {
            ItemSlot slotByIndex = SlotsByIndex[slotBackpackIsInIndex];
            if (slotByIndex is not SlotForVanillaBackpack slotForBackpack || slotByIndex.Itemstack == null)
            {
                continue;
            }

            IBackpack? backpack = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
            if (backpack == null)
            {
                continue;
            }

            RemoveSlots(backpack, slotByIndex.Itemstack, slotForBackpack, storeSlots: false);
            AddSlots(backpack, slotByIndex.Itemstack, slotForBackpack, setFromDeserialized: false);
            slotByIndex.MarkDirty();
        }
    }
}
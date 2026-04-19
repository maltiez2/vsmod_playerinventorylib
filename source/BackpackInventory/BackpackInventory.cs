using PlayerInventoryLib.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace PlayerInventoryLib;


public class BackpackInventory : InventoryPlayerBackpacks, IPlayerInventory
{
    public BackpackInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        SlotsSystem = api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new InvalidOperationException("Unable to find CharacterSlotsSystem");
        PlaceholderSlot = new(playerUID, "placeholder", this)
        {
            HexBackgroundColor = "#999999"
        };
        SlotsForBackpacksCount = SlotsSystem.BackpackSlotsCount;
        GenerateSlotsForVanillaBackpacks();
    }
    public BackpackInventory(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        SlotsSystem = api.ModLoader.GetModSystem<CharacterSlotsSystem>() ?? throw new InvalidOperationException("Unable to find CharacterSlotsSystem");
        PlaceholderSlot = new(playerUID, "placeholder", this)
        {
            HexBackgroundColor = "#999999"
        };
        SlotsForBackpacksCount = SlotsSystem.BackpackSlotsCount;
        GenerateSlotsForVanillaBackpacks();
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
    public string PlayerUID => playerUID;


    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        ResolveBlocksOrItems();
    }
    public virtual void BeforeTakeOutWhole(ItemSlot slot)
    {
        if (slot is not SlotForVanillaBackpack slotForBackpack)
        {
            return;
        }

        IBackpack? backpack = slot.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
        if (backpack != null && slot.Itemstack != null)
        {
            RemoveSlots(backpack, slot.Itemstack, slotForBackpack);
        }

        IHeldBag? bag = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>();
        if (bag != null && slotForBackpack.Itemstack != null)
        {
            string backpackId = $"{slotForBackpack.SlotId}@vanilla";
            if (SlotsByBackpackSlotId.ContainsKey(backpackId))
            {
                RemoveHeldBagSlots(bag, slotForBackpack);
            }
        }
    }
    public override void OnItemSlotModified(ItemSlot slot)
    {
        if (slot is IBackpackSlot backpackSlot)
        {
            backpackSlot.Backpack.OnBackpackSlotModified(backpackSlot);
        }
        else if (slot is SlotForVanillaBackpack slotForBackpack)
        {
            OnVanillaBackpackSlotModified(slotForBackpack);
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

        int version = tree.GetInt("version");
        if (version < CurrentImplementationVersion)
        {
            // process version changes
        }

        DeserializedSlotsContent.Clear();
        foreach ((string slotId, _) in slotsTree)
        {
            ItemStack? itemStack = slotsTree.GetItemstack(slotId);
            DeserializedSlotsContent.Add(slotId, itemStack);
        }

        if (version == 0) // Vanilla
        {
            for (int slotIndex = 0; slotIndex < SlotsForBackpacksCount; slotIndex++)
            {
                ItemStack? itemStack = PreviousVanillaSerializedData.GetItemstack(slotIndex.ToString());
                DeserializedSlotsContent[$"self{slotIndex}"] = itemStack;
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
    public override void ResolveBlocksOrItems()
    {
        foreach ((string code, ItemStack? stack) in DeserializedSlotsContent)
        {
            if (stack != null && !stack.ResolveBlockOrItem(Api.World))
            {
                DeserializedSlotsContent[code] = null;
            }
        }

        base.ResolveBlocksOrItems();
    }


    public virtual bool AddSlots(IBackpack backpack, ItemStack backpackStack, IPlayerInventorySlot slotBackpackIsIn, bool setFromDeserialized = true)
    {
        string backpackId = $"{slotBackpackIsIn.SlotId}@{backpack.BackpackId}";

        if (SlotsByBackpackSlotId.ContainsKey(backpackId))
        {
            return false;
        }
        SlotsByBackpackSlotId[backpackId] = [];

        Dictionary<string, ItemSlot> backpackSlots = backpack.GenerateSlots(backpackStack, slotBackpackIsIn, playerUID);
        foreach ((string slotId, ItemSlot slot) in backpackSlots)
        {
            if (slot is not IBackpackSlot backpackSlot)
            {
                LoggerUtil.Error(Api, this, $"Trying to add backpack slots from '{backpackStack.Collectible?.Code}' but supplied slot with id '{slotId}' is not 'IBackpackSlot'");
                continue;
            }

            string backpackSlotId = $"{backpackId}@{slotId}";

            SlotsBySlotId[backpackSlotId] = slot;
            SlotsByBackpackSlotId[backpackId].Add(slotId, slot);
            backpackSlot.SlotIndex = AddSlot(slot);

            if (setFromDeserialized && DeserializedSlotsContent.TryGetValue(backpackSlotId, out ItemStack? backpackSlotStack))
            {
                slot.Itemstack = backpackSlotStack;
                DeserializedSlotsContent.Remove(backpackSlotId);
            }
        }

        return true;
    }
    public virtual bool RemoveSlots(IBackpack backpack, ItemStack? backpackStack, IPlayerInventorySlot slotBackpackIsIn, bool storeSlots = true)
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

        if (storeSlots && backpackStack != null)
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
    public virtual ItemSlot GetSlotByBackpackSlotId(string backpackSlotId)
    {
        return SlotsBySlotId[backpackSlotId];
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot is IBackpackSlot playerSlot)
        {
            return base.GetSuitability(sourceSlot, targetSlot, isMerge) + playerSlot.BackpackSlotConfig.Priority;
        }
        else
        {
            return base.GetSuitability(sourceSlot, targetSlot, isMerge);
        }
    }



    protected const int CurrentImplementationVersion = 1;
    protected readonly CharacterSlotsSystem SlotsSystem;
    protected readonly Dictionary<string, ItemSlot> SlotsBySlotId = [];
    protected readonly Dictionary<string, Dictionary<string, ItemSlot>> SlotsByBackpackSlotId = [];
    protected readonly List<ItemSlot> SlotsByIndex = [];
    protected readonly PlaceholderItemSlot PlaceholderSlot;
    protected readonly Dictionary<string, ItemStack?> DeserializedSlotsContent = [];
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
    protected void GenerateSlotsForVanillaBackpacks()
    {
        for (int slotIndex = 0; slotIndex < SlotsForBackpacksCount; slotIndex++)
        {
            SlotForVanillaBackpack slotForBackpack = new(playerUID, $"self{slotIndex}", this);
            SlotsByIndex.Add(slotForBackpack);
            SlotsBySlotId.Add($"self{slotIndex}", slotForBackpack);
        }
    }

    protected virtual bool AddHeldBagSlots(IHeldBag bag, SlotForVanillaBackpack slotForBackpack)
    {
        if (slotForBackpack.Itemstack == null)
        {
            return false;
        }
        
        HeldBagBackpack bagBackpack = new("vanilla", bag, GetSlotId(slotForBackpack), this);

        return AddSlots(bagBackpack, slotForBackpack.Itemstack, slotForBackpack);
    }
    protected virtual bool RemoveHeldBagSlots(IHeldBag bag, SlotForVanillaBackpack slotForBackpack)
    {
        if (slotForBackpack.Itemstack == null)
        {
            return false;
        }

        HeldBagBackpack bagBackpack = new("vanilla", bag, GetSlotId(slotForBackpack), this);

        return RemoveSlots(bagBackpack, slotForBackpack.Itemstack, slotForBackpack);
    }
    protected virtual void OnVanillaBackpackSlotModified(SlotForVanillaBackpack slotForBackpack)
    {
        IBackpack? backpack = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
        if (backpack != null && slotForBackpack.Itemstack != null)
        {
            string backpackId = $"{slotForBackpack.SlotId}@{backpack.BackpackId}";
            if (SlotsByBackpackSlotId.ContainsKey(backpackId))
            {
                if (backpack.RequiresSlotsReload(slotForBackpack))
                {
                    RemoveSlots(backpack, null, slotForBackpack);
                    AddSlots(backpack, slotForBackpack.Itemstack, slotForBackpack);
                }
            }
            else
            {
                AddSlots(backpack, slotForBackpack.Itemstack, slotForBackpack);
            }
            return;
        }

        IHeldBag? bag = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>();
        if (bag != null && slotForBackpack.Itemstack != null)
        {
            string backpackId = $"{slotForBackpack.SlotId}@vanilla";
            if (!SlotsByBackpackSlotId.ContainsKey(backpackId))
            {
                AddHeldBagSlots(bag, slotForBackpack);
            }
        }
    }
}
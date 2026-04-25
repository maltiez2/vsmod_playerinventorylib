using OverhaulLib.Utils;
using PlayerInventoryLib.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
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
        InvNetworkUtil = new BackpackInventoryNetworkUtil(this, api);
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
        InvNetworkUtil = new BackpackInventoryNetworkUtil(this, api);
    }

    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        Api = api;
        string[] elems = inventoryID.Split('-', 2);
        className = elems[0];
        instanceID = elems[1];

        if (InvNetworkUtil == null)
        {
            InvNetworkUtil = new BackpackInventoryNetworkUtil(this, api);
        }
        else
        {
            InvNetworkUtil.Api = api;
        }

        AfterBlocksLoaded(api.World);
    }


    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < SlotsByIndex.Count)
            {
                return SlotsByIndex[slotId];
            }
            return PlaceholderSlot;
        }
        set
        {
            throw new InvalidOperationException("BackpackInventory slots can not be set");
        }
    }
    public override int Count => SlotsByIndex.Count + 1;
    public override int CountForNetworkPacket => Count;
    public string PlayerUID => playerUID;
    public int VanillaBackpackSlotsCount => SlotsForBackpacksCount;


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
            string backpackId = GetBackpackFullId("vanilla", slotForBackpack.SlotId);
            if (SlotsByBackpackSlotId.ContainsKey(backpackId))
            {
                RemoveHeldBagSlots(bag, slotForBackpack);
            }
        }
    }
    public override void OnItemSlotModified(ItemSlot slot)
    {
        foreach (ItemSlot existingSlot in SlotsByIndex)
        {
            if (existingSlot == slot)
            {
                if (slot.Itemstack?.Collectible != null)
                {
                    if (slot.Itemstack.Collectible is IOnSlotModifiedListener listener)
                    {
                        listener.OnSlotModified(slot);
                    }

                    foreach (IOnSlotModifiedListener behavior in slot.Itemstack.Collectible.CollectibleBehaviors.OfType<IOnSlotModifiedListener>())
                    {
                        behavior.OnSlotModified(slot);
                    }
                }
                break;
            }
        }

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
    public virtual void DropSlot(ItemSlot slot)
    {
        if (slot is not SlotForVanillaBackpack slotForBackpack || slot.Itemstack == null)
        {
            return;
        }

        if (Player?.Entity?.Pos?.XYZ == null)
        {
            return;
        }

        Vec3d position = Player.Entity.Pos.XYZ;

        IBackpack? backpack = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
        if (backpack != null)
        {
            RemoveSlots(backpack, slot.Itemstack, slotForBackpack);
        }

        Api.World.SpawnItemEntity(slot.Itemstack, position);

        slot.Itemstack = null;
        slot.MarkDirty();
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
            for (int slotBackpackIsInIndex = SlotsForBackpacksCount; slotBackpackIsInIndex < Count - 1; slotBackpackIsInIndex++)
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
    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        object packet = InvNetworkUtil.GetActivateSlotPacket(slotId, op);

        if (op.ShiftDown)
        {
            sourceSlot = this[slotId];
            string stackName = sourceSlot.Itemstack?.GetName();
            string sourceInv = sourceSlot.Inventory?.InventoryID;

            StringBuilder shiftClickDebugText = new();

            op.RequestedQuantity = sourceSlot.StackSize;
            op.ActingPlayer.InventoryManager.TryTransferAway(sourceSlot, ref op, false, shiftClickDebugText);

            Api.World.Logger.Audit("{0} shift clicked slot {1} in {2}. Moved {3}x{4} to ({5})", op.ActingPlayer?.PlayerName, slotId, sourceInv, op.MovedQuantity, stackName, shiftClickDebugText.ToString());
        }
        else
        {
            this[slotId].ActivateSlot(sourceSlot, ref op);
        }

        return packet;
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

    public static string GetBackpackFullId(string backpackId, string slotForBackpackId) => $"{backpackId}@{slotForBackpackId}";
    public static string GetSlotFullId(string backpackId, string slotForBackpackId, string slotId) => $"{backpackId}@{slotForBackpackId}@{slotId}";
    public static string GetSlotFullId(string backpackFullId, string slotId) => $"{backpackFullId}@{slotId}";



    public virtual bool TryAddOrUpdateSlots(IBackpack backpack, ItemStack backpackStack, IPlayerInventorySlot slotForBackpack, bool setFromDeserialized = true, bool storeSlots = true)
    {
        string backpackId = GetBackpackFullId(backpack.BackpackId, slotForBackpack.SlotId);
        if (!SlotsByBackpackSlotId.ContainsKey(backpackId))
        {
            return AddSlots(backpack, backpackStack, slotForBackpack, setFromDeserialized);
        }

        if (backpack.RequiresSlotsReload(slotForBackpack))
        {
            RemoveSlots(backpack, backpackStack, slotForBackpack, storeSlots);
            return AddSlots(backpack, backpackStack, slotForBackpack, setFromDeserialized);
        }

        return false;
    }
    public virtual bool AddSlots(IBackpack backpack, ItemStack backpackStack, IPlayerInventorySlot slotForBackpack, bool setFromDeserialized = true)
    {
        string backpackId = GetBackpackFullId(backpack.BackpackId, slotForBackpack.SlotId);

        //Log.Warn(Api, this, $"({Api.Side}) Adding '{backpackId}' slots");

        if (SlotsByBackpackSlotId.ContainsKey(backpackId))
        {
            return false;
        }
        SlotsByBackpackSlotId[backpackId] = [];

        Dictionary<string, ItemSlot> backpackSlots = backpack.GenerateSlots(backpackStack, slotForBackpack, playerUID, this);
        foreach ((string slotId, ItemSlot slot) in backpackSlots)
        {
            if (slot is not IBackpackSlot backpackSlot)
            {
                LoggerUtil.Error(Api, this, $"Trying to add backpack slots from '{backpackStack.Collectible?.Code}' but supplied slot with id '{slotId}' is not 'IBackpackSlot'");
                continue;
            }

            string backpackSlotId = GetSlotFullId(backpack.BackpackId, slotForBackpack.SlotId, backpackSlot.SlotId);
            backpackSlot.FullSlotId = backpackSlotId;

            SlotsBySlotId[backpackSlotId] = slot;
            SlotsByBackpackSlotId[backpackId].Add(slotId, slot);
            AddSlot(slot);

            if (setFromDeserialized && DeserializedSlotsContent.TryGetValue(backpackSlotId, out ItemStack? backpackSlotStack))
            {
                slot.Itemstack = backpackSlotStack;
            }
        }

        return true;
    }
    public virtual bool RemoveSlots(IBackpack backpack, ItemStack? backpackStack, IPlayerInventorySlot slotForBackpack, bool storeSlots = true)
    {
        string backpackId = GetBackpackFullId(backpack.BackpackId, slotForBackpack.SlotId);

        //Log.Warn(Api, this, $"({Api.Side}) Removing '{backpackId}' slots");

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

        if (storeSlots && backpackStack != null && Api.Side == EnumAppSide.Server)
        {
            backpack.StoreSlots(backpackStack, slotForBackpack, backpackSlots);
        }

        return true;
    }
    public virtual bool GetBackpackSlots(IBackpack backpack, PlayerInventorySlot slotForBackpack, [NotNullWhen(true)] out IReadOnlyDictionary<string, ItemSlot>? backpackSlots)
    {
        string backpackId = GetBackpackFullId(backpack.BackpackId, slotForBackpack.SlotId);

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
    public virtual bool GetSlotByBackpackSlotId(string fullSlotId, [NotNullWhen(true)] out ItemSlot? slot)
    {
        return SlotsBySlotId.TryGetValue(fullSlotId, out slot);
    }
    public virtual void SetDeserializedSlotContent(string fullSlotId, ItemStack? content)
    {
        DeserializedSlotsContent[fullSlotId] = content;
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
    protected const string ExcludeTag = "slot-exclude-backpack";
    protected TagSet? ExcludeTagSet;


    protected virtual int AddSlot(ItemSlot slot)
    {
        if (slot is IPlayerInventorySlot playerSlot)
        {
            ExcludeTagSet ??= TagsUtil.Get(ExcludeTag);
            playerSlot.ExcludeTags = playerSlot.ExcludeTags.Union(ExcludeTagSet.Value);
        }

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
                break;
            }
        }

        for (int slotIndex = SlotsByIndex.Count - 1; slotIndex > 0; slotIndex--)
        {
            if (SlotsByIndex[slotIndex] != PlaceholderSlot)
            {
                if (slotIndex != SlotsByIndex.Count - 1)
                {
                    SlotsByIndex.RemoveRange(slotIndex + 1, SlotsByIndex.Count - slotIndex - 1);
                }
                break;
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
        }
    }
    protected virtual void UpdateSlotsForVanillaBackpackSlots()
    {
        for (int slotBackpackIsInIndex = 0; slotBackpackIsInIndex < SlotsForBackpacksCount; slotBackpackIsInIndex++)
        {
            ItemSlot slotByIndex = SlotsByIndex[slotBackpackIsInIndex];

            if (slotByIndex is not SlotForVanillaBackpack slotForBackpack) continue;

            if (DeserializedSlotsContent.TryGetValue(slotForBackpack.SlotId, out ItemStack? stack))
            {
                slotByIndex.Itemstack = stack;
                stack?.ResolveBlockOrItem(Api.World);
            }

            if (slotByIndex.Itemstack == null) continue;

            IBackpack? backpack = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
            IHeldBag? bag = slotForBackpack.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>();

            if (backpack != null)
            {
                AddSlots(backpack, slotByIndex.Itemstack, slotForBackpack, setFromDeserialized: false);
                slotByIndex.MarkDirty();
            }
            else if (bag != null)
            {
                AddHeldBagSlots(bag, slotForBackpack);
                slotByIndex.MarkDirty();
            }
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
            string backpackId = GetBackpackFullId(backpack.BackpackId, slotForBackpack.SlotId);
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
            string backpackId = GetBackpackFullId("vanilla", slotForBackpack.SlotId);
            if (!SlotsByBackpackSlotId.ContainsKey(backpackId))
            {
                AddHeldBagSlots(bag, slotForBackpack);
            }
        }
    }
}
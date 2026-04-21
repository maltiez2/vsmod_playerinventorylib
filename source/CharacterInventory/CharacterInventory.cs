using OverhaulLib.Utils;
using PlayerInventoryLib.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace PlayerInventoryLib;


public class CharacterInventory : InventoryCharacter, IPlayerInventory
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

    public event Action? OnGuiRecomposeRequest;

    public string PlayerUID => playerUID;


    public virtual ItemSlot GetSlot(string id) => SlotsById[id];

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        PreviousVanillaSerializedData = tree.GetTreeAttribute("slots") ?? new TreeAttribute();
        VanillaSlotsCount = tree.GetInt("qslots", VanillaSlotsCount);

        ITreeAttribute slotsTree = tree.GetTreeAttribute(SlotsDataAttributeName) ?? new TreeAttribute();

        int version = tree.GetInt("version");
        if (version < CurrentImplementationVersion)
        {
            // process version changes
        }

        SlotsById.Clear();
        foreach (string slotId in SlotsSystem.SlotIndexToId)
        {
            ItemStack? itemStack = slotsTree.GetItemstack(slotId);
            itemStack?.ResolveBlockOrItem(Api.World);
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

    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        foreach ((_, ItemSlot slot) in SlotsById)
        {
            ProcessBackpack(slot);
            ProcessEnableSlots(slot);
        }
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

    public virtual void BeforeTakeOutWhole(ItemSlot slot)
    {
        RevertEnableSlots(slot);
        RevertBackpack(slot);
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

        ProcessBackpack(slot);
        ProcessEnableSlots(slot);

        base.OnItemSlotModified(slot);
    }

    public override void OnOwningEntityDeath(Vec3d pos)
    {
        foreach (string slotId in SlotsSystem.SlotsThatDropItemsOnDeath)
        {
            ItemSlot slot = SlotsById[slotId];

            if (slot.Itemstack == null) continue;

            RevertEnableSlots(slot);
            RevertBackpack(slot);

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

    public virtual void DropSlot(ItemSlot slot)
    {
        if (slot.Itemstack == null || !SlotsById.ContainsValue(slot))
        {
            return;
        }

        if (Player?.Entity?.Pos?.XYZ == null)
        {
            return;
        }

        Vec3d position = Player.Entity.Pos.XYZ;

        ProcessEnableSlots(slot);
        RevertBackpack(slot);

        ItemStack stack = slot.TakeOutWhole();

        Api.World.SpawnItemEntity(stack, position);

        slot.MarkDirty();
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
    protected readonly Dictionary<string, List<string>> EnabledSlotsBySlot = [];
    protected readonly Dictionary<string, List<string>> OverridenSlotsBySlot = [];


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

    protected virtual void ProcessEnableSlots(ItemSlot slot)
    {
        IEnableSlots? enableSlots = slot.Itemstack?.Collectible?.GetCollectibleInterface<IEnableSlots>();
        if (enableSlots == null || slot.Itemstack == null || slot is not IPlayerInventorySlot playerInventorySlot)
        {
            return;
        }

        if (EnabledSlotsBySlot.ContainsKey(playerInventorySlot.SlotId))
        {
            return;
        }

        string[] slotsToEnable = enableSlots.GetSlotsToEnable(slot);
        Dictionary<string, SlotConfig> configsOverride = enableSlots.GetConfigOverrides(slot);

        List<string> enabledSlots = [];
        List<string> overridenSlots = [];
        EnabledSlotsBySlot[playerInventorySlot.SlotId] = enabledSlots;
        OverridenSlotsBySlot[playerInventorySlot.SlotId] = overridenSlots;

        foreach (string slotToEnable in slotsToEnable)
        {
            if (!SlotsById.TryGetValue(slotToEnable, out ItemSlot? slotById) || slotById is not IPlayerInventorySlot playerSlotToEnable) continue;

            if (!playerSlotToEnable.Enabled)
            {
                playerSlotToEnable.Enabled = true;
                enabledSlots.Add(slotToEnable);
            }
        }

        foreach ((string slotToOverride, SlotConfig configToOverrideWith) in configsOverride)
        {
            if (!SlotsById.TryGetValue(slotToOverride, out ItemSlot? slotById) || slotById is not IConfigurableSlot configurableSLot) continue;

            configurableSLot.OverrideConfig(configToOverrideWith);
            overridenSlots.Add(slotToOverride);
        }

        OnGuiRecomposeRequest?.Invoke();
    }

    protected virtual void RevertEnableSlots(ItemSlot slot)
    {
        IEnableSlots? enableSlots = slot.Itemstack?.Collectible?.GetCollectibleInterface<IEnableSlots>();
        if (enableSlots == null || slot.Itemstack == null || slot is not IPlayerInventorySlot playerInventorySlot)
        {
            return;
        }

        if (!EnabledSlotsBySlot.ContainsKey(playerInventorySlot.SlotId))
        {
            return;
        }

        List<string> enabledSlotsIds = EnabledSlotsBySlot[playerInventorySlot.SlotId];
        List<string> overridenSlotsIds = OverridenSlotsBySlot[playerInventorySlot.SlotId];
        string[] slotsToEnable = enableSlots.GetSlotsToEnable(slot);
        List<ItemSlot> enabledSlots = slotsToEnable.Select(slotId => SlotsById[slotId]).ToList();

        enableSlots.OnBeforeTakenOut(slot, enabledSlots);

        foreach (string slotId in enabledSlotsIds)
        {
            if (!SlotsById.TryGetValue(slotId, out ItemSlot? slotById) || slotById is not IPlayerInventorySlot playerSlotToEnable) continue;

            playerSlotToEnable.Enabled = false;
        }

        foreach (string slotId in overridenSlotsIds)
        {
            if (!SlotsById.TryGetValue(slotId, out ItemSlot? slotById) || slotById is not IConfigurableSlot configurableSLot) continue;

            configurableSLot.ResetConfig();
        }

        EnabledSlotsBySlot.Remove(playerInventorySlot.SlotId);
        OverridenSlotsBySlot.Remove(playerInventorySlot.SlotId);

        OnGuiRecomposeRequest?.Invoke();
    }

    protected virtual void ProcessBackpack(ItemSlot slot)
    {
        IBackpack? backpack = slot.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
        BackpackInventory? inventory = Api.World.PlayerByUid(playerUID)?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName) as BackpackInventory;

        if (inventory == null)
        {
            Log.Error(Api, this, $"Unable to get player backpack inventory when updating backpacks from character inventory");
            return;
        }

        if (backpack != null && slot is IPlayerInventorySlot playerSlot && slot.Itemstack != null)
        {
            inventory.TryAddOrUpdateSlots(backpack, slot.Itemstack, playerSlot);
        }
    }

    protected virtual void RevertBackpack(ItemSlot slot)
    {
        IBackpack? backpack = slot.Itemstack?.Collectible?.GetCollectibleInterface<IBackpack>();
        if (backpack != null && Player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName) is BackpackInventory inventory && slot is IPlayerInventorySlot playerSlot && slot.Itemstack != null)
        {
            inventory.RemoveSlots(backpack, slot.Itemstack, playerSlot);
        }
    }
}

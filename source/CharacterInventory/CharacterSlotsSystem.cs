using OverhaulLib.Utils;
using PlayerInventoryLib.Utils;
using System.Collections.Immutable;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PlayerInventoryLib;


public delegate ItemSlot CreateCharacterSlotDelegate(CharacterInventory inventory, ItemStack? stack, string playerUid, ICoreAPI api, int index, string id);

public interface IEnableSlots
{
    string[] GetSlotsToEnable(ItemSlot inSlot);
    Dictionary<string, SlotConfig> GetConfigOverrides(ItemSlot inSlot);
    void OnBeforeTakenOut(ItemSlot fromSlot, List<ItemSlot> enabledSlots);
}

public class CharacterInventorySlotsState
{
    public List<int> LeftSlots { get; } = [];
    public List<int> RightSlots { get; } = [];
    public List<(string, List<int>)> Groups { get; } = [];

    public bool CompareAndSetFrom(CharacterInventorySlotsState other)
    {
        bool hasChanges = false;

        if (!LeftSlots.SequenceEqual(other.LeftSlots))
        {
            LeftSlots.Clear();
            LeftSlots.AddRange(other.LeftSlots);
            hasChanges = true;
        }

        if (!RightSlots.SequenceEqual(other.RightSlots))
        {
            RightSlots.Clear();
            RightSlots.AddRange(other.RightSlots);
            hasChanges = true;
        }

        bool groupsChanged = Groups.Count != other.Groups.Count ||
            Groups
                .Zip(other.Groups, (a, b) => a.Item1 != b.Item1 || !a.Item2.SequenceEqual(b.Item2))
                .Any(changed => changed);

        if (groupsChanged)
        {
            Groups.Clear();
            Groups.AddRange(other.Groups.Select(group => (group.Item1, new List<int>(group.Item2))));
            hasChanges = true;
        }

        return hasChanges;
    }
}

public class SlotConfig
{
    public bool OverrideTags { get; set; } = false;
    public ComplexTagCondition<TagSet> Tags { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public class CharacterSlotConfig : SlotConfig
{
    public bool Disabled { get; set; } = false;
    public bool HiddenWhenDisabled { get; set; } = true;
    public string? Group { get; set; }
    public string? DisabledColor { get; set; }
    public float Priority { get; set; } = -1;

    public CharacterSlotConfig()
    {

    }

    public CharacterSlotConfig(CharacterSlotConfig previous, SlotConfig overrides)
    {
        if (overrides.OverrideTags)
        {
            Tags = overrides.Tags;
        }
        Icon = overrides.Icon;
        Color = overrides.Color;
        Disabled = previous.Disabled;
        HiddenWhenDisabled = previous.HiddenWhenDisabled;
        Group = previous.Group;
        DisabledColor = previous.DisabledColor;
        Priority = previous.Priority;
    }
}

public class CharacterSlotGroupGuiConfig
{
    public string Text { get; set; } = "";
    public bool HideWhenNoSlotsShown { get; set; } = true;
    public float Priority { get; set; } = -1;
}

public class CharacterInventoryConfig
{
    public Dictionary<string, CharacterSlotConfig> VanillaSlots { get; set; } = [];
    public Dictionary<string, CharacterSlotGroupGuiConfig> Groups { get; set; } = [];
    public Dictionary<string, CharacterSlotConfig> Slots { get; set; } = [];
}

public class CharacterSlotsSystem : ModSystem
{
    public bool Ready { get; private set; }
    public ImmutableDictionary<string, int> SlotIdToIndex { get; private set; } = ImmutableDictionary.Create<string, int>();
    public ImmutableList<string> SlotIndexToId { get; private set; } = [];
    public ImmutableDictionary<string, TagSet> SlotIdToTag { get; private set; } = ImmutableDictionary.Create<string, TagSet>();
    public ImmutableDictionary<string, CharacterSlotConfig> SlotIdToConfig { get; private set; } = ImmutableDictionary.Create<string, CharacterSlotConfig>();
    public ImmutableDictionary<string, CharacterSlotGroupGuiConfig> GroupIdToGuiConfig { get; private set; } = ImmutableDictionary.Create<string, CharacterSlotGroupGuiConfig>();
    public List<string> DefaultVanillaSlotsOrder { get; } = [
        "Head",
        "Shoulder",
        "UpperBody",
        "LowerBody",
        "Foot",
        "Hand",
        "Neck",
        "Emblem",
        "Face",
        "Waist",
        "Arm",
        "UpperBodyOver",
        "ArmorHead",
        "ArmorBody",
        "ArmorLegs"
    ];
    public HashSet<string> SlotsThatDropItemsOnDeath { get; } = [];
    public CharacterInventorySlotsState SlotsState { get; } = new();
    public int BackpackSlotsCount { get; set; } = 4;
    public bool DropBackpackContent { get; set; } = true;

    public event Action? OnReady;

    public override double ExecuteOrder() => 0.1;


    public void RegisterSlotGroup(string groupId, CharacterSlotGroupGuiConfig config)
    {
        _groupConfigs.Add(groupId, config);
    }
    public void RegisterSlot(string slotId, CreateCharacterSlotDelegate createSlotDelegate, CharacterSlotConfig config)
    {
        if (_createSlotDelegates.ContainsKey(slotId))
        {
            throw new DuplicatedSlotIdException($"Slot with id '{slotId}' already registered.");
        }

        _createSlotDelegates.Add(slotId, createSlotDelegate);
        _configs.Add(slotId, config);
    }
    public void RegisterSlot(string slotId, CharacterSlotConfig config)
    {
        RegisterSlot(slotId, CreatePlayerInventorySlot, config);
    }
    public void RegisterPlayerInventorySlot(string slotId, CharacterSlotConfig config)
    {
        if (_createSlotDelegates.ContainsKey(slotId))
        {
            throw new DuplicatedSlotIdException($"Slot with id '{slotId}' already registered.");
        }

        _createSlotDelegates.Add(slotId, CreatePlayerInventorySlot);
        _configs.Add(slotId, config);
    }



    public ItemSlot CreateSlot(string slotId, out int index, CharacterInventory inventory, ItemStack? stack, string playerUid)
    {
        if (_api == null || !Ready)
        {
            throw new InvalidOperationException($"Trying to create '{slotId}' slot before 'CharacterInventorySystem' is ready.");
        }

        if (!_createSlotDelegates.ContainsKey(slotId))
        {
            throw new UnknownSlotIdException($"(CreateSlot) Slot with id '{slotId}' is not registered.");
        }

        index = SlotIdToIndex[slotId];
        return _createSlotDelegates[slotId].Invoke(inventory, stack, playerUid, _api, index, slotId);
    }
    public bool RecalculateSlotsState(IPlayer player)
    {
        CharacterInventory? inventory = GeneralUtils.GetCharacterInventory(player);
        if (inventory == null)
        {
            return false;
        }

        CharacterInventorySlotsState newState = new();

        List<(string, List<int>)> groups = [];
        foreach ((string groupId, CharacterSlotGroupGuiConfig groupConfig) in GroupIdToGuiConfig)
        {
            groups.Add((groupId, []));
        }

        foreach ((string slotId, CharacterSlotConfig slotConfig) in SlotIdToConfig)
        {
            ItemSlot slot = inventory.GetSlot(slotId);
            if (slot is not IPlayerInventorySlot playerSlot)
            {
                continue;
            }

            if (!playerSlot.Enabled && slotConfig.HiddenWhenDisabled)
            {
                continue;
            }

            switch (slotConfig.Group)
            {
                case null:
                    break;
                case "left":
                    newState.LeftSlots.Add(SlotIdToIndex[slotId]);
                    break;
                case "right":
                    newState.RightSlots.Add(SlotIdToIndex[slotId]);
                    break;
                default:
                    foreach ((string groupId, List<int> indexes) in groups)
                    {
                        if (groupId == slotConfig.Group)
                        {
                            indexes.Add(SlotIdToIndex[slotId]);
                            break;
                        }
                    }
                    break;
            }
        }

        groups = groups.OrderBy(entry => GroupIdToGuiConfig[entry.Item1].Priority).ToList();

        foreach ((string groupId, List<int> indexes) in groups)
        {
            if (indexes.Count > 0)
            {
                newState.Groups.Add((groupId, indexes));
            }
        }

        return SlotsState.CompareAndSetFrom(newState);
    }


    public override void StartPre(ICoreAPI api)
    {
        _api = api;
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        CharacterInventoryConfig inventoryConfig = LoadInventoryConfigFromAssets(api);
        ApplyInventoryConfig(inventoryConfig);
        ProcessSlots();

        if (api is ICoreServerAPI serverApi)
        {
            RegisterSlotTags(serverApi);

            Ready = true;
            OnReady?.Invoke();
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api is ICoreClientAPI)
        {
            CollectSlotTags(api);
            Ready = true;
            OnReady?.Invoke();
        }

        AddTagsToCollectibles(api);
    }



    private readonly Dictionary<string, CreateCharacterSlotDelegate> _createSlotDelegates = [];
    private readonly Dictionary<string, CharacterSlotConfig> _configs = [];
    private readonly Dictionary<string, CharacterSlotGroupGuiConfig> _groupConfigs = [];
    private const string _slotsConfigFile = "config/characterslotsconfig.json";
    private ICoreAPI? _api;


    private void ApplyInventoryConfig(CharacterInventoryConfig config)
    {
        foreach ((string slotCode, CharacterSlotConfig slotConfig) in config.VanillaSlots)
        {
            RegisterSlot(slotCode, CreateClothesSlot, slotConfig);
        }

        foreach ((string slotCode, CharacterSlotConfig slotConfig) in config.Slots)
        {
            RegisterSlot(slotCode, CreatePlayerInventorySlot, slotConfig);
        }

        foreach ((string groupCode, CharacterSlotGroupGuiConfig groupConfig) in config.Groups)
        {
            RegisterSlotGroup(groupCode, groupConfig);
        }
    }

    private CharacterInventoryConfig LoadInventoryConfigFromAssets(ICoreAPI api)
    {
        Dictionary<AssetLocation, CharacterInventoryConfig> inventoryConfigs = api.Assets.GetMany<CharacterInventoryConfig>(api.World.Logger, _slotsConfigFile);

        CharacterInventoryConfig result = new();

        foreach ((AssetLocation location, CharacterInventoryConfig config) in inventoryConfigs)
        {
            if (location.Domain == "playerinventorylib")
            {
                result.VanillaSlots = config.VanillaSlots;
            }

            foreach ((string groupCode, CharacterSlotGroupGuiConfig groupConfig) in config.Groups)
            {
                string groupCodeWithDomain = groupCode;
                if (!groupCode.Contains(':'))
                {
                    groupCodeWithDomain = location.Domain + ":" + groupCode;
                }
                result.Groups[groupCodeWithDomain] = groupConfig;
            }

            foreach ((string slotCode, CharacterSlotConfig slotConfig) in config.Slots)
            {
                string slotCodeWithDomain = slotCode;
                if (!slotCode.Contains(':'))
                {
                    slotCodeWithDomain = location.Domain + ":" + slotCode;
                }
                result.Slots[slotCodeWithDomain] = slotConfig;
            }
        }

        return result;
    }

    private void ProcessSlots()
    {
        IOrderedEnumerable<string> moddedSlotsOrder = _createSlotDelegates.Keys.Except(DefaultVanillaSlotsOrder).Order();
        IEnumerable<string> slotsOrder = DefaultVanillaSlotsOrder.Concat(moddedSlotsOrder);

        SlotIndexToId = slotsOrder.ToImmutableList();

        Dictionary<string, int> idToIndex = [];
        for (int slotIndex = 0; slotIndex < SlotIndexToId.Count; slotIndex++)
        {
            idToIndex[SlotIndexToId[slotIndex]] = slotIndex;
        }
        SlotIdToIndex = idToIndex.ToImmutableDictionary();
        SlotIdToConfig = _configs.ToImmutableDictionary();
        GroupIdToGuiConfig = _groupConfigs.ToImmutableDictionary();

        LoggerUtil.Notify(_api, this, $"Registered character slots: {SlotIndexToId.Aggregate((a, b) => $"{a}, {b}")}");
    }

    private void RegisterSlotTags(ICoreServerAPI api)
    {
        Dictionary<string, TagSet> tags = [];
        foreach (string slotId in SlotIndexToId)
        {
            string tagName = $"slot-{slotId.ToLowerInvariant().Replace(':', '-')}";
            api.CollectibleTagRegistry.Register(tagName);
            TagSet tag = api.CollectibleTagRegistry.CreateTagSet(tagName);
            tags.Add(slotId, tag);
        }
        SlotIdToTag = tags.ToImmutableDictionary();
    }

    private void CollectSlotTags(ICoreAPI api)
    {
        Dictionary<string, TagSet> tags = [];
        foreach (string slotId in SlotIndexToId)
        {
            string tagName = $"slot-{slotId.ToLowerInvariant().Replace(':', '-')}";
            TagSet tag = api.CollectibleTagRegistry.CreateTagSet(tagName);
            tags.Add(slotId, tag);
        }
        SlotIdToTag = tags.ToImmutableDictionary();
    }

    private void AddTagsToCollectibles(ICoreAPI api)
    {
        List<CollectibleObject> collectibles = api.World.Collectibles;
        StringBuilder addedTags = new();
        addedTags.AppendLine("Added tags for character inventory slots to collectibles:");
        foreach (CollectibleObject collectible in collectibles)
        {
            ItemStack stack = new(collectible);
            stack.ResolveBlockOrItem(api.World);
            string? clothesCategory = IAttachableToEntity.FromCollectible(collectible)?.GetCategoryCode(stack);

            if (clothesCategory == null)
            {
                continue;
            }

            foreach (string slotId in SlotIndexToId.Where(slotId => slotId.Equals(clothesCategory, StringComparison.InvariantCultureIgnoreCase)))
            {
                addedTags.AppendLine($"Added 'slot-{slotId.ToLowerInvariant()}' tag to '{collectible.Code}'");
                TagSet slotTag = SlotIdToTag[slotId];
                // WTF IS THIS MESS!? Where are my set opreations in TagSet!?
                /*IEnumerable<string> collectibleTags = api.CollectibleTagRegistry.SlowEnumerateTagNames(collectible.Tags);
                IEnumerable<string> slotTags = api.CollectibleTagRegistry.SlowEnumerateTagNames(slotTag);
                collectible.Tags = api.CollectibleTagRegistry.CreateTagSet(collectibleTags.Concat(slotTags));*/
                // Here they are, implemented them myself
                collectible.Tags = collectible.Tags.Union(slotTag);
            }
        }
        Log.Verbose(api, this, addedTags.ToString());
    }

    private ItemSlot CreateClothesSlot(CharacterInventory inventory, ItemStack? stack, string playerUid, ICoreAPI api, int index, string id)
    {
        return new ClothesSlot(SlotIdToTag[id], id, inventory, _configs[id], playerUid, Enum.Parse<EnumCharacterDressType>(id))
        {
            Itemstack = stack
        };
    }

    private ItemSlot CreatePlayerInventorySlot(CharacterInventory inventory, ItemStack? stack, string playerUid, ICoreAPI api, int index, string id)
    {
        CharacterSlotConfig config = _configs[id];
        return new PlayerInventorySlot(SlotIdToTag[id], id, inventory, config, playerUid)
        {
            Itemstack = stack,
            Enabled = !config.Disabled
        };
    }
}

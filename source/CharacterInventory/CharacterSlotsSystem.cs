using PlayerInventoryLib.Utils;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace PlayerInventoryLib;


public delegate ItemSlot CreateCharacterSlotDelegate(CharacterInventory inventory, ItemStack? stack, string playerUid, ICoreAPI api, int index, string id);

public interface IEnableSlots
{
    string[] GetSlotsToEnable();
    Dictionary<string, CharacterSlotConfig> GetConfigOverrides();
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
    public ComplexTagCondition<TagSet>? Tags { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public class CharacterSlotConfig : SlotConfig
{
    public bool Disabled { get; set; } = false;
    public bool HiddenWhenDisabled { get; set; } = true;
    public string? Group { get; set; }
    public string? DisabledColor { get; set; }
}

public class CharacterSlotGroupGuiConfig
{
    public string Text { get; set; } = "";
    public bool HideWhenNoSlotsShown { get; set; } = true;
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

    public override double ExecuteOrder() => 0.01;


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
    public void RegisterPlayerInventorySlot(string slotId, CharacterSlotConfig config)
    {
        if (_createSlotDelegates.ContainsKey(slotId))
        {
            throw new DuplicatedSlotIdException($"Slot with id '{slotId}' already registered.");
        }

        _createSlotDelegates.Add(slotId, CreatePlayerInventorySlot);
        _configs.Add(slotId, config);
    }
    public void RegisterSlot(string slotId, CharacterSlotConfig config)
    {
        RegisterSlot(slotId, CreatePlayerInventorySlot, config);
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
        RegisterVanillaSlots();
    }

    public override void Start(ICoreAPI api)
    {
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
        if (api is ICoreServerAPI serverApi)
        {
            AddTagsToCollectibles(serverApi);
        }
        else
        {
            CollectSlotTags(api);
            Ready = true;
            OnReady?.Invoke();
        }
    }



    private readonly Dictionary<string, CreateCharacterSlotDelegate> _createSlotDelegates = [];
    private readonly Dictionary<string, CharacterSlotConfig> _configs = [];
    private readonly Dictionary<string, CharacterSlotGroupGuiConfig> _groupConfigs = [];
    private ICoreAPI? _api;

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
            string tagName = $"slot-{slotId.ToLowerInvariant()}";
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
            string tagName = $"slot-{slotId.ToLowerInvariant()}";
            TagSet tag = api.CollectibleTagRegistry.CreateTagSet(tagName);
            tags.Add(slotId, tag);
        }
        SlotIdToTag = tags.ToImmutableDictionary();
    }

    private void AddTagsToCollectibles(ICoreServerAPI api)
    {
        List<CollectibleObject> collectibles = api.World.Collectibles;
        foreach (CollectibleObject collectible in collectibles)
        {
            string? clothesCategory = collectible.Attributes?["clothescategory"]?.AsString() ?? collectible.Attributes?["attachableToEntity"]?["categoryCode"]?.AsString();
            if (clothesCategory == null)
            {
                continue;
            }

            foreach (string slotId in SlotIndexToId.Where(slotId => slotId.Equals(clothesCategory, StringComparison.InvariantCultureIgnoreCase)))
            {
                TagSet slotTag = SlotIdToTag[slotId];
                // WTF IS THIS MESS!? Where are my set opreations in TagSet!?
                IEnumerable<string> collectibleTags = api.CollectibleTagRegistry.SlowEnumerateTagNames(collectible.Tags);
                IEnumerable<string> slotTags = api.CollectibleTagRegistry.SlowEnumerateTagNames(slotTag);
                collectible.Tags = api.CollectibleTagRegistry.CreateTagSet(collectibleTags.Concat(slotTags));
            }
        }
    }

    private void RegisterVanillaSlots()
    {
        CharacterSlotGroupGuiConfig armorConfig = new()
        {
            Text = "Armor",
        };

        RegisterSlotGroup("armor", armorConfig);


        foreach (string id in DefaultVanillaSlotsOrder)
        {
            CharacterSlotConfig config = id switch
            {
                "ArmorHead" => new()
                {
                    Group = "armor"
                },
                "ArmorBody" => new()
                {
                    Group = "armor"
                },
                "ArmorLegs" => new()
                {
                    Group = "armor"
                },
                _ => new()
            };

            RegisterSlot(id, CreateClothesSlot, config);
        }
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
        return new PlayerInventorySlot(SlotIdToTag[id], id, inventory, _configs[id], playerUid)
        {
            Itemstack = stack
        };
    }
}

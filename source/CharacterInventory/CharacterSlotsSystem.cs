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
    Dictionary<string, SlotConfig> GetConfigOverrides();
}

public class DuplicatedSlotIdException : Exception
{
    public DuplicatedSlotIdException()
    {
    }

    public DuplicatedSlotIdException(string message)
        : base("[Player Inventory lib] " + message)
    {
    }

    public DuplicatedSlotIdException(string message, Exception innerException)
        : base("[Player Inventory lib] " + message, innerException)
    {
    }
}

public class UnknownSlotIdException : Exception
{
    public UnknownSlotIdException()
    {
    }

    public UnknownSlotIdException(string message)
        : base("[Player Inventory lib] " + message)
    {
    }

    public UnknownSlotIdException(string message, Exception innerException)
        : base("[Player Inventory lib] " + message, innerException)
    {
    }
}

public class SlotConfig
{
    public string? Icon { get; set; }
    public bool Disabled { get; set; } = false;
    public bool HiddenWhenDisabled { get; set; } = true;
    public string? Group { get; set; }
    public string? Color { get; set; }
    public string? DisabledColor { get; set; }
}

public class SlotGroupGuiConfig
{
    public string? Text { get; set; }
    public bool HideWhenNoSlots { get; set; } = true;
}

public class CharacterSlotsSystem : ModSystem
{
    public bool Ready { get; private set; }
    public ImmutableDictionary<string, int> SlotIdToIndex { get; private set; } = ImmutableDictionary.Create<string, int>();
    public ImmutableList<string> SlotIndexToId { get; private set; } = [];
    public ImmutableDictionary<string, TagSet> SlotIdToTag { get; private set; } = ImmutableDictionary.Create<string, TagSet>();
    public ImmutableDictionary<string, SlotConfig> SlotIdToConfig { get; private set; } = ImmutableDictionary.Create<string, SlotConfig>();
    public ImmutableDictionary<string, SlotGroupGuiConfig> GroupIdToGuiConfig { get; private set; } = ImmutableDictionary.Create<string, SlotGroupGuiConfig>();
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

    public event Action? OnReady;

    public override double ExecuteOrder() => 0.01;


    public void RegisterSlotGroup(string groupId, SlotGroupGuiConfig config)
    {

    }

    public void RegisterSlot(string slotId, CreateCharacterSlotDelegate createSlotDelegate, SlotConfig guiConfig)
    {
        if (_createSlotDelegates.ContainsKey(slotId))
        {
            throw new DuplicatedSlotIdException($"Slot with id '{slotId}' already registered.");
        }

        _createSlotDelegates.Add(slotId, createSlotDelegate);
        _guiConfigs.Add(slotId, guiConfig);
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
    private readonly Dictionary<string, SlotConfig> _guiConfigs = [];
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
        foreach (string id in DefaultVanillaSlotsOrder)
        {
            RegisterSlot(id, CreateClothesSlot, new());
        }
    }

    private ItemSlot CreateClothesSlot(CharacterInventory inventory, ItemStack? stack, string playerUid, ICoreAPI api, int index, string id)
    {
        return new CharacterInventorySlot(SlotIdToTag[id], id, inventory);
    }
}

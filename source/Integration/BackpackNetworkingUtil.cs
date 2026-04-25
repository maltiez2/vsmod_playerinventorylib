using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace PlayerInventoryLib;


public class BackpackInventoryNetworkUtil : PlayerInventoryNetworkUtil
{
    private readonly BackpackInventory _backpackInv;

    public BackpackInventoryNetworkUtil(BackpackInventory inv, ICoreAPI api) : base(inv, api)
    {
        _backpackInv = inv;
    }

    #region Server -> Client: Outgoing packets

    /// <summary>
    /// Override ToPacket to bypass inaccessible internal CreatePacketItemStacks.
    /// </summary>
    public override Packet_InventoryContents ToPacket(IPlayer player)
    {
        int slotsNumber = _backpackInv.CountForNetworkPacket;
        Packet_ItemStack[] itemstacks = new Packet_ItemStack[slotsNumber];

        for (int slotIndex = 0; slotIndex < slotsNumber; slotIndex++)
        {
            ItemSlot slot = _backpackInv[slotIndex];
            IItemStack? stack = slot.Itemstack;

            if (stack != null)
            {
                MemoryStream ms = new();
                BinaryWriter writer = new(ms);
                stack.Attributes.ToBytes(writer);

                itemstacks[slotIndex] = new Packet_ItemStack()
                {
                    ItemClass = (int)stack.Class,
                    ItemId = stack.Id,
                    StackSize = stack.StackSize,
                    Attributes = ms.ToArray()
                };

                itemstacks[slotIndex] = InjectStringSlotId(itemstacks[slotIndex], slot);
            }
            else
            {
                itemstacks[slotIndex] = new Packet_ItemStack()
                {
                    ItemClass = -1,
                    ItemId = 0,
                    StackSize = 0
                };
            }
        }

        Packet_InventoryContents p = new()
        {
            ClientId = player.ClientId,
            InventoryId = _backpackInv.InventoryID,
            InventoryClass = _backpackInv.ClassName
        };
        p.SetItemstacks(itemstacks, itemstacks.Length, itemstacks.Length);
        return p;
    }

    /// <summary>
    /// Override to inject string slot ID into the single-slot update packet.
    /// </summary>
    public override Packet_Server getSlotUpdatePacket(IPlayer player, int slotId)
    {
        ItemSlot slot = _backpackInv[slotId];
        if (slot == null) return null;

        ItemStack itemstack = slot.Itemstack;
        Packet_ItemStack pstack = null;
        if (itemstack != null)
        {
            pstack = StackConverter.ToPacket(itemstack);
            pstack = InjectStringSlotId(pstack, slot);
        }

        Packet_InventoryUpdate packet = new()
        {
            ClientId = player.ClientId,
            InventoryId = _backpackInv.InventoryID,
            ItemStack = pstack,
            SlotId = slotId
        };

        return new Packet_Server()
        {
            Id = Packet_ServerIdEnum.InventoryUpdate,
            InventoryUpdate = packet
        };
    }

    #endregion

    #region Client <- Server: Incoming packets

    /// <summary>
    /// Single slot update. Resolves slot by string ID when present.
    /// Also handles the hotbar-cancel logic from PlayerInventoryNetworkUtil.
    /// </summary>
    public override void UpdateFromPacket(IWorldAccessor world, Packet_InventoryUpdate packet)
    {
        if (PauseInventoryUpdates)
        {
            pendingPackets.Enqueue(packet);
            return;
        }

        string? stringSlotId = ExtractStringSlotId(packet.ItemStack);
        if (stringSlotId != null)
        {
            StripStringSlotId(packet.ItemStack);

            if (_backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? targetSlot))
            {
                HandleHotbarCancelIfNeeded(world, targetSlot, packet.ItemStack);

                ItemStack? newStack = SafeItemStackFromPacket(world, packet.ItemStack);
                if (UpdateSlotStackInternal(targetSlot, newStack))
                {
                    _backpackInv.DidModifyItemSlot(targetSlot);
                }
            }
            else
            {
                // Slot doesn't exist yet, store for when backpack is opened
                ItemStack? newStack = SafeItemStackFromPacket(world, packet.ItemStack);
                newStack?.ResolveBlockOrItem(world);
                _backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
            }
            return;
        }

        // No string ID — vanilla backpack slot, use integer index
        if (packet.SlotId >= _backpackInv.Count)
        {
            throw new System.Exception(
                "Client received server InventoryUpdate for " + _backpackInv.InventoryID +
                ", slot " + packet.SlotId + " but max is " + (_backpackInv.Count - 1) +
                ". For " + _backpackInv.ClassName + " at " + _backpackInv.Pos);
        }

        ItemSlot fallbackSlot = _backpackInv[packet.SlotId];
        if (fallbackSlot == null) return;

        HandleHotbarCancelIfNeeded(world, fallbackSlot, packet.ItemStack);

        ItemStack? stack = SafeItemStackFromPacket(world, packet.ItemStack);
        if (UpdateSlotStackInternal(fallbackSlot, stack))
        {
            _backpackInv.DidModifyItemSlot(fallbackSlot);
        }
    }

    /// <summary>
    /// Full inventory contents update. Resolves each slot by string ID when present.
    /// </summary>
    public override void UpdateFromPacket(IWorldAccessor resolver, Packet_InventoryContents packet)
    {
        for (int i = 0; i < packet.ItemstacksCount; i++)
        {
            Packet_ItemStack pstack = packet.Itemstacks[i];

            string? stringSlotId = ExtractStringSlotId(pstack);
            if (stringSlotId != null)
            {
                StripStringSlotId(pstack);
                ItemStack? newStack = SafeItemStackFromPacket(resolver, pstack);

                if (_backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
                {
                    if (UpdateSlotStackInternal(slot, newStack))
                    {
                        _backpackInv.DidModifyItemSlot(slot);
                    }
                }
                else
                {
                    newStack?.ResolveBlockOrItem(resolver);
                    _backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
                }
                continue;
            }

            // Fallback to integer index
            ItemSlot fallbackSlot = _backpackInv[i];
            ItemStack? stack = SafeItemStackFromPacket(resolver, pstack);
            if (UpdateSlotStackInternal(fallbackSlot, stack))
            {
                _backpackInv.DidModifyItemSlot(fallbackSlot);
            }
        }
    }

    /// <summary>
    /// Double slot update (e.g. after flip/move). Resolves each side by string ID when present.
    /// </summary>
    public override void UpdateFromPacket(IWorldAccessor resolver, Packet_InventoryDoubleUpdate packet)
    {
        if (packet.InventoryId1 == _backpackInv.InventoryID)
        {
            ProcessDoubleUpdateSlot(resolver, packet.ItemStack1, packet.SlotId1);
        }

        if (packet.InventoryId2 == _backpackInv.InventoryID)
        {
            ProcessDoubleUpdateSlot(resolver, packet.ItemStack2, packet.SlotId2);
        }
    }

    private void ProcessDoubleUpdateSlot(IWorldAccessor resolver, Packet_ItemStack? pstack, int intSlotId)
    {
        string? stringSlotId = ExtractStringSlotId(pstack);
        if (stringSlotId != null)
        {
            if (pstack != null) StripStringSlotId(pstack);
            ItemStack? newStack = SafeItemStackFromPacket(resolver, pstack);

            if (_backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
            {
                if (UpdateSlotStackInternal(slot, newStack))
                {
                    _backpackInv.DidModifyItemSlot(slot);
                }
            }
            else
            {
                newStack?.ResolveBlockOrItem(resolver);
                _backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
            }
            return;
        }

        ItemSlot fallbackSlot = _backpackInv[intSlotId];
        ItemStack? stack = SafeItemStackFromPacket(resolver, pstack);
        if (UpdateSlotStackInternal(fallbackSlot, stack))
        {
            _backpackInv.DidModifyItemSlot(fallbackSlot);
        }
    }

    #endregion

    #region PauseInventoryUpdates (reimplemented — base queue is private)

    private readonly Queue<Packet_InventoryUpdate> pendingPackets = new();
    private bool pauseUpdates;

    public new bool PauseInventoryUpdates
    {
        get => pauseUpdates;
        set
        {
            bool doResume = !value && pauseUpdates;
            pauseUpdates = value;
            if (doResume)
            {
                while (pendingPackets.Count > 0)
                {
                    Packet_InventoryUpdate pkt = pendingPackets.Dequeue();
                    UpdateFromPacket(Api.World, pkt);
                }
            }
        }
    }

    #endregion

    #region Hotbar cancel logic (from PlayerInventoryNetworkUtil)

    private void HandleHotbarCancelIfNeeded(IWorldAccessor world, ItemSlot slot, Packet_ItemStack? packetStack)
    {
        if (!IsOwnHotbarSlotClient(slot)) return;

        ItemStack? prevStack = slot.Itemstack;
        if (prevStack == null) return;

        ItemStack? newStackPreview = SafeItemStackFromPacket(world, packetStack);
        if (newStackPreview == null || prevStack.Collectible != newStackPreview.Collectible)
        {
            if (world is IClientWorldAccessor clientWorld)
            {
                IClientPlayer plr = clientWorld.Player;
                prevStack.Collectible.OnHeldInteractCancel(
                    0, slot, plr.Entity, plr.CurrentBlockSelection,
                    plr.CurrentEntitySelection, EnumItemUseCancelReason.Destroyed);
            }
        }
    }

    private bool IsOwnHotbarSlotClient(ItemSlot slot)
    {
        if (Api is ICoreClientAPI capi)
        {
            return capi.World?.Player?.InventoryManager?.ActiveHotbarSlot == slot;
        }
        return false;
    }

    #endregion

    #region Managing string ids

    private static Packet_ItemStack InjectStringSlotId(Packet_ItemStack? stackPacket, ItemSlot slot)
    {
        if (stackPacket == null)
        {
            return new Packet_ItemStack()
            {
                ItemClass = -1,
                ItemId = 0,
                StackSize = 0
            };
        }

        if (slot is not IBackpackSlot backpackSlot) return stackPacket;

        string fullSlotId = GetFullSlotId(backpackSlot);

        if (fullSlotId == null) return stackPacket;

        stackPacket.Attributes = SlotIdByteEncoder.Append(stackPacket.Attributes, fullSlotId);
        return stackPacket;
    }

    private static string? ExtractStringSlotId(Packet_ItemStack? stackPacket)
    {
        if (stackPacket?.Attributes == null) return null;
        return SlotIdByteEncoder.Extract(stackPacket.Attributes);
    }

    private static void StripStringSlotId(Packet_ItemStack? stackPacket)
    {
        if (stackPacket?.Attributes == null) return;
        stackPacket.Attributes = SlotIdByteEncoder.Strip(stackPacket.Attributes);
    }

    /// <summary>
    /// For harmony patch on getDoubleUpdatePacket
    /// </summary>
    public static void InjectStringSlotIdPublic(Packet_ItemStack pstack, ItemSlot slot)
    {
        InjectStringSlotId(pstack, slot);
    }

    private static string GetFullSlotId(IBackpackSlot backpackSlot)
    {
        string backpackId = backpackSlot.Backpack.BackpackId;
        string slotId = backpackSlot.SlotId;
        string backpackSlotId = backpackSlot.BackpackSlotId;
        return BackpackInventory.GetSlotFullId(backpackId, backpackSlotId, slotId);
    }

    #endregion

    #region Reimplementation of private methods for handling stacks

    private static ItemStack? SafeItemStackFromPacket(IWorldAccessor resolver, Packet_ItemStack? stackPacket)
    {
        if (stackPacket == null || stackPacket.ItemClass == -1 || stackPacket.ItemId == 0) return null;
        return StackConverter.FromPacket(stackPacket, resolver);
    }

    private bool UpdateSlotStackInternal(ItemSlot slot, ItemStack? newStack)
    {
        if (slot.Itemstack != null && newStack != null && slot.Itemstack.Collectible == newStack.Collectible)
        {
            newStack.TempAttributes = slot.Itemstack?.TempAttributes;
        }

        bool didUpdate = ((newStack == null) != (slot.Itemstack == null))
            || (newStack != null && !newStack.Equals(Api.World, slot.Itemstack, GlobalConstants.IgnoredStackAttributes));

        slot.Itemstack = newStack;
        return didUpdate;
    }

    #endregion

    #region Double update packet: to work around calls to static method getDoubleUpdatePacket

    protected override void NotifyPlayersItemstackMoved(IPlayer player, string[] invIds, int[] slotIds)
    {
        Packet_Server serverPacket = GetDoubleUpdatePacketWithStringIds(player, invIds, slotIds);
        (Api as ICoreServerAPI)?.Network.BroadcastArbitraryPacket(serverPacket, (IServerPlayer)player);
    }

    protected override void RevertPlayerItemstackMove(IPlayer owningPlayer, string[] invIds, int[] slotIds)
    {
        ItemSlot[] slots = _backpackInv.GetSlotsIfExists(owningPlayer, invIds, slotIds);

        if (slots[0] != null && slots[1] != null) // GetSlotsIfExists always returns array of size 2
        {
            Packet_Server serverPacket = GetDoubleUpdatePacketWithStringIds(owningPlayer, invIds, slotIds);
            (Api as ICoreServerAPI)?.Network.SendArbitraryPacket(serverPacket, (IServerPlayer)owningPlayer);
        }
    }

    private Packet_Server GetDoubleUpdatePacketWithStringIds(IPlayer player, string[] invIds, int[] slotIds)
    {
        IInventory inventory_1 = player.InventoryManager.GetInventory(invIds[0]);
        IInventory inventory_2 = player.InventoryManager.GetInventory(invIds[1]);

        ItemStack? itemstack_1 = inventory_1[slotIds[0]]?.Itemstack;
        ItemStack? itemstack_2 = inventory_2[slotIds[1]]?.Itemstack;

        Packet_ItemStack? stackPacket_1 = itemstack_1 != null ? StackConverter.ToPacket(itemstack_1) : null;
        Packet_ItemStack? stackPacket_2 = itemstack_2 != null ? StackConverter.ToPacket(itemstack_2) : null;

        if (inventory_1 is BackpackInventory)
        {
            stackPacket_1 = InjectStringSlotId(stackPacket_1, inventory_1[slotIds[0]]);
        }
        if (inventory_2 is BackpackInventory)
        {
            stackPacket_2 = InjectStringSlotId(stackPacket_2, inventory_2[slotIds[1]]);
        }

        Packet_InventoryDoubleUpdate packet = new()
        {
            ClientId = player.ClientId,
            InventoryId1 = invIds[0],
            InventoryId2 = invIds[1],
            SlotId1 = slotIds[0],
            SlotId2 = slotIds[1],
            ItemStack1 = stackPacket_1,
            ItemStack2 = stackPacket_2
        };

        return new Packet_Server()
        {
            Id = Packet_ServerIdEnum.InventoryDoubleUpdate,
            InventoryDoubleUpdate = packet
        };
    }

    #endregion
}
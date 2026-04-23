using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace PlayerInventoryLib.Integration;

[HarmonyPatch]
public static class InventoryNetworkSlotIdPatches
{
    private const string SlotIdAttributeKey = "plrinvlib:netSlotId";

    #region Patch 1 - getSlotUpdatePacket

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getSlotUpdatePacket))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_getSlotUpdatePacket(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> codes = new(instructions);
        MethodInfo toPacketMethod = AccessTools.Method(typeof(StackConverter), nameof(StackConverter.ToPacket));

        for (int i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(toPacketMethod)) continue;

            int storeIndex = -1;
            for (int j = i + 1; j < codes.Count; j++)
            {
                if (codes[j].IsStloc())
                {
                    storeIndex = j;
                    break;
                }
            }
            if (storeIndex == -1) break;

            CodeInstruction storeInstr = codes[storeIndex];
            CodeInstruction loadInstr = StlocToLdloc(storeInstr);

            List<CodeInstruction> injected = new()
            {
                loadInstr,
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(InventoryNetworkUtil), "inv")),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(InventoryNetworkSlotIdPatches), nameof(InjectStringSlotId))),
                storeInstr.Clone()
            };

            codes.InsertRange(storeIndex + 1, injected);
            break;
        }

        return codes;
    }

    #endregion

    #region Patch 2 - CreatePacketItemStacks

    [HarmonyPatch(typeof(InventoryNetworkUtil), "CreatePacketItemStacks")]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_CreatePacketItemStacks(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> codes = new(instructions);

        for (int i = codes.Count - 1; i >= 0; i--)
        {
            if (codes[i].opcode != OpCodes.Ret) continue;

            List<CodeInstruction> injected = new()
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(InventoryNetworkUtil), "inv")),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(InventoryNetworkSlotIdPatches), nameof(InjectStringSlotIdsIntoArray)))
            };

            codes.InsertRange(i, injected);
            break;
        }

        return codes;
    }

    public static Packet_ItemStack[] InjectStringSlotIdsIntoArray(Packet_ItemStack[] stacks, InventoryBase inv)
    {
        if (inv is not BackpackInventory) return stacks;

        for (int i = 0; i < stacks.Length; i++)
        {
            Packet_ItemStack pstack = stacks[i];
            if (pstack == null || pstack.ItemClass == -1) continue;
            stacks[i] = InjectStringSlotId(pstack, inv, i);
        }

        return stacks;
    }

    #endregion

    #region Patch 3 - UpdateFromPacket(Packet_InventoryUpdate)

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) })]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_UpdateFromPacket_Single(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // Replace the entire method body with a call to our helper.
        // Original method: this is an instance method with args (IWorldAccessor resolver, Packet_InventoryUpdate packet)
        // We replace with: HandleUpdateFromPacket(this, resolver, packet); return;

        List<CodeInstruction> codes = new(instructions);
        List<Label> firstLabels = codes.Count > 0 ? new(codes[0].labels) : new();

        codes.Clear();
        codes.AddRange(new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0) { labels = firstLabels },  // this
            new CodeInstruction(OpCodes.Ldarg_1),                            // resolver
            new CodeInstruction(OpCodes.Ldarg_2),                            // packet
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(InventoryNetworkSlotIdPatches), nameof(HandleUpdateFromPacket))),
            new CodeInstruction(OpCodes.Ret)
        });

        return codes;
    }

    public static void HandleUpdateFromPacket(InventoryNetworkUtil netUtil,
        IWorldAccessor resolver, Packet_InventoryUpdate packet)
    {
        if (netUtil.PauseInventoryUpdates)
        {
            _pktsFieldGetter(netUtil).Enqueue(packet);
            return;
        }

        InventoryBase inv = _invFieldGetter(netUtil);

        if (inv is BackpackInventory backpackInv)
        {
            string? stringSlotId = ExtractStringSlotId(packet.ItemStack);
            if (stringSlotId != null)
            {
                StripStringSlotId(packet.ItemStack);

                ItemStack? newStack = ItemStackFromPacket(netUtil, resolver, packet.ItemStack);

                if (backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
                {
                    if (UpdateSlotStack(netUtil, slot, newStack))
                    {
                        inv.DidModifyItemSlot(slot);
                    }
                    return;
                }

                newStack?.ResolveBlockOrItem(resolver);
                backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
                return;
            }
        }

        if (packet.SlotId >= inv.Count)
        {
            throw new System.Exception("Client received server InventoryUpdate for " + inv.InventoryID +
                ", slot " + packet.SlotId + " but max is " + (inv.Count - 1) +
                ". For " + inv.ClassName + " at " + inv.Pos);
        }

        ItemSlot fallbackSlot = inv[packet.SlotId];
        if (fallbackSlot == null) return;

        ItemStack? stack = ItemStackFromPacket(netUtil, resolver, packet.ItemStack);
        if (UpdateSlotStack(netUtil, fallbackSlot, stack))
        {
            inv.DidModifyItemSlot(fallbackSlot);
        }
    }

    #endregion

    #region Patch 4 - UpdateFromPacket(Packet_InventoryContents)

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryContents) })]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_UpdateFromPacket_Contents(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> codes = new(instructions);
        List<Label> firstLabels = codes.Count > 0 ? new(codes[0].labels) : new();

        codes.Clear();
        codes.AddRange(new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0) { labels = firstLabels },
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(InventoryNetworkSlotIdPatches), nameof(HandleUpdateFromContentsPacket))),
            new CodeInstruction(OpCodes.Ret)
        });

        return codes;
    }

    public static void HandleUpdateFromContentsPacket(InventoryNetworkUtil netUtil,
        IWorldAccessor resolver, Packet_InventoryContents packet)
    {
        InventoryBase inv = _invFieldGetter(netUtil);

        for (int i = 0; i < packet.ItemstacksCount; i++)
        {
            Packet_ItemStack pstack = packet.Itemstacks[i];

            if (inv is BackpackInventory backpackInv)
            {
                string? stringSlotId = ExtractStringSlotId(pstack);
                if (stringSlotId != null)
                {
                    StripStringSlotId(pstack);

                    ItemStack? newStack = ItemStackFromPacket(netUtil, resolver, pstack);

                    if (backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
                    {
                        if (UpdateSlotStack(netUtil, slot, newStack))
                        {
                            inv.DidModifyItemSlot(slot);
                        }
                        continue;
                    }

                    newStack?.ResolveBlockOrItem(resolver);
                    backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
                    continue;
                }
            }

            ItemSlot fallbackSlot = inv[i];
            ItemStack? stack = ItemStackFromPacket(netUtil, resolver, pstack);
            if (UpdateSlotStack(netUtil, fallbackSlot, stack))
            {
                inv.DidModifyItemSlot(fallbackSlot);
            }
        }
    }

    #endregion

    #region Patch 5 - UpdateFromPacket(Packet_InventoryDoubleUpdate)

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate) })]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_UpdateFromPacket_Double(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> codes = new(instructions);
        List<Label> firstLabels = codes.Count > 0 ? new(codes[0].labels) : new();

        codes.Clear();
        codes.AddRange(new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0) { labels = firstLabels },
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(InventoryNetworkSlotIdPatches), nameof(HandleUpdateFromDoublePacket))),
            new CodeInstruction(OpCodes.Ret)
        });

        return codes;
    }

    public static void HandleUpdateFromDoublePacket(InventoryNetworkUtil netUtil,
        IWorldAccessor resolver, Packet_InventoryDoubleUpdate packet)
    {
        InventoryBase inv = _invFieldGetter(netUtil);

        if (packet.InventoryId1 == inv.InventoryID)
        {
            ProcessDoubleUpdateSlot(netUtil, inv, resolver, packet.ItemStack1, packet.SlotId1);
        }

        if (packet.InventoryId2 == inv.InventoryID)
        {
            ProcessDoubleUpdateSlot(netUtil, inv, resolver, packet.ItemStack2, packet.SlotId2);
        }
    }

    private static void ProcessDoubleUpdateSlot(InventoryNetworkUtil netUtil, InventoryBase inv,
        IWorldAccessor resolver, Packet_ItemStack? pstack, int intSlotId)
    {
        if (inv is BackpackInventory backpackInv)
        {
            string? stringSlotId = ExtractStringSlotId(pstack);
            if (stringSlotId != null)
            {
                if (pstack != null) StripStringSlotId(pstack);

                ItemStack? newStack = ItemStackFromPacket(netUtil, resolver, pstack);

                if (backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
                {
                    if (UpdateSlotStack(netUtil, slot, newStack))
                    {
                        inv.DidModifyItemSlot(slot);
                    }
                    return;
                }

                newStack?.ResolveBlockOrItem(resolver);
                backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
                return;
            }
        }

        ItemSlot fallbackSlot = inv[intSlotId];
        ItemStack? stack = ItemStackFromPacket(netUtil, resolver, pstack);
        if (UpdateSlotStack(netUtil, fallbackSlot, stack))
        {
            inv.DidModifyItemSlot(fallbackSlot);
        }
    }

    #endregion

    #region Patch 6 - getDoubleUpdatePacket

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getDoubleUpdatePacket))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_getDoubleUpdatePacket(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> codes = new(instructions);

        for (int i = codes.Count - 1; i >= 0; i--)
        {
            if (codes[i].opcode != OpCodes.Ret) continue;

            List<CodeInstruction> injected = new()
            {
                new CodeInstruction(OpCodes.Ldarg_0), // player
                new CodeInstruction(OpCodes.Ldarg_1), // invIds
                new CodeInstruction(OpCodes.Ldarg_2), // slotIds
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(InventoryNetworkSlotIdPatches), nameof(InjectDoubleUpdateSlotIds)))
            };

            codes.InsertRange(i, injected);
            break;
        }

        return codes;
    }

    public static Packet_Server InjectDoubleUpdateSlotIds(Packet_Server result, IPlayer player,
        string[] invIds, int[] slotIds)
    {
        Packet_InventoryDoubleUpdate doubleUpdate = result.InventoryDoubleUpdate;

        IInventory inv1 = player.InventoryManager.GetInventory(invIds[0]);
        if (inv1 is BackpackInventory && doubleUpdate.ItemStack1 != null)
        {
            doubleUpdate.ItemStack1 = InjectStringSlotId(doubleUpdate.ItemStack1, (InventoryBase)inv1, slotIds[0]);
        }

        IInventory inv2 = player.InventoryManager.GetInventory(invIds[1]);
        if (inv2 is BackpackInventory && doubleUpdate.ItemStack2 != null)
        {
            doubleUpdate.ItemStack2 = InjectStringSlotId(doubleUpdate.ItemStack2, (InventoryBase)inv2, slotIds[1]);
        }

        return result;
    }

    #endregion

    #region Patch 7 - PlayerInventoryNetworkUtil.UpdateFromPacket

    [HarmonyPatch(typeof(PlayerInventoryNetworkUtil), nameof(PlayerInventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) })]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_PlayerInventoryNetworkUtil_UpdateFromPacket(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // Replace the entire method body with our helper that handles both
        // the hotbar-cancel logic and the base.UpdateFromPacket call.
        //
        // Original:
        //   ItemStack prevStack = null;
        //   ItemSlot slot = inv[packet.SlotId];
        //   if (IsOwnHotbarSlotClient(slot)) { ... preview check ... }
        //   base.UpdateFromPacket(world, packet);
        //
        // Replacement:
        //   HandlePlayerInventoryUpdateFromPacket(this, world, packet);
        //   return;

        List<CodeInstruction> codes = new(instructions);
        List<Label> firstLabels = codes.Count > 0 ? new(codes[0].labels) : new();

        codes.Clear();
        codes.AddRange(new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0) { labels = firstLabels },  // this
            new CodeInstruction(OpCodes.Ldarg_1),                            // world
            new CodeInstruction(OpCodes.Ldarg_2),                            // packet
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(InventoryNetworkSlotIdPatches),
                    nameof(HandlePlayerInventoryUpdateFromPacket))),
            new CodeInstruction(OpCodes.Ret)
        });

        return codes;
    }

    public static void HandlePlayerInventoryUpdateFromPacket(
        PlayerInventoryNetworkUtil netUtil, IWorldAccessor world, Packet_InventoryUpdate packet)
    {
        InventoryBase inv = _invFieldGetter(netUtil);
        ItemStack? prevStack = null;

        // Resolve the slot (string-based or integer-based)
        ItemSlot slot = ResolveSlotForPlayerUpdate(inv, packet);

        // Replicate the hotbar-cancel logic from the original method
        if (IsOwnHotbarSlotClient(netUtil, slot))
        {
            prevStack = slot.Itemstack;
            if (prevStack != null)
            {
                ItemStack? newStackPreview = PeekItemStackFromPacket(world, packet.ItemStack);
                if (newStackPreview == null || prevStack.Collectible != newStackPreview.Collectible)
                {
                    var plr = (world as ICoreClientAPI)!.World.Player;
                    prevStack.Collectible.OnHeldInteractCancel(
                        0, slot, plr.Entity, plr.CurrentBlockSelection,
                        plr.CurrentEntitySelection, EnumItemUseCancelReason.Destroyed);
                }
            }
        }

        // Call the base UpdateFromPacket logic (which is now also patched by Patch 3)
        // We call our handler directly since the base method is also replaced.
        HandleUpdateFromPacket(netUtil, world, packet);
    }

    private static bool IsOwnHotbarSlotClient(InventoryNetworkUtil netUtil, ItemSlot slot)
    {
        ICoreAPI api = netUtil.Api;
        if (api is ICoreClientAPI capi)
        {
            return capi.World.Player.InventoryManager.ActiveHotbarSlot == slot;
        }
        return false;
    }

    public static ItemSlot ResolveSlotForPlayerUpdate(InventoryBase inv, Packet_InventoryUpdate packet)
    {
        if (inv is BackpackInventory backpackInv)
        {
            string? stringSlotId = ExtractStringSlotId(packet.ItemStack);
            if (stringSlotId != null)
            {
                if (backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
                {
                    return slot;
                }
                return backpackInv[inv.Count]; // PlaceholderSlot
            }
        }

        if (packet.SlotId < inv.Count)
        {
            return inv[packet.SlotId];
        }
        return inv[0];
    }

    public static ItemStack? PeekItemStackFromPacket(IWorldAccessor resolver, Packet_ItemStack? pItemStack)
    {
        if (pItemStack == null || pItemStack.ItemClass == -1 || pItemStack.ItemId == 0) return null;

        string? marker = ExtractStringSlotId(pItemStack);
        if (marker == null)
        {
            return StackConverter.FromPacket(pItemStack, resolver);
        }

        TreeAttribute attributes = new();
        if (pItemStack.Attributes != null && pItemStack.Attributes.Length > 0)
        {
            using MemoryStream ms = new(pItemStack.Attributes);
            using BinaryReader reader = new(ms);
            attributes.FromBytes(reader);
        }

        attributes.RemoveAttribute(SlotIdAttributeKey);

        using MemoryStream cleanMs = new();
        using BinaryWriter cleanWriter = new(cleanMs);
        attributes.ToBytes(cleanWriter);

        Packet_ItemStack cleanPacket = new()
        {
            ItemClass = pItemStack.ItemClass,
            ItemId = pItemStack.ItemId,
            StackSize = pItemStack.StackSize,
            Attributes = cleanMs.ToArray()
        };

        return StackConverter.FromPacket(cleanPacket, resolver);
    }

    #endregion

    #region Shared helper methods

    public static Packet_ItemStack InjectStringSlotId(Packet_ItemStack pstack, InventoryBase inv, int slotId)
    {
        if (pstack == null || inv is not BackpackInventory) return pstack;

        ItemSlot slot = inv[slotId];
        if (slot is not IBackpackSlot playerSlot) return pstack;

        string? stringSlotId = playerSlot.BackpackSlotId;
        if (stringSlotId == null) return pstack;

        // Build the full slot ID the same way BackpackInventory does
        string fullSlotId = $"{playerSlot.Backpack.BackpackId}@{((IPlayerInventorySlot)playerSlot).SlotId}@{playerSlot.BackpackSlotId}";

        TreeAttribute attributes = new();
        if (pstack.Attributes != null && pstack.Attributes.Length > 0)
        {
            using MemoryStream ms = new(pstack.Attributes);
            using BinaryReader reader = new(ms);
            attributes.FromBytes(reader);
        }

        attributes.SetString(SlotIdAttributeKey, fullSlotId);

        using MemoryStream outMs = new();
        using BinaryWriter writer = new(outMs);
        attributes.ToBytes(writer);
        pstack.Attributes = outMs.ToArray();

        return pstack;
    }

    public static string? ExtractStringSlotId(Packet_ItemStack? pstack)
    {
        if (pstack?.Attributes == null || pstack.Attributes.Length == 0) return null;

        try
        {
            TreeAttribute attributes = new();
            using MemoryStream ms = new(pstack.Attributes);
            using BinaryReader reader = new(ms);
            attributes.FromBytes(reader);

            return attributes.GetString(SlotIdAttributeKey);
        }
        catch
        {
            return null;
        }
    }

    public static Packet_ItemStack StripStringSlotId(Packet_ItemStack pstack)
    {
        if (pstack?.Attributes == null || pstack.Attributes.Length == 0) return pstack;

        try
        {
            TreeAttribute attributes = new();
            using MemoryStream ms = new(pstack.Attributes);
            using BinaryReader reader = new(ms);
            attributes.FromBytes(reader);

            if (attributes.HasAttribute(SlotIdAttributeKey))
            {
                attributes.RemoveAttribute(SlotIdAttributeKey);

                using MemoryStream outMs = new();
                using BinaryWriter writer = new(outMs);
                attributes.ToBytes(writer);
                pstack.Attributes = outMs.ToArray();
            }
        }
        catch
        {
            // leave it alone
        }

        return pstack;
    }

    public static ItemStack? ItemStackFromPacket(InventoryNetworkUtil netUtil,
        IWorldAccessor resolver, Packet_ItemStack? pstack)
    {
        return _itemStackFromPacketDelegate.Value(netUtil, resolver, pstack);
    }

    public static bool UpdateSlotStack(InventoryNetworkUtil netUtil, ItemSlot slot, ItemStack? newStack)
    {
        return _updateSlotStackDelegate.Value(netUtil, slot, newStack);
    }

    private static readonly Lazy<System.Func<InventoryNetworkUtil, IWorldAccessor, Packet_ItemStack?, ItemStack?>> _itemStackFromPacketDelegate =
        new(() =>
        {
            MethodInfo method = AccessTools.Method(typeof(InventoryNetworkUtil), "ItemStackFromPacket",
                new[] { typeof(IWorldAccessor), typeof(Packet_ItemStack) });
            return (System.Func<InventoryNetworkUtil, IWorldAccessor, Packet_ItemStack?, ItemStack?>)
                Delegate.CreateDelegate(
                    typeof(System.Func<InventoryNetworkUtil, IWorldAccessor, Packet_ItemStack?, ItemStack?>),
                    method);
        });

    private static readonly Lazy<System.Func<InventoryNetworkUtil, ItemSlot, ItemStack?, bool>> _updateSlotStackDelegate =
        new(() =>
        {
            MethodInfo method = AccessTools.Method(typeof(InventoryNetworkUtil), "UpdateSlotStack",
                new[] { typeof(ItemSlot), typeof(ItemStack) });
            return (System.Func<InventoryNetworkUtil, ItemSlot, ItemStack?, bool>)
                Delegate.CreateDelegate(
                    typeof(System.Func<InventoryNetworkUtil, ItemSlot, ItemStack?, bool>),
                    method);
        });

    private static readonly AccessTools.FieldRef<InventoryNetworkUtil, InventoryBase> _invFieldGetter =
        AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>("inv");

    private static readonly AccessTools.FieldRef<InventoryNetworkUtil, Queue<Packet_InventoryUpdate>> _pktsFieldGetter =
        AccessTools.FieldRefAccess<InventoryNetworkUtil, Queue<Packet_InventoryUpdate>>("pkts");

    #endregion

    #region IL helper utilities

    private static CodeInstruction StlocToLdloc(CodeInstruction stloc)
    {
        if (stloc.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
        if (stloc.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
        if (stloc.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
        if (stloc.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
        if (stloc.opcode == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, stloc.operand);
        if (stloc.opcode == OpCodes.Stloc) return new CodeInstruction(OpCodes.Ldloc, stloc.operand);
        throw new InvalidOperationException($"Not a stloc instruction: {stloc.opcode}");
    }

    #endregion
}
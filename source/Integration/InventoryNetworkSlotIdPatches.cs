using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

// Generated with llm. This vanilla inventory networks code is so convoluted and hard to change, that I just decided "fuck it, let claude deal with".
// May be I will find better solution later, this one seems to work just fine though.

namespace PlayerInventoryLib.Integration;

/// <summary>
/// Harmony patches that embed string slot IDs from BackpackInventory into the
/// Packet_ItemStack attributes during serialization, and retrieve them during
/// deserialization to update the correct slot.
/// </summary>
[HarmonyPatch]
public static class InventoryNetworkSlotIdPatches
{
    private const string SlotIdAttributeKey = "plrinvlib:netSlotId";

    // =========================================================================
    // PATCH 1: Server -> Client single slot update packet creation
    // In InventoryNetworkUtil.getSlotUpdatePacket, after StackConverter.ToPacket
    // is called, inject the string slot ID into the packet's Attributes bytes.
    // =========================================================================

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getSlotUpdatePacket))]
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpile_getSlotUpdatePacket(
        IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> codes = new(instructions);

        MethodInfo toPacketMethod = AccessTools.Method(typeof(StackConverter), nameof(StackConverter.ToPacket));

        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(toPacketMethod))
            {
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
        }

        return codes;
    }

    // =========================================================================
    // PATCH 2: Server -> Client bulk inventory contents packet creation
    // In InventoryNetworkUtil.CreatePacketItemStacks, inject string slot IDs
    // into each Packet_ItemStack's Attributes for BackpackInventory.
    // =========================================================================

    [HarmonyPatch(typeof(InventoryNetworkUtil), "CreatePacketItemStacks")]
    [HarmonyPostfix]
    public static void Postfix_CreatePacketItemStacks(
        InventoryNetworkUtil __instance,
        ref Packet_ItemStack[] __result)
    {
        var inv = AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>(__instance, "inv");
        if (inv is not BackpackInventory) return;

        for (int i = 0; i < __result.Length; i++)
        {
            Packet_ItemStack pstack = __result[i];
            if (pstack == null || pstack.ItemClass == -1) continue;

            __result[i] = InjectStringSlotId(pstack, inv, i);
        }
    }

    // =========================================================================
    // PATCH 3: Client-side UpdateFromPacket(Packet_InventoryUpdate)
    // Instead of using packet.SlotId as integer index, extract the string slot
    // ID from the attributes and resolve the correct slot.
    // We use a prefix to fully replace the method for BackpackInventory.
    // =========================================================================

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) })]
    [HarmonyPrefix]
    public static bool Prefix_UpdateFromPacket_Single(
        InventoryNetworkUtil __instance,
        IWorldAccessor resolver,
        Packet_InventoryUpdate packet)
    {
        var inv = AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>(__instance, "inv");
        if (inv is not BackpackInventory backpackInv) return true; // Run original

        // Check PauseInventoryUpdates
        if (__instance.PauseInventoryUpdates)
        {
            var pktsField = AccessTools.Field(typeof(InventoryNetworkUtil), "pkts");
            var pkts = (Queue<Packet_InventoryUpdate>)pktsField.GetValue(__instance);
            pkts.Enqueue(packet);
            return false;
        }

        MethodInfo itemStackFromPacketMethod = AccessTools.Method(typeof(InventoryNetworkUtil), "ItemStackFromPacket",
            new[] { typeof(IWorldAccessor), typeof(Packet_ItemStack) });
        MethodInfo updateSlotStackMethod = AccessTools.Method(typeof(InventoryNetworkUtil), "UpdateSlotStack",
            new[] { typeof(ItemSlot), typeof(ItemStack) });

        string? stringSlotId = ExtractStringSlotId(packet.ItemStack);
        if (stringSlotId != null)
        {
            StripStringSlotId(packet.ItemStack);
        }

        ItemStack? newStack = (ItemStack?)itemStackFromPacketMethod.Invoke(__instance,
            new object[] { resolver, packet.ItemStack });

        if (stringSlotId != null && backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
        {
            bool didUpdate = (bool)updateSlotStackMethod.Invoke(__instance, new object[] { slot, newStack });
            if (didUpdate)
            {
                inv.DidModifyItemSlot(slot);
            }
        }
        else if (stringSlotId != null)
        {
            // Slot not found — store for later when the slot is created
            newStack?.ResolveBlockOrItem(resolver);
            backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
        }
        else
        {
            // No string slot ID — fall back to integer index
            if (packet.SlotId >= inv.Count)
            {
                throw new Exception("Client received server InventoryUpdate for " + inv.InventoryID + ", slot " + packet.SlotId + " but max is " + (inv.Count - 1) + ". For " + inv.ClassName + " at " + inv.Pos);
            }
            ItemSlot fallbackSlot = inv[packet.SlotId];
            if (fallbackSlot == null) return false;

            bool didUpdate = (bool)updateSlotStackMethod.Invoke(__instance, new object[] { fallbackSlot, newStack });
            if (didUpdate)
            {
                inv.DidModifyItemSlot(fallbackSlot);
            }
        }

        return false; // Skip original
    }

    // =========================================================================
    // PATCH 4: Client-side UpdateFromPacket(Packet_InventoryContents)
    // For BackpackInventory, resolve slots by string ID instead of integer index.
    // =========================================================================

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryContents) })]
    [HarmonyPrefix]
    public static bool Prefix_UpdateFromPacket_Contents(
        InventoryNetworkUtil __instance,
        IWorldAccessor resolver,
        Packet_InventoryContents packet)
    {
        var inv = AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>(__instance, "inv");
        if (inv is not BackpackInventory backpackInv) return true; // Run original for non-backpack inventories

        MethodInfo updateSlotStackMethod = AccessTools.Method(typeof(InventoryNetworkUtil), "UpdateSlotStack",
            new[] { typeof(ItemSlot), typeof(ItemStack) });
        MethodInfo itemStackFromPacketMethod = AccessTools.Method(typeof(InventoryNetworkUtil), "ItemStackFromPacket",
            new[] { typeof(IWorldAccessor), typeof(Packet_ItemStack) });

        for (int i = 0; i < packet.ItemstacksCount; i++)
        {
            Packet_ItemStack pstack = packet.Itemstacks[i];
            string? stringSlotId = ExtractStringSlotId(pstack);
            if (stringSlotId != null)
            {
                StripStringSlotId(pstack);
            }

            ItemStack? newStack = (ItemStack?)itemStackFromPacketMethod.Invoke(__instance,
                new object[] { resolver, pstack });

            if (stringSlotId != null && backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
            {
                bool didUpdate = (bool)updateSlotStackMethod.Invoke(__instance, new object[] { slot, newStack });
                if (didUpdate)
                {
                    inv.DidModifyItemSlot(slot);
                }
            }
            else if (stringSlotId != null)
            {
                // Slot not found — store for later when the slot is created
                newStack?.ResolveBlockOrItem(resolver);
                backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
            }
            else
            {
                // No string ID — fall back to integer index
                ItemSlot fallbackSlot = inv[i];
                bool didUpdate = (bool)updateSlotStackMethod.Invoke(__instance, new object[] { fallbackSlot, newStack });
                if (didUpdate)
                {
                    inv.DidModifyItemSlot(fallbackSlot);
                }
            }
        }

        return false; // Skip original
    }

    // =========================================================================
    // PATCH 5: Client-side UpdateFromPacket(Packet_InventoryDoubleUpdate)
    // For BackpackInventory, resolve slots by string ID instead of integer index.
    // =========================================================================

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate) })]
    [HarmonyPrefix]
    public static bool Prefix_UpdateFromPacket_Double(
        InventoryNetworkUtil __instance,
        IWorldAccessor resolver,
        Packet_InventoryDoubleUpdate packet)
    {
        var inv = AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>(__instance, "inv");
        if (inv is not BackpackInventory backpackInv) return true; // Run original

        MethodInfo updateSlotStackMethod = AccessTools.Method(typeof(InventoryNetworkUtil), "UpdateSlotStack",
            new[] { typeof(ItemSlot), typeof(ItemStack) });
        MethodInfo itemStackFromPacketMethod = AccessTools.Method(typeof(InventoryNetworkUtil), "ItemStackFromPacket",
            new[] { typeof(IWorldAccessor), typeof(Packet_ItemStack) });

        if (packet.InventoryId1 == inv.InventoryID)
        {
            ProcessDoubleUpdateSlot(
                __instance, backpackInv, resolver, packet.ItemStack1, packet.SlotId1,
                updateSlotStackMethod, itemStackFromPacketMethod);
        }

        if (packet.InventoryId2 == inv.InventoryID)
        {
            ProcessDoubleUpdateSlot(
                __instance, backpackInv, resolver, packet.ItemStack2, packet.SlotId2,
                updateSlotStackMethod, itemStackFromPacketMethod);
        }

        return false; // Skip original
    }

    private static void ProcessDoubleUpdateSlot(
        InventoryNetworkUtil instance,
        BackpackInventory backpackInv,
        IWorldAccessor resolver,
        Packet_ItemStack? pstack,
        int intSlotId,
        MethodInfo updateSlotStackMethod,
        MethodInfo itemStackFromPacketMethod)
    {
        string? stringSlotId = ExtractStringSlotId(pstack);
        if (stringSlotId != null && pstack != null)
        {
            StripStringSlotId(pstack);
        }

        ItemStack? newStack = (ItemStack?)itemStackFromPacketMethod.Invoke(instance,
            new object[] { resolver, pstack });

        if (stringSlotId != null && backpackInv.GetSlotByBackpackSlotId(stringSlotId, out ItemSlot? slot))
        {
            bool didUpdate = (bool)updateSlotStackMethod.Invoke(instance, new object[] { slot, newStack });
            if (didUpdate)
            {
                backpackInv.DidModifyItemSlot(slot);
            }
        }
        else if (stringSlotId != null)
        {
            // Slot not found — store for later
            newStack?.ResolveBlockOrItem(resolver);
            backpackInv.SetDeserializedSlotContent(stringSlotId, newStack);
        }
        else
        {
            // No string ID — fall back to integer index
            ItemSlot fallbackSlot = backpackInv[intSlotId];
            bool didUpdate = (bool)updateSlotStackMethod.Invoke(instance, new object[] { fallbackSlot, newStack });
            if (didUpdate)
            {
                backpackInv.DidModifyItemSlot(fallbackSlot);
            }
        }
    }

    // =========================================================================
    // PATCH 6: Server-side getDoubleUpdatePacket
    // Inject string slot IDs into both ItemStack packets.
    // =========================================================================

    [HarmonyPatch(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getDoubleUpdatePacket))]
    [HarmonyPostfix]
    public static void Postfix_getDoubleUpdatePacket(
        ref Packet_Server __result,
        IPlayer player,
        string[] invIds,
        int[] slotIds)
    {
        Packet_InventoryDoubleUpdate doubleUpdate = __result.InventoryDoubleUpdate;

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
    }

    // =========================================================================
    // PATCH 7: PlayerInventoryNetworkUtil.UpdateFromPacket override
    // This calls base.UpdateFromPacket, but first accesses inv[packet.SlotId].
    // We need to patch the slot resolution there too.
    // =========================================================================

    [HarmonyPatch(typeof(PlayerInventoryNetworkUtil), nameof(PlayerInventoryNetworkUtil.UpdateFromPacket),
        new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) })]
    [HarmonyPrefix]
    public static bool Prefix_PlayerInventoryNetworkUtil_UpdateFromPacket(
        PlayerInventoryNetworkUtil __instance,
        IWorldAccessor world,
        Packet_InventoryUpdate packet)
    {
        var inv = AccessTools.FieldRefAccess<InventoryNetworkUtil, InventoryBase>(__instance, "inv");
        if (inv is not BackpackInventory backpackInv) return true; // Run original

        // Replicate the PlayerInventoryNetworkUtil logic for active hotbar slot cancel
        string? stringSlotId = ExtractStringSlotId(packet.ItemStack);

        ItemSlot? slot = null;
        if (stringSlotId != null)
        {
            backpackInv.GetSlotByBackpackSlotId(stringSlotId, out slot);
            // Don't strip yet — the base prefix (Patch 3) will handle it
        }
        else
        {
            if (packet.SlotId < inv.Count)
            {
                slot = inv[packet.SlotId];
            }
        }

        if (slot != null && IsOwnHotbarSlotClient(__instance, slot))
        {
            ItemStack? prevStack = slot.Itemstack;
            if (prevStack != null)
            {
                // Peek at the new stack without consuming the string slot id
                ItemStack? newStackPreview = PeekItemStackFromPacket(world, packet.ItemStack);
                if (newStackPreview == null || prevStack.Collectible != newStackPreview.Collectible)
                {
                    var api = __instance.Api;
                    if (api is Vintagestory.API.Client.ICoreClientAPI capi)
                    {
                        var plr = capi.World.Player;
                        prevStack.Collectible.OnHeldInteractCancel(0, slot, plr.Entity, plr.CurrentBlockSelection, plr.CurrentEntitySelection, EnumItemUseCancelReason.Destroyed);
                    }
                }
            }
        }

        // Now delegate to the base UpdateFromPacket which is handled by Patch 3
        // Call the base class method directly
        MethodInfo baseUpdateMethod = AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket),
            new Type[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) });
        baseUpdateMethod.Invoke(__instance, new object[] { world, packet });

        return false; // Skip original
    }

    private static bool IsOwnHotbarSlotClient(InventoryNetworkUtil instance, ItemSlot slot)
    {
        var api = instance.Api;
        if (api is Vintagestory.API.Client.ICoreClientAPI capi)
        {
            return capi.World.Player.InventoryManager.ActiveHotbarSlot == slot;
        }
        return false;
    }

    /// <summary>
    /// Creates an ItemStack from a packet without stripping the slot ID marker.
    /// Used for preview/comparison purposes only.
    /// </summary>
    private static ItemStack? PeekItemStackFromPacket(IWorldAccessor resolver, Packet_ItemStack? pItemStack)
    {
        if (pItemStack == null || pItemStack.ItemClass == -1 || pItemStack.ItemId == 0) return null;

        // Temporarily strip for deserialization, then restore
        TreeAttribute attributes = new();
        if (pItemStack.Attributes != null && pItemStack.Attributes.Length > 0)
        {
            using MemoryStream ms = new(pItemStack.Attributes);
            using BinaryReader reader = new(ms);
            attributes.FromBytes(reader);
        }

        // Remove our marker for clean deserialization
        attributes.RemoveAttribute(SlotIdAttributeKey);

        // Create a temporary clean packet
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

    // =========================================================================
    // Helper methods
    // =========================================================================

    /// <summary>
    /// Injects the string slot ID into a Packet_ItemStack's Attributes byte array.
    /// Called on the server side during packet creation.
    /// </summary>
    public static Packet_ItemStack InjectStringSlotId(Packet_ItemStack pstack, InventoryBase inv, int slotId)
    {
        if (pstack == null || inv is not BackpackInventory) return pstack;

        ItemSlot slot = inv[slotId];
        if (slot is not IBackpackSlot playerSlot) return pstack;

        string? stringSlotId = playerSlot.FullSlotId;
        if (stringSlotId == null) return pstack;

        TreeAttribute attributes = new();
        if (pstack.Attributes != null && pstack.Attributes.Length > 0)
        {
            using MemoryStream ms = new(pstack.Attributes);
            using BinaryReader reader = new(ms);
            attributes.FromBytes(reader);
        }

        attributes.SetString(SlotIdAttributeKey, stringSlotId);

        using MemoryStream outMs = new();
        using BinaryWriter writer = new(outMs);
        attributes.ToBytes(writer);
        pstack.Attributes = outMs.ToArray();

        return pstack;
    }

    /// <summary>
    /// Extracts the string slot ID from a Packet_ItemStack's Attributes, if present.
    /// Returns null if not found.
    /// </summary>
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

    /// <summary>
    /// Strips the injected slot ID attribute from the Packet_ItemStack attributes
    /// so it doesn't pollute the actual ItemStack attributes when deserialized.
    /// </summary>
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
            // If we can't parse it, leave it alone
        }

        return pstack;
    }

    // =========================================================================
    // IL helper utilities
    // =========================================================================

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
}
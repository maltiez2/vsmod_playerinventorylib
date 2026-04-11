using PlayerInventoryLib.Armor;
using PlayerInventoryLib.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace PlayerInventoryLib;

public class CharacterInventory : InventoryCharacter
{
    public CharacterInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        _api = api;

        _disableVanillaArmorSlots = _api.ModLoader.IsModEnabled("PlayerInventoryLib");

        FillArmorIconsDict(api);

        _slots = GenEmptySlots(_totalSlotsNumber);

        for (int index = 0; index < _slots.Length; index++)
        {
            if (_disableVanillaArmorSlots && IsVanillaArmorSlot(index))
            {
                if (ModifyColors)
                {
                    _slots[index].DrawUnavailable = true;
                    _slots[index].HexBackgroundColor = "#884444";
                }
                _slots[index].MaxSlotStackSize = 0;
            }
        }

        FillSlotsOwnerAndWorld();
    }
    public CharacterInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        _api = api;

        _disableVanillaArmorSlots = _api.ModLoader.IsModEnabled("PlayerInventoryLib");

        FillArmorIconsDict(api);

        _slots = GenEmptySlots(_totalSlotsNumber);

        for (int index = 0; index < _slots.Length; index++)
        {
            if (_disableVanillaArmorSlots && IsVanillaArmorSlot(index))
            {
                if (ModifyColors)
                {
                    _slots[index].DrawUnavailable = true;
                    _slots[index].HexBackgroundColor = "#884444";
                }
                _slots[index].MaxSlotStackSize = 0;
            }
        }

        FillSlotsOwnerAndWorld();
    }



    public override ItemSlot this[int slotId] { get => _slots[slotId]; set => LoggerUtil.Warn(Api, this, "Armor slots cannot be set"); }

    public override int Count => _totalSlotsNumber;

    public delegate void SlotModifiedDelegate(bool itemChanged, bool durabilityChanged, bool isArmorSlot);

    public event SlotModifiedDelegate? OnSlotModified;
    public event SlotModifiedDelegate? OnArmorSlotModified;

    public virtual bool ModifyColors { get; set; } = true;

    public static readonly List<string> GearSlotTypes = [
        "headgear",
        "frontgear",
        "backgear",
        "rightshouldergear",
        "leftshouldergear",
        "waistgear",
        "miscgear",
        "miscgear",
        "miscgear",
        "addBeltLeft",
        "addBeltRight",
        "addBeltBack",
        "addBeltFront",
        "addBackpack1",
        "addBackpack2",
        "addBackpack3",
        "addBackpack4"
    ];

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);
        for (int index = 0; index < _slots.Length; index++)
        {
            ItemStack? itemStack = tree.GetTreeAttribute("slots")?.GetItemstack(index.ToString() ?? "");

            if (itemStack != null)
            {
                if (Api?.World != null) itemStack.ResolveBlockOrItem(Api.World);

                _slots[index].Itemstack = itemStack;
            }

            if (_disableVanillaArmorSlots && IsVanillaArmorSlot(index))
            {
                if (ModifyColors)
                {
                    _slots[index].DrawUnavailable = true;
                    _slots[index].HexBackgroundColor = "#884444";
                }
                _slots[index].MaxSlotStackSize = 0;
            }
        }

        FillSlotsOwnerAndWorld();
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        tree.SetInt("qslots", _vanillaSlots);

        TreeAttribute treeAttribute = new();
        for (int index = 0; index < _slots.Length; index++)
        {
            if (_slots[index].Itemstack != null)
            {
                treeAttribute.SetItemstack(index.ToString() ?? "", _slots[index].Itemstack.Clone());
            }
        }

        tree["slots"] = treeAttribute;
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        base.OnItemSlotModified(slot);

        if (_api.Side == EnumAppSide.Server)
        {
            ClearArmorSlots();
        }

        if (slot is GearSlot gearSlotModified)
        {
            try
            {
                foreach (GearSlot gearSlot in _slots.OfType<GearSlot>())
                {
                    gearSlot.CheckParentSlot();
                    if (!gearSlot.Enabled && !gearSlot.Empty)
                    {
                        IInventory targetInv = Player.InventoryManager.GetOwnInventory(GlobalConstants.groundInvClassName);
                        gearSlot.TryPutInto(Player.Entity.Api.World, targetInv[0]);
                        gearSlot.MarkDirty();
                    }
                }
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(Api, this, $"Error while processing gear slots after slot was modified: {exception}");
                return;
            }

            if (gearSlotModified.PreviousItemId != (gearSlotModified.Itemstack?.Id ?? 0))
            {
                if (_api.Side == EnumAppSide.Client)
                {
                    _api.Event.EnqueueMainThreadTask(() => GuiDialogPatches.GuiDialogInventoryInstance?.ComposeGui(false), "");
                }

            }
            gearSlotModified.PreviousEmpty = gearSlotModified.Empty;
        }

        if (slot is ClothesSlot clothesSlot)
        {
            int currentItemId = slot.Itemstack?.Item?.ItemId ?? 0;
            bool itemChanged = currentItemId != clothesSlot.PreviousItemId;
            clothesSlot.PreviousItemId = currentItemId;

            bool containsBag = clothesSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>() != null;
            if (itemChanged && (clothesSlot.PreviouslyHeldBag || containsBag))
            {
                ReloadBagInventory();
            }
            clothesSlot.PreviouslyHeldBag = containsBag;

            bool durabilityChanged = false;
            if (!itemChanged)
            {
                int currentDurability = slot.Itemstack?.Item?.GetRemainingDurability(slot.Itemstack) ?? 0;
                durabilityChanged = currentDurability != clothesSlot.PreviousDurability;
                clothesSlot.PreviousDurability = currentDurability;
            }
            else
            {
                int currentDurability = slot.Itemstack?.Item?.GetRemainingDurability(slot.Itemstack) ?? 0;
                clothesSlot.PreviousDurability = currentDurability;
            }

            OnSlotModified?.Invoke(itemChanged, durabilityChanged, false);
        }

        if (slot is ArmorSlot armorSlot)
        {
            int currentItemId = slot.Itemstack?.Item?.ItemId ?? 0;
            bool itemChanged = currentItemId != armorSlot.PreviousItemId;
            armorSlot.PreviousItemId = currentItemId;

            bool containsBag = armorSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>() != null;
            if (itemChanged && (armorSlot.PreviouslyHeldBag || containsBag))
            {
                ReloadBagInventory();
            }
            armorSlot.PreviouslyHeldBag = containsBag;

            bool durabilityChanged = false;
            if (!itemChanged)
            {
                int currentDurability = slot.Itemstack?.Item?.GetRemainingDurability(slot.Itemstack) ?? 0;
                durabilityChanged = currentDurability != armorSlot.PreviousDurability;
                armorSlot.PreviousDurability = currentDurability;
            }
            else
            {
                int currentDurability = slot.Itemstack?.Item?.GetRemainingDurability(slot.Itemstack) ?? 0;
                armorSlot.PreviousDurability = currentDurability;
            }

            foreach (ArmorSlot armorSlot2 in this.OfType<ArmorSlot>())
            {
                if (!ModifyColors) break;
                bool available = IsSlotAvailable(armorSlot2.ArmorType) || !armorSlot2.Empty;
                if (!available)
                {
                    if (armorSlot2.HexBackgroundColor == "#5fbed4")
                    {

                    }
                    else
                    {
                        armorSlot2.HexBackgroundColor = "#777777";
                    }
                }
                else
                {
                    if (armorSlot2.HexBackgroundColor == "#5fbed4")
                    {

                    }
                    else
                    {
                        armorSlot2.HexBackgroundColor = null;
                    }
                }
            }

            GuiDialogPatches.RecalculateArmorStatsForGui();

            OnSlotModified?.Invoke(itemChanged, durabilityChanged, true);
            OnArmorSlotModified?.Invoke(itemChanged, durabilityChanged, true);
        }

        IGearSlotModifiedListener? listener = slot?.Itemstack?.Collectible?.GetCollectibleInterface<IGearSlotModifiedListener>();
        if (listener != null)
        {
            try
            {
                listener.OnSlotModified(slot, this, Owner as EntityPlayer);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(Api, this, $"Error while calling 'IGearSlotModifiedListener.OnSlotModified' on slot modified for '{slot?.Itemstack?.Collectible?.Code}':\n{exception}");
            }
        }
    }
    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        object result = base.ActivateSlot(slotId, sourceSlot, ref op);

        if (slotId < _clothesSlotsCount)
        {
            ReloadBagInventory();
        }

        return result;
    }
    public override void DiscardAll()
    {
        base.DiscardAll();

        ReloadBagInventory();
    }
    public override void DropAll(Vec3d pos, int maxStackSize = 0)
    {
        base.DropAll(pos, maxStackSize);

        ReloadBagInventory();
    }
}

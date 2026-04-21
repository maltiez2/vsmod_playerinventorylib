using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using static OpenTK.Graphics.OpenGL.GL;

namespace PlayerInventoryLib.GUI;


public class GuiDialogSurvivalInventory : GuiDialog
{
    public GuiDialogSurvivalInventory(ICoreClientAPI capi) : base(capi)
    {
        (capi.World as ClientMain)?.eventManager.OnPlayerModeChange.Add(this.OnPlayerModeChanged);
        _api = capi;
    }


    public override double DrawOrder => 0.2;

    public override string ToggleKeyCombinationCode => "inventorydialog";

    public override bool PrefersUngrabbedMouse => false;

    public override float ZSize => 250;


    public override void OnOwnPlayerDataReceived()
    {
        ComposeGui(true);

        _prevGameMode = capi.World.Player.WorldData.CurrentGameMode;
    }

    public void ComposeGui(bool firstBuild)
    {
        IPlayerInventoryManager invm = capi.World.Player.InventoryManager;
        _craftingInv = invm.GetOwnInventory(GlobalConstants.craftingInvClassName);
        _backpackInv = invm.GetOwnInventory(GlobalConstants.backpackInvClassName);
        _characterInv = invm.GetOwnInventory(GlobalConstants.characterInvClassName);


        if (firstBuild)
        {
            _backpackInv.SlotModified += BackpackInv_SlotModified;
            _characterInv.SlotModified += BackpackInv_SlotModified;
        }

        if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Spectator)
        {
            try
            {
                ComposeSurvivalInvDialog();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return;
            }

            if (_composer != null)
            {
                Composers["maininventory_survival"] = _composer;
            }
            
        }

        if (firstBuild)
        {
            OnPlayerModeChanged();
        }
    }


    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        ComposeGui(false);

        capi.World.Player.Entity.TryStopHandAction(true, EnumItemUseCancelReason.OpenedGui);

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
        {
            if (_craftingInv != null)
            {
                capi.Network.SendPacketClient((Packet_Client)_craftingInv.Open(capi.World.Player));
            }
            if (_backpackInv != null)
            {
                capi.Network.SendPacketClient((Packet_Client)_backpackInv.Open(capi.World.Player));
            }
        }
    }

    public override void OnGuiClosed()
    {
        if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            if (_craftingInv != null)
            {
                // Try to put the stuff into the players inventory first
                foreach (ItemSlot slot in _craftingInv)
                {
                    if (slot.Empty) continue;
                    ItemStackMoveOperation moveop = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, slot.StackSize);
                    moveop.ActingPlayer = capi.World.Player;
                    object[] packets = capi.World.Player.InventoryManager.TryTransferAway(slot, ref moveop, true, false);
                    for (int i = 0; packets != null && i < packets.Length; i++)
                    {
                        capi.Network.SendPacketClient((Packet_Client)packets[i]);
                    }
                }

                capi.World.Player.InventoryManager.DropAllInventoryItems(_craftingInv);

                capi.Network.SendPacketClient((Packet_Client)_craftingInv.Close(capi.World.Player));
                _composer.GetSlotGrid("craftinggrid").OnGuiClosed(capi);
                _composer.GetSlotGrid("outputslot").OnGuiClosed(capi);
            }

            if (_composer != null)
            {
                capi.Network.SendPacketClient((Packet_Client)_backpackInv.Close(capi.World.Player));
                _composer.GetSlotGrid("slotgrid").OnGuiClosed(capi);
            }
        }
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Handled) return;

        foreach (GuiComposer composer in Composers.Values)
        {
            composer.OnMouseDown(args);
            if (args.Handled)
            {
                return;
            }
        }

        if (!args.Handled)
        {
            foreach (GuiComposer composer in Composers.Values)
            {
                if (composer.Bounds.PointInside(args.X, args.Y))
                {
                    args.Handled = true;
                }
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        _composer?.Dispose();
    }



    private IInventory? _backpackInv;
    private IInventory? _craftingInv;
    private IInventory? _characterInv;
    private int _prevRows;
    private EnumGameMode _prevGameMode;
    private readonly ICoreClientAPI _api;
    private readonly List<int> _rows = [];
    private GuiComposer? _composer;

    private void ComposeSurvivalInvDialog()
    {
        if (_backpackInv == null || _craftingInv == null)
        {
            return;
        }
        
        double elemToDlgPad = GuiStyle.ElementToDialogPadding;
        double pad = GuiElementItemSlotGrid.unscaledSlotPadding;
        int rows = (int)Math.Ceiling(_backpackInv.Count / 6f);
        _prevRows = rows;


        // 1. The bounds of the slot grid itself. It is offseted by slot padding. It determines the size of the dialog, so we build the dialog from the bottom up
        ElementBounds slotGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, pad, 6, 7).FixedGrow(2 * pad, 2 * pad);

        // 1a.) Determine the full size of scrollable area, required to calculate scrollbar handle size
        ElementBounds fullGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 6, rows);

        // 2. Around that is the 3 wide inset stroke
        ElementBounds insetBounds = slotGridBounds.ForkBoundingParent(3, 3, 3, 3);

        // 2a. The scrollable bounds is also the clipping bounds. Needs it's parent to be set.
        ElementBounds clippingBounds = slotGridBounds.CopyOffsetedSibling();
        clippingBounds.fixedHeight -= 3; // Why?

        ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 3, 3).FixedRightOf(insetBounds, 45);
        gridBounds.fixedY += 50;

        ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, 1).FixedRightOf(insetBounds, 45).FixedUnder(gridBounds, 20);
        outputBounds.fixedX += pad + GuiElementPassiveItemSlot.unscaledSlotSize;

        // 3. Around all that is the dialog centered to screen middle, with some extra spacing right for the scrollbar
        ElementBounds dialogBounds =
            insetBounds
            .ForkBoundingParent(elemToDlgPad, elemToDlgPad + 30, elemToDlgPad + gridBounds.fixedWidth + 20, elemToDlgPad)
        ;

        if (_api.Settings.Bool["immersiveMouseMode"])
        {
            dialogBounds
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-12, 0)
            ;
        }
        else
        {
            dialogBounds
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(20, 0)
            ;
        }

        // 4. Don't forget the Scroll bar.  Sometimes mods add bags that are a bit big.
        ElementBounds scrollBarBounds = ElementStdBounds.VerticalScrollbar(insetBounds).WithParent(dialogBounds);
        scrollBarBounds.fixedOffsetX -= 2;
        scrollBarBounds.fixedWidth = 15;

        GuiComposer composer = _api.Gui.CreateCompo("inventory-backpack", dialogBounds);
        composer.AddShadedDialogBG(ElementBounds.Fill);
        composer.AddDialogTitleBar(Lang.Get("Inventory and Crafting"), () => CloseIconPressed());
        composer.AddVerticalScrollbar(OnNewScrollbarvalue, scrollBarBounds, "scrollbar");

        composer.AddInset(insetBounds, 3, 0.85f);
        composer.BeginClip(clippingBounds);
        ComposeBackpackSlots(composer, _backpackInv, fullGridBounds);
        composer.EndClip();

        composer.AddItemSlotGrid(_craftingInv, (data) => SendInvPacket(data), 3, [0, 1, 2, 3, 4, 5, 6, 7, 8], gridBounds, "craftinggrid");
        composer.AddItemSlotGrid(_craftingInv, (data) => SendInvPacket(data), 1, [9], outputBounds, "outputslot");

        try
        {
            composer.Compose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return;
        }

        composer.GetScrollbar("scrollbar").SetHeights(
            (float)(slotGridBounds.fixedHeight),
            (float)(_rows.Select(rows => (rows + 0.4) * (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding)).Sum()) - 12)
        ;

        _composer = composer;
    }

    private void OnNewScrollbarvalue(float value)
    {
        if (!IsOpened() || _composer == null)
        {
            return;
        }

        ElementBounds bounds = _composer.GetSlotGrid("slotgrid").Bounds;
        bounds.fixedY = 10 - GuiElementItemSlotGrid.unscaledSlotPadding - value;
        bounds.CalcWorldBounds();

        int index = 0;
        while (true)
        {
            try
            {
                if (_composer.GetElement($"slotgrid-{index}") == null) break;

                ElementBounds bounds2 = _composer.GetSlotGridExcl($"slotgrid-{index}").Bounds;
                ElementBounds bounds3 = _composer.GetRichtext($"category-{index}").Bounds;
                bounds3.fixedY = bounds.fixedY + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * _rows[index];
                bounds2.fixedY = bounds.fixedY + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * (_rows[index] + 0.4);
                bounds2.CalcWorldBounds();
                bounds3.CalcWorldBounds();
                bounds = bounds2;
                index++;
            }
            catch
            {
                break;
            }
        }
    }

    private void BackpackInv_SlotModified(int slotIndex)
    {
        int rows = (int)Math.Ceiling(_backpackInv.Count / 6f);

        ComposeSurvivalInvDialog();
        Composers.Remove("maininventory_survival");

        if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Spectator && _composer != null)
        {
            Composers["maininventory_survival"] = _composer;
        }
    }

    private void SendInvPacket(object packet)
    {
        capi.Network.SendPacketClient(packet);
    }

    private void CloseIconPressed()
    {
        TryClose();
    }

    private void OnPlayerModeChanged()
    {
        // These 2 lines were flipped, causing unecessary lag
        if (!IsOpened()) return;

        // Only recompose if the mode actually changed
        if (_prevGameMode != capi.World.Player.WorldData.CurrentGameMode)
        {
            Composers.Remove("maininventory_survival");
            ComposeGui(false);
            _prevGameMode = capi.World.Player.WorldData.CurrentGameMode;

            if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                capi.Network.SendPacketClient((Packet_Client)_backpackInv.Close(capi.World.Player));
            }

            if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
            {
                capi.Network.SendPacketClient((Packet_Client)_backpackInv.Open(capi.World.Player));
            }
        }
    }

    private void ComposeBackpackSlots(GuiComposer composer, IInventory? backPackInv, ElementBounds fullGridBounds)
    {
        if (backPackInv == null) return;

        _rows.Clear();

        List<int> generalSlots = [];
        List<(float order, string category, List<int> indexes)> specialSlots = [];

        int startingIndex = (backPackInv as BackpackInventory)?.VanillaBackpackSlotsCount ?? 4;

        for (int slotIndex = startingIndex; slotIndex < backPackInv.Count; slotIndex++)
        {
            ItemSlot? slot = backPackInv[slotIndex];

            if (slot is PlaceholderItemSlot)
            {
                continue;
            }

            if (slot == null)
            {
                continue;
            }

            if (slot is IBackpackSlot slotWithCategory && slotWithCategory.BackpackSlotConfig.BackpackCategory != null)
            {
                bool categoryExists = false;
                foreach ((float order, string category, List<int> indexes) in specialSlots)
                {
                    if (category == slotWithCategory.BackpackSlotConfig.BackpackCategory)
                    {
                        indexes.Add(slotIndex);
                        categoryExists = true;
                        break;
                    }
                }
                if (!categoryExists)
                {
                    specialSlots.Add((slotWithCategory.BackpackSlotConfig.Priority, slotWithCategory.BackpackSlotConfig.BackpackCategory, [slotIndex]));
                }
            }
            else
            {
                generalSlots.Add(slotIndex);
            }
        }

        generalSlots = generalSlots.OrderBy(index => (backPackInv[index] as IBackpackSlot)?.BackpackSlotId ?? "").ToList();
        generalSlots = generalSlots.OrderBy(index => (backPackInv[index] as IBackpackSlot)?.BackpackSlotConfig.Priority ?? 0).ToList();

        specialSlots.Sort((a, b) => Math.Sign(b.order - a.order));

        ElementBounds generalGridBounds = fullGridBounds;

        composer.AddItemSlotGrid(backPackInv, SendInvPacket, 6, generalSlots.ToArray(), generalGridBounds, "slotgrid");

        _rows.Add((int)Math.Ceiling(generalSlots.Count / 6f));

        ElementBounds specialGridBounds = fullGridBounds.FlatCopy();
        for (int categoryIndex = 0; categoryIndex < specialSlots.Count; categoryIndex++)
        {
            _rows.Add((int)Math.Ceiling(specialSlots[categoryIndex].indexes.Count / 6f));

            double Y = specialGridBounds.fixedY + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * _rows[categoryIndex];

            specialGridBounds = fullGridBounds.FlatCopy();
            specialGridBounds.fixedY = Y;

            composer.AddRichtext(Lang.Get($"slotcategory-{specialSlots[categoryIndex].category}"), CairoFont.WhiteSmallText().WithFontSize(14), specialGridBounds, $"category-{categoryIndex}");

            specialGridBounds = fullGridBounds.FlatCopy();
            specialGridBounds.fixedY = Y + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * 0.4;

            composer.AddItemSlotGridExcl(
                backPackInv,
                SendInvPacket,
                6,
                GetInvertedIndexes(specialSlots[categoryIndex].indexes, backPackInv.Count),
                specialGridBounds,
                $"slotgrid-{categoryIndex}");
        }
    }
    
    private static int[] GetInvertedIndexes(List<int> indexes, int total)
    {
        List<int> result = [];
        for (int index = 0; index < total; index++)
        {
            if (!indexes.Contains(index))
            {
                result.Add(index);
            }
        }
        return result.ToArray();
    }
}
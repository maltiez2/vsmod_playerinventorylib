using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace PlayerInventoryLib.GUI;


public class GuiDialogSurvivalInventory : GuiDialog
{
    public GuiDialogSurvivalInventory(ICoreClientAPI capi) : base(capi)
    {
        (capi.World as ClientMain)?.eventManager.OnPlayerModeChange.Add(this.OnPlayerModeChanged);
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

        if (firstBuild)
        {
            _backpackInv.SlotModified += BackpackInv_SlotModified;
        }

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
        {
            ComposeSurvivalInvDialog();
            Composers["maininventory"] = _survivalInvDialog;
        }

        if (firstBuild)
        {
            OnPlayerModeChanged();
        }
    }

    public override bool TryOpen()
    {
        EnumGameMode currentGameMode = capi.World.Player.WorldData.CurrentGameMode;

        if (currentGameMode != EnumGameMode.Creative) return false;

        return base.TryOpen();
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
                _survivalInvDialog.GetSlotGrid("craftinggrid").OnGuiClosed(capi);
                _survivalInvDialog.GetSlotGrid("outputslot").OnGuiClosed(capi);
            }

            if (_survivalInvDialog != null)
            {
                capi.Network.SendPacketClient((Packet_Client)_backpackInv.Close(capi.World.Player));
                _survivalInvDialog.GetSlotGridExcl("slotgrid").OnGuiClosed(capi);
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

        _survivalInvDialog?.Dispose();
    }



    private IInventory? _backpackInv;
    private IInventory? _craftingInv;
    private GuiComposer? _survivalInvDialog;
    private int _prevRows;
    private EnumGameMode _prevGameMode;


    private void ComposeSurvivalInvDialog()
    {
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

        if (capi.Settings.Bool["immersiveMouseMode"])
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

        //if (survivalInvDialog != null) survivalInvDialog.Dispose();

        _survivalInvDialog =
            capi.Gui
            .CreateCompo("inventory-backpack", dialogBounds)
            .AddShadedDialogBG(ElementBounds.Fill)
            .AddDialogTitleBar(Lang.Get("Inventory and Crafting"), CloseIconPressed)
            .AddVerticalScrollbar(OnNewScrollbarvalue, scrollBarBounds, "scrollbar")

            .AddInset(insetBounds, 3, 0.85f)
            .BeginClip(clippingBounds)
            .AddItemSlotGridExcl(_backpackInv, SendInvPacket, 6, new int[] { 0, 1, 2, 3 }, fullGridBounds, "slotgrid")
            .EndClip()

            .AddItemSlotGrid(_craftingInv, SendInvPacket, 3, new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, gridBounds, "craftinggrid")
            .AddItemSlotGrid(_craftingInv, SendInvPacket, 1, new int[] { 9 }, outputBounds, "outputslot")

            .Compose()
        ;

        _survivalInvDialog.GetScrollbar("scrollbar").SetHeights(
            (float)(slotGridBounds.fixedHeight),
            (float)(fullGridBounds.fixedHeight + pad))
        ;
    }

    private void BackpackInv_SlotModified(int t1)
    {
        int rows = (int)Math.Ceiling(_backpackInv.Count / 6f);
        if (rows != _prevRows)
        {
            ComposeSurvivalInvDialog();
            Composers.Remove("maininventory");

            if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
            {
                Composers["maininventory"] = _survivalInvDialog;
            }
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

    private void OnNewScrollbarvalue(float value)
    {
        if (!IsOpened()) return;

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival && _survivalInvDialog != null)
        {
            ElementBounds bounds = _survivalInvDialog.GetSlotGridExcl("slotgrid").Bounds;
            bounds.fixedY = 10 - GuiElementItemSlotGrid.unscaledSlotPadding - value;

            bounds.CalcWorldBounds();
        }
    }

    private void OnPlayerModeChanged()
    {
        // These 2 lines were flipped, causing unecessary lag
        if (!IsOpened()) return;

        // Only recompose if the mode actually changed
        if (_prevGameMode != capi.World.Player.WorldData.CurrentGameMode)
        {
            Composers.Remove("maininventory");
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

    /*internal override bool OnKeyCombinationToggle(KeyCombination viaKeyComb)
    {
        //if (!Composers.ContainsKey("maininventory")) return false;
        if (IsOpened() && (_creativeInv != null && _creativeInvDialog != null && _creativeInvDialog.GetTextInput("searchbox")?.HasFocus == true)) return false;

        return base.OnKeyCombinationToggle(viaKeyComb);
    }*/
}
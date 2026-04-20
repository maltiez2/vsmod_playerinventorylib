using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace PlayerInventoryLib.GUI;


public class GuiDialogCreativeInventory : GuiDialog
{
    public GuiDialogCreativeInventory(ICoreClientAPI capi) : base(capi)
    {
        (capi.World as ClientMain)?.eventManager.OnPlayerModeChange.Add(OnPlayerModeChanged);

        capi.Input.RegisterHotKey("creativesearch", Lang.Get("Search Creative inventory"), GlKeys.F, HotkeyType.CreativeTool, false, true);
        capi.Input.SetHotKeyHandler("creativesearch", OnSearchCreative);
    }


    public override double DrawOrder => 0.2; // Needs to be same as chest container guis so it can be on top of those dialogs if necessary

    public override string ToggleKeyCombinationCode => "inventorydialog-creative";

    public override float ZSize => 250;

    public override bool PrefersUngrabbedMouse => Composers["maininventory"] == _creativeInvDialog;


    public override void OnOwnPlayerDataReceived()
    {
        ComposeGui(true);
        //TyronThreadPool.QueueTask(() => creativeInv.CreativeTabs.CreateSearchCache(capi.World), "GuiDialogInventory.CreateSearchCache");
        _previousGameMode = capi.World.Player.WorldData.CurrentGameMode;
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

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative && _creativeInv != null)
        {
            capi.Network.SendPacketClient((Packet_Client)_creativeInv.Open(capi.World.Player));
        }
    }

    public override void OnGuiClosed()
    {
        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            _creativeInvDialog?.GetTextInput("searchbox")?.SetValue("");
            _creativeInvDialog?.GetSlotGrid("slotgrid")?.OnGuiClosed(capi);
            capi.Network.SendPacketClient((Packet_Client)_creativeInv.Close(capi.World.Player));
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

        // This is a really silly hack but it works.
        // This bascially allows you to drop itemstack into the empty are of the creative inventory and have them deleted that way
        // This is done by just pretending the player clicks on creative slot 0, or slot 1 if he holds the same thing in hands as slot 0
        if (!args.Handled && _creativeInv != null && _creativeClippingBounds != null && _creativeClippingBounds.PointInside(args.X, args.Y) && capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            ItemSlot mouseCursorSlot = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.mousecursorInvClassName)[0];
            if (!mouseCursorSlot.Empty)
            {
                ItemStackMoveOperation op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge);
                op.ActingPlayer = capi.World.Player;

                op.CurrentPriority = EnumMergePriority.DirectMerge;
                int slotid = mouseCursorSlot.Itemstack.Equals(capi.World, _creativeInv[0].Itemstack, GlobalConstants.IgnoredStackAttributes) ? 1 : 0;
                object packet = _creativeInv.ActivateSlot(slotid, mouseCursorSlot, ref op);

                if (packet != null)
                {
                    SendInvPacket(packet);
                }
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

    public override bool CaptureAllInputs()
    {
        return IsOpened() && _creativeInvDialog?.GetTextInput("searchbox").HasFocus == true;
    }

    public override void Dispose()
    {
        base.Dispose();

        _creativeInvDialog?.Dispose();
    }

    /*internal override bool OnKeyCombinationToggle(KeyCombination viaKeyComb)
    {
        //if (!Composers.ContainsKey("maininventory")) return false;
        if (IsOpened() && (creativeInv != null && creativeInvDialog != null && creativeInvDialog.GetTextInput("searchbox")?.HasFocus == true)) return false;

        return base.OnKeyCombinationToggle(viaKeyComb);
    }*/


    public void ComposeGui(bool firstBuild)
    {
        IPlayerInventoryManager invm = capi.World.Player.InventoryManager;
        _creativeInv = (InventoryPlayerCreative)invm.GetOwnInventory(GlobalConstants.creativeInvClassName);

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            ComposeCreativeInvDialog();
            Composers["maininventory"] = _creativeInvDialog;
        }

        if (firstBuild)
        {
            OnPlayerModeChanged();
        }
    }



    private readonly int _columnsNumber = 15;
    private InventoryPlayerCreative? _creativeInv;
    private GuiComposer? _creativeInvDialog;
    private ElementBounds? _creativeClippingBounds;
    private EnumGameMode _previousGameMode;
    private int _currentTabIndex;


    private bool OnSearchCreative(KeyCombination keyCombination)
    {
        if (capi.World.Player.WorldData.CurrentGameMode != EnumGameMode.Creative) return false;

        if (TryOpen())
        {
            _creativeInvDialog?.FocusElement(_creativeInvDialog.GetTextInput("searchbox").TabIndex);
        }

        return true;
    }

    private void ComposeCreativeInvDialog()
    {
        if (_creativeInv == null)
        {
            ScreenManager.Platform.Logger.Notification("Server did not send a creative inventory, so I won't display one");
            return;
        }

        double elemToDlgPad = GuiStyle.ElementToDialogPadding;
        double pad = GuiElementItemSlotGrid.unscaledSlotPadding;

        int rows = (int)Math.Ceiling(_creativeInv.Count / (float)_columnsNumber);

        // 1. The bounds of the slot grid itself. It is offseted by slot padding. It determines the size of the dialog, so we build the dialog from the bottom up
        ElementBounds slotGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, pad, _columnsNumber, 9).FixedGrow(2 * pad, 2 * pad);

        // 1a.) Determine the full size of scrollable area, required to calculate scrollbar handle size
        ElementBounds fullGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, _columnsNumber, rows);

        // 2a. The scrollable bounds is also the clipping bounds. Needs it's parent to be set.
        _creativeClippingBounds = slotGridBounds.ForkBoundingParent(0, 0, 0, 0);
        _creativeClippingBounds.Name = "clip";

        // 2. Around that is the 3 wide inset stroke
        ElementBounds insetBounds = _creativeClippingBounds.ForkBoundingParent(6, 3, 0, 3);
        insetBounds.Name = "inset";


        // 3. Around all that is the dialog centered to screen middle, with some extra spacing right for the scrollbar
        ElementBounds dialogBounds =
                insetBounds
                    .ForkBoundingParent(elemToDlgPad, elemToDlgPad + 70, elemToDlgPad + 31, elemToDlgPad)
                    .WithFixedAlignmentOffset(-3, -100)
                    .WithAlignment(EnumDialogArea.CenterBottom)
            ;

        // 4. Right of the slot grid is the scrollbar
        ElementBounds scrollbarBounds = ElementStdBounds.VerticalScrollbar(insetBounds).WithParent(dialogBounds);

        // 5. Above is text input
        ElementBounds textInputBounds = ElementBounds.Fixed(elemToDlgPad, 45, 250, 30);
        ElementBounds tabBoundsL = ElementBounds.Fixed(-130, 35, 130, 545);

        ElementBounds tabBoundsR = ElementBounds.Fixed(0, 35, 130, 545).FixedRightOf(dialogBounds).WithFixedAlignmentOffset(-4, 0);

        ElementBounds rightTextBounds = ElementBounds.Fixed(elemToDlgPad, 45, 250, 30).WithAlignment(EnumDialogArea.RightFixed).WithFixedAlignmentOffset(-28 - elemToDlgPad, 7);

        CreativeTabsConfig creativeTabsConfig = capi.Assets.TryGet("config/creativetabs.json").ToObject<CreativeTabsConfig>();

        IEnumerable<CreativeTab> unorderedTabs = _creativeInv.CreativeTabs.Tabs;
        List<TabConfig> orderedTabs = new List<TabConfig>();

        foreach (CreativeTab tab in unorderedTabs)
        {
            TabConfig tabcfg = creativeTabsConfig.TabConfigs.FirstOrDefault(cfg => cfg.Code == tab.Code);
            if (tabcfg == null) tabcfg = new TabConfig() { Code = tab.Code, ListOrder = 1 };

            int pos = 0;
            for (int i = 0; i < orderedTabs.Count; i++)
            {
                if (orderedTabs[i].ListOrder < tabcfg.ListOrder) pos++;
                else break;
            }

            orderedTabs.Insert(pos, tabcfg);
        }


        int currentGuiTabIndex = 0;
        GuiTab[] tabs = new GuiTab[orderedTabs.Count];
        double maxWidth = 0;
        double padding = GuiElement.scaled(3);
        CairoFont font = CairoFont.WhiteDetailText().WithFontSize(17);

        for (int i = 0; i < orderedTabs.Count; i++)
        {
            int tabIndex = unorderedTabs.FirstOrDefault(tab => tab.Code == orderedTabs[i].Code).Index;

            if (tabIndex == _currentTabIndex) currentGuiTabIndex = i;

            tabs[i] = new GuiTab()
            {
                DataInt = tabIndex,
                Name = Lang.Get("tabname-" + orderedTabs[i].Code),
                PaddingTop = orderedTabs[i].PaddingTop
            };

            double w = font.GetTextExtents(tabs[i].Name).Width + 1 + 2 * padding;
            maxWidth = Math.Max(w, maxWidth);
        }

        tabBoundsL.fixedWidth = Math.Max(tabBoundsL.fixedWidth, maxWidth);
        tabBoundsL.fixedX = -tabBoundsL.fixedWidth;

        if (_creativeInvDialog != null)
        {
            _creativeInvDialog.Dispose();
        }

        GuiTab[] tabsL = tabs, tabsR = null;

        if (tabs.Length > 16)
        {
            tabsL = tabs.Take(16).ToArray();
            tabsR = tabs.Skip(16).ToArray();
        }

        _creativeInvDialog =
            capi.Gui
            .CreateCompo("inventory-creative", dialogBounds)
            .AddShadedDialogBG(ElementBounds.Fill)
            .AddDialogTitleBar(Lang.Get("Creative Inventory"), CloseIconPressed)
            .AddVerticalTabs(tabsL, tabBoundsL, OnTabClicked, "verticalTabs")
        ;

        if (tabsR != null) _creativeInvDialog.AddVerticalTabs(tabsR, tabBoundsR, (index, tab) => OnTabClicked(index + 16, tabs[index + 16]), "verticalTabsR");

        _creativeInvDialog
            .AddInset(insetBounds, 3, 0.85f)
            .BeginClip(_creativeClippingBounds)
                .AddItemSlotGrid(_creativeInv, SendInvPacket, _columnsNumber, fullGridBounds, "slotgrid")
            .EndClip()
            .AddVerticalScrollbar(OnNewScrollbarvalue, scrollbarBounds, "scrollbar")
            .AddTextInput(textInputBounds, OnTextChanged, null, "searchbox")
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right), rightTextBounds, "searchResults")
        ;

        if (tabsR != null) _creativeInvDialog.GetVerticalTab("verticalTabsR").Right = true;

        _creativeInvDialog.Compose();

        _creativeInvDialog.UnfocusOwnElements();

        _creativeInvDialog.GetScrollbar("scrollbar").SetHeights(
            (float)(slotGridBounds.fixedHeight),
            (float)(fullGridBounds.fixedHeight + pad)
        );

        //creativeInvDialog.GetTextInput("searchbox").DeleteOnRefocusBackSpace = true;
        _creativeInvDialog.GetTextInput("searchbox").SetPlaceHolderText(Lang.Get("Search..."));
        _creativeInvDialog.GetVerticalTab(currentGuiTabIndex < 16 ? "verticalTabs" : "verticalTabsR").SetValue(currentGuiTabIndex < 16 ? currentGuiTabIndex : currentGuiTabIndex - 16, false);
        _creativeInv.SetTab(_currentTabIndex);

        Update();
    }

    private void Update()
    {
        OnTextChanged(_creativeInvDialog.GetTextInput("searchbox").GetText());
    }

    private void OnTabClicked(int index, GuiTab tab)
    {
        _currentTabIndex = tab.DataInt;
        _creativeInv.SetTab(tab.DataInt);
        _creativeInvDialog.GetSlotGrid("slotgrid").DetermineAvailableSlots();

        GuiElementItemSlotGrid slotgrid = _creativeInvDialog.GetSlotGrid("slotgrid");
        int rows = (int)Math.Ceiling(slotgrid.renderedSlots.Count / (float)_columnsNumber);
        ElementBounds bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, _columnsNumber, rows);
        slotgrid.Bounds.fixedHeight = bounds.fixedHeight;

        Update();
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

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            ElementBounds bounds = _creativeInvDialog.GetSlotGrid("slotgrid").Bounds;
            bounds.fixedY = 10 - GuiElementItemSlotGrid.unscaledSlotPadding - value;

            bounds.CalcWorldBounds();
        }
    }

    private void OnTextChanged(string text)
    {
        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            GuiElementItemSlotGrid slotgrid = _creativeInvDialog.GetSlotGrid("slotgrid");

            slotgrid.FilterItemsBySearchText(text, _creativeInv.CurrentTab.SearchCache, _creativeInv.CurrentTab.SearchCacheNames);

            int rows = (int)Math.Ceiling(slotgrid.renderedSlots.Count / (float)_columnsNumber);

            ElementBounds fullGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, _columnsNumber, rows);
            _creativeInvDialog.GetScrollbar("scrollbar").SetNewTotalHeight((float)(fullGridBounds.fixedHeight + 3));
            _creativeInvDialog.GetScrollbar("scrollbar").SetScrollbarPosition(0);

            _creativeInvDialog.GetDynamicText("searchResults").SetNewText(Lang.Get("creative-searchresults", slotgrid.renderedSlots.Count));
        }
    }

    private void OnPlayerModeChanged()
    {
        // These 2 lines were flipped, causing unecessary lag
        if (!IsOpened()) return;

        // Only recompose if the mode actually changed
        if (_previousGameMode != capi.World.Player.WorldData.CurrentGameMode)
        {
            Composers.Remove("maininventory");
            ComposeGui(false);
            _previousGameMode = capi.World.Player.WorldData.CurrentGameMode;

            if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                capi.Network.SendPacketClient((Packet_Client)_creativeInv.Open(capi.World.Player));
            }

            if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
            {
                capi.Network.SendPacketClient((Packet_Client)_creativeInv.Close(capi.World.Player));
            }
        }
    }
}
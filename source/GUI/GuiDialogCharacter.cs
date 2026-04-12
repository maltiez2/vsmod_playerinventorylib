using OpenTK.Graphics.OpenGL;
using PlayerInventoryLib.Integration;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace PlayerInventoryLib.GUI;

public class CustomGuiDialogCharacter : GuiDialogCharacter
{
    public CustomGuiDialogCharacter(ICoreClientAPI capi) : base(capi)
    {
        clientApi = capi;
        rendertabhandlers.Clear();
        rendertabhandlers.Add(ComposeCharacterTab);
    }


    //public List<Action<GuiComposer>> rendertabhandlers = new();

    public override event Action? ComposeExtraGuis;
    public override event Action<int>? TabClicked;

    /*public override string ToggleKeyCombinationCode => "characterdialog";
    public override float ZSize => RuntimeEnv.GUIScale * 400;
    public override List<GuiTab> Tabs => tabs;
    public override List<Action<GuiComposer>> RenderTabHandlers => rendertabhandlers;
    public override bool PrefersUngrabbedMouse => false;*/


    public ElementBounds? CharacterTabScrollAreaBounds { get; set; }
    public ElementBounds? DialogBounds { get; set; }
    public ElementBounds? BackgroundBounds { get; set; }
    public ElementBounds? CharacterTabClipBounds { get; set; }


    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Handled) return;

        GuiComposer[] composers = Composers.ToArray();    // Minimise risk of co-modification exception while iterating below [Github #4804]
        foreach (GuiComposer composer in composers)
        {
            composer.OnMouseDown(args);
            if (args.Handled)
            {
                return;
            }
        }

        if (!IsOpened()) return;
        foreach (GuiComposer composer in composers)
        {
            if (composer.Bounds.PointInside(args.X, args.Y))
            {
                args.Handled = true;
                return;
            }
        }

        rotateCharacter = insetSlotBounds.PointInside(args.X, args.Y);
    }
    public override void OnMouseUp(MouseEvent args)
    {
        if (args.Handled) return;

        GuiComposer[] composers = Composers.ToArray();    // Minimise risk of co-modification exception while iterating below [Github #4804]
        foreach (GuiComposer composer in composers)
        {
            composer.OnMouseUp(args);
            if (args.Handled) return;
        }

        foreach (GuiComposer composer in composers)
        {
            if (composer.Bounds.PointInside(args.X, args.Y))
            {
                args.Handled = true;
                return;
            }
        }

        rotateCharacter = false;
    }
    public override void OnMouseMove(MouseEvent args)
    {
        if (args.Handled) return;

        GuiComposer[] composers = Composers.ToArray();    // Minimise risk of co-modification exception while iterating below [Github #4804]
        foreach (GuiComposer composer in composers)
        {
            composer.OnMouseMove(args);
            if (args.Handled) return;
        }

        foreach (GuiComposer composer in composers)
        {
            if (composer.Bounds.PointInside(args.X, args.Y))
            {
                args.Handled = true;
                break;
            }
        }

        if (rotateCharacter) yaw -= args.DeltaX / 100f;
    }
    public override void OnRenderGUI(float deltaTime)
    {
        foreach (KeyValuePair<string, GuiComposer> val in Composers)
        {
            val.Value.Render(deltaTime);

            MouseOverCursor = val.Value.MouseOverCursor;
        }

        if (curTab == 0)
        {
            bool useScissor = CharacterTabClipBounds != null;
            if (useScissor)
            {
                ElementBounds cb = CharacterTabClipBounds;
                int fbWidth = capi.Render.FrameWidth;
                int fbHeight = capi.Render.FrameHeight;

                int scissorX = (int)cb.renderX;
                int scissorY = (int)(fbHeight - cb.renderY - cb.InnerHeight);
                int scissorW = (int)cb.InnerWidth;
                int scissorH = (int)cb.InnerHeight;

                GL.Enable(EnableCap.ScissorTest);
                GL.Scissor(scissorX, scissorY, scissorW, scissorH);
            }

            capi.Render.GlPushMatrix();

            if (focused) { capi.Render.GlTranslate(0, 0, 150); }

            double pad = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);

            capi.Render.GlRotate(-14, 1, 0, 0);
            mat.Identity();
            mat.RotateXDeg(-14);
            Vec4f lightRot = mat.TransformVector(lighPos);

            capi.Render.CurrentActiveShader.Uniform("lightPosition", lightRot.X, lightRot.Y, lightRot.Z);

            capi.Render.RenderEntityToGui(
                deltaTime,
                capi.World.Player.Entity,
                insetSlotBounds.renderX + pad - GuiElement.scaled(20 + 21),
                insetSlotBounds.renderY + pad - GuiElement.scaled(30),
                GuiElement.scaled(250),
                yaw,
                (float)GuiElement.scaled(135),
                ColorUtil.WhiteArgb);

            capi.Render.GlPopMatrix();

            capi.Render.CurrentActiveShader.Uniform("lightPosition", GameMath.ONEOVERROOT2, -GameMath.ONEOVERROOT2, 0f);

            if (!insetSlotBounds.PointInside(capi.Input.MouseX, capi.Input.MouseY) && !rotateCharacter)
            {
                yaw += (float)(Math.Sin(capi.World.ElapsedMilliseconds / 1000f) / 200.0);
            }

            if (useScissor)
            {
                GL.Disable(EnableCap.ScissorTest);
            }
        }

        HighlightSlots();
    }
    public override void OnGuiOpened()
    {
        ComposeGuis();

        if (capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Guest || capi.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival)
        {
            if (characterInv != null) characterInv.Open(capi.World.Player);
        }
    }
    public override void OnGuiClosed()
    {
        if (characterInv != null)
        {
            characterInv.Close(capi.World.Player);
            Composers["playercharacter"].GetSlotGrid("leftSlots")?.OnGuiClosed(capi);
            Composers["playercharacter"].GetSlotGrid("rightSlots")?.OnGuiClosed(capi);
        }

        curTab = 0;
    }



    /*protected IInventory? characterInv;
    protected ElementBounds? insetSlotBounds;
    protected float yaw = -GameMath.PIHALF + 0.3f;
    protected bool rotateCharacter;
    protected bool showArmorSlots = true;
    protected ICoreClientAPI clientApi;
    protected int curTab = 0;
    protected Vec4f lighPos = new Vec4f(-1, -1, 0, 0).NormalizeXYZ();
    protected Matrixf mat = new();
    protected int lastItemIdSelected = 0;
    protected Size2d mainTabInnerSize = new();
    protected List<GuiTab> tabs = [
        new GuiTab() { Name = Lang.Get("charactertab-character"), DataInt = 0 }
    ];*/


    protected virtual void RegisterArmorIcons()
    {
        capi.Gui.Icons.CustomIcons["armorhead"] = capi.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-helmet.svg"));
        capi.Gui.Icons.CustomIcons["armorbody"] = capi.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-body.svg"));
        capi.Gui.Icons.CustomIcons["armorlegs"] = capi.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-legs.svg"));
    }
    protected virtual void ComposeCharacterTab(GuiComposer compo)
    {
        if (!capi.Gui.Icons.CustomIcons.ContainsKey("armorhead"))
        {
            RegisterArmorIcons();
        }
        double padding = GuiElementItemSlotGridBase.unscaledSlotPadding;
        double slotSize = 48;
        double outerPaddding = 20;
        double playerShapeWidth = 190;
        double verticalPadding = outerPaddding + padding;
        double internalPadding = 4;
        double bottomMiddleSectionInternalPadding = 2;
        double textHeight = 16;
        double dialogPadding = 4;
        int totalHeight = 400;

        int outerColumnSlotsNumber = 6;
        int clothesColumnSlotsNumber = 6;
        int slotsInRowNumber = 6;
        int slotsRowsNumber = 1;

        CairoFont groupFont = CairoFont.WhiteSmallText();

        ElementBounds leftSlotsColumnBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, internalPadding, verticalPadding, 1, outerColumnSlotsNumber).FixedGrow(0, padding);
        ElementBounds rightSlotsColumnBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, verticalPadding, 1, outerColumnSlotsNumber).FixedGrow(0, padding);
        ElementBounds leftClothesSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, clothesColumnSlotsNumber).FixedGrow(0, padding);
        ElementBounds rightClothesSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, clothesColumnSlotsNumber).FixedGrow(0, padding);
        ElementBounds topMiddleSectionBounds = ElementBounds.Fixed(0, verticalPadding, padding + (padding + slotSize) * slotsInRowNumber, padding + (padding + slotSize) * clothesColumnSlotsNumber);
        ElementBounds middleGroupSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, bottomMiddleSectionInternalPadding, bottomMiddleSectionInternalPadding, slotsInRowNumber, 1).FixedGrow(0, padding);
        ElementBounds middleGroupTextBounds = ElementBounds.Fixed(bottomMiddleSectionInternalPadding, 2, middleGroupSlotsBounds.fixedWidth - bottomMiddleSectionInternalPadding, textHeight);
        ElementBounds middleGroupInsetBounds = ElementBounds.Fixed(0, 0, middleGroupSlotsBounds.fixedWidth + bottomMiddleSectionInternalPadding, middleGroupTextBounds.fixedHeight + middleGroupSlotsBounds.fixedHeight + bottomMiddleSectionInternalPadding).FixedGrow(0, padding);
        ElementBounds bottomMiddleSectionBounds = ElementBounds.Fixed(0, 0, middleGroupSlotsBounds.fixedWidth + bottomMiddleSectionInternalPadding * 2, bottomMiddleSectionInternalPadding + (middleGroupInsetBounds.fixedHeight + bottomMiddleSectionInternalPadding) * slotsRowsNumber);
        insetSlotBounds = ElementBounds.Fixed(0, 0, topMiddleSectionBounds.fixedWidth - leftClothesSlotsBounds.fixedWidth - rightClothesSlotsBounds.fixedWidth - 2 * internalPadding, padding + (padding + slotSize) * clothesColumnSlotsNumber);

        CharacterTabScrollAreaBounds = ElementBounds.Fixed(2, verticalPadding + dialogPadding + 6, leftSlotsColumnBounds.fixedWidth + rightSlotsColumnBounds.fixedWidth + topMiddleSectionBounds.fixedWidth + internalPadding * 4, totalHeight);
        ElementBounds scrollbarBounds = CharacterTabScrollAreaBounds.CopyOffsetedSibling(CharacterTabScrollAreaBounds.fixedWidth, 0, 0, 0).WithFixedWidth(20);
        CharacterTabClipBounds = CharacterTabScrollAreaBounds.FlatCopy().WithFixedOffset(0, 0);

        CharacterTabScrollAreaBounds.WithParent(DialogBounds);
        scrollbarBounds.WithParent(DialogBounds);
        CharacterTabClipBounds.WithParent(DialogBounds);
        leftSlotsColumnBounds.WithParent(CharacterTabScrollAreaBounds);
        rightSlotsColumnBounds.WithParent(CharacterTabScrollAreaBounds);
        topMiddleSectionBounds.WithParent(CharacterTabScrollAreaBounds);
        bottomMiddleSectionBounds.WithParent(CharacterTabScrollAreaBounds);

        middleGroupInsetBounds.WithParent(bottomMiddleSectionBounds);
        middleGroupSlotsBounds.WithParent(middleGroupInsetBounds);
        middleGroupTextBounds.WithParent(middleGroupInsetBounds);
        rightClothesSlotsBounds.WithParent(topMiddleSectionBounds);
        leftClothesSlotsBounds.WithParent(topMiddleSectionBounds);
        insetSlotBounds.WithParent(topMiddleSectionBounds);


        topMiddleSectionBounds.FixedRightOf(leftSlotsColumnBounds, internalPadding);
        insetSlotBounds.FixedRightOf(leftClothesSlotsBounds, internalPadding);
        rightClothesSlotsBounds.FixedRightOf(insetSlotBounds, internalPadding);
        rightSlotsColumnBounds.FixedRightOf(topMiddleSectionBounds, internalPadding);
        bottomMiddleSectionBounds.FixedRightOf(leftSlotsColumnBounds, internalPadding).FixedUnder(topMiddleSectionBounds, internalPadding);
        middleGroupSlotsBounds.FixedUnder(middleGroupTextBounds, bottomMiddleSectionInternalPadding * 2);

        compo.BeginClip(CharacterTabClipBounds);
        compo.BeginChildElements(CharacterTabScrollAreaBounds);

        compo.AddItemSlotGrid(characterInv, SendInvPacket, 1, [0, 1, 2, 11, 3, 4], leftSlotsColumnBounds, "leftSlots_");
        compo.AddItemSlotGrid(characterInv, SendInvPacket, 1, [0, 1, 2, 11, 3, 4], leftClothesSlotsBounds, "leftSlots");
        compo.AddItemSlotGrid(characterInv, SendInvPacket, 1, [6, 7, 8, 10, 5, 9], rightClothesSlotsBounds, "rightSlots");
        compo.AddItemSlotGrid(characterInv, SendInvPacket, 1, [6, 7, 8, 10, 5, 9], rightSlotsColumnBounds, "rightSlots_");
        compo.AddItemSlotGrid(characterInv, SendInvPacket, slotsInRowNumber, [6, 7, 8, 10, 5, 9], middleGroupSlotsBounds, "middleSlots");
        compo.AddRichtext("Backpack", groupFont, middleGroupTextBounds, "groupText");
        compo.AddScrollableInset(insetSlotBounds, 0, 0.8f);
        compo.AddScrollableInset(middleGroupInsetBounds, 3, 0.8f);
        compo.AddScrollableInset(bottomMiddleSectionBounds, 0, 1);
        compo.AddScrollableInset(topMiddleSectionBounds, 0, 1);
        //compo.AddInset(CharacterTabClipBounds, 4, 0.8f);

        compo.EndChildElements();
        compo.EndClip();
        compo.AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar");

        compo.GetScrollbar("scrollbar").SetHeights(
            totalHeight,
            totalHeight + totalHeight
        );
        compo.GetScrollbar("scrollbar").SetScrollbarPosition(0);
    }

    protected virtual void OnNewScrollbarValue(float value)
    {
        if (CharacterTabScrollAreaBounds != null)
        {
            CharacterTabScrollAreaBounds.fixedY = 13 - value;
            CharacterTabScrollAreaBounds.CalcWorldBounds();
        }
    }


    protected override void ComposeGuis()
    {
        try
        {

            characterInv = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);

            BackgroundBounds = ElementBounds.Fill.WithFixedPadding(2);

            if (curTab == 0)
            {
                BackgroundBounds.BothSizing = ElementSizing.FitToChildren;
            }
            else
            {
                BackgroundBounds.BothSizing = ElementSizing.Fixed;
                BackgroundBounds.fixedWidth = mainTabInnerSize.Width;
                BackgroundBounds.fixedHeight = mainTabInnerSize.Height;
            }


            DialogBounds = ElementStdBounds
                .AutosizedMainDialog.WithAlignment(EnumDialogArea.LeftMiddle)
                .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 0);

            string charClass = capi.World.Player.Entity.WatchedAttributes.GetString("characterClass");
            string title = Lang.Get("characterdialog-title-nameandclass", capi.World.Player.PlayerName, Lang.Get("characterclass-" + charClass));

            if (!Lang.HasTranslation("characterclass-" + charClass))
            {
                title = capi.World.Player.PlayerName;
            }

            ElementBounds tabBounds = ElementBounds.Fixed(5, -24, 350, 25);

        
            ClearComposers();
            Composers["playercharacter"] = capi.Gui
                .CreateCompo("playercharacter", DialogBounds)
                .AddShadedDialogBG(BackgroundBounds, true)
                .AddDialogTitleBar(title, OnTitleBarClose)
                .AddHorizontalTabs(tabs.ToArray(), tabBounds, OnTabClicked, CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold), CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold), "tabs")
                .BeginChildElements(BackgroundBounds)
            ;

            Composers["playercharacter"].GetHorizontalTabs("tabs").activeElement = curTab;

            rendertabhandlers[curTab](Composers["playercharacter"]);

            Composers["playercharacter"]
                .EndChildElements()
                .Compose();

            if (ComposeExtraGuis != null)
            {
                ComposeExtraGuis();
            }

            if (curTab == 0)
            {
                mainTabInnerSize.Width = BackgroundBounds.InnerWidth / RuntimeEnv.GUIScale;
                mainTabInnerSize.Height = BackgroundBounds.InnerHeight / RuntimeEnv.GUIScale;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }
    protected virtual void HighlightSlots()
    {
        ItemSlot mouseSlot = clientApi.World.Player.InventoryManager.MouseItemSlot;
        ItemSlot targetSlot = mouseSlot.Empty ? clientApi.World.Player.InventoryManager.CurrentHoveredSlot ?? mouseSlot : mouseSlot;
        int currentItem = targetSlot?.Itemstack?.Item?.ItemId ?? 0;
        if (currentItem == lastItemIdSelected) return;
        lastItemIdSelected = currentItem;

        foreach (ItemSlot slot in characterInv)
        {
            if (slot.CanHold(targetSlot))
            {
                slot.HexBackgroundColor = "#5fbed4";
            }
            else
            {
                slot.HexBackgroundColor = null;
            }
        }
    }

    protected void OnTabClicked(int tabindex)
    {
        TabClicked?.Invoke(tabindex);
        curTab = tabindex;
        ComposeGuis();
    }
    protected void SendInvPacket(object packet)
    {
        capi.Network.SendPacketClient(packet);
    }
}

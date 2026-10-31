using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    public enum CreatorTab
    {
        Personality,
        Ideoligion,
        Appearance,
        Genes,
        Health,
        Clothing,
        Inventory,
        Relationships,
        Animal
    }

    /// <summary>
    /// Replaces the vanilla "Create characters" page (via the scenario part's pageClass). If the player chose
    /// Normal colony creation it is exactly the vanilla page; otherwise it draws the full-screen creator.
    /// </summary>
    public partial class Page_CreateAColonist : Page_ConfigureStartingPawns
    {
        private bool creator;
        private CreatorState state;
        private Pawn selected;
        private CreatorTab tab = CreatorTab.Personality;
        private Rot4 rotation = Rot4.South;
        private bool showClothes = true;
        private bool showHeadgear = true;
        private Vector2 panelScroll;
        private float panelViewHeight;

        private const float TopBarHeight = 44f;
        private const float BottomBarHeight = 132f;
        private const float IconBarWidth = 68f;

        private static List<Pawn> Group => Find.GameInitData.startingAndOptionalPawns;

        private static List<Pawn> Colonists => Group.Take(Find.GameInitData.startingPawnCount).ToList();

        private bool SelectedIsPet => selected != null && !selected.RaceProps.Humanlike;

        public override string PageTitle => creator ? (string)"CAC_PageTitle".Translate() : base.PageTitle;

        public override Vector2 InitialSize => creator ? new Vector2(UI.screenWidth, UI.screenHeight) : base.InitialSize;

        protected override float Margin => creator ? 10f : base.Margin;

        public override void PreOpen()
        {
            creator = CreatorMode.Active && !TutorSystem.TutorialMode;
            if (creator)
            {
                drawShadow = false;
                closeOnAccept = false;
                closeOnCancel = false;
            }
            base.PreOpen();
            if (creator)
            {
                state = CreatorState.Get();
                EnsureSelection();
            }
        }

        public override void DoWindowContents(Rect rect)
        {
            if (!creator)
            {
                base.DoWindowContents(rect);
                DevVanillaPage();
                return;
            }
            state = CreatorState.Get();
            EnsureSelection();
            UndoInput();
            if (Event.current.type == EventType.Layout)
            {
                foreach (Pawn colonist in Colonists)
                {
                    PawnEditor.TakePossessionsIntoInventory(colonist);
                }
            }
            Text.Font = GameFont.Small;

            Rect top = new Rect(rect.x, rect.y, rect.width, TopBarHeight);
            Rect bottom = new Rect(rect.x, rect.yMax - BottomBarHeight, rect.width, BottomBarHeight);
            Rect middle = new Rect(rect.x, top.yMax + 4f, rect.width, bottom.y - top.yMax - 10f);
            float panelWidth = Mathf.Clamp(rect.width * 0.36f, 420f, 560f);
            Rect iconBar = new Rect(middle.x, middle.y, IconBarWidth, middle.height);
            Rect panel = new Rect(iconBar.xMax + 6f, middle.y, panelWidth, middle.height);
            Rect stage = new Rect(panel.xMax + 10f, middle.y, middle.xMax - panel.xMax - 10f, middle.height);

            DrawTopBar(top);
            DrawIconBar(iconBar);
            DrawPanel(panel);
            DrawStage(stage);
            DrawBottomBar(bottom);
            RunTour();
            RunPromo();
        }

        // Hooks for the developer test build (CAC_DEV, see DevTest.cs). In release builds they are empty
        // partial methods, so the compiler removes the calls and no test code ships.
        partial void RunTour();

        partial void RunPromo();

        partial void DevVanillaPage();

        private void EnsureSelection()
        {
            List<Pawn> colonists = Colonists;
            if (selected == null || (!colonists.Contains(selected) && !state.pets.Contains(selected)))
            {
                selected = colonists.FirstOrDefault();
            }
            if (!TabsFor(selected).Contains(tab))
            {
                tab = TabsFor(selected).First();
            }
        }

        private void Select(Pawn pawn)
        {
            if (pawn == selected)
            {
                return;
            }
            selected = pawn;
            panelScroll = Vector2.zero;
            if (!TabsFor(pawn).Contains(tab))
            {
                tab = TabsFor(pawn).First();
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        // ---------------------------------------------------------------- top bar

        private void DrawTopBar(Rect rect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(rect.x + 4f, rect.y, 400f, rect.height), PageTitle);
            }
            string pets = state.pets.Count == 1 ? "CAC_PetCountOne".Translate().ToString() : "CAC_PetCount".Translate(state.pets.Count).ToString();
            string summary = "CAC_GroupSummary".Translate(Find.Scenario.name, Find.GameInitData.startingPawnCount, state.maxColonists, pets);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(rect.x + 260f, rect.y, rect.width - 260f, rect.height), summary);
            }
        }

        // ---------------------------------------------------------------- left icon tabs

        private static IEnumerable<CreatorTab> TabsFor(Pawn pawn)
        {
            if (pawn != null && !pawn.RaceProps.Humanlike)
            {
                yield return CreatorTab.Animal;
                yield return CreatorTab.Relationships;
                yield break;
            }
            yield return CreatorTab.Personality;
            if (PawnEditor.CanHaveIdeo(pawn))
            {
                yield return CreatorTab.Ideoligion;
            }
            yield return CreatorTab.Appearance;
            if (ModsConfig.BiotechActive && pawn?.genes != null)
            {
                yield return CreatorTab.Genes;
            }
            yield return CreatorTab.Health;
            yield return CreatorTab.Clothing;
            yield return CreatorTab.Inventory;
            yield return CreatorTab.Relationships;
        }

        private static Texture2D TabIcon(CreatorTab t)
        {
            switch (t)
            {
                case CreatorTab.Personality: return CACTex.TabPersonality;
                case CreatorTab.Ideoligion: return CACTex.TabIdeoligion;
                case CreatorTab.Appearance: return CACTex.TabAppearance;
                case CreatorTab.Genes: return CACTex.TabGenes;
                case CreatorTab.Health: return CACTex.TabHealth;
                case CreatorTab.Clothing: return CACTex.TabClothing;
                case CreatorTab.Inventory: return CACTex.TabInventory;
                case CreatorTab.Relationships: return CACTex.TabRelationships;
                default: return CACTex.TabAnimal;
            }
        }

        private static string TabLabel(CreatorTab t)
        {
            return ("CAC_Tab_" + t).Translate();
        }

        private void DrawIconBar(Rect rect)
        {
            List<CreatorTab> tabs = TabsFor(selected).ToList();
            // Shrink the icons on short screens so every tab still fits.
            float size = Mathf.Min(rect.width, (rect.height - 6f * (tabs.Count - 1)) / tabs.Count);
            float y = rect.y;
            foreach (CreatorTab t in tabs)
            {
                Rect b = new Rect(rect.x + (rect.width - size) / 2f, y, size, size);
                bool sel = t == tab;
                Widgets.DrawOptionBackground(b, sel);
                GUI.color = sel ? Color.white : new Color(0.78f, 0.78f, 0.78f);
                GUI.DrawTexture(b.ContractedBy(size * 0.1f), TabIcon(t), ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                if (lockedTabs.Contains(t))
                {
                    GUI.DrawTexture(new Rect(b.xMax - 18f, b.y + 2f, 16f, 16f), CACTex.Lock);
                }
                if (Mouse.IsOver(b))
                {
                    TooltipHandler.TipRegion(b, TabLabel(t) + "\n\n" + ("CAC_TabDesc_" + t).Translate());
                }
                if (Widgets.ButtonInvisible(b) && !sel)
                {
                    tab = t;
                    panelScroll = Vector2.zero;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                y += b.height + 6f;
            }
        }

        // ---------------------------------------------------------------- tab panel

        private void DrawPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            using (new TextBlock(GameFont.Medium, TextAnchor.UpperLeft))
            {
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 34f), TabLabel(tab));
            }
            LockButton(new Rect(inner.xMax - 26f, inner.y + 4f, 24f, 24f));
            float headerH = 38f;
            Rect subRow = new Rect(inner.x, inner.y + headerH, inner.width, 28f);
            if (DrawSubTabs(subRow))
            {
                headerH += 34f;
            }
            Rect outer = new Rect(inner.x, inner.y + headerH, inner.width, inner.height - headerH);
            Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(panelViewHeight, outer.height - 4f));
            Widgets.BeginScrollView(outer, ref panelScroll, view);
            CACUI.ScrollVisible = new Rect(0f, panelScroll.y, view.width, outer.height);
            float y = 0f;
            float w = view.width;
            try
            {
                switch (tab)
                {
                    case CreatorTab.Personality: DrawPersonality(ref y, w); break;
                    case CreatorTab.Ideoligion: DrawIdeoligion(ref y, w); break;
                    case CreatorTab.Appearance: DrawAppearance(ref y, w); break;
                    case CreatorTab.Genes: DrawGenes(ref y, w); break;
                    case CreatorTab.Health: DrawHealth(ref y, w); break;
                    case CreatorTab.Clothing: DrawClothing(ref y, w); break;
                    case CreatorTab.Inventory: DrawInventory(ref y, w); break;
                    case CreatorTab.Relationships: DrawRelationships(ref y, w); break;
                    case CreatorTab.Animal: DrawAnimal(ref y, w); break;
                }
            }
            catch (System.Exception e)
            {
                // One broken entry (often from another mod's data) shouldn't take the whole creator down.
                Log.ErrorOnce("[Create a Colonist] The " + tab + " tab hit an error: " + e, 0x43AC00 + (int)tab);
            }
            finally
            {
                panelViewHeight = y + 12f;
                CACUI.ScrollVisible = null;
                Widgets.EndScrollView();
            }
        }

        /// <summary>Some tabs have a row of sub-categories under the title. Returns true if one was drawn.</summary>
        private bool DrawSubTabs(Rect row)
        {
            int before;
            switch (tab)
            {
                case CreatorTab.Appearance:
                    before = appearanceSub;
                    appearanceSub = CACUI.Pills(row, AppearanceSubLabels(), appearanceSub);
                    break;
                case CreatorTab.Inventory:
                    before = inventorySub;
                    inventorySub = CACUI.Pills(row, new[] { (string)"CAC_Carried".Translate(), (string)"CAC_ColonySupplies".Translate() }, inventorySub);
                    break;
                default:
                    return false;
            }
            if (before != (tab == CreatorTab.Appearance ? appearanceSub : inventorySub))
            {
                panelScroll = Vector2.zero;
            }
            return true;
        }

        // ---------------------------------------------------------------- centre stage

        private void DrawStage(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(12f);

            Rect nameRow = new Rect(inner.x, inner.y, inner.width, 32f);
            DrawNameRow(nameRow);
            DrawSubtitleRow(new Rect(inner.x, nameRow.yMax + 3f, inner.width, 24f));

            float controlsH = 40f;
            Rect portraitArea = new Rect(inner.x, nameRow.yMax + 30f, inner.width, inner.height - 30f - nameRow.height - controlsH - 6f);
            float h = portraitArea.height * 0.9f;
            float w = h * 0.75f;
            if (w > portraitArea.width)
            {
                w = portraitArea.width;
                h = w / 0.75f;
            }
            Rect portrait = new Rect(portraitArea.center.x - w / 2f, portraitArea.center.y - h / 2f, w, h);
            // Soft floor shadow so the colonist stands on something.
            if (!SelectedIsPet)
            {
                // Children are drawn smaller, so their feet sit higher in the portrait.
                float feet = selected.DevelopmentalStage.Baby() ? 0.58f : selected.DevelopmentalStage.Child() ? 0.62f : 0.74f;
                float spread = selected.DevelopmentalStage.Juvenile() ? 0.4f : 0.6f;
                Rect shadow = new Rect(portrait.center.x - w * spread / 2f, portrait.y + h * feet, w * spread, h * 0.1f);
                GUI.DrawTexture(shadow, CACTex.Shadow);
            }
            RenderTexture tex = PortraitsCache.Get(selected, new Vector2(w, h), rotation, default(Vector3), PortraitZoom(selected),
                supersample: true, compensateForUIScale: true, renderHeadgear: showHeadgear, renderClothes: showClothes, stylingStation: true);
            GUI.DrawTexture(portrait, tex);

            Rect controls = new Rect(inner.x, inner.yMax - controlsH, inner.width, controlsH);
            DrawStageControls(controls);
        }

        /// <summary>Animals come in very different sizes; scale them so they fill the portrait about the same.</summary>
        private static float PortraitZoom(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike)
            {
                return 1f;
            }
            float drawSize = pawn.ageTracker.CurKindLifeStage.bodyGraphicData?.drawSize.x ?? 1.5f;
            return Mathf.Clamp(1.6f / Mathf.Max(0.5f, drawSize), 0.35f, 1.6f);
        }

        internal static string StageSubtitle(Pawn pawn)
        {
            if (!pawn.RaceProps.Humanlike)
            {
                return "CAC_PetSubtitle".Translate(pawn.kindDef.LabelCap, pawn.gender.GetLabel(animal: true));
            }
            string xeno = pawn.genes != null && ModsConfig.BiotechActive ? pawn.genes.XenotypeLabelCap : null;
            string title = pawn.story.TitleCap;
            return xeno.NullOrEmpty()
                ? "CAC_ColonistSubtitle".Translate(title, pawn.gender.GetLabel()).ToString()
                : "CAC_ColonistSubtitleXeno".Translate(title, pawn.gender.GetLabel(), xeno).ToString();
        }

        private string bioAgeText, chronoAgeText;
        private Pawn ageBoxPawn;
        private int ageBoxMin;

        /// <summary>Under the name: who they are, then their biological and chronological age as small number boxes.</summary>
        private void DrawSubtitleRow(Rect row)
        {
            Pawn pawn = selected;
            Pawn_AgeTracker age = pawn.ageTracker;
            if (pawn != ageBoxPawn)
            {
                bioAgeText = chronoAgeText = null;
                ageBoxPawn = pawn;
                ageBoxMin = PawnEditor.MinAgeYears(pawn);
            }
            string subtitle = StageSubtitle(pawn) + "  ·";
            string bioLabel = "CAC_Age".Translate(), chronoLabel = "CAC_AgeChrono".Translate();
            float subtitleW = Text.CalcSize(subtitle).x + 10f;
            // Centre the whole line: subtitle, two boxes (label, gap, box, arrows) and the space between them.
            float x = row.center.x - (subtitleW + Text.CalcSize(bioLabel).x + Text.CalcSize(chronoLabel).x + 40f + 64f + 2f * 20f + 16f) / 2f;
            using (new TextBlock(TextAnchor.MiddleLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(x, row.y, subtitleW, row.height), subtitle);
            }
            x += subtitleW;
            // Biological age stops where the game stops (unless they're already older); chronological only can't be younger.
            int bio = AgeBox(ref x, row.y, bioLabel, "CAC_AgeTip".Translate(PawnEditor.MaxAgeYears(pawn)), age.AgeBiologicalYears,
                ageBoxMin, Mathf.Max(PawnEditor.MaxAgeYears(pawn), age.AgeBiologicalYears), 40f, ref bioAgeText);
            x += 16f;
            int chrono = AgeBox(ref x, row.y, chronoLabel, "CAC_AgeChronoTip".Translate(), age.AgeChronologicalYears,
                age.AgeBiologicalYears, 999999, 64f, ref chronoAgeText);
            if (bio != age.AgeBiologicalYears)
            {
                PawnEditor.SetBiologicalAge(pawn, bio);
            }
            else if (chrono != age.AgeChronologicalYears)
            {
                PawnEditor.SetChronologicalAge(pawn, chrono);
            }
        }

        /// <summary>
        /// "Label [number] up/down". A typed number applies on Enter or when the box loses focus, so passing through "5"
        /// on the way to "50" never makes them a child for a moment.
        /// </summary>
        internal static int AgeBox(ref float x, float y, string label, string tip, int value, int min, int max, float boxW, ref string text)
        {
            float labelW = Text.CalcSize(label).x;
            Rect all = new Rect(x, y, labelW + 4f + boxW + 16f, 24f);
            TooltipHandler.TipRegion(all, tip);
            using (new TextBlock(TextAnchor.MiddleLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(x, y, labelW, 24f), label);
            }
            Rect box = new Rect(x + labelW + 4f, y, boxW, 24f);
            string control = "CACAgeBox" + label;
            bool focused = GUI.GetNameOfFocusedControl() == control;
            Event e = Event.current;
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                GUI.FocusControl(null);
                e.Use();
                focused = false;
            }
            int result = value;
            if (!focused && text != null)
            {
                if (int.TryParse(text, out int typed))
                {
                    result = Mathf.Clamp(typed, min, max);
                }
                text = null;
            }
            GUI.SetNextControlName(control);
            string edited = Widgets.TextField(box, text ?? result.ToString(), 7, DigitsOnly);
            if (focused)
            {
                text = edited;
            }
            Rect up = new Rect(box.xMax + 2f, y, 14f, 12f);
            Rect down = new Rect(box.xMax + 2f, y + 12f, 14f, 12f);
            int step = Widgets.ButtonImage(up, CACTex.ArrowUp) ? 1 : Widgets.ButtonImage(down, CACTex.ArrowDown) ? -1 : 0;
            if (step != 0)
            {
                result = Mathf.Clamp(result + step, min, max);
                text = null;
                GUI.FocusControl(null);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            x = all.xMax;
            return result;
        }

        private void DrawNameRow(Rect row)
        {
            float diceSize = 30f;
            if (selected.Name is NameTriple nt)
            {
                float fieldW = Mathf.Min(170f, (row.width - diceSize - 30f - 240f) / 3f);
                float total = fieldW * 3f + 16f + diceSize + 6f;
                float x = row.center.x - total / 2f;
                string first = NameField(new Rect(x, row.y, fieldW, row.height), nt.First, "FirstName".Translate());
                string nick = NameField(new Rect(x + fieldW + 8f, row.y, fieldW, row.height), nt.Nick, "NickName".Translate());
                string last = NameField(new Rect(x + (fieldW + 8f) * 2f, row.y, fieldW, row.height), nt.Last, "LastName".Translate());
                if (first != nt.First || nick != nt.Nick || last != nt.Last)
                {
                    PawnEditor.SetName(selected, first, nick, last);
                }
                Rect dice = new Rect(x + (fieldW + 8f) * 3f, row.y + 1f, diceSize, diceSize);
                if (CACUI.IconButton(dice, CACTex.Dice, "CAC_RandomName".Translate()))
                {
                    selected.Name = PawnBioAndNameGenerator.GeneratePawnName(selected, NameStyle.Full);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }
            else
            {
                float fieldW = 220f;
                Rect field = new Rect(row.center.x - (fieldW + diceSize + 6f) / 2f, row.y, fieldW, row.height);
                string current = selected.Name?.ToStringShort ?? "";
                string edited = NameField(field, current, "CAC_Name".Translate());
                if (edited != current)
                {
                    selected.Name = new NameSingle(edited);
                }
                Rect dice = new Rect(field.xMax + 6f, row.y + 1f, diceSize, diceSize);
                if (CACUI.IconButton(dice, CACTex.Dice, "CAC_RandomName".Translate()))
                {
                    selected.Name = PawnBioAndNameGenerator.GeneratePawnName(selected);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }

            // Gallery buttons, top right of the stage.
            if (!SelectedIsPet)
            {
                Rect save = new Rect(row.xMax - 32f, row.y, 32f, 32f);
                Rect gallery = new Rect(save.x - 36f, row.y, 32f, 32f);
                if (CACUI.IconButton(save, CACTex.SaveColonist, "CAC_SaveMenuTip".Translate()))
                {
                    OpenSaveMenu();
                }
                if (CACUI.IconButton(gallery, CACTex.Gallery, "CAC_OpenGallery".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_Gallery(this, selected));
                }
            }
        }

        private static string NameField(Rect rect, string value, string placeholder)
        {
            TooltipHandler.TipRegion(rect, placeholder);
            string result = Widgets.TextField(rect, value ?? "", 16, CharacterCardUtility.ValidNameRegex);
            if (result.NullOrEmpty())
            {
                using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, CACUI.Dim))
                {
                    Widgets.Label(new Rect(rect.x + 6f, rect.y, rect.width - 6f, rect.height), placeholder);
                }
            }
            return result;
        }

        private void DrawStageControls(Rect rect)
        {
            float x = rect.x;
            Rect rotL = new Rect(x, rect.y + 4f, 32f, 32f);
            Rect rotR = new Rect(rotL.xMax + 4f, rect.y + 4f, 32f, 32f);
            if (CACUI.IconButton(rotL, CACTex.RotLeft, "CAC_RotateLeft".Translate()))
            {
                rotation.Rotate(RotationDirection.Counterclockwise);
            }
            if (CACUI.IconButton(rotR, CACTex.RotRight, "CAC_RotateRight".Translate()))
            {
                rotation.Rotate(RotationDirection.Clockwise);
            }
            Rect undo = new Rect(rotR.xMax + 14f, rect.y + 4f, 32f, 32f);
            Rect redo = new Rect(undo.xMax + 4f, rect.y + 4f, 32f, 32f);
            UndoButtons(undo, redo);
            if (!SelectedIsPet)
            {
                // Checkbox plus its label; translations can be wider than the English.
                Rect clothes = new Rect(redo.xMax + 16f, rect.y + 8f, CACUI.FitWidth("CAC_ShowApparel".Translate(), 170f, 34f), 24f);
                Widgets.CheckboxLabeled(clothes, "CAC_ShowApparel".Translate(), ref showClothes, placeCheckboxNearText: true);
                Rect headgear = new Rect(clothes.xMax + 10f, rect.y + 8f, CACUI.FitWidth("CAC_ShowHeadgear".Translate(), 170f, 34f), 24f);
                Widgets.CheckboxLabeled(headgear, "CAC_ShowHeadgear".Translate(), ref showHeadgear, placeCheckboxNearText: true);
            }
            Rect random = new Rect(rect.xMax - 190f, rect.y + 2f, 190f, 36f);
            if (Widgets.ButtonText(random, "   " + (SelectedIsPet ? "CAC_RandomPet" : "CAC_RandomColonist").Translate()))
            {
                RandomizeSelected();
            }
            GUI.DrawTexture(new Rect(random.x + 8f, random.y + 6f, 24f, 24f), CACTex.Dice);
        }

        private void RandomizeSelected()
        {
            if (SelectedIsPet)
            {
                PawnKindDef kind = CreatorState.AnimalKinds(petsOnly: true).RandomElement();
                Pawn oldPet = selected;
                selected = state.ChangePetSpecies(selected, kind);
                selected.Name = PawnBioAndNameGenerator.GeneratePawnName(selected);
                ReplaceInUndo(oldPet, selected);
                return;
            }
            int index = PawnEditor.StartingIndex(selected);
            if (index < 0)
            {
                return;
            }
            Pawn old = selected;
            ColonistPreset kept = lockedTabs.Count > 0 ? ColonistPreset.From(old, null, withFamily: false) : null;
            state.Notify_ColonistRemoved(selected);
            StartingPawnUtility.RandomizePawn(index);
            selected = Group[index];
            if (kept != null)
            {
                KeepLocked(kept, selected);
            }
            ReplaceInUndo(old, selected);
            state.RememberColonists();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // ---------------------------------------------------------------- bottom bar: back, group strip, play

        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width, CACUI.SectionLine);
            Rect back = new Rect(rect.x, rect.center.y - 19f, 150f, 38f);
            if (Widgets.ButtonText(back, "Back".Translate()) && CanDoBack())
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CAC_ConfirmBack".Translate(), DoBack, destructive: true));
            }

            Rect play = new Rect(rect.xMax - 210f, rect.center.y - 32f, 210f, 64f);
            if (DrawPlayButton(play) && CanDoNext())
            {
                DoNext();
            }
            if (NeedsGrownUp)
            {
                using (new TextBlock(GameFont.Tiny, TextAnchor.LowerCenter, ColorLibrary.RedReadable))
                {
                    Widgets.Label(new Rect(play.x, rect.y - 2f, play.width, play.y - rect.y), "CAC_NeedsGrownUp".Translate(Group[0].ageTracker.AdultMinAge));
                }
            }

            Rect strip = new Rect(back.xMax + 20f, rect.y + 8f, play.x - back.xMax - 40f, rect.height - 12f);
            DrawGroupStrip(strip);
        }

        private static bool DrawPlayButton(Rect rect)
        {
            Widgets.DrawOptionBackground(rect, false);
            MouseoverSounds.DoRegion(rect);
            Rect icon = new Rect(rect.x + 14f, rect.center.y - 20f, 40f, 40f);
            GUI.DrawTexture(icon, CACTex.Play);
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(icon.xMax + 12f, rect.y, rect.xMax - icon.xMax - 12f, rect.height), "CAC_Play".Translate());
            }
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, "CAC_PlayTip".Translate());
            }
            return Widgets.ButtonInvisible(rect);
        }

        private void DrawGroupStrip(Rect rect)
        {
            List<Pawn> colonists = Colonists;
            List<Pawn> pets = state.pets;
            int boxes = colonists.Count + pets.Count;
            float gap = 6f;
            float petGap = pets.Any() ? 18f : 0f;
            float plusW = 64f;
            float boxW = Mathf.Min(92f, (rect.width - plusW - petGap - gap * (boxes + 1)) / Mathf.Max(1, boxes));
            float x = rect.x;
            foreach (Pawn p in colonists)
            {
                DrawStripBox(new Rect(x, rect.y, boxW, rect.height), p, canRemove: colonists.Count > 1);
                x += boxW + gap;
            }
            if (pets.Any())
            {
                x += petGap - gap;
                Widgets.DrawLineVertical(x - petGap / 2f, rect.y + 10f, rect.height - 20f);
                foreach (Pawn p in pets.ToList())
                {
                    DrawStripBox(new Rect(x, rect.y, boxW, rect.height), p, canRemove: true);
                    x += boxW + gap;
                }
            }
            Rect plus = new Rect(x, rect.y + 14f, plusW, rect.height - 28f);
            Widgets.DrawOptionBackground(plus, false);
            GUI.DrawTexture(new Rect(plus.center.x - 16f, plus.center.y - 16f, 32f, 32f), CACTex.Plus);
            TooltipHandler.TipRegion(plus, "CAC_AddToGroup".Translate());
            if (Widgets.ButtonInvisible(plus))
            {
                OpenAddMenu();
            }
        }

        private void DrawStripBox(Rect box, Pawn pawn, bool canRemove)
        {
            bool sel = pawn == selected;
            Widgets.DrawOptionBackground(box, sel);
            Rect port = new Rect(box.x + 2f, box.y + 2f, box.width - 4f, box.height - 24f);
            GUI.DrawTexture(port, PortraitsCache.Get(pawn, port.size, Rot4.South, default(Vector3), PortraitZoom(pawn), supersample: true, compensateForUIScale: true, renderHeadgear: true, renderClothes: true));
            string name = pawn.Name is NameTriple nt ? (nt.Nick.NullOrEmpty() ? nt.First : nt.Nick) : pawn.LabelShortCap;
            using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter))
            {
                Widgets.Label(new Rect(box.x, box.yMax - 22f, box.width, 20f), name.Truncate(box.width - 4f));
            }
            if (!pawn.RaceProps.Humanlike)
            {
                GUI.DrawTexture(new Rect(box.x + 4f, box.y + 4f, 16f, 16f), CACTex.Pet);
            }
            if (Mouse.IsOver(box))
            {
                TooltipHandler.TipRegion(box, pawn.LabelCap + "\n" + StageSubtitle(pawn) + "\n"
                    + "CAC_AgeValue".Translate(pawn.ageTracker.AgeBiologicalYears, pawn.ageTracker.AgeChronologicalYears));
                if (canRemove)
                {
                    Rect x = new Rect(box.xMax - 20f, box.y + 2f, 18f, 18f);
                    if (Widgets.ButtonImage(x, CACTex.Dismiss))
                    {
                        ConfirmRemove(pawn);
                        return;
                    }
                }
            }
            if (Widgets.ButtonInvisible(box, doMouseoverSound: false))
            {
                Select(pawn);
            }
        }

        private void OpenAddMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            bool full = Find.GameInitData.startingPawnCount >= state.maxColonists;
            options.Add(new FloatMenuOption(full ? "CAC_AddColonistFull".Translate(state.maxColonists).ToString() : "CAC_AddColonist".Translate().ToString(),
                full ? null : (System.Action)(() => AddColonist()), CACTex.Colonist, Color.white));
            if (ModsConfig.BiotechActive)
            {
                options.Add(new FloatMenuOption(full ? "CAC_AddFromGeneticsFull".Translate().ToString() : "CAC_AddFromGenetics".Translate().ToString(),
                    full ? null : (System.Action)(() => Find.WindowStack.Add(new Dialog_Genetics(this))), CACTex.TabGenes, Color.white));
            }
            options.Add(new FloatMenuOption("CAC_AddPet".Translate(), OpenPetSpeciesPicker, CACTex.Pet, Color.white));
            options.Add(new FloatMenuOption(full ? "CAC_AddFromGalleryFull".Translate().ToString() : "CAC_AddFromGallery".Translate().ToString(),
                full ? null : (System.Action)(() => Find.WindowStack.Add(new Dialog_Gallery(this, null))), CACTex.Gallery, Color.white));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public Pawn AddColonist()
        {
            GameInitData data = Find.GameInitData;
            if (data.startingPawnCount >= state.maxColonists)
            {
                return null;
            }
            // Use one of the vanilla "left behind" candidates if there is one, otherwise generate a new pawn.
            if (data.startingAndOptionalPawns.Count <= data.startingPawnCount)
            {
                StartingPawnUtility.AddNewPawn(data.startingAndOptionalPawns.Count);
            }
            data.startingPawnCount++;
            state.RememberColonists();
            Pawn added = data.startingAndOptionalPawns[data.startingPawnCount - 1];
            Select(added);
            return added;
        }

        /// <summary>A colonist made in the genetics window joins the group, with the family it was made for.</summary>
        public void AddRelative(Pawn pawn, Kin kin)
        {
            GameInitData data = Find.GameInitData;
            // The game's list of requests runs alongside the pawns, and is often still empty.
            StartingPawnUtility.ReorderRequests(Group.Count, data.startingPawnCount);
            Group.Insert(data.startingPawnCount, pawn);
            data.startingPawnCount++;
            StartingPawnUtility.GeneratePossessions(pawn);
            kin.LinkTo(pawn);
            Notify_PresetApplied(pawn);
        }

        private void ConfirmRemove(Pawn pawn)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CAC_ConfirmRemove".Translate(pawn.LabelShortCap), () => Remove(pawn), destructive: true));
        }

        private void Remove(Pawn pawn)
        {
            ForgetUndo(pawn);
            if (!pawn.RaceProps.Humanlike)
            {
                state.RemovePet(pawn);
            }
            else
            {
                GameInitData data = Find.GameInitData;
                int index = data.startingAndOptionalPawns.IndexOf(pawn);
                if (index < 0 || data.startingPawnCount <= 1)
                {
                    return;
                }
                state.Notify_ColonistRemoved(pawn);
                foreach (Pawn other in Colonists)
                {
                    if (other != pawn)
                    {
                        PawnEditor.ClearRelationsBetween(other, pawn);
                    }
                }
                // Its generation request goes too, or the next added colonist would be generated from it
                // (forced xenotype and all).
                PawnEditor.RemoveGenerationRequest(index);
                data.startingAndOptionalPawns.RemoveAt(index);
                data.startingPawnCount--;
                data.startingPossessions.Remove(pawn);
                PawnComponentsUtility.RemoveComponentsOnDespawned(pawn);
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
            }
            if (selected == pawn)
            {
                selected = null;
                EnsureSelection();
            }
        }

        // ---------------------------------------------------------------- start the game

        // Not when the scenario forbids adults: that colony could never start.
        private bool NeedsGrownUp
        {
            get
            {
                if (!creator || !ModsConfig.BiotechActive || !Find.GameInitData.allowedDevelopmentalStages.Has(DevelopmentalStage.Adult))
                {
                    return false;
                }
                for (int i = 0; i < Find.GameInitData.startingPawnCount; i++)
                {
                    if (Group[i].DevelopmentalStage.Adult())
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        protected override bool CanDoNext()
        {
            if (NeedsGrownUp)
            {
                Messages.Message("CAC_NeedsGrownUp".Translate(Group[0].ageTracker.AdultMinAge), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            return base.CanDoNext();
        }

        protected override void DoNext()
        {
            if (creator)
            {
                // Ideoligions and carry weight are settled later, when the colony is generated (ScenPart_CreatorPets):
                // after this the game may still ask "nobody can do X, continue?", and the player can say no.
                state.ApplyToScenario();
            }
            base.DoNext();
        }

        protected override void DoBack()
        {
            if (creator)
            {
                PawnEditor.ForgetSavedIdeos();
            }
            base.DoBack();
        }

        /// <summary>Called by the gallery after it replaced or added a colonist.</summary>
        public void Notify_PresetApplied(Pawn pawn)
        {
            state.RememberColonists();
            Select(pawn);
        }
    }
}

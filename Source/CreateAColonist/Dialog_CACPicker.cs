using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>One choice in a picker list.</summary>
    public class PickerOption
    {
        public string label;
        public string description;

        // For a description that takes work to build: asked when the option is first shown, which most of a long list never is.
        public Func<string> describe;
        public string disabledReason;
        public Action onPick;
        public Action<Rect> drawIcon;
        public bool selected;
        public string extraSearch;
        public WorkBlock work;

        // The mod it comes from. When the options come from more than one mod, the picker offers a mod filter.
        public ModContentPack mod;

        /// <summary>Checked each time the row is drawn, for reasons that change while the picker is open (like weight).</summary>
        public Func<string> disabledCheck;

        public string DisabledReason => disabledCheck != null ? disabledCheck() : disabledReason;

        public string Description => description ??= describe?.Invoke();

        // The row's text as drawn: shortening a long label measures it once per letter, so that's done once, not every frame.
        public string shownLabel, shownCant;
        public float cantWidth;
    }

    /// <summary>
    /// The work a backstory or trait makes a colonist incapable of, read from the def itself, so backstories, traits
    /// and work types added by other mods are covered the same way as vanilla ones.
    /// </summary>
    public class WorkBlock
    {
        public const string ViolenceKey = "Violence";

        public bool violence;
        public readonly List<WorkTypeDef> types = new List<WorkTypeDef>();

        public bool Any => violence || types.Count > 0;

        public static WorkBlock From(WorkTags tags, IEnumerable<WorkTypeDef> alsoDisabled = null)
        {
            WorkBlock block = new WorkBlock { violence = (tags & WorkTags.Violent) != 0 };
            foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (w.visible && ((tags & w.workTags) != 0 || (alsoDisabled != null && alsoDisabled.Contains(w))))
                {
                    block.types.Add(w);
                }
            }
            block.types.SortByDescending(w => w.naturalPriority);
            return block;
        }

        public bool Blocks(string key)
        {
            return key == ViolenceKey ? violence : types.Any(w => w.defName == key);
        }

        private string label;

        public string Label
        {
            get
            {
                if (label == null)
                {
                    IEnumerable<string> names = types.Select(w => w.labelShort.CapitalizeFirst());
                    label = (violence ? names.Prepend("CAC_Violence".Translate().ToString()) : names).ToCommaList();
                }
                return label;
            }
        }
    }

    /// <summary>A searchable list with icons and descriptions, used for backstories, traits, items and so on.</summary>
    public class Dialog_CACPicker : Window
    {
        private readonly string title;
        private readonly List<PickerOption> options;
        private readonly bool closeOnPick;
        private readonly bool offerWorkFilter;
        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private Vector2 scroll;
        private PickerOption hovered, preselected;
        private bool hideAtOpen;
        private readonly List<ModContentPack> mods;
        private ModContentPack modFilter;

        // Filtering a long list (every item def with big mod lists) only when the search or filters change.
        private readonly CachedList<PickerOption> shownList = new CachedList<PickerOption>();

        private const float RowH = 32f;

        public override Vector2 InitialSize => new Vector2(720f, Mathf.Min(760f, UI.screenHeight - 60f));

        /// <param name="offerWorkFilter">Show the "hide ones that block work" and "must be able to do" filters
        /// (for options that carry a <see cref="WorkBlock"/>).</param>
        public Dialog_CACPicker(string title, IEnumerable<PickerOption> options, bool closeOnPick = true, bool offerWorkFilter = false)
        {
            this.title = title;
            this.options = options.ToList();
            this.closeOnPick = closeOnPick;
            this.offerWorkFilter = offerWorkFilter;
            mods = CACUI.ModsOf(this.options.Select(o => o.mod));
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            search.Focus();
            hideAtOpen = CreateAColonistMod.Settings.hideWorkBlockers;
            preselected = options.FirstOrDefault(o => o.selected);
            if (preselected != null)
            {
                scroll.y = Mathf.Max(0f, options.IndexOf(preselected) * RowH - 120f);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (offerWorkFilter && CreateAColonistMod.Settings.hideWorkBlockers != hideAtOpen)
            {
                CreateAColonistMod.Instance.WriteSettings();
            }
        }

        private bool PassesWorkFilter(PickerOption o)
        {
            if (!offerWorkFilter || o.work == null)
            {
                return true;
            }
            CreateAColonistSettings settings = CreateAColonistMod.Settings;
            if (settings.hideWorkBlockers && o.work.Any)
            {
                return false;
            }
            return !settings.requiredWork.Any(o.work.Blocks);
        }

        private float DrawWorkFilter(Rect row)
        {
            CreateAColonistSettings settings = CreateAColonistMod.Settings;
            Rect hide = new Rect(row.x, row.y, row.width * 0.5f, row.height);
            bool before = settings.hideWorkBlockers;
            Widgets.CheckboxLabeled(hide, "CAC_HideWorkBlockers".Translate(), ref settings.hideWorkBlockers, placeCheckboxNearText: true);
            if (before != settings.hideWorkBlockers)
            {
                scroll = Vector2.zero;
            }
            int required = settings.requiredWork.Count;
            string label = required > 0 ? "CAC_MustDoCount".Translate(required) : "CAC_MustDo".Translate();
            Rect mustDo = new Rect(row.x + row.width * 0.5f + 10f, row.y, row.width * 0.5f - 10f, row.height);
            if (Widgets.ButtonText(mustDo, label))
            {
                scroll = Vector2.zero;
                Find.WindowStack.Add(new Dialog_WorkRequirements());
            }
            if (required > 0 && Mouse.IsOver(mustDo))
            {
                TooltipHandler.TipRegion(mustDo, Dialog_WorkRequirements.Summary());
            }
            return row.height + 6f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 36f), title);
            }
            search.OnGUI(new Rect(inRect.x, inRect.y + 40f, inRect.width * 0.55f, 26f));
            if (mods.Count > 1)
            {
                CACUI.ModFilterButton(new Rect(inRect.x + inRect.width * 0.55f + 10f, inRect.y + 40f, inRect.width * 0.45f - 10f, 26f),
                    mods, modFilter, m =>
                    {
                        modFilter = m;
                        scroll = Vector2.zero;
                    });
            }
            float listTop = 74f;
            if (offerWorkFilter)
            {
                listTop += DrawWorkFilter(new Rect(inRect.x, inRect.y + 72f, inRect.width, 28f));
            }

            CreateAColonistSettings settings = CreateAColonistMod.Settings;
            string key = search.filter.Text + "|" + modFilter?.PackageId + (offerWorkFilter ? "|" + settings.hideWorkBlockers + "|" + string.Join(",", settings.requiredWork) : "");
            List<PickerOption> shown = shownList.Get(key, () => options
                .Where(o => (search.filter.Matches(o.label) || (o.extraSearch != null && search.filter.Matches(o.extraSearch))) && PassesWorkFilter(o)
                    && (modFilter == null || o.mod == modFilter))
                .ToList());

            float descW = inRect.width * 0.4f;
            Rect listRect = new Rect(inRect.x, inRect.y + listTop, inRect.width - descW - 12f, inRect.height - listTop);
            Rect descRect = new Rect(listRect.xMax + 12f, listRect.y, descW, listRect.height);

            if (shown.Count == 0)
            {
                using (new TextBlock(TextAnchor.UpperCenter, ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(new Rect(listRect.x, listRect.y + 20f, listRect.width, 60f), "CAC_NothingMatches".Translate());
                }
            }
            Rect view = new Rect(0f, 0f, listRect.width - 16f, shown.Count * RowH);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            PickerOption newHover = null;
            for (int i = 0; i < shown.Count; i++)
            {
                PickerOption opt = shown[i];
                Rect row = new Rect(0f, i * RowH, view.width, RowH);
                if (row.yMax < scroll.y || row.y > scroll.y + listRect.height)
                {
                    continue;
                }
                if (Mouse.IsOver(row))
                {
                    newHover = opt;
                }
                if (Event.current.type == EventType.Repaint)
                {
                    DrawRow(row, opt, i % 2 == 1);
                }
                if (Widgets.ButtonInvisible(row))
                {
                    string reason = opt.DisabledReason;
                    if (reason != null)
                    {
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                    else
                    {
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        opt.onPick?.Invoke();
                        if (closeOnPick)
                        {
                            Close();
                        }
                    }
                }
            }
            Widgets.EndScrollView();
            if (newHover != null)
            {
                hovered = newHover;
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Widgets.DrawMenuSection(descRect);
            PickerOption info = hovered ?? preselected;
            if (info == null)
            {
                using (new TextBlock(TextAnchor.MiddleCenter, ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(descRect.ContractedBy(16f), "CAC_HoverForDetails".Translate());
                }
            }
            else
            {
                Rect d = descRect.ContractedBy(10f);
                using (new TextBlock(GameFont.Medium))
                {
                    float h = Text.CalcHeight(info.label, d.width);
                    Widgets.Label(new Rect(d.x, d.y, d.width, h), info.label);
                    d.yMin += h + 6f;
                }
                string infoReason = info.DisabledReason;
                if (infoReason != null)
                {
                    using (new TextBlock(ColorLibrary.RedReadable))
                    {
                        float h = Text.CalcHeight(infoReason, d.width);
                        Widgets.Label(new Rect(d.x, d.y, d.width, h), infoReason);
                        d.yMin += h + 6f;
                    }
                }
                string description = info.Description;
                if (!description.NullOrEmpty())
                {
                    Widgets.Label(d, description);
                }
            }
        }

        private static void DrawRow(Rect row, PickerOption opt, bool odd)
        {
            if (odd)
            {
                Widgets.DrawLightHighlight(row);
            }
            if (opt.selected)
            {
                Widgets.DrawHighlightSelected(row);
            }
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }
            float x = row.x + 4f;
            if (opt.drawIcon != null)
            {
                opt.drawIcon(new Rect(x, row.y + 2f, RowH - 4f, RowH - 4f));
                x += RowH + 2f;
            }
            float right = row.xMax - 4f;
            if (opt.work != null && opt.work.Any)
            {
                // Short red "can't" note on the right, so blocked work shows without hovering.
                using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleRight, ColorLibrary.RedReadable))
                {
                    if (opt.shownCant == null)
                    {
                        string cant = "CAC_CantDo".Translate(opt.work.Label);
                        opt.cantWidth = Mathf.Min(Text.CalcSize(cant).x, row.width * 0.45f);
                        opt.shownCant = cant.Truncate(opt.cantWidth);
                    }
                    Widgets.Label(new Rect(right - opt.cantWidth, row.y, opt.cantWidth, row.height), opt.shownCant);
                    right -= opt.cantWidth + 8f;
                }
            }
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, opt.DisabledReason != null ? ColoredText.SubtleGrayColor : Color.white))
            {
                opt.shownLabel ??= opt.label.Truncate(right - x);
                Widgets.Label(new Rect(x, row.y, right - x, row.height), opt.shownLabel);
            }
        }
    }

    /// <summary>Tick the work a backstory or trait must leave available. Remembered between games.</summary>
    public class Dialog_WorkRequirements : Window
    {
        private readonly List<(string key, string label)> choices = Choices();
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(420f, Mathf.Min(680f, UI.screenHeight - 60f));

        public Dialog_WorkRequirements()
        {
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        private static List<(string key, string label)> Choices()
        {
            List<(string, string)> list = new List<(string, string)> { (WorkBlock.ViolenceKey, "CAC_Violence".Translate()) };
            list.AddRange(DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(w => w.visible).OrderByDescending(w => w.naturalPriority)
                .Select(w => (w.defName, w.labelShort.CapitalizeFirst())));
            return list;
        }

        public static string Summary()
        {
            List<string> required = CreateAColonistMod.Settings.requiredWork;
            return Choices().Where(c => required.Contains(c.key)).Select(c => c.label).ToCommaList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            List<string> required = CreateAColonistMod.Settings.requiredWork;
            inRect.height -= CloseButSize.y + 8f;
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 34f), "CAC_MustDoTitle".Translate());
            }
            float y = inRect.y + 38f;
            using (new TextBlock(GameFont.Small, ColoredText.SubtleGrayColor))
            {
                string note = "CAC_MustDoNote".Translate();
                float h = Text.CalcHeight(note, inRect.width);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, h), note);
                y += h + 6f;
            }
            if (required.Any() && Widgets.ButtonText(new Rect(inRect.x, y, 140f, 28f), "CAC_ClearAll".Translate()))
            {
                required.Clear();
            }
            y += 34f;
            Rect outer = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            Rect view = new Rect(0f, 0f, outer.width - 16f, choices.Count * 28f);
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < choices.Count; i++)
            {
                Rect row = new Rect(0f, i * 28f, view.width, 26f);
                bool on = required.Contains(choices[i].key);
                bool was = on;
                Widgets.CheckboxLabeled(row, choices[i].label, ref on);
                if (on != was)
                {
                    if (on)
                    {
                        required.Add(choices[i].key);
                    }
                    else
                    {
                        required.Remove(choices[i].key);
                    }
                }
            }
            Widgets.EndScrollView();
        }

        public override void PostClose()
        {
            base.PostClose();
            CreateAColonistMod.Instance.WriteSettings();
        }
    }
}

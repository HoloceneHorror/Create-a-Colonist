using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>Small drawing helpers shared by the creator tabs. All of them work on a running y cursor.</summary>
    [StaticConstructorOnStartup]
    public static class CACUI
    {
        public static readonly Texture2D SkillBarFill = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.1f));
        public static readonly Texture2D WeightBarFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.55f, 0.35f, 0.6f));
        public static readonly Texture2D OverBarFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.7f, 0.2f, 0.2f, 0.7f));
        public static readonly Color SectionLine = new Color(1f, 1f, 1f, 0.15f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>
        /// The part of the tab panel's scroll view that is on screen, set while the panel draws. Grids skip cells
        /// outside it, so long lists (hundreds of modded hairstyles or clothes) only draw what can be seen.
        /// </summary>
        public static Rect? ScrollVisible;

        // Hair, beard, tattoo and head icons: the game searches every mod each time it's asked, and grids ask every frame.
        private static readonly Dictionary<Def, Texture2D> icons = new Dictionary<Def, Texture2D>();

        private static bool KnownIcon(Def def, out Texture2D icon) => icons.TryGetValue(def, out icon) && icon != null;

        public static Texture2D IconOf(StyleItemDef def) => KnownIcon(def, out Texture2D icon) ? icon : icons[def] = def.Icon;

        public static Texture2D IconOf(HeadTypeDef def) => KnownIcon(def, out Texture2D icon) ? icon : icons[def] = def.Icon;

        public static void Section(ref float y, float width, string label)
        {
            y += 6f;
            using (new TextBlock(GameFont.Small, ColoredText.TipSectionTitleColor))
            {
                Widgets.Label(new Rect(0f, y, width, 24f), label);
            }
            y += 24f;
            Widgets.DrawLineHorizontal(0f, y, width, SectionLine);
            y += 6f;
        }

        public static void Note(ref float y, float width, string text)
        {
            using (new TextBlock(GameFont.Tiny, ColoredText.SubtleGrayColor))
            {
                float h = Text.CalcHeight(text, width);
                Widgets.Label(new Rect(0f, y, width, h), text);
                y += h + 4f;
            }
        }

        /// <summary>A row of toggle "pills". Returns the index that is selected after this frame.</summary>
        public static int Pills(Rect rect, IList<string> labels, int selected)
        {
            float x = rect.x;
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    float w = Text.CalcSize(labels[i]).x + 20f;
                    Rect r = new Rect(x, rect.y, w, rect.height);
                    Widgets.DrawOptionBackground(r, i == selected);
                    Widgets.Label(r, labels[i]);
                    if (Widgets.ButtonInvisible(r) && i != selected)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        selected = i;
                    }
                    x += w + 4f;
                }
            }
            return selected;
        }

        /// <summary>
        /// Lays out square cells in rows. drawCell(rect, item) draws one cell; clicking calls onPick (null: not clickable).
        /// Returns nothing; advances y past the grid.
        /// </summary>
        public static void Grid<T>(ref float y, float width, float cell, IList<T> items, Func<T, bool> isSelected,
            Action<Rect, T> drawCell, Func<T, string> tooltip, Action<T> onPick)
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + 4f) / (cell + 4f)));
            bool repaint = Event.current.type == EventType.Repaint;
            for (int i = 0; i < items.Count; i++)
            {
                T item = items[i];
                Rect r = new Rect((i % perRow) * (cell + 4f), y + (i / perRow) * (cell + 4f), cell, cell);
                if (ScrollVisible is Rect visible && (r.yMax < visible.y || r.y > visible.yMax))
                {
                    continue;
                }
                // A cell only draws, apart from the click: the other events of a frame can skip the rest.
                if (repaint)
                {
                    Widgets.DrawOptionBackground(r, isSelected(item));
                    drawCell(r.ContractedBy(4f), item);
                    if (Mouse.IsOver(r))
                    {
                        string tip = tooltip?.Invoke(item);
                        if (!tip.NullOrEmpty())
                        {
                            TooltipHandler.TipRegion(r, tip);
                        }
                    }
                }
                if (onPick != null && Widgets.ButtonInvisible(r))
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    onPick(item);
                }
            }
            int rows = Mathf.CeilToInt(items.Count / (float)perRow);
            y += rows * (cell + 4f);
        }

        /// <summary>Colour swatches in a row; returns true and sets color when one is clicked.</summary>
        public static bool Swatches(ref float y, float width, List<Color> colors, Color current, out Color picked, float size = 24f)
        {
            picked = current;
            bool clicked = false;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + 3f) / (size + 3f)));
            bool repaint = Event.current.type == EventType.Repaint;
            for (int i = 0; i < colors.Count; i++)
            {
                Rect r = new Rect((i % perRow) * (size + 3f), y + (i / perRow) * (size + 3f), size, size);
                if (repaint)
                {
                    Widgets.DrawBoxSolid(r, colors[i]);
                    bool same = ColorsClose(colors[i], current);
                    Widgets.DrawBox(r, same ? 2 : 1, same ? BaseContent.WhiteTex : null);
                    Widgets.DrawHighlightIfMouseover(r);
                }
                if (Widgets.ButtonInvisible(r))
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    picked = colors[i];
                    clicked = true;
                }
            }
            y += Mathf.CeilToInt(colors.Count / (float)perRow) * (size + 3f);
            return clicked;
        }

        public static bool ColorsClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        /// <summary>Label on the left, a dropdown-style button on the right. The tooltip is only built when hovered.</summary>
        public static bool LabeledButton(ref float y, float width, string label, string buttonText, float labelWidth = 110f, Func<string> tooltip = null)
        {
            Rect row = new Rect(0f, y, width, 30f);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(row.x, row.y, labelWidth, row.height), label);
            }
            Rect but = new Rect(row.x + labelWidth, row.y + 1f, row.width - labelWidth, 28f);
            bool clicked = Widgets.ButtonText(but, buttonText.Truncate(but.width - 12f));
            if (tooltip != null && Mouse.IsOver(but))
            {
                string tip = tooltip();
                if (!tip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(but, tip);
                }
            }
            y += 34f;
            return clicked;
        }

        public static bool IconButton(Rect rect, Texture2D icon, string tooltip)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            if (!tooltip.NullOrEmpty() && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            return Widgets.ButtonImage(rect.ContractedBy(2f), icon, Color.white, GenUI.MouseoverColor);
        }

        /// <summary>A width that fits the label in the current font, and at least min: translations run longer than English.</summary>
        public static float FitWidth(string label, float min, float extra = 16f)
        {
            return Mathf.Max(min, Text.CalcSize(label).x + extra);
        }

        /// <summary>The distinct mods in a list, the base game first, then by name. Nulls are left out.</summary>
        public static List<ModContentPack> ModsOf(IEnumerable<ModContentPack> packs)
        {
            List<ModContentPack> list = packs.Where(p => p != null).Distinct().ToList();
            list.Sort((a, b) => a.IsCoreMod != b.IsCoreMod ? (a.IsCoreMod ? -1 : 1)
                : a.IsOfficialMod != b.IsOfficialMod ? (a.IsOfficialMod ? -1 : 1)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>"Mod: All" button. Clicking it lists the mods to show things from; onPick gets null for all of them.</summary>
        public static void ModFilterButton(Rect rect, List<ModContentPack> mods, ModContentPack current, Action<ModContentPack> onPick)
        {
            string name = current == null ? (string)"CAC_ModAll".Translate() : current.Name;
            if (Widgets.ButtonText(rect, ("CAC_ModFilter".Translate(name)).Truncate(rect.width - 12f)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("CAC_ModAll".Translate(), () => onPick(null))
                };
                foreach (ModContentPack mod in mods)
                {
                    ModContentPack m = mod;
                    options.Add(new FloatMenuOption(m.Name, () => onPick(m)));
                }
                FloatMenu(options);
            }
            if (current != null && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, current.Name);
            }
        }

        public static void FloatMenu(IEnumerable<FloatMenuOption> options)
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>(options);
            if (list.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(list));
            }
        }
    }

    /// <summary>
    /// A filtered, sorted list that is rebuilt only when its key (everything the list depends on) changes.
    /// Filtering and sorting every def each frame is the costly part of the icon grids with big mod lists.
    /// </summary>
    public class CachedList<T>
    {
        private List<T> list;
        private string key;

        public List<T> Get(string key, Func<List<T>> build)
        {
            if (list == null || this.key != key)
            {
                this.key = key;
                list = build();
            }
            return list;
        }
    }
}

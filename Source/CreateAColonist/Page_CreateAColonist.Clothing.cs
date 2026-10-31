using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        private int apparelLayerFilter;
        private readonly QuickSearchWidget apparelSearch = new QuickSearchWidget();
        private static List<ThingDef> allApparel;
        private static List<ModContentPack> apparelMods;
        private ModContentPack apparelMod;
        private readonly CachedList<ThingDef> apparelOptions = new CachedList<ThingDef>();

        private static readonly string[] LayerKeys = { "CAC_Layer_All", "CAC_Layer_Head", "CAC_Layer_Outer", "CAC_Layer_Middle", "CAC_Layer_Skin", "CAC_Layer_Belt", "CAC_Layer_Other" };

        private static List<ThingDef> AllApparel => allApparel ??= DefDatabase<ThingDef>.AllDefs
            .Where(d => d.IsApparel && !d.destroyOnDrop && d.apparel != null)
            .OrderBy(d => d.label).ToList();

        private static bool LayerMatches(ThingDef def, int filter)
        {
            List<ApparelLayerDef> layers = def.apparel.layers;
            switch (filter)
            {
                case 0: return true;
                case 1: return layers.Contains(ApparelLayerDefOf.Overhead) || layers.Contains(ApparelLayerDefOf.EyeCover);
                case 2: return layers.Contains(ApparelLayerDefOf.Shell);
                case 3: return layers.Contains(ApparelLayerDefOf.Middle);
                case 4: return layers.Contains(ApparelLayerDefOf.OnSkin);
                case 5: return layers.Contains(ApparelLayerDefOf.Belt);
                default:
                    return !layers.Any(l => l == ApparelLayerDefOf.Overhead || l == ApparelLayerDefOf.EyeCover || l == ApparelLayerDefOf.Shell
                        || l == ApparelLayerDefOf.Middle || l == ApparelLayerDefOf.OnSkin || l == ApparelLayerDefOf.Belt);
            }
        }

        private void DrawClothing(ref float y, float w)
        {
            Pawn pawn = selected;
            CACUI.Section(ref y, w, "CAC_Wearing".Translate());
            List<Apparel> worn = pawn.apparel.WornApparel.ToList();
            if (!worn.Any())
            {
                CACUI.Note(ref y, w, "CAC_WearingNothing".Translate());
            }
            foreach (Apparel ap in worn)
            {
                ThingEditRow(new Rect(0f, y, w, 34f), ap,
                    (stuff, quality) => PawnEditor.ReplaceApparel(pawn, ap, stuff, quality),
                    () => PawnEditor.RemoveApparel(pawn, ap));
                y += 36f;
            }
            if (worn.Any())
            {
                Rect clear = new Rect(w - 140f, y, 140f, 26f);
                if (Widgets.ButtonText(clear, "CAC_RemoveAll".Translate()))
                {
                    foreach (Apparel ap in worn)
                    {
                        PawnEditor.RemoveApparel(pawn, ap);
                    }
                }
                y += 30f;
            }

            CACUI.Section(ref y, w, "CAC_AddClothing".Translate());
            float x = 0f;
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
            {
                for (int i = 0; i < LayerKeys.Length; i++)
                {
                    string label = LayerKeys[i].Translate();
                    float bw = Text.CalcSize(label).x + 16f;
                    if (x + bw > w)
                    {
                        x = 0f;
                        y += 30f;
                    }
                    Rect r = new Rect(x, y, bw, 26f);
                    Widgets.DrawOptionBackground(r, apparelLayerFilter == i);
                    Widgets.Label(r, label);
                    if (Widgets.ButtonInvisible(r))
                    {
                        apparelLayerFilter = i;
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                    x += bw + 4f;
                }
            }
            y += 32f;
            float searchW = Mathf.Min(260f, w);
            apparelSearch.OnGUI(new Rect(0f, y, searchW, 24f));
            apparelMods ??= CACUI.ModsOf(AllApparel.Select(d => d.modContentPack));
            if (apparelMods.Count > 1 && w - searchW - 8f >= 120f)
            {
                CACUI.ModFilterButton(new Rect(searchW + 8f, y, Mathf.Min(220f, w - searchW - 8f), 24f), apparelMods, apparelMod, m => apparelMod = m);
            }
            y += 30f;

            // What a colonist can wear depends on their age stage and which body parts they still have.
            int missingKey = 0;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_MissingPart && h.Part != null)
                {
                    missingKey = missingKey * 31 + h.Part.Index + 1;
                }
            }
            string key = pawn.thingIDNumber + "|" + pawn.DevelopmentalStage + "|" + missingKey + "|" + apparelLayerFilter + "|" + apparelSearch.filter.Text + "|" + apparelMod?.PackageId;
            List<ThingDef> options = apparelOptions.Get(key, () => AllApparel
                .Where(d => LayerMatches(d, apparelLayerFilter) && apparelSearch.filter.Matches(d) && (apparelMod == null || d.modContentPack == apparelMod)
                    && PawnEditor.CanWear(pawn, d))
                .ToList());
            if (!options.Any())
            {
                CACUI.Note(ref y, w, "CAC_NothingMatches".Translate());
            }
            HashSet<ThingDef> wornDefs = new HashSet<ThingDef>(worn.Select(a => a.def));
            CACUI.Grid(ref y, w, 58f, options, wornDefs.Contains,
                (r, d) => Widgets.ThingIcon(r, d, GenStuff.DefaultStuffFor(d)),
                d => d.LabelCap + "\n\n" + d.description,
                d => PawnEditor.Wear(pawn, d));
        }

        /// <summary>Clicking an item's icon opens the game's own info card for it, with all its stats.</summary>
        private static void InfoCardOnClick(Rect icon, System.Func<Dialog_InfoCard> card)
        {
            if (Mouse.IsOver(icon))
            {
                Widgets.DrawHighlight(icon);
                TooltipHandler.TipRegion(icon, "CAC_ClickForInfo".Translate());
            }
            if (Widgets.ButtonInvisible(icon))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Find.WindowStack.Add(card());
            }
        }

        /// <summary>
        /// Icon, label, then material and quality dropdowns and a remove button. apply(stuff, quality) is called when
        /// either changes. There's no colour choice: as in the game, a piece takes the colour of what it's made from.
        /// </summary>
        private static void ThingEditRow(Rect row, Thing thing, System.Action<ThingDef, QualityCategory> apply, System.Action remove)
        {
            Widgets.DrawHighlightIfMouseover(row);
            Rect icon = new Rect(row.x + 2f, row.y + 2f, 30f, 30f);
            Widgets.ThingIcon(icon, thing);
            InfoCardOnClick(icon, () => new Dialog_InfoCard(thing));
            float right = row.xMax;
            Rect del = new Rect(right - 26f, row.y + 6f, 22f, 22f);
            if (Widgets.ButtonImage(del, CACTex.Dismiss))
            {
                remove();
                return;
            }
            right = del.x - 4f;

            ThingDef def = thing.def;
            ThingDef stuff = thing.Stuff;
            QualityCategory quality = PawnEditor.QualityOf(thing);
            if (thing.TryGetComp<CompQuality>() != null)
            {
                Rect q = new Rect(right - 92f, row.y + 3f, 92f, 28f);
                if (Widgets.ButtonText(q, quality.GetLabel().CapitalizeFirst()))
                {
                    CACUI.FloatMenu(QualityUtility.AllQualityCategories.Select(qc =>
                        new FloatMenuOption(qc.GetLabel().CapitalizeFirst(), () => apply(stuff, qc))));
                }
                right = q.x - 4f;
            }
            if (def.MadeFromStuff)
            {
                Rect s = new Rect(right - 110f, row.y + 3f, 110f, 28f);
                if (Widgets.ButtonText(s, stuff != null ? stuff.LabelAsStuff.CapitalizeFirst().Truncate(100f) : "-"))
                {
                    CACUI.FloatMenu(GenStuff.AllowedStuffsFor(def).OrderBy(st => st.label).Select(st =>
                        new FloatMenuOption(st.LabelAsStuff.CapitalizeFirst(), () => apply(st, quality), st)));
                }
                right = s.x - 4f;
            }
            Rect label = new Rect(icon.xMax + 6f, row.y, right - icon.xMax - 8f, row.height);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(label, def.LabelCap.Truncate(label.width));
            }
            if (Mouse.IsOver(label))
            {
                TooltipHandler.TipRegion(label, thing.LabelCap + "\n\n" + def.description);
            }
        }

    }
}

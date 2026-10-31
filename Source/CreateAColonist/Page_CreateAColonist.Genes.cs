using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        private readonly QuickSearchWidget xenoSearch = new QuickSearchWidget();
        private readonly CachedList<XenotypeDef> xenotypeList = new CachedList<XenotypeDef>();

        private void DrawGenes(ref float y, float w)
        {
            Pawn pawn = selected;
            Pawn_GeneTracker genes = pawn.genes;

            // Current xenotype header
            Rect header = new Rect(0f, y, w, 56f);
            Widgets.DrawOptionBackground(header, true);
            Texture2D icon = genes.XenotypeIcon;
            GUI.DrawTexture(new Rect(header.x + 8f, header.y + 8f, 40f, 40f), icon);
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(header.x + 58f, header.y, header.width - 58f, header.height), genes.XenotypeLabelCap);
            }
            if (Mouse.IsOver(header))
            {
                TooltipHandler.TipRegion(header, genes.XenotypeDescShort);
            }
            y += 64f;

            CACUI.Section(ref y, w, "CAC_Xenotype".Translate());
            xenoSearch.OnGUI(new Rect(0f, y, Mathf.Min(260f, w), 24f));
            y += 30f;

            List<XenotypeDef> xenotypes = xenotypeList.Get(xenoSearch.filter.Text, () => DefDatabase<XenotypeDef>.AllDefs
                .Where(x => xenoSearch.filter.Matches(x.label ?? x.defName))
                .OrderByDescending(x => x == XenotypeDefOf.Baseliner)
                .ThenBy(x => x.label)
                .ToList());
            CACUI.Grid(ref y, w, 64f, xenotypes, x => !genes.UniqueXenotype && genes.Xenotype == x,
                (r, x) => GUI.DrawTexture(r.ContractedBy(4f), x.Icon, ScaleMode.ScaleToFit),
                x => x.LabelCap + "\n\n" + x.descriptionShort,
                x => PawnEditor.SetXenotype(pawn, x));

            List<CustomXenotype> customs = CharacterCardUtility.CustomXenotypesForReading.Where(c => xenoSearch.filter.Matches(c.name ?? "")).ToList();
            if (customs.Any())
            {
                CACUI.Section(ref y, w, "CAC_CustomXenotypes".Translate());
                CACUI.Grid(ref y, w, 64f, customs, c => genes.UniqueXenotype && genes.xenotypeName == c.name,
                    (r, c) => GUI.DrawTexture(r.ContractedBy(4f), c.IconDef.Icon, ScaleMode.ScaleToFit),
                    c => c.name.CapitalizeFirst(),
                    c => PawnEditor.SetCustomXenotype(pawn, c));
            }

            y += 4f;
            if (Widgets.ButtonText(new Rect(0f, y, Mathf.Min(260f, w), 32f), "CAC_OpenXenotypeEditor".Translate()))
            {
                OpenXenotypeEditor(pawn);
            }
            y += 40f;

            CACUI.Section(ref y, w, "CAC_GenesList".Translate());
            GeneIcons(ref y, w, genes.Endogenes, "Endogenes".Translate());
            GeneIcons(ref y, w, genes.Xenogenes, "Xenogenes".Translate());
        }

        private void OpenXenotypeEditor(Pawn pawn)
        {
            int index = PawnEditor.StartingIndex(pawn);
            Find.WindowStack.Add(new Dialog_CreateXenotype(index, delegate
            {
                CharacterCardUtility.cachedCustomXenotypes = null;
                CustomXenotype custom = StartingPawnUtility.GetGenerationRequest(index).ForcedCustomXenotype;
                if (custom != null)
                {
                    PawnEditor.SetCustomXenotype(pawn, custom);
                }
            }));
        }

        private static void GeneIcons(ref float y, float w, List<Gene> list, string label)
        {
            if (list.NullOrEmpty())
            {
                return;
            }
            using (new TextBlock(GameFont.Tiny, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(0f, y, w, 20f), label);
            }
            y += 20f;
            CACUI.Grid(ref y, w, 40f, list, g => false,
                (r, g) =>
                {
                    GUI.color = g.Overridden ? CACUI.Dim : g.def.IconColor;
                    GUI.DrawTexture(r, g.def.Icon, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                },
                g => g.def.LabelCap.ToString() + (g.Overridden ? " (" + "Overridden".Translate().ToString() + ")" : "") + "\n\n" + g.def.DescriptionFull,
                null);
            y += 4f;
        }
    }
}

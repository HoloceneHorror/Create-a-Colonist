using System;
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
        private void DrawHealth(ref float y, float w)
        {
            Pawn pawn = selected;

            CACUI.Section(ref y, w, "CAC_HealthOverview".Translate());
            DrawCapacities(ref y, w, pawn);

            CACUI.Section(ref y, w, "CAC_Conditions".Translate());
            List<Hediff> hediffs = PawnEditor.ShownHediffs(pawn);
            if (hediffs.Count == 0)
            {
                CACUI.Note(ref y, w, "CAC_Healthy".Translate(pawn.LabelShort));
            }
            foreach (Hediff hediff in hediffs)
            {
                HediffRow(new Rect(0f, y, w, 26f), pawn, hediff);
                y += 28f;
            }

            CACUI.Section(ref y, w, "CAC_AddToHealth".Translate());
            CACUI.Note(ref y, w, "CAC_HealthNote".Translate());
            float half = (w - 6f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, y, half, 30f), "CAC_AddImplant".Translate()))
            {
                OpenHealthPicker(pawn, "CAC_AddImplant".Translate(), PawnEditor.InstallOptions(pawn));
            }
            if (Widgets.ButtonText(new Rect(half + 6f, y, half, 30f), "CAC_AddScar".Translate()))
            {
                OpenHealthPicker(pawn, "CAC_AddScar".Translate(), PawnEditor.ScarOptions(pawn));
            }
            y += 34f;
            if (Widgets.ButtonText(new Rect(0f, y, half, 30f), "CAC_AddMissingPart".Translate()))
            {
                OpenHealthPicker(pawn, "CAC_AddMissingPart".Translate(), PawnEditor.MissingPartOptions(pawn));
            }
            if (Widgets.ButtonText(new Rect(half + 6f, y, half, 30f), "CAC_AddCondition".Translate()))
            {
                OpenHealthPicker(pawn, "CAC_AddCondition".Translate(), PawnEditor.ConditionOptions(pawn));
            }
            y += 34f;
            if (Widgets.ButtonText(new Rect(0f, y, half, 30f), "CAC_AddAddiction".Translate()))
            {
                OpenHealthPicker(pawn, "CAC_AddAddiction".Translate(), PawnEditor.AddictionOptions(pawn));
            }
            if (hediffs.Any(PawnEditor.CanRemoveHediff) && Widgets.ButtonText(new Rect(half + 6f, y, half, 30f), "CAC_HealAll".Translate()))
            {
                PawnEditor.RemoveAllHediffs(pawn);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            y += 38f;
        }

        private readonly CachedList<PawnCapacityDef> capacityList = new CachedList<PawnCapacityDef>();

        private void DrawCapacities(ref float y, float w, Pawn pawn)
        {
            BodyDef body = pawn.RaceProps.body;
            List<PawnCapacityDef> capacities = capacityList.Get(body.defName, () => DefDatabase<PawnCapacityDef>.AllDefsListForReading
                .Where(c => c.showOnHumanlikes && PawnCapacityUtility.BodyCanEverDoCapacity(body, c)).OrderBy(c => c.listOrder).ToList());
            // Only labels, so the levels are worked out just for drawing, and the breakdowns in the tooltips just when hovered.
            if (Event.current.type == EventType.Repaint)
            {
                // Two columns, pain first: label on the left, value in its colour on the right.
                float colW = (w - 12f) / 2f;
                for (int i = 0; i <= capacities.Count; i++)
                {
                    PawnCapacityDef cap = i == 0 ? null : capacities[i - 1];
                    Pair<string, Color> value = cap == null ? HealthCardUtility.GetPainLabel(pawn) : HealthCardUtility.GetEfficiencyLabel(pawn, cap);
                    Rect r = new Rect(i % 2 * (colW + 12f), y + i / 2 * 24f, colW, 22f);
                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(r, cap == null ? (string)"Pain".Translate().CapitalizeFirst() : cap.GetLabelFor(pawn).CapitalizeFirst());
                    }
                    using (new TextBlock(TextAnchor.MiddleRight, value.Second))
                    {
                        Widgets.Label(r, value.First);
                    }
                    if (Mouse.IsOver(r))
                    {
                        Widgets.DrawHighlight(r);
                        TooltipHandler.TipRegion(r, CapacityTip(pawn, cap), 7300 + i);
                    }
                }
            }
            y += Mathf.CeilToInt((capacities.Count + 1) / 2f) * 24f + 4f;
        }

        private static Func<string> CapacityTip(Pawn pawn, PawnCapacityDef cap)
        {
            return () => cap == null ? HealthCardUtility.GetPainTip(pawn) : HealthCardUtility.GetPawnCapacityTip(pawn, cap);
        }

        private static void HediffRow(Rect row, Pawn pawn, Hediff hediff)
        {
            float partW = Mathf.Min(150f, row.width * 0.36f);
            using (new TextBlock(TextAnchor.MiddleLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(row.x + 4f, row.y, partW - 4f, row.height), PawnEditor.PartLabel(hediff.Part).Truncate(partW - 6f));
            }
            Rect label = new Rect(row.x + partW, row.y, row.width - partW - 28f, row.height);
            using (new TextBlock(TextAnchor.MiddleLeft, hediff.LabelColor))
            {
                Widgets.Label(label, hediff.LabelCap.Truncate(label.width));
            }
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
                TooltipHandler.TipRegion(row, () => hediff.GetTooltip(pawn, showHediffsDebugInfo: false), hediff.loadID);
            }
            if (PawnEditor.CanRemoveHediff(hediff))
            {
                Rect remove = new Rect(row.xMax - 22f, row.y + 5f, 16f, 16f);
                if (Mouse.IsOver(remove))
                {
                    TooltipHandler.TipRegion(remove, "CAC_RemoveCondition".Translate());
                }
                if (Widgets.ButtonImage(remove, CACTex.CloseX))
                {
                    PawnEditor.RemoveHediff(pawn, hediff);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }
        }

        private static void OpenHealthPicker(Pawn pawn, string title, List<HealthOption> options)
        {
            if (options.Count == 0)
            {
                Messages.Message("CAC_NothingToAdd".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            List<PickerOption> picker = options.Select(o => new PickerOption
            {
                label = o.label,
                description = o.description,
                disabledCheck = () => o.BlockedReason,
                drawIcon = o.icon == null ? (Action<Rect>)null : r => Widgets.DefIcon(r, o.icon),
                mod = o.mod,
                onPick = () => PawnEditor.ApplyHealthOption(pawn, o)
            }).ToList();
            Find.WindowStack.Add(new Dialog_CACPicker(title, picker));
        }
    }
}

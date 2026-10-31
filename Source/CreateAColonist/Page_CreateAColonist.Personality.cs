using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        private void DrawPersonality(ref float y, float w)
        {
            Pawn pawn = selected;

            if (pawn.DevelopmentalStage.Baby())
            {
                CACUI.Note(ref y, w, "CAC_BabyNote".Translate());
            }
            else
            {
                CACUI.Section(ref y, w, "CAC_Backstory".Translate());
                BackstoryRow(ref y, w, pawn, BackstorySlot.Childhood, "Childhood".Translate());
                if (pawn.ageTracker.AgeBiologicalYearsFloat >= PawnEditor.AdulthoodMinAge)
                {
                    BackstoryRow(ref y, w, pawn, BackstorySlot.Adulthood, "Adulthood".Translate());
                }
                else
                {
                    CACUI.Note(ref y, w, "CAC_TooYoungForAdulthood".Translate(PawnEditor.AdulthoodMinAge));
                }

                CACUI.Section(ref y, w, "Traits".Translate());
                DrawTraitChips(ref y, w, pawn);
            }

            CACUI.Section(ref y, w, "Skills".Translate());
            CACUI.Note(ref y, w, "CAC_SkillsNote".Translate());
            if (editingSkill != null && editingSkillPawn != pawn)
            {
                StopSkillEdit();
            }
            foreach (SkillRecord record in pawn.skills.skills.OrderBy(s => s.def.listOrder))
            {
                SkillRow(new Rect(0f, y, w, 24f), record, pawn);
                y += 26f;
            }
            DrawManualSkills(ref y, w, pawn);

            WorkTags disabled = pawn.CombinedDisabledWorkTags;
            if (disabled != WorkTags.None)
            {
                CACUI.Section(ref y, w, "IncapableOf".Translate(pawn).CapitalizeFirst());
                if (disabled != incapableTags)
                {
                    incapableTags = disabled;
                    incapableList = disabled.GetAllSelectedItems<WorkTags>().Where(t => t != WorkTags.None).Select(t => t.LabelTranslated().CapitalizeFirst()).ToCommaList();
                }
                using (new TextBlock(ColorLibrary.RedReadable))
                {
                    float h = Text.CalcHeight(incapableList, w);
                    Widgets.Label(new Rect(0f, y, w, h), incapableList);
                    y += h + 4f;
                }
            }
        }

        // Listing an enum's flags is slow and allocates, so the text is kept until the flags change.
        private WorkTags incapableTags;
        private string incapableList;

        private void BackstoryRow(ref float y, float w, Pawn pawn, BackstorySlot slot, string label)
        {
            BackstoryDef current = pawn.story.GetBackstory(slot);
            string text = current != null ? current.TitleCapFor(pawn.gender) : (string)"CAC_None".Translate();
            float rowY = y;
            if (CACUI.LabeledButton(ref y, w - 34f, label, text, 100f, () => current?.FullDescriptionFor(pawn).Resolve()))
            {
                OpenBackstoryPicker(pawn, slot, label);
            }
            Rect dice = new Rect(w - 30f, rowY + 1f, 28f, 28f);
            if (CACUI.IconButton(dice, CACTex.Dice, "CAC_RandomBackstory".Translate()))
            {
                BackstoryDef random = PawnEditor.BackstoryOptions(pawn, slot).Where(b => b != current).RandomElementWithFallback();
                if (random != null)
                {
                    PawnEditor.SetBackstory(pawn, slot, random);
                }
            }
        }

        private static void OpenBackstoryPicker(Pawn pawn, BackstorySlot slot, string label)
        {
            BackstoryDef current = pawn.story.GetBackstory(slot);
            List<PickerOption> options = PawnEditor.BackstoryOptions(pawn, slot).Select(b => new PickerOption
            {
                label = b.TitleCapFor(pawn.gender),
                describe = () => b.FullDescriptionFor(pawn).Resolve(),
                selected = b == current,
                extraSearch = b.skillGains.Select(g => g.skill.label).ToCommaList(),
                mod = b.modContentPack,
                work = WorkBlock.From(b.workDisables, b.DisabledWorkTypes),
                onPick = () => PawnEditor.SetBackstory(pawn, slot, b)
            }).ToList();
            Find.WindowStack.Add(new Dialog_CACPicker(label, options, offerWorkFilter: true));
        }

        private void DrawTraitChips(ref float y, float w, Pawn pawn)
        {
            float x = 0f;
            float chipH = 28f;
            foreach (Trait trait in pawn.story.traits.allTraits.ToList())
            {
                string label = trait.LabelCap;
                bool fromGene = trait.sourceGene != null;
                float chipW = Text.CalcSize(label).x + (fromGene ? 16f : 36f);
                if (x + chipW > w)
                {
                    x = 0f;
                    y += chipH + 4f;
                }
                Rect chip = new Rect(x, y, chipW, chipH);
                Widgets.DrawOptionBackground(chip, false);
                using (new TextBlock(TextAnchor.MiddleLeft, trait.Suppressed ? ColoredText.SubtleGrayColor : Color.white))
                {
                    Widgets.Label(new Rect(chip.x + 8f, chip.y, chipW - 8f, chipH), label);
                }
                if (Mouse.IsOver(chip))
                {
                    TooltipHandler.TipRegion(chip, trait.TipString(pawn));
                }
                if (!fromGene)
                {
                    Rect remove = new Rect(chip.xMax - 22f, chip.y + 7f, 14f, 14f);
                    if (Widgets.ButtonImage(remove, CACTex.CloseX))
                    {
                        PawnEditor.RemoveTrait(pawn, trait);
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }
                }
                x += chipW + 6f;
            }
            Rect add = new Rect(x, y, CACUI.FitWidth("CAC_AddTrait".Translate(), 130f), chipH);
            if (x + add.width > w)
            {
                y += chipH + 4f;
                add.x = 0f;
                add.y = y;
            }
            if (Widgets.ButtonText(add, "CAC_AddTrait".Translate()))
            {
                OpenTraitPicker(pawn);
            }
            y += chipH + 8f;
        }

        private static void OpenTraitPicker(Pawn pawn)
        {
            List<PickerOption> options = new List<PickerOption>();
            foreach (TraitDef def in DefDatabase<TraitDef>.AllDefs)
            {
                if (pawn.DevelopmentalStage.Juvenile() && PawnEditor.IsOrientation(def))
                {
                    continue;
                }
                foreach (TraitDegreeData data in def.degreeDatas)
                {
                    TraitDef d = def;
                    int degree = data.degree;
                    options.Add(new PickerOption
                    {
                        label = data.GetLabelCapFor(pawn.gender),
                        describe = () =>
                        {
                            try
                            {
                                return new Trait(d, degree).TipString(pawn);
                            }
                            catch
                            {
                                return data.description;
                            }
                        },
                        disabledReason = PawnEditor.TraitBlockReason(pawn, d, degree),
                        mod = d.modContentPack,
                        work = WorkBlock.From(d.disabledWorkTags, d.disabledWorkTypes),
                        onPick = () => PawnEditor.AddTrait(pawn, d, degree)
                    });
                }
            }
            options.SortBy(o => o.label);
            Find.WindowStack.Add(new Dialog_CACPicker("CAC_AddTrait".Translate(), options, offerWorkFilter: true));
        }

        // The skill level being typed into, if any. Only one at a time.
        private static SkillRecord editingSkill;
        private static Pawn editingSkillPawn;
        private static string skillBuffer = "";
        private static bool skillFieldFocused;
        private const string SkillFieldName = "CAC_SkillLevelField";

        /// <summary>"Hand-set levels" checkbox and the button that works them out from the backstories again.</summary>
        private static void DrawManualSkills(ref float y, float w, Pawn pawn)
        {
            y += 4f;
            bool manual = PawnEditor.HasManualSkills(pawn);
            bool wanted = manual;
            Rect check = new Rect(0f, y, Mathf.Min(260f, w), 28f);
            Widgets.CheckboxLabeled(check, "CAC_ManualSkills".Translate(), ref wanted, placeCheckboxNearText: true);
            if (Mouse.IsOver(check))
            {
                TooltipHandler.TipRegion(check, "CAC_ManualSkillsTip".Translate());
            }
            if (wanted != manual)
            {
                PawnEditor.SetManualSkills(pawn, wanted);
            }
            string recalculate = "CAC_RecalculateSkills".Translate();
            float resetW = CACUI.FitWidth(recalculate, 220f);
            if (w - resetW < check.width + 8f)
            {
                y += 32f;
            }
            Rect reset = new Rect(w - resetW, y, resetW, 28f);
            if (Widgets.ButtonText(reset, recalculate))
            {
                StopSkillEdit();
                PawnEditor.RecalculateSkills(pawn, force: true);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            if (Mouse.IsOver(reset))
            {
                TooltipHandler.TipRegion(reset, "CAC_RecalculateSkillsTip".Translate());
            }
            y += 34f;
        }

        private static void StopSkillEdit()
        {
            editingSkill = null;
            editingSkillPawn = null;
            skillBuffer = "";
            skillFieldFocused = false;
        }

        private static void CommitSkillEdit()
        {
            if (editingSkill != null && int.TryParse(skillBuffer, out int typed))
            {
                PawnEditor.SetSkillLevel(editingSkillPawn, editingSkill, typed);
            }
            StopSkillEdit();
        }

        private static readonly Regex DigitsOnly = new Regex("^[0-9]*$");

        /// <summary>
        /// The level number turns into a small text box when clicked. Enter or clicking elsewhere keeps the number,
        /// Escape cancels. Returns false once editing has ended, so the row draws the number again this frame.
        /// (The creator page turns off the game's own Enter and Escape handling, so these keys only reach the box.)
        /// </summary>
        private static bool SkillLevelField(Rect rect)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                CommitSkillEdit();
                e.Use();
                return false;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                StopSkillEdit();
                e.Use();
                return false;
            }
            if (e.type == EventType.MouseDown && !Mouse.IsOver(rect))
            {
                CommitSkillEdit();
                return false;
            }
            GUI.SetNextControlName(SkillFieldName);
            skillBuffer = Widgets.TextField(rect, skillBuffer, 2, DigitsOnly);
            if (GUI.GetNameOfFocusedControl() == SkillFieldName)
            {
                skillFieldFocused = true;
            }
            else if (!skillFieldFocused)
            {
                // Focus can take a frame to arrive after the click.
                GUI.FocusControl(SkillFieldName);
            }
            else
            {
                // Had focus and lost it (tabbed away, another text box): keep what was typed.
                CommitSkillEdit();
                return false;
            }
            return true;
        }

        private static void SkillRow(Rect row, SkillRecord record, Pawn pawn)
        {
            bool disabled = record.TotallyDisabled;
            using (new TextBlock(TextAnchor.MiddleLeft, disabled ? ColoredText.SubtleGrayColor : Color.white))
            {
                Widgets.Label(new Rect(row.x, row.y, 120f, row.height), record.def.skillLabel.CapitalizeFirst());
            }
            Rect passion = new Rect(row.x + 122f, row.y, 24f, 24f);
            if (!disabled)
            {
                Texture2D tex = record.passion == Passion.Major ? CACTex.PassionMajor
                    : record.passion == Passion.Minor ? CACTex.PassionMinor : CACTex.PassionMinorGray;
                GUI.color = record.passion == Passion.None ? CACUI.Dim : Color.white;
                GUI.DrawTexture(passion, tex);
                GUI.color = Color.white;
                if (Mouse.IsOver(passion))
                {
                    Widgets.DrawHighlight(passion);
                    TooltipHandler.TipRegion(passion, "CAC_PassionTip".Translate(("CAC_Passion_" + record.passion).Translate()));
                }
                if (Widgets.ButtonInvisible(passion))
                {
                    bool lower = Event.current.button == 1;
                    PawnEditor.CyclePassion(record, lower);
                    (lower ? SoundDefOf.Tick_Low : SoundDefOf.Tick_High).PlayOneShotOnCamera();
                }
            }
            Rect bar = new Rect(passion.xMax + 6f, row.y + 2f, row.xMax - passion.xMax - 6f, row.height - 4f);
            if (!disabled)
            {
                Widgets.FillableBar(bar, Mathf.Clamp01(record.Level / 20f), CACUI.SkillBarFill, null, doBorder: false);
            }
            Rect level = new Rect(bar.x + 2f, bar.y, 40f, bar.height);
            if (disabled || editingSkill != record || !SkillLevelField(level))
            {
                using (new TextBlock(TextAnchor.MiddleLeft, disabled ? ColoredText.SubtleGrayColor : Color.white))
                {
                    Widgets.Label(new Rect(bar.x + 6f, bar.y, 60f, bar.height), disabled ? "-" : record.Level.ToString());
                }
                if (!disabled)
                {
                    if (Mouse.IsOver(level))
                    {
                        Widgets.DrawHighlight(level);
                    }
                    if (Widgets.ButtonInvisible(level))
                    {
                        if (editingSkill != null)
                        {
                            CommitSkillEdit();
                        }
                        editingSkill = record;
                        editingSkillPawn = pawn;
                        skillBuffer = record.Level.ToString();
                        skillFieldFocused = false;
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                }
            }
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
                string tip = record.def.LabelCap + "\n\n" + record.def.description;
                if (!disabled)
                {
                    tip += "\n\n" + "CAC_SkillLevelTip".Translate();
                }
                TooltipHandler.TipRegion(row, tip);
            }
        }
    }
}

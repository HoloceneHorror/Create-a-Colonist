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
        private void DrawRelationships(ref float y, float w)
        {
            Pawn me = selected;
            if (!me.RaceProps.Humanlike)
            {
                DrawPetBond(ref y, w, me);
                return;
            }

            CACUI.Note(ref y, w, "CAC_RelationsNote".Translate(me.LabelShortCap));
            List<Pawn> colonists = Colonists;
            List<Pawn> others = colonists.Where(p => p != me).ToList();
            if (others.Any())
            {
                CACUI.Section(ref y, w, "CAC_InYourColony".Translate());
                foreach (Pawn other in others)
                {
                    RelationRow(ref y, w, me, other);
                }
            }
            else
            {
                CACUI.Note(ref y, w, "CAC_NoOneElse".Translate());
            }

            if (state.pets.Any() && !me.DevelopmentalStage.Baby())
            {
                CACUI.Section(ref y, w, "CAC_Pets".Translate());
                foreach (Pawn pet in state.pets)
                {
                    PetBondRow(ref y, w, me, pet);
                }
            }

            List<DirectPawnRelation> outside = me.relations.DirectRelations
                .Where(r => r.otherPawn != null && !colonists.Contains(r.otherPawn) && !state.pets.Contains(r.otherPawn)).ToList();
            if (outside.Any())
            {
                CACUI.Section(ref y, w, "CAC_FamilyOutside".Translate());
                CACUI.Note(ref y, w, "CAC_FamilyOutsideNote".Translate());
                foreach (DirectPawnRelation rel in outside)
                {
                    Rect row = new Rect(0f, y, w, 28f);
                    Widgets.DrawHighlightIfMouseover(row);
                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(new Rect(row.x + 4f, row.y, row.width - 34f, row.height),
                            rel.def.GetGenderSpecificLabelCap(rel.otherPawn) + ": " + rel.otherPawn.LabelShortCap);
                    }
                    if (Widgets.ButtonImage(new Rect(row.xMax - 26f, row.y + 3f, 22f, 22f), CACTex.Dismiss))
                    {
                        me.relations.RemoveDirectRelation(rel);
                    }
                    y += 30f;
                }
            }
        }

        private void RelationRow(ref float y, float w, Pawn me, Pawn other)
        {
            Rect row = new Rect(0f, y, w, 64f);
            Widgets.DrawOptionBackground(row, false);
            Rect port = new Rect(row.x + 4f, row.y + 2f, 48f, 60f);
            GUI.DrawTexture(port, PortraitsCache.Get(other, port.size, Rot4.South, default(Vector3), 1.2f));
            Rect button = new Rect(row.xMax - 164f, row.y + 16f, 156f, 32f);
            float textX = port.xMax + 8f;
            float textW = Mathf.Min(190f, (button.x - textX) * 0.55f);
            string name = other.LabelShortCap;
            string title = other.story.TitleCap;
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft))
            {
                Widgets.Label(new Rect(textX, row.y + 8f, textW, 24f), name.Truncate(textW));
                textW = Mathf.Min(textW, Text.CalcSize(name).x);
            }
            using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, ColoredText.SubtleGrayColor))
            {
                float titleW = Mathf.Min(Mathf.Min(190f, (button.x - textX) * 0.55f), Text.CalcSize(title).x);
                Widgets.Label(new Rect(textX, row.y + 32f, titleW + 4f, 24f), title.Truncate(titleW + 4f));
                textW = Mathf.Max(textW, titleW);
            }

            PawnRelationDef current = PawnEditor.RelationBetween(me, other);
            // Read it as "<other> is <me>'s <relation>", on one line in whatever room is left.
            string currentLabel = current != null ? current.GetGenderSpecificLabelCap(other) : (string)"CAC_RelationNone".Translate();
            Rect isLabel = new Rect(textX + textW + 10f, row.y, button.x - 8f - (textX + textW + 10f), row.height);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleRight, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(isLabel, IsMyLabel(me.LabelShortCap, isLabel.width));
            }
            if (Widgets.ButtonText(button, currentLabel))
            {
                CACUI.FloatMenu(RelationOptions(me, other));
            }
            y += 68f;
        }

        /// <summary>
        /// "is Boneville's" that never wraps: if it's too wide, only the name is shortened, so the words around it
        /// (which sit before or after the name depending on the language) stay whole. Uses the current font.
        /// </summary>
        private static string IsMyLabel(string name, float width)
        {
            string full = "CAC_IsMy".Translate(name);
            if (Text.CalcSize(full).x <= width)
            {
                return full;
            }
            float around = Text.CalcSize("CAC_IsMy".Translate("")).x;
            string shortened = "CAC_IsMy".Translate(name.Truncate(Mathf.Max(width - around, 12f)));
            return Text.CalcSize(shortened).x <= width ? shortened : shortened.Truncate(width);
        }

        private IEnumerable<FloatMenuOption> RelationOptions(Pawn me, Pawn other)
        {
            yield return new FloatMenuOption("CAC_RelationNone".Translate(), () => PawnEditor.ClearRelationsBetween(me, other));
            if (PawnEditor.IsGrownUp(me) && PawnEditor.IsGrownUp(other))
            {
                foreach (PawnRelationDef def in PawnEditor.LoveRelations)
                {
                    PawnRelationDef d = def;
                    yield return new FloatMenuOption(d.GetGenderSpecificLabelCap(other), () => PawnEditor.SetLoveRelation(me, other, d));
                }
            }
            yield return new FloatMenuOption(PawnRelationDefOf.Sibling.GetGenderSpecificLabelCap(other), () => PawnEditor.SetSiblings(me, other));
            yield return ParentOption(PawnRelationDefOf.Parent.GetGenderSpecificLabelCap(other), me, other);
            yield return ParentOption(PawnRelationDefOf.Child.GetGenderSpecificLabelCap(other), other, me);
        }

        private static FloatMenuOption ParentOption(string label, Pawn child, Pawn parent)
        {
            return PawnEditor.OldEnoughToParent(parent, child)
                ? new FloatMenuOption(label, () => PawnEditor.SetParent(child, parent))
                : new FloatMenuOption(label + " (" + "CAC_TooYoungToBeParent".Translate(parent.LabelShortCap) + ")", null);
        }

        private static void PetBondRow(ref float y, float w, Pawn colonist, Pawn pet)
        {
            Rect row = new Rect(0f, y, w, 40f);
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.ThingIcon(new Rect(row.x + 4f, row.y + 4f, 32f, 32f), pet);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(row.x + 44f, row.y, row.width - 200f, row.height), pet.LabelShortCap + " (" + pet.kindDef.label + ")");
            }
            bool bonded = PawnEditor.GetBondedMaster(pet) == colonist;
            bool was = bonded;
            Widgets.CheckboxLabeled(new Rect(row.xMax - 150f, row.y + 8f, 146f, 24f), "CAC_Bonded".Translate(), ref bonded);
            if (bonded != was)
            {
                PawnEditor.SetBondedMaster(pet, bonded ? colonist : null);
            }
            y += 42f;
        }

        private void DrawPetBond(ref float y, float w, Pawn pet)
        {
            CACUI.Note(ref y, w, "CAC_BondNote".Translate(pet.LabelShortCap));
            Pawn master = PawnEditor.GetBondedMaster(pet);
            CACUI.Section(ref y, w, "CAC_BondedTo".Translate());
            Rect none = new Rect(0f, y, w, 40f);
            Widgets.DrawOptionBackground(none, master == null);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(none.x + 12f, none.y, none.width, none.height), "CAC_NoBond".Translate());
            }
            if (Widgets.ButtonInvisible(none))
            {
                PawnEditor.SetBondedMaster(pet, null);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            y += 44f;
            foreach (Pawn colonist in Colonists.Where(c => !c.DevelopmentalStage.Baby()))
            {
                Rect row = new Rect(0f, y, w, 64f);
                Widgets.DrawOptionBackground(row, master == colonist);
                Rect port = new Rect(row.x + 4f, row.y + 2f, 48f, 60f);
                GUI.DrawTexture(port, PortraitsCache.Get(colonist, port.size, Rot4.South, default(Vector3), 1.2f));
                using (new TextBlock(TextAnchor.MiddleLeft))
                {
                    Widgets.Label(new Rect(port.xMax + 8f, row.y, row.width - 100f, row.height), colonist.LabelShortCap);
                }
                if (master == colonist)
                {
                    GUI.DrawTexture(new Rect(row.xMax - 34f, row.center.y - 12f, 24f, 24f), CACTex.Bond);
                }
                if (Widgets.ButtonInvisible(row))
                {
                    PawnEditor.SetBondedMaster(pet, colonist);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                y += 68f;
            }
        }
    }
}

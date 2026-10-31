using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        private void DrawIdeoligion(ref float y, float w)
        {
            Pawn pawn = selected;
            Ideo ideo = pawn.Ideo;

            // The current belief: big icon, name and who else holds it.
            Rect card = new Rect(0f, y, w, 76f);
            Widgets.DrawOptionBackground(card, false);
            Rect icon = new Rect(card.x + 6f, card.y + 6f, 64f, 64f);
            ideo.DrawIcon(icon);
            using (new TextBlock(GameFont.Medium, TextAnchor.LowerLeft))
            {
                Widgets.Label(new Rect(icon.xMax + 10f, card.y + 4f, card.width - icon.width - 22f, 34f), ideo.name.CapitalizeFirst().Truncate(card.width - icon.width - 22f));
            }
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(icon.xMax + 10f, card.y + 40f, card.width - icon.width - 22f, 32f), PawnEditor.IdeoFollowers(ideo).Truncate(card.width - icon.width - 22f));
            }
            y += card.height + 8f;

            float half = (w - 6f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, y, half, 30f), "CAC_ChangeIdeo".Translate()))
            {
                OpenIdeoPicker(pawn);
            }
            if (Widgets.ButtonText(new Rect(half + 6f, y, half, 30f), "LoadExistingIdeoligion".Translate() + "..."))
            {
                OpenSavedIdeoPicker(pawn);
            }
            y += 34f;
            if (Widgets.ButtonText(new Rect(0f, y, half, 30f), "CAC_IdeoDetails".Translate()))
            {
                Find.WindowStack.Add(new Dialog_Ideo(ideo));
            }
            y += 38f;

            CACUI.Section(ref y, w, "Certainty".Translate().CapitalizeFirst());
            float certainty = pawn.ideo.Certainty;
            Rect slider = new Rect(0f, y, w, 24f);
            float edited = Widgets.HorizontalSlider(slider, certainty, 0f, 1f, middleAlignment: true, certainty.ToStringPercent(), roundTo: 0.01f);
            if (!Mathf.Approximately(edited, certainty))
            {
                PawnEditor.SetCertainty(pawn, edited);
            }
            y += 30f;
            CACUI.Note(ref y, w, "CAC_CertaintyNote".Translate(pawn.LabelShort));

            if (ideo.memes.Any())
            {
                CACUI.Section(ref y, w, "Memes".Translate().CapitalizeFirst());
                foreach (MemeDef meme in ideo.memes)
                {
                    Rect row = new Rect(0f, y, w, 30f);
                    GUI.DrawTexture(new Rect(row.x, row.y + 1f, 28f, 28f), meme.Icon);
                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(new Rect(row.x + 36f, row.y, row.width - 36f, row.height), meme.LabelCap);
                    }
                    if (Mouse.IsOver(row))
                    {
                        Widgets.DrawHighlight(row);
                        TooltipHandler.TipRegion(row, meme.LabelCap.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + meme.description);
                    }
                    y += 32f;
                }
            }

            if (!ideo.description.NullOrEmpty())
            {
                CACUI.Section(ref y, w, "CAC_IdeoStory".Translate());
                float h = Text.CalcHeight(ideo.description, w);
                Widgets.Label(new Rect(0f, y, w, h), ideo.description);
                y += h + 6f;
            }

            CACUI.Section(ref y, w, "CAC_ColonyIdeo".Translate());
            CACUI.Note(ref y, w, "CAC_ColonyIdeoNote".Translate());
        }

        private static void OpenIdeoPicker(Pawn pawn)
        {
            List<PickerOption> options = PawnEditor.WorldIdeos().Select(i => new PickerOption
            {
                label = i.name.CapitalizeFirst(),
                description = PawnEditor.IdeoSummary(i),
                selected = i == pawn.Ideo,
                extraSearch = i.memes.Select(m => m.label).ToCommaList(),
                drawIcon = r => i.DrawIcon(r),
                onPick = () => PawnEditor.SetIdeo(pawn, i)
            }).ToList();
            Find.WindowStack.Add(new Dialog_CACPicker("CAC_ChangeIdeo".Translate(), options));
        }

        private static void OpenSavedIdeoPicker(Pawn pawn)
        {
            List<(FileInfo file, Ideo ideo)> saved = PawnEditor.SavedIdeos();
            if (!saved.Any())
            {
                Messages.Message("CAC_NoSavedIdeos".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            List<PickerOption> options = saved.Select(s => new PickerOption
            {
                label = s.ideo.name.CapitalizeFirst(),
                description = "CAC_SavedIdeoFile".Translate(Path.GetFileNameWithoutExtension(s.file.Name)) + "\n\n" + PawnEditor.IdeoSummary(s.ideo),
                extraSearch = Path.GetFileNameWithoutExtension(s.file.Name),
                drawIcon = r => s.ideo.DrawIcon(r),
                onPick = () => PreLoadUtility.CheckVersionAndLoad(s.file.FullName, ScribeMetaHeaderUtility.ScribeHeaderMode.Ideo, delegate
                {
                    Ideo ideo = PawnEditor.UseSavedIdeo(s.file.FullName, s.ideo);
                    if (ideo != null)
                    {
                        PawnEditor.SetIdeo(pawn, ideo);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                })
            }).ToList();
            Find.WindowStack.Add(new Dialog_CACPicker("LoadExistingIdeoligion".Translate(), options));
        }
    }
}

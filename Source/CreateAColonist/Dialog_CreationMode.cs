using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>"Normal colony creation or Character Creation?" asked when New Colony is clicked.</summary>
    public class Dialog_CreationMode : Window
    {
        private readonly Page_SelectScenario scenarioPage;

        public override Vector2 InitialSize => new Vector2(760f, 440f);

        public Dialog_CreationMode(Page_SelectScenario scenarioPage)
        {
            this.scenarioPage = scenarioPage;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            doCloseX = false;
            layer = WindowLayer.Dialog;
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.UpperCenter))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 40f), "CAC_ModeTitle".Translate());
            }
            using (new TextBlock(GameFont.Small, TextAnchor.UpperCenter, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y + 40f, inRect.width, 26f), "CAC_ModeSubtitle".Translate());
            }

            float cardsTop = inRect.y + 80f;
            float cardH = inRect.height - 80f - 50f;
            float cardW = (inRect.width - 20f) / 2f;
            Rect normal = new Rect(inRect.x, cardsTop, cardW, cardH);
            Rect creator = new Rect(normal.xMax + 20f, cardsTop, cardW, cardH);

            if (Card(normal, CACTex.ModeNormal, "CAC_ModeNormal".Translate(), "CAC_ModeNormalDesc".Translate()))
            {
                Choose(false);
            }
            if (Card(creator, CACTex.ModeCreator, "CAC_ModeCreator".Translate(), "CAC_ModeCreatorDesc".Translate()))
            {
                Choose(true);
            }

            Rect back = new Rect(inRect.x, inRect.yMax - 38f, 150f, 38f);
            if (Widgets.ButtonText(back, "Back".Translate()))
            {
                Close();
                scenarioPage.Close();
            }
        }

        private void Choose(bool creator)
        {
            CreatorMode.Active = creator;
            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

        private static bool Card(Rect rect, Texture2D icon, string title, string desc)
        {
            Widgets.DrawOptionBackground(rect, false);
            MouseoverSounds.DoRegion(rect);
            Rect inner = rect.ContractedBy(14f);
            float iconSize = Mathf.Min(128f, inner.height - 110f);
            Rect iconRect = new Rect(inner.center.x - iconSize / 2f, inner.y + 4f, iconSize, iconSize);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            using (new TextBlock(GameFont.Medium, TextAnchor.UpperCenter))
            {
                Widgets.Label(new Rect(inner.x, iconRect.yMax + 8f, inner.width, 34f), title);
            }
            using (new TextBlock(GameFont.Small, TextAnchor.UpperCenter, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(inner.x, iconRect.yMax + 42f, inner.width, inner.yMax - iconRect.yMax - 42f), desc);
            }
            return Widgets.ButtonInvisible(rect);
        }
    }
}

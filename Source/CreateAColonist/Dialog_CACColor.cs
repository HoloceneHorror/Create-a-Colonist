using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    /// <summary>Colour wheel + brightness + preset swatches. Changes apply live so the colonist updates as you drag.</summary>
    public class Dialog_CACColor : Window
    {
        private readonly string title;
        private readonly List<Color> swatches;
        private readonly Action<Color> onChange;
        private readonly Color original;
        private Color color;
        private float value;
        private bool dragging;
        private Vector2 swatchScroll;
        private float swatchHeight;

        private const float SwatchSize = 26f;
        private const float SwatchPadding = 3f;
        private const float ButtonsH = 38f;

        // Tall enough for every swatch (big mod lists add a lot of hair colours), up to the screen's height; past
        // that the swatches scroll.
        public override Vector2 InitialSize
        {
            get
            {
                float swatchWidth = 560f - Margin * 2f - 240f;
                int perRow = Mathf.Max(1, Mathf.FloorToInt(swatchWidth / (SwatchSize + SwatchPadding * 2f)));
                float swatchesHeight = Mathf.CeilToInt(swatches.Count / (float)perRow) * (SwatchSize + SwatchPadding * 2f);
                float needed = Margin * 2f + 44f + Mathf.Max(330f, swatchesHeight) + 20f + ButtonsH;
                return new Vector2(560f, Mathf.Min(UI.screenHeight - 60f, needed));
            }
        }

        public Dialog_CACColor(string title, Color start, List<Color> swatches, Action<Color> onChange)
        {
            this.title = title;
            this.swatches = swatches ?? new List<Color>();
            this.onChange = onChange;
            original = start;
            color = start;
            Color.RGBToHSV(start, out _, out _, out value);
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), title);
            }
            Rect wheel = new Rect(inRect.x, inRect.y + 44f, 220f, 220f);
            Color before = color;
            Widgets.HSVColorWheel(wheel, ref color, ref dragging, value);

            Rect valueLabel = new Rect(inRect.x, wheel.yMax + 10f, 220f, 24f);
            Widgets.Label(valueLabel, "CAC_Brightness".Translate());
            float newValue = Widgets.HorizontalSlider(new Rect(inRect.x, valueLabel.yMax, 220f, 24f), value, 0.05f, 1f);
            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                Color.RGBToHSV(color, out float h, out float s, out _);
                color = Color.HSVToRGB(h, s, value);
            }

            Rect preview = new Rect(inRect.x, valueLabel.yMax + 34f, 220f, 40f);
            Widgets.DrawBoxSolidWithOutline(preview, color, Color.white * 0.6f);

            // Swatches stop above the buttons; when there are more than fit, they scroll.
            Rect swatchArea = new Rect(wheel.xMax + 20f, wheel.y, inRect.xMax - wheel.xMax - 20f, inRect.yMax - ButtonsH - 12f - wheel.y);
            Rect swatchView = new Rect(0f, 0f, swatchArea.width - 16f, Mathf.Max(swatchHeight, swatchArea.height));
            Widgets.BeginScrollView(swatchArea, ref swatchScroll, swatchView);
            Widgets.ColorSelector(new Rect(0f, 0f, swatchView.width, swatchView.height), ref color, swatches, out swatchHeight, null,
                (int)SwatchSize, (int)SwatchPadding);
            Widgets.EndScrollView();

            if (color != before)
            {
                Color.RGBToHSV(color, out _, out _, out value);
                onChange(color);
            }

            Rect cancel = new Rect(inRect.x, inRect.yMax - ButtonsH, 150f, ButtonsH);
            Rect reset = new Rect(inRect.center.x - 75f, inRect.yMax - ButtonsH, 150f, ButtonsH);
            Rect ok = new Rect(inRect.xMax - 150f, inRect.yMax - ButtonsH, 150f, ButtonsH);
            if (Widgets.ButtonText(cancel, "Cancel".Translate()))
            {
                Cancel();
            }
            if (Widgets.ButtonText(reset, "CAC_ResetColour".Translate()))
            {
                Reset();
            }
            if (Widgets.ButtonText(ok, "OK".Translate()))
            {
                Close();
            }
        }

        public void Cancel()
        {
            onChange(original);
            Close();
        }

        /// <summary>Back to the colour the window opened with, but stay open to keep choosing.</summary>
        public void Reset()
        {
            color = original;
            Color.RGBToHSV(color, out _, out _, out value);
            onChange(original);
        }
    }
}

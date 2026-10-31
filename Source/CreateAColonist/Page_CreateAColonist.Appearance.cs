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
        private int appearanceSub;
        private readonly QuickSearchWidget hairSearch = new QuickSearchWidget();

        private static List<string> AppearanceSubKeys()
        {
            List<string> keys = new List<string> { "CAC_Sub_Body", "CAC_Sub_Hair", "CAC_Sub_Beard" };
            if (ModsConfig.IdeologyActive)
            {
                keys.Add("CAC_Sub_Tattoos");
            }
            keys.Add("CAC_Sub_Colours");
            return keys;
        }

        private static List<string> AppearanceSubLabels()
        {
            return AppearanceSubKeys().Select(k => k.Translate().ToString()).ToList();
        }

        private void DrawAppearance(ref float y, float w)
        {
            List<string> keys = AppearanceSubKeys();
            appearanceSub = Mathf.Clamp(appearanceSub, 0, keys.Count - 1);
            switch (keys[appearanceSub])
            {
                case "CAC_Sub_Body": DrawBodySub(ref y, w); break;
                case "CAC_Sub_Hair": DrawHairSub(ref y, w); break;
                case "CAC_Sub_Beard": DrawBeardSub(ref y, w); break;
                case "CAC_Sub_Tattoos": DrawTattooSub(ref y, w); break;
                default: DrawColourSub(ref y, w); break;
            }
        }

        // ---------------------------------------------------------------- body

        private void DrawBodySub(ref float y, float w)
        {
            Pawn pawn = selected;
            CACUI.Section(ref y, w, "CAC_Gender".Translate());
            GenderButtons(ref y, pawn);
            if (ModsConfig.BiotechActive && pawn.DevelopmentalStage.Juvenile())
            {
                CACUI.Note(ref y, w, "CAC_ChildNote".Translate());
            }

            CACUI.Section(ref y, w, "CAC_BodyType".Translate());
            List<BodyTypeDef> bodies = PawnEditor.AllowedBodyTypes(pawn).ToList();
            float x = 0f;
            foreach (BodyTypeDef body in bodies)
            {
                string label = BodyLabel(body);
                float bw = Mathf.Max(70f, Text.CalcSize(label).x + 20f);
                if (x + bw > w)
                {
                    x = 0f;
                    y += 34f;
                }
                Rect r = new Rect(x, y, bw, 30f);
                Widgets.DrawOptionBackground(r, pawn.story.bodyType == body);
                using (new TextBlock(TextAnchor.MiddleCenter))
                {
                    Widgets.Label(r, label);
                }
                if (Widgets.ButtonInvisible(r))
                {
                    PawnEditor.SetBodyType(pawn, body);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                x += bw + 4f;
            }
            y += 38f;

            CACUI.Section(ref y, w, "CAC_HeadType".Translate());
            List<HeadTypeDef> heads = headList.Get(StyleKey(pawn), () => PawnEditor.AllowedHeads(pawn).ToList());
            CACUI.Grid(ref y, w, 64f, heads, h => h == pawn.story.headType,
                (r, h) =>
                {
                    GUI.color = pawn.story.SkinColor;
                    Widgets.DrawTextureFitted(r, CACUI.IconOf(h), 1.9f);
                    GUI.color = Color.white;
                },
                h => h.LabelCap.NullOrEmpty() ? h.defName : (string)h.LabelCap,
                h => PawnEditor.SetHead(pawn, h));

            if (pawn.RaceProps.Humanlike && FacialAnimationCompat.Active)
            {
                DrawFacialAnimation(ref y, w, pawn);
            }
        }

        // ---------------------------------------------------------------- Facial Animation (optional)

        private int faPart;
        private readonly CachedList<Def> faStyles = new CachedList<Def>();

        /// <summary>Part pills (head, eyes, brows, mouth, lids, skin) and a grid of that part's styles.</summary>
        private void DrawFacialAnimation(ref float y, float w, Pawn pawn)
        {
            List<FacialAnimationCompat.Part> parts = FacialAnimationCompat.Parts;
            CACUI.Section(ref y, w, "CAC_FacialAnimation".Translate());
            faPart = Mathf.Clamp(faPart, 0, parts.Count - 1);
            faPart = CACUI.Pills(new Rect(0f, y, w, 26f), parts.Select(p => ("CAC_FaPart_" + p.key).Translate().ToString()).ToList(), faPart);
            y += 32f;

            FacialAnimationCompat.Part part = parts[faPart];
            List<Def> styles = faStyles.Get(StyleKey(pawn) + "|" + part.key, () => FacialAnimationCompat.Styles(part, pawn));
            if (!styles.Any())
            {
                CACUI.Note(ref y, w, "CAC_FaNoStyles".Translate());
                return;
            }
            string current = FacialAnimationCompat.CurrentStyle(part, pawn);
            // Faces are small features on a big canvas, so they get bigger cells than whole heads.
            float cell = part.key == "Head" ? 64f : 90f;
            CACUI.Grid(ref y, w, cell, styles, d => d.defName == current,
                (r, d) => DrawFaThumbnail(r, part, d, pawn),
                d =>
                {
                    string tip = d.LabelCap.NullOrEmpty() ? d.defName : (string)d.LabelCap;
                    Gender gender = FacialAnimationCompat.GenderOf(part, d);
                    return gender == Gender.None ? tip : tip + "\n" + gender.GetLabel().CapitalizeFirst();
                },
                d => FacialAnimationCompat.SetStyle(part, pawn, d));
            if (part.key == "Eye")
            {
                CACUI.Note(ref y, w, "CAC_FaEyeColourNote".Translate());
            }
        }

        /// <summary>
        /// Facial Animation pictures are a 512 px face with the feature near the middle, so draw a zoomed-in crop.
        /// Features sit on a patch of the colonist's skin so dark mouths and brows stay readable.
        /// </summary>
        private static void DrawFaThumbnail(Rect r, FacialAnimationCompat.Part part, Def def, Pawn pawn)
        {
            Texture2D tex = FacialAnimationCompat.Thumbnail(part, def, pawn);
            if (tex == null)
            {
                using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter))
                {
                    Widgets.Label(r, def.LabelCap.NullOrEmpty() ? def.defName : (string)def.LabelCap);
                }
                return;
            }
            bool wholeHead = part.key == "Head";
            if (!wholeHead)
            {
                Widgets.DrawBoxSolid(r, pawn.story.SkinColor);
            }
            float crop = wholeHead ? 220f : 170f;
            float left = Mathf.Clamp(256f - crop / 2f, 0f, 512f - crop);
            float top = Mathf.Clamp((wholeHead ? 252f : 290f) - crop / 2f, 0f, 512f - crop);
            Rect uv = new Rect(left / 512f, 1f - (top + crop) / 512f, crop / 512f, crop / 512f);
            GUI.color = part.key == "Head" || part.key == "Skin" ? pawn.story.SkinColor
                : part.key == "Brow" ? pawn.story.HairColor : Color.white;
            GUI.DrawTextureWithTexCoords(r, tex, uv, true);
            GUI.color = Color.white;
        }

        private static string BodyLabel(BodyTypeDef body)
        {
            string key = "CAC_Body_" + body.defName;
            return key.CanTranslate() ? (string)key.Translate() : (body.label.NullOrEmpty() ? body.defName : body.LabelCap.ToString());
        }

        private static void GenderButtons(ref float y, Pawn pawn)
        {
            Rect male = new Rect(0f, y, 120f, 32f);
            Rect female = new Rect(male.xMax + 6f, y, 120f, 32f);
            GenderButton(male, pawn, Gender.Male, CACTex.Male);
            GenderButton(female, pawn, Gender.Female, CACTex.Female);
            y += 38f;
        }

        private static void GenderButton(Rect r, Pawn pawn, Gender gender, Texture2D icon)
        {
            Widgets.DrawOptionBackground(r, pawn.gender == gender);
            GUI.DrawTexture(new Rect(r.x + 6f, r.y + 6f, 20f, 20f), icon);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(r.x + 32f, r.y, r.width - 32f, r.height), gender.GetLabel(animal: !pawn.RaceProps.Humanlike).CapitalizeFirst());
            }
            if (Widgets.ButtonInvisible(r) && pawn.gender != gender)
            {
                PawnEditor.SetGender(pawn, gender);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        // ---------------------------------------------------------------- hair & beard

        private static bool StyleAllowed(Pawn pawn, StyleItemDef item)
        {
            if (item.requiredGene != null && (pawn.genes == null || !pawn.genes.HasActiveGene(item.requiredGene)))
            {
                return false;
            }
            return item.requiredMutant == null;
        }

        /// <summary>Everything that decides which styles a colonist can pick: who they are, their gender and genes.</summary>
        private static string StyleKey(Pawn pawn)
        {
            int geneKey = pawn.genes?.GenesListForReading.Sum(g => g.Active ? g.def.shortHash : 0) ?? 0;
            return pawn.thingIDNumber + "|" + pawn.gender + "|" + geneKey;
        }

        private readonly CachedList<HeadTypeDef> headList = new CachedList<HeadTypeDef>();
        private readonly CachedList<HairDef> hairList = new CachedList<HairDef>();
        private readonly CachedList<BeardDef> beardList = new CachedList<BeardDef>();
        private readonly CachedList<TattooDef> faceTattooList = new CachedList<TattooDef>();
        private readonly CachedList<TattooDef> bodyTattooList = new CachedList<TattooDef>();

        private void DrawHairSub(ref float y, float w)
        {
            Pawn pawn = selected;
            CACUI.Section(ref y, w, "CAC_HairColour".Translate());
            HairColourRow(ref y, w, pawn);

            CACUI.Section(ref y, w, "CAC_Hairstyle".Translate());
            hairSearch.OnGUI(new Rect(0f, y, Mathf.Min(260f, w), 24f));
            y += 30f;
            // Kept until the colonist, their gender, genes or the search change.
            List<HairDef> hairs = hairList.Get(StyleKey(pawn) + "|" + hairSearch.filter.Text, () => DefDatabase<HairDef>.AllDefs
                .Where(h => StyleAllowed(pawn, h) && hairSearch.filter.Matches(h.label ?? h.defName))
                .OrderByDescending(h => h.noGraphic)
                .ThenByDescending(h => GenderMatches(pawn, h.styleGender))
                .ThenBy(h => h.label)
                .ToList());
            CACUI.Grid(ref y, w, 64f, hairs, h => h == pawn.story.hairDef,
                (r, h) =>
                {
                    GUI.color = pawn.story.HairColor;
                    Widgets.DrawTextureFitted(r, CACUI.IconOf(h), 1.15f);
                    GUI.color = Color.white;
                },
                h => h.LabelCap,
                h => PawnEditor.SetHair(pawn, h));
        }

        private static int GenderMatches(Pawn pawn, StyleGender g)
        {
            if (g == StyleGender.Any || g == StyleGender.MaleUsually || g == StyleGender.FemaleUsually)
            {
                return 1;
            }
            return (pawn.gender == Gender.Male && g == StyleGender.Male) || (pawn.gender == Gender.Female && g == StyleGender.Female) ? 2 : 0;
        }

        private static List<Color> hairSwatches;

        private static List<Color> HairSwatches()
        {
            if (hairSwatches == null)
            {
                hairSwatches = DefDatabase<ColorDef>.AllDefs.Where(c => c.colorType == ColorType.Hair).OrderBy(c => c.displayOrder).Select(c => c.color).ToList();
                if (ModsConfig.BiotechActive)
                {
                    foreach (GeneDef g in DefDatabase<GeneDef>.AllDefs.Where(g => g.hairColorOverride.HasValue))
                    {
                        if (!hairSwatches.Any(c => CACUI.ColorsClose(c, g.hairColorOverride.Value)))
                        {
                            hairSwatches.Add(g.hairColorOverride.Value);
                        }
                    }
                }
            }
            return hairSwatches;
        }

        private static void HairColourRow(ref float y, float w, Pawn pawn)
        {
            if (CACUI.Swatches(ref y, w - 110f, HairSwatches(), pawn.story.HairColor, out Color picked))
            {
                PawnEditor.SetHairColor(pawn, picked);
            }
            Rect custom = new Rect(w - 104f, y - 27f, 104f, 26f);
            if (Widgets.ButtonText(custom, "CAC_Custom".Translate()))
            {
                Find.WindowStack.Add(new Dialog_CACColor("CAC_HairColour".Translate(), pawn.story.HairColor, HairSwatches(), c => PawnEditor.SetHairColor(pawn, c)));
            }
            y += 6f;
        }

        private void DrawBeardSub(ref float y, float w)
        {
            Pawn pawn = selected;
            if (pawn.style == null)
            {
                CACUI.Note(ref y, w, "CAC_NoStyle".Translate());
                return;
            }
            CACUI.Section(ref y, w, "CAC_HairColour".Translate());
            HairColourRow(ref y, w, pawn);
            CACUI.Section(ref y, w, "CAC_Beard".Translate());
            List<BeardDef> beards = beardList.Get(StyleKey(pawn), () => DefDatabase<BeardDef>.AllDefs.Where(b => StyleAllowed(pawn, b))
                .OrderByDescending(b => b.noGraphic).ThenBy(b => b.label).ToList());
            CACUI.Grid(ref y, w, 64f, beards, b => b == pawn.style.beardDef,
                (r, b) =>
                {
                    GUI.color = pawn.story.HairColor;
                    Widgets.DrawTextureFitted(r, CACUI.IconOf(b), 1.15f);
                    GUI.color = Color.white;
                },
                b => b.LabelCap,
                b => PawnEditor.SetBeard(pawn, b));
        }

        // ---------------------------------------------------------------- tattoos

        private void DrawTattooSub(ref float y, float w)
        {
            Pawn pawn = selected;
            if (pawn.style == null)
            {
                CACUI.Note(ref y, w, "CAC_NoStyle".Translate());
                return;
            }
            CACUI.Section(ref y, w, "CAC_FaceTattoo".Translate());
            TattooGrid(ref y, w, pawn, TattooType.Face, pawn.style.FaceTattoo, faceTattooList);
            CACUI.Section(ref y, w, "CAC_BodyTattoo".Translate());
            TattooGrid(ref y, w, pawn, TattooType.Body, pawn.style.BodyTattoo, bodyTattooList);
        }

        private static void TattooGrid(ref float y, float w, Pawn pawn, TattooType type, TattooDef current, CachedList<TattooDef> cache)
        {
            List<TattooDef> tattoos = cache.Get(StyleKey(pawn), () => DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == type && StyleAllowed(pawn, t))
                .OrderByDescending(t => t.noGraphic).ThenBy(t => t.label).ToList());
            CACUI.Grid(ref y, w, 64f, tattoos, t => t == current,
                (r, t) => Widgets.DrawTextureFitted(r, CACUI.IconOf(t), 1.1f),
                t => t.LabelCap,
                t => PawnEditor.SetTattoo(pawn, t));
        }

        // ---------------------------------------------------------------- colours

        private static List<Color> skinSwatches;

        private static List<Color> SkinSwatches()
        {
            if (skinSwatches == null)
            {
                skinSwatches = new List<Color>();
                if (ModsConfig.BiotechActive)
                {
                    IEnumerable<Color> natural = DefDatabase<GeneDef>.AllDefs.Where(g => g.skinColorBase.HasValue).Select(g => g.skinColorBase.Value)
                        .OrderByDescending(c => c.r + c.g + c.b);
                    IEnumerable<Color> unnatural = DefDatabase<GeneDef>.AllDefs.Where(g => g.skinColorOverride.HasValue).Select(g => g.skinColorOverride.Value);
                    foreach (Color c in natural.Concat(unnatural))
                    {
                        if (!skinSwatches.Any(s => CACUI.ColorsClose(s, c)))
                        {
                            skinSwatches.Add(c);
                        }
                    }
                }
                else
                {
                    for (float m = 0f; m <= 1.001f; m += 0.1f)
                    {
                        skinSwatches.Add(PawnSkinColors.GetSkinColor(m));
                    }
                }
            }
            return skinSwatches;
        }

        private void DrawColourSub(ref float y, float w)
        {
            Pawn pawn = selected;
            CACUI.Section(ref y, w, "CAC_SkinColour".Translate());
            if (CACUI.Swatches(ref y, w, SkinSwatches(), pawn.story.SkinColor, out Color picked))
            {
                PawnEditor.SetSkinColor(pawn, picked);
            }
            y += 4f;
            Rect custom = new Rect(0f, y, 140f, 28f);
            if (Widgets.ButtonText(custom, "CAC_Custom".Translate()))
            {
                Find.WindowStack.Add(new Dialog_CACColor("CAC_SkinColour".Translate(), pawn.story.SkinColor, SkinSwatches(), c => PawnEditor.SetSkinColor(pawn, c)));
            }
            Rect natural = new Rect(custom.xMax + 6f, y, 170f, 28f);
            if (pawn.story.skinColorOverride.HasValue && Widgets.ButtonText(natural, "CAC_NaturalSkin".Translate()))
            {
                PawnEditor.SetSkinColor(pawn, null);
            }
            y += 34f;

            CACUI.Section(ref y, w, "CAC_HairColour".Translate());
            HairColourRow(ref y, w, pawn);

            // Eye colour only exists as genes (Biotech, plus any mods that add eye genes).
            if (pawn.genes != null && PawnEditor.EyeGenes.Any())
            {
                CACUI.Section(ref y, w, "CAC_EyeColour".Translate());
                CACUI.Note(ref y, w, "CAC_EyeColourNote".Translate());
                GeneDef current = PawnEditor.CurrentEyeGene(pawn);
                List<GeneDef> options = new List<GeneDef> { null };
                options.AddRange(PawnEditor.EyeGenes);
                CACUI.Grid(ref y, w, 52f, options, g => g == current,
                    (r, g) =>
                    {
                        if (g == null)
                        {
                            using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter))
                            {
                                Widgets.Label(r, "CAC_NaturalEyesShort".Translate());
                            }
                        }
                        else
                        {
                            GUI.color = g.IconColor;
                            GUI.DrawTexture(r, g.Icon, ScaleMode.ScaleToFit);
                            GUI.color = Color.white;
                        }
                    },
                    g => g == null ? (string)"CAC_NaturalEyes".Translate() : g.LabelCap + "\n\n" + g.DescriptionFull,
                    g => PawnEditor.SetEyeGene(pawn, g));
            }

            if (FacialAnimationCompat.HasEyeColour(pawn))
            {
                DrawFaEyeColour(ref y, w, pawn);
            }
        }

        private static readonly List<Color> FaEyeSwatches = new List<Color>
        {
            new Color(0.35f, 0.55f, 0.85f), new Color(0.45f, 0.75f, 0.9f), new Color(0.3f, 0.7f, 0.7f), new Color(0.3f, 0.65f, 0.45f),
            new Color(0.55f, 0.65f, 0.4f), new Color(0.45f, 0.3f, 0.2f), new Color(0.25f, 0.15f, 0.1f), new Color(0.6f, 0.6f, 0.6f),
            new Color(0.85f, 0.65f, 0.25f), new Color(0.6f, 0.4f, 0.7f), new Color(0.75f, 0.2f, 0.2f), new Color(0.1f, 0.1f, 0.1f)
        };

        private int faEye;

        /// <summary>
        /// Facial Animation's iris colour, with an odd-eyes switch. With odd eyes on, pills pick which eye the
        /// swatches and the custom colour change.
        /// </summary>
        private void DrawFaEyeColour(ref float y, float w, Pawn pawn)
        {
            CACUI.Section(ref y, w, "CAC_FaEyeColour".Translate());
            bool odd = FacialAnimationCompat.HasOddEyes(pawn);
            bool wanted = odd;
            Widgets.CheckboxLabeled(new Rect(0f, y, Mathf.Min(220f, w), 28f), "CAC_OddEyes".Translate(), ref wanted, placeCheckboxNearText: true);
            y += 32f;
            if (wanted != odd)
            {
                FacialAnimationCompat.SetOddEyes(pawn, wanted);
                faEye = 0;
                odd = wanted;
            }
            if (odd)
            {
                faEye = CACUI.Pills(new Rect(0f, y, w, 26f), new List<string> { "CAC_FirstEye".Translate(), "CAC_SecondEye".Translate() }, faEye);
                y += 32f;
            }
            else
            {
                faEye = 0;
            }
            bool second = faEye == 1;
            Color current = second ? FacialAnimationCompat.SecondEyeColour(pawn) : FacialAnimationCompat.EyeColour(pawn);
            System.Action<Color> set = c =>
            {
                if (second)
                {
                    FacialAnimationCompat.SetSecondEyeColour(pawn, c);
                }
                else
                {
                    FacialAnimationCompat.SetEyeColour(pawn, c);
                }
            };
            if (CACUI.Swatches(ref y, w, FaEyeSwatches, current, out Color picked))
            {
                set(picked);
            }
            y += 4f;
            if (Widgets.ButtonText(new Rect(0f, y, 140f, 28f), "CAC_Custom".Translate()))
            {
                Find.WindowStack.Add(new Dialog_CACColor("CAC_FaEyeColour".Translate(), current, FaEyeSwatches, set));
            }
            y += 34f;
        }
    }
}

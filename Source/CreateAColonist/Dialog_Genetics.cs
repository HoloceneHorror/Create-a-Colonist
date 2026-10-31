using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>What the new colonist is to someone in their family.</summary>
    public enum KinRole
    {
        None,
        Child,
        Parent,
        Sibling
    }

    /// <summary>
    /// Makes a colonist from their family: say how they're related to the others, then roll until they look right.
    /// Whoever is rolled stays out of the group until added, and goes if they're rolled again or the window closes.
    /// </summary>
    public class Dialog_Genetics : Window
    {
        private readonly Page_CreateAColonist page;
        private readonly List<Pawn> relatives;
        private readonly List<Pawn> saved = new List<Pawn>();
        private readonly Dictionary<Pawn, KinRole> roles = new Dictionary<Pawn, KinRole>();
        private readonly GalleryThumbnails.Cache pictures = new GalleryThumbnails.Cache();
        private Pawn rolled;
        private Gender? gender;
        private int age, minAge, maxAge;
        private bool ageSet;
        private string ageText;
        private string problem;
        private Vector2 scroll;
        private float viewHeight;

        private const float RowH = 64f;
        private const float ResultW = 300f;

        public override Vector2 InitialSize => new Vector2(920f, Mathf.Min(680f, UI.screenHeight - 60f));

        public Pawn Rolled => rolled;

        public string Problem => problem;

        public Dialog_Genetics(Page_CreateAColonist page)
        {
            this.page = page;
            relatives = Find.GameInitData.startingAndOptionalPawns.Take(Find.GameInitData.startingPawnCount).ToList();
            FitAge(Family());
            absorbInputAroundWindow = true;
            forcePause = true;
            doCloseX = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            pictures.Clear();
            if (rolled != null)
            {
                PawnEditor.DiscardRelative(rolled);
            }
            saved.ForEach(PawnEditor.DiscardRelative);
        }

        private static int Age(Pawn pawn) => pawn.ageTracker.AgeBiologicalYears;

        private KinRole RoleOf(Pawn pawn) => roles.TryGetValue(pawn, KinRole.None);

        private Kin Family()
        {
            Kin kin = new Kin();
            foreach (Pawn p in relatives)
            {
                KinRole role = RoleOf(p);
                if (role != KinRole.None)
                {
                    (role == KinRole.Child ? kin.parents : role == KinRole.Parent ? kin.children : kin.siblings).Add(p);
                }
            }
            return kin;
        }

        // A sibling's parents living elsewhere keep the game's looser ages, as they do on the age box.
        private int ParentGap(Pawn parent) => relatives.Contains(parent) ? PawnEditor.GrownUpAge
            : (int)(parent.gender == Gender.Female ? ChildRelationUtility.MinFemaleAgeToHaveChildren : ChildRelationUtility.MinMaleAgeToHaveChildren);

        /// <summary>Parents have to be old enough for them, and they for any children. Until the age is set by hand, a likely one.</summary>
        private void FitAge(Kin kin)
        {
            minAge = PawnEditor.MinAgeYears(relatives[0]);
            maxAge = PawnEditor.MaxStartAge(relatives[0]);
            foreach (Pawn parent in kin.ActualParents)
            {
                maxAge = Mathf.Min(maxAge, Age(parent) - ParentGap(parent));
            }
            foreach (Pawn child in kin.children)
            {
                minAge = Mathf.Max(minAge, Age(child) + PawnEditor.GrownUpAge);
            }
            int wanted = ageSet ? age : kin.children.Count > 0 ? kin.children.Max(Age) + 25 : kin.siblings.Count > 0 ? Age(kin.siblings[0]) : 0;
            age = Mathf.Clamp(wanted, minAge, Mathf.Max(minAge, maxAge));
            ageText = null;
        }

        // A parent in the group stays: someone new only takes the place of a parent living elsewhere.
        private bool ParentTaken(Pawn child, Gender g) => PawnEditor.ParentsOf(child).Any(p => p.gender == g && relatives.Contains(p));

        private Gender? GenderToRoll(Kin kin)
        {
            if (gender.HasValue)
            {
                return gender;
            }
            foreach (Pawn child in kin.children)
            {
                if (ParentTaken(child, Gender.Male))
                {
                    return Gender.Female;
                }
                if (ParentTaken(child, Gender.Female))
                {
                    return Gender.Male;
                }
            }
            return null;
        }

        private string FindProblem(Kin kin, Gender? g)
        {
            if (kin.parents.Count > 2)
            {
                return "CAC_TwoParentsMax".Translate();
            }
            if (kin.siblings.Count > 0 && kin.parents.Count > 0)
            {
                return "CAC_SiblingOrParents".Translate();
            }
            foreach (Pawn other in kin.siblings.Skip(1))
            {
                if (!PawnRelationDefOf.Sibling.Worker.InRelation(kin.siblings[0], other))
                {
                    return "CAC_NotSiblings".Translate(kin.siblings[0].LabelShortCap, other.LabelShortCap);
                }
            }
            if (minAge > maxAge)
            {
                int youngest = PawnEditor.MinAgeYears(relatives[0]);
                Pawn tooYoung = kin.ActualParents.FirstOrDefault(p => relatives.Contains(p) && Age(p) - PawnEditor.GrownUpAge < youngest);
                return tooYoung != null ? "CAC_TooYoungToBeParent".Translate(tooYoung.LabelShortCap).ToString() : "CAC_NoAgeFits".Translate(PawnEditor.GrownUpAge).ToString();
            }
            Pawn full = kin.children.FirstOrDefault(c => g.HasValue ? ParentTaken(c, g.Value) : ParentTaken(c, Gender.Male) && ParentTaken(c, Gender.Female));
            return full != null ? "CAC_HasThatParent".Translate(full.LabelShortCap).ToString() : null;
        }

        /// <summary>Rolls someone new for the family as it stands, if it can be.</summary>
        public void Roll()
        {
            if (rolled != null)
            {
                PawnEditor.DiscardRelative(rolled);
                rolled = null;
            }
            Kin kin = Family();
            Gender? g = GenderToRoll(kin);
            problem = kin.Any ? FindProblem(kin, g) : null;
            if (!kin.Any || problem != null)
            {
                return;
            }
            try
            {
                rolled = PawnEditor.RollRelative(kin, age, g);
            }
            catch (Exception e)
            {
                Log.Warning("[Create a Colonist] Could not make a colonist from genetics: " + e);
                problem = "CAC_GeneticsFailed".Translate();
            }
        }

        public void SetRole(Pawn pawn, KinRole role)
        {
            roles[pawn] = role;
            FitAge(Family());
            Roll();
        }

        public void SetAge(int years)
        {
            age = years;
            ageSet = true;
            ageText = null;
            Roll();
        }

        public void SetGender(Gender g)
        {
            gender = g;
            Roll();
        }

        public void AddSaved(ColonistPreset preset)
        {
            Pawn pawn = PawnEditor.RelativeFrom(preset);
            if (pawn != null)
            {
                saved.Add(pawn);
                relatives.Add(pawn);
                SetRole(pawn, KinRole.Child);
            }
        }

        private void RemoveSaved(Pawn pawn)
        {
            saved.Remove(pawn);
            relatives.Remove(pawn);
            roles.Remove(pawn);
            PawnEditor.DiscardRelative(pawn);
            FitAge(Family());
            Roll();
        }

        public void AddToGroup()
        {
            Pawn pawn = rolled;
            rolled = null;
            Kin kin = Family();
            foreach (Pawn parent in saved.Where(kin.parents.Contains).ToList())
            {
                saved.Remove(parent);
                Find.WorldPawns.PassToWorld(parent, PawnDiscardDecideMode.KeepForever);
            }
            page.AddRelative(pawn, kin);
            Close();
        }

        private void PickSaved()
        {
            Find.WindowStack.Add(new Dialog_CACPicker("CAC_GeneticsSavedParent".Translate(), ColonistPreset.LoadAll().Select(p => new PickerOption
            {
                label = p.presetName,
                description = p.Summary,
                disabledReason = p.age < PawnEditor.GrownUpAge ? "CAC_TooYoungToBeParent".Translate(p.presetName).ToString() : null,
                drawIcon = r =>
                {
                    if (pictures.Get(p.filePath) is Texture2D tex)
                    {
                        GUI.DrawTexture(r, tex);
                    }
                },
                onPick = () => AddSaved(p)
            })));
        }

        private string RoleLabel(KinRole role)
        {
            PawnRelationDef def = role == KinRole.Child ? PawnRelationDefOf.Child : role == KinRole.Parent ? PawnRelationDefOf.Parent : role == KinRole.Sibling ? PawnRelationDefOf.Sibling : null;
            return def == null ? "CAC_RelationNone".Translate().ToString() : rolled != null ? def.GetGenderSpecificLabelCap(rolled) : (string)def.LabelCap;
        }

        // ---------------------------------------------------------------- drawing

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 36f), "CAC_GeneticsTitle".Translate());
            }
            Rect body = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
            DrawFamily(new Rect(body.x, body.y, body.width - ResultW - 14f, body.height));
            DrawResult(new Rect(body.xMax - ResultW, body.y, ResultW, body.height));
        }

        private void DrawFamily(Rect rect)
        {
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height - 42f);
            Rect view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(viewHeight, outer.height));
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;
            CACUI.Note(ref y, view.width, "CAC_GeneticsNote".Translate());
            CACUI.Section(ref y, view.width, "CAC_GeneticsTheirRelation".Translate());
            Pawn removed = null;
            foreach (Pawn pawn in relatives)
            {
                if (Row(ref y, view.width, pawn))
                {
                    removed = pawn;
                }
            }
            viewHeight = y;
            Widgets.EndScrollView();
            if (removed != null)
            {
                RemoveSaved(removed);
            }
            if (Widgets.ButtonText(new Rect(rect.x, rect.yMax - 36f, CACUI.FitWidth("CAC_GeneticsSavedParent".Translate(), 240f, 30f), 36f), "CAC_GeneticsSavedParent".Translate()))
            {
                PickSaved();
            }
        }

        /// <summary>Returns true when a saved colonist's row is dismissed.</summary>
        private bool Row(ref float y, float w, Pawn other)
        {
            Rect row = new Rect(0f, y, w, RowH);
            KinRole role = RoleOf(other);
            bool isSaved = saved.Contains(other);
            Widgets.DrawOptionBackground(row, role != KinRole.None);
            Rect port = new Rect(row.x + 4f, row.y + 2f, 48f, 60f);
            GUI.DrawTexture(port, PortraitsCache.Get(other, port.size, Rot4.South, default(Vector3), 1.2f));
            Rect button = new Rect(row.xMax - 164f, row.y + 16f, 156f, 32f);
            float textX = port.xMax + 8f, textW = button.x - textX - 8f;
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft))
            {
                Widgets.Label(new Rect(textX, row.y + 8f, textW, 24f), other.LabelShortCap.Truncate(textW));
            }
            using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(textX, row.y + 32f, textW, 24f), other.story.TitleCap.Truncate(textW));
            }
            y += RowH + 4f;
            if (isSaved)
            {
                // Someone from the gallery isn't here to be anything but a parent.
                using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
                {
                    Widgets.Label(button, RoleLabel(role));
                }
                return Widgets.ButtonImage(new Rect(row.xMax - 24f, row.y + 4f, 18f, 18f), CACTex.Dismiss);
            }
            if (Widgets.ButtonText(button, RoleLabel(role)))
            {
                CACUI.FloatMenu(new[] { KinRole.None, KinRole.Child, KinRole.Parent, KinRole.Sibling }
                    .Select(r => new FloatMenuOption(RoleLabel(r), () => SetRole(other, r))));
            }
            return false;
        }

        private void DrawResult(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            float h = Mathf.Min(320f, inner.height - 250f), w = h * 0.75f;
            Rect portrait = new Rect(inner.center.x - w / 2f, inner.y, w, h);
            float y = portrait.yMax + 4f;
            if (rolled != null)
            {
                GUI.DrawTexture(portrait, PortraitsCache.Get(rolled, portrait.size, Rot4.South, default(Vector3), 1f, supersample: true, compensateForUIScale: true,
                    renderHeadgear: true, renderClothes: true));
                using (new TextBlock(GameFont.Small, TextAnchor.UpperCenter))
                {
                    Widgets.Label(new Rect(inner.x, y, inner.width, 24f), rolled.Name.ToStringFull.Truncate(inner.width));
                }
                using (new TextBlock(GameFont.Tiny, TextAnchor.UpperCenter, ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(new Rect(inner.x, y + 24f, inner.width, 34f), Page_CreateAColonist.StageSubtitle(rolled));
                }
            }
            y += 62f;

            float half = (inner.width - 6f) / 2f;
            GenderButton(new Rect(inner.x, y, half, 32f), Gender.Male, CACTex.Male);
            GenderButton(new Rect(inner.x + half + 6f, y, half, 32f), Gender.Female, CACTex.Female);
            y += 40f;

            float x = inner.x;
            int chosen = Page_CreateAColonist.AgeBox(ref x, y, "CAC_Age".Translate(), "CAC_GeneticsAgeTip".Translate(), age, minAge, Mathf.Max(minAge, maxAge), 40f, ref ageText);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, ColoredText.SubtleGrayColor))
            {
                string stage = age == 0 ? "CAC_Newborn".Translate().ToString() : (string)PawnEditor.LifeStageAt(relatives[0].RaceProps, age).LabelCap;
                Widgets.Label(new Rect(x + 10f, y, inner.xMax - x - 10f, 24f), stage.Truncate(inner.xMax - x - 10f));
            }
            if (chosen != age)
            {
                SetAge(chosen);
            }
            y += 32f;

            string note = problem ?? (rolled == null ? (string)"CAC_GeneticsPickRelative".Translate() : null);
            if (note != null)
            {
                using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, problem != null ? ColorLibrary.RedReadable : ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(new Rect(inner.x, y, inner.width, inner.yMax - 44f - y), note);
                }
            }

            Rect add = new Rect(inner.x, inner.yMax - 38f, inner.width - 44f, 38f);
            if (Widgets.ButtonText(add, "CAC_GeneticsAdd".Translate(), active: rolled != null) && rolled != null)
            {
                AddToGroup();
                return;
            }
            if (rolled != null && CACUI.IconButton(new Rect(add.xMax + 6f, add.y, 38f, 38f), CACTex.Dice, "CAC_GeneticsRoll".Translate()))
            {
                Roll();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        private void GenderButton(Rect r, Gender g, Texture2D icon)
        {
            Widgets.DrawOptionBackground(r, (rolled?.gender ?? gender) == g);
            GUI.DrawTexture(new Rect(r.x + 6f, r.y + 6f, 20f, 20f), icon);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(r.x + 32f, r.y, r.width - 32f, r.height), g.GetLabel().CapitalizeFirst());
            }
            if (Widgets.ButtonInvisible(r))
            {
                SetGender(g);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }
    }
}

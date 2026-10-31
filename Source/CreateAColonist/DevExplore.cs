using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

// Developer test tooling. Only compiled into dev builds (build.ps1 -Dev); never part of the published mod.
#if CAC_DEV
namespace CreateAColonist
{
    /// <summary>
    /// -cacExplore: walks each feature through its edge cases, one scene at a time, with checks after each, while a
    /// layout lint watches every frame our windows draw (clipped text, overlapping controls). Any error the game logs
    /// during a scene fails it. Then the colony starts and the result is checked in the game.
    /// </summary>
    public static class DevExplore
    {
        public static int errors;
        public static Pawn kinBaby;
        public static int kinFood;
        public static string lastError;

        public static readonly Dictionary<Pawn, string> expectedGear = new Dictionary<Pawn, string>();
        public static readonly Dictionary<Pawn, string> expectedFaces = new Dictionary<Pawn, string>();
        public static readonly Dictionary<Pawn, string> expectedSkills = new Dictionary<Pawn, string>();

        public static string GearOf(Pawn p) => p.inventory.innerContainer
            .GroupBy(t => t.def.defName + "/" + t.Stuff?.defName + "/" + PawnEditor.QualityOf(t))
            .Select(g => g.Key + " x" + g.Sum(t => t.stackCount)).OrderBy(s => s).ToCommaList();

        public static string FacesOf(Pawn p) => FacialAnimationCompat.Parts.Select(part => part.key + "=" + FacialAnimationCompat.CurrentStyle(part, p)).ToCommaList();

        // Levels of the skills the colonist can use (a disabled skill's stored level is never shown or used).
        public static string SkillsOf(Pawn p) => p.skills.skills.Where(s => !s.TotallyDisabled).Select(s => s.def.defName + "=" + s.levelInt).ToCommaList();

        public static void CheckCarriedOver(List<Pawn> colonists)
        {
            if (!DevTest.Explore)
            {
                return;
            }
            foreach (Pawn p in colonists)
            {
                string Diff(Dictionary<Pawn, string> expected, Func<Pawn, string> now, string what)
                    => !expected.TryGetValue(p, out string e) || e == now(p) ? "" : " " + what + " DIFFERENT expected [" + e + "] got [" + now(p) + "]";
                string diffs = Diff(expectedGear, GearOf, "gear") + Diff(expectedFaces, FacesOf, "faces") + Diff(expectedSkills, SkillsOf, "hand-set skills");
                Log.Message("[CAC explore] in game " + p.LabelShort + ": " + (diffs.Length == 0 ? "gear, faces and hand-set skills as set -> PASS" : diffs + " -> FAIL"));
            }
        }
    }

    /// <summary>
    /// Layout lint: while one of our windows draws, every label is checked for text that doesn't fit its box, and
    /// every button, checkbox and text box is compared with the others drawn in the same window that frame.
    /// </summary>
    public static class DevLint
    {
        private struct Hit
        {
            public Rect rect;
            public string kind;
            public string text;
            public Window window;
        }

        public static Window window;
        public static string scene = "";
        public static readonly Dictionary<string, string> problems = new Dictionary<string, string>();
        private static readonly List<Hit> hits = new List<Hit>();
        private static int hitFrame;

        private static bool Watching => window != null && Event.current != null && Event.current.type == EventType.Repaint;

        private static void Problem(string what)
        {
            if (!problems.ContainsKey(what))
            {
                problems[what] = scene;
            }
        }

        public static void Label(Rect rect, string label)
        {
            if (!Watching || label.NullOrEmpty() || rect.width < 2f || rect.height < 2f || rect.height > 3000f)
            {
                return;
            }
            string plain = label.StripTags();
            bool clipped = Text.WordWrap ? Text.CalcHeight(plain, rect.width) > rect.height + 2f : Text.CalcSize(plain).x > rect.width + 2f;
            if (clipped)
            {
                Problem(window.GetType().Name + ": text doesn't fit (" + Text.Font + ", " + rect.width.ToString("0") + "x" + rect.height.ToString("0")
                    + (Text.WordWrap ? "" : ", one line") + "): \"" + plain.Replace("\n", " ").Truncate(400f) + "\"");
            }
        }

        public static void Control(Rect rect, string kind, string text)
        {
            if (!Watching)
            {
                return;
            }
            if (hitFrame != RealTime.frameCount)
            {
                Analyse();
                hits.Clear();
                hitFrame = RealTime.frameCount;
            }
            hits.Add(new Hit { rect = GUIUtility.GUIToScreenRect(rect), kind = kind, text = text?.StripTags() ?? "", window = window });
        }

        private static void Analyse()
        {
            for (int i = 0; i < hits.Count; i++)
            {
                for (int j = i + 1; j < hits.Count; j++)
                {
                    Hit a = hits[i], b = hits[j];
                    if (a.window != b.window || !a.rect.Overlaps(b.rect))
                    {
                        continue;
                    }
                    float w = Mathf.Min(a.rect.xMax, b.rect.xMax) - Mathf.Max(a.rect.x, b.rect.x);
                    float h = Mathf.Min(a.rect.yMax, b.rect.yMax) - Mathf.Max(a.rect.y, b.rect.y);
                    float smaller = Mathf.Min(a.rect.width * a.rect.height, b.rect.width * b.rect.height);
                    if (w > 2f && h > 2f && w * h > smaller * 0.2f && !(a.rect == b.rect && a.text == b.text))
                    {
                        Problem(a.window.GetType().Name + ": " + a.kind + " \"" + a.text.Truncate(200f) + "\" overlaps " + b.kind + " \"" + b.text.Truncate(200f) + "\"");
                    }
                }
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_LintWindows
    {
        public static bool Prepare() => DevTest.Explore;

        // Every window this mod adds.
        public static IEnumerable<MethodBase> TargetMethods() => typeof(Patch_LintWindows).Assembly.GetTypes()
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.GetMethod(nameof(Window.DoWindowContents), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m != null);

        public static void Prefix(Window __instance, out Window __state)
        {
            __state = DevLint.window;
            DevLint.window = __instance;
        }

        public static void Postfix(Window __state) => DevLint.window = __state;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), typeof(Rect), typeof(string))]
    public static class Patch_LintLabel
    {
        public static bool Prepare() => DevTest.Explore;

        public static void Prefix(Rect rect, string label) => DevLint.Label(rect, label);
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?))]
    public static class Patch_LintButton
    {
        public static bool Prepare() => DevTest.Explore;

        public static void Prefix(Rect rect, string label) => DevLint.Control(rect, "button", label);
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color), typeof(bool), typeof(string))]
    public static class Patch_LintImage
    {
        public static bool Prepare() => DevTest.Explore;

        public static void Prefix(Rect butRect, Texture2D tex) => DevLint.Control(butRect, "icon button", tex?.name);
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxLabeled))]
    public static class Patch_LintCheckbox
    {
        public static bool Prepare() => DevTest.Explore;

        public static void Prefix(Rect rect, string label) => DevLint.Control(rect, "checkbox", label);
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField), typeof(Rect), typeof(string), typeof(int), typeof(System.Text.RegularExpressions.Regex))]
    public static class Patch_LintTextField
    {
        public static bool Prepare() => DevTest.Explore;

        public static void Prefix(Rect rect, string text) => DevLint.Control(rect, "text box", text);
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Error), typeof(string))]
    public static class Patch_ExploreCountErrors
    {
        public static bool Prepare() => DevTest.Explore;

        public static void Postfix(string text)
        {
            DevExplore.errors++;
            DevExplore.lastError = text;
        }
    }

    public partial class Page_CreateAColonist
    {
        private class Scene
        {
            public string name;
            public Action act;
            public Action mid;
            public Action check;
            public bool shot;
            public int frames = 3;
        }

        private List<Scene> scenes;
        private int sceneIndex;
        private int sceneFrame;
        private int sceneErrors;
        private bool exploreDone;
        private int explorePassed;
        private readonly List<string> exploreFails = new List<string>();
        private readonly List<(double ms, string scene)> exploreSlow = new List<(double, string)>();
        private System.Diagnostics.Stopwatch exploreClock;

        private static readonly (CreatorTab tab, int sub)[] AllViews =
        {
            (CreatorTab.Personality, 0), (CreatorTab.Ideoligion, 0), (CreatorTab.Appearance, 0), (CreatorTab.Appearance, 1),
            (CreatorTab.Appearance, 2), (CreatorTab.Appearance, 3), (CreatorTab.Appearance, 4), (CreatorTab.Genes, 0),
            (CreatorTab.Health, 0), (CreatorTab.Clothing, 0), (CreatorTab.Inventory, 0), (CreatorTab.Inventory, 1),
            (CreatorTab.Relationships, 0)
        };

        /// <summary>Returns true while scenes are left; afterwards the tour's Play steps take over.</summary>
        private bool RunExplore()
        {
            if (exploreDone)
            {
                return false;
            }
            if (scenes == null)
            {
                exploreClock = System.Diagnostics.Stopwatch.StartNew();
                scenes = BuildScenes();
                // Nothing draws without a window, so only the scenes about behaviour can run.
                scenes = DevTest.Headless ? scenes.Where(x => x.name.StartsWith("kin_") || x.name.StartsWith("skill_bonus") || x.name.StartsWith("skill_penalty")).ToList() : scenes;
                Log.Message("[CAC explore] start: " + scenes.Count + " scenes, Facial Animation " + (FacialAnimationCompat.Active ? "on" : "off")
                    + ", " + ItemDefs.Count + " items, " + LanguageDatabase.activeLanguage?.folderName);
            }
            Scene s = scenes[sceneIndex];
            sceneFrame++;
            int checkFrame = s.shot ? 24 : s.frames;
            if (sceneFrame == 1)
            {
                DevLint.scene = s.name;
                sceneErrors = DevExplore.errors;
                Attempt(s, "act", s.act);
                DevPerf.Reset();
            }
            else if (sceneFrame == 2)
            {
                Attempt(s, "mid", s.mid);
            }
            else if (s.shot && sceneFrame == checkFrame - 2)
            {
                Messages.Clear();
                DevTest.Shot("explore_" + s.name);
            }
            else if (sceneFrame >= checkFrame)
            {
                double page = DevPerf.Max("page");
                exploreSlow.Add((page, s.name));
                int failsBefore = exploreFails.Count;
                Attempt(s, "check", s.check);
                if (DevExplore.errors != sceneErrors)
                {
                    Fail(s, (DevExplore.errors - sceneErrors) + " error(s) logged, last: " + DevExplore.lastError?.Split('\n')[0]);
                }
                if (exploreFails.Count == failsBefore)
                {
                    explorePassed++;
                }
                CloseDialogs();
                sceneIndex++;
                sceneFrame = 0;
                if (sceneIndex >= scenes.Count)
                {
                    FinishExplore();
                    return false;
                }
            }
            return true;
        }

        private void Attempt(Scene s, string part, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Fail(s, part + " threw " + e.GetType().Name + ": " + e.Message + " at " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
            }
        }

        private void Fail(Scene s, string note)
        {
            exploreFails.Add(s.name + ": " + note);
            Log.Message("[CAC explore] FAIL " + s.name + ": " + note);
        }

        private Scene current => scenes[sceneIndex];

        private void Expect(bool ok, string what)
        {
            if (!ok)
            {
                Fail(current, what);
            }
        }

        private void FinishExplore()
        {
            exploreDone = true;
            CloseDialogs();
            StopSkillEdit();
            Log.Message("[CAC explore] done: " + scenes.Count + " scenes in " + exploreClock.Elapsed.TotalSeconds.ToString("0") + " s, "
                + explorePassed + " passed, " + exploreFails.Count + " failure(s)");
            foreach (string f in exploreFails)
            {
                Log.Message("[CAC explore]   FAILED " + f);
            }
            Log.Message("[CAC explore] layout lint: " + DevLint.problems.Count + " problem(s)");
            foreach (KeyValuePair<string, string> kv in DevLint.problems.OrderBy(kv => kv.Key).Take(80))
            {
                Log.Message("[CAC explore]   LINT " + kv.Key + "  (first in " + kv.Value + ")");
            }
            Log.Message("[CAC explore] slowest frames: " + exploreSlow.OrderByDescending(x => x.ms).Take(6).Select(x => x.scene + " " + x.ms.ToString("0.0") + " ms").ToCommaList());

            foreach (Pawn p in Colonists)
            {
                while (PawnEditor.OverCarryLimit(p) && p.inventory.innerContainer.Count > 0)
                {
                    PawnEditor.RemoveInventoryThing(p, p.inventory.innerContainer[p.inventory.innerContainer.Count - 1]);
                }
                DevExplore.expectedGear[p] = DevExplore.GearOf(p);
                if (FacialAnimationCompat.Active)
                {
                    DevExplore.expectedFaces[p] = DevExplore.FacesOf(p);
                }
                if (PawnEditor.HasManualSkills(p))
                {
                    DevExplore.expectedSkills[p] = DevExplore.SkillsOf(p);
                }
            }
            Log.Message("[CAC explore] summary " + (exploreFails.Count == 0 ? "PASS" : "FAIL") + ", " + DevLint.problems.Count + " lint problem(s)");
        }

        private void Show(Pawn p, CreatorTab t, int sub = 0, bool bottom = false)
        {
            Select(p);
            tab = t;
            if (t == CreatorTab.Appearance)
            {
                appearanceSub = sub;
            }
            if (t == CreatorTab.Inventory)
            {
                inventorySub = sub;
            }
            panelScroll = bottom ? new Vector2(0f, 99999f) : Vector2.zero;
        }

        // ---------------------------------------------------------------- the scenes

        private List<Scene> BuildScenes()
        {
            List<Scene> list = new List<Scene>();
            void Add(string name, Action act, Action check = null, bool shot = false, Action mid = null, int frames = 3)
                => list.Add(new Scene { name = name, act = act, check = check, shot = shot, mid = mid, frames = frames });

            List<Pawn> group = Colonists;
            Pawn a = group[0], b = group[1 % group.Count], c = group[group.Count - 1];

            // The lint must catch a planted clipped label and two overlapping buttons, or its silence means nothing.
            Add("lint_self_test", () =>
            {
                Widgets.Label(new Rect(0f, 0f, 20f, 10f), "CAC lint test: far too long to fit");
                Widgets.ButtonText(new Rect(0f, 0f, 60f, 30f), "CAC lint A");
                Widgets.ButtonText(new Rect(10f, 5f, 60f, 30f), "CAC lint B");
            }, () =>
            {
                List<string> planted = DevLint.problems.Keys.Where(k => k.Contains("CAC lint")).ToList();
                Expect(planted.Any(k => k.Contains("doesn't fit")) && planted.Any(k => k.Contains("overlaps")), "the lint missed planted problems: " + planted.ToCommaList());
                planted.ForEach(k => DevLint.problems.Remove(k));
            }, false, null, 3);

            // Every view of every colonist, top and bottom, for the layout lint.
            foreach (Pawn p in group)
            {
                foreach ((CreatorTab t, int sub) in AllViews)
                {
                    Add("view_" + p.LabelShort + "_" + t + sub, () => Show(p, t, sub));
                    Add("view_" + p.LabelShort + "_" + t + sub + "_bottom", () => Show(p, t, sub, bottom: true));
                }
            }

            AddSkillScenes(Add, a, b);
            AddGearScenes(list, Add, a, b, c);
            AddFilterScenes(Add, a);
            if (FacialAnimationCompat.Active)
            {
                AddFaceScenes(list, Add, a, b);
            }
            AddColourScenes(Add, a);
            AddKinScenes(Add, a, b, c);
            return list;
        }

        private void AddSkillScenes(Action<string, Action, Action, bool, Action, int> Add, Pawn a, Pawn b)
        {
            SkillRecord rec = null;
            int formula = 0;
            Add("skill_box_open", () =>
            {
                PawnEditor.RecalculateSkills(a, force: true);
                rec = a.skills.skills.First(s => !s.TotallyDisabled && s.Aptitude == 0);
                formula = rec.Level;
                Show(a, CreatorTab.Personality);
                editingSkill = rec;
                editingSkillPawn = a;
                skillBuffer = formula == 12 ? "13" : "12";
                skillFieldFocused = false;
            }, () => Expect(editingSkill == rec && skillFieldFocused, "the level box didn't open with focus (open=" + (editingSkill == rec) + " focused=" + skillFieldFocused + ")"), true, null, 3);
            Add("skill_box_enter", () => CommitSkillEdit(), () =>
            {
                int want = formula == 12 ? 13 : 12;
                Expect(rec.Level == want && PawnEditor.HasManualSkills(a), "Enter gave " + rec.Level + " (wanted " + want + "), hand-set=" + PawnEditor.HasManualSkills(a));
                Expect(editingSkill == null, "the box stayed open after Enter");
            }, false, null, 3);
            int before = 0;
            Add("skill_box_escape", () =>
            {
                before = rec.Level;
                editingSkill = rec;
                editingSkillPawn = a;
                skillBuffer = "3";
            }, () => Expect(rec.Level == before && editingSkill == null, "Escape changed the level or left the box open"), false, () => StopSkillEdit(), 3);
            Add("skill_box_empty", () =>
            {
                before = rec.Level;
                editingSkill = rec;
                editingSkillPawn = a;
                skillBuffer = "";
            }, () => Expect(rec.Level == before, "an empty box changed the level to " + rec.Level), false, () => CommitSkillEdit(), 3);
            string bSkills = "";
            Add("skill_box_switch_colonist", () =>
            {
                before = rec.Level;
                bSkills = DevExplore.SkillsOf(b);
                editingSkill = rec;
                editingSkillPawn = a;
                skillBuffer = "5";
            }, () =>
            {
                Expect(editingSkill == null, "the box stayed open after picking another colonist");
                Expect(rec.Level == before && DevExplore.SkillsOf(b) == bSkills, "picking another colonist mid-edit typed into someone");
            }, false, () => Select(b), 4);

            void Aptitude(string name, Func<GeneDef, bool> which, int typed, Func<SkillRecord, int> expected)
            {
                Gene gene = null;
                SkillRecord boosted = null;
                Add(name, () =>
                {
                    GeneDef def = ModsConfig.BiotechActive ? DefDatabase<GeneDef>.AllDefs.FirstOrDefault(g => !g.aptitudes.NullOrEmpty() && which(g) && !a.skills.GetSkill(g.aptitudes[0].skill).TotallyDisabled) : null;
                    if (def == null)
                    {
                        return;
                    }
                    gene = a.genes.AddGene(def, xenogene: true);
                    a.skills.DirtyAptitudes();
                    boosted = a.skills.GetSkill(def.aptitudes[0].skill);
                    Show(a, CreatorTab.Personality);
                    editingSkill = boosted;
                    editingSkillPawn = a;
                    skillBuffer = typed.ToString();
                    skillFieldFocused = false;
                }, () =>
                {
                    if (boosted == null)
                    {
                        return;
                    }
                    CommitSkillEdit();
                    int want = expected(boosted);
                    Expect(boosted.Level == want, boosted.def.defName + (boosted.TotallyDisabled ? " (this colonist can't use it)" : "") + " bonus " + boosted.Aptitude + ", typed " + typed + ": shows " + boosted.Level + ", wanted " + want);
                    a.genes.RemoveGene(gene);
                    a.skills.DirtyAptitudes();
                }, false, null, 3);
            }
            Aptitude("skill_bonus_type_0", g => g.aptitudes[0].level > 0, 0, r => r.Aptitude);
            Aptitude("skill_bonus_type_20", g => g.aptitudes[0].level > 0, 20, r => 20);
            Aptitude("skill_penalty_type_20", g => g.aptitudes[0].level < 0, 20, r => 20 + r.Aptitude);
            Aptitude("skill_penalty_type_0", g => g.aptitudes[0].level < 0, 0, r => 0);

            string typedSkills = "";
            // Every stored level, disabled skills too: in hand-set mode nothing may recalculate any of them.
            string AllLevels(Pawn p) => p.skills.skills.Select(s => s.levelInt.ToString()).ToCommaList();
            Add("skill_handset_survives_edits", () =>
            {
                PawnEditor.SetSkillLevel(a, rec, 17);
                typedSkills = AllLevels(a);
                foreach (BackstorySlot slot in new[] { BackstorySlot.Childhood, BackstorySlot.Adulthood })
                {
                    BackstoryDef other = PawnEditor.BackstoryOptions(a, slot).FirstOrDefault(x => x != (slot == BackstorySlot.Childhood ? a.story.Childhood : a.story.Adulthood)
                        && x.workDisables == WorkTags.None);
                    if (other != null)
                    {
                        PawnEditor.SetBackstory(a, slot, other);
                    }
                }
                PawnEditor.SetBiologicalAge(a, a.ageTracker.AgeBiologicalYears + 7);
                if (ModsConfig.BiotechActive)
                {
                    PawnEditor.SetXenotype(a, XenotypeDefOf.Baseliner);
                }
                Show(a, CreatorTab.Personality);
            }, () => Expect(PawnEditor.HasManualSkills(a) && AllLevels(a) == typedSkills,
                "hand-set levels changed by backstory/age/xenotype edits: [" + typedSkills + "] -> [" + AllLevels(a) + "]"), true, null, 3);

            string loadedInto = "";
            Add("skill_handset_preset_roundtrip", () =>
            {
                typedSkills = DevExplore.SkillsOf(a);
                ColonistPreset saved = ColonistPreset.From(a, "CAC Explore skills", withFamily: false);
                saved.Save();
                ColonistPreset loaded = ColonistPreset.Load(saved.filePath);
                DeletePreset(saved.filePath);
                loaded.ApplyTo(b, withFamily: false);
                loadedInto = DevExplore.SkillsOf(b);
                Show(b, CreatorTab.Personality);
            }, () => Expect(PawnEditor.HasManualSkills(b) && loadedInto == typedSkills && DevExplore.SkillsOf(b) == typedSkills,
                "saved and loaded: [" + typedSkills + "] -> [" + loadedInto + "] now [" + DevExplore.SkillsOf(b) + "]"), false, null, 3);
            Add("skill_old_preset_not_handset", () =>
            {
                ColonistPreset old = ColonistPreset.From(b, "CAC Explore old", withFamily: false);
                old.manualSkills = false;
                old.ApplyTo(b, withFamily: false);
            }, () => Expect(!PawnEditor.HasManualSkills(b), "a preset without hand-set levels turned them on"), false, null, 3);
            Add("skill_recalculate_button", () => PawnEditor.RecalculateSkills(a, force: true), () =>
            {
                Expect(!PawnEditor.HasManualSkills(a), "recalculating left hand-set levels on");
                string once = DevExplore.SkillsOf(a);
                PawnEditor.RecalculateSkills(a, force: true);
                Expect(DevExplore.SkillsOf(a) == once, "recalculating twice gave different levels");
            }, false, null, 3);
            Add("skill_disabled_row_no_box", () =>
            {
                Pawn p = Colonists.FirstOrDefault(x => x.skills.skills.Any(s => s.TotallyDisabled));
                SkillRecord off = p?.skills.skills.First(s => s.TotallyDisabled);
                if (off == null)
                {
                    return;
                }
                Show(p, CreatorTab.Personality);
                editingSkill = off;
                editingSkillPawn = p;
                skillBuffer = "9";
                skillFieldFocused = false;
            }, () =>
            {
                Expect(!skillFieldFocused, "a disabled skill's row opened a level box");
                StopSkillEdit();
            }, false, null, 3);
            // Leave one colonist with hand-set levels for the colony start check.
            Add("skill_handset_for_start", () => { PawnEditor.SetSkillLevel(a, rec, 14); Show(a, CreatorTab.Personality); }, null, false, null, 2);
        }

        private void AddGearScenes(List<Scene> list, Action<string, Action, Action, bool, Action, int> Add, Pawn a, Pawn b, Pawn c)
        {
            ThingDef Named(string n) => DefDatabase<ThingDef>.GetNamedSilentFail(n);
            List<ThingDef> kit = new[] { "MedicineIndustrial", "Gun_Revolver", "Gun_AssaultRifle", "MeleeWeapon_LongSword", "Apparel_Duster", "Apparel_FlakVest" }
                .Select(Named).Where(d => d != null).ToList();
            Add("gear_add_kit", () =>
            {
                b.inventory.innerContainer.ClearAndDestroyContents();
                foreach (ThingDef d in kit)
                {
                    PawnEditor.AddInventory(b, d, d.stackLimit > 1 ? 5 : 1, withinCarryLimit: false);
                }
                Show(b, CreatorTab.Inventory);
            }, () =>
            {
                Expect(b.inventory.innerContainer.Count == kit.Count, "kit has " + b.inventory.innerContainer.Count + " rows, wanted " + kit.Count);
                foreach (Thing t in b.inventory.innerContainer)
                {
                    Expect(PawnEditor.HasVariants(t) == (t.def.stackLimit <= 1 && (t.def.MadeFromStuff || t.TryGetComp<CompQuality>() != null)), t.def.defName + " has the wrong kind of row");
                }
                Expect(!PawnEditor.HasVariants(b.inventory.innerContainer[0]), "medicine got material/quality dropdowns instead of a count");
            }, true, null, 3);
            List<string> order = null;
            Add("gear_replace_keeps_row", () =>
            {
                order = b.inventory.innerContainer.Select(t => t.def.defName).ToList();
                Thing middle = b.inventory.innerContainer.First(t => t.def.MadeFromStuff && t.def.IsWeapon);
                ThingDef stuff = GenStuff.AllowedStuffsFor(middle.def).OrderByDescending(s => s.BaseMarketValue).First();
                PawnEditor.ReplaceInventoryThing(b, middle, stuff, QualityCategory.Legendary);
            }, () =>
            {
                Expect(b.inventory.innerContainer.Select(t => t.def.defName).SequenceEqual(order), "rows moved: " + order.ToCommaList() + " -> " + b.inventory.innerContainer.Select(t => t.def.defName).ToCommaList());
                Thing changed = b.inventory.innerContainer.First(t => t.def.MadeFromStuff && t.def.IsWeapon);
                Expect(PawnEditor.QualityOf(changed) == QualityCategory.Legendary, "quality didn't change");
            }, false, null, 3);
            Add("gear_info_card_item", () =>
            {
                Thing gun = b.inventory.innerContainer.FirstOrDefault(t => t.def.IsRangedWeapon);
                if (gun != null)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(gun));
                }
            }, () => Expect(Find.WindowStack.IsOpen<Dialog_InfoCard>(), "the info card didn't open"), true, null, 3);
            Add("gear_info_card_supply", () => Find.WindowStack.Add(new Dialog_InfoCard(ThingDefOf.MedicineIndustrial, null)), null, false, null, 3);
            string kitGear = "";
            Add("gear_preset_roundtrip", () =>
            {
                kitGear = DevExplore.GearOf(b);
                ColonistPreset saved = ColonistPreset.From(b, "CAC Explore gear", withFamily: false);
                saved.Save();
                ColonistPreset loaded = ColonistPreset.Load(saved.filePath);
                DeletePreset(saved.filePath);
                loaded.ApplyTo(c, withFamily: false);
                Show(c, CreatorTab.Inventory);
            }, () => Expect(DevExplore.GearOf(c) == kitGear, "carried gear came back different: [" + kitGear + "] -> [" + DevExplore.GearOf(c) + "]"), false, null, 3);

            // Every item the add-item picker offers: make it, carry it, draw its row.
            List<ThingDef> all = ItemDefs;
            for (int i = 0; i < all.Count; i += 15)
            {
                List<ThingDef> batch = all.Skip(i).Take(15).ToList();
                Add("every_item_" + i, () =>
                {
                    c.inventory.innerContainer.ClearAndDestroyContents();
                    foreach (ThingDef d in batch)
                    {
                        int errorsBefore = DevExplore.errors;
                        try
                        {
                            PawnEditor.AddInventory(c, d, 1, withinCarryLimit: false);
                            Thing t = c.inventory.innerContainer.FirstOrDefault(x => x.def == d);
                            if (t == null)
                            {
                                Fail(current, d.defName + " wasn't added");
                                continue;
                            }
                            string label = t.LabelCap + t.GetStatValue(StatDefOf.Mass);
                            ColonistPreset.ThingEntry.From(t);
                        }
                        catch (Exception e)
                        {
                            Fail(current, d.defName + " threw " + e.GetType().Name + ": " + e.Message);
                        }
                        if (DevExplore.errors != errorsBefore)
                        {
                            Fail(current, d.defName + " logged: " + DevExplore.lastError?.Split('\n')[0]);
                        }
                    }
                    Show(c, CreatorTab.Inventory, 0, bottom: i % 30 == 15);
                }, null, false, null, 2);
            }
            Add("every_item_clear", () => c.inventory.innerContainer.ClearAndDestroyContents(), null, false, null, 2);
            // One info card per kind of item.
            foreach (ThingDef d in all.GroupBy(d => d.thingClass).Select(g => g.First()))
            {
                Add("info_card_" + d.thingClass.Name, () => Find.WindowStack.Add(new Dialog_InfoCard(d, GenStuff.DefaultStuffFor(d))), null, false, null, 2);
            }
        }

        private static void DeletePreset(string path)
        {
            foreach (string file in new[] { path, GalleryThumbnails.PathFor(path) })
            {
                if (System.IO.File.Exists(file))
                {
                    System.IO.File.Delete(file);
                }
            }
        }

        private void AddFilterScenes(Action<string, Action, Action, bool, Action, int> Add, Pawn a)
        {
            string[] names = { "items", "supplies", "backstory", "traits", "install", "conditions", "addictions", "pets" };
            for (int which = 0; which < names.Length; which++)
            {
                int w = which;
                Dialog_CACPicker picker = null;
                Traverse t = null;
                List<ModContentPack> mods = null;
                void Open()
                {
                    Show(a, CreatorTab.Personality);
                    switch (w)
                    {
                        case 0: OpenItemPicker("CAC_AddItem".Translate(), ItemDefs, d => { }, null); break;
                        case 1: OpenItemPicker("CAC_AddSupply".Translate(), SupplyDefs, d => { }, null); break;
                        case 2: OpenBackstoryPicker(a, BackstorySlot.Childhood, "Childhood".Translate()); break;
                        case 3: OpenTraitPicker(a); break;
                        case 4: OpenHealthPicker(a, "install", PawnEditor.InstallOptions(a)); break;
                        case 5: OpenHealthPicker(a, "conditions", PawnEditor.ConditionOptions(a)); break;
                        case 6: OpenHealthPicker(a, "addictions", PawnEditor.AddictionOptions(a)); break;
                        default: OpenPetSpeciesPicker(); break;
                    }
                    picker = Find.WindowStack.WindowOfType<Dialog_CACPicker>();
                    t = Traverse.Create(picker);
                    mods = t.Field("mods").GetValue<List<ModContentPack>>();
                }
                // The base game, the biggest mods and the smallest ones.
                List<int> picks = new List<int>();
                for (int k = 0; k < 9; k++)
                {
                    int m = k;
                    ModContentPack chosen = null;
                    Add("filter_" + names[w] + "_" + k, () =>
                    {
                        Open();
                        if (mods.Count < 2)
                        {
                            return;
                        }
                        List<PickerOption> options = t.Field("options").GetValue<List<PickerOption>>();
                        List<ModContentPack> bySize = mods.OrderByDescending(x => options.Count(o => o.mod == x)).ToList();
                        List<ModContentPack> wanted = new[] { mods[0] }.Concat(bySize.Take(4)).Concat(bySize.AsEnumerable().Reverse().Take(4)).Distinct().ToList();
                        chosen = m < wanted.Count ? wanted[m] : null;
                        t.Field("modFilter").SetValue(chosen);
                    }, () =>
                    {
                        if (picker == null)
                        {
                            Fail(current, "picker didn't open");
                            return;
                        }
                        List<PickerOption> options = t.Field("options").GetValue<List<PickerOption>>();
                        List<PickerOption> shown = Traverse.Create(t.Field("shownList").GetValue()).Field("list").GetValue<List<PickerOption>>();
                        bool workFilter = t.Field("offerWorkFilter").GetValue<bool>();
                        int want = options.Count(o => chosen == null || o.mod == chosen);
                        Expect(shown != null && shown.All(o => chosen == null || o.mod == chosen), "filter " + chosen?.Name + " lists other mods' things");
                        Expect(shown != null && (workFilter ? shown.Count <= want : shown.Count == want), "filter " + (chosen?.Name ?? "All") + " lists " + shown?.Count + " of " + want);
                        Expect(mods.Count < 2 || mods[0].IsCoreMod || !mods.Any(x => x.IsCoreMod), "the base game isn't first in the mod list");
                    }, w == 0 && k == 1, null, 3);
                }
            }
            List<ModContentPack> apparel = CACUI.ModsOf(AllApparel.Select(d => d.modContentPack));
            foreach (ModContentPack mod in apparel.Take(6).Concat(new ModContentPack[] { null }))
            {
                Add("filter_clothing_" + (mod?.PackageId ?? "all"), () => { Show(a, CreatorTab.Clothing); apparelMod = mod; }, () =>
                {
                    List<ThingDef> shown = Traverse.Create(apparelOptions).Field("list").GetValue<List<ThingDef>>();
                    Expect(shown != null && (mod == null || shown.All(d => d.modContentPack == mod)), "clothing filter " + mod?.Name + " shows other mods' clothes");
                }, mod != null && mod.IsCoreMod, null, 3);
            }
        }

        private void AddFaceScenes(List<Scene> list, Action<string, Action, Action, bool, Action, int> Add, Pawn a, Pawn b)
        {
            List<FacialAnimationCompat.Part> parts = FacialAnimationCompat.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                int index = i;
                FacialAnimationCompat.Part part = parts[i];
                Add("face_grid_" + part.key, () => { Show(a, CreatorTab.Appearance, 0, bottom: true); faPart = index; }, () =>
                {
                    List<Def> styles = FacialAnimationCompat.Styles(part, a);
                    Expect(styles.Count > 0, "no " + part.key + " styles for " + a.LabelShort);
                    int pictures = styles.Count(d => FacialAnimationCompat.Thumbnail(part, d, a) != null);
                    Log.Message("[CAC explore] face " + part.key + ": " + styles.Count + " styles, " + pictures + " with pictures");
                }, part.key == "Eye" || part.key == "Mouth" || part.key == "Head", null, 3);
                Add("face_every_style_" + part.key, () =>
                {
                    foreach (Def d in FacialAnimationCompat.Styles(part, a))
                    {
                        FacialAnimationCompat.SetStyle(part, a, d);
                        if (FacialAnimationCompat.CurrentStyle(part, a) != d.defName)
                        {
                            Fail(current, "setting " + d.defName + " left " + FacialAnimationCompat.CurrentStyle(part, a));
                        }
                    }
                }, null, false, null, 3);
            }
            Add("face_gender_switch", () =>
            {
                Gender was = a.gender;
                PawnEditor.SetGender(a, was == Gender.Male ? Gender.Female : Gender.Male);
                Show(a, CreatorTab.Appearance, 0, bottom: true);
            }, () =>
            {
                foreach (FacialAnimationCompat.Part part in parts)
                {
                    string now = FacialAnimationCompat.CurrentStyle(part, a);
                    Expect(now == null || FacialAnimationCompat.Styles(part, a).Any(d => d.defName == now),
                        "after a gender change the " + part.key + " style " + now + " isn't one Facial Animation allows for " + a.gender);
                }
            }, true, null, 3);
            Add("face_eye_colours", () =>
            {
                Show(a, CreatorTab.Appearance, 4, bottom: true);
                FacialAnimationCompat.SetOddEyes(a, false);
                FacialAnimationCompat.SetEyeColour(a, new Color(0.35f, 0.55f, 0.85f));
            }, () =>
            {
                Expect(CACUI.ColorsClose(FacialAnimationCompat.SecondEyeColour(a), FacialAnimationCompat.EyeColour(a)), "without odd eyes the second eye didn't follow");
                FacialAnimationCompat.SetOddEyes(a, true);
                FacialAnimationCompat.SetSecondEyeColour(a, new Color(0.85f, 0.65f, 0.25f));
                Expect(FacialAnimationCompat.HasOddEyes(a) && !CACUI.ColorsClose(FacialAnimationCompat.SecondEyeColour(a), FacialAnimationCompat.EyeColour(a)), "odd eyes didn't take");
            }, false, null, 3);
            Add("face_odd_eyes_section", () => Show(a, CreatorTab.Appearance, 4, bottom: true), null, true, null, 3);
            string faces = "";
            Add("face_preset_roundtrip", () =>
            {
                faces = DevExplore.FacesOf(a) + " odd=" + FacialAnimationCompat.HasOddEyes(a) + " " + FacialAnimationCompat.EyeColour(a) + " " + FacialAnimationCompat.SecondEyeColour(a);
                ColonistPreset saved = ColonistPreset.From(a, "CAC Explore face", withFamily: false);
                saved.Save();
                ColonistPreset loaded = ColonistPreset.Load(saved.filePath);
                DeletePreset(saved.filePath);
                loaded.ApplyTo(b, withFamily: false);
                Show(b, CreatorTab.Appearance, 0, bottom: true);
            }, () =>
            {
                string got = DevExplore.FacesOf(b) + " odd=" + FacialAnimationCompat.HasOddEyes(b) + " " + FacialAnimationCompat.EyeColour(b) + " " + FacialAnimationCompat.SecondEyeColour(b);
                Expect(got == faces, "face came back different: [" + faces + "] -> [" + got + "]");
            }, false, null, 3);
            Add("face_child", () =>
            {
                PawnEditor.SetBiologicalAge(b, 8);
                Show(b, CreatorTab.Appearance, 0, bottom: true);
            }, () => PawnEditor.SetBiologicalAge(b, 30), true, null, 3);
        }

        private void AddColourScenes(Action<string, Action, Action, bool, Action, int> Add, Pawn a)
        {
            Dialog_CACColor dialog = null;
            Color start = default;
            Add("colour_window", () =>
            {
                Show(a, CreatorTab.Appearance, 1);
                start = a.story.HairColor;
                dialog = new Dialog_CACColor("CAC_HairColour".Translate(), start, HairSwatches(), col => PawnEditor.SetHairColor(a, col));
                Find.WindowStack.Add(dialog);
            }, () =>
            {
                Expect(dialog.windowRect.yMax <= UI.screenHeight + 1f && dialog.windowRect.y >= -1f, "the colour window doesn't fit the screen: " + dialog.windowRect);
                Traverse.Create(dialog).Field("onChange").GetValue<Action<Color>>()(Color.red);
                Expect(CACUI.ColorsClose(a.story.HairColor, Color.red), "picking a colour didn't change the hair");
                dialog.Reset();
                Expect(dialog.IsOpen && CACUI.ColorsClose(a.story.HairColor, start), "Reset didn't bring the colour back or closed the window");
                Traverse.Create(dialog).Field("onChange").GetValue<Action<Color>>()(Color.green);
                dialog.Cancel();
                Expect(!dialog.IsOpen && CACUI.ColorsClose(a.story.HairColor, start), "Cancel didn't bring the colour back or close the window");
            }, true, () => Traverse.Create(dialog).Field("swatchScroll").SetValue(new Vector2(0f, 99999f)), 3);
        }
    }
}
#endif

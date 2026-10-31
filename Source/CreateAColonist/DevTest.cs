using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

// Developer test tooling. Only compiled into dev builds (build.ps1 -Dev); never part of the published mod.
#if CAC_DEV
namespace CreateAColonist
{
    /// <summary>
    /// Developer-only test helpers, active only with command-line flags:
    ///   -cacTest   build a tiny Crashlanded world and open the creator straight from the main menu
    ///   -cacTour   additionally click through every tab, start the game, and screenshot each step
    ///   -cacExplore walk every feature through its edge cases with checks and a layout lint (DevExplore.cs),
    ///              then start the game and check the result carried over
    /// Screenshots land in the save-data folder's Screenshots directory.
    /// </summary>
    public static class DevTest
    {
        public static bool QuickTest => GenCommandLine.CommandLineArgPassed("cacTest");
        public static bool Headless => GenCommandLine.CommandLineArgPassed("Headless");
        // The exploration run (DevExplore.cs) uses the tour's machinery: timing, Play, and the checks once the colony lands.
        public static bool Tour => GenCommandLine.CommandLineArgPassed("cacTour") || Explore;
        public static bool Explore => GenCommandLine.CommandLineArgPassed("cacExplore");
        public static bool MenuTest => GenCommandLine.CommandLineArgPassed("cacMenu");
        public static bool Promo => GenCommandLine.CommandLineArgPassed("cacPromo");

        private static int menuFrame = -1;

        /// <summary>
        /// -cacMenu: open New Colony from the main menu (our popup should appear), screenshot it, pick "Normal",
        /// then build a quick world and screenshot the character page, which should be plain vanilla.
        /// </summary>
        public static void MenuTestOnGUI()
        {
            if (!MenuTest || Event.current.type != EventType.Repaint || LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }
            menuFrame++;
            if (menuFrame == 30)
            {
                Find.WindowStack.Add(new Page_SelectScenario());
            }
            else if (menuFrame == 90)
            {
                Shot("00_newcolony_popup");
            }
            else if (menuFrame == 110)
            {
                Find.WindowStack.WindowOfType<Dialog_CreationMode>()?.Close(doCloseSound: false);
                Find.WindowStack.WindowOfType<Page_SelectScenario>()?.Close(doCloseSound: false);
                Log.Message("[CAC test] chose Normal; CreatorMode.Active=" + CreatorMode.Active);
                vanillaRun = true;
                StartQuickTest();
            }
        }

        private static bool vanillaRun;
        private static int vanillaFrame = -1;

        public static void VanillaPageOnGUI(Page_CreateAColonist page)
        {
            if (!vanillaRun || Event.current.type != EventType.Repaint)
            {
                return;
            }
            vanillaFrame++;
            if (vanillaFrame == 60)
            {
                Messages.Clear();
                Shot("00b_normal_mode_page");
            }
        }

        private static bool started;
        private static int inGameCountdown = -1;
        private static bool podsRunning;

        // After the tour: leave the colony for the main menu and see what of it is still in memory.
        private static System.WeakReference<Game> leftGame;
        private static System.WeakReference<Pawn> leftPawn;
        private static int leaveCountdown = -1;
        private static System.Threading.Timer leftTimer;

        private static void LeaveColony()
        {
            leftGame = new System.WeakReference<Game>(Current.Game);
            leftPawn = new System.WeakReference<Pawn>(Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault());
            Log.Message("[CAC test] leaving the colony for the main menu");
            GenScene.GoToMainMenu();
        }

        private static void ReportLeftGame()
        {
            if (leftGame != null)
            {
                // A few seconds in, so nothing from the scene change is still on a stack.
                leftTimer = new System.Threading.Timer(_ =>
                {
                    long before = GC.GetTotalMemory(false);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Log.Message("[CAC test] left colony: game still in memory=" + leftGame.TryGetTarget(out Game _) + ", colonist still in memory=" + leftPawn.TryGetTarget(out Pawn _)
                        + ", managed heap " + before / (1024 * 1024) + " -> " + GC.GetTotalMemory(false) / (1024 * 1024) + " MB");
                }, null, 4000, System.Threading.Timeout.Infinite);
            }
        }

        public static void StartQuickTest()
        {
            if (started)
            {
                ReportLeftGame();
                return;
            }
            started = true;
            LongEventHandler.QueueLongEvent(delegate
            {
                Current.ProgramState = ProgramState.Entry;
                Game.ClearCaches();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.Scenario = ScenarioDefOf.Crashlanded.scenario;
                Find.Scenario.PreConfigure();
                Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
                Current.Game.World = WorldGenerator.GenerateWorld(0.05f, GenText.RandomSeedString(), OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, LandmarkDensity.Normal);
                Find.GameInitData.ChooseRandomStartingTile();
                Find.GameInitData.mapSize = 200;
                Find.GameInitData.startedFromEntry = true;
                Find.Scenario.PostIdeoChosen();
                // Registered from inside this event, so it runs when the world is ready.
                LongEventHandler.ExecuteWhenFinished(OpenCreator);
            }, "GeneratingWorld", doAsynchronously: true, null);
        }

        private static void OpenCreator()
        {
            CreatorMode.Active = !vanillaRun;
            Page_CreateAColonist page = new Page_CreateAColonist();
            page.nextAct = delegate
            {
                PageUtility.InitGameStart();
                if (Tour)
                {
                    LongEventHandler.ExecuteWhenFinished(() => inGameCountdown = 120);
                }
            };
            Find.WindowStack.Add(page);
        }

        public static void Shot(string name)
        {
            if (!Headless)
            {
                ScreenshotTaker.TakeNonSteamShot("cac_" + name);
            }
            Log.Message("[CAC test] screenshot " + name);
        }

        /// <summary>Runs every frame in play; once the new colony is up, let the drop pods land, then report and screenshot.</summary>
        public static void InGameOnGUI()
        {
            if (Event.current.type == EventType.Repaint && leaveCountdown > 0 && --leaveCountdown == 0)
            {
                LeaveColony();
                return;
            }
            if (inGameCountdown < 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            inGameCountdown--;
            if (inGameCountdown == 119 && !podsRunning)
            {
                podsRunning = true;
                foreach (Window w in Find.WindowStack.Windows.ToList())
                {
                    if (w is Dialog_NodeTree || w is Dialog_MessageBox)
                    {
                        w.Close(doCloseSound: false);
                    }
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                inGameCountdown = 600;
            }
            if (inGameCountdown == 60)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Messages.Clear();
                Map map = Find.CurrentMap;
                if (map != null)
                {
                    List<Pawn> colonists = map.mapPawns.FreeColonists.ToList();
                    List<Pawn> animals = map.mapPawns.AllPawns.Where(p => p.Faction == Faction.OfPlayer && p.RaceProps.Animal).ToList();
                    Log.Message("[CAC test] colony started: colonists=" + colonists.Count + " (" + colonists.Select(p => p.LabelShort).ToCommaList() + ")"
                        + ", pets=" + animals.Count + " (" + animals.Select(a => a.LabelShort + " the " + a.kindDef.label + (a.playerSettings?.Master != null ? " bonded to " + a.playerSettings.Master.LabelShort : "")).ToCommaList() + ")");
                    foreach (Pawn p in colonists)
                    {
                        Log.Message("[CAC test]   " + p.LabelShort + ": inventory=" + p.inventory.innerContainer.Select(t => t.LabelCap).ToCommaList()
                            + "; weapon=" + (p.equipment.Primary?.LabelCap ?? "none") + "; apparel=" + p.apparel.WornApparel.Select(a => a.def.label).ToCommaList());
                    }
                    DevExplore.CheckCarriedOver(colonists);
                    DevKin.CheckInGame(colonists);
                    Log.Message("[CAC test] scenario parts after start: " + CreatorState.PartsOf(Find.Scenario).Select(s => s.GetType().Name).ToCommaList() + "; world pawns=" + Find.WorldPawns.AllPawnsAliveOrDead.Count);
                    foreach (Pawn p in colonists)
                    {
                        Log.Message("[CAC test]   " + p.LabelShort + ": ideo=" + p.Ideo?.name + " certainty=" + p.ideo?.Certainty.ToStringPercent()
                            + "; health=" + p.health.hediffSet.hediffs.Where(h => h.Visible).Select(h => (h.Part?.Label ?? "body") + "/" + h.Label).ToCommaList());
                    }
                    ThingDef uraniumDef = DefDatabase<ThingDef>.GetNamed("Uranium");
                    Log.Message("[CAC test] weight at start: " + colonists.Select(p => p.LabelShort + " " + PawnEditor.CarriedMass(p).ToStringMass() + "/" + PawnEditor.CarryCapacity(p).ToStringMass()
                        + (PawnEditor.OverCarryLimit(p) ? " OVER" : "") + " uranium=" + p.inventory.innerContainer.Where(t => t.def == uraniumDef).Sum(t => t.stackCount)).ToCommaList()
                        + "; uranium on the ground=" + map.listerThings.ThingsOfDef(uraniumDef).Sum(t => t.stackCount));
                    if (ModsConfig.IdeologyActive)
                    {
                        FactionIdeosTracker ideos = Faction.OfPlayer.ideos;
                        Log.Message("[CAC test] colony ideos: primary=" + ideos.PrimaryIdeo?.name + " minor=" + ideos.IdeosMinorListForReading.Select(i => i.name).ToCommaList()
                            + " world=" + Find.IdeoManager.IdeosListForReading.Select(i => i.name).ToCommaList()
                            + " unusedCreedGone=" + !Find.IdeoManager.IdeosListForReading.Any(i => i.name == "Unused Creed"));
                    }
                    Find.CameraDriver.SetRootPosAndSize(colonists.FirstOrDefault()?.Position.ToVector3Shifted() ?? map.Center.ToVector3Shifted(), 18f);
                }
            }
            if (inGameCountdown == 0)
            {
                Shot("20_colony");
                if (!Explore)
                {
                    leaveCountdown = 30;
                }
            }
        }
    }

    /// <summary>
    /// -cacTour timing: how long the creator page and the pickers on top of it take to draw, per frame. All IMGUI
    /// events in a frame (layout, repaint, input) are added up, since together they are what a frame costs.
    /// </summary>
    public static class DevPerf
    {
        private static readonly System.Diagnostics.Stopwatch Watch = new System.Diagnostics.Stopwatch();
        private static long startBytes;
        private static readonly Dictionary<string, (double total, double max, double frame, int frames, int lastFrame, long bytes, long frameBytes)> Data =
            new Dictionary<string, (double, double, double, int, int, long, long)>();

        public static void Begin()
        {
            startBytes = GC.GetAllocatedBytesForCurrentThread();
            Watch.Restart();
        }

        public static void End(string what)
        {
            double ms = Watch.Elapsed.TotalMilliseconds;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
            Data.TryGetValue(what, out var d);
            if (d.lastFrame != RealTime.frameCount)
            {
                if (d.frame > 0)
                {
                    d.total += d.frame;
                    d.max = Math.Max(d.max, d.frame);
                    d.bytes += d.frameBytes;
                    d.frames++;
                }
                d.frame = 0;
                d.frameBytes = 0;
                d.lastFrame = RealTime.frameCount;
            }
            d.frame += ms;
            d.frameBytes += bytes;
            Data[what] = d;
        }

        public static void Reset()
        {
            Data.Clear();
        }

        public static double Max(string what) => Data.TryGetValue(what, out var d) ? Math.Max(d.max, d.frame) : 0;

        public static string Report()
        {
            return Data.Where(kv => kv.Value.frames > 0)
                .Select(kv => kv.Key + " avg " + (kv.Value.total / kv.Value.frames).ToString("0.00") + " ms, max " + kv.Value.max.ToString("0.00") + " ms, "
                    + (kv.Value.bytes / 1024.0 / kv.Value.frames).ToString("0.0") + " KB/frame")
                .ToCommaList();
        }

        /// <summary>Time and memory of one action (opening a picker, a snapshot...), averaged over the runs.</summary>
        public static void Bench(string name, Action act, int runs = 1)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++)
            {
                act();
            }
            watch.Stop();
            Log.Message("[CAC bench] " + name + ": " + (watch.Elapsed.TotalMilliseconds / runs).ToString("0.0000") + " ms, "
                + ((GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024.0 / runs).ToString("0.00") + " KB");
        }
    }

    [HarmonyPatch(typeof(Page_CreateAColonist), nameof(Page_CreateAColonist.DoWindowContents))]
    public static class Patch_PerfPage
    {
        public static bool Prepare() => DevTest.Tour;

        public static void Prefix() => DevPerf.Begin();

        public static void Postfix() => DevPerf.End("page");
    }

    [HarmonyPatch]
    public static class Patch_PerfDialogs
    {
        public static bool Prepare() => DevTest.Tour;

        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            return new[] { typeof(Dialog_CACPicker), typeof(Dialog_Gallery), typeof(Dialog_CACColor), typeof(Dialog_WorkRequirements) }
                .Select(t => (System.Reflection.MethodBase)AccessTools.Method(t, nameof(Window.DoWindowContents)));
        }

        public static void Prefix() => DevPerf.Begin();

        public static void Postfix(System.Reflection.MethodBase __originalMethod) => DevPerf.End(__originalMethod.DeclaringType.Name);
    }

    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
    public static class Patch_UIRootEntry_Init
    {
        public static void Postfix()
        {
            if (DevTest.QuickTest)
            {
                // The main menu is created while game data is still loading; start once that's done.
                LongEventHandler.ExecuteWhenFinished(DevTest.StartQuickTest);
            }
        }
    }

    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootOnGUI))]
    public static class Patch_UIRootEntry_OnGUI
    {
        public static bool Prepare()
        {
            return DevTest.MenuTest;
        }

        public static void Postfix()
        {
            DevTest.MenuTestOnGUI();
        }
    }

    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_UIRootPlay_OnGUI
    {
        public static bool Prepare()
        {
            return DevTest.Tour;
        }

        public static void Postfix()
        {
            DevTest.InGameOnGUI();
        }
    }

    /// <summary>-cacPromo: full-screen lineup of the group on a flat background, for the Workshop preview image.</summary>
    public class Dialog_PromoLineup : Window
    {
        public static readonly Color Background = new Color(0.118f, 0.129f, 0.141f);
        private readonly List<Pawn> pawns;
        private readonly float spacing;
        private readonly Rot4 facing;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        protected override float Margin => 0f;

        public Dialog_PromoLineup(List<Pawn> pawns, float spacing = 0.62f, Rot4? facing = null)
        {
            this.pawns = pawns;
            this.spacing = spacing;
            this.facing = facing ?? Rot4.South;
            layer = WindowLayer.Super;
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, Background);
            float h = inRect.height * 0.92f;
            float w = h * 0.75f;
            float step = Mathf.Min(w * spacing, (inRect.width - w) / Mathf.Max(1, pawns.Count - 1));
            float total = step * (pawns.Count - 1) + w;
            float x = inRect.center.x - total / 2f;
            foreach (Pawn p in pawns)
            {
                Rect r = new Rect(x, inRect.yMax - h, w, h);
                if (!GenCommandLine.CommandLineArgPassed("cacPromoSingles"))
                {
                    // Cut-outs for composing get no floor shadow; it would stay behind as a smudge.
                    Rect shadow = new Rect(r.center.x - w * 0.22f, r.y + h * 0.73f, w * 0.44f, h * 0.08f);
                    GUI.DrawTexture(shadow, CACTex.Shadow);
                }
                GUI.DrawTexture(r, PortraitsCache.Get(p, new Vector2(w, h), facing, default(Vector3), 1f, supersample: true, compensateForUIScale: true, renderHeadgear: true, renderClothes: true));
                x += step;
            }
        }
    }

    public partial class Page_CreateAColonist
    {
        private static string PawnFingerprint(Pawn p, List<Pawn> colonists)
        {
            return string.Join(" | ", new[]
            {
                p.Name?.ToStringFull, p.gender.ToString(), p.ageTracker.AgeBiologicalYears + "/" + p.ageTracker.AgeChronologicalYears,
                p.genes?.XenotypeLabel, p.story.Childhood?.defName, p.story.Adulthood?.defName,
                p.story.traits.allTraits.Select(t => t.def.defName + t.Degree + (t.sourceGene != null ? "(gene " + t.sourceGene.def.defName + ")" : "") + (t.Suppressed ? "(greyed out)" : "")).OrderBy(x => x).ToCommaList(),
                "genes " + (p.genes == null ? "" : p.genes.GenesListForReading.Select(g => g.def.defName).OrderBy(x => x).ToCommaList()),
                p.skills.skills.Select(k => k.def.defName + ":" + k.Level + ":" + k.passion).ToCommaList(),
                p.story.bodyType?.defName, p.story.headType?.defName, p.story.hairDef?.defName, p.style?.beardDef?.defName,
                p.style?.FaceTattoo?.defName, p.style?.BodyTattoo?.defName,
                "hair " + p.story.HairColor.ToString(), "skin " + p.story.SkinColorBase.ToString() + "/" + p.story.skinColorOverride,
                "eyes " + PawnEditor.CurrentEyeGene(p)?.defName,
                "wears " + p.apparel.WornApparel.Select(t => t.def.defName + "/" + t.Stuff?.defName + "/" + PawnEditor.QualityOf(t)).OrderBy(x => x).ToCommaList(),
                "weapon " + p.equipment.Primary?.def.defName,
                "carries " + p.inventory.innerContainer.Select(t => t.def.defName + "x" + t.stackCount).OrderBy(x => x).ToCommaList(),
                "beside " + (Find.GameInitData.startingPossessions.TryGetValue(p, out List<ThingDefCount> left) ? left.Select(t => t.ThingDef.defName + "x" + t.Count).ToCommaList() : ""),
                "health " + PawnEditor.ShownHediffs(p).Select(h => PawnEditor.PartLabel(h.Part) + "/" + h.LabelCap).OrderBy(x => x).ToCommaList(),
                "ideo " + p.Ideo?.name + " " + p.ideo?.Certainty.ToStringPercent(),
                "family outside " + p.relations.DirectRelations.Where(r => !colonists.Contains(r.otherPawn) && r.def != PawnRelationDefOf.Bond)
                    .Select(r => r.def.defName + ":" + r.otherPawn.Name?.ToStringFull + ":" + r.otherPawn.gender + ":" + r.otherPawn.ageTracker.AgeBiologicalYears + (r.otherPawn.Dead ? ":dead" : ""))
                    .OrderBy(x => x).ToCommaList()
                    + "; children outside " + p.relations.Children.Where(c => !colonists.Contains(c)).Select(c => c.Name?.ToStringFull).OrderBy(x => x).ToCommaList()
            });
        }

        /// <summary>Size of a saved gallery picture and how much of it is actually drawn (not see-through).</summary>
        private static string PictureReport(string xmlPath)
        {
            string path = GalleryThumbnails.PathFor(xmlPath);
            if (!System.IO.File.Exists(path))
            {
                return "MISSING";
            }
            Texture2D tex = new Texture2D(0, 0);
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            Color32[] px = tex.GetPixels32();
            int drawn = px.Count(c => c.a > 0);
            string report = tex.width + "x" + tex.height + ", " + (100 * drawn / Mathf.Max(1, px.Length)) + "% drawn";
            UnityEngine.Object.Destroy(tex);
            return report;
        }

        /// <summary>Everything a saved colony should bring back, one line per colonist plus pets, supplies and relations.</summary>
        private string ColonyFingerprint()
        {
            List<string> lines = new List<string>();
            List<Pawn> colonists = Colonists;
            foreach (Pawn p in colonists)
            {
                lines.Add(PawnFingerprint(p, colonists));
            }
            for (int i = 0; i < colonists.Count; i++)
            {
                for (int j = 0; j < colonists.Count; j++)
                {
                    if (i != j)
                    {
                        lines.Add("relation " + i + "->" + j + " " + PawnEditor.RelationBetween(colonists[i], colonists[j])?.defName);
                    }
                }
            }
            foreach (Pawn pet in state.pets)
            {
                lines.Add("pet " + pet.kindDef.defName + " " + pet.Name?.ToStringFull + " " + pet.gender + " " + pet.ageTracker.AgeBiologicalYears
                    + " bonded to " + PawnEditor.GetBondedMaster(pet)?.Name?.ToStringFull + " coat " + pet.GetGraphicIndex());
            }
            foreach (SupplyEntry e in state.supplies)
            {
                lines.Add("supply " + e.def.defName + "/" + e.stuff?.defName + " x" + e.count + " q" + e.quality + (e.scattered ? " scattered" : ""));
            }
            return string.Join("\n", lines);
        }

        partial void DevVanillaPage()
        {
            DevTest.VanillaPageOnGUI(this);
        }

        private int promoFrame = -1;
        private List<Pawn> promoPawns;
        private float promoSpacing = 0.62f;

        // -cacPromoSingles: each pawn alone (and one colonist in several looks), for composing preview images.
        private List<(string shot, System.Action setup, List<Pawn> pawns)> promoShots;
        private int promoShot;
        private Rot4 promoFacing = Rot4.South;

        private static T Named<T>(string defName) where T : Def
        {
            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.Warning("[CAC test] promo: no " + typeof(T).Name + " named " + defName);
            }
            return def;
        }

        private static void PromoLook(Pawn p, Gender gender, int age, string xenotype, string hair, string beard, Color hairColor, string body, params string[] apparel)
        {
            PawnEditor.SetGender(p, gender);
            if (Named<XenotypeDef>(xenotype) is XenotypeDef x)
            {
                PawnEditor.SetXenotype(p, x);
            }
            PawnEditor.SetBiologicalAge(p, age);
            if (Named<HairDef>(hair) is HairDef h)
            {
                PawnEditor.SetHair(p, h);
            }
            if (Named<BeardDef>(beard) is BeardDef b)
            {
                PawnEditor.SetBeard(p, b);
            }
            PawnEditor.SetHairColor(p, hairColor);
            if (ModsConfig.IdeologyActive)
            {
                PawnEditor.SetTattoo(p, Named<TattooDef>("NoTattoo_Face"));
                PawnEditor.SetTattoo(p, Named<TattooDef>("NoTattoo_Body"));
            }
            Log.Message("[CAC test] promo look: " + p.LabelShort + " beard=" + p.style?.beardDef?.defName + " faceTattoo=" + p.style?.FaceTattoo?.defName
                + " head=" + p.story.headType.defName + " genes=" + p.genes?.GenesListForReading.Where(g => g.def.HasDefinedGraphicProperties).Select(g => g.def.defName).ToCommaList());
            if (body != null && Named<BodyTypeDef>(body) is BodyTypeDef bt)
            {
                PawnEditor.SetBodyType(p, bt);
            }
            if (apparel.Length > 0)
            {
                foreach (Apparel ap in p.apparel.WornApparel.ToList())
                {
                    PawnEditor.RemoveApparel(p, ap);
                }
                foreach (string a in apparel)
                {
                    if (Named<ThingDef>(a) is ThingDef def && PawnEditor.CanWear(p, def))
                    {
                        PawnEditor.Wear(p, def);
                    }
                }
            }
        }

        /// <summary>Replaces what the pawn wears; a colour dyes the piece where it can be dyed.</summary>
        private static void Dress(Pawn p, params (string def, Color? color)[] items)
        {
            foreach (Apparel ap in p.apparel.WornApparel.ToList())
            {
                PawnEditor.RemoveApparel(p, ap);
            }
            foreach ((string def, Color? color) in items)
            {
                if (Named<ThingDef>(def) is ThingDef d && PawnEditor.CanWear(p, d))
                {
                    PawnEditor.Wear(p, d, null, QualityCategory.Good, color);
                }
                else
                {
                    Log.Message("[CAC test] promo: " + p.LabelShort + " couldn't wear " + def);
                }
            }
        }

        /// <summary>Like Dress, but each piece can name its material too.</summary>
        private static void DressIn(Pawn p, params (string def, string stuff, Color? color)[] items)
        {
            foreach (Apparel ap in p.apparel.WornApparel.ToList())
            {
                PawnEditor.RemoveApparel(p, ap);
            }
            foreach ((string def, string stuff, Color? color) in items)
            {
                if (Named<ThingDef>(def) is ThingDef d && PawnEditor.CanWear(p, d))
                {
                    PawnEditor.Wear(p, d, stuff != null ? Named<ThingDef>(stuff) : null, QualityCategory.Excellent, color);
                }
                else
                {
                    Log.Message("[CAC test] promo: " + p.LabelShort + " couldn't wear " + def);
                }
            }
        }

        /// <summary>-cacPromoCast (with the full mod list): four distinct presenter candidates in modded clothing.</summary>
        private void PromoCast(List<Pawn> c)
        {
            // The vampire battle-nun: pale sanguophage in plate and chainmail under a war wimple.
            PromoLook(c[0], Gender.Female, 27, "Sanguophage", "Long", "NoBeard", new Color(0.1f, 0.08f, 0.08f), "Thin");
            PawnEditor.SetSkinColor(c[0], new Color(0.95f, 0.91f, 0.9f));
            DressIn(c[0], ("VAE_Apparel_Chainmail", "Steel", null), ("VAE_Apparel_LightPlateArmor", "Steel", null),
                ("VAE_Handwear_PlateGloves", "Steel", null), ("VAE_Footwear_PlateBoots", "Steel", null), ("PL_Apparel_WarSister", null, new Color(0.5f, 0.06f, 0.08f)));
            // The sea captain: a saurid in a navy captain's coat.
            PromoLook(c[1], Gender.Male, 46, "VRESaurids_Saurid", "Bald", "NoBeard", new Color(0.2f, 0.15f, 0.1f), "Male");
            DressIn(c[1], ("MCO_AdventurerOutfit", null, new Color(0.78f, 0.72f, 0.6f)), ("MCO_CaptainCoat", null, new Color(0.13f, 0.17f, 0.32f)),
                ("VAE_Footwear_Boots", "Leather_Plain", null), ("VAE_Handwear_Gloves", "Leather_Plain", null));
            // The heavy: a hussar in forsaken marine armour.
            PromoLook(c[2], Gender.Male, 38, "Hussar", "Mohawk", "Norse", new Color(0.24f, 0.16f, 0.1f), "Hulk");
            DressIn(c[2], ("VAE_Apparel_MilitaryUniform", null, null), ("AG_ForsakenMarineArmor", null, null), ("AG_ForsakenMarineHelmet", null, null),
                ("VAE_Footwear_MarineBoots", null, null), ("VAE_Handwear_MarineGloves", null, null));
            // The punk: a waster with pink spikes, tank top, jeans, black jacket and shades.
            PromoLook(c[3], Gender.Female, 24, "Baseliner", "Spikes", "NoBeard", new Color(0.95f, 0.25f, 0.6f), "Female");
            PawnEditor.SetSkinColor(c[3], new Color(0.86f, 0.68f, 0.55f));
            DressIn(c[3], ("VAE_Apparel_TankTop", null, new Color(0.9f, 0.9f, 0.88f)), ("VAE_Apparel_Jeans", null, new Color(0.22f, 0.3f, 0.48f)),
                ("Apparel_Jacket", "Leather_Plain", new Color(0.1f, 0.1f, 0.11f)), ("VAE_Headgear_Sunglasses", null, null), ("VAE_Footwear_Boots", "Leather_Plain", null));
            for (int i = 0; i < 4; i++)
            {
                Pawn one = c[i];
                Log.Message("[CAC test] promo cast " + i + ": " + one.genes?.XenotypeLabel + " wears " + one.apparel.WornApparel.Select(a => a.def.defName + "/" + a.Stuff?.defName).ToCommaList());
                promoShots.Add(("promo_cast_" + i + "_south", () => promoFacing = Rot4.South, new List<Pawn> { one }));
                promoShots.Add(("promo_cast_" + i + "_east", () => promoFacing = Rot4.East, new List<Pawn> { one }));
            }
            promoShots.Add(("promo_cast_lineup", () => promoFacing = Rot4.South, new List<Pawn> { c[0], c[1], c[2], c[3] }));
        }

        /// <summary>-cacPromoSlides: the cowboy in presenter poses, and the creator screens she shows off.</summary>
        private void PromoSlides(List<Pawn> c, Pawn husky)
        {
            Pawn cowboy = c[1];
            PawnEditor.SetName(cowboy, "Mira", "Boots", "Hale");
            // -cacPromoLead=<saved colonist>: present with a colonist from the gallery instead (spaces written as _,
            // since RimWorld splits its command line on spaces).
            if (GenCommandLine.TryGetCommandLineArg("cacPromoLead", out string lead) && !lead.NullOrEmpty())
            {
                string wanted = lead.Replace('_', ' ');
                ColonistPreset preset = ColonistPreset.LoadAll().FirstOrDefault(p => p.presetName == wanted
                    || System.IO.Path.GetFileNameWithoutExtension(p.filePath).Replace('_', ' ') == wanted);
                if (preset != null)
                {
                    preset.ApplyTo(cowboy);
                }
                Log.Message("[CAC test] promo lead: " + wanted + " -> " + (preset != null ? "loaded" : "NOT FOUND in the gallery"));
            }
            promoShots.Clear();
            foreach (Rot4 facing in new[] { Rot4.South, Rot4.West, Rot4.East })
            {
                Rot4 f = facing;
                promoShots.Add(("promo_cowboy_" + f.ToStringHuman().ToLower(), () => promoFacing = f, new List<Pawn> { cowboy }));
            }
            promoShots.Add(("slide_appearance", () =>
            {
                promoFacing = Rot4.South;
                CloseDialogs();
                Select(cowboy);
                tab = CreatorTab.Appearance;
                appearanceSub = 1;
                panelScroll = Vector2.zero;
            }, null));
            promoShots.Add(("slide_ideo", () =>
            {
                CloseDialogs();
                tab = CreatorTab.Ideoligion;
                panelScroll = Vector2.zero;
                OpenIdeoPicker(cowboy);
            }, null));
            promoShots.Add(("slide_inventory", () =>
            {
                CloseDialogs();
                PawnEditor.SetWeapon(cowboy, Named<ThingDef>("Gun_Revolver"));
                cowboy.inventory.innerContainer.ClearAndDestroyContents();
                foreach ((string def, int count) in new[] { ("MedicineIndustrial", 6), ("MealSurvivalPack", 8), ("Beer", 4), ("Silver", 120) })
                {
                    if (Named<ThingDef>(def) is ThingDef d)
                    {
                        PawnEditor.AddInventory(cowboy, d, count);
                    }
                }
                tab = CreatorTab.Inventory;
                inventorySub = 0;
                panelScroll = Vector2.zero;
            }, null));
            promoShots.Add(("slide_gallery", () =>
            {
                CloseDialogs();
                // A tidy gallery for the screenshot (test profile only): the crew and their colony, not test leftovers.
                foreach (string folder in new[] { ColonistPreset.Folder, ColonyPreset.Folder })
                {
                    foreach (string f in System.IO.Directory.GetFiles(folder))
                    {
                        GalleryFiles.Delete(f, folder);
                    }
                }
                foreach (Pawn p in c.Take(6))
                {
                    ColonistPreset saved = ColonistPreset.From(p, p.Name.ToStringFull);
                    saved.Save();
                    GalleryThumbnails.SaveColonist(p, saved.filePath);
                }
                ColonyPreset colony = ColonyPreset.From(Colonists, state, "The Dusk Riders");
                colony.Save();
                GalleryThumbnails.SaveGroup(Colonists, colony.filePath);
                Find.WindowStack.Add(new Dialog_Gallery(this, cowboy));
            }, null));
            promoShots.Add(("slide_colonies", () =>
            {
                CloseDialogs();
                Find.WindowStack.Add(new Dialog_Gallery(this, cowboy, showColonies: true));
            }, null));
            promoShots.Add(("slide_relationships", () =>
            {
                CloseDialogs();
                tab = CreatorTab.Relationships;
                panelScroll = Vector2.zero;
            }, null));
        }

        partial void RunPromo()
        {
            if (!DevTest.Promo || Event.current.type != EventType.Repaint)
            {
                return;
            }
            promoFrame++;
            if (promoFrame == 5)
            {
                while (Colonists.Count < 4 && AddColonist() != null)
                {
                }
                List<Pawn> c = Colonists;
                if (GenCommandLine.CommandLineArgPassed("cacPromoIcon"))
                {
                    // Three everyday colonist looks, spaced apart so each can be cropped for the mode icon.
                    PromoLook(c[0], Gender.Male, 30, "Baseliner", "Mop", "NoBeard", new Color(0.3f, 0.2f, 0.12f), "Male", "Apparel_BasicShirt", "Apparel_Pants");
                    PromoLook(c[1], Gender.Female, 28, "Baseliner", "Bob", "NoBeard", new Color(0.62f, 0.26f, 0.12f), "Female", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Jacket");
                    PromoLook(c[2], Gender.Male, 41, "Baseliner", "Mop", "Boxed", new Color(0.2f, 0.15f, 0.1f), "Male", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Duster", "Apparel_CowboyHat");
                    promoPawns = new List<Pawn> { c[0], c[1], c[2] };
                    promoSpacing = 1.05f;
                    return;
                }
                PromoLook(c[0], Gender.Male, 34, "Hussar", "Mohawk", "Norse", new Color(0.24f, 0.16f, 0.1f), "Hulk", "Apparel_BasicShirt", "Apparel_Pants",
                    GenCommandLine.CommandLineArgPassed("cacPromoSingles") ? "Apparel_Jacket" : "Apparel_FlakVest");
                PromoLook(c[1], Gender.Female, 27, "Baseliner", "Long", "NoBeard", new Color(0.62f, 0.24f, 0.1f), "Thin", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Duster", "Apparel_CowboyHat");
                PromoLook(c[2], Gender.Male, 9, "Baseliner", "Mop", "NoBeard", new Color(0.86f, 0.72f, 0.4f), null);
                PromoLook(c[3], Gender.Female, 23, "Impid", "Pigtails", "NoBeard", new Color(0.12f, 0.1f, 0.1f), "Female", "Apparel_TribalA");
                Pawn husky = state.pets.FirstOrDefault(p => p.kindDef.defName == "Husky") ?? state.AddPet(Named<PawnKindDef>("Husky"));
                promoPawns = new List<Pawn> { c[0], c[1], c[2], c[3], husky };
                if (GenCommandLine.CommandLineArgPassed("cacPromoSingles"))
                {
                    while (Colonists.Count < 6 && AddColonist() != null)
                    {
                    }
                    c = Colonists;
                    PromoLook(c[4], Gender.Male, 45, "Pigskin", "Mop", "NoBeard", new Color(0.55f, 0.35f, 0.2f), "Male", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Parka");
                    PawnEditor.SetSkinColor(c[0], new Color(0.45f, 0.3f, 0.2f));
                    PawnEditor.SetSkinColor(c[1], new Color(0.86f, 0.66f, 0.5f));
                    PromoLook(c[5], Gender.Female, 25, "Highmate", "Long", "NoBeard", new Color(0.9f, 0.85f, 0.95f), "Female", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Jacket");
                    Pawn v = c[1];
                    promoShots = new List<(string, System.Action, List<Pawn>)>();
                    if (GenCommandLine.CommandLineArgPassed("cacPromoCast"))
                    {
                        PromoCast(c);
                        promoShot = 0;
                        return;
                    }
                    if (GenCommandLine.CommandLineArgPassed("cacPromoCool"))
                    {
                        // A cooler-looking crew for the comic-poster preview, with the cowboy as the lead.
                        Dress(c[1], ("Apparel_CollarShirt", new Color(0.62f, 0.56f, 0.46f)), ("Apparel_Pants", new Color(0.2f, 0.2f, 0.22f)),
                            ("Apparel_Duster", new Color(0.17f, 0.16f, 0.16f)), ("Apparel_CowboyHat", new Color(0.55f, 0.36f, 0.18f)));
                        Dress(c[0], ("Apparel_BasicShirt", null), ("Apparel_Pants", null), ("Apparel_PowerArmor", null));
                        PromoLook(c[5], Gender.Female, 25, "Sanguophage", "Long", "NoBeard", new Color(0.9f, 0.9f, 0.92f), "Female");
                        Dress(c[5], ("Apparel_CollarShirt", null), ("Apparel_Pants", null), ("Apparel_ArmorRecon", null), ("Apparel_Cape", new Color(0.55f, 0.06f, 0.08f)));
                        PromoLook(c[4], Gender.Male, 38, "Baseliner", "Mop", "Boxed", new Color(0.12f, 0.1f, 0.09f), "Male");
                        PawnEditor.SetSkinColor(c[4], new Color(0.6f, 0.42f, 0.3f));
                        Dress(c[4], ("Apparel_CollarShirt", null), ("Apparel_Pants", null), ("Apparel_MechlordSuit", null), ("Apparel_AirwireHeadset", null));
                        Dress(c[3], ("Apparel_TribalA", null));
                        PawnEditor.SetSkinColor(c[3], new Color(0.78f, 0.28f, 0.22f));
                        Dress(c[2], ("Apparel_KidShirt", null), ("Apparel_KidPants", null), ("Apparel_KidParka", new Color(0.3f, 0.5f, 0.32f)), ("Apparel_KidHelmet", null));
                        for (int i = 0; i < 6; i++)
                        {
                            Pawn one = c[i];
                            promoShots.Add(("promo_cool_" + i, null, new List<Pawn> { one }));
                        }
                        promoShots.Add(("promo_cool_dog", null, new List<Pawn> { husky }));
                        // More of RimWorld's variety, each a new look on one spare pawn after its own shot is taken.
                        Pawn x = c[4];
                        promoShots.Add(("promo_cool_pigskin", () =>
                        {
                            PromoLook(x, Gender.Male, 33, "Pigskin", "Locks", "NoBeard", new Color(0.3f, 0.2f, 0.12f), "Fat");
                            Dress(x, ("Apparel_CollarShirt", null), ("Apparel_Pants", null), ("Apparel_FlakJacket", null), ("Apparel_BowlerHat", new Color(0.15f, 0.15f, 0.15f)));
                        }, new List<Pawn> { x }));
                        promoShots.Add(("promo_cool_waster", () =>
                        {
                            PromoLook(x, Gender.Female, 29, "Waster", "Spikes", "NoBeard", new Color(0.85f, 0.35f, 0.6f), "Female");
                            Dress(x, ("Apparel_BasicShirt", null), ("Apparel_Pants", null), ("Apparel_Jacket", new Color(0.25f, 0.27f, 0.2f)));
                        }, new List<Pawn> { x }));
                        promoShots.Add(("promo_cool_genie", () =>
                        {
                            PromoLook(x, Gender.Male, 44, "Genie", "Decent", "NoBeard", new Color(0.2f, 0.16f, 0.12f), "Thin");
                            PawnEditor.SetSkinColor(x, new Color(0.93f, 0.8f, 0.68f));
                            Dress(x, ("Apparel_CollarShirt", new Color(0.85f, 0.85f, 0.88f)), ("Apparel_Pants", null), ("Apparel_Jacket", new Color(0.22f, 0.3f, 0.5f)));
                        }, new List<Pawn> { x }));
                        promoShots.Add(("promo_cool_neanderthal", () =>
                        {
                            PromoLook(x, Gender.Male, 36, "Neanderthal", "Savage", "Full", new Color(0.4f, 0.26f, 0.14f), "Hulk");
                            Dress(x, ("Apparel_TribalA", null), ("Apparel_Parka", new Color(0.5f, 0.36f, 0.22f)));
                        }, new List<Pawn> { x }));
                        promoShots.Add(("promo_cool_elder", () =>
                        {
                            PromoLook(x, Gender.Female, 78, "Baseliner", "FancyBun", "NoBeard", new Color(0.82f, 0.82f, 0.82f), "Female");
                            PawnEditor.SetSkinColor(x, new Color(0.38f, 0.25f, 0.17f));
                            Dress(x, ("Apparel_CollarShirt", null), ("Apparel_Pants", null), ("Apparel_Robe", new Color(0.36f, 0.2f, 0.42f)));
                        }, new List<Pawn> { x }));
                        if (GenCommandLine.CommandLineArgPassed("cacPromoSlides"))
                        {
                            PromoSlides(c, husky);
                        }
                        promoShot = 0;
                        return;
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        Pawn one = c[i];
                        promoShots.Add(("promo_single_" + i, null, new List<Pawn> { one }));
                    }
                    promoShots.Add(("promo_single_dog", null, new List<Pawn> { husky }));
                    // One colonist, many looks.
                    promoShots.Add(("promo_variant_0", () => PromoLook(v, Gender.Female, 27, "Baseliner", "Bald", "NoBeard", new Color(0.62f, 0.24f, 0.1f), "Thin", "Apparel_BasicShirt", "Apparel_Pants"), new List<Pawn> { v }));
                    promoShots.Add(("promo_variant_1", () => PromoLook(v, Gender.Female, 27, "Baseliner", "Bob", "NoBeard", new Color(0.15f, 0.12f, 0.1f), "Female", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Jacket"), new List<Pawn> { v }));
                    promoShots.Add(("promo_variant_2", () => PromoLook(v, Gender.Female, 27, "Sanguophage", "Long", "NoBeard", new Color(0.85f, 0.85f, 0.88f), "Female", "Apparel_CollarShirt", "Apparel_Pants", "Apparel_Duster"), new List<Pawn> { v }));
                    promoShots.Add(("promo_variant_3", () => PromoLook(v, Gender.Female, 27, "Impid", "Pigtails", "NoBeard", new Color(0.2f, 0.5f, 0.3f), "Female", "Apparel_TribalA"), new List<Pawn> { v }));
                    promoShot = 0;
                }
            }
            else if (promoShots != null)
            {
                // One shot every 60 frames: set up the look, open the window, screenshot, close.
                int t = promoFrame - 15;
                if (t < 0 || promoShot >= promoShots.Count)
                {
                    return;
                }
                var shot = promoShots[promoShot];
                if (t % 60 == 0)
                {
                    shot.setup?.Invoke();
                    if (shot.pawns != null)
                    {
                        Find.WindowStack.Add(new Dialog_PromoLineup(shot.pawns, 1f, promoFacing));
                    }
                }
                else if (t % 60 == 50)
                {
                    Messages.Clear();
                    DevTest.Shot(shot.shot);
                    Find.WindowStack.WindowOfType<Dialog_PromoLineup>()?.Close(doCloseSound: false);
                    promoShot++;
                }
            }
            else if (promoFrame == 15)
            {
                Find.WindowStack.Add(new Dialog_PromoLineup(promoPawns, promoSpacing));
            }
            else if (promoFrame == 70)
            {
                Messages.Clear();
                DevTest.Shot(GenCommandLine.CommandLineArgPassed("cacPromoIcon") ? "promo_icon" : "promo_lineup");
            }
        }

        private int tourStep = -1;
        private int tourFrame;
        private List<(string name, Action act)> tourSteps;

        /// <summary>Scripted walk through the creator for -cacTour. Each step acts, waits for portraits to render, then screenshots.</summary>
        partial void RunTour()
        {
            if (!DevTest.Tour || Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (DevTest.Explore && RunExplore())
            {
                return;
            }
            if (tourSteps == null)
            {
                // After a stress run only the Play steps are left.
                tourSteps = DevTest.Explore ? BuildTour().Where(s => s.Item1.StartsWith("19")).ToList() : BuildTour();
                tourStep = 0;
                tourFrame = 0;
            }
            if (tourStep >= tourSteps.Count)
            {
                return;
            }
            tourFrame++;
            if (tourFrame == 1)
            {
                try
                {
                    DevPerf.Bench("step " + tourSteps[tourStep].name, tourSteps[tourStep].act);
                }
                catch (Exception e)
                {
                    Log.Error("[CAC test] step " + tourSteps[tourStep].name + " failed: " + e);
                }
            }
            else if (tourFrame == 6)
            {
                DevPerf.Reset();
            }
            else if (tourFrame == 36)
            {
                Log.Message("[CAC perf] " + tourSteps[tourStep].name + ": " + DevPerf.Report());
            }
            else if (tourFrame == 38)
            {
                Messages.Clear();
            }
            else if (tourFrame == 40)
            {
                DevTest.Shot(tourSteps[tourStep].name);
            }
            else if (tourFrame >= 55)
            {
                tourStep++;
                tourFrame = 0;
            }
        }

        private static void TourHealth(Pawn pawn, List<HealthOption> options, string match)
        {
            HealthOption option = options.FirstOrDefault(o => o.label.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0);
            if (option == null || option.BlockedReason != null)
            {
                Log.Message("[CAC test] health: " + match + " not added: " + (option == null ? "no matching option" : "blocked, " + option.BlockedReason));
                return;
            }
            PawnEditor.ApplyHealthOption(pawn, option);
        }

        private static void SaveTourIdeo(string file, string name)
        {
            string path = GenFilePaths.AbsPathForIdeo(file);
            Ideo source = PawnEditor.WorldIdeos().Last();
            string oldFileName = source.fileName;
            GameDataSaveLoader.SaveIdeo(source, path);
            source.fileName = oldFileName;
            if (GameDataSaveLoader.TryLoadIdeo(path, out Ideo copy))
            {
                copy.name = name;
                GameDataSaveLoader.SaveIdeo(copy, path);
            }
        }

        private void CloseDialogs()
        {
            foreach (Window w in Find.WindowStack.Windows.ToList())
            {
                if (w != this && !(w is ImmediateWindow))
                {
                    w.Close(doCloseSound: false);
                }
            }
        }

        private List<(string, Action)> BuildTour()
        {
            Name tourSavedName = null;
            string tourColonyBefore = null;
            ColonistPreset beforeHat = null;
            string Xml(Pawn p) => Scribe.saver.DebugOutputFor(ColonistPreset.From(p, null, withFamily: false));
            string FirstDiff(string a, string b)
            {
                string[] la = a.Split('\n'), lb = b.Split('\n');
                int i = 0;
                while (i < la.Length && i < lb.Length && la[i] == lb[i]) i++;
                return i < la.Length || i < lb.Length ? " first difference at line " + i + ": [" + la.ElementAtOrDefault(i)?.Trim() + "] vs [" + lb.ElementAtOrDefault(i)?.Trim() + "]" : "";
            }
            string deepStart = null, deepLog = null;
            int deepCount = 0, deepEdits = 0;
            List<(string, Action)> tour = new List<(string, Action)>
            {
                ("00_bench", () =>
                {
                    // One-off costs the per-frame timing can't see, on the first colonist as generated.
                    Pawn p = selected;
                    void Warm(string name, Action act)
                    {
                        DevPerf.Bench(name + " (first)", act);
                        DevPerf.Bench(name, act, 5);
                    }
                    void Open(string name, Action open) => Warm(name, () =>
                    {
                        open();
                        CloseDialogs();
                    });
                    Open("picker childhood", () => OpenBackstoryPicker(p, BackstorySlot.Childhood, "x"));
                    Open("picker adulthood", () => OpenBackstoryPicker(p, BackstorySlot.Adulthood, "x"));
                    Open("picker trait", () => OpenTraitPicker(p));
                    Open("picker item", () => OpenItemPicker("x", ItemDefs, d => { }, d => null));
                    Open("picker supply", () => OpenItemPicker("x", SupplyDefs, d => { }));
                    Open("picker species", OpenPetSpeciesPicker);
                    Open("picker ideo", () => OpenIdeoPicker(p));
                    Warm("options install", () => PawnEditor.InstallOptions(p));
                    Warm("options scar", () => PawnEditor.ScarOptions(p));
                    Warm("options missing", () => PawnEditor.MissingPartOptions(p));
                    Warm("options condition", () => PawnEditor.ConditionOptions(p));
                    Warm("options addiction", () => PawnEditor.AddictionOptions(p));
                    Rect spot = new Rect(10f, 10f, 64f, 64f);
                    void Api(string name, Action act) { act(); DevPerf.Bench("api " + name, act, 5000); }
                    Api("option background", () => Widgets.DrawOptionBackground(spot, false));
                    Api("button invisible", () => Widgets.ButtonInvisible(spot));
                    Api("button invisible, no sound", () => Widgets.ButtonInvisible(spot, doMouseoverSound: false));
                    Api("texture fitted", () => Widgets.DrawTextureFitted(spot, CACTex.Dice, 1f));
                    Api("draw texture", () => GUI.DrawTexture(spot, CACTex.Dice));
                    Api("thing icon", () => Widgets.ThingIcon(spot, ThingDefOf.Steel));
                    Api("label", () => Widgets.Label(spot, "Some label"));
                    Api("calc size", () => Text.CalcSize("Some label text here"));
                    Api("calc height", () => Text.CalcHeight("Some label text here that goes on for a while, longer than one line of the box", 100f));
                    Api("translate", () => "CAC_PageTitle".Translate());
                    Api("translate with arg", () => "CAC_PetCount".Translate(3));
                    Api("portrait cached", () => PortraitsCache.Get(p, new Vector2(92f, 100f), Rot4.South));
                    Api("colonists list", () => { List<Pawn> c = Colonists; });
                    Api("tabs list", () => TabsFor(p).ToList());
                    Api("style key", () => StyleKey(p));
                    Api("weight bar sums", () => PawnEditor.CarriedMass(p));
                    Api("carry fits", () => PawnEditor.CountThatFits(p, 1f));
                    Api("unit mass", () => PawnEditor.UnitMass(ThingDefOf.Steel));
                    Api("shown hediffs", () => PawnEditor.ShownHediffs(p));
                    Api("skills ordered", () => p.skills.skills.OrderBy(s => s.def.listOrder).ToList());
                    Api("disabled work tags", () => { WorkTags t = p.CombinedDisabledWorkTags; });
                    HairDef hair = DefDatabase<HairDef>.AllDefs.First(h => !h.noGraphic);
                    Api("style icon from the game", () => { Texture2D t = hair.Icon; });
                    Api("style icon cached", () => CACUI.IconOf(hair));
                    Api("work tag list", () => p.CombinedDisabledWorkTags.GetAllSelectedItems<WorkTags>().Where(t => t != WorkTags.None).Select(t => t.LabelTranslated().CapitalizeFirst()).ToCommaList());
                    Api("capacity label", () => HealthCardUtility.GetEfficiencyLabel(p, PawnCapacityDefOf.Moving));
                    Harmony scan = new Harmony("cac.bench");
                    Type[] types = AccessTools.GetTypesFromAssembly(typeof(Page_CreateAColonist).Assembly);
                    DevPerf.Bench("harmony scan of " + types.Length + " types", () =>
                    {
                        foreach (Type t in types)
                        {
                            scan.CreateClassProcessor(t);
                        }
                    }, 5);
                    DevPerf.Bench("snapshot", () => Snapshot(p), 20);
                    ColonistPreset preset = ColonistPreset.From(p, null, withFamily: false);
                    DevPerf.Bench("preset capture", () => ColonistPreset.From(p, null, withFamily: false), 20);
                    DevPerf.Bench("preset to text", () => Scribe.saver.DebugOutputFor(preset), 20);
                    Log.Message("[CAC bench] snapshot text: " + Snapshot(p).key.Length + " chars");
                    Warm("gallery files read", () =>
                    {
                        ColonistPreset.LoadAll();
                        ColonyPreset.LoadAll();
                    });
                    DevPerf.Bench("relation between (all pairs)", () =>
                    {
                        foreach (Pawn a in Colonists)
                        {
                            foreach (Pawn b in Colonists)
                            {
                                if (a != b)
                                {
                                    PawnEditor.RelationBetween(a, b);
                                }
                            }
                        }
                    }, 20);
                }),
                ("01_personality", () =>
                {
                    tab = CreatorTab.Personality;
                    Log.Message("[CAC test] possessions: " + Colonists.Select(p => p.LabelShort + " inventory=[" + p.inventory.innerContainer.Select(t => t.LabelCap).ToCommaList()
                        + "] stillPossessions=[" + (Find.GameInitData.startingPossessions.TryGetValue(p, out List<ThingDefCount> left) ? left.Select(l => l.LabelCap).ToCommaList() : "")
                        + "] " + PawnEditor.CarriedMass(p).ToStringMass() + "/" + PawnEditor.CarryCapacity(p).ToStringMass()).ToCommaList());
                }),
                ("01b_skill_typing", () =>
                {
                    // A typed level must survive backstory changes, come back from a saved colonist (through the file),
                    // and the recalculate button must bring back the backstory numbers.
                    Pawn p = Colonists[0];
                    Pawn q = Colonists[1];
                    ColonistPreset qBefore = ColonistPreset.From(q, "Tour backup", withFamily: false);
                    PawnEditor.RecalculateSkills(p, force: true);
                    SkillRecord rec = p.skills.skills.First(s => !s.TotallyDisabled && s.Aptitude == 0);
                    int formula = rec.Level;
                    int typed = formula == 17 ? 16 : 17;
                    PawnEditor.SetSkillLevel(p, rec, typed);
                    bool manualOn = PawnEditor.HasManualSkills(p);
                    BackstoryDef oldChildhood = p.story.Childhood;
                    BackstoryDef otherChildhood = PawnEditor.BackstoryOptions(p, BackstorySlot.Childhood).First(b => b != oldChildhood);
                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, otherChildhood);
                    int afterBackstory = rec.Level;

                    ColonistPreset saved = ColonistPreset.From(p, "Tour skill test", withFamily: false);
                    saved.Save();
                    ColonistPreset loaded = ColonistPreset.Load(saved.filePath);
                    System.IO.File.Delete(saved.filePath);
                    loaded.ApplyTo(q, withFamily: false);
                    int onCopy = q.skills.GetSkill(rec.def).Level;
                    bool copyManual = PawnEditor.HasManualSkills(q);
                    qBefore.ApplyTo(q, withFamily: false);
                    bool backupManual = PawnEditor.HasManualSkills(q);

                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, oldChildhood);
                    PawnEditor.RecalculateSkills(p, force: true);
                    int recalculated = rec.Level;
                    Log.Message("[CAC test] skill typing: " + rec.def.defName + " formula=" + formula + " typed=" + typed + " manual=" + manualOn
                        + " afterBackstoryChange=" + afterBackstory + " savedAndLoaded=" + onCopy + " copyManual=" + copyManual
                        + " otherColonistRestored(manual should be False)=" + backupManual + " recalculated=" + recalculated + " manualAfterReset=" + PawnEditor.HasManualSkills(p)
                        + " -> " + (manualOn && afterBackstory == typed && onCopy == typed && copyManual && !backupManual && recalculated == formula && !PawnEditor.HasManualSkills(p) ? "PASS" : "FAIL"));

                    // Gene bonuses: the shown number is the stored level plus the bonus, so typing 10 must show 10.
                    // A skill the colonist can do: a disabled one always shows 0.
                    GeneDef aptGene = DefDatabase<GeneDef>.AllDefs.FirstOrDefault(g => !g.aptitudes.NullOrEmpty() && g.aptitudes[0].level > 0 && g.aptitudes[0].level < 10
                        && p.skills.GetSkill(g.aptitudes[0].skill)?.TotallyDisabled == false);
                    if (aptGene != null && p.genes != null)
                    {
                        Gene gene = p.genes.AddGene(aptGene, xenogene: true);
                        p.skills.DirtyAptitudes();
                        SkillRecord boosted = p.skills.GetSkill(aptGene.aptitudes[0].skill);
                        PawnEditor.SetSkillLevel(p, boosted, 10);
                        Log.Message("[CAC test] skill typing with gene bonus: " + aptGene.defName + " +" + boosted.Aptitude + " typed 10 -> shown " + boosted.Level + " stored " + boosted.levelInt
                            + " -> " + (boosted.Level == 10 && boosted.levelInt == 10 - boosted.Aptitude ? "PASS" : "FAIL"));
                        p.genes.RemoveGene(gene);
                        p.skills.DirtyAptitudes();
                        PawnEditor.RecalculateSkills(p, force: true);
                    }

                    // Leave a level box open for the screenshot.
                    Select(p);
                    tab = CreatorTab.Personality;
                    editingSkill = rec;
                    editingSkillPawn = p;
                    skillBuffer = "12";
                    skillFieldFocused = false;
                }),
                ("01c_skill_committed", () =>
                {
                    SkillRecord rec = editingSkill;
                    CommitSkillEdit();
                    Log.Message("[CAC test] skill box commit: " + rec?.def.defName + " now " + rec?.Level + " manual=" + PawnEditor.HasManualSkills(selected)
                        + " -> " + (rec != null && rec.Level == 12 && PawnEditor.HasManualSkills(selected) ? "PASS" : "FAIL"));
                    PawnEditor.RecalculateSkills(selected, force: true);
                }),
                ("01d_backstory_traits", () =>
                {
                    // A backstory's own traits leave with it; a trait the player added stays.
                    Pawn p = selected;
                    ColonistPreset before = ColonistPreset.From(p, "Tour backstory traits", withFamily: false);
                    foreach (Trait t in p.story.traits.allTraits.Where(t => t.sourceGene == null).ToList())
                    {
                        PawnEditor.RemoveTraitOnly(p, t);
                    }
                    BackstoryDef adult = p.story.Adulthood;
                    BackstoryDef giver = PawnEditor.BackstoryOptions(p, BackstorySlot.Childhood).FirstOrDefault(b => b.forcedTraits?.Count == 1
                        && adult?.forcedTraits?.Any(a => a.def == b.forcedTraits[0].def) != true);
                    BackstoryDef plain = PawnEditor.BackstoryOptions(p, BackstorySlot.Childhood).First(b => b.forcedTraits.NullOrEmpty());
                    BackstoryTrait given = giver.forcedTraits[0];
                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, giver);
                    bool added = p.story.traits.HasTrait(given.def, given.degree);
                    for (int i = 0; i < 3; i++)
                    {
                        PawnEditor.SetBackstory(p, BackstorySlot.Childhood, plain);
                        PawnEditor.SetBackstory(p, BackstorySlot.Childhood, giver);
                    }
                    int copies = p.story.traits.allTraits.Count(t => t.def == given.def);
                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, plain);
                    bool removed = !p.story.traits.HasTrait(given.def);
                    PawnEditor.AddTrait(p, given.def, given.degree);
                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, giver);
                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, plain);
                    bool kept = p.story.traits.HasTrait(given.def, given.degree);
                    // The hand-added mark survives the gallery (through the file) onto another colonist.
                    Pawn q = Colonists[1];
                    ColonistPreset qBefore = ColonistPreset.From(q, "Tour backup", withFamily: false);
                    ColonistPreset saved = ColonistPreset.From(p, "Tour manual trait", withFamily: false);
                    saved.Save();
                    ColonistPreset.Load(saved.filePath).ApplyTo(q, withFamily: false);
                    System.IO.File.Delete(saved.filePath);
                    PawnEditor.SetBackstory(q, BackstorySlot.Childhood, giver);
                    PawnEditor.SetBackstory(q, BackstorySlot.Childhood, plain);
                    bool keptAfterLoad = q.story.traits.HasTrait(given.def, given.degree);
                    qBefore.ApplyTo(q, withFamily: false);
                    Log.Message("[CAC test] backstory traits: " + giver.defName + " gives " + given.def.defName + " added=" + added + " copiesAfterSwaps=" + copies
                        + " removedWithBackstory=" + removed + " handAddedKept=" + kept + " keptAfterGallery=" + keptAfterLoad
                        + " -> " + (added && copies == 1 && removed && kept && keptAfterLoad ? "PASS" : "FAIL"));
                    before.ApplyTo(p, withFamily: false);
                }),
                ("01e_undo", () =>
                {
                    Pawn p = selected;
                    RecordUndo();
                    string start = Xml(p);
                    PawnEditor.SetName(p, "Undo", "Tester", "Person");
                    PawnEditor.SetBackstory(p, BackstorySlot.Childhood, PawnEditor.BackstoryOptions(p, BackstorySlot.Childhood).First(b => b != p.story.Childhood));
                    RecordUndo();
                    PawnEditor.SetBiologicalAge(p, p.ageTracker.AgeBiologicalYears + 7);
                    RecordUndo();
                    string changed = Xml(p);
                    StepUndo(redo: false);
                    StepUndo(redo: false);
                    string undone = Xml(p);
                    StepUndo(redo: true);
                    StepUndo(redo: true);
                    string redone = Xml(p);
                    StepUndo(redo: false);
                    StepUndo(redo: false);
                    Log.Message("[CAC test] undo: steps=" + undoSteps.Count + "/" + redoSteps.Count + " undone=" + (undone == start) + FirstDiff(start, undone)
                        + " redone=" + (redone == changed) + FirstDiff(changed, redone) + " -> " + (undone == start && redone == changed ? "PASS" : "FAIL"));
                }),
                // Deep undo across real frames: every kind of edit undoes one at a time, and nothing the game settles a
                // frame later may count as a new edit (that would block undoing further).
                ("01e2_undo_deep", () =>
                {
                    Pawn p = selected;
                    RecordUndo();
                    deepStart = Xml(p);
                    deepCount = undoSteps.Count;
                    deepLog = "";
                    var edits = new List<Action>
                    {
                        () => PawnEditor.SetName(p, "Deep", "Undo", "Test"),
                        () => PawnEditor.SetBiologicalAge(p, p.ageTracker.AgeBiologicalYears + 5),
                        () => PawnEditor.SetBackstory(p, BackstorySlot.Childhood, PawnEditor.BackstoryOptions(p, BackstorySlot.Childhood).First(b => b != p.story.Childhood)),
                        () => PawnEditor.SetHair(p, DefDatabase<HairDef>.AllDefs.First(h => h != p.story.hairDef)),
                        () => PawnEditor.SetBodyType(p, p.story.bodyType == BodyTypeDefOf.Hulk ? BodyTypeDefOf.Thin : BodyTypeDefOf.Hulk),
                        () => PawnEditor.Wear(p, ThingDef.Named("Apparel_Duster")),
                        () => PawnEditor.AddTrait(p, TraitDefOf.Kind, 0)
                    };
                    if (ModsConfig.BiotechActive)
                    {
                        edits.Add(() => PawnEditor.SetXenotype(p, DefDatabase<XenotypeDef>.AllDefs.First(x => x != p.genes.Xenotype && x != XenotypeDefOf.Baseliner)));
                    }
                    foreach (Action edit in edits)
                    {
                        edit();
                        RecordUndo();
                    }
                    deepEdits = undoSteps.Count - deepCount;
                }),
                ("01f_ages", () =>
                {
                    // Chronological age has no cap but is never below biological; biological stops at the race's oldest.
                    Pawn p = selected;
                    int bio = p.ageTracker.AgeBiologicalYears;
                    PawnEditor.SetChronologicalAge(p, 5000);
                    bool far = p.ageTracker.AgeChronologicalYears == 5000 && p.ageTracker.AgeBiologicalYears == bio;
                    PawnEditor.SetChronologicalAge(p, 1);
                    bool floor = p.ageTracker.AgeChronologicalYears == bio;
                    PawnEditor.SetChronologicalAge(p, bio + 312);
                    Log.Message("[CAC test] ages: bio=" + bio + " chrono5000=" + far + " chronoFloorIsBio=" + floor + " maxBio=" + PawnEditor.MaxAgeYears(p)
                        + " -> " + (far && floor && PawnEditor.MaxAgeYears(p) == 100 ? "PASS" : "FAIL"));
                }),
                ("01g_headgear_on", () =>
                {
                    Pawn p = selected;
                    PawnEditor.SetChronologicalAge(p, p.ageTracker.AgeBiologicalYears);
                    beforeHat = ColonistPreset.From(p, "Tour headgear", withFamily: false);
                    ThingDef hat = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_CowboyHat");
                    if (hat != null && PawnEditor.CanWear(p, hat))
                    {
                        PawnEditor.Wear(p, hat, GenStuff.DefaultStuffFor(hat));
                    }
                    showHeadgear = true;
                    tab = CreatorTab.Clothing;
                }),
                ("01h_headgear_off", () => showHeadgear = false),
                ("01i_helmet_on", () =>
                {
                    ThingDef helmet = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PowerArmorHelmet");
                    if (helmet != null && PawnEditor.CanWear(selected, helmet))
                    {
                        PawnEditor.Wear(selected, helmet, GenStuff.DefaultStuffFor(helmet));
                    }
                    showHeadgear = true;
                }),
                ("01j_helmet_off", () => showHeadgear = false),
                ("02_body", () =>
                {
                    showHeadgear = true;
                    beforeHat?.ApplyTo(selected, withFamily: false);
                    tab = CreatorTab.Appearance;
                    appearanceSub = 0;
                }),
                ("02b_facial_animation", () =>
                {
                    if (!FacialAnimationCompat.Active)
                    {
                        Log.Message("[CAC test] facial animation: not loaded, section hidden (expected without the mod)");
                        return;
                    }
                    Pawn p = selected;
                    List<string> counts = new List<string>();
                    foreach (FacialAnimationCompat.Part part in FacialAnimationCompat.Parts)
                    {
                        List<Def> styles = FacialAnimationCompat.Styles(part, p);
                        int withPicture = styles.Count(d => FacialAnimationCompat.Thumbnail(part, d, p) != null);
                        counts.Add(part.key + "=" + styles.Count + " (" + withPicture + " pictures)");
                    }
                    FacialAnimationCompat.Part mouth = FacialAnimationCompat.PartFor("Mouth");
                    string set = "";
                    if (mouth != null)
                    {
                        Def pick = FacialAnimationCompat.Styles(mouth, p).LastOrDefault();
                        if (pick != null)
                        {
                            FacialAnimationCompat.SetStyle(mouth, p, pick);
                            set = " set mouth " + pick.defName + " -> now " + FacialAnimationCompat.CurrentStyle(mouth, p)
                                + (FacialAnimationCompat.CurrentStyle(mouth, p) == pick.defName ? " PASS" : " FAIL");
                        }
                    }
                    Log.Message("[CAC test] facial animation: " + counts.ToCommaList() + set);
                    faPart = Mathf.Max(0, FacialAnimationCompat.Parts.FindIndex(x => x.key == "Mouth"));
                    panelScroll.y = 9999f;
                }),
                ("03_hair", () => { appearanceSub = 1; }),
                ("03b_hair_scrolled", () => { panelScroll.y = 900f; }),
                ("04_beard", () => { panelScroll = Vector2.zero; appearanceSub = 2; }),
                ("05_tattoos", () => { appearanceSub = 3; }),
                ("06_colours", () => { appearanceSub = 4; }),
                ("06b_eyes", () =>
                {
                    PawnEditor.SetEyeGene(selected, DefDatabase<GeneDef>.GetNamedSilentFail("Eyes_Gray"));
                    Log.Message("[CAC test] eyes: options=" + PawnEditor.EyeGenes.Select(g => g.defName).ToCommaList()
                        + " current=" + PawnEditor.CurrentEyeGene(selected)?.defName);
                    panelScroll.y = 9999f;
                }),
                ("06b2_fa_eye_colour", () =>
                {
                    Pawn p = selected;
                    if (!FacialAnimationCompat.HasEyeColour(p))
                    {
                        Log.Message("[CAC test] facial animation eye colour: not available (Facial Animation not loaded)");
                        return;
                    }
                    Color blue = new Color(0.35f, 0.55f, 0.85f);
                    Color gold = new Color(0.85f, 0.65f, 0.25f);
                    FacialAnimationCompat.SetOddEyes(p, false);
                    FacialAnimationCompat.SetEyeColour(p, blue);
                    bool bothBlue = CACUI.ColorsClose(FacialAnimationCompat.EyeColour(p), blue) && CACUI.ColorsClose(FacialAnimationCompat.SecondEyeColour(p), blue);
                    FacialAnimationCompat.SetOddEyes(p, true);
                    FacialAnimationCompat.SetSecondEyeColour(p, gold);
                    bool odd = FacialAnimationCompat.HasOddEyes(p) && CACUI.ColorsClose(FacialAnimationCompat.EyeColour(p), blue)
                        && CACUI.ColorsClose(FacialAnimationCompat.SecondEyeColour(p), gold);
                    Log.Message("[CAC test] facial animation eye colour: bothBlue=" + bothBlue + " oddEyes=" + odd + " -> " + (bothBlue && odd ? "PASS" : "FAIL"));
                    panelScroll.y = 9999f;
                }),
                ("06b3_colour_dialog", () =>
                {
                    // The colour window must fit every swatch above its buttons (big mod lists add many hair colours).
                    Pawn p = selected;
                    Find.WindowStack.Add(new Dialog_CACColor("CAC_HairColour".Translate(), p.story.HairColor, HairSwatches(), c => PawnEditor.SetHairColor(p, c)));
                    Log.Message("[CAC test] colour window: " + HairSwatches().Count + " hair swatches");
                }),
                ("06c_ideo", () =>
                {
                    CloseDialogs();
                    panelScroll = Vector2.zero;
                    tab = CreatorTab.Ideoligion;
                    Log.Message("[CAC test] ideo: tabs=" + TabsFor(selected).Select(t => t.ToString()).ToCommaList() + " current=" + selected.Ideo?.name + " certainty=" + selected.ideo?.Certainty.ToStringPercent()
                        + " world ideos=" + PawnEditor.WorldIdeos().Select(i => i.name).ToCommaList());
                }),
                ("06d_ideo_picker", () => OpenIdeoPicker(selected)),
                ("06e_ideo_changed", () =>
                {
                    CloseDialogs();
                    Ideo other = PawnEditor.WorldIdeos().FirstOrDefault(i => i != selected.Ideo);
                    PawnEditor.SetIdeo(selected, other, 0.35f);
                    Log.Message("[CAC test] ideo changed: " + selected.LabelShort + " now " + selected.Ideo?.name + " (" + PawnEditor.IdeoFollowers(selected.Ideo) + ") certainty="
                        + selected.ideo.Certainty.ToStringPercent() + " previous=" + selected.ideo.PreviousIdeos.Count);
                }),
                ("06f_ideo_saved_picker", () =>
                {
                    // Two saved ideoligions in the test profile: one a colonist will follow, one nobody will (it must be cleaned up at start).
                    SaveTourIdeo("CAC Tour Creed", "Tour Creed");
                    SaveTourIdeo("CAC Tour Unused", "Unused Creed");
                    OpenSavedIdeoPicker(selected);
                }),
                ("06g_ideo_loaded", () =>
                {
                    CloseDialogs();
                    Ideo creed = PawnEditor.UseSavedIdeo(GenFilePaths.AbsPathForIdeo("CAC Tour Creed"));
                    Ideo again = PawnEditor.UseSavedIdeo(GenFilePaths.AbsPathForIdeo("CAC Tour Creed"));
                    Ideo unused = PawnEditor.UseSavedIdeo(GenFilePaths.AbsPathForIdeo("CAC Tour Unused"));
                    Pawn second = Colonists[1];
                    PawnEditor.SetIdeo(second, creed);
                    Select(second);
                    tab = CreatorTab.Ideoligion;
                    Log.Message("[CAC test] saved ideo: loaded=" + creed?.name + " sameInstanceTwice=" + (creed == again) + " unusedLoaded=" + (unused != null)
                        + " " + second.LabelShort + " follows " + second.Ideo?.name + " inManager=" + Find.IdeoManager.IdeosListForReading.Contains(creed));
                }),
                ("07_genes", () => { Select(Colonists[0]); tab = CreatorTab.Genes; }),
                ("08_genes_hussar", () => { PawnEditor.SetXenotype(selected, DefDatabase<XenotypeDef>.GetNamed("Hussar")); }),
                ("08a_book_before_landing", () =>
                {
                    // Making a book before landing must not read the game's start date while it's still unset
                    // (Research Reinvented logs an error), and the date must be unset again afterwards.
                    ThingDef novel = DefDatabase<ThingDef>.GetNamedSilentFail("Novel");
                    if (novel == null)
                    {
                        Log.Message("[CAC test] book before landing: no Novel def, skipped");
                        return;
                    }
                    Pawn p = Colonists[0];
                    Thing made = PawnEditor.MakeThing(novel, null, QualityCategory.Normal);
                    bool unsetAfterMake = Find.TickManager.gameStartAbsTick == 0;
                    made.Destroy();
                    if (!Find.GameInitData.startingPossessions.TryGetValue(p, out List<ThingDefCount> list))
                    {
                        list = Find.GameInitData.startingPossessions[p] = new List<ThingDefCount>();
                    }
                    list.Add(new ThingDefCount(novel, 1));
                    PawnEditor.TakePossessionsIntoInventory(p);
                    Thing book = p.inventory.innerContainer.FirstOrDefault(t => t.def == novel);
                    Log.Message("[CAC test] book before landing: made=" + made.def.defName + " unsetAfterMake=" + unsetAfterMake
                        + " possessionInInventory=" + (book != null) + " unsetAfterPossession=" + (Find.TickManager.gameStartAbsTick == 0));
                    book?.Destroy();
                }),
                ("08a_gene_leftovers", () =>
                {
                    // Alpha Genes' metal-eating gene adds a hidden condition and never takes it away. Changing the
                    // xenotype or genes must not leave it behind, or it errors on the colony's first tick.
                    Pawn p = Colonists[Colonists.Count - 1];
                    GeneDef metal = DefDatabase<GeneDef>.AllDefs.FirstOrDefault(g => g.geneClass?.Name == "Gene_Resource_Metal");
                    if (metal == null)
                    {
                        Log.Message("[CAC test] gene leftovers: Alpha Genes not loaded, skipped");
                        return;
                    }
                    bool Fueled() => p.health.hediffSet.hediffs.Any(h => h.def.defName == "AG_MineralFueled");
                    p.genes.AddGene(metal, xenogene: true);
                    bool added = Fueled();
                    PawnEditor.SetXenotype(p, XenotypeDefOf.Baseliner);
                    bool afterXenotype = Fueled();
                    p.genes.AddGene(metal, xenogene: false);
                    bool readded = Fueled();
                    PawnEditor.SetExactGenes(p, p.genes.Endogenes.Where(g => g.def != metal).Select(g => g.def).ToList(), new List<GeneDef>());
                    bool afterExact = Fueled();
                    Log.Message("[CAC test] gene leftovers: added=" + added + " afterXenotype=" + afterXenotype
                        + " readded=" + readded + " afterExactGenes=" + afterExact);
                }),
                ("08b_health", () =>
                {
                    panelScroll = Vector2.zero;
                    tab = CreatorTab.Health;
                    Pawn p = selected;
                    PawnEditor.RemoveAllHediffs(p);
                    TourHealth(p, PawnEditor.InstallOptions(p), "Bionic arm");
                    TourHealth(p, PawnEditor.InstallOptions(p), "Bionic eye");
                    TourHealth(p, PawnEditor.MissingPartOptions(p), "finger");
                    TourHealth(p, PawnEditor.ScarOptions(p), "leg");
                    TourHealth(p, PawnEditor.ConditionOptions(p), "Bad back");
                    TourHealth(p, PawnEditor.AddictionOptions(p), "lcohol");
                    Log.Message("[CAC test] health: " + p.LabelShort + " has " + PawnEditor.ShownHediffs(p).Select(h => PawnEditor.PartLabel(h.Part) + "/" + h.LabelCap).ToCommaList()
                        + "; moving=" + p.health.capacities.GetLevel(PawnCapacityDefOf.Moving).ToStringPercent() + " manipulation=" + p.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation).ToStringPercent()
                        + " sight=" + p.health.capacities.GetLevel(PawnCapacityDefOf.Sight).ToStringPercent()
                        + "; options: implants=" + PawnEditor.InstallOptions(p).Count + " scars=" + PawnEditor.ScarOptions(p).Count + " missing=" + PawnEditor.MissingPartOptions(p).Count
                        + " conditions=" + PawnEditor.ConditionOptions(p).Count + " addictions=" + PawnEditor.AddictionOptions(p).Count
                        + " deadlyBlocked=" + PawnEditor.MissingPartOptions(p).Count(o => o.BlockedReason != null));
                }),
                ("08c_health_picker", () => OpenHealthPicker(selected, "CAC_AddImplant".Translate(), PawnEditor.InstallOptions(selected))),
                ("08d_health_removed", () =>
                {
                    CloseDialogs();
                    Pawn p = Colonists[2];
                    int before = PawnEditor.ShownHediffs(p).Count;
                    TourHealth(p, PawnEditor.MissingPartOptions(p), "Left leg");
                    TourHealth(p, PawnEditor.InstallOptions(p), "Peg leg");
                    int withLeg = PawnEditor.ShownHediffs(p).Count;
                    PawnEditor.RemoveAllHediffs(p);
                    Log.Message("[CAC test] health remove all: " + p.LabelShort + " before=" + before + " withPegLeg=" + withLeg + " after=" + PawnEditor.ShownHediffs(p).Count
                        + " moving=" + p.health.capacities.GetLevel(PawnCapacityDefOf.Moving).ToStringPercent());
                    Select(p);
                }),
                ("09_clothing", () => { Select(Colonists[0]); tab = CreatorTab.Clothing; rotation = Rot4.East; }),
                ("09b_clothing_material", () =>
                {
                    // Clothing takes its material's colour: changing the material gives a fresh, undyed piece in the
                    // new material's colour, even if the old piece was dyed. Calling again with the old (now gone)
                    // piece, which is what the removed colour window did, must do nothing rather than error.
                    Pawn p = Colonists[0];
                    ThingDef shirtDef = DefDatabase<ThingDef>.GetNamed("Apparel_CollarShirt");
                    PawnEditor.Wear(p, shirtDef, DefDatabase<ThingDef>.GetNamed("Cloth"));
                    Apparel shirt = p.apparel.WornApparel.First(x => x.def == shirtDef);
                    shirt.SetColor(new Color(0.9f, 0.1f, 0.6f));
                    bool dyedBefore = PawnEditor.ColorOf(shirt).HasValue;
                    List<string> results = new List<string>();
                    Apparel current = shirt;
                    foreach (string stuffName in new[] { "DevilstrandCloth", "Hyperweave", "Synthread" })
                    {
                        ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);
                        if (stuff == null)
                        {
                            continue;
                        }
                        PawnEditor.ReplaceApparel(p, current, stuff, QualityCategory.Normal);
                        current = p.apparel.WornApparel.First(x => x.def == shirtDef);
                        Color want = shirtDef.GetColorForStuff(stuff);
                        bool match = CACUI.ColorsClose(current.DrawColor, want) && !PawnEditor.ColorOf(current).HasValue;
                        results.Add(stuffName + (match ? " ok" : " WRONG (" + current.DrawColor + " vs " + want + ")"));
                    }
                    PawnEditor.ReplaceApparel(p, shirt, DefDatabase<ThingDef>.GetNamed("Cloth"), QualityCategory.Normal);
                    int shirts = p.apparel.WornApparel.Count(x => x.def == shirtDef);
                    Log.Message("[CAC test] clothing material colour: dyed before=" + dyedBefore + " -> " + results.ToCommaList()
                        + "; stale call ignored=" + (shirts == 1 && p.apparel.WornApparel.First(x => x.def == shirtDef).Stuff?.defName == "Synthread"));
                }),
                ("10_inventory", () => { rotation = Rot4.South; tab = CreatorTab.Inventory; inventorySub = 0; PawnEditor.AddInventory(selected, ThingDefOf.MedicineIndustrial, 5); }),
                ("10b_inventory_weight", () =>
                {
                    Pawn p = selected;
                    p.inventory.innerContainer.ClearAndDestroyContents();
                    ThingDef uranium = DefDatabase<ThingDef>.GetNamed("Uranium");
                    int added = PawnEditor.AddInventory(p, uranium, 1000);
                    Log.Message("[CAC test] weight: " + p.LabelShort + " capacity=" + PawnEditor.CarryCapacity(p).ToStringMass() + " gear=" + MassUtility.GearMass(p).ToStringMass()
                        + " asked 1000 uranium, got " + added + " -> carrying " + PawnEditor.CarriedMass(p).ToStringMass() + " over=" + PawnEditor.OverCarryLimit(p)
                        + " moreFit=" + PawnEditor.CountThatFits(p, PawnEditor.UnitMass(uranium)));
                }),
                ("10c_item_picker_full", () =>
                {
                    Pawn p = selected;
                    OpenItemPicker("CAC_AddItem".Translate(), ItemDefs, d => { }, d => PawnEditor.CountThatFits(p, PawnEditor.UnitMass(d)) < 1
                        ? (string)"CAC_TooHeavyToAdd".Translate(p.LabelShort, MassUtility.FreeSpace(p).ToStringMass()) : null);
                }),
                ("10d_overweight", () =>
                {
                    CloseDialogs();
                    Pawn p = selected;
                    ThingDef armor = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PowerArmor");
                    if (armor != null && PawnEditor.CanWear(p, armor))
                    {
                        PawnEditor.Wear(p, armor);
                    }
                    Log.Message("[CAC test] overweight: " + p.LabelShort + " wears " + armor?.label + " -> carrying " + PawnEditor.CarriedMass(p).ToStringMass()
                        + " of " + PawnEditor.CarryCapacity(p).ToStringMass() + " over=" + PawnEditor.OverCarryLimit(p));
                }),
                ("10e_spare_gear", () =>
                {
                    // Spare weapons and clothes can be carried, and get a material and quality like worn clothes.
                    Pawn p = selected;
                    p.inventory.innerContainer.ClearAndDestroyContents();
                    ThingDef rifle = DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle");
                    ThingDef duster = DefDatabase<ThingDef>.GetNamed("Apparel_Duster");
                    ThingDef leather = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_Plain") ?? GenStuff.DefaultStuffFor(duster);
                    bool listed = ItemDefs.Contains(rifle) && ItemDefs.Contains(duster);
                    PawnEditor.AddInventory(p, ThingDefOf.MedicineIndustrial, 5, withinCarryLimit: false);
                    PawnEditor.AddInventory(p, rifle, 1, withinCarryLimit: false);
                    PawnEditor.AddInventory(p, duster, 1, withinCarryLimit: false);
                    Thing gun = p.inventory.innerContainer.First(t => t.def == rifle);
                    PawnEditor.ReplaceInventoryThing(p, gun, null, QualityCategory.Masterwork);
                    Thing coat = p.inventory.innerContainer.First(t => t.def == duster);
                    PawnEditor.ReplaceInventoryThing(p, coat, leather, QualityCategory.Excellent);
                    List<Thing> items = p.inventory.innerContainer.ToList();
                    string order = items.Select(t => t.def.defName).ToCommaList();
                    bool orderKept = items.Count == 3 && items[0].def == ThingDefOf.MedicineIndustrial && items[1].def == rifle && items[2].def == duster;
                    bool gunOk = PawnEditor.QualityOf(items[1]) == QualityCategory.Masterwork && PawnEditor.HasVariants(items[1]);
                    bool coatOk = PawnEditor.QualityOf(items[2]) == QualityCategory.Excellent && items[2].Stuff == leather;
                    bool medsCount = !PawnEditor.HasVariants(items[0]);

                    ColonistPreset saved = ColonistPreset.From(p, "Tour gear", withFamily: false);
                    string kept = saved.inventory.Select(e => e.def.defName + "/" + e.stuff?.defName + "/" + e.quality).ToCommaList();
                    Log.Message("[CAC test] spare gear: inAddList=" + listed + " order=" + order + " orderKept=" + orderKept + " rifleMasterwork=" + gunOk
                        + " dusterLeatherExcellent=" + coatOk + " medicineHasCount=" + medsCount + " preset=" + kept
                        + " -> " + (listed && orderKept && gunOk && coatOk && medsCount ? "PASS" : "FAIL"));
                }),
                ("10f_info_card", () =>
                {
                    Thing gun = selected.inventory.innerContainer.FirstOrDefault(t => t.def.IsWeapon);
                    if (gun != null)
                    {
                        Find.WindowStack.Add(new Dialog_InfoCard(gun));
                    }
                }),
                ("10g_mod_filter", () =>
                {
                    CloseDialogs();
                    List<ModContentPack> mods = CACUI.ModsOf(ItemDefs.Select(d => d.modContentPack));
                    int total = 0;
                    List<string> perMod = new List<string>();
                    foreach (ModContentPack mod in mods)
                    {
                        int n = ItemDefs.Count(d => d.modContentPack == mod);
                        total += n;
                        perMod.Add(mod.Name + "=" + n);
                    }
                    int unowned = ItemDefs.Count(d => d.modContentPack == null);
                    Log.Message("[CAC test] mod filter: items " + ItemDefs.Count + " from " + mods.Count + " mods (" + perMod.ToCommaList() + ") unowned=" + unowned
                        + " -> " + (total + unowned == ItemDefs.Count && mods.Count > 0 && mods[0].IsCoreMod ? "PASS" : "FAIL"));
                    OpenItemPicker("CAC_AddItem".Translate(), ItemDefs, d => { }, null);
                }),
                ("11_supplies", () => { CloseDialogs(); inventorySub = 1; }),
                ("12_relationships", () => { tab = CreatorTab.Relationships; PawnEditor.SetLoveRelation(selected, Colonists[1], PawnRelationDefOf.Spouse); }),
                ("12b_long_name", () =>
                {
                    // A long name must stay on one line next to each relationship button.
                    tourSavedName = selected.Name;
                    NameTriple n = (NameTriple)selected.Name;
                    selected.Name = new NameTriple(n.First, "Maximilianus", n.Last);
                }),
                ("13_pet", () => { selected.Name = tourSavedName ?? selected.Name; Pawn pet = state.AddPet(DefDatabase<PawnKindDef>.GetNamed("Husky")); PawnEditor.SetBondedMaster(pet, Colonists[0]); Select(pet); }),
                ("13b_pet_undo", () =>
                {
                    // Name, age and species are steps; undoing all three brings the same pet back, bond and all.
                    Pawn pet = selected;
                    int petCount = state.pets.Count;
                    tab = CreatorTab.Animal;
                    RecordUndo();
                    string start = Snapshot(pet).key;
                    pet.Name = new NameSingle("Undoggo");
                    PawnEditor.SetBiologicalAge(pet, pet.ageTracker.AgeBiologicalYears + 2);
                    RecordUndo();
                    selected = state.ChangePetSpecies(pet, DefDatabase<PawnKindDef>.GetNamed("Cat"));
                    ReplaceInUndo(pet, selected);
                    RecordUndo();
                    string changed = Snapshot(selected).key;
                    StepUndo(redo: false);
                    StepUndo(redo: false);
                    string undone = Snapshot(selected).key;
                    StepUndo(redo: true);
                    StepUndo(redo: true);
                    string redone = Snapshot(selected).key;
                    StepUndo(redo: false);
                    StepUndo(redo: false);
                    // The master's ID is in the key, so a matching key means the bond came back too.
                    Log.Message("[CAC test] pet undo: start=" + start + " undone=" + undone + " redone=" + (redone == changed) + " pets=" + state.pets.Count
                        + " -> " + (undone == start && redone == changed && state.pets.Count == petCount ? "PASS" : "FAIL"));
                }),
                ("13c_pet_coat", () =>
                {
                    // Every coat the picker offers sticks, the bond survives, and undo brings the old coat back.
                    PawnKindDef kind = CreatorState.AnimalKinds(petsOnly: true).FirstOrDefault(k => k.alternateGraphics?.Count >= 2)
                        ?? CreatorState.AnimalKinds(petsOnly: false).FirstOrDefault(k => k.alternateGraphics?.Count >= 2);
                    if (kind == null)
                    {
                        Log.Message("[CAC test] pet coat: no animal with coats loaded");
                        return;
                    }
                    Pawn master = PawnEditor.GetBondedMaster(selected);
                    selected = state.ChangePetSpecies(selected, kind);
                    RecordUndo();
                    int first = selected.GetGraphicIndex();
                    List<string> results = new List<string>();
                    for (int coat = kind.alternateGraphicChance < 1f ? -1 : 0; coat < kind.alternateGraphics.Count; coat++)
                    {
                        UndoState s = Snapshot(selected);
                        s.pet.coat = coat;
                        selected = Restore(selected, s);
                        results.Add(coat + "->" + selected.GetGraphicIndex());
                    }
                    bool all = results.All(r => r.Split(new[] { "->" }, StringSplitOptions.None).Distinct().Count() == 1);
                    bool bonded = PawnEditor.GetBondedMaster(selected) == master;
                    RecordUndo();
                    StepUndo(redo: false);
                    bool undone = selected.GetGraphicIndex() == first;
                    tab = CreatorTab.Animal;
                    Log.Message("[CAC test] pet coat: " + kind.defName + " " + results.ToCommaList() + " bondKept=" + bonded + " undoBackTo" + first + "=" + undone
                        + " -> " + (all && bonded && undone ? "PASS" : "FAIL"));
                }),
                ("13d_locks", () =>
                {
                    // Locked tabs survive "Randomize colonist"; the name (never locked) is re-rolled.
                    Select(Colonists.Last());
                    Pawn p = selected;
                    lockedTabs.Add(CreatorTab.Personality);
                    lockedTabs.Add(CreatorTab.Appearance);
                    string before = p.story.Childhood?.defName + "|" + p.story.Adulthood?.defName + "|" + p.story.traits.allTraits.Where(t => t.sourceGene == null).Select(t => t.def.defName + t.Degree).ToCommaList()
                        + "|" + p.story.hairDef?.defName + "|" + p.story.bodyType?.defName + "|" + p.story.headType?.defName + "|" + p.gender + "|" + p.ageTracker.AgeBiologicalYears;
                    string nameBefore = p.Name.ToStringFull;
                    RandomizeSelected();
                    Pawn q = selected;
                    string after = q.story.Childhood?.defName + "|" + q.story.Adulthood?.defName + "|" + q.story.traits.allTraits.Where(t => t.sourceGene == null).Select(t => t.def.defName + t.Degree).ToCommaList()
                        + "|" + q.story.hairDef?.defName + "|" + q.story.bodyType?.defName + "|" + q.story.headType?.defName + "|" + q.gender + "|" + q.ageTracker.AgeBiologicalYears;
                    tab = CreatorTab.Personality;
                    Log.Message("[CAC test] locks: newPawn=" + (q != p) + " kept=" + (before == after) + " [" + before + "] vs [" + after + "] nameRerolled=" + (q.Name.ToStringFull != nameBefore)
                        + " -> " + (q != p && before == after ? "PASS" : "FAIL"));
                }),
                ("13e_locks_shown", () => lockedTabs.Clear()),
                ("14_pet_bond", () => { tab = CreatorTab.Relationships; }),
                ("15_added_colonist", () => { AddColonist(); tab = CreatorTab.Personality; }),
                ("16_trait_picker", () => OpenTraitPicker(selected)),
                ("16b_backstory_filter", () =>
                {
                    CloseDialogs();
                    CreateAColonistSettings s = CreateAColonistMod.Settings;
                    s.hideWorkBlockers = false;
                    s.requiredWork.Clear();
                    s.requiredWork.Add(WorkBlock.ViolenceKey);
                    s.requiredWork.Add("Firefighter");
                    List<BackstoryDef> all = PawnEditor.BackstoryOptions(selected, BackstorySlot.Adulthood).ToList();
                    int blocking = all.Count(b => WorkBlock.From(b.workDisables, b.DisabledWorkTypes).Any);
                    int failRequired = all.Count(b => s.requiredWork.Any(WorkBlock.From(b.workDisables, b.DisabledWorkTypes).Blocks));
                    BackstoryDef sample = all.FirstOrDefault(b => WorkBlock.From(b.workDisables, b.DisabledWorkTypes).violence);
                    Log.Message("[CAC test] backstory filter: adulthood options=" + all.Count + " blockSomething=" + blocking + " hiddenByRequired=" + failRequired
                        + " sample=" + sample?.title + " -> " + (sample == null ? "" : WorkBlock.From(sample.workDisables, sample.DisabledWorkTypes).Label));
                    OpenBackstoryPicker(selected, BackstorySlot.Adulthood, "Adulthood".Translate());
                }),
                ("16c_backstory_hide_all", () =>
                {
                    CreateAColonistMod.Settings.hideWorkBlockers = true;
                }),
                ("16d_work_requirements", () => Find.WindowStack.Add(new Dialog_WorkRequirements())),
                ("16e_trait_filter", () =>
                {
                    CloseDialogs();
                    CreateAColonistSettings s = CreateAColonistMod.Settings;
                    s.hideWorkBlockers = false;
                    int blocking = DefDatabase<TraitDef>.AllDefs.Count(d => WorkBlock.From(d.disabledWorkTags, d.disabledWorkTypes).Any);
                    Log.Message("[CAC test] trait filter: traits that block work=" + blocking + " ("
                        + DefDatabase<TraitDef>.AllDefs.Where(d => WorkBlock.From(d.disabledWorkTags, d.disabledWorkTypes).Any)
                            .Select(d => d.defName + ": " + WorkBlock.From(d.disabledWorkTags, d.disabledWorkTypes).Label).ToCommaList() + ")");
                    OpenTraitPicker(selected);
                }),
                ("16f_passions", () =>
                {
                    CloseDialogs();
                    CreateAColonistSettings s = CreateAColonistMod.Settings;
                    s.hideWorkBlockers = false;
                    s.requiredWork.Clear();
                    CreateAColonistMod.Instance.WriteSettings();
                    SkillRecord skill = selected.skills.skills.First(r => !r.TotallyDisabled);
                    skill.passion = Passion.None;
                    List<Passion> down = new List<Passion>();
                    for (int i = 0; i < 3; i++)
                    {
                        PawnEditor.CyclePassion(skill, lower: true);
                        down.Add(skill.passion);
                    }
                    List<Passion> up = new List<Passion>();
                    for (int i = 0; i < 3; i++)
                    {
                        PawnEditor.CyclePassion(skill);
                        up.Add(skill.passion);
                    }
                    Log.Message("[CAC test] passions on " + skill.def.defName + ": right-click from None -> " + down.Select(p => p.ToString()).ToCommaList()
                        + "; left-click -> " + up.Select(p => p.ToString()).ToCommaList());
                }),
                ("17_add_menu", () => { CloseDialogs(); OpenAddMenu(); }),
                ("18_gallery", () =>
                {
                    CloseDialogs();
                    ColonistPreset saved = ColonistPreset.From(Colonists[0], "Tour test colonist");
                    saved.Save();
                    GalleryThumbnails.SaveColonist(Colonists[0], saved.filePath);
                    Log.Message("[CAC test] gallery picture: " + PictureReport(saved.filePath));
                    Find.WindowStack.Add(new Dialog_Gallery(this, selected));
                }),
                ("18b_family", () =>
                {
                    CloseDialogs();
                    List<Pawn> c = Colonists;
                    PawnEditor.SetSiblings(c[0], c[1]);
                    PawnEditor.SetParent(c[2], c[0]);
                    Log.Message("[CAC test] relations: " + c[1].LabelShort + " is " + c[0].LabelShort + "'s " + PawnEditor.RelationBetween(c[0], c[1])?.defName
                        + "; " + c[0].LabelShort + " is " + c[2].LabelShort + "'s " + PawnEditor.RelationBetween(c[2], c[0])?.defName);

                    // Half-siblings (one shared parent) must stop being half-siblings when given another relation.
                    Pawn sharedMother = c[1].GetMother();
                    c[3].SetMother(sharedMother);
                    bool halfBefore = PawnRelationDefOf.HalfSibling.Worker.InRelation(c[3], c[1]);
                    PawnEditor.SetParent(c[3], c[1]);
                    Log.Message("[CAC test] half-siblings: before=" + halfBefore + " after=" + PawnRelationDefOf.HalfSibling.Worker.InRelation(c[3], c[1])
                        + " (should be False) now=" + PawnEditor.RelationBetween(c[3], c[1])?.defName + " siblingsStill=" + PawnRelationDefOf.Sibling.Worker.InRelation(c[0], c[1]));
                    Select(c[0]);
                    tab = CreatorTab.Relationships;
                }),
                ("18c_child", () =>
                {
                    Pawn kid = Colonists[3];
                    Select(kid);
                    tab = CreatorTab.Appearance;
                    appearanceSub = 0;
                    PawnEditor.SetBiologicalAge(kid, 9);
                    Log.Message("[CAC test] child: age=" + kid.ageTracker.AgeBiologicalYears + " stage=" + kid.DevelopmentalStage + " body=" + kid.story.bodyType.defName
                        + " adulthood=" + (kid.story.Adulthood?.defName ?? "none") + " apparel=" + kid.apparel.WornApparel.Select(a => a.def.label).ToCommaList());
                }),
                ("18d_gallery_load", () =>
                {
                    // One colonist loaded into a different one must come out the same (relationships with the group aside).
                    string source = PawnFingerprint(Colonists[0], Colonists);
                    ColonistPreset.From(Colonists[0], "Round trip").ApplyTo(Colonists[1]);
                    string loaded = PawnFingerprint(Colonists[1], Colonists);
                    Log.Message("[CAC test] single colonist round trip: identical=" + (source == loaded));
                    if (source != loaded)
                    {
                        string[] x = source.Split(new[] { " | " }, System.StringSplitOptions.None);
                        string[] y = loaded.Split(new[] { " | " }, System.StringSplitOptions.None);
                        for (int i = 0; i < Mathf.Min(x.Length, y.Length); i++)
                        {
                            if (x[i] != y[i])
                            {
                                Log.Message("[CAC test]   before: " + x[i] + "\n[CAC test]   after:  " + y[i]);
                            }
                        }
                    }
                    ColonistPreset preset = ColonistPreset.LoadAll().FirstOrDefault(p => p.presetName == "Tour test colonist");
                    Pawn target = Colonists[2];
                    preset?.ApplyTo(target);
                    Select(target);
                    tab = CreatorTab.Personality;
                    Log.Message("[CAC test] gallery load: preset=" + (preset?.presetName ?? "MISSING") + " -> " + target.Name.ToStringFull + ", " + target.genes?.XenotypeLabel
                        + ", traits=" + target.story.traits.allTraits.Select(t => t.LabelCap).ToCommaList()
                        + ", ideo=" + target.Ideo?.name + " certainty=" + target.ideo?.Certainty.ToStringPercent()
                        + ", health=" + PawnEditor.ShownHediffs(target).Select(h => PawnEditor.PartLabel(h.Part) + "/" + h.LabelCap).ToCommaList());

                    // A renamed copy still has the original name inside; deleting it must remove the copy, not the original.
                    string original = ColonistPreset.PathFor("Tour test colonist");
                    string copy = System.IO.Path.Combine(ColonistPreset.Folder, "Renamed copy.xml");
                    System.IO.File.Copy(original, copy, overwrite: true);
                    ColonistPreset.LoadAll().First(p => p.filePath.EndsWith("Renamed copy.xml")).Delete();
                    Log.Message("[CAC test] delete renamed copy: copyExists=" + System.IO.File.Exists(copy) + " originalExists=" + System.IO.File.Exists(original));

                    // A file naming its own object type wasn't written by this mod and must be skipped.
                    string crafted = System.IO.Path.Combine(ColonistPreset.Folder, "Crafted.xml");
                    System.IO.File.WriteAllText(crafted, System.IO.File.ReadAllText(original).Replace("<colonist>", "<colonist Class=\"CreateAColonist.ColonistPreset\">"));
                    bool listed = ColonistPreset.LoadAll().Any(x => x.filePath != null && x.filePath.EndsWith("Crafted.xml"));
                    System.IO.File.Delete(crafted);
                    Log.Message("[CAC test] gallery file with Class marker listed=" + listed + " (should be False); normal presets still load=" + ColonistPreset.LoadAll().Count);
                }),
                ("18e_remove", () =>
                {
                    System.Collections.IList requests = HarmonyLib.AccessTools.Field(typeof(StartingPawnUtility), "StartingAndOptionalPawnGenerationRequests").GetValue(null) as System.Collections.IList;
                    int before = Find.GameInitData.startingPawnCount;
                    int requestsBefore = requests?.Count ?? -1;
                    Pawn extra = AddColonist();
                    Remove(extra);
                    Log.Message("[CAC test] add+remove: before=" + before + " after=" + Find.GameInitData.startingPawnCount + " listed=" + Colonists.Count
                        + " generation requests before=" + requestsBefore + " after=" + (requests?.Count ?? -1) + " (should match)");
                }),
                ("18e2_save_menu", () => { CloseDialogs(); OpenSaveMenu(); }),
                ("18f_colony_save", () =>
                {
                    CloseDialogs();
                    tourColonyBefore = ColonyFingerprint();
                    ColonyPreset tourColony = ColonyPreset.From(Colonists, state, "Tour test colony");
                    tourColony.Save();
                    GalleryThumbnails.SaveGroup(Colonists, tourColony.filePath);
                    Log.Message("[CAC test] colony picture: " + PictureReport(tourColony.filePath));
                    // Scramble everything: brand-new random colonists, no pets, no supplies.
                    for (int i = 0; i < Colonists.Count; i++)
                    {
                        state.Notify_ColonistRemoved(Group[i]);
                        StartingPawnUtility.RandomizePawn(i);
                    }
                    state.RememberColonists();
                    foreach (Pawn pet in state.pets.ToList())
                    {
                        state.RemovePet(pet);
                    }
                    state.supplies.Clear();
                    selected = null;
                    EnsureSelection();
                    Log.Message("[CAC test] colony saved, then scrambled: same as before=" + (ColonyFingerprint() == tourColonyBefore) + " (should be False)");
                    Find.WindowStack.Add(new Dialog_Gallery(this, selected, showColonies: true));
                }),
                ("18f2_gallery_manage", () =>
                {
                    // Duplicate, rename and delete a saved colonist and a saved colony; every file and picture must
                    // land where it should and the originals stay untouched.
                    ColonistPreset original = ColonistPreset.LoadAll().First(x => x.presetName == "Tour test colonist");
                    ColonistPreset copy = original.Duplicate();
                    string copyPath = copy.filePath;
                    bool copyOk = System.IO.File.Exists(copyPath) && GalleryThumbnails.Exists(copyPath) && copy.presetName.StartsWith("CAC_CopyName".Translate("Tour test colonist"));
                    copy.RenameTo("Renamed tour colonist");
                    bool renameOk = !System.IO.File.Exists(copyPath) && !System.IO.File.Exists(GalleryThumbnails.PathFor(copyPath))
                        && System.IO.File.Exists(copy.filePath) && GalleryThumbnails.Exists(copy.filePath)
                        && ColonistPreset.LoadAll().Any(x => x.presetName == "Renamed tour colonist");
                    string renamedPath = copy.filePath;
                    copy.Delete();
                    bool deleteOk = !System.IO.File.Exists(renamedPath) && !System.IO.File.Exists(GalleryThumbnails.PathFor(renamedPath));
                    bool originalOk = System.IO.File.Exists(original.filePath) && GalleryThumbnails.Exists(original.filePath);
                    Log.Message("[CAC test] gallery colonist: copy=" + copyOk + " (" + System.IO.Path.GetFileNameWithoutExtension(copyPath) + ") rename=" + renameOk + " delete=" + deleteOk + " original kept=" + originalOk);

                    ColonyPreset colony = ColonyPreset.LoadAll().First(x => x.presetName == "Tour test colony");
                    ColonyPreset colonyCopy = colony.Duplicate();
                    string colonyCopyPath = colonyCopy.filePath;
                    bool cCopy = System.IO.File.Exists(colonyCopyPath) && GalleryThumbnails.Exists(colonyCopyPath);
                    colonyCopy.RenameTo("Renamed tour colony");
                    bool cRename = !System.IO.File.Exists(colonyCopyPath) && System.IO.File.Exists(colonyCopy.filePath) && GalleryThumbnails.Exists(colonyCopy.filePath);
                    string colonyRenamed = colonyCopy.filePath;
                    colonyCopy.Delete();
                    bool cDelete = !System.IO.File.Exists(colonyRenamed) && !System.IO.File.Exists(GalleryThumbnails.PathFor(colonyRenamed));
                    Log.Message("[CAC test] gallery colony: copy=" + cCopy + " rename=" + cRename + " delete=" + cDelete
                        + " original kept=" + (System.IO.File.Exists(colony.filePath) && GalleryThumbnails.Exists(colony.filePath)));
                    // Leave one copy in the gallery for the screenshots.
                    original.Duplicate();
                    CloseDialogs();
                    Find.WindowStack.Add(new Dialog_Gallery(this, selected));
                }),
                ("18f3_gallery_colonies", () => { CloseDialogs(); Find.WindowStack.Add(new Dialog_Gallery(this, selected, showColonies: true)); }),
                ("18g_colony_load", () =>
                {
                    ColonyPreset colony = ColonyPreset.LoadAll().FirstOrDefault(c => c.presetName == "Tour test colony");
                    CloseDialogs();
                    if (colony == null)
                    {
                        Log.Message("[CAC test] colony load: saved colony MISSING");
                        return;
                    }
                    int worldBefore = Find.WorldPawns.AllPawnsAliveOrDead.Count;
                    LoadColony(colony);
                    Log.Message("[CAC test] colony load: world pawns " + worldBefore + " -> " + Find.WorldPawns.AllPawnsAliveOrDead.Count);
                    string after = ColonyFingerprint();
                    Log.Message("[CAC test] colony load: identical=" + (after == tourColonyBefore) + " colonists=" + colony.colonists.Count + " pets=" + colony.pets.Count
                        + " supplies=" + colony.supplies.Count + " relatives outside=" + colony.family.people.Count + " family links=" + colony.family.links.Count);
                    if (after != tourColonyBefore)
                    {
                        string[] b = tourColonyBefore.Split('\n');
                        string[] n = after.Split('\n');
                        for (int i = 0; i < Mathf.Max(b.Length, n.Length); i++)
                        {
                            string x = i < b.Length ? b[i] : "";
                            string y = i < n.Length ? n[i] : "";
                            if (x != y)
                            {
                                Log.Message("[CAC test]   before: " + x + "\n[CAC test]   after:  " + y);
                            }
                        }
                    }
                    tab = CreatorTab.Inventory;
                }),
                ("19_mode_dialog", () => { CloseDialogs(); Find.WindowStack.Add(new Dialog_CreationMode(null)); }),
                ("19b_play", () =>
                {
                    CloseDialogs();
                    // As if the player had pressed Play before and backed out of the game's warning: applying again must
                    // not double up, and nothing is settled or taken out of inventories until the colony is generated.
                    state.ApplyToScenario();
                    ThingDef uranium = DefDatabase<ThingDef>.GetNamed("Uranium");
                    if (CanDoNext())
                    {
                        DoNext();
                    }
                    Log.Message("[CAC test] play pressed: creator parts=" + CreatorState.PartsOf(Find.Scenario).Count(p => p is ScenPart_CreatorPets)
                        + " uranium still carried=" + Colonists.Sum(p => p.inventory.innerContainer.Where(t => t.def == uranium).Sum(t => t.stackCount))
                        + " unused creed still loaded=" + Find.IdeoManager.IdeosListForReading.Any(i => i.name == "Unused Creed"));
                }),
                ("19c_confirm_warning", () =>
                {
                    // The game may ask "nobody can do X, continue?" (random colonists can leave a job uncovered).
                    // Answer it like a player who wants to go on, so the tour never waits on a click.
                    foreach (Dialog_MessageBox box in Find.WindowStack.Windows.OfType<Dialog_MessageBox>().ToList())
                    {
                        string confirm = "Confirm".Translate();
                        System.Action go = box.buttonBText == confirm ? box.buttonBAction : box.buttonAText == confirm ? box.buttonAAction : null;
                        Log.Message("[CAC test] game asked before starting: " + box.text.Resolve().Replace("\n", " ") + " -> " + (go != null ? "confirmed" : "no Confirm button"));
                        box.Close(doCloseSound: false);
                        go?.Invoke();
                    }
                }),
            };
            // One undo per step, so a real frame (and the check the page makes after input) runs between them.
            tour.InsertRange(tour.FindIndex(t => t.Item1 == "01f_ages"), Enumerable.Range(1, 9).Select(i => ("01e3_undo_" + i, (Action)(() =>
            {
                RecordUndo();
                deepLog += " " + (undoSteps.Count - deepCount) + "/" + redoSteps.Count;
                if (i <= deepEdits)
                {
                    StepUndo(redo: false);
                    return;
                }
                if (i > deepEdits + 1)
                {
                    return;
                }
                string now = Xml(selected);
                Log.Message("[CAC test] deep undo: edits=" + deepEdits + " undo/redo after each:" + deepLog + " back=" + (now == deepStart) + FirstDiff(deepStart, now)
                    + " -> " + (now == deepStart && undoSteps.Count == deepCount && redoSteps.Count == deepEdits ? "PASS" : "FAIL"));
            }))));
            return tour;
        }
    }
}
#endif

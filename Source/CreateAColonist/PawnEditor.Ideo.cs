using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    public static partial class PawnEditor
    {
        private static readonly FieldInfo CertaintyField = AccessTools.Field(typeof(Pawn_IdeoTracker), "certaintyInt");
        private static readonly FieldInfo IdeoField = AccessTools.Field(typeof(Pawn_IdeoTracker), "ideo");

        /// <summary>Babies have no ideoligion. The game's SetIdeo ignores them, so this sets the stored value.</summary>
        private static void ClearIdeo(Pawn pawn)
        {
            if (pawn.ideo != null)
            {
                IdeoField.SetValue(pawn.ideo, null);
                pawn.ideo.PreviousIdeos.Clear();
            }
        }

        /// <summary>A baby growing up during play picks this up from those around them.</summary>
        private static void JoinColonyIdeo(Pawn pawn)
        {
            if (pawn.Ideo == null)
            {
                SetIdeo(pawn, Faction.OfPlayer.ideos?.PrimaryIdeo, ConversionTuning.InitialCertaintyRange.RandomInRange);
            }
        }

        // Saved ideoligions loaded into this game by the creator, so they can be reused and cleaned up.
        private static Game loadedIdeosGame;
        private static readonly Dictionary<string, Ideo> loadedIdeos = new Dictionary<string, Ideo>();

        /// <summary>The Ideoligion tab only makes sense with Ideology on, outside classic mode, for anyone past babyhood.</summary>
        public static bool CanHaveIdeo(Pawn pawn)
        {
            return ModsConfig.IdeologyActive && Find.IdeoManager != null && !Find.IdeoManager.classicMode
                && pawn?.ideo != null && pawn.Ideo != null && !pawn.DevelopmentalStage.Baby();
        }

        /// <summary>Ideoligions in this world a colonist can follow, the colony's own first.</summary>
        public static List<Ideo> WorldIdeos()
        {
            return Find.IdeoManager.IdeosInViewOrder.Where(i => !i.hidden).ToList();
        }

        public static void SetIdeo(Pawn pawn, Ideo ideo, float? certainty = null)
        {
            if (pawn.ideo == null || ideo == null)
            {
                return;
            }
            float keep = certainty ?? pawn.ideo.Certainty;
            if (pawn.Ideo != ideo)
            {
                pawn.ideo.SetIdeo(ideo);
                // A new colonist has no history of earlier beliefs.
                pawn.ideo.PreviousIdeos.Clear();
            }
            SetCertainty(pawn, keep);
            Dirty(pawn);
        }

        /// <summary>The game only changes certainty through conversion and events, so set the stored value directly.</summary>
        public static void SetCertainty(Pawn pawn, float certainty)
        {
            if (pawn.ideo != null && !pawn.DevelopmentalStage.Baby())
            {
                CertaintyField.SetValue(pawn.ideo, Mathf.Clamp01(certainty));
            }
        }

        /// <summary>Who follows an ideoligion, for the picker: "Your colony" or the factions that hold it.</summary>
        public static string IdeoFollowers(Ideo ideo)
        {
            if (Faction.OfPlayer.ideos?.IsPrimary(ideo) ?? false)
            {
                return "CAC_IdeoOfColony".Translate();
            }
            List<Faction> factions = Find.IdeoManager.GetFactionsWithIdeo(ideo, onlyPrimary: true, onlyNpcFactions: true);
            if (factions.Any())
            {
                return "CAC_IdeoOfFactions".Translate(factions.Select(f => f.Name).ToCommaList());
            }
            return IsLoadedIdeo(ideo) ? (string)"CAC_IdeoFromSaved".Translate() : (string)"CAC_IdeoNoFaction".Translate();
        }

        public static string IdeoSummary(Ideo ideo)
        {
            string memes = ideo.memes.Select(m => m.LabelCap.Resolve()).ToCommaList();
            string text = IdeoFollowers(ideo);
            if (!memes.NullOrEmpty())
            {
                text += "\n\n" + "Memes".Translate().CapitalizeFirst() + ": " + memes;
            }
            if (!ideo.description.NullOrEmpty())
            {
                text += "\n\n" + ideo.description;
            }
            return text;
        }

        // ---------------------------------------------------------------- saved ideoligions (.rid files)

        private static void ForgetOtherGames()
        {
            if (loadedIdeosGame != Current.Game)
            {
                loadedIdeosGame = Current.Game;
                loadedIdeos.Clear();
            }
        }

        public static bool IsLoadedIdeo(Ideo ideo)
        {
            ForgetOtherGames();
            return loadedIdeos.ContainsValue(ideo);
        }

        /// <summary>The player's saved ideoligions, read fresh from disk, paired with their files.</summary>
        public static List<(FileInfo file, Ideo ideo)> SavedIdeos()
        {
            List<(FileInfo, Ideo)> result = new List<(FileInfo, Ideo)>();
            foreach (FileInfo file in GenFilePaths.AllCustomIdeoFiles)
            {
                if (GameDataSaveLoader.TryLoadIdeo(file.FullName, out Ideo ideo) && ideo != null)
                {
                    result.Add((file, ideo));
                }
            }
            return result;
        }

        /// <summary>
        /// Brings a saved ideoligion into this game (once per file) and returns the game's copy. Loading the same file
        /// again gives the same ideoligion, so two colonists picking it share one belief.
        /// </summary>
        public static Ideo UseSavedIdeo(string path, Ideo freshlyLoaded = null)
        {
            ForgetOtherGames();
            string key = Path.GetFullPath(path);
            if (loadedIdeos.TryGetValue(key, out Ideo existing) && Find.IdeoManager.IdeosListForReading.Contains(existing))
            {
                return existing;
            }
            Ideo ideo = freshlyLoaded;
            if (ideo == null && (!File.Exists(key) || !GameDataSaveLoader.TryLoadIdeo(key, out ideo)))
            {
                return null;
            }
            IdeoGenerator.InitLoadedIdeo(ideo);
            Find.IdeoManager.Add(ideo);
            loadedIdeos[key] = ideo;
            return ideo;
        }

        /// <summary>The saved file an ideoligion came from, if any (the game remembers it for ones the player saved).</summary>
        public static string SavedIdeoFileName(Ideo ideo)
        {
            if (!ideo.fileName.NullOrEmpty())
            {
                return ideo.fileName;
            }
            ForgetOtherGames();
            foreach (KeyValuePair<string, Ideo> kv in loadedIdeos)
            {
                if (kv.Value == ideo)
                {
                    return Path.GetFileNameWithoutExtension(kv.Key);
                }
            }
            return null;
        }

        /// <summary>Going back: put colonists on loaded ideoligions back on the colony's and drop the loaded ones.</summary>
        public static void ForgetSavedIdeos()
        {
            ForgetOtherGames();
            Ideo primary = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            foreach (Ideo ideo in loadedIdeos.Values.ToList())
            {
                foreach (Pawn p in Find.GameInitData.startingAndOptionalPawns)
                {
                    if (p.Ideo == ideo && primary != null)
                    {
                        SetIdeo(p, primary);
                    }
                }
                if (!AnyFactionHas(ideo))
                {
                    Find.IdeoManager.Remove(ideo);
                }
            }
            loadedIdeos.Clear();
            loadedIdeosGame = null;
        }

        private static bool AnyFactionHas(Ideo ideo)
        {
            return Find.FactionManager.AllFactions.Any(f => f.ideos != null && f.ideos.AllIdeos.Contains(ideo));
        }

        /// <summary>
        /// At game start, makes the colony's ideoligions match what its colonists believe, the same way the game
        /// does it during play (the most-followed one becomes the main ideoligion), and drops any ideoligion that
        /// nobody ended up following because of the creator.
        /// </summary>
        public static void SettleIdeos(List<Pawn> colonists)
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager.classicMode || Faction.OfPlayer?.ideos == null)
            {
                return;
            }
            ForgetOtherGames();
            FactionIdeosTracker tracker = Faction.OfPlayer.ideos;
            Dictionary<Ideo, int> counts = new Dictionary<Ideo, int>();
            foreach (Pawn p in colonists)
            {
                if (p.Ideo != null)
                {
                    counts[p.Ideo] = counts.TryGetValue(p.Ideo, 0) + 1;
                }
            }
            Ideo oldPrimary = tracker.PrimaryIdeo;
            Ideo newPrimary = oldPrimary;
            int best = counts.TryGetValue(oldPrimary, 0);
            foreach (KeyValuePair<Ideo, int> kv in counts)
            {
                if (kv.Value > best)
                {
                    best = kv.Value;
                    newPrimary = kv.Key;
                }
            }
            if (newPrimary != oldPrimary)
            {
                oldPrimary?.Notify_NotPrimaryAnymore(newPrimary);
                tracker.SetPrimary(newPrimary);
            }
            List<Ideo> minor = tracker.IdeosMinorListForReading;
            minor.Clear();
            minor.AddRange(counts.Keys.Where(i => i != newPrimary));

            // Loaded ideoligions nobody follows, and an old main ideoligion everyone left, would otherwise linger
            // in the world with no believers.
            List<Ideo> unused = loadedIdeos.Values.Where(i => !counts.ContainsKey(i)).ToList();
            if (oldPrimary != null && oldPrimary != newPrimary && !counts.ContainsKey(oldPrimary))
            {
                unused.Add(oldPrimary);
            }
            foreach (Ideo ideo in unused.Distinct())
            {
                if (AnyFactionHas(ideo) || Find.WorldPawns.AllPawnsAliveOrDead.Any(p => p.Ideo == ideo && !p.wasLeftBehindStartingPawn))
                {
                    continue;
                }
                // Candidates left behind at the start (the game has already moved them to the world by now) must not
                // point at a removed ideoligion.
                foreach (Pawn p in Find.GameInitData.startingAndOptionalPawns.Concat(Find.WorldPawns.AllPawnsAliveOrDead.Where(w => w.wasLeftBehindStartingPawn)).ToList())
                {
                    if (p.Ideo == ideo)
                    {
                        SetIdeo(p, newPrimary);
                    }
                }
                Find.IdeoManager.Remove(ideo);
            }
            loadedIdeos.Clear();
            loadedIdeosGame = null;
        }
    }
}

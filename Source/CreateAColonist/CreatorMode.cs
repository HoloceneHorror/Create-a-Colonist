using HarmonyLib;
using RimWorld;
using Verse;

namespace CreateAColonist
{
    /// <summary>Which character screen the player picked when starting this colony.</summary>
    public static class CreatorMode
    {
        public static bool Active;

        // Weak: the page links to every page after it, and through them to the colonists and the game.
        private static WeakReference<Page_SelectScenario> lastPrompted;

        public static void PromptIfNeeded(Page_SelectScenario page)
        {
            // Going Back from the storyteller page reopens the same scenario page; only ask once per visit.
            if (lastPrompted?.Target == page)
            {
                return;
            }
            lastPrompted = new WeakReference<Page_SelectScenario>(page);
            Active = false;
            Find.WindowStack.Add(new Dialog_CreationMode(page));
        }
    }

    // The main menu opens Page_SelectScenario directly, so the first frame it draws is our cue to ask.
    [HarmonyPatch(typeof(Page_SelectScenario), nameof(Page_SelectScenario.DoWindowContents))]
    public static class Patch_SelectScenario_DoWindowContents
    {
        public static void Prefix(Page_SelectScenario __instance)
        {
            if (__instance.prev == null)
            {
                // Next from here always starts a new game, so nothing of an earlier setup can come back.
                CreatorState.Forget();
                CreatorMode.PromptIfNeeded(__instance);
            }
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace CreateAColonist
{
    public class CreateAColonistMod : Mod
    {
        public const string HarmonyId = "HoloceneHorror.CreateAColonist";

        public static CreateAColonistMod Instance;

        public static CreateAColonistSettings Settings => Instance.GetSettings<CreateAColonistSettings>();

        public CreateAColonistMod(ModContentPack content) : base(content)
        {
            Instance = this;
            new Harmony(HarmonyId).PatchAll();
        }
    }

    /// <summary>Choices remembered between games: the backstory and trait pickers' work filter.</summary>
    public class CreateAColonistSettings : ModSettings
    {
        public bool hideWorkBlockers;

        // Work type defNames, or WorkBlock.ViolenceKey. Names from removed mods are kept but match nothing.
        public List<string> requiredWork = new List<string>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref hideWorkBlockers, "hideWorkBlockers");
            Scribe_Collections.Look(ref requiredWork, "requiredWork", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                requiredWork ??= new List<string>();
            }
        }
    }
}

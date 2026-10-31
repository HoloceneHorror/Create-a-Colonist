using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    /// <summary>
    /// Optional support for Facial Animation. It keeps its own head, eye, brow, mouth, lid and skin styles in def
    /// types of its own, set through comps on the pawn, so the creator's head grid never sees them. Everything is
    /// looked up by name, so nothing here needs Facial Animation to be installed; without it the list of parts is
    /// empty and the creator shows nothing extra.
    /// </summary>
    public static class FacialAnimationCompat
    {
        public class Part
        {
            public string key;
            public Type compType;
            public PropertyInfo allDefs;
            public PropertyInfo styleName;
            public MethodInfo init;
            public MethodInfo reload;
            public MethodInfo setDirty;
            public MethodInfo applicable;
            public FieldInfo gender;
            public FieldInfo raceName;
            public FieldInfo texPath;
        }

        private static List<Part> parts;
        private static readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();

        // Colonists the player turned odd eyes on or off for; read from the colours otherwise. Weak, like the
        // creator's hand-set skills, so it goes away with the colonist.
        private static readonly ConditionalWeakTable<Pawn, object> oddEyes = new ConditionalWeakTable<Pawn, object>();

        public static List<Part> Parts
        {
            get
            {
                if (parts == null)
                {
                    parts = new List<Part>();
                    AddPart("Head", "HeadTypeDef", "HeadControllerComp");
                    AddPart("Eye", "EyeballTypeDef", "EyeballControllerComp");
                    AddPart("Brow", "BrowTypeDef", "BrowControllerComp");
                    AddPart("Mouth", "MouthTypeDef", "MouthControllerComp");
                    AddPart("Lid", "LidTypeDef", "LidControllerComp");
                    AddPart("Skin", "SkinTypeDef", "SkinControllerComp");
                }
                return parts;
            }
        }

        public static bool Active => Parts.Count > 0;

        private static void AddPart(string key, string defName, string compName)
        {
            Type defType = AccessTools.TypeByName("FacialAnimation." + defName);
            Type compType = AccessTools.TypeByName("FacialAnimation." + compName);
            if (defType == null || compType == null)
            {
                return;
            }
            Type generator = defType.Assembly.GetType("FacialAnimation.FaceTypeGenerator`1")?.MakeGenericType(defType);
            Part part = new Part
            {
                key = key,
                compType = compType,
                allDefs = typeof(DefDatabase<>).MakeGenericType(defType).GetProperty("AllDefs", BindingFlags.Public | BindingFlags.Static),
                styleName = compType.GetProperty("FaceTypeDefName", BindingFlags.Public | BindingFlags.Instance),
                init = compType.GetMethod("InitializeIfNeed", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                reload = compType.GetMethod("ReloadIfNeed", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                setDirty = compType.GetMethod("SetDirty", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                // Facial Animation's own race, gender and gene filter: the same list it picks from itself.
                applicable = generator?.GetMethod("GetApplicableFaceTypeDefsForRaceConsideringGenes", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Pawn) }, null),
                gender = defType.GetField("gender", BindingFlags.Public | BindingFlags.Instance),
                raceName = defType.GetField("raceName", BindingFlags.Public | BindingFlags.Instance),
                texPath = defType.GetField("texPath", BindingFlags.Public | BindingFlags.Instance)
            };
            if (part.allDefs != null && part.styleName != null && part.styleName.CanWrite)
            {
                parts.Add(part);
            }
            else
            {
                Log.Warning("[Create a Colonist] Facial Animation's " + key + " styles have a different layout than expected, so they can't be picked here.");
            }
        }

        public static Part PartFor(string key)
        {
            return Parts.Find(p => p.key == key);
        }

        /// <summary>The styles this colonist can have for one part, sorted by name.</summary>
        public static List<Def> Styles(Part part, Pawn pawn)
        {
            List<Def> result = new List<Def>();
            if (part.applicable != null)
            {
                try
                {
                    if (part.applicable.Invoke(null, new object[] { pawn }) is IEnumerable fits)
                    {
                        foreach (object o in fits)
                        {
                            if (o is Def def)
                            {
                                result.Add(def);
                            }
                        }
                        result.SortBy(d => d.defName);
                        return result;
                    }
                }
                catch (Exception e)
                {
                    Log.WarningOnce("[Create a Colonist] Facial Animation couldn't say which " + part.key + " styles fit, so all of them are shown: " + e.Message,
                        part.key.GetHashCode());
                }
            }
            // Fallback: filter by gender and race the same way Facial Animation does.
            if (part.allDefs.GetValue(null, null) is IEnumerable all)
            {
                foreach (object o in all)
                {
                    if (!(o is Def def))
                    {
                        continue;
                    }
                    Gender gender = GenderOf(part, def);
                    if (gender != Gender.None && gender != pawn.gender)
                    {
                        continue;
                    }
                    string race = part.raceName?.GetValue(def) as string;
                    if (!race.NullOrEmpty() && !race.Equals(pawn.def.defName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    result.Add(def);
                }
            }
            result.SortBy(d => d.defName);
            return result;
        }

        public static Gender GenderOf(Part part, Def def)
        {
            return part.gender != null && part.gender.GetValue(def) is Gender g ? g : Gender.None;
        }

        public static string CurrentStyle(Part part, Pawn pawn)
        {
            ThingComp comp = CompOf(part, pawn);
            return comp == null ? null : part.styleName.GetValue(comp, null) as string;
        }

        public static void SetStyle(Part part, Pawn pawn, Def def)
        {
            ThingComp comp = CompOf(part, pawn);
            if (comp == null)
            {
                return;
            }
            try
            {
                // Let Facial Animation catch up on gender and gene changes first: when it notices one later it picks a
                // random style, replacing this one (loading a gallery colonist changes both just before this).
                part.init?.Invoke(comp, null);
                part.reload?.Invoke(comp, null);
                part.styleName.SetValue(comp, def.defName, null);
                part.reload?.Invoke(comp, null);
                part.setDirty?.Invoke(comp, null);
                Refresh(pawn);
            }
            catch (Exception e)
            {
                Log.Warning("[Create a Colonist] Could not set the Facial Animation " + part.key + " style " + def.defName + ": " + e.Message);
            }
        }

        /// <summary>
        /// A thumbnail for a style. Facial Animation keeps each style's pictures under texPath/Male or Female; lids
        /// use their own file names. Null when there's no picture to show.
        /// </summary>
        public static Texture2D Thumbnail(Part part, Def def, Pawn pawn)
        {
            string path = part.texPath?.GetValue(def) as string;
            if (path.NullOrEmpty())
            {
                return null;
            }
            string gender = pawn.gender == Gender.Female ? "Female" : "Male";
            string key = path + "|" + gender;
            if (thumbnails.TryGetValue(key, out Texture2D tex))
            {
                return tex;
            }
            string[] names = part.key == "Lid"
                ? new[] { "normal_cover_south", "normal_bottom_south", "normal_south", "normal_cover_east" }
                : new[] { "normal_south", "normal_east", "normal_cover_south" };
            foreach (string name in names)
            {
                tex = ContentFinder<Texture2D>.Get(path + "/" + gender + "/" + name, false);
                if (tex != null)
                {
                    break;
                }
            }
            thumbnails[key] = tex;
            return tex;
        }

        // ---------------------------------------------------------------- eye colour

        public static bool HasEyeColour(Pawn pawn) => CompOf(PartFor("Eye"), pawn) != null;

        public static Color EyeColour(Pawn pawn)
        {
            ThingComp comp = CompOf(PartFor("Eye"), pawn);
            if (comp != null && AccessTools.Field(comp.GetType(), "color")?.GetValue(comp) is Color c && c.a > 0f)
            {
                return c;
            }
            return Color.white;
        }

        public static Color SecondEyeColour(Pawn pawn)
        {
            ThingComp comp = CompOf(PartFor("Eye"), pawn);
            return comp != null && AccessTools.Field(comp.GetType(), "secondColor")?.GetValue(comp) is Color c ? c : Color.clear;
        }

        /// <summary>Sets the main eye colour; without odd eyes both eyes take it.</summary>
        public static void SetEyeColour(Pawn pawn, Color colour)
        {
            WriteEyeColour(pawn, colour, second: false);
            if (!HasOddEyes(pawn))
            {
                WriteEyeColour(pawn, colour, second: true);
            }
        }

        public static void SetSecondEyeColour(Pawn pawn, Color colour)
        {
            WriteEyeColour(pawn, colour, second: true);
        }

        public static bool HasOddEyes(Pawn pawn)
        {
            if (oddEyes.TryGetValue(pawn, out object odd))
            {
                return (bool)odd;
            }
            Color second = SecondEyeColour(pawn);
            return second.a > 0f && !CACUI.ColorsClose(EyeColour(pawn), second);
        }

        public static void SetOddEyes(Pawn pawn, bool odd)
        {
            oddEyes.AddOrUpdate(pawn, odd);
            if (odd)
            {
                Color second = SecondEyeColour(pawn);
                if (second.a <= 0f || CACUI.ColorsClose(EyeColour(pawn), second))
                {
                    // Start the second eye on a clearly different colour so the change shows.
                    WriteEyeColour(pawn, new Color(0.85f, 0.65f, 0.2f), second: true);
                }
            }
            else
            {
                // Copy rather than clear: Facial Animation picks a random second colour when it's empty.
                WriteEyeColour(pawn, EyeColour(pawn), second: true);
            }
        }

        private static void WriteEyeColour(Pawn pawn, Color colour, bool second)
        {
            ThingComp comp = CompOf(PartFor("Eye"), pawn);
            if (comp == null)
            {
                return;
            }
            try
            {
                Type type = comp.GetType();
                if (second)
                {
                    AccessTools.Field(type, "secondColor")?.SetValue(comp, colour);
                }
                else
                {
                    PropertyInfo faceColour = AccessTools.Property(type, "FaceColor");
                    if (faceColour != null && faceColour.CanWrite)
                    {
                        faceColour.SetValue(comp, colour, null);
                    }
                    AccessTools.Field(type, "color")?.SetValue(comp, colour);
                }
                // Mark it changed and let the renderer pick it up. Reloading textures here would clear the flag
                // before the portrait is redrawn, and the change wouldn't show.
                AccessTools.Field(type, "dirtyFlag")?.SetValue(comp, true);
                Refresh(pawn);
            }
            catch (Exception e)
            {
                Log.Warning("[Create a Colonist] Could not set the Facial Animation eye colour: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- gallery presets

        /// <summary>Styles and iris colours as "key=value" lines for a gallery preset, or null without Facial Animation.</summary>
        public static List<string> Save(Pawn pawn)
        {
            if (!Active || !HasEyeColour(pawn))
            {
                return null;
            }
            List<string> lines = new List<string>();
            foreach (Part part in Parts)
            {
                string style = CurrentStyle(part, pawn);
                if (!style.NullOrEmpty())
                {
                    lines.Add(part.key + "=" + style);
                }
            }
            lines.Add("OddEyes=" + HasOddEyes(pawn));
            lines.Add("EyeColour=" + ColourText(EyeColour(pawn)));
            lines.Add("SecondEyeColour=" + ColourText(SecondEyeColour(pawn)));
            return lines;
        }

        /// <summary>Puts saved styles back. Styles are only looked up by name among Facial Animation's own defs.</summary>
        public static void Restore(Pawn pawn, List<string> lines)
        {
            if (lines == null || !Active || !HasEyeColour(pawn))
            {
                return;
            }
            Dictionary<string, string> saved = new Dictionary<string, string>();
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    saved[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            foreach (Part part in Parts)
            {
                if (saved.TryGetValue(part.key, out string name) && part.allDefs.GetValue(null, null) is IEnumerable all)
                {
                    foreach (object o in all)
                    {
                        if (o is Def def && def.defName == name)
                        {
                            SetStyle(part, pawn, def);
                            break;
                        }
                    }
                }
            }
            if (saved.TryGetValue("OddEyes", out string odd) && bool.TryParse(odd, out bool isOdd))
            {
                SetOddEyes(pawn, isOdd);
            }
            if (saved.TryGetValue("EyeColour", out string first) && TryParseColour(first, out Color c1))
            {
                SetEyeColour(pawn, c1);
            }
            if (HasOddEyes(pawn) && saved.TryGetValue("SecondEyeColour", out string second) && TryParseColour(second, out Color c2))
            {
                SetSecondEyeColour(pawn, c2);
            }
        }

        private static string ColourText(Color c)
        {
            return string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        }

        private static bool TryParseColour(string text, out Color colour)
        {
            colour = Color.white;
            string[] parts = text.Split(',');
            float[] v = new float[4];
            if (parts.Length != 4)
            {
                return false;
            }
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                {
                    return false;
                }
            }
            colour = new Color(Mathf.Clamp01(v[0]), Mathf.Clamp01(v[1]), Mathf.Clamp01(v[2]), Mathf.Clamp01(v[3]));
            return true;
        }

        // ---------------------------------------------------------------- helpers

        private static ThingComp CompOf(Part part, Pawn pawn)
        {
            if (part == null || pawn?.AllComps == null)
            {
                return null;
            }
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (comp != null && part.compType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }

        private static void Refresh(Pawn pawn)
        {
            PortraitsCache.SetDirty(pawn);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace CreateAColonist
{
    /// <summary>
    /// A saved colony: every colonist (as a <see cref="ColonistPreset"/>, inventories and all), their family (each
    /// other and relatives outside the colony), the pets and who they're bonded to, and the colony supplies. Like saved colonists it is stored
    /// as defs and values, so it loads into any new game and anything a removed mod added is simply skipped.
    /// </summary>
    public class ColonyPreset : IExposable
    {
        public string presetName;
        public string scenarioName;
        public List<ColonistPreset> colonists = new List<ColonistPreset>();
        public FamilyTree family = new FamilyTree();
        public List<PetEntry> pets = new List<PetEntry>();
        public List<SupplyData> supplies = new List<SupplyData>();

        /// <summary>The file this colony was loaded from (not saved).</summary>
        public string filePath;

        public class PetEntry : IExposable
        {
            public PawnKindDef kind;
            public string name;
            public Gender gender;
            public int age;
            public int master = -1;
            public int coat = -1;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref kind, "kind");
                Scribe_Values.Look(ref name, "name");
                Scribe_Values.Look(ref gender, "gender");
                Scribe_Values.Look(ref age, "age");
                Scribe_Values.Look(ref master, "master", -1);
                Scribe_Values.Look(ref coat, "coat", -1);
            }
        }

        public class SupplyData : IExposable
        {
            public ThingDef def;
            public ThingDef stuff;
            public int count = 1;
            public bool hasQuality;
            public QualityCategory quality = QualityCategory.Normal;
            public bool scattered;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref def, "def");
                Scribe_Defs.Look(ref stuff, "stuff");
                Scribe_Values.Look(ref count, "count", 1);
                Scribe_Values.Look(ref hasQuality, "hasQuality");
                Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
                Scribe_Values.Look(ref scattered, "scattered");
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref presetName, "presetName");
            Scribe_Values.Look(ref scenarioName, "scenarioName");
            Scribe_Collections.Look(ref colonists, "colonists", LookMode.Deep);
            Scribe_Deep.Look(ref family, "family");
            Scribe_Collections.Look(ref pets, "pets", LookMode.Deep);
            Scribe_Collections.Look(ref supplies, "supplies", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                colonists ??= new List<ColonistPreset>();
                family ??= new FamilyTree();
                pets ??= new List<PetEntry>();
                supplies ??= new List<SupplyData>();
                colonists.RemoveAll(c => c == null);
                pets.RemoveAll(p => p?.kind == null);
                supplies.RemoveAll(s => s?.def == null || s.count <= 0);
            }
        }

        public string Summary => "CAC_ColonySummary".Translate(colonists.Count, pets.Count, supplies.Count);

        // ---------------------------------------------------------------- capture

        public static ColonyPreset From(List<Pawn> colonistPawns, CreatorState state, string name)
        {
            ColonyPreset c = new ColonyPreset { presetName = name, scenarioName = state.originalScenario?.name };
            foreach (Pawn p in colonistPawns)
            {
                c.colonists.Add(ColonistPreset.From(p, p.Name?.ToStringFull ?? p.LabelShortCap, withFamily: false));
            }
            c.family = FamilyTree.From(colonistPawns);
            foreach (Pawn pet in state.pets)
            {
                c.pets.Add(new PetEntry
                {
                    kind = pet.kindDef,
                    name = pet.Name?.ToStringFull,
                    gender = pet.gender,
                    age = pet.ageTracker.AgeBiologicalYears,
                    master = colonistPawns.IndexOf(PawnEditor.GetBondedMaster(pet)),
                    coat = pet.GetGraphicIndex()
                });
            }
            foreach (SupplyEntry s in state.supplies.Where(s => s.def != null && s.count > 0))
            {
                c.supplies.Add(new SupplyData
                {
                    def = s.def,
                    stuff = s.stuff,
                    count = s.count,
                    hasQuality = s.quality.HasValue,
                    quality = s.quality ?? QualityCategory.Normal,
                    scattered = s.scattered
                });
            }
            return c;
        }

        public List<SupplyEntry> SupplyEntries()
        {
            return supplies.Select(s => new SupplyEntry
            {
                def = s.def,
                stuff = s.def.MadeFromStuff ? (s.stuff ?? GenStuff.DefaultStuffFor(s.def)) : null,
                count = s.count,
                quality = s.hasQuality ? s.quality : (QualityCategory?)null,
                scattered = s.scattered
            }).ToList();
        }

        // ---------------------------------------------------------------- files

        /// <summary>Saved colonies live in their own subfolder, so the colonist list never picks them up.</summary>
        public static string Folder
        {
            get
            {
                string dir = Path.Combine(ColonistPreset.Folder, "Colonies");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string PathFor(string name)
        {
            return Path.Combine(Folder, GenFile.SanitizedFileName(name) + ".xml");
        }

        public void Save()
        {
            filePath = PathFor(presetName);
            GalleryFiles.Save(filePath, "CreateAColonistColony", "colony", this);
        }

        public static List<ColonyPreset> LoadAll()
        {
            List<ColonyPreset> result = new List<ColonyPreset>();
            foreach (string file in Directory.GetFiles(Folder, "*.xml").OrderBy(f => f))
            {
                ColonyPreset c = GalleryFiles.Load<ColonyPreset>(file, "colony");
                if (c != null && c.colonists.Any())
                {
                    c.filePath = Path.GetFullPath(file);
                    if (c.presetName.NullOrEmpty())
                    {
                        c.presetName = Path.GetFileNameWithoutExtension(file);
                    }
                    result.Add(c);
                }
            }
            return result;
        }

        public void Delete()
        {
            string path = filePath ?? PathFor(presetName);
            GalleryFiles.Delete(path, Folder);
            GalleryFiles.Delete(GalleryThumbnails.PathFor(path), Folder);
        }

        /// <summary>Saves under the new name, then removes the old file (its picture moves with it).</summary>
        public void RenameTo(string newName)
        {
            string oldPath = filePath ?? PathFor(presetName);
            presetName = newName;
            Save();
            GalleryFiles.FinishRename(oldPath, filePath, Folder);
        }

        /// <summary>A copy under a free "(copy)" name, picture included. Null if the original can't be read.</summary>
        public ColonyPreset Duplicate()
        {
            ColonyPreset copy = filePath != null ? GalleryFiles.Load<ColonyPreset>(filePath, "colony") : null;
            if (copy == null)
            {
                return null;
            }
            copy.presetName = GalleryFiles.CopyName(presetName, PathFor);
            copy.Save();
            GalleryFiles.CopyPicture(filePath, copy.filePath, Folder, move: false);
            return copy;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    /// <summary>
    /// A saved colonist. Stored as defs and values (not a whole Pawn), so presets load into any future colony
    /// and survive mods being removed: anything that no longer exists is simply skipped.
    /// </summary>
    public class ColonistPreset : IExposable
    {
        public string presetName;
        public string first, nick, last;
        public Gender gender;
        public int age;
        public int ageChronological = -1;

        public XenotypeDef xenotype;
        public string customXenotypeName;
        public XenotypeIconDef customXenotypeIcon;
        public bool customXenotypeInheritable;
        public List<GeneDef> customGenes;

        public bool eyeChoiceSaved;
        public GeneDef eyeGene;

        // Every gene exactly (some xenotypes pick genes at random). Null in older presets.
        public List<GeneDef> endogenes, xenogenes;

        public BackstoryDef childhood, adulthood;
        public List<TraitEntry> traits = new List<TraitEntry>();

        // Traits their genes give them. Kept so a gene whose trait the colonist doesn't have (the game drops it when it
        // clashes with a trait they already had) comes back without it. Null in older presets.
        public List<TraitEntry> geneTraits;
        public List<PassionEntry> passions = new List<PassionEntry>();

        // Skill levels were set by hand: they come back exactly, whatever the backstories. False in older presets.
        public bool manualSkills;

        // Facial Animation styles and iris colours ("key=value"); null without it and in older presets.
        public List<string> facialAnimation;

        public BodyTypeDef bodyType;
        public HeadTypeDef headType;
        public HairDef hair;
        public BeardDef beard;
        public TattooDef faceTattoo, bodyTattoo;
        public Color hairColor = Color.white;
        public bool hasSkinOverride;
        public Color skinOverride = Color.white;
        public ColorDef favoriteColor;

        // Natural looks: the skin and hair colour genes every colonist is born with, and the skin colour itself.
        public GeneDef melaninGene, hairColorGene;
        public bool hasSkinBase;
        public Color skinBase = Color.white;

        public List<ThingEntry> apparel = new List<ThingEntry>();
        public ThingEntry weapon;
        public List<ThingEntry> inventory = new List<ThingEntry>();

        // Things that arrive next to the colonist (what didn't fit in their inventory). Null in older presets.
        public List<ThingEntry> possessions;

        // Presets from before the Health tab have no health list and leave health alone.
        public List<HediffEntry> health;

        public string ideoName;
        public string ideoFile;
        public float ideoCertainty = -1f;

        // Their relatives outside the group (parents, partners, children...). Null in older presets, and in the
        // colonists of a saved colony, whose family is saved for the colony as a whole.
        public FamilyTree family;

        /// <summary>The file this preset was loaded from (not saved). Delete uses it, not the name inside the file.</summary>
        public string filePath;

        public class TraitEntry : IExposable
        {
            public TraitDef def;
            public int degree;
            public bool greyedOut;

            // Added by the player, so it stays when a backstory that also gives it is swapped. False in older presets.
            public bool manual;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref def, "def");
                Scribe_Values.Look(ref degree, "degree");
                Scribe_Values.Look(ref greyedOut, "greyedOut");
                Scribe_Values.Look(ref manual, "manual");
            }

            public bool Matches(Trait trait)
            {
                return trait.def == def && trait.Degree == degree;
            }

            public static TraitEntry From(Trait trait)
            {
                // Greyed out by a clashing trait (a gene's, usually); greying from genes is worked out again on load.
                return new TraitEntry { def = trait.def, degree = trait.Degree, greyedOut = trait.suppressedByTrait, manual = PawnEditor.IsManualTrait(trait) };
            }
        }

        public class PassionEntry : IExposable
        {
            public SkillDef skill;
            public Passion passion;
            public int level = -1;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref skill, "skill");
                Scribe_Values.Look(ref passion, "passion");
                Scribe_Values.Look(ref level, "level", -1);
            }
        }

        public class HediffEntry : IExposable
        {
            public HediffDef def;
            public BodyPartDef part;
            public int partOrdinal;
            public float severity;
            public bool permanent;
            public int level;
            public int painCategory = -1;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref def, "def");
                Scribe_Defs.Look(ref part, "part");
                Scribe_Values.Look(ref partOrdinal, "partOrdinal");
                Scribe_Values.Look(ref severity, "severity");
                Scribe_Values.Look(ref permanent, "permanent");
                Scribe_Values.Look(ref level, "level");
                Scribe_Values.Look(ref painCategory, "painCategory", -1);
            }

            public static HediffEntry From(Pawn pawn, Hediff hediff)
            {
                return new HediffEntry
                {
                    def = hediff.def,
                    part = hediff.Part?.def,
                    partOrdinal = PawnEditor.PartOrdinal(pawn, hediff.Part),
                    severity = hediff.Severity,
                    permanent = hediff.IsPermanent(),
                    level = hediff is Hediff_Level leveled ? leveled.level : 0,
                    // Whether an old wound aches, itches or doesn't hurt.
                    painCategory = hediff.TryGetComp<HediffComp_GetsPermanent>() is HediffComp_GetsPermanent comp && comp.IsPermanent ? (int)comp.PainCategory : -1
                };
            }
        }

        public class ThingEntry : IExposable
        {
            public ThingDef def;
            public ThingDef stuff;
            public QualityCategory quality = QualityCategory.Normal;
            public bool hasColor;
            public Color color = Color.white;
            public int count = 1;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref def, "def");
                Scribe_Defs.Look(ref stuff, "stuff");
                Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
                Scribe_Values.Look(ref hasColor, "hasColor");
                Scribe_Values.Look(ref color, "color", Color.white);
                Scribe_Values.Look(ref count, "count", 1);
            }

            public static ThingEntry From(Thing thing)
            {
                Color? c = PawnEditor.ColorOf(thing);
                return new ThingEntry
                {
                    def = thing.def,
                    stuff = thing.Stuff,
                    quality = PawnEditor.QualityOf(thing),
                    hasColor = c.HasValue,
                    color = c ?? Color.white,
                    count = thing.stackCount
                };
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref presetName, "presetName");
            Scribe_Values.Look(ref first, "first");
            Scribe_Values.Look(ref nick, "nick");
            Scribe_Values.Look(ref last, "last");
            Scribe_Values.Look(ref gender, "gender");
            Scribe_Values.Look(ref age, "age", 25);
            Scribe_Values.Look(ref ageChronological, "ageChronological", -1);
            Scribe_Defs.Look(ref xenotype, "xenotype");
            Scribe_Values.Look(ref customXenotypeName, "customXenotypeName");
            Scribe_Defs.Look(ref customXenotypeIcon, "customXenotypeIcon");
            Scribe_Values.Look(ref customXenotypeInheritable, "customXenotypeInheritable");
            Scribe_Collections.Look(ref customGenes, "customGenes", LookMode.Def);
            Scribe_Values.Look(ref eyeChoiceSaved, "eyeChoiceSaved");
            Scribe_Defs.Look(ref eyeGene, "eyeGene");
            Scribe_Collections.Look(ref endogenes, "endogenes", LookMode.Def);
            Scribe_Collections.Look(ref xenogenes, "xenogenes", LookMode.Def);
            Scribe_Defs.Look(ref childhood, "childhood");
            Scribe_Defs.Look(ref adulthood, "adulthood");
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            Scribe_Collections.Look(ref geneTraits, "geneTraits", LookMode.Deep);
            Scribe_Collections.Look(ref passions, "passions", LookMode.Deep);
            Scribe_Values.Look(ref manualSkills, "manualSkills");
            Scribe_Collections.Look(ref facialAnimation, "facialAnimation", LookMode.Value);
            Scribe_Defs.Look(ref bodyType, "bodyType");
            Scribe_Defs.Look(ref headType, "headType");
            Scribe_Defs.Look(ref hair, "hair");
            Scribe_Defs.Look(ref beard, "beard");
            Scribe_Defs.Look(ref faceTattoo, "faceTattoo");
            Scribe_Defs.Look(ref bodyTattoo, "bodyTattoo");
            Scribe_Values.Look(ref hairColor, "hairColor", Color.white);
            Scribe_Values.Look(ref hasSkinOverride, "hasSkinOverride");
            Scribe_Values.Look(ref skinOverride, "skinOverride", Color.white);
            Scribe_Defs.Look(ref favoriteColor, "favoriteColor");
            Scribe_Defs.Look(ref melaninGene, "melaninGene");
            Scribe_Defs.Look(ref hairColorGene, "hairColorGene");
            Scribe_Values.Look(ref hasSkinBase, "hasSkinBase");
            Scribe_Values.Look(ref skinBase, "skinBase", Color.white);
            Scribe_Collections.Look(ref apparel, "apparel", LookMode.Deep);
            Scribe_Deep.Look(ref weapon, "weapon");
            Scribe_Collections.Look(ref inventory, "inventory", LookMode.Deep);
            Scribe_Collections.Look(ref possessions, "possessions", LookMode.Deep);
            Scribe_Collections.Look(ref health, "health", LookMode.Deep);
            Scribe_Values.Look(ref ideoName, "ideoName");
            Scribe_Values.Look(ref ideoFile, "ideoFile");
            Scribe_Values.Look(ref ideoCertainty, "ideoCertainty", -1f);
            Scribe_Deep.Look(ref family, "family");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                traits ??= new List<TraitEntry>();
                passions ??= new List<PassionEntry>();
                apparel ??= new List<ThingEntry>();
                inventory ??= new List<ThingEntry>();
                // A trait mod can be removed or change its trait's levels since the colonist was saved.
                traits.RemoveAll(t => t?.def == null || !t.def.degreeDatas.Any(d => d.degree == t.degree));
                geneTraits?.RemoveAll(t => t?.def == null);
                passions.RemoveAll(p => p?.skill == null);
                apparel.RemoveAll(a => a?.def == null);
                inventory.RemoveAll(a => a?.def == null);
                possessions?.RemoveAll(a => a?.def == null);
                endogenes?.RemoveAll(g => g == null);
                xenogenes?.RemoveAll(g => g == null);
                health?.RemoveAll(h => h?.def == null);
                if (weapon?.def == null)
                {
                    weapon = null;
                }
            }
        }

        public string Summary
        {
            get
            {
                List<string> bits = new List<string>();
                bits.Add(gender.GetLabel().CapitalizeFirst() + ", " + age);
                if (!customXenotypeName.NullOrEmpty())
                {
                    bits.Add(customXenotypeName.CapitalizeFirst());
                }
                else if (xenotype != null && ModsConfig.BiotechActive)
                {
                    bits.Add(xenotype.LabelCap);
                }
                BackstoryDef title = adulthood ?? childhood;
                if (title != null)
                {
                    bits.Add(title.TitleCapFor(gender));
                }
                return bits.ToCommaList();
            }
        }

        // ---------------------------------------------------------------- capture / apply

        /// <param name="withFamily">Also save their relatives outside the group (off for a colony's colonists).</param>
        public static ColonistPreset From(Pawn pawn, string name, bool withFamily = true)
        {
            ColonistPreset p = new ColonistPreset { presetName = name };
            NameTriple nt = pawn.Name as NameTriple;
            p.first = nt?.First;
            p.nick = nt?.Nick;
            p.last = nt?.Last;
            p.gender = pawn.gender;
            p.age = pawn.ageTracker.AgeBiologicalYears;
            p.ageChronological = pawn.ageTracker.AgeChronologicalYears;
            if (pawn.genes != null && ModsConfig.BiotechActive)
            {
                if (pawn.genes.UniqueXenotype)
                {
                    p.customXenotypeName = pawn.genes.xenotypeName;
                    p.customXenotypeIcon = pawn.genes.iconDef;
                    p.customXenotypeInheritable = pawn.genes.Xenogenes.NullOrEmpty();
                    p.customGenes = (p.customXenotypeInheritable ? pawn.genes.Endogenes : pawn.genes.Xenogenes)
                        .Where(g => g.def.endogeneCategory != EndogeneCategory.Melanin && g.def.endogeneCategory != EndogeneCategory.HairColor)
                        .Select(g => g.def).ToList();
                }
                else
                {
                    p.xenotype = pawn.genes.Xenotype;
                }
                p.eyeChoiceSaved = true;
                p.eyeGene = PawnEditor.CurrentEyeGene(pawn);
                p.endogenes = pawn.genes.Endogenes.Select(g => g.def).ToList();
                p.xenogenes = pawn.genes.Xenogenes.Select(g => g.def).ToList();
            }
            p.childhood = pawn.story.Childhood;
            p.adulthood = pawn.story.Adulthood;
            p.traits = pawn.story.traits.allTraits.Where(t => t.sourceGene == null).Select(TraitEntry.From).ToList();
            p.geneTraits = pawn.story.traits.allTraits.Where(t => t.sourceGene != null).Select(TraitEntry.From).ToList();
            // The level without gene aptitudes: the game adds those on top whenever the level is read.
            p.passions = pawn.skills.skills.Select(s => new PassionEntry { skill = s.def, passion = s.passion, level = s.levelInt }).ToList();
            p.manualSkills = PawnEditor.HasManualSkills(pawn);
            p.facialAnimation = FacialAnimationCompat.Save(pawn);
            p.bodyType = pawn.story.bodyType;
            p.headType = pawn.story.headType;
            p.hair = pawn.story.hairDef;
            p.beard = pawn.style?.beardDef;
            p.faceTattoo = pawn.style?.FaceTattoo;
            p.bodyTattoo = pawn.style?.BodyTattoo;
            p.hairColor = pawn.story.HairColor;
            p.hasSkinOverride = pawn.story.skinColorOverride.HasValue;
            p.skinOverride = pawn.story.skinColorOverride ?? Color.white;
            p.favoriteColor = pawn.story.favoriteColor;
            p.melaninGene = pawn.genes?.Endogenes.FirstOrDefault(g => PawnEditor.IsSkinGene(g.def))?.def;
            p.hairColorGene = pawn.genes?.Endogenes.FirstOrDefault(g => g.def.endogeneCategory == EndogeneCategory.HairColor)?.def;
            p.hasSkinBase = true;
            p.skinBase = pawn.story.SkinColorBase;
            p.apparel = pawn.apparel.WornApparel.Select(ThingEntry.From).ToList();
            p.weapon = pawn.equipment.Primary != null ? ThingEntry.From(pawn.equipment.Primary) : null;
            p.inventory = pawn.inventory.innerContainer.Select(ThingEntry.From).ToList();
            p.possessions = Find.GameInitData?.startingPossessions != null && Find.GameInitData.startingPossessions.TryGetValue(pawn, out List<ThingDefCount> left)
                ? left.Select(t => new ThingEntry { def = t.ThingDef, count = t.Count }).ToList()
                : new List<ThingEntry>();
            p.health = PawnEditor.ShownHediffs(pawn).Where(PawnEditor.CanRemoveHediff).Select(h => HediffEntry.From(pawn, h)).ToList();
            if (PawnEditor.CanHaveIdeo(pawn))
            {
                p.ideoName = pawn.Ideo.name;
                p.ideoFile = PawnEditor.SavedIdeoFileName(pawn.Ideo);
                p.ideoCertainty = pawn.ideo.Certainty;
            }
            if (withFamily)
            {
                p.family = FamilyTree.From(new List<Pawn> { pawn }, OthersInGroup(pawn));
            }
            return p;
        }

        /// <summary>The other colonists in the group: their relationships with this colonist are left alone.</summary>
        private static HashSet<Pawn> OthersInGroup(Pawn pawn)
        {
            // Left-behind candidates aren't in the group: to this colonist they're relatives elsewhere.
            GameInitData data = Find.GameInitData;
            HashSet<Pawn> others = new HashSet<Pawn>(data == null ? Enumerable.Empty<Pawn>() : data.startingAndOptionalPawns.Take(data.startingPawnCount));
            others.Remove(pawn);
            return others;
        }

        /// <param name="withFamily">Also bring back their relatives outside the group (off when a colony does it).</param>
        public void ApplyTo(Pawn pawn, bool withFamily = true)
        {
            PawnEditor.SetGender(pawn, gender);
            if (!first.NullOrEmpty())
            {
                PawnEditor.SetName(pawn, first, nick, last);
            }
            // A scenario can ask for someone older than this colonist was saved at.
            bool oldAsSaved = age >= PawnEditor.MinAgeYears(pawn);
            PawnEditor.SetBiologicalAge(pawn, Mathf.Max(age, PawnEditor.MinAgeYears(pawn)));
            bool baby = pawn.DevelopmentalStage.Baby();
            if (ageChronological >= 0)
            {
                // Years spent in cryptosleep and the like.
                PawnEditor.SetChronologicalAge(pawn, ageChronological);
            }

            if (pawn.genes != null && ModsConfig.BiotechActive)
            {
                if (!customXenotypeName.NullOrEmpty() && customGenes != null)
                {
                    CustomXenotype custom = new CustomXenotype
                    {
                        name = customXenotypeName,
                        iconDef = customXenotypeIcon,
                        inheritable = customXenotypeInheritable
                    };
                    custom.genes.AddRange(customGenes.Where(g => g != null));
                    PawnEditor.SetCustomXenotype(pawn, custom);
                }
                else if (xenotype != null)
                {
                    PawnEditor.SetXenotype(pawn, xenotype);
                }
                if (endogenes != null && xenogenes != null)
                {
                    // Every gene as saved, including ones the xenotype picks at random.
                    PawnEditor.SetExactGenes(pawn, endogenes, xenogenes);
                }
                else if (eyeChoiceSaved)
                {
                    // Older presets: eye colour if it was saved (presets from before eye colour don't touch the eyes).
                    PawnEditor.SetEyeGene(pawn, eyeGene);
                }
            }
            TraceTraits(pawn, "genes");
            // Natural skin and hair colour genes, so the colonist doesn't come back a different shade.
            PawnEditor.SetNaturalGene(pawn, melaninGene, skin: true);
            PawnEditor.SetNaturalGene(pawn, hairColorGene, skin: false);
            if (hasSkinBase)
            {
                pawn.story.SkinColorBase = skinBase;
            }

            TraceTraits(pawn, "skin");
            if (childhood != null && oldAsSaved)
            {
                pawn.story.Childhood = childhood;
            }
            pawn.story.Adulthood = pawn.ageTracker.AgeBiologicalYearsFloat >= PawnEditor.AdulthoodMinAge ? (adulthood ?? pawn.story.Adulthood) : null;

            RestoreTraits(pawn);
            TraceTraits(pawn, "traits");
            PawnEditor.RecalculateSkills(pawn, force: true);
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.skills.Notify_SkillDisablesChanged();
            // Skill levels come from the backstories, with a random part; keep the saved ones when the backstories
            // came back exactly, so the colonist is the same one that was saved. Hand-set levels always come back.
            bool sameStory = manualSkills || (pawn.story.Childhood == childhood && pawn.story.Adulthood == adulthood);
            PawnEditor.SetManualSkills(pawn, manualSkills);
            foreach (PassionEntry pe in passions)
            {
                SkillRecord record = pawn.skills.GetSkill(pe.skill);
                if (record == null)
                {
                    continue;
                }
                record.passion = pe.passion;
                if (sameStory && pe.level >= 0 && !record.TotallyDisabled)
                {
                    record.levelInt = Mathf.Clamp(pe.level, SkillRecord.MinLevel, SkillRecord.MaxLevel);
                }
            }

            TraceTraits(pawn, "skills");
            if (bodyType != null && PawnEditor.AllowedBodyTypes(pawn).Contains(bodyType))
            {
                pawn.story.bodyType = bodyType;
            }
            if (headType != null && PawnEditor.AllowedHeads(pawn).Contains(headType))
            {
                pawn.story.headType = headType;
            }
            if (hair != null)
            {
                pawn.story.hairDef = hair;
            }
            if (pawn.style != null)
            {
                if (beard != null)
                {
                    pawn.style.beardDef = beard;
                }
                if (ModsConfig.IdeologyActive)
                {
                    if (faceTattoo != null)
                    {
                        pawn.style.FaceTattoo = faceTattoo;
                    }
                    if (bodyTattoo != null)
                    {
                        pawn.style.BodyTattoo = bodyTattoo;
                    }
                }
            }
            pawn.story.HairColor = hairColor;
            pawn.story.skinColorOverride = hasSkinOverride ? skinOverride : (Color?)null;
            if (favoriteColor != null)
            {
                pawn.story.favoriteColor = favoriteColor;
            }

            TraceTraits(pawn, "looks");
            // What a baby or child wore won't fit someone a scenario has aged, who stays dressed for their age.
            if (oldAsSaved)
            {
                foreach (Apparel ap in pawn.apparel.WornApparel.ToList())
                {
                    pawn.apparel.Remove(ap);
                    ap.Destroy();
                }
                foreach (ThingEntry e in apparel)
                {
                    if (e.def.IsApparel && PawnEditor.CanWear(pawn, e.def))
                    {
                        PawnEditor.Wear(pawn, e.def, e.stuff, e.quality, e.hasColor ? e.color : (Color?)null);
                    }
                }
            }
            PawnEditor.SetWeapon(pawn, baby ? null : weapon?.def, weapon?.stuff, weapon?.quality ?? QualityCategory.Normal);
            pawn.inventory.innerContainer.ClearAndDestroyContents();
            // Exactly as saved, even over the carry limit (say, heavy armour): the overflow lands next to the
            // colonist at the start, as it would have if the colony had started when it was saved.
            foreach (ThingEntry e in baby ? Enumerable.Empty<ThingEntry>() : inventory)
            {
                PawnEditor.AddInventory(pawn, e.def, e.count, e.stuff, e.quality, withinCarryLimit: false);
            }
            // A locked re-roll carries a grown colonist's belongings; a baby keeps the food the game gave them.
            if (!baby && oldAsSaved && possessions != null && Find.GameInitData?.startingPossessions != null)
            {
                Find.GameInitData.startingPossessions[pawn] = possessions.Select(e => new ThingDefCount(e.def, Mathf.Max(1, e.count))).ToList();
            }

            TraceTraits(pawn, "gear");
            if (health != null)
            {
                PawnEditor.RemoveAllHediffs(pawn);
                // Missing parts first, then prosthetics, then everything else, so each finds its part in the right state.
                foreach (HediffEntry e in health.OrderBy(h => typeof(Hediff_MissingPart).IsAssignableFrom(h.def.hediffClass) ? 0
                    : typeof(Hediff_AddedPart).IsAssignableFrom(h.def.hediffClass) ? 1 : 2))
                {
                    BodyPartRecord part = e.part == null ? null : PawnEditor.PartByOrdinal(pawn, e.part, e.partOrdinal);
                    if (e.part != null && part == null)
                    {
                        continue;
                    }
                    try
                    {
                        PawnEditor.RestoreHediff(pawn, e.def, part, e.severity, e.permanent, e.level, e.painCategory >= 0 ? (PainCategory)e.painCategory : (PainCategory?)null);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Create a Colonist] Could not restore " + e.def.defName + " from a saved colonist: " + ex.Message);
                    }
                }
                PawnEditor.AfterHealthChanged(pawn);
            }

            TraceTraits(pawn, "health");
            if (!ideoName.NullOrEmpty() && PawnEditor.CanHaveIdeo(pawn))
            {
                // The same ideoligion if this world has it, otherwise the player's saved copy if there is one.
                Ideo ideo = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => !i.hidden && i.name == ideoName);
                // Only a plain file name in the game's own Ideos folder, never a path from the preset.
                if (ideo == null && !ideoFile.NullOrEmpty() && ideoFile == GenFile.SanitizedFileName(ideoFile))
                {
                    ideo = PawnEditor.UseSavedIdeo(GenFilePaths.AbsPathForIdeo(ideoFile));
                }
                if (ideo != null)
                {
                    PawnEditor.SetIdeo(pawn, ideo, ideoCertainty >= 0f ? ideoCertainty : (float?)null);
                }
            }
            TraceTraits(pawn, "ideo");
            if (withFamily && family != null)
            {
                family.ApplyTo(new List<Pawn> { pawn }, OthersInGroup(pawn));
            }
            FacialAnimationCompat.Restore(pawn, facialAnimation);
            // Again at the end: with big mod lists, other mods can add or take a trait while the rest is applied.
            RestoreTraits(pawn);
            // Traits decide which work a colonist can do; refresh that after any change above.
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.skills.Notify_SkillDisablesChanged();
            TraceTraits(pawn, "done");
            PawnEditor.Dirty(pawn);
        }

        /// <summary>
        /// Exactly the saved traits, greyed out where they were. They came from a real colonist, so they go together
        /// even where the picker wouldn't allow it (a backstory can force a trait that normally clashes).
        /// </summary>
        private void RestoreTraits(Pawn pawn)
        {
            TraitSet set = pawn.story.traits;
            // Anything not saved goes, greyed out or not. Gene traits only when the preset knows them (older ones don't).
            foreach (Trait t in set.allTraits.Where(t => t.sourceGene == null ? !traits.Any(e => e.Matches(t)) : geneTraits != null && !geneTraits.Any(e => e.Matches(t))).ToList())
            {
                PawnEditor.RemoveTraitOnly(pawn, t);
            }
            foreach (TraitEntry e in traits)
            {
                if (!set.allTraits.Any(t => t.sourceGene == null && e.Matches(t)))
                {
                    // suppressConflicts lets it sit beside a gene's trait of the same kind (greyed out), as the game
                    // does; the saved greying is set below either way.
                    set.GainTrait(new Trait(e.def, e.degree), suppressConflicts: true);
                }
            }
            // A gene's trait goes back the way the game adds it, tied to its gene.
            foreach (TraitEntry e in geneTraits ?? Enumerable.Empty<TraitEntry>())
            {
                Gene gene = pawn.genes?.GenesListForReading.FirstOrDefault(g => g.def.forcedTraits != null && g.def.forcedTraits.Any(f => f.def == e.def && f.degree == e.degree));
                if (gene != null && !set.allTraits.Any(t => t.sourceGene != null && e.Matches(t)))
                {
                    set.GainTrait(new Trait(e.def, e.degree) { sourceGene = gene }, suppressConflicts: true);
                }
            }
            foreach (Trait t in set.allTraits)
            {
                TraitEntry saved = (t.sourceGene == null ? traits : geneTraits)?.FirstOrDefault(e => e.Matches(t));
                if (saved != null)
                {
                    t.suppressedByTrait = saved.greyedOut;
                    PawnEditor.SetManualTrait(t, saved.manual);
                }
            }
            set.RecalculateSuppression();
        }

        // Dev builds only: the traits after each stage of loading, to see where one appears or goes.
        [System.Diagnostics.Conditional("CAC_DEV")]
        private static void TraceTraits(Pawn pawn, string stage)
        {
#if CAC_DEV
            Log.Message("[CAC trace] " + pawn.LabelShort + " after " + stage + ": " + pawn.story.traits.allTraits
                .Select(t => t.def.defName + t.Degree + (t.sourceGene != null ? "(gene)" : "") + (t.Suppressed ? "(suppressed)" : "")).ToCommaList());
#endif
        }

        // ---------------------------------------------------------------- files

        public static string Folder
        {
            get
            {
                string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "CreateAColonist");
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
            GalleryFiles.Save(filePath, "CreateAColonistPreset", "colonist", this);
        }

        public static ColonistPreset Load(string path)
        {
            ColonistPreset preset = GalleryFiles.Load<ColonistPreset>(path, "colonist");
            if (preset != null)
            {
                preset.filePath = Path.GetFullPath(path);
            }
            return preset;
        }

        public static List<ColonistPreset> LoadAll()
        {
            List<ColonistPreset> result = new List<ColonistPreset>();
            foreach (string file in Directory.GetFiles(Folder, "*.xml").OrderBy(f => f))
            {
                ColonistPreset p = Load(file);
                if (p != null)
                {
                    if (p.presetName.NullOrEmpty())
                    {
                        p.presetName = Path.GetFileNameWithoutExtension(file);
                    }
                    result.Add(p);
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
        public ColonistPreset Duplicate()
        {
            ColonistPreset copy = filePath != null ? Load(filePath) : null;
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

    /// <summary>Reading, writing and deleting the gallery's files (saved colonists and saved colonies).</summary>
    public static class GalleryFiles
    {
        // The game's loader creates whatever object type a Class="..." attribute names. Gallery files never contain
        // one, so a file that does wasn't written by this mod and is skipped rather than handed to the loader.
        private static readonly Regex ClassAttribute = new Regex(@"\sClass\s*=", RegexOptions.CultureInvariant);

        public static void Save<T>(string path, string root, string node, T obj) where T : class, IExposable
        {
            SafeSaver.Save(path, root, delegate
            {
                ScribeMetaHeaderUtility.WriteMetaHeader();
                Scribe_Deep.Look(ref obj, node);
            });
        }

        public static T Load<T>(string path, string node) where T : class, IExposable
        {
            T result = null;
            try
            {
                if (ClassAttribute.IsMatch(File.ReadAllText(path)))
                {
                    Log.Warning("[Create a Colonist] Skipped " + Path.GetFileName(path) + ": it wasn't saved by Create a Colonist.");
                    return null;
                }
                Scribe.loader.InitLoading(path);
                try
                {
                    Scribe_Deep.Look(ref result, node);
                    Scribe.loader.FinalizeLoading();
                }
                catch
                {
                    Scribe.ForceStop();
                    throw;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Create a Colonist] Could not load " + path + ": " + e.Message);
                return null;
            }
            return result;
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Inside(string path, string folder)
        {
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"Name (copy)", then "Name (copy) 2" and so on: the first that isn't taken.</summary>
        public static string CopyName(string name, Func<string, string> pathFor)
        {
            string baseName = "CAC_CopyName".Translate(name);
            string candidate = baseName;
            for (int i = 2; File.Exists(pathFor(candidate)); i++)
            {
                candidate = baseName + " " + i;
            }
            return candidate;
        }

        /// <summary>Copies (or moves) a gallery file's picture to go with another gallery file in the same folder.</summary>
        public static void CopyPicture(string fromXml, string toXml, string folder, bool move)
        {
            string from = GalleryThumbnails.PathFor(fromXml);
            string to = GalleryThumbnails.PathFor(toXml);
            if (!Inside(from, folder) || !Inside(to, folder) || SamePath(from, to))
            {
                return;
            }
            if (!File.Exists(from))
            {
                // Taking over another file's name: don't leave that file's old picture behind.
                if (move && File.Exists(to))
                {
                    File.Delete(to);
                }
                return;
            }
            File.Copy(from, to, overwrite: true);
            if (move)
            {
                File.Delete(from);
            }
        }

        /// <summary>After saving under a new name: move the picture across and remove the old file.</summary>
        public static void FinishRename(string oldPath, string newPath, string folder)
        {
            if (SamePath(oldPath, newPath))
            {
                return;
            }
            CopyPicture(oldPath, newPath, folder, move: true);
            Delete(oldPath, folder);
        }

        /// <summary>Deletes a gallery file, but only ever one inside the given gallery folder.</summary>
        public static void Delete(string path, string folder)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            {
                File.Delete(full);
            }
        }
    }
}

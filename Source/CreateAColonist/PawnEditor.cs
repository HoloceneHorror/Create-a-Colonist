using System;
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
    /// <summary>Every change the creator makes to a pawn goes through here, so the game's caches stay in sync.</summary>
    public static partial class PawnEditor
    {
        public const float AdulthoodMinAge = 20f;

        /// <summary>Love relationships need both people to be this old, and a parent this much older than their child.</summary>
        public const int GrownUpAge = 18;

        private static readonly MethodInfo FinalLevelOfSkillMethod = AccessTools.Method(typeof(PawnGenerator), "FinalLevelOfSkill");
        private static readonly MethodInfo BackstoryFiltersMethod = AccessTools.Method(typeof(PawnBioAndNameGenerator), "GetBackstoryCategoryFiltersFor");
        private static readonly FieldInfo GenerationRequestsField = AccessTools.Field(typeof(StartingPawnUtility), "StartingAndOptionalPawnGenerationRequests");

        public static void Dirty(Pawn pawn)
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
        }

        public static int StartingIndex(Pawn pawn)
        {
            return Find.GameInitData.startingAndOptionalPawns.IndexOf(pawn);
        }

        public static PawnGenerationRequest RequestFor(Pawn pawn)
        {
            int index = StartingIndex(pawn);
            return StartingPawnUtility.GetGenerationRequest(Mathf.Max(index, 0));
        }

        /// <summary>
        /// Drops a starting pawn's generation request along with the pawn. The game keeps the requests in a list
        /// parallel to the pawns and has no public way to remove one; if the field ever goes away, fall back to
        /// moving it to the end (the old behaviour).
        /// </summary>
        public static void RemoveGenerationRequest(int index)
        {
            if (GenerationRequestsField?.GetValue(null) is List<PawnGenerationRequest> requests)
            {
                if (index < requests.Count)
                {
                    requests.RemoveAt(index);
                }
                return;
            }
            StartingPawnUtility.ReorderRequests(index, Find.GameInitData.startingAndOptionalPawns.Count);
        }

        // ---------------------------------------------------------------- name, gender, age

        public static void SetName(Pawn pawn, string first, string nick, string last)
        {
            pawn.Name = new NameTriple(first ?? "", nick ?? "", last ?? "");
        }

        public static void SetGender(Pawn pawn, Gender gender)
        {
            if (pawn.gender == gender)
            {
                return;
            }
            pawn.gender = gender;
            if (pawn.RaceProps.Humanlike)
            {
                if (pawn.story.bodyType == BodyTypeDefOf.Male && gender == Gender.Female)
                {
                    pawn.story.bodyType = BodyTypeDefOf.Female;
                }
                else if (pawn.story.bodyType == BodyTypeDefOf.Female && gender == Gender.Male)
                {
                    pawn.story.bodyType = BodyTypeDefOf.Male;
                }
                if (!HeadAllowed(pawn, pawn.story.headType))
                {
                    HeadTypeDef head = AllowedHeads(pawn).RandomElementWithFallback();
                    if (head != null)
                    {
                        pawn.story.headType = head;
                    }
                }
                DropUnwearableApparel(pawn);
            }
            Dirty(pawn);
        }

        public static void SetBiologicalAge(Pawn pawn, int years)
        {
            Pawn_AgeTracker age = pawn.ageTracker;
            long extraChrono = Math.Max(0L, age.AgeChronologicalTicks - age.AgeBiologicalTicks);
            DevelopmentalStage oldStage = pawn.DevelopmentalStage;
            int oldYears = age.AgeBiologicalYears;
            long ticks = years == 0 ? 0L : (long)years * GenDate.TicksPerYear + (pawn.thingIDNumber * 7919L) % (GenDate.TicksPerYear / 2);
            age.AgeBiologicalTicks = ticks;
            // Years in cryptosleep don't belong to someone who hasn't lived yet.
            age.AgeChronologicalTicks = pawn.DevelopmentalStage.Baby() ? ticks : ticks + extraChrono;
            if (!pawn.RaceProps.Humanlike)
            {
                Dirty(pawn);
                return;
            }
            DevelopmentalStage stage = pawn.DevelopmentalStage;
            if (years < GrownUpAge)
            {
                ClearLoveRelations(pawn);
            }
            ClearImpossibleParents(pawn);
            if (stage.Juvenile())
            {
                foreach (Trait t in pawn.story.traits.allTraits.Where(t => t.sourceGene == null && IsOrientation(t.def)).ToList())
                {
                    RemoveTraitOnly(pawn, t);
                }
            }
            if (years < AdulthoodMinAge && pawn.story.Adulthood != null)
            {
                SwapBackstory(pawn, BackstorySlot.Adulthood, null);
            }
            else if (years >= AdulthoodMinAge && pawn.story.Adulthood == null)
            {
                BackstoryDef adult = BackstoryOptions(pawn, BackstorySlot.Adulthood).RandomElementWithFallback();
                if (adult != null)
                {
                    SwapBackstory(pawn, BackstorySlot.Adulthood, adult);
                }
            }
            if (stage != oldStage)
            {
                pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
                DropUnwearableApparel(pawn);
                if (!pawn.apparel.WornApparel.Any())
                {
                    // Grown-up clothes don't fit a child (and vice versa); dress them like the game would.
                    PawnGenerationRequest request = RequestFor(pawn);
                    request.AllowedDevelopmentalStages = stage;
                    PawnApparelGenerator.GenerateStartingApparelFor(pawn, request);
                }
                if (stage.Baby() != oldStage.Baby())
                {
                    CrossBabyLine(pawn);
                }
                else if (oldStage.Child() && stage.Adult() && pawn.story.Childhood?.IsPlayerColonyChildBackstory == true)
                {
                    RerollChildhood(pawn);
                }
            }
            // Gaining or losing the adult backstory (and growing up) changes skills and the work they can do.
            AfterStoryChanged(pawn);
            GrowUp(pawn, oldYears, years);
            age.TrySimulateGrowthPoints();
            Dirty(pawn);
        }

        /// <summary>The game only does this during play: babies have no traits, gear or beliefs, and start life as newborns.</summary>
        private static void CrossBabyLine(Pawn pawn)
        {
            if (pawn.DevelopmentalStage.Baby())
            {
                foreach (Trait t in pawn.story.traits.allTraits.Where(t => t.sourceGene == null).ToList())
                {
                    RemoveTraitOnly(pawn, t);
                }
                pawn.equipment.DestroyAllEquipment();
                pawn.inventory.innerContainer.ClearAndDestroyContents();
                ClearIdeo(pawn);
                CreatorState.Get().Notify_ColonistRemoved(pawn);
            }
            else
            {
                JoinColonyIdeo(pawn);
            }
            RerollChildhood(pawn);
            if (StartingIndex(pawn) >= 0)
            {
                StartingPawnUtility.GeneratePossessions(pawn);
            }
        }

        private static void RerollChildhood(Pawn pawn)
        {
            if (BackstoryFiltersMethod?.Invoke(null, new object[] { pawn, Faction.OfPlayer.def }) is List<BackstoryCategoryFilter> filters)
            {
                PawnBioAndNameGenerator.FillBackstorySlotShuffled(pawn, BackstorySlot.Childhood, filters, Faction.OfPlayer.def);
            }
        }

        /// <summary>What the game gives someone on a growth birthday when there's no letter to answer: a trait and perhaps passions.</summary>
        private static void GrowUp(Pawn pawn, int fromYears, int toYears)
        {
            if (!ModsConfig.BiotechActive)
            {
                return;
            }
            for (int year = fromYears + 1; year <= toYears; year++)
            {
                if (!GrowthUtility.IsGrowthBirthday(year))
                {
                    continue;
                }
                Trait trait = PawnGenerator.GenerateTraitsFor(pawn, 1, null, growthMomentTrait: true).FirstOrFallback();
                if (trait != null)
                {
                    pawn.story.traits.GainTrait(trait);
                }
                for (int i = Rand.RangeInclusive(0, 3); i > 0; i--)
                {
                    SkillDef skill = ChoiceLetter_GrowthMoment.PassionOptions(pawn, 1, checkGenes: true).FirstOrDefault();
                    if (skill != null)
                    {
                        SkillRecord record = pawn.skills.GetSkill(skill);
                        record.passion = record.passion.IncrementPassion();
                    }
                }
            }
        }

        /// <summary>The youngest the scenario allows, by the race's own life stages, so a mod's changes to them count.</summary>
        public static int MinAgeYears(Pawn pawn)
        {
            if (!pawn.RaceProps.Humanlike)
            {
                return 0;
            }
            if (!ModsConfig.BiotechActive)
            {
                return 14;
            }
            DevelopmentalStage allowed = ScenarioUtility.AllowsChildSelection(Find.Scenario) ? Find.GameInitData.allowedDevelopmentalStages : DevelopmentalStage.Adult;
            LifeStageAge youngest = pawn.RaceProps.lifeStageAges.FirstOrDefault(s => allowed.Has(s.def.developmentalStage));
            int years = youngest != null ? Mathf.CeilToInt(youngest.minAge) : 0;
            return Mathf.Max(years, Find.Scenario.AllParts.OfType<ScenPart_PawnFilter_Age>().Select(f => f.allowedAgeRange.min).DefaultIfEmpty().Max());
        }

        /// <summary>Years since birth, counting time in cryptosleep: never less than their biological age.</summary>
        public static void SetChronologicalAge(Pawn pawn, int years)
        {
            Pawn_AgeTracker age = pawn.ageTracker;
            age.AgeChronologicalTicks = age.AgeBiologicalTicks + Math.Max(0L, years - age.AgeBiologicalYears) * GenDate.TicksPerYear;
        }

        /// <summary>The oldest the game itself makes this race (100 for humans); chronological age has no limit.</summary>
        public static int MaxAgeYears(Pawn pawn)
        {
            SimpleCurve curve = pawn.RaceProps.ageGenerationCurve;
            return curve != null ? Mathf.RoundToInt(curve.Points.Last().x) : Mathf.Max(1, Mathf.RoundToInt(pawn.RaceProps.lifeExpectancy));
        }

        // ---------------------------------------------------------------- backstories, traits, skills

        public static IEnumerable<BackstoryDef> BackstoryOptions(Pawn pawn, BackstorySlot slot)
        {
            return DefDatabase<BackstoryDef>.AllDefs
                .Where(b => b.slot == slot && b.shuffleable)
                .OrderBy(b => b.TitleCapFor(pawn.gender).ToString());
        }

        public static void SetBackstory(Pawn pawn, BackstorySlot slot, BackstoryDef backstory)
        {
            SwapBackstory(pawn, slot, backstory);
            AfterStoryChanged(pawn);
        }

        // Traits the player picked themselves. A backstory's traits go with it when it's swapped, these stay. Kept by
        // trait, weakly, like manualSkillPawns below; nothing goes in saves.
        private static readonly ConditionalWeakTable<Trait, object> manualTraits = new ConditionalWeakTable<Trait, object>();

        public static bool IsManualTrait(Trait trait) => manualTraits.TryGetValue(trait, out _);

        public static void SetManualTrait(Trait trait, bool manual)
        {
            if (manual)
            {
                manualTraits.AddOrUpdate(trait, null);
            }
            else
            {
                manualTraits.Remove(trait);
            }
        }

        private static void SwapBackstory(Pawn pawn, BackstorySlot slot, BackstoryDef backstory)
        {
            BackstoryDef old = pawn.story.GetBackstory(slot);
            if (slot == BackstorySlot.Childhood)
            {
                pawn.story.Childhood = backstory;
            }
            else
            {
                pawn.story.Adulthood = backstory;
            }
            if (old?.forcedTraits != null)
            {
                BackstoryDef other = pawn.story.GetBackstory(slot == BackstorySlot.Childhood ? BackstorySlot.Adulthood : BackstorySlot.Childhood);
                foreach (Trait t in pawn.story.traits.allTraits.Where(t => t.sourceGene == null && !IsManualTrait(t)
                    && old.forcedTraits.Any(bt => bt.def == t.def && bt.degree == t.Degree)
                    && other?.forcedTraits?.Any(bt => bt.def == t.def && bt.degree == t.Degree) != true).ToList())
                {
                    RemoveTraitOnly(pawn, t);
                }
            }
            if (backstory != null)
            {
                if (backstory.disallowedTraits != null)
                {
                    foreach (BackstoryTrait bt in backstory.disallowedTraits)
                    {
                        Trait existing = pawn.story.traits.allTraits.FirstOrDefault(t => t.def == bt.def && t.Degree == bt.degree && t.sourceGene == null);
                        if (existing != null)
                        {
                            pawn.story.traits.RemoveTrait(existing);
                        }
                    }
                }
                if (backstory.forcedTraits != null)
                {
                    foreach (BackstoryTrait bt in backstory.forcedTraits)
                    {
                        if (!pawn.story.traits.HasTrait(bt.def))
                        {
                            Trait forced = new Trait(bt.def, bt.degree, forced: true);
                            if (!pawn.story.traits.allTraits.Any(t => t.def.ConflictsWith(forced)))
                            {
                                pawn.story.traits.GainTrait(forced);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Why this trait can't be added right now, or null if it can.</summary>
        public static string TraitBlockReason(Pawn pawn, TraitDef def, int degree)
        {
            foreach (Trait t in pawn.story.traits.allTraits)
            {
                if (t.def == def)
                {
                    return "CAC_AlreadyHasTrait".Translate();
                }
                if (t.def.ConflictsWith(def) || def.ConflictsWith(t))
                {
                    return "CAC_ConflictsWith".Translate(t.LabelCap);
                }
            }
            foreach (BackstoryDef b in pawn.story.AllBackstories)
            {
                if (b?.disallowedTraits != null && b.disallowedTraits.Any(bt => bt.def == def && bt.degree == degree))
                {
                    return "CAC_BackstoryForbids".Translate(b.TitleCapFor(pawn.gender));
                }
            }
            return null;
        }

        /// <summary>
        /// Takes a trait off whatever its state. The game's own RemoveTrait skips greyed-out traits (they count as
        /// "not had") and takes a gene's trait off by removing the whole gene.
        /// </summary>
        public static void RemoveTraitOnly(Pawn pawn, Trait trait)
        {
            trait.sourceGene = null;
            trait.suppressedByTrait = false;
            trait.suppressedByGene = null;
            pawn.story.traits.RemoveTrait(trait);
        }

        /// <summary>Orientation traits share this tag, in mods too. The game only gives them from the adult stage on.</summary>
        public static bool IsOrientation(TraitDef def) => def.exclusionTags?.Contains("SexualOrientation") == true;

        public static void AddTrait(Pawn pawn, TraitDef def, int degree)
        {
            Trait trait = new Trait(def, degree);
            manualTraits.AddOrUpdate(trait, null);
            pawn.story.traits.GainTrait(trait);
            AfterStoryChanged(pawn);
        }

        public static void RemoveTrait(Pawn pawn, Trait trait)
        {
            pawn.story.traits.RemoveTrait(trait);
            AfterStoryChanged(pawn);
        }

        private static void AfterStoryChanged(Pawn pawn)
        {
            RecalculateSkills(pawn);
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.skills.Notify_SkillDisablesChanged();
            foreach (SkillRecord record in pawn.skills.skills)
            {
                if (record.TotallyDisabled)
                {
                    record.passion = Passion.None;
                }
            }
        }

        // Colonists whose skill levels were typed in by hand. Backstory and trait changes leave those numbers alone
        // until the player turns it off or recalculates. Kept by colonist rather than ID (IDs start over in every new
        // game) and weakly, so it goes away with the colonist; nothing goes in saves.
        private static readonly ConditionalWeakTable<Pawn, object> manualSkillPawns = new ConditionalWeakTable<Pawn, object>();

        public static bool HasManualSkills(Pawn pawn)
        {
            return pawn != null && manualSkillPawns.TryGetValue(pawn, out _);
        }

        public static void SetManualSkills(Pawn pawn, bool manual)
        {
            if (pawn == null)
            {
                return;
            }
            if (manual)
            {
                manualSkillPawns.AddOrUpdate(pawn, null);
            }
            else
            {
                manualSkillPawns.Remove(pawn);
            }
        }

        /// <summary>
        /// Sets the level the player sees. Genes and implants add an aptitude on top of the stored level whenever
        /// it's read, so the stored level is the typed number minus that, and the aptitude is the lowest it can show.
        /// </summary>
        public static void SetSkillLevel(Pawn pawn, SkillRecord record, int shown)
        {
            // A bonus is the lowest the level can show; a penalty lowers the highest.
            int aptitude = record.Aptitude;
            int target = Mathf.Clamp(shown, Mathf.Clamp(aptitude, SkillRecord.MinLevel, SkillRecord.MaxLevel),
                Mathf.Clamp(SkillRecord.MaxLevel + aptitude, SkillRecord.MinLevel, SkillRecord.MaxLevel));
            if (target == record.Level)
            {
                return;
            }
            record.levelInt = Mathf.Clamp(target - aptitude, SkillRecord.MinLevel, SkillRecord.MaxLevel);
            record.xpSinceLastLevel = record.XpRequiredForLevelUp * 0.5f;
            SetManualSkills(pawn, true);
        }

        /// <summary>
        /// Skill levels come from backstories and traits, using the game's own formula. The random part is
        /// seeded per pawn, so switching a backstory back and forth always gives the same numbers. Skipped for
        /// colonists with hand-set levels unless force is set (the recalculate button).
        /// </summary>
        public static void RecalculateSkills(Pawn pawn, bool force = false)
        {
            if (pawn.skills == null || FinalLevelOfSkillMethod == null)
            {
                return;
            }
            if (HasManualSkills(pawn))
            {
                if (!force)
                {
                    return;
                }
                SetManualSkills(pawn, false);
            }
            PawnGenerationRequest request = RequestFor(pawn);
            foreach (SkillRecord record in pawn.skills.skills)
            {
                Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, record.def.shortHash));
                try
                {
                    record.Level = (int)FinalLevelOfSkillMethod.Invoke(null, new object[] { pawn, record.def, request });
                }
                finally
                {
                    Rand.PopState();
                }
                record.xpSinceLastLevel = record.XpRequiredForLevelUp * 0.5f;
            }
        }

        /// <summary>None, interested, burning and round again; lower goes the other way (none to burning in one step).</summary>
        public static void CyclePassion(SkillRecord record, bool lower = false)
        {
            if (record.TotallyDisabled)
            {
                return;
            }
            record.passion = lower
                ? record.passion switch
                {
                    Passion.Major => Passion.Minor,
                    Passion.Minor => Passion.None,
                    _ => Passion.Major
                }
                : record.passion switch
                {
                    Passion.None => Passion.Minor,
                    Passion.Minor => Passion.Major,
                    _ => Passion.None
                };
        }

        // ---------------------------------------------------------------- appearance

        public static bool HeadAllowed(Pawn pawn, HeadTypeDef head)
        {
            if (head == null)
            {
                return false;
            }
            if (head.gender != Gender.None && head.gender != pawn.gender)
            {
                return false;
            }
            if (!head.requiredGenes.NullOrEmpty())
            {
                return pawn.genes != null && head.requiredGenes.All(g => pawn.genes.HasActiveGene(g));
            }
            return head.randomChosen;
        }

        public static IEnumerable<HeadTypeDef> AllowedHeads(Pawn pawn)
        {
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                List<HeadTypeDef> forced = pawn.genes.GenesListForReading
                    .Where(g => g.Active && !g.def.forcedHeadTypes.NullOrEmpty())
                    .SelectMany(g => g.def.forcedHeadTypes)
                    .Where(h => h.gender == Gender.None || h.gender == pawn.gender)
                    .Distinct().ToList();
                if (forced.Any())
                {
                    return forced;
                }
            }
            return DefDatabase<HeadTypeDef>.AllDefs.Where(h => HeadAllowed(pawn, h));
        }

        public static IEnumerable<BodyTypeDef> AllowedBodyTypes(Pawn pawn)
        {
            if (pawn.DevelopmentalStage.Baby())
            {
                return new[] { BodyTypeDefOf.Baby };
            }
            if (pawn.DevelopmentalStage.Child())
            {
                return new[] { BodyTypeDefOf.Child };
            }
            return DefDatabase<BodyTypeDef>.AllDefs.Where(b => b != BodyTypeDefOf.Baby && b != BodyTypeDefOf.Child);
        }

        public static void SetBodyType(Pawn pawn, BodyTypeDef body)
        {
            pawn.story.bodyType = body;
            DropUnwearableApparel(pawn);
            Dirty(pawn);
        }

        public static void SetHead(Pawn pawn, HeadTypeDef head)
        {
            pawn.story.headType = head;
            Dirty(pawn);
        }

        public static void SetHair(Pawn pawn, HairDef hair)
        {
            pawn.story.hairDef = hair;
            Dirty(pawn);
        }

        public static void SetHairColor(Pawn pawn, Color color)
        {
            pawn.story.HairColor = color;
            Dirty(pawn);
        }

        public static void SetSkinColor(Pawn pawn, Color? color)
        {
            pawn.story.skinColorOverride = color;
            Dirty(pawn);
        }

        private static List<GeneDef> eyeGenes;

        /// <summary>
        /// Eye colour in RimWorld comes from genes (Biotech's red and gray eyes, plus any that mods add).
        /// An eye gene is one tagged as an eye colour or one that draws eyes.
        /// </summary>
        public static List<GeneDef> EyeGenes
        {
            get
            {
                if (eyeGenes == null)
                {
                    // Some mods generate copies of every gene (android-only versions, astrogene versions) with the
                    // same name; show each eye colour once, preferring the official/original one.
                    eyeGenes = !ModsConfig.BiotechActive ? new List<GeneDef>() : DefDatabase<GeneDef>.AllDefs
                        .Where(g => IsEyeGene(g) && !g.GetType().Name.Contains("Android"))
                        .OrderByDescending(g => g.modContentPack?.IsOfficialMod ?? false)
                        .ThenBy(g => g.defName.Length)
                        .GroupBy(g => g.label ?? g.defName)
                        .Select(group => group.First())
                        .OrderByDescending(g => g.modContentPack?.IsOfficialMod ?? false)
                        .ThenBy(g => g.label)
                        .ToList();
                }
                return eyeGenes;
            }
        }

        /// <summary>
        /// Eye-colour genes: tagged as an eye colour (like Biotech's), or cosmetic genes that draw eyes.
        /// Eye mutations and defects with gameplay effects (filed outside Cosmetic) are left out.
        /// </summary>
        public static bool IsEyeGene(GeneDef gene)
        {
            if (gene.displayCategory == null || gene.displayCategory.defName != "Cosmetic")
            {
                return false;
            }
            return (gene.exclusionTags != null && gene.exclusionTags.Contains("EyeColor"))
                || (gene.renderNodeProperties != null && gene.renderNodeProperties.Any(p => p is PawnRenderNodeProperties_Eye));
        }

        public static GeneDef CurrentEyeGene(Pawn pawn)
        {
            return pawn.genes?.GenesListForReading.FirstOrDefault(g => g.Active && IsEyeGene(g.def))?.def;
        }

        /// <summary>Swaps the pawn's eye gene. Null removes it, leaving the normal eyes.</summary>
        public static void SetEyeGene(Pawn pawn, GeneDef gene)
        {
            if (pawn.genes == null)
            {
                return;
            }
            foreach (Gene g in pawn.genes.GenesListForReading.Where(g => IsEyeGene(g.def)).ToList())
            {
                RemoveGene(pawn, g);
            }
            if (gene != null)
            {
                pawn.genes.AddGene(gene, xenogene: false);
            }
            AfterStoryChanged(pawn);
            Dirty(pawn);
        }

        public static void SetBeard(Pawn pawn, BeardDef beard)
        {
            if (pawn.style != null)
            {
                pawn.style.beardDef = beard;
                Dirty(pawn);
            }
        }

        public static void SetTattoo(Pawn pawn, TattooDef tattoo)
        {
            if (pawn.style == null || !ModsConfig.IdeologyActive)
            {
                return;
            }
            if (tattoo.tattooType == TattooType.Face)
            {
                pawn.style.FaceTattoo = tattoo;
            }
            else
            {
                pawn.style.BodyTattoo = tattoo;
            }
            Dirty(pawn);
        }

        // ---------------------------------------------------------------- genes

        /// <summary>Gives the pawn exactly these genes, keeping the ones they already have.</summary>
        public static void SetExactGenes(Pawn pawn, List<GeneDef> endogenes, List<GeneDef> xenogenes)
        {
            if (pawn.genes == null)
            {
                return;
            }
            SyncGenes(pawn, pawn.genes.Endogenes, endogenes, xenogene: false);
            SyncGenes(pawn, pawn.genes.Xenogenes, xenogenes, xenogene: true);
            AfterGenesChanged(pawn);
        }

        private static void SyncGenes(Pawn pawn, List<Gene> current, List<GeneDef> wanted, bool xenogene)
        {
            List<GeneDef> missing = wanted.ToList();
            foreach (Gene gene in current.ToList())
            {
                if (!missing.Remove(gene.def))
                {
                    RemoveGene(pawn, gene);
                }
            }
            foreach (GeneDef def in missing)
            {
                pawn.genes.AddGene(def, xenogene);
            }
        }

        /// <summary>The gene behind a colonist's natural skin colour.</summary>
        public static bool IsSkinGene(GeneDef gene)
        {
            return gene.endogeneCategory == EndogeneCategory.Melanin || gene.skinColorBase.HasValue;
        }

        /// <summary>Swaps a natural-looks gene (skin colour, or hair colour) for another of the same kind.</summary>
        public static void SetNaturalGene(Pawn pawn, GeneDef gene, bool skin)
        {
            if (pawn.genes == null || gene == null || pawn.genes.Endogenes.Any(g => g.def == gene))
            {
                return;
            }
            foreach (Gene g in pawn.genes.Endogenes.ToList())
            {
                if (skin ? IsSkinGene(g.def) : g.def.endogeneCategory == EndogeneCategory.HairColor)
                {
                    RemoveGene(pawn, g);
                }
            }
            pawn.genes.AddGene(gene, xenogene: false);
        }

        private static void ClearInheritedGenes(Pawn pawn)
        {
            // Keep the natural looks (skin melanin, hair colour); everything else came from the old xenotype.
            foreach (Gene gene in pawn.genes.Endogenes.ToList())
            {
                EndogeneCategory cat = gene.def.endogeneCategory;
                if (cat == EndogeneCategory.HairColor || IsSkinGene(gene.def))
                {
                    continue;
                }
                RemoveGene(pawn, gene);
            }
            WithoutLeftovers(pawn, () => pawn.genes.ClearXenogenes());
        }

        private static void RemoveGene(Pawn pawn, Gene gene)
        {
            WithoutLeftovers(pawn, () => pawn.genes.RemoveGene(gene));
        }

        /// <summary>
        /// Some genes add a hidden condition and don't take it away when they're removed (Alpha Genes' metal-eating
        /// gene and its "mineral fueled" condition). The game drops it on the first tick, but it errors first, so
        /// anything a gene change left with nothing to do is removed here instead.
        /// </summary>
        private static void WithoutLeftovers(Pawn pawn, Action change)
        {
            List<Hediff> before = pawn.health.hediffSet.hediffs.Where(h => !ShouldRemove(h)).ToList();
            change();
            foreach (Hediff hediff in before)
            {
                if (pawn.health.hediffSet.hediffs.Contains(hediff) && ShouldRemove(hediff))
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        private static bool ShouldRemove(Hediff hediff)
        {
            try
            {
                return hediff.ShouldRemove;
            }
            catch
            {
                return false;
            }
        }

        public static void SetXenotype(Pawn pawn, XenotypeDef xenotype)
        {
            if (pawn.genes == null)
            {
                return;
            }
            ClearInheritedGenes(pawn);
            pawn.genes.SetXenotype(xenotype);
            int index = StartingIndex(pawn);
            if (index >= 0)
            {
                PawnGenerationRequest request = StartingPawnUtility.GetGenerationRequest(index);
                request.ForcedXenotype = xenotype;
                request.ForcedCustomXenotype = null;
                StartingPawnUtility.SetGenerationRequest(index, request);
            }
            AfterGenesChanged(pawn);
        }

        public static void SetCustomXenotype(Pawn pawn, CustomXenotype custom)
        {
            if (pawn.genes == null)
            {
                return;
            }
            ClearInheritedGenes(pawn);
            pawn.genes.SetXenotype(XenotypeDefOf.Baseliner);
            pawn.genes.xenotypeName = custom.name;
            pawn.genes.iconDef = custom.IconDef;
            foreach (GeneDef gene in custom.genes)
            {
                if (!pawn.genes.HasActiveGene(gene))
                {
                    pawn.genes.AddGene(gene, !custom.inheritable);
                }
            }
            int index = StartingIndex(pawn);
            if (index >= 0)
            {
                PawnGenerationRequest request = StartingPawnUtility.GetGenerationRequest(index);
                request.ForcedXenotype = null;
                request.ForcedCustomXenotype = custom;
                StartingPawnUtility.SetGenerationRequest(index, request);
            }
            AfterGenesChanged(pawn);
        }

        private static void AfterGenesChanged(Pawn pawn)
        {
            // Genes can force body shape, head shape and hair; respect them so the colonist renders correctly.
            List<Gene> genes = pawn.genes.GenesListForReading;
            if (pawn.DevelopmentalStage.Adult() && genes.Any(g => g.Active && g.def.bodyType.HasValue))
            {
                pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
            }
            if (!AllowedHeads(pawn).Contains(pawn.story.headType))
            {
                HeadTypeDef head = AllowedHeads(pawn).RandomElementWithFallback();
                if (head != null)
                {
                    pawn.story.headType = head;
                }
            }
            Gene forcedHair = genes.FirstOrDefault(g => g.Active && g.def.forcedHair != null);
            if (forcedHair != null)
            {
                pawn.story.hairDef = forcedHair.def.forcedHair;
            }
            foreach (Gene gene in genes)
            {
                if (gene.Active && !gene.def.forcedTraits.NullOrEmpty())
                {
                    foreach (GeneticTraitData data in gene.def.forcedTraits)
                    {
                        if (!pawn.story.traits.HasTrait(data.def))
                        {
                            pawn.story.traits.GainTrait(new Trait(data.def, data.degree) { sourceGene = gene }, suppressConflicts: true);
                        }
                    }
                }
            }
            AfterStoryChanged(pawn);
            Dirty(pawn);
        }

        // ---------------------------------------------------------------- apparel, equipment, inventory

        public static bool CanWear(Pawn pawn, ThingDef def)
        {
            return def.IsApparel && def.apparel.PawnCanWear(pawn, ignoreGender: true) && ApparelUtility.HasPartsToWear(pawn, def);
        }

        public static void DropUnwearableApparel(Pawn pawn)
        {
            if (pawn.apparel == null)
            {
                return;
            }
            foreach (Apparel ap in pawn.apparel.WornApparel.ToList())
            {
                if (!CanWear(pawn, ap.def))
                {
                    pawn.apparel.Remove(ap);
                    ap.Destroy();
                }
            }
        }

        /// <summary>
        /// The game only sets its start date when the colony lands. Making things before that (a book writes its date
        /// into its description) trips mods that read the date, like Research Reinvented, so lend them the date the
        /// colony will start on, the same one the game sets later.
        /// </summary>
        public static T AtGameStartDate<T>(Func<T> make)
        {
            TickManager ticks = Find.TickManager;
            if (ticks == null || ticks.gameStartAbsTick != 0 || Current.ProgramState == ProgramState.Playing)
            {
                return make();
            }
            ticks.gameStartAbsTick = GenTicks.ConfiguredTicksAbsAtGameStart;
            try
            {
                return make();
            }
            finally
            {
                ticks.gameStartAbsTick = 0;
            }
        }

        public static Thing MakeThing(ThingDef def, ThingDef stuff, QualityCategory quality, Color? color = null)
        {
            if (def.MadeFromStuff && stuff == null)
            {
                stuff = GenStuff.DefaultStuffFor(def);
            }
            Thing thing = AtGameStartDate(() =>
            {
                Thing made = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
                made.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
                return made;
            });
            if (color.HasValue && thing.TryGetComp<CompColorable>() != null)
            {
                thing.SetColor(color.Value, reportFailure: false);
            }
            return thing;
        }

        public static void Wear(Pawn pawn, ThingDef def, ThingDef stuff = null, QualityCategory quality = QualityCategory.Normal, Color? color = null)
        {
            Apparel apparel = (Apparel)MakeThing(def, stuff, quality, color);
            foreach (Apparel worn in pawn.apparel.WornApparel.ToList())
            {
                if (!ApparelUtility.CanWearTogether(def, worn.def, pawn.RaceProps.body))
                {
                    pawn.apparel.Remove(worn);
                    worn.Destroy();
                }
            }
            pawn.apparel.Wear(apparel, dropReplacedApparel: false);
            Dirty(pawn);
        }

        public static void RemoveApparel(Pawn pawn, Apparel apparel)
        {
            pawn.apparel.Remove(apparel);
            apparel.Destroy();
            Dirty(pawn);
        }

        /// <summary>Remakes a worn item with a different material, quality or colour (keeps its slot).</summary>
        /// <summary>
        /// The same piece in another material or quality. It's made fresh, so it takes the new material's own colour
        /// (any dye on the old piece doesn't carry over), just as the game colours clothing.
        /// </summary>
        public static void ReplaceApparel(Pawn pawn, Apparel old, ThingDef stuff, QualityCategory quality)
        {
            if (old.Destroyed || !pawn.apparel.WornApparel.Contains(old))
            {
                return;
            }
            ThingDef def = old.def;
            pawn.apparel.Remove(old);
            old.Destroy();
            Apparel apparel = (Apparel)MakeThing(def, stuff, quality);
            pawn.apparel.Wear(apparel, dropReplacedApparel: false);
            Dirty(pawn);
        }

        public static QualityCategory QualityOf(Thing thing)
        {
            return thing.TryGetQuality(out QualityCategory q) ? q : QualityCategory.Normal;
        }

        public static Color? ColorOf(Thing thing)
        {
            CompColorable comp = thing.TryGetComp<CompColorable>();
            return comp != null && comp.Active ? comp.Color : (Color?)null;
        }

        public static void SetWeapon(Pawn pawn, ThingDef def, ThingDef stuff = null, QualityCategory quality = QualityCategory.Normal)
        {
            pawn.equipment.DestroyAllEquipment();
            if (def != null)
            {
                pawn.equipment.AddEquipment((ThingWithComps)MakeThing(def, stuff, quality));
            }
            Dirty(pawn);
        }

        /// <summary>Puts items in the colonist's inventory, never past what they can carry. Returns how many went in.</summary>
        public static int AddInventory(Pawn pawn, ThingDef def, int count, ThingDef stuff = null, QualityCategory quality = QualityCategory.Normal, bool withinCarryLimit = true)
        {
            int added = 0;
            while (count > 0)
            {
                Thing thing = MakeThing(def, stuff, quality);
                int stack = Mathf.Min(count, def.stackLimit);
                if (withinCarryLimit)
                {
                    stack = Mathf.Min(stack, CountThatFits(pawn, thing.GetStatValue(StatDefOf.Mass)));
                }
                if (stack <= 0)
                {
                    thing.Destroy();
                    break;
                }
                thing.stackCount = stack;
                count -= stack;
                added += stack;
                pawn.inventory.innerContainer.TryAdd(thing);
            }
            return added;
        }

        // ---------------------------------------------------------------- relationships

        public static readonly PawnRelationDef[] LoveRelations =
        {
            PawnRelationDefOf.Spouse, PawnRelationDefOf.Fiance, PawnRelationDefOf.Lover,
            PawnRelationDefOf.ExSpouse, PawnRelationDefOf.ExLover
        };

        public static void ClearRelationsBetween(Pawn a, Pawn b)
        {
            foreach (PawnRelationDef def in LoveRelations)
            {
                if (a.relations.DirectRelationExists(def, b))
                {
                    a.relations.RemoveDirectRelation(def, b);
                }
            }
            // The direct links, not GetMother and GetFather: those only see the first parent of each gender.
            if (a.relations.DirectRelationExists(PawnRelationDefOf.Parent, b))
            {
                a.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, b);
            }
            if (b.relations.DirectRelationExists(PawnRelationDefOf.Parent, a))
            {
                b.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, a);
            }
            if (PawnRelationDefOf.Sibling.Worker.InRelation(a, b) || PawnRelationDefOf.HalfSibling.Worker.InRelation(a, b))
            {
                // Siblings and half-siblings are related through shared parents: drop only the parents they share, so
                // a parent of a's that isn't b's (another colonist, say) stays.
                foreach (Pawn parent in ParentsOf(a))
                {
                    if (b.relations.DirectRelationExists(PawnRelationDefOf.Parent, parent))
                    {
                        a.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, parent);
                    }
                }
            }
        }

        public static List<Pawn> ParentsOf(Pawn pawn)
        {
            return pawn.relations.DirectRelations.Where(r => r.def == PawnRelationDefOf.Parent).Select(r => r.otherPawn).ToList();
        }

        public static bool IsGrownUp(Pawn pawn) => pawn.ageTracker.AgeBiologicalYears >= GrownUpAge;

        public static bool OldEnoughToParent(Pawn parent, Pawn child)
        {
            return parent.ageTracker.AgeBiologicalYears - child.ageTracker.AgeBiologicalYears >= GrownUpAge;
        }

        private static void ClearLoveRelations(Pawn pawn)
        {
            foreach (DirectPawnRelation rel in pawn.relations.DirectRelations.Where(r => LoveRelations.Contains(r.def)).ToList())
            {
                pawn.relations.RemoveDirectRelation(rel);
            }
        }

        /// <summary>
        /// After an age change: nobody under 18 is anyone's parent, and within the group a parent stays old enough for
        /// their child. Relatives elsewhere keep the game's looser ages.
        /// </summary>
        private static void ClearImpossibleParents(Pawn pawn)
        {
            List<Pawn> group = Find.GameInitData?.startingAndOptionalPawns;
            bool Impossible(Pawn parent, Pawn child) => group != null && group.Contains(parent) && group.Contains(child) && !OldEnoughToParent(parent, child);
            foreach (Pawn parent in ParentsOf(pawn).Where(p => Impossible(p, pawn)))
            {
                pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, parent);
            }
            foreach (Pawn child in pawn.relations.Children.Where(c => !IsGrownUp(pawn) || Impossible(pawn, c)).ToList())
            {
                child.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, pawn);
            }
        }

        public static void SetLoveRelation(Pawn a, Pawn b, PawnRelationDef def)
        {
            if (!IsGrownUp(a) || !IsGrownUp(b))
            {
                return;
            }
            ClearRelationsBetween(a, b);
            a.relations.AddDirectRelation(def, b);
        }

        public static void SetParent(Pawn child, Pawn parent)
        {
            ClearRelationsBetween(child, parent);
            if (parent.gender == Gender.Female)
            {
                child.SetMother(parent);
            }
            else
            {
                child.SetFather(parent);
            }
        }

        public static void SetSiblings(Pawn a, Pawn b)
        {
            ClearRelationsBetween(a, b);
            List<Pawn> parents = ParentsOf(b);
            if (parents.Count < 2)
            {
                PawnGenerationRequest request = RequestFor(a);
                PawnRelationDefOf.Sibling.Worker.CreateRelation(a, b, ref request);
                return;
            }
            // Two parents already: copied, as the game would invent a third for a same-gender couple.
            foreach (Pawn parent in ParentsOf(a))
            {
                a.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, parent);
            }
            foreach (Pawn parent in parents)
            {
                a.relations.AddDirectRelation(PawnRelationDefOf.Parent, parent);
            }
        }

        public static PawnRelationDef RelationBetween(Pawn me, Pawn other)
        {
            foreach (PawnRelationDef def in LoveRelations)
            {
                if (me.relations.DirectRelationExists(def, other))
                {
                    return def;
                }
            }
            if (me.relations.DirectRelationExists(PawnRelationDefOf.Bond, other))
            {
                return PawnRelationDefOf.Bond;
            }
            if (me.relations.DirectRelationExists(PawnRelationDefOf.Parent, other))
            {
                return PawnRelationDefOf.Parent;
            }
            if (other.relations.DirectRelationExists(PawnRelationDefOf.Parent, me))
            {
                return PawnRelationDefOf.Child;
            }
            if (PawnRelationDefOf.Sibling.Worker.InRelation(me, other))
            {
                return PawnRelationDefOf.Sibling;
            }
            return me.GetMostImportantRelation(other);
        }

        // ---------------------------------------------------------------- pets

        public static void SetBondedMaster(Pawn animal, Pawn master)
        {
            foreach (DirectPawnRelation rel in animal.relations.DirectRelations.ToList())
            {
                if (rel.def == PawnRelationDefOf.Bond)
                {
                    animal.relations.RemoveDirectRelation(rel);
                }
            }
            if (master == null)
            {
                if (animal.playerSettings != null)
                {
                    animal.playerSettings.Master = null;
                }
                return;
            }
            // A master needs obedience first; animals that can't learn it just get the bond.
            if (animal.training != null && animal.training.CanAssignToTrain(TrainableDefOf.Obedience).Accepted)
            {
                animal.training.Train(TrainableDefOf.Obedience, null, complete: true);
                animal.training.SetWantedRecursive(TrainableDefOf.Obedience, checkOn: true);
                if (animal.playerSettings != null)
                {
                    animal.playerSettings.Master = master;
                }
            }
            master.relations.AddDirectRelation(PawnRelationDefOf.Bond, animal);
        }

        /// <summary>
        /// Some animals come in several coats, picked from the animal's ID. Give a fresh animal (not yet known to
        /// anything else) new IDs until it has the saved coat.
        /// </summary>
        public static void MatchCoat(Pawn animal, int coat)
        {
            // -1 is the standard coat, which an animal of a kind with alternates can roll too (1 in 100 for some kinds).
            if (animal.kindDef.alternateGraphics.NullOrEmpty() || coat < -1 || coat >= animal.kindDef.alternateGraphics.Count)
            {
                return;
            }
            for (int tries = 0; tries < 2000 && animal.GetGraphicIndex() != coat; tries++)
            {
                animal.thingIDNumber = -1;
                ThingIDMaker.GiveIDTo(animal);
            }
            Dirty(animal);
        }

        public static Pawn GetBondedMaster(Pawn animal)
        {
            return animal.relations.DirectRelations.FirstOrDefault(r => r.def == PawnRelationDefOf.Bond)?.otherPawn;
        }
    }
}

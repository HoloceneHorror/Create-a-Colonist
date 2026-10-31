using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace CreateAColonist
{
    /// <summary>One thing the Health tab can add: what it is, where it goes, and how to put it on the colonist.</summary>
    public class HealthOption
    {
        public string label;
        public string description;
        public ThingDef icon;
        public string blockedReason;

        // For a reason that's slow to find out: asked when the option is first shown, which most of a long list never is.
        public Func<string> blockedCheck;
        public Func<Pawn, bool> apply;

        // The mod it comes from, for the picker's mod filter. Null for things every body has (scars, missing parts).
        public ModContentPack mod;

        public string BlockedReason
        {
            get
            {
                if (blockedCheck != null)
                {
                    blockedReason = blockedCheck();
                    blockedCheck = null;
                }
                return blockedReason;
            }
        }
    }

    public static partial class PawnEditor
    {
        private static readonly DamageDef[] ScarDamages = { DamageDefOf.Bullet, DamageDefOf.Cut, DamageDefOf.Scratch, DamageDefOf.Bite, DamageDefOf.Stab, DamageDefOf.Burn, DamageDefOf.Frostbite };

        private const float ScarSeverity = 4f;

        /// <summary>
        /// Conditions shown on the Health tab, whole-body ones first, in body order. Like the game's own health tab,
        /// a missing limb shows once (not every bone in it), and parts under a prosthetic aren't listed.
        /// </summary>
        public static List<Hediff> ShownHediffs(Pawn pawn)
        {
            return pawn.health.hediffSet.GetMissingPartsCommonAncestors().Cast<Hediff>()
                .Concat(pawn.health.hediffSet.hediffs.Where(h => h.Visible && !(h is Hediff_MissingPart)))
                .OrderBy(h => h.Part == null ? -1 : h.Part.Index)
                .ThenBy(h => h.LabelCap.ToString())
                .ToList();
        }

        /// <summary>Gene-linked dependencies come back on their own, so they can't be removed here.</summary>
        public static bool CanRemoveHediff(Hediff hediff)
        {
            return !(hediff is Hediff_ChemicalDependency dependency && dependency.LinkedGene != null);
        }

        public static void RemoveHediff(Pawn pawn, Hediff hediff)
        {
            RemoveQuietly(pawn, hediff);
            AfterHealthChanged(pawn);
        }

        public static void RemoveAllHediffs(Pawn pawn)
        {
            foreach (Hediff h in ShownHediffs(pawn).Where(CanRemoveHediff))
            {
                RemoveQuietly(pawn, h);
            }
            AfterHealthChanged(pawn);
        }

        /// <summary>
        /// A missing part or prosthetic comes off by restoring the natural part, which also clears the parts under it
        /// (a bionic arm marks the arm's bones as removed; they must come back with it).
        /// </summary>
        private static void RemoveQuietly(Pawn pawn, Hediff hediff)
        {
            if (!pawn.health.hediffSet.hediffs.Contains(hediff))
            {
                return;
            }
            if (hediff.Part != null && (hediff is Hediff_MissingPart || hediff is Hediff_AddedPart))
            {
                pawn.health.RestorePart(hediff.Part);
            }
            else
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        public static void AfterHealthChanged(Pawn pawn)
        {
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.skills?.Notify_SkillDisablesChanged();
            Dirty(pawn);
        }

        public static string PartLabel(BodyPartRecord part)
        {
            return part == null ? (string)"WholeBody".Translate() : part.LabelCap;
        }

        /// <summary>Would adding this hediff kill the colonist? Used to grey out deadly choices.</summary>
        private static string DeadlyReason(Pawn pawn, Hediff hediff)
        {
            return pawn.health.WouldDieAfterAddingHediff(hediff) ? (string)"CAC_WouldKill".Translate(pawn.LabelShort) : null;
        }

        // ---------------------------------------------------------------- prosthetics and implants

        /// <summary>Every prosthetic, bionic and implant this race can have installed, on every part it fits right now.</summary>
        public static List<HealthOption> InstallOptions(Pawn pawn)
        {
            List<HealthOption> result = new List<HealthOption>();
            IEnumerable<RecipeDef> recipes = pawn.def.AllRecipes
                .Where(r => r.addsHediff != null && (r.Worker is Recipe_InstallArtificialBodyPart || r.Worker is Recipe_InstallImplant))
                .GroupBy(r => r.addsHediff)
                .Select(g => g.First());
            foreach (RecipeDef recipe in recipes)
            {
                List<BodyPartRecord> parts;
                try
                {
                    if (!recipe.AvailableOnNow(pawn))
                    {
                        continue;
                    }
                    parts = recipe.Worker.GetPartsToApplyOn(pawn, recipe).Distinct().ToList();
                }
                catch (Exception e)
                {
                    Log.Warning("[Create a Colonist] Skipping " + recipe.defName + ": " + e.Message);
                    continue;
                }
                HediffDef hediff = recipe.addsHediff;
                foreach (BodyPartRecord part in parts)
                {
                    RecipeDef r = recipe;
                    BodyPartRecord p = part;
                    string description = hediff.Description;
                    if (hediff.addedPartProps != null)
                    {
                        description += "\n\n" + "CAC_PartEfficiency".Translate(hediff.addedPartProps.partEfficiency.ToStringPercent());
                    }
                    result.Add(new HealthOption
                    {
                        label = hediff.LabelCap + ": " + part.LabelCap,
                        description = description,
                        icon = hediff.spawnThingOnRemoved,
                        mod = hediff.modContentPack ?? r.modContentPack,
                        apply = target =>
                        {
                            r.Worker.ApplyOnPawn(target, p, null, new List<Thing>(), null);
                            return true;
                        }
                    });
                }
            }
            return result.OrderBy(o => o.label).ToList();
        }

        // ---------------------------------------------------------------- scars, missing parts

        /// <summary>Old healed injuries, the same kinds the game gives colonists when it generates them.</summary>
        public static List<HealthOption> ScarOptions(Pawn pawn)
        {
            List<HealthOption> result = new List<HealthOption>();
            HashSet<string> seen = new HashSet<string>();
            IEnumerable<BodyPartRecord> parts = pawn.health.hediffSet.GetNotMissingParts()
                .Where(x => x.depth == BodyPartDepth.Outside && x.def.permanentInjuryChanceFactor > 0f && !x.def.conceptual
                    && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(x));
            foreach (BodyPartRecord part in parts)
            {
                foreach (DamageDef damage in ScarDamages)
                {
                    if (damage == null || (damage == DamageDefOf.Frostbite && part.def.frostbiteVulnerability <= 0f))
                    {
                        continue;
                    }
                    HediffDef def = HealthUtility.GetHediffDefFromDamage(damage, pawn, part);
                    if (def == null || !def.HasComp(typeof(HediffComp_GetsPermanent)))
                    {
                        continue;
                    }
                    Hediff preview = MakeScar(pawn, def, part);
                    string label = preview.LabelCap + ": " + part.LabelCap;
                    if (!seen.Add(label))
                    {
                        continue;
                    }
                    HediffDef d = def;
                    BodyPartRecord p = part;
                    result.Add(new HealthOption
                    {
                        label = label,
                        description = "CAC_ScarDesc".Translate(),
                        blockedCheck = () => DeadlyReason(pawn, preview),
                        apply = target =>
                        {
                            target.health.AddHediff(MakeScar(target, d, p), p);
                            return true;
                        }
                    });
                }
            }
            return result.OrderBy(o => o.label).ToList();
        }

        private static Hediff MakeScar(Pawn pawn, HediffDef def, BodyPartRecord part)
        {
            Hediff hediff = HediffMaker.MakeHediff(def, pawn, part);
            hediff.Severity = ScarSeverity;
            HediffComp_GetsPermanent permanent = hediff.TryGetComp<HediffComp_GetsPermanent>();
            if (permanent != null)
            {
                permanent.IsPermanent = true;
            }
            return hediff;
        }

        public static List<HealthOption> MissingPartOptions(Pawn pawn)
        {
            List<HealthOption> result = new List<HealthOption>();
            IEnumerable<BodyPartRecord> parts = pawn.health.hediffSet.GetNotMissingParts()
                .Where(x => x.parent != null && x.depth == BodyPartDepth.Outside && !x.def.conceptual && x.def.canSuggestAmputation
                    && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(x));
            foreach (BodyPartRecord part in parts)
            {
                BodyPartRecord p = part;
                result.Add(new HealthOption
                {
                    label = part.LabelCap,
                    description = "CAC_MissingPartDesc".Translate(part.Label),
                    blockedCheck = () => DeadlyReason(pawn, MakeMissing(pawn, p)),
                    apply = target =>
                    {
                        target.health.AddHediff(MakeMissing(target, p), p);
                        return true;
                    }
                });
            }
            return result;
        }

        private static Hediff_MissingPart MakeMissing(Pawn pawn, BodyPartRecord part)
        {
            Hediff_MissingPart missing = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, part);
            missing.lastInjury = HediffDefOf.Cut;
            missing.IsFresh = false;
            return missing;
        }

        // ---------------------------------------------------------------- age conditions and addictions

        /// <summary>The conditions colonists can pick up on birthdays as they get older: bad back, cataracts and so on.</summary>
        public static List<HealthOption> ConditionOptions(Pawn pawn)
        {
            List<HealthOption> result = new List<HealthOption>();
            IEnumerable<HediffGiver_Birthday> givers = (pawn.RaceProps.hediffGiverSets ?? new List<HediffGiverSetDef>())
                .SelectMany(s => s.hediffGivers ?? new List<HediffGiver>())
                .OfType<HediffGiver_Birthday>()
                .Where(g => g.hediff != null)
                .GroupBy(g => g.hediff)
                .Select(g => g.First());
            foreach (HediffGiver_Birthday giver in givers)
            {
                HediffGiver_Birthday g = giver;
                result.Add(new HealthOption
                {
                    label = giver.hediff.LabelCap,
                    description = giver.hediff.Description,
                    mod = giver.hediff.modContentPack,
                    apply = target => g.TryApply(target)
                });
            }
            return result.OrderBy(o => o.label).ToList();
        }

        public static List<HealthOption> AddictionOptions(Pawn pawn)
        {
            List<HealthOption> result = new List<HealthOption>();
            bool teetotaler = pawn.IsTeetotaler();
            foreach (ChemicalDef chemical in DefDatabase<ChemicalDef>.AllDefs.Where(c => c.canBeAddicted && c.addictionHediff != null))
            {
                ChemicalDef c = chemical;
                result.Add(new HealthOption
                {
                    label = chemical.addictionHediff.LabelCap,
                    description = chemical.addictionHediff.Description,
                    mod = chemical.modContentPack,
                    icon = DrugsFor(chemical).FirstOrDefault(),
                    blockedReason = AddictionUtility.IsAddicted(pawn, chemical) ? (string)"CAC_AlreadyHasThis".Translate()
                        : teetotaler ? (string)"CAC_WontTakeDrugs".Translate(pawn.LabelShort) : null,
                    apply = target =>
                    {
                        AddAddiction(target, c);
                        return true;
                    }
                });
            }
            return result.OrderBy(o => o.label).ToList();
        }

        private static IEnumerable<ThingDef> DrugsFor(ChemicalDef chemical)
        {
            return DefDatabase<ThingDef>.AllDefs.Where(t => t.category == ThingCategory.Item && t.GetCompProperties<CompProperties_Drug>()?.chemical == chemical);
        }

        /// <summary>Same as the game's generated addictions: a strong addiction, plus whatever the drug does to an addict at the start.</summary>
        private static void AddAddiction(Pawn pawn, ChemicalDef chemical)
        {
            Hediff addiction = HediffMaker.MakeHediff(chemical.addictionHediff, pawn);
            addiction.Severity = 0.8f;
            pawn.health.AddHediff(addiction);
            if (chemical.onGeneratedAddictedEvents != null)
            {
                foreach (HediffGiver_Event e in chemical.onGeneratedAddictedEvents)
                {
                    e.EventOccurred(pawn);
                }
            }
            foreach (ThingDef drug in DrugsFor(chemical))
            {
                foreach (IngestionOutcomeDoer doer in drug.ingestible?.outcomeDoers ?? new List<IngestionOutcomeDoer>())
                {
                    if (doer.doToGeneratedPawnIfAddicted)
                    {
                        doer.DoIngestionOutcome(pawn, null, 1);
                    }
                }
            }
        }

        /// <summary>Runs one Health tab choice; tells the player if the game didn't accept it.</summary>
        public static void ApplyHealthOption(Pawn pawn, HealthOption option)
        {
            bool changed;
            try
            {
                changed = option.apply(pawn);
            }
            catch (Exception e)
            {
                Log.Warning("[Create a Colonist] Could not add " + option.label + ": " + e);
                changed = false;
            }
            if (!changed)
            {
                Messages.Message("CAC_CouldNotAdd".Translate(option.label), MessageTypeDefOf.RejectInput, historical: false);
            }
            AfterHealthChanged(pawn);
        }

        // ---------------------------------------------------------------- saving and restoring (gallery)

        /// <summary>The n-th body part with this def, so a saved condition finds the same part on another colonist.</summary>
        public static int PartOrdinal(Pawn pawn, BodyPartRecord part)
        {
            return part == null ? 0 : pawn.RaceProps.body.AllParts.Where(p => p.def == part.def).ToList().IndexOf(part);
        }

        public static BodyPartRecord PartByOrdinal(Pawn pawn, BodyPartDef def, int ordinal)
        {
            return pawn.RaceProps.body.AllParts.Where(p => p.def == def).Skip(ordinal).FirstOrDefault();
        }

        /// <summary>Puts a saved condition back on a colonist. Skips it if the part is gone or it would kill them.</summary>
        public static void RestoreHediff(Pawn pawn, HediffDef def, BodyPartRecord part, float severity, bool permanent, int level, PainCategory? pain = null)
        {
            if (part != null && !pawn.health.hediffSet.GetNotMissingParts().Contains(part))
            {
                return;
            }
            if (typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass))
            {
                if (part != null)
                {
                    pawn.health.RestorePart(part);
                }
                pawn.health.AddHediff(def, part);
                return;
            }
            Hediff hediff = typeof(Hediff_MissingPart).IsAssignableFrom(def.hediffClass) && part != null ? MakeMissing(pawn, part) : HediffMaker.MakeHediff(def, pawn, part);
            if (hediff is Hediff_Level leveled)
            {
                leveled.level = Math.Max(1, level);
                leveled.Severity = leveled.level;
            }
            else if (!(hediff is Hediff_MissingPart))
            {
                hediff.Severity = severity;
            }
            if (permanent && hediff.TryGetComp<HediffComp_GetsPermanent>() is HediffComp_GetsPermanent comp)
            {
                comp.IsPermanent = true;  // picks a random pain category, so the saved one goes after it
                if (pain.HasValue)
                {
                    comp.SetPainCategory(pain.Value);
                }
            }
            if (!pawn.health.WouldDieAfterAddingHediff(hediff))
            {
                pawn.health.AddHediff(hediff, part);
            }
        }
    }
}

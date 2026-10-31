using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CreateAColonist
{
    /// <summary>The family a colonist made from genetics joins.</summary>
    public class Kin
    {
        public readonly List<Pawn> parents = new List<Pawn>();
        public readonly List<Pawn> children = new List<Pawn>();
        public readonly List<Pawn> siblings = new List<Pawn>();

        public bool Any => parents.Count + children.Count + siblings.Count > 0;

        /// <summary>The parents they'll have: the ones chosen, or a sibling's.</summary>
        public List<Pawn> ActualParents => siblings.Count > 0 ? PawnEditor.ParentsOf(siblings[0]) : parents;

        private List<Pawn> Closest => parents.Count > 0 ? parents : siblings.Count > 0 ? siblings : children;

        public string LastName => PawnNamingUtility.GetLastName(Closest.RandomElement());

        /// <summary>
        /// Whose genes they get. Without parents a sibling's or child's, so they match their family. Someone on their
        /// own counts twice: the game's rule for one known parent loses about half the genes at random.
        /// </summary>
        public (Pawn father, Pawn mother) GeneSources()
        {
            Pawn a = Closest[0], b = Closest[Closest.Count > 1 ? 1 : 0];
            return a.gender == Gender.Female ? (b, a) : (a, b);
        }

        public void LinkTo(Pawn pawn)
        {
            if (siblings.Count > 0)
            {
                PawnEditor.SetSiblings(pawn, siblings[0]);
            }
            // Directly, as a real birth does: SetFather would replace a first father with a second.
            foreach (Pawn parent in parents)
            {
                pawn.relations.AddDirectRelation(PawnRelationDefOf.Parent, parent);
            }
            foreach (Pawn child in children)
            {
                PawnEditor.SetParent(child, pawn);
            }
        }
    }

    public static partial class PawnEditor
    {
        private static readonly MethodInfo InheritedXenotypeMethod = AccessTools.Method(typeof(PregnancyUtility), "TryGetInheritedXenotype");
        private static readonly MethodInfo HybridMethod = AccessTools.Method(typeof(PregnancyUtility), "ShouldByHybrid");

        /// <summary>The oldest the scenario allows.</summary>
        public static int MaxStartAge(Pawn pawn)
        {
            return Find.Scenario.AllParts.OfType<ScenPart_PawnFilter_Age>().Select(f => f.allowedAgeRange.max).Append(MaxAgeYears(pawn)).Min();
        }

        public static LifeStageDef LifeStageAt(RaceProperties race, int years)
        {
            return race.lifeStageAges.LastOrDefault(s => s.minAge <= years)?.def ?? race.lifeStageAges[0].def;
        }

        /// <summary>
        /// Someone new with the genes this family would pass on. Inherited the game's way, then made by its generator
        /// as a starting colonist of this age, so other mods' changes to either apply and everything fits the age.
        /// </summary>
        public static Pawn RollRelative(Kin kin, int years, Gender? gender)
        {
            (Pawn father, Pawn mother) = kin.GeneSources();
            // Copied: the game reuses this list. Baby, not Newborn, which ignores the age and leaves them undressed.
            List<GeneDef> genes = PregnancyUtility.GetInheritedGenes(father, mother).ToList();
            DevelopmentalStage stage = LifeStageAt(father.RaceProps, years).developmentalStage;
            float age = years == 0 ? 0f : years + Rand.Range(0f, 0.5f);
            string lastName = kin.LastName;
            // The faction's usual kind, not a parent's: a mechanitor's would give a baby their implant.
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Faction.OfPlayer.def.basicMemberKind ?? PawnKindDefOf.Colonist, Faction.OfPlayer,
                PawnGenerationContext.PlayerStarter, forceGenerateNewPawn: true, allowDowned: stage.Baby(), canGeneratePawnRelations: false, allowAddictions: false,
                fixedBiologicalAge: age, fixedChronologicalAge: age, fixedGender: gender, fixedLastName: lastName.NullOrEmpty() ? null : lastName,
                forcedEndogenes: genes, forcedXenotype: XenotypeDefOf.Baseliner, developmentalStages: stage));

            // From here as a real birth does it.
            object[] args = { mother, father, null };
            if (InheritedXenotypeMethod != null && (bool)InheritedXenotypeMethod.Invoke(null, args))
            {
                pawn.genes.SetXenotypeDirect((XenotypeDef)args[2]);
            }
            else if (HybridMethod != null && (bool)HybridMethod.Invoke(null, new object[] { mother, father }))
            {
                pawn.genes.hybrid = true;
                pawn.genes.xenotypeName = "Hybrid".Translate();
            }
            // After the above, which clears the name, and only for genes that pass on.
            if (GeneUtility.SameHeritableXenotype(mother, father) && mother.genes.UniqueXenotype && mother.genes.Xenogenes.NullOrEmpty())
            {
                pawn.genes.xenotypeName = mother.genes.xenotypeName;
                pawn.genes.iconDef = mother.genes.iconDef;
            }
            // The game calls every baby "Baby Lastname" until the player names them at birth.
            if (stage.Baby())
            {
                pawn.Name = PawnBioAndNameGenerator.GeneratePawnName(pawn, NameStyle.Full, lastName);
            }
            pawn.relations.everSeenByPlayer = true;
            PawnComponentsUtility.AddComponentsForSpawn(pawn);
            return pawn;
        }

        public static void DiscardRelative(Pawn pawn)
        {
            PawnComponentsUtility.RemoveComponentsOnDespawned(pawn);
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }

        /// <summary>A saved colonist brought back as someone living elsewhere, who keeps to the beliefs out there.</summary>
        public static Pawn RelativeFrom(ColonistPreset preset)
        {
            Pawn pawn = FamilyTree.GenerateRelative(new FamilyTree.Person
            {
                first = preset.first,
                nick = preset.nick,
                last = preset.last,
                gender = preset.gender,
                age = preset.age,
                ageChronological = preset.ageChronological,
                xenotype = preset.xenotype
            }, null);
            if (pawn != null)
            {
                preset.ideoName = null;
                preset.possessions = null;
                preset.ApplyTo(pawn, withFamily: false);
            }
            return pawn;
        }
    }
}

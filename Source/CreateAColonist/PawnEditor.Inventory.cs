using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    public static partial class PawnEditor
    {
        // Carry weight always comes from the game's own MassUtility, the same numbers it uses for caravans and for
        // colonists picking things up. Body size, genes and any mod that changes carrying capacity or item mass
        // are included that way, instead of a fixed 35 kg.

        public static float CarryCapacity(Pawn pawn)
        {
            return MassUtility.Capacity(pawn);
        }

        /// <summary>Worn clothes, the weapon and the inventory, as the game counts it.</summary>
        public static float CarriedMass(Pawn pawn)
        {
            return MassUtility.GearAndInventoryMass(pawn);
        }

        public static bool OverCarryLimit(Pawn pawn)
        {
            return MassUtility.IsOverEncumbered(pawn);
        }

        public static float UnitMass(ThingDef def, ThingDef stuff = null)
        {
            return def.GetStatValueAbstract(StatDefOf.Mass, def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null);
        }

        /// <summary>How many more items of this weight fit in the colonist's inventory.</summary>
        public static int CountThatFits(Pawn pawn, float unitMass)
        {
            if (unitMass <= 0.0001f)
            {
                return int.MaxValue;
            }
            return Mathf.FloorToInt(MassUtility.FreeSpace(pawn) / unitMass + 0.0001f);
        }

        /// <summary>
        /// A carried item remade in another material or quality, in the same place in the inventory. Like clothing it's
        /// made fresh, so it takes the new material's colour. A heavier material can push the colonist over the limit;
        /// the weight bar shows that, and at the start the overflow lands next to them.
        /// </summary>
        public static void ReplaceInventoryThing(Pawn pawn, Thing old, ThingDef stuff, QualityCategory quality)
        {
            ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
            int index = inventory.IndexOf(old);
            if (index < 0)
            {
                return;
            }
            Thing made = MakeThing(old.def, stuff, quality);
            made.stackCount = old.stackCount;
            // Adding always goes at the end, so take out what came after it and put it back behind the new one:
            // the row stays where it was instead of jumping to the bottom.
            List<Thing> after = new List<Thing>();
            while (inventory.Count > index + 1)
            {
                after.Add(inventory.Take(inventory[index + 1]));
            }
            inventory.Remove(old);
            old.Destroy();
            inventory.TryAdd(made, canMergeWithExistingStacks: false);
            foreach (Thing thing in after)
            {
                inventory.TryAdd(thing, canMergeWithExistingStacks: false);
            }
        }

        public static void RemoveInventoryThing(Pawn pawn, Thing thing)
        {
            pawn.inventory.innerContainer.Remove(thing);
            thing.Destroy();
        }

        /// <summary>One-of items that come in materials or qualities (weapons, clothing) get the dropdowns, not a count.</summary>
        public static bool HasVariants(Thing thing)
        {
            return thing.def.stackLimit <= 1 && (thing.def.MadeFromStuff || thing.TryGetComp<CompQuality>() != null);
        }

        /// <summary>
        /// The game's starting possessions (from backstory and traits) go into the same inventory as everything else,
        /// as far as they fit. Whatever doesn't fit, and furniture, stays a possession and arrives next to the colonist.
        /// </summary>
        public static void TakePossessionsIntoInventory(Pawn pawn)
        {
            if (!Find.GameInitData.startingPossessions.TryGetValue(pawn, out List<ThingDefCount> possessions) || possessions.Count == 0
                || !MassUtility.CanEverCarryAnything(pawn))
            {
                return;
            }
            foreach (ThingDefCount p in possessions.ToList())
            {
                // Cheap check first: this runs every frame, and making a Thing just to find it doesn't fit is wasteful.
                if (p.ThingDef == null || p.ThingDef.Minifiable || CountThatFits(pawn, UnitMass(p.ThingDef)) < 1)
                {
                    continue;
                }
                Thing thing = AtGameStartDate(() => StartingPawnUtility.GenerateStartingPossession(p));
                int take = Mathf.Min(p.Count, CountThatFits(pawn, thing.GetStatValue(StatDefOf.Mass)));
                if (take <= 0)
                {
                    thing.Destroy();
                    continue;
                }
                thing.stackCount = take;
                pawn.inventory.innerContainer.TryAdd(thing);
                possessions.Remove(p);
                if (take < p.Count)
                {
                    possessions.Add(new ThingDefCount(p.ThingDef, p.Count - take));
                }
            }
        }

        /// <summary>
        /// At game start: if heavier clothes or a weapon pushed a colonist over the limit, take items back out of the
        /// inventory (last added first) until they're within it. The caller sends those along to land next to them.
        /// </summary>
        public static List<Thing> TakeOutOverflow(Pawn pawn)
        {
            List<Thing> removed = new List<Thing>();
            ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
            for (int i = inventory.Count - 1; i >= 0 && OverCarryLimit(pawn); i--)
            {
                Thing thing = inventory[i];
                float unit = thing.GetStatValue(StatDefOf.Mass);
                if (unit <= 0f)
                {
                    continue;
                }
                float over = CarriedMass(pawn) - CarryCapacity(pawn);
                int count = Mathf.Clamp(Mathf.CeilToInt(over / unit - 0.0001f), 1, thing.stackCount);
                removed.Add(inventory.Take(thing, count));
            }
            return removed;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CreateAColonist
{
    /// <summary>One line of the scenario's starting supplies, as the player has edited it.</summary>
    public class SupplyEntry
    {
        public ThingDef def;
        public ThingDef stuff;
        public int count = 1;
        public QualityCategory? quality;
        public bool scattered;

        public string Label => GenLabel.ThingLabel(def, stuff, count).CapitalizeFirst();
    }

    /// <summary>
    /// Creator data that isn't stored on the colonists themselves: pets and colony supplies.
    /// Lives for one new-game setup; it resets if the game regenerates the starting colonists.
    /// </summary>
    public class CreatorState
    {
        private static CreatorState current;

        private static readonly FieldInfo PartsField = AccessTools.Field(typeof(Scenario), "parts");
        private static readonly FieldInfo ThingDefField = AccessTools.Field(typeof(ScenPart_ThingCount), "thingDef");
        private static readonly FieldInfo StuffField = AccessTools.Field(typeof(ScenPart_ThingCount), "stuff");
        private static readonly FieldInfo CountField = AccessTools.Field(typeof(ScenPart_ThingCount), "count");
        private static readonly FieldInfo QualityField = AccessTools.Field(typeof(ScenPart_ThingCount), "quality");

        public readonly Game game;
        public readonly Scenario originalScenario;
        public readonly List<Pawn> pets = new List<Pawn>();
        public readonly List<SupplyEntry> supplies = new List<SupplyEntry>();
        public readonly int maxColonists;

        private readonly HashSet<Pawn> knownColonists = new HashSet<Pawn>();
        private Scenario appliedScenario;

        public static CreatorState Get()
        {
            Game game = Current.Game;
            if (current == null || current.game != game || !current.StillMatchesColonists())
            {
                Scenario original = (current != null && current.game == game && game.Scenario == current.appliedScenario)
                    ? current.originalScenario
                    : game.Scenario;
                current?.DiscardPets();
                current = new CreatorState(game, original);
            }
            return current;
        }

        /// <summary>The setup is over: let go of it, or its colonists and game stay in memory after the colony is left.</summary>
        public static void Forget()
        {
            current = null;
        }

        private CreatorState(Game game, Scenario original)
        {
            this.game = game;
            originalScenario = original;
            if (game.Scenario != original)
            {
                game.Scenario = original;
            }
            maxColonists = UnityEngine.Mathf.Max(6, Find.GameInitData.startingPawnCount);
            RememberColonists();
            supplies.AddRange(ReadSupplies(original));
            foreach (ScenPart part in PartsOf(original))
            {
                if (part is ScenPart_StartingAnimal)
                {
                    // Generate the scenario's animals now so they show up in the bottom strip like colonists.
                    foreach (Thing thing in part.PlayerStartingThings())
                    {
                        if (thing is Pawn animal)
                        {
                            pets.Add(animal);
                        }
                    }
                }
            }
        }

        public static List<ScenPart> PartsOf(Scenario scenario)
        {
            return (List<ScenPart>)PartsField.GetValue(scenario);
        }

        /// <summary>The scenario's "start with" and "scattered near start" items.</summary>
        public static List<SupplyEntry> ReadSupplies(Scenario scenario)
        {
            List<SupplyEntry> result = new List<SupplyEntry>();
            foreach (ScenPart part in PartsOf(scenario))
            {
                if (!(part is ScenPart_StartingThing_Defined) && !(part is ScenPart_ScatterThingsNearPlayerStart))
                {
                    continue;
                }
                ThingDef def = (ThingDef)ThingDefField.GetValue(part);
                if (def == null)
                {
                    continue;
                }
                result.Add(new SupplyEntry
                {
                    def = def,
                    stuff = (ThingDef)StuffField.GetValue(part),
                    count = (int)CountField.GetValue(part),
                    quality = (QualityCategory?)QualityField.GetValue(part),
                    scattered = part is ScenPart_ScatterThingsNearPlayerStart
                });
            }
            return result;
        }

        public void RememberColonists()
        {
            foreach (Pawn p in Find.GameInitData.startingAndOptionalPawns)
            {
                knownColonists.Add(p);
            }
        }

        private bool StillMatchesColonists()
        {
            return Find.GameInitData.startingAndOptionalPawns.Any(p => knownColonists.Contains(p));
        }

        private void DiscardPets()
        {
            foreach (Pawn pet in pets)
            {
                if (!pet.Destroyed && !pet.Spawned)
                {
                    Find.WorldPawns.PassToWorld(pet, PawnDiscardDecideMode.Discard);
                }
            }
            pets.Clear();
        }

        // ---------------------------------------------------------------- pets

        public static IEnumerable<PawnKindDef> AnimalKinds(bool petsOnly)
        {
            return DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.RaceProps != null && k.RaceProps.Animal && !k.RaceProps.Dryad && k.race.race.AnyPawnKind == k)
                .Where(k => !petsOnly || k.RaceProps.petness > 0f)
                .OrderBy(k => k.label);
        }

        public static Pawn GenerateAnimal(PawnKindDef kind, Gender? gender = null)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, fixedGender: gender);
            Pawn animal = PawnGenerator.GeneratePawn(request);
            if (animal.Name == null || animal.Name.Numerical)
            {
                animal.Name = PawnBioAndNameGenerator.GeneratePawnName(animal);
            }
            return animal;
        }

        public Pawn AddPet(PawnKindDef kind, Gender? gender = null)
        {
            Pawn animal = GenerateAnimal(kind, gender);
            pets.Add(animal);
            return animal;
        }

        /// <summary>A pet as saved (in a colony, or as an undo step), built fresh: a coat can only be picked on a new animal.</summary>
        public Pawn AddPet(ColonyPreset.PetEntry entry, Pawn master, int index = -1, int chronological = -1)
        {
            Pawn pet = AddPet(entry.kind, entry.kind.RaceProps.hasGenders ? entry.gender : (Gender?)null);
            if (index >= 0)
            {
                pets.Remove(pet);
                pets.Insert(UnityEngine.Mathf.Clamp(index, 0, pets.Count), pet);
            }
            PawnEditor.MatchCoat(pet, entry.coat);
            if (!entry.name.NullOrEmpty())
            {
                pet.Name = new NameSingle(entry.name);
            }
            PawnEditor.SetBiologicalAge(pet, UnityEngine.Mathf.Max(entry.age, PawnEditor.MinAgeYears(pet)));
            if (chronological >= 0)
            {
                PawnEditor.SetChronologicalAge(pet, chronological);
            }
            if (master != null)
            {
                PawnEditor.SetBondedMaster(pet, master);
            }
            return pet;
        }

        /// <summary>Swaps a pet for a new animal of another species, keeping its name and bond.</summary>
        public Pawn ChangePetSpecies(Pawn pet, PawnKindDef kind)
        {
            int index = pets.IndexOf(pet);
            Pawn master = PawnEditor.GetBondedMaster(pet);
            Gender? gender = kind.RaceProps.hasGenders ? pet.gender : (Gender?)null;
            Pawn animal = GenerateAnimal(kind, gender);
            animal.Name = pet.Name;
            RemovePet(pet);
            pets.Insert(UnityEngine.Mathf.Clamp(index, 0, pets.Count), animal);
            if (master != null)
            {
                PawnEditor.SetBondedMaster(animal, master);
            }
            return animal;
        }

        public void RemovePet(Pawn pet)
        {
            PawnEditor.SetBondedMaster(pet, null);
            pets.Remove(pet);
            Find.WorldPawns.PassToWorld(pet, PawnDiscardDecideMode.Discard);
        }

        public void Notify_ColonistRemoved(Pawn colonist)
        {
            foreach (Pawn pet in pets)
            {
                if (PawnEditor.GetBondedMaster(pet) == colonist || pet.playerSettings?.Master == colonist)
                {
                    PawnEditor.SetBondedMaster(pet, null);
                }
            }
        }

        // ---------------------------------------------------------------- applying to the game

        /// <summary>
        /// Builds a copy of the scenario whose starting items and animals are exactly what the player chose,
        /// and makes it the game's scenario. The original (shared) scenario object is never modified, so this can
        /// run again if the player backs out of the game's start-up warning and presses Play later.
        /// </summary>
        public void ApplyToScenario()
        {
            Scenario copy = originalScenario.CopyForEditing();
            copy.Category = originalScenario.Category;
            copy.fileName = originalScenario.fileName;
            List<ScenPart> parts = PartsOf(copy);
            parts.RemoveAll(p => p is ScenPart_StartingThing_Defined || p is ScenPart_ScatterThingsNearPlayerStart || p is ScenPart_StartingAnimal);

            ScenPartDef definedDef = DefDatabase<ScenPartDef>.GetNamed("StartingThing_Defined");
            ScenPartDef scatterDef = DefDatabase<ScenPartDef>.GetNamed("ScatterThingsNearPlayerStart");
            foreach (SupplyEntry entry in supplies.Where(s => s.def != null && s.count > 0))
            {
                ScenPart part = ScenarioMaker.MakeScenPart(entry.scattered ? scatterDef : definedDef);
                ThingDefField.SetValue(part, entry.def);
                StuffField.SetValue(part, entry.def.MadeFromStuff ? (entry.stuff ?? GenStuff.DefaultStuffFor(entry.def)) : null);
                CountField.SetValue(part, entry.count);
                QualityField.SetValue(part, entry.quality);
                parts.Add(part);
            }

            // Always added: it also settles ideoligions and carry weight once the colony really starts.
            ScenPart_CreatorPets creatorPart = (ScenPart_CreatorPets)ScenarioMaker.MakeScenPart(DefDatabase<ScenPartDef>.GetNamed("CAC_CreatorPets"));
            creatorPart.pets.AddRange(pets);
            parts.Add(creatorPart);

            appliedScenario = copy;
            game.Scenario = copy;
        }
    }

    /// <summary>
    /// The creator's part of the colony start, run only once the game is actually generating the colony:
    /// settles the colony's ideoligions, delivers the pets, and sends any inventory over a colonist's carry limit
    /// (say, after switching to heavier armour) to land next to them. Removes itself afterwards, so saves don't
    /// need this mod.
    /// </summary>
    public class ScenPart_CreatorPets : ScenPart
    {
        public List<Pawn> pets = new List<Pawn>();
        private List<Thing> overflow;

        private static IEnumerable<Pawn> StartingColonists => Find.GameInitData.startingAndOptionalPawns.Take(Find.GameInitData.startingPawnCount);

        public override void PreMapGenerate()
        {
            PawnEditor.SettleIdeos(StartingColonists.ToList());
        }

        public override IEnumerable<Thing> PlayerStartingThings()
        {
            foreach (Pawn pet in pets)
            {
                if (pet.Faction != Faction.OfPlayer)
                {
                    pet.SetFaction(Faction.OfPlayer);
                }
                yield return pet;
            }
            overflow ??= StartingColonists.SelectMany(PawnEditor.TakeOutOverflow).ToList();
            foreach (Thing thing in overflow)
            {
                yield return thing;
            }
        }

        public override void PostGameStart()
        {
            // The scenario is still looping over its parts here, so remove ourselves once that's done.
            Scenario scenario = Find.Scenario;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                CreatorState.PartsOf(scenario).Remove(this);
                CreatorState.Forget();
            });
        }

        public override string Summary(Scenario scen)
        {
            return null;
        }
    }
}

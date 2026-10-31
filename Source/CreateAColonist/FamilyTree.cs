using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CreateAColonist
{
    /// <summary>
    /// The family around some colonists, saved so it can be rebuilt: every relative within a few steps (parents,
    /// grandparents, aunts and uncles, nieces and nephews, children, partners) and every direct relationship between
    /// them. Siblings, cousins and the like aren't stored; the game works them out from the parent links, so they
    /// come back as they were. Relatives outside the colony are stored as name, gender, age and whether they're
    /// alive, and generated again as people elsewhere in the world.
    /// </summary>
    public class FamilyTree : IExposable
    {
        private const int MaxSteps = 3;
        private const int MaxPeople = 40;

        /// <summary>Relatives outside the colonists given to <see cref="From"/>. Node i is colonist i, then these.</summary>
        public List<Person> people = new List<Person>();
        public List<Link> links = new List<Link>();

        public class Person : IExposable
        {
            public string first, nick, last;
            public Gender gender;
            public int age, ageChronological;
            public bool dead;
            public PawnKindDef kind;
            public XenotypeDef xenotype;

            public void ExposeData()
            {
                Scribe_Values.Look(ref first, "first");
                Scribe_Values.Look(ref nick, "nick");
                Scribe_Values.Look(ref last, "last");
                Scribe_Values.Look(ref gender, "gender");
                Scribe_Values.Look(ref age, "age");
                Scribe_Values.Look(ref ageChronological, "ageChronological");
                Scribe_Values.Look(ref dead, "dead");
                Scribe_Defs.Look(ref kind, "kind");
                Scribe_Defs.Look(ref xenotype, "xenotype");
            }
        }

        /// <summary>"to is from's relation" (the way the game stores it, on the from side).</summary>
        public class Link : IExposable
        {
            public int from, to;
            public PawnRelationDef def;

            public void ExposeData()
            {
                Scribe_Values.Look(ref from, "from");
                Scribe_Values.Look(ref to, "to");
                Scribe_Defs.Look(ref def, "def");
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref people, "people", LookMode.Deep);
            Scribe_Collections.Look(ref links, "links", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                people ??= new List<Person>();
                links ??= new List<Link>();
                people.RemoveAll(p => p == null);
                links.RemoveAll(l => l?.def == null || l.from < 0 || l.to < 0 || l.from == l.to);
            }
        }

        private static bool Kept(PawnRelationDef def)
        {
            // Pet bonds are saved with the pets.
            return def != PawnRelationDefOf.Bond;
        }

        /// <summary>Who someone is directly related to: the people in their own relations, and their children.</summary>
        private static IEnumerable<Pawn> Neighbours(Pawn p)
        {
            foreach (DirectPawnRelation r in p.relations.DirectRelations)
            {
                if (Kept(r.def))
                {
                    yield return r.otherPawn;
                }
            }
            foreach (Pawn child in p.relations.Children)
            {
                yield return child;
            }
        }

        /// <param name="colonists">Saved by position: node i is colonists[i].</param>
        /// <param name="stopAt">People not to follow or include (for one colonist: the rest of the group, whose
        /// relationships aren't that colonist's to save).</param>
        public static FamilyTree From(List<Pawn> colonists, ICollection<Pawn> stopAt = null)
        {
            List<Pawn> nodes = colonists.ToList();
            Dictionary<Pawn, int> steps = nodes.ToDictionary(p => p, p => 0);
            Queue<Pawn> queue = new Queue<Pawn>(nodes);
            while (queue.Count > 0)
            {
                Pawn p = queue.Dequeue();
                if (steps[p] >= MaxSteps)
                {
                    continue;
                }
                foreach (Pawn other in Neighbours(p))
                {
                    if (other == null || steps.ContainsKey(other) || !other.RaceProps.Humanlike || (stopAt != null && stopAt.Contains(other)) || nodes.Count >= MaxPeople)
                    {
                        continue;
                    }
                    steps[other] = steps[p] + 1;
                    nodes.Add(other);
                    queue.Enqueue(other);
                }
            }

            FamilyTree tree = new FamilyTree();
            foreach (Pawn p in nodes.Skip(colonists.Count))
            {
                NameTriple nt = p.Name as NameTriple;
                tree.people.Add(new Person
                {
                    first = nt?.First ?? p.Name?.ToStringShort,
                    nick = nt?.Nick,
                    last = nt?.Last,
                    gender = p.gender,
                    age = p.ageTracker.AgeBiologicalYears,
                    ageChronological = p.ageTracker.AgeChronologicalYears,
                    dead = p.Dead,
                    kind = p.kindDef,
                    xenotype = p.genes?.Xenotype
                });
            }
            Dictionary<Pawn, int> index = new Dictionary<Pawn, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                index[nodes[i]] = i;
            }
            foreach (Pawn p in nodes)
            {
                foreach (DirectPawnRelation r in p.relations.DirectRelations)
                {
                    if (Kept(r.def) && r.otherPawn != null && index.TryGetValue(r.otherPawn, out int to))
                    {
                        tree.links.Add(new Link { from = index[p], to = to, def = r.def });
                    }
                }
            }
            return tree;
        }

        /// <summary>
        /// Rebuilds the family on these colonists (by position). Their old family goes first: everything when
        /// <paramref name="keep"/> is null, otherwise everything except relationships with the people in it.
        /// </summary>
        /// <param name="dropped">Saved colonists left off the end of <paramref name="colonists"/>.</param>
        public void ApplyTo(List<Pawn> colonists, ICollection<Pawn> keep = null, int dropped = 0)
        {
            foreach (Pawn p in colonists)
            {
                foreach (DirectPawnRelation r in p.relations.DirectRelations.ToList())
                {
                    if (Kept(r.def) && (keep == null || !keep.Contains(r.otherPawn)))
                    {
                        p.relations.RemoveDirectRelation(r);
                    }
                }
                foreach (Pawn child in p.relations.Children.ToList())
                {
                    if (keep == null || !keep.Contains(child))
                    {
                        child.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, p);
                    }
                }
            }

            List<Pawn> nodes = colonists.ToList();
            // Links count by position, so the dropped keep their places or their links land on relatives.
            nodes.AddRange(new Pawn[dropped]);
            foreach (Person person in people)
            {
                nodes.Add(MakeRelative(person, colonists.FirstOrDefault()));
            }
            foreach (Link l in links)
            {
                if (l.from >= nodes.Count || l.to >= nodes.Count || nodes[l.from] == null || nodes[l.to] == null)
                {
                    continue;
                }
                Pawn a = nodes[l.from];
                Pawn b = nodes[l.to];
                // Files from older versions can hold love relationships for children.
                if (PawnEditor.LoveRelations.Contains(l.def) && (!PawnEditor.IsGrownUp(a) || !PawnEditor.IsGrownUp(b)))
                {
                    continue;
                }
                // Two-way relationships (partners) are stored on both people; add each once.
                if (!a.relations.DirectRelationExists(l.def, b))
                {
                    a.relations.AddDirectRelation(l.def, b);
                }
            }
        }

        private static Pawn MakeRelative(Person person, Pawn likeColonist)
        {
            Pawn pawn = GenerateRelative(person, likeColonist);
            if (pawn != null && !Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            return pawn;
        }

        /// <summary>A relative living elsewhere, made the way the game makes a colonist's generated parents.</summary>
        public static Pawn GenerateRelative(Person person, Pawn likeColonist)
        {
            PawnKindDef kind = person.kind ?? likeColonist?.kindDef ?? PawnKindDefOf.Colonist;
            if (!Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out Faction faction, tryMedievalOrBetter: false, allowDefeated: true))
            {
                faction = Faction.OfAncients;
            }
            int age = System.Math.Max(0, person.age);
            PawnGenerationRequest request = new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDead: true, allowDowned: true, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false, allowPregnant: false, fixedBiologicalAge: age,
                fixedChronologicalAge: System.Math.Max(age, person.ageChronological), fixedGender: person.gender,
                fixedLastName: person.last, forceDead: person.dead,
                forcedXenotype: ModsConfig.BiotechActive ? person.xenotype : null);
            try
            {
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (!person.first.NullOrEmpty())
                {
                    pawn.Name = person.last.NullOrEmpty() && person.nick.NullOrEmpty()
                        ? new NameSingle(person.first)
                        : new NameTriple(person.first, person.nick ?? person.first, person.last ?? "");
                }
                return pawn;
            }
            catch (System.Exception e)
            {
                Log.Warning("[Create a Colonist] Could not bring back a relative (" + person.first + "): " + e.Message);
                return null;
            }
        }
    }
}

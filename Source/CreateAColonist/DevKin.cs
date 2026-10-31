using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

// Developer test tooling. Only compiled into dev builds (build.ps1 -Dev); never part of the published mod.
#if CAC_DEV
namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        private static string KinReport(Pawn p)
        {
            return p.LabelShort + " age " + p.ageTracker.AgeBiologicalYears + " " + p.DevelopmentalStage + " " + p.gender + " " + p.genes?.XenotypeLabel
                + (p.genes?.hybrid == true ? " (hybrid)" : "") + " body=" + p.story.bodyType?.defName + " child=" + p.story.Childhood?.defName + " adult=" + p.story.Adulthood?.defName
                + " traits=" + p.story.traits.allTraits.Select(t => t.def.defName).ToCommaList() + " ideo=" + p.Ideo?.name
                + " wears=" + p.apparel.WornApparel.Select(a => a.def.defName).ToCommaList() + " weapon=" + p.equipment.Primary?.def.defName
                + " beside=" + (Find.GameInitData?.startingPossessions != null && Find.GameInitData.startingPossessions.TryGetValue(p, out List<ThingDefCount> left) ? left.Select(t => t.ThingDef.defName + "x" + t.Count).ToCommaList() : "")
                + " genes=" + p.genes?.Endogenes.Count + " growth=" + p.ageTracker.growthPoints.ToString("0.0");
        }

        private static void KinLog(Pawn p) => Log.Message("[CAC kin] " + KinReport(p));

        public void HeadlessStep() => RunTour();

        private void AddKinScenes(Action<string, Action, Action, bool, Action, int> Add, Pawn a, Pawn b, Pawn c)
        {
            if (!ModsConfig.BiotechActive)
            {
                Add("kin_no_biotech", null, () =>
                {
                    Expect(PawnEditor.MinAgeYears(a) == 14, "without Biotech the youngest age should stay 14: " + PawnEditor.MinAgeYears(a));
                    Expect(!NeedsGrownUp, "the grown-up rule ran without Biotech");
                }, false, null, 3);
                return;
            }
            Pawn kid = null;
            Dialog_Genetics window = null;
            int rolled0 = 0;
            string keptA = PawnFingerprint(a, Colonists), keptB = PawnFingerprint(b, Colonists);

            Dialog_Genetics Open()
            {
                window = new Dialog_Genetics(this);
                Find.WindowStack.Add(window);
                return window;
            }

            // Grown-ups to work with, whatever the earlier scenes did to the colonists.
            void Grown(params Pawn[] pawns)
            {
                foreach (Pawn p in pawns)
                {
                    PawnEditor.SetBiologicalAge(p, 40);
                    PawnEditor.SetChronologicalAge(p, 40);
                }
            }

            XenotypeDef Xeno(string name) => DefDatabase<XenotypeDef>.GetNamed(name);

            Add("kin_labels_exist", null, () => Expect(new[] { "CAC_ShowApparel", "CAC_ShowHeadgear", "CAC_Name" }.All(k => k.CanTranslate()), "a label has no text"), false, null, 3);
            Add("kin_age_setup", () => kid = AddColonist(), () => Expect(kid != null, "no room to add a colonist"), false, null, 3);

            int previous = -1;
            foreach (int years in new[] { 0, 2, 3, 6, 7, 10, 13, 18, 20, 7, 0, 25 })
            {
                int y = years;
                Add("kin_age_" + y, () =>
                {
                    previous = kid.ageTracker.AgeBiologicalYears;
                    Show(kid, CreatorTab.Personality);
                    PawnEditor.SetBiologicalAge(kid, y);
                }, () =>
                {
                    KinLog(kid);
                    Expect(kid.ageTracker.AgeBiologicalYears == y, "age is " + kid.ageTracker.AgeBiologicalYears + " not " + y);
                    Expect(kid.DevelopmentalStage == PawnEditor.LifeStageAt(kid.RaceProps, y).developmentalStage, "wrong stage for " + y + ": " + kid.DevelopmentalStage);
                    bool baby = y < 3;
                    Expect(baby == (kid.story.Childhood?.spawnCategories?.Contains("Newborn") == true), "backstory doesn't fit age " + y + ": " + kid.story.Childhood?.defName);
                    Expect(!baby || kid.story.traits.allTraits.All(t => t.sourceGene != null), "a baby has traits");
                    Expect(!baby || (kid.Ideo == null && kid.equipment.Primary == null && kid.inventory.innerContainer.Count == 0), "a baby has an ideoligion or gear");
                    Expect(!baby || Find.GameInitData.startingPossessions[kid].Any(t => t.ThingDef == ThingDefOf.BabyFood), "a baby has no baby food");
                    Expect(baby || kid.Ideo != null, "lost their ideoligion growing up");
                    Expect(!kid.DevelopmentalStage.Juvenile() || !kid.story.traits.allTraits.Any(t => PawnEditor.IsOrientation(t.def)), "a child has an orientation trait");
                    Expect(y >= previous || kid.story.bodyType == PawnGenerator.GetBodyTypeFor(kid) || kid.DevelopmentalStage.Adult(), "body doesn't fit");
                }, y == 0 || y == 7, null, 3);
                if (y == 0)
                {
                    foreach ((CreatorTab t, int sub) in AllViews)
                    {
                        Add("kin_baby_view_" + t + sub, () => Show(kid, t, sub), null, false, null, 3);
                    }
                }
            }

            // Why the skill scenes once failed at random: a skill the colonist can't use shows 0, whatever is typed or added.
            Add("kin_disabled_skill_shows_zero", () =>
            {
                PawnEditor.SetBiologicalAge(kid, 25);
                PawnEditor.SetBackstory(kid, BackstorySlot.Adulthood, DefDatabase<BackstoryDef>.AllDefs.First(d => d.slot == BackstorySlot.Adulthood && d.shuffleable && (d.workDisables & WorkTags.Violent) != 0));
            }, () =>
            {
                SkillRecord shooting = kid.skills.GetSkill(SkillDefOf.Shooting);
                GeneDef bonus = DefDatabase<GeneDef>.AllDefs.First(g => !g.aptitudes.NullOrEmpty() && g.aptitudes[0].skill == SkillDefOf.Shooting && g.aptitudes[0].level > 0);
                Gene gene = kid.genes.AddGene(bonus, xenogene: true);
                kid.skills.DirtyAptitudes();
                Log.Message("[CAC kin] skill cause: first bonus gene is " + DefDatabase<GeneDef>.AllDefs.First(g => !g.aptitudes.NullOrEmpty() && g.aptitudes[0].level > 0).defName + "; on a colonist who can't use Shooting, " + bonus.defName
                    + " gives aptitude " + shooting.Aptitude + ", level " + shooting.Level + ", disabled " + shooting.TotallyDisabled);
                Expect(shooting.TotallyDisabled && shooting.Level == 0 && shooting.Aptitude > 0, "a disabled skill didn't show 0");
                kid.genes.RemoveGene(gene);
                kid.skills.DirtyAptitudes();
            }, false, null, 3);
            Add("kin_baby_to_clean_up", () => PawnEditor.SetBiologicalAge(kid, 0), null, false, null, 3);
            Add("kin_undo_into_baby_on_ideo_tab", () =>
            {
                PawnEditor.SetBiologicalAge(kid, 25);
                Show(kid, CreatorTab.Ideoligion);
                RecordUndo();
            }, null, false, () => PawnEditor.SetBiologicalAge(kid, 0), 3);
            Add("kin_undo_step", () =>
            {
                RecordUndo();
                StepUndo(redo: false);
            }, () => Log.Message("[CAC kin] after undo: " + KinReport(kid) + " tab=" + tab), false, null, 3);
            Add("kin_redo_into_baby", () => StepUndo(redo: true), () =>
            {
                KinLog(kid);
                Expect(TabsFor(selected).Contains(tab), "left on a tab they don't have: " + tab);
            }, false, null, 3);

            Add("kin_denial_all_children", () =>
            {
                foreach (Pawn p in Colonists)
                {
                    PawnEditor.SetBiologicalAge(p, 5);
                }
            }, () =>
            {
                Expect(NeedsGrownUp, "a group of children is allowed");
                Expect(!CanDoNext(), "Play went ahead with only children");
            }, true, null, 3);
            Add("kin_denial_adult_with_baby", () =>
            {
                Grown(a);
                PawnEditor.SetBiologicalAge(kid, 0);
            }, () => Expect(!NeedsGrownUp, "a group with an adult was refused"), false, null, 3);
            Add("kin_love_gate", () =>
            {
                Grown(a, b);
                PawnEditor.SetLoveRelation(a, b, PawnRelationDefOf.Spouse);
            }, () =>
            {
                Expect(a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b), "two adults couldn't marry");
                PawnEditor.SetBiologicalAge(a, 15);
                Expect(!a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b), "a love relationship stayed on someone under 18");
                PawnEditor.SetLoveRelation(a, b, PawnRelationDefOf.Lover);
                Expect(!a.relations.DirectRelationExists(PawnRelationDefOf.Lover, b), "a love relationship was set on someone under 18");
                List<FloatMenuOption> options = RelationOptions(a, b).ToList();
                Expect(!options.Any(o => PawnEditor.LoveRelations.Any(d => o.Label == d.GetGenderSpecificLabelCap(b))), "the menu offers love to someone under 18");
                Grown(a);
            }, false, null, 3);

            Add("kin_scenario_stage_limits", () =>
            {
                Find.GameInitData.allowedDevelopmentalStages = DevelopmentalStage.Adult;
                Log.Message("[CAC kin] adults only: youngest " + PawnEditor.MinAgeYears(a));
            }, () =>
            {
                Expect(PawnEditor.MinAgeYears(a) == 13, "adults-only scenario still allows " + PawnEditor.MinAgeYears(a));
                Find.GameInitData.allowedDevelopmentalStages = DevelopmentalStage.Child;
                Expect(PawnEditor.MinAgeYears(a) == 3, "children-only scenario gives " + PawnEditor.MinAgeYears(a));
                Expect(!NeedsGrownUp, "the grown-up rule ran on a scenario that forbids adults");
                Find.GameInitData.allowedDevelopmentalStages = DevelopmentalStage.Baby | DevelopmentalStage.Child | DevelopmentalStage.Adult;
                ScenPart_PawnFilter_Age filter = new ScenPart_PawnFilter_Age { def = DefDatabase<ScenPartDef>.GetNamed("PawnFilter_Age"), allowedAgeRange = new IntRange(20, 40) };
                List<ScenPart> parts = CreatorState.PartsOf(Find.Scenario);
                parts.Add(filter);
                Expect(PawnEditor.MinAgeYears(a) == 20 && PawnEditor.MaxStartAge(a) == 40, "age filter gives " + PawnEditor.MinAgeYears(a) + " to " + PawnEditor.MaxStartAge(a));
                parts.Remove(filter);
                Expect(PawnEditor.MinAgeYears(a) == 0, "youngest age is " + PawnEditor.MinAgeYears(a));
            }, false, null, 3);

            Add("kin_window_empty", () =>
            {
                Grown(a, b);
                Open();
            }, () => Expect(window.Rolled == null && window.Problem == null, "something was rolled with no family chosen"), true, null, 3);
            Add("kin_one_parent", () =>
            {
                PawnEditor.SetGender(a, Gender.Female);
                PawnEditor.SetXenotype(a, Xeno("Yttakin"));
                Open().SetRole(a, KinRole.Child);
            }, () =>
            {
                KinLog(window.Rolled);
                Expect(window.Rolled.ageTracker.AgeBiologicalYears == 0 && window.Rolled.DevelopmentalStage.Baby(), "should default to a newborn");
                Expect(window.Rolled.genes.Xenotype == Xeno("Yttakin"), "didn't inherit the xenotype: " + window.Rolled.genes.XenotypeLabel);
                Expect(a.genes.Endogenes.Where(g => g.def.biostatArc <= 0).All(g => g.def.endogeneCategory == EndogeneCategory.Melanin || window.Rolled.genes.Endogenes.Any(x => x.def == g.def)), "one parent should pass on all their genes");
                Expect(window.Rolled.Name is NameTriple n && n.First != "Baby".Translate().CapitalizeFirst().ToString(), "still named Baby: " + window.Rolled.Name);
                Expect(PawnNamingUtility.GetLastName(window.Rolled) == PawnNamingUtility.GetLastName(a), "didn't take the family name");
                Expect(!Colonists.Contains(window.Rolled), "a preview got into the group");
            }, true, null, 3);
            Add("kin_reroll_and_cancel", () =>
            {
                Open().SetRole(a, KinRole.Child);
                rolled0 = Find.GameInitData.startingAndOptionalPawns.Count;
            }, () =>
            {
                Pawn first = window.Rolled;
                window.Roll();
                Expect(window.Rolled != first && window.Rolled != null, "rolling again kept the same pawn");
                window.Close();
                Expect(Find.GameInitData.startingAndOptionalPawns.Count == rolled0, "cancelling changed the group");
            }, false, null, 3);
            Add("kin_couple_hybrid", () =>
            {
                PawnEditor.SetGender(b, Gender.Male);
                PawnEditor.SetXenotype(b, Xeno("Neanderthal"));
                Open().SetRole(a, KinRole.Child);
                window.SetRole(b, KinRole.Child);
            }, () =>
            {
                KinLog(window.Rolled);
                Expect(window.Rolled.genes.hybrid, "two xenotypes should make a hybrid: " + window.Rolled.genes.XenotypeLabel);
            }, true, null, 3);
            Add("kin_couple_same_xenotype", () =>
            {
                PawnEditor.SetXenotype(b, Xeno("Yttakin"));
                Open().SetRole(a, KinRole.Child);
                window.SetRole(b, KinRole.Child);
            }, () => Expect(window.Rolled.genes.Xenotype == Xeno("Yttakin") && !window.Rolled.genes.hybrid, "same xenotype should pass on: " + window.Rolled.genes.XenotypeLabel), false, null, 3);
            Add("kin_xenogene_parent", () =>
            {
                PawnEditor.SetXenotype(a, Xeno("Sanguophage"));
                Open().SetRole(a, KinRole.Child);
            }, () =>
            {
                KinLog(window.Rolled);
                Expect(window.Rolled.genes.Xenogenes.Count == 0 && !window.Rolled.genes.Endogenes.Any(g => g.def.biostatArc > 0), "implanted or archite genes passed on");
            }, false, null, 3);
            Add("kin_custom_xenotype", () =>
            {
                CustomXenotype custom = new CustomXenotype { name = "Kin test", iconDef = DefDatabase<XenotypeIconDef>.AllDefs.First(), inheritable = true };
                custom.genes.Add(DefDatabase<GeneDef>.GetNamed("Hair_LongOnly"));
                PawnEditor.SetCustomXenotype(a, custom);
                Open().SetRole(a, KinRole.Child);
            }, () =>
            {
                KinLog(window.Rolled);
                Expect(window.Rolled.genes.xenotypeName == "Kin test", "custom xenotype's name lost: " + window.Rolled.genes.xenotypeName);
            }, false, null, 3);
            Add("kin_too_young_parent", () =>
            {
                PawnEditor.SetXenotype(a, XenotypeDefOf.Baseliner);
                PawnEditor.SetXenotype(b, XenotypeDefOf.Baseliner);
                PawnEditor.SetBiologicalAge(a, 16);
                Open().SetRole(a, KinRole.Child);
            }, () =>
            {
                Expect(window.Rolled == null && window.Problem != null, "a 16-year-old became a parent");
                Log.Message("[CAC kin] too young: " + window.Problem);
                Grown(a);
            }, true, null, 3);
            Add("kin_rules", () =>
            {
                Grown(a, b, c);
                Open().SetRole(a, KinRole.Child);
                window.SetRole(b, KinRole.Child);
            }, () =>
            {
                window.SetRole(c, KinRole.Child);
                Expect(window.Problem != null && window.Rolled == null, "three parents were allowed");
                window.SetRole(c, KinRole.Sibling);
                Expect(window.Problem != null && window.Rolled == null, "a sibling and parents were allowed together");
                window.SetRole(c, KinRole.None);
                Expect(window.Rolled != null, "couldn't get back to two parents: " + window.Problem);
            }, false, null, 3);

            // Every xenotype on the mod list, as a parent, and as the child of a new parent.
            foreach (XenotypeDef x in DefDatabase<XenotypeDef>.AllDefsListForReading)
            {
                if (x.defName == "GS_Molong")
                {
                    // A Molong baby made the game's way, with none of this code: does its mod's error come with it?
                    XenotypeDef molong = x;
                    Add("kin_plain_molong_baby", () =>
                    {
                        Log.Message("[CAC kin] plain Molong baby, no genetics code:");
                        PawnEditor.DiscardRelative(PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                            allowDowned: true, forcedXenotype: molong, developmentalStages: DevelopmentalStage.Baby)));
                    }, null, false, null, 3);
                }
                XenotypeDef xeno = x;
                Add("kin_xeno_" + x.defName, () =>
                {
                    Grown(a);
                    PawnEditor.SetXenotype(a, xeno);
                    Open().SetRole(a, KinRole.Child);
                }, () =>
                {
                    Expect(window.Rolled != null, "couldn't roll a child: " + window.Problem);
                    Log.Message("[CAC kin] child of " + xeno.defName + " (inheritable " + xeno.inheritable + "): " + window.Rolled?.genes.XenotypeLabel + ", " + window.Rolled?.genes.Endogenes.Count + "/" + a.genes.Endogenes.Count + " genes");
                    window.Close();
                    PawnEditor.SetBiologicalAge(a, 30);
                    Open().SetRole(a, KinRole.Parent);
                    Expect(window.Rolled?.DevelopmentalStage.Adult() == true, "couldn't roll a parent: " + window.Problem);
                }, false, null, 2);
            }

            Pawn father = null;
            Add("kin_father_of_colonist", () =>
            {
                Grown(a);
                PawnEditor.SetBiologicalAge(b, 30);
                PawnEditor.SetXenotype(b, Xeno("Yttakin"));
                foreach (Pawn parent in PawnEditor.ParentsOf(b).Where(p => Colonists.Contains(p)))
                {
                    b.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, parent);
                }
                Open().SetRole(b, KinRole.Parent);
                window.SetGender(Gender.Male);
            }, () =>
            {
                KinLog(window.Rolled);
                Expect(window.Rolled.ageTracker.AgeBiologicalYears >= 48, "a parent should be at least 18 years older: " + window.Rolled.ageTracker.AgeBiologicalYears);
                Expect(window.Rolled.gender == Gender.Male && window.Rolled.story.Adulthood != null, "not a grown man");
                Expect(window.Rolled.genes.Xenotype == Xeno("Yttakin"), "a parent should match their child: " + window.Rolled.genes.XenotypeLabel);
                father = window.Rolled;
                window.AddToGroup();
                Expect(Colonists.Contains(father) && b.GetFather() == father, "didn't become their father");
                Expect(PawnEditor.RelationBetween(b, father) == PawnRelationDefOf.Parent, "the Relationships tab doesn't show it");
                Expect(selected == father, "the new colonist wasn't selected");
            }, true, null, 3);
            Add("kin_remove_father", () => Remove(father), () => Expect(b.GetFather() != father, "removing them left the link"), false, null, 3);
            Add("kin_sibling", () =>
            {
                Open().SetRole(b, KinRole.Sibling);
            }, () =>
            {
                KinLog(window.Rolled);
                // Their sibling's age, unless their parents are too young for that.
                Expect(window.Rolled.ageTracker.AgeBiologicalYears <= b.ageTracker.AgeBiologicalYears, "shouldn't default to older than their sibling");
                Pawn sister = window.Rolled;
                window.AddToGroup();
                Expect(PawnRelationDefOf.Sibling.Worker.InRelation(sister, b), "didn't become siblings");
                Remove(sister);
            }, false, null, 3);
            Add("kin_same_gender_couple", () =>
            {
                Grown(a, b);
                PawnEditor.SetGender(a, Gender.Female);
                PawnEditor.SetGender(b, Gender.Female);
                Open().SetRole(a, KinRole.Child);
                window.SetRole(b, KinRole.Child);
            }, () =>
            {
                Pawn child = window.Rolled;
                Expect(child != null, "a same-gender couple was refused: " + window.Problem);
                window.AddToGroup();
                Expect(PawnEditor.ParentsOf(child).Count == 2, "should have two parents, has " + PawnEditor.ParentsOf(child).Count);
                Expect(PawnEditor.RelationBetween(child, b) == PawnRelationDefOf.Parent && PawnEditor.RelationBetween(b, child) == PawnRelationDefOf.Child, "second parent isn't shown");
                PawnEditor.ClearRelationsBetween(child, b);
                Expect(PawnEditor.ParentsOf(child).Count == 1, "clearing left the second parent");
                Remove(child);
            }, false, null, 3);

            string savedPath = null;
            Pawn adultKid = null;
            Add("kin_saved_parent", () =>
            {
                Grown(a);
                ColonistPreset preset = ColonistPreset.From(a, "CAC Kin parent", withFamily: false);
                preset.Save();
                savedPath = preset.filePath;
                Open().AddSaved(ColonistPreset.Load(savedPath));
            }, () =>
            {
                KinLog(window.Rolled);
                Expect(window.Rolled != null, "no one rolled from a saved colonist: " + window.Problem);
                window.SetAge(20);
                adultKid = window.Rolled;
                Expect(adultKid.DevelopmentalStage.Adult() && adultKid.story.Adulthood != null, "an adult child should be a full adult");
                window.AddToGroup();
                Pawn parent = PawnEditor.ParentsOf(adultKid).FirstOrDefault();
                Expect(parent != null && Find.WorldPawns.Contains(parent) && !Colonists.Contains(parent), "the saved colonist should be a relative elsewhere");
                Log.Message("[CAC kin] saved parent: " + parent?.LabelShort + " " + parent?.Faction + " age " + parent?.ageTracker.AgeBiologicalYears);
                GalleryFiles.Delete(savedPath, ColonistPreset.Folder);
                GalleryFiles.Delete(GalleryThumbnails.PathFor(savedPath), ColonistPreset.Folder);
                Remove(adultKid);
            }, true, null, 3);

            ColonistPreset babyPreset = null;
            Add("kin_baby_saves_and_loads", () =>
            {
                babyPreset = ColonistPreset.From(kid, "CAC Kin baby");
                babyPreset.Save();
                babyPreset = ColonistPreset.Load(babyPreset.filePath);
                PawnEditor.SetBiologicalAge(kid, 30);
                babyPreset.ApplyTo(kid);
            }, () =>
            {
                KinLog(kid);
                Expect(kid.DevelopmentalStage.Baby() && kid.ageTracker.AgeBiologicalYears == 0, "a saved baby didn't load back as a baby");
                Expect(Find.GameInitData.startingPossessions[kid].Any(t => t.ThingDef == ThingDefOf.BabyFood), "a loaded baby has no baby food");
                Expect(kid.equipment.Primary == null && kid.inventory.innerContainer.Count == 0, "a loaded baby has gear");
                Find.GameInitData.allowedDevelopmentalStages = DevelopmentalStage.Adult;
                babyPreset.ApplyTo(kid);
                KinLog(kid);
                Expect(kid.ageTracker.AgeBiologicalYears == 13 && kid.story.Childhood?.spawnCategories?.Contains("Newborn") != true, "a baby loaded into an adults-only scenario");
                Find.GameInitData.allowedDevelopmentalStages = DevelopmentalStage.Baby | DevelopmentalStage.Child | DevelopmentalStage.Adult;
                babyPreset.ApplyTo(kid);
                babyPreset.Delete();
            }, false, null, 3);
            Add("kin_locked_reroll_of_baby", () =>
            {
                Show(kid, CreatorTab.Personality);
                lockedTabs.Add(CreatorTab.Personality);
                RandomizeSelected();
            }, () =>
            {
                lockedTabs.Clear();
                kid = selected;
                KinLog(kid);
                Expect(kid.DevelopmentalStage.Baby(), "a locked baby didn't stay a baby");
                Expect(kid.equipment.Primary == null && kid.inventory.innerContainer.Count == 0, "a locked baby took a grown colonist's gear");
                Expect(Find.GameInitData.startingPossessions[kid].Any(t => t.ThingDef == ThingDefOf.BabyFood), "a locked baby lost its food");
            }, false, null, 3);

            Add("kin_age_breaks_parent_link", () =>
            {
                Grown(a);
                PawnEditor.SetBiologicalAge(kid, 5);
                PawnEditor.SetParent(kid, a);
            }, () =>
            {
                Expect(PawnEditor.ParentsOf(kid).Contains(a), "couldn't set a parent");
                PawnEditor.SetBiologicalAge(a, 15);
                Expect(!PawnEditor.ParentsOf(kid).Contains(a), "someone under 18 stayed a parent");
                Grown(a);
                PawnEditor.SetParent(kid, a);
                PawnEditor.SetBiologicalAge(kid, 30);
                Expect(!PawnEditor.ParentsOf(kid).Contains(a), "a child aged close to their parent kept the link");
            }, false, null, 3);

            Add("kin_undo_age_keeps_family", () =>
            {
                Grown(a, b);
                PawnEditor.SetBiologicalAge(kid, 5);
                PawnEditor.ClearRelationsBetween(a, b);
                PawnEditor.ClearRelationsBetween(a, kid);
                PawnEditor.SetLoveRelation(a, b, PawnRelationDefOf.Spouse);
                PawnEditor.SetParent(kid, a);
                Select(a);
                RecordUndo();
            }, () =>
            {
                bool Linked() => a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b) && PawnEditor.ParentsOf(kid).Contains(a);
                bool Cut() => !a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b) && !PawnEditor.ParentsOf(kid).Contains(a);
                Expect(Linked(), "couldn't set up the family");
                PawnEditor.SetBiologicalAge(a, 15);
                RecordUndo();
                Expect(Cut(), "an age of 15 kept a spouse or a child");
                StepUndo(false);
                Expect(Linked() && a.ageTracker.AgeBiologicalYears == 40, "undo didn't bring the family back");
                StepUndo(true);
                Expect(Cut() && a.ageTracker.AgeBiologicalYears == 15, "redo didn't take the family away again");
                StepUndo(false);
                Expect(Linked(), "a second undo didn't bring the family back");
                // A link removed by hand isn't undone along with something else.
                PawnEditor.SetBiologicalAge(a, 41);
                RecordUndo();
                PawnEditor.ClearRelationsBetween(a, b);
                RecordUndo();
                StepUndo(false);
                Expect(a.ageTracker.AgeBiologicalYears == 40 && !a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b), "undo brought back a spouse removed by hand");
                PawnEditor.ClearRelationsBetween(a, kid);
            }, false, null, 3);

            Add("kin_sibling_parents_elsewhere", () =>
            {
                Grown(a, b);
                List<Pawn> elsewhere = PawnEditor.ParentsOf(a).Where(p => !Colonists.Contains(p)).ToList();
                if (elsewhere.Count == 0)
                {
                    Pawn parent = PawnGenerator.GeneratePawn(new PawnGenerationRequest(a.kindDef, a.Faction, forceGenerateNewPawn: true, fixedGender: Gender.Female));
                    Find.WorldPawns.PassToWorld(parent, PawnDiscardDecideMode.KeepForever);
                    a.relations.AddDirectRelation(PawnRelationDefOf.Parent, parent);
                    elsewhere.Add(parent);
                }
                // 57 fits 41 under the game's ages, but only 39 under the group's 18 years.
                elsewhere.ForEach(p => p.ageTracker.AgeBiologicalTicks = 57L * GenDate.TicksPerYear + 1);
                Open().SetAge(41);
                window.SetRole(a, KinRole.Sibling);
            }, () =>
            {
                Expect(window.Problem == null, "the sibling was refused: " + window.Problem);
                Expect(window.Rolled?.ageTracker.AgeBiologicalYears == 41, "held to the 18-year rule for a parent elsewhere: " + window.Rolled?.ageTracker.AgeBiologicalYears);
                window.Close();
            }, false, null, 3);

            Pawn newborn = null;
            Add("kin_final_family", () =>
            {
                Remove(kid);
                Grown(a, b);
                PawnEditor.SetGender(a, Gender.Female);
                PawnEditor.SetGender(b, Gender.Male);
                Open().SetRole(a, KinRole.Child);
                window.SetRole(b, KinRole.Child);
            }, () =>
            {
                newborn = window.Rolled;
                window.AddToGroup();
                DevExplore.kinBaby = newborn;
                DevExplore.kinFood = Find.GameInitData.startingPossessions[newborn].Where(t => t.ThingDef == ThingDefOf.BabyFood).Sum(t => t.Count);
                KinLog(newborn);
                Expect(newborn.GetMother() == a && newborn.GetFather() == b, "parents not linked");
                Expect(Find.GameInitData.startingAndOptionalPawns.Take(Find.GameInitData.startingPawnCount).Contains(newborn), "the baby isn't in the group");
            }, true, null, 3);
            Add("kin_colony_round_trip", () =>
            {
                ColonyPreset saved = ColonyPreset.From(Colonists, state, "CAC Kin colony");
                saved.Save();
                ColonyPreset colony = ColonyPreset.LoadAll().First(x => x.presetName == "CAC Kin colony");
                LoadColony(colony);
                colony.Delete();
            }, () =>
            {
                KinLog(newborn);
                Expect(newborn.DevelopmentalStage.Baby() && Colonists.Contains(newborn), "the baby didn't come back with the colony");
                Expect(newborn.GetMother() == a && newborn.GetFather() == b, "the baby's parents didn't come back with the colony");
                Expect(Find.GameInitData.startingPossessions[newborn].Any(t => t.ThingDef == ThingDefOf.BabyFood), "the baby's food didn't come back with the colony");
            }, false, null, 3);
        }
    }

    public static partial class DevKin
    {
        /// <summary>In play: the game's own growing up runs on the baby brought from the creator.</summary>
        public static void CheckInGame(List<Pawn> colonists)
        {
            Pawn baby = DevExplore.kinBaby;
            if (baby == null)
            {
                return;
            }
            Map map = Find.CurrentMap;
            // Everyone lands first; the game drops every starting item forbidden, so then the food is allowed, as a player would.
            for (int i = 0; i < 1500; i++)
            {
                Find.TickManager.DoSingleTick();
            }
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.BabyFood))
            {
                thing.SetForbidden(false, warnOnFail: false);
            }
            // Hungry, so someone has to feed them, then half a day to do it.
            baby.needs.food.CurLevelPercentage = 0.1f;
            for (int i = 0; i < 30000 && !baby.Dead; i++)
            {
                Find.TickManager.DoSingleTick();
            }
            int food = map.listerThings.ThingsOfDef(ThingDefOf.BabyFood).Sum(t => t.stackCount) + colonists.Sum(p => p.inventory.innerContainer.Where(t => t.def == ThingDefOf.BabyFood).Sum(t => t.stackCount));
            Log.Message("[CAC kin] in game after " + Find.TickManager.TicksGame + " ticks: " + baby.LabelShort + " spawned=" + baby.Spawned + " downed=" + baby.Downed + " hunger=" + baby.needs.food.CurCategory
                + " fed " + baby.needs.food.CurLevelPercentage.ToStringPercent() + ", baby food " + DevExplore.kinFood + " -> " + food + ", job of carers: " + colonists.Where(p => p != baby).Select(p => p.CurJobDef?.defName).ToCommaList()
                + " father=" + baby.GetFather()?.LabelShort + " mother=" + baby.GetMother()?.LabelShort);
            WorkTypeDef childcare = DefDatabase<WorkTypeDef>.GetNamed("Childcare");
            WorkGiver_Scanner bottle = (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("BottleFeedBaby").Worker;
            foreach (Pawn p in colonists.Where(c => c != baby))
            {
                Job job = bottle.JobOnThing(p, baby);
                Log.Message("[CAC kin] carer " + p.LabelShort + " childcare priority=" + p.workSettings?.GetPriority(childcare) + " disabled=" + p.WorkTypeIsDisabled(childcare)
                    + " job=" + job?.def?.defName + " why=" + JobFailReason.Reason + " food=" + ChildcareUtility.FindBabyFoodForBaby(p, baby)
                    + " canHaul=" + ChildcareUtility.CanHaulBabyNow(p, baby, false, out ChildcareUtility.BreastfeedFailReason? why) + "/" + why
                    + " reach=" + p.CanReach(baby, Verse.AI.PathEndMode.Touch, Danger.Deadly) + " at " + p.Position + " hour " + GenLocalDate.HourOfDay(p));
            }
            Log.Message("[CAC kin] baby at " + baby.Position + " forbidden=" + baby.IsForbidden(colonists[0]) + "; food: " + map.listerThings.ThingsOfDef(ThingDefOf.BabyFood).Select(t => t.stackCount + " at " + t.Position + (t.IsForbidden(Faction.OfPlayer) ? " forbidden" : "") + (t.Spawned ? "" : " unspawned")).ToCommaList());
            int letters = Find.LetterStack.LettersListForReading.Count;
            foreach (int years in new[] { 3, 7, 10, 13 })
            {
                baby.ageTracker.DebugSetAge(years * 3600000L + 1000L);
                Log.Message("[CAC kin] grew to " + years + ": " + baby.LabelShort + " stage=" + baby.DevelopmentalStage + " body=" + baby.story.bodyType?.defName + " child=" + baby.story.Childhood?.defName
                    + " adult=" + baby.story.Adulthood?.defName + " ideo=" + baby.Ideo?.name + " traits=" + baby.story.traits.allTraits.Select(t => t.def.defName).ToCommaList()
                    + " letters=" + Find.LetterStack.LettersListForReading.Skip(letters).Select(l => l.Label.ToString()).ToCommaList());
            }
        }
    }
}
namespace CreateAColonist
{
    /// <summary>-Headless never draws, so the steps that run on the page's repaint run from the game's update instead.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class Patch_HeadlessDrive
    {
        public static bool Prepare() => DevTest.Headless && DevTest.Tour;

        public static void Postfix()
        {
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }
            Event.current = new Event { type = EventType.Repaint };
            if (Current.ProgramState == ProgramState.Playing)
            {
                DevTest.InGameOnGUI();
            }
            else if (Find.WindowStack?.WindowOfType<Page_CreateAColonist>() is Page_CreateAColonist page)
            {
                page.HeadlessStep();
            }
        }
    }
}
#endif

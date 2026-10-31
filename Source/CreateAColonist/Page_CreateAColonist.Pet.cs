using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        private bool petsOnly = true;
        private readonly QuickSearchWidget animalSearch = new QuickSearchWidget();
        private readonly CachedList<PawnKindDef> animalKindList = new CachedList<PawnKindDef>();

        private void DrawAnimal(ref float y, float w)
        {
            Pawn pet = selected;
            if (pet.RaceProps.hasGenders)
            {
                CACUI.Section(ref y, w, "CAC_Gender".Translate());
                GenderButtons(ref y, pet);
            }
            DrawCoats(ref y, w, pet);
            CACUI.Section(ref y, w, "CAC_Species".Translate());
            animalSearch.OnGUI(new Rect(0f, y, Mathf.Min(220f, w - 160f), 24f));
            Widgets.CheckboxLabeled(new Rect(w - 150f, y, 150f, 24f), "CAC_PetsOnly".Translate(), ref petsOnly);
            y += 30f;
            List<PawnKindDef> kinds = animalKindList.Get(petsOnly + "|" + animalSearch.filter.Text,
                () => CreatorState.AnimalKinds(petsOnly).Where(k => animalSearch.filter.Matches(k.label)).ToList());
            CACUI.Grid(ref y, w, 64f, kinds, k => k == pet.kindDef,
                (r, k) => Widgets.DefIcon(r, k),
                k => k.LabelCap + "\n\n" + k.race.description,
                k =>
                {
                    if (k != pet.kindDef)
                    {
                        selected = state.ChangePetSpecies(pet, k);
                        ReplaceInUndo(pet, selected);
                    }
                });
        }

        private readonly CachedList<(int index, Graphic graphic)> coatList = new CachedList<(int, Graphic)>();

        /// <summary>
        /// Animals that come in several coats (the game picks one from the animal's ID): a picture of each to choose from.
        /// -1 is the standard coat, offered only if the game can roll it for this animal.
        /// </summary>
        private void DrawCoats(ref float y, float w, Pawn pet)
        {
            List<AlternateGraphic> alternates = pet.kindDef.alternateGraphics;
            PawnKindLifeStage stage = pet.ageTracker.CurKindLifeStage;
            GraphicData standard = pet.gender == Gender.Female && stage.femaleGraphicData != null ? stage.femaleGraphicData : stage.bodyGraphicData;
            if (alternates.NullOrEmpty() || standard == null)
            {
                return;
            }
            List<(int index, Graphic graphic)> coats = coatList.Get(pet.kindDef.defName + "|" + standard.texPath, () =>
            {
                List<(int, Graphic)> list = alternates.Select((a, i) => (i, a.GetGraphic(standard.Graphic))).ToList();
                if (pet.kindDef.alternateGraphicChance < 1f)
                {
                    list.Insert(0, (-1, standard.Graphic));
                }
                return list;
            });
            CACUI.Section(ref y, w, "CAC_Coat".Translate());
            int current = pet.GetGraphicIndex();
            CACUI.Grid(ref y, w, 64f, coats, c => c.index == current,
                (r, c) =>
                {
                    GUI.color = c.graphic.color;
                    GUI.DrawTexture(r.ContractedBy(4f), c.graphic.MatSouth.mainTexture, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                },
                c => "CAC_CoatNumber".Translate(coats.IndexOf(c) + 1),
                c =>
                {
                    if (c.index != current)
                    {
                        // A coat can only be picked on a fresh animal, so this rebuilds the pet like undo does.
                        UndoState s = Snapshot(pet);
                        s.pet.coat = c.index;
                        selected = Restore(pet, s);
                    }
                });
        }

        private void OpenPetSpeciesPicker()
        {
            List<PickerOption> options = CreatorState.AnimalKinds(petsOnly: false)
                .OrderByDescending(k => k.RaceProps.petness > 0f)
                .ThenBy(k => k.label)
                .Select(k => new PickerOption
                {
                    label = k.LabelCap.ToString() + (k.RaceProps.petness > 0f ? "" : " " + "CAC_NotAPet".Translate().ToString()),
                    description = k.race.description,
                    mod = k.race.modContentPack,
                    drawIcon = r => Widgets.DefIcon(r, k),
                    onPick = () => Select(state.AddPet(k))
                }).ToList();
            Find.WindowStack.Add(new Dialog_CACPicker("CAC_AddPet".Translate(), options));
        }
    }
}

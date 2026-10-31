using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    public partial class Page_CreateAColonist
    {
        /// <summary>The save button: this colonist on their own, or the whole colony.</summary>
        private void OpenSaveMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("CAC_SaveThisColonist".Translate(selected.LabelShortCap), () => Find.WindowStack.Add(Dialog_SavePreset.ForColonist(selected)),
                    CACTex.Colonist, Color.white),
                new FloatMenuOption("CAC_SaveWholeColony".Translate(), () => Find.WindowStack.Add(Dialog_SavePreset.ForColony(Colonists, state)),
                    CACTex.Gallery, Color.white)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Replaces the whole group with a saved colony: colonists (inventories and all), their relationships, the pets
        /// and their bonds, and the colony supplies.
        /// </summary>
        public void LoadColony(ColonyPreset colony)
        {
            int wanted = Mathf.Clamp(colony.colonists.Count, 1, state.maxColonists);
            if (colony.colonists.Count > wanted)
            {
                Messages.Message("CAC_ColonyTooBig".Translate(colony.colonists.Count, wanted), MessageTypeDefOf.CautionInput, historical: false);
            }
            for (int count = Colonists.Count; count > wanted; count--)
            {
                Remove(Colonists.Last());
            }
            for (int count = Colonists.Count; count < wanted && AddColonist() != null; count++)
            {
            }
            List<Pawn> colonists = Colonists;

            for (int i = 0; i < colonists.Count; i++)
            {
                colony.colonists[i].ApplyTo(colonists[i], withFamily: false);
            }
            // The whole family at once: each other, and relatives outside the colony.
            colony.family.ApplyTo(colonists);

            foreach (Pawn pet in state.pets.ToList())
            {
                state.RemovePet(pet);
            }
            foreach (ColonyPreset.PetEntry entry in colony.pets)
            {
                state.AddPet(entry, entry.master >= 0 && entry.master < colonists.Count ? colonists[entry.master] : null);
            }

            state.supplies.Clear();
            state.supplies.AddRange(colony.SupplyEntries());
            countBuffers.Clear();

            state.RememberColonists();
            selected = null;
            Select(colonists[0]);
            // Saves from before gallery pictures existed get one now.
            if (!GalleryThumbnails.Exists(colony.filePath))
            {
                GalleryThumbnails.SaveGroup(colonists, colony.filePath);
            }
            Messages.Message("CAC_ColonyLoaded".Translate(colony.presetName), MessageTypeDefOf.TaskCompletion, historical: false);
        }
    }
}

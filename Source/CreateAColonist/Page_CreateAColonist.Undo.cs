using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>
    /// Undo and redo for edits to a colonist or pet. Just after a click or key press the selected one is compared with
    /// how it was (a colonist saved the way the gallery saves colonists, a pet the way a saved colony keeps pets); if
    /// anything changed, the old version becomes an undo step and undoing loads it back. Adding or removing them and
    /// family links aren't steps: they change the group.
    /// </summary>
    public partial class Page_CreateAColonist
    {
        private class UndoState
        {
            public ColonistPreset colonist;
            public ColonyPreset.PetEntry pet;
            public Pawn master;
            public int chronological;
            public string key;
        }

        private const int MaxUndoSteps = 50;
        private readonly List<(Pawn pawn, UndoState before)> undoSteps = new List<(Pawn, UndoState)>();
        private readonly List<(Pawn pawn, UndoState before)> redoSteps = new List<(Pawn, UndoState)>();
        private Pawn undoPawn;
        private int undoIndex;
        private UndoState undoBase;
        private int undoCheckFrames;
        private bool undoTyping;

        private UndoState Snapshot(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike)
            {
                ColonistPreset preset = ColonistPreset.From(pawn, null, withFamily: false);
                return new UndoState { colonist = preset, key = Scribe.saver.DebugOutputFor(preset) };
            }
            UndoState s = new UndoState
            {
                pet = new ColonyPreset.PetEntry
                {
                    kind = pawn.kindDef,
                    name = pawn.Name?.ToStringFull,
                    gender = pawn.gender,
                    age = pawn.ageTracker.AgeBiologicalYears,
                    coat = pawn.GetGraphicIndex()
                },
                master = PawnEditor.GetBondedMaster(pawn),
                chronological = pawn.ageTracker.AgeChronologicalYears
            };
            s.key = string.Join("|", s.pet.kind.defName, s.pet.name, s.pet.gender, s.pet.age, s.chronological, s.pet.coat, s.master?.thingIDNumber);
            return s;
        }

        /// <summary>Puts them back as they were. A pet comes back as a fresh animal, so this returns who they are now.</summary>
        private Pawn Restore(Pawn pawn, UndoState s)
        {
            if (s.colonist != null)
            {
                s.colonist.ApplyTo(pawn, withFamily: false);
                return pawn;
            }
            int index = state.pets.IndexOf(pawn);
            state.RemovePet(pawn);
            Pawn pet = state.AddPet(s.pet, Colonists.Contains(s.master) ? s.master : null, index, s.chronological);
            ReplaceInUndo(pawn, pet);
            return pet;
        }

        private bool InGroup(Pawn pawn) => state.pets.Contains(pawn) || Colonists.Contains(pawn);

        private int Slot(Pawn pawn) => pawn.RaceProps.Humanlike ? PawnEditor.StartingIndex(pawn) : state.pets.IndexOf(pawn);

        /// <summary>A pet's new species (or a re-rolled colonist) is a new pawn: keep its history with it.</summary>
        private void ReplaceInUndo(Pawn from, Pawn to)
        {
            foreach (List<(Pawn pawn, UndoState before)> steps in new[] { undoSteps, redoSteps })
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].pawn == from)
                    {
                        steps[i] = (to, steps[i].before);
                    }
                }
            }
        }

        private void ForgetUndo(Pawn pawn)
        {
            undoSteps.RemoveAll(s => s.pawn == pawn);
            redoSteps.RemoveAll(s => s.pawn == pawn);
            undoPawn = null;
        }

        private void UndoButtons(Rect undo, Rect redo)
        {
            UndoButton(undo, CACTex.Undo, "CAC_Undo".Translate(), redo: false);
            UndoButton(redo, CACTex.Redo, "CAC_Redo".Translate(), redo: true);
        }

        private void UndoButton(Rect rect, Texture2D icon, string tip, bool redo)
        {
            if ((redo ? redoSteps : undoSteps).Count > 0)
            {
                if (CACUI.IconButton(rect, icon, tip))
                {
                    StepUndo(redo);
                }
                return;
            }
            // Nothing to undo: greyed out, still explains itself.
            TooltipHandler.TipRegion(rect, tip);
            GUI.color = CACUI.Dim;
            GUI.DrawTexture(rect.ContractedBy(2f), icon);
            GUI.color = Color.white;
        }

        private void UndoInput()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.control || e.command) && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
            {
                StepUndo(redo: e.keyCode == KeyCode.Y || e.shift);
                e.Use();
            }
            if (e.type != EventType.Repaint)
            {
                return;
            }
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                undoTyping = false;
            }
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.anyKeyDown)
            {
                undoCheckFrames = 2;
            }
            // Only look just after input (and when the selection changes), so idle frames cost nothing; wait out drags.
            if (selected != undoPawn || (undoCheckFrames > 0 && !Input.GetMouseButton(0)))
            {
                undoCheckFrames = Mathf.Max(0, undoCheckFrames - 1);
                RecordUndo();
            }
        }

        private void RecordUndo()
        {
            if (selected == null)
            {
                return;
            }
            UndoState now = Snapshot(selected);
            // One swapped for a new pawn in the same place (Randomize, a pet's new species) is still an edit to it.
            bool replaced = selected != undoPawn && undoPawn != null && !InGroup(undoPawn)
                && undoPawn.RaceProps.Humanlike == selected.RaceProps.Humanlike && Slot(selected) == undoIndex;
            if ((selected == undoPawn && now.key != undoBase.key) || replaced)
            {
                // Typing a name is one step, not one per letter.
                bool typing = GUIUtility.keyboardControl != 0;
                if (!(typing && undoTyping && undoSteps.Count > 0 && undoSteps[undoSteps.Count - 1].pawn == selected))
                {
                    undoSteps.Add((selected, undoBase));
                    if (undoSteps.Count > MaxUndoSteps)
                    {
                        undoSteps.RemoveAt(0);
                    }
                }
                undoTyping = typing;
                redoSteps.Clear();
            }
            undoPawn = selected;
            undoIndex = Slot(selected);
            undoBase = now;
        }

        private void StepUndo(bool redo)
        {
            RecordUndo();
            List<(Pawn pawn, UndoState before)> from = redo ? redoSteps : undoSteps, to = redo ? undoSteps : redoSteps;
            from.RemoveAll(s => !InGroup(s.pawn));
            if (from.Count == 0)
            {
                return;
            }
            (Pawn pawn, UndoState before) = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            // Boxes still holding the old text would write it back next frame, which looks like a new edit and blocks
            // undoing further.
            UI.UnfocusCurrentControl();
            countBuffers.Clear();
            bioAgeText = chronoAgeText = null;
            StopSkillEdit();
            to.Add((pawn, Snapshot(pawn)));
            pawn = Restore(pawn, before);
            Select(pawn);
            // Undoing can take a tab away (a baby has no Ideoligion tab).
            EnsureSelection();
            undoPawn = null;
            undoTyping = false;
            RecordUndo();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
    }
}

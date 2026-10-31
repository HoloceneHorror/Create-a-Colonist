using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>Asks for a name and saves a colonist, or the whole colony, to the gallery. Also used to rename.</summary>
    public class Dialog_SavePreset : Window
    {
        private readonly string title;
        private readonly System.Func<string, string> pathFor;
        private readonly System.Func<string, string> overwriteText;
        private readonly System.Action<string> save;
        private readonly string doneKey;
        private readonly string ownPath;
        private string name;
        private bool focused;

        public override Vector2 InitialSize => new Vector2(460f, 190f);

        /// <param name="ownPath">When renaming: the file being renamed, which may keep its own name without asking.</param>
        private Dialog_SavePreset(string title, string defaultName, System.Func<string, string> pathFor, System.Func<string, string> overwriteText,
            System.Action<string> save, string doneKey = "CAC_Saved", string ownPath = null)
        {
            this.title = title;
            this.pathFor = pathFor;
            this.overwriteText = overwriteText;
            this.save = save;
            this.doneKey = doneKey;
            this.ownPath = ownPath;
            name = defaultName;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
            doCloseX = true;
        }

        public static Dialog_SavePreset ForColonist(Pawn pawn)
        {
            return new Dialog_SavePreset("CAC_SaveToGallery".Translate(), pawn.Name?.ToStringFull ?? pawn.LabelShortCap, ColonistPreset.PathFor,
                n => "CAC_OverwritePreset".Translate(n), n =>
                {
                    ColonistPreset preset = ColonistPreset.From(pawn, n);
                    preset.Save();
                    GalleryThumbnails.SaveColonist(pawn, preset.filePath);
                });
        }

        /// <summary>Captured when saved, not when the window opens, so it's the colony as it is at that moment.</summary>
        public static Dialog_SavePreset ForColony(List<Pawn> colonists, CreatorState state)
        {
            string suggested = Find.World?.info?.name.NullOrEmpty() == false ? Find.World.info.name : state.originalScenario?.name ?? "Colony";
            return new Dialog_SavePreset("CAC_SaveColonyTitle".Translate(), suggested, ColonyPreset.PathFor,
                n => "CAC_OverwriteColony".Translate(n), n =>
                {
                    ColonyPreset colony = ColonyPreset.From(colonists, state, n);
                    colony.Save();
                    GalleryThumbnails.SaveGroup(colonists, colony.filePath);
                });
        }

        public static Dialog_SavePreset ForRename(string current, string currentPath, System.Func<string, string> pathFor, System.Func<string, string> overwriteText,
            System.Action<string> rename)
        {
            return new Dialog_SavePreset("CAC_RenameTitle".Translate(current), current, pathFor, overwriteText, rename, "CAC_Renamed", currentPath);
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), title.Truncate(inRect.width));
            }
            GUI.SetNextControlName("CAC_PresetName");
            name = Widgets.TextField(new Rect(inRect.x, inRect.y + 46f, inRect.width, 32f), name, 40);
            if (!focused)
            {
                UI.FocusControl("CAC_PresetName", this);
                focused = true;
            }
            Rect save = new Rect(inRect.xMax - 150f, inRect.yMax - 38f, 150f, 38f);
            bool enter = Event.current.type == EventType.keyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if ((Widgets.ButtonText(save, ownPath != null ? "CAC_Rename".Translate() : "Save".Translate()) || enter) && !name.Trim().NullOrEmpty())
            {
                string path = pathFor(name.Trim());
                if (File.Exists(path) && (ownPath == null || !GalleryFiles.SamePath(path, ownPath)))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(overwriteText(name.Trim()), DoSave, destructive: true));
                }
                else
                {
                    DoSave();
                }
                if (enter)
                {
                    Event.current.Use();
                }
            }
        }

        private void DoSave()
        {
            save(name.Trim());
            Messages.Message(doneKey.Translate(name.Trim()), MessageTypeDefOf.TaskCompletion, historical: false);
            Close();
        }
    }

    /// <summary>
    /// Browse the gallery. Colonists: load one into the selected colonist or add it as a new colonist.
    /// Colonies: replace the whole group with a saved colony. Both can be renamed, copied and deleted.
    /// </summary>
    public class Dialog_Gallery : Window
    {
        private readonly Page_CreateAColonist page;
        private readonly Pawn target;
        private List<ColonistPreset> presets;
        private ColonistPreset chosen;
        private List<ColonyPreset> colonies;
        private ColonyPreset chosenColony;
        private int sub;
        private Vector2 scroll;
        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private readonly GalleryThumbnails.Cache pictures = new GalleryThumbnails.Cache();

        private const float RowH = 60f;

        public override Vector2 InitialSize => new Vector2(800f, 640f);

        /// <param name="target">Colonist to overwrite, or null to add the preset as a new colonist.</param>
        public Dialog_Gallery(Page_CreateAColonist page, Pawn target, bool showColonies = false)
        {
            sub = showColonies ? 1 : 0;
            this.page = page;
            this.target = target;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
            doCloseX = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            Reload(null, null);
        }

        public override void PostClose()
        {
            base.PostClose();
            pictures.Clear();
        }

        /// <summary>Reads the gallery again (after a rename or copy), keeping the given file selected.</summary>
        private void Reload(string colonistPath, string colonyPath)
        {
            pictures.Clear();
            presets = ColonistPreset.LoadAll();
            chosen = presets.FirstOrDefault(p => colonistPath != null && GalleryFiles.SamePath(p.filePath, colonistPath)) ?? presets.FirstOrDefault();
            colonies = ColonyPreset.LoadAll();
            chosenColony = colonies.FirstOrDefault(c => colonyPath != null && GalleryFiles.SamePath(c.filePath, colonyPath)) ?? colonies.FirstOrDefault();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), "CAC_Gallery".Translate());
            }
            int before = sub;
            sub = CACUI.Pills(new Rect(inRect.x, inRect.y + 40f, inRect.width, 28f),
                new[] { (string)"CAC_GalleryColonists".Translate(), (string)"CAC_GalleryColonies".Translate() }, sub);
            if (sub != before)
            {
                scroll = Vector2.zero;
            }
            inRect.yMin += 36f;
            if (sub == 1)
            {
                DrawColonies(inRect);
            }
            else
            {
                DrawColonists(inRect);
            }
        }

        // ---------------------------------------------------------------- shared pieces

        /// <summary>The saved picture, or a plain silhouette for saves from before pictures existed.</summary>
        private void DrawPicture(Rect r, string filePath)
        {
            Texture2D tex = pictures.Get(filePath);
            if (tex != null)
            {
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color = CACUI.Dim;
                GUI.DrawTexture(r.ContractedBy(r.width * 0.15f), CACTex.Colonist, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
        }

        private static void ListRowText(Rect row, float left, string name, string summary)
        {
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft))
            {
                Widgets.Label(new Rect(left, row.y + 8f, row.xMax - left - 6f, 22f), name.Truncate(row.xMax - left - 6f));
            }
            using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(left, row.y + 32f, row.xMax - left - 6f, 20f), summary.Truncate(row.xMax - left - 6f));
            }
        }

        /// <summary>Rename and Duplicate, side by side.</summary>
        private void ManageButtons(Rect r, System.Action rename, System.Action duplicate)
        {
            float half = (r.width - 6f) / 2f;
            if (Widgets.ButtonText(new Rect(r.x, r.y, half, r.height), "CAC_Rename".Translate()))
            {
                rename();
            }
            if (Widgets.ButtonText(new Rect(r.x + half + 6f, r.y, half, r.height), "CAC_Duplicate".Translate()))
            {
                duplicate();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        // ---------------------------------------------------------------- colonists

        private void DrawColonists(Rect inRect)
        {
            if (!presets.Any())
            {
                using (new TextBlock(TextAnchor.MiddleCenter, ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 80f), "CAC_GalleryEmpty".Translate());
                }
                return;
            }
            search.OnGUI(new Rect(inRect.x, inRect.y + 40f, 260f, 24f));
            Rect list = new Rect(inRect.x, inRect.y + 72f, inRect.width * 0.45f, inRect.height - 72f);
            Rect detail = new Rect(list.xMax + 12f, list.y, inRect.xMax - list.xMax - 12f, list.height);

            List<ColonistPreset> shown = presets.Where(p => search.filter.Matches(p.presetName)).ToList();
            Rect view = new Rect(0f, 0f, list.width - 16f, shown.Count * (RowH + 4f));
            Widgets.BeginScrollView(list, ref scroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                ColonistPreset p = shown[i];
                Rect row = new Rect(0f, i * (RowH + 4f), view.width, RowH);
                if (row.yMax < scroll.y || row.y > scroll.y + list.height)
                {
                    continue;
                }
                if (Event.current.type == EventType.Repaint)
                {
                    Widgets.DrawOptionBackground(row, p == chosen);
                    DrawPicture(new Rect(row.x + 4f, row.y + 2f, 46f, RowH - 4f), p.filePath);
                    ListRowText(row, row.x + 56f, p.presetName, p.Summary);
                }
                if (Widgets.ButtonInvisible(row))
                {
                    chosen = p;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }
            Widgets.EndScrollView();

            Widgets.DrawMenuSection(detail);
            if (chosen == null)
            {
                return;
            }
            Rect d = detail.ContractedBy(12f);
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(d.x, d.y, d.width, 32f), chosen.presetName.Truncate(d.width));
            }
            if (Event.current.type == EventType.Repaint)
            {
                Rect pic = new Rect(d.xMax - 104f, d.y + 40f, 104f, 130f);
                DrawPicture(pic, chosen.filePath);
                string text = chosen.Summary + "\n\n"
                    + "Traits".Translate() + ": " + chosen.traits.Select(t => t.def.DataAtDegree(t.degree).GetLabelCapFor(chosen.gender)).ToCommaList() + "\n\n"
                    + "CAC_Wearing".Translate() + ": " + chosen.apparel.Select(a => a.def.label).ToCommaList();
                float buttons = target != null ? 3 : 2;
                Widgets.Label(new Rect(d.x, d.y + 40f, d.width - pic.width - 10f, d.height - 40f - buttons * 44f), text);
            }

            float by = d.yMax - 38f;
            ColonistPreset current = chosen;
            ManageButtons(new Rect(d.x, by - 44f, d.width, 38f),
                () => Find.WindowStack.Add(Dialog_SavePreset.ForRename(current.presetName, current.filePath, ColonistPreset.PathFor,
                    n => "CAC_OverwritePreset".Translate(n), n =>
                    {
                        current.RenameTo(n);
                        Reload(current.filePath, null);
                    })),
                () =>
                {
                    ColonistPreset copy = current.Duplicate();
                    Reload(copy?.filePath ?? current.filePath, null);
                    if (copy != null)
                    {
                        Messages.Message("CAC_Duplicated".Translate(copy.presetName), MessageTypeDefOf.TaskCompletion, historical: false);
                    }
                });
            if (target != null && Widgets.ButtonText(new Rect(d.x, by - 88f, d.width, 38f), "CAC_LoadInto".Translate(target.LabelShortCap)))
            {
                Apply(current, target);
                return;
            }
            if (Widgets.ButtonText(new Rect(d.x, by, d.width * 0.6f, 38f), "CAC_AddAsNew".Translate()))
            {
                Pawn added = page.AddColonist();
                if (added == null)
                {
                    Messages.Message("CAC_GroupFull".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                }
                else
                {
                    Apply(current, added);
                    return;
                }
            }
            if (Widgets.ButtonText(new Rect(d.x + d.width * 0.6f + 6f, by, d.width * 0.4f - 6f, 38f), "Delete".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CAC_DeletePreset".Translate(current.presetName), () =>
                {
                    pictures.Forget(current.filePath);
                    current.Delete();
                    presets.Remove(current);
                    chosen = presets.FirstOrDefault();
                }, destructive: true));
            }
        }

        private void Apply(ColonistPreset preset, Pawn pawn)
        {
            preset.ApplyTo(pawn);
            page.Notify_PresetApplied(pawn);
            // Saves from before pictures existed get one now, from the colonist they just became.
            if (!GalleryThumbnails.Exists(preset.filePath))
            {
                GalleryThumbnails.SaveColonist(pawn, preset.filePath);
            }
            Close();
        }

        // ---------------------------------------------------------------- colonies

        private void DrawColonies(Rect inRect)
        {
            if (!colonies.Any())
            {
                using (new TextBlock(TextAnchor.MiddleCenter, ColoredText.SubtleGrayColor))
                {
                    Widgets.Label(new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 80f), "CAC_GalleryNoColonies".Translate());
                }
                return;
            }
            search.OnGUI(new Rect(inRect.x, inRect.y + 40f, 260f, 24f));
            Rect list = new Rect(inRect.x, inRect.y + 72f, inRect.width * 0.45f, inRect.height - 72f);
            Rect detail = new Rect(list.xMax + 12f, list.y, inRect.xMax - list.xMax - 12f, list.height);

            List<ColonyPreset> shown = colonies.Where(c => search.filter.Matches(c.presetName)).ToList();
            Rect view = new Rect(0f, 0f, list.width - 16f, shown.Count * (RowH + 4f));
            Widgets.BeginScrollView(list, ref scroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                ColonyPreset c = shown[i];
                Rect row = new Rect(0f, i * (RowH + 4f), view.width, RowH);
                if (row.yMax < scroll.y || row.y > scroll.y + list.height)
                {
                    continue;
                }
                if (Event.current.type == EventType.Repaint)
                {
                    Widgets.DrawOptionBackground(row, c == chosenColony);
                    DrawPicture(new Rect(row.x + 4f, row.y + 2f, 96f, RowH - 4f), c.filePath);
                    ListRowText(row, row.x + 106f, c.presetName, c.Summary);
                }
                if (Widgets.ButtonInvisible(row))
                {
                    chosenColony = c;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }
            Widgets.EndScrollView();

            Widgets.DrawMenuSection(detail);
            if (chosenColony == null)
            {
                return;
            }
            Rect d = detail.ContractedBy(12f);
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(d.x, d.y, d.width, 32f), chosenColony.presetName.Truncate(d.width));
            }
            if (Event.current.type == EventType.Repaint)
            {
                Rect pic = new Rect(d.x, d.y + 38f, d.width, 96f);
                DrawPicture(pic, chosenColony.filePath);
                string text = chosenColony.colonists.Select(p => "- " + (p.nick.NullOrEmpty() ? p.first ?? p.presetName : p.nick) + ": " + p.Summary).ToLineList();
                if (chosenColony.pets.Any())
                {
                    text += "\n\n" + "CAC_Pets".Translate() + ": " + chosenColony.pets.Select(p => (p.name ?? p.kind.label) + " (" + p.kind.label + ")").ToCommaList();
                }
                if (chosenColony.supplies.Any())
                {
                    text += "\n\n" + "CAC_ColonySupplies".Translate() + ": " + chosenColony.supplies.Count;
                }
                Widgets.Label(new Rect(d.x, pic.yMax + 8f, d.width, d.yMax - pic.yMax - 8f - 2 * 44f), text);
            }

            float by = d.yMax - 38f;
            ColonyPreset current = chosenColony;
            ManageButtons(new Rect(d.x, by - 44f, d.width, 38f),
                () => Find.WindowStack.Add(Dialog_SavePreset.ForRename(current.presetName, current.filePath, ColonyPreset.PathFor,
                    n => "CAC_OverwriteColony".Translate(n), n =>
                    {
                        current.RenameTo(n);
                        Reload(null, current.filePath);
                    })),
                () =>
                {
                    ColonyPreset copy = current.Duplicate();
                    Reload(null, copy?.filePath ?? current.filePath);
                    if (copy != null)
                    {
                        Messages.Message("CAC_Duplicated".Translate(copy.presetName), MessageTypeDefOf.TaskCompletion, historical: false);
                    }
                });
            if (Widgets.ButtonText(new Rect(d.x, by, d.width * 0.6f, 38f), "CAC_LoadColony".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CAC_LoadColonyConfirm".Translate(current.presetName), () =>
                {
                    page.LoadColony(current);
                    Close();
                }, destructive: true));
            }
            if (Widgets.ButtonText(new Rect(d.x + d.width * 0.6f + 6f, by, d.width * 0.4f - 6f, 38f), "Delete".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CAC_DeletePreset".Translate(current.presetName), () =>
                {
                    pictures.Forget(current.filePath);
                    current.Delete();
                    colonies.Remove(current);
                    chosenColony = colonies.FirstOrDefault();
                }, destructive: true));
            }
        }
    }
}

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
        private int inventorySub;
        private readonly Dictionary<object, string> countBuffers = new Dictionary<object, string>();

        private static List<ThingDef> weaponDefs;
        private static List<ThingDef> itemDefs;
        private static List<ThingDef> supplyDefs;

        private static List<ThingDef> WeaponDefs => weaponDefs ??= DefDatabase<ThingDef>.AllDefs
            .Where(d => d.equipmentType == EquipmentType.Primary && d.category == ThingCategory.Item && !d.destroyOnDrop && !d.IsCorpse)
            .OrderBy(d => d.label).ToList();

        // Everything a colonist can carry, spare weapons and clothes included (those pick a material and quality on
        // their row once added, like worn clothes). Not quest items the game won't let be destroyed (removing one errors).
        private static List<ThingDef> ItemDefs => itemDefs ??= DefDatabase<ThingDef>.AllDefs
            .Where(d => d.category == ThingCategory.Item && d.EverHaulable && !d.destroyOnDrop && !d.IsCorpse && !d.isUnfinishedThing
                && d.destroyable && d.BaseMarketValue > 0f)
            .OrderBy(d => d.label).ToList();

        // Same list the scenario editor offers for "start with".
        private static List<ThingDef> SupplyDefs => supplyDefs ??= DefDatabase<ThingDef>.AllDefs
            .Where(d => (d.category == ThingCategory.Item && d.scatterableOnMapGen && !d.destroyOnDrop) || (d.category == ThingCategory.Building && d.Minifiable))
            .OrderBy(d => d.label).ToList();

        private void DrawInventory(ref float y, float w)
        {
            if (inventorySub == 0)
            {
                DrawCarried(ref y, w, selected);
            }
            else
            {
                DrawSupplies(ref y, w);
            }
        }

        // ---------------------------------------------------------------- carried by this colonist

        private static void DrawWeapon(ref float y, float w, Pawn pawn)
        {
            CACUI.Section(ref y, w, "CAC_Weapon".Translate());
            ThingWithComps weapon = pawn.equipment.Primary;
            if (weapon != null)
            {
                ThingEditRow(new Rect(0f, y, w, 34f), weapon,
                    (stuff, quality) => PawnEditor.SetWeapon(pawn, weapon.def, stuff, quality),
                    () => PawnEditor.SetWeapon(pawn, null));
                y += 38f;
            }
            else
            {
                CACUI.Note(ref y, w, "CAC_Unarmed".Translate());
            }
            if (Widgets.ButtonText(new Rect(0f, y, 180f, 28f), weapon != null ? "CAC_ChangeWeapon".Translate() : "CAC_ChooseWeapon".Translate()))
            {
                List<PickerOption> options = WeaponDefs.Select(d => new PickerOption
                {
                    label = d.LabelCap,
                    description = d.description,
                    selected = weapon?.def == d,
                    mod = d.modContentPack,
                    drawIcon = r => Widgets.ThingIcon(r, d, GenStuff.DefaultStuffFor(d)),
                    onPick = () => PawnEditor.SetWeapon(pawn, d)
                }).ToList();
                Find.WindowStack.Add(new Dialog_CACPicker("CAC_ChooseWeapon".Translate(), options));
            }
            y += 36f;
        }

        private void DrawCarried(ref float y, float w, Pawn pawn)
        {
            if (!pawn.DevelopmentalStage.Baby())
            {
                DrawWeapon(ref y, w, pawn);
            }

            CACUI.Section(ref y, w, "CAC_CarriedItems".Translate());
            if (!MassUtility.CanEverCarryAnything(pawn))
            {
                CACUI.Note(ref y, w, "CAC_CantCarry".Translate(pawn.LabelShort));
                return;
            }
            CACUI.Note(ref y, w, "CAC_CarriedNote".Translate());
            DrawWeightBar(ref y, w, pawn);
            foreach (Thing thing in pawn.inventory.innerContainer.ToList())
            {
                Thing t = thing;
                if (PawnEditor.HasVariants(t))
                {
                    ThingEditRow(new Rect(0f, y, w, 34f), t,
                        (stuff, quality) => PawnEditor.ReplaceInventoryThing(pawn, t, stuff, quality),
                        () => PawnEditor.RemoveInventoryThing(pawn, t));
                    y += 36f;
                    continue;
                }
                float unit = t.GetStatValue(StatDefOf.Mass);
                string label = t.LabelCapNoCount + "  (" + (unit * t.stackCount).ToStringMass() + ")";
                CountRow(ref y, w, t, t.def, t.Stuff, label, t.stackCount, n =>
                {
                    if (n <= 0)
                    {
                        pawn.inventory.innerContainer.Remove(t);
                        t.Destroy();
                        return 0;
                    }
                    // Never past what the colonist can carry, or the item's stack size.
                    int most = Mathf.Min(t.def.stackLimit, t.stackCount + Mathf.Min(PawnEditor.CountThatFits(pawn, unit), t.def.stackLimit));
                    if (n > most && n > t.stackCount)
                    {
                        Messages.Message("CAC_NoRoom".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput, historical: false);
                    }
                    t.stackCount = Mathf.Min(n, Mathf.Max(most, t.stackCount));
                    return t.stackCount;
                });
            }
            if (Widgets.ButtonText(new Rect(0f, y, 180f, 28f), "CAC_AddItem".Translate()))
            {
                OpenItemPicker("CAC_AddItem".Translate(), ItemDefs, d =>
                {
                    if (PawnEditor.AddInventory(pawn, d, DefaultCount(d)) == 0)
                    {
                        Messages.Message("CAC_NoRoom".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput, historical: false);
                    }
                }, d => PawnEditor.CountThatFits(pawn, PawnEditor.UnitMass(d)) < 1
                    ? (string)"CAC_TooHeavyToAdd".Translate(pawn.LabelShort, MassUtility.FreeSpace(pawn).ToStringMass())
                    : null);
            }
            y += 36f;

            if (Find.GameInitData.startingPossessions.TryGetValue(pawn, out List<ThingDefCount> possessions) && possessions.Any())
            {
                // Possessions that don't fit (or furniture) still arrive next to the colonist, as in vanilla.
                CACUI.Note(ref y, w, "CAC_ArrivesNextTo".Translate(pawn.LabelShort));
                foreach (ThingDefCount p in possessions.ToList())
                {
                    Rect row = new Rect(0f, y, w, 30f);
                    Widgets.DrawHighlightIfMouseover(row);
                    Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 2f, 26f, 26f), p.ThingDef);
                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(new Rect(row.x + 34f, row.y, row.width - 64f, row.height), p.LabelCap);
                    }
                    if (Widgets.ButtonImage(new Rect(row.xMax - 26f, row.y + 4f, 22f, 22f), CACTex.Dismiss))
                    {
                        possessions.Remove(p);
                    }
                    y += 32f;
                }
            }
        }

        /// <summary>"Carrying 18 kg of 35 kg", in red when over (heavier clothes or a weapon can do that).</summary>
        private static void DrawWeightBar(ref float y, float w, Pawn pawn)
        {
            float mass = PawnEditor.CarriedMass(pawn);
            float capacity = PawnEditor.CarryCapacity(pawn);
            bool over = PawnEditor.OverCarryLimit(pawn);
            Rect bar = new Rect(0f, y, w, 24f);
            Widgets.FillableBar(bar, capacity > 0f ? Mathf.Clamp01(mass / capacity) : 1f, over ? CACUI.OverBarFill : CACUI.WeightBarFill);
            using (new TextBlock(TextAnchor.MiddleCenter, over ? ColorLibrary.RedReadable : Color.white))
            {
                Widgets.Label(bar, "CAC_Weight".Translate(mass.ToStringMass(), capacity.ToStringMass()));
            }
            if (Mouse.IsOver(bar))
            {
                TooltipHandler.TipRegion(bar, "CAC_WeightTip".Translate(MassUtility.GearMass(pawn).ToStringMass(), MassUtility.InventoryMass(pawn).ToStringMass()));
            }
            y += 28f;
            if (over)
            {
                using (new TextBlock(GameFont.Tiny, ColorLibrary.RedReadable))
                {
                    string text = "CAC_OverWeight".Translate(pawn.LabelShort, (mass - capacity).ToStringMass());
                    float h = Text.CalcHeight(text, w);
                    Widgets.Label(new Rect(0f, y, w, h), text);
                    y += h + 4f;
                }
            }
        }

        private static int DefaultCount(ThingDef def)
        {
            if (def.stackLimit <= 1)
            {
                return 1;
            }
            if (def.IsMedicine || def.IsDrug)
            {
                return Mathf.Min(5, def.stackLimit);
            }
            if (def.IsIngestible)
            {
                return Mathf.Min(10, def.stackLimit);
            }
            return Mathf.Min(def == ThingDefOf.Silver ? 100 : 25, def.stackLimit);
        }

        // ---------------------------------------------------------------- colony supplies (scenario)

        private void DrawSupplies(ref float y, float w)
        {
            CACUI.Note(ref y, w, "CAC_SuppliesNote".Translate(Find.Scenario.name));
            SupplyList(ref y, w, false, "CAC_ArrivesWithColonists".Translate());
            SupplyList(ref y, w, true, "CAC_ScatteredNearby".Translate());
            y += 6f;
            if (Widgets.ButtonText(new Rect(0f, y, CACUI.FitWidth("CAC_ResetSupplies".Translate(), 220f), 30f), "CAC_ResetSupplies".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CAC_ResetSuppliesConfirm".Translate(), ResetSupplies));
            }
            y += 36f;
        }

        private void ResetSupplies()
        {
            state.supplies.Clear();
            state.supplies.AddRange(CreatorState.ReadSupplies(state.originalScenario));
            countBuffers.Clear();
        }

        private void SupplyList(ref float y, float w, bool scattered, string title)
        {
            CACUI.Section(ref y, w, title);
            List<SupplyEntry> entries = state.supplies.Where(s => s.scattered == scattered).ToList();
            if (!entries.Any())
            {
                CACUI.Note(ref y, w, "CAC_None".Translate());
            }
            foreach (SupplyEntry entry in entries)
            {
                SupplyEntry e = entry;
                Rect row = new Rect(0f, y, w, 32f);
                if (e.def.MadeFromStuff)
                {
                    Rect stuffBut = new Rect(row.xMax - 26f - 8f - 150f - 8f - 110f, row.y + 2f, 110f, 28f);
                    ThingDef stuff = e.stuff ?? GenStuff.DefaultStuffFor(e.def);
                    if (Widgets.ButtonText(stuffBut, stuff?.LabelAsStuff.CapitalizeFirst() ?? "-"))
                    {
                        CACUI.FloatMenu(GenStuff.AllowedStuffsFor(e.def).OrderBy(st => st.label)
                            .Select(st => new FloatMenuOption(st.LabelAsStuff.CapitalizeFirst(), () => e.stuff = st, st)));
                    }
                }
                CountRow(ref y, w, e, e.def, e.stuff, e.def.LabelCap, e.count, n =>
                {
                    if (n <= 0)
                    {
                        state.supplies.Remove(e);
                        return 0;
                    }
                    e.count = n;
                    return n;
                }, reserveRight: e.def.MadeFromStuff ? 118f : 0f);
            }
            if (Widgets.ButtonText(new Rect(0f, y, 200f, 28f), "CAC_AddSupply".Translate()))
            {
                OpenItemPicker("CAC_AddSupply".Translate(), SupplyDefs, d => state.supplies.Add(new SupplyEntry
                {
                    def = d,
                    stuff = d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null,
                    count = DefaultCount(d),
                    scattered = scattered
                }));
            }
            y += 36f;
        }

        /// <summary>
        /// Icon, label, and a "- [count] +" editor. Shift = x10, Ctrl = x100. setCount returns the count that was
        /// actually kept (a limit can lower it), and the number box always shows that.
        /// </summary>
        private void CountRow(ref float y, float w, object key, ThingDef def, ThingDef stuff, string label, int count, System.Func<int, int> setCount, float reserveRight = 0f)
        {
            Rect row = new Rect(0f, y, w, 32f);
            Widgets.DrawHighlightIfMouseover(row);
            Rect icon = new Rect(row.x + 2f, row.y + 3f, 26f, 26f);
            if (key is Thing thing)
            {
                Widgets.ThingIcon(icon, thing);
                InfoCardOnClick(icon, () => new Dialog_InfoCard(thing));
            }
            else
            {
                Widgets.ThingIcon(icon, def, stuff);
                InfoCardOnClick(icon, () => new Dialog_InfoCard(def, stuff));
            }
            Rect del = new Rect(row.xMax - 26f, row.y + 5f, 22f, 22f);
            Rect plus = new Rect(del.x - 8f - 28f, row.y + 3f, 26f, 26f);
            Rect field = new Rect(plus.x - 70f, row.y + 3f, 66f, 26f);
            Rect minus = new Rect(field.x - 30f, row.y + 3f, 26f, 26f);
            Rect labelRect = new Rect(row.x + 34f, row.y, minus.x - row.x - 38f - reserveRight, row.height);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(labelRect, label.Truncate(labelRect.width));
            }
            if (Mouse.IsOver(labelRect))
            {
                TooltipHandler.TipRegion(labelRect, def.LabelCap + "\n\n" + def.description);
            }
            int step = GenUI.CurrentAdjustmentMultiplier();
            int clicked = count;
            if (Widgets.ButtonImage(minus, CACTex.Minus))
            {
                clicked = count - step;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            if (Widgets.ButtonImage(plus, CACTex.Plus))
            {
                clicked = count + step;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            if (clicked != count)
            {
                count = setCount(clicked);
                // The number box must show the new count straight away. Left alone it would keep the number from
                // before the click and push it back, so the display lagged one click behind.
                countBuffers[key] = Mathf.Max(count, 0).ToString();
                UI.UnfocusCurrentControl();
            }
            countBuffers.TryGetValue(key, out string buffer);
            int value = count;
            Widgets.TextFieldNumeric(field, ref value, ref buffer, 0f, 100000f);
            countBuffers[key] = buffer;
            if (value != count && value > 0)
            {
                int kept = setCount(value);
                if (kept != value)
                {
                    // Typed more than allowed: show what was kept rather than the typed number.
                    countBuffers[key] = kept.ToString();
                    UI.UnfocusCurrentControl();
                }
            }
            if (Widgets.ButtonImage(del, CACTex.Dismiss))
            {
                setCount(0);
                countBuffers.Remove(key);
            }
            y += 34f;
        }

        private static void OpenItemPicker(string title, List<ThingDef> defs, System.Action<ThingDef> onPick, System.Func<ThingDef, string> blockedBy = null)
        {
            List<PickerOption> options = defs.Select(d => new PickerOption
            {
                label = d.LabelCap,
                describe = () => d.description + "\n\n" + "Mass".Translate().CapitalizeFirst() + ": " + PawnEditor.UnitMass(d).ToStringMass(),
                extraSearch = d.FirstThingCategory?.label,
                mod = d.modContentPack,
                drawIcon = r => Widgets.ThingIcon(r, d, GenStuff.DefaultStuffFor(d)),
                disabledCheck = blockedBy == null ? null : (System.Func<string>)(() => blockedBy(d)),
                onPick = () => onPick(d)
            }).ToList();
            Find.WindowStack.Add(new Dialog_CACPicker(title, options, closeOnPick: false));
        }
    }
}

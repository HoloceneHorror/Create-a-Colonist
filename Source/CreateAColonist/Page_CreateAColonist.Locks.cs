using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CreateAColonist
{
    /// <summary>
    /// Padlocks on the tabs: a locked tab's part of the colonist is kept when "Randomize colonist" rolls a new one.
    /// The old colonist is saved the way the gallery saves colonists, the locked parts are copied over the new
    /// colonist's own, and the result is loaded onto the new colonist.
    /// </summary>
    public partial class Page_CreateAColonist
    {
        private readonly HashSet<CreatorTab> lockedTabs = new HashSet<CreatorTab>();

        private static bool Lockable(CreatorTab t) => t != CreatorTab.Relationships && t != CreatorTab.Animal;

        private void LockButton(Rect rect)
        {
            if (!Lockable(tab))
            {
                return;
            }
            bool locked = lockedTabs.Contains(tab);
            TooltipHandler.TipRegion(rect, (locked ? "CAC_UnlockTab" : "CAC_LockTab").Translate("CAC_RandomColonist".Translate()));
            if (Widgets.ButtonImage(rect, CACTex.Lock, locked ? Color.white : new Color(1f, 1f, 1f, 0.25f), GenUI.MouseoverColor))
            {
                if (!lockedTabs.Remove(tab))
                {
                    lockedTabs.Add(tab);
                }
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }

        /// <summary>After a re-roll: puts the locked parts of the old colonist back on the new one.</summary>
        private void KeepLocked(ColonistPreset old, Pawn fresh)
        {
            if (lockedTabs.Count == 0)
            {
                return;
            }
            ColonistPreset p = ColonistPreset.From(fresh, null, withFamily: false);
            bool Has(CreatorTab t) => lockedTabs.Contains(t);
            if (Has(CreatorTab.Personality) || Has(CreatorTab.Appearance))
            {
                // Backstories need the age they were picked for (an adult one needs 20+), and age shows.
                p.age = old.age;
                p.ageChronological = old.ageChronological;
            }
            if (Has(CreatorTab.Personality))
            {
                p.childhood = old.childhood;
                p.adulthood = old.adulthood;
                p.traits = old.traits;
                p.passions = old.passions;
                p.manualSkills = old.manualSkills;
            }
            if (Has(CreatorTab.Appearance))
            {
                p.gender = old.gender;
                p.bodyType = old.bodyType;
                p.headType = old.headType;
                p.hair = old.hair;
                p.beard = old.beard;
                p.faceTattoo = old.faceTattoo;
                p.bodyTattoo = old.bodyTattoo;
                p.hairColor = old.hairColor;
                p.hasSkinOverride = old.hasSkinOverride;
                p.skinOverride = old.skinOverride;
                p.hasSkinBase = old.hasSkinBase;
                p.skinBase = old.skinBase;
                p.melaninGene = old.melaninGene;
                p.hairColorGene = old.hairColorGene;
                p.favoriteColor = old.favoriteColor;
                p.facialAnimation = old.facialAnimation;
            }
            if (Has(CreatorTab.Genes))
            {
                p.xenotype = old.xenotype;
                p.customXenotypeName = old.customXenotypeName;
                p.customXenotypeIcon = old.customXenotypeIcon;
                p.customXenotypeInheritable = old.customXenotypeInheritable;
                p.customGenes = old.customGenes;
                p.eyeChoiceSaved = old.eyeChoiceSaved;
                p.eyeGene = old.eyeGene;
                p.endogenes = old.endogenes;
                p.xenogenes = old.xenogenes;
                p.geneTraits = old.geneTraits;
            }
            if (Has(CreatorTab.Health))
            {
                p.health = old.health;
            }
            if (Has(CreatorTab.Clothing))
            {
                p.apparel = old.apparel;
                p.weapon = old.weapon;
            }
            if (Has(CreatorTab.Inventory))
            {
                p.inventory = old.inventory;
                p.possessions = old.possessions;
            }
            if (Has(CreatorTab.Ideoligion))
            {
                p.ideoName = old.ideoName;
                p.ideoFile = old.ideoFile;
                p.ideoCertainty = old.ideoCertainty;
            }
            p.ApplyTo(fresh, withFamily: false);
        }
    }
}

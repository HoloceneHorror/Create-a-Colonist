using UnityEngine;
using Verse;

namespace CreateAColonist
{
    [StaticConstructorOnStartup]
    public static class CACTex
    {
        private const string Root = "UI/CreateAColonist/";

        public static readonly Texture2D TabPersonality = ContentFinder<Texture2D>.Get(Root + "TabPersonality");
        public static readonly Texture2D TabAppearance = ContentFinder<Texture2D>.Get(Root + "TabAppearance");
        public static readonly Texture2D TabGenes = ContentFinder<Texture2D>.Get(Root + "TabGenes");
        public static readonly Texture2D TabClothing = ContentFinder<Texture2D>.Get(Root + "TabClothing");
        public static readonly Texture2D TabInventory = ContentFinder<Texture2D>.Get(Root + "TabInventory");
        public static readonly Texture2D TabRelationships = ContentFinder<Texture2D>.Get(Root + "TabRelationships");
        public static readonly Texture2D TabAnimal = ContentFinder<Texture2D>.Get(Root + "TabAnimal");
        public static readonly Texture2D TabHealth = ContentFinder<Texture2D>.Get(Root + "TabHealth");
        public static readonly Texture2D TabIdeoligion = ContentFinder<Texture2D>.Get(Root + "TabIdeoligion");

        public static readonly Texture2D Dice = ContentFinder<Texture2D>.Get(Root + "Dice");
        public static readonly Texture2D Play = ContentFinder<Texture2D>.Get(Root + "Play");
        public static readonly Texture2D Gallery = ContentFinder<Texture2D>.Get(Root + "Gallery");
        public static readonly Texture2D SaveColonist = ContentFinder<Texture2D>.Get(Root + "SaveColonist");
        public static readonly Texture2D ModeNormal = ContentFinder<Texture2D>.Get(Root + "ModeNormal");
        public static readonly Texture2D ModeCreator = ContentFinder<Texture2D>.Get(Root + "ModeCreator");
        public static readonly Texture2D Colonist = ContentFinder<Texture2D>.Get(Root + "Colonist");
        public static readonly Texture2D Pet = ContentFinder<Texture2D>.Get(Root + "Pet");
        public static readonly Texture2D Shadow = ContentFinder<Texture2D>.Get(Root + "Shadow");
        public static readonly Texture2D Undo = ContentFinder<Texture2D>.Get(Root + "Undo");
        public static readonly Texture2D Redo = ContentFinder<Texture2D>.Get(Root + "Redo");
        public static readonly Texture2D ArrowUp = ContentFinder<Texture2D>.Get(Root + "ArrowUp");
        public static readonly Texture2D ArrowDown = ContentFinder<Texture2D>.Get(Root + "ArrowDown");
        public static readonly Texture2D CloseX = ContentFinder<Texture2D>.Get("UI/Widgets/CloseXSmall");

        public static readonly Texture2D Plus = ContentFinder<Texture2D>.Get("UI/Buttons/Plus");
        public static readonly Texture2D Minus = ContentFinder<Texture2D>.Get("UI/Buttons/Minus");
        public static readonly Texture2D Dismiss = ContentFinder<Texture2D>.Get("UI/Buttons/Dismiss");
        public static readonly Texture2D RotLeft = ContentFinder<Texture2D>.Get("UI/Widgets/RotLeft");
        public static readonly Texture2D RotRight = ContentFinder<Texture2D>.Get("UI/Widgets/RotRight");
        public static readonly Texture2D PassionMinor = ContentFinder<Texture2D>.Get("UI/Icons/PassionMinor");
        public static readonly Texture2D PassionMajor = ContentFinder<Texture2D>.Get("UI/Icons/PassionMajor");
        public static readonly Texture2D PassionMinorGray = ContentFinder<Texture2D>.Get("UI/Icons/PassionMinorGray");
        public static readonly Texture2D Male = ContentFinder<Texture2D>.Get("UI/Icons/Gender/Male");
        public static readonly Texture2D Female = ContentFinder<Texture2D>.Get("UI/Icons/Gender/Female");
        public static readonly Texture2D Bond = ContentFinder<Texture2D>.Get("UI/Icons/Animal/Bond");
        public static readonly Texture2D Lock = ContentFinder<Texture2D>.Get("UI/Overlays/Locked");
    }
}

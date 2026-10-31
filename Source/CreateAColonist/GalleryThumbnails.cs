using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CreateAColonist
{
    /// <summary>
    /// Small portraits kept next to gallery files (Name.png beside Name.xml): one colonist, or a colony's colonists
    /// side by side. Made when something is saved; older saves get one the first time they're loaded.
    /// </summary>
    public static class GalleryThumbnails
    {
        private const int Width = 96;
        private const int Height = 120;
        private const int GroupWidth = 72;
        private const int GroupHeight = 90;
        private const float Zoom = 1.6f;

        public static string PathFor(string xmlPath)
        {
            return Path.ChangeExtension(xmlPath, ".png");
        }

        public static bool Exists(string xmlPath)
        {
            return xmlPath != null && File.Exists(PathFor(xmlPath));
        }

        public static void SaveColonist(Pawn pawn, string xmlPath)
        {
            Write(xmlPath, () => Render(pawn, Width, Height));
        }

        /// <summary>Up to six colonists side by side.</summary>
        public static void SaveGroup(List<Pawn> pawns, string xmlPath)
        {
            Write(xmlPath, () =>
            {
                List<Pawn> shown = pawns.Take(6).ToList();
                Texture2D group = new Texture2D(GroupWidth * shown.Count, GroupHeight, TextureFormat.ARGB32, false);
                group.SetPixels32(new Color32[group.width * group.height]);
                for (int i = 0; i < shown.Count; i++)
                {
                    Texture2D one = Render(shown[i], GroupWidth, GroupHeight);
                    group.SetPixels32(i * GroupWidth, 0, GroupWidth, GroupHeight, one.GetPixels32());
                    UnityEngine.Object.Destroy(one);
                }
                group.Apply();
                return group;
            });
        }

        private static Texture2D Render(Pawn pawn, int w, int h)
        {
            // Zoomed in: the game's portrait leaves a lot of empty space around the colonist, too much for a small picture.
            RenderTexture rt = PortraitsCache.Get(pawn, new Vector2(w, h), Rot4.South, new Vector3(0f, 0f, 0.1f), Zoom, supersample: false, compensateForUIScale: false,
                renderHeadgear: true, renderClothes: true);
            return rt.CreateTexture2D();
        }

        private static void Write(string xmlPath, Func<Texture2D> make)
        {
            if (xmlPath == null)
            {
                return;
            }
            // Only ever next to a gallery file (the Colonies folder sits inside the gallery folder).
            string gallery = Path.GetFullPath(ColonistPreset.Folder) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(PathFor(xmlPath)).StartsWith(gallery, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                Texture2D tex = make();
                File.WriteAllBytes(PathFor(xmlPath), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e)
            {
                // A missing picture is harmless; the gallery shows a plain silhouette instead.
                Log.Warning("[Create a Colonist] Could not save a gallery picture: " + e.Message);
            }
        }

        /// <summary>Loaded pictures, kept while the gallery is open.</summary>
        public class Cache
        {
            private readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>();

            public Texture2D Get(string xmlPath)
            {
                if (xmlPath == null)
                {
                    return null;
                }
                string path = PathFor(xmlPath);
                if (!loaded.TryGetValue(path, out Texture2D tex))
                {
                    tex = null;
                    if (File.Exists(path) && new FileInfo(path).Length < 2 * 1024 * 1024)
                    {
                        tex = new Texture2D(0, 0);
                        if (!tex.LoadImage(File.ReadAllBytes(path)))
                        {
                            UnityEngine.Object.Destroy(tex);
                            tex = null;
                        }
                    }
                    loaded[path] = tex;
                }
                return tex;
            }

            public void Forget(string xmlPath)
            {
                if (xmlPath != null && loaded.TryGetValue(PathFor(xmlPath), out Texture2D tex))
                {
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                    loaded.Remove(PathFor(xmlPath));
                }
            }

            public void Clear()
            {
                foreach (Texture2D tex in loaded.Values)
                {
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                }
                loaded.Clear();
            }
        }
    }
}

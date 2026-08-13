using System.IO;
using UnityEditor;
using UnityEngine;
using Undertown.Core.Buildings;
using Undertown.Core.World;
using Undertown.Game.Presentation;

namespace Undertown.Editor
{
    /// <summary>
    /// Writes every generated sprite to disk as a PNG.
    ///
    /// All the art here is produced in code, which means a mistake in a drawing routine and a
    /// mistake in how the drawing is placed on screen look identical in a screenshot. Being
    /// able to look at a sprite on its own settles which of the two it is in one step instead
    /// of several rounds of guessing.
    /// </summary>
    public static class ArtDump
    {
        public static void Dump()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "..", "artifacts", "art");
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);

            foreach (TileKind kind in System.Enum.GetValues(typeof(TileKind)))
            {
                Write(root, $"tile_{kind}", IsoTileArt.TileFor(kind, 0).sprite);
                if (IsoPropArt.HasProp(kind))
                    for (int v = 0; v < 4; v++)
                        Write(root, $"prop_{kind}_{v}", IsoPropArt.For(kind, v));
            }

            foreach (BuildingKind kind in System.Enum.GetValues(typeof(BuildingKind)))
                Write(root, $"building_{kind}", IsoBuildingArt.For(kind));

            Write(root, "agent_townsfolk", IsoAgentArt.Person(
                new Color32(0x6E, 0x5A, 0x3E, 0xFF), new Color32(0x6E, 0x5A, 0x3E, 0xFF)));
            Write(root, "agent_inspector", IsoAgentArt.Person(
                new Color32(0x2E, 0x38, 0x54, 0xFF), new Color32(0xF0, 0xE0, 0xC0, 0xFF), tall: true));

            Debug.Log($"[ARTDUMP] wrote sprites to {root}");
        }

        private static void Write(string root, string name, Sprite sprite)
        {
            if (sprite == null) return;
            var texture = sprite.texture;
            File.WriteAllBytes(Path.Combine(root, name + ".png"), texture.EncodeToPNG());
            Debug.Log($"[ARTDUMP] {name} {texture.width}x{texture.height} pivot={sprite.pivot}");
        }
    }
}

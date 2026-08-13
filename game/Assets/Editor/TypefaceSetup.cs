using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Monster.EditorTools
{
    /// <summary>Builds the booth's typeface as a TextMeshPro font asset.
    ///
    /// The booth is entirely monospaced on purpose. Every printed field is a padded label
    /// followed by a value, so the values only line up into a readable column in a fixed
    /// pitch face, and the portrait grids are drawn out of block characters that have to
    /// be square. A proportional font would break both.
    ///
    /// The atlas is baked statically rather than populated at runtime, because a dynamic
    /// atlas has to carry the source font into the build and rasterise glyphs on first
    /// use, which on a machine with no GPU shows up as a visible hitch the first time a
    /// document appears.</summary>
    public static class TypefaceSetup
    {
        public const string FontAssetPath = "Assets/Settings/BoothMono SDF.asset";
        private const string SourceFontPath = "Assets/Fonts/DejaVuSansMono.ttf";

        /// <summary>Printable ASCII, plus the two block characters the portrait grids are
        /// drawn with and the box-drawing rules used for separators.</summary>
        private const string CharacterSet =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~" +
            "\u2588\u2591\u2500\u2502\u2026\u00b0";

        [MenuItem("MONSTER/Setup/Build Typeface")]
        public static TMP_FontAsset Build()
        {
            MonsterSetup.EnsureFolder(MonsterSetup.SettingsFolder);

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                Debug.LogError($"[Typeface] source font missing at {SourceFontPath}");
                return null;
            }

            var asset = TMP_FontAsset.CreateFontAsset(source, 72, 8, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic);
            if (asset == null)
            {
                Debug.LogError("[Typeface] TMP_FontAsset.CreateFontAsset returned null");
                return null;
            }

            asset.name = "BoothMono SDF";
            AssetDatabase.CreateAsset(asset, FontAssetPath);

            if (!asset.TryAddCharacters(CharacterSet, out var missing))
            {
                Debug.LogWarning($"[Typeface] could not rasterise: {missing}");
            }

            // Baked, so nothing is rasterised at runtime.
            asset.atlasPopulationMode = AtlasPopulationMode.Static;

            // Same trap as the post-process profile: the atlas texture and the material are
            // separate objects, and if they are not attached to the asset file they are
            // dropped on serialisation and the font renders as nothing at all.
            foreach (var texture in asset.atlasTextures.Where(t => t != null))
            {
                texture.name = asset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(texture, asset);
            }

            if (asset.material != null)
            {
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);

            var written = AssetDatabase.LoadAllAssetsAtPath(FontAssetPath);
            var hasTexture = written.OfType<Texture>().Any();
            var hasMaterial = written.OfType<Material>().Any();
            var glyphs = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath)?.characterTable.Count ?? 0;

            if (!hasTexture || !hasMaterial || glyphs < 90)
            {
                Debug.LogError($"[Typeface] font asset is incomplete: texture={hasTexture} " +
                               $"material={hasMaterial} glyphs={glyphs}");
            }
            else
            {
                Debug.Log($"[Typeface] built {asset.name} with {glyphs} glyphs, atlas and material attached");
            }

            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        }
    }
}

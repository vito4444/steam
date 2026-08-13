using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Decoder.EditorTools
{
    /// <summary>
    /// 把程序化贴图烘焙成资源，并装配成可直接使用的材质。
    ///
    /// 画面自检显示细节密度只有目标参考图的四分之一（0.048 对 0.195），
    /// 根因是场景里全是纯色图元：没有法线起伏、没有粗糙度变化、没有磨损和污渍，
    /// 于是每个面都是一片均匀的色块，梯度上不去。加几何解决不了这个问题，
    /// 得靠材质在每个面内部制造可读的结构。
    ///
    /// 每种材质按它真实的成因来配方，而不是随便叠噪声：
    /// 拉丝钢的纹理来自磨削方向，掉漆总是先从边角开始，混凝土的气孔是浇筑时的气泡。
    /// 按成因做出来的纹理，即使分辨率不高也"像那么回事"。
    /// </summary>
    public static class ProceduralMaterialBaker
    {
        private const string TextureDir = "Assets/Textures/Procedural";
        private const string MaterialDir = "Assets/Materials/Procedural";
        private const int Size = 512;

        private struct Maps
        {
            public Color[] Albedo;
            public Color[] MetallicSmoothness;
            public float[] Height;
            public float NormalStrength;
            public Color EmissionTint;
            public float EmissionStrength;
        }

        public static void BakeAll()
        {
            try
            {
                Directory.CreateDirectory(TextureDir);
                Directory.CreateDirectory(MaterialDir);

                var recipes = new Dictionary<string, Func<Maps>>
                {
                    ["SteelPanel"] = BakeBrushedSteel,
                    ["OlivePaint"] = BakeWornOlivePaint,
                    ["Concrete"] = BakeConcrete,
                    ["Bakelite"] = BakeBakelite,
                    ["Paper"] = BakeAgedPaper,
                    ["Brass"] = BakeBrass,
                };

                foreach (var recipe in recipes)
                {
                    Bake(recipe.Key, recipe.Value());
                    Log($"已烘焙 {recipe.Key}");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                Log($"MATERIALS_BAKED {recipes.Count}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProceduralMaterialBaker] 烘焙失败: {e}");
                Console.Error.WriteLine($"[ProceduralMaterialBaker] 烘焙失败: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ---------- 拉丝钢：设备面板 ----------

        private static Maps BakeBrushedSteel()
        {
            var albedo = new Color[Size * Size];
            var mask = new Color[Size * Size];
            var height = new float[Size * Size];
            const int seed = 1101;

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = x / (float)Size;
                    var v = y / (float)Size;
                    var i = y * Size + x;

                    // 拉丝：磨削方向上频率极低，垂直方向上频率极高。
                    // 这个各向异性就是拉丝金属看上去有方向感的全部原因。
                    var brush = ProceduralTexture.Fbm(u, v, 9, 38, 2, 0.45f, seed);

                    // 稀疏长划痕：脊状噪声在同一方向拉长，形成偶发的深痕。
                    var scratch = ProceduralTexture.Ridged(u, v, 6, 62, 3, seed + 31);
                    scratch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.93f, 1f, scratch));

                    // 大尺度污渍：靠近底部更脏，模拟手汗和积灰。
                    var grime = ProceduralTexture.Fbm(u, v, 3, 3, 4, 0.6f, seed + 77);
                    grime = Mathf.Lerp(grime, grime * 1.35f, 1f - v);

                    var h = brush * 0.22f + scratch * 0.16f + grime * 0.10f;
                    height[i] = h;

                    var baseTone = Mathf.Lerp(0.170f, 0.205f, brush);
                    baseTone *= Mathf.Lerp(1f, 0.78f, grime * 0.6f);
                    baseTone *= Mathf.Lerp(1f, 0.62f, scratch);

                    albedo[i] = new Color(baseTone * 1.02f, baseTone, baseTone * 0.96f, 1f);

                    // 划痕处金属裸露、更粗糙；污渍处光泽下降。
                    var metallic = Mathf.Lerp(0.72f, 0.86f, scratch);
                    var smoothness = Mathf.Lerp(0.62f, 0.34f, grime) + brush * 0.10f - scratch * 0.22f;
                    mask[i] = ProceduralTexture.MetallicSmoothness(
                        metallic, ProceduralTexture.Saturate(smoothness));
                }
            }

            return new Maps
            {
                Albedo = albedo,
                MetallicSmoothness = mask,
                Height = height,
                NormalStrength = 1.3f,
            };
        }

        // ---------- 磨损军绿漆：机箱 ----------

        private static Maps BakeWornOlivePaint()
        {
            var albedo = new Color[Size * Size];
            var mask = new Color[Size * Size];
            var height = new float[Size * Size];
            const int seed = 2202;

            var paint = new Color(0.19f, 0.21f, 0.15f);
            var primer = new Color(0.21f, 0.16f, 0.12f);
            var bareMetal = new Color(0.33f, 0.33f, 0.34f);

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = x / (float)Size;
                    var v = y / (float)Size;
                    var i = y * Size + x;

                    // 橘皮：喷漆干燥后的表面起伏，是漆面区别于塑料的关键手感。
                    var orangePeel = ProceduralTexture.Fbm(u, v, 40, 40, 3, 0.5f, seed);

                    // 掉漆：先出大块分布，再用高频噪声把边缘啃出不规则形状。
                    // 真实的掉漆边界永远是碎的，光滑的边界一眼假。
                    var wearField = ProceduralTexture.Fbm(u, v, 4, 4, 5, 0.58f, seed + 13);
                    var wearEdge = ProceduralTexture.Fbm(u, v, 48, 48, 3, 0.5f, seed + 29);
                    var chipped = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.79f, 0.89f, wearField + wearEdge * 0.14f));

                    // 掉漆最深处露出底金属，浅处只到底漆。
                    var deep = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.90f, 0.99f, wearField + wearEdge * 0.14f));

                    // 竖向流痕：常年从上往下流的水渍。
                    var streak = ProceduralTexture.Fbm(u, v, 12, 2, 3, 0.5f, seed + 41);
                    streak = Mathf.SmoothStep(0.45f, 0.95f, streak) * 0.35f;

                    height[i] = orangePeel * 0.12f - chipped * 0.30f + streak * 0.04f + 0.5f;

                    var color = Color.Lerp(paint, primer, chipped);
                    color = Color.Lerp(color, bareMetal, deep);
                    color = Color.Lerp(color, color * 0.72f, streak);
                    color *= Mathf.Lerp(0.94f, 1.06f, orangePeel);
                    albedo[i] = color;

                    var metallic = Mathf.Lerp(0.04f, 0.82f, deep);
                    var smoothness = Mathf.Lerp(0.42f, 0.18f, chipped) - streak * 0.2f;
                    mask[i] = ProceduralTexture.MetallicSmoothness(
                        metallic, ProceduralTexture.Saturate(smoothness));
                }
            }

            return new Maps
            {
                Albedo = albedo,
                MetallicSmoothness = mask,
                Height = height,
                NormalStrength = 1.2f,
            };
        }

        // ---------- 混凝土：墙面 ----------

        private static Maps BakeConcrete()
        {
            var albedo = new Color[Size * Size];
            var mask = new Color[Size * Size];
            var height = new float[Size * Size];
            const int seed = 3303;

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = x / (float)Size;
                    var v = y / (float)Size;
                    var i = y * Size + x;

                    // 三个尺度叠起来：大块浇筑不均、中等砂粒、细微粗糙。
                    var macro = ProceduralTexture.Fbm(u, v, 3, 3, 3, 0.6f, seed);
                    var grain = ProceduralTexture.Fbm(u, v, 28, 28, 4, 0.55f, seed + 17);
                    var micro = ProceduralTexture.Fbm(u, v, 64, 64, 2, 0.5f, seed + 53);

                    // 气孔：浇筑时留下的气泡，是混凝土最有辨识度的特征。
                    var pores = ProceduralTexture.Blotches(u, v, 34, 0.74f, 0.045f, seed + 71);

                    var h = macro * 0.62f + grain * 0.24f + micro * 0.08f - pores * 0.42f;
                    height[i] = h;

                    var tone = Mathf.Lerp(0.14f, 0.21f, macro * 0.6f + grain * 0.4f);
                    tone *= Mathf.Lerp(1f, 0.62f, pores);
                    tone *= Mathf.Lerp(0.92f, 1.04f, micro);

                    albedo[i] = new Color(tone, tone * 0.99f, tone * 0.94f, 1f);
                    mask[i] = ProceduralTexture.MetallicSmoothness(
                        0f, ProceduralTexture.Saturate(Mathf.Lerp(0.10f, 0.03f, pores)));
                }
            }

            return new Maps
            {
                Albedo = albedo,
                MetallicSmoothness = mask,
                Height = height,
                NormalStrength = 2.0f,
            };
        }

        // ---------- 贝克莱特：旋钮 ----------

        private static Maps BakeBakelite()
        {
            var albedo = new Color[Size * Size];
            var mask = new Color[Size * Size];
            var height = new float[Size * Size];
            const int seed = 4404;

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = x / (float)Size;
                    var v = y / (float)Size;
                    var i = y * Size + x;

                    // 滚花：旋钮侧面的直纹。圆柱 UV 的水平方向就是周向，
                    // 所以周向的规则条纹在这里是竖直的正弦波。
                    var knurl = Mathf.Sin(u * Mathf.PI * 2f * 22f) * 0.5f + 0.5f;
                    knurl = Mathf.Pow(knurl, 1.6f);

                    // 模压料本身的斑驳，以及长期手摸出来的包浆。
                    var mottle = ProceduralTexture.Fbm(u, v, 12, 12, 4, 0.55f, seed);
                    var polish = ProceduralTexture.Fbm(u, v, 5, 5, 3, 0.6f, seed + 23);

                    height[i] = knurl * 0.75f + mottle * 0.25f;

                    var tone = Mathf.Lerp(0.030f, 0.058f, mottle);
                    tone *= Mathf.Lerp(0.85f, 1.15f, knurl);
                    albedo[i] = new Color(tone * 1.05f, tone, tone * 0.95f, 1f);

                    // 凸起的滚花棱被摸得发亮，凹槽里留着灰。
                    var smoothness = Mathf.Lerp(0.22f, 0.66f, knurl) * Mathf.Lerp(0.8f, 1.1f, polish);
                    mask[i] = ProceduralTexture.MetallicSmoothness(
                        0.04f, ProceduralTexture.Saturate(smoothness));
                }
            }

            return new Maps
            {
                Albedo = albedo,
                MetallicSmoothness = mask,
                Height = height,
                NormalStrength = 1.8f,
            };
        }

        // ---------- 泛黄纸：电报纸与档案 ----------

        private static Maps BakeAgedPaper()
        {
            var albedo = new Color[Size * Size];
            var mask = new Color[Size * Size];
            var height = new float[Size * Size];
            const int seed = 5505;

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = x / (float)Size;
                    var v = y / (float)Size;
                    var i = y * Size + x;

                    // 纤维：造纸时纤维大致沿一个方向排列，所以是各向异性的细噪声。
                    var fiber = ProceduralTexture.Fbm(u, v, 72, 26, 3, 0.5f, seed);

                    // 折痕：少数几条近乎笔直的脊线。
                    var crease = ProceduralTexture.Ridged(u, v, 2, 5, 2, seed + 19);
                    crease = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.90f, 1f, crease));

                    // 泛黄与霉斑：边缘比中心黄，这是纸张老化的实际规律。
                    var edgeFalloff = Mathf.Max(
                        Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2f;
                    var foxing = ProceduralTexture.Blotches(u, v, 9, 0.68f, 0.10f, seed + 37);
                    var yellowing = ProceduralTexture.Saturate(
                        edgeFalloff * 0.55f + foxing * 0.45f);

                    height[i] = fiber * 0.5f + crease * 0.5f;

                    var paper = new Color(0.56f, 0.52f, 0.43f);
                    var aged = new Color(0.40f, 0.33f, 0.21f);
                    var color = Color.Lerp(paper, aged, yellowing * 0.8f);
                    color *= Mathf.Lerp(0.95f, 1.05f, fiber);
                    color = Color.Lerp(color, color * 0.85f, crease * 0.5f);
                    albedo[i] = color;

                    mask[i] = ProceduralTexture.MetallicSmoothness(
                        0f, ProceduralTexture.Saturate(0.06f + fiber * 0.05f));
                }
            }

            return new Maps
            {
                Albedo = albedo,
                MetallicSmoothness = mask,
                Height = height,
                NormalStrength = 0.9f,
            };
        }

        // ---------- 黄铜：把手与指针 ----------

        private static Maps BakeBrass()
        {
            var albedo = new Color[Size * Size];
            var mask = new Color[Size * Size];
            var height = new float[Size * Size];
            const int seed = 6606;

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = x / (float)Size;
                    var v = y / (float)Size;
                    var i = y * Size + x;

                    var polish = ProceduralTexture.Fbm(u, v, 7, 40, 3, 0.5f, seed);
                    // 铜绿：黄铜氧化后的暗绿色沉积，总是先在凹处积起来。
                    var patina = ProceduralTexture.Blotches(u, v, 14, 0.62f, 0.13f, seed + 43);

                    height[i] = polish * 0.6f + patina * 0.4f;

                    var bright = new Color(0.62f, 0.47f, 0.18f);
                    var dull = new Color(0.26f, 0.28f, 0.19f);
                    var color = Color.Lerp(bright, dull, patina * 0.75f);
                    color *= Mathf.Lerp(0.9f, 1.1f, polish);
                    albedo[i] = color;

                    mask[i] = ProceduralTexture.MetallicSmoothness(
                        Mathf.Lerp(0.92f, 0.35f, patina),
                        ProceduralTexture.Saturate(Mathf.Lerp(0.70f, 0.16f, patina)));
                }
            }

            return new Maps
            {
                Albedo = albedo,
                MetallicSmoothness = mask,
                Height = height,
                NormalStrength = 1.2f,
            };
        }

        // ---------- 写盘与装配 ----------

        private static void Bake(string name, Maps maps)
        {
            var albedoPath = WritePng($"{name}_Albedo", maps.Albedo, srgb: true);
            var maskPath = WritePng($"{name}_Mask", maps.MetallicSmoothness, srgb: false);

            var normals = ProceduralTexture.HeightToNormal(maps.Height, Size, maps.NormalStrength);
            var normalPath = WritePng($"{name}_Normal", normals, srgb: false, normalMap: true);

            var material = new Material(Shader.Find("Standard"))
            {
                name = name,
            };
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICGLOSSMAP");
            // 采样金属光滑度贴图时，Standard 用的是 _MetallicGlossMap 的 A 通道，
            // 这个开关必须打开，否则光滑度会退回材质上的标量值。
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Glossiness", 1f);
            material.SetFloat("_Metallic", 1f);

            AssetDatabase.CreateAsset(material, $"{MaterialDir}/{name}.mat");
        }

        private static string WritePng(string name, Color[] pixels, bool srgb,
            bool normalMap = false)
        {
            var texture = ProceduralTexture.Create(Size, pixels, name, linear: !srgb);
            var path = $"{TextureDir}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

            return path;
        }

        private static void Log(string message)
        {
            Debug.Log($"[ProceduralMaterialBaker] {message}");
            Console.WriteLine($"[ProceduralMaterialBaker] {message}");
        }
    }
}

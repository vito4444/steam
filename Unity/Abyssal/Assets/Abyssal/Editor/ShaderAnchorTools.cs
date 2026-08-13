using System.IO;
using UnityEditor;
using UnityEngine;

namespace Abyssal.EditorTools
{
    /// <summary>
    /// 保证运行时构造的材质在构建产物里仍然可用。
    ///
    /// 两个方案的材质全部是运行时用 <c>Shader.Find</c> 创建的，没有任何资产引用它们。
    /// Unity 打包时因此认为这些着色器没人用，直接剥离，构建出来的游戏一启动就是
    ///     shader 'Universal Render Pipeline/Unlit' not found
    /// 接着抛 ArgumentNullException，整个画面全黑。这个问题在编辑器里看不出来。
    ///
    /// 曾经试过把着色器塞进 GraphicsSettings 的「始终包含」列表，那是个陷阱：
    /// 它会强制编译该着色器的**全部**变体组合。实测 URP Unlit 一个就有 589,824 个变体，
    /// 在这台没有显卡的机器上以每秒 11 个的速度编译，需要十五小时。
    ///
    /// 正确做法是在启动场景里放一组真实的材质资产引用。
    /// Unity 会顺着引用只打包实际用到的那几个变体，构建时间回到分钟级。
    /// </summary>
    public static class ShaderAnchorTools
    {
        const string MaterialDir = "Assets/Overclock/Materials";

        /// <summary>运行时会从这里克隆材质，而不是靠 Shader.Find 现找。</summary>
        public const string ResourcesDir = "Assets/Overclock/Resources";

        /// <summary>
        /// 生成模板材质资产，并在当前场景里挂一个引用它们的锚点物体。
        /// 锚点缩到极小并放在相机后方，玩家看不到它，但它让引用链成立。
        /// </summary>
        public static GameObject CreateAnchor(bool includeLit)
        {
            StripAlwaysIncludedUrpShaders();
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(ResourcesDir);

            var materials = new System.Collections.Generic.List<Material>
            {
                EnsureMaterial("UnlitOpaque", "Universal Render Pipeline/Unlit", m =>
                {
                    m.SetFloat("_Surface", 0f);
                }),
                EnsureMaterial("UnlitTransparent", "Universal Render Pipeline/Unlit", m =>
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }),
                EnsureMaterial("UnlitAdditive", "Universal Render Pipeline/Unlit", m =>
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 1f);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }),
            };

            if (includeLit)
            {
                materials.Add(EnsureMaterial("LitOpaque", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetFloat("_Metallic", 0.5f);
                    m.SetFloat("_Smoothness", 0.4f);
                }));
                materials.Add(EnsureMaterial("LitEmissive", "Universal Render Pipeline/Lit", m =>
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.white * 0.5f);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }));
                materials.Add(EnsureMaterial("LitTransparent", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }));
            }

            AssetDatabase.SaveAssets();

            var anchor = new GameObject("ShaderAnchor");
            // 放在相机后方并缩到微不可见。它唯一的作用是建立资产引用链。
            anchor.transform.position = new Vector3(0f, -9000f, 0f);
            anchor.transform.localScale = Vector3.one * 0.0001f;

            for (int i = 0; i < materials.Count; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = materials[i].name;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(anchor.transform, false);
                quad.transform.localPosition = new Vector3(i * 2f, 0f, 0f);
                quad.GetComponent<MeshRenderer>().sharedMaterial = materials[i];
            }

            Debug.Log($"ABYSSAL: shader anchor 建立，引用 {materials.Count} 个模板材质");
            return anchor;
        }

        /// <summary>
        /// 把之前塞进「始终包含」列表的 URP 着色器摘出来。
        ///
        /// 留在那里会强制编译它们的全部变体组合，一个 URP Unlit 就是 589,824 个，
        /// 而场景里的材质引用只需要其中几十个。不清掉的话锚点方案完全不起作用。
        /// </summary>
        public static void StripAlwaysIncludedUrpShaders()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            UnityEngine.Object graphics = null;
            foreach (var asset in settings)
            {
                if (asset == null || asset.GetType().Name != "GraphicsSettings") continue;
                graphics = asset;
                break;
            }
            if (graphics == null) return;

            var so = new SerializedObject(graphics);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            int removed = 0;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var shader = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader == null) continue;
                if (!shader.name.StartsWith("Universal Render Pipeline/")) continue;

                list.DeleteArrayElementAtIndex(i);
                removed++;
            }

            if (removed > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"ABYSSAL: 从始终包含列表移除 {removed} 个 URP 着色器，剩余 {list.arraySize} 项");
        }

        static Material EnsureMaterial(string name, string shaderName, System.Action<Material> configure)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"ABYSSAL: 找不到着色器 {shaderName}");
                return null;
            }

            var material = new Material(shader) { name = name };
            configure?.Invoke(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}

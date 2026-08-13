using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 在运行时搭出一个最小照明场景：一块地、一个盒子、一盏灯。
    /// 全部对象都在运行时创建，与控制舱走的是完全相同的代码路径，
    /// 因此能如实反映「运行时创建的材质与光源在构建产物里是否真的生效」。
    /// </summary>
    public sealed class LightProbeDiagnostic : MonoBehaviour
    {
        public LightType lightType = LightType.Point;
        public float intensity = 600f;

        void Awake()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Debug.Log($"[Probe] shader={(lit == null ? "NULL" : lit.name)} type={lightType}");

            var mat = new Material(lit) { name = "M_Probe" };
            mat.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.3f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;

            for (int i = -1; i <= 1; i++)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.position = new Vector3(i * 1.6f, 0.5f, 0f);
                box.GetComponent<Renderer>().sharedMaterial = mat;
            }

            var lightGo = new GameObject("ProbeLight");
            var light = lightGo.AddComponent<Light>();
            light.type = lightType;
            light.color = Color.white;
            light.shadows = LightShadows.None;

            if (lightType == LightType.Directional)
            {
                light.intensity = 20f;
                lightGo.transform.rotation = Quaternion.Euler(42f, 20f, 0f);
            }
            else
            {
                light.intensity = intensity;
                light.range = 12f;
                lightGo.transform.position = new Vector3(0f, 2.6f, -1.2f);
            }

            Debug.Log($"[Probe] light type={light.type} intensity={light.intensity} range={light.range} unit={light.lightUnit}");
        }
    }
}

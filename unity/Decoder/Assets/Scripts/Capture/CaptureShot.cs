using UnityEngine;

namespace Decoder.Capture
{
    /// <summary>
    /// 标记一个用于自动截图的固定机位。挂在带 Camera 的物体上，
    /// 由 CaptureHarness 在无头环境下批量渲染。shotName 同时作为输出文件名。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CaptureShot : MonoBehaviour
    {
        [Tooltip("输出文件名（不含扩展名），也是与目标参考图比对时的键名。")]
        public string shotName = "shot";

        [Tooltip("这个机位对标的参考图，相对仓库根目录。留空表示暂无对标目标。")]
        public string referenceImagePath = string.Empty;
    }
}

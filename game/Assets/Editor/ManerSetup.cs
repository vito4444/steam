using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Maner.EditorTools
{
    /// <summary>
    /// 批处理模式下的一次性工程配置入口。通过 -executeMethod 调用。
    /// </summary>
    public static class ManerSetup
    {
        static readonly string[] RequiredPackages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.test-framework",
        };

        public static void InstallPackages()
        {
            var pending = new List<string>(RequiredPackages);
            foreach (var id in pending)
            {
                Console.WriteLine($"[ManerSetup] 开始安装 {id}");
                AddRequest request = Client.Add(id);
                while (!request.IsCompleted)
                {
                    System.Threading.Thread.Sleep(200);
                }

                if (request.Status == StatusCode.Success)
                {
                    Console.WriteLine($"[ManerSetup] 已安装 {request.Result.packageId}");
                }
                else
                {
                    Console.WriteLine($"[ManerSetup] 安装失败 {id}: {request.Error?.message}");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            Console.WriteLine("[ManerSetup] 全部依赖安装完成");
            EditorApplication.Exit(0);
        }
    }
}

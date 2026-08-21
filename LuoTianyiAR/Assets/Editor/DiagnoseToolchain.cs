// DiagnoseToolchain.cs — 运行时反射诊断 Unity 6 Android 工具链 API
// 找出 AndroidExternalToolsSettings 的真实属性名，并设置路径
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DiagnoseToolchain
{
    public static void DiagnoseAndSet()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "UnityEditor.Android.Extensions");

        if (asm == null)
        {
            Debug.LogError("[Diag] UnityEditor.Android.Extensions 程序集未加载");
            return;
        }

        var type = asm.GetType("UnityEditor.Android.AndroidExternalToolsSettings");
        if (type == null)
        {
            Debug.LogError("[Diag] AndroidExternalToolsSettings 类型未找到");
            return;
        }

        Debug.Log("[Diag] 找到类型: " + type.FullName);

        // 列出所有静态属性
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            Debug.Log($"[Diag] 属性: {p.Name} : {p.PropertyType.Name}");
        }

        // 列出所有静态字段
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            Debug.Log($"[Diag] 字段: {f.Name} : {f.FieldType.Name}");
        }

        // 尝试按候选名设置
        var candidates = new[] { "JdkPath", "AndroidSdkPath", "AndroidNdkPath", "jdkPath", "sdkPath", "ndkPath" };
        var values = new[] { "JdkPath", "SdkPath", "NdkPath" };

        foreach (var name in candidates)
        {
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.CanWrite)
            {
                var key = name.Contains("Jdk") || name.Contains("jdk") ? "JdkPath" : name.Contains("Sdk") || name.Contains("sdk") ? "SdkPath" : "NdkPath";
                var val = key == "JdkPath" ? @"C:\Program Files\Unity 6000.3.22f1\jdk"
                    : key == "SdkPath" ? @"C:\Users\Administrator\AppData\Local\Android\Sdk"
                    : @"C:\Users\Administrator\AppData\Local\Android\Sdk\ndk\27.2.12479018";
                prop.SetValue(null, val);
                Debug.Log($"[Diag] 已设置 {name} = {val}");
            }
            else
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    var key = name.Contains("Jdk") || name.Contains("jdk") ? "JdkPath" : name.Contains("Sdk") || name.Contains("sdk") ? "SdkPath" : "NdkPath";
                    var val = key == "JdkPath" ? @"C:\Program Files\Unity 6000.3.22f1\jdk"
                        : key == "SdkPath" ? @"C:\Users\Administrator\AppData\Local\Android\Sdk"
                        : @"C:\Users\Administrator\AppData\Local\Android\Sdk\ndk\27.2.12479018";
                    field.SetValue(null, val);
                    Debug.Log($"[Diag] 已设置字段 {name} = {val}");
                }
            }
        }
    }
}
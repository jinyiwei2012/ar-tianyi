// AndroidBuild.cs — Phase 4: 一键配置 Android 构建参数并打包 APK
// 使用方式: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod AndroidBuild.Build -logFile <log>
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

public static class AndroidBuild
{
    private const string ARCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";

    public static void Build()
    {
        ConfigureToolchainPaths();
        ConfigurePlayerSettings();
        EnableARCoreLoader();
        AddSceneToBuild();

        var buildDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
        Directory.CreateDirectory(buildDir);
        var apkPath = Path.Combine(buildDir, "LuoTianyiAR.apk");

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/ARScene.unity" },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[AndroidBuild] SUCCESS -> {apkPath} ({report.summary.totalSize / 1048576.0:F1} MB)");
        }
        else
        {
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[AndroidBuild] {step.name}: {msg.content}");
            throw new Exception($"[AndroidBuild] FAILED: {report.summary.result}");
        }
    }

    private static void ConfigureToolchainPaths()
    {
        // 工具链路径优先级: 环境变量(CI 用) > 本机硬编码(手动安装)
        // CI(GameCI unityci/editor 镜像)自带 Unity Hub 安装的 JDK/SDK/NDK，设置环境变量即可覆盖
        var jdk = Environment.GetEnvironmentVariable("UNITY_JDK_PATH")
                  ?? @"C:\Program Files\Unity 6000.3.22f1\jdk";
        var sdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                  ?? @"C:\Users\Administrator\AppData\Local\Android\Sdk";
        var ndk = Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT")
                  ?? @"C:\Users\Administrator\AppData\Local\Android\Sdk\ndk\27.2.12479018";

        // 环境变量未设置时，让 Unity 自动探测（CI 镜像内置工具链）
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNITY_JDK_PATH")) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT")))
        {
            Debug.Log("[AndroidBuild] 未检测到 CI 环境变量，使用 Unity 自动探测工具链");
            return;
        }

        AndroidExternalToolsSettings.jdkRootPath = jdk;
        AndroidExternalToolsSettings.sdkRootPath = sdk;
        AndroidExternalToolsSettings.ndkRootPath = ndk;
        Debug.Log($"[AndroidBuild] 工具链路径已设置: JDK={jdk} SDK={sdk} NDK={ndk}");
    }

    private static void ConfigurePlayerSettings()
    {
        // 基础身份
        PlayerSettings.companyName = "LuoTianyiLab";
        PlayerSettings.productName = "LuoTianyiAR";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.luotianyi.lab.ar");

        // 脚本后端 IL2CPP + ARM64（ARCore 要求，32 位不兼容）
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // 最低 API 25（Android 7.1，ARCore 插件对 6000.3 的要求；Vulkan 需 29）
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;

        // 图形 API：OpenGLES3（ARCore 最稳；默认 Vulkan 需提高 min API）
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

        // IL2CPP 编译选项
        PlayerSettings.SetAdditionalIl2CppArgs("");

        Debug.Log("[AndroidBuild] PlayerSettings 已配置: IL2CPP/ARM64/API25/OpenGLES3");
    }

    private static void EnableARCoreLoader()
    {
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (settings == null)
        {
            Debug.LogError("[AndroidBuild] XR General Settings 不存在");
            return;
        }

        var assigned = settings.AssignedSettings;
        if (assigned == null)
        {
            Debug.LogError("[AndroidBuild] 未指定 XR Settings 资产");
            return;
        }

        XRPackageMetadataStore.AssignLoader(assigned, ARCoreLoaderType, BuildTargetGroup.Android);
        Debug.Log("[AndroidBuild] ARCore Loader 已启用 (Android)");
    }

    private static void AddSceneToBuild()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/ARScene.unity", true)
        };
        EditorBuildSettings.scenes = scenes;
        Debug.Log("[AndroidBuild] ARScene 已加入 Build Settings");
    }
}
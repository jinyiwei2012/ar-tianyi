// AndroidBuild.cs — Phase 4: 一键配置 Android 构建参数并打包 APK
// 使用方式: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod AndroidBuild.Build -logFile <log>
using System;
using System.IO;
using System.Linq;
using Live2D.Cubism.Rendering;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.ARCore;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

public static class AndroidBuild
{
    private const string ARCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
    private const string XRSettingsFolder = "Assets/XR";
    private const string XRSettingsAsset = XRSettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
    private const string ARCoreSettingsFolder = XRSettingsFolder + "/Settings";
    private const string ARCoreSettingsAsset = ARCoreSettingsFolder + "/ARCoreSettings.asset";
    private const string RenderSettingsFolder = "Assets/Settings";
    private const string RenderPipelineAssetPath = RenderSettingsFolder + "/LuoTianyiURPAsset.asset";
    private const string CubismRendererTemplateAssetPath =
        "Assets/Live2D/Cubism/Rendering/URP/CubismURPRenderer.asset";
    private const string AppRendererAssetPath = RenderSettingsFolder + "/LuoTianyiARRenderer.asset";
    private const string ModelPrefabPath = "Assets/Live2D/Models/LuoTianyi/model.prefab";
    private const string CubismRenderPassFeatureType =
        "Live2D.Cubism.Rendering.URP.CubismRenderPassFeature";
    private const string ARBackgroundRendererFeatureType =
        "UnityEngine.XR.ARFoundation.ARBackgroundRendererFeature";
    private const string CubismModelType = "Live2D.Cubism.Core.CubismModel";

    public static void Build()
    {
        ConfigureToolchainPaths();
        ConfigurePlayerSettings();
        ConfigureRenderPipeline();
        ValidateCubismRenderingConfiguration();
        ValidateBillboardFacingConvention();
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
            // ARCore 的 manifest 后处理器只“添加”声明；增量 Gradle 工程可能保留
            // 上一次的 required depth。清理构建缓存，保证 APK 清单等于当前配置。
            options = BuildOptions.CleanBuildCache
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

    /// <summary>
    /// Cubism SDK 5 的模型材质仅提供 UniversalPipeline SubShader，并由
    /// CubismRenderPassFeature 提交绘制。只安装 URP Package 而没有在
    /// Graphics Settings 中启用它时，模型资源会成功加载但完全不可见。
    /// </summary>
    public static void ConfigureRenderPipeline()
    {
        EnsureAssetFolder(RenderSettingsFolder);
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AppRendererAssetPath);
        if (rendererData == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(CubismRendererTemplateAssetPath);
            if (template == null)
                throw new BuildFailedException($"[AndroidBuild] 找不到 Cubism URP Renderer 模板: {CubismRendererTemplateAssetPath}");
            if (!AssetDatabase.CopyAsset(CubismRendererTemplateAssetPath, AppRendererAssetPath))
                throw new BuildFailedException($"[AndroidBuild] 无法创建应用 Renderer: {AppRendererAssetPath}");

            AssetDatabase.ImportAsset(AppRendererAssetPath, ImportAssetOptions.ForceSynchronousImport);
            rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AppRendererAssetPath);
        }

        if (rendererData == null)
            throw new BuildFailedException($"[AndroidBuild] 无法加载应用 Renderer: {AppRendererAssetPath}");

        EnsureARBackgroundRendererFeature(rendererData);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineAssetPath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.name = "LuoTianyiURPAsset";
            AssetDatabase.CreateAsset(pipeline, RenderPipelineAssetPath);
        }

        // 即使资产已存在，也要校验 Renderer 引用，防止后续在面板中
        // 误换成不含 CubismRenderPassFeature 的默认 Universal Renderer。
        var serializedPipeline = new SerializedObject(pipeline);
        var rendererList = serializedPipeline.FindProperty("m_RendererDataList");
        var defaultRendererIndex = serializedPipeline.FindProperty("m_DefaultRendererIndex");
        if (rendererList == null || defaultRendererIndex == null)
            throw new BuildFailedException("[AndroidBuild] 当前 URP 版本缺少 Renderer 配置字段");

        rendererList.arraySize = 1;
        rendererList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
        defaultRendererIndex.intValue = 0;
        serializedPipeline.ApplyModifiedPropertiesWithoutUndo();

        // AR 相机需要深度纹理参与遮挡；移动端关闭 HDR/MSAA 降低带宽。
        pipeline.supportsCameraDepthTexture = true;
        pipeline.supportsCameraOpaqueTexture = false;
        pipeline.supportsHDR = false;
        pipeline.msaaSampleCount = 1;
        pipeline.renderScale = 1f;

        GraphicsSettings.defaultRenderPipeline = pipeline;
        int originalQualityLevel = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(originalQualityLevel, false);
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();

        if (GraphicsSettings.defaultRenderPipeline != pipeline ||
            QualitySettings.renderPipeline != pipeline ||
            pipeline.rendererDataList.Length != 1 ||
            pipeline.rendererDataList[0] != rendererData)
        {
            throw new BuildFailedException("[AndroidBuild] Cubism URP 渲染管线未能持久化");
        }

        Debug.Log("[AndroidBuild] 已启用 URP + ARBackgroundRendererFeature + CubismRenderPassFeature");
    }

    private static void EnsureARBackgroundRendererFeature(ScriptableRendererData rendererData)
    {
        var existing = rendererData.rendererFeatures.FirstOrDefault(feature =>
            feature != null && feature.GetType().FullName == ARBackgroundRendererFeatureType);
        if (existing != null)
        {
            existing.SetActive(true);
            rendererData.SetDirty();
            EditorUtility.SetDirty(existing);
            EditorUtility.SetDirty(rendererData);
            return;
        }

        var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
        feature.name = "ARBackgroundRendererFeature";
        feature.SetActive(true);
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
            throw new BuildFailedException("[AndroidBuild] 无法持久化 ARBackgroundRendererFeature");

        var serializedRenderer = new SerializedObject(rendererData);
        var features = serializedRenderer.FindProperty("m_RendererFeatures");
        var featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
        if (features == null || featureMap == null)
            throw new BuildFailedException("[AndroidBuild] 当前 URP 版本缺少 Renderer Feature 配置字段");

        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        featureMap.arraySize++;
        featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
        serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        rendererData.SetDirty();
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssetIfDirty(rendererData);
        AssetDatabase.ImportAsset(AppRendererAssetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ValidateCubismRenderingConfiguration()
    {
        if (GraphicsSettings.defaultRenderPipeline is not UniversalRenderPipelineAsset pipeline)
            throw new BuildFailedException("[AndroidBuild] Graphics Settings 未启用 URP");

        var rendererData = pipeline.rendererDataList.Length > 0 ? pipeline.rendererDataList[0] : null;
        if (rendererData == null)
            throw new BuildFailedException("[AndroidBuild] URP 缺少默认 Renderer Data");

        var serializedRenderer = new SerializedObject(rendererData);
        var features = serializedRenderer.FindProperty("m_RendererFeatures");
        bool hasActiveCubismPass = false;
        bool hasActiveARBackgroundPass = false;
        if (features != null)
        {
            for (int i = 0; i < features.arraySize; i++)
            {
                var feature = features.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                if (feature != null && feature.isActive && feature.GetType().FullName == CubismRenderPassFeatureType)
                    hasActiveCubismPass = true;
                if (feature != null && feature.isActive && feature.GetType().FullName == ARBackgroundRendererFeatureType)
                    hasActiveARBackgroundPass = true;
            }
        }

        if (!hasActiveCubismPass)
            throw new BuildFailedException("[AndroidBuild] 默认 URP Renderer 未启用 CubismRenderPassFeature");
        if (!hasActiveARBackgroundPass)
            throw new BuildFailedException("[AndroidBuild] 默认 URP Renderer 未启用 ARBackgroundRendererFeature");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
        if (prefab == null)
            throw new BuildFailedException($"[AndroidBuild] 找不到模型 Prefab: {ModelPrefabPath}");

        bool hasCubismModel = prefab.GetComponentsInChildren<MonoBehaviour>(true)
            .Any(component => component != null && component.GetType().FullName == CubismModelType);
        if (!hasCubismModel)
            throw new BuildFailedException("[AndroidBuild] 模型 Prefab 缺少 CubismModel 组件");

        var materials = prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Distinct()
            .ToArray();
        if (materials.Length == 0)
            throw new BuildFailedException("[AndroidBuild] 模型 Prefab 没有可用材质");

        var incompatible = materials.FirstOrDefault(material =>
            material.shader == null ||
            material.GetTag("RenderPipeline", false, string.Empty) != "UniversalPipeline");
        if (incompatible != null)
            throw new BuildFailedException($"[AndroidBuild] Cubism 材质不兼容 URP: {incompatible.name}");

        var probe = UnityEngine.Object.Instantiate(prefab);
        probe.name = "Cubism Runtime Mesh Build Probe";
        probe.hideFlags = HideFlags.HideAndDontSave;
        int runtimeMeshCount;
        int cubismRendererCount;
        try
        {
            var cubismRenderers = probe.GetComponentsInChildren<CubismRenderer>(true);
            cubismRendererCount = cubismRenderers.Length;
            runtimeMeshCount = cubismRenderers.Count(renderer =>
                renderer.Mesh != null && renderer.Mesh.vertexCount > 0);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }

        if (cubismRendererCount == 0 || runtimeMeshCount == 0)
            throw new BuildFailedException(
                $"[AndroidBuild] Cubism 运行时网格创建失败: Renderer={cubismRendererCount}, Mesh={runtimeMeshCount}");

        Debug.Log(
            $"[AndroidBuild] Cubism 渲染校验通过: Renderer={rendererData.name}, " +
            $"ARBackground=active, CubismRenderPass=active, Materials={materials.Length}, " +
            $"RuntimeMeshes={runtimeMeshCount}/{cubismRendererCount}");
    }

    private static void ValidateBillboardFacingConvention()
    {
        var modelPosition = Vector3.zero;
        var cameraPosition = new Vector3(2f, 1.5f, -3f);
        if (!CylindricalBillboard.TryGetFacingRotation(
                modelPosition, cameraPosition, out var rotation))
        {
            throw new BuildFailedException("[AndroidBuild] Billboard 无法计算有效朝向");
        }

        var expectedFront = cameraPosition - modelPosition;
        expectedFront.y = 0f;
        expectedFront.Normalize();
        float frontDot = Vector3.Dot(rotation * Vector3.back, expectedFront);
        float upDot = Vector3.Dot(rotation * Vector3.up, Vector3.up);
        if (frontDot < 0.9999f || upDot < 0.9999f)
        {
            throw new BuildFailedException(
                $"[AndroidBuild] Billboard 朝向约定错误: frontDot={frontDot:F5}, upDot={upDot:F5}");
        }

        Debug.Log(
            $"[AndroidBuild] Billboard 朝向校验通过: CubismFront=-Z, " +
            $"frontDot={frontDot:F5}, upDot={upDot:F5}");
    }

    private static void EnableARCoreLoader()
    {
        EnsureAssetFolder(XRSettingsFolder);

        var perBuildTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XRSettingsAsset);
        if (perBuildTarget == null)
        {
            perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            perBuildTarget.name = "XRGeneralSettingsPerBuildTarget";
            AssetDatabase.CreateAsset(perBuildTarget, XRSettingsAsset);
        }

        // XR Management 的 Android manifest 处理器只读取这个配置对象。
        // 仅仅在磁盘上存在一个空资产并不够，必须为 Android 建立 General + Manager 子资产。
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);

        if (!perBuildTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            perBuildTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        var settings = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        var assigned = settings != null ? settings.AssignedSettings : null;
        if (assigned == null)
            throw new BuildFailedException("[AndroidBuild] 无法创建 Android XR Manager Settings");

        assigned.automaticLoading = true;
        assigned.automaticRunning = true;

        bool alreadyAssigned = assigned.activeLoaders.Any(
            loader => loader != null && loader.GetType().FullName == ARCoreLoaderType);
        if (!alreadyAssigned &&
            !XRPackageMetadataStore.AssignLoader(assigned, ARCoreLoaderType, BuildTargetGroup.Android))
        {
            throw new BuildFailedException("[AndroidBuild] ARCore Loader 分配失败；停止构建，避免产出非 AR APK");
        }

        EditorUtility.SetDirty(perBuildTarget);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(assigned);
        AssetDatabase.SaveAssets();

        var persisted = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        bool valid = persisted?.AssignedSettings?.activeLoaders.Any(
            loader => loader != null && loader.GetType().FullName == ARCoreLoaderType) == true;
        if (!valid)
            throw new BuildFailedException("[AndroidBuild] ARCore Loader 未能持久化；停止构建");

        ConfigureARCoreRequirements();
        Debug.Log("[AndroidBuild] ARCore Loader 已启用 (Android)");
    }

    private static void ConfigureARCoreRequirements()
    {
        EnsureAssetFolder(ARCoreSettingsFolder);
        var settings = AssetDatabase.LoadAssetAtPath<ARCoreSettings>(ARCoreSettingsAsset);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<ARCoreSettings>();
            settings.name = "ARCoreSettings";
            AssetDatabase.CreateAsset(settings, ARCoreSettingsAsset);
        }

        // P0 依赖 ARCore，所以 AR 本身必需；Depth 属于 P1，必须允许不支持深度的
        // ARCore 设备安装并由 OcclusionController 在运行时降级。
        settings.requirement = ARCoreSettings.Requirement.Required;
        settings.depth = ARCoreSettings.Requirement.Optional;
        ARCoreSettings.currentSettings = settings;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[AndroidBuild] ARCore=Required, Depth=Optional");
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        var leaf = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            throw new BuildFailedException($"[AndroidBuild] 无效的资产目录: {folderPath}");

        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
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

// ARSceneSetup.cs — 批处理模式创建 AR 场景骨架
// 使用方式: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod ARSceneSetup.CreateARScene
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine.XR.ARSubsystems;

public static class ARSceneSetup
{
    private const string ScenePath = "Assets/Scenes/ARScene.unity";
    private const string ModelPrefabPath = "Assets/Live2D/Models/LuoTianyi/model.prefab";
    private const string MarkerTexturePath = "Assets/AR/Markers/LuoTianyiDeskMarkerV1.png";
    private const string MarkerLibraryPath = "Assets/AR/Markers/LuoTianyiMarkerLibrary.asset";
    private const string MarkerMaterialPath = "Assets/AR/Markers/MarkerDiagnosticUnlit.mat";
    private const float MarkerPhysicalSizeMeters = 0.12f;

    public static void CreateARScene()
    {
        AndroidBuild.ConfigureRenderPipeline();

        // 新建场景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. AR Session (ARSession + ARInputManager)
        var sessionGo = ObjectFactory.CreateGameObject("AR Session", typeof(ARSession), typeof(ARInputManager));

        // 2. XR Origin
        var originGo = ObjectFactory.CreateGameObject("XR Origin", typeof(XROrigin));

        // Camera Offset 子对象
        var offsetGo = ObjectFactory.CreateGameObject("Camera Offset");
        offsetGo.transform.SetParent(originGo.transform);

        // Main Camera 子对象
        var cameraGo = ObjectFactory.CreateGameObject(
            "Main Camera",
            typeof(Camera),
            typeof(AudioListener),
            typeof(ARCameraManager),
            typeof(ARCameraBackground),
            typeof(TrackedPoseDriver));
        cameraGo.transform.SetParent(offsetGo.transform);
        cameraGo.transform.localPosition = Vector3.zero;
        cameraGo.transform.localRotation = Quaternion.identity;

        var camera = cameraGo.GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.Color;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 20f;

        // ARCore 在 Any 模式下默认选择 BeforeOpaques。部分设备/新版
        // Google Play Services for AR 在 URP 的该路径上会只显示清屏色。
        // AfterOpaques 仍早于 Cubism 的 BeforeRenderingTransparents，既能避开
        // 相机黑屏兼容性问题，也不会覆盖透明 Live2D 模型。
        cameraGo.GetComponent<ARCameraManager>().requestedBackgroundRenderingMode =
            CameraBackgroundRenderingMode.AfterOpaques;

        // TrackedPoseDriver 绑定 AR 输入
        var tpd = cameraGo.GetComponent<TrackedPoseDriver>();
        var posAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        posAction.AddBinding("<HandheldARInputDevice>/devicePosition");
        var rotAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        rotAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
        tpd.positionInput = new InputActionProperty(posAction);
        tpd.rotationInput = new InputActionProperty(rotAction);

        // 关联 XROrigin
        var origin = originGo.GetComponent<XROrigin>();
        origin.CameraFloorOffsetObject = offsetGo;
        origin.Camera = camera;
        // Handheld AR 使用设备原点，不能沿用 XR/VR 模板默认的 1.1176m 头部高度。
        // 否则相机被额外抬高，而 ARCore 平面、raycast 与 anchor 仍位于 session 空间，
        // 会表现为模型靠近手机、落不到平面且随追踪漂移。
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
        origin.CameraYOffset = 0f;

        // 3. Trackable 管理器挂到 XR Origin (ARF 6.x 规范)
        var planeManager = originGo.AddComponent<ARPlaneManager>();
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        originGo.AddComponent<ARRaycastManager>();
        originGo.AddComponent<ARAnchorManager>();

        // 4. P0 交互：点击/拖动真实平面，Anchor 保持世界位姿，双指按米缩放。
        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
        if (modelPrefab == null)
            throw new System.InvalidOperationException($"[ARSceneSetup] 找不到模型 Prefab: {ModelPrefabPath}");

        var placement = originGo.AddComponent<PlaceOnPlane>();
        var placementObject = new SerializedObject(placement);
        placementObject.FindProperty("modelPrefab").objectReferenceValue = modelPrefab;
        placementObject.ApplyModifiedPropertiesWithoutUndo();
        originGo.AddComponent<PlacementGuideUI>();
        originGo.AddComponent<PositionLockUI>();
        originGo.AddComponent<CameraCaptureUI>();

        // 5. 遮挡必须和 ARCameraManager/Camera 在同一对象；否则 RequireComponent 会在
        // XR Origin 上生成一台多余 Camera，既浪费渲染又可能破坏相机选择。
        cameraGo.AddComponent<AROcclusionManager>();
        cameraGo.AddComponent<OcclusionController>();

        // 6. 保存场景
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[ARSceneSetup] ARScene created: horizontal planes + anchors + 4:3 camera UI + layered capture + camera occlusion");
    }

    /// <summary>
    /// 将现有 ARScene 收敛到纯水平面定位和相机式 UI。AndroidBuild 每次构建都会调用，
    /// 同时移除历史二维码诊断与旧式悬浮按钮。
    /// </summary>
    public static void ConfigureCameraExperience()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var originGo = GameObject.Find("XR Origin");
        if (originGo == null)
            throw new System.InvalidOperationException("[ARSceneSetup] 未找到 XR Origin");

        var cameraManager = Object.FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (cameraManager == null)
            throw new System.InvalidOperationException("[ARSceneSetup] 未找到 ARCameraManager");
        cameraManager.requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.AfterOpaques;
        EditorUtility.SetDirty(cameraManager);

        foreach (var diagnostics in originGo.GetComponents<ARMarkerDiagnostics>())
            Object.DestroyImmediate(diagnostics);
        foreach (var manager in originGo.GetComponents<ARTrackedImageManager>())
            Object.DestroyImmediate(manager);
        foreach (var nudge in originGo.GetComponents<ModelNudgeUI>())
            Object.DestroyImmediate(nudge);
        foreach (var expressions in originGo.GetComponents<ExpressionCycleUI>())
            Object.DestroyImmediate(expressions);

        if (originGo.GetComponent<PlaceOnPlane>() != null)
        {
            if (originGo.GetComponent<PositionLockUI>() == null)
                originGo.AddComponent<PositionLockUI>();
            if (originGo.GetComponent<CameraCaptureUI>() == null)
                originGo.AddComponent<CameraCaptureUI>();
            if (originGo.GetComponent<CaptureGalleryUI>() == null)
                originGo.AddComponent<CaptureGalleryUI>();
        }
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ARSceneSetup] 已切换为纯平面定位，AR 背景强制 AfterOpaques，并接入 4:3 相机界面与图层拍摄");
    }

    private static void ConfigureMarkerTracking(GameObject originGo)
    {
        var library = EnsureMarkerReferenceLibrary();
        var imageManager = originGo.GetComponent<ARTrackedImageManager>() ??
                           originGo.AddComponent<ARTrackedImageManager>();
        imageManager.referenceLibrary = library;
        imageManager.requestedMaxNumberOfMovingImages = 1;

        var diagnostics = originGo.GetComponent<ARMarkerDiagnostics>() ??
                          originGo.AddComponent<ARMarkerDiagnostics>();
        var diagnosticsObject = new SerializedObject(diagnostics);
        diagnosticsObject.FindProperty("axisLengthMeters").floatValue = 0.04f;
        diagnosticsObject.FindProperty("diagnosticMaterial").objectReferenceValue =
            EnsureMarkerDiagnosticMaterial();
        diagnosticsObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(originGo);
        EditorUtility.SetDirty(imageManager);
        EditorUtility.SetDirty(diagnostics);
    }

    private static Material EnsureMarkerDiagnosticMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
        if (material != null)
            return material;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            throw new System.InvalidOperationException("[ARSceneSetup] Editor 中找不到 URP Unlit Shader");

        material = new Material(shader)
        {
            name = "MarkerDiagnosticUnlit"
        };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(material, MarkerMaterialPath);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static XRReferenceImageLibrary EnsureMarkerReferenceLibrary()
    {
        if (AssetImporter.GetAtPath(MarkerTexturePath) is not TextureImporter importer)
            throw new System.InvalidOperationException($"[ARSceneSetup] 找不到定位卡图片: {MarkerTexturePath}");

        importer.textureType = TextureImporterType.Default;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerTexturePath);
        if (texture == null)
            throw new System.InvalidOperationException($"[ARSceneSetup] 无法导入定位卡图片: {MarkerTexturePath}");

        var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(MarkerLibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
            library.name = "LuoTianyiMarkerLibrary";
            AssetDatabase.CreateAsset(library, MarkerLibraryPath);
        }

        while (library.count > 1)
            library.RemoveAt(library.count - 1);
        if (library.count == 0)
            library.Add();

        library.SetName(0, ARMarkerDiagnostics.DefaultMarkerName);
        library.SetTexture(0, texture, false);
        library.SetSpecifySize(0, true);
        library.SetSize(0, new Vector2(MarkerPhysicalSizeMeters, MarkerPhysicalSizeMeters));
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        var referenceImage = library[0];
        if (referenceImage.name != ARMarkerDiagnostics.DefaultMarkerName ||
            !referenceImage.specifySize ||
            Mathf.Abs(referenceImage.size.x - MarkerPhysicalSizeMeters) > 0.0001f)
        {
            throw new System.InvalidOperationException("[ARSceneSetup] 定位卡 Reference Image Library 校验失败");
        }

        return library;
    }

    /// 给已存在的 ARScene 补挂遮挡组件（不重建场景）
    public static void AddOcclusion()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var cameraGo = GameObject.Find("Main Camera");
        if (cameraGo == null)
        {
            Debug.LogError("[ARSceneSetup] 未找到 Main Camera");
            return;
        }

        if (cameraGo.GetComponent<AROcclusionManager>() == null)
            cameraGo.AddComponent<AROcclusionManager>();
        if (cameraGo.GetComponent<OcclusionController>() == null)
            cameraGo.AddComponent<OcclusionController>();

        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ARSceneSetup] AROcclusionManager + OcclusionController 已挂到 Main Camera");
    }
}

// ARSceneSetup.cs — 批处理模式创建 AR 场景骨架
// 使用方式: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod ARSceneSetup.CreateARScene
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.XR.ARSubsystems;

public static class ARSceneSetup
{
    private const string ScenePath = "Assets/Scenes/ARScene.unity";
    private const string ModelPrefabPath = "Assets/Live2D/Models/LuoTianyi/model.prefab";

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

        // 5. 遮挡必须和 ARCameraManager/Camera 在同一对象；否则 RequireComponent 会在
        // XR Origin 上生成一台多余 Camera，既浪费渲染又可能破坏相机选择。
        cameraGo.AddComponent<AROcclusionManager>();
        cameraGo.AddComponent<OcclusionController>();

        // 6. 保存场景
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[ARSceneSetup] ARScene created: horizontal planes + anchors + P0 placement UI + camera occlusion");
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

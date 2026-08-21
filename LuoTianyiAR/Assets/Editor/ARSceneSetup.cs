// ARSceneSetup.cs — 批处理模式创建 AR 场景骨架
// 使用方式: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod ARSceneSetup.CreateARScene
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class ARSceneSetup
{
    public static void CreateARScene()
    {
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

        // 3. 管理器挂到 XR Origin (ARF 6.x 规范)
        originGo.AddComponent<ARPlaneManager>();
        originGo.AddComponent<ARRaycastManager>();

        // 4. 保存场景
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/ARScene.unity");
        Debug.Log("[ARSceneSetup] ARScene created: XR Origin + AR Session + PlaneManager + RaycastManager");
    }
}
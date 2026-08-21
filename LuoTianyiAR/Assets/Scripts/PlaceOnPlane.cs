// PlaceOnPlane.cs — PRD Phase 1: 点击真实平面放置洛天依
// 挂载到 XR Origin 上。首次点击放置模型，之后点击重新定位。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager))]
public class PlaceOnPlane : MonoBehaviour
{
    [Header("模型")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("洛天依在 AR 世界中的身高（米）。Cubism 模型默认 1:1，需按此缩放")] 
    [SerializeField] private float targetHeightMeters = 0.6f;

    private ARRaycastManager raycastManager;
    private readonly List<ARRaycastHit> hits = new();
    private GameObject spawnedModel;

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += OnFingerDown;
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= OnFingerDown;
        EnhancedTouchSupport.Disable();
    }

    private void Start()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        if (modelPrefab == null)
            Debug.LogError("[PlaceOnPlane] 未指定 modelPrefab！请在 Inspector 拖入 model.prefab");
    }

    private void OnFingerDown(Finger finger)
    {
        // 屏上点击 → 射线检测真实平面
        if (raycastManager.Raycast(finger.screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            var hitPose = hits[0].pose;
            if (spawnedModel == null)
            {
                spawnedModel = Instantiate(modelPrefab, hitPose.position, Quaternion.identity);
                ScaleToHeight(spawnedModel, targetHeightMeters);
                var billboard = spawnedModel.AddComponent<CylindricalBillboard>();
                var movement = spawnedModel.AddComponent<LuoMovement>();
                // 关联: billboard 在走路时让位给朝向逻辑
                billboard.movement = movement;
                Debug.Log("[PlaceOnPlane] 洛天依已放置于 " + hitPose.position);

                // 放置完成后把点击控制权交给 LuoMovement（走路），禁用自身避免冲突
                enabled = false;
            }
            hits.Clear();
        }
    }

    /// 按目标身高缩放模型（Cubism 模型原始高度通常为编辑器画布像素数）
    private void ScaleToHeight(GameObject model, float heightMeters)
    {
        var renderer = model.GetComponentInChildren<Renderer>();
        if (renderer == null) return;
        float originalHeight = renderer.bounds.size.y;
        if (originalHeight > 0.001f)
            model.transform.localScale = Vector3.one * (heightMeters / originalHeight);
    }
}
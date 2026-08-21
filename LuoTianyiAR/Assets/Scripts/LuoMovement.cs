// LuoMovement.cs — PRD Phase 2: 洛天依在世界坐标中走动
// 点击平面目标点 → 模型平滑走向目标，每帧向下 raycast 贴地，移动时转向运动方向。
// 挂载到模型根节点上；由 PlaceOnPlane 在放置时 AddComponent。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class LuoMovement : MonoBehaviour
{
    [Header("移动参数")]
    [SerializeField] private float moveSpeed = 0.8f;      // 米/秒
    [SerializeField] private float turnSpeed = 360f;      // 度/秒
    [Tooltip("向下 raycast 贴地的偏移量（从脚底向下探测）")]
    [SerializeField] private float groundProbeOffset = 0.05f;

    private ARRaycastManager raycastManager;
    private readonly List<ARRaycastHit> hits = new();
    private Vector3? targetPosition;   // null = 未移动
    private Vector3 currentVelocity;
    private bool isWalking;

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
        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
    }

    private void OnFingerDown(Finger finger)
    {
        if (raycastManager == null) return;
        if (raycastManager.Raycast(finger.screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            targetPosition = hits[0].pose.position;
            hits.Clear();
        }
    }

    private void Update()
    {
        if (targetPosition == null) return;

        var target = targetPosition.Value;
        var pos = transform.position;

        // 水平方向朝目标移动
        Vector3 toTarget = target - pos;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist > 0.02f)
        {
            isWalking = true;
            // 面向运动方向（只绕 Y 轴，绕开 CylindricalBillboard 的朝向逻辑）
            RotateToward(toTarget);

            // 世界坐标移动
            Vector3 move = toTarget.normalized * moveSpeed * Time.deltaTime;
            if (move.magnitude > dist) move = toTarget;  // 避免过冲
            transform.position += move;

            // 贴地: 从脚底向下 raycast 保持在地面上
            KeepOnGround();
        }
        else
        {
            isWalking = false;
            targetPosition = null;  // 到达
        }
    }

    private void RotateToward(Vector3 dir)
    {
        var targetRot = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    /// 向下 raycast：保持脚底贴合真实平面（PRD Phase 2: downward raycast）
    private void KeepOnGround()
    {
        if (raycastManager == null) return;
        var probeOrigin = transform.position + Vector3.up * groundProbeOffset;
        if (raycastManager.Raycast(new Ray(probeOrigin, Vector3.down), hits, TrackableType.PlaneWithinPolygon))
        {
            var p = transform.position;
            p.y = hits[0].pose.position.y;
            transform.position = p;
        }
        hits.Clear();
    }

    public bool IsWalking => isWalking;

    /// 供外部调用：移动到指定世界坐标（Agent 接口预留）
    public void WalkTo(Vector3 worldPos)
    {
        targetPosition = worldPos;
    }

    /// 回到锚点（PRD: returnToAnchor）
    public void ReturnToAnchor(Vector3 anchorPos)
    {
        targetPosition = anchorPos;
    }
}
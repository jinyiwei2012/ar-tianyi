// LuoMovement.cs — PRD Phase 2: 洛天依在世界坐标中走动
// 状态机：Idle <-> Walking。由 PlaceOnPlane 在模型放置时挂载到 Placement Root 并注入依赖；
// 外部通过 WalkTo/ReturnToAnchor/LookAtUser/Stop 控制，不自行监听触摸（交互归 PlaceOnPlane）。
// 移动时暂停 CylindricalBillboard（面向运动方向），到达/停止后恢复面向相机；每帧向下 raycast 贴地。
using System.Collections.Generic;
using UnityEngine;
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
    private CylindricalBillboard billboard;
    private LuoMotionAnimation motionAnimation;
    private readonly List<ARRaycastHit> hits = new();
    private Vector3? targetPosition;   // null = Idle
    private bool isWalking;

    public bool IsWalking => isWalking;

    /// <summary>由 PlaceOnPlane 注入依赖（raycast 用于贴地探测，billboard 用于行走时暂停朝向）。</summary>
    public void Initialize(ARRaycastManager raycast, CylindricalBillboard facing, LuoMotionAnimation animation = null)
    {
        raycastManager = raycast;
        billboard = facing;
        motionAnimation = animation;
    }

    /// <summary>走到指定世界坐标（PRD: walk(x,z)）。移动中再次调用会更新目标点。</summary>
    public void WalkTo(Vector3 worldPosition)
    {
        targetPosition = worldPosition;
        if (billboard != null)
            billboard.SetPaused(true);
        if (motionAnimation != null)
            motionAnimation.SetWalking(true);
    }

    /// <summary>回到锚点（PRD: returnToAnchor）。</summary>
    public void ReturnToAnchor(Vector3 anchorPosition)
    {
        WalkTo(anchorPosition);
    }

    /// <summary>停止移动并恢复面向相机（PRD: lookAtUser）。</summary>
    public void LookAtUser()
    {
        Stop();
    }

    /// <summary>立即停止移动，恢复 billboard 面向相机。</summary>
    public void Stop()
    {
        targetPosition = null;
        isWalking = false;
        if (billboard != null)
            billboard.SetPaused(false);
        if (motionAnimation != null)
            motionAnimation.SetWalking(false);
    }

    private void Update()
    {
        if (targetPosition == null)
        {
            if (isWalking)
                Stop();
            return;
        }

        var target = targetPosition.Value;
        var position = transform.position;

        Vector3 toTarget = target - position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance > 0.02f)
        {
            isWalking = true;
            RotateToward(toTarget);

            Vector3 move = toTarget.normalized * moveSpeed * Time.deltaTime;
            if (move.magnitude > distance)
                move = toTarget;          // 避免过冲
            transform.position += move;
            KeepOnGround();
        }
        else
        {
            // 到达目标：回 Idle，恢复面向相机，最后再贴地一次。
            targetPosition = null;
            isWalking = false;
            if (billboard != null)
                billboard.SetPaused(false);
            if (motionAnimation != null)
                motionAnimation.SetWalking(false);
            KeepOnGround();
        }
    }

    private void RotateToward(Vector3 direction)
    {
        var targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    /// 向下 raycast：保持脚底贴合真实平面（PRD Phase 2: downward raycast）。
    private void KeepOnGround()
    {
        if (raycastManager == null)
            return;

        var probeOrigin = transform.position + Vector3.up * groundProbeOffset;
        if (raycastManager.Raycast(new Ray(probeOrigin, Vector3.down), hits, TrackableType.PlaneWithinPolygon))
        {
            var p = transform.position;
            p.y = hits[0].pose.position.y;
            transform.position = p;
        }
        hits.Clear();
    }
}

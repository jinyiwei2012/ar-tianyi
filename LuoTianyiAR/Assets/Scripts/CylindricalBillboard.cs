// CylindricalBillboard.cs — PRD 第 2 节: 只绕世界竖直轴转向相机。
// Cubism 模型的可见正面朝本地 -Z（SDK 示例相机位于模型的 -Z 侧），
// 因此必须让 transform.back 而不是 transform.forward 指向相机。
using UnityEngine;

public class CylindricalBillboard : MonoBehaviour
{
    private Transform cameraTransform;
    private bool isPaused;

    private void Start()
    {
        ResolveCamera();
        FaceCameraNow();
    }

    private void LateUpdate()
    {
        if (!isPaused)
            FaceCameraNow();
    }

    /// <summary>
    /// 暂停/恢复面向相机（供 LuoMovement 在行走时接管朝向，停止后恢复面向相机）。
    /// </summary>
    public void SetPaused(bool paused)
    {
        isPaused = paused;
        if (!paused)
            FaceCameraNow();
    }

    /// <summary>
    /// PlaceOnPlane 在实例化后注入实际 AR 相机，避免依赖 Camera.main 的查找时机。
    /// </summary>
    public void SetCamera(Camera camera)
    {
        cameraTransform = camera != null ? camera.transform : null;
        FaceCameraNow();
    }

    /// <summary>
    /// 立即校正朝向。重新挂到另一个 Anchor 后也应调用，避免一帧显示背面。
    /// </summary>
    public bool FaceCameraNow()
    {
        if (cameraTransform == null)
            ResolveCamera();
        if (cameraTransform == null)
            return false;

        if (!TryGetFacingRotation(transform.position, cameraTransform.position, out var rotation))
            return false;

        transform.rotation = rotation;
        return true;
    }

    /// <summary>
    /// 计算仅含 yaw 的稳定朝向。返回的 rotation 保证本地 -Z 指向相机、Y 保持世界竖直。
    /// </summary>
    public static bool TryGetFacingRotation(
        Vector3 modelPosition,
        Vector3 cameraPosition,
        out Quaternion rotation)
    {
        // LookRotation 令本地 +Z 指向 forward；Cubism 正面是本地 -Z，
        // 所以 forward 应从相机指向模型，而不是从模型指向相机。
        var cameraToModel = modelPosition - cameraPosition;
        cameraToModel.y = 0f;
        if (cameraToModel.sqrMagnitude < 0.0001f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        rotation = Quaternion.LookRotation(cameraToModel, Vector3.up);
        return true;
    }

    public float FrontFacingDot
    {
        get
        {
            if (cameraTransform == null)
                return float.NaN;

            var toCamera = cameraTransform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.0001f)
                return float.NaN;

            return Vector3.Dot(-transform.forward, toCamera.normalized);
        }
    }

    private void ResolveCamera()
    {
        var camera = Camera.main;
        cameraTransform = camera != null ? camera.transform : null;
    }
}

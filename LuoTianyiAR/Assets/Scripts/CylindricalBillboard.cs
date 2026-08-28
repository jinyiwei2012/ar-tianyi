// CylindricalBillboard.cs — 兼容旧组件名的完整相机朝向 Billboard。
// Cubism 模型的可见正面朝本地 -Z（SDK 示例相机位于模型的 -Z 侧），
// 因此必须让 transform.back 而不是 transform.forward 始终指向相机。
using UnityEngine;

public class CylindricalBillboard : MonoBehaviour
{
    private Transform cameraTransform;

    private void Start()
    {
        ResolveCamera();
        FaceCameraNow();
    }

    private void LateUpdate()
    {
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
    /// 计算完整 3D 朝向。返回的 rotation 保证本地 -Z 指向相机；本地 Y 使用
    /// 世界竖直在模型平面上的投影，既能跟随相机俯仰，又避免手机横滚使角色侧倒。
    /// </summary>
    public static bool TryGetFacingRotation(
        Vector3 modelPosition,
        Vector3 cameraPosition,
        out Quaternion rotation)
    {
        // LookRotation 令本地 +Z 指向 forward；Cubism 正面是本地 -Z，
        // 所以 forward 应从相机指向模型，而不是从模型指向相机。
        var cameraToModel = modelPosition - cameraPosition;
        if (cameraToModel.sqrMagnitude < 0.0001f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        cameraToModel.Normalize();
        Vector3 stableUp = Vector3.ProjectOnPlane(Vector3.up, cameraToModel);
        if (stableUp.sqrMagnitude < 0.0001f)
            stableUp = Vector3.ProjectOnPlane(Vector3.forward, cameraToModel);
        if (stableUp.sqrMagnitude < 0.0001f)
            stableUp = Vector3.right;

        rotation = Quaternion.LookRotation(cameraToModel, stableUp.normalized);
        return true;
    }

    public float FrontFacingDot
    {
        get
        {
            if (cameraTransform == null)
                return float.NaN;

            var toCamera = cameraTransform.position - transform.position;
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

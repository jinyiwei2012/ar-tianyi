// CubismScreenLookController.cs — 将屏幕触摸角度适配到 Cubism 官方 LookAt 链路。
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.LookAt;
using Live2D.Cubism.Rendering;
using UnityEngine;

public interface IModelFocus
{
    void FocusOn(Vector2 yawPitchDegrees);
    void ReleaseFocus();
}

[DisallowMultipleComponent]
public sealed class CubismScreenLookController : MonoBehaviour, IModelFocus
{
    private const string HeadDrawableId = "ArtMesh52";
    private const float DefaultDampingSeconds = 0.15f;

    [SerializeField, Range(5f, 60f)] private float maximumYawDegrees = 30f;
    [SerializeField, Range(5f, 45f)] private float maximumPitchDegrees = 20f;
    [SerializeField, Min(0f)] private float releaseHoldSeconds = 1.5f;

    private CubismModel model;
    private CubismLookController lookController;
    private CubismLookTargetAdapter targetAdapter;
    private CubismRenderer headRenderer;
    private bool configured;
    private float holdUntil;
    private float returnUntil;
    private string state = "neutral";
    private string centerSource = "unavailable";
    private Vector2 lastAnglesDegrees;
    private Vector2 lastNormalizedOffset;
    private Vector2 lastScreenPosition;
    private int configuredParameterCount;

    public bool IsConfigured => configured;

    public string StatusSummary =>
        $"lookAt={(configured ? "on" : "missing")}, state={state}, source=screen_angles, " +
        $"angles={lastAnglesDegrees:F1}deg, normalized={lastNormalizedOffset:F3}, " +
        $"screen={lastScreenPosition:F1}, center={centerSource}, parameters={configuredParameterCount}/6";

    private void Update()
    {
        if (state == "user_hold" && Time.unscaledTime >= holdUntil)
        {
            targetAdapter?.SetActive(false);
            state = "returning";
            returnUntil = Time.unscaledTime + DefaultDampingSeconds * 4f;
        }
        else if (state == "returning" && Time.unscaledTime >= returnUntil)
        {
            state = "neutral";
        }
    }

    public bool Configure(CubismModel cubismModel)
    {
        if (configured)
            return true;

        model = cubismModel != null ? cubismModel : GetComponent<CubismModel>();
        if (model == null || model.Parameters == null)
        {
            Debug.LogError("[LookAt] 找不到 CubismModel 或参数列表，视线功能无法初始化。");
            return false;
        }

        targetAdapter = GetComponent<CubismLookTargetAdapter>() ??
                        gameObject.AddComponent<CubismLookTargetAdapter>();
        lookController = GetComponent<CubismLookController>() ??
                         gameObject.AddComponent<CubismLookController>();
        lookController.Target = targetAdapter;
        lookController.BlendMode = CubismParameterBlendMode.Additive;
        lookController.Damping = DefaultDampingSeconds;

        configuredParameterCount = 0;
        configuredParameterCount += ConfigureParameter("ParamEyeBallX", CubismLookAxis.X);
        configuredParameterCount += ConfigureParameter("ParamAngleZ", CubismLookAxis.X);
        configuredParameterCount += ConfigureParameter("ParamBodyAngleZ", CubismLookAxis.X);
        configuredParameterCount += ConfigureParameter("ParamEyeBallY", CubismLookAxis.Y);
        configuredParameterCount += ConfigureParameter("ParamAngleY", CubismLookAxis.Y);
        configuredParameterCount += ConfigureParameter("ParamBodyAngleY", CubismLookAxis.Y);

        foreach (var renderer in GetComponentsInChildren<CubismRenderer>(true))
        {
            if (renderer.Drawable != null && renderer.Drawable.Id == HeadDrawableId)
            {
                headRenderer = renderer;
                break;
            }
        }

        lookController.enabled = true;
        lookController.Refresh();
        GetComponent<CubismUpdateController>()?.Refresh();
        configured = configuredParameterCount == 6;
        if (configured)
            Debug.Log($"[LookAt] 屏幕角度视线已启用: {StatusSummary}");
        else
            Debug.LogWarning($"[LookAt] 视线参数存在缺项: {StatusSummary}");
        return configured;
    }

    public bool FocusOnScreenPoint(Camera arCamera, Vector2 screenPosition)
    {
        if ((!configured && !Configure(model)) || arCamera == null ||
            !TryGetLookCenterWorld(out Vector3 lookCenterWorld))
            return false;

        Vector3 projectedCenter = arCamera.WorldToScreenPoint(lookCenterWorld);
        if (projectedCenter.z <= arCamera.nearClipPlane)
            return false;

        Vector3 centerDirection = arCamera.transform.InverseTransformDirection(
            arCamera.ScreenPointToRay(projectedCenter).direction);
        Vector3 targetDirection = arCamera.transform.InverseTransformDirection(
            arCamera.ScreenPointToRay(screenPosition).direction);

        float centerYaw = Mathf.Atan2(centerDirection.x, centerDirection.z) * Mathf.Rad2Deg;
        float targetYaw = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;
        float centerPitch = DirectionPitchDegrees(centerDirection);
        float targetPitch = DirectionPitchDegrees(targetDirection);

        lastScreenPosition = screenPosition;
        FocusOn(new Vector2(
            Mathf.DeltaAngle(centerYaw, targetYaw),
            targetPitch - centerPitch));
        return true;
    }

    public void FocusOn(Vector2 yawPitchDegrees)
    {
        if (!configured || targetAdapter == null)
            return;

        lastAnglesDegrees = new Vector2(
            Mathf.Clamp(yawPitchDegrees.x, -maximumYawDegrees, maximumYawDegrees),
            Mathf.Clamp(yawPitchDegrees.y, -maximumPitchDegrees, maximumPitchDegrees));
        lastNormalizedOffset = new Vector2(
            lastAnglesDegrees.x / maximumYawDegrees,
            lastAnglesDegrees.y / maximumPitchDegrees);
        targetAdapter.SetNormalizedOffset(lastNormalizedOffset);
        targetAdapter.SetActive(true);
        state = "user_tracking";
        holdUntil = 0f;
        returnUntil = 0f;
    }

    public void ReleaseFocus()
    {
        if (!configured || targetAdapter == null || !targetAdapter.IsActive())
            return;

        state = "user_hold";
        holdUntil = Time.unscaledTime + releaseHoldSeconds;
    }

    public void CancelFocus()
    {
        if (targetAdapter != null)
            targetAdapter.SetActive(false);
        state = "neutral";
        holdUntil = 0f;
        returnUntil = 0f;
    }

    private int ConfigureParameter(string parameterId, CubismLookAxis inputAxis)
    {
        var parameter = model.Parameters.FindById(parameterId);
        if (parameter == null)
        {
            Debug.LogWarning($"[LookAt] 找不到视线参数 {parameterId}");
            return 0;
        }

        var lookParameter = parameter.GetComponent<CubismLookParameter>() ??
                            parameter.gameObject.AddComponent<CubismLookParameter>();
        lookParameter.Axis = inputAxis;
        lookParameter.Factor = Mathf.Max(
            Mathf.Abs(parameter.MinimumValue),
            Mathf.Abs(parameter.MaximumValue));
        return 1;
    }

    private bool TryGetLookCenterWorld(out Vector3 center)
    {
        if (TryGetRendererWorldBounds(headRenderer, out Bounds headBounds))
        {
            center = headBounds.center;
            centerSource = $"head:{HeadDrawableId}";
            return true;
        }

        bool hasBounds = false;
        Bounds modelBounds = default;
        foreach (var renderer in GetComponentsInChildren<CubismRenderer>(true))
        {
            if (!TryGetRendererWorldBounds(renderer, out Bounds rendererBounds))
                continue;
            if (!hasBounds)
            {
                modelBounds = rendererBounds;
                hasBounds = true;
            }
            else
            {
                modelBounds.Encapsulate(rendererBounds);
            }
        }

        if (hasBounds)
        {
            center = modelBounds.center + Vector3.up * modelBounds.extents.y * 0.35f;
            centerSource = "model_bounds_upper";
            return true;
        }

        center = transform.position;
        centerSource = "model_transform";
        return true;
    }

    private static bool TryGetRendererWorldBounds(CubismRenderer renderer, out Bounds bounds)
    {
        bounds = default;
        if (renderer == null || renderer.Mesh == null || renderer.Mesh.vertexCount == 0)
            return false;

        Bounds localBounds = renderer.Mesh.bounds;
        Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
        Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
        Vector3 worldExtents = new(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        bounds = new Bounds(center, worldExtents * 2f);
        return true;
    }

    private static float DirectionPitchDegrees(Vector3 direction)
    {
        float horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
        return Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg;
    }
}

[DisallowMultipleComponent]
public sealed class CubismLookTargetAdapter : MonoBehaviour, ICubismLookTarget
{
    private Vector2 normalizedOffset;
    private bool active;

    public Vector3 GetPosition()
    {
        return transform.TransformPoint(new Vector3(normalizedOffset.x, normalizedOffset.y, 0f));
    }

    public bool IsActive()
    {
        return active;
    }

    public void SetNormalizedOffset(Vector2 value)
    {
        normalizedOffset = new Vector2(
            Mathf.Clamp(value.x, -1f, 1f),
            Mathf.Clamp(value.y, -1f, 1f));
    }

    public void SetActive(bool value)
    {
        active = value;
    }
}

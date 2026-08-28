// ARMarkerDiagnostics.cs — 用已知尺寸的图像标记独立校验 AR 世界位姿与水平面检测。
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager), typeof(ARRaycastManager), typeof(ARPlaneManager))]
public sealed class ARMarkerDiagnostics : MonoBehaviour
{
    public const string DefaultMarkerName = "LuoTianyiDeskMarkerV1";

    [SerializeField] private string expectedMarkerName = DefaultMarkerName;
    [SerializeField, Min(0.01f)] private float expectedMarkerWidthMeters = 0.12f;
    [SerializeField, Min(0.001f)] private float acceptableHeightErrorMeters = 0.03f;
    [SerializeField, Min(0.1f)] private float acceptableNormalErrorDegrees = 5f;
    [SerializeField, Min(0.01f)] private float axisLengthMeters = 0.04f;
    [Tooltip("显式引用以防 Android 构建剥离运行时 Shader.Find 所需的 URP Shader。")]
    [SerializeField] private Material diagnosticMaterial;

    private readonly List<ARRaycastHit> planeHits = new();
    private ARTrackedImageManager trackedImageManager;
    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARTrackedImage activeImage;
    private GameObject visualRoot;

    private TrackingState markerTrackingState = TrackingState.None;
    private bool hasEverDetectedMarker;
    private bool hasPlaneComparison;
    private float signedHeightErrorMeters = float.NaN;
    private float lateralErrorMeters = float.NaN;
    private float normalErrorDegrees = float.NaN;
    private TrackableId comparedPlaneId = TrackableId.invalidId;
    private Vector3 markerNormal;
    private Vector3 markerPosition;
    private Vector3 planeHitPosition;
    private Pose firstTrackingPose;
    private bool hasFirstTrackingPose;
    private float positionDeltaFromFirstMeters;
    private float rotationDeltaFromFirstDegrees;
    private bool? lastLoggedComparisonResult;
    private TrackingState lastLoggedTrackingState = TrackingState.None;

    public bool HasEverDetectedMarker => hasEverDetectedMarker;
    public bool IsMarkerTracked => activeImage != null && markerTrackingState == TrackingState.Tracking;
    public bool HasPlaneComparison => hasPlaneComparison;
    public bool PlaneComparisonPasses =>
        hasPlaneComparison &&
        Mathf.Abs(signedHeightErrorMeters) <= acceptableHeightErrorMeters &&
        normalErrorDegrees <= acceptableNormalErrorDegrees;
    public float HeightErrorCentimeters => Mathf.Abs(signedHeightErrorMeters) * 100f;
    public float LateralErrorCentimeters => lateralErrorMeters * 100f;
    public float NormalErrorDegrees => normalErrorDegrees;
    public TrackingState MarkerTrackingState => markerTrackingState;

    public bool TryGetTrackedMarkerPose(out Pose pose)
    {
        if (!IsMarkerTracked)
        {
            pose = default;
            return false;
        }

        pose = new Pose(activeImage.transform.position, activeImage.transform.rotation);
        return true;
    }

    private void Awake()
    {
        trackedImageManager = GetComponent<ARTrackedImageManager>();
        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();
    }

    private void OnEnable()
    {
        trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
    }

    private void OnDisable()
    {
        trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
        SetVisualsActive(false);
    }

    private void Update()
    {
        if (activeImage == null)
        {
            markerTrackingState = TrackingState.None;
            hasPlaneComparison = false;
            SetVisualsActive(false);
            return;
        }

        markerTrackingState = activeImage.trackingState;
        bool tracking = markerTrackingState == TrackingState.Tracking;
        SetVisualsActive(tracking);
        LogTrackingStateIfChanged();
        if (!tracking)
        {
            hasPlaneComparison = false;
            return;
        }

        markerPosition = activeImage.transform.position;
        markerNormal = GetMarkerNormalFacingCamera(activeImage.transform);
        UpdateDriftDiagnostics();
        UpdatePlaneComparison();
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> changes)
    {
        foreach (var image in changes.added)
            ConsiderTrackedImage(image);

        foreach (var image in changes.updated)
            ConsiderTrackedImage(image);

        foreach (var removed in changes.removed)
        {
            if (activeImage != null && removed.Key == activeImage.trackableId)
            {
                Debug.Log($"[ARMarker] 定位卡已移除: {activeImage.trackableId}");
                activeImage = null;
                markerTrackingState = TrackingState.None;
                hasPlaneComparison = false;
                SetVisualsActive(false);
            }
        }
    }

    private void ConsiderTrackedImage(ARTrackedImage image)
    {
        if (image == null || image.referenceImage.name != expectedMarkerName)
            return;

        if (activeImage != image)
        {
            activeImage = image;
            hasEverDetectedMarker = true;
            hasFirstTrackingPose = false;
            lastLoggedComparisonResult = null;
            AttachVisuals(image.transform);
            Debug.Log(
                $"[ARMarker] 已识别定位卡: name={image.referenceImage.name}, " +
                $"id={image.trackableId}, referenceSize={image.referenceImage.size:F3}m, " +
                $"reportedSize={image.size:F3}m");
        }

        markerTrackingState = image.trackingState;
    }

    private void UpdateDriftDiagnostics()
    {
        var currentPose = new Pose(activeImage.transform.position, activeImage.transform.rotation);
        if (!hasFirstTrackingPose)
        {
            firstTrackingPose = currentPose;
            hasFirstTrackingPose = true;
        }

        positionDeltaFromFirstMeters = Vector3.Distance(firstTrackingPose.position, currentPose.position);
        rotationDeltaFromFirstDegrees = Quaternion.Angle(firstTrackingPose.rotation, currentPose.rotation);
    }

    private void UpdatePlaneComparison()
    {
        hasPlaneComparison = false;
        planeHits.Clear();
        var camera = Camera.main;
        if (camera == null || raycastManager == null)
            return;

        Vector3 screenPoint3 = camera.WorldToScreenPoint(markerPosition);
        if (screenPoint3.z <= 0f ||
            screenPoint3.x < 0f || screenPoint3.x > Screen.width ||
            screenPoint3.y < 0f || screenPoint3.y > Screen.height)
            return;

        var screenPoint = new Vector2(screenPoint3.x, screenPoint3.y);
        if (!raycastManager.Raycast(screenPoint, planeHits, TrackableType.PlaneWithinPolygon))
            return;

        // 使用命中位置最接近定位卡中心的平面，避免重叠平面时误取前景结果。
        ARRaycastHit bestHit = planeHits[0];
        float bestDistance = Vector3.Distance(bestHit.pose.position, markerPosition);
        for (int i = 1; i < planeHits.Count; i++)
        {
            float distance = Vector3.Distance(planeHits[i].pose.position, markerPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHit = planeHits[i];
            }
        }

        planeHits.Clear();
        var plane = planeManager != null ? planeManager.GetPlane(bestHit.trackableId) : null;
        if (plane == null || plane.alignment == PlaneAlignment.Vertical)
            return;

        planeHitPosition = bestHit.pose.position;
        comparedPlaneId = bestHit.trackableId;
        Vector3 planeNormal = bestHit.pose.up.normalized;
        if (Vector3.Dot(planeNormal, markerNormal) < 0f)
            planeNormal = -planeNormal;

        Vector3 delta = planeHitPosition - markerPosition;
        signedHeightErrorMeters = Vector3.Dot(delta, markerNormal);
        lateralErrorMeters = Vector3.ProjectOnPlane(delta, markerNormal).magnitude;
        normalErrorDegrees = Vector3.Angle(planeNormal, markerNormal);
        hasPlaneComparison = true;

        bool passes = PlaneComparisonPasses;
        if (lastLoggedComparisonResult != passes)
        {
            lastLoggedComparisonResult = passes;
            Debug.Log(
                $"[ARMarker] 平面对比 {(passes ? "通过" : "超差")}: " +
                $"height={HeightErrorCentimeters:F1}cm, lateral={LateralErrorCentimeters:F1}cm, " +
                $"normal={normalErrorDegrees:F1}deg, plane={comparedPlaneId}");
        }
    }

    private static Vector3 GetMarkerNormalFacingCamera(Transform markerTransform)
    {
        Vector3 normal = markerTransform.up.normalized;
        var camera = Camera.main;
        if (camera != null && Vector3.Dot(normal, camera.transform.position - markerTransform.position) < 0f)
            normal = -normal;
        return normal;
    }

    private void LogTrackingStateIfChanged()
    {
        if (markerTrackingState == lastLoggedTrackingState)
            return;

        lastLoggedTrackingState = markerTrackingState;
        Debug.Log($"[ARMarker] 定位卡追踪状态: {markerTrackingState}");
    }

    private void AttachVisuals(Transform markerTransform)
    {
        EnsureVisuals();
        visualRoot.transform.SetParent(markerTransform, false);
        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
        visualRoot.transform.localScale = Vector3.one;
    }

    private void EnsureVisuals()
    {
        if (visualRoot != null)
            return;

        visualRoot = new GameObject("AR Marker Diagnostic Axes");
        float width = expectedMarkerWidthMeters;
        float edgeThickness = 0.0015f;
        float lift = 0.002f;

        CreateBar("Marker Top", new Vector3(0f, lift, width * 0.5f), new Vector3(width, edgeThickness, edgeThickness), Color.white);
        CreateBar("Marker Bottom", new Vector3(0f, lift, -width * 0.5f), new Vector3(width, edgeThickness, edgeThickness), Color.white);
        CreateBar("Marker Left", new Vector3(-width * 0.5f, lift, 0f), new Vector3(edgeThickness, edgeThickness, width), Color.white);
        CreateBar("Marker Right", new Vector3(width * 0.5f, lift, 0f), new Vector3(edgeThickness, edgeThickness, width), Color.white);

        CreateBar("Axis +X", new Vector3(axisLengthMeters * 0.5f, lift * 2f, 0f), new Vector3(axisLengthMeters, edgeThickness * 1.5f, edgeThickness * 1.5f), Color.red);
        CreateBar("Axis +Y Normal", new Vector3(0f, axisLengthMeters * 0.5f, 0f), new Vector3(edgeThickness * 1.5f, axisLengthMeters, edgeThickness * 1.5f), Color.green);
        CreateBar("Axis +Z Top", new Vector3(0f, lift * 2f, axisLengthMeters * 0.5f), new Vector3(edgeThickness * 1.5f, edgeThickness * 1.5f, axisLengthMeters), Color.blue);
        SetVisualsActive(false);
    }

    private void CreateBar(string objectName, Vector3 localPosition, Vector3 localScale, Color color)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = objectName;
        bar.transform.SetParent(visualRoot.transform, false);
        bar.transform.localPosition = localPosition;
        bar.transform.localRotation = Quaternion.identity;
        bar.transform.localScale = localScale;

        var collider = bar.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = bar.GetComponent<Renderer>();
        if (diagnosticMaterial != null)
            renderer.sharedMaterial = diagnosticMaterial;

        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        renderer.SetPropertyBlock(properties);
    }

    private void SetVisualsActive(bool active)
    {
        if (visualRoot != null && visualRoot.activeSelf != active)
            visualRoot.SetActive(active);
    }

    public string GetShortStatus()
    {
        if (!hasEverDetectedMarker)
            return "未识别";
        if (!IsMarkerTracked)
            return markerTrackingState.ToString();
        if (!hasPlaneComparison)
            return "已锁定，尚无同位置平面";
        return $"{(PlaneComparisonPasses ? "一致" : "超差")} H={HeightErrorCentimeters:F1}cm N={normalErrorDegrees:F1}°";
    }

    public string GetDebugSnapshot()
    {
        var report = new StringBuilder(512);
        report.AppendLine($"marker={expectedMarkerName}, expectedWidth={expectedMarkerWidthMeters:F3}m");
        report.AppendLine($"detected={hasEverDetectedMarker}, state={markerTrackingState}, activeId={(activeImage != null ? activeImage.trackableId.ToString() : "none")}");
        if (activeImage != null)
        {
            report.AppendLine($"reportedSize={activeImage.size:F3}m, position={markerPosition:F3}, rotation={activeImage.transform.eulerAngles:F1}");
            report.AppendLine($"normalTowardCamera={markerNormal:F3}, firstPoseDelta={positionDeltaFromFirstMeters * 100f:F2}cm/{rotationDeltaFromFirstDegrees:F2}deg");
        }

        if (hasPlaneComparison)
        {
            report.AppendLine(
                $"planeComparison={(PlaneComparisonPasses ? "pass" : "fail")}, plane={comparedPlaneId}, hit={planeHitPosition:F3}, " +
                $"signedHeight={signedHeightErrorMeters * 100f:F2}cm, lateral={lateralErrorMeters * 100f:F2}cm, normal={normalErrorDegrees:F2}deg");
        }
        else
        {
            report.AppendLine("planeComparison=unavailable");
        }

        return report.ToString().TrimEnd();
    }
}

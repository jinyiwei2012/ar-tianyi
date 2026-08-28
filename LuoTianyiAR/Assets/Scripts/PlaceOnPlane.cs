// PlaceOnPlane.cs — PRD P0: 在真实水平面上放置/拖动洛天依，并以世界米为单位缩放。
using System.Collections;
using System.Collections.Generic;
using Live2D.Cubism.Rendering;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager), typeof(ARPlaneManager), typeof(ARAnchorManager))]
public class PlaceOnPlane : MonoBehaviour
{
    [Header("模型")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("首次放置时，洛天依在 AR 世界中的身高（米）")]
    [SerializeField, Min(0.05f)] private float targetHeightMeters = 0.6f;
    [SerializeField, Min(0.05f)] private float minimumHeightMeters = 0.08f;
    [SerializeField, Min(0.05f)] private float maximumHeightMeters = 1.5f;
    [Tooltip("首次放置时模型最多占屏幕高度的比例；离平面很近时会自动缩小。")]
    [SerializeField, Range(0.15f, 0.8f)] private float maximumInitialViewportHeight = 0.38f;
    [Tooltip("首次放置时模型最多占屏幕宽度的比例。")]
    [SerializeField, Range(0.15f, 0.9f)] private float maximumInitialViewportWidth = 0.55f;
    [Tooltip("用于确定真实脚底的 Live2D 左腿 Drawable Id。")]
    [SerializeField] private string leftLegDrawableId = "ArtMesh97";
    [Tooltip("用于确定真实脚底的 Live2D 右腿 Drawable Id。")]
    [SerializeField] private string rightLegDrawableId = "ArtMesh98";
    [Tooltip("Cubism 网格在生成后的第一帧才可用；超过此帧数视为加载失败。")]
    [SerializeField, Min(1)] private int modelInitializationFrameLimit = 120;
    [Tooltip("已放置模型后，单指移动超过该像素距离才进入拖动，避免普通点击偷换 Anchor。")]
    [SerializeField, Min(0f)] private float dragStartThresholdPixels = 32f;

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARAnchorManager anchorManager;
    private PlacementGuideUI guideUI;
    private ARMarkerDiagnostics markerDiagnostics;
    private readonly List<ARRaycastHit> hits = new();

    private ARAnchor currentAnchor;
    private GameObject modelPoseRoot;
    private GameObject spawnedModel;
    private CylindricalBillboard billboard;
    private Live2DModelFeatures live2DFeatures;
    private Vector3 scalePerMeter;
    private Coroutine modelInitialization;
    private bool modelReady;
    private string modelLoadFailure;
    private int placementFingerIndex = -1;
    private Vector2 placementFingerDownPosition;
    private bool placementFingerDragging;
    private float pinchStartDistance;
    private float pinchStartHeight;
    private Vector2 lastRequestedScreenPosition;
    private Vector2 lastProjectedHitPosition;
    private float lastProjectionErrorPixels = float.NaN;
    private Vector3 lastFootCenter;
    private float lastFootAlignmentErrorMeters = float.NaN;
    private string lastFootAlignmentSource = "unavailable";
    private Vector3 lastCameraPositionAtPlacement;
    private Vector3 lastHitPosition;
    private Vector3 lastAnchorPositionAtCreation;
    private float lastCameraToHitMeters = float.NaN;
    private float lastAnchorToHitMeters = float.NaN;
    private Vector2 lastInitialViewportCoverage;
    private float lastRequestedInitialHeightMeters = float.NaN;
    private bool anchorCreationPending;
    private int placementRequestVersion;
    private Vector3 manualWorldOffset;
    private bool positionLocked;

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();
        anchorManager = GetComponent<ARAnchorManager>();
        guideUI = GetComponent<PlacementGuideUI>();
        markerDiagnostics = GetComponent<ARMarkerDiagnostics>();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerMove += OnFingerMove;
        Touch.onFingerUp += OnFingerUp;
    }

    private void OnDisable()
    {
        placementRequestVersion++;
        anchorCreationPending = false;
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerMove -= OnFingerMove;
        Touch.onFingerUp -= OnFingerUp;
        live2DFeatures?.CancelUserFocus();
        EnhancedTouchSupport.Disable();
    }

    private void Start()
    {
        if (modelPrefab == null)
            Debug.LogError("[PlaceOnPlane] 未指定 modelPrefab；请运行 ARSceneSetup.ConfigureARScene。");
    }

    private void Update()
    {
        UpdatePinchScale();
    }

    private void OnFingerDown(Finger finger)
    {
        if (RuntimeDebugPanel.IsPointerOverDebugUI(finger.screenPosition) ||
            ModelNudgeUI.IsPointerOverNudgeUI(finger.screenPosition) ||
            ExpressionCycleUI.IsPointerOverExpressionUI(finger.screenPosition) ||
            PositionLockUI.IsPointerOverLockUI(finger.screenPosition))
            return;

        if (Touch.activeTouches.Count > 1)
            return;

        placementFingerIndex = finger.index;
        placementFingerDownPosition = finger.screenPosition;
        placementFingerDragging = false;

        // 首次放置采用固定中心准星：点击只是“确认”，不再把模型放到手指位置。
        // 已放置后，FingerDown 只准备拖动，避免一次普通点击让模型瞬移。
        if (spawnedModel == null && !anchorCreationPending)
        {
            var target = GetInitialPlacementScreenPosition();
            guideUI?.ShowTap(target);
            TryPlaceAtScreenPosition(target, true);
        }
        else if (positionLocked && modelReady)
        {
            guideUI?.ShowGaze(finger.screenPosition);
            live2DFeatures?.FocusOnScreenPoint(Camera.main, finger.screenPosition);
        }
    }

    private void OnFingerMove(Finger finger)
    {
        if (RuntimeDebugPanel.IsOpen)
            return;

        if (finger.index != placementFingerIndex || Touch.activeTouches.Count != 1)
            return;

        if (spawnedModel == null || !modelReady)
            return;

        if (positionLocked)
        {
            guideUI?.ShowGaze(finger.screenPosition);
            live2DFeatures?.FocusOnScreenPoint(Camera.main, finger.screenPosition);
            return;
        }

        if (!placementFingerDragging)
        {
            if (Vector2.Distance(finger.screenPosition, placementFingerDownPosition) < dragStartThresholdPixels)
                return;

            placementFingerDragging = true;
            guideUI?.ShowTap(finger.screenPosition);
        }

        // 超过阈值后才持续更新位置；反馈只在开始拖动时显示一次。
        TryPlaceAtScreenPosition(finger.screenPosition, false);
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger.index == placementFingerIndex)
        {
            if (positionLocked)
                live2DFeatures?.ReleaseUserFocus();
            placementFingerIndex = -1;
            placementFingerDragging = false;
        }
    }

    private async void TryPlaceAtScreenPosition(Vector2 screenPosition, bool showFeedback)
    {
        if (modelPrefab == null)
        {
            ReportFailure(screenPosition, showFeedback, "模型资源未配置，请重新安装应用。");
            return;
        }

        if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            hits.Clear();
            ReportFailure(screenPosition, showFeedback, "这里还没有识别到水平平面，请继续扫描后再试。");
            return;
        }

        var hit = hits[0];
        hits.Clear();
        UpdateHitProjectionDiagnostics(screenPosition, hit.pose.position);
        var plane = planeManager.GetPlane(hit.trackableId);
        if (plane == null || plane.alignment == PlaneAlignment.Vertical)
        {
            ReportFailure(screenPosition, showFeedback, "该位置不是可用的水平平面。");
            return;
        }

        // 使用独立 Pose Anchor，而不是附着在仍会被 ARCore 持续细化的 Plane 上。
        // 创建 Anchor 是异步的；同时显式保存 raycast 世界 Pose，避免刚创建的
        // ARAnchor Transform 尚未同步时把模型错误归零到 XR Origin/手机初始位置。
        anchorCreationPending = true;
        int requestVersion = ++placementRequestVersion;
        var anchorResult = await anchorManager.TryAddAnchorAsync(hit.pose);
        anchorCreationPending = false;

        if (this == null || !isActiveAndEnabled || requestVersion != placementRequestVersion)
        {
            if (anchorResult.status.IsSuccess() && anchorResult.value != null)
                anchorManager.TryRemoveAnchor(anchorResult.value);
            return;
        }

        if (!anchorResult.status.IsSuccess() || anchorResult.value == null)
        {
            Debug.LogWarning($"[PlaceOnPlane] 无法创建世界 Pose Anchor: {anchorResult.status}");
            ReportFailure(screenPosition, showFeedback, "无法在该位置建立空间锚点，请换一个位置重试。");
            return;
        }

        var newAnchor = anchorResult.value;
        var hitPose = hit.pose;
        var camera = Camera.main;
        lastHitPosition = hitPose.position;
        lastAnchorPositionAtCreation = newAnchor.transform.position;
        lastCameraPositionAtPlacement = camera != null ? camera.transform.position : default;
        lastAnchorToHitMeters = Vector3.Distance(lastAnchorPositionAtCreation, lastHitPosition);
        lastCameraToHitMeters = camera != null
            ? Vector3.Distance(lastCameraPositionAtPlacement, lastHitPosition)
            : float.NaN;

        bool wasAlreadyPlaced = modelPoseRoot != null;
        var previousAnchor = currentAnchor;
        currentAnchor = newAnchor;

        if (spawnedModel == null)
        {
            manualWorldOffset = Vector3.zero;
            positionLocked = false;
            // Anchor/朝向/移动由稳定的 Pose Root 承担；Live2D 作为子对象做枢轴校正。
            // 这样模型资源本身的非中心 pivot 不会让可见脚底偏离命中点，billboard
            // 旋转也始终围绕真正的落点，而不是围绕 Cubism prefab 原点。
            modelPoseRoot = new GameObject("LuoTianyi Placement Root");
            modelPoseRoot.transform.SetPositionAndRotation(hitPose.position, Quaternion.identity);
            modelPoseRoot.transform.SetParent(currentAnchor.transform, true);
            modelPoseRoot.transform.localScale = Vector3.one;
            billboard = modelPoseRoot.AddComponent<CylindricalBillboard>();
            billboard.SetCamera(Camera.main);

            spawnedModel = Instantiate(modelPrefab, modelPoseRoot.transform);
            spawnedModel.name = "LuoTianyi (AR)";
            spawnedModel.transform.localPosition = Vector3.zero;
            live2DFeatures = spawnedModel.GetComponent<Live2DModelFeatures>() ??
                             spawnedModel.AddComponent<Live2DModelFeatures>();
            SetModelRenderersEnabled(false);
            modelReady = false;
            modelLoadFailure = null;
            modelInitialization = StartCoroutine(InitializeModel(screenPosition, plane.trackableId));
            Debug.Log(
                $"[PlaceOnPlane] 中心准星锚点已建立，正在等待 Cubism 网格初始化: {plane.trackableId} | " +
                $"screen={screenPosition:F1}, projectedHit={lastProjectedHitPosition:F1}, " +
                $"pixelError={lastProjectionErrorPixels:F1}, cameraToHit={lastCameraToHitMeters:F3}m, " +
                $"anchorToHit={lastAnchorToHitMeters:F3}m");
        }
        else
        {
            manualWorldOffset = Vector3.zero;
            modelPoseRoot.transform.SetParent(currentAnchor.transform, true);
            modelPoseRoot.transform.position = hitPose.position;
            modelPoseRoot.transform.localScale = Vector3.one;
            billboard?.FaceCameraNow();
            if (modelReady)
                PlaceFeetOnAnchor();
        }

        if (previousAnchor != null)
            anchorManager.TryRemoveAnchor(previousAnchor);

        if (showFeedback)
        {
            guideUI?.ReportPlacement(
                screenPosition,
                true,
                wasAlreadyPlaced ? "位置已更新。" : "锚点已建立，正在加载洛天依…");
        }
    }

    private IEnumerator InitializeModel(Vector2 feedbackPosition, TrackableId planeId)
    {
        // CubismRenderer 会在初始化时创建自己的运行时 Mesh。SDK 5 的 MeshFilter
        // 仅在 UNITY_EDITOR 下用于 Scene View picking，真机上 sharedMesh 按设计始终为空。
        for (int frame = 0; frame < modelInitializationFrameLimit; frame++)
        {
            yield return null;
            if (spawnedModel == null)
                yield break;

            if (!TryGetModelBounds(out var bounds))
                continue;

            float originalHeight = bounds.size.y;
            if (!float.IsFinite(originalHeight) || originalHeight <= 0.001f)
                continue;

            float modelAspect = bounds.size.x / originalHeight;
            lastRequestedInitialHeightMeters = targetHeightMeters;
            targetHeightMeters = CalculateAdaptiveInitialHeight(targetHeightMeters, modelAspect);
            spawnedModel.transform.localScale *= targetHeightMeters / originalHeight;
            scalePerMeter = spawnedModel.transform.localScale / targetHeightMeters;
            modelReady = true;
            modelLoadFailure = null;
            SetModelRenderersEnabled(true);
            PlaceFeetOnAnchor();
            modelInitialization = null;

            Debug.Log(
                $"[PlaceOnPlane] Cubism 模型已就绪，平面 {planeId}，" +
                $"原始高度 {originalHeight:F3}，请求身高 {lastRequestedInitialHeightMeters:F2}m，" +
                $"自适应身高 {targetHeightMeters:F2}m，屏幕占比 " +
                $"{lastInitialViewportCoverage.x * 100f:F1}%x{lastInitialViewportCoverage.y * 100f:F1}%，" +
                $"脚底中心误差 {lastFootAlignmentErrorMeters * 100f:F2}cm，" +
                $"落点重投影误差 {lastProjectionErrorPixels:F1}px");
            Debug.Log("[PlaceOnPlane] 运行时快照\n" + GetDebugSnapshot());
            guideUI?.ReportPlacement(
                feedbackPosition,
                true,
                "洛天依已加载：可以拖动位置或双指调整大小。");
            yield break;
        }

        modelInitialization = null;
        modelReady = false;
        modelLoadFailure = "模型网格初始化超时，请重启应用后重试。";
        SetModelRenderersEnabled(false);
        Debug.LogError(
            $"[PlaceOnPlane] Cubism 模型初始化超时（{modelInitializationFrameLimit} 帧）；" +
            $"Renderer={spawnedModel.GetComponentsInChildren<Renderer>(true).Length}, " +
            $"CubismRenderer={spawnedModel.GetComponentsInChildren<CubismRenderer>(true).Length}, " +
            $"RuntimeMesh={CountRuntimeMeshes()}");
        guideUI?.ReportPlacement(feedbackPosition, false, modelLoadFailure);
        RuntimeDebugPanel.Open("Cubism 模型网格初始化超时");
    }

    private void ReportFailure(Vector2 screenPosition, bool showFeedback, string message)
    {
        if (showFeedback)
            guideUI?.ReportPlacement(screenPosition, false, message);
    }

    private void UpdatePinchScale()
    {
        if (RuntimeDebugPanel.IsOpen || positionLocked || spawnedModel == null ||
            !modelReady || Touch.activeTouches.Count < 2)
        {
            pinchStartDistance = 0f;
            return;
        }

        var first = Touch.activeTouches[0].screenPosition;
        var second = Touch.activeTouches[1].screenPosition;
        float distance = Vector2.Distance(first, second);
        if (pinchStartDistance <= 0f)
        {
            pinchStartDistance = Mathf.Max(distance, 1f);
            pinchStartHeight = targetHeightMeters;
            placementFingerIndex = -1;
            placementFingerDragging = false;
            return;
        }

        targetHeightMeters = Mathf.Clamp(
            pinchStartHeight * distance / pinchStartDistance,
            minimumHeightMeters,
            maximumHeightMeters);
        spawnedModel.transform.localScale = scalePerMeter * targetHeightMeters;
        PlaceFeetOnAnchor();
    }

    private void PlaceFeetOnAnchor()
    {
        if (spawnedModel == null || modelPoseRoot == null || currentAnchor == null)
            return;

        // 先恢复 prefab 原点，再按缩放/朝向后的腿部 runtime bounds 计算真实脚底。
        // 不能使用整个模型 bounds.min：长发、裙摆等 Drawable 可能低于鞋底，
        // 以其最低点落地会让角色的双脚悬在平面上方。
        spawnedModel.transform.localPosition = Vector3.zero;
        bool hasLegBounds = TryGetFootDrawableBounds(out var footBounds);
        if (!hasLegBounds)
            TryGetModelBounds(out footBounds);

        if (footBounds.size.sqrMagnitude > 0f)
        {
            lastFootAlignmentSource = hasLegBounds
                ? $"legs({leftLegDrawableId},{rightLegDrawableId})"
                : "fallback-model-bounds";
            var footCenter = new Vector3(
                footBounds.center.x,
                footBounds.min.y,
                footBounds.center.z);
            var correction = modelPoseRoot.transform.position - footCenter;
            spawnedModel.transform.position += correction;

            Bounds alignedFootBounds;
            bool hasAlignedLegBounds = TryGetFootDrawableBounds(out alignedFootBounds);
            if (!hasAlignedLegBounds)
                TryGetModelBounds(out alignedFootBounds);
            if (alignedFootBounds.size.sqrMagnitude > 0f)
            {
                lastFootCenter = new Vector3(
                    alignedFootBounds.center.x,
                    alignedFootBounds.min.y,
                    alignedFootBounds.center.z);
                lastFootAlignmentErrorMeters = Vector3.Distance(
                    lastFootCenter,
                    modelPoseRoot.transform.position);
            }
        }
    }

    private bool TryGetFootDrawableBounds(out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;
        if (spawnedModel == null)
            return false;

        foreach (var renderer in spawnedModel.GetComponentsInChildren<CubismRenderer>(true))
        {
            string drawableId = renderer.Drawable != null ? renderer.Drawable.Id : null;
            if (drawableId != leftLegDrawableId && drawableId != rightLegDrawableId)
                continue;

            var mesh = renderer.Mesh;
            if (mesh == null || mesh.vertexCount == 0)
                continue;

            var rendererBounds = TransformBounds(mesh.bounds, renderer.transform.localToWorldMatrix);
            if (!hasBounds)
            {
                bounds = rendererBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return hasBounds && float.IsFinite(bounds.size.y) && bounds.size.y > 0.001f;
    }

    private static Vector2 GetScreenCenter()
    {
        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    private Vector2 GetInitialPlacementScreenPosition()
    {
        var camera = Camera.main;
        if (camera == null || markerDiagnostics == null ||
            !markerDiagnostics.TryGetTrackedMarkerPose(out var markerPose))
            return GetScreenCenter();

        Vector3 screen = camera.WorldToScreenPoint(markerPose.position);
        if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width ||
            screen.y < 0f || screen.y > Screen.height)
            return GetScreenCenter();

        var markerScreenPosition = new Vector2(screen.x, screen.y);
        Debug.Log(
            $"[PlaceOnPlane] 定位卡正在追踪，首次放置目标改为二维码中心: " +
            $"world={markerPose.position:F3}, screen={markerScreenPosition:F1}");
        return markerScreenPosition;
    }

    private float CalculateAdaptiveInitialHeight(float requestedHeight, float modelAspect)
    {
        requestedHeight = Mathf.Clamp(requestedHeight, minimumHeightMeters, maximumHeightMeters);
        modelAspect = Mathf.Clamp(modelAspect, 0.1f, 4f);
        billboard?.FaceCameraNow();

        if (FitsInitialViewport(requestedHeight, modelAspect, out lastInitialViewportCoverage))
            return requestedHeight;

        float low = Mathf.Min(minimumHeightMeters, requestedHeight);
        float high = requestedHeight;
        if (!FitsInitialViewport(low, modelAspect, out lastInitialViewportCoverage))
            return low;

        // 投影并非严格线性（尤其相机俯视桌面时），用二分法求当前构图下
        // 能完整留在画面内的最大初始世界高度。
        for (int i = 0; i < 16; i++)
        {
            float candidate = (low + high) * 0.5f;
            if (FitsInitialViewport(candidate, modelAspect, out _))
                low = candidate;
            else
                high = candidate;
        }

        FitsInitialViewport(low, modelAspect, out lastInitialViewportCoverage);
        return low;
    }

    private bool FitsInitialViewport(float heightMeters, float modelAspect, out Vector2 coverage)
    {
        coverage = default;
        var camera = Camera.main;
        if (camera == null || modelPoseRoot == null)
            return true;

        Vector3 foot = modelPoseRoot.transform.position;
        Vector3 top = foot + Vector3.up * heightMeters;
        Vector3 center = foot + Vector3.up * (heightMeters * 0.5f);
        Vector3 halfWidth = modelPoseRoot.transform.right * (heightMeters * modelAspect * 0.5f);

        Vector3 footViewport = camera.WorldToViewportPoint(foot);
        Vector3 topViewport = camera.WorldToViewportPoint(top);
        Vector3 leftViewport = camera.WorldToViewportPoint(center - halfWidth);
        Vector3 rightViewport = camera.WorldToViewportPoint(center + halfWidth);
        float safeDepth = camera.nearClipPlane * 1.05f;
        if (footViewport.z <= safeDepth || topViewport.z <= safeDepth ||
            leftViewport.z <= safeDepth || rightViewport.z <= safeDepth)
            return false;

        float minX = Mathf.Min(leftViewport.x, rightViewport.x);
        float maxX = Mathf.Max(leftViewport.x, rightViewport.x);
        float minY = Mathf.Min(footViewport.y, topViewport.y, leftViewport.y, rightViewport.y);
        float maxY = Mathf.Max(footViewport.y, topViewport.y, leftViewport.y, rightViewport.y);
        coverage = new Vector2(maxX - minX, maxY - minY);

        const float horizontalMargin = 0.04f;
        const float verticalMargin = 0.05f;
        return coverage.x <= maximumInitialViewportWidth &&
               coverage.y <= maximumInitialViewportHeight &&
               minX >= horizontalMargin && maxX <= 1f - horizontalMargin &&
               minY >= verticalMargin && maxY <= 1f - verticalMargin;
    }

    private void UpdateHitProjectionDiagnostics(Vector2 requestedScreenPosition, Vector3 hitPosition)
    {
        lastRequestedScreenPosition = requestedScreenPosition;
        var camera = Camera.main;
        if (camera == null)
        {
            lastProjectedHitPosition = default;
            lastProjectionErrorPixels = float.NaN;
            return;
        }

        Vector3 projected = camera.WorldToScreenPoint(hitPosition);
        lastProjectedHitPosition = new Vector2(projected.x, projected.y);
        lastProjectionErrorPixels = Vector2.Distance(
            lastProjectedHitPosition,
            requestedScreenPosition);
    }

    private bool TryGetModelBounds(out Bounds bounds)
    {
        var renderers = spawnedModel != null
            ? spawnedModel.GetComponentsInChildren<CubismRenderer>(true)
            : System.Array.Empty<CubismRenderer>();

        bool hasBounds = false;
        bounds = default;
        foreach (var renderer in renderers)
        {
            var mesh = renderer.Mesh;
            if (mesh == null || mesh.vertexCount == 0)
                continue;

            var rendererBounds = TransformBounds(mesh.bounds, renderer.transform.localToWorldMatrix);
            if (!hasBounds)
            {
                bounds = rendererBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return hasBounds && float.IsFinite(bounds.size.y) && bounds.size.y > 0.001f;
    }

    private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 localToWorld)
    {
        Vector3 center = localToWorld.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;
        Vector3 axisX = localToWorld.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = localToWorld.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = localToWorld.MultiplyVector(new Vector3(0f, 0f, extents.z));
        var worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(center, worldExtents * 2f);
    }

    private void SetModelRenderersEnabled(bool enabled)
    {
        if (spawnedModel == null)
            return;

        foreach (var renderer in spawnedModel.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = enabled;
    }

    private int CountRuntimeMeshes()
    {
        if (spawnedModel == null)
            return 0;

        int count = 0;
        foreach (var renderer in spawnedModel.GetComponentsInChildren<CubismRenderer>(true))
        {
            if (renderer.Mesh != null && renderer.Mesh.vertexCount > 0)
                count++;
        }
        return count;
    }

    public bool HasPlacedModel => spawnedModel != null;
    public bool IsModelReady => modelReady;
    public bool IsModelLoading => spawnedModel != null && !modelReady && string.IsNullOrEmpty(modelLoadFailure);
    public bool IsPositionLocked => positionLocked;
    public string ModelLoadFailure => modelLoadFailure;
    public Vector3 ManualWorldOffset => manualWorldOffset;
    public string CurrentExpressionName => live2DFeatures?.CurrentExpressionName ?? "无";
    public string Live2DFeatureStatus => live2DFeatures?.StatusSummary ?? "not_initialized";

    public bool SetPositionLocked(bool locked)
    {
        if (!modelReady || modelPoseRoot == null)
            return false;
        if (positionLocked == locked)
            return true;

        positionLocked = locked;
        placementFingerIndex = -1;
        placementFingerDragging = false;
        pinchStartDistance = 0f;
        live2DFeatures?.CancelUserFocus();
        Debug.Log(
            $"[Interaction] 位置锁定已{(locked ? "启用" : "解除")}，" +
            $"root={modelPoseRoot.transform.position:F3}, height={targetHeightMeters:F3}m");
        return true;
    }

    public bool TryNextExpression(out string displayName)
    {
        displayName = "不可用";
        return modelReady && live2DFeatures != null &&
               live2DFeatures.NextExpression(out displayName);
    }

    public bool NudgeModelWorld(Vector3 worldDelta)
    {
        if (positionLocked || modelPoseRoot == null || !modelReady || !float.IsFinite(worldDelta.sqrMagnitude))
            return false;

        modelPoseRoot.transform.position += worldDelta;
        manualWorldOffset += worldDelta;
        billboard?.FaceCameraNow();
        Debug.Log(
            $"[ModelNudge] worldDelta={worldDelta:F3}, accumulated={manualWorldOffset:F3}, " +
            $"root={modelPoseRoot.transform.position:F3}, {GetMarkerAlignmentSummary()}");
        return true;
    }

    public bool ResetModelWorldNudge()
    {
        if (positionLocked || modelPoseRoot == null || !modelReady)
            return false;

        Vector3 resetDelta = -manualWorldOffset;
        modelPoseRoot.transform.position += resetDelta;
        manualWorldOffset = Vector3.zero;
        billboard?.FaceCameraNow();
        Debug.Log(
            $"[ModelNudge] 累计偏移已归零，worldDelta={resetDelta:F3}, " +
            $"root={modelPoseRoot.transform.position:F3}, {GetMarkerAlignmentSummary()}");
        return true;
    }

    private string GetMarkerAlignmentSummary()
    {
        if (markerDiagnostics == null || modelPoseRoot == null ||
            !markerDiagnostics.TryGetTrackedMarkerPose(out var markerPose))
            return "markerDelta=unavailable";

        Vector3 delta = modelPoseRoot.transform.position - markerPose.position;
        return $"markerDelta={delta:F3}/{delta.magnitude * 100f:F2}cm";
    }

    public string GetDebugSnapshot()
    {
        if (spawnedModel == null)
            return $"state=not_placed, prefab={(modelPrefab != null ? modelPrefab.name : "missing")}, targetHeight={targetHeightMeters:F3}m";

        var renderers = spawnedModel.GetComponentsInChildren<Renderer>(true);
        var cubismRenderers = spawnedModel.GetComponentsInChildren<CubismRenderer>(true);
        int enabledRenderers = 0;
        int validMeshes = 0;
        string shaderName = "missing";
        bool shaderSupported = false;

        foreach (var renderer in renderers)
        {
            if (renderer.enabled)
                enabledRenderers++;
            if (shaderName == "missing" && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
            {
                shaderName = renderer.sharedMaterial.shader.name;
                shaderSupported = renderer.sharedMaterial.shader.isSupported;
            }
        }

        foreach (var cubismRenderer in cubismRenderers)
        {
            if (cubismRenderer.Mesh != null && cubismRenderer.Mesh.vertexCount > 0)
                validMeshes++;
        }

        bool hasBounds = TryGetModelBounds(out var bounds);
        string boundsText = hasBounds
            ? $"center={bounds.center:F3}, size={bounds.size:F3}"
            : "invalid";
        string anchorState = currentAnchor != null ? currentAnchor.trackingState.ToString() : "missing";
        string state = modelReady ? "ready" : IsModelLoading ? "loading" : "failed";
        string facingText = billboard != null && float.IsFinite(billboard.FrontFacingDot)
            ? $"frontDot={billboard.FrontFacingDot:F4}"
            : "frontDot=unknown";
        string projectionText = float.IsFinite(lastProjectionErrorPixels)
            ? $"requested={lastRequestedScreenPosition:F1}, projectedHit={lastProjectedHitPosition:F1}, error={lastProjectionErrorPixels:F1}px"
            : "unavailable";
        string alignmentText = float.IsFinite(lastFootAlignmentErrorMeters)
            ? $"source={lastFootAlignmentSource}, footCenter={lastFootCenter:F3}, error={lastFootAlignmentErrorMeters * 100f:F2}cm"
            : "unavailable";
        var liveCamera = Camera.main;
        string liveSpatialText = liveCamera != null && modelPoseRoot != null
            ? $"camera={liveCamera.transform.position:F3}, root={modelPoseRoot.transform.position:F3}, distance={Vector3.Distance(liveCamera.transform.position, modelPoseRoot.transform.position):F3}m, rootScreen={liveCamera.WorldToScreenPoint(modelPoseRoot.transform.position):F1}"
            : "unavailable";
        string poseRootText = modelPoseRoot != null
            ? $"position={modelPoseRoot.transform.position:F3}, rotation={modelPoseRoot.transform.eulerAngles:F1}"
            : "missing";
        string spatialText = float.IsFinite(lastCameraToHitMeters) && float.IsFinite(lastAnchorToHitMeters)
            ? $"camera={lastCameraPositionAtPlacement:F3}, hit={lastHitPosition:F3}, anchorAtCreation={lastAnchorPositionAtCreation:F3}, cameraToHit={lastCameraToHitMeters:F3}m, anchorToHit={lastAnchorToHitMeters * 100f:F2}cm"
            : "unavailable";
        string initialSizingText = float.IsFinite(lastRequestedInitialHeightMeters)
            ? $"requested={lastRequestedInitialHeightMeters:F3}m, adapted={targetHeightMeters:F3}m, viewport={lastInitialViewportCoverage.x * 100f:F1}%x{lastInitialViewportCoverage.y * 100f:F1}%"
            : "unavailable";
        string markerAlignmentText = markerDiagnostics != null &&
                                     markerDiagnostics.TryGetTrackedMarkerPose(out var liveMarkerPose) &&
                                     modelPoseRoot != null
            ? $"marker={liveMarkerPose.position:F3}, root={modelPoseRoot.transform.position:F3}, delta={Vector3.Distance(liveMarkerPose.position, modelPoseRoot.transform.position) * 100f:F2}cm"
            : "unavailable";

        return
            $"state={state}, failure={(!string.IsNullOrEmpty(modelLoadFailure) ? modelLoadFailure : "none")}\n" +
            $"anchor={anchorState}, targetHeight={targetHeightMeters:F3}m, positionLock={(positionLocked ? "locked" : "adjustable")}\n" +
            $"renderers={renderers.Length}, enabled={enabledRenderers}, cubismRenderers={cubismRenderers.Length}, runtimeMeshes={validMeshes}\n" +
            $"bounds={boundsText}\n" +
            $"poseRoot={poseRootText}\n" +
            $"spatial={spatialText}\n" +
            $"liveSpatial={liveSpatialText}\n" +
            $"initialSizing={initialSizingText}\n" +
            $"markerAlignment={markerAlignmentText}\n" +
            $"manualWorldOffset={manualWorldOffset:F3}/{manualWorldOffset.magnitude * 100f:F2}cm\n" +
            $"visualLocalPosition={spawnedModel.transform.localPosition:F3}, localScale={spawnedModel.transform.localScale:F5}\n" +
            $"alignment={alignmentText}\n" +
            $"projection={projectionText}\n" +
            $"billboard={(billboard != null ? "active" : "missing")}, {facingText}\n" +
            $"gaze={live2DFeatures?.LookStatusSummary ?? "lookAt=not_initialized"}\n" +
            $"live2d={Live2DFeatureStatus}\n" +
            $"shader={shaderName}, supported={shaderSupported}";
    }
}

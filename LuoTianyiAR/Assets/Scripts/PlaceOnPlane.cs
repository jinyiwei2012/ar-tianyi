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
    [SerializeField, Min(0.05f)] private float minimumHeightMeters = 0.2f;
    [SerializeField, Min(0.05f)] private float maximumHeightMeters = 1.5f;
    [Tooltip("Cubism 网格在生成后的第一帧才可用；超过此帧数视为加载失败。")]
    [SerializeField, Min(1)] private int modelInitializationFrameLimit = 120;

    [Header("交互模式")]
    [Tooltip("true=中心准星模式：模型始终放到屏幕中心准星指向处，点击仅作为确认；false=点选模式：模型放到手指点击处。")]
    [SerializeField] private bool centerCrosshairMode = true;
    [Tooltip("已放置后，手指移动超过该像素距离才进入拖动，避免普通点击/微移重建 Anchor。")]
    [SerializeField, Min(1f)] private float dragThresholdPixels = 32f;

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARAnchorManager anchorManager;
    private PlacementGuideUI guideUI;
    private readonly List<ARRaycastHit> hits = new();

    private ARAnchor currentAnchor;
    private GameObject spawnedModel;
    private Vector3 scalePerMeter;
    private Coroutine modelInitialization;
    private bool modelReady;
    private string modelLoadFailure;
    private int placementFingerIndex = -1;
    private float pinchStartDistance;
    private float pinchStartHeight;

    // 稳定根节点层级：Anchor -> LuoTianyi Placement Root -> Live2D（billboard 挂在模型上）。
    private GameObject placementRoot;
    private bool isDragging;
    private Vector2 dragStartScreen;

    // 放置诊断采样（供 RuntimeDebugPanel 报告真实命中/对齐数据）。
    private bool hasPlacementSample;
    private Vector2 lastRequestScreenPoint;
    private Vector3 lastHitPosition;
    private Quaternion lastHitRotation;
    private Vector3 lastHitScreen;
    private float lastScreenErrorPx;
    private Vector3 lastFootCenter;
    private Vector3 lastAnchorPosition;
    private Vector3 lastAlignmentError;
    private readonly List<GameObject> diagnosticMarkers = new();

    // Phase 2 移动：模型放置后挂载到 Placement Root 上，由点击走路 / 拖动接管。
    private LuoMovement luoMovement;

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();
        anchorManager = GetComponent<ARAnchorManager>();
        guideUI = GetComponent<PlacementGuideUI>();
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
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerMove -= OnFingerMove;
        Touch.onFingerUp -= OnFingerUp;
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
        if (RuntimeDebugPanel.IsPointerOverDebugUI(finger.screenPosition))
            return;

        if (Touch.activeTouches.Count > 1)
            return;

        placementFingerIndex = finger.index;
        dragStartScreen = finger.screenPosition;
        isDragging = false;

        // 中心准星模式：固定 raycast 屏幕中心，手指仅作为“确认”；点选模式：raycast 手指落点。
        Vector2 requestPoint = centerCrosshairMode
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : finger.screenPosition;

        // Phase 2：已放置且模型就绪 → 点击目标点让洛天依走过去；否则走原有放置逻辑。
        if (spawnedModel != null && modelReady && luoMovement != null)
        {
            TryWalkTo(requestPoint);
            return;
        }

        guideUI?.ShowTap(requestPoint);
        TryPlaceAtScreenPosition(requestPoint, true);
    }

    private void TryWalkTo(Vector2 screenPosition)
    {
        if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            hits.Clear();
            guideUI?.ReportPlacement(screenPosition, false, "这里还没有可通行的平面，请继续扫描后再试。");
            return;
        }

        var hit = hits[0];
        hits.Clear();
        var plane = planeManager.GetPlane(hit.trackableId);
        if (plane == null || plane.alignment == PlaneAlignment.Vertical)
        {
            guideUI?.ReportPlacement(screenPosition, false, "该位置不是可通行的水平平面。");
            return;
        }

        // 记录本次命中诊断样本（与放置共用同一套字段）。
        hasPlacementSample = true;
        lastRequestScreenPoint = screenPosition;
        lastHitPosition = hit.pose.position;
        lastHitRotation = hit.pose.rotation;

        guideUI?.ShowTap(screenPosition);
        guideUI?.ReportPlacement(screenPosition, true, "正在前往指定位置…");
        luoMovement.WalkTo(hit.pose.position);
    }

    private void OnFingerMove(Finger finger)
    {
        if (RuntimeDebugPanel.IsOpen)
            return;

        if (finger.index != placementFingerIndex || Touch.activeTouches.Count != 1)
            return;

        // 已放置后：手指移动超过拖动阈值才进入拖动，避免普通点击/微移重建 Anchor 偷换位置。
        if (spawnedModel != null && !isDragging)
        {
            if (Vector2.Distance(finger.screenPosition, dragStartScreen) < dragThresholdPixels)
                return;
            isDragging = true;
            // 拖动接管：打断正在进行的走路。
            if (luoMovement != null)
                luoMovement.Stop();
        }

        // 拖动始终跟随手指落点（模型由用户拖到屏幕上手指的位置）。
        TryPlaceAtScreenPosition(finger.screenPosition, false);
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger.index == placementFingerIndex)
        {
            placementFingerIndex = -1;
            isDragging = false;
        }
    }

    private void TryPlaceAtScreenPosition(Vector2 screenPosition, bool showFeedback)
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

        // 记录本次命中诊断样本：请求屏幕点、hit 世界位姿、重投影屏幕点与像素误差。
        hasPlacementSample = true;
        lastRequestScreenPoint = screenPosition;
        lastHitPosition = hit.pose.position;
        lastHitRotation = hit.pose.rotation;
        if (Camera.main != null)
        {
            // WorldToScreenPoint 是左下原点；Input System 屏幕坐标是左上原点，需翻转 Y 再比较。
            var projected = Camera.main.WorldToScreenPoint(hit.pose.position);
            lastHitScreen = projected;
            lastScreenErrorPx = Vector2.Distance(
                new Vector2(projected.x, Screen.height - projected.y),
                screenPosition);
        }
        else
        {
            lastHitScreen = Vector3.zero;
            lastScreenErrorPx = float.NaN;
        }

        var plane = planeManager.GetPlane(hit.trackableId);
        if (plane == null || plane.alignment == PlaneAlignment.Vertical)
        {
            ReportFailure(screenPosition, showFeedback, "该位置不是可用的水平平面。");
            return;
        }

        // Anchor 必须先创建成功，再移除旧 Anchor，避免追踪短暂失败时模型消失。
        var newAnchor = anchorManager.AttachAnchor(plane, hit.pose);
        if (newAnchor == null)
        {
            Debug.LogWarning("[PlaceOnPlane] 当前平面无法创建 Anchor，本次放置已忽略。");
            ReportFailure(screenPosition, showFeedback, "无法在该位置建立空间锚点，请换一个位置重试。");
            return;
        }

        bool wasAlreadyPlaced = spawnedModel != null;
        var previousAnchor = currentAnchor;
        currentAnchor = newAnchor;

        if (spawnedModel == null)
        {
            // 稳定根节点：Anchor -> Placement Root -> Live2D。billboard 挂在模型上绕 Y 转向，
            // Placement Root 保持原位，避免朝向旋转带动整个 anchor 层级偏移。
            placementRoot = new GameObject("LuoTianyi Placement Root");
            placementRoot.transform.SetParent(currentAnchor.transform, false);
            placementRoot.transform.localPosition = Vector3.zero;

            spawnedModel = Instantiate(modelPrefab, placementRoot.transform);
            spawnedModel.name = "LuoTianyi (AR)";
            spawnedModel.transform.localPosition = Vector3.zero;
            var billboard = spawnedModel.AddComponent<CylindricalBillboard>();
            billboard.SetCamera(Camera.main);

            // Phase 2：动画组件挂到模型，驱动呼吸/眨眼/走路律动；移动组件挂到稳定根节点，注入依赖。
            var motionAnimation = spawnedModel.AddComponent<LuoMotionAnimation>();
            luoMovement = placementRoot.AddComponent<LuoMovement>();
            luoMovement.Initialize(raycastManager, billboard, motionAnimation);

            SetModelRenderersEnabled(false);
            modelReady = false;
            modelLoadFailure = null;
            modelInitialization = StartCoroutine(InitializeModel(screenPosition, plane.trackableId));
            Debug.Log($"[PlaceOnPlane] 锚点已建立，正在等待 Cubism 网格初始化: {plane.trackableId}");
        }
        else
        {
            // 重挂 Anchor 会改变位置，先打断正在进行的走路。
            if (luoMovement != null)
                luoMovement.Stop();

            placementRoot.transform.SetParent(currentAnchor.transform, false);
            placementRoot.transform.localPosition = Vector3.zero;
            spawnedModel.GetComponent<CylindricalBillboard>()?.FaceCameraNow();
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

            spawnedModel.transform.localScale *= targetHeightMeters / originalHeight;
            scalePerMeter = spawnedModel.transform.localScale / targetHeightMeters;
            modelReady = true;
            modelLoadFailure = null;
            SetModelRenderersEnabled(true);
            PlaceFeetOnAnchor();
            modelInitialization = null;

            Debug.Log(
                $"[PlaceOnPlane] Cubism 模型已就绪，平面 {planeId}，" +
                $"原始高度 {originalHeight:F3}，AR 身高 {targetHeightMeters:F2}m");
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
        if (RuntimeDebugPanel.IsOpen || spawnedModel == null || !modelReady || Touch.activeTouches.Count < 2)
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
            return;
        }

        targetHeightMeters = Mathf.Clamp(
            pinchStartHeight * distance / pinchStartDistance,
            minimumHeightMeters,
            maximumHeightMeters);
        spawnedModel.transform.localScale = scalePerMeter * targetHeightMeters;
        PlaceFeetOnAnchor();
    }

    private void SpawnDiagnosticMarker(Vector3 worldPosition)
    {
        // 独立于 Live2D 的高可见基准：小球标记命中点 + 从地面向上 10cm 短线。
        // 使用普通 URP primitive，避免 Cubism 渲染路径干扰；禁用 Collider 以免影响 raycast。
        foreach (var marker in diagnosticMarkers)
        {
            if (marker != null)
                Destroy(marker);
        }
        diagnosticMarkers.Clear();

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Placement Diagnostic Ball";
        ball.transform.position = worldPosition;
        ball.transform.localScale = Vector3.one * 0.025f;
        if (ball.TryGetComponent<Collider>(out var ballCollider))
            ballCollider.enabled = false;
        diagnosticMarkers.Add(ball);

        var stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stick.name = "Placement Diagnostic Stick";
        stick.transform.position = worldPosition + Vector3.up * 0.05f;
        stick.transform.localScale = new Vector3(0.004f, 0.1f, 0.004f);
        if (stick.TryGetComponent<Collider>(out var stickCollider))
            stickCollider.enabled = false;
        diagnosticMarkers.Add(stick);
    }

    private void PlaceFeetOnAnchor()
    {
        if (spawnedModel == null || placementRoot == null || currentAnchor == null)
            return;

        // 校正顺序固定：先让 Placement Root 归零，再基于 billboard+缩放后的世界 bounds
        // 计算脚底中心 footCenter=(center.x, min.y, center.z)，把整个根节点平移过去，完整 XYZ 对齐。
        placementRoot.transform.localPosition = Vector3.zero;
        if (!TryGetModelBounds(out var bounds))
            return;

        var anchorPosition = currentAnchor.transform.position;
        var footCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        placementRoot.transform.position += anchorPosition - footCenter;

        // 记录平移后的真实 footCenter 与 anchor 的对齐误差，供真机诊断旋转/缩放后的偏差。
        lastAnchorPosition = anchorPosition;
        lastFootCenter = TryGetModelBounds(out var moved)
            ? new Vector3(moved.center.x, moved.min.y, moved.center.z)
            : footCenter;
        lastAlignmentError = lastFootCenter - lastAnchorPosition;
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
    public string ModelLoadFailure => modelLoadFailure;

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
        string placementText = hasPlacementSample
            ? $"requestScreen={lastRequestScreenPoint:F1}, hitWorld={lastHitPosition:F3}, hitScreen={lastHitScreen:F1}, screenErrorPx={lastScreenErrorPx:F1}"
            : "requestScreen=none";
        string alignText = hasPlacementSample && placementRoot != null
            ? $"root={placementRoot.transform.position:F3}, footCenter={lastFootCenter:F3}, anchor={lastAnchorPosition:F3}, alignErr={lastAlignmentError.magnitude * 1000f:F1}mm"
            : "align=none";
        string anchorState = currentAnchor != null ? currentAnchor.trackingState.ToString() : "missing";
        string state = modelReady ? "ready" : IsModelLoading ? "loading" : "failed";
        var billboard = spawnedModel.GetComponent<CylindricalBillboard>();
        string facingText = billboard != null && float.IsFinite(billboard.FrontFacingDot)
            ? $"frontDot={billboard.FrontFacingDot:F4}"
            : "frontDot=unknown";

        return
            $"state={state}, failure={(!string.IsNullOrEmpty(modelLoadFailure) ? modelLoadFailure : "none")}\n" +
            $"anchor={anchorState}, targetHeight={targetHeightMeters:F3}m, mode={(centerCrosshairMode ? "center" : "tap")}\n" +
            $"renderers={renderers.Length}, enabled={enabledRenderers}, cubismRenderers={cubismRenderers.Length}, runtimeMeshes={validMeshes}\n" +
            $"bounds={boundsText}\n" +
            $"localPosition={spawnedModel.transform.localPosition:F3}, localScale={spawnedModel.transform.localScale:F5}\n" +
            $"placement: {placementText}\n" +
            $"alignment: {alignText}\n" +
            $"billboard={(billboard != null ? "active" : "missing")}, {facingText}\n" +
            $"shader={shaderName}, supported={shaderSupported}";
    }
}

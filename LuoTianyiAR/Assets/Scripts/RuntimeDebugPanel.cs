// RuntimeDebugPanel.cs — 真机问题回报面板：运行状态、模型诊断、最近日志与一键复制。
// 只收集技术信息，不读取或保存相机画面。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;

public sealed class RuntimeDebugPanel : MonoBehaviour
{
    private const int MaximumLogLines = 120;
    private const int MaximumAbnormalLines = 512;
    private static RuntimeDebugPanel instance;
    private static Rect panelRect;

    private readonly Queue<string> logLines = new();
    private readonly Queue<string> abnormalLogLines = new();
    private Vector2 scrollPosition;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle diagnosisStyle;
    private GUIStyle logStyle;
    private GUIStyle buttonStyle;
    private bool isOpen;
    private float smoothedFps;
    private float nextReferenceRefresh;
    private float copyStatusUntil;
    private string copyStatus;

    private PlaceOnPlane placement;
    private AutoHarmonizationController harmonization;
    private ARPlaneManager planeManager;
    private Camera unityCamera;
    private ARCameraManager cameraManager;
    private ARCameraBackground cameraBackground;
    private ARCameraManager subscribedCameraManager;
    private AROcclusionManager occlusionManager;
    private ARSession arSession;
    private AndroidCameraPermissionGate cameraPermissionGate;
    private XROrigin xrOrigin;
    private int cameraFrameCount;
    private int lastCameraTextureCount;
    private int droppedAbnormalLineCount;
    private float cameraReferenceSeenAt = -1f;
    private float firstCameraFrameAt = -1f;
    private float previousCameraFrameAt = -1f;
    private float lastCameraFrameAt = -1f;
    private float smoothedCameraFps;
    private bool cameraFrameTimeoutReported;
    private bool cameraRecoveryInProgress;
    private int cameraRecoveryAttemptCount;
    private string cameraRecoveryState = "not-attempted";
    private bool arCorePackageVersionResolved;
    private string arCorePackageVersion = "unresolved";

    public static bool IsOpen => instance != null && instance.isOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        var go = new GameObject("Runtime Debug Panel");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RuntimeDebugPanel>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Application.logMessageReceived += OnLogMessage;
        AddInternalLog("INFO", "调试面板已启动");
    }

    private void Update()
    {
        float instantaneousFps = Time.unscaledDeltaTime > 0.0001f
            ? 1f / Time.unscaledDeltaTime
            : 0f;
        smoothedFps = smoothedFps <= 0f
            ? instantaneousFps
            : Mathf.Lerp(smoothedFps, instantaneousFps, 0.08f);

        if (Time.unscaledTime >= nextReferenceRefresh)
        {
            RefreshReferences();
            nextReferenceRefresh = Time.unscaledTime + 1f;
        }

        CheckCameraFrameHealth();
    }

    private void RefreshReferences()
    {
        if (placement == null)
            placement = FindFirstObjectByType<PlaceOnPlane>(FindObjectsInactive.Include);
        if (harmonization == null)
            harmonization = FindFirstObjectByType<AutoHarmonizationController>(FindObjectsInactive.Include);
        if (planeManager == null)
            planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        var foundCameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (foundCameraManager != cameraManager)
        {
            UnsubscribeCameraFrames();
            cameraManager = foundCameraManager;
            unityCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : null;
            cameraBackground = cameraManager != null ? cameraManager.GetComponent<ARCameraBackground>() : null;
            SubscribeCameraFrames();
        }
        if (occlusionManager == null)
            occlusionManager = FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
        if (arSession == null)
            arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        if (cameraPermissionGate == null)
            cameraPermissionGate = FindFirstObjectByType<AndroidCameraPermissionGate>(FindObjectsInactive.Include);
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (!arCorePackageVersionResolved)
        {
            arCorePackageVersion = ResolveArCorePackageVersion();
            arCorePackageVersionResolved = true;
        }
    }

    private void SubscribeCameraFrames()
    {
        if (cameraManager == null)
            return;

        subscribedCameraManager = cameraManager;
        subscribedCameraManager.frameReceived += OnCameraFrameReceived;
        cameraReferenceSeenAt = Time.unscaledTime;
        cameraFrameCount = 0;
        lastCameraTextureCount = 0;
        firstCameraFrameAt = -1f;
        previousCameraFrameAt = -1f;
        lastCameraFrameAt = -1f;
        smoothedCameraFps = 0f;
        cameraFrameTimeoutReported = false;
    }

    private void UnsubscribeCameraFrames()
    {
        if (subscribedCameraManager != null)
            subscribedCameraManager.frameReceived -= OnCameraFrameReceived;
        subscribedCameraManager = null;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        float now = Time.unscaledTime;
        if (firstCameraFrameAt < 0f)
            firstCameraFrameAt = now;
        if (previousCameraFrameAt >= 0f)
        {
            float interval = now - previousCameraFrameAt;
            if (interval > 0.0001f)
            {
                float instantaneousFps = 1f / interval;
                smoothedCameraFps = smoothedCameraFps <= 0f
                    ? instantaneousFps
                    : Mathf.Lerp(smoothedCameraFps, instantaneousFps, 0.12f);
            }
        }

        cameraFrameCount++;
        lastCameraTextureCount = eventArgs.textures.Count;
        previousCameraFrameAt = now;
        lastCameraFrameAt = now;
    }

    private void CheckCameraFrameHealth()
    {
        float permissionGrantedAt = AndroidCameraPermissionGate.PermissionGrantedAtRealtime;
        float cameraHealthStartedAt = Mathf.Max(cameraReferenceSeenAt, permissionGrantedAt);
        if (cameraFrameTimeoutReported || cameraManager == null || !cameraManager.enabled ||
            AndroidCameraPermissionGate.IsPermissionBlocking || !cameraManager.permissionGranted ||
            cameraHealthStartedAt < 0f || Time.unscaledTime - cameraHealthStartedAt < 10f ||
            cameraFrameCount > 0)
        {
            return;
        }

        if (ARSession.state != ARSessionState.SessionInitializing &&
            ARSession.state != ARSessionState.SessionTracking)
        {
            return;
        }

        cameraFrameTimeoutReported = true;
        isOpen = true;
        AddInternalLog(
            "ERROR",
            $"AR 相机已获授权但 10 秒内未收到任何帧；requested={cameraManager.requestedBackgroundRenderingMode}, " +
            $"current={cameraManager.currentRenderingMode}, session={ARSession.state}");
        BeginCameraRecovery("授权后 10 秒无相机帧，自动执行一次恢复");
    }

    private void BeginCameraRecovery(string reason)
    {
        if (cameraRecoveryInProgress)
            return;
        if (cameraManager == null)
        {
            cameraRecoveryState = "failed:no-camera-manager";
            AddInternalLog("ERROR", "无法重启相机：ARCameraManager 不存在");
            return;
        }

        StartCoroutine(RecoverCameraPipeline(reason));
    }

    private IEnumerator RecoverCameraPipeline(string reason)
    {
        cameraRecoveryInProgress = true;
        cameraRecoveryAttemptCount++;
        cameraRecoveryState = $"attempting:{cameraRecoveryAttemptCount}";
        int frameCountBeforeRecovery = cameraFrameCount;
        AddInternalLog(
            "WARN",
            $"开始重启 AR 相机链路（第 {cameraRecoveryAttemptCount} 次）：{reason}；" +
            $"session={ARSession.state}, frames={cameraFrameCount}");

        if (cameraBackground != null)
            cameraBackground.enabled = false;
        cameraManager.enabled = false;
        yield return new WaitForSecondsRealtime(0.35f);

        harmonization?.InvalidateManualLight("调试面板重启 AR 相机");
        if (arSession != null && arSession.enabled)
            arSession.Reset();

        cameraManager.enabled = true;
        if (cameraBackground != null)
            cameraBackground.enabled = true;

        float deadline = Time.unscaledTime + 8f;
        while (Time.unscaledTime < deadline && cameraFrameCount <= frameCountBeforeRecovery)
            yield return null;

        cameraRecoveryInProgress = false;
        if (cameraFrameCount > frameCountBeforeRecovery)
        {
            cameraRecoveryState = $"succeeded:{cameraRecoveryAttemptCount}";
            AddInternalLog(
                "INFO",
                $"AR 相机链路恢复成功；frames={cameraFrameCount}, " +
                $"current={cameraManager.currentRenderingMode}, session={ARSession.state}");
        }
        else
        {
            cameraRecoveryState = $"failed:{cameraRecoveryAttemptCount}";
            AddInternalLog(
                "ERROR",
                $"AR 相机链路重启后 8 秒仍无帧；permission={cameraManager.permissionGranted}, " +
                $"managerEnabled={cameraManager.enabled}, backgroundEnabled={cameraBackground != null && cameraBackground.enabled}, " +
                $"requested={cameraManager.requestedBackgroundRenderingMode}, current={cameraManager.currentRenderingMode}, " +
                $"configuration={GetCurrentCameraConfigurationSummary()}, arCore={arCorePackageVersion}, " +
                $"session={ARSession.state}, reason={ARSession.notTrackingReason}");
        }
    }

    public static void Open(string reason)
    {
        if (instance == null)
            return;

        instance.isOpen = true;
        instance.AddInternalLog("EVENT", reason);
    }

    public static void ToggleFromToolbar()
    {
        if (instance == null)
            return;

        instance.isOpen = !instance.isOpen;
        if (instance.isOpen)
        {
            instance.RefreshReferences();
            instance.AddInternalLog("EVENT", "用户打开顶部调试面板");
        }
    }

    public static bool IsPointerOverDebugUI(Vector2 screenPosition)
    {
        return instance != null && instance.isOpen;
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        string level = type switch
        {
            LogType.Error => "ERROR",
            LogType.Assert => "ASSERT",
            LogType.Warning => "WARN",
            LogType.Exception => "EXCEPTION",
            _ => "INFO"
        };

        string message = Normalize(condition, 1200);
        if ((type == LogType.Error || type == LogType.Assert || type == LogType.Exception) &&
            !string.IsNullOrWhiteSpace(stackTrace))
        {
            message += " | stack=" + Normalize(stackTrace, 2400);
        }

        AddInternalLog(level, message);
        if (type == LogType.Exception || type == LogType.Assert)
            isOpen = true;
    }

    private void AddInternalLog(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        logLines.Enqueue($"[{timestamp}] [{level}] {message}");
        while (logLines.Count > MaximumLogLines)
            logLines.Dequeue();

        if (level == "WARN" || level == "ERROR" || level == "ASSERT" || level == "EXCEPTION")
        {
            abnormalLogLines.Enqueue($"[{timestamp}] [{level}] {message}");
            while (abnormalLogLines.Count > MaximumAbnormalLines)
            {
                abnormalLogLines.Dequeue();
                droppedAbnormalLineCount++;
            }
        }
    }

    private static string Normalize(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return "<empty>";

        string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized.Substring(0, maximumLength) + "…";
    }

    private void OnGUI()
    {
        if (!isOpen)
        {
            panelRect = Rect.zero;
            return;
        }

        GUI.depth = -1000;
        EnsureStyles();

        float scale = Mathf.Clamp(Screen.width / 1080f, 0.78f, 1.45f);
        Rect viewfinder = CameraCaptureUI.GetViewfinderRect();
        float margin = 18f * scale;
        panelRect = new Rect(
            viewfinder.x + margin,
            viewfinder.y + margin,
            Mathf.Max(320f, viewfinder.width - margin * 2f),
            Mathf.Max(420f, viewfinder.height - margin * 2f));
        DrawSolid(panelRect, new Color(0.015f, 0.02f, 0.025f, 0.84f));

        GUILayout.BeginArea(new Rect(
            panelRect.x + 18f * scale,
            panelRect.y + 12f * scale,
            panelRect.width - 36f * scale,
            panelRect.height - 24f * scale));

        GUILayout.BeginHorizontal();
        GUILayout.Label("调试信息", titleStyle, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("关闭", buttonStyle, GUILayout.Width(120f * scale), GUILayout.Height(54f * scale)))
            isOpen = false;
        GUILayout.EndHorizontal();

        GUILayout.Space(8f * scale);
        GUILayout.Label(BuildStatusSummary(), bodyStyle);
        GUILayout.Space(6f * scale);
        GUILayout.Label(BuildCameraDiagnosis(), diagnosisStyle);
        GUILayout.Space(8f * scale);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("复制全部信息", buttonStyle, GUILayout.Height(58f * scale)))
            CopyReport();
        GUI.enabled = !cameraRecoveryInProgress;
        if (GUILayout.Button(
                cameraRecoveryInProgress ? "正在重启…" : "重启相机",
                buttonStyle,
                GUILayout.Width(160f * scale),
                GUILayout.Height(58f * scale)))
        {
            BeginCameraRecovery("用户从调试面板手动重启");
        }
        GUI.enabled = true;
        if (GUILayout.Button("清空", buttonStyle, GUILayout.Width(110f * scale), GUILayout.Height(58f * scale)))
        {
            logLines.Clear();
            abnormalLogLines.Clear();
            droppedAbnormalLineCount = 0;
            AddInternalLog("INFO", "日志已由用户清空");
        }
        GUILayout.EndHorizontal();

        if (Time.unscaledTime < copyStatusUntil && !string.IsNullOrEmpty(copyStatus))
            GUILayout.Label(copyStatus, bodyStyle);

        GUILayout.Space(8f * scale);
        GUILayout.Label(
            $"异常与警告（{abnormalLogLines.Count}" +
            (droppedAbnormalLineCount > 0 ? $"，另有 {droppedAbnormalLineCount} 条已截断" : string.Empty) +
            "）",
            titleStyle);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        if (abnormalLogLines.Count == 0)
            GUILayout.Label("当前未捕获到警告、错误、断言或异常。", logStyle);
        foreach (string line in abnormalLogLines)
            GUILayout.Label(line, logStyle);
        GUILayout.Space(10f * scale);
        GUILayout.Label($"最近运行日志（{logLines.Count}/{MaximumLogLines}）", titleStyle);
        foreach (string line in logLines)
            GUILayout.Label(line, logStyle);
        GUILayout.EndScrollView();
        GUILayout.Label("不采集相机画面。请点击“复制全部信息”并把内容完整发给开发者。", bodyStyle);
        GUILayout.EndArea();
    }

    private string BuildStatusSummary()
    {
        RefreshReferences();
        int planeCount = planeManager != null ? planeManager.trackables.count : 0;
        string modelState = placement == null
            ? "放置组件缺失"
            : placement.IsModelReady
                ? "已就绪"
                : placement.IsModelLoading
                    ? "加载中"
                    : placement.HasPlacedModel
                        ? "加载失败"
                        : "未放置";
        string pipeline = GraphicsSettings.currentRenderPipeline != null
            ? GraphicsSettings.currentRenderPipeline.name
            : "Built-in";
        string lastFrameAge = lastCameraFrameAt >= 0f
            ? $"{Time.unscaledTime - lastCameraFrameAt:F2}s"
            : "从未收到";
        string requestedBackground = cameraManager != null
            ? cameraManager.requestedBackgroundRenderingMode.ToString()
            : "missing";
        string currentBackground = cameraManager != null
            ? cameraManager.currentRenderingMode.ToString()
            : "missing";

        return
            $"相机启用: Camera={unityCamera != null && unityCamera.enabled}  " +
            $"Manager={cameraManager != null && cameraManager.enabled}  " +
            $"Background={cameraBackground != null && cameraBackground.enabled}  " +
            $"权限={cameraManager != null && cameraManager.permissionGranted}\n" +
            $"相机帧: count={cameraFrameCount}  textures={lastCameraTextureCount}  " +
            $"cameraFPS={smoothedCameraFps:F1}  last={lastFrameAge}\n" +
            $"背景模式: requested={requestedBackground}  current={currentBackground}\n" +
            $"相机配置: {GetCurrentCameraConfigurationSummary()}  ARCore={arCorePackageVersion}\n" +
            $"恢复状态: {cameraRecoveryState}  attempts={cameraRecoveryAttemptCount}\n" +
            $"AR: {ARSession.state} / {ARSession.notTrackingReason}  平面={planeCount}  模型={modelState}\n" +
            $"应用FPS={smoothedFps:F0}  图形API={SystemInfo.graphicsDeviceType}  管线={pipeline}\n" +
            $"Render Features: {GetRendererFeatureSummary()}\n" +
            $"自动融合: {(harmonization != null ? harmonization.GetCompactStatus() : "component-missing")}";
    }

    private string BuildCameraDiagnosis()
    {
        RefreshReferences();
        if (AndroidCameraPermissionGate.IsWaitingForDecision)
            return "正在等待用户授予 Android CAMERA 运行时权限，AR 链路尚未启动。";
        if (AndroidCameraPermissionGate.IsPermissionDenied)
            return AndroidCameraPermissionGate.State ==
                   AndroidCameraPermissionGate.PermissionState.DeniedDontAskAgain
                ? "当前异常：相机权限被永久拒绝，请在系统设置中手动授权。"
                : "当前异常：用户拒绝了相机权限，请授权后重新进入应用。";
        if (cameraRecoveryInProgress)
            return $"正在执行第 {cameraRecoveryAttemptCount} 次相机链路重启，请等待相机帧恢复。";
        if (unityCamera == null)
            return "当前异常：场景中没有找到 Unity Camera。";
        if (!unityCamera.gameObject.activeInHierarchy || !unityCamera.enabled)
            return "当前异常：Unity Camera 未启用。";
        if (cameraManager == null || !cameraManager.enabled)
            return "当前异常：ARCameraManager 缺失或未启用。";
        if (cameraBackground == null || !cameraBackground.enabled)
            return "当前异常：ARCameraBackground 缺失或未启用。";
        if (!cameraManager.permissionGranted)
            return "当前异常：应用尚未获得相机权限。";
        if (cameraManager.currentRenderingMode == UnityEngine.XR.ARSubsystems.XRCameraBackgroundRenderingMode.None)
            return "当前异常：AR 相机背景当前没有有效渲染模式。";
        if (cameraFrameCount == 0)
        {
            return cameraRecoveryAttemptCount > 0 && cameraRecoveryState.StartsWith("failed", StringComparison.Ordinal)
                ? "当前异常：相机链路已重启但仍无任何帧。更可能是 ARCore 服务、设备相机配置或厂商系统兼容问题。"
                : "当前异常：ARCameraManager 尚未输出任何相机帧。";
        }

        float frameAge = Time.unscaledTime - lastCameraFrameAt;
        if (frameAge > 2f)
            return $"当前异常：相机帧已停止更新 {frameAge:F1} 秒。";

        return "相机采集链正在持续输出帧；若取景画面仍为黑色，优先检查 AR 背景材质、Renderer Feature 或 GPU 渲染兼容性。";
    }

    private void CopyReport()
    {
        try
        {
            GUIUtility.systemCopyBuffer = BuildReport();
            copyStatus = "诊断报告已复制，可以直接粘贴发送。";
            copyStatusUntil = Time.unscaledTime + 3f;
            AddInternalLog("EVENT", "诊断报告已复制到剪贴板");
        }
        catch (Exception exception)
        {
            copyStatus = "复制失败，请截取调试窗口并发送。";
            copyStatusUntil = Time.unscaledTime + 4f;
            AddInternalLog("ERROR", "复制诊断报告失败: " + exception.Message);
        }
    }

    private string BuildReport()
    {
        RefreshReferences();
        var report = new StringBuilder(8192);
        report.AppendLine("=== LuoTianyiAR 诊断报告 ===");
        report.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"应用版本: {Application.version} / Unity {Application.unityVersion}");
        report.AppendLine($"平台: {Application.platform} / {SystemInfo.operatingSystem}");
        report.AppendLine($"设备: {SystemInfo.deviceModel} / {SystemInfo.deviceName}");
        report.AppendLine($"CPU: {SystemInfo.processorType}, {SystemInfo.processorCount} cores");
        report.AppendLine($"内存: {SystemInfo.systemMemorySize} MB");
        report.AppendLine($"GPU: {SystemInfo.graphicsDeviceName}");
        report.AppendLine($"图形 API: {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceVersion}");
        report.AppendLine($"屏幕: {Screen.width}x{Screen.height}, DPI={Screen.dpi:F1}, FPS={smoothedFps:F1}");
        report.AppendLine($"渲染管线: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in")}");
        report.AppendLine($"Render Features: {GetRendererFeatureSummary()}");
        report.AppendLine($"相机诊断结论: {BuildCameraDiagnosis()}");
        report.AppendLine();

        report.AppendLine("--- AR ---");
        report.AppendLine($"Session: state={ARSession.state}, reason={ARSession.notTrackingReason}");
        report.AppendLine($"Google Play Services for AR: {arCorePackageVersion}");
        report.AppendLine($"Camera permission gate: {AndroidCameraPermissionGate.GetDebugSummary()}");
        report.AppendLine($"ARSession component: present={arSession != null}, enabled={arSession != null && arSession.enabled}");
        string xrOffset = xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null
            ? xrOrigin.CameraFloorOffsetObject.transform.localPosition.ToString("F3")
            : "missing";
        report.AppendLine(xrOrigin != null
            ? $"XR Origin: requested={xrOrigin.RequestedTrackingOriginMode}, current={xrOrigin.CurrentTrackingOriginMode}, cameraYOffset={xrOrigin.CameraYOffset:F4}m, offsetLocal={xrOffset}"
            : "XR Origin: missing");
        report.AppendLine($"Planes: count={(planeManager != null ? planeManager.trackables.count : 0)}, mode={(planeManager != null ? planeManager.currentDetectionMode.ToString() : "missing")}");
        string cameraMaterial = cameraManager != null && cameraManager.cameraMaterial != null
            ? cameraManager.cameraMaterial.name
            : "missing";
        string cameraShader = cameraManager != null && cameraManager.cameraMaterial != null &&
                              cameraManager.cameraMaterial.shader != null
            ? cameraManager.cameraMaterial.shader.name
            : "missing";
        string lastFrameAge = lastCameraFrameAt >= 0f
            ? $"{Time.unscaledTime - lastCameraFrameAt:F2}s"
            : "never";
        report.AppendLine(
            $"Camera: present={unityCamera != null}, enabled={unityCamera != null && unityCamera.enabled}, activeInHierarchy={unityCamera != null && unityCamera.gameObject.activeInHierarchy}, " +
            $"managerPresent={cameraManager != null}, managerEnabled={cameraManager != null && cameraManager.enabled}, " +
            $"backgroundPresent={cameraBackground != null}, backgroundEnabled={cameraBackground != null && cameraBackground.enabled}, " +
            $"permission={cameraManager != null && cameraManager.permissionGranted}, " +
            $"facing={(cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "missing")}, " +
            $"requestedBackground={(cameraManager != null ? cameraManager.requestedBackgroundRenderingMode.ToString() : "missing")}, " +
            $"currentBackground={(cameraManager != null ? cameraManager.currentRenderingMode.ToString() : "missing")}");
        report.AppendLine(
            $"Camera frames: count={cameraFrameCount}, textures={lastCameraTextureCount}, " +
            $"cameraFPS={smoothedCameraFps:F2}, lastAge={lastFrameAge}, material={cameraMaterial}, shader={cameraShader}");
        report.AppendLine($"Camera configuration: {GetCurrentCameraConfigurationSummary()}");
        report.AppendLine(
            $"Camera recovery: state={cameraRecoveryState}, attempts={cameraRecoveryAttemptCount}, " +
            $"inProgress={cameraRecoveryInProgress}");
        report.AppendLine($"Occlusion: present={occlusionManager != null}, enabled={occlusionManager != null && occlusionManager.enabled}, requested={(occlusionManager != null ? occlusionManager.requestedEnvironmentDepthMode.ToString() : "missing")}, current={(occlusionManager != null ? occlusionManager.currentEnvironmentDepthMode.ToString() : "missing")}");
        report.AppendLine();
        report.AppendLine("--- 自动融合 ---");
        report.AppendLine(harmonization != null ? harmonization.GetDebugSummary() : "AutoHarmonizationController component missing");
        report.AppendLine();

        report.AppendLine("--- 模型 ---");
        report.AppendLine(placement != null ? placement.GetDebugSnapshot() : "PlaceOnPlane component missing");
        report.AppendLine();

        report.AppendLine($"--- 异常与警告 ({abnormalLogLines.Count}) ---");
        if (droppedAbnormalLineCount > 0)
            report.AppendLine($"注意: 更早的 {droppedAbnormalLineCount} 条异常或警告已因容量限制截断。");
        if (abnormalLogLines.Count == 0)
            report.AppendLine("none");
        foreach (string line in abnormalLogLines)
            report.AppendLine(line);
        report.AppendLine();

        report.AppendLine($"--- 最近日志 ({logLines.Count}) ---");
        foreach (string line in logLines)
            report.AppendLine(line);
        report.AppendLine("=== 报告结束 ===");
        return report.ToString();
    }

    private string GetCurrentCameraConfigurationSummary()
    {
        if (cameraManager == null)
            return "missing-manager";

        try
        {
            var current = cameraManager.currentConfiguration;
            if (!current.HasValue)
                return "none";

            var configuration = current.Value;
            string frameRate = configuration.framerate.HasValue
                ? configuration.framerate.Value.ToString()
                : "unknown";
            return $"{configuration.width}x{configuration.height}@{frameRate}fps, " +
                   $"depthSensorSupported={configuration.depthSensorSupported}";
        }
        catch (Exception exception)
        {
            return $"error:{exception.GetType().Name}:{CompactSingleLine(exception.Message)}";
        }
    }

    private static string ResolveArCorePackageVersion()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject packageManager = activity.Call<AndroidJavaObject>("getPackageManager");
            using AndroidJavaObject packageInfo = packageManager.Call<AndroidJavaObject>(
                "getPackageInfo",
                "com.google.ar.core",
                0);

            string versionName = packageInfo.Get<string>("versionName");
            long versionCode;
            try
            {
                versionCode = packageInfo.Call<long>("getLongVersionCode");
            }
            catch
            {
                versionCode = packageInfo.Get<int>("versionCode");
            }
            return $"{versionName} ({versionCode})";
        }
        catch (Exception exception)
        {
            return $"unavailable:{exception.GetType().Name}:{CompactSingleLine(exception.Message)}";
        }
#else
        return "not-android";
#endif
    }

    private static string CompactSingleLine(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "no-message";

        const int maximumLength = 180;
        string compact = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= maximumLength
            ? compact
            : compact.Substring(0, maximumLength) + "...";
    }

    private static string GetRendererFeatureSummary()
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline ||
            pipeline.rendererDataList.Length == 0 ||
            pipeline.rendererDataList[0] == null)
        {
            return "unavailable";
        }

        var summary = new StringBuilder();
        foreach (var feature in pipeline.rendererDataList[0].rendererFeatures)
        {
            if (summary.Length > 0)
                summary.Append(", ");
            summary.Append(feature != null
                ? $"{feature.GetType().Name}({(feature.isActive ? "on" : "off")})"
                : "missing");
        }
        return summary.Length > 0 ? summary.ToString() : "none";
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        int titleSize = Mathf.Clamp(Screen.width / 30, 26, 42);
        int bodySize = Mathf.Clamp(Screen.width / 42, 20, 30);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = titleSize,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = bodySize,
            wordWrap = true,
            normal = { textColor = new Color(0.94f, 0.94f, 0.94f, 1f) }
        };
        diagnosisStyle = new GUIStyle(bodyStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.82f, 0.24f, 1f) }
        };
        logStyle = new GUIStyle(bodyStyle)
        {
            fontSize = Mathf.Max(18, bodySize - 2)
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = bodySize,
            fontStyle = FontStyle.Bold
        };
    }

    private static void DrawSolid(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        UnsubscribeCameraFrames();
        Application.logMessageReceived -= OnLogMessage;
        if (instance == this)
            instance = null;
    }
}

// RuntimeDebugPanel.cs — 真机问题回报面板：运行状态、模型诊断、最近日志与一键复制。
// 只收集技术信息，不读取或保存相机画面。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

public sealed class RuntimeDebugPanel : MonoBehaviour
{
    private const int MaximumLogLines = 120;
    private static RuntimeDebugPanel instance;
    private static Rect toggleRect;
    private static Rect panelRect;

    private readonly Queue<string> logLines = new();
    private Vector2 scrollPosition;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle logStyle;
    private GUIStyle buttonStyle;
    private bool isOpen;
    private float smoothedFps;
    private float nextReferenceRefresh;
    private float copyStatusUntil;
    private string copyStatus;

    private PlaceOnPlane placement;
    private ARPlaneManager planeManager;
    private ARCameraManager cameraManager;
    private AROcclusionManager occlusionManager;
    private ARSession arSession;

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
    }

    private void RefreshReferences()
    {
        if (placement == null)
            placement = FindFirstObjectByType<PlaceOnPlane>(FindObjectsInactive.Include);
        if (planeManager == null)
            planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (occlusionManager == null)
            occlusionManager = FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
        if (arSession == null)
            arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
    }

    public static void Open(string reason)
    {
        if (instance == null)
            return;

        instance.isOpen = true;
        instance.AddInternalLog("EVENT", reason);
    }

    public static bool IsPointerOverDebugUI(Vector2 screenPosition)
    {
        if (instance == null)
            return false;

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return instance.isOpen || toggleRect.Contains(guiPoint) || panelRect.Contains(guiPoint);
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

        string message = Normalize(condition, 900);
        if ((type == LogType.Error || type == LogType.Assert || type == LogType.Exception) &&
            !string.IsNullOrWhiteSpace(stackTrace))
        {
            string firstStackLine = stackTrace.Split('\n')[0].Trim();
            message += " | " + Normalize(firstStackLine, 280);
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
        GUI.depth = -1000;
        EnsureStyles();

        float scale = Mathf.Clamp(Screen.width / 1080f, 0.78f, 1.45f);
        var safe = Screen.safeArea;
        float safeTop = Screen.height - safe.yMax;
        float margin = 14f * scale;
        toggleRect = new Rect(
            safe.xMax - 142f * scale - margin,
            safeTop + margin,
            142f * scale,
            58f * scale);

        if (!isOpen)
        {
            if (GUI.Button(toggleRect, "调试", buttonStyle))
            {
                isOpen = true;
                RefreshReferences();
            }
            panelRect = Rect.zero;
            return;
        }

        panelRect = new Rect(
            safe.x + margin,
            safeTop + margin,
            Mathf.Max(320f, safe.width - margin * 2f),
            Mathf.Max(420f, safe.height - margin * 2f));
        GUI.Box(panelRect, GUIContent.none);

        GUILayout.BeginArea(new Rect(
            panelRect.x + 18f * scale,
            panelRect.y + 12f * scale,
            panelRect.width - 36f * scale,
            panelRect.height - 24f * scale));

        GUILayout.BeginHorizontal();
        GUILayout.Label("真机调试信息", titleStyle, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("关闭", buttonStyle, GUILayout.Width(120f * scale), GUILayout.Height(54f * scale)))
            isOpen = false;
        GUILayout.EndHorizontal();

        GUILayout.Space(8f * scale);
        GUILayout.Label(BuildStatusSummary(), bodyStyle);
        GUILayout.Space(8f * scale);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("复制诊断报告", buttonStyle, GUILayout.Height(58f * scale)))
            CopyReport();
        if (GUILayout.Button("清空日志", buttonStyle, GUILayout.Width(150f * scale), GUILayout.Height(58f * scale)))
        {
            logLines.Clear();
            AddInternalLog("INFO", "日志已由用户清空");
        }
        GUILayout.EndHorizontal();

        if (Time.unscaledTime < copyStatusUntil && !string.IsNullOrEmpty(copyStatus))
            GUILayout.Label(copyStatus, bodyStyle);

        GUILayout.Space(8f * scale);
        GUILayout.Label($"最近日志（{logLines.Count}/{MaximumLogLines}）", titleStyle);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        foreach (string line in logLines)
            GUILayout.Label(line, logStyle);
        GUILayout.EndScrollView();
        GUILayout.Label("不采集相机画面。请点击“复制诊断报告”并把内容完整发给开发者。", bodyStyle);
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

        return
            $"AR: {ARSession.state} / {ARSession.notTrackingReason}    平面: {planeCount}\n" +
            $"模型: {modelState}    FPS: {smoothedFps:F0}    管线: {pipeline}\n" +
            $"Render Features: {GetRendererFeatureSummary()}";
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
        report.AppendLine();

        report.AppendLine("--- AR ---");
        report.AppendLine($"Session: state={ARSession.state}, reason={ARSession.notTrackingReason}");
        report.AppendLine($"ARSession component: present={arSession != null}, enabled={arSession != null && arSession.enabled}");
        report.AppendLine($"Planes: count={(planeManager != null ? planeManager.trackables.count : 0)}, mode={(planeManager != null ? planeManager.currentDetectionMode.ToString() : "missing")}");
        report.AppendLine($"Camera: present={cameraManager != null}, enabled={cameraManager != null && cameraManager.enabled}, permission={cameraManager != null && cameraManager.permissionGranted}, facing={(cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "missing")}, background={(cameraManager != null ? cameraManager.currentRenderingMode.ToString() : "missing")}");
        report.AppendLine($"Occlusion: present={occlusionManager != null}, enabled={occlusionManager != null && occlusionManager.enabled}, requested={(occlusionManager != null ? occlusionManager.requestedEnvironmentDepthMode.ToString() : "missing")}, current={(occlusionManager != null ? occlusionManager.currentEnvironmentDepthMode.ToString() : "missing")}");
        report.AppendLine();

        report.AppendLine("--- 模型 ---");
        report.AppendLine(placement != null ? placement.GetDebugSnapshot() : "PlaceOnPlane component missing");
        report.AppendLine();

        report.AppendLine($"--- 最近日志 ({logLines.Count}) ---");
        foreach (string line in logLines)
            report.AppendLine(line);
        report.AppendLine("=== 报告结束 ===");
        return report.ToString();
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
            wordWrap = true
        };
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = bodySize,
            wordWrap = true
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

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
        if (instance == this)
            instance = null;
    }
}

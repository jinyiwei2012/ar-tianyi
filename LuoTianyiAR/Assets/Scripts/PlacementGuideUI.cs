// PlacementGuideUI.cs — P0 运行时引导、点击反馈与 AR 失败提示。
// 使用 OnGUI 绘制，不依赖 Canvas/字体资产，场景装配后即可工作。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager), typeof(ARPlaneManager))]
public class PlacementGuideUI : MonoBehaviour
{
    private static readonly Color ReadyColor = new(0.20f, 0.90f, 0.55f, 1f);
    private static readonly Color WaitingColor = new(1.00f, 0.76f, 0.20f, 1f);
    private static readonly Color FailureColor = new(1.00f, 0.30f, 0.30f, 1f);

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private PlaceOnPlane placement;
    private ARMarkerDiagnostics markerDiagnostics;
    private readonly List<ARRaycastHit> centerHits = new();

    private Texture2D pixel;
    private GUIStyle titleStyle;
    private GUIStyle detailStyle;
    private GUIStyle hintStyle;

    private bool canPlaceAtCenter;
    private Vector2 feedbackPosition;
    private Color feedbackColor;
    private float feedbackStartedAt = -10f;
    private string transientMessage;
    private bool transientIsFailure;
    private float transientUntil;

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();
        placement = GetComponent<PlaceOnPlane>();
        markerDiagnostics = GetComponent<ARMarkerDiagnostics>();

        pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        pixel.name = "PlacementGuidePixel";
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void Update()
    {
        canPlaceAtCenter = false;
        centerHits.Clear();
        if (ARSession.state == ARSessionState.SessionTracking && raycastManager != null)
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            canPlaceAtCenter = raycastManager.Raycast(
                center, centerHits, TrackableType.PlaneWithinPolygon);
        }
    }

    public void ShowTap(Vector2 screenPosition)
    {
        feedbackPosition = screenPosition;
        feedbackColor = WaitingColor;
        feedbackStartedAt = Time.unscaledTime;
    }

    public void ShowGaze(Vector2 screenPosition)
    {
        feedbackPosition = screenPosition;
        feedbackColor = ReadyColor;
        feedbackStartedAt = Time.unscaledTime;
    }

    public void ReportPlacement(Vector2 screenPosition, bool success, string message)
    {
        feedbackPosition = screenPosition;
        feedbackColor = success ? ReadyColor : FailureColor;
        feedbackStartedAt = Time.unscaledTime;
        transientMessage = message;
        transientIsFailure = !success;
        transientUntil = Time.unscaledTime + (success ? 1.8f : 3.0f);
    }

    private void OnGUI()
    {
        EnsureStyles();
        var safe = Screen.safeArea;
        float safeTop = Screen.height - safe.yMax;
        float margin = Mathf.Max(18f, Screen.width * 0.035f);
        float panelWidth = Mathf.Min(Screen.width - margin * 2f, 860f);
        float panelX = (Screen.width - panelWidth) * 0.5f;

        GetStatus(out string title, out string detail, out Color statusColor);
        var topRect = new Rect(panelX, safeTop + margin, panelWidth, Mathf.Max(120f, Screen.height * 0.13f));
        DrawPanel(topRect, new Color(0f, 0f, 0f, 0.62f));
        DrawAccent(topRect, statusColor);
        GUI.Label(new Rect(topRect.x + 24f, topRect.y + 12f, topRect.width - 48f, topRect.height * 0.43f), title, titleStyle);
        GUI.Label(new Rect(topRect.x + 24f, topRect.y + topRect.height * 0.45f, topRect.width - 48f, topRect.height * 0.47f), detail, detailStyle);

        DrawCenterReticle(canPlaceAtCenter ? ReadyColor : WaitingColor);
        DrawTouchFeedback();

        string hint;
        if (placement != null && placement.IsWalking)
            hint = "正在前往指定位置… 拖动可随时接管";
        else if (placement != null && placement.IsModelReady)
            hint = placement.IsPositionLocked
                ? "位置已锁定  ·  点击或单指滑动引导视线"
                : "单指拖动/点击走路  ·  双指捏合调整大小  ·  可锁定位置";
        else if (markerDiagnostics != null && !markerDiagnostics.IsMarkerTracked)
            hint = "先将完整的 120mm 定位卡放在桌面并移入画面";
        else
            hint = "将中心准星对准已识别平面，准星变绿后点击屏幕确认";
        if (Time.unscaledTime < transientUntil && !string.IsNullOrEmpty(transientMessage))
            hint = transientMessage;

        float bottomY = Screen.height - safe.yMin - Mathf.Max(100f, Screen.height * 0.10f) - margin;
        var bottomRect = new Rect(panelX, bottomY, panelWidth, Mathf.Max(100f, Screen.height * 0.10f));
        var bottomColor = Time.unscaledTime < transientUntil && transientIsFailure
            ? new Color(0.45f, 0.04f, 0.04f, 0.82f)
            : new Color(0f, 0f, 0f, 0.62f);
        DrawPanel(bottomRect, bottomColor);
        GUI.Label(new Rect(bottomRect.x + 22f, bottomRect.y + 10f, bottomRect.width - 44f, bottomRect.height - 20f), hint, hintStyle);
    }

    private void GetStatus(out string title, out string detail, out Color color)
    {
        int planeCount = planeManager != null ? planeManager.trackables.count : 0;
        switch (ARSession.state)
        {
            case ARSessionState.Unsupported:
                title = "此设备不支持 AR";
                detail = "请确认设备支持 ARCore，并已安装 Google Play Services for AR。";
                color = FailureColor;
                return;
            case ARSessionState.NeedsInstall:
                title = "需要安装 ARCore 组件";
                detail = "按系统提示安装 Google Play Services for AR 后重新打开应用。";
                color = FailureColor;
                return;
            case ARSessionState.Installing:
            case ARSessionState.CheckingAvailability:
            case ARSessionState.None:
            case ARSessionState.Ready:
                title = "正在准备 AR";
                detail = "请稍候，不要遮挡摄像头。";
                color = WaitingColor;
                return;
            case ARSessionState.SessionInitializing:
                title = "正在恢复空间追踪";
                detail = TrackingReasonText(ARSession.notTrackingReason);
                color = WaitingColor;
                return;
        }

        if ((placement == null || !placement.HasPlacedModel) && markerDiagnostics != null)
        {
            if (!markerDiagnostics.IsMarkerTracked)
            {
                title = markerDiagnostics.HasEverDetectedMarker ? "二维码定位卡追踪丢失" : "寻找二维码定位卡";
                detail = "请对准整张 120mm 定位卡，确保边框、TOP 箭头和二维码都在画面内。";
                color = WaitingColor;
                return;
            }

            if (!markerDiagnostics.HasPlaneComparison)
            {
                title = "二维码定位卡已锁定";
                detail = "图像空间位姿已建立，但卡片中心还没有匹配到水平面；请缓慢左右扫描桌面。";
                color = WaitingColor;
                return;
            }

            title = markerDiagnostics.PlaneComparisonPasses
                ? "二维码与水平面一致"
                : "二维码与水平面不一致";
            detail =
                $"高度误差 {markerDiagnostics.HeightErrorCentimeters:F1}cm，" +
                $"法向误差 {markerDiagnostics.NormalErrorDegrees:F1}°，" +
                $"平面内偏移 {markerDiagnostics.LateralErrorCentimeters:F1}cm；点击会将模型放到定位卡中心。";
            color = markerDiagnostics.PlaneComparisonPasses ? ReadyColor : FailureColor;
            return;
        }

        if (planeCount == 0)
        {
            title = "正在寻找水平平面";
            detail = "缓慢左右移动手机，让地面或桌面保持在画面中。";
            color = WaitingColor;
        }
        else if (placement == null || !placement.HasPlacedModel)
        {
            title = $"已识别 {planeCount} 个平面";
            detail = canPlaceAtCenter
                ? "准星已变绿：点击屏幕确认，洛天依会出现在准星位置。"
                : "移动手机，将中心准星对准已识别的地面或桌面。";
            color = canPlaceAtCenter ? ReadyColor : WaitingColor;
        }
        else if (placement.IsModelLoading)
        {
            title = "正在加载洛天依";
            detail = "空间锚点已建立，正在初始化模型网格。";
            color = WaitingColor;
        }
        else if (!string.IsNullOrEmpty(placement.ModelLoadFailure))
        {
            title = "模型加载失败";
            detail = placement.ModelLoadFailure;
            color = FailureColor;
        }
        else if (placement.IsWalking)
        {
            title = "洛天依正在行走";
            detail = "正在前往指定位置，拖动或点击可随时接管。";
            color = WaitingColor;
        }
        else
        {
            title = placement.IsPositionLocked ? "洛天依位置已锁定" : "洛天依已放置";
            detail = placement.IsPositionLocked
                ? "位置和大小保持不变；点击或滑动屏幕可以引导她的视线。"
                : "可以绕她走动，也可以继续拖动位置或双指调整大小。";
            color = ReadyColor;
        }
    }

    private static string TrackingReasonText(NotTrackingReason reason)
    {
        return reason switch
        {
            NotTrackingReason.InsufficientLight => "环境太暗，请增加照明。",
            NotTrackingReason.InsufficientFeatures => "画面纹理不足，请对准有纹理的地面或物体。",
            NotTrackingReason.ExcessiveMotion => "手机移动过快，请放慢并保持稳定。",
            NotTrackingReason.Relocalizing => "正在重新定位，请缓慢环视之前扫描的区域。",
            NotTrackingReason.CameraUnavailable => "摄像头被其他应用占用，请关闭其他相机应用。",
            _ => "请缓慢移动手机，等待空间追踪稳定。"
        };
    }

    private void DrawCenterReticle(Color color)
    {
        float size = Mathf.Clamp(Screen.width * 0.06f, 54f, 92f);
        var rect = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
        DrawOutline(rect, color, 4f);
        DrawSolid(new Rect(rect.center.x - 2f, rect.y - 10f, 4f, 20f), color);
        DrawSolid(new Rect(rect.center.x - 2f, rect.yMax - 10f, 4f, 20f), color);
        DrawSolid(new Rect(rect.x - 10f, rect.center.y - 2f, 20f, 4f), color);
        DrawSolid(new Rect(rect.xMax - 10f, rect.center.y - 2f, 20f, 4f), color);
    }

    private void DrawTouchFeedback()
    {
        float elapsed = Time.unscaledTime - feedbackStartedAt;
        if (elapsed < 0f || elapsed > 0.65f)
            return;

        float progress = elapsed / 0.65f;
        float size = Mathf.Lerp(34f, 120f, progress);
        float guiY = Screen.height - feedbackPosition.y;
        var color = feedbackColor;
        color.a = 1f - progress;
        DrawOutline(new Rect(feedbackPosition.x - size * 0.5f, guiY - size * 0.5f, size, size), color, 5f);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        int titleSize = Mathf.Clamp(Screen.width / 28, 28, 46);
        int bodySize = Mathf.Clamp(Screen.width / 38, 22, 34);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = titleSize,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        detailStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = bodySize,
            wordWrap = true
        };
        hintStyle = new GUIStyle(detailStyle) { fontStyle = FontStyle.Bold };
    }

    private void DrawPanel(Rect rect, Color color)
    {
        DrawSolid(rect, color);
    }

    private void DrawAccent(Rect rect, Color color)
    {
        DrawSolid(new Rect(rect.x, rect.y, 8f, rect.height), color);
    }

    private void DrawOutline(Rect rect, Color color, float thickness)
    {
        DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void DrawSolid(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        if (pixel != null)
            Destroy(pixel);
    }
}

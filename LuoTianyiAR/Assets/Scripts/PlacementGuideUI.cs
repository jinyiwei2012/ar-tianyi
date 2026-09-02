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
            Rect viewfinder = CameraCaptureUI.GetViewfinderRect();
            var center = new Vector2(
                viewfinder.center.x,
                Screen.height - viewfinder.center.y);
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
        if (CaptureGalleryUI.IsOpen || CameraCaptureUI.IsManualLightEditing)
            return;

        GUI.depth = 100;
        EnsureStyles();
        Rect viewfinder = CameraCaptureUI.GetViewfinderRect();
        float margin = Mathf.Max(16f, Screen.width * 0.025f);
        float panelWidth = Mathf.Min(viewfinder.width - margin * 2f, 900f);
        float panelX = viewfinder.center.x - panelWidth * 0.5f;
        GetStatus(out string title, out string detail, out Color statusColor);
        if (Time.unscaledTime < transientUntil && !string.IsNullOrEmpty(transientMessage))
            detail = transientMessage;

        float promptHeight = Mathf.Clamp(Screen.height * 0.085f, 118f, 210f);
        var promptRect = new Rect(panelX, viewfinder.y + margin, panelWidth, promptHeight);
        DrawPanel(promptRect, transientIsFailure && Time.unscaledTime < transientUntil
            ? new Color(0.42f, 0.03f, 0.03f, 0.74f)
            : new Color(0f, 0f, 0f, 0.58f));
        DrawAccent(promptRect, statusColor);
        GUI.Label(
            new Rect(promptRect.x + 22f, promptRect.y + 8f, promptRect.width - 44f, promptRect.height * 0.44f),
            title,
            titleStyle);
        GUI.Label(
            new Rect(promptRect.x + 22f, promptRect.y + promptRect.height * 0.43f, promptRect.width - 44f, promptRect.height * 0.51f),
            detail,
            detailStyle);

        if (placement == null || !placement.IsModelReady)
            DrawCenterReticle(viewfinder, canPlaceAtCenter ? ReadyColor : WaitingColor);
        DrawTouchFeedback();
    }

    private void GetStatus(out string title, out string detail, out Color color)
    {
        if (AndroidCameraPermissionGate.IsWaitingForDecision)
        {
            title = "需要相机权限";
            detail = "请在系统弹窗中允许相机权限，授权后将自动启动 AR。";
            color = WaitingColor;
            return;
        }

        if (AndroidCameraPermissionGate.IsPermissionDenied)
        {
            title = "相机权限未授予";
            detail = AndroidCameraPermissionGate.State ==
                     AndroidCameraPermissionGate.PermissionState.DeniedDontAskAgain
                ? "请前往系统设置，为 LuoTianyiAR 开启相机权限后重新进入应用。"
                : "请允许相机权限后重新进入应用。";
            color = FailureColor;
            return;
        }

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
                ? "准星已变绿：点击取景框，将洛天依放到准星所在平面。"
                : "缓慢移动手机，将中心准星对准桌面或地面。";
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
        else
        {
            title = placement.IsPositionLocked ? "洛天依位置已锁定" : "洛天依已放置";
            detail = placement.IsPositionLocked
                ? "点击或滑动取景框可以引导视线；现在可以拍照。"
                : "单指拖动位置、双指调整大小；完成后点击右下角锁定。";
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

    private void DrawCenterReticle(Rect viewfinder, Color color)
    {
        float size = Mathf.Clamp(Screen.width * 0.06f, 54f, 92f);
        var rect = new Rect(
            viewfinder.center.x - size * 0.5f,
            viewfinder.center.y - size * 0.5f,
            size,
            size);
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

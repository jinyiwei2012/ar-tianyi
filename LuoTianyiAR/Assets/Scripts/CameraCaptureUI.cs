// CameraCaptureUI.cs — 4:3 AR 相机界面、前后摄切换与三图层照片保存。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(PlaceOnPlane))]
public sealed class CameraCaptureUI : MonoBehaviour
{
    private const string CaptureFolderName = "Captures";
    private const string GalleryFolderName = "Pictures/LuoTianyiAR";
    private const string LastCompositePathKey = "LuoTianyiAR.LastCompositePath";

    private static CameraCaptureUI instance;
    private static Rect topBarRect;
    private static Rect bottomBarRect;
    private static Rect shutterRect;
    private static Rect recycleRect;
    private static Rect flipRect;
    private static Rect thumbnailRect;
    private static Rect expressionButtonRect;
    private static Rect debugButtonRect;
    private static Rect harmonizationButtonRect;
    private static Rect manualLightButtonRect;
    private static Rect manualLightMenuRect;
    private static Rect manualCalibrationPanelRect;

    private PlaceOnPlane placement;
    private AutoHarmonizationController harmonization;
    private ARSession arSession;
    private CaptureGalleryUI gallery;
    private Camera arCamera;
    private ARCameraManager cameraManager;
    private ARCameraBackground cameraBackground;
    private TrackedPoseDriver trackedPoseDriver;
    private Texture2D shutterIcon;
    private Texture2D recycleIcon;
    private Texture2D flipIcon;
    private Texture2D debugIcon;
    private Texture2D expressionIcon;
    private Texture2D manualLightAddIcon;
    private Texture2D manualLightReadyIcon;
    private Texture2D pixel;
    private Texture2D lastThumbnail;
    private GUIStyle titleStyle;
    private GUIStyle statusStyle;
    private GUIStyle smallStyle;
    private GUIStyle harmonizationStyle;
    private GUIStyle calibrationTitleStyle;
    private GUIStyle calibrationBodyStyle;
    private GUIStyle calibrationButtonStyle;
    private GUIStyle manualLightParameterStyle;
    private GUIStyle manualLightButtonStyle;
    private GUIStyle manualLightSliderStyle;
    private GUIStyle manualLightSliderThumbStyle;
    private bool isCapturing;
    private bool isSwitchingCamera;
    private bool showManualLightMenu;
    private string operationStatus = "平面定位";
    private float statusUntil;
    private float flashStartedAt = -10f;
    private bool resetSessionAfterResume;
    private bool recycledForPause;

    public static bool IsCapturing => instance != null && instance.isCapturing;
    public static bool IsManualLightCalibrationActive =>
        instance != null && instance.harmonization != null && instance.harmonization.IsManualCalibrationActive;
    public static bool IsManualLightEditing =>
        instance != null && (instance.showManualLightMenu || IsManualLightCalibrationActive);

    private void Awake()
    {
        instance = this;
        placement = GetComponent<PlaceOnPlane>();
        harmonization = GetComponent<AutoHarmonizationController>() ??
                        gameObject.AddComponent<AutoHarmonizationController>();
        gallery = GetComponent<CaptureGalleryUI>() ?? gameObject.AddComponent<CaptureGalleryUI>();
        arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        arCamera = Camera.main;
        if (arCamera != null)
        {
            cameraManager = arCamera.GetComponent<ARCameraManager>();
            cameraBackground = arCamera.GetComponent<ARCameraBackground>();
            trackedPoseDriver = arCamera.GetComponent<TrackedPoseDriver>();
        }

        shutterIcon = Resources.Load<Texture2D>("UI/shutter");
        recycleIcon = Resources.Load<Texture2D>("UI/recycle-model");
        flipIcon = Resources.Load<Texture2D>("UI/flip-camera");
        debugIcon = Resources.Load<Texture2D>("UI/debug-wrench");
        expressionIcon = CreateExpressionIcon();
        manualLightAddIcon = CreateManualLightIcon(false);
        manualLightReadyIcon = CreateManualLightIcon(true);
        pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
        LoadLastThumbnail();
    }

    public static Rect GetViewfinderRect()
    {
        float viewHeight = Mathf.Min(Screen.height, Screen.width * 4f / 3f);
        float spareHeight = Mathf.Max(0f, Screen.height - viewHeight);
        float topHeight = spareHeight * 0.35f;
        return new Rect(0f, topHeight, Screen.width, viewHeight);
    }

    public static bool IsWithinViewfinder(Vector2 screenPosition)
    {
        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return GetViewfinderRect().Contains(guiPoint);
    }

    public static bool IsPointerOverCameraUI(Vector2 screenPosition)
    {
        if (instance == null)
            return false;

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return CaptureGalleryUI.IsOpen || instance.isCapturing || instance.isSwitchingCamera ||
               IsManualLightCalibrationActive ||
               topBarRect.Contains(guiPoint) || bottomBarRect.Contains(guiPoint) ||
               shutterRect.Contains(guiPoint) || recycleRect.Contains(guiPoint) || flipRect.Contains(guiPoint) ||
               thumbnailRect.Contains(guiPoint) || expressionButtonRect.Contains(guiPoint) ||
               debugButtonRect.Contains(guiPoint) ||
               harmonizationButtonRect.Contains(guiPoint) || manualLightButtonRect.Contains(guiPoint) ||
               manualLightMenuRect.Contains(guiPoint) || manualCalibrationPanelRect.Contains(guiPoint);
    }

    private void OnGUI()
    {
        if (CaptureGalleryUI.IsOpen)
        {
            topBarRect = new Rect(0f, 0f, Screen.width, Screen.height);
            bottomBarRect = Rect.zero;
            shutterRect = Rect.zero;
            recycleRect = Rect.zero;
            flipRect = Rect.zero;
            thumbnailRect = Rect.zero;
            expressionButtonRect = Rect.zero;
            debugButtonRect = Rect.zero;
            harmonizationButtonRect = Rect.zero;
            manualLightButtonRect = Rect.zero;
            manualLightMenuRect = Rect.zero;
            manualCalibrationPanelRect = Rect.zero;
            return;
        }

        GUI.depth = 900;
        EnsureStyles();
        Rect viewfinder = GetViewfinderRect();
        topBarRect = new Rect(0f, 0f, Screen.width, viewfinder.y);
        bottomBarRect = new Rect(0f, viewfinder.yMax, Screen.width, Screen.height - viewfinder.yMax);
        DrawSolid(topBarRect, Color.black);
        DrawSolid(bottomBarRect, Color.black);

        float scale = Mathf.Clamp(Screen.width / 1260f, 0.75f, 1.5f);
        DrawHarmonizationButton(topBarRect, scale);
        DrawManualLightButton(topBarRect, scale);
        DrawDebugButton(topBarRect, scale);

        DrawSolid(new Rect(0f, viewfinder.y, Screen.width, 2f), new Color(1f, 1f, 1f, 0.16f));
        DrawSolid(new Rect(0f, viewfinder.yMax - 2f, Screen.width, 2f), new Color(1f, 1f, 1f, 0.16f));
        DrawBottomControls(bottomBarRect, scale);
        DrawManualLightMenu(scale);
        DrawManualLightCalibration(viewfinder, scale);
        DrawCaptureFlash(viewfinder);
    }

    private void DrawHarmonizationButton(Rect topBar, float scale)
    {
        float buttonSize = GetToolbarButtonSize(topBar, scale);
        float leftMargin = Mathf.Max(32f * scale, Screen.safeArea.x + 24f * scale);
        harmonizationButtonRect = new Rect(
            leftMargin,
            topBar.y + (topBar.height - buttonSize) * 0.5f,
            buttonSize,
            buttonSize);

        GUI.enabled = !isCapturing && !isSwitchingCamera && !RuntimeDebugPanel.IsOpen &&
                      !IsManualLightCalibrationActive;
        if (GUI.Button(harmonizationButtonRect, GUIContent.none, GUIStyle.none))
            harmonization?.ToggleFromToolbar();
        GUI.enabled = true;

        Color previous = harmonizationStyle.normal.textColor;
        harmonizationStyle.normal.textColor = harmonization != null && harmonization.IsHarmonizationEnabled
            ? new Color(1f, 0.82f, 0.14f, 1f)
            : Color.white;
        float visualSize = buttonSize * 1.30f;
        var visualRect = new Rect(
            harmonizationButtonRect.center.x - visualSize * 0.5f,
            harmonizationButtonRect.center.y - visualSize * 0.5f,
            visualSize,
            visualSize);
        GUI.Label(visualRect, "H", harmonizationStyle);
        harmonizationStyle.normal.textColor = previous;
    }

    private void DrawManualLightButton(Rect topBar, float scale)
    {
        float buttonSize = GetToolbarButtonSize(topBar, scale);
        manualLightButtonRect = new Rect(
            harmonizationButtonRect.xMax + 72f * scale,
            harmonizationButtonRect.y,
            buttonSize,
            buttonSize);

        bool modelReady = placement != null && placement.IsModelReady;
        bool canInteract = modelReady && !isCapturing && !isSwitchingCamera &&
                           !RuntimeDebugPanel.IsOpen && !IsManualLightCalibrationActive;
        GUI.enabled = canInteract;
        if (GUI.Button(manualLightButtonRect, GUIContent.none, GUIStyle.none))
        {
            if (harmonization != null && harmonization.HasManualLight)
            {
                showManualLightMenu = !showManualLightMenu;
            }
            else if (harmonization != null)
            {
                if (harmonization.BeginManualLightCalibration(out string message))
                    showManualLightMenu = false;
                ShowStatus(message, 3f);
            }
        }
        GUI.enabled = true;

        Texture2D icon = harmonization != null && harmonization.HasManualLight
            ? manualLightReadyIcon
            : manualLightAddIcon;
        if (icon != null)
        {
            Color previous = GUI.color;
            GUI.color = !modelReady
                ? new Color(0.45f, 0.45f, 0.45f, 1f)
                : harmonization != null && harmonization.HasManualLight
                    ? new Color(1f, 0.78f, 0.10f, 1f)
                    : Color.white;
            float visualSize = buttonSize * 1.586f;
            var visualRect = new Rect(
                manualLightButtonRect.center.x - visualSize * 0.5f,
                manualLightButtonRect.center.y - visualSize * 0.5f,
                visualSize,
                visualSize);
            GUI.DrawTexture(visualRect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }
    }

    private void DrawManualLightMenu(float scale)
    {
        if (!showManualLightMenu || harmonization == null || !harmonization.HasManualLight ||
            RuntimeDebugPanel.IsOpen || IsManualLightCalibrationActive)
        {
            manualLightMenuRect = Rect.zero;
            return;
        }

        float toolbarIconSize = GetToolbarButtonSize(topBarRect, scale);
        float width = Mathf.Min(Screen.width - 24f * scale, 1120f * scale);
        float height = 540f * scale;
        manualLightMenuRect = new Rect(
            Mathf.Clamp(manualLightButtonRect.x, 12f, Screen.width - width - 12f),
            topBarRect.yMax + 12f * scale,
            width,
            height);
        DrawSolid(manualLightMenuRect, new Color(0.015f, 0.02f, 0.025f, 0.90f));

        manualLightParameterStyle.fontSize = Mathf.RoundToInt(toolbarIconSize);
        manualLightButtonStyle.fontSize = Mathf.RoundToInt(toolbarIconSize);
        manualLightSliderStyle.fixedHeight = 14f * scale;
        manualLightSliderThumbStyle.fixedWidth = toolbarIconSize;
        manualLightSliderThumbStyle.fixedHeight = toolbarIconSize;

        float horizontalPadding = 24f * scale;
        float labelWidth = 410f * scale;
        float rowHeight = Mathf.Max(100f * scale, toolbarIconSize);
        float rowGap = 28f * scale;
        float sliderGap = 24f * scale;
        float sliderX = manualLightMenuRect.x + horizontalPadding + labelWidth + sliderGap;
        float sliderWidth = manualLightMenuRect.xMax - horizontalPadding - sliderX;
        float firstRowY = manualLightMenuRect.y + 18f * scale;

        Rect FirstLabelRect(float rowY) => new(
            manualLightMenuRect.x + horizontalPadding,
            rowY,
            labelWidth,
            rowHeight);
        Rect SliderRect(float rowY) => new(
            sliderX,
            rowY,
            sliderWidth,
            rowHeight);

        GUI.Label(
            FirstLabelRect(firstRowY),
            $"主光 {harmonization.ManualLightStrength:F2}",
            manualLightParameterStyle);
        float value = GUI.HorizontalSlider(
            SliderRect(firstRowY),
            harmonization.ManualLightStrength,
            0.15f,
            0.65f,
            manualLightSliderStyle,
            manualLightSliderThumbStyle);
        harmonization.SetManualLightStrength(value);

        float secondRowY = firstRowY + rowHeight + rowGap;
        GUI.Label(
            FirstLabelRect(secondRowY),
            $"影长 {harmonization.ShadowLengthScale:F2}",
            manualLightParameterStyle);
        float lengthValue = GUI.HorizontalSlider(
            SliderRect(secondRowY),
            harmonization.ShadowLengthScale,
            0.35f,
            1.10f,
            manualLightSliderStyle,
            manualLightSliderThumbStyle);
        harmonization.SetShadowLengthScale(lengthValue);

        float thirdRowY = secondRowY + rowHeight + rowGap;
        GUI.Label(
            FirstLabelRect(thirdRowY),
            $"硬度 {harmonization.ShadowHardness:F2}",
            manualLightParameterStyle);
        float hardnessValue = GUI.HorizontalSlider(
            SliderRect(thirdRowY),
            harmonization.ShadowHardness,
            0f,
            1f,
            manualLightSliderStyle,
            manualLightSliderThumbStyle);
        harmonization.SetShadowHardness(hardnessValue);

        float buttonY = manualLightMenuRect.yMax - 118f * scale;
        float gap = 20f * scale;
        float buttonWidth = (manualLightMenuRect.width - 36f * scale - gap) * 0.5f;
        if (GUI.Button(
                new Rect(manualLightMenuRect.x + 18f * scale, buttonY, buttonWidth, 100f * scale),
                "重新标定",
                manualLightButtonStyle))
        {
            if (harmonization.BeginManualLightCalibration(out string message))
            {
                showManualLightMenu = false;
                ShowStatus(message, 3f);
            }
            else
            {
                ShowStatus(message, 3f);
            }
        }
        if (GUI.Button(
                new Rect(
                    manualLightMenuRect.x + 18f * scale + buttonWidth + gap,
                    buttonY,
                    buttonWidth,
                    100f * scale),
                "删除光源",
                manualLightButtonStyle))
        {
            harmonization.ClearManualLight();
            showManualLightMenu = false;
            ShowStatus("已恢复 ARCore 自动主光", 3f);
        }
    }

    private void DrawManualLightCalibration(Rect viewfinder, float scale)
    {
        if (harmonization == null || !harmonization.IsManualCalibrationActive)
        {
            manualCalibrationPanelRect = Rect.zero;
            return;
        }

        showManualLightMenu = false;
        float crosshairSize = Mathf.Clamp(Screen.width * 0.12f, 110f, 172f);
        Rect crosshair = new(
            viewfinder.center.x - crosshairSize * 0.5f,
            viewfinder.center.y - crosshairSize * 0.5f,
            crosshairSize,
            crosshairSize);
        Color guideColor = new(1f, 0.78f, 0.10f, 1f);
        DrawOutline(crosshair, guideColor, 5f * scale);
        DrawSolid(new Rect(crosshair.center.x - 2f * scale, crosshair.y - 14f * scale, 4f * scale, 28f * scale), guideColor);
        DrawSolid(new Rect(crosshair.center.x - 2f * scale, crosshair.yMax - 14f * scale, 4f * scale, 28f * scale), guideColor);
        DrawSolid(new Rect(crosshair.x - 14f * scale, crosshair.center.y - 2f * scale, 28f * scale, 4f * scale), guideColor);
        DrawSolid(new Rect(crosshair.xMax - 14f * scale, crosshair.center.y - 2f * scale, 28f * scale, 4f * scale), guideColor);

        float panelWidth = Mathf.Min(viewfinder.width - 40f * scale, 820f * scale);
        float panelHeight = 238f * scale;
        manualCalibrationPanelRect = new Rect(
            viewfinder.center.x - panelWidth * 0.5f,
            viewfinder.yMax - panelHeight - 30f * scale,
            panelWidth,
            panelHeight);
        DrawSolid(manualCalibrationPanelRect, new Color(0f, 0f, 0f, 0.78f));
        GUI.Label(
            new Rect(
                manualCalibrationPanelRect.x + 20f * scale,
                manualCalibrationPanelRect.y + 10f * scale,
                manualCalibrationPanelRect.width - 40f * scale,
                52f * scale),
            "将光源置于准星内",
            calibrationTitleStyle);
        GUI.Label(
            new Rect(
                manualCalibrationPanelRect.x + 20f * scale,
                manualCalibrationPanelRect.y + 64f * scale,
                manualCalibrationPanelRect.width - 40f * scale,
                48f * scale),
            harmonization.ManualCalibrationStatus,
            calibrationBodyStyle);
        Rect progressBackground = new(
            manualCalibrationPanelRect.x + 28f * scale,
            manualCalibrationPanelRect.y + 117f * scale,
            manualCalibrationPanelRect.width - 56f * scale,
            12f * scale);
        DrawSolid(progressBackground, new Color(1f, 1f, 1f, 0.18f));
        DrawSolid(
            new Rect(
                progressBackground.x,
                progressBackground.y,
                progressBackground.width * harmonization.ManualCalibrationProgress,
                progressBackground.height),
            guideColor);
        Rect tintSwatch = new(
            manualCalibrationPanelRect.x + 28f * scale,
            manualCalibrationPanelRect.y + 142f * scale,
            34f * scale,
            34f * scale);
        DrawSolid(tintSwatch, harmonization.ManualCalibrationPreviewTint);

        float gap = 18f * scale;
        float buttonWidth = (manualCalibrationPanelRect.width - 56f * scale - gap) * 0.5f;
        float buttonY = manualCalibrationPanelRect.yMax - 62f * scale;
        if (GUI.Button(
                new Rect(manualCalibrationPanelRect.x + 28f * scale, buttonY, buttonWidth, 50f * scale),
                "取消",
                calibrationButtonStyle))
        {
            harmonization.CancelManualLightCalibration();
            ShowStatus("已取消光源标定", 2.5f);
        }
        if (GUI.Button(
                new Rect(
                    manualCalibrationPanelRect.x + 28f * scale + buttonWidth + gap,
                    buttonY,
                    buttonWidth,
                    50f * scale),
                "完成",
                calibrationButtonStyle))
        {
            if (harmonization.TryCompleteManualLightCalibration(out string message))
                ShowStatus(message, 3f);
            else
                ShowStatus(message, 2.5f);
        }
    }

    private void DrawDebugButton(Rect topBar, float scale)
    {
        float buttonSize = GetToolbarButtonSize(topBar, scale);
        float rightMargin = Mathf.Max(32f * scale, Screen.width - Screen.safeArea.xMax + 24f * scale);
        debugButtonRect = new Rect(
            Screen.width - rightMargin - buttonSize,
            topBar.y + (topBar.height - buttonSize) * 0.5f,
            buttonSize,
            buttonSize);

        GUI.enabled = !IsManualLightCalibrationActive;
        if (GUI.Button(debugButtonRect, GUIContent.none, GUIStyle.none))
            RuntimeDebugPanel.ToggleFromToolbar();
        GUI.enabled = true;
        if (debugIcon != null)
            GUI.DrawTexture(debugButtonRect, debugIcon, ScaleMode.ScaleToFit, true);
    }

    private void DrawBottomControls(Rect bottomBar, float scale)
    {
        float controlsY = bottomBar.y + Mathf.Max(24f * scale, bottomBar.height * 0.17f);
        float originalShutterSize = Mathf.Clamp(bottomBar.height * 0.40f, 126f * scale, 190f * scale);
        float shutterSize = originalShutterSize * 1.5f;
        float secondarySize = originalShutterSize * 0.66f;
        shutterRect = new Rect(
            (Screen.width - shutterSize) * 0.5f,
            controlsY,
            shutterSize,
            shutterSize);
        thumbnailRect = new Rect(
            Mathf.Max(30f * scale, Screen.safeArea.x + 24f * scale),
            controlsY + (shutterSize - secondarySize) * 0.5f,
            secondarySize,
            secondarySize);
        flipRect = new Rect(
            Mathf.Min(Screen.width - secondarySize - 30f * scale,
                Screen.safeArea.xMax - secondarySize - 24f * scale),
            thumbnailRect.y,
            secondarySize,
            secondarySize);
        recycleRect = new Rect(
            flipRect.x - secondarySize - 28f * scale,
            flipRect.y,
            secondarySize,
            secondarySize);
        float expressionGapWidth = shutterRect.x - thumbnailRect.xMax;
        expressionButtonRect = new Rect(
            thumbnailRect.xMax + (expressionGapWidth - secondarySize) * 0.5f,
            thumbnailRect.y,
            secondarySize,
            secondarySize);

        string facing = cameraManager == null
            ? "相机不可用"
            : cameraManager.currentFacingDirection == CameraFacingDirection.User
                ? "前置相机"
                : "后置相机";
        string status = Time.unscaledTime < statusUntil ? operationStatus : $"{facing}  ·  平面定位";
        float statusWidth = Mathf.Min(Screen.width - 40f * scale, 560f * scale);
        GUI.Label(
            new Rect(
                shutterRect.center.x - statusWidth * 0.5f,
                shutterRect.y - 50f * scale,
                statusWidth,
                44f * scale),
            status,
            statusStyle);

        if (lastThumbnail != null)
        {
            GUI.DrawTexture(thumbnailRect, lastThumbnail, ScaleMode.ScaleAndCrop, false);
            DrawOutline(thumbnailRect, new Color(1f, 1f, 1f, 0.65f), 3f * scale);
            if (!RuntimeDebugPanel.IsOpen && !IsManualLightCalibrationActive &&
                GUI.Button(thumbnailRect, GUIContent.none, GUIStyle.none) &&
                (gallery == null || !gallery.OpenLatest()))
                ShowStatus("照片无法打开", 2.5f);
        }
        else
        {
            DrawSolid(thumbnailRect, new Color(1f, 1f, 1f, 0.10f));
            GUI.Label(thumbnailRect, "照片", smallStyle);
        }

        bool canChangeExpression = placement != null && placement.IsModelReady;
        GUI.enabled = !isCapturing && !isSwitchingCamera && !RuntimeDebugPanel.IsOpen &&
                      !IsManualLightCalibrationActive && canChangeExpression;
        if (GUI.Button(expressionButtonRect, GUIContent.none, GUIStyle.none))
        {
            if (placement.TryNextExpression(out string expressionName))
            {
                harmonization?.SetShadowMaskVariant(UsesAlternateShadowMask(expressionName) ? 1 : 0);
                ShowStatus($"表情：{expressionName}", 1.8f);
            }
            else
                ShowStatus("表情不可用", 1.8f);
        }
        GUI.enabled = true;
        if (expressionIcon != null)
        {
            Color previous = GUI.color;
            GUI.color = canChangeExpression
                ? Color.white
                : new Color(0.60f, 0.60f, 0.60f, 1f);
            GUI.DrawTexture(expressionButtonRect, expressionIcon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }
        if (canChangeExpression)
        {
            float labelWidth = secondarySize * 1.7f;
            GUI.Label(
                new Rect(
                    expressionButtonRect.center.x - labelWidth * 0.5f,
                    expressionButtonRect.yMax + 8f * scale,
                    labelWidth,
                    42f * scale),
                "切换表情",
                statusStyle);
        }

        GUI.enabled = !isCapturing && !isSwitchingCamera && !RuntimeDebugPanel.IsOpen &&
                      !IsManualLightCalibrationActive;
        if (GUI.Button(shutterRect, GUIContent.none, GUIStyle.none))
            StartCoroutine(CaptureLayers());
        if (shutterIcon != null)
            GUI.DrawTexture(shutterRect, shutterIcon, ScaleMode.ScaleToFit, true);

        bool canRecycle = placement != null && placement.HasPlacedModel;
        GUI.enabled = !isCapturing && !isSwitchingCamera && !RuntimeDebugPanel.IsOpen &&
                      !IsManualLightCalibrationActive && canRecycle;
        if (GUI.Button(recycleRect, GUIContent.none, GUIStyle.none) && placement.RecyclePlacedModel())
            ShowStatus("洛天依已回收", 2.5f);
        GUI.enabled = true;
        if (recycleIcon != null)
        {
            Color previous = GUI.color;
            GUI.color = canRecycle
                ? Color.white
                : new Color(0.60f, 0.60f, 0.60f, 1f);
            GUI.DrawTexture(recycleRect, recycleIcon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }
        if (canRecycle)
        {
            float labelWidth = secondarySize * 1.7f;
            GUI.Label(
                new Rect(
                    recycleRect.center.x - labelWidth * 0.5f,
                    recycleRect.yMax + 8f * scale,
                    labelWidth,
                    42f * scale),
                "回收天依",
                statusStyle);
        }

        GUI.enabled = !isCapturing && !isSwitchingCamera && !RuntimeDebugPanel.IsOpen &&
                      !IsManualLightCalibrationActive;
        if (GUI.Button(flipRect, GUIContent.none, GUIStyle.none))
            StartCoroutine(ToggleCameraFacing());
        if (flipIcon != null)
            GUI.DrawTexture(flipRect, flipIcon, ScaleMode.ScaleToFit, true);
        GUI.enabled = true;

    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            harmonization?.InvalidateManualLight("应用暂停，AR Session 将重置");
            recycledForPause = placement != null && placement.RecycleForApplicationPause();
            resetSessionAfterResume = true;
            Debug.Log(
                $"[Lifecycle] pause=True, arState={ARSession.state}, " +
                $"recycledPlacement={recycledForPause}");
            return;
        }

        if (resetSessionAfterResume)
            StartCoroutine(ResetARAfterResume());
    }

    private IEnumerator ResetARAfterResume()
    {
        resetSessionAfterResume = false;
        yield return null;

        if (arSession != null)
            arSession.Reset();
        ShowStatus(
            recycledForPause
                ? "应用已恢复，洛天依已回收，请重新扫描平面"
                : "应用已恢复，请重新扫描平面",
            4f);
        Debug.Log(
            $"[Lifecycle] pause=False, arState={ARSession.state}, " +
            $"sessionReset={arSession != null}, recycledPlacement={recycledForPause}");
        recycledForPause = false;
    }

    private IEnumerator ToggleCameraFacing()
    {
        if (cameraManager == null || isCapturing || isSwitchingCamera)
            yield break;

        isSwitchingCamera = true;
        CameraFacingDirection previous = cameraManager.currentFacingDirection == CameraFacingDirection.User
            ? CameraFacingDirection.User
            : CameraFacingDirection.World;
        CameraFacingDirection requested = previous == CameraFacingDirection.World
            ? CameraFacingDirection.User
            : CameraFacingDirection.World;
        harmonization?.PrepareForCameraFacing(requested);
        cameraManager.requestedFacingDirection = requested;
        ShowStatus(requested == CameraFacingDirection.User ? "正在切换到前置相机…" : "正在切换到后置相机…", 3f);

        float deadline = Time.unscaledTime + 3f;
        while (Time.unscaledTime < deadline && cameraManager.currentFacingDirection != requested)
            yield return null;

        if (cameraManager.currentFacingDirection == requested)
        {
            harmonization?.InvalidateManualLight("相机方向已切换");
            placement?.ResetForCameraChange();
            if (arSession != null)
                arSession.Reset();
            else
                Debug.LogWarning("[Camera] 未找到 ARSession，无法显式重置平面追踪");

            ShowStatus("相机已切换，请重新扫描平面", 4f);
            Debug.Log($"[Camera] facing={requested}, placementReset=True, sessionReset={arSession != null}");
        }
        else
        {
            harmonization?.PrepareForCameraFacing(previous);
            cameraManager.requestedFacingDirection = previous;
            ShowStatus("当前设备的 AR 模式不支持该相机", 3f);
            Debug.LogWarning($"[Camera] 无法切换相机: requested={requested}, current={cameraManager.currentFacingDirection}");
        }

        isSwitchingCamera = false;
    }

    private IEnumerator CaptureLayers()
    {
        if (isCapturing || arCamera == null || cameraBackground == null ||
            placement == null || !placement.IsModelReady)
        {
            ShowStatus(placement != null && placement.IsModelReady
                ? "相机尚未就绪"
                : "请先放置洛天依", 2.5f);
            yield break;
        }

        isCapturing = true;
        flashStartedAt = Time.unscaledTime;
        ShowStatus("正在保存相机层…", 4f);

        Rect viewfinder = GetViewfinderRect();
        int cropWidth = Mathf.Clamp(Mathf.RoundToInt(viewfinder.width), 1, Screen.width);
        int cropHeight = Mathf.Clamp(Mathf.RoundToInt(viewfinder.height), 1, Screen.height);
        int cropX = Mathf.Clamp(Mathf.RoundToInt(viewfinder.x), 0, Screen.width - cropWidth);
        int cropY = Mathf.Clamp(
            Screen.height - Mathf.RoundToInt(viewfinder.yMax),
            0,
            Screen.height - cropHeight);

        var cameraLayerTarget = RenderTexture.GetTemporary(
            Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var modelLayerTarget = RenderTexture.GetTemporary(
            Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cameraLayerTarget.name = "AR Camera Layer Capture";
        modelLayerTarget.name = "Live2D Transparent Layer Capture";

        RenderTexture previousTarget = arCamera.targetTexture;
        CameraClearFlags previousClearFlags = arCamera.clearFlags;
        Color previousBackgroundColor = arCamera.backgroundColor;
        bool previousBackgroundEnabled = cameraBackground.enabled;
        bool previousPoseDriverEnabled = trackedPoseDriver != null && trackedPoseDriver.enabled;
        Vector3 frozenPosition = arCamera.transform.position;
        Quaternion frozenRotation = arCamera.transform.rotation;
        Matrix4x4 frozenProjection = arCamera.projectionMatrix;
        Renderer[] modelRenderers = GetCaptureRenderers();
        bool[] rendererStates = new bool[modelRenderers.Length];
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            rendererStates[i] = modelRenderers[i] != null && modelRenderers[i].enabled;
            if (modelRenderers[i] != null)
                modelRenderers[i].enabled = false;
        }

        if (trackedPoseDriver != null)
            trackedPoseDriver.enabled = false;
        arCamera.transform.SetPositionAndRotation(frozenPosition, frozenRotation);
        arCamera.projectionMatrix = frozenProjection;
        cameraBackground.enabled = true;
        arCamera.targetTexture = cameraLayerTarget;
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D cameraLayer = ReadCrop(cameraLayerTarget, cropX, cropY, cropWidth, cropHeight, false);

        ShowStatus("正在保存透明模型层…", 4f);
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            if (modelRenderers[i] != null)
                modelRenderers[i].enabled = rendererStates[i];
        }
        cameraBackground.enabled = false;
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        arCamera.transform.SetPositionAndRotation(frozenPosition, frozenRotation);
        arCamera.projectionMatrix = frozenProjection;
        arCamera.targetTexture = modelLayerTarget;
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D modelLayer = ReadCrop(modelLayerTarget, cropX, cropY, cropWidth, cropHeight, true);

        arCamera.targetTexture = previousTarget;
        arCamera.clearFlags = previousClearFlags;
        arCamera.backgroundColor = previousBackgroundColor;
        cameraBackground.enabled = previousBackgroundEnabled;
        arCamera.transform.SetPositionAndRotation(frozenPosition, frozenRotation);
        arCamera.projectionMatrix = frozenProjection;
        if (trackedPoseDriver != null)
            trackedPoseDriver.enabled = previousPoseDriverEnabled;
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            if (modelRenderers[i] != null)
                modelRenderers[i].enabled = rendererStates[i];
        }
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(cameraLayerTarget);
        RenderTexture.ReleaseTemporary(modelLayerTarget);
        yield return null;

        ShowStatus("正在生成合成图…", 4f);
        Texture2D composite = Composite(cameraLayer, modelLayer);
        byte[] cameraPng = cameraLayer.EncodeToPNG();
        byte[] modelPng = modelLayer.EncodeToPNG();
        byte[] compositePng = composite.EncodeToPNG();
        string captureDirectory = WriteCaptureSet(cameraPng, modelPng, compositePng, cropWidth, cropHeight);
        string galleryUri = PublishCompositeToGallery(compositePng, Path.GetFileName(captureDirectory));
        bool published = !string.IsNullOrEmpty(galleryUri);
        UpdateCaptureGalleryUri(captureDirectory, galleryUri);
        UpdateThumbnail(composite);

        Destroy(cameraLayer);
        Destroy(modelLayer);
        Destroy(composite);
        isCapturing = false;
        flashStartedAt = Time.unscaledTime;
        ShowStatus(published
            ? "已保存三图层，合成图已加入手机相册"
            : "已保存三图层；系统相册写入失败", 4f);
        Debug.Log(
            $"[Capture] 完成: directory={captureDirectory}, size={cropWidth}x{cropHeight}, " +
            $"gallery={(published ? "saved" : "failed")}, facing={cameraManager.currentFacingDirection}");
    }

    private Renderer[] GetCaptureRenderers()
    {
        var unique = new HashSet<Renderer>();
        foreach (var renderer in placement.GetModelRenderersForCapture())
        {
            if (renderer != null)
                unique.Add(renderer);
        }
        if (harmonization != null)
        {
            foreach (var renderer in harmonization.GetAdditionalCaptureRenderers())
            {
                if (renderer != null)
                    unique.Add(renderer);
            }
        }
        var renderers = new Renderer[unique.Count];
        unique.CopyTo(renderers);
        return renderers;
    }

    private static Texture2D ReadCrop(
        RenderTexture source,
        int x,
        int y,
        int width,
        int height,
        bool alpha)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        texture.ReadPixels(new Rect(x, y, width, height), 0, 0, false);
        texture.Apply(false, false);
        RenderTexture.active = previous;

        if (alpha)
            return texture;

        Color32[] pixels = texture.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].a = 255;
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static Texture2D Composite(Texture2D cameraLayer, Texture2D modelLayer)
    {
        Color32[] background = cameraLayer.GetPixels32();
        Color32[] foreground = modelLayer.GetPixels32();
        var output = new Color32[background.Length];
        for (int i = 0; i < output.Length; i++)
        {
            int alpha = foreground[i].a;
            int inverse = 255 - alpha;
            output[i] = new Color32(
                (byte)((foreground[i].r * alpha + background[i].r * inverse + 127) / 255),
                (byte)((foreground[i].g * alpha + background[i].g * inverse + 127) / 255),
                (byte)((foreground[i].b * alpha + background[i].b * inverse + 127) / 255),
                255);
        }

        var composite = new Texture2D(cameraLayer.width, cameraLayer.height, TextureFormat.RGBA32, false, false);
        composite.SetPixels32(output);
        composite.Apply(false, false);
        return composite;
    }

    private string WriteCaptureSet(
        byte[] cameraPng,
        byte[] modelPng,
        byte[] compositePng,
        int width,
        int height)
    {
        string captureId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string directory = Path.Combine(Application.persistentDataPath, CaptureFolderName, captureId);
        Directory.CreateDirectory(directory);
        string cameraPath = Path.Combine(directory, "camera.png");
        string modelPath = Path.Combine(directory, "model.png");
        string compositePath = Path.Combine(directory, "composite.png");
        File.WriteAllBytes(cameraPath, cameraPng);
        File.WriteAllBytes(modelPath, modelPng);
        File.WriteAllBytes(compositePath, compositePng);

        var metadata = new CaptureMetadata
        {
            captureId = captureId,
            createdAt = DateTime.Now.ToString("o"),
            width = width,
            height = height,
            cameraFacing = cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "Unknown",
            cameraLayer = "camera.png",
            modelLayer = "model.png",
            composite = "composite.png"
        };
        File.WriteAllText(
            Path.Combine(directory, "metadata.json"),
            JsonUtility.ToJson(metadata, true));
        PlayerPrefs.SetString(LastCompositePathKey, compositePath);
        PlayerPrefs.Save();
        return directory;
    }

    private static string PublishCompositeToGallery(byte[] png, string captureId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            int sdk = version.GetStatic<int>("SDK_INT");
            if (sdk < 29)
            {
                Debug.LogWarning("[Capture] Android 9 及以下暂只保存到软件内部目录。");
                return null;
            }

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver");
            using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
            using AndroidJavaObject collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
            using var values = new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "_display_name", $"LuoTianyiAR_{captureId}.png");
            values.Call("put", "mime_type", "image/png");
            values.Call("put", "relative_path", GalleryFolderName);
            using AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null)
                return null;

            using (AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
            {
                if (stream == null)
                    return null;
                stream.Call("write", png);
                stream.Call("flush");
            }
            return uri.Call<string>("toString");
        }
        catch (Exception exception)
        {
            Debug.LogError("[Capture] 写入系统相册失败: " + exception.Message);
            return null;
        }
#else
        return null;
#endif
    }

    private static void UpdateCaptureGalleryUri(string captureDirectory, string galleryUri)
    {
        if (string.IsNullOrEmpty(captureDirectory))
            return;

        string metadataPath = Path.Combine(captureDirectory, "metadata.json");
        if (!File.Exists(metadataPath))
            return;

        try
        {
            var metadata = JsonUtility.FromJson<CaptureMetadata>(File.ReadAllText(metadataPath));
            if (metadata == null)
                return;
            metadata.galleryUri = galleryUri ?? string.Empty;
            File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Capture] 无法更新系统相册索引: " + exception.Message);
        }
    }

    private void UpdateThumbnail(Texture2D composite)
    {
        const int thumbnailSize = 192;
        RenderTexture previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(thumbnailSize, thumbnailSize, 0, RenderTextureFormat.ARGB32);
        Texture2D thumbnail;
        try
        {
            Graphics.Blit(composite, target);
            RenderTexture.active = target;
            thumbnail = new Texture2D(thumbnailSize, thumbnailSize, TextureFormat.RGBA32, false);
            thumbnail.ReadPixels(new Rect(0, 0, thumbnailSize, thumbnailSize), 0, 0);
            thumbnail.Apply();
        }
        finally
        {
            // Graphics.Blit 在部分 Android GPU 上会把目标纹理留作 active。
            // 必须先恢复旧状态，再把临时纹理交还池，否则 Unity 会报告渲染状态污染。
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
        if (lastThumbnail != null)
            Destroy(lastThumbnail);
        lastThumbnail = thumbnail;
    }

    private void LoadLastThumbnail()
    {
        ReloadLastThumbnailFromStorage();
    }

    public void ReloadLastThumbnailFromStorage()
    {
        if (lastThumbnail != null)
        {
            Destroy(lastThumbnail);
            lastThumbnail = null;
        }

        string path = FindLatestCompositePath();
        if (string.IsNullOrEmpty(path))
        {
            PlayerPrefs.DeleteKey(LastCompositePathKey);
            PlayerPrefs.Save();
            return;
        }

        PlayerPrefs.SetString(LastCompositePathKey, path);
        PlayerPrefs.Save();

        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(path)))
                UpdateThumbnail(texture);
            Destroy(texture);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Capture] 无法加载最近照片缩略图: " + exception.Message);
        }
    }

    private static string FindLatestCompositePath()
    {
        string preferred = PlayerPrefs.GetString(LastCompositePathKey, string.Empty);
        if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred))
            return preferred;

        string root = Path.Combine(Application.persistentDataPath, CaptureFolderName);
        if (!Directory.Exists(root))
            return null;

        string[] directories = Directory.GetDirectories(root);
        Array.Sort(directories, StringComparer.Ordinal);
        for (int i = directories.Length - 1; i >= 0; i--)
        {
            string candidate = Path.Combine(directories[i], "composite.png");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private void DrawCaptureFlash(Rect viewfinder)
    {
        float elapsed = Time.unscaledTime - flashStartedAt;
        if (elapsed < 0f || elapsed > 0.24f)
            return;

        float alpha = 1f - elapsed / 0.24f;
        DrawSolid(viewfinder, new Color(1f, 1f, 1f, alpha * 0.72f));
    }

    private void ShowStatus(string message, float seconds)
    {
        operationStatus = message;
        statusUntil = Time.unscaledTime + seconds;
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        int titleSize = Mathf.Clamp(Screen.width / 27, 30, 50);
        int bodySize = Mathf.Clamp(Screen.width / 43, 22, 34);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = titleSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        statusStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = bodySize,
            normal = { textColor = new Color(0.92f, 0.92f, 0.92f, 1f) }
        };
        smallStyle = new GUIStyle(statusStyle)
        {
            fontSize = Mathf.Max(18, bodySize - 4)
        };
        harmonizationStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width / 16f * 1.30f), 68, 117),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        calibrationTitleStyle = new GUIStyle(titleStyle)
        {
            fontSize = Mathf.Max(28, titleSize - 2)
        };
        calibrationBodyStyle = new GUIStyle(statusStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = Mathf.Max(20, bodySize - 2)
        };
        calibrationButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(20, bodySize - 2),
            fontStyle = FontStyle.Bold
        };
        manualLightParameterStyle = new GUIStyle(titleStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Normal,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        manualLightButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = titleSize,
            fontStyle = FontStyle.Bold
        };
        manualLightSliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
        manualLightSliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
    }

    private static float GetToolbarButtonSize(Rect topBar, float scale)
    {
        return Mathf.Clamp(topBar.height * 0.28f, 64f * scale, 92f * scale);
    }

    private static bool UsesAlternateShadowMask(string expressionName)
    {
        return expressionName is "共鸣" or "唱歌" or "放松" or "悲伤";
    }

    private static Texture2D CreateManualLightIcon(bool ready)
    {
        const int size = 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = ready ? "ManualLightReadyIcon" : "ManualLightAddIcon",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        Color32 transparent = new(255, 255, 255, 0);
        Color32 solid = new(255, 255, 255, 255);
        Color32 fill = new(255, 255, 255, 70);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        for (int y = 0; y < size; y++)
        {
            float ny = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1);
                float dx = nx - 0.40f;
                float dy = ny - 0.60f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                bool globeOutline = Mathf.Abs(distance - 0.22f) < 0.032f && ny >= 0.43f;
                bool globeFill = ready && distance < 0.19f && ny >= 0.43f;
                bool neck = nx >= 0.31f && nx <= 0.49f && ny >= 0.30f && ny <= 0.45f &&
                            (nx <= 0.345f || nx >= 0.455f);
                bool baseLine = nx >= 0.31f && nx <= 0.49f &&
                                (Mathf.Abs(ny - 0.29f) < 0.025f || Mathf.Abs(ny - 0.23f) < 0.025f);

                bool marker;
                if (ready)
                {
                    float checkA = Mathf.Abs(ny - (0.67f - (nx - 0.64f) * 0.85f));
                    float checkB = Mathf.Abs(ny - (0.58f + (nx - 0.70f) * 0.95f));
                    marker = (nx >= 0.61f && nx <= 0.71f && checkA < 0.030f) ||
                             (nx >= 0.69f && nx <= 0.85f && checkB < 0.030f);
                }
                else
                {
                    marker = (Mathf.Abs(nx - 0.75f) < 0.028f && ny >= 0.59f && ny <= 0.83f) ||
                             (Mathf.Abs(ny - 0.71f) < 0.028f && nx >= 0.63f && nx <= 0.87f);
                }

                int index = y * size + x;
                if (globeOutline || neck || baseLine || marker)
                    pixels[index] = solid;
                else if (globeFill)
                    pixels[index] = fill;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateExpressionIcon()
    {
        const int size = 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ExpressionCycleIcon",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        Color32 transparent = new(255, 255, 255, 0);
        Color32 solid = new(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        Vector2 center = new(size * 0.5f, size * 0.5f);
        float faceRadius = size * 0.38f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                bool face = Mathf.Abs(radius - faceRadius) <= 3.2f;
                bool leftEye = (new Vector2(x, y) - new Vector2(size * 0.37f, size * 0.58f)).sqrMagnitude <= 4.5f * 4.5f;
                bool rightEye = (new Vector2(x, y) - new Vector2(size * 0.63f, size * 0.58f)).sqrMagnitude <= 4.5f * 4.5f;
                float smileY = size * 0.32f + Mathf.Abs(dx) * 0.23f;
                bool smile = Mathf.Abs(y - smileY) <= 3.2f && Mathf.Abs(dx) <= size * 0.22f;
                if (face || leftEye || rightEye || smile)
                    pixels[y * size + x] = solid;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void DrawSolid(Rect rect, Color color)
    {
        if (pixel == null)
            return;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }

    private void DrawOutline(Rect rect, Color color, float thickness)
    {
        DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
        if (pixel != null)
            Destroy(pixel);
        if (lastThumbnail != null)
            Destroy(lastThumbnail);
        if (expressionIcon != null)
            Destroy(expressionIcon);
        if (manualLightAddIcon != null)
            Destroy(manualLightAddIcon);
        if (manualLightReadyIcon != null)
            Destroy(manualLightReadyIcon);
    }

    [Serializable]
    private sealed class CaptureMetadata
    {
        public string captureId;
        public string createdAt;
        public int width;
        public int height;
        public string cameraFacing;
        public string cameraLayer;
        public string modelLayer;
        public string composite;
        public string galleryUri;
    }
}

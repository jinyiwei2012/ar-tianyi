// AutoHarmonizationController.cs — 使用 AR 光照估计对纸片化 Live2D 做自动色调/亮度匹配和地面影子。
using System;
using System.Collections;
using System.Collections.Generic;
using Live2D.Cubism.Rendering;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlaceOnPlane))]
public sealed class AutoHarmonizationController : MonoBehaviour
{
    public const LightEstimation WorldLightEstimation =
        LightEstimation.AmbientIntensity |
        LightEstimation.AmbientColor |
        LightEstimation.AmbientSphericalHarmonics |
        LightEstimation.MainLightDirection |
        LightEstimation.MainLightIntensity;

    public const LightEstimation UserLightEstimation =
        LightEstimation.AmbientIntensity |
        LightEstimation.AmbientColor;

    private const float ArCoreMiddleGrayGamma = 0.466f;
    private const string ShadowObjectName = "LuoTianyi Auto Harmonization Shadow";
    private const int RequiredCalibrationSamples = 8;
    private const float CalibrationSampleIntervalSeconds = 0.12f;
    private const float MaximumCalibrationAngularSpread = 8f;
    private const float EnvironmentSampleIntervalSeconds = 0.35f;
    private static readonly Vector2Int CalibrationImageDimensions = new(128, 96);
    private static readonly int EdgeBlurEnabledId = Shader.PropertyToID("_LuoEdgeBlurEnabled");
    private static readonly int EdgeBlurStrengthId = Shader.PropertyToID("_LuoEdgeBlurStrength");
    private static readonly int EdgeWrapColorId = Shader.PropertyToID("_LuoEdgeWrapColor");
    private static readonly int EdgeWrapStrengthId = Shader.PropertyToID("_LuoEdgeWrapStrength");

    [Header("自动融合")]
    [SerializeField] private bool harmonizationEnabled = true;
    [SerializeField, Min(0.1f)] private float responseSpeed = 5f;
    [SerializeField, Range(0.2f, 1f)] private float minimumBrightness = 0.48f;
    [SerializeField, Range(1f, 2f)] private float maximumBrightness = 1.35f;

    [Header("环境色正片叠底")]
    [SerializeField] private bool multiplyBlendEnabled = true;
    [FormerlySerializedAs("correctionStrength")]
    [SerializeField, Range(0f, 1f)] private float multiplyBlendStrength = 0.72f;
    [SerializeField, Range(0.5f, 1.5f)] private float multiplyBrightnessAdjustment = 1f;

    [Header("角色轮廓模糊")]
    [SerializeField] private bool edgeBlurEnabled;
    [SerializeField, Range(0f, 1f)] private float edgeBlurStrength = 0.35f;

    [Header("人工主光")]
    [SerializeField, Range(0.15f, 0.65f)] private float manualKeyStrength = 0.36f;
    [SerializeField, Range(0f, 1f)] private float manualAmbientInfluence = 0.45f;
    [SerializeField, Range(0.6f, 1f)] private float manualMinimumBrightness = 0.82f;

    [Header("影子")]
    [SerializeField] private Material shadowMaterial;
    [SerializeField] private Texture2D primaryShadowMask;
    [SerializeField] private Texture2D alternateShadowMask;
    [SerializeField, Range(0f, 1f)] private float ambientShadowOpacity = 0.14f;
    [SerializeField, Range(0f, 1f)] private float directionalShadowOpacity = 0.36f;
    [SerializeField, Range(0f, 1f)] private float shadowSoftness = 0.62f;
    [SerializeField, Range(0.35f, 1.10f)] private float shadowLengthScale = 0.55f;
    [SerializeField, Min(0f)] private float shadowPlaneOffsetMeters = 0.003f;

    private readonly List<CubismColorState> rendererStates = new();
    private readonly List<Vector3> calibrationDirections = new();
    private readonly List<Color> calibrationColors = new();
    private readonly List<float> calibrationLuminances = new();
    private readonly List<float> calibrationOverexposureRatios = new();
    private readonly Vector3[] sphericalHarmonicDirections = new Vector3[1];
    private readonly Color[] sphericalHarmonicColors = new Color[1];

    private PlaceOnPlane placement;
    private ARCameraManager cameraManager;
    private ARCameraManager subscribedCameraManager;
    private Transform boundModel;
    private GameObject shadowObject;
    private MeshRenderer shadowRenderer;
    private Mesh shadowMesh;
    private Material runtimeShadowMaterial;
    private MaterialPropertyBlock shadowPropertyBlock;

    private ARLightEstimationData latestLightEstimation;
    private bool hasLightEstimationFrame;
    private float lastLightFrameAt = -1f;
    private Color smoothedMultiply = Color.white;
    private Color smoothedScreen = Color.black;
    private bool hasSmoothedCorrection;
    private bool colorsAreOverridden;
    private Vector3 paperNormal = Vector3.back;
    private Vector3 mainLightDirection = Vector3.down;
    private Color evaluatedLightColor = Color.white;
    private Color ambientTintForShadow = Color.white;
    private Color shadowTint = Color.white;
    private float brightnessFactor = 1f;
    private float paperDiffuse;
    private float mainLightBrightness;
    private float shadowOpacity;
    private float shadowLengthMeters;
    private float shadowLightElevationDegrees;
    private int shadowMaskVariant;
    private Vector3 shadowPlaneNormal = Vector3.up;
    private string estimationSource = "waiting";
    private string lastLoggedSource;
    private bool directionalLightAvailableForShadow;

    private bool manualCalibrationActive;
    private bool hasManualLight;
    private Vector3 manualLightToSource = Vector3.up;
    private Color manualLightTint = Color.white;
    private float manualLightCctKelvin = 6500f;
    private float manualLightConfidence;
    private float manualAngularSpreadDegrees;
    private float calibrationOverexposureRatio;
    private string manualCalibrationStatus = "not-calibrated";
    private Coroutine calibrationCoroutine;
    private int calibrationGeneration;
    private Vector3 arCoreMainDirectionRaw;
    private float manualToArCoreRawAngle = -1f;
    private float manualToArCoreNegatedAngle = -1f;
    private Coroutine environmentSamplingCoroutine;
    private int environmentSamplingGeneration;
    private bool hasEnvironmentSample;
    private Color environmentSampleColor = Color.white;
    private float environmentSampleLuminance = 0.5f;
    private float environmentSampleConfidence;
    private float environmentSampledAt = -1f;
    private float environmentTintWeight;
    private float environmentBrightnessWeight;
    private Color edgeWrapColor = Color.white;
    private float edgeWrapStrength;
    private string environmentSamplingStatus = "waiting";
    private float lastEnvironmentSamplingWarningAt = -10f;

    public bool IsHarmonizationEnabled => harmonizationEnabled;
    public bool HasConfiguredShadowMaterial => shadowMaterial != null;
    public bool HasConfiguredShadowMasks => primaryShadowMask != null && alternateShadowMask != null;
    public bool IsManualCalibrationActive => manualCalibrationActive;
    public bool HasManualLight => hasManualLight;
    public bool CanBeginManualLightCalibration =>
        placement != null && placement.IsModelReady && cameraManager != null &&
        ARSession.state == ARSessionState.SessionTracking;
    public float ManualCalibrationProgress =>
        Mathf.Clamp01(calibrationColors.Count / (float)RequiredCalibrationSamples);
    public string ManualCalibrationStatus => manualCalibrationStatus;
    public Color ManualCalibrationPreviewTint => calibrationColors.Count > 0
        ? NormalizeTint(MedianColor(calibrationColors))
        : manualLightTint;
    public float ManualLightStrength => manualKeyStrength;
    public float ShadowLengthScale => shadowLengthScale;
    public float ShadowHardness => 1f - shadowSoftness;
    public int ShadowMaskVariant => shadowMaskVariant;
    public bool IsMultiplyBlendEnabled => multiplyBlendEnabled;
    public float MultiplyBlendStrength => multiplyBlendStrength;
    public float MultiplyBrightnessAdjustment => multiplyBrightnessAdjustment;
    public bool IsEdgeBlurEnabled => edgeBlurEnabled;
    public float EdgeBlurStrength => edgeBlurStrength;

    private void Awake()
    {
        placement = GetComponent<PlaceOnPlane>();
        EnsureCameraManager();
        Debug.Log("[Harmonization] 自动融合控制器已启动，默认状态=ON");
    }

    private void OnEnable()
    {
        EnsureCameraManager();
        environmentSamplingGeneration++;
        environmentSamplingCoroutine = StartCoroutine(
            CollectEnvironmentSamples(environmentSamplingGeneration));
    }

    private void OnDisable()
    {
        manualCalibrationActive = false;
        calibrationGeneration++;
        environmentSamplingGeneration++;
        environmentSamplingCoroutine = null;
        UnsubscribeCameraManager();
        RestoreCubismColors();
        SetShadowVisible(false);
        ApplyEdgeBlurGlobals(false);
    }

    private void LateUpdate()
    {
        EnsureCameraManager();

        if (!TryGetTarget(
                out Transform poseRoot,
                out Transform model,
                out Vector3 footPosition,
                out float heightMeters,
                out Vector3 placementPlaneNormal))
        {
            ReleaseBoundModel();
            SetShadowVisible(false);
            ApplyEdgeBlurGlobals(false);
            return;
        }

        if (boundModel != model)
            BindModel(model);

        paperNormal = poseRoot.TransformDirection(Vector3.back).normalized;
        if (!harmonizationEnabled)
        {
            RestoreCubismColors();
            SetShadowVisible(false);
            ApplyEdgeBlurGlobals(false);
            return;
        }

        EvaluatePaperLighting(paperNormal, out Color targetMultiply, out Color targetScreen);
        float blend = hasSmoothedCorrection
            ? 1f - Mathf.Exp(-responseSpeed * Time.unscaledDeltaTime)
            : 1f;
        smoothedMultiply = Color.Lerp(smoothedMultiply, targetMultiply, blend);
        smoothedScreen = Color.Lerp(smoothedScreen, targetScreen, blend);
        hasSmoothedCorrection = true;

        if (multiplyBlendEnabled)
            ApplyCubismColors(smoothedMultiply, smoothedScreen);
        else
            RestoreCubismColors();
        ApplyEdgeBlurGlobals(edgeBlurEnabled);
        UpdateShadow(poseRoot, footPosition, heightMeters, placementPlaneNormal);
        LogSourceTransitionIfNeeded();
    }

    public void ToggleFromToolbar()
    {
        SetHarmonizationEnabled(!harmonizationEnabled);
    }

    public void SetHarmonizationEnabled(bool value)
    {
        if (harmonizationEnabled == value)
            return;

        harmonizationEnabled = value;
        if (!harmonizationEnabled)
        {
            RestoreCubismColors();
            SetShadowVisible(false);
        }
        else
        {
            hasSmoothedCorrection = false;
        }

        Debug.Log($"[Harmonization] 用户切换自动融合: {(harmonizationEnabled ? "ON" : "OFF")}");
    }

    public void SetMultiplyBlendEnabled(bool value)
    {
        if (multiplyBlendEnabled == value)
            return;
        multiplyBlendEnabled = value;
        hasSmoothedCorrection = false;
        if (!multiplyBlendEnabled)
            RestoreCubismColors();
        Debug.Log($"[Harmonization] 环境色正片叠底: {(value ? "ON" : "OFF")}");
    }

    public void SetMultiplyBlendStrength(float value)
    {
        multiplyBlendStrength = Mathf.Clamp01(value);
    }

    public void SetMultiplyBrightnessAdjustment(float value)
    {
        multiplyBrightnessAdjustment = Mathf.Clamp(value, 0.5f, 1.5f);
    }

    public void SetEdgeBlurEnabled(bool value)
    {
        if (edgeBlurEnabled == value)
            return;
        edgeBlurEnabled = value;
        ApplyEdgeBlurGlobals(harmonizationEnabled && edgeBlurEnabled);
        Debug.Log($"[Harmonization] 角色轮廓模糊: {(value ? "ON" : "OFF")}");
    }

    public void SetEdgeBlurStrength(float value)
    {
        edgeBlurStrength = Mathf.Clamp01(value);
        ApplyEdgeBlurGlobals(harmonizationEnabled && edgeBlurEnabled);
    }

    public bool BeginManualLightCalibration(out string message)
    {
        EnsureCameraManager();
        if (manualCalibrationActive)
        {
            message = "正在标定光源";
            return false;
        }
        if (placement == null || !placement.IsModelReady)
        {
            message = "请先加载洛天依";
            return false;
        }
        if (ARSession.state != ARSessionState.SessionTracking || cameraManager == null)
        {
            message = "请等待 AR 空间追踪稳定";
            return false;
        }

        calibrationDirections.Clear();
        calibrationColors.Clear();
        calibrationLuminances.Clear();
        calibrationOverexposureRatios.Clear();
        manualAngularSpreadDegrees = 0f;
        calibrationOverexposureRatio = 0f;
        manualCalibrationStatus = "请将光源置于准星内并保持稳定";
        manualCalibrationActive = true;
        int generation = ++calibrationGeneration;
        calibrationCoroutine = StartCoroutine(CollectManualLightSamples(generation));
        message = "请对准光源并保持稳定";
        Debug.Log("[Harmonization] 开始人工主光标定");
        return true;
    }

    public bool TryCompleteManualLightCalibration(out string message)
    {
        if (!manualCalibrationActive)
        {
            message = "当前没有正在进行的光源标定";
            return false;
        }
        if (calibrationColors.Count < RequiredCalibrationSamples)
        {
            message = $"请继续保持稳定（{calibrationColors.Count}/{RequiredCalibrationSamples}）";
            manualCalibrationStatus = message;
            return false;
        }

        Vector3 averageDirection = AverageDirection(calibrationDirections);
        manualAngularSpreadDegrees = MaximumAngularDeviation(calibrationDirections, averageDirection);
        if (manualAngularSpreadDegrees > MaximumCalibrationAngularSpread)
        {
            message = $"手机移动过大，请保持稳定（{manualAngularSpreadDegrees:F1}°）";
            manualCalibrationStatus = message;
            return false;
        }

        Color sampledColor = MedianColor(calibrationColors);
        float sampledLuminance = Median(calibrationLuminances);
        calibrationOverexposureRatio = Median(calibrationOverexposureRatios);
        manualLightToSource = averageDirection;
        manualLightTint = NormalizeTint(Color.Lerp(Color.white, sampledColor, 0.68f));
        manualLightCctKelvin = EstimateCorrelatedColorTemperature(manualLightTint);
        manualKeyStrength = Mathf.Clamp(
            0.30f + (sampledLuminance - 0.45f) * 0.24f,
            0.24f,
            0.48f);
        manualLightConfidence = Mathf.Clamp01(
            Mathf.Clamp01(calibrationColors.Count / 12f) *
            (1f - manualAngularSpreadDegrees / 18f) *
            (1f - calibrationOverexposureRatio * 0.45f));
        hasManualLight = true;
        manualCalibrationActive = false;
        calibrationGeneration++;
        calibrationCoroutine = null;
        UpdateManualArCoreComparison();
        manualCalibrationStatus =
            $"已标定 {manualLightCctKelvin:F0}K，强度 {manualKeyStrength:F2}";
        message = "人工主光已添加";
        Debug.Log(
            $"[Harmonization] 人工主光标定完成: toSource={manualLightToSource:F3}, " +
            $"tint={FormatColor(manualLightTint)}, cct={manualLightCctKelvin:F0}K, " +
            $"strength={manualKeyStrength:F3}, confidence={manualLightConfidence:F3}, " +
            $"spread={manualAngularSpreadDegrees:F2}deg, overexposure={calibrationOverexposureRatio:F3}");
        return true;
    }

    public void CancelManualLightCalibration()
    {
        if (!manualCalibrationActive)
            return;
        manualCalibrationActive = false;
        calibrationGeneration++;
        manualCalibrationStatus = hasManualLight ? "保留上一次人工主光" : "标定已取消";
        Debug.Log("[Harmonization] 人工主光标定已取消");
    }

    public void ClearManualLight(string reason = "用户删除")
    {
        manualCalibrationActive = false;
        calibrationGeneration++;
        hasManualLight = false;
        manualCalibrationStatus = "not-calibrated";
        manualToArCoreRawAngle = -1f;
        manualToArCoreNegatedAngle = -1f;
        hasSmoothedCorrection = false;
        Debug.Log($"[Harmonization] 人工主光已清除: reason={reason}");
    }

    public void InvalidateManualLight(string reason)
    {
        if (!hasManualLight && !manualCalibrationActive)
            return;
        ClearManualLight(reason);
    }

    public void SetManualLightStrength(float value)
    {
        manualKeyStrength = Mathf.Clamp(value, 0.15f, 0.65f);
        if (hasManualLight)
            manualCalibrationStatus = $"已标定 {manualLightCctKelvin:F0}K，强度 {manualKeyStrength:F2}";
    }

    public void SetShadowLengthScale(float value)
    {
        shadowLengthScale = Mathf.Clamp(value, 0.35f, 1.10f);
    }

    public void SetShadowHardness(float value)
    {
        shadowSoftness = 1f - Mathf.Clamp01(value);
    }

    public void SetShadowMaskVariant(int variant)
    {
        int nextVariant = variant == 1 && alternateShadowMask != null ? 1 : 0;
        if (shadowMaskVariant == nextVariant)
            return;

        shadowMaskVariant = nextVariant;
        Debug.Log($"[Harmonization] 影子遮罩切换: variant={shadowMaskVariant + 1}");
    }

    public void PrepareForCameraFacing(CameraFacingDirection facingDirection)
    {
        EnsureCameraManager();
        if (cameraManager == null)
            return;

        LightEstimation requested = facingDirection == CameraFacingDirection.User
            ? UserLightEstimation
            : WorldLightEstimation;
        cameraManager.requestedLightEstimation = requested;
        Debug.Log($"[Harmonization] 相机方向预配置: facing={facingDirection}, requested={requested}");
    }

    public Renderer[] GetAdditionalCaptureRenderers()
    {
        return shadowRenderer != null
            ? new Renderer[] { shadowRenderer }
            : Array.Empty<Renderer>();
    }

    public string GetDebugSummary()
    {
        EnsureCameraManager();
        string age = lastLightFrameAt >= 0f
            ? $"{Time.unscaledTime - lastLightFrameAt:F2}s"
            : "never";
        string requested = cameraManager != null
            ? cameraManager.requestedLightEstimation.ToString()
            : "missing";
        string current = cameraManager != null
            ? cameraManager.currentLightEstimation.ToString()
            : "missing";

        return
            $"enabled={harmonizationEnabled}, source={estimationSource}, frame={hasLightEstimationFrame}, age={age}\n" +
            $"lightEstimation: requested={requested}, current={current}\n" +
            $"paperNormal={paperNormal:F3}, mainDirection={mainLightDirection:F3}, NdotL={paperDiffuse:F3}\n" +
            $"evaluatedRGB={FormatColor(evaluatedLightColor)}, brightness={brightnessFactor:F3}, mainBrightness={mainLightBrightness:F3}\n" +
            $"multiply={FormatColor(smoothedMultiply)}, screen={FormatColor(smoothedScreen)}\n" +
            $"environmentMultiply: enabled={multiplyBlendEnabled}, strength={multiplyBlendStrength:F2}, brightnessAdjust={multiplyBrightnessAdjustment:F2}, sampled={hasEnvironmentSample}, rgb={FormatColor(environmentSampleColor)}, luminance={environmentSampleLuminance:F3}, confidence={environmentSampleConfidence:F3}, tintWeight={environmentTintWeight:F3}, brightnessWeight={environmentBrightnessWeight:F3}, age={(environmentSampledAt >= 0f ? (Time.unscaledTime - environmentSampledAt).ToString("F2") + "s" : "never")}\n" +
            $"environmentSamplingStatus={environmentSamplingStatus}\n" +
            $"edgeBlur: enabled={edgeBlurEnabled}, strength={edgeBlurStrength:F2}, radius={edgeBlurStrength * 6f:F1}px, wrapColor={FormatColor(edgeWrapColor)}, wrapStrength={edgeWrapStrength:F3}\n" +
            $"shadow: visible={shadowRenderer != null && shadowRenderer.enabled}, opacity={shadowOpacity:F3}, length={shadowLengthMeters:F3}m, lengthScale={shadowLengthScale:F2}, elevation={shadowLightElevationDegrees:F1}deg, mask={shadowMaskVariant + 1}, tint={FormatColor(shadowTint)}, softness={shadowSoftness:F2}, hardness={ShadowHardness:F2}\n" +
            $"shadowPlane: normal={shadowPlaneNormal:F3}, meshUp={(shadowObject != null ? shadowObject.transform.up.ToString("F3") : "missing")}, alignment={(shadowObject != null ? Vector3.Dot(shadowObject.transform.up, shadowPlaneNormal).ToString("F4") : "missing")}\n" +
            $"manual: calibrated={hasManualLight}, calibrating={manualCalibrationActive}, samples={calibrationColors.Count}/{RequiredCalibrationSamples}, status={manualCalibrationStatus}\n" +
            $"manualLight: toSource={manualLightToSource:F3}, tint={FormatColor(manualLightTint)}, cct={manualLightCctKelvin:F0}K, strength={manualKeyStrength:F3}, confidence={manualLightConfidence:F3}\n" +
            $"manualQuality: spread={manualAngularSpreadDegrees:F2}deg, overexposure={calibrationOverexposureRatio:F3}\n" +
            $"ARCoreDirectionRaw={arCoreMainDirectionRaw:F3}, angle(raw)={FormatAngle(manualToArCoreRawAngle)}, angle(negated)={FormatAngle(manualToArCoreNegatedAngle)}";
    }

    public string GetCompactStatus()
    {
        return
            $"{(harmonizationEnabled ? "ON" : "OFF")}/{estimationSource}  " +
            $"主光={(hasManualLight ? $"手动{manualLightCctKelvin:F0}K" : "自动")}  " +
            $"亮度={brightnessFactor:F2}  NdotL={paperDiffuse:F2}  " +
            $"影子={(harmonizationEnabled ? $"{shadowOpacity:F2}/{shadowLengthMeters:F2}m/{shadowLightElevationDegrees:F0}°" : "隐藏")}";
    }

    public static float ComputePaperDiffuse(Vector3 frontNormal, Vector3 lightRayDirection)
    {
        if (frontNormal.sqrMagnitude < 0.000001f || lightRayDirection.sqrMagnitude < 0.000001f)
            return 0f;
        return Mathf.Clamp01(Vector3.Dot(frontNormal.normalized, -lightRayDirection.normalized));
    }

    public static float ComputeManualPaperDiffuse(Vector3 frontNormal, Vector3 lightToSourceDirection)
    {
        if (frontNormal.sqrMagnitude < 0.000001f || lightToSourceDirection.sqrMagnitude < 0.000001f)
            return 0f;
        return Mathf.Clamp01(Vector3.Dot(frontNormal.normalized, lightToSourceDirection.normalized));
    }

    public static Vector3 ComputeManualShadowDirection(Vector3 lightToSourceDirection, Vector3 planeNormal)
    {
        if (lightToSourceDirection.sqrMagnitude < 0.000001f || planeNormal.sqrMagnitude < 0.000001f)
            return Vector3.zero;
        Vector3 direction = Vector3.ProjectOnPlane(-lightToSourceDirection.normalized, planeNormal.normalized);
        return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.zero;
    }

    public static float ComputeShadowLightElevationDegrees(
        Vector3 lightRayDirection,
        Vector3 planeNormal)
    {
        if (lightRayDirection.sqrMagnitude < 0.000001f || planeNormal.sqrMagnitude < 0.000001f)
            return 90f;

        Vector3 ray = lightRayDirection.normalized;
        Vector3 normal = planeNormal.normalized;
        float horizontal = Vector3.ProjectOnPlane(ray, normal).magnitude;
        float vertical = Mathf.Abs(Vector3.Dot(ray, normal));
        return Mathf.Atan2(vertical, Mathf.Max(0.0001f, horizontal)) * Mathf.Rad2Deg;
    }

    public static float ComputeShadowLengthMeters(
        float heightMeters,
        Vector3 lightRayDirection,
        Vector3 planeNormal,
        float lengthScale)
    {
        if (heightMeters <= 0f || lightRayDirection.sqrMagnitude < 0.000001f ||
            planeNormal.sqrMagnitude < 0.000001f)
            return 0f;

        Vector3 ray = lightRayDirection.normalized;
        Vector3 normal = planeNormal.normalized;
        float horizontal = Vector3.ProjectOnPlane(ray, normal).magnitude;
        float vertical = Mathf.Max(0.25f, Mathf.Abs(Vector3.Dot(ray, normal)));
        float physicalLength = Mathf.Clamp(
            heightMeters * horizontal / vertical,
            heightMeters * 0.16f,
            heightMeters * 1.20f);
        return Mathf.Clamp(
            physicalLength * Mathf.Clamp(lengthScale, 0.35f, 1.10f),
            Mathf.Max(0.025f, heightMeters * 0.10f),
            heightMeters * 0.85f);
    }

    private IEnumerator CollectManualLightSamples(int generation)
    {
        var wait = new WaitForSecondsRealtime(CalibrationSampleIntervalSeconds);
        while (manualCalibrationActive && generation == calibrationGeneration)
        {
            EnsureCameraManager();
            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                manualCalibrationStatus = "正在等待相机图像…";
                yield return wait;
                continue;
            }

            Vector3 capturedDirection = cameraManager.transform.forward.normalized;
            XRCpuImage.AsyncConversion request = default;
            bool conversionStarted = false;
            try
            {
                request = image.ConvertAsync(new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = CalibrationImageDimensions,
                    outputFormat = TextureFormat.RGB24,
                    transformation = XRCpuImage.Transformation.None
                });
                conversionStarted = true;
            }
            catch (Exception exception)
            {
                manualCalibrationStatus = "设备暂不支持光色采样";
                Debug.LogWarning($"[Harmonization] CPU 相机图像转换启动失败: {exception.Message}");
            }
            image.Dispose();
            if (!conversionStarted)
            {
                yield return wait;
                continue;
            }

            while (!request.status.IsDone())
                yield return null;

            if (manualCalibrationActive && generation == calibrationGeneration &&
                request.status == XRCpuImage.AsyncConversionStatus.Ready &&
                TrySampleLightColor(
                    request.GetData<byte>(),
                    request.conversionParams.outputDimensions,
                    out Color sampledColor,
                    out float sampledLuminance,
                    out float overexposure))
            {
                calibrationDirections.Add(capturedDirection);
                calibrationColors.Add(sampledColor);
                calibrationLuminances.Add(sampledLuminance);
                calibrationOverexposureRatios.Add(overexposure);
                Vector3 averageDirection = AverageDirection(calibrationDirections);
                manualAngularSpreadDegrees = MaximumAngularDeviation(calibrationDirections, averageDirection);
                calibrationOverexposureRatio = Median(calibrationOverexposureRatios);
                manualCalibrationStatus = manualAngularSpreadDegrees > MaximumCalibrationAngularSpread
                    ? $"手机移动过大，请保持稳定（{manualAngularSpreadDegrees:F1}°）"
                    : calibrationColors.Count < RequiredCalibrationSamples
                        ? $"正在采样光色（{calibrationColors.Count}/{RequiredCalibrationSamples}）"
                        : $"采样完成，可点击完成（约 {EstimateCorrelatedColorTemperature(NormalizeTint(MedianColor(calibrationColors))):F0}K）";

                const int maximumRetainedSamples = 24;
                if (calibrationColors.Count > maximumRetainedSamples)
                {
                    calibrationDirections.RemoveAt(0);
                    calibrationColors.RemoveAt(0);
                    calibrationLuminances.RemoveAt(0);
                    calibrationOverexposureRatios.RemoveAt(0);
                }
            }
            else if (manualCalibrationActive && generation == calibrationGeneration)
            {
                manualCalibrationStatus = "未取得有效光色，请避免灯芯完全充满准星";
            }

            request.Dispose();
            yield return wait;
        }

        if (generation == calibrationGeneration)
            calibrationCoroutine = null;
    }

    private IEnumerator CollectEnvironmentSamples(int generation)
    {
        var wait = new WaitForSecondsRealtime(EnvironmentSampleIntervalSeconds);
        while (isActiveAndEnabled && generation == environmentSamplingGeneration)
        {
            if (!harmonizationEnabled || !multiplyBlendEnabled || manualCalibrationActive ||
                boundModel == null || cameraManager == null)
            {
                yield return wait;
                continue;
            }

            if (!TryGetModelScreenRect(out Rect modelScreenRect))
            {
                ReportEnvironmentSamplingWait("model-screen-rect-unavailable");
                yield return wait;
                continue;
            }

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                ReportEnvironmentSamplingWait("cpu-image-unavailable");
                yield return wait;
                continue;
            }

            XRCpuImage.AsyncConversion request = default;
            bool conversionStarted = false;
            try
            {
                request = image.ConvertAsync(new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = CalibrationImageDimensions,
                    outputFormat = TextureFormat.RGB24,
                    transformation = XRCpuImage.Transformation.None
                });
                conversionStarted = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Harmonization] 环境色采样转换启动失败: {exception.Message}");
            }
            image.Dispose();

            if (!conversionStarted)
            {
                yield return wait;
                continue;
            }

            while (!request.status.IsDone())
                yield return null;

            string samplingStatus = request.status.ToString();
            if (generation == environmentSamplingGeneration &&
                request.status == XRCpuImage.AsyncConversionStatus.Ready &&
                TrySampleEnvironmentColor(
                    request.GetData<byte>(),
                    request.conversionParams.outputDimensions,
                    modelScreenRect,
                    out Color sampledColor,
                    out float sampledLuminance,
                    out float confidence,
                    out samplingStatus))
            {
                bool firstSample = !hasEnvironmentSample;
                float response = hasEnvironmentSample ? 0.28f : 1f;
                environmentSampleColor = Color.Lerp(environmentSampleColor, sampledColor, response);
                environmentSampleLuminance = Mathf.Lerp(
                    environmentSampleLuminance,
                    sampledLuminance,
                    response);
                environmentSampleConfidence = Mathf.Lerp(
                    environmentSampleConfidence,
                    confidence,
                    response);
                environmentSampledAt = Time.unscaledTime;
                hasEnvironmentSample = true;
                environmentSamplingStatus = samplingStatus;
                if (firstSample)
                {
                    Debug.Log(
                        $"[Harmonization] 环境色环采样就绪: rgb={FormatColor(environmentSampleColor)}, " +
                        $"luminance={environmentSampleLuminance:F3}, confidence={environmentSampleConfidence:F3}");
                }
            }
            else if (generation == environmentSamplingGeneration &&
                     request.status == XRCpuImage.AsyncConversionStatus.Ready)
            {
                environmentSamplingStatus = samplingStatus;
                if (Time.unscaledTime - lastEnvironmentSamplingWarningAt >= 5f)
                {
                    lastEnvironmentSamplingWarningAt = Time.unscaledTime;
                    Debug.LogWarning($"[Harmonization] 环境色环采样未取得有效结果: {samplingStatus}");
                }
            }

            request.Dispose();
            yield return wait;
        }

        if (generation == environmentSamplingGeneration)
            environmentSamplingCoroutine = null;
    }

    private void ReportEnvironmentSamplingWait(string status)
    {
        environmentSamplingStatus = status;
        if (Time.unscaledTime - lastEnvironmentSamplingWarningAt < 5f)
            return;
        lastEnvironmentSamplingWarningAt = Time.unscaledTime;
        Debug.LogWarning($"[Harmonization] 环境色环采样等待: {status}");
    }

    private void EnsureCameraManager()
    {
        var found = cameraManager != null
            ? cameraManager
            : FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (found == subscribedCameraManager)
            return;

        UnsubscribeCameraManager();
        cameraManager = found;
        if (cameraManager == null)
            return;

        subscribedCameraManager = cameraManager;
        subscribedCameraManager.frameReceived += OnCameraFrameReceived;
        PrepareForCameraFacing(cameraManager.currentFacingDirection == CameraFacingDirection.User
            ? CameraFacingDirection.User
            : CameraFacingDirection.World);
    }

    private void UnsubscribeCameraManager()
    {
        if (subscribedCameraManager != null)
            subscribedCameraManager.frameReceived -= OnCameraFrameReceived;
        subscribedCameraManager = null;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        latestLightEstimation = eventArgs.lightEstimation;
        hasLightEstimationFrame =
            latestLightEstimation.averageBrightness.HasValue ||
            latestLightEstimation.colorCorrection.HasValue ||
            latestLightEstimation.ambientSphericalHarmonics.HasValue ||
            latestLightEstimation.mainLightDirection.HasValue ||
            latestLightEstimation.mainLightColor.HasValue ||
            latestLightEstimation.mainLightIntensityLumens.HasValue;
        lastLightFrameAt = Time.unscaledTime;
        arCoreMainDirectionRaw = latestLightEstimation.mainLightDirection.HasValue
            ? latestLightEstimation.mainLightDirection.Value.normalized
            : Vector3.zero;
        UpdateManualArCoreComparison();
    }

    private bool TryGetTarget(
        out Transform poseRoot,
        out Transform model,
        out Vector3 footPosition,
        out float heightMeters,
        out Vector3 placementPlaneNormal)
    {
        poseRoot = null;
        model = null;
        footPosition = default;
        heightMeters = 0f;
        placementPlaneNormal = Vector3.up;
        return placement != null &&
               placement.TryGetHarmonizationTarget(
                   out poseRoot,
                   out model,
                   out footPosition,
                   out heightMeters,
                   out placementPlaneNormal);
    }

    private void BindModel(Transform model)
    {
        RestoreCubismColors();
        boundModel = model;
        shadowMaskVariant = 0;
        rendererStates.Clear();

        foreach (var renderer in placement.GetCubismRenderersForHarmonization())
        {
            if (renderer == null)
                continue;
            rendererStates.Add(new CubismColorState(renderer));
        }

        hasSmoothedCorrection = false;
        Debug.Log($"[Harmonization] 已绑定 Live2D 纸片: renderers={rendererStates.Count}");
    }

    private void ReleaseBoundModel()
    {
        RestoreCubismColors();
        boundModel = null;
        rendererStates.Clear();
        hasSmoothedCorrection = false;
    }

    private void EvaluatePaperLighting(Vector3 frontNormal, out Color multiply, out Color screen)
    {
        Color rawLight = Color.white;
        Color tint = Color.white;
        Color ambientColor = Color.white;
        Color ambientTint = Color.white;
        float ambientBrightness = 1f;
        string ambientSource = "fallback";
        brightnessFactor = 1f;
        paperDiffuse = 0f;
        mainLightBrightness = 0f;
        mainLightDirection = Vector3.down;
        estimationSource = "unavailable";
        directionalLightAvailableForShadow = false;

        bool hasSphericalHarmonics = latestLightEstimation.ambientSphericalHarmonics.HasValue;
        bool hasMainDirection = latestLightEstimation.mainLightDirection.HasValue;
        bool hasAmbientMode = latestLightEstimation.averageBrightness.HasValue ||
                              latestLightEstimation.colorCorrection.HasValue;
        bool hasArAmbientLighting = hasSphericalHarmonics || hasAmbientMode;
        environmentTintWeight = 0f;
        environmentBrightnessWeight = 0f;

        if (hasSphericalHarmonics)
        {
            sphericalHarmonicDirections[0] = frontNormal;
            var harmonics = latestLightEstimation.ambientSphericalHarmonics.Value;
            harmonics.Evaluate(sphericalHarmonicDirections, sphericalHarmonicColors);
            ambientColor = ClampPositive(sphericalHarmonicColors[0]);
            ambientBrightness = Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(0.01f, Luminance(ambientColor))),
                minimumBrightness,
                maximumBrightness);
            ambientTint = NormalizeTint(ambientColor);
            ambientSource = "environmental-hdr";
        }
        else if (hasAmbientMode)
        {
            if (latestLightEstimation.averageBrightness.HasValue)
            {
                ambientBrightness = Mathf.Clamp(
                    latestLightEstimation.averageBrightness.Value / ArCoreMiddleGrayGamma,
                    minimumBrightness,
                    maximumBrightness);
            }
            ambientTint = NormalizeTint(latestLightEstimation.colorCorrection ?? Color.white);
            ambientColor = ambientTint * ambientBrightness;
            ambientSource = "ambient-intensity";
        }

        bool localEnvironmentFresh = hasEnvironmentSample &&
                                     environmentSampledAt >= 0f &&
                                     Time.unscaledTime - environmentSampledAt <= 2f;
        if (localEnvironmentFresh)
        {
            // 相机周边颜色包含背景物体本身的反射色，不能代替入射照明。
            // 它只以 15%~25% 修正球谐光/AR 环境色；整体亮度仍主要来自 AR。
            environmentTintWeight = hasArAmbientLighting
                ? Mathf.Lerp(0.15f, 0.25f, environmentSampleConfidence)
                : 1f;
            Color localTint = NormalizeTint(environmentSampleColor);
            float localBrightness = Mathf.Clamp(
                environmentSampleLuminance / 0.50f,
                0.65f,
                1.15f);
            ambientTint = NormalizeTint(Color.Lerp(ambientTint, localTint, environmentTintWeight));
            environmentBrightnessWeight = hasArAmbientLighting
                ? Mathf.Lerp(0.04f, 0.08f, environmentSampleConfidence)
                : 1f;
            ambientBrightness = Mathf.Lerp(
                ambientBrightness,
                localBrightness,
                environmentBrightnessWeight);
            ambientColor = ambientTint * ambientBrightness;
            ambientSource = $"{ambientSource}+local-ring";
        }

        ambientTintForShadow = ambientTint;
        if (hasManualLight)
        {
            // 人工标定保存“角色指向远处光源”的方向；现有渲染和影子统一使用
            // 光线传播方向，因此在此明确取反，避免方向语义混用。
            mainLightDirection = -manualLightToSource.normalized;
            paperDiffuse = ComputeManualPaperDiffuse(frontNormal, manualLightToSource);
            mainLightBrightness = manualKeyStrength;
            float softKeyResponse = Mathf.Sqrt(paperDiffuse);
            float liftedAmbient = Mathf.Lerp(1f, ambientBrightness, manualAmbientInfluence);
            brightnessFactor = Mathf.Clamp(
                liftedAmbient + manualKeyStrength * 0.32f * softKeyResponse,
                manualMinimumBrightness,
                Mathf.Min(maximumBrightness, 1.28f));
            float tintWeight = manualKeyStrength * (0.28f + 0.72f * softKeyResponse);
            tint = NormalizeTint(Color.Lerp(ambientTint, manualLightTint, tintWeight));
            rawLight = tint * brightnessFactor;
            estimationSource = $"manual-main+{ambientSource}";
            directionalLightAvailableForShadow = true;
        }
        else if (hasSphericalHarmonics)
        {
            rawLight = ambientColor;
            estimationSource = ambientSource;
            if (hasMainDirection)
            {
                mainLightDirection = latestLightEstimation.mainLightDirection.Value.normalized;
                paperDiffuse = ComputePaperDiffuse(frontNormal, mainLightDirection);
                mainLightBrightness = latestLightEstimation.averageMainLightBrightness ?? 0.5f;
                Color mainTint = NormalizeTint(latestLightEstimation.mainLightColor ?? Color.white);
                rawLight += mainTint * (paperDiffuse * Mathf.Clamp01(mainLightBrightness) * 0.55f);
                directionalLightAvailableForShadow = true;
            }

            brightnessFactor = Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(0.01f, Luminance(rawLight))),
                minimumBrightness,
                maximumBrightness);
            tint = NormalizeTint(rawLight);
        }
        else if (hasAmbientMode)
        {
            estimationSource = ambientSource;
            brightnessFactor = ambientBrightness;
            tint = ambientTint;
            rawLight = tint * brightnessFactor;
        }
        else if (hasMainDirection || latestLightEstimation.mainLightColor.HasValue)
        {
            estimationSource = "main-light-only";
            if (hasMainDirection)
            {
                mainLightDirection = latestLightEstimation.mainLightDirection.Value.normalized;
                paperDiffuse = ComputePaperDiffuse(frontNormal, mainLightDirection);
                directionalLightAvailableForShadow = true;
            }
            mainLightBrightness = latestLightEstimation.averageMainLightBrightness ?? 0.5f;
            tint = NormalizeTint(latestLightEstimation.mainLightColor ?? Color.white);
            brightnessFactor = Mathf.Clamp(
                0.68f + paperDiffuse * Mathf.Clamp01(mainLightBrightness) * 0.55f,
                minimumBrightness,
                maximumBrightness);
            rawLight = tint * brightnessFactor;
        }

        evaluatedLightColor = rawLight;
        float effectiveMinimumBrightness = hasManualLight ? manualMinimumBrightness : minimumBrightness;
        Color target = new(
            Mathf.Clamp(brightnessFactor * multiplyBrightnessAdjustment * tint.r, effectiveMinimumBrightness * 0.75f, maximumBrightness * 1.12f),
            Mathf.Clamp(brightnessFactor * multiplyBrightnessAdjustment * tint.g, effectiveMinimumBrightness * 0.75f, maximumBrightness * 1.12f),
            Mathf.Clamp(brightnessFactor * multiplyBrightnessAdjustment * tint.b, effectiveMinimumBrightness * 0.75f, maximumBrightness * 1.12f),
            1f);
        target = Color.Lerp(Color.white, target, multiplyBlendStrength);

        multiply = new Color(
            Mathf.Min(1f, target.r),
            Mathf.Min(1f, target.g),
            Mathf.Min(1f, target.b),
            1f);
        screen = new Color(
            Mathf.Clamp01((target.r - 1f) * 0.72f),
            Mathf.Clamp01((target.g - 1f) * 0.72f),
            Mathf.Clamp01((target.b - 1f) * 0.72f),
            1f);
    }

    private void ApplyCubismColors(Color effectMultiply, Color effectScreen)
    {
        foreach (var state in rendererStates)
        {
            if (state.Renderer == null)
                continue;

            state.Renderer.DrawObjectMultiplyColorEnabled = true;
            state.Renderer.DrawObjectScreenColorEnabled = true;
            state.Renderer.MultiplyColor = MultiplyRgb(state.OriginalMultiply, effectMultiply);
            state.Renderer.ScreenColor = CombineScreen(state.OriginalScreen, effectScreen);
            state.Renderer.ApplyMultiplyColor();
            state.Renderer.ApplyScreenColor();
        }
        colorsAreOverridden = true;
    }

    private void RestoreCubismColors()
    {
        if (!colorsAreOverridden)
            return;

        foreach (var state in rendererStates)
        {
            if (state.Renderer == null)
                continue;

            state.Renderer.MultiplyColor = state.OriginalMultiply;
            state.Renderer.ScreenColor = state.OriginalScreen;
            state.Renderer.DrawObjectMultiplyColorEnabled = state.OriginalMultiplyOverride;
            state.Renderer.DrawObjectScreenColorEnabled = state.OriginalScreenOverride;
            state.Renderer.ApplyMultiplyColor();
            state.Renderer.ApplyScreenColor();
        }
        colorsAreOverridden = false;
    }

    private void UpdateShadow(
        Transform poseRoot,
        Vector3 footPosition,
        float heightMeters,
        Vector3 placementPlaneNormal)
    {
        EnsureShadowRenderer();
        if (shadowRenderer == null)
            return;

        Vector3 planeNormal = placementPlaneNormal.sqrMagnitude > 0.0001f
            ? placementPlaneNormal.normalized
            : Vector3.up;
        shadowPlaneNormal = planeNormal;
        Vector3 projectedDirection = Vector3.ProjectOnPlane(mainLightDirection, planeNormal);
        bool hasDirectionalEstimate = directionalLightAvailableForShadow;
        if (!hasDirectionalEstimate || projectedDirection.sqrMagnitude < 0.0001f)
        {
            projectedDirection = Vector3.ProjectOnPlane(poseRoot.forward, planeNormal);
            shadowLightElevationDegrees = 90f;
            float ambientLength = Mathf.Max(0.04f, heightMeters * 0.16f);
            shadowLengthMeters = Mathf.Max(0.025f, ambientLength * shadowLengthScale);
            shadowOpacity = ambientShadowOpacity * ShadowHardness;
        }
        else
        {
            projectedDirection.Normalize();
            shadowLightElevationDegrees = ComputeShadowLightElevationDegrees(
                mainLightDirection,
                planeNormal);
            shadowLengthMeters = ComputeShadowLengthMeters(
                heightMeters,
                mainLightDirection,
                planeNormal,
                shadowLengthScale);
            float physicalOpacity = Mathf.Lerp(
                ambientShadowOpacity,
                directionalShadowOpacity,
                Mathf.Sqrt(Mathf.Clamp01(mainLightBrightness)));
            shadowOpacity = physicalOpacity * ShadowHardness;
        }

        if (projectedDirection.sqrMagnitude < 0.0001f)
            projectedDirection = Vector3.forward;
        projectedDirection.Normalize();

        Texture2D activeMask = shadowMaskVariant == 1 && alternateShadowMask != null
            ? alternateShadowMask
            : primaryShadowMask;
        float maskAspect = activeMask != null && activeMask.height > 0
            ? activeMask.width / (float)activeMask.height
            : 0.75f;
        float widthMeters = Mathf.Max(0.04f, heightMeters * maskAspect);
        shadowObject.transform.SetPositionAndRotation(
            footPosition + planeNormal * shadowPlaneOffsetMeters,
            Quaternion.LookRotation(projectedDirection, planeNormal));
        Vector3 parentScale = transform.lossyScale;
        shadowObject.transform.localScale = new Vector3(
            widthMeters / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            shadowLengthMeters / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));

        shadowPropertyBlock ??= new MaterialPropertyBlock();
        shadowRenderer.GetPropertyBlock(shadowPropertyBlock);
        shadowTint = hasManualLight
            ? ComputeRemainingAmbientTint(ambientTintForShadow, manualLightTint, manualKeyStrength)
            : ambientTintForShadow;
        shadowPropertyBlock.SetColor(
            "_BaseColor",
            new Color(
                shadowTint.r * 0.026f,
                shadowTint.g * 0.026f,
                shadowTint.b * 0.026f,
                shadowOpacity));
        shadowPropertyBlock.SetFloat("_Softness", shadowSoftness);
        if (activeMask != null)
            shadowPropertyBlock.SetTexture("_ShadowMask", activeMask);
        shadowRenderer.SetPropertyBlock(shadowPropertyBlock);
        SetShadowVisible(true);
    }

    private void EnsureShadowRenderer()
    {
        if (shadowRenderer != null)
            return;

        Material material = shadowMaterial;
        if (material == null)
        {
            Shader shader = Shader.Find("LuoTianyiAR/SoftShadow");
            if (shader == null)
            {
                Debug.LogError("[Harmonization] 找不到 LuoTianyiAR/SoftShadow Shader，影子已禁用");
                return;
            }

            runtimeShadowMaterial = new Material(shader)
            {
                name = "Auto Harmonization Shadow (Runtime)"
            };
            material = runtimeShadowMaterial;
        }

        shadowObject = new GameObject(ShadowObjectName);
        shadowObject.transform.SetParent(transform, false);
        var meshFilter = shadowObject.AddComponent<MeshFilter>();
        shadowRenderer = shadowObject.AddComponent<MeshRenderer>();
        shadowMesh = CreateShadowMesh();
        meshFilter.sharedMesh = shadowMesh;
        shadowRenderer.sharedMaterial = material;
        shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        shadowRenderer.receiveShadows = false;
        shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
        shadowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        shadowRenderer.enabled = false;
    }

    private static Mesh CreateShadowMesh()
    {
        var mesh = new Mesh
        {
            name = "Auto Harmonization Shadow Mesh",
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 0f, 1f),
                new Vector3(0.5f, 0f, 1f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            },
            triangles = new[] { 0, 2, 1, 1, 2, 3 },
            normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up }
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void SetShadowVisible(bool visible)
    {
        if (shadowRenderer != null)
            shadowRenderer.enabled = visible;
    }

    private void LogSourceTransitionIfNeeded()
    {
        if (lastLoggedSource == estimationSource)
            return;
        lastLoggedSource = estimationSource;
        Debug.Log(
            $"[Harmonization] 光照来源切换: source={estimationSource}, " +
            $"requested={(cameraManager != null ? cameraManager.requestedLightEstimation.ToString() : "missing")}, " +
            $"current={(cameraManager != null ? cameraManager.currentLightEstimation.ToString() : "missing")}");
    }

    private void UpdateManualArCoreComparison()
    {
        if (!hasManualLight || arCoreMainDirectionRaw.sqrMagnitude < 0.0001f)
        {
            manualToArCoreRawAngle = -1f;
            manualToArCoreNegatedAngle = -1f;
            return;
        }

        manualToArCoreRawAngle = Vector3.Angle(manualLightToSource, arCoreMainDirectionRaw);
        manualToArCoreNegatedAngle = Vector3.Angle(manualLightToSource, -arCoreMainDirectionRaw);
    }

    private bool TryGetModelScreenRect(out Rect screenRect)
    {
        screenRect = default;
        Camera camera = cameraManager != null ? cameraManager.GetComponent<Camera>() : Camera.main;
        if (camera == null)
            return false;

        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        bool found = false;

        void IncludeWorldPoint(Vector3 world)
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            if (viewport.z <= 0f)
                return;
            minimum = Vector2.Min(minimum, viewport);
            maximum = Vector2.Max(maximum, viewport);
            found = true;
        }

        // Cubism 由自定义 URP Pass 提交，MeshRenderer 的 enabled/bounds 不保证能代表
        // 最终屏幕轮廓。纸片近似直接使用放置姿态、脚底和目标身高构造稳定包围框。
        if (TryGetTarget(
                out Transform poseRoot,
                out _,
                out Vector3 footPosition,
                out float heightMeters,
                out _))
        {
            Vector3 up = poseRoot.up.sqrMagnitude > 0.0001f ? poseRoot.up.normalized : Vector3.up;
            Vector3 right = poseRoot.right.sqrMagnitude > 0.0001f ? poseRoot.right.normalized : Vector3.right;
            float halfWidth = Mathf.Max(0.03f, heightMeters * 0.46f);
            Vector3 top = footPosition + up * heightMeters;
            IncludeWorldPoint(footPosition - right * halfWidth);
            IncludeWorldPoint(footPosition + right * halfWidth);
            IncludeWorldPoint(top - right * halfWidth);
            IncludeWorldPoint(top + right * halfWidth);
        }

        // 极端情况下退回运行时网格 bounds，但不依赖 MeshRenderer.enabled。
        if (!found)
        {
            foreach (CubismColorState state in rendererStates)
            {
                MeshRenderer meshRenderer = state.Renderer != null ? state.Renderer.MeshRenderer : null;
                if (meshRenderer == null || !meshRenderer.gameObject.activeInHierarchy)
                    continue;
                Bounds bounds = meshRenderer.bounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    IncludeWorldPoint(center + Vector3.Scale(
                        extents,
                        new Vector3(
                            (corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f)));
                }
            }
        }

        if (!found)
            return false;

        minimum.x = Mathf.Clamp01(minimum.x);
        minimum.y = Mathf.Clamp01(minimum.y);
        maximum.x = Mathf.Clamp01(maximum.x);
        maximum.y = Mathf.Clamp01(maximum.y);
        if (maximum.x - minimum.x < 0.01f || maximum.y - minimum.y < 0.01f)
            return false;
        screenRect = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        return true;
    }

    private static bool TrySampleEnvironmentColor(
        NativeArray<byte> rgbData,
        Vector2Int dimensions,
        Rect modelScreenRect,
        out Color color,
        out float luminance,
        out float confidence,
        out string status)
    {
        color = Color.white;
        luminance = 0.5f;
        confidence = 0f;
        status = "invalid-buffer";
        if (!rgbData.IsCreated || dimensions.x <= 0 || dimensions.y <= 0 ||
            rgbData.Length < dimensions.x * dimensions.y * 3)
            return false;

        float expandX = Mathf.Clamp(modelScreenRect.width * 0.22f, 0.055f, 0.14f);
        float expandY = Mathf.Clamp(modelScreenRect.height * 0.14f, 0.045f, 0.12f);
        Rect outer = ExpandNormalized(modelScreenRect, expandX, expandY);
        Rect inner = ExpandNormalized(modelScreenRect, 0.012f, 0.012f);

        const int binCount = 32;
        var counts = new int[binCount];
        var redSums = new float[binCount];
        var greenSums = new float[binCount];
        var blueSums = new float[binCount];
        var luminanceSums = new float[binCount];
        int candidateCount = 0;
        int validCount = 0;

        for (int y = 0; y < dimensions.y; y++)
        {
            float imageV = (y + 0.5f) / dimensions.y;
            for (int x = 0; x < dimensions.x; x++)
            {
                float imageU = (x + 0.5f) / dimensions.x;
                Vector2 screenUv = MapCpuImageUvToScreenUv(new Vector2(imageU, imageV));
                if (!outer.Contains(screenUv) || inner.Contains(screenUv))
                    continue;
                candidateCount++;

                int index = (y * dimensions.x + x) * 3;
                float r = rgbData[index] / 255f;
                float g = rgbData[index + 1] / 255f;
                float b = rgbData[index + 2] / 255f;
                float maximum = Mathf.Max(r, Mathf.Max(g, b));
                float minimum = Mathf.Min(r, Mathf.Min(g, b));
                float pixelLuminance = r * 0.2126f + g * 0.7152f + b * 0.0722f;
                float saturation = maximum > 0.001f ? (maximum - minimum) / maximum : 0f;
                // 白桌面、白墙是有效的环境综合色来源，不能像灯芯标定那样排除高亮。
                // 上下各 10% 的截尾统计会抑制孤立反光点。
                if (pixelLuminance < 0.015f || saturation > 0.90f)
                    continue;

                int bin = Mathf.Clamp(Mathf.FloorToInt(pixelLuminance * binCount), 0, binCount - 1);
                counts[bin]++;
                redSums[bin] += r;
                greenSums[bin] += g;
                blueSums[bin] += b;
                luminanceSums[bin] += pixelLuminance;
                validCount++;
            }
        }

        if (candidateCount < 24 || validCount < 16)
        {
            status = $"insufficient-pixels candidates={candidateCount}, valid={validCount}";
            return false;
        }

        float trimPerSide = validCount * 0.10f;
        float lowerRemaining = trimPerSide;
        float upperRemaining = trimPerSide;
        var acceptedFractions = new float[binCount];
        for (int bin = 0; bin < binCount; bin++)
        {
            float accepted = counts[bin];
            float removed = Mathf.Min(accepted, lowerRemaining);
            accepted -= removed;
            lowerRemaining -= removed;
            acceptedFractions[bin] = counts[bin] > 0 ? accepted / counts[bin] : 0f;
        }
        for (int bin = binCount - 1; bin >= 0; bin--)
        {
            float currentlyAccepted = counts[bin] * acceptedFractions[bin];
            float removed = Mathf.Min(currentlyAccepted, upperRemaining);
            currentlyAccepted -= removed;
            upperRemaining -= removed;
            acceptedFractions[bin] = counts[bin] > 0 ? currentlyAccepted / counts[bin] : 0f;
        }

        float acceptedCount = 0f;
        float red = 0f;
        float green = 0f;
        float blue = 0f;
        float light = 0f;
        for (int bin = 0; bin < binCount; bin++)
        {
            float fraction = acceptedFractions[bin];
            acceptedCount += counts[bin] * fraction;
            red += redSums[bin] * fraction;
            green += greenSums[bin] * fraction;
            blue += blueSums[bin] * fraction;
            light += luminanceSums[bin] * fraction;
        }

        if (acceptedCount < 12f)
        {
            status = $"trimmed-empty accepted={acceptedCount:F1}, valid={validCount}";
            return false;
        }
        color = new Color(red / acceptedCount, green / acceptedCount, blue / acceptedCount, 1f);
        luminance = light / acceptedCount;
        confidence = Mathf.Clamp01(validCount / (float)candidateCount) *
                     Mathf.Clamp01(candidateCount / 240f);
        status = $"ready candidates={candidateCount}, valid={validCount}, accepted={acceptedCount:F1}";
        return true;
    }

    private static Rect ExpandNormalized(Rect rect, float horizontal, float vertical)
    {
        return Rect.MinMaxRect(
            Mathf.Clamp01(rect.xMin - horizontal),
            Mathf.Clamp01(rect.yMin - vertical),
            Mathf.Clamp01(rect.xMax + horizontal),
            Mathf.Clamp01(rect.yMax + vertical));
    }

    private static Vector2 MapCpuImageUvToScreenUv(Vector2 imageUv)
    {
        return Screen.orientation switch
        {
            ScreenOrientation.Portrait => new Vector2(1f - imageUv.y, imageUv.x),
            ScreenOrientation.PortraitUpsideDown => new Vector2(imageUv.y, 1f - imageUv.x),
            ScreenOrientation.LandscapeRight => new Vector2(1f - imageUv.x, 1f - imageUv.y),
            _ => imageUv
        };
    }

    private void ApplyEdgeBlurGlobals(bool active)
    {
        Shader.SetGlobalFloat(EdgeBlurEnabledId, active ? 1f : 0f);
        Shader.SetGlobalFloat(EdgeBlurStrengthId, active ? edgeBlurStrength : 0f);

        bool freshEnvironment = active && hasEnvironmentSample &&
                                environmentSampledAt >= 0f &&
                                Time.unscaledTime - environmentSampledAt <= 2f;
        if (freshEnvironment)
        {
            // 轮廓包色直接采用角色周围的画面颜色，但限制到较低强度，避免形成色边。
            edgeWrapColor = Color.Lerp(Color.white, environmentSampleColor, 0.75f);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                edgeWrapColor = edgeWrapColor.linear;
            edgeWrapStrength = edgeBlurStrength *
                               Mathf.Lerp(0.12f, 0.24f, environmentSampleConfidence);
        }
        else
        {
            edgeWrapColor = Color.white;
            edgeWrapStrength = 0f;
        }

        Shader.SetGlobalColor(EdgeWrapColorId, edgeWrapColor);
        Shader.SetGlobalFloat(EdgeWrapStrengthId, edgeWrapStrength);
    }

    private static bool TrySampleLightColor(
        NativeArray<byte> rgbData,
        Vector2Int dimensions,
        out Color color,
        out float luminance,
        out float overexposureRatio)
    {
        color = Color.white;
        luminance = 0f;
        overexposureRatio = 0f;
        if (!rgbData.IsCreated || dimensions.x <= 0 || dimensions.y <= 0 ||
            rgbData.Length < dimensions.x * dimensions.y * 3)
            return false;

        float centerX = (dimensions.x - 1) * 0.5f;
        float centerY = (dimensions.y - 1) * 0.5f;
        float radius = Mathf.Min(dimensions.x, dimensions.y) * 0.24f;
        float radiusSquared = radius * radius;
        float weightSum = 0f;
        float weightedR = 0f;
        float weightedG = 0f;
        float weightedB = 0f;
        float weightedLuminance = 0f;
        int roiPixelCount = 0;
        int overexposedPixelCount = 0;
        int validPixelCount = 0;

        for (int y = 0; y < dimensions.y; y++)
        {
            float dy = y - centerY;
            for (int x = 0; x < dimensions.x; x++)
            {
                float dx = x - centerX;
                if (dx * dx + dy * dy > radiusSquared)
                    continue;

                int index = (y * dimensions.x + x) * 3;
                float r = rgbData[index] / 255f;
                float g = rgbData[index + 1] / 255f;
                float b = rgbData[index + 2] / 255f;
                float maximum = Mathf.Max(r, Mathf.Max(g, b));
                float pixelLuminance = r * 0.2126f + g * 0.7152f + b * 0.0722f;
                roiPixelCount++;
                if (maximum >= 0.985f)
                {
                    overexposedPixelCount++;
                    continue;
                }
                if (pixelLuminance < 0.10f)
                    continue;

                // 对高亮光晕加权，同时排除已经剪裁成纯白的灯芯。
                float weight = Mathf.Pow(pixelLuminance, 3f);
                weightedR += r * weight;
                weightedG += g * weight;
                weightedB += b * weight;
                weightedLuminance += pixelLuminance * weight;
                weightSum += weight;
                validPixelCount++;
            }
        }

        overexposureRatio = roiPixelCount > 0
            ? overexposedPixelCount / (float)roiPixelCount
            : 0f;
        if (validPixelCount < 24 || weightSum < 0.001f)
            return false;

        color = new Color(
            weightedR / weightSum,
            weightedG / weightSum,
            weightedB / weightSum,
            1f);
        luminance = weightedLuminance / weightSum;
        return true;
    }

    private static Vector3 AverageDirection(List<Vector3> directions)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 direction in directions)
        {
            if (direction.sqrMagnitude > 0.0001f)
                sum += direction.normalized;
        }
        return sum.sqrMagnitude > 0.0001f ? sum.normalized : Vector3.forward;
    }

    private static float MaximumAngularDeviation(List<Vector3> directions, Vector3 averageDirection)
    {
        float maximum = 0f;
        foreach (Vector3 direction in directions)
        {
            if (direction.sqrMagnitude > 0.0001f)
                maximum = Mathf.Max(maximum, Vector3.Angle(direction, averageDirection));
        }
        return maximum;
    }

    private static Color MedianColor(List<Color> colors)
    {
        if (colors.Count == 0)
            return Color.white;
        var red = new List<float>(colors.Count);
        var green = new List<float>(colors.Count);
        var blue = new List<float>(colors.Count);
        foreach (Color color in colors)
        {
            red.Add(color.r);
            green.Add(color.g);
            blue.Add(color.b);
        }
        return new Color(Median(red), Median(green), Median(blue), 1f);
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0f;
        var sorted = new List<float>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) * 0.5f
            : sorted[middle];
    }

    private static float EstimateCorrelatedColorTemperature(Color gammaColor)
    {
        float r = GammaToLinear(gammaColor.r);
        float g = GammaToLinear(gammaColor.g);
        float b = GammaToLinear(gammaColor.b);
        float xValue = r * 0.4124f + g * 0.3576f + b * 0.1805f;
        float yValue = r * 0.2126f + g * 0.7152f + b * 0.0722f;
        float zValue = r * 0.0193f + g * 0.1192f + b * 0.9505f;
        float sum = xValue + yValue + zValue;
        if (sum < 0.0001f)
            return 6500f;
        float x = xValue / sum;
        float y = yValue / sum;
        float denominator = 0.1858f - y;
        if (Mathf.Abs(denominator) < 0.0001f)
            return 6500f;
        float n = (x - 0.3320f) / denominator;
        float kelvin = -449f * n * n * n + 3525f * n * n - 6823.3f * n + 5520.33f;
        return Mathf.Clamp(kelvin, 2000f, 10000f);
    }

    private static float GammaToLinear(float value)
    {
        return value <= 0.04045f
            ? value / 12.92f
            : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    private static Color ComputeRemainingAmbientTint(Color ambientTint, Color keyTint, float keyStrength)
    {
        float influence = Mathf.Clamp01(keyStrength) * 0.55f;
        Color remaining = new(
            ambientTint.r / Mathf.Max(0.45f, 1f + (keyTint.r - 1f) * influence),
            ambientTint.g / Mathf.Max(0.45f, 1f + (keyTint.g - 1f) * influence),
            ambientTint.b / Mathf.Max(0.45f, 1f + (keyTint.b - 1f) * influence),
            1f);
        return NormalizeTint(remaining);
    }

    private static Color NormalizeTint(Color color)
    {
        color = ClampPositive(color);
        float luminance = Mathf.Max(0.05f, Luminance(color));
        return new Color(
            Mathf.Clamp(color.r / luminance, 0.68f, 1.32f),
            Mathf.Clamp(color.g / luminance, 0.68f, 1.32f),
            Mathf.Clamp(color.b / luminance, 0.68f, 1.32f),
            1f);
    }

    private static Color ClampPositive(Color color)
    {
        return new Color(
            Mathf.Max(0f, color.r),
            Mathf.Max(0f, color.g),
            Mathf.Max(0f, color.b),
            1f);
    }

    private static float Luminance(Color color)
    {
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }

    private static Color MultiplyRgb(Color a, Color b)
    {
        return new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);
    }

    private static Color CombineScreen(Color a, Color b)
    {
        return new Color(
            1f - (1f - a.r) * (1f - b.r),
            1f - (1f - a.g) * (1f - b.g),
            1f - (1f - a.b) * (1f - b.b),
            1f);
    }

    private static string FormatColor(Color color)
    {
        return $"({color.r:F3},{color.g:F3},{color.b:F3})";
    }

    private static string FormatAngle(float angle)
    {
        return angle >= 0f ? $"{angle:F2}deg" : "unavailable";
    }

    private void OnDestroy()
    {
        manualCalibrationActive = false;
        calibrationGeneration++;
        environmentSamplingGeneration++;
        UnsubscribeCameraManager();
        RestoreCubismColors();
        ApplyEdgeBlurGlobals(false);
        if (shadowObject != null)
            Destroy(shadowObject);
        if (shadowMesh != null)
            Destroy(shadowMesh);
        if (runtimeShadowMaterial != null)
            Destroy(runtimeShadowMaterial);
    }

    private sealed class CubismColorState
    {
        public readonly CubismRenderer Renderer;
        public readonly bool OriginalMultiplyOverride;
        public readonly bool OriginalScreenOverride;
        public readonly Color OriginalMultiply;
        public readonly Color OriginalScreen;

        public CubismColorState(CubismRenderer renderer)
        {
            Renderer = renderer;
            OriginalMultiplyOverride = renderer.DrawObjectMultiplyColorEnabled;
            OriginalScreenOverride = renderer.DrawObjectScreenColorEnabled;
            OriginalMultiply = renderer.MultiplyColor;
            OriginalScreen = renderer.ScreenColor;
        }
    }
}

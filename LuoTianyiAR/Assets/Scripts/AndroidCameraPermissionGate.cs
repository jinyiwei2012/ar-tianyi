// AndroidCameraPermissionGate.cs — 在启动 ARCore 前显式申请 Android 相机运行时权限。
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

[DefaultExecutionOrder(-10000)]
public sealed class AndroidCameraPermissionGate : MonoBehaviour
{
    public enum PermissionState
    {
        NotRequired,
        AlreadyGranted,
        Requesting,
        Granted,
        Denied,
        DeniedDontAskAgain
    }

    private static AndroidCameraPermissionGate instance;

    private ARSession arSession;
    private ARCameraManager cameraManager;
    private ARCameraBackground cameraBackground;
    private PermissionState state = PermissionState.NotRequired;
    private bool pipelineStateCaptured;
    private bool sessionWasEnabled;
    private bool cameraManagerWasEnabled;
    private bool cameraBackgroundWasEnabled;
    private float permissionGrantedAt = -1f;

#if UNITY_ANDROID
    private PermissionCallbacks permissionCallbacks;
#endif

    public static PermissionState State => instance != null
        ? instance.state
        : PermissionState.NotRequired;

    public static bool IsWaitingForDecision =>
        State == PermissionState.Requesting;

    public static bool IsPermissionDenied =>
        State == PermissionState.Denied || State == PermissionState.DeniedDontAskAgain;

    public static bool IsPermissionBlocking => IsWaitingForDecision || IsPermissionDenied;

    public static float PermissionGrantedAtRealtime => instance != null
        ? instance.permissionGrantedAt
        : -1f;

    public static string GetDebugSummary()
    {
        if (instance == null)
            return "missing";

#if UNITY_ANDROID && !UNITY_EDITOR
        bool authorized = Permission.HasUserAuthorizedPermission(Permission.Camera);
        return $"state={instance.state}, requestedExplicitly=True, authorized={authorized}, " +
               $"sessionEnabled={instance.arSession != null && instance.arSession.enabled}, " +
               $"cameraManagerEnabled={instance.cameraManager != null && instance.cameraManager.enabled}, " +
               $"backgroundEnabled={instance.cameraBackground != null && instance.cameraBackground.enabled}";
#else
        return $"state={instance.state}, requestedExplicitly=False, platform={Application.platform}";
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            state = PermissionState.AlreadyGranted;
            permissionGrantedAt = Time.unscaledTime;
            Debug.Log("[CameraPermission] CAMERA 已授权，无需再次申请");
            return;
        }

        CapturePipelineState();
        SetARPipelineEnabled(false);
        state = PermissionState.Requesting;
        Debug.Log("[CameraPermission] CAMERA 尚未授权，已暂停 AR 链路，准备主动申请运行时权限");
#else
        state = PermissionState.NotRequired;
#endif
    }

    private IEnumerator Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (state != PermissionState.Requesting)
            yield break;

        // 等待首帧，确保 Android Activity 已可显示系统权限窗口。
        yield return null;
        RequestCameraPermission();
#else
        yield break;
#endif
    }

    private void OnApplicationFocus(bool hasFocus)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!hasFocus || !IsPermissionBlocking ||
            !Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            return;
        }

        HandlePermissionGranted(Permission.Camera);
#endif
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

#if UNITY_ANDROID
        if (permissionCallbacks != null)
        {
            permissionCallbacks.PermissionGranted -= HandlePermissionGranted;
            permissionCallbacks.PermissionDenied -= HandlePermissionDenied;
        }
#endif
    }

    private void ResolveReferences()
    {
        arSession = GetComponent<ARSession>() ??
                    FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        cameraBackground = cameraManager != null
            ? cameraManager.GetComponent<ARCameraBackground>()
            : FindFirstObjectByType<ARCameraBackground>(FindObjectsInactive.Include);
    }

    private void CapturePipelineState()
    {
        if (pipelineStateCaptured)
            return;

        sessionWasEnabled = arSession != null && arSession.enabled;
        cameraManagerWasEnabled = cameraManager != null && cameraManager.enabled;
        cameraBackgroundWasEnabled = cameraBackground != null && cameraBackground.enabled;
        pipelineStateCaptured = true;
    }

    private void SetARPipelineEnabled(bool enabled)
    {
        if (arSession != null)
            arSession.enabled = enabled && sessionWasEnabled;
        if (cameraManager != null)
            cameraManager.enabled = enabled && cameraManagerWasEnabled;
        if (cameraBackground != null)
            cameraBackground.enabled = enabled && cameraBackgroundWasEnabled;
    }

#if UNITY_ANDROID
    private void RequestCameraPermission()
    {
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            HandlePermissionGranted(Permission.Camera);
            return;
        }

        permissionCallbacks = new PermissionCallbacks();
        permissionCallbacks.PermissionGranted += HandlePermissionGranted;
        permissionCallbacks.PermissionDenied += HandlePermissionDenied;

        Debug.Log("[CameraPermission] 正在调用 Android CAMERA 运行时权限申请");
        Permission.RequestUserPermission(Permission.Camera, permissionCallbacks);
    }

    private void HandlePermissionGranted(string permissionName)
    {
        if (permissionName != Permission.Camera ||
            !Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            return;
        }

        if (state == PermissionState.Granted || state == PermissionState.AlreadyGranted)
            return;

        state = PermissionState.Granted;
        permissionGrantedAt = Time.unscaledTime;
        Debug.Log("[CameraPermission] CAMERA 授权成功，正在启动 ARSession 与相机链路");
        StartCoroutine(EnableARPipelineAfterPermission());
    }

    private IEnumerator EnableARPipelineAfterPermission()
    {
        ResolveReferences();
        yield return null;

        if (arSession != null)
            arSession.enabled = sessionWasEnabled;
        yield return null;

        if (cameraManager != null)
            cameraManager.enabled = cameraManagerWasEnabled;
        if (cameraBackground != null)
            cameraBackground.enabled = cameraBackgroundWasEnabled;

        Debug.Log(
            $"[CameraPermission] AR 链路已启用；session={arSession != null && arSession.enabled}, " +
            $"cameraManager={cameraManager != null && cameraManager.enabled}, " +
            $"background={cameraBackground != null && cameraBackground.enabled}");
    }

    private void HandlePermissionDenied(string permissionName)
    {
        if (permissionName != Permission.Camera)
            return;

        bool canExplainAndRequestAgain = Permission.ShouldShowRequestPermissionRationale(Permission.Camera);
        state = canExplainAndRequestAgain
            ? PermissionState.Denied
            : PermissionState.DeniedDontAskAgain;
        SetARPipelineEnabled(false);
        if (canExplainAndRequestAgain)
        {
            Debug.LogWarning("[CameraPermission] 用户拒绝了 CAMERA 权限；AR 链路保持暂停");
            RuntimeDebugPanel.Open("相机权限被拒绝，请授权后重新进入应用");
        }
        else
        {
            Debug.LogError("[CameraPermission] CAMERA 权限无法再次弹窗申请；请前往系统设置手动授权");
            RuntimeDebugPanel.Open("相机权限无法再次申请，请在系统设置中允许相机权限");
        }
    }
#endif
}

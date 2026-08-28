// OcclusionController.cs — PRD Phase 3: 真实物体遮挡洛天依（需求 c）
// 运行时检测设备 Depth 能力 → 支持则启用遮挡，不支持则优雅降级（c=OFF）。
// 与 AROcclusionManager 一起挂载到 Main Camera。
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class OcclusionController : MonoBehaviour
{
    [Header("遮挡开关")]
    [Tooltip("手动强制禁用遮挡（调试用）")]
    [SerializeField] private bool forceDisable = false;

    private AROcclusionManager occlusionManager;
    private EnvironmentDepthMode lastObservedMode = EnvironmentDepthMode.Disabled;

    private void Update()
    {
        // 持续监控实际 depth 模式：设备中途丢失/恢复能力时输出一次日志，便于真机诊断。
        if (occlusionManager == null || !occlusionManager.enabled)
            return;

        var current = occlusionManager.currentEnvironmentDepthMode;
        if (current != lastObservedMode)
        {
            lastObservedMode = current;
            Debug.Log($"[Occlusion] 深度模式变化: {lastObservedMode} → {current} " +
                      $"(supported={occlusionManager.descriptor?.environmentDepthImageSupported})");
        }
    }

    /// <summary>当前遮挡是否实际启用（组件启用且 depth 模式非 Disabled）。</summary>
    public bool IsOcclusionEnabled
    {
        get
        {
            return !forceDisable && occlusionManager != null && occlusionManager.enabled
                && occlusionManager.currentEnvironmentDepthMode != EnvironmentDepthMode.Disabled;
        }
    }

    /// <summary>诊断行：manager 状态、请求/当前 depth 模式、设备支持情况。</summary>
    public string GetDiagnosticLine()
    {
        if (occlusionManager == null)
            return "Occlusion: manager=missing";

        var descriptor = occlusionManager.descriptor;
        string support = descriptor == null
            ? "unknown"
            : descriptor.environmentDepthImageSupported.ToString();
        return $"Occlusion: enabled={occlusionManager.enabled}, " +
               $"requested={occlusionManager.requestedEnvironmentDepthMode}, " +
               $"current={occlusionManager.currentEnvironmentDepthMode}, " +
               $"support={support}, forceDisable={forceDisable}";
    }

    private void Start()
    {
        occlusionManager = GetComponent<AROcclusionManager>();
        if (occlusionManager == null)
            occlusionManager = FindObjectOfType<AROcclusionManager>();

        if (forceDisable || occlusionManager == null)
        {
            if (occlusionManager != null)
            {
                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
                occlusionManager.enabled = false;
            }
            Debug.Log("[Occlusion] 遮挡已手动禁用或组件缺失 → OFF");
            return;
        }

        // 不在 Start 时把“subsystem 尚未启动”误判为“不支持”。先提出深度请求，
        // 等 AR 子系统给出明确 descriptor 后再决定是否降级。
        occlusionManager.enabled = true;
        occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
        StartCoroutine(ConfirmDepthSupport());
    }

    private IEnumerator ConfirmDepthSupport()
    {
        const int maxFrames = 120;
        for (int i = 0; i < maxFrames && occlusionManager.descriptor == null; i++)
            yield return null;

        var support = occlusionManager.descriptor?.environmentDepthImageSupported ?? Supported.Unknown;
        // 对 P0 放置而言，错误的深度遮挡比没有遮挡更糟。部分 Android/ARCore
        // 设备会长期返回 Unknown，同时不断产生 Invalid depth；若继续保留请求，
        // 无效深度纹理可能把已经正确放置的模型整张遮掉。因此只有明确 Supported
        // 才启用，其余状态都保守降级，后续可由专门的 Depth 验收再恢复。
        if (support != Supported.Supported)
        {
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            occlusionManager.enabled = false;
            Debug.Log($"[Occlusion] 环境深度能力为 {support} → OFF（保守降级，优先保证模型可见）");
            yield break;
        }

        Debug.Log("[Occlusion] 环境深度明确受支持 → ON");
    }
}

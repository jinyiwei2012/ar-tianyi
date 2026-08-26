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
        if (support == Supported.Unsupported)
        {
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            occlusionManager.enabled = false;
            Debug.Log("[Occlusion] 设备明确不支持环境深度 → OFF（优雅降级）");
            yield break;
        }

        Debug.Log(support == Supported.Supported
            ? "[Occlusion] 环境深度受支持 → ON"
            : "[Occlusion] 深度能力仍未知，保留请求并由 AR Foundation 运行时降级");
    }
}

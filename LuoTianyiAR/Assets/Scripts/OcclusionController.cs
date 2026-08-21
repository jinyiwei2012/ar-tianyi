// OcclusionController.cs — PRD Phase 3: 真实物体遮挡洛天依（需求 c）
// 运行时检测设备 Depth 能力 → 支持则启用遮挡，不支持则优雅降级（c=OFF）。
// 挂载到 XR Origin 上，与 AROcclusionManager 配合。
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

        // 运行时能力检测（PRD 12: depth supported ? ON : OFF）
        bool depthSupported = IsEnvironmentDepthSupported();

        if (forceDisable || occlusionManager == null || !depthSupported)
        {
            if (occlusionManager != null)
                occlusionManager.enabled = false;
            Debug.Log("[Occlusion] 设备不支持 Depth 或已禁用 → 遮挡 OFF（优雅降级）");
            return;
        }

        occlusionManager.enabled = true;
        Debug.Log("[Occlusion] Depth 支持 → 遮挡 ON");
    }

    /// 检测环境深度支持（ARCore Depth API 约 66% 设备）
    private bool IsEnvironmentDepthSupported()
    {
        if (occlusionManager == null) return false;

        // 通过子系统 descriptor 查询
        var subsystem = occlusionManager.subsystem;
        if (subsystem != null)
        {
            var descriptor = subsystem.subsystemDescriptor;
            if (descriptor != null)
            {
                return descriptor.environmentDepthImageSupported == Supported.Supported;
            }
        }

        // subsystem 未激活时回退：直接检查支持标志
        var desc = occlusionManager.descriptor;
        if (desc != null)
            return desc.environmentDepthImageSupported == Supported.Supported;

        return false;
    }
}
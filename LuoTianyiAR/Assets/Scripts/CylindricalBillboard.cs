// CylindricalBillboard.cs — PRD 第 2 节: 只绕世界竖直轴转向相机
// 不修改 pitch/roll，保持人物竖直（避免 2D 角色纸片化）。
using UnityEngine;

public class CylindricalBillboard : MonoBehaviour
{
    private Transform cameraTransform;

    private void Start()
    {
        var cam = Camera.main;
        if (cam != null) cameraTransform = cam.transform;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            cameraTransform = cam.transform;
        }

        // 世界竖直方向向量 (0,1,0)
        var toCamera = cameraTransform.position - transform.position;
        toCamera.y = 0f; // 投影到水平面

        if (toCamera.sqrMagnitude < 0.0001f) return; // 相机与角色重合时跳过

        // 只设置 Y 轴旋转: 面向相机水平方向
        transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
    }
}
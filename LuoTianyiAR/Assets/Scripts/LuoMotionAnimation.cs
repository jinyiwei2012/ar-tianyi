// LuoMotionAnimation.cs — PRD Phase 2: 程序化驱动 Cubism 参数动画
// Idle: 呼吸（ParamBodyAngleZ）+ 头部微摆（ParamAngleY）
// Walking: 走路律动（身体前后/左右摆动，不依赖 motion3.json 资产）
// 参数按 Id 缓存，缺失时跳过；挂在模型根节点上，由 LuoMovement 切换模式。
// 与 Live2DModelFeatures 的 SDK Framework 组件分工：
// - 眨眼（ParamEyeLOpen/ROpen）、表情、物理、ParamBreath 呼吸由 SDK 组件负责；
//   本组件不得再写眼睛参数，否则两套系统每帧互相覆盖。
// - 视线追踪（CubismLookController）为 Additive 混合，叠加在本组件写入的
//   身体/头部基础值之上，互不冲突。
using UnityEngine;
using Live2D.Cubism.Core;

public class LuoMotionAnimation : MonoBehaviour
{
    [Header("Idle 呼吸")]
    [SerializeField] private float breatheAmplitude = 2f;       // 度
    [SerializeField] private float breatheSpeed = 1.2f;         // Hz
    [SerializeField] private float headSwayAmplitude = 3f;      // 度
    [SerializeField] private float headSwaySpeed = 0.25f;       // Hz

    [Header("Walking 律动")]
    [SerializeField] private float walkBounceAmplitude = 2f;    // 度（前后倾）
    [SerializeField] private float walkSwayAmplitude = 3f;      // 度（左右摆）
    [SerializeField] private float walkFrequency = 3f;          // Hz

    private CubismParameter bodyAngleZ;
    private CubismParameter bodyAngleY;
    private CubismParameter angleY;

    private bool isWalking;
    private float walkingIntensity = 1f;

    private void Start()
    {
        var model = GetComponentInChildren<CubismModel>();
        if (model == null || model.Parameters == null)
        {
            Debug.LogWarning("[LuoMotionAnimation] 未找到 CubismModel，已禁用。");
            enabled = false;
            return;
        }

        foreach (var p in model.Parameters)
        {
            switch (p.Id)
            {
                case "ParamBodyAngleZ": bodyAngleZ = p; break;
                case "ParamBodyAngleY": bodyAngleY = p; break;
                case "ParamAngleY":    angleY = p;    break;
            }
        }
    }

    /// <summary>由 LuoMovement 在行走/停止时切换。</summary>
    public void SetWalking(bool walking)
    {
        if (isWalking == walking)
            return;

        isWalking = walking;

        if (walking)
        {
            // 进入走路：复位头部，让位给走路律动。
            if (angleY != null) angleY.Value = 0f;
        }
        // 回到 idle：呼吸从头开始由 Update 自然接管，眨眼交还 SDK 眨眼组件。
    }

    /// <summary>由 LuoMovement 在行走时传入当前强度（0=减速停止，1=全速），联动律动频率与幅度。</summary>
    public void SetWalkingIntensity(float intensity)
    {
        walkingIntensity = Mathf.Clamp01(intensity);
    }

    private void Update()
    {
        if (isWalking)
            UpdateWalking();
        else
            UpdateIdle();
    }

    private void UpdateIdle()
    {
        float t = Time.time;

        // 呼吸：身体前后轻微起伏（SDK 的 ParamBreath 谐振是独立参数，可叠加）。
        if (bodyAngleZ != null)
            bodyAngleZ.Value = breatheAmplitude * Mathf.Sin(t * breatheSpeed * Mathf.PI * 2f);

        // 头部缓慢左右摆动；视线追踪在此基础上 Additive 叠加。
        if (angleY != null)
            angleY.Value = headSwayAmplitude * Mathf.Sin(t * headSwaySpeed * Mathf.PI * 2f);
    }

    private void UpdateWalking()
    {
        float t = Time.time;
        // 频率与幅度随行走强度联动：减速时身体律动变慢变弱。
        float phase = t * walkFrequency * walkingIntensity * Mathf.PI * 2f;

        // 走路律动：身体前后倾 + 左右摆动
        // （腿部 Param9x 参数未知，待真机调参后补充）
        if (bodyAngleZ != null)
            bodyAngleZ.Value = walkBounceAmplitude * walkingIntensity * Mathf.Sin(phase);
        if (bodyAngleY != null)
            bodyAngleY.Value = walkSwayAmplitude * walkingIntensity * Mathf.Sin(phase * 0.5f);
    }
}

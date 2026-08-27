// LuoMotionAnimation.cs — PRD Phase 2: 程序化驱动 Cubism 参数动画
// Idle: 呼吸（ParamBodyAngleZ）+ 随机眨眼（ParamEyeLOpen/ROpen）+ 头部微摆
// Walking: 走路律动（身体前后/左右摆动，不依赖 motion3.json 资产）
// 参数按 Id 缓存，缺失时跳过；挂在模型根节点上，由 LuoMovement 切换模式。
using UnityEngine;
using Live2D.Cubism.Core;

public class LuoMotionAnimation : MonoBehaviour
{
    [Header("Idle 呼吸")]
    [SerializeField] private float breatheAmplitude = 2f;       // 度
    [SerializeField] private float breatheSpeed = 1.2f;         // Hz
    [SerializeField] private float headSwayAmplitude = 3f;      // 度
    [SerializeField] private float headSwaySpeed = 0.25f;       // Hz

    [Header("Idle 眨眼")]
    [SerializeField] private float blinkMinInterval = 2f;       // 秒
    [SerializeField] private float blinkMaxInterval = 6f;
    [SerializeField] private float blinkDuration = 0.18f;       // 闭眼→睁眼总时长

    [Header("Walking 律动")]
    [SerializeField] private float walkBounceAmplitude = 2f;    // 度（前后倾）
    [SerializeField] private float walkSwayAmplitude = 3f;      // 度（左右摆）
    [SerializeField] private float walkFrequency = 3f;          // Hz

    private CubismParameter bodyAngleZ;
    private CubismParameter bodyAngleY;
    private CubismParameter angleY;
    private CubismParameter eyeLOpen;
    private CubismParameter eyeROpen;

    private bool isWalking;
    private float nextBlinkAt;

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
                case "ParamEyeLOpen":  eyeLOpen = p;  break;
                case "ParamEyeROpen":  eyeROpen = p;  break;
            }
        }

        // 初始睁眼
        if (eyeLOpen != null) eyeLOpen.Value = 1f;
        if (eyeROpen != null) eyeROpen.Value = 1f;

        ScheduleNextBlink();
    }

    /// <summary>由 LuoMovement 在行走/停止时切换。</summary>
    public void SetWalking(bool walking)
    {
        if (isWalking == walking)
            return;

        isWalking = walking;

        if (walking)
        {
            // 进入走路：睁眼，复位头部
            if (eyeLOpen != null) eyeLOpen.Value = 1f;
            if (eyeROpen != null) eyeROpen.Value = 1f;
            if (angleY != null) angleY.Value = 0f;
        }
        else
        {
            // 回到 idle：从零相位开始呼吸
            ScheduleNextBlink();
        }
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

        // 呼吸：身体前后轻微起伏
        if (bodyAngleZ != null)
            bodyAngleZ.Value = breatheAmplitude * Mathf.Sin(t * breatheSpeed * Mathf.PI * 2f);

        // 头部缓慢左右摆动
        if (angleY != null)
            angleY.Value = headSwayAmplitude * Mathf.Sin(t * headSwaySpeed * Mathf.PI * 2f);

        // 眨眼
        float blink = ComputeBlink();
        if (eyeLOpen != null) eyeLOpen.Value = blink;
        if (eyeROpen != null) eyeROpen.Value = blink;
    }

    private void UpdateWalking()
    {
        float t = Time.time;
        float phase = t * walkFrequency * Mathf.PI * 2f;

        // 走路律动：身体前后倾 + 左右摆动
        // （腿部 Param9x 参数未知，待真机调参后补充）
        if (bodyAngleZ != null)
            bodyAngleZ.Value = walkBounceAmplitude * Mathf.Sin(phase);
        if (bodyAngleY != null)
            bodyAngleY.Value = walkSwayAmplitude * Mathf.Sin(phase * 0.5f);

        // 走路时睁眼
        if (eyeLOpen != null) eyeLOpen.Value = 1f;
        if (eyeROpen != null) eyeROpen.Value = 1f;
    }

    private float ComputeBlink()
    {
        if (Time.time < nextBlinkAt)
            return 1f;

        float elapsed = Time.time - nextBlinkAt;
        float half = blinkDuration * 0.5f;

        if (elapsed < half)
        {
            // 闭眼阶段：1 → 0
            return 1f - elapsed / half;
        }
        else if (elapsed < blinkDuration)
        {
            // 睁眼阶段：0 → 1
            return (elapsed - half) / half;
        }
        else
        {
            ScheduleNextBlink();
            return 1f;
        }
    }

    private void ScheduleNextBlink()
    {
        nextBlinkAt = Time.time + Random.Range(blinkMinInterval, blinkMaxInterval);
    }
}
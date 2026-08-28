// Live2DModelFeatures.cs — 为运行时实例启用待机动画、物理与表情切换。
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.HarmonicMotion;
using Live2D.Cubism.Framework.Physics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Live2DModelFeatures : MonoBehaviour
{
    private const string BreathParameterId = "ParamBreath";
    private static readonly string[] EyeBlinkParameterIds =
    {
        "ParamEyeLOpen",
        "ParamEyeROpen"
    };

    private CubismModel model;
    private CubismExpressionController expressionController;
    private CubismPhysicsController physicsController;
    private CubismScreenLookController screenLookController;
    private bool configured;

    public int ExpressionCount =>
        expressionController?.ExpressionsList?.CubismExpressionObjects?.Length ?? 0;

    public int CurrentExpressionIndex =>
        expressionController != null ? expressionController.CurrentExpressionIndex : -1;

    public string CurrentExpressionName => GetExpressionDisplayName(CurrentExpressionIndex);

    public string StatusSummary =>
        $"breath={(HasBreathDriver() ? "on" : "missing")}, " +
        $"blink={(HasEyeBlinkDriver() ? "on" : "missing")}, " +
        $"physics={(physicsController != null && physicsController.enabled ? "on" : "missing")}, " +
        $"expression={CurrentExpressionIndex + 1}/{ExpressionCount}:{CurrentExpressionName}, " +
        $"{LookStatusSummary}";

    public string LookStatusSummary => screenLookController?.StatusSummary ?? "lookAt=not_initialized";

    private void Awake()
    {
        Configure();
    }

    public bool Configure()
    {
        if (configured)
            return true;

        model = GetComponent<CubismModel>();
        if (model == null || model.Parameters == null)
        {
            Debug.LogError("[Live2D] 找不到 CubismModel 或参数列表，待机功能无法初始化。");
            return false;
        }

        ConfigureAutomaticBreath();
        ConfigureAutomaticEyeBlink();
        screenLookController = GetComponent<CubismScreenLookController>() ??
                               gameObject.AddComponent<CubismScreenLookController>();
        screenLookController.Configure(model);

        physicsController = GetComponent<CubismPhysicsController>();
        if (physicsController != null)
            physicsController.enabled = true;

        expressionController = GetComponent<CubismExpressionController>();
        if (expressionController != null)
        {
            expressionController.enabled = true;
            if (ExpressionCount > 0 && expressionController.CurrentExpressionIndex < 0)
                expressionController.CurrentExpressionIndex = 0;
        }

        // 运行时新增的控制器需要重新登记到 Cubism 的统一 LateUpdate 顺序中：
        // 表情 -> 眨眼 -> 呼吸 -> 物理 -> 渲染。
        GetComponent<CubismUpdateController>()?.Refresh();

        configured = HasBreathDriver() && HasEyeBlinkDriver() &&
                     physicsController != null && expressionController != null &&
                     ExpressionCount > 0 && screenLookController.IsConfigured;
        if (configured)
            Debug.Log($"[Live2D] 待机功能已启用: {StatusSummary}");
        else
            Debug.LogWarning($"[Live2D] 待机功能存在缺项: {StatusSummary}");

        return configured;
    }

    public bool FocusOnScreenPoint(Camera arCamera, Vector2 screenPosition)
    {
        if (screenLookController == null && !Configure())
            return false;
        return screenLookController != null &&
               screenLookController.FocusOnScreenPoint(arCamera, screenPosition);
    }

    public void ReleaseUserFocus()
    {
        screenLookController?.ReleaseFocus();
    }

    public void CancelUserFocus()
    {
        screenLookController?.CancelFocus();
    }

    public bool NextExpression(out string displayName)
    {
        displayName = "不可用";
        if (!configured && !Configure())
            return false;
        if (expressionController == null || ExpressionCount == 0)
            return false;

        int nextIndex = (expressionController.CurrentExpressionIndex + 1) % ExpressionCount;
        expressionController.CurrentExpressionIndex = nextIndex;
        displayName = GetExpressionDisplayName(nextIndex);
        Debug.Log($"[Live2D] 表情切换: index={nextIndex}, name={displayName}");
        return true;
    }

    private void ConfigureAutomaticBreath()
    {
        var parameter = model.Parameters.FindById(BreathParameterId);
        if (parameter == null)
        {
            Debug.LogWarning($"[Live2D] 找不到呼吸参数 {BreathParameterId}");
            return;
        }

        var motion = parameter.GetComponent<CubismHarmonicMotionParameter>() ??
                     parameter.gameObject.AddComponent<CubismHarmonicMotionParameter>();
        motion.Channel = 0;
        motion.Direction = CubismHarmonicMotionDirection.Centric;
        motion.NormalizedOrigin = 0.5f;
        motion.NormalizedRange = 0.5f;
        motion.Duration = 3.8f;

        var controller = GetComponent<CubismHarmonicMotionController>() ??
                         gameObject.AddComponent<CubismHarmonicMotionController>();
        controller.BlendMode = CubismParameterBlendMode.Additive;
        controller.ChannelTimescales = new[] { 1f };
        controller.enabled = true;
        controller.Refresh();
    }

    private void ConfigureAutomaticEyeBlink()
    {
        int taggedParameters = 0;
        foreach (string parameterId in EyeBlinkParameterIds)
        {
            var parameter = model.Parameters.FindById(parameterId);
            if (parameter == null)
            {
                Debug.LogWarning($"[Live2D] 找不到眨眼参数 {parameterId}");
                continue;
            }

            if (parameter.GetComponent<CubismEyeBlinkParameter>() == null)
                parameter.gameObject.AddComponent<CubismEyeBlinkParameter>();
            taggedParameters++;
        }

        if (taggedParameters == 0)
            return;

        var controller = GetComponent<CubismEyeBlinkController>() ??
                         gameObject.AddComponent<CubismEyeBlinkController>();
        controller.BlendMode = CubismParameterBlendMode.Multiply;
        controller.enabled = true;
        controller.Refresh();

        var input = GetComponent<CubismAutoEyeBlinkInput>() ??
                    gameObject.AddComponent<CubismAutoEyeBlinkInput>();
        input.Mean = 3.2f;
        input.MaximumDeviation = 1.6f;
        input.Timescale = 10f;
        input.enabled = true;
    }

    private bool HasBreathDriver()
    {
        return GetComponent<CubismHarmonicMotionController>() != null &&
               model != null &&
               model.Parameters.FindById(BreathParameterId)?.GetComponent<CubismHarmonicMotionParameter>() != null;
    }

    private bool HasEyeBlinkDriver()
    {
        if (GetComponent<CubismEyeBlinkController>() == null ||
            GetComponent<CubismAutoEyeBlinkInput>() == null || model == null)
            return false;

        foreach (string parameterId in EyeBlinkParameterIds)
        {
            if (model.Parameters.FindById(parameterId)?.GetComponent<CubismEyeBlinkParameter>() == null)
                return false;
        }

        return true;
    }

    private string GetExpressionDisplayName(int index)
    {
        var expressions = expressionController?.ExpressionsList?.CubismExpressionObjects;
        if (expressions == null || index < 0 || index >= expressions.Length || expressions[index] == null)
            return "无";

        string rawName = expressions[index].name
            .Replace(".exp3", string.Empty)
            .Replace(".json", string.Empty);
        return rawName switch
        {
            "normal" => "普通",
            "resonate" => "共鸣",
            "like" => "喜欢",
            "sleepy" => "困倦",
            "sing" => "唱歌",
            "dumb" => "呆萌",
            "angry" => "生气",
            "ease" => "放松",
            "fear" => "害怕",
            "excited" => "兴奋",
            "sad" => "悲伤",
            "moemoe" => "卖萌",
            _ => rawName
        };
    }
}

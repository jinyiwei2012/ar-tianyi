// ExpressionCycleUI.cs — 真机表情切换按钮。
using UnityEngine;

[RequireComponent(typeof(PlaceOnPlane))]
public sealed class ExpressionCycleUI : MonoBehaviour
{
    private static Rect panelRect;
    private PlaceOnPlane placement;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private float feedbackUntil;
    private string feedbackText;

    private void Awake()
    {
        placement = GetComponent<PlaceOnPlane>();
    }

    public static bool IsPointerOverExpressionUI(Vector2 screenPosition)
    {
        if (panelRect.width <= 0f || panelRect.height <= 0f)
            return false;

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return panelRect.Contains(guiPoint);
    }

    private void OnGUI()
    {
        if (placement == null || !placement.IsModelReady || RuntimeDebugPanel.IsOpen)
        {
            panelRect = Rect.zero;
            return;
        }

        EnsureStyles();
        var safe = Screen.safeArea;
        float scale = Mathf.Clamp(Screen.width / 1260f, 0.8f, 1.45f);
        float margin = Mathf.Max(14f, Screen.width * 0.015f);
        float safeTop = Screen.height - safe.yMax;
        float topGuideHeight = Mathf.Max(120f, Screen.height * 0.13f);
        float width = Mathf.Clamp(Screen.width * 0.24f, 300f, 430f);
        float height = 145f * scale;
        panelRect = new Rect(
            safe.x + margin,
            safeTop + margin + topGuideHeight + 12f,
            width,
            height);

        GUI.Box(panelRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(
            panelRect.x + 12f * scale,
            panelRect.y + 8f * scale,
            panelRect.width - 24f * scale,
            panelRect.height - 16f * scale));

        string expressionName = Time.unscaledTime < feedbackUntil
            ? feedbackText
            : placement.CurrentExpressionName;
        GUILayout.Label($"当前表情：{expressionName}", titleStyle);
        if (GUILayout.Button("下一个表情", buttonStyle, GUILayout.Height(58f * scale)))
        {
            if (placement.TryNextExpression(out string nextExpression))
            {
                feedbackText = nextExpression;
                feedbackUntil = Time.unscaledTime + 1.2f;
            }
            else
            {
                feedbackText = "表情不可用";
                feedbackUntil = Time.unscaledTime + 1.2f;
            }
        }

        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        int bodySize = Mathf.Clamp(Screen.width / 55, 20, 34);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = bodySize,
            fontStyle = FontStyle.Bold
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = bodySize,
            fontStyle = FontStyle.Bold
        };
    }
}

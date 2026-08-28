// ModelNudgeUI.cs — 真机诊断用：按世界 XYZ 轴微调模型 Root，并显示累计偏移。
using UnityEngine;

[RequireComponent(typeof(PlaceOnPlane))]
public sealed class ModelNudgeUI : MonoBehaviour
{
    [SerializeField, Min(0.005f)] private float stepMeters = 0.05f;

    private static Rect panelRect;
    private PlaceOnPlane placement;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle valueStyle;

    private void Awake()
    {
        placement = GetComponent<PlaceOnPlane>();
    }

    public static bool IsPointerOverNudgeUI(Vector2 screenPosition)
    {
        if (panelRect.width <= 0f || panelRect.height <= 0f)
            return false;

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return panelRect.Contains(guiPoint);
    }

    private void OnGUI()
    {
        if (placement == null || !placement.IsModelReady || placement.IsPositionLocked ||
            RuntimeDebugPanel.IsOpen)
        {
            panelRect = Rect.zero;
            return;
        }

        EnsureStyles();
        var safe = Screen.safeArea;
        float scale = Mathf.Clamp(Screen.width / 1260f, 0.8f, 1.45f);
        float margin = Mathf.Max(14f, Screen.width * 0.015f);
        float width = Mathf.Clamp(Screen.width * 0.26f, 330f, 480f);
        float height = 330f * scale;
        float safeTop = Screen.height - safe.yMax;
        float topGuideHeight = Mathf.Max(120f, Screen.height * 0.13f);
        panelRect = new Rect(
            safe.xMax - width - margin,
            safeTop + margin + topGuideHeight + 12f,
            width,
            height);

        GUI.Box(panelRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(
            panelRect.x + 12f * scale,
            panelRect.y + 8f * scale,
            panelRect.width - 24f * scale,
            panelRect.height - 16f * scale));

        GUILayout.Label($"模型微调 · 每步 {stepMeters * 100f:F0}cm", titleStyle);
        DrawAxisRow("X", Vector3.right, new Color(1f, 0.35f, 0.35f));
        DrawAxisRow("Y", Vector3.up, new Color(0.35f, 1f, 0.45f));
        DrawAxisRow("Z", Vector3.forward, new Color(0.35f, 0.55f, 1f));

        Vector3 offset = placement.ManualWorldOffset;
        GUILayout.Label(
            $"累计 XYZ = ({offset.x:F2}, {offset.y:F2}, {offset.z:F2}) m\n" +
            $"总位移 = {offset.magnitude * 100f:F1} cm",
            valueStyle);
        if (GUILayout.Button("偏移归零", buttonStyle, GUILayout.Height(48f * scale)))
            placement.ResetModelWorldNudge();

        GUILayout.EndArea();
    }

    private void DrawAxisRow(string axis, Vector3 direction, Color color)
    {
        GUILayout.BeginHorizontal();
        var previous = GUI.color;
        GUI.color = color;
        GUILayout.Label(axis, labelStyle, GUILayout.Width(42f));
        GUI.color = previous;
        if (GUILayout.Button(axis + "-", buttonStyle, GUILayout.Height(48f)))
            placement.NudgeModelWorld(-direction * stepMeters);
        if (GUILayout.Button(axis + "+", buttonStyle, GUILayout.Height(48f)))
            placement.NudgeModelWorld(direction * stepMeters);
        GUILayout.EndHorizontal();
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
        labelStyle = new GUIStyle(titleStyle)
        {
            alignment = TextAnchor.MiddleLeft
        };
        valueStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(18, bodySize - 2),
            wordWrap = true
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = bodySize,
            fontStyle = FontStyle.Bold
        };
    }
}

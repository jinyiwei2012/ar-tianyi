// PositionLockUI.cs — 放置完成后由用户明确锁定/解锁模型位置。
using UnityEngine;

[RequireComponent(typeof(PlaceOnPlane))]
public sealed class PositionLockUI : MonoBehaviour
{
    private static Rect buttonRect;
    private PlaceOnPlane placement;
    private GUIStyle buttonStyle;

    private void Awake()
    {
        placement = GetComponent<PlaceOnPlane>();
    }

    public static bool IsPointerOverLockUI(Vector2 screenPosition)
    {
        if (buttonRect.width <= 0f || buttonRect.height <= 0f)
            return false;

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return buttonRect.Contains(guiPoint);
    }

    private void OnGUI()
    {
        if (placement == null || !placement.IsModelReady || RuntimeDebugPanel.IsOpen)
        {
            buttonRect = Rect.zero;
            return;
        }

        EnsureStyle();
        var safe = Screen.safeArea;
        float scale = Mathf.Clamp(Screen.width / 1260f, 0.8f, 1.45f);
        float width = Mathf.Clamp(Screen.width * 0.34f, 360f, 520f);
        float height = 74f * scale;
        float safeBottom = Screen.height - safe.yMin;
        float bottomGuideHeight = Mathf.Max(100f, Screen.height * 0.10f);
        float margin = Mathf.Max(18f, Screen.width * 0.035f);
        buttonRect = new Rect(
            (Screen.width - width) * 0.5f,
            safeBottom - bottomGuideHeight - margin - height - 16f * scale,
            width,
            height);

        var previousColor = GUI.backgroundColor;
        GUI.backgroundColor = placement.IsPositionLocked
            ? new Color(0.16f, 0.78f, 0.46f, 1f)
            : new Color(0.95f, 0.66f, 0.14f, 1f);
        string label = placement.IsPositionLocked
            ? "已锁定 · 点击解锁"
            : "锁定位置";
        if (GUI.Button(buttonRect, label, buttonStyle))
            placement.SetPositionLocked(!placement.IsPositionLocked);
        GUI.backgroundColor = previousColor;
    }

    private void EnsureStyle()
    {
        if (buttonStyle != null)
            return;

        int bodySize = Mathf.Clamp(Screen.width / 48, 22, 36);
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = bodySize,
            fontStyle = FontStyle.Bold
        };
    }
}

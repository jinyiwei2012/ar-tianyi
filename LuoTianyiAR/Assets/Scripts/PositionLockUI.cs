// PositionLockUI.cs — 放置完成后由用户明确锁定/解锁模型位置。
using UnityEngine;

[RequireComponent(typeof(PlaceOnPlane))]
public sealed class PositionLockUI : MonoBehaviour
{
    private static Rect buttonRect;
    private PlaceOnPlane placement;
    private Texture2D lockOpenIcon;
    private Texture2D lockClosedIcon;

    private void Awake()
    {
        placement = GetComponent<PlaceOnPlane>();
        lockOpenIcon = Resources.Load<Texture2D>("UI/lock-open");
        lockClosedIcon = Resources.Load<Texture2D>("UI/lock-closed");
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
        GUI.depth = -20;
        if (placement == null || !placement.IsModelReady || RuntimeDebugPanel.IsOpen ||
            CaptureGalleryUI.IsOpen || CameraCaptureUI.IsManualLightEditing)
        {
            buttonRect = Rect.zero;
            return;
        }

        Rect viewfinder = CameraCaptureUI.GetViewfinderRect();
        float scale = Mathf.Clamp(Screen.width / 1260f, 0.8f, 1.45f);
        float touchSize = 126f * scale;
        float iconSize = 92f * scale;
        float margin = Mathf.Max(18f, Screen.width * 0.025f);
        buttonRect = new Rect(
            viewfinder.xMax - touchSize - margin,
            viewfinder.yMax - touchSize - margin,
            touchSize,
            touchSize);

        if (GUI.Button(buttonRect, GUIContent.none, GUIStyle.none))
            placement.SetPositionLocked(!placement.IsPositionLocked);

        Rect iconRect = new(
            buttonRect.center.x - iconSize * 0.5f,
            buttonRect.center.y - iconSize * 0.5f,
            iconSize,
            iconSize);
        Texture2D icon = placement.IsPositionLocked ? lockClosedIcon : lockOpenIcon;
        if (icon != null)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.48f);
            GUI.DrawTexture(new Rect(iconRect.x + 4f * scale, iconRect.y + 4f * scale, iconRect.width, iconRect.height),
                icon, ScaleMode.ScaleToFit, true);
            GUI.color = placement.IsPositionLocked
                ? new Color(1f, 0.78f, 0.08f, 1f)
                : Color.white;
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

    }
}

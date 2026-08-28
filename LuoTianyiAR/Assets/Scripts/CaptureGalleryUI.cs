// CaptureGalleryUI.cs — 应用内照片查看器与整组图层删除。
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(CameraCaptureUI))]
public sealed class CaptureGalleryUI : MonoBehaviour
{
    private const string CaptureFolderName = "Captures";
    private const string LastCompositePathKey = "LuoTianyiAR.LastCompositePath";

    private static CaptureGalleryUI instance;

    private CameraCaptureUI cameraUI;
    private Texture2D photo;
    private Texture2D pixel;
    private Texture2D backIcon;
    private Texture2D shareIcon;
    private Texture2D editIcon;
    private Texture2D deleteIcon;
    private Texture2D moreIcon;
    private GUIStyle dateStyle;
    private GUIStyle timeStyle;
    private GUIStyle toolLabelStyle;
    private GUIStyle dialogTitleStyle;
    private GUIStyle dialogBodyStyle;
    private GUIStyle dialogActionStyle;
    private GUIStyle deleteActionStyle;
    private GUIStyle toastStyle;
    private Texture2D dialogPanelTexture;
    private Texture2D deleteOutlineTexture;
    private Vector2Int dialogPanelTextureSize;
    private Vector2Int deleteOutlineTextureSize;

    private bool isOpen;
    private bool confirmDelete;
    private string captureDirectory;
    private string captureId;
    private string galleryUri;
    private DateTime createdAt;
    private string toast;
    private float toastUntil;

    public static bool IsOpen => instance != null && instance.isOpen;

    private void Awake()
    {
        instance = this;
        cameraUI = GetComponent<CameraCaptureUI>();
        backIcon = Resources.Load<Texture2D>("UI/gallery-back");
        shareIcon = Resources.Load<Texture2D>("UI/gallery-share");
        editIcon = Resources.Load<Texture2D>("UI/gallery-edit");
        deleteIcon = Resources.Load<Texture2D>("UI/gallery-delete");
        moreIcon = Resources.Load<Texture2D>("UI/gallery-more");

        pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        pixel.name = "CaptureGalleryPixel";
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    public bool OpenLatest()
    {
        string path = FindLatestCompositePath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (!LoadCapture(path))
            return false;

        isOpen = true;
        confirmDelete = false;
        toast = null;
        return true;
    }

    public void Close()
    {
        isOpen = false;
        confirmDelete = false;
        toast = null;
        ReleasePhoto();
    }

    private void OnGUI()
    {
        if (!isOpen)
            return;

        GUI.depth = -1000;
        EnsureStyles();
        DrawSolid(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.965f, 0.965f, 0.965f, 1f));

        float scale = Mathf.Clamp(Screen.width / 1260f, 0.75f, 1.5f);
        float safeTop = Screen.height - Screen.safeArea.yMax;
        float safeBottom = Screen.safeArea.y;
        float headerHeight = Mathf.Clamp(Screen.height * 0.125f, 300f * scale, 380f * scale);
        float toolbarHeight = Mathf.Clamp(Screen.height * 0.19f, 470f * scale, 570f * scale);
        Rect header = new(0f, 0f, Screen.width, headerHeight);
        Rect toolbar = new(0f, Screen.height - toolbarHeight, Screen.width, toolbarHeight);
        Rect photoArea = new(0f, header.yMax, Screen.width, Mathf.Max(1f, toolbar.y - header.yMax));

        DrawHeader(header, safeTop, scale);
        DrawPhoto(photoArea);
        DrawToolbar(toolbar, safeBottom, scale);

        if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast))
        {
            float width = Mathf.Min(Screen.width * 0.72f, 760f * scale);
            Rect toastRect = new(
                (Screen.width - width) * 0.5f,
                toolbar.y - 104f * scale,
                width,
                76f * scale);
            DrawSolid(toastRect, new Color(0.10f, 0.10f, 0.10f, 0.90f));
            GUI.Label(toastRect, toast, toastStyle);
        }

        if (confirmDelete)
            DrawDeleteConfirmation(scale);
    }

    private void DrawHeader(Rect header, float safeTop, float scale)
    {
        DrawSolid(header, new Color(0.965f, 0.965f, 0.965f, 1f));
        float contentY = Mathf.Max(safeTop + 42f * scale, header.height * 0.32f);
        float backSize = 104f * scale;
        Rect backRect = new(24f * scale, contentY - 10f * scale, backSize, backSize);
        if (GUI.Button(backRect, GUIContent.none, GUIStyle.none))
        {
            Close();
            return;
        }
        if (backIcon != null)
            GUI.DrawTexture(backRect, backIcon, ScaleMode.ScaleToFit, true);

        string day = createdAt.Date == DateTime.Now.Date
            ? "今天"
            : createdAt.Date == DateTime.Now.Date.AddDays(-1)
                ? "昨天"
                : createdAt.ToString("M月d日", CultureInfo.CurrentCulture);
        float textX = backRect.xMax + 8f * scale;
        GUI.Label(
            new Rect(textX, contentY - 22f * scale, Screen.width - textX - 30f * scale, 72f * scale),
            day,
            dateStyle);
        GUI.Label(
            new Rect(textX, contentY + 48f * scale, Screen.width - textX - 30f * scale, 52f * scale),
            createdAt.ToString("HH:mm", CultureInfo.CurrentCulture),
            timeStyle);
        DrawSolid(new Rect(0f, header.yMax - 2f, header.width, 2f), new Color(0f, 0f, 0f, 0.10f));
    }

    private void DrawPhoto(Rect area)
    {
        DrawSolid(area, new Color(0.965f, 0.965f, 0.965f, 1f));
        if (photo != null)
            GUI.DrawTexture(area, photo, ScaleMode.ScaleToFit, false);
    }

    private void DrawToolbar(Rect toolbar, float safeBottom, float scale)
    {
        DrawSolid(toolbar, new Color(0.975f, 0.975f, 0.975f, 1f));
        DrawSolid(new Rect(toolbar.x, toolbar.y, toolbar.width, 2f), new Color(0f, 0f, 0f, 0.10f));

        float filmHeight = 130f * scale;
        float thumbHeight = 92f * scale;
        float thumbWidth = photo != null && photo.height > 0
            ? thumbHeight * photo.width / photo.height
            : thumbHeight;
        thumbWidth = Mathf.Clamp(thumbWidth, 54f * scale, 124f * scale);
        Rect thumbRect = new(
            (Screen.width - thumbWidth) * 0.5f,
            toolbar.y + (filmHeight - thumbHeight) * 0.5f,
            thumbWidth,
            thumbHeight);
        if (photo != null)
            GUI.DrawTexture(thumbRect, photo, ScaleMode.ScaleAndCrop, false);
        DrawOutline(thumbRect, new Color(0.12f, 0.12f, 0.12f, 0.65f), 3f * scale);

        string[] labels = { "分享", "编辑", "删除", "更多" };
        Texture2D[] icons = { shareIcon, editIcon, deleteIcon, moreIcon };
        float segmentWidth = Screen.width / labels.Length;
        float iconSize = 94f * scale;
        float iconY = toolbar.y + filmHeight + 32f * scale;
        float labelY = iconY + iconSize + 18f * scale;

        for (int i = 0; i < labels.Length; i++)
        {
            Rect segment = new(i * segmentWidth, toolbar.y + filmHeight, segmentWidth, toolbar.height - filmHeight - safeBottom);
            Rect iconRect = new(segment.center.x - iconSize * 0.5f, iconY, iconSize, iconSize);
            if (icons[i] != null)
                GUI.DrawTexture(iconRect, icons[i], ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(segment.x, labelY, segment.width, 58f * scale), labels[i], toolLabelStyle);

            if (!confirmDelete && GUI.Button(segment, GUIContent.none, GUIStyle.none))
            {
                if (i == 2)
                    confirmDelete = true;
                else
                    ShowToast($"{labels[i]}功能暂未开放");
            }
        }
    }

    private void DrawDeleteConfirmation(float scale)
    {
        DrawSolid(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.44f));
        float width = Mathf.Min(Screen.width - 96f * scale, 860f * scale);
        float height = 610f * scale;
        Rect panel = new(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);
        float deleteWidth = panel.width - 152f * scale;
        float deleteHeight = 124f * scale;
        EnsureDialogTextures(
            Mathf.RoundToInt(panel.width),
            Mathf.RoundToInt(panel.height),
            Mathf.RoundToInt(deleteWidth),
            Mathf.RoundToInt(deleteHeight));
        if (dialogPanelTexture != null)
            GUI.DrawTexture(panel, dialogPanelTexture, ScaleMode.StretchToFill, true);
        else
            DrawSolid(panel, Color.white);

        GUI.Label(new Rect(panel.x + 36f * scale, panel.y + 34f * scale, panel.width - 72f * scale, 82f * scale),
            "删除这张照片？", dialogTitleStyle);
        GUI.Label(new Rect(panel.x + 62f * scale, panel.y + 124f * scale, panel.width - 124f * scale, 108f * scale),
            "相机原图、透明模型层和合成图将一起删除。", dialogBodyStyle);

        Rect delete = new(
            panel.center.x - deleteWidth * 0.5f,
            panel.y + 284f * scale,
            deleteWidth,
            deleteHeight);
        if (deleteOutlineTexture != null)
            GUI.DrawTexture(delete, deleteOutlineTexture, ScaleMode.StretchToFill, true);
        if (GUI.Button(delete, "删除", deleteActionStyle))
            DeleteCurrentCapture();

        Rect cancel = new(
            panel.x + 76f * scale,
            delete.yMax + 48f * scale,
            panel.width - 152f * scale,
            104f * scale);
        if (GUI.Button(cancel, "取消", dialogActionStyle))
            confirmDelete = false;
    }

    private void DeleteCurrentCapture()
    {
        confirmDelete = false;
        if (string.IsNullOrEmpty(captureDirectory) || !Directory.Exists(captureDirectory))
        {
            ShowToast("照片文件已经不存在");
            cameraUI?.ReloadLastThumbnailFromStorage();
            Close();
            return;
        }

        bool galleryDeleted = DeleteGalleryCopy(galleryUri);
        try
        {
            string deletedId = captureId;
            Directory.Delete(captureDirectory, true);
            cameraUI?.ReloadLastThumbnailFromStorage();

            string next = FindLatestCompositePath();
            if (!string.IsNullOrEmpty(next) && LoadCapture(next))
                ShowToast(galleryDeleted ? "照片已删除" : "应用内照片已删除，系统相册删除失败");
            else
                Close();

            Debug.Log($"[Gallery] 删除完成: captureId={deletedId}, gallery={(galleryDeleted ? "deleted_or_untracked" : "failed")}");
        }
        catch (Exception exception)
        {
            ShowToast("删除失败，请稍后重试");
            Debug.LogError("[Gallery] 删除失败: " + exception.Message);
        }
    }

    private bool LoadCapture(string compositePath)
    {
        if (string.IsNullOrEmpty(compositePath) || !File.Exists(compositePath))
            return false;

        try
        {
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!loaded.LoadImage(File.ReadAllBytes(compositePath)))
            {
                Destroy(loaded);
                return false;
            }

            ReleasePhoto();
            photo = loaded;
            captureDirectory = Path.GetDirectoryName(compositePath);
            captureId = Path.GetFileName(captureDirectory);
            galleryUri = string.Empty;
            createdAt = File.GetCreationTime(compositePath);

            string metadataPath = Path.Combine(captureDirectory, "metadata.json");
            if (File.Exists(metadataPath))
            {
                var metadata = JsonUtility.FromJson<CaptureMetadata>(File.ReadAllText(metadataPath));
                if (metadata != null)
                {
                    if (!string.IsNullOrEmpty(metadata.captureId))
                        captureId = metadata.captureId;
                    galleryUri = metadata.galleryUri ?? string.Empty;
                    if (DateTime.TryParse(metadata.createdAt, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out DateTime parsed))
                        createdAt = parsed.ToLocalTime();
                }
            }

            PlayerPrefs.SetString(LastCompositePathKey, compositePath);
            PlayerPrefs.Save();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Gallery] 无法加载照片: " + exception.Message);
            return false;
        }
    }

    private static string FindLatestCompositePath()
    {
        string preferred = PlayerPrefs.GetString(LastCompositePathKey, string.Empty);
        if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred))
            return preferred;

        string root = Path.Combine(Application.persistentDataPath, CaptureFolderName);
        if (!Directory.Exists(root))
            return null;

        return Directory.GetDirectories(root)
            .OrderByDescending(Path.GetFileName)
            .Select(directory => Path.Combine(directory, "composite.png"))
            .FirstOrDefault(File.Exists);
    }

    private static bool DeleteGalleryCopy(string uriText)
    {
        if (string.IsNullOrEmpty(uriText))
            return true;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver");
            using var uriClass = new AndroidJavaClass("android.net.Uri");
            using AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", uriText);
            resolver.Call<int>("delete", uri, null, null);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Gallery] 无法删除系统相册副本: " + exception.Message);
            return false;
        }
#else
        return true;
#endif
    }

    private void ShowToast(string message)
    {
        toast = message;
        toastUntil = Time.unscaledTime + 2.2f;
    }

    private void EnsureStyles()
    {
        if (dateStyle != null)
            return;

        float scale = Mathf.Clamp(Screen.width / 1260f, 0.75f, 1.5f);
        Color ink = new(0.10f, 0.10f, 0.10f, 1f);
        dateStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = Mathf.RoundToInt(54f * scale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = ink }
        };
        timeStyle = new GUIStyle(dateStyle)
        {
            fontSize = Mathf.RoundToInt(34f * scale),
            fontStyle = FontStyle.Normal,
            normal = { textColor = new Color(0.40f, 0.40f, 0.40f, 1f) }
        };
        toolLabelStyle = new GUIStyle(dateStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(34f * scale),
            fontStyle = FontStyle.Normal
        };
        dialogTitleStyle = new GUIStyle(dateStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(42f * scale)
        };
        dialogBodyStyle = new GUIStyle(toolLabelStyle)
        {
            fontSize = Mathf.RoundToInt(29f * scale),
            wordWrap = true,
            normal = { textColor = new Color(0.34f, 0.34f, 0.34f, 1f) }
        };
        int actionFontSize = Mathf.Clamp(
            Mathf.RoundToInt(14f * Screen.width / 420f),
            32,
            52);
        dialogActionStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = actionFontSize,
            fontStyle = FontStyle.Normal,
            normal = { textColor = ink }
        };
        deleteActionStyle = new GUIStyle(dialogActionStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.86f, 0.12f, 0.12f, 1f) }
        };
        toastStyle = new GUIStyle(toolLabelStyle)
        {
            fontSize = Mathf.RoundToInt(27f * scale),
            normal = { textColor = Color.white }
        };
    }

    private void DrawSolid(Rect rect, Color color)
    {
        if (pixel == null)
            return;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }

    private void DrawOutline(Rect rect, Color color, float thickness)
    {
        DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void EnsureDialogTextures(int panelWidth, int panelHeight, int buttonWidth, int buttonHeight)
    {
        var panelSize = new Vector2Int(Mathf.Max(1, panelWidth), Mathf.Max(1, panelHeight));
        if (dialogPanelTexture == null || dialogPanelTextureSize != panelSize)
        {
            if (dialogPanelTexture != null)
                Destroy(dialogPanelTexture);
            dialogPanelTexture = CreateRoundedTexture(
                panelSize.x,
                panelSize.y,
                Mathf.RoundToInt(panelSize.x * 0.07f),
                Color.white,
                Color.clear,
                0);
            dialogPanelTextureSize = panelSize;
        }

        var buttonSize = new Vector2Int(Mathf.Max(1, buttonWidth), Mathf.Max(1, buttonHeight));
        if (deleteOutlineTexture == null || deleteOutlineTextureSize != buttonSize)
        {
            if (deleteOutlineTexture != null)
                Destroy(deleteOutlineTexture);
            deleteOutlineTexture = CreateRoundedTexture(
                buttonSize.x,
                buttonSize.y,
                Mathf.RoundToInt(buttonSize.y * 0.48f),
                Color.clear,
                new Color(0.94f, 0.05f, 0.10f, 1f),
                Mathf.Max(4, Mathf.RoundToInt(buttonSize.y * 0.055f)));
            deleteOutlineTextureSize = buttonSize;
        }
    }

    private static Texture2D CreateRoundedTexture(
        int width,
        int height,
        int radius,
        Color fill,
        Color border,
        int borderWidth)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "GalleryRoundedUI";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[width * height];
        Color32 fillColor = fill;
        Color32 borderColor = border;
        Color32 transparent = new(0, 0, 0, 0);
        int innerWidth = width - borderWidth * 2;
        int innerHeight = height - borderWidth * 2;
        int innerRadius = Mathf.Max(0, radius - borderWidth);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool insideOuter = IsInsideRoundedRect(x, y, width, height, radius);
                if (!insideOuter)
                {
                    pixels[y * width + x] = transparent;
                    continue;
                }

                bool insideInner = borderWidth <= 0 ||
                    IsInsideRoundedRect(
                        x - borderWidth,
                        y - borderWidth,
                        innerWidth,
                        innerHeight,
                        innerRadius);
                pixels[y * width + x] = borderWidth > 0 && !insideInner
                    ? borderColor
                    : fillColor;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static bool IsInsideRoundedRect(int x, int y, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || x >= width || y >= height)
            return false;
        radius = Mathf.Clamp(radius, 0, Mathf.Min(width, height) / 2);
        if (radius == 0 || (x >= radius && x < width - radius) ||
            (y >= radius && y < height - radius))
            return true;

        float centerX = x < radius ? radius - 0.5f : width - radius - 0.5f;
        float centerY = y < radius ? radius - 0.5f : height - radius - 0.5f;
        float deltaX = x - centerX;
        float deltaY = y - centerY;
        return deltaX * deltaX + deltaY * deltaY <= radius * radius;
    }

    private void ReleasePhoto()
    {
        if (photo != null)
            Destroy(photo);
        photo = null;
    }

    private void OnDestroy()
    {
        ReleasePhoto();
        if (pixel != null)
            Destroy(pixel);
        if (dialogPanelTexture != null)
            Destroy(dialogPanelTexture);
        if (deleteOutlineTexture != null)
            Destroy(deleteOutlineTexture);
        if (instance == this)
            instance = null;
    }

    [Serializable]
    private sealed class CaptureMetadata
    {
        public string captureId;
        public string createdAt;
        public string galleryUri;
    }
}

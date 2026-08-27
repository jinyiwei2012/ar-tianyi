// ParameterDebugWindow.cs — 真机参数探测工具
// 枚举模型全部 Cubism 参数并实时驱动，用于确定 Param9x 等编号参数对应哪个 BodyPart，
// 为走路摆腿等动画调参。由 RuntimeDebugPanel 的“参数探测”按钮打开。
using System.Collections.Generic;
using UnityEngine;
using Live2D.Cubism.Core;

public sealed class ParameterDebugWindow : MonoBehaviour
{
    private const float SliderMin = -30f;
    private const float SliderMax = 30f;

    private static ParameterDebugWindow instance;

    private CubismParameter[] parameters;
    private readonly List<float> parameterValues = new();
    private Vector2 scrollPosition;
    private bool isOpen;
    private float nextReferenceRefresh;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;

    public static bool IsOpen => instance != null && instance.isOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        var go = new GameObject("Parameter Debug Window");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ParameterDebugWindow>();
    }

    public static void Toggle()
    {
        if (instance == null)
            return;

        instance.isOpen = !instance.isOpen;
        if (instance.isOpen)
            instance.Refresh();
    }

    private void Update()
    {
        // 模型放置后参数才存在；每 0.5s 重试绑定一次，不打扰每帧逻辑。
        if (!isOpen || Time.unscaledTime < nextReferenceRefresh)
            return;
        nextReferenceRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    private void Refresh()
    {
        var model = FindFirstObjectByType<CubismModel>(FindObjectsInactive.Include);
        if (model == null || model.Parameters == null)
            return;

        parameters = model.Parameters;
        parameterValues.Clear();
        foreach (var p in parameters)
            parameterValues.Add(p.Value);
    }

    private void OnGUI()
    {
        if (!isOpen)
            return;

        GUI.depth = -1001;
        EnsureStyles();

        var rect = new Rect(24f, 90f, 380f, Mathf.Max(300f, Screen.height - 180f));
        GUI.Box(rect, GUIContent.none);

        GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, rect.height - 24f));

        GUILayout.BeginHorizontal();
        GUILayout.Label("参数探测：拖动滑块看模型哪个部位动", titleStyle);
        if (GUILayout.Button("关闭", GUILayout.Width(80f), GUILayout.Height(40f)))
            isOpen = false;
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);

        if (parameters == null || parameters.Length == 0)
        {
            GUILayout.Label("未找到模型参数（请先放置洛天依）", labelStyle);
        }
        else
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < parameters.Length; i++)
            {
                GUILayout.Label(parameters[i].Id, labelStyle);
                float value = parameterValues[i];
                float newValue = GUILayout.HorizontalSlider(value, SliderMin, SliderMax);
                if (Mathf.Abs(newValue - value) > 0.001f)
                {
                    parameterValues[i] = newValue;
                    parameters[i].Value = newValue;
                }
                GUILayout.Space(4f);
            }
            GUILayout.EndScrollView();
            GUILayout.Label($"共 {parameters.Length} 个参数（范围 {SliderMin:F0}~{SliderMax:F0}）", labelStyle);
        }

        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        int titleSize = Mathf.Clamp(Screen.width / 32, 22, 34);
        int bodySize = Mathf.Clamp(Screen.width / 44, 18, 26);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = titleSize,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = bodySize,
            wordWrap = true
        };
    }
}

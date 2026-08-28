# 洛天依 AR（LuoTianyiAR）

把洛天依 Live2D 角色实时放入真实环境的 Android AR 应用。
技术路线：**Unity 6 + AR Foundation + ARCore + Live2D Cubism SDK**，Live2D 作为"3D 世界中的 2D 物体"渲染在真实平面上。

## 功能特性

- **P0 已实现**：识别真实水平面 → 点击放置洛天依（0.60m 世界高度）→ 单指拖动换位置 → 双指缩放（0.20–1.50m）→ 模型始终正面朝向相机（cylindrical billboard）
- **遮挡（P1）**：环境深度可用时开启 `AROcclusionManager`，不可用时自动降级，不限制普通 ARCore 设备安装
- **移动（P2 进行中）**：`LuoMovement.cs` 提供平面内移动能力
- **真机调试**：内置 `RuntimeDebugPanel`，一键复制完整诊断报告

## 技术栈

| 组件 | 版本 |
|------|------|
| Unity | 6000.3.22f1 |
| AR Foundation | 6.3.5 |
| ARCore XR Plugin | 6.3.5 |
| XR Management | 4.5.0 |
| Input System | 1.11.2 |
| URP | 17.2.0（lock 解析为 17.3.0） |
| Live2D Cubism SDK | 5-r.5 |
| Android | IL2CPP / ARM64 / OpenGLES3 / min API 25 / target 34 |

## 目录结构

```
├── docs/
│   ├── PRD.md                    # 核心方案文档（需求、技术路线、MVP 顺序）
│   └── PLAN.md                   # 实施计划（Phase 0-4）与当前状态快照
├── HANDOFF_AR_PLACEMENT.md       # 位置偏移问题交接（debut 分支，诊断入口）
├── LuoTianyiAR/                  # Unity 工程
│   └── Assets/
│       ├── Scripts/              # PlaceOnPlane / PlacementGuideUI / RuntimeDebugPanel 等
│       ├── Scenes/ARScene.unity  # 主场景
│       ├── Editor/               # AndroidBuild.cs / ARSceneSetup.cs
│       ├── Settings/             # URP 资产
│       └── XR/                   # ARCore / Simulation Loader 配置
└── live2d/                       # 洛天依 Cubism 模型资产
```

## 构建 APK

需要本机安装 Unity 6000.3.22f1。命令行批处理构建（详见 `docs/HANDOFF_AR_PLACEMENT.md` 第 9 节）：

```powershell
$project = '<repo>\LuoTianyiAR'
$unity = '<Unity 6000.3.22f1>\Editor\Unity.exe'
$android = '<Unity>\Editor\Data\PlaybackEngines\AndroidPlayer'
$env:UNITY_JDK_PATH    = "$android\OpenJDK"
$env:ANDROID_SDK_ROOT  = "$android\SDK"
$env:ANDROID_NDK_ROOT  = "$android\NDK"

& $unity -batchmode -quit -nographics -projectPath $project `
  -executeMethod AndroidBuild.Build -logFile "$project\Logs\build.log"
```

输出：`LuoTianyiAR/Builds/LuoTianyiAR.apk`

## 真机测试

- 需要支持 ARCore 的 Android 设备（安装 Google Play Services for AR）
- `adb install -r LuoTianyiAR.apk` 覆盖安装后启动
- 右上角"调试"按钮可复制诊断报告；`PlaceOnPlane` 的 `enablePlacementDiagnosticMarkers` 开关会在命中点生成独立标记，用于区分 ARCore 命中与模型对齐问题

## 当前状态（2026-08-27）

- ✅ P0 平面链路已在真机跑通：模型显示、相机读取、放置 UI、平面识别、Anchor
- ✅ Phase 2 移动代码已实现：点击走路 + 状态机 + 程序化动画（呼吸/眨眼/走路律动），待真机验证
- 🔧 进行中："模型不在准星位置"的偏移诊断（见 `HANDOFF_AR_PLACEMENT.md`）——已实现中心准星模式、拖动阈值、Placement Root、footCenter 完整 XYZ 对齐，待真机复测
- 🔧 进行中：Phase 3 遮挡（`feat/phase3-occlusion` 分支）——遮挡诊断 + 参照立方体 + 深度模式监控已实现，待真机验证

## 分支说明

| 分支 | 说明 |
|------|------|
| `main` | 主开发分支（含最新代码修复与文档更新） |
| `debut` | 含 `HANDOFF_AR_PLACEMENT.md` 交接文档（诊断入口） |
| `release/v1.0.0` | 发布分支，推送时由 CI 构建 APK |

> 约束：不要把洛天依模型数据喂给生成式大模型（见 PRD）。

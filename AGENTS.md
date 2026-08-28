# 项目知识库

**生成时间:** 2026-08-28（debut × phase3 合并后更新）
**仓库状态:** git 仓库；含 Unity AR 工程源码 + Live2D 资产 + 方案文档

## OVERVIEW

洛天依（Luo Tianyi）AR 项目：把 Live2D 角色实时放入真实环境（Unity + AR Foundation + ARCore）。
`docs/PRD.md` 是技术方案文档；`LuoTianyiAR/` 是可运行的 Unity 工程（6000.3.22f1）。
已跑通"模型显示 + 相机读取 + 放置 UI + 移动 + 遮挡"。"模型不在准星位置"偏移问题的根因
已定位并修复（XR Origin 误用 VR 式 1.1176m Camera Y Offset + Anchor 首帧 Transform 依赖，
已改为 Device/0m + 异步 Pose Anchor），并增加二维码定位卡作为位置 ground truth，待真机复验。

## GIT

- 分支：`main`（集成分支，已合并 debut/phase2/phase3/camera_mode/harmory）、`debut`、`feat/phase3-occlusion`、`release/v1.0.0`
- 远程：`origin` = https://github.com/jinyiwei2012/ar-tianyi.git
- 维护约定：修复/功能开独立分支，不要直接大改 `main`

## STRUCTURE

```
ar tianyi/
├── AGENTS.md                       # 本文件（项目知识库）
├── docs/
│   ├── PRD.md                      # 核心方案文档（技术路线 / MVP / 备选）
│   └── PLAN.md                     # 实施计划（Phase 0-4）
├── tools/                          # generate_qr_marker.py（定位卡生成）等
├── LuoTianyiAR/                    # Unity 工程（6000.3.22f1 + URP + AR Foundation 6.3.5）
│   ├── Assets/
│   │   ├── AR/Markers/             # 二维码定位卡图 + MarkerLibrary 资产
│   │   ├── Editor/                 # ARSceneSetup.cs、AndroidBuild.cs
│   │   ├── Scenes/ARScene.unity    # 主场景
│   │   ├── Scripts/                # 运行时脚本（见 KEY SCRIPTS）
│   │   ├── Settings/               # URP 资产（LuoTianyiURPAsset / Renderer）
│   │   ├── Shaders/ Textures/      # 自动和声化影子 shader / shadow mask
│   │   ├── XR/                     # ARCore / Simulation Loader 配置
│   │   └── Live2D/                 # Cubism SDK 5-r.5 + 模型 prefab
│   ├── Packages/                   # 依赖清单（AR Foundation / ARCore / XR Management）
│   └── ProjectSettings/
└── live2d/                         # 洛天依模型资产（原始语源）
    ├── backgrounds/                # 2 张背景图（bg1.jpg / bg2.jpg）
    └── models/luo/                 # Cubism 3 模型（model.model3.json 等）
```

## KEY SCRIPTS（LuoTianyiAR/Assets/Scripts/）

| 文件 | 职责 |
|------|------|
| `PlaceOnPlane.cs` | 放置/拖动/缩放 + Pose Root + 腿 Drawable 脚底对齐 + 异步 Anchor + 定位卡首放 + 位置锁定/微调 API + 诊断采样 |
| `PlacementGuideUI.cs` | 准星/点击反馈/状态提示（OnGUI） |
| `RuntimeDebugPanel.cs` | 真机调试面板 + 一键复制诊断报告（含 XR Origin/定位卡/遮挡状态） |
| `LuoMovement.cs` | 移动能力（Phase 2）：点击走路 + 状态机 + 贴地 raycast + 平面边界限制，由 PlaceOnPlane 挂载 |
| `LuoMotionAnimation.cs` | 程序化动画（Phase 2）：呼吸/头部微摆/走路律动；眨眼/表情/物理由 SDK 组件负责 |
| `Live2DModelFeatures.cs` | Cubism SDK Framework 接线：HarmonicMotion 呼吸、EyeBlink、表情、物理、视线追踪 |
| `CubismScreenLookController.cs` | 视线追踪：Additive 驱动眼/头/身体参数看向屏幕点 |
| `CylindricalBillboard.cs` | 完整 3D 相机朝向（正面朝 -Z，跟随俯仰、抗横滚，支持行走时暂停） |
| `OcclusionController.cs` | AR 遮挡与设备能力降级（仅明确 Supported 才启用）+ 诊断暴露 |
| `ARMarkerDiagnostics.cs` | 二维码定位卡追踪、位置 ground truth 与诊断快照 |
| `ModelNudgeUI.cs` / `PositionLockUI.cs` / `ExpressionCycleUI.cs` | 手动纠偏按钮 / 位置锁定 / 表情切换 |
| `ParameterDebugWindow.cs` | 编辑器参数探测窗口（枚举 Cubism 参数找 BodyPart） |

## ASSET MAP（live2d/models/luo/）

| 文件 | 引用方 | 角色 |
|------|--------|------|
| `Moc.moc3` | model.model3.json | Cubism 3 模型本体 |
| `Textures_.png` | model.model3.json | 唯一纹理 |
| `Physics.json` | model.model3.json | 物理参数 |
| `model.model3.json` | Cubism SDK | 标准运行时描述，11 表情 + 7 动作 |
| `model.json` | Web 引擎 | 旧格式副本，勿用 |
| `luo.cdi3.json` | Cubism Editor | 编辑器源文件，SDK 忽略 |

## CONVENTIONS

- 描述文件以 `model.model3.json` 为准；`model.json`（Type 0）与 `*_copy.json` 是历史残留。
- Motions/Expressions 文件名含中文，已由 model3.json 正确引用；工具链注意 UTF-8 编码。
- 模型含 15 个 HitAreas（辫子/左右腿/袖/头发/头/身体/裙子/左右手等）。
- Cubism 正面是本地 `-Z`；billboard 为完整 3D LookAt：`-transform.forward` 指向相机，
  本地 Y 取世界竖直在模型平面上的投影（跟随俯仰、不随横滚侧倒）——真机发现仅绕 Y 轴
  俯视桌面时会看到斜面/背面，已由 PRD 记录并取代旧的"仅水平投影"约定。
- 层级约定：Anchor -> "LuoTianyi Placement Root"（世界位姿=命中点，billboard 挂此）-> Live2D 模型
  （脚底由腿 Drawable 校正，根节点保持与 Anchor/定位卡对齐）。
- 不得回退的既有修复以 PRD/PLAN 记录为准：URP 双 Render Feature、运行时 Mesh、异步 Pose Anchor、
  XR Origin Device/0m、ARCore Required / Depth Optional、腿 Drawable 脚底对齐等。

## ANTI-PATTERNS（本项目）

- **`excited.exp3.json`** 存在于磁盘但未被 model3.json 引用 —— 需要时手动加入 Expressions 数组。
- **`model.model3_copy.json`** 是重复副本，不要作为运行时入口。
- **不要把洛天依模型数据喂给生成式大模型**（PRD.md 明确约束）。
- **没有真机数据时，不要宣称位置问题"已修复"**——当前修复（XR Origin/异步 Anchor）待真机复验。
- **`LuoMotionAnimation` 不得写 ParamEyeLOpen/ROpen**——眨眼由 Live2DModelFeatures 的 SDK
  EyeBlink 组件负责，两边同写会每帧互相覆盖。

## COMMANDS

- 构建 APK：Unity 6000.3.22f1 命令行执行 `AndroidBuild.Build`，输出 `LuoTianyiAR/Builds/LuoTianyiAR.apk`。
- 定位卡生成：`tools/generate_qr_marker.py`（依赖见 `tools/requirements-marker.txt`），说明见 `docs/二维码定位卡使用说明.md`。
- 真机日志/截图：ADB 命令见 docs/PRD.md 附录。
- 本机 Unity 路径：`C:\Program Files\Unity 6000.3.22f1\Editor\Unity.exe`。

## NOTES

- **PRD 核心结论**：L2D 当作"3D 世界中的 2D 物体"，AR 世界坐标 + 透视相机解决尺度（需求 b），平面检测 + raycast + anchor 解决站立（需求 a），Depth API occlusion 解决遮挡（需求 c）。推荐 Unity + AR Foundation + Cubism SDK，MVP 先做 a+b。需求优先级：a > b > c。
- **当前进展**：MVP 真机已跑通；Phase 2 移动/程序化动画、Phase 3 遮挡诊断、二维码定位卡、
  视线追踪/表情/手动纠偏均已入库。位置偏移根因（XR Origin Camera Y Offset + Anchor 首帧依赖）
  已修复，待真机复验后关闭。
- 6 个 motion 文件名乱码问题已排除——磁盘文件名实为正确中文，乱码是控制台代码页显示问题。

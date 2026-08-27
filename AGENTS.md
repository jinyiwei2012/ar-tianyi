# 项目知识库

**生成时间:** 2026-08-27
**仓库状态:** git 仓库；含 Unity AR 工程源码 + Live2D 资产 + 方案/交接文档

## OVERVIEW

洛天依（Luo Tianyi）AR 项目：把 Live2D 角色实时放入真实环境（Unity + AR Foundation + ARCore）。
`docs/PRD.md` 是技术方案文档；`LuoTianyiAR/` 是可运行的 Unity 工程（6000.3.22f1），
已跑通"模型显示 + 相机读取 + 放置 UI"，当前正在诊断"模型不在准星位置"的偏移问题。

## GIT

- 分支：`debut`（当前，含交接文档 `HANDOFF_AR_PLACEMENT.md`）、`main`、`release/v1.0.0`
- 远程：`origin` = https://github.com/jinyiwei2012/ar-tianyi.git
- 维护约定：修复/功能开独立分支，不要直接大改 `main`（见 HANDOFF 第 12 节）

## STRUCTURE

```
ar tianyi/
├── AGENTS.md                       # 本文件（项目知识库）
├── HANDOFF_AR_PLACEMENT.md         # 位置偏移问题交接（debut 分支，诊断唯一入口）
├── docs/
│   ├── PRD.md                      # 核心方案文档（技术路线 / MVP / 备选）
│   └── PLAN.md                     # 实施计划（Phase 0-4）
├── LuoTianyiAR/                    # Unity 工程（6000.3.22f1 + URP + AR Foundation 6.3.5）
│   ├── Assets/
│   │   ├── Editor/                 # ARSceneSetup.cs、AndroidBuild.cs
│   │   ├── Scenes/ARScene.unity    # 主场景
│   │   ├── Scripts/                # 运行时脚本（见 KEY SCRIPTS）
│   │   ├── Settings/               # URP 资产（LuoTianyiURPAsset / Renderer）
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
| `PlaceOnPlane.cs` | 放置/拖动/缩放 + Placement Root + footCenter 完整 XYZ 对齐 + 诊断采样 |
| `PlacementGuideUI.cs` | 准星/点击反馈/状态提示（OnGUI） |
| `RuntimeDebugPanel.cs` | 真机调试面板 + 一键复制诊断报告 |
| `CylindricalBillboard.cs` | 绕世界 Y 轴面向相机（正面朝 -Z，支持行走时暂停） |
| `OcclusionController.cs` | AR 遮挡与设备能力降级 |
| `LuoMovement.cs` | 移动能力（Phase 2）：点击走路 + 状态机 + 贴地 raycast，由 PlaceOnPlane 挂载 |

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
- Cubism 正面是本地 `-Z`；billboard 必须保证 `-transform.forward` 指向相机水平投影。
- 不得回退的既有修复见 HANDOFF 第 10 节（URP 双 Render Feature、运行时 Mesh、0.6m 缩放、ARCore Required / Depth Optional 等）。

## ANTI-PATTERNS（本项目）

- **`excited.exp3.json`** 存在于磁盘但未被 model3.json 引用 —— 需要时手动加入 Expressions 数组。
- **`model.model3_copy.json`** 是重复副本，不要作为运行时入口。
- **不要把洛天依模型数据喂给生成式大模型**（PRD.md 明确约束）。
- **没有真机数据时，不要宣称位置问题"已修复"**（HANDOFF 第 11 节验收标准）。

## COMMANDS

- 构建 APK：Unity 6000.3.22f1 命令行执行 `AndroidBuild.Build`，输出 `LuoTianyiAR/Builds/LuoTianyiAR.apk`（详见 HANDOFF 第 9 节）。
- 真机日志/截图：ADB 命令见 HANDOFF 第 9 节。
- 本机 Unity 路径：`C:\Program Files\Unity 6000.3.22f1\Editor\Unity.exe`。

## NOTES

- **PRD 核心结论**：L2D 当作"3D 世界中的 2D 物体"，AR 世界坐标 + 透视相机解决尺度（需求 b），平面检测 + raycast + anchor 解决站立（需求 a），Depth API occlusion 解决遮挡（需求 c）。推荐 Unity + AR Foundation + Cubism SDK，MVP 先做 a+b。需求优先级：a > b > c。
- **当前进展**：MVP 真机已跑通（模型显示/相机读取/放置 UI/平面识别/Anchor）。"模型不在准星位置"偏移问题处于诊断阶段，入口与判定表见 `HANDOFF_AR_PLACEMENT.md`。
- 6 个 motion 文件名乱码问题已排除——磁盘文件名实为正确中文，乱码是控制台代码页显示问题。

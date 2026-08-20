# 项目知识库

**生成时间:** 2026-08-20
**仓库状态:** 非 git 仓库；无源码，纯资产 + 方案文档项目

## OVERVIEW

洛天依（Luo Tianyi）Live2D 模型资产库 + AR 落地可行性调研。`PRD.md` 是从零调研"把 L2D 角色实时放入真实环境"的技术方案（Unity + Live2D Cubism SDK + AR Foundation 路线）。当前阶段无任何实现代码。

## STRUCTURE

```
ar tianyi/
├── PRD.md                          # 核心方案文档（中文，1197 行）
└── live2d/
    ├── backgrounds/                # 2 张背景图（bg1.jpg 2.7MB, bg2.jpg 0.3MB）
    └── models/
        └── luo/                    # 洛天依 Cubism 3 模型
            ├── model.model3.json   # 主描述文件（运行时入口）
            ├── model.model3_copy.json  # 冗余副本，勿用
            ├── model.json          # 旧版描述（"Type": 0），Web 引擎用
            ├── luo.cdi3.json       # Cubism Editor 工程数据，运行时无关
            ├── Moc.moc3            # 模型二进制（1MB）
            ├── Textures_.png       # 纹理（4.4MB）
            ├── Physics.json        # 物理参数（54KB）
            ├── expressions/        # 12 个表情（.exp3.json）
            └── motions/            # 7 个动作（.json）
```

## WHERE TO LOOK

| 任务 | 位置 | 说明 |
|------|------|------|
| 技术路线/架构决策 | `PRD.md` | AR 可行性分析、MVP 顺序、备选方案 |
| 模型运行时引用 | `live2d/models/luo/model.model3.json` | Descriptor：Moc/Textures/Physics/Expressions/Motions/HitAreas |
| 表情包 | `live2d/models/luo/expressions/` | 12 个文件，模型只引用 11 个 |
| 动作包 | `live2d/models/luo/motions/` | 7 个动作，文件名含中文（Tap部位/倾听点头） |
| 背景素材 | `live2d/backgrounds/` | 2 张 jpg |

## ASSET MAP（live2d/models/luo/）

| 文件 | 引用方 | 角色 |
|------|--------|------|
| `Moc.moc3` | model.model3.json | Cubism 3 模型本体 |
| `Textures_.png` | model.model3.json | 唯一纹理 |
| `Physics.json` | model.model3.json | 物理参数（含 PhysicsV2 声明） |
| `model.model3.json` | Cubism SDK | 标准运行时描述，11 表情 + 7 动作 |
| `model.json` | Web 引擎 | 旧格式副本，与 model3.json 内容近似 |
| `luo.cdi3.json` | Cubism Editor | 编辑器源文件，SDK 忽略 |

## CONVENTIONS

- **描述文件以 `model.model3.json` 为准**；同名 `model.json`（Type 0）与 `*_copy.json` 是历史残留。
- Motions/Expressions 文件名**含中文**（如 `Motions_Tap辫子_0.json`、`Motions_倾听点头.json`），已由 model3.json 正确引用；工具链注意 UTF-8 编码，避免重命名/传输时破坏。
- 模型是**洛天依官方风格 L2D**，含 15 个 HitAreas（辫子/左腿/袖/头发1-3/8/头/身体/裙子/右腿/左手/右手1-3）。

## ANTI-PATTERNS（本项目）

- **`excited.exp3.json` 存在于磁盘但未被 model3.json 引用** —— 需要时需手动加入 Expressions 数组。
- **`model.model3_copy.json` 是重复副本**，不要作为运行时入口。
- 不要把洛天依模型数据喂给生成式大模型（PRD.md 明确约束）。
- 不要基于此 repo 猜测实现代码 —— 目前没有代码，技术选型一律以 PRD.md 为准。

## COMMANDS

无构建/测试命令。纯资产目录，无 package.json / 工程文件。

## NOTES

- **PRD.md 核心结论**：L2D 当作"3D 世界中的 2D 物体"，用 AR 世界坐标 + 透视相机解决尺度（需求 b），平面检测 + raycast + anchor 解决站立（需求 a），Depth API occlusion 解决遮挡（需求 c）。推荐 Unity + AR Foundation + Cubism SDK，MVP 先做 a+b。
- 需求优先级：a（真实平面）> b（跨帧尺度固定）> c（遮挡）。
- 6 个 motion 文件名误显示乱码问题已排除 —— 磁盘文件名实为正确中文，此前乱码是控制台代码页显示问题。若工具读到乱码，检查读取编码而非文件本身。
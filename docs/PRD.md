# 洛天依相机功能
## 需求说明
使用Unity制作一个安卓APP，调用手机相机，使用AR功能，实现将洛天依live2d模型放置在现实环境中，要求：
1. 洛天依站立的平面是现实中存在的平面；
2. 洛天依和环境的大小关系在帧之间固定，镜头拉远/洛天依走远都会使模型变小；
3. 洛天依和环境的遮挡关系正确。

用户经典操作路径：
1. 唤出洛天依模型，拖动到现实中某个平面上(P0)；
2. 用户绕着洛天依走动，观察模型大小变化(P0)；
3. 拖动到不同的平面上(P0)；
4. 双指缩放模型大小(P0)；
5. 解除模型与平面的绑定，洛天依漂浮在空中(P1)。
6. 配置洛天依与环境中物体的遮挡关系(P1)。
7. 唤出相机按钮，拍摄并保存照片(P2)。
8. 进入相册界面查看相片(P2)。
9. 通过用户手动添加光源，模糊边界等方式，为已经拍摄的照片中的洛天依添加光影效果(P2)。

## 实现状态（2026-08-26）

### 已落地并通过本机构建验证

- Unity 6.0 + AR Foundation 6.3.5 + ARCore 6.3.5 + Live2D Cubism SDK 工程可编译并生成 ARM64 APK。
- Android XR 设置会由构建脚本幂等创建；ARCore Loader 自动加载/启动，构建失败时不再产出“非 AR 的成功 APK”。
- P0 平面链路已接通：仅检测水平平面，屏幕触摸 raycast 到真实平面，并创建附着于该平面的 ARAnchor。
- 模型首次按 0.60 米世界高度放置，合并全部 Cubism ArtMesh 的包围盒计算身高；透视相机负责随距离变化显示大小。
- 支持单指拖动到其他已检测平面、双指在 0.20–1.50 米之间调整世界高度；模型按 Cubism 正面本地 `-Z` 约定，仅绕世界竖直轴持续朝向相机，移动和 Anchor 更新均不得暴露背面。
- Depth 配置为 Optional；支持环境深度时请求遮挡，不支持时运行时降级，不限制普通 ARCore 设备安装。

### 必须在 ARCore 真机完成的验收

- P0：地板/桌面检测、跨平面拖放、Anchor 跨帧稳定、绕角色走动时 0.60 米尺度一致性、双指缩放后尺度保持。
- P1：分别在支持与不支持 Depth 的设备验证真实物体遮挡和降级；确认 Cubism URP 自定义渲染是否参与环境深度测试。
- P2：相机按钮、照片保存/权限、相册浏览和拍后光影编辑尚未实现。

## 原始预研对话
我有一个需求：把洛天依的l2d模型实时放到环境中，要求：a）优先级最高：所站立的平面是实际上存在的平面；b）优先级其次：洛天依和环境的大小关系在帧之间固定，镜头拉远/洛天依走远都会使模型变小。c）优先级最低：洛天依和环境的遮挡关系正确。需求a）我知道目前ar可以把3d模型放到环境中，对l2d模型不一定适合（但我能接受始终面向或基本面向用户）。需求bc我不知道有什么办法。我们的ai的使用比较宽松，除了不能把洛天依的模型输入到生成式大模型中，其他技术都可以。请你调研，分析需求的可行性与可能的技术路线

可以，而且从你的优先级来看，我认为**没有必要自己从头做视觉定位/深度估计**。最合适的思路是：

> **把 Live2D 模型当作 AR 世界中的一个“2D 虚拟物体”，而不是屏幕上的 Overlay。**
>
> ARCore / ARKit 负责真实世界坐标系、平面、相机位姿和深度；Live2D 只负责人物自身的动画与绘制。

这样 a、b 可以做到相当可靠，c 在支持 Depth API 的手机上也有成熟方案。ARCore 本身就提供运动跟踪、平面检测、Anchor 和 Depth；Unity AR Foundation 又把 ARCore/ARKit 抽象成了一套跨平台接口。

我会把可行性评价成：

| 需求 | 可行性 | 推荐技术 |
|---|---:|---|
| a. 真正站在现实平面上 | **很高** | AR plane detection + raycast + anchor |
| b. 与环境尺度关系跨帧固定 | **很高** | AR 世界坐标 + 透视相机 |
| c. 与现实物体正确遮挡 | **中高** | AR Depth / LiDAR + depth test |
| L2D 始终/基本朝向用户 | **很高** | cylindrical billboard |
| 在地面上走动 | **高** | 世界坐标移动 + downward raycast |
| 上楼梯/复杂地形 | **中等** | depth/scene mesh，而非单纯 plane |

下面分别展开。

---

# 1. 最重要的观念：L2D 完全可以作为“3D 世界中的二维物体”

Live2D 并不要求模型一定是屏幕 UI。官方 Cubism SDK for Unity 本身就在 Unity 场景中渲染模型，并提供各种 Drawable、mesh、draw order 等机制。Live2D 官方也明确提供 Unity SDK。

因此完全可以建立：

```text
真实摄像头
    ↓
ARCore / ARKit
    ↓
AR Camera
    ↓
AR World
 ├── Floor Plane
 ├── Table Plane
 ├── Real-world Depth
 │
 └── LuoTianyiRoot
       └── Live2D Model
```

关键区别只是：

普通 L2D：

```text
Camera
  ↓
Screen
  ↓
Live2D Overlay
```

你需要的：

```text
Camera pose
     ↓
3D world coordinates
     ↓
Live2D plane at (x,y,z)
```

这一步一旦完成，**b 几乎就自动解决了。**

---

# 2. 需求 a：让洛天依站在“实际存在”的平面上

这是当前移动 AR 最成熟的功能之一。

ARCore 会持续检测现实中的平面，并把它们作为可跟踪对象；虚拟对象可以通过 Anchor 与这些 Trackable 保持稳定的空间关系。

Unity AR Foundation 对应：

```text
ARPlaneManager
      ↓
检测 floor / table / wall
      ↓
ARRaycastManager
      ↓
用户点击屏幕
      ↓
raycast 到真实平面
      ↓
ARAnchor
      ↓
LuoTianyiRoot
```

AR Foundation/ARCore 官方也把 `ARRaycastManager`、平面、相机背景和 Depth API 都作为现成 AR 能力暴露出来。

例如用户第一次放置洛天依：

```text
用户点击这里
        ↓
camera ray
        ↓
----------------------- 桌面
            X
```

获得：

\[
{}^WT_L
\]

即洛天依在 AR 世界坐标系里的 pose。

然后设置：

```text
LuoTianyi.position = hitPose.position
```

模型的 **feet/root pivot** 位于模型脚底。

于是：

```text
        洛天依
          │
         / \
---------●------------- floor
       anchor
```

### 我建议不要让 L2D 做完整 LookAt

最自然的方式是 **cylindrical billboard**：

只绕世界竖直轴转向手机，而不是完整 3D LookAt。

即：

\[
\theta=\operatorname{atan2}(x_c-x_l,z_c-z_l)
\]

只设置：

```text
rotation.y = theta
```

而不修改 pitch / roll。

结果是：

```text
用户在左边      用户正面       用户右边

   ↖               ↑              ↗
    洛              洛              洛
```

人物永远保持竖直，同时大体面对用户。

这实际上非常适合 Live2D：**你不需要解决一个二维人物从侧面看变成纸片的问题。**

---

# 3. 如果洛天依要“走路”，a 仍然可以成立

最简单的情况是一个大平面：

```text
------------------------- floor

   洛 → → → →
```

那么直接沿平面移动：

\[
p_{t+1}=p_t+v\Delta t
\]

即可。

但实际环境可能是：

```text
                  ________
                 |
        _________|
       |
_______|
```

例如台阶。

此时我建议：

```text
目标 horizontal position
          ↓
      downward raycast
          ↓
     floor/mesh intersection
          ↓
      更新脚底高度
```

也就是每隔若干帧从人物脚底附近向下做 AR raycast。

第一版甚至**完全不用支持台阶**。

只允许人物在当前 `ARPlane` polygon 内活动，就已经可以得到很可靠的效果：

```text
   detected floor polygon

   +------------------+
   |                  |
   |       洛 →       |
   |                  |
   +------------------+
```

这对你的项目而言可能是更合理的 MVP。

---

# 4. 需求 b：其实 AR 天然解决

这是三个需求里容易被误认为困难、实际上最简单的一个。

你要求：

> 洛天依和环境的大小关系在帧之间固定。

其本质是要求存在一个**metric 3D coordinate system**：

例如定义：

```text
洛天依身高 = 0.60 m
```

然后模型就在 AR 世界中高 0.60 m。

如果人物距离相机：

```text
1 m
2 m
4 m
```

那么普通透视投影自然产生：

\[
h_\text{pixel}\propto\frac{fH}{Z}
\]

其中：

- \(H\)：人物真实世界尺度，例如 0.60 m
- \(Z\)：相机到人物距离
- \(f\)：相机内参

因此：

```text
camera

洛          洛       洛
████       ██       █
1 m        2 m      4 m
```

这不是一个需要 AI 去估计的问题。

**只要洛天依存在于 AR 世界坐标中，使用 AR Camera 的透视矩阵绘制，模型大小自然正确变化。**

ARKit本身就是通过 world tracking 把 2D/3D 虚拟内容放进现实世界，ARCore同样维护相机与 Trackable 的 pose。

---

# 5. “洛天依走远模型变小”也自动成立

例如：

```text
Luo position:
(0,0,1 m)

→

(0,0,3 m)
```

你不要写：

```text
screenScale = something
```

而是直接：

```text
transform.position += worldVelocity * dt;
```

例如：

```text
0.5 m/s
```

AR Camera 自己完成：

\[
3D \rightarrow 2D
\]

投影。

于是她走远以后自然变小。

这也是为什么我会强烈建议：

> **不要自己实现 b 的“视觉尺度估计”。**

一旦使用 AR tracking，你自己做：

```text
检测桌子
→ 估计深度
→ 算人物像素高度
→ 手工 scale Live2D
```

反而是在重复造 ARCore 已经解决的轮子。

---

# 6. 有一个很重要的细节：不要真的把 Live2D 当普通 Quad 拉伸

这里存在两种实现方式。

### 方案 A：Live2D 模型本身存在 Unity 3D scene 中

类似：

```text
GameObject
└── Live2D Cubism Model
```

然后整个 GameObject：

```text
position = AR world position
rotation = billboard rotation
scale = physical scale
```

这是最直接的方法。

Live2D SDK for Unity 本身支持在 Unity 中管理和渲染模型。

不过有一个值得提前注意的问题：当前 Cubism Unity SDK 的新 URP 渲染路线使用 custom render pass；Live2D 官方明确指出新版渲染、透明排序和传统 `MeshRenderer.sortingOrder` 之间存在区别。

这会影响后面的 **现实遮挡**。

---

# 7. 需求 c：遮挡，理论非常直接

假设场景是：

```text
camera
   ↓

   椅子       洛天依
    █           洛
    █          / \
```

如果椅子比洛天依距离 camera 更近：

\[
D_\text{real}(u,v)
<
D_\text{luo}(u,v)
\]

那么该像素应该绘制现实环境。

反之：

\[
D_\text{real}(u,v)
>
D_\text{luo}(u,v)
\]

则绘制洛天依。

也就是经典：

```text
if realDepth < virtualDepth:
    real world
else:
    virtual Luo
```

---

# 8. ARCore 已经直接提供这件事

Google 的 Depth API 会生成现实环境的逐像素 depth map，可以专门用于让现实物体正确遮挡虚拟内容；官方把 **object occlusion** 列为核心用途。

AR Foundation 中则由：

```text
AROcclusionManager
```

负责取得 environment depth / stencil 等每帧数据。

Unity 官方对它的描述正是：

> 让真实环境中的对象遮挡位于其后的虚拟内容。

其实现本质是比较物理环境 depth 和 Unity scene depth。

因此对于普通 3D 模型：

```text
AR Camera
   ↓
AR camera background
   ↓
environment depth
   ↓
virtual geometry
   ↓
depth test
```

几乎现成。

---

# 9. 但是 Live2D 在 c 上有一个真正的工程坑

普通 3D mesh 很简单：

```text
cube fragment:
z = 2.1 m
```

Live2D 通常包含大量：

```text
transparent texture
mask
draw order
blend
```

而且 Cubism 新版 Unity SDK 使用 custom render pass。

所以我不会预期：

> “把 AROcclusionManager 打开，Live2D 一定立刻无修改工作。”

这是需要做一次验证的地方。

---

# 10. 我反而推荐一种非常稳的 c 实现

把 Live2D **先渲染到 RenderTexture**：

```text
Live2D
   ↓
RenderTexture RGBA
   ↓
transparent quad
   ↓
AR world
```

也就是：

```text
          Unity world

        ┌──────────┐
        │ Live2D   │
        │ texture  │
        └──────────┘
           Quad
```

这个 Quad 是普通 Unity 3D geometry。

所以可以非常容易地：

```text
AR environment depth
        ↕
Quad depth
```

做遮挡。

Live2D 官方目前的新版绘制方案本身甚至已经存在“Drawables → RenderTexture → Plane”的相关绘制路径，因此这个思路和 Cubism 当前渲染架构并不违和。

### 代价

模型整个身体近似位于同一个深度平面：

```text
      head
       │
 torso │    ← same z
       │
      feet
```

但是——

**Live2D 本来就是一个二维角色。**

所以我认为这不仅可以接受，反而非常符合你的视觉目标。

---

# 11. 遮挡最终会是什么效果

例如桌子在她前面：

```text
现实画面：

             洛
       ┌──────────┐
       │   桌子   │
───────┴──────────┴────
```

Depth：

```text
桌子：1.5 m

洛天依 quad：2.0 m
```

于是桌子的 pixels：

```text
1.5 < 2.0
```

覆盖洛天依。

最终：

```text
       洛
      /|
████████████ 桌子
```

人物下半身自然被挡住。

ARCore 官方 Depth 示例本身就演示了使用每帧 depth image 对 placed virtual assets 实现 occlusion。

---

# 12. Depth 不一定需要手机有 LiDAR

这点对你的 Android 项目非常重要。

ARCore Depth API **不要求必须存在 ToF/LiDAR**。Google 会使用 depth-from-motion 等方法估计深度；如果手机本身有支持的硬件深度传感器，则也会利用它。

所以支持的 Android 手机上：

```text
RGB Camera
   +
camera motion
   +
possibly ToF
      ↓
ARCore Depth
```

即可。

不过 Depth API 只支持一部分 ARCore 设备，因此你仍应做：

```text
if depth supported:
    c = ON
else:
    c = OFF
```

Google 官方也明确要求运行时检查相应支持，并指出 Depth 并非所有设备都可用。

考虑到你的排序：

> a > b > c

这正好非常适合 graceful degradation。

---

# 13. 于是我建议定义三个设备能力等级

### Level 1

```text
AR tracking
+
plane
+
anchor
```

实现：

**a + b**

已经是非常好的体验。

---

### Level 2

```text
AR tracking
+
plane
+
Depth API
```

实现：

**a + b + c**

---

### Level 3

LiDAR / 高质量 scene reconstruction：

```text
AR world
+
dense scene geometry
+
depth
```

这样甚至能处理：

```text
床
沙发
桌腿
台阶
复杂家具
```

Apple 在 LiDAR 设备上可以通过 scene reconstruction 把现实环境重建成 polygon mesh。

但我认为这完全不应该成为 MVP 的要求。

---

# 14. 还有一条你提到的“AI 宽松”路线，我认为反而不应该优先

你理论上可以做：

```text
camera RGB
 ↓
Depth Anything / Metric3D / ...
 ↓
depth map

camera RGB
 ↓
plane segmentation

IMU + image
 ↓
SLAM

全部融合
```

从而自己获得：

\[
T_{WC},D(u,v),\Pi
\]

再完成：

```text
placement
scale
occlusion
```

学术上是可行的。

但存在一个根本问题：

### 单目深度不天然提供可靠 metric scale

这意味着：

```text
模型预测：
桌子 depth=3.2

究竟是：
3.2 m
还是
1.6 m
```

需要额外标定/metric depth 模型/IMU/VIO 等解决。

移动 AR 的 metric depth 本身就是一个专门研究问题；相关研究也把 accurate metric depth 视作移动 AR 中实现 object placement 和 occlusion 的关键问题。

而 ARCore/ARKit 已经把：

```text
camera calibration
IMU
visual tracking
metric world
plane fitting
anchor stabilization
```

整个工程链替你完成。

所以 AI 应该作为：

> **AR 的补充**

而不是 AR 的替代。

---

# 15. AI 真正值得加在哪里

比如以后可以做：

```text
camera
 ↓
semantic segmentation
 ↓
floor / table / chair / bed
```

然后：

> “洛天依应该站在哪里？”

例如识别：

```text
地板 ✔
桌面 ✔
沙发 ×
墙壁 ×
电视 ×
```

再把语义 mask 和 AR plane/depth 对齐。

甚至：

```text
LLM/VLM：
“去坐到沙发旁边”
       ↓
scene semantic
       ↓
find sofa
       ↓
find adjacent floor
       ↓
AR navigation target
```

这与 Agent-LuoTianyi 的长期方向反而非常契合。

---

# 16. 对你现有技术栈，我会特别提醒一个问题

你现在 App 是：

```text
React
TypeScript
Expo
WebView
```

而这个功能的核心变成：

```text
camera
AR session
GPU renderer
Live2D
depth texture
```

这已经不是 WebView 擅长的工作。

我**不建议尝试在现有 HTML WebView 中硬实现 AR + L2D**。

更合理的是：

```text
React / Expo
│
├── Chat UI
├── Settings
├── normal pages
│
└── Native AR View
      ↓
    Unity
      ↓
 AR Foundation
      ↓
 ARCore / ARKit
      ↓
 Live2D Cubism SDK
```

或者把 AR 模式做成一个独立 Unity activity/view。

---

# 17. 我认为 Unity 是这个项目非常强的交集点

因为你同时需要：

```text
                 Unity
                /     \
               /       \
           Live2D       AR
             │           │
        Cubism SDK   AR Foundation
                         │
                  ┌──────┴──────┐
                ARCore        ARKit
```

Live2D 官方有 Unity SDK。

Unity AR Foundation 又提供 ARCore / ARKit 抽象，并直接支持 raycasting、camera background、Depth/occlusion 等功能。

因此从工程风险来看，这基本是一个相当漂亮的技术交汇点。

---

# 18. 推荐的最终架构

我会设计成：

```text
                    Agent Luo
                        │
              behavior / commands
                        │
                    Unity
                        │
           ┌────────────┴─────────────┐
           │                          │
      Character System            AR System
           │                          │
      Live2D Cubism              AR Foundation
           │                          │
    motion/expression       ┌─────────┴─────────┐
                            │                   │
                       World Tracking        Depth
                            │                   │
                    plane / anchors      occlusion
                            │                   │
                            └────────┬──────────┘
                                     │
                               Luo Root Pose
                                     │
                                Billboard
                                     │
                                  Live2D
```

其中 Agent 只输出高层动作：

```json
{
  "action": "walk",
  "target": [1.2, 0, 3.1]
}
```

或者：

```text
look_at_user
walk_to_anchor
sit
wave
idle
```

**不要让 Agent 管屏幕像素。**

Agent 生活在：

\[
\text{AR world coordinates}
\]

里。

这个设计对你未来“数字生命 → 机器人实体”的方向也非常有价值，因为它第一次让洛天依拥有真正意义上的：

```text
我在哪里？
用户在哪里？
地面在哪里？
桌子在哪里？
目标在哪里？
```

而不再只是：

```text
屏幕上人物在哪个像素？
```

这是我认为这个需求最有意义的地方。

---

# 19. 我建议的 MVP 顺序

### Phase 1：只实现 a+b

**暂时完全不要 c。**

```text
AR Foundation
+
ARPlaneManager
+
ARRaycastManager
+
ARAnchor
+
Live2D
+
yaw billboard
```

交互：

```text
扫描房间
 ↓
看到地面
 ↓
点击地面
 ↓
洛天依出现
 ↓
用户绕她走
 ↓
位置、大小稳定
```

如果这个做不好，c 完全没有意义。

---

### Phase 2：让她移动

建立：

```text
LuoRoot
position
velocity
orientation
currentPlane
```

实现：

```text
walk(x,z)
turn
lookAtUser
returnToAnchor
```

仍然只在一个 ARPlane 上移动。

---

### Phase 3：Depth occlusion

加入：

```text
AROcclusionManager
```

首先拿一个：

```text
普通 opaque cube
```

验证：

```text
现实桌子能不能遮住 cube
```

然后再换 Live2D。

如果 Live2D 的 Cubism custom render pass 与 AR environment depth 冲突，就采用：

```text
Live2D
→ RenderTexture
→ transparent depth-tested quad
```

这是我认为**最稳妥的工程 fallback**。

---

### Phase 4：scene understanding

最后才考虑：

```text
semantic segmentation
scene mesh
object detection
VLM
```

实现：

> “天依，站到桌子旁边。”

---

# 20. 最终我对三项指标的预期

如果以目前移动设备能力来看：

### a 平面一致性

我会给：

**8.5/10**

普通地板、桌面非常成熟。

主要失败场景：

```text
纯白墙/纯色地面
强反光
玻璃
极暗环境
快速运动
```

ARCore 自己也提醒现实环境复杂度会影响 tracking 和 occlusion。

---

### b 尺度一致性

**9/10**

因为这是 AR tracking 最核心的能力。

只要 tracking 没 lost：

```text
用户靠近
→ 人变大

用户后退
→ 人变小

洛天依走远
→ 人变小
```

都不需要你专门实现。

---

### c 遮挡

**6～8/10**

取决于设备。

有优质 depth/LiDAR：

**很好。**

只有视觉估计 depth：

```text
细桌腿
头发
椅背
快速运动物体
纹理困难区域
```

边缘可能抖动、穿帮。

Raw Depth 虽然可以提供更准确的几何深度，但覆盖未必是逐像素完整，并且有对应 confidence 数据。

不过鉴于你已经把 c 定为最低优先级，我认为完全可以接受。

---

## 所以我的最终推荐是

**不要把问题定义成「怎样让 Live2D 看起来像 AR」。**

把它定义成：

> **「在正常的 AR 3D 世界中，放一个永远绕竖直轴朝向用户的二维角色。」**

技术栈我会直接选：

```text
Unity
+ Live2D Cubism SDK for Unity
+ AR Foundation
+ ARCore / ARKit

MVP:
Plane detection
+ Raycast
+ Anchor
+ Cylindrical billboard

第二阶段:
world-space locomotion

第三阶段:
AR Occlusion Manager
+ Environment Depth
+ Live2D → RenderTexture → Quad（必要时）
```

这样你提出的 **a 和 b 几乎都属于成熟工程能力，而不是研究问题**；真正值得做实验验证的只有 **Live2D 渲染管线与 AR depth occlusion 的兼容性**。从投入产出比看，我认为这条路线非常值得做，而且第一版甚至不需要任何新的 AI 模型。

# 洛天依 AR 放置位置偏移问题交接

**交接日期：** 2026-08-26  
**接手目标：** 诊断并修复“AR 能识别桌面、Live2D 能生成，但模型总不在相机/准星指向位置”的问题。优先区分 AR 平面/射线定位错误与 Live2D 放置逻辑错误。用户建议引入二维码作为已知位置基准。

## 0. 交接文档生成后的最新进展

用户随后要求直接修复并重新构建。当前工作树已经实现以下修改（尚未提交）：

1. 首次点击不再使用 `finger.screenPosition` 放置，而是固定 raycast 屏幕中心准星；点击仅作为确认。
2. 已放置状态的 FingerDown 不再立即移动模型；手指移动超过 32 px 后才进入拖动，避免普通点击/微移重建 Anchor。
3. Anchor 下新增 `LuoTianyi Placement Root`，billboard 挂在该稳定根节点上。
4. Live2D prefab 作为 Pose Root 子节点，按全部 Cubism runtime Mesh 的世界 bounds 计算 `footCenter=(center.x,min.y,center.z)`，完整 XYZ 对齐 Pose Root/Anchor。
5. 诊断报告新增请求屏幕点、hit 重投影点/像素误差、Pose Root、footCenter 和脚底对齐误差。
6. UI 文案已统一为“将中心准星对准平面，准星变绿后点击屏幕确认”。
7. Unity Android Clean Build 已成功，构建日志为 `LuoTianyiAR/Logs/build-placement-fix.log`，APK 为 `LuoTianyiAR/Builds/LuoTianyiAR.apk`。

构建期间设备未连接，所以上述状态只能记为“代码修复 + 本地全量构建通过”，不能记为“ARCore 真机问题已确认修复”。接手者的第一任务应是安装该 APK，检查调试报告中的 `projection.error` 与 `alignment.error`，再决定是否仍需第 7 节二维码实验。

## 1. 仓库与版本快照

- 仓库根目录：`E:\ar-tianyi`
- Unity 工程：`E:\ar-tianyi\LuoTianyiAR`
- 当前本地分支：`debut`
- 当前提交：`b030c1d80e2836f764f17ece69edb3999b6617b1`
- 远程关系：
  - `origin`：用户自己的 Fork；`main` 当前为 `61f053f`
  - `upstream`：原始仓库；`main` 当前为 `b030c1d`
- `debut` 当前直接跟踪 `upstream/main`，并不是原始仓库中的独立功能分支。
- 创建本文档前工作树是 clean；本文档本身将成为新的未提交文件。

接手后不要继续直接提交到 `upstream/main`。建议先执行：

```powershell
git status --short --branch
git switch -c codex/ar-placement-diagnostics
git push -u upstream codex/ar-placement-diagnostics
```

项目固定版本：

- Unity `6000.3.22f1`
- AR Foundation `6.3.5`
- ARCore XR Plugin `6.3.5`
- XR Management `4.5.0`
- Input System `1.11.2`
- URP：`manifest.json` 请求 `17.2.0`，`packages-lock.json` 实际解析为 `17.3.0`
- Cubism SDK 5-r.5
- Android：IL2CPP、ARM64、OpenGLES3、min API 25、target API 34

权威需求与当前实现状态见 [docs/PRD.md](./docs/PRD.md)。不要重复重写原始技术调研。

注意：以下两份资料已明显过时，只能参考历史背景：

- [AGENTS.md](./AGENTS.md) 仍写着“非 git 仓库、无源码”，与当前事实不符。
- [docs/PLAN.md](./docs/PLAN.md) 仍把 AR 依赖安装列为未完成；实际依赖、场景、构建和真机运行均已完成。

## 2. 已验证事实

以下不是推测：

1. Android 应用可以启动并显示真实相机背景。
2. ARSession 曾报告 `SessionTracking / None`。
3. 真机曾检测到 5 个、后续 14 个水平平面。
4. 相机权限为 true，ARCameraBackground 与 URP 的 `ARBackgroundRendererFeature` 正常。
5. Anchor 曾报告 `Tracking`。
6. Cubism 模型现在可以成功初始化：188 个 CubismRenderer 均有运行时 Mesh；一次真机初始化约 33–43 ms。
7. 最新真机截图中模型可以正确显示正面；此前背面暴露问题已通过 cylindrical billboard 修复：Cubism 正面按本地 `-Z` 处理，每帧只绕世界 Y 轴面向 AR 相机。
8. 最新本地 APK：`LuoTianyiAR/Builds/LuoTianyiAR.apk`（该目录被 gitignore，不会随仓库交付）。
9. 最新 APK SHA256：`E6D4AAC1BE1563632A12E841B877090D0AEE8D12F01D180011B649CB80BB6676`。
10. 编写本文档时 `adb devices -l` 没有设备，无法继续采集本次“位置偏移”的定量日志。

这些事实只能说明 AR 跟踪、平面发现、Anchor 创建和模型渲染链路总体可运行，**不能证明屏幕射线命中位置或模型可见中心正确**。

## 3. 当前阻塞症状

用户观察：

- 桌面能被识别，准星会变绿。
- 点击后模型可以生成。
- 模型显示位置通常不在相机/屏幕中心准星指向的位置。
- 目前无法判断是：
  1. ARCore 平面/相机位姿/屏幕 raycast 有误；或
  2. raycast/anchor 正确，但 Live2D 根节点、枢轴、包围盒或重挂 Anchor 的逻辑造成偏移。

本轮没有拿到以下关键数据：触摸像素、中心准星像素、hit pose、anchor pose、模型 root pose、模型 bounds 的底部中心、这些世界点重新投影回屏幕后的像素误差。因此不要直接宣称 ARCore 有问题，也不要直接把二维码方案当作最终修复。

## 4. 真实放置调用链

核心文件：[PlaceOnPlane.cs](./LuoTianyiAR/Assets/Scripts/PlaceOnPlane.cs)

```text
EnhancedTouch FingerDown/FingerMove
  -> finger.screenPosition
  -> ARRaycastManager.Raycast(screenPosition, PlaneWithinPolygon)
  -> planeManager.GetPlane(hit.trackableId)
  -> anchorManager.AttachAnchor(plane, hit.pose)
  -> Instantiate(modelPrefab, anchor)
  -> model.localPosition = Vector3.zero
  -> 初始化 Cubism runtime mesh 并按 0.60m 缩放
  -> PlaceFeetOnAnchor()
```

相关位置：

- `OnFingerDown`：约 74–84 行
- `OnFingerMove`：约 87–96 行
- `TryPlaceAtScreenPosition`：约 105–172 行
- `AttachAnchor(plane, hit.pose)`：约 130 行
- 实例化及重挂 Anchor：约 144–160 行
- `PlaceFeetOnAnchor`：约 256–267 行
- `TryGetModelBounds`：约 269–296 行

场景链路见：

- [ARSceneSetup.cs](./LuoTianyiAR/Assets/Editor/ARSceneSetup.cs)
- [ARScene.unity](./LuoTianyiAR/Assets/Scenes/ARScene.unity)

当前场景的 XR Origin、Camera Offset、Main Camera 初始 transform 都是 identity/zero；XROrigin 的 `RequestedTrackingOriginMode=NotSpecified`，序列化 `CameraYOffset=1.1176`（Core Utils 默认坐姿高度）。这主要可能影响高度，但也应在诊断报告中记录运行时 `CurrentTrackingOriginMode` 与 Camera Offset，排除错误的 tracking-origin 配置。

## 5. 代码证据支持的竞争性假设

### H1：固定中心准星与真实触摸放置坐标不一致（高优先级，代码已确认）

[PlacementGuideUI.cs](./LuoTianyiAR/Assets/Scripts/PlacementGuideUI.cs) 每帧用屏幕中心做 raycast，只用于准星颜色：

```csharp
var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
canPlaceAtCenter = raycastManager.Raycast(center, ...);
```

但 [PlaceOnPlane.cs](./LuoTianyiAR/Assets/Scripts/PlaceOnPlane.cs) 实际用 `finger.screenPosition` 放置。

因此当前产品同时表达了两种互相冲突的交互契约：

- UI 暗示“模型放到中心准星指向处”；
- 代码实际是“模型放到手指点击处”。

如果用户点击屏幕任意区域以确认中心准星，模型自然不会出现在相机中心。这是无需怀疑 ARCore 就能解释症状的第一候选。

需要产品上二选一：

- **中心准星模式：** 点击屏幕仅作为确认，永远 raycast 屏幕中心；或
- **点选模式：** raycast 手指位置，移除/弱化固定中心准星，在点击处显示落点。

根据用户措辞“相机指向的位置”和现有固定准星，建议先实现中心准星模式作为诊断版本。

### H2：Live2D 可见包围盒没有在水平面内对齐 Anchor（高优先级，代码风险已确认）

`PlaceFeetOnAnchor()` 当前流程：

1. `spawnedModel.transform.localPosition = Vector3.zero`
2. 计算全部 Cubism runtime Mesh 的世界 bounds
3. 只沿 `Vector3.up` 抬升，使 `bounds.min.y == anchor.y`

它没有将模型 bounds 的水平中心（X/Z）或“脚底中心”对齐到 Anchor。若 Cubism prefab 根节点不在可见模型的脚底中心，即使 Anchor 精确位于点击射线上，角色也会在屏幕上横向偏离。

建议记录并比较：

```text
anchor.position
modelRoot.position
bounds.center
footCenter = (bounds.center.x, bounds.min.y, bounds.center.z)
```

不要只打印 localPosition；当前 `RuntimeDebugPanel` 还不足以直接判断这一项。

### H3：轻微手指移动会连续重建 Anchor（中高优先级，代码已确认）

FingerDown 创建 Anchor 后，任何 FingerMove 都会立即再次 raycast、创建新 Anchor 并删除旧 Anchor，没有拖动阈值。普通“点击”也可能产生微小 move 事件，使最终位置不是按下瞬间的位置。

诊断版本应先禁用 `OnFingerMove` 放置，或加入屏幕像素/持续时间阈值；确认单次 FingerDown 的 hit 是否稳定后再恢复拖动。

### H4：AR 平面估计或相机/显示矩阵存在偏差（未排除，但当前证据不足）

纯色、反光、低纹理或边缘区域会影响 ARCore 平面质量；但目前只知道平面存在和 Anchor Tracking，没有任何 hit 重投影误差或独立世界基准，不能判断 ARCore 是否给出了错误落点。

### H5：XROrigin tracking origin / Camera Y Offset 配置不合适（低至中优先级）

场景保留了 XROrigin 默认 `CameraYOffset=1.1176` 且 requested mode 为 NotSpecified。它更可能造成 Y 方向高度问题，而不是显著的左右偏移，但接手者应记录运行时模式和 Camera Offset 后再排除；不要无证据直接把 offset 改为 0 并声称修复。

## 6. 推荐的最短诊断实验（先于二维码）

先建立与 Live2D 完全独立的“命中点标记”。一次构建就应输出以下数据：

```text
input.screenPosition
screenCenter
Screen.width / Screen.height / safeArea
hit.pose.position / rotation
hit.trackableId / distance / hitType
plane.alignment / trackingState / center / extents
anchor.position / rotation / trackingState
modelRoot.position / rotation / scale
modelBounds.center / size / min / max
footCenter
Camera.WorldToScreenPoint(hit.pose.position)
Camera.WorldToScreenPoint(anchor.position)
Camera.WorldToScreenPoint(footCenter)
pixelError = projectedHit.xy - requestedScreenPoint
XROrigin transform / CurrentTrackingOriginMode / Camera Offset transform
```

同时在 `hit.pose.position` 放置一个与 Live2D 无父子关系的高可见小标记（例如 2–3 cm 的十字或球），并画一条从 Anchor 向上 10 cm 的线。标记必须使用普通 URP 材质，避免 Cubism 渲染路径干扰。

建议按以下固定步骤真机复现：

1. 禁用拖动重放置，只保留单次确认。
2. 手机静止 1 秒，中心准星对准桌面上一个可辨识物理点。
3. 用中心准星坐标 raycast 并冻结 hit/anchor。
4. 同时生成普通 primitive 标记和 Live2D。
5. 手机不动时截图并保存完整日志。
6. 缓慢左右移动观察 10 秒，记录 primitive、Live2D 和物理点的相对关系。

判定表：

| 观察 | 结论倾向 | 下一步 |
|---|---|---|
| primitive 正确位于准星物理点，Live2D 偏移 | Live2D pivot/bounds/父子 transform 问题 | 修正 footCenter 到 Anchor 的完整 XYZ 偏移 |
| primitive 与 Live2D 同时落在手指处而非中心准星处 | UI 交互契约问题 | 统一为中心准星或点选模式 |
| FingerDown 正确，抬手后位置跳变 | FingerMove 重建 Anchor | 加拖动阈值/手势状态机 |
| primitive 本身就与目标物理点稳定偏离 | raycast/显示矩阵/XROrigin/ARCore 问题 | 再进入二维码基准实验 |
| 初始正确、静止时 primitive 持续漂移 | tracking/anchor 稳定性问题 | 记录 tracking reason、平面 subsumption 和 anchor 更新 |
| hit 世界点重投影不接近请求像素 | 坐标、相机或更新时序异常 | 核对 Input System 坐标、显示旋转与采样帧 |

诊断阈值可先采用：同一帧 hit 重投影误差不超过屏幕短边 1%；超过即保存完整快照。该阈值只用于发现明显工程错误，不是最终 ARCore 精度承诺。

## 7. 二维码/参考图像定位实验

用户建议使用二维码进行平面定位，这个方向适合做第二层独立基准，但实现时要区分两件事：

- **扫码解码**只能得到字符串，不自动提供可靠 6DoF 世界位姿。
- 本实验需要的是**已知物理尺寸的平面图像跟踪**。

建议方案：

1. 打印一个高对比度、非重复、四周留白的二维码图案，记录实际边长（建议 10–15 cm）。内容本身不重要。
2. 将原始二维码图片加入 `XRReferenceImageLibrary`，填写准确 physical size。
3. 在 XR Origin 上添加 `ARTrackedImageManager`，使用该 reference library。
4. 当 `ARTrackedImage.trackingState == Tracking` 时，在图像中心生成独立 primitive 标记并记录 tracked image pose。
5. 把二维码平放在桌面；计算其屏幕中心，对同一屏幕像素执行 `ARRaycastManager.Raycast(... PlaneWithinPolygon)`。
6. 比较：
   - tracked image 中心与 plane raycast hit 的位置差；
   - 两者平面法向夹角；
   - 两者分别重投影到屏幕后的像素；
   - Live2D footCenter 与两种基准的距离。
7. 不要把模型纹理或 Live2D 资产发送给生成式模型；二维码图案可以独立生成或使用普通测试图。

解释：

- 图像标记正确、plane hit 错：优先检查平面/raycast/相机坐标链。
- 图像标记和 plane hit 一致、Live2D 错：确认是模型放置逻辑。
- 图像和 plane 都漂移：环境纹理、AR tracking 或 tracking-origin 问题。
- 如果只需要更强的定量姿态基准，AprilTag/ArUco 通常比“扫码库 + 自行估姿”更合适；但先用 AR Foundation 自带 `ARTrackedImageManager` 可减少新依赖。

本项目本地 AR Foundation 6.3.5 文档已包含 image tracking、`ARTrackedImageManager` 与 `XRReferenceImageLibrary`，无需升级包即可实现。

## 8. 诊断后可能的修复方向

只有在实验给出结论后再选：

### 若是中心准星契约

在 `OnFingerDown` 中将确认坐标替换为：

```csharp
new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
```

并确保 UI 文案明确“点击任意位置确认准星落点”。拖动则应进入独立模式，不能复用确认手势。

### 若是模型 pivot/bounds

使用缩放和 billboard 后的最终世界 bounds，计算脚底中心到 Anchor 的完整世界偏移，而不是只修正 Y。注意 yaw 改变后 bounds.center 也会改变，校正顺序应固定并加测试。

### 若是拖动手势

实现最小状态机：`Pressed -> PendingTap -> Dragging -> Released`，只有位移超过阈值才进入 Dragging；Pinch 开始时取消单指放置。

### 若是 AR/raycast

先用 primitive 和二维码数据定位到具体层：Input 坐标、相机显示旋转、XROrigin、plane provider 或 anchor 更新。避免绕过 AR Foundation 自己编写视觉定位，除非证据明确指向 provider 限制。

## 9. 构建、安装与日志

构建入口：[AndroidBuild.cs](./LuoTianyiAR/Assets/Editor/AndroidBuild.cs)，方法 `AndroidBuild.Build`。

当前开发机 Unity 路径：

```text
D:\Unity\Hub\Editor\6000.3.22f1\Editor\Unity.exe
```

当前开发机可用的完整构建工具链是 Unity 自带 AndroidPlayer。`D:\Android\Sdk` 可用于 ADB，但缺少 `cmdline-tools/latest`，直接交给 Unity 会报 SDK 无效。

```powershell
$project = 'E:\ar-tianyi\LuoTianyiAR'
$unity = 'D:\Unity\Hub\Editor\6000.3.22f1\Editor\Unity.exe'
$android = 'D:\Unity\Hub\Editor\6000.3.22f1\Editor\Data\PlaybackEngines\AndroidPlayer'

$env:UNITY_JDK_PATH = "$android\OpenJDK"
$env:ANDROID_SDK_ROOT = "$android\SDK"
$env:ANDROID_NDK_ROOT = "$android\NDK"

& $unity `
  -batchmode -quit -nographics `
  -projectPath $project `
  -executeMethod AndroidBuild.Build `
  -logFile "$project\Logs\build-handoff.log"
```

安装和启动：

```powershell
$adb = 'D:\Android\Sdk\platform-tools\adb.exe'
$apk = 'E:\ar-tianyi\LuoTianyiAR\Builds\LuoTianyiAR.apk'

& $adb devices -l
& $adb install -r $apk
& $adb shell am force-stop com.luotianyi.lab.ar
& $adb logcat -c
& $adb shell monkey -p com.luotianyi.lab.ar -c android.intent.category.LAUNCHER 1
```

过滤 Unity 日志：

```powershell
$appPid = (& $adb shell pidof com.luotianyi.lab.ar).Trim()
& $adb logcat --pid=$appPid -v threadtime 'Unity:V' 'AndroidRuntime:E' '*:S'
```

截图：

```powershell
& $adb exec-out screencap -p > "$project\Logs\device-current.png"
```

运行时应用右上角有“调试”按钮；[RuntimeDebugPanel.cs](./LuoTianyiAR/Assets/Scripts/RuntimeDebugPanel.cs) 可以复制诊断报告，当前报告包含 ARSession、平面数、相机、遮挡、Renderer Feature、模型 bounds、runtime mesh 数和 billboard `frontDot`。接手者应把第 6 节列出的 placement pose/reprojection 数据补进去。

## 10. 不得回退的既有修复

1. URP Renderer 必须同时启用：
   - `CubismRenderPassFeature`
   - `ARBackgroundRendererFeature`
2. Android 上 Cubism runtime mesh 应读取 `CubismRenderer.Mesh`，不能用 `MeshFilter.sharedMesh`；后者在 Cubism SDK 中仅为 Editor picking 服务。
3. Cubism 正面是本地 `-Z`；billboard 要保证 `-transform.forward` 指向相机的水平投影。
4. 模型首次缩放目标高度是 0.60 m，范围 0.20–1.50 m。
5. ARCore 为 Required，Depth 为 Optional；不能因设备不支持 Depth 阻止普通 AR 放置。
6. 模型数据不得发送给生成式大模型。

## 11. 验收标准

位置问题修复需至少满足：

- 中心准星模式或点选模式只有一种明确契约，文案和代码一致。
- 独立 primitive 标记与请求屏幕点的同帧重投影误差小于屏幕短边 1%。
- Live2D 脚底中心与 Anchor/诊断 primitive 的世界距离在稳定后小于 2 cm（初始工程验收阈值，可依据设备噪声调整并记录理由）。
- 单次点击不会因微小 FingerMove 偷换 Anchor。
- 静止观察 10 秒没有肉眼可见跳变；若有漂移，日志能区分 plane 更新、anchor 更新和 model transform 更新。
- 绕模型移动时始终显示正面，调试报告 `frontDot` 应接近 1。
- 相机背景、模型渲染、0.60 m 尺度与运行时降级均无回归。
- 真机验证与本地静态/构建验证分开报告；没有真机数据时不得称问题已修复。

## 12. 建议接手顺序

1. 新建原始仓库功能分支，避免继续直接改 `upstream/main`。
2. 阅读 `docs/PRD.md` 的实现状态与平面/Anchor/billboard章节。
3. 先实现第 6 节的 primitive + 完整 pose/reprojection 日志，临时禁用 FingerMove 重放置。
4. 真机完成一次中心准星固定实验。
5. 根据判定表决定是否需要二维码参考图像；不要先大规模重写 AR 链路。
6. 若 primitive 正确，修正 Live2D footCenter/pivot；若 primitive 错，再做第 7 节二维码实验。
7. 构建、ADB 覆盖安装、保存截图与过滤日志。
8. 将定量结果回填到 PRD 实现状态或新 issue；不要继续维护已过时的 PLAN 表述。

## 13. Suggested skills

新会话建议调用以下技能：

- `tdd`：先为屏幕坐标选择、拖动阈值、footCenter 对齐和 billboard 方向建立 EditMode 测试，再修改运行时代码。
- `codebase-design`：如果需要把 `ARRaycast/Anchor pose provider`、`placement policy` 和 `Live2D alignment` 分离成可测试模块，用该技能设计边界；不要为单一修复过度抽象。
- `code-review`：功能分支完成后，以 `b030c1d` 为基线检查 PRD 契约、Unity 工程规范和既有渲染修复是否回归。

## 14. 交接完成条件

接手者应能只依赖本仓库、本文档、一台 ARCore 真机和普通打印标记完成诊断。仍缺少且必须由实机补充的唯一关键证据，是“请求屏幕点 → hit/anchor → primitive/Live2D → 屏幕重投影”的同次事件数据。

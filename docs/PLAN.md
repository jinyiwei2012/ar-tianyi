# 洛天依 AR App 实施计划

**依据:** PRD.md（技术路线、MVP 顺序、备选方案）
**目标:** 把洛天依 Live2D 放入真实环境，最终打包为 Android App
**基线:** Unity 6000.3.22f1 + Cubism SDK 5-r.5 + URP 17.3.0（工程 `LuoTianyiAR/`）

---

## 当前状态快照（2026-08-27）

- **Phase 0 已完成**：依赖、场景、构建、真机运行均就绪（历史"未完成"表述已修正）。
- **Phase 1 主体完成**：ARScene 可运行，真机已验证模型显示、相机读取、放置 UI、平面识别与 Anchor。
- **进行中**：`HANDOFF_AR_PLACEMENT.md`（debut 分支）记录的"模型不在准星位置"偏移问题。已实现中心准星模式 / 拖动阈值 / Placement Root / footCenter 完整 XYZ 对齐，等待真机复测。
- **Phase 2 代码已实现（待真机验证）**：点击已放置模型 → 洛天依走向目标点（LuoMovement 状态机 + 贴地 raycast + 行走时暂停 billboard），拖动仍可瞬移接管；`feat/phase2-movement` 分支。
- **Phase 3 代码已实现（待真机验证）**：遮挡诊断（IsOcclusionEnabled/GetDiagnosticLine + 深度模式变化监控）+ 遮挡参照立方体（普通 URP 几何，对比 Cubism 是否参与 depth test）；`feat/phase3-occlusion` 分支。若真机确认 Cubism 未参与 depth test，再启用 RenderTexture fallback（PRD 第 10 节）。

---

## 需求回顾（PRD 优先级）

| 需求 | 优先级 | 技术方案（PRD） |
|------|--------|-----------------|
| a. 站在真实平面上 | **最高** | ARPlaneManager + ARRaycastManager + ARAnchor |
| b. 尺度跨帧固定 | 次高 | AR 世界坐标 + 透视相机（天然解决） |
| c. 与现实物体遮挡 | 最低 | AROcclusionManager + Environment Depth |
| 始终朝向用户 | 伴随 | cylindrical billboard（只绕 Y 轴） |

**核心观念:** L2D 作为"3D 世界中的 2D 物体"，不是屏幕 Overlay。

---

## Phase 0 — 工程基础（已完成）

- [x] Unity 6000.3.22f1 + URP 17.3.0 工程
- [x] Cubism SDK 5-r.5 导入（含 URP 渲染管线）
- [x] 洛天依模型导入并生成 `model.prefab`
- [x] **安装 AR 依赖包**
  - `com.unity.xr.arfoundation` 6.3.5
  - `com.unity.xr.arcore` 6.3.5（Android）
  - `com.unity.xr.management` 4.x（XR 管理）
  - 已配置 ARCore Loader + XR Simulation Loader（`Assets/XR/`）

## Phase 1 — 需求 a + b：放置到真实平面（MVP）

**目标:** 点击屏幕 → 洛天依站在真实地面上，尺度稳定。

任务分解：

1. **场景骨架**
   - 新建场景 `ARScene`
   - 创建 AR Session + AR Session Origin（含 AR Camera）
   - 添加 ARPlaneManager（检测地面）、ARRaycastManager（点击检测）
   - 场景挂 URP 全局设置（已有）

2. **放置逻辑** `PlaceOnPlane.cs`
   - 触摸检测（Input System）→ 屏幕坐标
   - `ARRaycastManager.Raycast()` → 命中 ARPlane
   - 实例化 `LuoTianyi model.prefab`，`position = hitPose.position`
   - **脚底对齐:** 模型 pivot 应位于脚底（Cubism 模型默认脚底锚点，验证时确认）

3. **Billboard 朝向** `CylindricalBillboard.cs`
   - 每帧 `rotation.y = atan2(camera.x - luo.x, camera.z - luo.z)`
   - 不修改 pitch/roll（保持竖直，避免纸片化）

4. **尺度验证**
   - 洛天依身高映射到 AR 世界 ≈ 0.6m（`transform.localScale` 校准一次）
   - 走近变大、走远变小（AR 透视相机自动处理，不需写代码）

5. **构建配置（Android）**
   - Player Settings → Android：
     - Minimum API Level ≥ 25（ARCore 插件要求）
     - Scripting Backend = IL2CPP，Target Architectures = ARM64
     - Graphics API = OpenGLES3（稳妥；Vulkan 需 API ≥ 29）
   - 包名：`com.<org>.luotianyi`

**验收:** 真机安装后扫描房间 → 点击地面 → 洛天依出现且站立稳定、围绕走动时大小/位置正确。

## Phase 2 — 移动能力

**目标:** 洛天依在平面内走动（PRD Phase 2）。

- `LuoRoot` 状态机：position / velocity / orientation / currentPlane
- 动作集：`walk(x,z)`、`turn`、`lookAtUser`、`returnToAnchor`
- 移动后 **downward raycast** 贴地保持脚底对齐
- 仅在当前 ARPlane 多边形内活动（MVP 不处理台阶）
- 接入 Cubism 动画：移动时播放 walk motion，静止回 idle

**验收:** 洛天依可在平面内走/转/看向用户，脚不悬空。

## Phase 3 — 需求 c：遮挡（可选，低优先）

**目标:** 真实物体能遮住洛天依（PRD 方案 10-12）。

1. 先验证：放置一个 **opaque cube**，确认 `AROcclusionManager` 启用后桌子能遮住 cube
2. 再换 Live2D：
   - 若 Cubism URP custom render pass 与 environment depth 冲突
   - → 采用 PRD 推荐 fallback：**Live2D → RenderTexture → transparent depth-tested quad**
3. 设备能力降级：`if depth supported: c=ON else: c=OFF`（ARCore Depth 支持约 66% 设备）

**验收:** 桌面/椅子靠近时正确遮挡洛天依身体，不穿帮。

## Phase 4 — 打包为 App（最终交付）

**目标:** 生成可安装的 Android APK。

1. **签名配置**
   - 生成 keystore：`keytool -genkeypair -alias luotianyi -keyalg RSA ...`
   - Player Settings → Publishing Settings → Keystore 配置

2. **打包**
   - Build Settings → Platform = Android → Build（或 Build App Bundle / APK）
   - 输出：`LuoTianyiAR/Builds/LuoTianyiAR.apk`
   - IL2CPP 首次构建较慢（10-30 分钟），后续增量

3. **真机安装测试**
   - 支持 ARCore 的手机（Android 8.0+，具备 Depth 更佳）
   - `adb install` 或拷贝 APK 安装
   - 验证：平面检测、放置、遮挡、帧率

4. **可选增强（PRD 16-18，非 MVP）**
   - 宿主 React/Expo App + Native AR View（Unity 嵌入）
   - Agent 高层指令接口（`{"action":"walk","target":[...]}`）
   - 语义分割 / VLM 场景理解（Phase 4）

---

## 风险清单（PRD 已预判）

| 风险 | 应对 |
|------|------|
| Cubism custom render pass 与 AR depth 冲突 | RenderTexture → quad fallback（PRD 第 10 节） |
| 手机不支持 Depth API | 运行时检测，c 优雅降级为关 |
| 纯色地面/强反光导致平面丢失 | 提示用户换场景；接受 a 的已知失败场景 |
| IL2CPP 构建慢 | 增量构建；必要时升级测试机内存 |

## 里程碑

| 里程碑 | 内容 | 预计工作量 |
|--------|------|-----------|
| M1 | Phase 0 完成（AR 依赖装好、工程能跑） | 0.5 天 |
| M2 | Phase 1 完成（真机可放置洛天依） | 1-2 天 |
| M3 | Phase 2 完成（可移动） | 1 天 |
| M4 | Phase 3 完成（遮挡，可选） | 1-2 天 |
| M5 | Phase 4 完成（APK 交付） | 0.5 天 |

---

## 下一步行动

1. 真机安装最新 APK，开启 `enablePlacementDiagnosticMarkers`，按 HANDOFF 第 6 节做中心准星固定实验，收集 pose/reprojection 数据。
2. 依据 HANDOFF 第 6 节判定表决定是否需要第 7 节二维码实验。
3. 修复确认后，将定量结果回填 PRD 实现状态；不要继续维护旧的"未完成"表述。
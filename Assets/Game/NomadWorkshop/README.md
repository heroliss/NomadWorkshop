# 《游牧工坊》技术 Spike

> 状态：**居民 Utility AI + 实时 3D + Humanoid / Blender 静态道具 + URP 3D Renderer 证据 v0.4**，更新于 2026-09-01。它仍是可删除的技术验证，不是 Foundation Prototype、垂直切片或正式美术基线。当前产品真值见 [`docs/nomad-workshop-game-vision.md`](../../../docs/nomad-workshop-game-vision.md)。

## 当前证明了什么

- 居民决策内核可以脱离 Unity 场景运行：按需求压力、工作价值、玩家优先级、个人倾向、等待时间与执行成本评分；
- 同意图目标先归并，紧急候选优先进入选择池，再在相对高分短名单中用确定性 Softmax 抽样；
- 同一世界 Seed、居民稳定 ID 与决策序号会重现相同随机值、候选分解和选择；
- 目标、材料与设施交互位可以全有或全无地预留，失败不会残留部分占用；
- Unity 展示层能把选中行动推进为“走到设施 → 执行 → 结算 → 再决策”，并显示中文诊断面板；
- Quaternius Universal Base Characters 的选定模型在 Unity 6000.3 中生成**有效 Humanoid Avatar**；
- Universal Animation Library 免费标准版的无 Root Motion FBX 导入出 43 个 30 FPS Human Motion；项目只抽取 Idle、Walk、Pickup、Fixing、Sitting 五个 `.anim`，并用稳定语义状态隔离上游 Clip 名；
- 运行时实际实例化共享 Humanoid、禁用 Root Motion、按模拟倍率播放动作；任一资产或状态契约失效时会回退程序假人；
- 六个灰盒设施都通过 `FacilityInteractionAnchor` 声明站位、朝向、动作语义和可选手部目标，已经覆盖站立取用、跪姿维修、拾取搬运与坐姿休息等三类以上接缝；
- 内嵌 FBX 材质被显式重映射为项目自有 URP Lit 材质，身体与眼睛法线贴图按 Normal Map 导入；
- Blender 5.2 的版本化 `bpy` 脚本可以生成一个 14 Mesh 的废土储物箱，并输出 `.blend`、FBX、预览和 manifest；
- Unity Editor 工具会核对来源哈希，配置静态 ModelImporter，把 manifest 的 Base Color / Metallic / Roughness 翻译为三个外部 URP/Lit 材质并显式 Remap，再生成 Root / Visual Identity 的 Prefab、一个 Bounds BoxCollider、预览场景和审计报告；
- 静态 FBX 的坐标轴已经实际烘焙并验证直立；784 个 Blender 源顶点导入后因硬边、法线与材质边界拆分为 3024 个 Runtime Vertex，三角形仍为 1512。
- 项目默认 `Renderer2D` 保持为 Universal RP Asset 的 index 0；游戏 Spike 从 URP 官方模板生成唯一的次级 `UniversalRendererData`（index 1），只有隔离预览相机显式选择它；
- 固定 3D Game View 已实际看到箱体体积、硬阴影、金属高光和青 / 橙 / 黑材质区分，没有粉材质、黑屏或错误姿态；自动审计仍把最终审美结论留给人工。

这些证据仍不能证明游戏好玩、正式画面达标、多人居民调度自然、IK 接触可靠或参数已经平衡。3D Renderer 只在一个可删除的棚拍场景中闭环了首个静态道具；正式车辆镜头、后处理、Shader / VFX、目标平台性能和统一艺术指导仍未成立。

## 目录与边界

```text
NomadWorkshop/
├── Simulation/       # 无 UnityEngine 依赖：候选、效用选择、轨迹与预留
├── Runtime/          # 可删除灰盒表现：行动执行、Humanoid Adapter、设施锚点与回退假人
├── Editor/           # 游戏本地 Importer、动作抽取、材质 / Renderer 配置和审计工具
├── Animation/        # 五个项目动作和稳定 Animator Controller
├── Materials/        # 项目自有 URP Lit 材质，不直接修改第三方内嵌材质
├── ThirdParty/       # 最小选入资产、原始许可证、来源、下载哈希与重建说明
├── Scenes/           # 只经 Unity Editor 保存的 Spike 场景
├── Spikes/           # 可删除的 Blender Import 与 URP 3D Rendering 证据，不是 Runtime Content
└── Tests/
    ├── EditMode/     # 决策规则、Avatar、动作、材质、Controller 与锚点契约
    └── PlayMode/     # 回退路径和真实 Humanoid 五状态实例化
```

`UtilityDecisionEngine` 只决定“现在做什么”；它不拥有寻路、任务进度、资源结算或动画。`ReservationLedger` 只处理单线程模拟中的原子占用。`ResidentHumanoidPresentation` 只呈现模拟结果，Animator 不拥有移动或任务完成真值。`FacilityInteractionAnchor` 只声明表现接缝，尚未驱动 IK。

这些程序集暂时不引用 SSFramework：当前风险是游戏专属 Utility AI、角色管线和 3D 可读性，不需要先用 Context 包装后再证明一次。进入 Foundation Prototype 时，游戏 Runtime 会作为独立业务程序集消费 Framework 的时钟、存档、UI、资源和诊断等公共接缝；只有出现跨游戏证据后，游戏专属 AI 或资产规则才考虑回流 Framework。

## 运行与观察

1. 用 Unity 打开 [`Scenes/UtilityAiSpike.unity`](Scenes/UtilityAiSpike.unity) 并进入 Play；
2. 左侧面板显示需求、动力损伤、积压、当前行动以及每个候选的效用分项、概率和排除原因；
3. “制造严重故障”用来观察紧急维修、跪姿动作和设施朝向，“提高 / 降低”用来观察玩家优先级；
4. “重置”恢复固定 Seed 和初始状态，便于重现同一决策序列；
5. 若场景中的模型或 Controller 引用被清空，标题下方会明确显示程序假人回退，不会假装 Humanoid 管线仍成立。

Editor 菜单 `Assets/SSFramework/游牧工坊/配置并审计 Humanoid 资产` 会重放角色导入、贴图类型、材质映射与最终产物审计。完整动作源默认不在仓库；重新抽取步骤与上游哈希见 [`ThirdParty/QuaterniusUniversalAnimationLibrary/SOURCE.md`](ThirdParty/QuaterniusUniversalAnimationLibrary/SOURCE.md)。

Editor 菜单 `Assets/SSFramework/游牧工坊/Blender Import Spike/配置并审计储物箱` 会重放 FBX 哈希校验、Importer、URP 材质 Remap、Prefab、Collider、预览场景和最终审计。重建步骤、已验证数值和删除边界见 [`Spikes/BlenderImport/NW_StorageCrate_01/README.md`](Spikes/BlenderImport/NW_StorageCrate_01/README.md)。完整原理见 [`docs/blender-art-pipeline.md`](../../../docs/blender-art-pipeline.md)。

Editor 菜单 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer` 会从当前 URP 包的官方模板幂等生成次级 3D Renderer、材质和隔离预览场景。它拒绝覆盖未知默认 Renderer，审计默认 index 与相机 index，并把报告写到被忽略的 `ArtPipelineOutput/`。重建与删除边界见 [`Spikes/Rendering/Urp3D/README.md`](Spikes/Rendering/Urp3D/README.md)。

## 第三方资产边界

- 角色与动作均来自 Quaternius 官方免费标准包，许可证为 CC0 1.0；
- 仓库保留一个约 0.83 MB 基准角色、实际使用贴图和五个抽取动作，不保留 129 MB 角色压缩包或 23.75 MB 完整动作源 FBX；
- 当前 Base Character 是穿基础内衣的中性身体基体，只验证 Avatar、比例、材质和重定向，**不是废土服装或正式角色美术**；
- 上游免费包的 Roughness 贴图未进入仓库，因为当前 URP Lit 材质没有可靠的通道打包流程，不为“资产齐全”保留未使用文件；
- 许可证、官方下载页、upload id、文件大小与 SHA-256 分别记录在两个 `SOURCE.md` 中；
- 储物箱是项目脚本生成的自有技术探针，不依赖第三方模型；可重建 `.blend` 和批量输出被忽略，只提交一个小型 FBX + manifest 作为跨机器 Unity 导入证据。

## 当前验证证据

- 编译：0 error / 0 warning；
- EditMode：`Game.NomadWorkshop.Simulation.Tests` + `Game.NomadWorkshop.Editor.Tests`，18/18；其中 Blender 静态道具导入与 URP 3D Renderer 契约各 2 项；
- PlayMode：`Game.NomadWorkshop.PlayMode.Tests`，2/2；既验证未配置资产时的假人回退，也实际实例化模型并依次进入五个 Animator 状态；
- Game View：实际检查过普通模拟、Idle 比例、紧急维修姿态、Blender 导入预览和独立 URP 3D 预览；截图属于临时证据，位于被 Git 忽略的 `Screenshots/`。默认 Renderer2D 下的 Import Preview 仍保持 `inconclusive`，次级 Universal Renderer 的固定镜头已人工确认体积、硬阴影、材质分区和高光成立。

测试重点覆盖危险口渴压过休闲、多需求行动按实际压力得分、同 Seed 重现、不同 Seed 只在短名单内变化、重复设施不放大意图概率、无正效用时安全等待、冲突预留不泄漏，以及 Avatar / Clip / 材质 / Controller / 锚点的导入契约。

## 明确未做

- 三名居民同时投标、路径拥堵、设施队列与任务中断恢复；
- 行动承诺和任务老化的长时间平衡统计；
- 正式废土服装、模块化发型/背包、人物差异、面部与布料；
- Animation Rigging、手部 IK、工具挂点实际消费与专用交互修正；
- 正式 NavMesh / 网格寻路、建造、库存、保存读取和 Framework Context 接线；
- 正式 UI、艺术指导、音效、性能采样、Player Build 与玩家体验验证；
- 项目默认 Renderer 仍是 `Renderer2D`；次级 3D Renderer 只证明隔离镜头可用，尚未决定正式游戏场景的 Renderer 组织、后处理、VFX、灯光风格和性能预算。

下一步不再扩张动作数量或继续装饰棚拍。用第二种 Blender 几何尝试把当前 14 个 Renderer 按材质合并到约 3 个，并比较 Draw Call、Runtime Vertex、遮挡、碰撞与可编辑性；第二轮仍能稳定复现后，才决定是否形成 `blender-asset-pipeline` Project Skill。随后以兼容 Humanoid 的废土服装和三名居民任务竞争推进 Foundation Prototype；手部 IK 只在真实设施接触误差证明有必要后加入。

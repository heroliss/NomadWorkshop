# 《游牧工坊》3D 资产族生产策略

> 状态：**研究决策 v0.2**，更新于 2026-09-03。本文定义“一个可用于游戏的资产族”应交付什么，以及参数化代理、概念图、AI Mesh、Blender、PBR、动画、状态表现、音效和 Unity 如何接缝。它不是指定永久供应商、要求所有资产走同一路径，或承诺生成结果无需人工判断；出现更好的工具或真实生产证据时，应复审本页默认值。

## 1. 当前结论

- **Mesh 只是资产的一部分。** 正式资产还需要尺寸、Pivot、拓扑 / UV、PBR、Collider、交互锚点、可见状态、动画接口、音效事件、来源权利和 Unity 实景证据。
- **AI Provider 只提供 Candidate。** Blender、项目 manifest、Unity Importer 和验收 Harness 才是项目拥有的稳定边界；换 Provider 不应改变 Runtime 玩法真值。
- **统一风格先于批量生成。** 先用少量代表资产冻结 Art Bible（美术基线）和材质族，再扩大到单件 Brief；不让每件资产各自从 Prompt 发明一种废土风格。
- **状态优先组合，轮廓改变才换 Mesh。** 沙尘、积雪、潮湿和轻度磨损主要由共享 Shader、Mask、Decal 与 Overlay 表现；破损到改变轮廓、部件、碰撞或交互时才使用 Mesh 模块或变体。
- **动画按资产类型分流。** 居民共享 Humanoid 骨架与动作库；机器优先分件、正确 Pivot 和程序动画；特效用 VFX / Shader；不为所有道具做骨骼动画。
- **URP 是当前交付目标，PBR 数据保持中立。** Base Color、Metallic、Roughness、Normal、AO 等源数据进入项目后由 Adapter 转为 URP 语义；以后迁移 HDRP 时重建材质与 Shader Adapter，不重做玩法状态和原始 Mesh。
- **玩法验证不等待正式美术。** Foundation Prototype 先用 Unity 参数化代理锁定尺寸、分件、Collider、Anchor 和状态接口；外部 AI / Blender 资产只替换视觉槽。已验证实现见[参数化道具 Harness](nomad-workshop-parametric-prop-harness.md)。

## 2. “资产族”到底包含什么

单个 `FBX` 或 `GLB` 不能代表完整资产。建议用稳定 Asset ID 把以下产物串起来：

| 层 | 最小产物 | 主要问题 |
|---|---|---|
| 身份 | Asset ID、用途、尺寸、优先级、Candidate / Approved 状态 | 它在玩法里做什么，是否值得继续投入 |
| 视觉设计 | 文字 Brief、Hero 图、必要的多视图 / 结构图、色板与禁止项 | 轮廓、比例、功能故事是否一致 |
| 几何 | Mesh、Pivot、Scale、LOD、Collider、可拆部件与 Socket | 是否可落地、可编辑、可交互 |
| 材质 | 中立 PBR 源图、材质族、Unity 打包图、导入设置 | 动态灯光下是否可信且与同族一致 |
| 状态 | 模拟参数、Mask、Decal、Overlay、VFX 与 Mesh 阈值 | 脏、湿、冻、坏怎样组合和恢复 |
| 动画 | 骨架或部件层级、Clip / 程序曲线、IK / 接触点 | 谁动、怎么复用、逻辑真值在哪里 |
| 交互 | 站位、朝向、手部目标、工具点、入口 / 出口、VFX / SFX Anchor | 居民怎样真实使用它 |
| 声音 | Start / Loop / Stop / Success / Fail / Impact 等事件族 | 声音是否跟动作和状态同步，而非一条孤立音频 |
| Unity | Prefab、材质、Importer、Collider、固定镜头和性能证据 | 在真正游戏镜头中是否成立 |
| 来源 | Provider / 模型版本、Prompt / Seed、输入与输出 Hash、费用、许可、清理时间 | 能否追溯、商用、重生成和替换 |

“水循环设施资产族”可以包含多种尺寸或外观实例，但共享相同的管线口径、交互 Socket、材质语言、状态语义和声音事件。这样才能复用，而不是只复用文件格式。

## 3. 从游戏方向到正式资产的默认流程

```text
产品幻想、固定镜头、目标平台与性能预算
  → 代表性 Style Kit 与多种视觉方向
  → 冻结 Art Bible、材质族和禁止项
  → 资产族 Brief、功能接口和状态矩阵
  → Hero 图 + 必要的正交 / 多视图 / 结构参考
  → AI / 程序化 / 手工 / 合规商店资产候选
  → Blender 清理、分件、Pivot、UV、LOD 与 PBR 规范化
  → Unity Importer、Prefab、交互锚点与状态 Adapter
  → 同族成组、代表性镜头、动画 / 音效与性能验收
  → 保留、返工或删除
```

### 3.1 先用 Style Kit 冻结方向

不要从几十个随机道具开始。第一套 Style Kit 建议只包含：

- 一名近景居民与一名远景居民；
- 车辆外壳 / 甲板模块；
- 水循环设施；
- 一个普通储物或研究台 / 工作桌；
- 一块荒漠停靠环境与天气状态；
- 一张与 3D 视觉语言一致的 UI 面板。

每个方向都放入接近正式斜俯视镜头的 Unity Style Scene，比较轮廓、明度层级、材质响应、尺寸、可读性和生产成本。选中的不是“最好看的单张图”，而是最能支撑长时间经营、不同天气和大量同屏设施的一整套规则。

Art Bible 至少固定：形体语言、圆角 / 倒角尺度、比例、色板、明度范围、材质族、磨损尺度、Texel Density 候选区间、灯光、镜头、UI 语言、允许的夸张和明确禁止项。数值仍可随实景证据调整。

### 3.2 Hero 图优先，只为契约补参考

用户提出的“文字讨论 → 多风格图片 → 多轮优化 → 选图 → 3D”主线是合理的。Hero 图是默认输入；额外视图不是形式门槛，只在减少关键不确定性时增加：

- Hero 图负责情绪、轮廓、材质和主视角；
- 不重要的背面、螺丝和装饰允许 3D 模型合理推断；
- 只有模块拼接、门轴、角色接触、维修盖、管口、可拆件或固定尺寸会影响玩法时，才补一个有信息增益的方向图、局部标注或简单 Blockout；
- 多张图只要求主要轮廓和契约接口不冲突，不追求每个表面细节像 CAD 一样完全一致；
- 状态 Sheet 负责干净、沙尘、潮湿、结霜、损坏的层次，不要求每格都是完整独立模型；
- Animation Brief 负责动作意图和接触点；SFX Brief 负责事件、距离、循环和材质感。

概念图是设计证据，不是自动生成后续全部资产的唯一真值。动画和音效应从交互语义派生，而不是让同一张图片黑盒“配套生成”。

### 3.3 先让代理资产承载真实玩法

参数化代理不是随手拼几块无法继承的灰盒，而是正式玩法接口的第一任实现。Profile 先锁定实际米制尺寸、功能分区、可动部件、Collider、库存 / 管线 / 居民交互 Anchor 与材质语义；生成后的普通 Mesh / Prefab 可以直接进入第一版游戏。外部高品质模型到来后只替换 `Visual_Final`，继续复用相同根契约。

首个野战厨房已经用项目现有 ProBuilder 6.1.2 跑通“临时参数几何 → 合并普通 Mesh → 三门 Pivot → URP/Lit 材质 → 预览与审计”，没有增加运行时 Package。它的作用是尽早暴露比例、空间、交互和状态设计错误，而不是宣称程序几何已经达到正式美术。

## 4. 3D 生成器实际能交付什么

| 候选 | 几何与外观 | 动画能力 | 对本项目的判断 |
|---|---|---|---|
| [TripoSR](https://github.com/VAST-AI-Research/TripoSR) | 2024 单图重建；默认 Vertex Color，可用 `--bake-texture` 烘焙颜色纹理；没有完整的 Metallic / Roughness / Normal / AO 生产链 | 无自动 Rig 或动画 | 免费、MIT、可本地复现的**形体基线**，不是完整 URP PBR 资产生成器 |
| [Stable Fast 3D](https://github.com/Stability-AI/stable-fast-3d) | 在 TripoSR 基础上增加 UV、去光照、材质参数和 Remesh；约 6GB VRAM | 无完整角色动画生产链 | 更像本地 PBR 候选，但 Windows 仍为实验支持且需要额外工具链 / 权重许可 |
| [Rodin Gen-2.5](https://docs.hyper3d.ai/en/api-specification/rodin-gen2-5) | 1–5 图、方向标签、固定 Seed、Bounding Box、Quad / Raw、面数控制；PBR 输出包含 Base Color、Metallic、Normal、Roughness | 可生成 T / A Pose 条件，但公开 API 未提供完整生产级 Rig / Clip 生成 | 当前机械道具盲测首选；PBR 和可控性明显超出 TripoSR，但仍需 Blender 清理和 Unity 验收 |
| [Meshy 7](https://docs.meshy.ai/en/webapp/image-to-3d) | 多视图、PBR、Smart Topology、分件、重贴图与 Remesh | [Animate](https://docs.meshy.ai/en/webapp/guides/animate) 可自动绑定 Humanoid / Quadruped，并提供 500+ 预设 Clip | 角色与有机资产的强候选，也可作为 Rodin 分件失败时的挑战者 |
| [Tripo H3.1](https://developers.tripo3d.ai/en/models/v3-1) | PBR、Quad、Smart Low-poly、多视图、部件和高细节选项 | [Auto Rig](https://developers.tripo3d.ai/en/docs/animations-rig) 支持双足及多类非人形，并可按 Clip 重定向 | 云端全链候选；它与开源 TripoSR 不是同一代产品 |
| [Hunyuan3D 2.1](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1) | 开放 Shape + PBR Paint；官方给出约 10 / 21 / 29GB 的形体 / 贴图 / 全流程显存 | 仍需独立 Rig / 动画链 | 可在短时高显存云 GPU 做开放模型上限测试，本机 8GB 不适合完整链 |
| [TRELLIS.2](https://github.com/microsoft/TRELLIS.2) | MIT、复杂拓扑与 Base / Roughness / Metallic / Opacity；官方要求 Linux 和至少 24GB NVIDIA GPU | 不替代角色绑定和 Clip 制作 | 开放 PBR 上限参考，不作为当前本机安装项 |

因此“Rodin 对 TripoSR 好多少”不能诚实地换算成固定倍数：前者在可选多图、背面推断、PBR、面数、分件和云端工程化上属于代际差距；是否真的省时，必须从一个没有现成 Mesh 的新资产统计 **Brief + 概念 + 生成 + 清理分钟数 + Unity 实景**。现有水循环设施只提供 Harness 与风格下限，把它的渲染再生成同一模型不能算生产收益。

## 5. PBR 到 Unity 的规范化

无论来源是 Rodin、Tripo、Meshy、Substance 还是手工，项目保存中立源图，再由导入 Adapter 生成目标管线数据：

- Base Color / Emission 作为颜色数据使用 sRGB；Metallic、Roughness、AO、Height 与 Mask 作为数据纹理关闭 sRGB；
- Normal 必须按 Normal Map 导入，并在代表性灯光下核对 Tangent Space 与绿通道约定；
- URP Lit 推荐通道打包为 `R=Metallic, G=AO, B=Unused, A=Smoothness`；Provider 的 Roughness 需转换为 `Smoothness = 1 - Roughness`。Unity 6 的[官方通道打包说明](https://docs.unity3d.com/6000.0/Manual/urp/shaders-in-universalrp-channel-packed-texture.html)也是这一语义；
- Provider 若把高光和阴影烘入 Base Color，应先去光照或判为 Shaded 候选，不能假装它会在动态天气下正确响应；
- PBR 图齐全也不等于风格统一。最终色板、对比度、边缘尺度、磨损频率和材质族由项目规范化层控制。

普通金属、涂漆、橡胶、塑料、布料和木材优先共享少量项目材质族。Hero 资产可以有独特 Mask 和局部纹理，但不应为每个 AI 输出保留一套不可解释的 Shader。

## 6. 沙尘、积雪、潮湿和破损怎样组合

模拟层保存可解释的玩法状态，例如 `DustCoverage`、`FrostCoverage`、`Wetness`、`DamageStage`、`LeakRate`。Renderer 只消费这些数据，不拥有耐久结算、污染传播或修理真值。

| 变化 | 默认表现 | 何时增加几何 / 变体 |
|---|---|---|
| 沙尘 / 污垢 | World / Object Space Mask、顶面 / 迎风权重、噪声、共享 Dust 材质层；局部脚印或擦痕用 Decal | 厚沙堆改变轮廓、通道或清理工作量时增加可复用 Overlay Mesh |
| 积雪 / 结霜 | 朝上表面和温度驱动的 Snow / Frost 层，改变 Base、Normal、Smoothness；冰晶、飘雪和融水用 VFX | 雪帽、冰柱或结冰堵塞改变轮廓 / 交互时挂接独立 Mesh |
| 潮湿 / 泄漏 | 颜色压暗、Smoothness 提高、流痕 Decal、滴水 / 蒸汽 VFX 与地面积水对象 | 管道爆裂、容器变形或积水影响导航时切换部件 / 世界对象 |
| 锈蚀 / 轻度磨损 | Curvature / AO / Position / Artist Mask 驱动材质层和局部 Decal | 腐蚀穿孔、缺失紧固件或功能端口变化时换局部部件 |
| 破损 | 裂纹、焦痕、火花、烟、漏液、指示灯和音效先表达阶段 | 轮廓、碰撞、可用部件或动画层级变化时使用 Intact / Damaged / Destroyed 模块 |
| 垃圾 / 排泄物 | 有搬运、容量和处理玩法的内容应是有限、可合并的真实 Item / Pile；残留污渍再用 Decal / Shader | 只有堆积真实阻挡空间或成为可处理资源时需要 Mesh 实体 |

建议渲染顺序为：**Base PBR → 固有磨损 → 环境覆盖 → 局部 Decal → Overlay / VFX → 必要的 Mesh 阈值切换**。例如积雪可以覆盖旧沙尘，融化后重新露出；潮湿会改变沙尘颜色和粗糙度，而不是生成“湿沙雪破损版”整套贴图。组合政策由少量规则和 Mask 决定，避免状态笛卡尔积。

首轮不做一个包含所有 Feature Keyword 的万能 Mega Shader。更稳妥的家族是：`SurfaceLit`、`SurfaceStateful`、`Character`、`Decal`、`Water`、`VFX`；同屏代表场景再决定功能和采样预算。URP 的 [Decal Shader Graph](https://docs.unity3d.com/6000.0/Manual/urp/prebuilt-shader-graphs-urp-decal.html)可以覆盖局部 Base / Normal / Metallic / AO / Smoothness，但它仍有 Draw Call 与 Renderer Feature 成本。

每物体参数也不能默认依赖 `MaterialPropertyBlock`：Unity 6 官方说明它会使该 Renderer 失去 SRP Batcher 兼容。第一版可先用少量离散材质档位、全局天气参数和共享 Mask；若大量实例确需连续独立状态，再在代表场景比较 SRP Batcher、GPU Instancing、Structured Buffer 或其他数据路径后选择。

## 7. 动画生产按类型分流

| 资产类型 | 默认路线 | AI / 外部工具的合理角色 |
|---|---|---|
| 居民 | 一套项目共享 Humanoid 骨架、Avatar 与通用动作；模块化头发 / 衣服 / 工具；IK 修接触 | Meshy / Tripo / Mixamo 可绑骨或提供候选 Clip，Blender 清权重和 Clip，Unity Humanoid Retarget 复用 |
| 门、阀、风扇、指针、活塞 | 独立部件、正确 Pivot / Socket、程序曲线或少量 Generic Clip | Rodin Bang / Meshy 分件可给起点，但不能自动保证 Pivot、层级和机械约束 |
| 可破坏设施 | 模块替换、Blend Shape 或少量阶段 Clip，玩法状态驱动 | AI 可生成替换部件或损坏参考，不能让动画事件结算耐久与资源 |
| 布、软管、悬挂物 | 简化骨链、Shader / Secondary Motion 或烘焙 Clip | 只有镜头确实看得见时引入更重模拟 |
| VFX / 液体 / 天气 | Shader、Particle / VFX Graph、Flipbook、程序参数 | 生成器提供纹理 / Flipbook 候选，Runtime 规则仍由 Unity 控制 |

角色应在所有改形、重拓扑、分件和主要 UV 修改完成后再绑定；Tripo 官方也明确提醒这些操作会丢失已有 Rig / Animation。T / A Pose 只让资产“适合绑定”，不代表权重、骨架、Clip 和脚手接触已经完成。

当前最省成本的居民路线仍是项目已有的标准 Humanoid + 合规通用动作库。若要增加来源：

- [Mixamo](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) 官方仍声明 Adobe ID 可免费使用且游戏可商用，但只支持双足 Humanoid，并明确存在中国账号地区限制；它只能是可用时的补充，不做唯一依赖；
- Meshy Web 当前提供 Humanoid / Quadruped 自动绑定和 500+ 预设，适合快速验证人物 / 动物；
- Tripo API 当前提供 Rig Check、Auto Rig 与逐 Clip Retarget，适合以后自动批处理；
- 专用、情绪化或工作动作仍需 Blender 关键帧、动作捕捉 / 视频动捕候选和人工清理，尤其要验 Foot Slide、Hand Contact、Root Motion 与工具穿插。

## 8. Hyper3D 功能分别有什么用

| 功能 | 对项目的价值 | 当前边界 |
|---|---|---|
| Image / Multi-image to 3D | 从新资产 Hero 概念快速得到可检查的 3D 候选 | Hero 单图优先；只有关键接口或隐藏结构反复猜错时才按失败原因补图，不把多视图当固定门槛 |
| 3D Editing / [Bang](https://docs.hyper3d.ai/en/api-specification/bang) | 把已有 Rodin 或上传 Mesh 分成部件、局部重做；API 当前每次 0.5 Credit | 不能假设自动得到正确 Pivot、封闭背面、骨架、权重和机械约束 |
| [Texture Generator](https://docs.hyper3d.ai/en/api-specification/generate-texture) | 给已有 Mesh 生成 / 重做 PBR，适合独立比较几何与材质；API 当前每次 0.5 Credit | 仍需去光照、UV、接缝、材质族和 Unity 打包验收 |
| HDRI Generator | 概念照明、Blender 预览、远景天空候选 | 不把一张 HDRI 当正式关卡灯光或商店截图质量保证 |
| WorldGen | 当前 Workspace 显示 Manual、AI Assist、Full AI、3D Objects 和 3D Gaussian Environment 等模式，适合 Mood、场景 Blockout、聚落构图和远景预演 | 暂无足够公开技术契约证明其碰撞、NavMesh、LOD、可编辑性、确定性和 Unity 导出适合核心可玩世界；Gaussian Scene 不承担 Runtime 世界真值 |
| Unity / Blender Add-on | 减少手工下载和导入步骤；可直接使用所选场景物体做 ControlNet 输入 | 官方流程仍由 Rodin Web Interface 承接会话、进度和部分操作；首轮仍走中立文件 + manifest，不把 Add-on 当 Headless API 或项目真值 |

WorldGen 可能很有创意价值，但《游牧工坊》的世界需要居民导航、设施交互、状态保存、天气、选择性持久化与性能控制。现阶段它更适合“看起来可能是什么样”的 Previs，不适合直接替代 Seed 世界、模块场景和项目生成器。

### 8.1 Add-on、网页与 API 的自动化边界

官方 [Blender Add-on](https://docs.hyper3d.ai/en/addons/blender-addon) / [Unity Add-on](https://docs.hyper3d.ai/en/addons/unity-addon) 文档只承诺其设置与网页设置“对应（correspond）”，并没有承诺两边字段永远一一相同或提供稳定的脚本 API。两种 Add-on 的 One Click 仍会自动打开 Rodin Web Interface，要求启用浮动窗口来监视进度；Manual 更明确要求在浮动窗口继续每个阶段。因此它们是 **DCC / Engine 到网页的交互桥**，不是浏览器无关的后台服务：

| 通道 | 最适合 | 不承担什么 |
|---|---|---|
| Rodin Web | 首次试用、免费预览 / Redo、观察新功能和账户状态 | 不承担批量、无人值守、固定参数回放；同一标签页被人工切换或点击会让 UI 自动化失去焦点、命中旧状态或超时 |
| Blender Add-on | 人工在 Blender 里选参考物、发起生成并导入候选，随后马上清理拓扑、UV、Pivot 与材质 | 不消除 Chrome 116+ 和网页会话依赖；不替代 Raw 下载、manifest、Hash 与 Unity 验收 |
| Unity Add-on | 快速把候选放进场景观察尺度和构图 | 不作为正式资产入库入口；直接进 Scene 容易绕过 Blender 清理、Importer 规则和来源证据 |
| Rodin API | 固定参数、固定 Seed、余额 / 成本预检、异步轮询、批量下载和可恢复任务 | 不自动保证美术质量；API Key、付费权限、限流、临时下载 URL 和供应商字段变化仍需项目 Adapter 管理 |

人工点击同一 Rodin 标签页可能打断的是 **本地自动化的下一步**，不等于已被服务端接受的生成必然取消；但它足以让上传、确认、下载或导入走错状态。若必须用网页，使用单独标签页 / 窗口、短时监督关键操作，并在每个产生费用或上传资产的动作前重新核对页面和参数。

2026-09-02 对 DeemosTech 公开实现的只读审计结论：

- `rodin3d-skills` 固定到 `46f311c768c0d12c6b63078e562110f958d3bf66`（2026-05-25）。其请求结构和轮询流程有参考价值，但文档仍把 `geometry_instruct_mode=faithful` 写成默认值，晚于官方 [2026-07 Changelog](https://docs.hyper3d.ai/en/get-started/changelog) 已改成 `creative`；尚未覆盖 `Hybrid`、`is_symmetric`、`image_label`、`uhd_texture` 等现行字段。
- 该 Skill 的提交异常分支会再次发送同一个 `POST /api/v2/rodin` 以打印响应；若第一次请求已被服务端接受而客户端丢失响应，可能重复建任务和计费。它还把“应实现指数退避”只写在文档中，代码没有实现，所以不能按其“enterprise-grade”自述原样采用。
- `rodin-api-mcp` 固定到 `855b2a864072f3409ee4d302ba907993a53e6e92`（2025-04-22），仍是 Gen-1 / 1.5 参数、旧域名和内置共享 Key；不安装、不连接。
- `rodin3d-bang-skills` 固定到 `ce1c38d2bef9bf977a3d9ad19641631ed247f83e`（2026-04-25），下载命名和 manifest 比普通 Skill 更稳健，但它专门执行“Rodin Gen-2 源资产 → Bang 拆件”两次任务，不是 Gen-2.5 普通资产主入口；只有部件分离成为已验证瓶颈时再单独评估。
- 两个 Skill 仓库的插件元数据声称 MIT，但三个当前克隆树都没有实际 `LICENSE` 文件；在许可文本补齐前，只借鉴公开 API 事实和设计思路，不复制实现进入仓库。

### 8.2 Rodin Blender Bridge v0.2.0 本机静态审计

本机从 Rodin 首页取得的 `blender_rodin_bridge_v0.2.0.zip` SHA-256 为
`CCC92717BF8341BE80561FBAB10C6EDBDB4614BA36C5F36540BDE4CB9DE71E19`。
它是没有 `blender_manifest.toml` 的 Legacy Add-on，压缩包名为 `v0.2.0`，但
`bl_info` 仍报告 `(0, 1, 9)`；这可能只是打包元数据漏改，也意味着不能仅凭
文件名判断实际代码版本。官方要求 Blender 4.0+ 与 Chrome 116+，本机
Blender 5.2.1 LTS 满足最低版本；面板启用已完成，但仍需发送与导入冒烟测试后
才能声称完整兼容。

该 Bridge 不是无浏览器 API Client。静态代码会在 Blender 内启动绑定到
`127.0.0.1:61863` 的 WebSocket Server，把图片或选中 Mesh 编码后交给 Rodin
网页，主动启动 Chrome，最后接收并导入 OBJ / FBX / glTF / USDZ 及贴图。它能
明显减少手工上传、下载和导入步骤，也能提供 Multi-view 与 ControlNet；账号、
免费预览、确认和云端生成仍由网页承担。`One Click` 会把 `bypass` 设为 `true`，
代码注释为“直接 Send to Blender”，在实测确认其 Credit 边界前不得把它用于
免费筛选；首次启用只用 `Manual`，并在空白 `.blend` 中检查是否仍保留模型确认
关口。稳定后它适合作为人工 / Agent 监督的 DCC Bridge，而不是项目级 Headless
Harness。

用户已在 Blender 5.2.1 LTS 中启用该 Add-on，实际看到的是 3D View 右侧
`N` 面板中的小型 Rodin 区块，这与代码声明的 `VIEW_3D` / `UI` / `Rodin`
完全一致，不是安装失败。它目前只证明面板能注册；尚未证明网页发送、模型确认
边界、WebSocket 回传和自动导入在 Blender 5.2 中都工作。对本轮“确认前免费
预览”，Bridge 几乎不增加价值，因为审查仍发生在网页；一旦用户批准某个候选并
需要把 Raw 结果直接送进 Blender，它才可能显著减少下载、解压和导入操作。

若盲测通过并出现真实批量需求，再实现项目自有的薄 Adapter。它至少要把输入 Hash、请求参数、文档版本、预估最高 Credit、任务 UUID、原始提交响应、状态历史、下载文件 Hash 和失败分类写入 manifest；提交 `POST` **不得**因未知结果自动重试，必须先查回已有任务或交由用户确认，防止重复计费。Key 只从进程环境 / 密钥存储读取，不写仓库、不作为命令参数、不回显。官方当前 [API 数据政策](https://docs.hyper3d.ai/en/legal/data-retention-policy)承诺请求与输出在活动系统保留 7 天、不用于训练且不公开到 ASSETS；该承诺只作为 API Lane 的当前外部契约，测试日仍需复核。

## 9. 2026-09 成本与采用建议

价格和权益会变化，测试日仍应以官方页面与实际 Checkout 为准。当前可比较的公开基线是：

| 平台 | 当前公开方案 | 粗略产能 / 权利 | 更适合什么 |
|---|---|---|---|
| [Hyper3D](https://hyper3d.ai/zh/pricing) | Free 为确认后付费，直接 Credit 为 $1.5；Creator 月付 $30 或年付折合 $24 / 月，官方估算约 60 模型；Business 月付 $120 或年付折合 $96 / 月并开放完整 API | Creator 含多图、Smart Low-poly、HD / Custom Texture、High-poly 法线烘焙、更多 Redo、私有资产和任意用途 | 少量高质量机械 / 道具与可控 PBR；先人工盲测，暂不买 Business API |
| [Tripo Studio](https://www.tripo3d.ai/pricing) | Free 每月 200 Credit；Pro 当前年付折合 $20 / 月、3000 Credit、官方估算约 200 模型 | Free 为公开且非商用；Pro 私有 / 商用，含多视图、Smart Mesh、分件、Rig / Animation 与 DCC Bridge | 若角色、怪物、批量和全链能力成为主要需求，性价比很强 |
| [Tripo API](https://developers.tripo3d.ai/en/pricing) | $0.01 / Credit；图生 3D 标准贴图 30 Credit（约 $0.30），HD +10、Quad +5、Smart Low-poly +10、Parts +20；Auto Rig 25，Retarget 每 Clip 10 | Pay-as-you-go，参数与成本透明 | 工作流稳定后做自动 Adapter；不因 API 便宜而跳过质量 Gate |
| [Meshy](https://docs.meshy.ai/en/webapp/pricing) | Free 每月 100 Credit；Meshy 7 Image-to-3D 当前 25 Credit；Pro 官方当前 $20 / 月、1000 Credit | Free 输出 CC BY 4.0 需署名；付费私有 / 完整商用；Web Rig / Animate 当前不额外耗 Credit | 约 4 个免费月度候选；付费约 40 个 Meshy 7 候选，角色动画与分件尤其值得挑战 |
| 本地开放模型 | 软件 / 权重本身可能免费，成本转为 GPU、安装、维护、运行和许可审计 | TripoSR / SF3D 可在约 6GB 档尝试；完整 Hunyuan3D / TRELLIS.2 超出当前 8GB 主机 | 隐私、离线、批量或可训练性带来明确价值时，用隔离环境或短时云 GPU |

### 当前推荐顺序

1. **不立即订阅任何平台，也不上传现有水循环设施渲染。** `NW_FieldKitchen_01` 的两条免费预览均已完成；Reviewed Hero 在功能布局上明显胜过 Direct Text，已经足以回答概念关口是否有价值。
2. **下一笔费用只用于回答 Raw Mesh 问题。** 若用户批准，最多确认 Reviewed Hero 这一个候选，检查分件、拓扑、UV、PBR、Blender 清理分钟和 Unity 固定镜头；不为看更多漂亮预览补 Seed，也不确认已知语义较弱的 Direct Text 候选。
3. **Raw / Unity Gate 通过后再考虑 Creator 月付。** Creator 适合进入一段集中机械资产生产期；目前没有理由购买 Business，因为 API 自动化尚无稳定输入、清理规则和真实批量。
4. **角色另开 Lane。** 用项目已有 Humanoid 基线，对 Meshy 或 Tripo 做一次 T / A Pose、自动 Rig、共享 Clip Retarget 的盲测；不要用机械道具胜负决定人物供应商。
5. **本地路线暂不装 TripoSR。** 当前 Windows 工具链成本高，而它又不提供完整 PBR / 动画。若以后每月需要大量资产、云端隐私或可重现性成为瓶颈，再在短时高显存云 GPU 比较 Hunyuan3D / TRELLIS.2，比先扩张本机环境更合理。

当前 Hyper3D 账号显示的 Credit、限时折扣和 7 天试用属于个人 / 临时状态，不进入仓库真值。可见页面没有充分说明“7 天试用究竟额外发多少 Credit”，因此不能把 Creator 的“每月重置 30 Credit”直接当成试用赠送量；开始试用前应在 Checkout 最后一页核对自动续费、结束日期和实际 Credit，再由用户决定。

## 10. 下一组最小证据

1. 由用户决定是否为 Reviewed Hero 候选执行一次模型确认与 Raw 导出；若继续，先保存供应商原始文件、参数、费用与 Hash，再验证 Rodin Bridge 的回传是否真的比中立下载更稳。
2. 在隔离 Blender 文件中记录自动归一化与人工清理分钟，重点检查门 / 抽屉、顶部散件、细管、背面封板、Pivot、UV 和 PBR；只有 Raw 值得保留才进入 Unity。
3. 把清理后的候选与储物箱、车辆甲板放进同一代表性镜头，检查比例、风格和材质族；通过后再做很薄的 `SurfaceStateful` 视觉 Spike，验证沙尘、潮湿、局部泄漏和轻损坏能否组合。
4. 角色 Lane 只验证一名共享 Humanoid：T / A Pose → Rig → Walk / Carry / Repair Retarget → Unity Avatar / Foot / Hand 接触，不立即批量生成人物。
5. 每个 Lane 都记录生成费用、人工分钟、失败类型和最终删除率；达到 10–20 个真实候选后，再判断订阅、API Adapter、本地模型或 Project Skill 是否值得维护。

相关基线：[Blender 3D 资产管线](blender-art-pipeline.md)、[《游牧工坊》产品愿景](nomad-workshop-game-vision.md)、[AI 游戏开发能力图谱](ai-game-development-capability-map.md)、[AI 音乐与音效生产候选](ai-audio-production-research.md)。

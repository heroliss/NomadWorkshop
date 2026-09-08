# 《游牧工坊》首轮 AI 资产生产实验协议

> 状态：**实验记录 v0.4**，更新于 2026-09-02。同一文字 Brief 下的 `Text-to-3D` 与“概念图 → `Image-to-3D`”首个免费预览均已完成；经用户确认上传的 Hero 图在功能语义关口明显胜出。两路都停在模型确认前，没有外部生成结果被批准为游戏资产。

## 1. 这次实验究竟回答什么

`NW_WaterRecycler_01` 已经拥有可编辑 `.blend`、FBX、UV、三组 PBR 材质、封闭拓扑、Unity Prefab 和导入证据。把它重新渲染为图片，再让 Provider 近似重建同一模型，会丢失已有精确结构，且不能证明 AI 降低了**新资产**生产成本。因此：

- 水循环设施只保留为 Blender / Unity Harness 对照、风格参考和现有能力下限，不再作为首个 Rodin 上传输入；
- 已生成的中性 PNG 只用于重建算法回归、粗 Blockout 细化等**可选诊断**，不进入本轮生产价值排名；
- 首轮改做仓库中尚不存在几何的新资产 `NW_FieldKitchen_01`（车载生存厨房工作站）；先冻结文字 Brief，再并行验证“直接文字生成”和“先收敛概念图再生成”两条路线。

实验问题不是“哪张宣传图最好看”，而是：

> **从一个尚不存在 3D 几何的玩法需求出发，直接 `Text-to-3D` 和“文字 → 概念 → 必要参考 → `Image-to-3D`”哪条路线能以更低总成本进入真实 Unity 镜头，同时保持功能、风格、可编辑性与权利边界可信？**

现有程序化资产保留为 Harness / 风格基准，新厨房候选始终使用独立路径。候选只有通过验收后才讨论进入正式资产目录；未胜出的原始输出留在被忽略的实验目录或直接删除。

## 2. Provider-neutral Asset Brief

### 身份与玩法

| 项目 | 要求 |
|---|---|
| Asset ID | `NW_FieldKitchen_01`；每次外部候选使用独立 Experiment ID |
| 用途 | 供约 3 名居民使用的车载生存厨房工作站，承担储存、备餐、烹饪和基础洗涤 |
| 核心过程 | 冷藏 / 柜体取食材 → 台面备餐 → 消耗水与电力 / 燃料烹饪 → 产出餐食、脏餐具、灰水与厨余 |
| 故障语义 | 缺水、断电、堵塞、油污、食物腐败和设施磨损；设备不会凭空生成食物或销毁垃圾 |
| 玩家读图 | 一眼分辨保温冷藏柜、备餐台、灶具、小水槽、储物区和维护 / 控制区 |
| 交互 | 正面 1–2 个工作位；食材输入、餐食输出、净水输入、灰水输出、厨余输出和电力接缝都有稳定 Anchor |

食物、水、污染、维护和物流属于游戏模拟；Mesh 只表现设施。食材、锅中内容、流水、蒸汽、油烟、污迹增长和维修火花由可替换 Prop、Shader / Decal / VFX 呈现，不烘死在静态几何或 Base Color 中。

### 形体与镜头

| 项目 | 目标 |
|---|---|
| 尺寸 | 约宽 1.80 m × 深 0.70 m × 高 1.65 m；允许候选为清理留出小幅误差 |
| 主轮廓 | 下部柜体 / 冷藏区 + 连续备餐台 + 一侧灶具 + 一侧小水槽 + 上部收纳；不能只是普通现代家装橱柜 |
| 方向 | 前方是居民工作面，背面贴近车辆结构；侧面需说明管线 / 电力连接，但不能堆成无意义复杂机器 |
| 镜头 | 固定斜俯视游戏镜头优先；细线、背面微小零件和不可见内部结构价值低 |
| 风格 | 半写实、略风格化的废土工业；结实、可修、由不同年代零件拼装，但不脏成一团 |
| 色彩 | 低饱和青色旧漆、深色结构、不锈钢工作面、少量安全橙操作点；颜色服务功能层级 |

禁止项：不可读文字或 Logo、现代豪华家装感、悬浮零件、没有去向的管线、极薄易闪烁表面、把阴影 / 油污烘成不可关闭的永久色块、正面成立而背面融化、为了“科幻感”堆叠无用途发光件。

### 几何、材质与 Unity 目标

- 米制、稳定 Pivot、Root Identity；模型能靠底座落地。
- 首轮不把某个三角面数写成审美目标；暂以清理后约 8k–20k Triangle 为观察区间，同时记录 Runtime Vertex、Renderer 和屏幕可读性。
- 目标为 3–4 个主要材质族；Provider 输出多少槽都记录，清理时合并所花时间也计入成本。
- 至少有有效 UV。若只提供 Vertex Color 或单张烘焙色图，必须如实记录，不伪称完整 PBR。
- 可接受来源通道为 Base Color、Normal、Metallic、Roughness / Smoothness、Occlusion；进入 URP 时由 Adapter 统一打包和创建 `URP/Lit` Material。
- 柜门、冷藏门和抽屉若会运动，必须能分离并建立可靠 Pivot；锅具、食材和垃圾属于独立可替换 Prop。
- 候选必须经过现有 Hero / Front / Side / Top / Wireframe / UV Checker 证据和 Unity 固定镜头；单张 Provider 渲染不算验收。

## 3. 文字真值 → 两条受控生成路线 → 3D

### 3.1 文字是真值，不预设哪条路线获胜

Canonical Brief、尺寸、功能接缝、禁用项和材质族是设计真值。`Text-to-3D` 可以省去项目侧的概念生成、挑选和上传，但 Provider 仍可能在内部先把文字渲染成参考图；因此本实验比较的是**有没有经过项目侧可审查、可冻结的视觉设计关口**，不把“内部完全没有图片”当作无法证实的前提。

只有当柜门、工作位、管线或装配关系会影响玩法时，才需要图片或 Blockout 进一步约束。对不重要的背面细节允许模型合理推断，不为形式上的图纸完整度增加工作。实验结果可以形成按资产风险分流的规则：便宜背景物可能更适合文字直出，重要功能设施可能更需要概念关口；不追求一条路线永久包办所有资产。

固定英文概念 Prompt：

```text
A compact vehicle-mounted survival field kitchen for three people in a mobile
wasteland workshop. A sturdy lower cabinet and insulated refrigerator, one
continuous stainless preparation counter, a compact two-zone cooker on the left,
a small deep sink with a practical faucet on the right, upper storage racks,
visible but orderly water and power service connections, accessible maintenance
panels, repairable modular construction, worn muted-teal painted metal, dark
structural steel, stainless work surfaces and a few safety-orange controls.
Semi-realistic stylized PBR game asset, strong readable silhouette from a fixed
isometric game camera, plausible sides and back, isolated object, no person, no
food, no text, no logo, no floating parts, no luxury domestic kitchen.
```

每次概念生成都同时保存 Brief 版本、Prompt、模型版本、Seed、生成次数、图片和权利条款；不能在每个 Provider 前临时改词让它“赢”。

### 3.2 Image-to-3D 路线的概念收敛

1. 先从 Canonical Prompt 生成少量有真实结构差异的 Hero 概念；数量以能看见有意义的选择为止，不为了凑满固定张数而继续生成。
2. 以玩法可读性、车辆空间占用、工作位、功能连接和游戏镜头轮廓筛到 1 个；人和 Agent 可以讨论并修改文字 Brief。
3. 冻结 Hero 后，先列出**真正不能让 3D 模型自由猜测**的局部。只有背部接口、侧面工作位、门轴或模块装配等重要信息在 Hero 中不可见时，才补对应方向图或局部标注；若没有这种信息，单张 Hero 就可以进入首轮 3D。
4. 多张参考只要求轮廓、主要部件和契约接口不冲突，不要求每颗螺丝完全一致。非关键矛盾可以记录后交给 3D 候选与 Blender 清理解决；关键矛盾才回到 2D 修正。
5. 简单 Blockout 只负责体块、尺寸、可动部件和连接点，不做正式拓扑、UV 与材质；只在图片难以表达精确接口时使用。已经完成生产几何、UV 和 PBR 的资产不走这条回环。

冻结输入目录建议为：

```text
ArtPipelineOutput/AIConcept/NW_FieldKitchen_01/<ConceptVersion>/
  brief.md
  hero.png
  references/       # 可选，只放确有控制价值的方向图或局部标注
  manifest.json
```

本段限制只适用于 `Image-to-3D` 路线，不阻塞同时进行的直接文字候选。Hero 选定后优先尽快做低成本 3D 试投；只有已知的关键接口仍含糊时才继续补图，使失败尽量停留在便宜、容易修改的阶段，又不把前期设计做成小型 CAD 项目。

### 3.3 Rodin A/B 实验契约

| 条件 | Lane A：Direct Text | Lane B：Reviewed Hero Image |
|---|---|---|
| 项目侧输入 | 冻结的 Canonical Prompt，直接交给 Rodin Web 的“极速生成” | 同一 Canonical Prompt 先生成一个 Hero 候选；通过功能与风格审查后交给 `Image-to-3D` |
| 视觉关口 | 无；Provider 若显示内部参考图，原样截图并计入结果 | 有；保存图片、Prompt、模型版本、筛选或返工理由与耗时 |
| 首轮次数 | 1 个候选 | 1 个候选；Hero 明显不合格时只针对失败原因返工一次 |
| 3D 条件 | 两路尽可能使用相同 Provider、模型档、PBR、拓扑、面数、贴图和去光照设置；不支持的参数记为路线差异 | 同左 |
| Seed | 支持时两路都记录 `1103`；Seed 只用于各自路线内部复现，不声称跨文字 / 图片输入具有相同语义 | 同左 |
| 追加实验 | 只有首轮无法回答路线差异，或发生可归因的服务故障时才增加 `2207 / 3301`；必须先记录追加理由 | 同左 |

两路使用同一功能 Brief，但**不追求像素或几何一一对应**；那会把实验错误地变成复刻比赛。比较项包括：首个可用候选耗时、概念生成与筛选成本、3D 直接费用、功能与轮廓可读性、隐藏面可信度、部件可分离性、拓扑 / UV / PBR、Blender 清理分钟、Unity 固定镜头表现、风格一致性和后续修改成本。

Lane B 向 Provider 提交 1–5 张有实际信息增益的图片，第一张使用 Hero 作为主要材质参考；附加图片有明确方向时再标记 `Front / Right / Back / Up`。输入像素、Brief 或概念版本发生会影响结果的变化时建立新的 Experiment；只修正文档错字不机械升版。

现有 `run-blender-ai-mesh-input.ps1` 仍可把**粗 Blockout**输出为透明、中性光照参考，也可做供应商重建回归；对已经完成的 `NW_WaterRecycler_01` 运行它只产生诊断夹具，不代表下一步应该上传该图片。

三个 Seed 是单条路线的实验上限，不是必须花完的配额。挑选规则、失败结果和生成次数全部保留，不能只保存最好的一次而隐藏概念重试和 3D 重试成本。

## 4. 2026-09 候选角色与最小矩阵

`TripoSR` 是一个可理解、许可宽松的**本地基线**，不是预设的画质冠军，也不是生产路线承诺。它的价值是回答“低成本单图重建能提供多少可用形体”；若安装与维护成本本身已经很高，就失去基线意义。

### 4.1 首轮实际比较

| 角色 | 候选 | 决策 |
|---|---|---|
| Harness / 风格对照 | 当前 `bpy` 水循环设施 | 已有完整 Blender、FBX、PBR、Unity 和 Contact Sheet 证据；用于证明项目最低可交付链和废土材质语言，不参加同资产几何复刻排名 |
| 新资产生产题 | `NW_FieldKitchen_01` | 没有既有 Mesh；从 Brief、概念选择、必要补充参考到清理的全部时间都进入成本，能真实检验 AI 是否节省新资产生产工作 |
| 云端上限候选 | [Rodin Gen-2.5](https://docs.hyper3d.ai/en/api-specification/rodin-gen2-5) | **首选外部候选**。支持 1–5 张带方向标签的图片、固定 Seed、`faithful` 模式、Bounding Box、PBR、去光照、Quad / Raw 和目标面数；这些控制项可以直接进入项目 Harness，而不只是在宣传渲染上比较漂亮程度 |
| 条件式本地形体基线 | [TripoSR](https://github.com/VAST-AI-Research/TripoSR) | MIT，官方称单图默认约需 6GB VRAM；它默认输出 Vertex Color，可烘焙颜色纹理，但不交付完整 PBR Map 族。仓库仍依赖本地编译 `torchmcubes`，当前 Windows 主机没有 WSL、CUDA 编译器或 Visual Studio C++ Build Tools，且官方仓库已有持续未解决的 [Windows 构建失败](https://github.com/VAST-AI-Research/TripoSR/issues/3) 记录。因此首轮不为一个仍需重做材质的形体基线扩张系统工具链 |
| 条件式本地材质基线 | [Stable Fast 3D](https://github.com/Stability-AI/stable-fast-3d) | 约需 6GB VRAM，提供 UV、去光照材质参数和 Remesh；但官方仍把 Windows 标为实验性，并要求 VS 2022、受控权重授权与带收入边界的许可。只有云端结果证明“本地自托管”值得投入时再补测 |
| 高显存开源参考 | [Hunyuan3D 2.1](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1) / [TRELLIS.2](https://github.com/microsoft/TRELLIS.2) | 两者都能代表更先进的开源 PBR 方向，但官方完整流程分别约需 29GB 与至少 24GB NVIDIA 显存；当前 8GB 主机不安装，需要时只考虑短时云 GPU |

Rodin 首轮按 3.3 同时运行 Direct Text 与 Reviewed Hero Image。API Lane 若以后启用，可把 `Gen-2.5-Medium`、`geometry_instruct_mode=faithful`、`is_symmetric=asymmetric`、`material=PBR`、`mesh_mode=Quad`、`quality=low`（约 8k Quad）、2K 贴图和 `texture_delight=true` 作为可审计目标；图片路线另用 `use_original_alpha=true`。当前 Web Lane 只能尽量保持同一模型档、Thinking Effort 和推荐预设，界面不暴露的字段必须留空，不把 API 参数反推成网页事实。若厨房背部水 / 电接口在 Hero 中完全不可见且首轮结果证明它确实重要，再增加一张背面或右侧参考，并作为新的 `assisted-image` Lane 记录。

2026-09-02 的 Direct Text 首候选使用“极速生成”、`Medium` Thinking Effort 与 `Use Recommended`。Rodin 先生成一张内部参考图，再把完整 Brief 收缩为 `Compact wheeled outdoor camping kitchen cabinet.`；免费 3D 预览保留了“带脚轮的厨房柜”类别，却明显弱化左灶台—中央操作台—右水槽、车载维修结构和废土风格。结果页同时显示 `Low` Badge，不能断言它与前一页的 Thinking Effort 是同一个维度。该候选按协议不 Redo、不确认模型、不生成材质或导出；它是路线证据，不是可入库资产。

同日的 Reviewed Hero Image 首候选上传了已审查的 `v0.1` Hero，仍使用 Gen-2.5、`Medium` Thinking Effort、单结果与推荐预设，图片方向保持 `Unknown`，不为首轮补三视图。Rodin 将其概括为 `Industrial mobile outdoor kitchen workstation.`；免费预览保留了左灶台、连续操作台、右水槽、上下储物和工业框架。旋转检查还能看到合理的侧面水箱、管线和维护接口，但背面被简化成大面积封板。

在用户授权后，该候选继续确认非对称 Quad 模型并生成材质。材质使用 Native / High、De-light、PBR Detail 7 和网页 8K 设置；提示词约束青色旧漆、不锈钢、深色结构、橙色控制件、轻微沙尘磨损，并禁止烘焙光照、文字、Logo 和额外物件。材质确认页明确显示 0.5 Credit，完成后可见余额为 4.5；网页没有提供足够证据把模型确认和材质各自的总费用完全拆开，因此实验不推断未显示的计费明细。

网页直接下载受订阅弹窗限制，没有绕过；官方 `Send → DCC → Blender` 经 Rodin Blender Bridge v0.2.0 成功传入 Mesh、Principled PBR 节点与四张图像。Bridge 只在 Blender 内存中交付了图像像素，最初没有把 `//textures/...` 文件写到磁盘；逐张保存、Pack Resources 并重开验证后，四张图实际均为 2048×2048，而不是网页 8K 选择值。

Blender Intake 记录源候选为 18,924 Vertex / 37,908 Triangle、5 个重复面、6 个封闭几何岛、0 Loose / Boundary / Non-manifold Edge；UV 全部位于 0–1。它保留源 `.blend`，只在导出副本清除 5 个重复面。FBX 往返为 37,903 Triangle，与清理副本一致；GLB 少 1 个三角形，因此 Unity 采用 FBX。材质包含 Base Color、Metallic、Roughness、Normal；Intake 为 URP 派生 `R=Metallic, A=1-Roughness`。

Unity 6000.3.22f1 最终读到 1 Mesh / 1 Material Slot、22,058 Runtime Vertex、37,903 Triangle，Bounds 与 Blender 轴转换后逐轴相符；外部 URP/Lit 材质、BoxCollider、Prefab、次级 Universal Renderer 和预览场景审计通过。固定相机可读出旧漆、金属、橙色安全件、织物与软管，法线没有明显反转。完整 EditMode 回归为 645/645。候选仍保持 `manualArtReviewRequired=true`、`productionApproved=false`：单一合并 Mesh 不支持柜门、抽屉、软管与顶部散件的独立动画、损坏或替换，商业输出权利也尚未按当日条款归档。

因此首轮形成的最终结论是：玩法关键设施默认走“Brief → 审核 Hero → Image-to-3D”，低风险杂物仍可用 Direct Text 快速探索；Rodin Reviewed Hero 路线已经证明能快速得到**可进入 Unity 的高质量视觉候选 / 底模**，但还没有证明能直接交付可编辑的生产资产。单张 Hero 足以约束这次功能布局，没有证据支持为了形式完整而补三视图。下一次投入应衡量 Blender 拆件、Pivot、Anchor 与局部重拓扑成本，而不是继续重抽同一外观。

### 4.2 已考虑但不同时铺开的云端替补

| 候选 | 有价值的方向 | 为什么不和 Rodin 同时首测 |
|---|---|---|
| [Meshy 7](https://docs.meshy.ai/en/webapp/image-to-3d) | 2–8 张多视图、Smart Topology、部件分离和目标面数；适合以后测试模块化机械或有机角色 | 免费输出为 CC BY 4.0、付费输出可设 Private；能力很有吸引力，但首轮再加入会把实验变成 Provider 选美 |
| [Tripo H3.1](https://developers.tripo3d.ai/en/models/v3-1) | 单图 / 多视图、PBR、Quad FBX、Smart Low-poly 与高细节几何；适合以后测试人物、怪物或高保真英雄资产 | 它是 2026 云端旗舰，与 2024 开源 `TripoSR` 不是同一个能力等级；当前先保留为 Rodin 无法使用或在机械背面结构上失败时的替补 |

选择 Rodin 不是宣布它永久最好，而是因为当前厨房工作站需要“可选方向提示、非对称结构、固定 Seed、PBR 和面数可控”这组可验证能力。若首个外部候选证明真正瓶颈是部件分离，就换 Meshy；若是英雄级几何与角色链，就换 Tripo H3.1。每次只引入一个最能检验当前假设的候选。

各 Provider 的材质、动画、状态分层、当前公开成本和采用顺序见[《游牧工坊》3D 资产族生产策略](nomad-workshop-asset-family-pipeline.md)。机械道具 Lane 的胜者不自动成为角色、环境或材质 Lane 的唯一供应商。

[Roblox Cube 3D](https://github.com/Roblox/cube) 在 2026 年已经提供文字生成、Bounding Box 条件和 part-controllable 方向，值得以后研究“功能部件可编辑性”；它当前不与单图重建 Lane 直接比较，也不为追新而扩大首轮矩阵。

## 5. 三阶段记录，不让人工清理隐身

每个候选依次保存三份统计：

1. **Raw**：Provider 原始下载，不修复、不改材质；记录生成秒数、费用、Seed、模型版本、格式、权利与文件 Hash。
2. **Normalized**：只允许可重放的 Blender 自动步骤，例如单位、轴、命名、Root、无损格式转换和现有质量报告。
3. **Cleaned**：允许人工或 Agent 修补拓扑、背面、UV、材质槽和功能细节；每个动作与分钟数记账，不能把两小时修模描述成“AI 秒出”。

最小记录字段：

```text
Experiment ID / Provider / Model Version / Terms URL + Date
Brief + Concept Version / Input Files + SHA-256 / Prompt / Seed / Generation Count
Generation Seconds / Download Bytes / Direct Cost
Raw Mesh, Triangle, Material, Texture, Topology and UV Evidence
Automated Cleanup Actions + Seconds
Manual Cleanup Actions + Minutes
Final Blender Harness / Unity Audit / Contact Sheet Verdict
Keep, Retry, Reject + Reason
```

## 6. 评分与硬 Gate

先过 Gate，再评分：

- 输入与输出权利允许当前商业原型使用，来源和条款可追溯；
- 文件能在隔离目录打开，不要求向 Blender / Unity 注入不受控插件；
- 清理后满足尺寸、Root、可落地、UV、材质和 Unity 导入契约；
- 正面、侧面、顶部和背面不存在会破坏玩法可读性的坍塌；
- 工作位、柜门 / 抽屉 Pivot 与六类输入输出接缝可以在 Blender 中建立稳定 Anchor；
- 没有通过删除主要功能结构来“修复”模型。

通过后按 0–5 评分：

| 维度 | 重点 |
|---|---|
| 功能与轮廓 | 是否像可在移动工坊中工作的厨房，储存、备餐、烹饪和洗涤能否在游戏镜头读懂 |
| 隐藏面可信度 | 单图未展示的侧面、背面和顶部是否合理；不因未提供正交图就要求机械复制 Hero |
| 可编辑性 | 部件是否能选择、替换、移动和增加 Anchor，而不是一团不可维护三角形 |
| 拓扑与 UV | 缺陷、拉伸、密度突变、无意义内部面和自动修复代价 |
| 材质价值 | 是否真有可复用 PBR 数据，还是把光照和噪声烘在一张颜色图中 |
| Unity 实景 | 固定镜头中的比例、材质响应、功能焦点和同场资产一致性 |
| 总成本 | 账号 / 许可、生成、重试、下载、自动处理、人工清理和后续可修改成本 |

不存在只靠总分自动胜出的 Provider。权利不清、不可编辑或真实镜头不成立时，即使 Hero 图漂亮也淘汰。

## 7. 安装与账号边界

当前主机有 `uv` 可创建隔离 Python 环境，但没有 WSL、CUDA 编译器或 Visual Studio C++ Build Tools；Blender 自带 Python 只作为已验证 DCC 环境。不得把 PyTorch、CUDA 扩展和模型依赖塞进 Blender Python，也不修改 Blender 用户插件目录。

本地试验若启动，应使用仓库外的独立环境和缓存，并固定：

- Provider 仓库 Commit、Python / PyTorch / CUDA 版本；
- 模型权重 ID 与 Hash；
- 输入 Hash、参数、Seed 和原始输出；
- 一条可卸载的运行入口，不要求其他开发者全局安装。

需要用户完成或明确授权的外部步骤：

- Rodin：注册 / 登录账号，在测试日确认商业输出、隐私和数据保留条款；Free 方案允许确认前免费生成 / Redo，先筛预览再消耗最小额度。限时折扣、当前余额和 7 天试用属于个人临时状态；不得把 Creator“每月重置 30 Credit”推断为试用必然赠送量。首个低频候选使用受监督的独立 Web 标签页。Blender Bridge v0.2.0 已证明适合登录态下的 DCC 传输，但仍依赖 Web Interface，且本轮不会自动写出贴图文件；关闭 Blender 前必须保存图像或 Pack 并重开验证。它不是无人值守 API。若 Web UI 无法固定关键参数或稳定复跑，再决定是否创建 API Key 和项目薄 Adapter；Key 不进入仓库、文档、命令参数、工具输出或聊天。
- SF3D：注册 / 登录 Hugging Face、阅读并接受权重许可证，以本机 CLI 保存 Token；Token 不进入仓库、文档、命令输出或聊天。
- 其他云服务：只有 Rodin 无法使用或未通过当前硬 Gate 时，才从 Meshy 7 与 Tripo H3.1 中选一个替补；不同时订阅多个平台。
- 大型本地环境：不为了 TripoSR 基线先安装 WSL、VS 或完整 CUDA Toolkit。只有云端实测证明自托管的成本、隐私或批处理价值明确时，才重新评估仓库外的隔离环境或短时云 GPU。

在这些外部条件成立前，项目可以完成输入、Adapter 接缝和评审模板，但不会伪造 Provider 已验证的结论。

## 8. 首轮结束条件

获得以下任一结论就结束，不无限调参：

- 某候选从 Brief 到清理后的总链明显降低新资产制作时间，并达到项目质量下限：进入第二个不同形体资产复测；
- 两路均达到质量下限但擅长不同资产风险：形成“Direct Text / Reviewed Hero”分流条件，不强行选唯一永久胜者；
- 只能提供灵感或底模，但正式资产仍需重做：归类为 Concept / Blockout 工具，不宣传为 game-ready 生成器；
- 外部候选在背面、功能结构、权利或清理成本上失败，且一个针对失败原因选择的替补仍无改善：保留现有 `bpy` + 人工 / 合规资产混合路线，等待工具真正进步；
- Harness 暴露共同缺口：只把已被多个候选证明的清洗、记录或检查步骤提升为项目工具 / Skill。

首个外部候选已经走完整链。当前先保留版本化 Harness、候选 README 与本协议，不立即创建 `blender-asset-pipeline` Project Skill：只有一个 Provider / 一个形体时，Skill 主要会重复仍在变化的 Bridge 与网页细节。第二个不同风险资产重放后，若输入、判断顺序和故障恢复重复成立，再把稳定部分提炼为 Skill 与安装指南；账号、余额、浏览器位置和临时 UI 不进入项目契约。

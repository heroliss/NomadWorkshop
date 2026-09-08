# Blender 3D 资产管线：探索基线

> 状态：**Spike v0.8**，验证于 2026-09-02。两个确定性静态道具与一个 Rodin 外部 AI Mesh 候选已经闭环 Blender 5.2、PBR 贴图、FBX 回读、拓扑 / UV 质量门禁、Unity Importer、外部 URP/Lit 材质、Prefab、Collider、次级 Universal Renderer、自动审计与人工看图；另有中性重建输入与通用 AI Mesh Intake 入口。它仍是可删除的技术证据，不是正式美术基线；人物往返、目标平台预算、权利归档和成组资产一致性仍待验证。

## 1. 当前决策

本机 Blender **5.2.1 LTS** 已足够承担当前 DCC（Digital Content Creation）基线，不需要先安装一组互相重叠的“AI Blender”插件：

- Blender 自带 Python 3.13.13 和 `bpy`，可以用 `--background --factory-startup --python` 做版本化、可重复的生成、清洗、导出、预览和统计；
- 项目以 **Asset Brief → 版本化脚本 / 可编辑源 → manifest → 中立导出 → Unity 审计 → 人工视觉复核** 为主链；Codex、其他 Agent、人和 CI 都可以重放同一个入口；
- Blender MCP 只作为未来的交互探索入口，不是资产真值。确认有价值的操作必须回写到脚本、节点组或明确的 `.blend` 源；
- AI 图生 3D、材质生成和自动绑定均视为“候选来源”。没有通过拓扑、UV、比例、权利、Unity 显示与真实镜头验收，就不是 game-ready；
- 自动指标能阻止坏文件进入项目，却不能数学证明“美”。正式视觉质量必须同时依靠艺术方向、代表性镜头、成组资产一致性和人的审美判断。

官方 [Blender 5.2 LTS](https://www.blender.org/releases/5-2/) 支持周期到 2028 年 7 月，适合作为当前稳定基线。仓库不固定某台机器的安装路径；脚本通过参数、环境变量、PATH 或 Windows 注册表定位 Blender。

## 2. 已实现的静态资产 Harness 与外部输入适配

### 2.1 几何 Smoke：废土储物箱

入口：

```powershell
pwsh -File Tools/ArtPipeline/Blender/run-blender-smoke.ps1
```

实际证据：

| 项目 | 结果 |
|---|---|
| Harness / manifest | 0.2.0 / schema 1 |
| 输出 | `.blend`、FBX、512×512 Preview、manifest |
| 几何 | 14 Mesh；784 Blender Vertex；1512 Triangle |
| Unity | 14 Renderer；3024 Runtime Vertex；1512 Triangle |
| 尺寸 | 约 1.235 × 0.958 × 0.945 m |
| 材质 | 3 个 Principled 参数材质 → 3 个外部 URP/Lit 材质 |
| 目的 | 首次证明轴、Root Identity、材质 Remap、Prefab、Collider 与 3D 显示链 |

### 2.2 PBR 与合并探针：废土水循环设施

入口：

```powershell
pwsh -File Tools/ArtPipeline/Blender/run-blender-textured-prop.ps1
```

脚本在 `--factory-startup` 下生成一个水循环设施、Blender 预览、FBX、manifest 和 12 张贴图，并把 FBX 重新导回 Blender 验证导出边界。实际证据见 [`NW_WaterRecycler_01`](../Assets/Game/NomadWorkshop/Spikes/BlenderImport/NW_WaterRecycler_01/README.md)：

| 项目 | 结果 |
|---|---|
| Harness / manifest | 0.4.0 / schema 3 |
| 来源组织 | 28 个 Part 按材质合并为 3 Mesh / 3 Material Slot |
| 来源与 FBX 回读 | 3928 Vertex；7772 Triangle；0 Degenerate Triangle |
| Unity | 3 Mesh / Renderer；8083 Runtime Vertex；7772 Triangle |
| Bounds | Blender 1.460 × 0.895 × 1.475 m；Unity 轴转换后相符 |
| 贴图 | 3 个材质族 × Base Color / Normal / Metallic Smoothness / Occlusion，512×512 |
| 材质 | 3 个项目自有 URP/Lit 材质，12 张贴图按语义配置并显式 Remap |
| 源质量 | 来源与 FBX 回读均为 0 Loose / Boundary / Non-manifold Edge、0 Degenerate UV Triangle |
| UV 指标 | 允许重叠与重复；材质平铺后的面积加权有效密度 1001.091 px/m |
| 视觉 | 1536×1024 六视图 Contact Sheet 与固定 Unity 3D Game View 均已人工检查；仍未批准为正式美术 |

初版生成曾包含 48 个零面积三角形：Blender 侧统计 7820，而 Unity 只保留 7772。流程没有把差异加入白名单，而是增加三角形面积检查、`bmesh.ops.dissolve_degenerate` 清理和 FBX 回读，最终三侧都为 7772。这说明 Harness 的关键不是“能导出”，而是**独立消费方回读后仍满足契约**。

两个静态道具入口把可重建输出写到被 Git 忽略的 `ArtPipelineOutput/`，不会修改 Blender 用户偏好，也不会直接向 `Assets/` 写入文件。这里的可重复指同版本、同参数与同 Seed 能重建相同结构、命名、尺寸、统计和视觉意图；不承诺 `.blend`、FBX 或渲染 PNG 跨运行逐字节一致。单次 manifest 的 SHA-256 保护同一轮证据不被混用，跨运行回归比较结构化契约和经过批准的视觉指标。

### 2.3 既有几何中性渲染夹具（非默认生产输入）

入口：

```powershell
pwsh -File Tools/ArtPipeline/Blender/run-blender-ai-mesh-input.ps1
```

它读取 2.2 已生成的 `.blend`，隐藏棚拍地面和有色预览灯，改用透明 Film、中性三点光和原 Hero 相机，输出到：

```text
ArtPipelineOutput/AIMeshInput/NW_WaterRecycler_01/
  NW_WaterRecycler_01_ai_mesh_input.png
  manifest.json
```

Harness `0.2.0` / schema `1` 在 Blender 5.2.1 LTS 产出 640×640 RGBA8，约 27.4% 像素属于可见物体。manifest 固定源 `.blend`、渲染脚本和本次 PNG 的 SHA-256；源文件与 Blender 用户设置均不修改。这个夹具适合粗 Blockout 细化、算法重建回归或给**不同资产**提供风格参考；`NW_WaterRecycler_01` 已有生产可用几何、UV 和 PBR，重新生成同一模型不会证明 AI 节省资产成本，因此当前 PNG 不上传 Rodin。

### 2.4 外部 AI Mesh Intake：Rodin 野战厨房

`NW_FieldKitchen_01` 使用冻结 Hero 图完成首条 `Rodin Gen-2.5 → Blender Bridge → Blender Intake → Unity URP` 链。网页先确认非对称 Quad 模型，再以 Native / High / De-light / PBR Detail 7 和 8K 生成设置提交材质；提示词明确青色旧漆、不锈钢工作面、深色结构、橙色安全件、细微沙尘与磨损，并禁止烘焙光照、文字、Logo 和额外物件。网页预览不是项目证据，最终只认 Bridge / Blender 实际收到的文件。

通用 Intake 入口：

```powershell
pwsh -File Tools/ArtPipeline/Blender/run-blender-ai-mesh-intake.ps1 `
  -SourceBlend <candidate.blend> `
  -AssetId NW_FieldKitchen_01 `
  -SourceObject model
```

带独立门、抽屉或其他 Empty Pivot 的道具应把语义根节点传给 `-SourceObject`，并显式加 `-PreserveHierarchy`。该模式保留根节点以下的 Empty / Mesh 与父子关系，关闭 FBX Object Axis Bake，仍不导出动画；静态单 Mesh 不加此开关，保持原有路径。

它在后台打开源 `.blend`，但只对复制的数据块执行最小 `Mesh.validate`；源文件不会被覆盖。输出包含打包 `.blend`、FBX、GLB、归档贴图、URP MetallicSmoothness 和带 SHA-256 的 `intake-report.json`，状态固定为 `inspected / candidate-only`，不会因自动检查通过而写成 game-ready。

本轮真实证据：

| 项目 | 结果 |
|---|---|
| Blender 源 | 1 Mesh、18,924 Vertex、37,908 Triangle、18,940 Quad、无 N-gon |
| 源问题 | 5 个重复面；6 个封闭几何岛；0 Loose / Boundary / Non-manifold Edge |
| UV | 有效层 `st`；范围 0.001–0.999；0 Degenerate UV Triangle |
| 自动规范化 | 只在副本清除 5 个重复面；FBX 为 37,903 Triangle |
| 格式回读 | FBX 37,903 Triangle，与清理副本一致；GLB 37,902，只保留作参考 |
| PBR | Base Color / Metallic / Roughness / Normal 完整；无 AO / Emission |
| 实际贴图 | 四张 2048×2048，而不是网页选择的 8K；Unity 只纳入 Base Color、Normal 和派生 MetallicSmoothness |
| Unity | 1 Mesh / 1 Material Slot、22,058 Runtime Vertex、37,903 Triangle；Bounds 与 Blender 轴转换后逐轴一致 |
| 视觉 | 固定 URP 相机可读出旧漆、金属、橙色操作件、织物和软管；法线没有明显反转；仍需人工批准 |

Rodin Blender Bridge v0.2.0 对低频受监督传输有价值：官方 `Send → DCC → Blender` 能把 Mesh、材质节点和图像像素送入已登录的 Blender，不必依赖 Chrome 下载。但它不是无人值守 API，也不是完整资产归档工具。本轮图像路径指向 `//textures/...`，像素存在于 Blender 内存，磁盘文件却没有自动写出；关闭前必须保存图像或 `Pack Resources`，再重新打开核对贴图尺寸、色彩空间和 Pack 状态。网页直下还受订阅弹窗限制，项目没有绕过付费边界。

Unity 侧的 [`NomadAiMeshCandidateAssetPipeline`](../Assets/Game/NomadWorkshop/Editor/NomadAiMeshCandidateAssetPipeline.cs) 再核对 Intake 脚本、FBX 和三张运行时贴图哈希，生成独立 URP/Lit 材质、Prefab、BoxCollider 与预览场景。完整候选说明见 [`NW_FieldKitchen_01_Rodin`](../Assets/Game/NomadWorkshop/Spikes/BlenderImport/NW_FieldKitchen_01_Rodin/README.md)。单一合并 Mesh 可以快速验证画面，却不能满足柜门、抽屉、软管或顶层物件的独立动画、损坏与替换；正式化时应在 Blender 拆件、重拓扑和建立 Pivot，而不是靠运行时脚本伪造可编辑性。

### 2.5 Smart Mesh 柜门正式化 Spike

用户下载的 Rodin Smart Mesh 候选通过 `Blender --background + bpy` 完成了全程无界面检查、拆件试验、渲染、FBX 导出与回读。源 FBX 尺寸为 2.302394 × 0.877246 × 2 m，包含 6,219 Vertex、7,078 Polygon、12,296 Triangle 和 43 个连通分量；但最大分量独占 5,419 Vertex / 10,904 Triangle，并把柜体和中央白色柜门融合在一起，因此 `Separate by Loose Parts` 不能取得可动门。

首轮中央门实验进一步暴露了“按空间框删面”的危险：目标只有 `x=-0.335…0.425`，但 AI 网格存在跨区大面；“中心或任一顶点落入范围即删除”选中 379 面，其中 62 面只因顶点重叠被带入，所选面顶点实际扩散到 `x=-0.888…1.037`。台面边、左右门边和底梁因此真实破损；只按面中心也仍会选中跨部件大面。该版本已经废弃，不能再称为受控裁剪。

修正版从未裁剪源重新生成，删除 Rodin 面数为 0。它用浅内衬遮住三个融合原门，再按语义重建左下门、中央门与右下门，每扇门拥有独立 Empty Pivot，门板、把手和活动铰链作为子物体；左侧通风/按钮区及右侧抽屉保持静态。资产不含 Action、关键帧或 NLA，目标是在 Unity 由设施状态直接旋转 Pivot，复杂连续动作才另做 Clip。

这轮也暴露了 Harness 边界：静态 Intake 原本只保留 Mesh 和根节点，会删除中间 Pivot Empty。现已增加显式 `-PreserveHierarchy` 模式，保留根节点以下的 Empty / Mesh、父子关系与本地轴，同时关闭会破坏铰链语义的 FBX Object Axis Bake；静态资产默认行为不变。三门 Spike 的 48 个节点（4 Empty、44 Mesh、47 条父子关系）在 FBX / GLB 回读后完全一致。12,812 个源 Triangle 经 Bevel 求值为 16,940，FBX / GLB 也均为 16,940；这同时验证了 Evaluated Geometry 统计口径。

当前方案证明 AI Mesh 可以作为**外观与主体底模**，并以低成本语义重建可动件；它不是完整重拓扑。浅内衬只适合验证开合与近中景遮挡。若玩法需要真实储物深度、物品进出、柜内污染或碰撞，必须把整个下柜模块重建为干净拓扑，而不是继续对融合三角面局部补洞。实验脚本和产物仍留在被忽略目录，尚未升级为生产资产或通用 Skill。

首轮门状态图在柜腔和门两侧暗部出现细颗粒。关闭 Normal Map 后现象未消失，全模型替换为统一中性材质后仍存在，因此排除了 UV、Base Color 和法线贴图损坏；根因是 EEVEE 默认 64 个 Render Sample / 1 条 Shadow Ray 在软阴影暗部留下的单帧随机采样。离线审查图提高到 256 / 8 后颗粒基本消失，模型与 FBX 无需返工。后续遇到相似问题应先做“原材质 → 无法线 → Clay Override”的最小对照，再决定修改贴图、法线、几何还是渲染设置。

第二轮人眼复核先发现首轮白门浮雕嵌入过浅、Bevel 过大，视觉上像空心夹层；随后在 `.blend` 中又确认左右原门确有删面缺损。前者是重建件造型问题，后者是跨区选择造成的真实拓扑破坏，两者不能混为贴图或阴影问题。当前三门非破坏版本同时规避了这两类错误。

## 3. 贴图、材质和 Shader 到底如何生成

### 3.1 当前贴图不是黑盒 AI 输出

`blender_textured_prop.py` 使用固定 Seed 的可平铺数学噪声、划痕线段和凹坑规则，先生成一张高度 / 磨损 / 灰尘场，再确定性写出 RGBA8 PNG。它不需要 Pillow、NumPy、云服务或 Blender 插件：

| 贴图 | 生成逻辑 | 色彩空间 / 通道 |
|---|---|---|
| Base Color | 基础色、宏观色差、裸露金属和灰尘按磨损 Mask 混合 | sRGB；RGB |
| Normal | 对可平铺高度场做中心差分，归一化为 OpenGL 切线空间法线 | Linear；RGB；+Y |
| Metallic Smoothness | 完整涂层与裸露金属按磨损混合，灰尘降低光滑度 | Linear；R=Metallic，A=Smoothness |
| Occlusion | 从局部高度凹陷得到轻量 Cavity 近似 | Linear；G=Occlusion |

这里的 Occlusion 是程序化的局部凹陷近似，不是对最终 Mesh 烘焙的真实 AO；划痕也不是根据模型边缘、遮挡、重力和操作区域生成。它适合验证 Unity PBR 数据契约和低成本风格原型，不能替代正式 Mesh-specific Texturing。

### 3.2 Blender 中的材质只负责 DCC 预览

脚本为每个材质族创建 Principled BSDF 节点图：UV 经 Mapping 控制平铺；Base Color 与 Occlusion 相乘；Metallic Smoothness 的 R 接 Metallic、A 经 `1 - x` 接 Roughness；Normal 经 Tangent Normal Map 节点接入。Blender 预览用来尽早发现贴图错位、过度起伏、颜色和材质响应问题。

FBX 只携带 Mesh、UV、Normal / Tangent 和材质槽身份。项目**不尝试把 Blender 节点图翻译成 Unity Shader Graph**，因为两个渲染器的节点、光照和打包语义不同；这种隐式翻译很难审计，也不利于后续替换。

### 3.3 Unity 拥有最终 Shader 和导入语义

Unity Editor 工具读取 manifest，逐张核对哈希与语义并配置 Importer：

| 来源 | Unity Importer | URP/Lit 属性 |
|---|---|---|
| Base Color | Default、sRGB | `_BaseMap` |
| OpenGL Normal +Y | Normal Map、Linear、不翻 G | `_BumpMap` + `_BumpScale` |
| R Metallic / A Smoothness | Default、Linear、保留输入 Alpha | `_MetallicGlossMap`，Smoothness 来源为 Metallic Alpha |
| G Occlusion | Default、Linear | `_OcclusionMap` + `_OcclusionStrength` |

材质 Keyword 通过 URP 官方 Editor helper 重建，而不是手写一组容易随包版本漂移的关键字。三个材质按 FBX 源材质名显式 Remap，Prefab Root / Visual 保持 Identity；Unity 最终资产不依赖 Blender 内嵌材质的显示结果。

Rodin 候选证明同一契约也适用于 AI 贴图，但来源通道并不天然符合 Unity 打包：Intake 保留 Base Color 与 Tangent Normal，另把独立 Metallic、Roughness 合成为 `R=Metallic, A=1-Roughness`。网页写着 PBR 或 8K 不能代替落盘检查；颜色空间、实际尺寸、Alpha、法线方向和 Unity Shader 响应都要由消费方重验。当前法线按 `flipGreenChannel=false` 显示正常，若后续 Provider 改变约定，应把 A/B 看图结果写回 manifest，而不是永久硬编码“所有 AI 法线都不翻 G”。

自定义 Shader 只在 URP/Lit 不能表达明确的游戏需求时创建，例如沙尘覆盖、积水 / 湿润、损坏渐变、全局风摆和选择高亮。普通金属、油漆、塑料和布料优先共享少量 Shader 与材质族，避免“每个资产一个 Shader”造成变体、维护和合批成本。

## 4. 怎样提高而不是假装“保证”美术效果

好效果不是某个生成按钮的属性，而是以下闭环共同形成的：

1. **先有艺术方向和用途**：Asset Brief 明确镜头距离、现实尺寸、轮廓、功能故事、材质族、色板、参考图和禁止项。水循环设施应一眼看出进水、过滤、储水、维修与危险区域，而不只是“复杂机器”。
2. **先看大形再看纹理**：固定镜头下的轮廓、比例、负空间和功能层级不成立时，不用高分辨率贴图掩盖。当前青色主壳、黑色结构、橙色操作点只是最小层级验证。
3. **测量硬约束**：检查单位、Pivot、Bounds、退化面、非流形风险、UV、材质槽、三角面、Runtime Vertex、贴图通道、色彩空间、Collider、Renderer 数与来源权利。
4. **用中立 DCC 证据找源问题**：输出 Hero / Front / Side / Top / Wireframe / UV Checker 的固定 Contact Sheet。当前 Harness 会把它与 manifest 哈希、尺寸和布局一起导入 Unity；自动化只证明证据完整，仍必须由人查看是否裁切、轮廓失衡、拓扑异常或 UV 拉伸。
5. **在 Unity 代表性镜头验收**：同一 URP、灯光、后处理、相机高度、居民比例和目标分辨率下检查，而不是只相信 Blender 棚拍。法线方向、材质过黑、细节频率和实际可读性经常在这一层暴露。
6. **成组审查而非单件自嗨**：至少把 3–5 个同材质族资产放在一起比较色相、明度、磨损密度、边缘宽度、Detail Scale 和 Texel Density，防止每件单看不错、组合后风格破碎。
7. **人工 Art Pass**：自动门禁能淘汰错误文件，视觉模型可提供批评，但最终仍要有人判断焦点、节奏、叙事、吸引力和是否符合《游牧工坊》。不把单张截图或 AI 自评分当作批准。

当前水循环设施只是“管线与方向 Probe”：轮廓和材质层级已可读，但磨损仍显程序化，缺少标签、接缝、紧固件、维护痕迹和功能细节。正式化时更适合在 Blender 完成形体与 UV，再用专门的 PBR 工具做基于 Curvature / AO / Position 的 Mesh-specific Mask 和人工局部修饰。

当前 `1001.091 px/m` 是 `sqrt(sum(UV 面积 × 材质平铺面积) × 分辨率² / 表面积)` 得到的**有效平铺纹素密度**。它允许共享、重叠 UV 和重复采样，可用来发现导出前后突变，却不表示 512×512 贴图具有同等的唯一细节或内存成本，也尚未形成 PC / Mobile 的正式预算。真正预算还要结合唯一 UV 覆盖、屏幕占比、压缩格式、Mip、实例数量和代表性镜头。

## 5. 网络调研：哪些 Blender AI 工作流值得吸收

### 5.1 Agent / MCP：适合试形，不应成为唯一真值

[Blender 官方 MCP Lab](https://www.blender.org/lab/mcp-server/) 已发布，支持 Blender 5.1+，本机 Blender 5.2.1 LTS 满足版本要求。它通过 Add-on + 本地 MCP Server 把当前 Blender Session 的 Python API 暴露给 MCP Client，适合自然语言查询场景、检查关系和做少量交互修改；它并不是另一套建模引擎。官方明确说明它会执行 LLM 生成的代码、没有内置护栏，并建议在虚拟机或无敏感数据环境中运行。[ahujasid/blender-mcp](https://github.com/ahujasid/blender-mcp) 等社区实现还能接入资产库和若干 3D 生成服务，但任意代码、凭据、遥测和第三方服务边界都需要额外审计，当前没有证据值得与官方实现并存。

三种控制面的职责不同：

| 控制面 | 最适合 | 成本与边界 |
|---|---|---|
| `Blender --background + bpy` | 批量导入、统计、确定性改造、渲染、导出、回读 | 无焦点和截图循环，最快且可版本管理；视觉判断仍看最终渲染 |
| 官方 Blender MCP | 查询当前打开场景、定位问题、小范围“看—改—再看” | 比桌面坐标控制稳定，但仍执行生成的 Python；应只连接可丢弃副本并限制凭据与敏感目录 |
| Windows UI / 截图控制 | 原生文件选择器、授权弹窗、只能人工确认的视觉操作 | 慢、脆弱、上下文成本高，只作为兜底 |

项目当前决定：

- **生产主通道使用无头 `bpy`**；本轮 Smart Mesh 检查、三门 Pivot、状态渲染、FBX / GLB 导出与回读都已证明不需要控制鼠标；
- 当前仅安装 Rodin Bridge，没有安装 Blender MCP。官方 MCP 作为下一阶段可选的交互补充，不是本轮阻塞项；
- 当“看图—小改—再看图”的次数成为可测量瓶颈时，在可丢弃 `.blend` 和隔离环境中试官方实现；
- MCP 产生的有价值结果必须固化为脚本、节点组、Asset Brief 或经版本管理的源文件，再进入同一 Harness；
- 不同时安装多个功能重叠的 MCP，也不把 API Key 写入仓库。

两个近期 GitHub 项目提供了有用的设计启发，但成熟度不足以直接成为依赖：

- [SCRIPT3D](https://github.com/llada60/SCRIPT3D) 把自然语言变成白名单 JSON 操作，保存生成脚本、Seed、资产元数据和场景索引，并加入渲染反馈；“资产是可重建记录，不只是一个 Mesh”与当前 manifest 路线一致。
- [Blender-AI-Agent](https://github.com/ahmedsayed1911/Blender-AI-Agent) 用受限 MCP 工具、只读视觉评审、作业修订和回滚隔离生成过程；其 README 也明确称自己仍是实验系统，不是生产级游戏资产管线。
- [bforge / studio-foundation](https://github.com/lxsolutions/studio-foundation) 展示了 Typed Operations、真实 Blender 集成测试、Contact Sheet、预算 Gate 和确定性基准的高价值思路；但仓库使用 PolyForm Perimeter 的 source-available 许可证且面向 Godot / Web 栈，当前只借鉴理念，不复制代码或引入整套平台。

### 5.2 程序化资产：Infinigen 适合研究“生成器”，不是直接搬进游戏

[Princeton Infinigen](https://github.com/princeton-vl/infinigen) 是 Blender 上的 BSD-3-Clause 程序化自然 / 室内场景系统，资产由固定 Seed 的 Factory 和数学规则生成，也支持把 Geometry / Shader Nodes 转为 Python。它对荒漠地貌、岩石、植被、云雨、散布与成组变体很有启发。

但 Infinigen 主要为合成视觉数据和高细节离线场景设计，强调真实几何，依赖和复杂度远高于当前 Unity 道具 Harness。项目不整包集成；后续按需研究它的 Fixed Seed、Factory、参数覆盖、节点转代码和资产级导出模式，再用自己的 Unity 预算与 Shader 契约重写最小生成器。

### 5.3 图生 3D / PBR：把模型当候选底模

| 候选 | 适配判断 | 当前决定 |
|---|---|---|
| [TripoSR](https://github.com/VAST-AI-Research/TripoSR) | MIT；单图快速重建，官方约 6GB VRAM；默认 Vertex Color，可烘焙颜色纹理，但不交付完整 Metallic / Roughness / Normal / AO PBR 族 | 保留为可解释的本地形体基线；当前 Windows 缺少它所需的编译工具链，且即使成功也仍要独立补材质，不优先安装 |
| [TRELLIS.2](https://github.com/microsoft/TRELLIS.2) | MIT；可从图生成带 Base / Roughness / Metal / Opacity 的 PBR GLB；官方要求 Linux 与至少 24GB NVIDIA GPU | 本机不适合；以后只在确有高价值输入时评估云 GPU |
| [Hunyuan3D 2.1](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1) | PBR 能力完整，但官方给出的形体 / 贴图 / 全流程显存约 10 / 21 / 29GB；[社区许可证](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE) 还有地域和使用边界 | 不作为面向全球 Steam / Mobile 的默认生产来源 |
| [Meshy](https://docs.meshy.ai/)、[Tripo API](https://developers.tripo3d.ai/en/models/v3-1)、[Hyper3D Rodin](https://docs.hyper3d.ai/en) | 提供图 / 文生 3D、PBR、重拓扑、分件，以及不同程度的自动绑定 / 动画云服务 | Provider 按资产类型挑战同一项目 Contract；只有真实胜出且达到批量规模者再接付费 API，不预设全项目唯一平台 |

选择 Provider 时不要只比较宣传图。统一记录：输入参考、Prompt / Seed / 模型版本、耗时与费用、许可证、Mesh / Material 数、三角面、非流形、UV、贴图通道、Unity 修复时间、固定镜头评分和最终是否保留。**总清理时间**比“生成用了几秒”更接近真实生产价值。

### 5.4 材质与贴图：独立工具比把扩散模型塞进 Blender 更稳

推荐按成本分三层：

1. [Material Maker](https://www.materialmaker.org/)：开源、节点式程序化 PBR，能导出 Unity / Unreal / Godot；适合建立可复用的沙尘、旧漆、橡胶、织物等材质族。它更接近免费 Substance Designer 替代，值得作为首个独立工具试用。
2. [ArmorPaint](https://armorpaint.org/)：开源 PBR 3D Painting，支持节点、烘焙、照片转 Base / Height / Normal / Occlusion / Roughness，以及 Unity 通道打包；官方也明确标为 Alpha、有粗糙边缘。它适合低成本验证 Mesh-specific Wear，不立即承担唯一生产职责。
3. [Adobe Substance 3D Painter / Sampler](https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/export/export)：Painter 的层、Mask、烘焙和可分享 Export Template 更适合英雄道具；Sampler 的 [Image to Material](https://experienceleague.adobe.com/en/docs/substance-3d-sampler/using/filters/tools/image-to-material) 与 [Generative Workflows](https://experienceleague.adobe.com/en/docs/substance-3d-sampler/using/features-and-workflows/generative-workflows) 能从照片或文本取得可平铺 PBR 起点。若免费方案打磨时间明显更高，这是最值得付费的软件方向。

[Dream Textures](https://github.com/carson-katri/dream-textures) 与 [ComfyUI-BlenderAI-node](https://github.com/AIGODLIKE/ComfyUI-BlenderAI-node) 展示了 Blender 内直接生成 / 投射 / 烘焙的可能，但现有说明集中在更老的 Blender 版本，并引入模型、Python 依赖和节点兼容风险。当前不安装。若后续采用 ComfyUI，先把它作为独立、可替换的生成服务，通过文件 + manifest 接入，避免污染 Blender 5.2 环境。

### 5.5 绑定与动画：AI 可加速，骨架契约仍先于模型

[Meshy Animate](https://docs.meshy.ai/en/webapp/guides/animate) 当前提供 Humanoid / Quadruped 自动绑定与 500+ 预设，[Tripo Auto Rig](https://developers.tripo3d.ai/en/docs/animations-rig) 支持双足和多类非人形，[Mixamo](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) 则仍是可用时的免费双足动作来源；Puppeteer、Kimodo 和视频动捕等开源方案可以继续作为实验候选。它们都不能替代项目已经验证的 Unity Humanoid + 合规共享动画基线，也不自动保证权重、Foot Slide、Hand Contact 或 Root Motion 正确。

正式居民路线仍是：先锁一套共享 Humanoid 骨架和模块接口，再用 AI / 第三方动作扩充候选；每个角色检查 Bind Pose、权重、Root Motion、Foot / Hand 接触与 Unity Avatar。表情、布料和英雄级专用动作后置。

模型、材质、状态、动画与音效如何组成同一个可复用资产族，见[《游牧工坊》3D 资产族生产策略](nomad-workshop-asset-family-pipeline.md)。

## 6. 插件与安装策略

### 当前已够用

| 能力 | 当前入口 | 说明 |
|---|---|---|
| FBX / glTF | Blender 内置 I/O | 已在 `factory-startup` 下验证 FBX 导出与回读 |
| 几何、UV、材质节点 | Blender 原生 + `bpy` | 不依赖用户工作区或第三方节点包 |
| Humanoid 原型 | 内置 Rigify | 已验证可加载；正式使用前另做 Unity Avatar 往返证据 |
| 常用节点操作 | 内置 Node Wrangler | 可人工按需启用；当前 Harness 不依赖 |
| 批处理 / 统计 / 预览 | 版本化脚本 | 跨 Agent、可审查、可测试 |
| Rodin 低频 DCC 传输 | Blender Bridge v0.2.0 | 已验证 Mesh / PBR 像素传入；仍需手动保存或 Pack，不能替代 Intake 与 API |

### 触发真实痛点再装

- Material Maker：当需要第二个可复用材质族或程序贴图库时试；
- ArmorPaint / Substance Painter：当首个候选道具需要基于 Mesh 的边缘磨损、标签、泄漏和维修痕迹时试；
- Blender Retarget / Auto-Rig Pro：当 Unity Humanoid 无法覆盖实际重定向、Root Motion 或批处理痛点时试；
- UVPackmaster：当真实资产集证明原生 UV 打包消耗显著人工时间或浪费可测量 Texel Density 时试；
- 官方 Blender MCP：当交互修改次数明显高于脚本维护成本时，在隔离环境试。

稳定前不写一份要求所有人安装十几个插件的指南。每个新增工具应回答：它替代哪一步、输入输出是什么、许可证与版本如何锁定、如何卸载、没有它能否重建、Unity 如何验收。

## 7. 正式资产的目录与所有权

当前可重建 `.blend`、批量输出和 DCC 预览留在被忽略的 `ArtPipelineOutput/`；仓库只提交小型 FBX、manifest、贴图与 Unity 生成资产作为跨边界证据。正式资产达到实际规模后再建立：

```text
ArtSource/NomadWorkshop/            # 可编辑 .blend、贴图工程、参考和 Asset Brief
Assets/Game/NomadWorkshop/Content/  # Unity Runtime 导出与配置
docs/asset-register.*               # 来源、许可、生成信息与验收状态（达到规模后）
```

所有权链：

```text
Asset Brief
  → Source / Provider 原始结果
  → Blender 规范化源
  → FBX / glTF + Texture + manifest
  → Unity Import Settings / Material / Prefab
  → 代表性游戏镜头与目标平台验收
```

- `.blend` 是可编辑源，不被运行时代码直接引用；
- Unity 只消费明确导出的 Mesh、贴图和配置；
- 生成平台原始结果与人工清理后源文件必须区分；
- 第三方或生成资产记录来源、模型 / 工具版本、许可证、修改与最终用途；
- 稳定 Asset ID 服务于替换，游戏逻辑不依赖 Provider 文件名；
- 二进制源达到真实规模后再评估 Git LFS，不为一个 Spike 预建存储体系。

## 8. 每类资产的最小 Contract

| 类别 | 关键问题 |
|---|---|
| 身份 | Asset ID、用途、来源、Prototype / Candidate / Approved 状态 |
| 几何 | 米制尺寸、Pivot、朝向、Root Transform、三角面、退化 / 非流形、材质槽、LOD |
| 交互 | Collider、NavMesh / 占地、访问面、Hand / Tool / VFX / SFX 锚点 |
| 材质贴图 | UV / Texel Density、色彩空间、PBR 通道、法线约定、分辨率、压缩、复用策略 |
| 角色动画 | 骨架、Avatar、Bind Pose、Root Motion、Clip、循环、Foot / Hand 接触 |
| 性能 | 目标镜头、实例数、Renderer / Draw Call、GPU / CPU / 内存预算与目标平台 |
| 权利 | 输入权利、Provider / 模型 / 日期、Prompt / Seed、许可证、人工修改与可替换性 |
| 证据 | DCC Contact Sheet、Unity 固定镜头、Importer、运行时 / 性能、人工未决问题 |

对《游牧工坊》的固定斜俯视镜头，轮廓、尺寸、材质层级、交互锚点和共享骨架的价值高于隐藏面的细节密度。

## 9. 推荐生产闭环

这里描述 DCC / Unity 主链；Art Bible、状态矩阵、Provider 分工、动画和 SFX 接缝的完整上游合同见[《游牧工坊》3D 资产族生产策略](nomad-workshop-asset-family-pipeline.md)。

### 环境模块和普通道具

1. 写用途、尺寸、轮廓、接口、材质族、镜头和参考；
2. 用 `bpy`、手工、AI 底模、程序生成或合规第三方源取得候选；
3. 外部候选先运行 AI Mesh Intake，保存原始字节、贴图与缺陷；Blender 再统一 Scale / Rotation、Pivot、命名、Mesh、UV、可动部件和材质槽；
4. 输出 FBX / glTF、Texture、Contact Sheet、统计、来源和 Hash；
5. Unity Editor 工具按 manifest 配置 Importer、材质、Prefab 与 Collider；
6. 在真实车辆 / 停靠镜头检查比例、遮挡、交互、光照和批量实例；
7. 保留、返工或删除，不因已经生成或付费而降低标准。

### 居民与动画

1. 锁一套共享 Humanoid 骨架、基础比例和少量通用动作；
2. 头发、服装、背包、工具和材质做模块差异；
3. 设施声明站位、朝向、动作、手部目标和工具挂点；
4. Unity 先验证 Walk / Carry / Repair / Rest 等共享动作；
5. 接触误差有真实证据后，再做权重、IK、Root Motion 或专用 Clip；
6. 面部绑定、口型、布料和多套骨架继续后置。

推荐混合生产：**项目拥有英雄轮廓、模块规范、材质族和验收；合规底模与通用动作可以复用；AI 负责概念、候选、变体和机械清理；人负责功能叙事、关键局部与最终审美。** 完全自产并不自动更便宜，全部购买也不自动更一致。

## 10. 下一步证据

首轮 Brief、概念收敛、按需参考、Provider 顺序、清理记账和结束条件已经固定在[《游牧工坊》首轮 AI 资产生产实验协议](nomad-workshop-ai-mesh-blind-test.md)。

1. 三门 Pivot Spike 与层级保留 Intake 已完成：融合 AI Mesh 不能可靠自动拆件，但“保留主体底模 + 非破坏遮挡 + 语义重建少量可动件 + 稳定 Pivot”成立。下一步把该层级放入隔离 Unity 预览，由运行时状态旋转 Pivot，验证坐标、材质、Collider、门间干涉和交互接缝；暂不把一次性硬编码脚本包装成通用 Skill。
2. 选择一个形体和材质风险不同的第二候选重放 Intake，并记录它是否也需要语义重建。只有重复证明脚本、目录和判断顺序稳定后，才创建 `blender-asset-pipeline` Project Skill；当前 Harness + 文档已经可复用，过早增加 Skill 只会复制尚在变化的 Provider 细节。
3. 选择 Material Maker 或 ArmorPaint 做首个 Mesh-specific 材质试验，验证 Curvature / AO / Position Mask、标签和泄漏痕迹，再决定是否值得购买 Substance。
4. 把三个同材质族资产放入接近车辆甲板的代表性镜头，建立首份与平台相关的 Renderer、Draw Call、Runtime Vertex、贴图内存和屏幕可读性基线；届时再定义 Texel Density 区间。
5. 为共享 Humanoid 建立一次 Blender → Unity 往返证据；骨骼与动画不能直接套用静态道具的轴烘焙策略。
6. 在商业使用前归档 Rodin 当日条款、订阅层与输出权利。当前试用账号、网页余额和生成成功只证明技术链，不等于项目已经完成授权 Gate。

官方基础参考：[Command Line](https://docs.blender.org/manual/en/5.0/advanced/command_line/index.html)、[Python API Tips](https://docs.blender.org/api/5.0/info_tips_and_tricks.html)、[Rigify](https://docs.blender.org/manual/en/latest/addons/rigify/index.html)、[FBX Operator](https://docs.blender.org/api/current/bpy.ops.export_scene.html)。

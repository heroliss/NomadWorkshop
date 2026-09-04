# Rodin 野战厨房 AI Mesh 候选

这是首条经过“概念图 → Rodin 图生 3D → Blender Bridge → Blender Intake → Unity URP”全链路验证的外部 AI Mesh 候选。它用于验证工作流和发现真实缺口，当前仍是可删除的 Spike，不是已批准的生产美术。

## Provider 输入证据

- Provider / 模型：Hyper3D Rodin Web / Gen-2.5（页面显示 `0702`）。
- 输入：`NW_FieldKitchen_01/v0.1/hero-imagegen-v1.png`，1254×1254，SHA-256 `1543CB903D0C3E76DF86D0C0E62B14CD6948E3AE661C6E92BA292D35D30F458A`。
- 几何：单候选、Medium Thinking Effort、Use Recommended、Quad、非对称。
- 材质：Native、High、网页 8K 设置、De-light、PBR Detail 7；Bridge 实际交付 2048×2048。

材质原始提示词：

```text
Worn mobile wasteland field kitchen. Muted teal painted steel cabinet panels with restrained chipped edges and subtle sand abrasion; dark charcoal structural steel frame and hardware; brushed stainless-steel cooker, continuous worktop, sink and faucet; black rubber hoses and wheels; small safety-orange controls and latches; practical repairable industrial construction. Semi-realistic stylized PBR, coherent material scale, clean readable value grouping, subtle dust in crevices, minimal rust, no baked lighting, no dramatic highlights, no text, no logo, no added objects.
```

输入图仍属于被忽略的本地实验产物；本目录的可验证交付边界从 Intake 报告、FBX 和运行时贴图开始。若该候选晋升正式资产，应把冻结概念、完整 Provider 条款快照与可编辑 Blender 源迁入届时确定的 `ArtSource` / 资产登记体系，不能只依赖本机实验目录。

## 可复现入口

1. 外部生成结果先保存为独立 `.blend`，不要覆盖手工源文件。
2. 在仓库根目录运行 `Tools/ArtPipeline/Blender/run-blender-ai-mesh-intake.ps1`，生成带哈希的 Intake 报告、清理副本、FBX、GLB 与 URP MetallicSmoothness 贴图。
3. 只把审查需要的 FBX、Base Color、Normal、MetallicSmoothness 和 Intake 报告复制到本目录。
4. Unity 执行菜单 `Assets/SSFramework/游牧工坊/AI Mesh Spike/配置并审计 Rodin 野战厨房`。
5. 运行 `NomadAiMeshCandidateAssetPipelineTests`，并实际检查预览场景或截图。

Unity 导入脚本会验证 Intake 脚本与资产 SHA-256，配置贴图色彩空间、法线类型、FBX 坐标/材质重映射，生成外部 URP/Lit 材质、带 BoxCollider 的 Prefab 和隔离预览场景。审计通过仍保持 `manualArtReviewRequired=true`、`productionApproved=false`。

## 本轮真实结论

- Rodin 网页材质设置选择过 8K，但 Blender Bridge 实际交付四张 2048×2048 贴图；预算和质量判断必须以落盘文件为准。
- Bridge v0.2.0 把图像像素送进了 Blender，但最初只留下 `//textures/...` 路径，没有自动写出对应文件。需要保存图像或 Pack Resources 后再关闭 Blender。
- 源模型有 18,924 顶点、37,908 三角形和 5 个重复面。Intake 保留源 `.blend`，只在导出副本上运行 `Mesh.validate`；Unity 主 FBX 是 37,903 三角形。
- FBX 往返与清理结果一致；GLB 往返少 1 个三角形，因此本轮 Unity 以 FBX 为主，GLB 只保留作跨工具参考。
- UV 位于 0–1 范围，核心 Base Color / Metallic / Roughness / Normal 映射完整；Unity 由 Metallic 与 Roughness 派生 `R=metallic, A=1-roughness` 的 MetallicSmoothness。
- 候选是单一合并 Mesh，内部有 6 个封闭几何岛。它适合静态原型，但门、抽屉、软管等需要独立动画、损坏或交互时，应由 Blender 重拓扑/拆件，而不是继续堆 Shader 或脚本补丁。
- 当前没有 AO 与 Emission。它们不是 URP PBR 成立的硬前提，只有画面或玩法证据表明需要时再补。

## Smart Mesh 柜门正式化实验

后续下载的 Rodin Smart Mesh 版本是 2.302394 × 0.877246 × 2 m，包含 6,219 顶点、12,296 三角面和 43 个连通分量；最大分量仍把柜体、台面和三个下柜区域融合在一起。Loose Parts 因而不能直接拆门。首轮中央门实验采用“面中心或任一顶点落入矩形即删除”，目标范围只有 `x=-0.335…0.425`，实际被选面的顶点范围却扩散到 `x=-0.888…1.037`：62 个面仅因跨区顶点被误带入，台面边、左右柜门边和底部横梁均受损。即使改为只按面中心选择，跨越多个部件的大面仍会连带删除，因此该破坏性裁剪版本已经废弃，不能作为可交付模型。

修正版从未裁剪源文件重新生成，不删除任何 Rodin 面。三个原始门区由略微前置的浅内衬遮挡，再重建 `LeftLower`、`Center`、`RightLower` 三套封闭门板、把手、活动铰链和独立 Pivot。左下门上沿止于 `z=0.685`，通风口、旋钮和控制条始终属于静态主体；右侧上方抽屉也不随下门旋转。资产没有 Action、关键帧或 NLA，FBX 回读后逐个程序旋转 Pivot，确认 Pivot 位置固定，门板与把手随父节点移动。

静态 AI Mesh Intake 原本会删掉 Mesh 之间的 Empty，不能用于这类资产。本轮新增显式 `-PreserveHierarchy` 模式；默认静态路径保持不变。三门资产 Intake 记录 48 个导出节点（4 Empty、44 Mesh、47 条父子关系），FBX 与 GLB 回读完全一致；12,812 个源三角面经 Bevel 求值为 16,940，两个格式也都回读为 16,940。该证据只证明层级和交换格式可靠，不自动批准美术质量。

当前内衬是非破坏式、浅深度的遮挡结构，优点是不会再次损伤融合底模，足以验证 Unity 运行时开合；缺点是尚不是真正可放置物品的深柜腔。若玩法需要玩家看见库存、搬运物真实进出或柜内污染/损坏，生产版本应把整个下柜模块重拓扑，而不是继续对融合三角网格做局部补洞。

首轮图片中门两侧暗部的细颗粒不是 Rodin 贴图或 UV 错位：禁用 Normal Map、再用统一 Clay 材质重渲染后颗粒仍在；把 EEVEE 离线审查设置从默认 64 Render Sample / 1 Shadow Ray 提高到 256 / 8 后基本消失。它属于预览软阴影采样不足，不能据此返工模型。

随后的人眼复核正确指出首轮白门像空心夹层，并进一步发现左右原门确有几何缺损。前者来自饰板嵌入过浅与 Bevel 过大，后者就是上述跨区删面；两者不是同一个问题。当前三门非破坏版本同时规避了这两类错误。

## 仍需人工判断

自动测试不能回答轮廓是否好看、材质是否像真实废土设备、尺度是否适合车内空间、法线细节是否反转、碰撞是否满足寻路，以及风格能否与后续人物和环境统一。这些结论必须结合代表性镜头、玩法尺度和至少一次人眼复核；通过前不要把本目录当作正式资产库。

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

## 仍需人工判断

自动测试不能回答轮廓是否好看、材质是否像真实废土设备、尺度是否适合车内空间、法线细节是否反转、碰撞是否满足寻路，以及风格能否与后续人物和环境统一。这些结论必须结合代表性镜头、玩法尺度和至少一次人眼复核；通过前不要把本目录当作正式资产库。

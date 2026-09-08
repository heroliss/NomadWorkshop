# 《游牧工坊》URP 图形基线

> 状态：**Baseline v0.1**，验证于 Unity 6000.3.22f1 / URP 17.3.0，2026-09-04。它用于让灰盒阶段也在真实 PBR、环境光、阴影和后处理下暴露问题；不是最终 Art Bible，也不是目标平台性能结论。

## 当前决定

- 项目继续使用 **URP**。当前游戏需要 Steam 与潜在 Android / iOS 的同一资产和 Shader 主干，切换 HDRP 会扩大材质、VFX、平台与验证分叉，却尚无画面证据证明收益值得。
- 色彩空间保持 **Linear**。Universal RP Asset 使用 64-bit 内部 HDR Color Buffer、4x MSAA、HDR Color Grading；Foundation Camera 开启 HDR、SMAA High、Depth Texture、NaN 保护与 Dithering。
- `Renderer2D.asset` 唯一保留在 index 0，`NW_UniversalRenderer3D.asset` 唯一保留在 index 1 并成为项目默认。既有 2D Scene Template、Framework Demo 与 Outpost Camera 显式选择 index 0；全部 Nomad 3D Camera 显式选择 index 1。这样新 3D 场景默认正确，旧 2D 场景也不依赖可变默认值。
- 3D Renderer 保持 **Forward**。当前代表性场景灯数很少，Forward+ 暂无可见收益；过早启用只会给移动端和 Shader 变体增加变量。
- Renderer Feature 只加入一份保守 SSAO：半分辨率、Depth Normals、Medium Sample / Blur、Intensity 1.25。URP Asset 使用 2 Cascades、Medium Soft Shadow，并开启 Reflection Probe Blending / Box Projection / Atlas。
- Foundation Global Volume 使用 ACES、0.12 Bloom、+0.5 stop Exposure、+3 Contrast、-3 Saturation。效果刻意克制，负责建立稳定动态范围，不把调色当成正式美术。
- 程序化废土天空同时提供环境漫反射与远景高光；128 分辨率局部 Reflection Probe 在运行时灰盒生成完成后只捕获一次，不逐帧更新。低端平台可在 View Inspector 关闭该捕获。

这里的 HDR 是**渲染管线内部高动态范围**，不是已经交付 HDR10 / scRGB 显示输出；后者还需要 Player、显示器、UI 亮度和目标设备单独验证。

## 为什么暂不启用更多功能

| 候选 | 当前决定 | 重新评估条件 |
|---|---|---|
| HDRP | 不切换 | URP 无法达到已经冻结的关键画面目标，且桌面收益足以承担移动端分叉 |
| Forward+ | 保持 Forward | 动态灯数量与 CPU / GPU 采样证明 Forward 已成为瓶颈 |
| APV | 暂不烘焙 | 车辆内部静态几何与昼夜光照稳定，能建立可重复 Bake / Scenario 流程 |
| Surface Cache GI / URP SSR / GTAO | 不启用实验开关 | 进入对应稳定 Unity LTS，并在目标 GPU 上有版本化性能证据 |
| GPU Resident Drawer | 暂停 | 正式重复 Mesh 数量、LOD 与平台能力矩阵成立 |
| VFX Graph 全局优先 | 按效果选择 | 桌面高档可用 VFX Graph；需要覆盖普通手机的核心反馈仍保留 Particle System / Shader 降级 |

## 质量分层边界

目前六个 Quality Level 仍引用同一份 Universal RP Asset。这适合尽快冻结第一条代表性画面链，但不是发布配置。进入 Android / iOS 性能 Gate 前，应拆成至少两套受审计资产：

- Desktop High：当前 SSAO、两级软阴影、局部反射和完整后处理；
- Mobile Baseline：按真实设备决定 SSAO、反射捕获、阴影级联、MSAA 与 Render Scale，不能只按编辑器感觉关闭功能。

材质、Prefab 和玩法代码不应因质量档分叉；差异集中在 Pipeline / Renderer / Volume 与少量可显式降级的 VFX。

## 重建与验证

1. 执行 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer`；
2. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开最小垂直切片`；
3. 运行 `NomadRenderingSpikePipelineTests` 与 `NomadFoundationVerticalSlicePipelineTests`，再跑受影响的 Foundation PlayMode 套件；
4. 在固定 Game View 检查甲板暗部、设施色彩、高光、阴影、UI 可读性和错误材质。自动绿灯不能替代实际看图。

本轮本机证据为默认被 Git 忽略的 `Screenshots/nomad-foundation-graphics-baseline.png` 与调校后的 `Screenshots/nomad-foundation-graphics-baseline-tuned.png`；前者保留“技术成立但过暗”的失败证据，后者才是当前人工基线。截图目录不作为跨设备像素级黄金图，正式视觉回归还需冻结分辨率、平台、相机状态、时间和容差。

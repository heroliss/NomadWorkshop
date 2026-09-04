# 游牧工坊 URP 3D Renderer Spike

这是一个**可删除的游戏本地显示证据**：隔离相机显式使用游牧工坊共享的 `UniversalRendererData`，验证 Blender 储物箱的 3D 灯光、阴影、SSAO 和 PBR 材质可读性。预览材质与场景可删；共享 Renderer、项目默认迁移、2D 场景固定和 Foundation 图形资产已经是正式基线，不属于可删 Spike。这仍不代表最终艺术指导或目标平台性能成立。

## 重建与审计

1. 先按 [`BlenderImport/NW_StorageCrate_01`](../../BlenderImport/NW_StorageCrate_01/README.md) 的步骤生成并审计储物箱 Prefab；
2. 在 Unity 执行 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer`；
3. 运行 EditMode fixture `NomadRenderingSpikePipelineTests`；
4. 打开 [`Preview/NW_Urp3DVisualPreview.unity`](Preview/NW_Urp3DVisualPreview.unity)，进入 Play 并人工检查 Game View。

菜单会从当前 URP Package 的官方 `UniversalRendererData.asset` 模板创建或更新 `Assets/Game/NomadWorkshop/Rendering/NW_UniversalRenderer3D.asset`，避免遗漏包内 Shader / Post-process Resource；随后添加唯一的保守 SSAO，把 index 1 设为项目默认，并在此之前把已知 2D 场景 Camera 显式固定到 index 0。它还维护 Foundation 程序化天空盒、Global Volume Profile 与隔离预览。审计报告位于被 Git 忽略的 `ArtPipelineOutput/UnityRenderingSpike/Urp3D/report.json`。

## 已验证契约

| 检查项 | 已验证结果 |
|---|---|
| Renderer 列表 | `Renderer2D.asset` 唯一保留为 index 0；`NW_UniversalRenderer3D.asset` 唯一注册为 index 1 并成为默认值；未知默认值会停止迁移 |
| 2D 兼容 | 2D Scene Template、Framework Demo 与 Outpost Camera 显式选择 index 0，不依赖项目默认值 |
| 3D Renderer | 保持 Forward、全 Layer、唯一降采样 SSAO（Depth Normals、Medium Sample / Blur），不抢跑 Forward+ |
| URP Asset | Linear 项目下启用 64-bit 内部 HDR、4x MSAA、2 Cascades / Medium Soft Shadow、Reflection Probe Blending / Box Projection |
| 相机 | 预览 Camera 显式选择 index 1，HDR + SMAA High，不启用实验外后处理或 XR |
| 场景 | 引用实际储物箱 Prefab、URP/Lit 地面、程序化 Skybox、一个带硬阴影主光和两个无阴影辅助光 |
| Foundation 图形资产 | ACES、轻量 Bloom / Color Adjustments、程序化废土天空与 128px 首帧一次性局部 Reflection Probe |
| 自动证据 | Pipeline / 默认 index / 显式 2D / Renderer Feature 子资产 / Camera / Prefab / Material / Light / Scene 审计通过 |
| 人工证据 | 固定 Game View 中体积、硬阴影、青 / 橙 / 黑材质区分和金属高光成立；无粉材质、黑屏或错误姿态 |

自动审计的视觉结论刻意保持 `manual_review_required`。测试可以证明相机选择和灯光配置，不能替代构图、审美、材质可信度或最终玩家镜头判断。

## 删除边界

本目录下的预览材质、场景与报告可以整体删除；`Assets/Game/NomadWorkshop/Rendering/` 下的共享 Renderer、Foundation Skybox / Volume Profile，以及 `Assets/Settings/UniversalRP.asset` 和三个显式 2D Camera 不能随 Spike 删除。如果未来替换 Renderer，必须先迁移全部显式 Camera 并重跑场景生成契约；不要手改 Unity YAML。

# 游牧工坊 URP 3D Renderer Spike

这是一个**可删除的游戏本地显示证据**：在不替换项目默认 `Renderer2D` 的前提下，让隔离相机显式使用游牧工坊共享的 `UniversalRendererData`，验证 Blender 储物箱的 3D 灯光、阴影和 PBR 材质可读性。预览材质与场景可删；共享 Renderer 现已被正式 Foundation 消费，不属于可删 Spike。这仍不代表艺术指导或性能基线。

## 重建与审计

1. 先按 [`BlenderImport/NW_StorageCrate_01`](../../BlenderImport/NW_StorageCrate_01/README.md) 的步骤生成并审计储物箱 Prefab；
2. 在 Unity 执行 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer`；
3. 运行 EditMode fixture `NomadRenderingSpikePipelineTests`；
4. 打开 [`Preview/NW_Urp3DVisualPreview.unity`](Preview/NW_Urp3DVisualPreview.unity)，进入 Play 并人工检查 Game View。

菜单会从当前 URP Package 的官方 `UniversalRendererData.asset` 模板创建或更新 `Assets/Game/NomadWorkshop/Rendering/NW_UniversalRenderer3D.asset`，避免遗漏包内 Shader / Post-process Resource。随后将它作为次级 Renderer 唯一注册到 `Assets/Settings/UniversalRP.asset`，并生成地面、程序化 Skybox 与隔离预览场景。审计报告位于被 Git 忽略的 `ArtPipelineOutput/UnityRenderingSpike/Urp3D/report.json`。

## 已验证契约

| 检查项 | 已验证结果 |
|---|---|
| 全局默认 | `Renderer2D.asset` 保持 index 0；配置工具遇到未知默认值会停止，不自行覆盖 |
| 次级 Renderer | `Rendering/NW_UniversalRenderer3D.asset` 唯一注册为 index 1，Forward、全 Layer、无 Renderer Feature |
| 相机 | 预览 Camera 显式选择 index 1，HDR + SMAA High，不启用实验外后处理或 XR |
| 场景 | 引用实际储物箱 Prefab、URP/Lit 地面、程序化 Skybox、一个带硬阴影主光和两个无阴影辅助光 |
| 自动证据 | Pipeline / 默认 index / 次级 index / Renderer / Camera / Prefab / Material / Light / Scene 审计通过 |
| 人工证据 | 固定 Game View 中体积、硬阴影、青 / 橙 / 黑材质区分和金属高光成立；无粉材质、黑屏或错误姿态 |

自动审计的视觉结论刻意保持 `manual_review_required`。测试可以证明相机选择和灯光配置，不能替代构图、审美、材质可信度或最终玩家镜头判断。

## 删除边界

本目录下的预览材质、场景与报告可以整体删除；`Assets/Game/NomadWorkshop/Rendering/NW_UniversalRenderer3D.asset` 与 `Assets/Settings/UniversalRP.asset` 中的次级引用不能随 Spike 删除，因为正式 Foundation 相机已显式使用它。如果未来要替换该 Renderer，必须先迁移所有 3D 相机并重跑场景生成契约；不要手改 Unity YAML。

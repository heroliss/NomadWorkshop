# 游牧工坊 URP 3D Renderer Spike

这是一个**可删除的游戏本地显示证据**：在不替换项目默认 `Renderer2D` 的前提下，让隔离相机显式使用次级 `UniversalRendererData`，验证 Blender 储物箱的 3D 灯光、阴影和 PBR 材质可读性。它不是正式游戏 Renderer、艺术指导或性能基线。

## 重建与审计

1. 先按 [`BlenderImport/NW_StorageCrate_01`](../../BlenderImport/NW_StorageCrate_01/README.md) 的步骤生成并审计储物箱 Prefab；
2. 在 Unity 执行 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer`；
3. 运行 EditMode fixture `NomadRenderingSpikePipelineTests`；
4. 打开 [`Preview/NW_Urp3DVisualPreview.unity`](Preview/NW_Urp3DVisualPreview.unity)，进入 Play 并人工检查 Game View。

菜单会从当前 URP Package 的官方 `UniversalRendererData.asset` 模板创建或更新 `NW_UniversalRenderer3D.asset`，避免遗漏包内 Shader / Post-process Resource。随后将它作为次级 Renderer 唯一注册到 `Assets/Settings/UniversalRP.asset`，并生成地面、程序化 Skybox 与隔离预览场景。审计报告位于被 Git 忽略的 `ArtPipelineOutput/UnityRenderingSpike/Urp3D/report.json`。

## 已验证契约

| 检查项 | 已验证结果 |
|---|---|
| 全局默认 | `Renderer2D.asset` 保持 index 0；配置工具遇到未知默认值会停止，不自行覆盖 |
| 次级 Renderer | `NW_UniversalRenderer3D.asset` 唯一注册为 index 1，Forward、全 Layer、无 Renderer Feature |
| 相机 | 预览 Camera 显式选择 index 1，HDR + SMAA High，不启用实验外后处理或 XR |
| 场景 | 引用实际储物箱 Prefab、URP/Lit 地面、程序化 Skybox、一个带硬阴影主光和两个无阴影辅助光 |
| 自动证据 | Pipeline / 默认 index / 次级 index / Renderer / Camera / Prefab / Material / Light / Scene 审计通过 |
| 人工证据 | 固定 Game View 中体积、硬阴影、青 / 橙 / 黑材质区分和金属高光成立；无粉材质、黑屏或错误姿态 |

自动审计的视觉结论刻意保持 `manual_review_required`。测试可以证明相机选择和灯光配置，不能替代构图、审美、材质可信度或最终玩家镜头判断。

## 删除边界

本 Spike 对共享配置只有一处影响：`Assets/Settings/UniversalRP.asset` 的 Renderer 列表引用了 `NW_UniversalRenderer3D.asset`。删除前先在 Unity Inspector 或项目 Editor API 中移除该**次级**引用，并再次确认 `Renderer2D.asset` 仍是默认 index 0；不要手改 Unity YAML。然后可通过 Unity 删除整个本目录，并删除 `NomadRenderingSpikePipeline.cs`、对应测试及文档说明。`Game.NomadWorkshop.Editor.asmdef` 的 URP Runtime 引用只有在没有其他 Editor 代码消费 URP API 时才一并移除。

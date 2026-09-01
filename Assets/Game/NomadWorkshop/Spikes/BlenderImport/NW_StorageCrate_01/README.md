# NW_StorageCrate_01 Blender Import Spike

这是一个**可删除的跨工具技术证据**，用于证明版本化 Blender 脚本生成的静态道具能被 Unity 以确定、可审计的方式接管。它不是 Runtime Content，也不是《游牧工坊》的正式美术资产。

## 重建与导入

1. 在仓库根目录运行：

   ```powershell
   pwsh -File Tools/ArtPipeline/Blender/run-blender-smoke.ps1
   ```

   当前入口以 PowerShell 7 验证；不要用可能错误解码 UTF-8 无 BOM 脚本的 Windows PowerShell 5.1。

2. 从同一次 `ArtPipelineOutput/BlenderSmoke/NW_StorageCrate_01/` 输出中，将 FBX 和 `manifest.json` 导入本目录；manifest 在 Unity 中命名为 `NW_StorageCrate_01.manifest.json`。不要混用不同 Smoke 运行的 FBX 与 manifest。
3. 在 Unity 执行 `Assets/SSFramework/游牧工坊/Blender Import Spike/配置并审计储物箱`。
4. 运行 EditMode fixture `NomadPropAssetPipelineTests`。

菜单会重建或更新 `Materials/`、`Prefabs/` 和 `Preview/`，并把审计报告写入被 Git 忽略的 `ArtPipelineOutput/UnityImportSpike/NW_StorageCrate_01/report.json`。

## 已验证契约

| 检查项 | 已验证结果 |
|---|---|
| 来源 | manifest schema 1 / Harness 0.2.0；Blender 源脚本和 FBX SHA-256 均与当前仓库 / Unity 资产一致 |
| 几何 | 14 Mesh、784 Blender Source Vertex、3024 Unity Runtime Vertex、1512 Triangle |
| 坐标 | Blender `+Z Up` 到 Unity `+Y Up` 的静态 FBX 空间变换已经烘焙，FBX、Prefab Root 与 Visual 均为 Identity |
| Bounds | 1.235 × 0.945 × 0.958 米，与 manifest 的轴转换结果一致 |
| 材质 | 三个 manifest PBR 材质参数被翻译为外部 `Universal Render Pipeline/Lit` 材质，并按源材质名显式 Remap |
| 交互 | Prefab 只有一个基于实际 Renderer Bounds 的 Root BoxCollider，没有 MeshCollider |
| 证据 | 隔离预览场景存在，Importer、层级、几何、材质、Prefab 和 Collider 审计通过 |

Source Vertex 与 Runtime Vertex 不是同一个指标。硬边、法线、UV 或材质边界会让 Unity 拆分运行时顶点；本探针要求三角形与 Bounds 保持稳定，并把顶点膨胀显式记录和设限。

## 当前视觉边界

项目默认 Universal RP Renderer 仍是 `Renderer2DData`。本 Import Preview 已人工确认模型直立、比例合理、轮廓和三种基础颜色可见，但它不负责验证 Metallic、Roughness、法线、3D 阴影和灯光，所以 rendering verdict 继续保持 `inconclusive`。独立的 [`Rendering/Urp3D`](../../Rendering/Urp3D/README.md) 已通过相机显式选择次级 Universal Renderer 补上首轮 PBR 显示证据，没有替换全局默认值；两个场景的结论不能混用。

## 删除边界

删除整个本目录、`NomadPropAssetPipeline.cs`、`NomadPropAssetPipelineTests.cs` 以及文档中的对应说明即可移除本探针。它没有新增 asmdef、全局 AssetPostprocessor、Runtime 引用或 Framework API；删除时仍应通过 Unity Editor 移除 Prefab / Scene 资产，避免手改 YAML。

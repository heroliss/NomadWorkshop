# 游牧工坊：局部顶棚与剖切样板

接续 `e0e777c`，落实 S0 的局部顶棚与显示实验；当前仍是单层，不将可见屋顶当成已经实现建造、雨水覆盖或多层模拟。

## 可检查计划

- [x] 为 NW5 左侧工位制作悬挑棚架、薄板屋顶和暖色工作灯。立柱在原甲板之外，跨通路梁保持净高；不改变设施或居民的空间真值。
- [x] 用独立 Renderer 绑定区分屋顶与棚架，复用现有 PBR 微表面，不为本次屋顶再增加整套大贴图。
- [x] 默认剖开屋顶；按钮 / H 切换外观偏好。进入建造时强制剖开，退出后恢复偏好。隐藏仅调整 Renderer/投影策略，保留原启用状态、对象、Collider 和工作灯。
- [x] 验证切换/暂停/建造进入退出/读档不影响居民工作和资源；验证按钮矩形的输入阻挡与隐藏后的建造拾取，恢复持有者释放后的原渲染状态。
- [x] 检查完整与剖开实机截图及暗部可读性，完成定向与 Foundation 回归，收口为同一阶段变更。

## 所有权与边界

模型包装 Prefab 通过显式 Renderer 引用声明屋顶；换模型只需重新绑定目标，运行期不猜节点名称。`FoundationRoofPresentation` 由 World View / Bag 拥有，保存原 Renderer 状态；HUD 仅借用该表现控制器，不持有 World View 或新增游戏 Model。显示偏好留在当前观看会话，世界检查点恢复不改它；不增加业务存档字段。

默认剖开仍保留屋顶投影和棚架，体现遮阴并让角色/设施可见；原本不投影的 Renderer 隐藏时仍不投影。进入建造自动剖开，避免用当前单层平面拾取穿过可见的屋顶；完整模式暂用于观看外观，不声称完成多层有限区域拾取。

屋顶、棚架和灯具可分件；本轮使用现有金属表面纹理并调节材质，几何由 Blender 生成。灯光与渲染隐藏均不参与天气、库存、导航或设施工作结算。真正围护、支撑、雨水和楼层选择仍按[可扩建方案](nomad-workshop-expandable-decks-design.md)继续推进。

## 资产和显示实现

沿用 `ReferenceVehicleSample` 和 NW5 包装 Prefab。Blender 生成器升级到 v0.3.0，以 `blender_nomad_canopy.py` 作为第五个有摘要校核的来源；最终产物目录为 `ArtPipelineOutput/VehicleForms/v09`。屋顶由五条有板厚、立边咬合缝和轻微坡度的板件构成，C 形槽钢、两根外侧立柱、斜撑和灯具托架形成独立棚架；Unity 导入后按原有材质/父节点规则合并。

整车为 **118,384 三角形、243 个 Mesh**，比上一轮 110,700 增加 7,684（约 6.9%）。导出与 FBX 回读保持三角形数、尺寸、轴点、分件身份一致，UV 完整、退化面为 0；屋顶和棚架内部顶点保持在原走动矩形之外或离地至少 2.02 m。这只校核当前装饰网格，不代表任意高设施、局部扩建、多层人物携物扫掠或承载结构已验证。

`NW5_CanopySheet` / `NW5_CanopyFrame` 复用已有 NW1 金属表面贴图，不增加纹理文件；三组原图集仍为九张 2048²。两盏暖色点光源为强度 6、范围 4 m、不投影，实际强度是本场景 URP 下的观测调校，尚无目标 Player 性能结论。导入时精确选择两个语义灯位，保存后回读屋顶引用和灯数量。

`FoundationRoofVisual` 只存 Renderer 引用。`FoundationRoofPresentation` 在修改前验证所有引用，拒绝缺失、重复和跨模型根的引用；剖开时原投影网格使用 `ShadowsOnly`，原来关闭投影的网格关闭 Renderer。不会启用原本禁用的网格，也不改 `forceRenderingOff`；释放会话恢复原状态。HUD 借用句柄由 World View 的 Bag 清理，旧句柄不能解除新绑定。

显示偏好不写业务存档。进入建造自动剖开，H 在该模式下无效，左下按钮说明退出后会恢复偏好。当前拾取仍落到单层甲板平面；后续多层需要把可见层、目标表面和空间占用一起接入，不把这次自动剖开当成多层拾取已经完成。

## 本轮验收（2026-09-08）

环境为 Unity 6000.3.22f1 / URP 17.3 / Windows Editor，基于 `e0e777c` 的工作树。四份精确测试计划全部通过，共 **139/139**：

| 范围 | 数量 | job | 时间 |
|---|---:|---|---:|
| `FoundationRoofPresentationTests` · EditMode | 6/6 | `01153524a422` | 0.79 s |
| `FoundationHudLayoutTests` · EditMode | 10/10 | `e90e80c43120` | 0.89 s |
| `NomadReferenceVehiclePlayModeTests` · PlayMode | 17/17 | `4056acf1f361` | 49.85 s |
| `NomadFoundationVerticalSlicePlayModeTests` · PlayMode | 106/106 | `7e06022ec7ed` | 316 s |

每次运行前均保存本次 READY 日志；完整发现、计划、dispatch、逐用例终态与身份核验位于 `Logs/AIValidation/nomad-warm-art/roof-{unit,layout,play,foundation}-*`。新检查覆盖引用失败不部分修改、保留原启用/投影状态、释放恢复、UI 实际矩形、单层甲板拾取，以及模拟键盘经过 Input System → `WorldView.Update` 的 H 路径。它不代表物理键盘或任意操作系统焦点链路已验证。

真实工坊在屋顶反复切换期间完成取水并进入搬运；暂停期间资源、任务阶段和人物位置不改变，恢复模拟后继续行走。原三居民动作、身份、维修、设施和旅程用例继续在带棚候选中通过；无顶棚的 Foundation 基线覆盖可选绑定的旧路径。Foundation 保留的“主文件与备份均无法反序列化”是 `PlayerCheckpoint_CorruptMainUsesBackup_BothCorruptKeepWorld` 用 `LogAssert.Expect` 验证的负例，不是本次运行时存档失败。

已实际打开检查以下 2450 × 1426 Game 截图：

- [剖开俯瞰](../Screenshots/nomad-canopy-cutaway-02.png)与[完整屋顶](../Screenshots/nomad-canopy-exterior-02.png)：同一相机、同一业务暂停时刻对照。
- [棚下近景](../Screenshots/nomad-canopy-workspace-02.png)：检查真实人物、灯具连接、槽钢和厨房净空；近景明确暴露设施仍过于简化的问题。
- [建造时自动剖开](../Screenshots/nomad-canopy-build-02.png)：网格、设施工作位与屋顶按钮的强制状态。

居民 01 实际到驾驶位并行驶 **15.2 m** 后由业务暂停，报告 `roof-journey-review.json`；`roof-visual-state.json` 记录同一 tick 23,200 下进入/退出建造后偏好恢复、两盏工作灯继续启用。源文件和已导入资产摘要见 `roof-artifact-audit.json`；九张原图集与上一轮内容逐项相同，见 `roof-texture-diff.json`。

本轮确认了局部结构与剖切在真实玩法中的衔接；仍未完成可建造屋顶、雨水覆盖、多层、任意高设施的净空检测、目标 Player 性能或整体美术验收。环境、普通玩家 HUD、设施造型和服装仍需按 Foundation §9 推进。验收后恢复干净的 `ReferenceVehicleSample`，退出 Play；编译检查无错误。

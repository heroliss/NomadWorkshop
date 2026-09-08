# 居民工装与原动作兼容

接续 `e1a48a5`。当前 NW3 人物的正常头身、共享 Humanoid 和携物动作已成立，但服装仍有中世纪皮甲、护腕和高靴特征。本阶段把现有蒙皮衣物调整为低饱和工作上衣、朴素长裤和短工作靴，保留三人的发型和身份差异；不重做身体/骨架，也不增加运行时捏人依赖。

## 可检查计划

- [x] 检查原衣物拓扑、独立装饰岛和骨权重，明确可直接复用的衣料与手足边界；第三方和 NW3 基线保持原样。
- [x] 在 Blender 制作独立 NW10 工装候选：去除装甲扣件，平顺领口、腰部和裤靴轮廓，提供少量缝线/口袋；查看实际正面、侧面和俯视。仍保留部分原服装结构，尚非最终美术定版。
- [x] 导出并校核骨架、权重、网格、材质来源，在 Unity 生成普通 Humanoid Prefab 并按原居民稳定 id 接入参考车辆。
- [x] 用实际携水、拿放、暂停/恢复和三款人物的楼梯验证，记录关节衣物/手指/脚底与工作接触的实机画面和已知不足，形成阶段交付。

身体尺寸和原骨架保持一致，造型调整只发生在自有派生网格。不得只把原金属贴图染成另一种颜色就宣称已经成为工装；新增细节必须贴合已有蒙皮表面，不重新使用刚性方块衣物。优先完成经过验证的固定款式，随机外观和捏人仍是后续可选范围。

## 证据与边界

生成器为 [blender_nomad_workwear.py](../Tools/ArtPipeline/Blender/blender_nomad_workwear.py)，当前版本 0.2.3；输出为 `ArtPipelineOutput/Workwear/v10`。可编辑的中性源文件为 [NW10_Workwear_Source.blend](../ArtPipelineOutput/Workwear/v10/NW10_Workwear_Source.blend)，布光与站姿预览为 [NW10_Workwear_Lineup.blend](../ArtPipelineOutput/Workwear/v10/NW10_Workwear_Lineup.blend)。两者属于本地输出；Unity 的三份 NW10 FBX、Prefab、六份新材质及 manifest 纳入仓库。上游 Quaternius 和 NW3 均未改写。

男款缩短衣摆、压平原系带突起并封合前领口；女款去掉独立护腕扣件、适度放宽和平顺腰部；靴筒上段作为裤腿派生，并按实际裤脚截面收拢接合处。口袋与缝线由衣物三角形表面采样，继承归一化权重，随后作为网格岛合并回上衣/裤子；不为每条缝线增加一个 Renderer。布料复用 NW1 Fabric，UV 平铺 12 倍、降低法线强度；皮肤、眼睛和发型继续使用 NW3 的实际来源。没有新增人体生成或运行时换装依赖。

原 NW3 部分顶点包含 5 个影响骨且权重总和不是 1。新派生模型在插值前按当前 Unity Bone4 选择前四个影响并归一化，源顶点裁减比例与每片新衣料的裁减均记录在 manifest。先插值原始非归一化权重会使口袋偏向权重总和较大的顶点，不能等导出后再补归一化。此处理只针对当前固定人物，不能作为任意服装的自动适配保证。

三人均保留 65 根骨骼，FBX 回读矩阵误差小于 0.0001；网格数分别为 8、8、9，三角形为 18,076、21,176、19,581。已实际查看 v10 正面、侧面和俯视预览。女款仍偏贴身，袖口、鞋型及小腿轮廓还有原素材特征；不以新配色宣称已达到 J 的最终服装风格。真实携物/蹲下时的新增衣片穿插仍需实机取证。

Unity 菜单“Assets/SSFramework/游牧工坊/首版美术/生成工装并接入参考车辆”检查源文件哈希，生成普通 Humanoid 与材质，按 `resident-01/02/03` 接入 `ReferenceVehicleSample`。独立楼梯样本为 `WorkwearMechanicStairs`、`WorkwearCaretakerStairs`、`WorkwearDriverStairs`；均沿用现有楼梯动作实验，不代表正式多层建造已经接入。

### 实机取证与工程验收

实际查看了[默认俯瞰](../Screenshots/nomad-workwear-overview-01.png)、[三居民近景](../Screenshots/nomad-workwear-crew-close-01.png)、[真实携水](../Screenshots/nomad-workwear-carry-05.png)，以及三款人物的[维修者](../Screenshots/nomad-workwear-mechanic-stairs-01.png)、[束发居民](../Screenshots/nomad-workwear-caretaker-stairs-02.png)、[驾驶者](../Screenshots/nomad-workwear-driver-stairs-01.png)持桶楼梯帧。真实携行报告为 `workwear-carry-review-02.json`：居民 01，携水 2 L；三份 `workwear-*-stair-review-01.json` 记录实际 8 L 携行、高度约 1.4 m 的中途暂停。报告位于 `Logs/AIValidation/nomad-warm-art/`。近景只临时改相机，不挪人物、设施或水罐；部分栏杆和设施遮挡仍可见。

初次 Unity 取证使衣裤显著偏暗，随后把 Blender 配方的线性 RGB 转成 Unity 材质属性的 sRGB 后再赋值。修正六份 NW10 材质并重新导入后，三份 FBX 和三份 Prefab 的哈希不变；导入元数据与材质更新另存最终快照。前四张携行截图属于颜色修正前或相机被棚架遮挡的诊断，不作为最终观感证据。

| 范围 | 结果 | job / 证据前缀 |
|---|---|---|
| 首轮参考车辆，拿放/携水/旅程/杯具/暂停和检查点回退 | PlayMode 23/23 | `3b6d2032292f` / `workwear-reference-01` |
| 维修者持桶上下楼、暂停、取消恢复与无效检查点 | PlayMode 4/4 | `d4782bdac23c` / `workwear-stair-mechanic-01` |
| 束发居民，同一完整楼梯契约 | PlayMode 4/4 | `557508a46d93` / `workwear-stair-caretaker-01` |
| 驾驶者，同一完整楼梯契约 | PlayMode 4/4 | `40836585b292` / `workwear-stair-driver-01` |
| 最终材质版本的参考车辆回归 | PlayMode 23/23 | `56defefbf399` / `workwear-reference-final` |

最终参考车辆回归 23/23，78.85 s；结合三款各 4 项楼梯，本阶段相关用例 35 项通过，首轮同一参考车辆的重复执行不叠加计数。楼梯自动测试在颜色修正前执行，之后三份 FBX/Prefab 字节未变，最终材质另有三款真实楼梯截图。测试均通过完整发现、精确名单、新鲜 READY 预检与逐项终态核对；骨架和导出检查来自本轮 Blender 及 Unity 实际导入，不将旧 EditMode 结果计入本轮。环境为 Unity 6000.3.22f1 / URP 17.3、StandaloneWindows64 Editor、RTX 4060 Laptop / Very Low；Game View 1225 × 713，截图为 2 倍。

最终源码/资产/元数据 47 份快照为 `workwear-final-source-hashes.json`。早期 `workwear-source-hashes-01.json` 误包含两份 Unity Runner 临时场景，后续明确按 NomadWorkshop 与 ArtPipeline 范围剔除，保留该记录供审计；没有把临时文件消失当作产品改动。结束恢复非 Play、干净的 `ReferenceVehicleSample`，两类观察器均已撤销；编译和 Console 错误为 0。使用临时 Play 世界、内存检查点与独立楼梯记录，未覆盖玩家持久存档。原 NW3/Warm/Foundation 全量、目标 Player 和性能基准没有在本轮重复验证。

握点/足底与任务恢复通过，不等于携物姿势已经自然。楼梯帧的肘部偏高，原袖口、女款贴身轮廓、小腿过渡与男款衣摆仍需美术调整；普通杯具的握把和伸手过渡也未在本轮改善。没有枚举全部衣片与身体的逐帧穿插，亦未验证随机身材、性能预算、目标 Player 或用户审美接受度。下一轮优先调整真实提桶的肘部姿态和手持物造型，再推进紧凑梯/局部扩建，避免一直用原长楼梯代表最终车内结构。

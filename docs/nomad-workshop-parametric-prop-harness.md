# 《游牧工坊》参数化道具 Harness

> 状态：**已验证 v0.1**，更新于 2026-09-03。本文记录 Foundation Prototype 阶段的快速 3D 道具路线：先用 Unity 内的可审查参数锁定尺寸、分件和玩法接口，再按资产价值选择 Blender、Rodin 或其他外部美术流程。它不是“所有正式模型都由程序生成”的美术决策。

## 1. 当前决策

- 第一版游戏地基优先使用**参数化代理资产（Parametric Proxy）**，避免玩法迭代被网页生成、积分、下载、重拓扑和手工拆件阻塞。
- ProBuilder 只作为 Editor 端几何算法库；落盘和运行时使用普通 Unity `Mesh`、URP/Lit `Material` 与 `Prefab`，不携带 `ProBuilderMesh` Component。
- 玩法真值放在稳定根层：尺寸、Collider、交互锚点、资源接口与可动部件语义不能依赖最终外观。
- 最终视觉进入 `Visual_Final`，原型留在 `Visual_Prototype` 作为可删除回退和尺寸证据。高品质模型替换外观，不重做设施的玩法身份。
- 只给值得近看的 Hero / 高频资产投入 Rodin、Blender 清理和 Mesh-specific PBR；普通背景件、隐藏设备和早期设施可以长期使用参数化或模块化资产。

这条路线解决的是**迭代速度与接口准确性**，不是直接解决最终审美。好看的轮廓、材质叙事、统一风格和真实镜头仍要通过 Art Bible、代表性场景和人工评审成立。

## 2. 已跑通的生成链

```text
ScriptableObject Profile
  → ProBuilder ShapeGenerator / Bevel 临时几何
  → 按材质合并
  → 拷贝为稳定 GUID 的普通 Mesh 资产
  → Root / Visual_Prototype / Visual_Final / Anchors Prefab
  → 版本化预览场景
  → 结构、预算、材质、Collider 与交互契约审计
  → 人工视觉检查
```

首个样本是 `NW_FieldKitchen_Prototype_01`：

- 玩法外形为 `2.30 × 2.00 × 0.88 m`；
- 一个合并静态主体，加左下、中间、右下三扇独立门；
- 共 4 个普通 Mesh、4,856 Vertex、2,172 Triangle；
- 三个门轴分别为 `Hinge_LeftLower`、`Hinge_Center`、`Hinge_RightLower`，Prefab 默认闭合，预览场景才展开；
- 根级只有一个玩法 `BoxCollider`；
- `Anchors` 提供 `WorkPosition`、`PrimaryHandTarget`、`StorageAccess`、`WaterInput` 与 `WasteOutput`；
- 五个共享原型材质区分旧青漆、深色金属、安全橙、不锈钢和橡胶。

权威入口：

- Profile：`Assets/Game/NomadWorkshop/Prototype/ParametricProps/NW_FieldKitchen_Prototype_01/NW_FieldKitchen_Prototype_01_Profile.asset`
- Prefab：`Assets/Game/NomadWorkshop/Prototype/ParametricProps/NW_FieldKitchen_Prototype_01/Prefabs/NW_FieldKitchen_Prototype_01.prefab`
- 预览：`Assets/Game/NomadWorkshop/Prototype/ParametricProps/NW_FieldKitchen_Prototype_01/Preview/NW_FieldKitchen_Prototype_01_Preview.unity`
- 生成器：`Assets/Game/NomadWorkshop/Editor/NomadFieldKitchenPrototypePipeline.cs`

Editor 菜单：

1. `Assets/SSFramework/游牧工坊/参数化道具/创建或定位野战厨房配置`
2. 在 Inspector 调整 Profile；不直接编辑生成 Mesh、Prefab 或场景。
3. `Assets/SSFramework/游牧工坊/参数化道具/生成并审计野战厨房`
4. 打开预览场景检查轮廓、分件、材质可读性和门的转轴。

## 3. 为什么暂时不再安装一套建模 Package

项目已经有 ProBuilder 6.1.2，它足够覆盖当前的方盒、圆柱、倒角、合并和普通 Mesh 输出。再安装一个顶点建模或 CSG Package 并不会自动带来更好的美术，反而会增加：

- Unity 版本与 Package API 兼容面；
- Editor-only 依赖和自动化入口；
- 多套几何真值之间的转换与测试；
- 未来迁移、升级和排错成本。

遇到以下真实瓶颈时再选工具：

| 瓶颈 | 优先办法 |
|---|---|
| 更多硬表面模块和精确接口 | 扩展当前参数 Profile 与部件模板 |
| 自由曲面、雕刻、复杂布线或重拓扑 | Blender |
| 道路、管线、轨道等连续路径 | 评估 Unity Splines，但先由真实关卡需求触发 |
| Mesh-specific 烘焙与手绘材质 | Substance 3D Painter / Blender 烘焙；免费路线可单独 Spike Material Maker |
| Hero 概念快速变成候选形体 | Rodin / Meshy / 其他 Provider，仍经 Blender 与 Unity Intake |

不需要为了“以后也许有用”现在装齐这些工具。Package 或软件只有进入一个可重放、可验收的生产步骤后才成为项目依赖。

## 4. 原型材质怎样过渡到正式 PBR

Foundation Prototype 的五个材质只用 URP/Lit 参数色、Metallic 与 Smoothness，目标是让功能分区和体积在真实灯光下可读；它们不是正式贴图。

正式化按三层推进：

1. **共享材质族**：旧漆金属、不锈钢、橡胶、塑料、布料等使用少量统一材质和可平铺纹理。
2. **状态层**：沙尘、潮湿、结霜、轻度锈蚀和清洁度由 Shader Graph、共享 Mask、Decal、Overlay 与 VFX 组合，不为每种状态复制整套模型和贴图。
3. **Hero 独有细节**：只有屏幕占比和叙事价值足够时，才增加唯一 UV、烘焙 Normal/AO、局部 Mask 与手绘细节。

Unity 的 Shader Graph、URP/Lit、Decal Renderer Feature 和材质导入规则能负责**消费与组合** PBR 数据，但 Unity 本身不是完整的 Painter 替代品。普通图片生成模型可以提供色彩、污渍、图案和材质概念，不能默认产出互相物理一致的 Base Color、Normal、Metallic、Roughness 与 AO。正式贴图仍需在 Blender、Substance、Material Maker 或经过验证的专用生成流程中校正、打包并放进代表性灯光检查。

## 5. 与 Rodin / Blender 的交接

参数化原型不是拿去“重新生成同一个模型”的最终输入，而是结构契约和比较基线：

- 文字 Brief 与概念图决定视觉语言；
- 原型渲染、尺寸和部件图告诉外部模型哪些比例、门区、管口和接触点不能猜错；
- Rodin 等 Provider 提供候选外观；
- Blender 负责真正的分件、Pivot、背面、拓扑、UV、LOD、烘焙和命名；
- Unity Intake 把最终 Mesh 放进 `Visual_Final`，并核对 Bounds、Collider、Anchor 与状态接口。

若候选是不可拆的整体 Mesh，不再对融合大面做盲目矩形裁剪。可以先把它只当概念参考，或在 Blender 重建需要运动的门和内衬；可动件的正确机械结构比“保留每个 AI 三角形”更重要。

## 6. 已记录的实现坑

### 6.1 不用 `CopySerialized` 更新已有 Mesh

首次实现用 `EditorUtility.CopySerialized` 把新 Mesh 覆盖到稳定资产。CPU 审计读取到正确的 SubMesh 与材质槽，但 GPU 端仍可能显示旧缓存，曾把不锈钢台面错误渲染成安全橙。当前实现显式复制 Vertex、Normal、Tangent、Color、UV0–UV8、SubMesh Index 与 Bounds，再调用 `UploadMeshData(false)`；既保留 GUID，也让 GPU 数据同步。

### 6.2 临时 ProBuilder 对象不能污染用户场景

ProBuilder API 会先在 Active Scene 创建临时 GameObject。生成器把这些对象限制在 Additive 打开的可丢弃预览场景中，完成后不保存该 Scratch 状态并恢复原 Active Scene。因此参数化生成不会给用户正在编辑的场景增加对象或改变 dirty 状态。

### 6.3 预览场景不能每次全部重建

即使画面完全相同，删除并重建所有 GameObject 也会分配新的 local fileID，制造无意义场景 diff。预览现在带 `__NW_FieldKitchenPreview_v1` 契约标记；结构、Prefab 来源和门轴仍有效时直接复用。修改相机、灯光或根层级时应递增标记后缀，让场景只重建一次。

### 6.4 自动审计不等于美术通过

自动化能证明资产存在、尺寸合理、Mesh 预算、材质 Shader、Collider、锚点、门轴和重复生成稳定；它不能证明轮廓漂亮、操作反馈清楚、长时间观看不疲劳或与全游戏风格一致。审计报告继续标记 `manual_review_required`。

## 7. 当前证据与下一步

- C# 编译：0 error / 0 warning；
- `NomadFieldKitchenPrototypePipelineTests`：4/4；
- `Game.NomadWorkshop.Editor.Tests`：18/18；
- 连续两次生成后，Profile、材质、Mesh、Prefab 和预览场景 GUID / Dependency Hash 保持一致；
- 固定预览镜头已人工检查，厨房轮廓、三扇门、炉盘、水槽、水龙头与五材质分区可读，但仍属于原型品质。
- 参数化 Prefab 已进入实体物流 Foundation Harness：中门由任务阶段直接旋转 Pivot，`StorageAccess`、`WaterInput`、`WasteOutput` 和 `WorkPosition` 已被实际消费，而不再只是结构审计字段；食材、水、成品餐食和污水均通过居民随身库存跨空间搬运。

下一步不继续给厨房增加外观细节，而是用同一实体物流接缝推进优先级更高的饮水 / 排泄 / 污物清运链，并观察门、站位和输出口在多人任务下是否仍合理。只有第二个形体和交互明显不同的设施也证明相同结构值得复用时，才把当前游戏本地代码提炼成通用参数化道具框架或 Project Skill；不要从单个样本过早抽象。资源流实现、验证边界与下一轮实验见 [`nomad-workshop-resource-flow-foundation.md`](nomad-workshop-resource-flow-foundation.md)。

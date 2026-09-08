# 局部甲板覆盖与通行样板

承接[扩建设计](nomad-workshop-expandable-decks-design.md) S1。起步甲板仍保留当前尺寸与坐标原点，本轮先消除矩形假设，再接入可见、可走的局部板；材料施工、结构事务和旧档迁移仍是 S1 的后续验收，不能因几何样板通过就宣称扩建完成。

## 工作计划

- [x] Simulation 建立不可变的板片并集减孔洞，供设施占地与功能净空完整覆盖检查使用。
- [x] 连续通路预览使用同一支撑区域，禁止跨孔洞、凹角和小于采样间距的板间缺口；保留矩形调用兼容与候选不落账语义。
- [x] 从规范化板面派生 Unity 地板与碰撞，证明跨缝可走、孔洞没有隐藏地板；接入独立三居民车辆的局部板实际体验。
- [x] 完成边界、输入顺序、重叠板、旋转占地、不同楼层和实际 Adapter 的针对性验证；记录可观察证据与剩余范围。

## 数据与调用边界

现有 `ContinuousFacilityPlacementLedger` 与 `ContinuousDeckReachabilityProbe` 分别保存矩形边界，现有 WorldView / 导航场景也各自生成矩形地板。这些调用方必须逐步使用同一支撑数据，不能单独扩大包围盒。

两种可行表示中，直接维护任意多边形会增加布尔运算和编辑成本；本轮选择整数毫米的轴对齐板片和开口，构建时按边坐标划分并合并为互不重叠矩形。板片输入顺序、重复与重叠不影响物理覆盖；输出只是一份可重新派生的面分解，不充当有稳定 id 的结构实例或存档。

`DeckSupportRegion` 拥有规范化支撑面和包围盒内的缺口。旋转设施仍使用已有有向矩形与分离轴判定：必须在外包围盒内，并且不与任何缺口内部相交。接触边缘允许；沿用 0.0001 mm 浮点容差，不用面积阈值放过狭窄缺口。查询不从美术模型推导规则。

预览按居民净空保守检查采样点和点间扫过的区域。静态支撑连边在区域创建后缓存，拖动设施只重新叠加设施障碍。最终仍由真实 Unity 导航与物理裁决；不能将预览近似等同于实际人体通行。

本轮不更改 Framework 的公共 API、五层依赖或持久化 Schema；新增类型属于游牧工坊 Simulation，区域实例由上层布局/结构所有者创建。结构更新将创建新实例，不能在查询过程中原地修改板数组。

## 实际入口与接线

运行 `Assets/Game/NomadWorkshop/Scenes/LocalDeckSupportSample.unity`。原三居民、设施和旅程保持参考车辆配置，左侧增加 3.6 × 4.8 m 平台并扣除 0.8 × 1.2 m 孔洞，净增加 16.32 m²。两根可见托梁伸入旧车架，原车架和设施不重新居中；托梁尚未参与承载预算。

布局为 `ArtFirstPass/Definitions/NW13_DeckSupport.asset`，板面/碰撞/托梁为 `ArtFirstPass/Models/NW13_*.asset`，由 Unity 顶点代码生成。`NW13_Vehicle.prefab` 只在车体副本关闭对应左侧板形成入口。既有 NW5 模型、Prefab 和 NW_DeckLayout 均未覆盖。

`FoundationDeckSupportSurface` 在 Awake 根据布局重建完整碰撞，Renderer 只显示原矩形外的板面，以保留原精制甲板贴图；组件拥有并销毁生成 Mesh。System 的账本、净空与预览使用同一区域实例；辅助网格和地板 Adapter 从相同静态布局建立等价快照。`CreateBounds()` 仍代表起步/车架矩形，不是外扩支撑真值。

建造饮水站时选择 **(-6.2 m, 1.0 m)、朝向 0°**，可走正式设施提交事务；孔洞中心 **(-7.6 m, 0)** 会被拒绝。实机 `Screenshots/nomad-local-deck-overview-02.png` 在实际提交第七座设施后拍摄，记录为 `deck-support-overview-review-02.json`。镜头换到另一侧以看清外扩轮廓，灯光与材质未为截图另行调整；外扩板的封边、板缝、护栏仍待完成。

Editor 重建菜单为“Assets/SSFramework/游牧工坊/首版美术/创建局部甲板覆盖样板”，只重建自己的候选场景/资产，要求非 Play、无脏场景。

## 验证与边界

Unity 6000.3.22f1 / Windows Editor。最终通过 **195 项**；精确发现、计划、READY 预检、dispatch、完整终态和身份核验在 `Logs/AIValidation/nomad-warm-art/`。下表给出文件前缀，不将此前失败或重复执行计入通过数量。

| 范围 | 通过 | Job | 证据前缀 |
|---|---:|---|---|
| 新支撑几何 EditMode | 15/15 | `87a81d949f7f` | `deck-support-edit-03` |
| 原连续摆放 EditMode | 14/14 | `e180b8f7b77b` | `deck-placement-legacy-01` |
| 原连续预览 EditMode | 5/5 | `676f89c54b7e` | `deck-reachability-legacy-01` |
| 局部甲板场景 PlayMode | 16/16 | `3020598c3ca4` | `deck-support-play-04` |
| 原参考车辆 PlayMode | 24/24 | `4dfde651cbae` | `deck-reference-regression-01` |
| 原 Warm PlayMode | 12/12 | `721d1f841491` | `deck-warm-regression-01` |
| Foundation 完整 fixture PlayMode | 109/109 | `f8283e499abb` | `deck-foundation-regression-01` |

新用例覆盖 L/U 凹口、内部 1 mm 孔、旋转占地、跨板缝、必要净空、重复板/顺序/不可变输出、不同楼层、窄桥与对角接触。线段按区间完整覆盖，沿最外沿也不能跨越贯穿缺口。实际场景检查 Collider 孔洞射线、板缝等高、原生路径绕孔、正式设施建造和真实提罐上板、暂停及位置恢复。调用方回归包含原有生活、真实搬运、取消、建造回滚、存档与不同种子往返旅程。

正式 Foundation 检查点仍按既有协议中止瞬时携物行动，把水归还车载水箱，释放掌心所有权；验证居民留在外扩板上、水量守恒，不能写成携物动作原位续播。NW12 的专用楼梯动作快照与此边界不同。

回归捕获放罐末段固定蹲身偏移导致臂长不足。`FoundationResidentCarryIK` 现在按腕部目标与臂长补偿骨盆位置，保留 3% 肘部弯曲余量，物品和导航胶囊不移动。掌心 <15 mm / 4°、脚部 <45 mm、肢体与水罐避让门槛均未放宽，并新增固定脚底时臂长比 <1 的断言。深跪姿的审美仍需后续调整。

`Screenshots/nomad-deck-ground-reach-01.png` 已实际查看，对应真实下放进度 0.405，掌心误差约 1.007 mm / 1.441°，双脚误差均小于 0.002 mm；见 `deck-ground-reach-review-01.json`。该帧不代替动作末段测试，放罐仍偏深跪且衣袖/衣摆较粗糙。画面使用原 Very Low / Direct3D12 / URP 17.3.0 配置。结束后已回到干净、非 Play 的 ReferenceVehicleSample，观察器解绑；19 个源/测试/资产文件摘要见 `deck-support-source-hashes-01.json`，环境记录为 `deck-support-final-editor-checks.json`。

最终编译错误为 0。Console 保留 1 条“主文件与备份均无法反序列化”，已核对为 `NomadFoundationVerticalSlicePlayModeTests.CheckpointOperation.cs` 中 `LogAssert.Expect` 声明的损坏存档测试日志，时间也属于该已通过用例；不将它误报为新增运行时故障，也不把 Console 声称为空。原始记录见 `deck-support-compile-final.json` / `deck-support-console-final.json`。

## 后续优先级

1. **精确停靠后续已收口。** [统一短段支撑与身体查询](nomad-workshop-exact-docking.md)使原问题位置 (-6.0 m, 0) 能真实到岗倒水，并验证七种平移/旋转、静态阻挡、暂停与恢复；同时修正同帧视觉 Collider 和拐角站姿读档误判。该阶段最终 183 项通过，仍不代表任意布局穷举。近期先回到 Foundation §9 的 B 美术观感，再继续以下结构功能。
2. 把板片接入有稳定 id 的结构提交，补材料、支撑连通、拆除依赖、候选物理/导航准备及取消回滚；正式结构真值落地时再升级 Schema 并验证旧矩形迁移。
3. 继续板缝、封边与护栏，再接局部上层、跨层行动与切层。当前板体分解仍有内部侧面，未承诺大型甲板的最优网格或性能。

未执行 Player 构建、性能基线或三四层场景验收。静态布局不保存为玩家结构实例，也不承诺不同布局资产之间加载同一存档保持结构；本阶段不等于 S1 或首版美术样板全部完成。

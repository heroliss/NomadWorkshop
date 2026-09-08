# NW15 紧凑单人陡梯实验

> 阶段：保守尺寸与单人通行占用工程收口，待用户观感反馈。日期：2026-09-08。独立实验，不是正式多层建造。

## 目标、取舍与入口

[CompactSteepCarrySpike](../Assets/Game/NomadWorkshop/Scenes/CompactSteepCarrySpike.unity)连接相差 3.2 m 的上下平台，复用 NW10 工装人物、NW11 水罐、共享 Humanoid/IK 和正式身体移动 Adapter。用 Unity 菜单“Assets/SSFramework/游牧工坊/首版美术/创建或打开紧凑单人陡梯实验”可重建；Play 后面板支持上下行、暂停、取消和中途检查点。

| 几何 | 当前值与含义 |
|---|---|
| 踏步 | 16 级，每级高 0.20 m、深 0.25 m；坡度约 38.7° |
| 可踩踏宽度 | 1.48 m；两侧栏杆立柱内侧间距约 1.57 m |
| 长度 | 踏板投影 4.0 m；扶手/立柱外包约 1.69 × 4.06 m，不含平台 |
| 比较口径 | 踏板投影从原直梯 1.65 × 5.4 m 缩到 1.48 × 4.0 m，减少约 34%；不把它等同于整个车内占地 |
| 携物净空 | 原完整水罐携行半径约 0.641 m；继续保留 0.67 m 导航半径，1.48 m 踏面相对直径 1.34 m 共余 0.14 m，约每侧 0.07 m |

这仍未达到用户希望的 0.85–0.95 m 单人窄梯。当前成果是确认一条更短、更陡的保守路线，而不是拿现有水罐缩进任意窄梯。更窄宽度需要小容器/不同携姿，或真正方向相关的净空方案。

踏板复用 NW12 的折边薄钢网格，由 [生成器](../Assets/Game/NomadWorkshop/Editor/NomadCompactSteepStairPipeline.cs)按尺寸缩放并保留真实 MeshCollider，配侧梁、栏杆、木扶手和踏缘标记。上层楼板从最后一级出口起铺，避免楼板穿入最后两级。场景不使用不可见斜坡替代踏面。上下大平台只是实验基座，不能作为车辆空间美术样板。

## 通行和取消语义

[NomadStairTrafficGate](../Assets/Game/NomadWorkshop/Runtime/Navigation/NomadStairTrafficGate.cs)复用现有 ReservationLedger，按完整的平台间搬运占用通道：

- 原持有人可重发或改选目的地；其他 owner 被拒绝，不能释放别人的租约。
- 暂停和中途取消保持占用。取消只让身体停止，不能假定它已经离开楼梯。
- 返回经过验证的平台停靠点或正常到达后释放；超时停在楼梯也继续占用。
- 楼梯中途的非 moving 检查点同样恢复占用；通道被另一 owner 持有时，拒绝该恢复且保持原身体状态。
- 系统销毁释放自己的租约，并调用基类清理，保留 Context/Bag 原有生命周期。

当前只运行一个实际人物，用第二个 owner 验证排斥；没有接入多人寻路、排队、公平性或死锁恢复。这份实验也没有改变正式世界存档格式。

## 验证证据与局限

[PlayMode fixture](../Assets/Game/NomadWorkshop/Tests/PlayMode/NomadCompactSteepStairPlayModeTests.cs)复用真实身体/掌心/肘部/足部断言，增加尺寸和中途占用生命周期检查。编译无错误；最终补全栏杆立柱后 job `9c9b12929136` 为 **7/7**，92.38 s，精确名单和结果已核对，证据目录 `Logs/AIValidation/nomad-warm-art/compact-play-04/`。此前 NW10/NW11 与薄踏板版本 `9bb42b79a376` 同为 7/7，作为修改前证据保留在 `compact-play-03/`。

实际上/下行检查完整罐体定向包围盒的**离散重叠**，包括踏板、楼板和扶手；它不是连续扫掠。未来方向相关方案还需要覆盖平移和旋转、插入路径中部障碍的反例、暂停/恢复与导航接线。单次固定朝向 BoxCast 不能证明转身安全，本轮不新增未被运行消费的通用扫掠框架。

人物保持共享 Move + 右手携物 IK + 踏面足部 IK，没有专用负重登梯动作。已查看[持罐近景](../Screenshots/nomad-compact-stair-carry-02.png)和[结构全景](../Screenshots/nomad-compact-stair-overview-02.png)：可见薄踏板、扶手和实际人物，所见帧水罐未穿入台阶；普通行走步态与负重身体倾斜仍有改善空间，截图不替代完整动画或用户审美验收。全景观察发现上层栏杆缺少立柱，已补入最终生成器并完成落盘场景复验。

本轮初次误用 MCP 的 `filter` 别名，实际扩大成全量 PlayMode **1049/1049**（`a151840b5b36`，1655.89 s）；该结果仅属于当时工作树，不用于证明后续修改。之后从完整 discovery 经 UnityTestEvidence 生成精确 testNames，保留计划、dispatch、最终逐用例结果，再核对身份和数量。没有为已有机器契约再增加一条根规则。

## 下一步来源

本实验工程收口后回 [Foundation §9](nomad-workshop-foundation-vertical-slice.md#9-近期队列美术样板与可扩建建造) 的 B/S0，优先让同一车辆镜头和完整旅程达到首版体验要求。更窄携物档位、方向相关连续检测、专用负重姿态与正式多层施工各自作为后续可验收工作，当前不同时扩张。

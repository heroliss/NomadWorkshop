# 游牧工坊：沙漠环境首版

接续 `c61da73`。目标是让参考 J 中灰暖砂地、碎石与层状岩石的整体层次进入同一俯瞰实机，保留车辆和居民的可读性。先完成一个可复现的环境资产与运行时接线，再据实际画面调整；不同时增加天气玩法或车外地形导航。

## 可检查计划

- [x] 用 Blender 脚本制作起伏克制的砂砾地、四种层状岩石、大小碎石和少量枯草；降低旧巨石高度，保留车辆与右侧停靠通路净空。
- [x] 生成砂地/砂岩颜色、切线法线和表面贴图，旧金属条车辙替换为土色透明压痕；共享材质，控制纹理和网格规模，核对 UV、退化面、FBX 往返与三轴。
- [x] 环境 Prefab 显式声明滚动表面、网格 UV 的米制间距和循环地景组；行驶使用同一绝对距离，暂停/回退/恢复不能漂移。旧环境仍使用已有路径。
- [x] 通过 Unity Editor 接入现有 ReferenceVehicleSample，保存并回读资产引用；不覆盖 NW1 环境和对比场景，不新增玩法 Collider。
- [x] 在同一相机/光照下实际查看俯瞰、地表近景和行驶画面，验证行为、材质绑定、暂停与恢复；记录规模和当前限制，形成阶段提交。

地景循环是当前直线旅途的视觉表现，不等于车辆已经在开放世界地形上行驶。环境配方与共享表面能力保持在 NomadWorkshop 内；不为单个美术切片扩张 Framework API。

## 当前资产与制作

制作脚本：[blender_nomad_desert.py](../Tools/ArtPipeline/Blender/blender_nomad_desert.py)。可编辑源场景：`ArtPipelineOutput/DesertEnvironment/v02/NW6_Desert.blend`；Unity 使用 `ArtFirstPass/Models/NW6_Desert.fbx` 和 `ArtFirstPass/Prefabs/NW6_Desert.prefab`，导入来源摘要为 `ArtFirstPass/NW6-source-manifest.json`。这些 `ArtFirstPass` 路径位于 `Assets/Game/NomadWorkshop/` 下。

地面为 80 × 80 m，仅远侧路肩缓慢抬高。四种岩石采用不规则截面、局部层理和侵蚀倒角，结合细碎石和少量枯草组成 16 组路边景物。相同组内石块合并网格；当前总计 **114,314 三角面、34 网格**，UV 完整、导出和回读退化三角面均为零，Unity 导入再次校验规模、边界、三轴和材质。

砂地与砂岩各使用三张 2048² 图：颜色、切线法线、表面（R 金属度、A 光滑度）。它们由 Blender Python/NumPy 的周期噪声、独立矿物颗粒和层理函数直接写出，不经过 AI 生图或高模烘焙。压痕为 256 × 1024 RGBA 平铺图，URP Lit 透明混合、关闭深度写入与投影，仍接受光照和阴影；关闭镜面反射并调低主色，避免像另一层亮色路面。共 7 张 PNG，磁盘约 15.6 MiB；这不是显存/Player 性能测量。

首次实机 `nomad-desert-overview-01.png` 中法线产生过密的网格状起伏，改为较浅且分散的颗粒。第二轮近景已实际查看：`Screenshots/nomad-desert-ground-02.png`。岩石仍是风格化首版，表层断裂与地表过渡不具备参考 J 的精细度；后续应结合设施、服装和整体明暗一起调整，不继续单独堆高环境细节。

从源场景逐顶点读取，左侧地景最靠近中心的 X 为 -8.223 m，右侧为 13.205 m，最高点 Y 为 -0.127 m（甲板为 0）；当前循环只改变 Z，因此右侧 X∈[6,13] 的停靠通路在循环中仍保留。证据为 `Logs/AIValidation/nomad-warm-art/desert-clearance.json`。该检查仅覆盖装饰几何与当前走廊，不能当成未来可施工地形/车外导航已完成。

## 行驶接线与替换约束

`FoundationEnvironmentVisual` 记录三个滚动 Renderer、网格 UV 的 V 每单位对应米数和 16 个地景组，循环长 60 m。地表为 6 m/UV，压痕为 1.4 m/UV；UV 的 V 沿环境局部 +Z，材质自身 Tiling 和初始 offset 保留。替换资产可改变组数/表面数，并在 Prefab 中重绑引用；运行时不按 NW6 网格或材质名猜绑定。

`FoundationEnvironmentPresentation` 从绝对微米距离计算位姿和属性块，不累计帧位移，不改变共享材质，也不移动甲板、角色或导航根。表面与地景不可重复引用或嵌套叠加移动；无效绑定在任何表现修改前拒绝。`FoundationJourneyPresentation` 持有该对象并释放，仍独立处理轮轴、履带和真实时钟驱动的扬尘。未配置组件的 NW1 历史环境保留原有 4 m 地表与命名发现规则。

地表/压痕目前是整段循环景观，不是逐轮留下永久痕迹；不记录地形变形，也不把同一组岩石的循环当作程序世界生成。

## 复现顺序

1. 在项目根执行 `D:/Blender 5.2/blender.exe --background --factory-startup --disable-autoexec --python-exit-code 1 --python Tools/ArtPipeline/Blender/blender_nomad_desert.py -- --output ArtPipelineOutput/DesertEnvironment/<新版本>`。
2. 查看导出结果与 manifest，通过后将该版本产物复制到 `ArtPipelineOutput/DesertEnvironment/current`。`ArtPipelineOutput` 被 Git 忽略；生成器和已导入 Unity 的资产纳入版本控制。
3. 在非 Play、编译空闲且场景已保存时，经 Unity Editor 菜单 `Assets/SSFramework/游牧工坊/首版美术/导入沙漠并打开参考车体` 导入。需要已有 ReferenceVehicleSample；先做参考车体，再接入环境。
4. 查看真实 Game View，并验证实际旅程；离线 `passed-export` 和 Editor `passed-import` 均不声明视觉/运行时已经验收。

## 验收与限制（2026-09-08）

Unity 6000.3.22f1、URP 17.3、Windows Editor、RTX 4060 Laptop GPU、Very Low，Game View 1225 × 713，截图 2×。俯瞰采用场景原相机：位置 (9,15,11)、Euler (46.5438,219.2894,0)、透视 FOV 42；没有调整场景曝光或主光来掩盖资产差异。

| 范围 | 最终证据 |
|---|---|
| 米制 UV、初始 Tiling/属性块、反向/60 m 循环、重复投影/恢复、无效引用无部分修改、旧环境、释放 | `FoundationEnvironmentPresentationTests` EditMode **10/10**，job `36ccd4d73c1f`，0.83 s |
| 当前参考车体真实旅程、地表/压痕暂停与恢复、16 组引用、原携物/设施工作/剖切/HUD | `NomadReferenceVehiclePlayModeTests` PlayMode **20/20**，job `3854be54d002`，52.65 s |
| 实机画面 | [最终行驶俯瞰](../Screenshots/nomad-desert-driving-final.png)：真实旅程 15.1 m，tick 36950 后自动暂停，read model Ready；[砂砾近景](../Screenshots/nomad-desert-ground-02.png)已实际查看 |

完整发现名单、精确计划、派发、终态逐用例与身份核验位于 `Logs/AIValidation/nomad-warm-art/desert-{edit,play}-final-*`。每个 job 前独立验证新鲜 READY；测试使用 StairCarrySpike 作为隔离宿主。先前 10/10 与 20/20 记录保留，最终轮在压痕材质落盘后执行，不重复累计为 60 个用例。

来源脚本与 Unity FBX/PNG 的 SHA-256 已同 manifest 核对。当前 42 份相关代码/资产摘要记录于 `desert-source-hashes.json`，仅为本次变更范围，不是全项目复现或 Player 构建证明。图像与现场信息见 `desert-visual-final.json`。临时相机和材质探针随退出 Play 还原，自动观察回调已移除，未写玩家手动存档。

阶段仍未验证目标 Player 的帧率/显存、永久车辙、真实车外地形与天气；当前砂地循环较容易看出重复，岩石层理与服装/设施的细节也仍比参考 J 简化。后续先处理模型空间与代表设施，避免把本轮环境独立精修成与可玩甲板脱节的样板。

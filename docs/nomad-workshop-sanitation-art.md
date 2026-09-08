# 生活设施轮廓与主镜头：旱厕样板

接续 `6947c17`，回到 J 的整体观感。当前主镜头 `(9,15,11)` 主要看到饮水站、画架和旱厕背面；实际比较 `(9,15,-11)` 后，这三类设施的用途和人物正面更清楚，小厨房仍可见。旱厕的平板帆布与方块底座仍明显偏原型。

## 本阶段计划

- [x] 固定同一场景/暂停初态/原光照，比较实际主镜头；记录 `nomad-art-composition-before-01.png` 和 `nomad-art-composition-angle-02.png`，均已打开检查。
- [x] 用 Blender 制作带折边底盘、空腔座体、椭圆座圈、褶皱帆布与简洁生活件的 NW14 旱厕；保持现有 0.8 × 0.9 m 占地和操作位。
- [x] 保留源组件，烘焙共享材质图集、导出 FBX 并检查往返；不把造型细节焊进车体。
- [x] 通过 Editor 接入 ReferenceVehicleSample 与局部甲板副本，落盘更清楚的主镜头配置；完成拆装/清运与检查点回退验证。
- [x] 查看相同镜头的前后实机与近景，验证设施占地/材质和使用、污物搬运、暂停/恢复路径；阶段已提交为 `0827912`。用户最终观感验收仍保留。

旱厕仍使用现有前侧操作位；本轮不伪造已经有坐下如厕动画。可拆污物桶暂沿用原独立物件，模型空腔须容得下它，不能再用实心底座盖住桶。保持原空间签名和存档兼容；后续精细桶也应走可替换容器绑定。

主镜头调整是正式可玩入口的修改，最终效果必须从落盘场景重新 Play 验证，不能只交付临时相机截图。暖光、其余设施与甲板封边继续依照 Foundation §9 的整体目标推进，不用单件模型通过宣称 J 已达到。

## 制作与导入边界

当前输出为 `ArtPipelineOutput/Sanitation/v04`，配方 `Tools/ArtPipeline/Blender/blender_nomad_sanitation.py`。`NW14_Toilet_Source.blend` 保留独立网格和可编辑曲线；随后显式把曲线转换成网格，再烘焙 2048² Color/Normal/Surface，保留六组渲染网格和座盖枢轴。导出与 FBX 回读均为 11,200 个三角面，无退化三角形，完整 UV；帆布含真实褶皱。底盘顶部为 18 mm，给现有桶底的 20 mm 高度留出间隙。Unity 成品进入 `ArtFirstPass`，源 `.blend` 仍为本地可重建输出。

`NomadBakedPropTextureImporter` 合并厨房、水罐和旱厕三类同构的图集导入：颜色使用 sRGB，法线与 Surface 使用线性数据，Surface 的 R/Alpha 分别存放金属度/光滑度。调用者仍负责来源哈希与模型/场景归属，没有把设施空间或动画接线塞入纹理工具。

`NomadFoundationWorldView.artCameraInitialPosition` 只控制美术车辆镜头会话的初始甲板局部位置，旧场景默认保留 `(9,15,11)`，本轮两份场景设为 `(9,15,-11)`；之后仍由现有轨道镜头处理玩家输入。Editor 接线在每次打开场景后重新加载目标定义，并保存回读核对，避免切场景卸载尚未使用的资产后写入空引用。

## 验证与后台帧驱动

EditMode `NomadSanitationArtTests` 5/5（job `6d7e0a8c8410`，证据前缀 `Logs/AIValidation/nomad-warm-art/sanitation-edit-02`）：原空间签名、独立座盖、当前实体桶与导入表面的净空，以及三类图集的绑定/颜色空间/Alpha 契约。净空直接读取导出三角形，对实体桶各部件的包围盒做分离轴检查；额外将探针插入侧壁，要求它确实报告相交。静态净空不替代搬运路径或美术评价。

参考车辆 PlayMode `NomadReferenceVehiclePlayModeTests` 25/25（job `473d444c7dfb`，`sanitation-reference-play-02`）：新增测试从合法停靠检查点开始，实际取桶、暂停、召回回装、恢复携带中检查点，再完成一次清运回装；检查物品唯一性、原实例归还、900 mL 库存守恒与默认镜头保持。原携水、设施机械反馈、人物身份和旅途表现回归同时通过。

初轮整组 `ee270b733e7f` 为 21/25；只读追踪同一失败动作的诊断 job `de1f6a09ad32` 仍失败。Test Runner 已处于 NoThrottling，101 个帧样本的平均间隔仍为 127.3 ms、最大 263.5 ms，首次可见倒水进度达 19.4%，漏过抬起段。临时在 Editor update 请求 `QueuePlayerLoopUpdate` 后，同一断言 job `774b58657499` 通过，1591 个帧样本均值 7.1 ms、最大 85.2 ms，并观察到从进度 0 开始的抬起过程。原始记录为 `sanitation-test-frame-probe-01.csv` 与 `sanitation-test-frame-pump-01.csv`；这些是本机后台测试节奏诊断，不能作为游戏性能基线。

因此新增测试专用 `NomadBackgroundFramePump`，由美术 fixture 的 SetUp/TearDown 拥有，并在退出 Play 时兜底解除订阅。不改变模拟时间、断言阈值、倍速或个人配置，不调用 OS 焦点工具。加入后参考车辆整组 25 项通过；它只改善后台采样条件，不能替代长帧容错测试。

已实际查看 [Unity 主镜头](../Screenshots/nomad-sanitation-overview-01.png)、[最终同初态镜头](../Screenshots/nomad-sanitation-overview-02.png)、[旱厕近景](../Screenshots/nomad-sanitation-close-01.png)与[局部甲板镜头](../Screenshots/nomad-sanitation-local-overview-01.png)。近景临时移动镜头；帆布、座圈、中空座体与原独立污物桶可辨认。桶仍为旧橙色灰盒，座盖尚无玩法驱动开合；整体车体、驾驶台与画架仍需继续靠近 J。

最终补充回归：局部甲板 PlayMode 20/20（`279d38068344`，`sanitation-local-play-01`）；原 Warm PlayMode 12/12（`0f68887a26af`，`sanitation-warm-play-01`）；轨道镜头 EditMode 3/3（`6fc53fde9de1`，`sanitation-camera-edit-01`）。这些与上述 5/5、25/25 属于本阶段不同范围，完整证据保存在 `Logs/AIValidation/nomad-warm-art/`，不声称整款游戏或用户美术验收已完成。

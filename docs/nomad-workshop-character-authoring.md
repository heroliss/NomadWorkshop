# 游牧工坊：参数化人物制作候选

2026-09-08 更新：当前参考车辆使用基于 NW3 派生的 [NW10 工装候选](nomad-workshop-workwear.md)，原 `ResidentCrewSample` 与 NW3 资产继续作为基线。NW10 制作源文件、固定配方的变形检查与最新实机截图统一在该阶段文档维护；下文的 NW3/NW2 结果是各自历史资产的证据。

2026-09-06。用户明确否定当前方块衣物/附件的人物观感，并提出复用完整人物制作系统，通过参数生成符合游戏风格的居民，后续支持随机外观。本页记录候选核验与小样验收；**当前试玩入口为三种 Quaternius 现成服装人物的 ResidentCrewSample，已验证共享动作和各自的持桶楼梯。UMA / MPFB 尚未完成 Unity 内验证，旧 Warm 场景保留为原人物基线**。主排期在 [Foundation §9](nomad-workshop-foundation-vertical-slice.md#9-近期队列美术样板与可扩建建造)。

## 当前判断

当前先用现成蒙皮服装完成正常人物的可玩对比，避免为可选捏人功能拖住美术样板。运行时生成器仍优先研究 UMA 3，它与 Unity 运行时生成、换装的长期方向匹配；Blender 用于修改基础形体、工装和材质，MPFB 保留为本地批量制作的备选。没有将候选包提前变成 Framework 依赖，也不以功能列表替代人物观感验收。

此前 `NomadWorkshopResidentArtPipeline` 从正常人体按高度切分材质，再添加刚性立方体背心、围巾、口袋与背包。该配方只验证挂骨与身份区分，没有形成合格的人物造型。停止继续精修这批附件；优先得到实际镜头下正常的头颈、肩袖、腰胯、手足和服装轮廓。

## 2026-09-06 核验

| 候选 | 已核实的能力与来源 | 对本项目的取舍 |
|---|---|---|
| UMA 3.05 | [官方仓库](https://github.com/umasteeringgroup/UMA)注明免费、MIT；[3.05 发布](https://github.com/umasteeringgroup/UMA/releases/tag/v3.05)发布于 2026-08-31。[DCA 文档](https://github.com/umasteeringgroup/UMA/blob/v3.05/UMAProject/Assets/UMA/Docs/DynamicCharacterAvatar.md)支持体型/面部参数、服装槽位、颜色、保存和重建；[随机人物](https://github.com/umasteeringgroup/UMA/blob/v3.05/UMAProject/Assets/UMA/Docs/RandomAvatar.md)提供身体与服装分别随机的入口。 | 优先做三个居民的小样，适合长期运行时组合。新资产仍要适配服装槽位、骨架与变形。实际成本、服装质量、生成耗时和动画兼容均待测；附带素材逐项保留来源和许可证。 |
| MPFB 2.0.17 / MakeHuman | [Blender 官方扩展页](https://extensions.blender.org/add-ons/mpfb/)提供人体参数、自动绑骨和服装库；[2.0.17](https://static.makehumancommunity.org/mpfb/releases/release_2017.html)增加带种子、范围约束、服装槽位的批量随机，作者仍将新随机功能列为实验性。[许可](https://github.com/makehumancommunity/mpfb2/blob/master/LICENSE.md)区分 GPL 插件代码与 CC0 核心图形资产/输出。 | 适合本地 Blender 自动化，导出普通资产后不需要 Unity 运行 MPFB。运行时连续体型变化/换装仍需另外接线；额外社区服装单独核查许可。 |
| Character Creator 5 | [官方产品页](https://www.reallusion.com/character-creator/)提供人物、服装、材质及 Unity 导出；[2026-05-25 软件 EULA §5](https://www.reallusion.com/Content/EULA/AP/EULA_AP.htm)对基于 CC Base Model 的角色列出用于人物生成系统的限制。 | 不是当前免费自动化和后续游戏内人物生成的优先候选。若以后选择，需针对实际用途核对授权；没有购买或安装。 |
| Quaternius 模块化服装 | [作者页面](https://quaternius.itch.io/modular-character-outfits-fantasy)免费 Standard 包内确认有 Peasant 男女整套及 Arms / Body / Legs / Feet 分件、贴图和 CC0 许可。已组合男女衣物、正常头部和三种发型，接入独立玩法和楼梯场景。 | 是当前正常人物的对比候选，衣服偏中世纪；不是体型/捏脸生成器。三种固定外观已通过定向动作检查，整体观感仍需用户评价。 |

项目为 Unity 6000.3.22f1、URP 17.3.0。UMA v3.05 标签中的 `package.json` 要求 Unity 6000.3，但内部版本仍写 3.0.4；它声明 Burst / Collections / Jobs 等依赖。版本范围吻合只说明值得实验，不证明导入或构建已通过。固定 tag、实际包哈希和最终解析依赖，避免仅记录包内版本字符串。官方完整 UMA3 包约 1.16 GB，实验先审计包内容与必要依赖，不把全部示例导入正式项目。

## 三居民外观候选（NW3，已完成定向工程验收）

新入口为 `Scenes/ResidentCrewSample.unity`，菜单“Assets/SSFramework/游牧工坊/首版美术/生成并打开三居民外观候选”。三个普通 Humanoid Prefab 分别采用短发、双髻和灰发胡须，去掉统一布帽，以独立衣料材质控制青绿、土黄和灰蓝色。男女服装各自保留源骨架，驾驶者整体放大 4%；这是三个固定组合，尚不支持玩家捏人或任意体型变化。

[参数配方](../Tools/ArtPipeline/Blender/nomad_resident_variants.json)和[Blender 生成器](../Tools/ArtPipeline/Blender/blender_nomad_resident_variants.py)导出中性 FBX 后才摆放预览，不把预览姿势写入游戏模型。三人的实际 Blender [正面](../ArtPipelineOutput/CharacterIntake/ResidentVariants/current/residents-front.png)与[俯视](../ArtPipelineOutput/CharacterIntake/ResidentVariants/current/residents-overhead.png)已查看；女性低领需要保留更低的颈胸过渡，并裁去被衣服覆盖的侧肩，男款头部裁剪阈值不能直接沿用。服装仍有束带、长靴和束腰等中世纪特征，尚未定版为废土工装。

`NomadFoundationWorldView` 增加可选的居民稳定 id → Prefab 绑定。未绑定的居民沿用默认模型，重复 id、空模型或缺少 Controller 的绑定明确拒绝。绑定只归表现层所有，不消费玩法随机数，也未增加存档字段或生成器依赖；它解决当前固定居民在表现重建后的外观选择，不代表已完成随机居民配方存档或工作中换装。

导入共用原候选的 Avatar、骨骼、实际眼部朝向和 Bone4 检查。固定配方/生成器及三份 FBX 必须与 `ArtFirstPass/Models/NW3_manifest.json` 的哈希一致后才能生成 Prefab。三个模型分别为 17,708 / 22,420 / 19,213 三角形，8 / 8 / 9 个蒙皮 Renderer；尚未声明目标 Player 性能达标。新增 `NomadResidentCrewPlayModeTests` 复用原 12 项工坊检查并增加身份/外观保持检查；三个对应楼梯候选分别复用原 4 项断言。

| 本轮 PlayMode 范围 | 终态与证据目录（`Logs/AIValidation/nomad-warm-art/` 下） |
|---|---|
| 三居民工坊与身份保持 | **13/13 Passed**，job `e4a9c9a0857f`，47.54 s；`crew-warm-02`。包括检查点恢复、更换玩法种子后的固定外观选择。 |
| 短发维修者楼梯 | **4/4 Passed**，job `7cac8636a466`，71.59 s；`crew-stair-mechanic-01`。 |
| 束发居民楼梯 | **4/4 Passed**，job `4048acd00e13`，71.63 s；`crew-stair-caretaker-01`。 |
| 灰发驾驶者楼梯 | **4/4 Passed**，job `8100276057f3`，71.65 s；`crew-stair-driver-01`。 |
| 原 Warm 默认人物回归 | **12/12 Passed**，job `e6caaaee1795`，47.14 s；`crew-original-warm-01`，确认未配置身份绑定的默认路径。 |

合计为新候选 25 项和原场景回归 12 项，**37/37**。每个范围均保留实际发现名单、派发、完整终态逐项明细和匹配判定；此前 NW2 与材质落盘前的 `crew-warm-01` 不重复计入。未在本轮重跑 Foundation 106 项，也不把旧基线计入这 37 项。

实际查看了 Unity 的[俯瞰工坊](../Screenshots/nomad-crew-overhead-01.png)、[束发居民携水](../Screenshots/nomad-crew-carry-01.png)和[持桶上楼近景](../Screenshots/nomad-crew-caretaker-stairs-01.png)。携水帧为 resident-02 持有真实 2,900 mL 水，掌心目标误差约 2.07 mm、角度误差约 2.54°；部分手臂被设施遮挡，不能用这一帧判定完整持桶轮廓。楼梯帧为持 8 L 水、身体高度约 1.404 m 的中途暂停，完整持桶姿态可见，所见肩颈没有明显孔洞。单帧不证明所有服装姿态无穿插；也未逐个居民枚举所有地面/设施动作组合。观察报告与对应场景状态在 `crew-visual-01`，Warm 观察报告本身不含场景路径，应连同当次 MCP 状态读取。

最终汇总为 `Logs/AIValidation/nomad-warm-art/crew-final-evidence.json`。材质稳定后的 529 份相关源码/资产快照作为本轮结束核对范围，另核对三份 FBX、生成器、配方和原始输入；这不是完整仓库或目标 Player 的可复现证明。旧模型与旧场景继续保留，当前试玩使用独立 `ResidentCrewSample`。

复现顺序：在项目根运行 `blender_nomad_resident_variants.py`，传入 `--output-dir ArtPipelineOutput/CharacterIntake/ResidentVariants/current`、`--outfit-dir Assets/Game/NomadWorkshop/ThirdParty/QuaterniusModularOutfits`、`--base-dir Assets/Game/NomadWorkshop/ThirdParty/QuaterniusUniversalBaseCharacters`；Blender 使用 `--background --factory-startup --disable-autoexec --python-exit-code 1`。查看实际预览后将三份 FBX 和对应 manifest（改名为 `NW3_manifest.json`）放入 `ArtFirstPass/Models`，再用 Unity 菜单生成。场景/Prefab 始终经 Editor 修改。新材质还需完成导入与落盘，不能在菜单刚返回时就把哈希视为最终版本：本次 `crew-source-hashes-01.json` 捕获过早，十份材质随后变化，定向导入/保存后另存 `crew-source-hashes-02.json`，旧快照和漂移记录继续保留。

## 单一男款候选的验证基线（NW2）

试玩入口为 `Assets/Game/NomadWorkshop/Scenes/ResidentAppearanceSample.unity`，也可通过菜单“Assets/SSFramework/游牧工坊/首版美术/创建或打开现成人物对比”打开，再进入 Play。它复用温暖工坊的真实玩法，仅换成 `NW2_ResidentSample`。楼梯入口为 `Scenes/ResidentStairSample.unity`，对应菜单末项为“创建或打开现成人物楼梯对比”，保留真实 18 级踏步与上下楼、暂停、取消、记录/恢复按钮；它仍是独立动作实验，未接入多层建造。目前三名居民使用同一个男款外观，尚未做三种体型和身份配色。

最新资产已实际查看[拿桶](../Screenshots/nomad-character-ground-pickup-04.png)、[放桶过程](../Screenshots/nomad-character-ground-placement-02.png)和[持 8 L 水上楼](../Screenshots/nomad-character-stair-01.png)截图。正常头颈、肩袖、腰胯和靴子可辨认；近景部分腿部被另一居民/设施遮挡，不能据此声称所有姿态无穿插。布帽和服装仍偏中世纪，不能作为最终废土工装定版。此前[俯瞰全景](../Screenshots/nomad-character-sample-overhead-01.png)属于内袖修正前的初始候选。

[Blender 组合脚本](../Tools/ArtPipeline/Blender/blender_nomad_resident_sample.py)保留服装原骨架，组合兼容的头部/眼睛/眉毛及真实蒙皮布帽，不再添加方块衣物。源服装与人体的头颈骨位相容，但四肢有数厘米差异，因此不把整身衣物强挂到旧人体骨架。[来源说明与原始许可](../Assets/Game/NomadWorkshop/ThirdParty/QuaterniusModularOutfits/SOURCE.md)随资产保留。Unity Editor 负责 Humanoid、URP 材质、方向校核与独立 Prefab；没有新增 Framework 或玩法层对候选包的依赖。

最新资产为 8 个网格、19,134 个三角形，每人 8 个蒙皮 Renderer。固定 Peasant 配方裁去被外衫覆盖的内袖肩部 286 个顶点、576 个三角形；原始第三方 FBX/PNG 保持不变。候选 Prefab 显式使用 Bone4，项目 Very Low 档的全局 OneBone 设置保持原状。**Bone4 单独没有消除肩部棕色穿插；实际有效的是内袖遮罩。** 排查也排除了头部裁剪残留。遮罩阈值仅适用于这套固定 T-pose，不能推广为任意衣服或体型的自动适配规则。

当前 Unity FBX 与 `ArtPipelineOutput/CharacterIntake/QuaterniusOutfits/SleeveMask/PeasantResidentPreview.fbx` 为相同字节，SHA-256 为 `69fe8cb8fe3fad9abdd8faf24d76f14eb63372c8fea4ae83335ba67ab1bb774a`，配方与原输入哈希见该目录 `manifest.json`。此前 `export-comparison.json` 对中性几何/权重的 7 位小数比较属于旧的 19,710 三角形配方，不能作为最新遮罩配方的重复导出证明；当前字节核对也不是性能预算或任意服装适配保证。

| 验收 | 本轮证据与结论 |
|---|---|
| 实际导入与朝向 | 三人 Avatar 均 Human/Valid，必要手指与脚骨齐全、Root Motion 关闭、材质均为 URP/Lit；朝向基于实际 BakeMesh 眼部几何。生成与 CompilationPipeline 零错误。 |
| 最新候选工坊 | `NomadResidentAppearancePlayModeTests` **12/12 Passed**，job `5586c049c1e4`，47.35 s。覆盖真实拿起/下放/松手、掌心与脚底、倾倒、设施工作、旅程、暂停，以及携行和下放期间的恢复。 |
| 最新候选楼梯 | `NomadResidentStairAppearancePlayModeTests` **4/4 Passed**，job `668426309fa4`，71.61 s。覆盖 18 级实体踏步的上下行、暂停/继续、取消/恢复、无效检查点保持当前搬运。 |
| 原人物回归 | `NomadWarmWorkshopPlayModeTests` **12/12 Passed**，job `d680a0af43ea`，47.51 s。确认共享 fixture 调整及真实眼部朝向检查兼容原场景；不计入新候选的 16 项结果。 |
| 最新目视取证 | 实际拾起进度 0.826 和下放进度 0.405 的当帧掌心误差约 1.01 mm；楼梯持 8 L 水暂停于 1.40 m 高度。修正后的肩部穿插在已查看拿放帧中消失；单帧不能证明全部衣物动作无穿插。 |
| 未覆盖 | 三种外观、任意比例/模型兼容、生成性能、目标 Player、完整衣物碰撞与用户审美验收。楼梯尚未接入正式多层建造，UMA 仍未在 Unity 中验证。 |

最新汇总为 `Logs/AIValidation/nomad-warm-art/character-motion-final-evidence.json`，精确发现名单、派发、完整终态明细和判定分别位于 `character-motion-warm-02`、`character-motion-stair-02`、`character-motion-original-warm-01`。末轮 Warm 后、Stair 前固定了 501 份相关源码/资产哈希，结束核对无变化；范围包括本玩法运行时、候选资产、场景、测试、Editor 配方等，不是整个仓库或 Player 的复现证明。最新实际观察报告位于 `character-motion-visual-01/*-sleeve-mask-report.json`，包含场景身份。

此前 `character-sample-evidence.json` 的携桶/暂停和 `character-motion-*-01` 的候选测试属于内袖修正前的历史资产，继续保留但不混作最新结果。候选未替换正式默认场景；取证收尾恢复非 Play、干净的 `NomadWarmWorkshop`。

朝向检查曾误用 `SkinnedMeshRenderer.bounds.center`：保守包围盒不能代表动作后的眼部位置。现从实际 `BakeMesh` 几何计算，并针对当前 FBX 的嵌套单位缩放核对 `useScale` 参数；保留错误诊断和失败生成记录，没有通过反转正确模型来迎合错误检查。空眼部网格也明确拒绝，避免除零后的 NaN 绕过校核。该规则目前只落在具体导入验证中，不扩张为所有 FBX 的通用缩放假设。

## UMA 实验进度

官方 `UMA3_f5.unitypackage` 已下载并审计索引、核心代码和文档，SHA-256 为 `3ef141918809da7168c9ca912fc05ade61863b02143080e09579944617b933d5`。独立实验目录已准备；启动第二个 Unity 编辑器的操作被自动审批以 `blocked by policy` 拒绝，未执行，也未换路径重试。主项目没有导入 UMA 或增加其依赖。

随后从[官方 content-pack 固定提交](https://github.com/umasteeringgroup/content-pack/tree/218f523e1283a3bca0a010afada2d6c47b9621d4/ContentPack_3.0)取得原始 `UMA_3.blend`，保留 MIT 许可，并在禁止嵌入脚本自动执行的 Blender 会话中只读检查：156 根骨骼、6 个网格，身体有 22 个 Shape Key（含 Basis），包括脸颊、鼻子、嘴唇等。文件没有服装，贴图也未打包；完整体型 DNA 还涉及 Unity 中的骨骼/形变配置。因此原始 Blender 数据可用于离线研究，不证明运行时生成、换装或三人美术样板已完成。证据为 intake 下 `base-content-source.json`、`blend-inspection.json`，未保存改写上游 `.blend`。

## 外观与生成边界

- 体型、脸型、发型、上衣、裤子、鞋、配色与适量磨损形成外观配方。开始只开放经过验证的小范围比例和兼容服装组合，色彩使用青绿、灰蓝、土黄及少量陶土色，维持温暖、克制的工坊风格。
- 随机化从经过筛选的组合中抽取；服装必须有覆盖/互斥规则，避免上下装冲突、头发穿帽子或身体穿出衣服。参数控制已有形体，不能生成任意新衣服设计。
- 记录生成种子、配方版本、明确选择的资产 id 和参数。只保存种子不足以保证升级生成器或资产后外观不变。外观随机流与玩法随机流分开，读档不得重新抽取居民外貌。
- 复用共享 Humanoid 动作的语义和业务移动。Avatar、T-pose、蒙皮、手指骨、朝向、脚底与手掌参考必须有效；不同身材仍要验证携物可达性、蹲下和楼梯净空。现有固定下蹲位移与水罐尺寸只在当前模型上通过，不能直接宣布支持随机体型。
- UMA 的生成/重建跨帧完成；生成完成后才能取得最终骨架、Renderer 并初始化 IK。销毁、读档和换装必须释放旧订阅/旧绑定；外观重建不得重置居民任务或资源所有权。首轮在进入场景前准备小样，不先实现工作动作中换装。
- Rodin 等来源可以替换整个人物的视觉模型，也可以贡献服装/头发。若要进入同一体型变化和换装系统，需适配拓扑或形变、蒙皮、衣物覆盖、骨架与材质；共享 Humanoid 动作不等于共享所有捏人参数。玩法、存档身份、动作语义和交互配置继续保留。

## 最小实验与停止条件

当前迭代计划（2026-09-06，三居民外观）：

- [x] 从已核验的免费标准包取得分件发型与女性头部，先在 Blender 检查骨位、衣服覆盖和真实前视/俯视组合。以短发、束发和胡须形成轮廓差异，配色分开衣料、皮带、皮肤，避免整张图集染色。
- [x] 制作三个可替换的普通 Humanoid Prefab，以明确居民身份绑定候选外观；先固定组合，不增加玩法层生成器依赖或随机体型 UI。
- [x] 接入独立可玩场景，检查三人可辨认、表现绑定/读档后外观稳定；模型变化按实际影响复查持物和楼梯，并保存实际截图。

三种固定衣物候选已接入独立玩法/楼梯场景，继承原 Warm / Stair 的同一组行为断言，未放松接触、物品唯一性或恢复约束。下一步依据实际俯瞰观感调整仍偏中世纪的服装，按模型变化复查相关姿态，并继续可变设施锚点/区域/机构的小实验。场景打开助手目前只拦截活动场景的脏状态；后续需检查所有已加载场景再 Single 打开，避免叠加编辑场景时遗漏未保存内容。本轮取证仅有单个干净场景，不据此声明多场景编辑保护已验证。

用户补充：若运行时捏人可行且成本合理，可作为后续附带功能；代价太大则放弃。因此它是可选范围，**不是当前美术样板或活动目标的完成条件**。若采用 UMA 且小样通过，先做“随机一个 + 少量体型滑杆 + 发型/服装/颜色选择”，限制在验证过的组合与比例内。先支持新建居民时编辑；不提前实现工作中改变身高、精细面部雕刻或每件服装的任意形变。是否推进取决于小样中服装穿模、动作接触、生成性能与维护成本的实际结果。

即使最终不接入 UMA，也可用少量经过验证的预制组合与独立衣料/头发颜色实现游戏内外观选择；运行时只选用已有资产，不需要现场启动 Blender 或 AI 生成模型。这比连续捏脸和任意体型更适合作为附带功能，但当前仍只有三个固定 Prefab，选择 UI、组合约束与外观配方存档尚未实现。优先顺序为：固定人物观感成立 → 有限组合/颜色及保存 → 再评估体型参数。随机按钮与手动选择应使用同一份外观配方，避免以后维护两套系统。

1. 在独立 intake / 实验范围固定候选版本、许可、依赖与自动化入口。先验证一个穿完整衣物的正常人物；没有实际模型预览时，不把官网宣传图或 AI 概念图计为完成。
2. 用同一流程制作三个比例与发型有区别、配色协调的居民。先检查默认俯瞰、缩放后的辨认，再看近景肩袖、手足与服装；视觉不合格就先调整基础资产，不继续搭建捏脸 UI 或扩大随机组合数量。
3. 接入当前五类共享动作和持物表现，验证走动、弯肘、蹲下、携桶上下楼。按实际改动选择 Warm / Stair 定向验证；旧模型的 12/12 和 4/4 不能作为新模型通过的证据。
4. 小样观感与动作均成立后，再测配方保存/恢复、有限组合随机、生成耗时、Renderer/材质数量及释放。正式接入之前记录实际依赖影响；核心玩法和 SSFramework 不直接依赖人物制作包。

后续先比较成熟生成器输出与现成服装候选的实际观感，再决定人物生产工具。若完整生成器的导入、服装维护或性能成本不合适，可继续使用编辑期生成的固定人物与有限预制组合；不为可选游戏内捏人拖住当前美术样板。

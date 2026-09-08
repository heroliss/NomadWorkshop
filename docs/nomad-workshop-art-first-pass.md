# 游牧工坊：首版可玩美术样板

> 更新于 2026-09-08，当前仍在 B/S0 收口：用户接受以 J 起步工坊继续增加温暖感，授权目标模式持续推进。K 未生成不再阻塞制作；实机样板仍需用户体验评价。技术可行性与分阶段验收见[可扩建甲板方案](nomad-workshop-expandable-decks-design.md)，近期顺序统一由 Foundation §9 维护。

## 当前实施：温暖工坊与人物动作

### 模型、源文件和材质在哪（2026-09-08）

以下路径均相对项目根 `D:/SSFramework`。`Assets/` 内是 Unity 导入并纳入版本控制的资产；`ArtPipelineOutput/` 在同一项目目录下，但属于 Git 忽略的本地制作产物，换机器时需按对应脚本重新生成。

| 内容 | 当前可查看的文件 |
|---|---|
| 车体、甲板、驾驶舱、棚架的 Blender 源场景 | [VehicleForms/v09/NW5_Vehicle.blend](../ArtPipelineOutput/VehicleForms/v09/NW5_Vehicle.blend) |
| 当前 NW8 水箱与饮水站的未合并源组件 | [WaterFacilities/v02/NW8_WaterFacilities_Source.blend](../ArtPipelineOutput/WaterFacilities/v02/NW8_WaterFacilities_Source.blend) |
| 当前 NW9 小厨房的未合并源组件 | [Kitchen/v02/NW9_Kitchen_Source.blend](../ArtPipelineOutput/Kitchen/v02/NW9_Kitchen_Source.blend) |
| 当前 NW11 手提水罐的未合并源组件 | [WaterCan/v03/NW11_WaterCan_Source.blend](../ArtPipelineOutput/WaterCan/v03/NW11_WaterCan_Source.blend)；接线与验收见[水罐与姿态](nomad-workshop-watercan-art-and-posture.md) |
| 当前 NW14 旱厕的未合并源组件 | [Sanitation/v04/NW14_Toilet_Source.blend](../ArtPipelineOutput/Sanitation/v04/NW14_Toilet_Source.blend)；接线与验收见[旱厕与生活区镜头](nomad-workshop-sanitation-art.md) |
| 多数设施与旧环境的 Blender 源场景 | [Nomad_FirstPass.blend](../ArtPipelineOutput/NomadWarmPass/facility-feedback-v0.6.1/Nomad_FirstPass.blend) |
| 当前砂地、层状岩石与碎石环境 | [NW6_Desert.blend](../ArtPipelineOutput/DesertEnvironment/v02/NW6_Desert.blend) |
| 三种人物组合源场景 | [resident-lineup.blend](../ArtPipelineOutput/CharacterIntake/ResidentVariants/current/resident-lineup.blend) |
| 当前参考车辆的 NW10 工装派生源模型 | [NW10_Workwear_Source.blend](../ArtPipelineOutput/Workwear/v10/NW10_Workwear_Source.blend)；制作与验证见[居民工装](nomad-workshop-workwear.md) |
| Unity 模型、组装、材质和贴图 | [Models](../Assets/Game/NomadWorkshop/ArtFirstPass/Models)、[Prefabs](../Assets/Game/NomadWorkshop/ArtFirstPass/Prefabs)、[Materials](../Assets/Game/NomadWorkshop/ArtFirstPass/Materials)、[Textures](../Assets/Game/NomadWorkshop/ArtFirstPass/Textures) |
| 可复现制作脚本 | [Tools/ArtPipeline/Blender](../Tools/ArtPipeline/Blender)；车辆入口 `blender_nomad_vehicle_forms.py` |

车辆与多数设施由 Blender Python 建模，包含编辑截面、斜面、倒角和 UV；早期资产仍有较多基本体组合，当前 Unity 视觉细节也可作为不改变玩法空间签名的替换层。参考车辆当前使用 Blender 制作的 `ArtFirstPass/Prefabs/NW9_Kitchen.prefab`，带独立柜门、水槽、灶具和真实备餐面；原 Unity/ProBuilder 的 `NW1_Kitchen.prefab` 保留供旧场景使用。人物来自 Quaternius 基础人物和 Modular Outfits，做组合、内袖处理、调色与共享 Humanoid 接线，未声称从零雕刻。

NW5 车体、甲板和驾驶舱分别把 Blender 程序材质烘焙为 2048² 的 `Color`、切线 `Normal`、`Surface` 三张 PNG；Surface 的 R 为金属度，A 为光滑度，导入后接到 URP Lit。旧漆、接缝积尘和磨损由节点与 UV 分布生成，不是 AI 生图或人工精绘贴图。早期 NW1 设施通过 `NomadWorkshopSurfacePipeline` 在 Unity 生成 512² 的金属/织物/土地循环噪声、法线与表面图，再乘材质主色；棚架复用其中的金属图。Unity 材质、灯光与 Blender 预览分别调校，不能把离线节点直接当成游戏内效果。

当前 [NW6 沙漠](nomad-workshop-desert-first-pass.md)由 `blender_nomad_desert.py` 直接生成砂地/砂岩的两组三张 2048² 平铺图，以及一张带透明度的压痕图。使用周期噪声、离散颗粒和层理函数；其几何与贴图一同导出，Unity 按米制间距和真实旅程距离滚动。

### 当前进度

2026-09-08：[NW11 手提水罐与容器绑定](nomad-workshop-watercan-art-and-posture.md)已接入参考车辆和三款工装楼梯，带圆角收肩、曲线把手、独立封盖、真实罐口和烘焙材质。握点/罐口位置及避让区域数量与旧罐不同，共享搬水流程和物品身份保留；EditMode 4/4、PlayMode 52/52，包含旧罐体回归。实际拿放、携行、倾倒和三款上楼画面已检查；地面深跪姿、近景棚架遮挡和均匀水流仍待改善，不以接触检查通过代替整体美术验收。

2026-09-08：B/S0 参考车辆视觉小步在驾驶台和画架 Prefab 下增加独立 `NW16_VisualDetail` 子件（仪表、显示屏、操纵杆、画框和调色盘），并将场景主光调整为克制暖色。原工作位、占地、资产入口和导航未改变；EditMode 2/2 通过，实机截图为 `Screenshots/nomad-s0-warm-detail-final-20260908.png`。这一步改善用途辨识度，但整车轮廓、设施一致性和用户审美仍待试玩；实际启程暂停画面见 `Screenshots/nomad-s0-journey-review-20260908.png`。

2026-09-08：同一参考车辆继续增加独立 `NW17_VisualDetail` 子件：甲板四边封边、四角橙色识别片、内侧青绿色细线和棚下暖色灯罩。只复用现有材质，不含 Collider，不改变车辆空间签名、导航或工作位；`NomadS0VehicleDetailTests` EditMode 1/1、`NomadWarmWorkshopPlayModeTests` 12/12 通过。实际车辆画面见 `Screenshots/nomad-s0-vehicle-edge-20260908.png`，启程中暂停画面见 `Screenshots/nomad-s0-journey-review-nw17-20260908.png`。封边和暖光的存在性已有取证，人物 / 设施对比与整体温暖感仍需用户试玩评价。

2026-09-08：[NW9 小厨房](nomad-workshop-kitchen-art.md)已接入参考车辆，木质备餐区、灰米壳体和旧青绿柜门增加生活层次，起步操作面朝向甲板中央。台面/站位的空间签名保持原厨房不变，旧检查点的朝向仍正确恢复；网格/空间 EditMode 2/2，最新参考车辆 PlayMode 23/23，实际俯瞰、近景和携杯画面已查看。杯具精细握把/伸手过渡、其他设施及服装仍待完善。

2026-09-08：[NW8 水设施](nomad-workshop-water-facilities-art.md)已接入下述参考车辆入口，更新曲面储罐、独立检修舱、实际开孔的补水面和分件动作；共用空间机制让托盘支撑点与新模型同步。当前参考车辆 PlayMode 22/22，包含实际物品底面和新版倒水落点，导入/网格 EditMode 3/3。源模型和配方已保留，其他生活设施仍需按 J 逐步改善。旧 NW7 空间场景保持独立，历史阶段数量见下文；本轮完整证据和实机图片见水设施文档。

美术试玩入口为 `Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity`：NW5 的结构边框、错缝钢板、折面车首、烘焙旧漆与[局部棚架](nomad-workshop-canopy-cutaway.md)已接入。默认剖开屋顶，左下按钮或 H 查看外观，建造时自动剖开。[首版玩家 HUD](nomad-workshop-hud-first-pass.md)提供居民选择、真实资源、暂停和倍速；[NW6 沙漠](nomad-workshop-desert-first-pass.md)提供砂地、层状岩石、压痕和距离驱动表现。[模型空间生成 P1](nomad-workshop-facility-space-baking.md)已验证站位、区域、组容量与存档兼容，其阶段基线为变体 14/14、参考车体 20/20、Foundation 109/109；最新水设施结果以上文为准。其余设施、服装仍有明显风格差距，[折返梯](nomad-workshop-reference-form-study.md)仍为离线尺寸实验。离线渲染、Unity 实机和动作验收分别记录。

原人物对比入口为 `Assets/Game/NomadWorkshop/Scenes/ResidentCrewSample.unity`：短发、束发、灰发胡须三个固定 Humanoid 外观共用已有动作，保持三居民生活与旅程玩法。三人场景检查 13/13、三款人物分别通过楼梯 4/4；外观通过稳定居民 id 选择，读档或更换玩法种子不重新随机。详细资产来源、领口/内袖处理、材质导入时序和验收记录见[人物制作](nomad-workshop-character-authoring.md)。工装已接入参考车辆；旧 Warm / NW2 场景作为历史对比保留。

暖化优先来自旧象牙白/青绿设备、陶土色织物、少量木作和暖色工作灯；甲板继续保持低噪声的中明度灰褐色，不把全局曝光或黄色滤镜当作温暖感。先复用当前 10.8 × 8.4 m 布局，设施与角色保持米制尺寸。已获授权制作，不再要求用户重新选图。

用户随后明确要求减少替换模型后会重做的工作，并否定方块衣物/附件的人物观感，提出参数化人物与后续随机外观。停止精修这批附件；当前已完成[三种现成服装人物小样](nomad-workshop-character-authoring.md)的定向动作验证。2026-09-07 的 [NW4 模型替换与交互绑定](nomad-workshop-model-interaction-bindings.md)已让水设施的接口位置、可选点数量、局部轴和行程经共同配置执行，独立场景为 `FacilityBindingSample`；空间生成与人物开门同步尚未实现。接下来依据实际观感调整服装、HUD 与遮挡。UMA 3 / MPFB 作为后续制作工具候选，运行时捏人为可选范围。现有 Prefab/Humanoid 入口可以复用，道具尺寸仍未通用化，不声称任意 Rodin 模型可直接替换或自动拥有捏人能力。阶段验收见[检查点](nomad-workshop-stage-checkpoint-2026-09.md)。

此前单款 NW2 基线为 `Scenes/ResidentAppearanceSample.unity` 和 `Scenes/ResidentStairSample.unity`，用 Quaternius 正常头部与真实蒙皮服装替换独立候选场景的方块附件。修正被外衫覆盖的内袖肩部穿插后，该模型通过工坊 12/12、楼梯 4/4，复用原动作/行为断言；[拿放桶近景](../Screenshots/nomad-character-ground-placement-02.png)与[持桶上楼](../Screenshots/nomad-character-stair-01.png)已实际查看。旧场景三人共用一个男款外观，当前三种外观以文首 NW3 入口为准；两轮测试分开记录。测试通过不代表所有衣物姿态无穿插。UMA 目前只有源包/Blender 数据检查，尚未在 Unity 中验证；游戏内捏人按用户要求保留为后续可选功能。详见人物小样记录。

人物使用已验证的共享 Humanoid 与五个语义动作，先接线实际三居民和携物。平地/楼梯行走使用下肢移动动作，上肢保持持物姿态，再由手部 IK 对齐物品把手；物品挂接和资源交接始终由业务状态决定，Animation Event 不直接结算库存。直梯需要双手，不默认允许手提水桶攀爬；先用空手/背负小物验证，水桶上下层优先楼梯，较大物资后续通过吊运。不同身高和衣服不能只换任意网格，需复用有效 Avatar、蒙皮权重及手脚锚点。

第一条动作验收是走到水箱 → 接近/拿取 → 手持携行 → 放到饮水设施 → 松手，以及任务取消、暂停、倍率变化、读档后的同一物品身份。随后独立场景验证持桶上下短楼梯和空手攀梯：导航/身体拥有脚底位移，动画跟随；IK 修正末端接触，不替代攀爬动作或通行规则。Unity 内置 [Animator IK](https://docs.unity3d.com/kr/6000.0/ScriptReference/Animator.SetIKPosition.html) 可修正手脚目标；[Controller Layer 的 IK Pass](https://docs.unity3d.com/cn/6000.0/ScriptReference/Animations.AnimatorControllerLayer.html)需与实际 Animator 宿主接线，避免仅有目标 Transform 却没有 IK 回调。

已知难点：手柄与手掌姿态、桶/膝盖/门洞碰撞、上下梯出口净空、脚滑与动作速度匹配、转身及两人让路、背负物转换、半途保存/取消的资源守恒、切层不停止其他楼层动作、缩放后人物辨认、FBX 坐标/Pivot/材质回读和替换资源释放。先验证这些真实边界，不为每个道具与行走状态都单独录制整套动画。

本轮实施计划：

- [x] 读取现有角色、五动作、WorldView 携物锚点和 Blender 草稿，确认 Unity 处于非 Play、无编译错误。
- [x] 修复 Blender 草稿 FBX 回读问题，生成四履带车架、设施、驿站和荒漠九类资产；核对源/导出/Unity 尺寸、面数及三轴锚点。保留为尺度/交互验证用占位资产。
- [x] 建立独立可玩美术样板，接入六类实际设施、三人 Humanoid 工作服、平地携物、行驶履带和扬尘。
- [x] 接线设施工作反馈，校准水罐掌心/肘部，连续验证抬桶倾倒与持桶楼梯。
- [x] 收口地面拾放的基本接触验收；证据限于当时的角色与水罐。
- [x] 制作三个正常人物固定小样，完成共享动作、身份保持和各自持桶楼梯的定向验证，交付实际截图与独立试玩场景；服装风格仍需评价与调整。
- [x] 完成模型替换 P1 空间与人物同步；P0 水设施绑定、局部顶棚显示和首版玩家 HUD 已验证。
- [x] 检查参考车辆俯瞰、暂停、设施作业和检查点恢复；当前截图与定向回归已归档，完整 1× 旅程试玩仍由 Foundation §9 维护。
- [x] 用独立小实验验证局部扩建和持桶楼梯接缝；结果分别记录在 NW12–NW15，未宣称正式多层完成。

### 首个实机接线检查（2026-09-06，继续制作中）

独立入口为 `Assets/Game/NomadWorkshop/Scenes/NomadWarmWorkshop.unity`。菜单“Assets/SSFramework/游牧工坊/首版美术/导入并打开温暖工坊样板”先核对 Blender manifest/哈希，再生成独立材质、Prefab、六类设施定义副本和三人工作服；基础场景与玩法定义仍保留。Blender 中间产物位于 `ArtPipelineOutput/NomadWarmPass/current`，游戏使用 `ArtFirstPass` 中经 Unity 导入的资产。

源资产检查已经捕获并修复：FBX 带平移父节点的轴转换烘焙偏移、薄圆片倒角导致的零面积三角形、前后方向翻转、设施工厂重置导入根旋转、开放地形法线向下，以及替换厕所外壳后遗漏独立污物桶。现在除了尺寸与面数，还核对三个方向锚点、单位 Prefab 外层根和地形朝上法线。

`NomadWarmWorkshopPlayModeTests` 的真实场景检查已通过 4/4，并在侧让修正、光照和履带舱调整后再次通过（job `cff89a04d601`，精确证据 `Logs/AIValidation/nomad-warm-art/playmode-09/evidence.json`）：三名穿工作服的有效 Humanoid 与六设施；手部接触水罐把手、暂停冻结身体/物品/动作并恢复移动；持罐时恢复检查点后保留唯一物品并释放旧手部约束；实际居民到驾驶位后带动履带、路旁地景和真实扬尘，暂停冻结，恢复检查点重建原姿态并清除旧扬尘，取消旅程停止转动。

动作验证先后发现固定握点超出骨架可达范围、源人物朝向与游戏前进方向相反、手指未握持，以及停止状态的粒子系统虽能通过“暂停后不变”却根本没有发射。现按肩高/臂长标定握点，保留源骨架并用外层统一朝向，在独立 Controller 添加右手指层，粒子由 Play/Pause 跟随模拟状态；新增断言要求实际行驶必须产生粒子。近景 `Screenshots/nomad-carry-close-facing-grip.png` 与暖色补光后 `Screenshots/nomad-carry-warm-fill.png` 已实际查看。自动测试覆盖腕部位置与所有权，不代表手掌旋转、拾放过渡和所有物品形状已完成美术验收。

第一张可玩接线截图为 `Screenshots/nomad-warm-playable-01.png`；第二遍材质/生活细节截图为 `Screenshots/nomad-warm-surfaces-02.png`，均已实际查看。该轮 Blender 配方 v0.5.1 九类资产共 64,933 三角形；四组履带保留 12 根轮轴和 168 块等间距履带片，由绝对旅程距离驱动，不依赖动画累计时间，也不移动逻辑甲板。履带舱适度外移以保持俯瞰辨认，外观总宽约 13.01 m，可建造甲板仍是 10.8 × 8.4 m；扬尘锚点从实际履带舱派生。路旁岩石循环和地面纹理移动仍是首版地景表现，返程暂为直线倒行，尚未提供真实地块流送或车辆掉头。

导入检查增加相对原点的 Bounds 最小/最大坐标，防止只比较尺寸却漏掉整体偏移，并修正轮轴诊断名称筛选，实际记录 12 根轴。照明实验确认 Trilight 会重新计算并覆盖手动 ambientProbe；因此调整实际渐变环境色，并降低主光的橙色偏染，保留原始光照日志和前后截图。实验菜单已收为只读“记录当前环境光”。

首批设施机械反馈已接线，见下文；停靠视觉、直梯实验及完整视觉交付尚未完成，人物衣物、岩石和 HUD 仍需继续实机评价。持桶楼梯已进入独立实机验证，见下文。真实动作观察菜单属于本游戏 Editor 工具，显式引用 R3/ObservableCollections 以消费同一只读模型，没有向框架反向引入游戏类型。

基础玩法整组回归随后发现三人交叉通行停滞，首次结果 99/100；补充必要行走者的临时侧让后，整组复验 **100/100**（`afd1bcd72ce9`，336.48 s），详见[三居民移动记录](nomad-workshop-three-residents.md)。该结果包含真实原生往返与身体/水量守卫，仍不替代当前美术样板的默认参数试玩和审美评价。

当前实机进度截图为 [nomad-warm-playable-progress.png](../Screenshots/nomad-warm-playable-progress.png)，已实际查看：三居民、六设施和履带车在真实行驶 15.2 m 后暂停，存在实际扬尘粒子。图片仍明显偏原型，工作服形体、底盘轮廓、设施生活细节和 UI 尚未达到参考图品质。试玩打开 `Scenes/NomadWarmWorkshop.unity` 后 Play，通过右上角旅程/居民/建造入口操作；本次取证结束已退出 Play，场景保持可直接重新开始的状态。持桶楼梯的后续证据见下文；拾放、直梯与局部切层仍待推进。

### 设施工作与携物接触（2026-09-06）

水箱装水阀门/软管、饮水站及高位水箱补水盖、维修上滑盖已按精确设施租约与业务进度接线，同一水罐绕握点倾倒。故障改为局部警示灯，保留青绿/象牙白/金属的材料区别；罐口从把手立柱移到前侧，补出独立注水颈。配方 v0.6.1 的九类 Blender 资产当前共 **68,469 三角形**，九个工作/状态锚点经 Unity 回读验证。

设施阶段的美术场景测试 **8/8**（`92fb9b27f47a`，21.52 s），包括双同类设施隔离、实际管线与身体/把手净空、低位/高位倒水接触、暂停、读档，以及取消外勤后完成已带回的一批水。已实际查看 [装水](../Screenshots/nomad-facility-fill-close.png)、[倒水](../Screenshots/nomad-facility-pour-close.png) 和[维修](../Screenshots/nomad-facility-repair-close.png)近景；这些旧照片只验证当时的腕部位置接线。设计、失败证据、报告路径和回归范围见[设施工作反馈](nomad-workshop-facility-feedback.md)。

后续[人物接触迭代](nomad-workshop-resident-contact.md)增加实际掌心表面/朝向校准、肘部净空与连续倾倒检查；装水/倒水使用站姿，水罐移出大腿范围。地面拾放现已接入侧面工作位、真实来源/预留落点、蹲身支撑及所有权交接，交水后仍实际携罐走到停放区再松手。当前美术场景 **12/12**（`c609024b6120`，47.48 s，`handling-warm-03/evidence.json`），包括 1× 连续拾放、双腿净空与下放中恢复。先前未含双腿检查的 12/12 在 `handling-warm-01/`，掌心阶段的 10/10 在 `contact-playmode-12/`；失败候选和姿态限制见人物接触记录。完整人物美术、维修工具握持与其他道具的拾放还未完成。

### 持桶楼梯接缝

独立场景 `Scenes/StairCarrySpike.unity` 复用工作服 Humanoid、水罐几何与原生移动 Adapter。两层相隔 3.2 m，18 级实体台阶；上下目标刻意使用相同 XZ。导航按人加水罐的净空规划，身体按实际躯干尺寸碰撞，脚部 IK 区分支撑和摆动，表现骨盆补偿跨踏步的高度差。最终楼梯夹具 **4/4**（`9ba48a9e5bea`，67.15 s），包含整段路径的支撑脚目标残差与水罐体积净空检查，以及暂停、取消、检查点高度和同一物品身份。

入口为 Unity 菜单“Assets/SSFramework/游牧工坊/首版美术/创建或打开持桶楼梯实验”，场景内可上楼、下楼、暂停、停止、记录/恢复位置。此实验尚不接正式库存交接、多层施工或正式存档升级；后续先验证直梯的空手/背负约束、入口出口互斥，再做局部楼板和切层显示。完整实现与失败证据见[携物跨层实验](nomad-workshop-stair-traversal-spike.md)。

共享水罐几何和移动查询的基础回归还发现[自动返程误清取水请求](nomad-workshop-stop-resource-ownership.md)。修正后 Foundation **102/102**（`bac603ea947d`，323.28 s），同版美术场景 **4/4**（`0c545797153e`，6.42 s）；证据分别为 `foundation-regression-04/evidence.json` 与 `playmode-10/evidence.json`，位于 `Logs/AIValidation/nomad-warm-art/`。这些是当前代码的行为证据，审美质量仍需实机评价。

旧导航实验另暴露新建 Agent 默认类型与 Surface 不一致，现由工厂显式传入类型，最终 **2/2**（`65754021d28a`）。当前[楼梯近景](../Screenshots/nomad-stair-carry-close.png)已实际查看；它验证携物接触和跨层移动，人物形体、负重节奏和拾放过渡仍是后续美术工作。

## 第四轮：融合风格与成长尺度

用户反馈：G 甲板太亮、太干净，长时间看不舒适；I 的机械与旧材质方向较好，但过脏偏暗、物件杂乱。目标是中明度灰褐色甲板，少量接缝积灰与行走磨损，克制的旧漆；设施保持象牙白/青绿/橄榄的大色块、可辨功能与少量橙色操作件。人物与可交互设施应从地板中分离，避免通过全局曝光补亮或密集杂物增加细节。

本轮将此前 H 中“清楚结构、低纹理噪声”的特点融入两张新图，不把未产出的 H 称为已补齐。J 展示约四分之一面积的三人起步工坊；K 展示同风格的大平台、少量不对称外扩、一块局部二层甲板和可见梯子。屋顶/近墙用剖开表达内部，允许实际围护而不遮住主要玩法。两图是成长状态示意，不能由生图推导实际承载与布局尺寸。

两次含参考图的内置请求均返回网络错误。用户授权纯文字重试后，[J · 起步工坊](art-references/nomad-first-pass/J-starter-balanced.png)成功生成、实际查看并按原字节归档；K 的首个纯文字请求及一次单独补试均返回网络错误，没有输出。本轮只交付 J，不把旧图或提示词冒充 K。[首次生成说明](nomad-workshop-art-round4-prompts.json)、[纯文字重试原文](nomad-workshop-art-round4-text-retry-prompts.json)与[中文版可复制说明](nomad-workshop-art-round4-manual.md)已归档。纯文字成功与失败都已出现，不能确认故障只发生在参考图接口；未改用付费 API 或外部 3D 服务。

J 可见三名居民、三铺位、独立饮水/维修/厨房模块、连续灰褐色甲板、小型外挑区与后缘窄顶棚。相比 G 降低了甲板明度，相比 I 减少零碎杂物，操作件和设施外形更易区分。用户已确认增加温暖感并开始实现。图像没有提供可测量的尺度和机械支撑验证，不声称精确达到 10 × 7 m 或生产资产标准。

## 第三轮空间约束与结果（第四轮已扩展为起步/成长两种状态）

参考图需要证明“可以在车上经营和建设”，因此固定以下条件：

- 主体为宽大的单层连续平板甲板。车架与轮组/履带在甲板下方，驾驶台作为小型边缘模块，不占据中央。
- 相机采用约 70° 向下的俯瞰视角、近似正交投影，完整展示车辆和绝大多数可用地面；不再以低角度车辆肖像判断经营画面。
- 至少一半甲板保留清楚可见、连续的空闲地面，宽通道贯通；设施以可独立排布的小模块沿边示意，不把整个平台做成固定建筑。
- 玩家活动区域不被屋顶、大棚、高墙、巨大引擎或景物遮挡；睡眠/卫生角以低墙和剖开顶部表达。
- 人物相对平台足够小，能直观看出多人活动和扩建余地。第三轮用六名居民表达概念尺度，不代表正式场景人数已增加。
- 场景只保留低矮的沙地、岩石和灌木作为环境参照；地面材质与阴影应帮助分辨居民和设施，不用纹理噪声填满可玩区域。

第三轮生成说明使用约 20 × 14 m 的平台帮助控制比例；这是参考图的视觉尺度，实际游戏尺寸仍需结合布局、导航与镜头验证后确定。

| 第三轮画面风格 | 重点 |
|---|---|
| G · 温暖柔和 | 圆润倒角、哑光手作质感、奶油白/鼠尾草绿/陶土色，减少细碎磨损 |
| H · 清晰利落的风格化工业 | 清楚的结构面、低噪声蓝灰地板、青绿/象牙白设施、克制的琥珀色操作件 |
| I · 粗粝厚重的废土工业 | 较写实的金属与帆布、沙卡其/橄榄色、集中在使用部位的磨损，保证阴影内可读 |

三个请求都以第二轮 F 为宽体底盘参考，明确改变原图低镜头与高棚架，保留相同空间约束。已实际检查并按原字节归档 [G · 温暖柔和](art-references/nomad-first-pass/G-flatbed-warm.png) 和 [I · 粗粝厚重](art-references/nomad-first-pass/I-flatbed-weathered.png)；H 首次请求及一次单独重试均返回生成服务网络错误，尚无图片，不能计为交付。完整请求与结果记录见 [第三轮生成说明](nomad-workshop-art-round3-prompts.json)。

两张成功图片都展示了高位俯瞰的宽大连续甲板、边缘设施、多个居民和中央空闲区域，空间方向比第二轮 D/E 更符合用户要求。G 的浅色甲板、柔和体块和生活物件较亲切；I 的暗色钢板与集中磨损更有机械重量，但人物与地面的明暗区分较弱，细碎纹理更多。建议优先讨论 G 的温暖色组和克制细节，同时让地板与人物、交互设施保持足够差异。生成图仍是空间与风格参照，设施只沿边摆放并非最终布局规则；下一步必须用实际占地和居民通行检查布局密度。

## 第二轮整体方向探索

首轮 A/B 保持了相同构图和布局，适合比较局部材质与结构，未满足当前探索大方向的目的。本轮生成三张独立新图，不把 A/B 作为图像输入。只固定“三人生活、旅行与维护移动工坊”的题材，让轮廓、尺寸、镜头与渲染风格共同变化。

| 新方案 | 形体与比例 | 镜头与风格 |
|---|---|---|
| 1 · 紧凑生活篷车（资产 ID D） | 短而高的四轮篷车，侧面展开小工作露台，约 6 × 3 m 的概念尺度 | 较高的等距视角；圆润造型、哑光大色块、奶油白/杏橙/鼠尾草绿 |
| 2 · 模块化陆行车队（资产 ID E） | 牵引车加两节独立工坊/生活拖车，约 17 m 长、3 m 宽 | 低角度侧前方横向构图；精确的工业结构、青绿/赭黄、克制的半写实材质 |
| 3 · 宽体履带工坊（资产 ID F） | 四组履带承载接近方形的大型工作庭院，约 12 × 11 m | 较低的前方三分之四视角；厚重雕塑式体块、手绘材质感、象牙白/灰橄榄/陶土红、峡谷光影 |

第二轮比较“哪种移动工坊值得进一步做”。用户反馈 D/E 的空间不足，只有 F 的宽大平台大致符合多人走动和建筑排布，因此第三轮固定宽大连续甲板与俯瞰视角。概念尺度和车厢结构不是新玩法契约；仍需评估甲板面积、居民动线、连接方式与当前业务边界，不能仅按图片外观直接改存档和导航规则。

三张新图均已由内置生图成功生成、实际查看并按原字节保存：[1 · 紧凑生活篷车](art-references/nomad-first-pass/D-compact-camper.png)、[2 · 模块化陆行车队](art-references/nomad-first-pass/E-modular-land-train.png)、[3 · 宽体履带工坊](art-references/nomad-first-pass/F-crawler-courtyard.png)。[本轮完整说明](nomad-workshop-art-round2-prompts.json)保留生成时的主题、差异和无输入图约束。

实际画面中，1 的短车身、圆润大色块、亲近的人物比例和展开工作区形成轻巧的生活感；2 以低角度盐滩横向全景展示独立牵引车与两个车厢，结构和材质更接近半写实；3 以峡谷内的宽大履带工作庭院表达重量和基地空间。三个方向已在主体轮廓、尺度、镜头和细节密度上明显区分。屋顶遮挡、车厢连通、工作平台的实际承重以及居民碰撞仍未通过这些概念图验证。

## 方向与制作选择

主角是沙漠里有人生活的移动工坊。沙金环境、奶油色车壳与帆布、青绿设备、少量橙色操作件、深色机械骨架构成统一色组。远景以大轮廓和明暗层次为主；近景保留倒角、装配结构和可辨认的用途。磨损集中在结构和使用区域，不靠全表面噪点增加细节。

本轮以 Blender `bpy` 制作车辆与硬表面设施，保存源文件、分件、UV、材质槽、导出和回读证据。Unity 负责实际材质、灯光、阴影、环境、VFX、UI 和逻辑驱动的动画。Unity 参数化几何继续承担需要随玩法改变的辅助图形；高频静态美术不在每次运行时重新建模。

人物先复用项目已验证的 Humanoid 和五个共享动作，增加工坊服装色组和骨骼配件。换皮需要共享骨架/骨骼映射和权重，不能把任意网格换上去就称为兼容。Root Motion 不拥有居民位置；物理脚底仍由原有运动层决定。机械门轴、轮轴和运行反馈读取业务投影，暂停和读档后仍以真实状态为准。

Rodin 保留为复杂形体的候选；已有厨房实验显示，漂亮底模仍可能需要处理融合门板、Pivot 和贴图归档。它不预先获得“效果必定最好”的结论。本机实测为 RTX 4060 Laptop、8188 MiB 显存。本轮不安装本地生成模型：截至本次核查，[Hunyuan3D 2.1](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1) 官方完整流程约需 29 GB，[TRELLIS.2](https://github.com/microsoft/TRELLIS.2) 官方要求至少 24 GB。轻量/卸载方案的质量、速度和清理成本尚未在本机实测。

## 可检查的工作计划

- [x] 核对现有 Blender、URP、角色与素材链；检查当前 Game View，并确认艺术方向。
- [x] 生成第二轮三张差异明显的整体参考图，实际检查并并列交付。
- [x] 第三轮固定宽大单层平板甲板、俯瞰镜头、多人动线和建筑排布空间；第四轮增加小甲板起步与局部上层方向。
- [x] 生成、实际检查并归档第三轮 G/I 两种风格；记录 H 首次及重试均失败，先交付现有图片比较。
- [x] 根据 G/I 反馈整理融合明度、材质和辨识度；形成扩建/多层/围护可行性方案。
- [x] 第四轮 J 已通过纯文字内置生图成功生成、实际检查并归档；K 含参考图与两次纯文字均网络失败，记录待补。
- [x] 用户认可 J 并要求增加温暖感，授权目标模式继续；K 未产出不再阻塞制作。
- [ ] 根据选定参考，在 Unity 复现代表性镜头，确认实际可达到的造型、材质和光照效果后扩展成组资产。
- [ ] 生成一组相互一致、部件可编辑的车辆和设施模型，回读核对尺寸、轴向、材质与三角形。
- [ ] 在独立美术样板场景接入实际 Foundation 玩法，补齐沙漠、驿站与行驶状态反馈。
- [ ] 接入共享骨骼角色和身份差异；统一 HUD 样式，保留实际玩家控制入口。
- [ ] 检查固定实机镜头、近景、移动/停靠/暂停，运行改动对应的契约验证，整理试玩入口和局限。

## 边界与验收

以下保留首轮规划：固定同一个俯视三分之四镜头、三名居民、开放工作甲板、六轮车架、同一组生活设施和沙漠驿站，只比较三个子方向。A/B 已生成；旧 C 不再按原计划补图，当前以第二轮三个新方案比较整体方向。

| 方案 | 主体造型与材质 | 希望判断的差异 |
|---|---|---|
| A · 移动之家 | 奶油色圆润车壳、青绿设施、克制的生活物件 | 舒适感和玩法可读性是否足够，同时不显得像玩具 |
| B · 荒野工程车 | 更硬朗的模块化结构、明确的检修盖与轮组 | 机械可信度与结构层次是否更符合工坊主题 |
| C · 游牧拼装工坊 | 统一色组下的非对称拼装、木作、帆布与小旗 | 人情味、个性和材质变化是否值得额外制作成本 |

选择时先看车辆轮廓、环境氛围、材质细节密度和人物比例；不要求喜欢某一图里的每个部件。可以指定主体方案，再从另一张借用一个明确元素。图片中的设施布局与 UI 只表达意图，进入 Unity 后仍需对齐实际碰撞、通路、工作位和屏幕尺寸。

美术样板场景和定义使用独立资产，保留原 Foundation 场景作为工程基线。改变可见模型不改变设施占地、居民碰撞、物质账本、工作位和存档语义；新视觉部件不带 Collider，不进入 NavMesh 烘焙。新的模型只有在实机画面里检查过才可标记“已检查”；审美是否达到项目要求由用户试玩判断。

这轮验收关注：车辆一眼可认、居民与设施可区分、行驶/停止有明确视觉差异、驿站任务可读、近看无缺材质或明显姿态错误、暂停后动画冻结、UI 不阻断原本可用的建造与旅程操作。性能先记录本机观察，不宣称目标平台优化完成。

开始前 Unity 已处于 Play。已把该现场的业务检查点另存到 `Logs/AIValidation/nomad-art-v1-20260905/pre-art-runtime-checkpoint.json`，再退出 Play；不覆盖玩家手动存档。

## 交付记录

已开始编写 Blender 成组模型生成草稿，但首次 FBX 回读未通过 Bounds 一致性检查，尚未导入 Unity 或被视为可用资产。用户提出先比较整体参考图后，暂停该草稿的修正与制作；后续依据选定参考调整，而不让已有草稿反过来限制艺术方向。

参考图属于概念目标，不是 Unity 实机截图，也不代表对应资产已经完成。首版样板不自动替代已验证的工程场景，也不代表成组美术已获人工批准。

本轮三份完整生图说明已归档到 [art-concept-prompts.json](nomad-workshop-art-concept-prompts.json)。首批三个内置生图请求均返回生成服务网络错误，随后单独重试 A 也返回同一错误，未产出任何图片。当前阻塞是生图服务连接；模型制作继续暂停，参考图尚未供用户选择。没有改用付费 API、下载本地模型或提交 Rodin 任务。

2026-09-06 应用户要求再次单独重试 A，仍返回同一网络错误。已补齐 [中文版手动生图说明](nomad-workshop-art-reference-manual.md)，可在 ChatGPT 网页依次生成三个参考方案。当前没有证据表明是额度不足；不把连接失败归因于未配置 API Key。

同日用户在网页成功生成并提供了 [A · 移动之家原图](art-references/nomad-first-pass/A-mobile-home-user.png)，随后重启桌面应用和 VPN，要求在这里重试 B。A 已从临时附件按原字节归档；观察可见开放甲板、三名居民和生活设施，木台、地毯、绿植与帆布构成较强的生活气息。A 尚不是最终选定美术方向。

重启后此次内置生图成功，已检查并归档 [B · 荒野工程车](art-references/nomad-first-pass/B-field-engineering.png) 和 [本次生成说明](art-references/nomad-first-pass/B-generation-prompt.txt)。B 使用 A 作为构图和内容参考，保持三名居民、开放甲板和驿站；可见车体改为更方正的沙黄/石墨模块，金属甲板取代生活地毯，检修盖、轮组与设备外壳更厚实。该成功证明这次内置调用恢复，不足以区分应用重启、VPN 重启或服务自身恢复各自的作用。当前已有 A/B 两张候选，C 尚未生成，最终方向等待用户比较。

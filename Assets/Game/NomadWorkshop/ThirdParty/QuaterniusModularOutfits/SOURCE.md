# Quaternius 模块化服装来源

- 作者：Quaternius
- 作品：Modular Character Outfits - Fantasy，Standard 免费版
- 官方页：https://quaternius.itch.io/modular-character-outfits-fantasy
- 许可：CC0 1.0，原始许可证为同目录 License_Standard.txt。
- 获取日期：2026-09-06；免费上传项 16289385，原始 ZIP 294347394 字节。
- 当前保留：Male_Peasant 全套、Male_Ranger_Head_Hood、相应 BaseColor/Normal。未改写这些原始 FBX / PNG 的字节。

游戏自有候选 ArtFirstPass/Models/NW2_PeasantResident.fbx 由
Tools/ArtPipeline/Blender/blender_nomad_resident_sample.py 生成：保留服装骨架，组合本项目已保留的 Universal Base Characters 的头部/眼睛/眉毛，修剪被衣服覆盖的原身体，并在派生网格中隐藏短袖上衣内侧的长袖肩部。头部与颈部骨位相容；衣服骨架的手臂/腿与原 Superhero 人体不同，未将整身衣服直接挂到原人体骨架。

袖子遮罩只属于这个固定 Peasant 配方：T-pose 下 Arms 网格的 `abs(world.x) < 0.34 m` 处被短袖上衣覆盖，抬臂时两层权重差异会导致棕色内袖穿出白色肩部，因此从派生网格去除这部分，保留衣袖出口的重叠。它不是任意人物/衣物的通用坐标规则。当前候选共 19,134 个三角形，Prefab 明确使用四骨骼权重；没有改写源 FBX/PNG 或项目全局质量设置。

生成工具需要 --outfit-dir 指向本目录、--body-dir 指向相邻 QuaterniusUniversalBaseCharacters、--output-dir 指向工程外中间目录；用 Blender --background --factory-startup --python-exit-code 1 --python <脚本> -- <参数> 执行。它只导出中间 .blend / FBX 与离线预览，Unity 材质/Avatar/包装 Prefab 由 NomadResidentSamplePipeline 配置。

这套候选服装仍偏中世纪，不是最终工装，也没有参数化身材或运行时换装能力。Unity 候选场景为 ResidentAppearanceSample；主场景在候选验收前继续保留原资产。

三居民候选另保留同一免费标准包的 `Female_Peasant.fbx`，SHA-256 为 `cb33c493852310400ca3974fe6734fcd4e1da5232b527708a6082a86b90ca5d5`。女性服装保留自身骨架；其正常头部和束发来自 Universal Base Characters 同性基体，不套用男款内袖遮罩。

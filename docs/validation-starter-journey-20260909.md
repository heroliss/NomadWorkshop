# N1-B 起步样板验证（2026-09-09）

本记录收口 Foundation §9 的 N1-B 最小目标：一名玩家化身从只有驾驶位的微型车开始，在路边拾取有限废料，并能选择在驾驶位休息；旧的三居民 Foundation 场景继续保留为独立正式入口。

## 已交付

- 纯 C# `NomadStarterScavengeState`：废料点、有限携带量、拾取阻塞原因、座位休息意图和慢速恢复 / 健康与心情代价。
- Unity Editor 生成并回读 `Assets/Game/NomadWorkshop/Scenes/StarterJourneySample.unity`，没有手改场景 YAML。
- 场景包含沿车身长轴朝前的微型车、单驾驶位、路边废料堆、直线路面标记和低干扰中文 HUD。
- 运行时输入：`E` 拾取一份废料，`R` 开关座位休息。正式资源库存、驾驶租约和 Command 接线留给后续 N2/N3。

## 自动化证据

- Unity：`6000.3.22f1`，`StandaloneWindows64`，独立工程 `D:\SSFramework-Migration\nomad`。
- 编译错误：0。
- 新增规则定向 EditMode：4/4，通过，任务 `ee9f80076d11`。
- 完整 EditMode 回归：882/882，通过，0 失败，0 跳过，任务 `f6eddde874be`，约 73.9 秒。
- 运行时场景状态：进入 `StarterJourneySample` Play Mode 后，调用同一公开入口完成一次拾取，废料从 `0/3` 变为 `1/3`、路边剩余从 5 变为 4；随后启用座位休息并推进 120 秒，疲劳 `0.58 → 0.16`、健康 `1.00 → 0.9856`、心情 `0.70 → 0.658`。
- 画面证据：`Screenshots/nomad-n1b-starter-journey-20260909.png` 已实际查看，确认 HUD、车身长轴、驾驶位、直线路面和废料堆同框可读。

## 仍未覆盖

这份证据证明的是 N1-B 的规则与起步样板可运行，不等同于完整游戏开局。尚未覆盖真实 Input System 设备链路、车辆加减速、正式资源库存 / 存档、车外建造和玩家对美术舒适度的最终判断；进入 Play 后已停止并恢复到非 Play、场景未修改状态。

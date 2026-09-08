# NomadWorkshop

NomadWorkshop 是当前正式开发的游牧工坊游戏。它是独立 Unity 工程，只通过 `com.liss.ssframework` 消费框架；玩法、模拟、场景、美术和生产文档都属于本仓库。

## 依赖边界

- Unity：6000.3.22f1
- Framework：`com.liss.ssframework`
- Framework 以 Git submodule 固定在 `Packages/com.liss.ssframework/`
- `Game.NomadWorkshop.Simulation` 保持纯 C#，不依赖 Unity 或 Framework

NomadWorkshop 不依赖 Outpost 或 FrameworkTutorial。跨项目通用能力先在本项目形成证据，再回流 Framework。

## 分支

`main` 保持可运行里程碑，`develop` 用于当前集成，功能使用短期 `feature/*` 分支。阶段发布使用 `v0.x.y` 或带里程碑后缀的 Tag。

## 打开与验证

```text
git submodule update --init --recursive
```

Foundation、Journey、玩法设计和验证记录位于 `Assets/Game/NomadWorkshop/README.md` 与 `docs/`。
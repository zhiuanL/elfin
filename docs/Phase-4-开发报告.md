# Phase 4 开发报告

## 1. Status

PARTIAL — Phase 4 代码与自动验证完成；部分人工反馈已复核，M03/M06 未达到体验预期，其余人工与环境矩阵仍不完整。未进入 Phase 5。

日期：2026-08-28。基线为 main / v0.3-phase3；开发前工作区干净，Phase 0–3 的 214 项测试通过。

## 2. 完成功能

- 独立 MovementEngine、四模式、Home/VisualAnchor、Quiet/Natural/Lively、运动硬限制。
- 显示器抽象、授权屏幕范围、负坐标、DPI 尺寸、保守跨屏与显示变化恢复。
- Runtime 移动行为、方向语义/镜像/fallback、拖动抢占与生命周期取消。
- 统一鼠标穿透服务/命令、8 秒临时穿透、托盘恢复交互。
- 最小移动诊断入口与中英文资源；Settings schema 5；角色 Schema 1 可选字段。
- 未新增项目、数据库业务表或 Phase 5+ 功能；原有九项目边界保持。

## 3. 主要文件

路径均相对于仓库根目录：

- `src/DesktopPet.Domain/Movement/`：强类型模型、模式/显示策略、几何、运动预设与轨迹。
- `src/DesktopPet.Application/Movement/`：引擎、目标编排、Runtime action、平台端口、穿透服务/命令。
- `src/DesktopPet.Application/Runtime/PetRuntime.cs`、`BehaviorScheduler.cs`：动作接入、生命周期串行化。
- `src/DesktopPet.Windows/Windowing/`：WindowsMovementSurface、MonitorDpiProbe、显示器/窗口/穿透适配。
- `src/DesktopPet.Windows/Characters/WpfAnimationSurface.cs`、`src/DesktopPet.App/Views/PetWindow.xaml`：镜像绑定。
- `src/DesktopPet.Application/Configuration/MovementSettings.cs`、`AppSettings.cs`、Infrastructure 的 `JsonSettingsService.cs`：偏好与迁移。
- `src/DesktopPet.CharacterSdk/CharacterDefinition.cs`、`CharacterPackageValidator.cs`、`docs/character-manifest.schema.json`：兼容扩展及校验。
- `src/DesktopPet.App/ViewModels/MovementToolsViewModel.cs`、Bootstrap 接线、MainWindow、Localization：诊断和交互入口。
- `tests/DesktopPet.Tests.Unit/MovementPolicyTests.cs`、`MovementEngineTests.cs`、`MouseInteractionTests.cs`；Integration 的 `MovementIntegrationTests.cs`、`WindowsWindowTests.cs`；`tests/Shared/ManualTimeProvider.cs`。
- `tools/Verify-Phase4.ps1`、README、执行规范第 24 节：验证、使用说明与实施契约。

## 4. Movement Engine

行为选择“移动”，目标策略选择“去哪里”，引擎执行“怎样移动”。引擎不访问 PNG、数据库或 Win32。

异步可取消直线轨迹；三次/五次 easing，由导数峰值计算持续时间，起止速度为零。速度上限 300 DIP/s、加减速上限 600 DIP/s²、最短移动间隔 8 秒、最长单段 45 秒。只在可见且移动时约 30fps 更新；帧中断超过 250ms 取消，避免唤醒后瞬移。

系统硬限制 > 显式用户风格/参数 > 角色推荐 > Natural 默认值。Quiet/Natural/Lively 的默认速度为 40/80/140 DIP/s，间隔为 45/25/15 秒。

## 5. Movement Modes

| 用户语义 | 已有配置枚举 | 本阶段行为 |
|---|---|---|
| Fixed | Fixed | 禁止自主位移，保留拖动 |
| SmallRange | Local | 围绕 Home 的 DIP 半径采样 |
| FullDesktop | Desktop | 在允许屏幕工作区内采样 |
| Hybrid | Hybrid | 默认局部；空闲两分钟后以 20% 概率扩大范围；互动后可返回 Home |

默认 Hybrid / SmartHybrid / Natural。Anchor 保持局部，Roaming 偏漫游；ScenarioBased 暂用基本混合策略，不引入 Focus/Pomodoro。

Home 为全局物理锚点与屏幕 ID，拖动默认更新；无效 Home/已移除屏幕重新夹取。Settings Service 原子保存 Home、窗口位置和偏好，schema 1–4 迁移为 5 并保留备份；不保存运行中路径或动画帧。

## 6. Display / Multi-Monitor

WindowsDisplayService 实现 IDisplayService / IDisplayTopologyService，返回 Device ID、Bounds、WorkingArea、DPI、Primary 与简单邻接。

默认 LockedCurrent（CurrentDisplay），不主动跨屏。PrimaryOnly、SelectedMonitors、AllMonitors 对应另外三种策略；用户通过诊断入口明确应用选择。

只跨越已授权、相邻且工作区并集为完整矩形的屏幕。屏幕空洞、错位工作区和不连续路径拒绝跨越；不进行复杂图搜索。指定屏幕均离线时恢复可见位置，但不在未授权屏幕自主活动。

显示变化沿既有窗口消息通知 Runtime，先停止旧动作再重读拓扑、校正窗口/Home、恢复调度。

## 7. DPI / Coordinates

全局原点和工作区均为物理像素，负 X/Y 合法；WPF 尺寸为 DIP。跨屏路径按起止屏幕尺寸的最大包络检查整个窗口，不只检查原点。

沿用 PerMonitorV2 manifest；WPF 处理 WM_DPICHANGED，不重复应用建议矩形。每屏 DPI 使用临时、不可见的 PMv2 HWND 调用 GetDpiForWindow，立即 Dispose 并恢复线程 DPI 上下文，不调用在 PM-aware 线程上不适用的 GetDpiForMonitor。依据：[GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)、[SetThreadDpiAwarenessContext](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext)。

DPI 改变会中止当前移动并校正后再调度；不宣称已完成真实混合 DPI 无缝漫游。

## 8. Facing / Animation

Schema 保持 1，新增可选 visualAnchor / supportsMirroring / movement。旧包全部兼容；依赖新字段的包可声明 minimumAppVersion 0.4.0。

左/右优先对应 walk-left / walk-right；允许镜像时使用相反方向资源，否则普通 walk，再 idle/fallback。移动仍保持 Moving 状态，即使视觉 fallback 为 idle；完成后恢复 Idle，取消/换角色清除镜像。

VisualAnchor 默认为整个窗口画布底部中点 (0.5, 1)，允许归一化自定义；不是 Alpha 像素轮廓或完整 HitArea。窗口仍使用既有 220 DIP 画布，不按角色名称或物种分支。

## 9. ClickThrough

统一 SetInteractive / SetClickThrough / ToggleClickThrough / TemporaryClickThrough 命令调用 IMouseInteractionService；诊断 ViewModel 直接复用同一服务，避免命令注册器与窗口构造形成依赖环。

Windows 层在透明 layered window 上仅切换 WS_EX_TRANSPARENT，保留其他样式，并用 SetWindowPos / FRAMECHANGED 刷新。依据：[Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)。

临时穿透 8 秒自动恢复，显式命令取消旧定时器；托盘始终提供“恢复鼠标交互”。隐藏/退出恢复交互，启动不恢复穿透，避免不可点击的历史状态。未实现智能 Focus/Fullscreen 穿透。

## 10. 生命周期与并发

- Pointer Down 在平台线程先撤销自主位移权限；每次实际移动再次核查权限，拖动期间不会继续 SetWindowPos。
- Runtime 使用既有生命周期门串行化；Hide、切换、拖动、显示变化、Exit 取消并等待旧移动及动画。
- Drag Complete 校正位置、按设置更新 Home，然后恢复调度；Show 先校正后恢复。
- Stop/Dispose 幂等；退出停止 Runtime、临时穿透计时器，释放窗口与托盘。
- 没有全局可变业务单例、UI SQL、Window JSON 读写或 Application Win32 调用。

## 11. 自动测试

使用仓库 SDK 10.0.400 / .NET 10.0.11，执行验证脚本中的 locked `dotnet restore`、`dotnet build -c Debug/Release`、`dotnet test -c Debug/Release`。

| 配置 | Restore | Build | Test |
|---|---|---|---|
| Debug | 成功 | 0 warnings / 0 errors | 148 Unit + 111 Integration = 259 passed，0 failed / skipped |
| Release | 成功 | 0 warnings / 0 errors | 148 Unit + 111 Integration = 259 passed，0 failed / skipped |

最终 TRX 位于 `artifacts/TestResults/`（忽略目录）：

- Debug：`phase-4_net10.0_20260830220452.trx`、`phase-4_net10.0_20260830220509.trx`。
- Release：`phase-4_net10.0_20260830220457.trx`、`phase-4_net10.0_20260830220516.trx`。

新增 45 项；覆盖起止点、取消/拖动/隐藏/长帧/尺寸变化、四模式、显示策略/空洞/移除、Home/设置迁移、100/125/150/200% 数学、朝向/fallback、穿透命令和真实 HWND 样式。2026-08-30 针对反馈新增全桌面远目标、SmartHybrid 空闲统计边界和三风格参数/轨迹排序测试。

保留原有 214 项。历史测试只将迁移目标版本断言从字面值 4 改为 CurrentSchemaVersion，其余验收断言保留。集成测试发现并修复了 UI 构造依赖循环，以及虚拟时钟早于动画截止计时器注册的竞争；最终生命周期五案例额外连续执行五轮均通过，没有靠重试忽略失败。

## 12. 人工测试

可执行步骤、逐项结果表与反馈模板见 [Phase 4 人工测试文档](Phase-4-人工测试文档.md)，证据映射见 [Phase 4 反馈复核报告](Phase-4-反馈复核报告.md)。状态仍为 PARTIAL。

用户已人工确认 W01、M01、M02、M05、H01、L01、L02、L03 共 8 项通过。M03 未观察到全桌面大范围移动，M06 三风格视觉区分不明显，均保留 FAIL；M04 未观察到 Hybrid 扩大范围，按概率事件规则保留 BLOCKED。策略、UI 枚举映射与设置路径复核通过，但自动结果不能覆盖上述人工结论。

其余核心项没有本次人工证据；方向项缺少不对称素材，扩展矩阵缺少多屏/混合 DPI/热插拔/Windows 10 条件。computer-use 按技能初始化并重置重试仍失败：`windows sandbox failed: helper_unknown_error: setup refresh had errors` / `trusted Node process exited unexpectedly`。未使用其他 UI 自动化绕过，也未声称完成鼠标/视觉验收。

已完成的真实进程检查属于自动验证：Windows 11 家庭版中文版 10.0.26200，x64，单屏，工作区 1920×1080，DPI ×1。Release EXE 在独立 `artifacts/phase4-soak-20260828-1455` 数据目录运行约 181.7 秒，退出码 0；日志含 2 次 MovementStarted / 2 次 MovementStopped，随后回到 Idle，无 Failure；位置已保存，进程 PID 98644 已结束。此过程没有覆盖用户配置。

人工待验收步骤：

1. 按 README 启动；分别应用四模式，观察范围、Home 和屏幕边界，留出调度间隔。
2. 移动中立即拖动接管；释放后检查 Home，重启确认位置恢复。
3. 用带左右动画/镜像能力的包核对朝向；用旧 Basic 包核对无 walk 仍能移动与结束恢复。
4. 测试穿透/临时穿透与托盘恢复，Hide/Show、换角色、退出无残留，并回归 Phase 1–3 操作。
5. 有条件时测试真实副屏负坐标、125/150/200% 混合 DPI、跨屏开关、热插拔/任务栏变化及 Windows 10。

## 13. Open Decisions

- 为保留既有设置，Local/Desktop、LockedCurrent/SelectedMonitors/AllMonitors 不改名，UI 显示用户语义。
- 设计要求显示器邻接，阶段提示不要求复杂图搜索：采用邻接数据 + 连续矩形直线段，宁可不跨屏。
- “可配置穿透”当前为会话级命令设置，不落盘；复杂恢复/自动切换留后续明确需求。
- 显式 UserMotionStyle 区分“默认 Natural”与“用户选 Natural”，保证角色推荐不会覆盖明确用户意图。
- 锚点以窗口画布为基准；非对称素材的精确脚点镜像补偿留后续资源/HitArea 设计，不假定角色类型。
- Phase 3 报告标题为 PASS，但部分历史正文仍记录当时的人工待验收；本次以用户明确的人工通过声明为基线，不追改历史记录。

## 14. Risks / Technical Debt

- 当前最大缺口是 M03 全桌面现场体验与策略测试不一致、M06 三档视觉区分不足，以及方向素材/生命周期回归尚未完成；Windows 10、多显示器、混合 DPI、热插拔体验未真实验证。
- 极窄工作区放不下整个窗口时只恢复原点可见并暂停自主移动；跨屏受任务栏布局/45 秒段长限制可能保持原屏。
- 显示变更目前由 PetWindow 消息驱动；独立 IDisplayTopologyService.TopologyChanged 的主动发布接线未作为另一套重复事件源启用。
- 无真实锁屏/睡眠/全屏策略；长帧保护不能替代后续会话/电源订阅。帧率、路径段长和行为频率仍需真实设备调优。
- GetWindowLongPtrW 等按既定 Windows x64 目标验证；未新增 x86 分发支持。
- 诊断为手动快照，SelectedDisplays 暂用设备 ID 文本；正式设置页/快捷键留 Phase 5。既有全应用多实例互斥仍未实现。

## 15. Git 状态

main；origin 保持 `https://github.com/zhiuanL/elfin.git`；基线 Tags v0.0-phase0 / v0.1-phase1 / v0.2-phase2 / v0.3-phase3 均存在。

开发完成时（追加人工测试文档前）已检查 git status / git diff --stat / git diff / git diff --check：38 个已跟踪文件修改、22 个新增未跟踪文件（含本报告）；当时已跟踪差异 389 insertions / 52 deletions，未跟踪新增不计入普通 diff --stat。没有删除文件。

未提交、未暂存、未 push，未改 origin/Tag/历史，未执行 init/reset/rebase。未将 API Key、数据库、bin/obj、日志或本地配置纳入跟踪；运行产物仅位于忽略目录。Git 提示的 LF→CRLF 是既有换行策略提示，不是构建 Warning；diff --check 无格式错误。

## 16. Phase 5 建议

先完成并确认 Phase 4 人工验收。获得下一条指令后，按规范实现 Control Center 的 Home / Settings / Character Manager / Hotkeys，复用本阶段服务与命令；本次不开始。

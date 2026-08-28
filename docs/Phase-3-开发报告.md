# Phase 3 开发报告

## 1. Status

**PASS — 实现与自动回归通过，人工行为验收待完成。**

仅执行 Phase 3。没有进入 Phase 4。Phase 0–2 原有 174 项测试全部保留并通过。

## 2. 完成功能

- 语义状态机、强类型行为定义、异步调度、独立冷却、近期动作抑制。
- Mood / Energy / Boredom / Affinity、本地可解释 Utility 评分、可注入时钟和随机源。
- 消费 Phase 2 Behavior/Emotion Profile；支持 Idle/Blink/Happy/Rest 自主选择。
- 单击反馈、连续互动计数、拖拽时暂停；切换/隐藏/显示/退出统一取消与恢复。
- 按角色保存稳定情绪；控制中心增加轻量运行诊断；中英文资源同步。
- 没有新增项目、包依赖、数据库业务表、AI/网络调用或自主位置移动。

## 3. 主要新增/修改文件

路径相对于仓库根目录。

| 文件 | 职责 |
|---|---|
| src/DesktopPet.Domain/Pets/BehaviorModels.cs、PetStateMachine.cs | 行为/上下文/评分模型与状态转换 |
| 同目录 RuntimePolicy.cs、EmotionModel.cs、UtilityDecisionEngine.cs、RecentBehaviorMemory.cs | 集中策略、情绪、评分和有界近期记忆 |
| src/DesktopPet.Application/Runtime/PetRuntime.cs、BehaviorScheduler.cs | 实例生命周期及单一调度循环 |
| 同目录 BehaviorCatalog.cs、RuntimeContracts.cs | Profile/用户覆盖与平台无关契约 |
| src/DesktopPet.Application/Characters/CharacterPresentationService.cs | 复用 PNG Provider，增加有时限播放及实际 fallback 回调 |
| src/DesktopPet.Application/Configuration/RuntimeSettings.cs、AppSettings.cs | 情绪 checkpoint 与 schema 4 |
| src/DesktopPet.Infrastructure/Characters/CharacterBehaviorProfileReader.cs | 安全读取已校验 Profile，异常降级 |
| src/DesktopPet.Infrastructure/Configuration/JsonSettingsService.cs | schema 1/2/3 升级至 4 |
| src/DesktopPet.Windows/Windowing/WindowsPetWindow.cs | 原生拖动入口转发指针/点击/结束事件 |
| src/DesktopPet.App/Bootstrap/、ViewModels/RuntimeDiagnosticsViewModel.cs、Views/MainWindow.xaml | 宿主接线、诊断与有界运行检查选项 |
| tests/…/BehaviorTests.cs、BehaviorSchedulerTests.cs、PetRuntimeTests.cs、tests/Shared/ManualTimeProvider.cs | 领域、虚拟时间和集成回归 |
| tools/Verify-Phase3.ps1、README.md、V1 Codex 执行规范 | 验证入口、启动说明及跨模块契约 |

## 4. State Machine 架构

沿用设计文档 Primary/Transient：Idle、Acting、Resting、Talking、Dragging；Blink/Happy 为瞬时语义。BehaviorId 记录意图，AnimationSemantic 记录实际播放，fallback 后不虚报 Happy/Rest。状态机只处理进入、完成、优先级、最短时长和可中断性，不负责评分。

动画完成后回到 Idle。Talking 仅有状态/语义入口，不包含 AI/TTS。实例状态由 PetHost 持有的单个 PetRuntime 所有，不使用静态业务状态。

## 5. Behavior Scheduler

一个可取消异步循环等待动画完成/持续时间，再决策；非 Idle 行为之后先回到稳定 Idle。无毫秒轮询或额外 DispatcherTimer。取消并等待旧循环后才能重配角色或恢复，拒绝重复 Run。

默认参数集中于 RuntimePolicy：

| 行为 | 基础权重 | 冷却 | 持续时间 |
|---|---:|---:|---:|
| Idle | 2 | 0 | 2–6 秒 |
| Blink | 6 | 4 秒 | 0.2–0.5 秒 |
| Happy | 1.2 | 18 秒 | 1–3 秒 |
| Rest | 0.8 | 40 秒 | 6–12 秒 |

近期记录最多 64 条/2 分钟；12 秒内抑制同一非 Idle 动作连续再次执行，近期次数进一步衰减权重。保留每个行为最后执行时间用于独立冷却。角色冷却限制 2–300 秒，基础权重和推荐倍率有上限。

## 6. Emotion Model

内部保留小数，输出统一 Clamp 到 0–100；初始值依次为 60/70/20/20。无操作时间增加 Boredom，休息恢复 Energy，互动改善 Mood/Boredom 并轻量增加 Affinity；动作完成也有小幅变化。单次时间补偿最多 5 分钟，互动奖励 500ms 防抖，变化规则集中配置。

通过现有 Settings Service 原子保存 Mood/Energy/Affinity，每 2 分钟以及隐藏、切换、正常退出保存。按角色最多保存 256 个 checkpoint。Boredom 重置为初始值，近期动作/冷却/帧号不恢复；不写 ai.db，不高频逐帧写盘。

## 7. Utility 评分策略

先过滤不可见、互动中、禁用/非法、缺少能力、冷却、近期连续重复，再计算：

`BaseWeight × CharacterModifier × EmotionModifier × ContextModifier × UserModifier × RecentModifier`

低 Energy 提高 Rest，高 Mood/Boredom 提高 Happy，Affinity 对 Happy 最多增加 20% 倾向。最近互动 15 秒内提高 Happy，本地夜间 22:00–06:00 轻量提高 Rest。各倍率与过滤原因保存在诊断快照；按最终分数加权抽样，全零回退安全 Idle。

生产日志只记录枚举行为/状态与既有安全字段，调度事件按种类至少间隔 10 秒；不记录每次评分或路径、Profile 正文和敏感数据。界面评分/冷却为上次决策快照，不是实时倒计时。

## 8. Character Behavior Profile

消费 Schema 1 的 `behaviors[].animation/weight/cooldownSeconds` 及 Emotion Profile 语义映射。无 Profile 使用引擎默认；缺失角色能力直接过滤。读取保持路径、链接和文件大小限制，损坏配置报告后降级。

优先级为系统安全限制 > 用户配置 > 角色推荐 > 引擎默认。用户 Weight 是绝对基础权重覆盖，角色 Weight 是推荐倍率；用户可禁用非 Idle 行为。Idle 永远不能禁用或重映射。没有新增角色包格式，也没有实现未来情绪倾向字段。

## 9. 生命周期与并发处理

- PetRuntime 的操作门串行化启动、角色切换、隐藏/显示、互动和退出；调度循环不反向获取该门，避免退出互等。
- 角色切换先取消并等待旧行为，保存旧情绪，激活新包、清空近期状态、加载新能力与情绪，再恢复。
- 隐藏停止调度与动画；显示重新进入 Idle。指针按下交出动画控制，拖拽结束恢复，单击触发情绪和短反馈。生命周期/指针接管属于强制取消边界。
- 正常退出先结束工具操作与 Runtime，再停止窗口/托盘；Runtime Dispose 可重复调用。任务异常经原有统一异常处理器上报。
- UI 不直接读写 JSON/SQLite；App.xaml.cs、PetWindow.xaml.cs 未增加业务逻辑。未新增 Win32 移动/DPI 实现。

## 10. 自动测试数量及结果

2026-08-28，仓库 .NET SDK 10.0.400；执行脚本等价于 locked restore、对应配置 build 和 test。

| 验证 | 结果 |
|---|---|
| 修改前 Phase 2 Release 基线 | 174/174 通过 |
| dotnet restore DesktopPet.sln --locked-mode --packages .packages | 成功 |
| dotnet build / test -c Debug | 0 警告、0 错误；112 单元 + 102 集成 = 214 通过 |
| dotnet build / test -c Release | 0 警告、0 错误；112 单元 + 102 集成 = 214 通过 |

新增 40 个测试用例；无失败、无跳过。覆盖状态/非法转换/中断/fallback、评分与固定种子、冷却/重复/能力过滤、Clamp/时间/互动、取消/重复调度/隐藏恢复、角色切换竞争、磁盘情绪恢复、损坏配置、配置集合值相等、版本迁移、checkpoint 和幂等释放。纯时间测试使用 ManualTimeProvider，不真实等待数十秒。

最终 TRX：
- Debug：`phase-3_net10.0_20260828121101.trx`、`phase-3_net10.0_20260828121117.trx`。
- Release：`phase-3_net10.0_20260828121144.trx`、`phase-3_net10.0_20260828121200.trx`。
- 位于忽略目录 `artifacts/TestResults/{Configuration}/`。

首轮发现配置集合引用比较和 DI 重复释放问题，均修复实现后重跑通过；未删除或弱化原有断言。

## 11. 人工测试结果

**人工验收未通过确认。** Windows 11 家庭中文版 x64，10.0.26200。computer-use 初始化后内核退出，重建连接重试仍报 `windows sandbox failed: helper_unknown_error: setup refresh had errors`；无法取得桌面观察及执行鼠标交互。

已做的真实进程检查（不是人工验收）：
- Release EXE 使用独立数据根及 `--smoke-test --smoke-duration-seconds 180`，2026-08-28 12:07:14–12:10:16（UTC+8）运行约三分钟。
- 日志存在 Idle/Blink/Happy/Rest、Started/Stopping/SchedulerStopped，无 Failure；这些是限频采样，不能据此统计所有动作次数或评判观感。
- 退出后 PID 91016 已不存在，Settings 保存了 Standard 的 Mood 62 / Energy 70 / Affinity 20。该次外部进程观察未取得数值 ExitCode，不宣称取得了 0；独立真实 WPF 回归测试已验证正常退出码。
- 数据仅在 `artifacts/phase3-soak-ea26b98aca12423b99d93d14068bc8ed/`；未覆盖用户便携/安装数据。

待人工执行：按 README 启动 Debug，激活 Standard，至少观察 3–5 分钟；确认 Idle/Blink/其他动作及节奏，单击反馈、拖拽、双击控制中心，动作中切换角色，托盘 Hide/Show/Exit，重启恢复情绪。视觉、动画流畅度、真实拖拽/托盘及混合 DPI 交互均未在本阶段声称验证。

## 12. Open Decisions

- Phase 2 报告标题 PASS 与正文历史“人工未完成”描述不完全一致；以用户此次明确确认通过作为基线，未重写历史报告。
- Primary/Transient 命名优先沿用设计文档，概念 Interacting 由行为意图和指针接管状态表示。
- Profile Schema 1 未定义数值情绪倾向/复杂允许条件，当前只消费已有强类型字段；未知未来行为不擅自执行。
- 默认持续时间、权重、冷却及情绪变化量为本阶段最小可调实现，后续依据人工观感调优，不以固定动画循环替代 Utility。
- 新安装无选择时优先动作较完整的包；已有有效选择不变。Basic 仅有 idle，不能凭空显示其缺失动作。
- 稳定情绪保存到现有 Settings，不为本阶段增加 SQLite 表；未来环境信息继续扩展 BehaviorContext，不接入完整 Environment 服务。

## 13. Risks / Technical Debt

- **首要缺口是人工行为验收**，当前不满足 PASS 条件。Windows 10、不同 Windows 11 环境、多屏/混合 DPI/热插拔仍需真机验证。
- 单击以拖拽前后原点判断；拖出后精确回到原点可能计为单击。双击的第一次按下释放可能带来一次基础互动奖励，双击打开控制中心入口仍保留，需人工确认体验。
- 调度时长用单调时钟；近期记录/冷却使用 UTC，系统时钟大幅回拨可能延长一次冷却，未实现后续完整睡眠/环境策略。
- 动态修改用户运行参数在下次启动或角色重配时生效，未增加正式设置界面。评分显示是快照。
- 所有资源回退均不可用时停止该次调度并报告，不进行无限重试；需要重新校验/切换角色。全应用多进程互斥仍未实现。
- AppSettings 的集合值比较显式实现；未来增加设置字段必须同步 Equals/GetHashCode 和往返测试。缓存图像仍沿用 Phase 2 策略，长时间内存/CPU 体验需后续测量。

## 14. Git 状态

- 开始前工作区干净，分支 main；origin 为 https://github.com/zhiuanL/elfin.git，保留 v0.0-phase0 / v0.1-phase1 / v0.2-phase2。
- 本次未提交、未打 Tag、未 push，未修改 origin 或重写历史，未删除用户文件；resource/ 未改。
- 当前 27 个已跟踪文件修改、19 个新增文件（含本报告），全部为本阶段代码/测试/文档。已检查 git status / git diff --stat / git diff。
- 已跟踪差异统计 207 行新增、45 行删除；git diff --stat 不包含上述未跟踪新增文件。
- git diff --check 无空白错误。Git 提示 LF 将转换为 CRLF 是现有 Windows 换行策略提示，不是编译警告；未修改 Git 配置。
- bin/obj/数据库/日志/本地配置/测试数据均在忽略目录中，未暂存；没有新增密钥。

## 15. Phase 4 建议

先完成人工 Phase 3 验收，再等待用户明确指令。Phase 4 按设计处理 Movement/Display、移动模式、多显示器/DPI/穿透及其测试；**本次未开始**。


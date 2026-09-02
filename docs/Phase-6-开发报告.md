# Phase 6 开发报告

## 1. Status

**PARTIAL**。代码、数据库、Debug/Release 构建及 278 项自动测试均通过；真实 WPF 启动/渲染/八页面遍历/退出烟测通过。由于 Windows 应用控制辅助程序在本机连续两次初始化失败，18 项真实人工交互以及睡眠、锁屏、系统通知、多 DPI/多屏尚未完成，按 DoD 不报告 PASS。

## 2. 完成功能

- 离线 Pomodoro、Task/Tag、Statistics、Reminder、恢复、宠物联动和控制中心 Productivity 页面。
- Home 卡片、统一 Application Commands、托盘 Start/Pause、默认全局快捷键及中英文资源。
- 应用版本更新为 0.6.0；Settings schema 更新为 7。

## 3. 主要文件

- `Domain/Productivity/ProductivityModels.cs`：强类型 Session、Schedule、Reminder、统计与事件模型。
- `Application/Productivity/*`：Pomodoro、Task/Tag、统计/CSV、Reminder 调度/渠道、恢复和命令。
- `Infrastructure/Persistence/SqliteMigration.cs`、`SqliteProductivityRepositories.cs`：app.db v2 与 Repository。
- `Windows/Windowing/WindowsSessionStateService.cs`、`WindowsReminderNotificationChannel.cs`：Windows 会话/电源与通知适配。
- `App/ViewModels/{Pomodoro,Reminders,Statistics}ViewModel.cs` 与对应页面：控制中心 UI。
- `App/Bootstrap/ProductivityRuntimeBridge.cs`：业务事件、会话恢复与 PetRuntime 解耦桥接。
- `tests/*/PhaseSixProductivityTests.cs`、`ProductivityPersistenceTests.cs`、`tools/Verify-Phase6.ps1`：阶段验证。

## 4. DB Migration

app.db 从 v1 只向前升级到 v2，新增 `Tasks`、`Tags`、`TaskTags`、`PomodoroSessions`、`Reminders`、`ReminderExecutions` 及索引/外键。单活动 Pomodoro 和 Reminder occurrence 均有唯一约束；执行记录与下一次触发时间同事务提交。空库创建、旧库升级和失败回滚均有回归测试。ai.db 保持 v1，未写入 Productivity 数据。

## 5. Pomodoro

支持 Start/Pause/Resume/Stop、Focus/ShortBreak/LongBreak、自动循环、长休间隔、幂等 Complete、Running/Paused 重启恢复。业务真相为 `TargetAtUtc - TimeProvider.GetUtcNow()`；一秒循环仅刷新 UI，退出会取消并等待循环/计时器。

## 6. Task / Tag

Task 支持创建、更新、归档、列表和多标签；Tag 支持创建、重命名、删除和列表。Focus Session 可关联 0/1 Task；Task 使用归档保留历史，Tag 删除不会删除 Session。

## 7. Statistics

统计只读取持久化 Focus Session，提供今日时长/完成数、日/周/月趋势、Streak、Task/Tag 摘要和轻量 CSV。Completed 贡献实际时长、完成数和 Streak；Stopped 仅贡献实际时长；Running/Paused 不计入。UTC 数据在 Application 按 TimeZone 转换为本地日期，日界线使用确定性 DST 解析。

## 8. Reminder

支持 RelativeOneTime、AbsoluteOneTime、Recurring（Daily/Weekly/SelectedWeekdays/Interval）及 CRUD/启停。调度器只等待最近事件，变更时唤醒重算，不高频扫描。渠道包含 Pet Bubble、Pet Action、Windows Notification；Sound 为可选 NoOp，未实现 TTS。

## 9. Time / DST / Missed Strategy

Reminder 保存 TimeZoneId。DST 不存在时间前移到首个有效分钟，重复时间选择较早 occurrence。默认 Smart 补发窗口 15 分钟；短错过补发、长错过抑制、Recurring 只处理最近一次。ReminderExecution 事务唯一键避免重启/竞争重复发送。

## 10. Session / Sleep Recovery

Windows 层通过 WTS/Power 消息提供 Active/Locked/Sleeping/Resuming。锁屏/睡眠暂停 Pet 动画与自主行为，Pomodoro/Reminder 继续使用绝对时间；Resuming 只执行一次业务重算，然后恢复 Runtime。启动顺序为 app/ai migration → settings → Productivity/漏提醒 → PetRuntime。

## 11. Pet Integration

Pomodoro/Reminder 只发布 Application Event，不直接操作窗口。Focus 上下文降低 Move 与 Talking 权重；Focus 完成请求短暂 `happy` 语义；Bubble/Action 使用平台端口和 Runtime 门面。恢复中的 Running Focus 会在桥接启动时立即同步。

## 12. UI / Hotkey / Tray

新增 Pomodoro、Reminders、Statistics 三页及 Home 三张卡片/快速操作。Reminder 删除走确认服务。StartOrPausePomodoro、OpenPomodoro、OpenReminders 注册到统一命令；托盘提供 Start/Pause；默认 `Ctrl+Alt+F` 可配置并沿用冲突回滚。

## 13. 自动测试

| 配置 | Restore | Build | Unit | Integration | 结果 |
| --- | --- | --- | ---: | ---: | --- |
| Debug | PASS（locked） | PASS，0 warning / 0 error | 162/162 | 116/116 | PASS |
| Release | PASS（locked） | PASS，0 warning / 0 error | 162/162 | 116/116 | PASS |

覆盖绝对时间、暂停/恢复/停止、自动阶段/LongBreak、重复完成、重启/延迟、相对/绝对/重复提醒、启停/编辑/删除、Smart missed、DST、调度取消、execution 去重、Task/Tag CRUD/关联、统计/本地日期/Streak、schema 6→7、migration/回滚、恢复顺序及 Phase 0–5 回归。TRX 位于 `artifacts/TestResults/{Debug,Release}`。

## 14. 人工测试

环境：Windows 11 家庭版中文版 x64，10.0.26200；commit `b7c2844` 基线。真实 WPF 自动烟测确认控制中心与 PetWindow 可创建、八个非 AI 页面可渲染/遍历、应用可正常退出且无残留进程。Windows 应用控制组件因 sandbox helper 初始化错误无法进行可靠点击和视觉验收，因此 `Phase-6-人工测试文档.md` 的 18 项核心用例均保留未测；睡眠/锁屏、通知、多 DPI/多屏也未验证。

## 15. Open Decisions

- Stopped Focus 按实际专注秒数计入时长但不计完成数/Streak；符合“实际时长可解释”原则，等待产品确认。
- Task 采用 Archive；Tag 允许删除当前关联，历史 Session 的 TaskId 保留，但删除后的 Tag 不再显示历史标签聚合。若未来要求不可变历史标签，应增加 SessionTag 快照 migration。
- Sound 渠道保持 NoOp；真实音效资产和策略后续再决定，但不扩展到 TTS。

## 16. Risks / Technical Debt

- Windows 10/11 双版本、真实 Toast/托盘、锁屏/睡眠、时区切换、100/125/150% DPI、多显示器和热插拔仍需按人工文档复验。
- WTS 隐藏窗口与系统通知依赖交互式桌面；服务会话/远程桌面行为未验证。
- 全应用多实例互斥仍未实现；多个实例不应共享同一数据目录。
- Statistics 页面为轻量列表，不是复杂图表；符合 Phase 6 范围。

## 17. Git 状态

分支 `main`，origin 未修改，基线 tag `v0.5-phase5` 存在。工作树仅含本次 Phase 6 源码、测试和文档变更；未提交、未 push。未跟踪/提交 `*.db`、`bin/`、`obj/`、`logs/`、用户配置或烟测临时目录。

## 18. Phase 7 建议

在用户完成 Phase 6 人工验收并确认 PASS 后，再按文档进入 AI Provider、安全凭据、流式会话与记忆边界；不要将 AI 反向依赖到离线 Productivity 或 Pet Core。

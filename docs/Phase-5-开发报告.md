# Phase 5 开发报告

## 1. Status

PASS**

实现、Debug/Release 构建和自动测试全部通过；真实 Debug Portable 进程可启动、响应，schema 5→6 与 `Started` 日志已确认。Windows UI 控制助手在初始化阶段连续失败，无法取得导航、视觉、角色选择器、托盘和系统级快捷键的人工操作证据，因此不能报告 PASS。

## 2. 完成功能

- 控制中心导航壳：首页、角色、设置、快捷键、诊断；重复导航不重复创建窗口。
- 首页：当前角色、运行状态、移动摘要及显示/隐藏/穿透快捷操作。
- 正式角色管理：资源管理器选择 ZIP、校验、预览、等级/完整度/能力、导入并启用、切换、受保护删除与二次确认。
- 分类设置：语言、关闭行为、小精灵显示/置顶、移动/显示器/风格/Home、鼠标交互、System/Light/Dark、版本信息。
- 全局快捷键：六组强类型默认组合、启用/修改/禁用、重复校验、Windows 冲突反馈、失败回滚、恢复默认、持久化及退出释放。
- zh-CN/en-US 即时切换；运行时 WPF 动态主题资源。
- Settings schema 6 与 1–5 向前迁移；应用/角色兼容版本更新为 0.5.0。
- 未实现 Phase 6+ 的办公、AI、TTS 或其他业务。

## 3. 主要新增/修改文件

- `src/DesktopPet.App/Views/MainWindow.xaml`、`Views/Pages/*`：导航壳及五个独立页面。
- `src/DesktopPet.App/ViewModels/*Dashboard*`、`CharacterManagerViewModel.cs`、`SettingsViewModel.cs`、`HotkeysViewModel.cs`：页面状态与命令意图。
- `src/DesktopPet.Application/Navigation/ControlCenterNavigation.cs`：可测试导航状态。
- `src/DesktopPet.Application/Hotkeys/HotkeyCoordinator.cs`、`Configuration/ControlCenterSettings.cs`：快捷键生命周期与强类型配置。
- `src/DesktopPet.Windows/Windowing/WindowsGlobalHotkeyService.cs`：`RegisterHotKey` 平台适配。
- `src/DesktopPet.Windows/Characters/CharacterPreviewLoader.cs`：受限 PNG 预览加载。
- `src/DesktopPet.App/Appearance/WpfAppearanceService.cs`：集中主题资源应用。
- `tools/Verify-Phase5.ps1`、`docs/Phase-5-人工测试文档.md`：阶段验证与人工清单。

## 4. 架构决策

- MainWindow 只负责页面承载；页面不读 JSON/SQLite、不调 Win32、不写业务逻辑到 Code-Behind。
- 首页/设置意图经 MainWindow → WindowEventBridge → ICommandRegistry；避免 ViewModel 构造时反向创建 MainWindow 的循环依赖。
- 角色页复用既有 Character Application Service；预览 IO 放在 Windows 适配层。
- 快捷键 ViewModel 在窗口建立后由事件桥调用 Coordinator；Coordinator 统一注册、回滚、持久化和命令分发。

## 5. Windows API / 主题 / 本地化

- `RegisterHotKey`/`UnregisterHotKey` 使用隐藏 `HwndSource`、`MOD_NOREPEAT`，错误 1409 映射为冲突。
- 主题只更新集中动态画刷，不修改 PetWindow 透明渲染链。
- Culture 通过 ITextLocalizer 事件刷新导航、正式页面和移动选项；资源缺失按 en-US→键名回退。

## 6. Settings / Migration

- schema 6 新增 `AppearanceSettings` 与 `HotkeySettings`；组合键、命令、主题均为枚举/记录类型。
- schema 1–5 升级到 6 并保留既有备份；未来 schema 仍拒绝降级覆盖。
- `app.db` / `ai.db` 及数据库 Migration 未改变；Phase 5 不新增业务表。

## 7. Character / Hotkey / Lifecycle 策略

- ZIP 流程显式为 Validate → Import → Activate；失败不覆盖已有角色。
- 当前角色删除命令不可用；其他角色删除需 Windows 确认框。
- 快捷键启用项必须包含修饰键、命令唯一、组合唯一；应用失败恢复上一组注册。
- Stop 顺序先停止页面请求、注销全局快捷键，再停止 Runtime/输入和窗口/托盘资源。

## 8. 自动测试结果

| 配置      | Build               | Unit     | Integration | 总计       |
| ------- | ------------------- | --------:| -----------:| --------:|
| Debug   | 0 warning / 0 error | 151 PASS | 112 PASS    | 263 PASS |
| Release | 0 warning / 0 error | 151 PASS | 112 PASS    | 263 PASS |

TRX：`artifacts/TestResults/Debug/phase-5_*.trx`、`artifacts/TestResults/Release/phase-5_*.trx`。覆盖导航、schema 6 round-trip、快捷键重复/冲突回滚/禁用/分发/释放、既有窗口/角色/行为/移动及真实 WPF 进程回归。Phase 5 冒烟模式还会在真实 Dispatcher 上依次创建 Home、Characters、Settings、Hotkeys、Diagnostics 五个页面后返回首页，用于捕获 XAML 加载和绑定异常。

## 9. 实际人工测试环境与结果

- 环境：Windows 11 25H2 / Build 26200.9168 / x64；当前检测 2560×1440，AppliedDPI 120（125%）。未取得真实多显示器或 Windows 10 证据。
- Debug Portable 通过仓库 `.tools/dotnet/dotnet.exe` 启动；进程响应，标题为“桌面小精灵 · 控制中心”。
- 旧便携 settings 从 schema 5 迁移到 6；最新日志包含 Starting/CharacterSwitched/Started，无新的 StartupFailed。
- `computer-use` 内核连续两次因 Windows sandbox helper 初始化错误退出。未实际点击页面、切换主题/语言、选择 ZIP、触发托盘或系统快捷键；这些项目保持未测。
- 人工步骤见 `docs/Phase-5-人工测试文档.md`。为解除 Debug 输出文件锁并完成最终双配置验证，先前启动的验收实例已经停止；当前没有由本次任务保留的应用进程。

## 10. Open Decisions

- System 主题当前在应用/设置时解析系统颜色；是否监听 Windows 主题变化并即时刷新，留待后续体验决策。
- 快捷键暂限定 A–Z、F1–F12 与常用修饰键，避免 V1 提前引入无边界键盘映射。
- 多实例共享同一数据目录和全局快捷键的产品策略尚未定义；当前冲突安全失败，不覆盖另一实例。

## 11. Risks / Technical Debt

- Windows 10、真实多屏、负坐标、混合 DPI 和热插拔仍需按人工清单验证。
- 未取得真实视觉/交互证据，Status 必须保持 PARTIAL。
- System 主题不主动订阅系统主题变更；运行中由用户重新应用即可刷新。
- DOCX 需求已结构化完整读取，但当前环境缺少 LibreOffice，未能额外执行页面渲染检查；不影响源码编译测试。

## 12. Git 状态

- 分支 `main`；origin 保持 `https://github.com/zhiuanL/elfin.git`，基线 tag `v0.4-phase4` 未改。
- 未 commit、未 push、未 rebase、未 force、未修改 origin。
- 保留进入本阶段前用户已有的 `docs/Phase-4-开发报告.md` 修改；未覆盖。
- `bin/`、`obj/`、`*.db`、日志、本地 settings 和 UserData 未纳入 Git。

## 13. Phase 6 建议

等待 Phase 5 人工验收完成并确认 PASS 后，再按设计文档单独启动 Phase 6；本次不进入 Phase 6。

# Phase 1 开发报告

## 1. Status

**PARTIAL**。Phase 1 代码和 Debug/Release 自动验证已完成；真实桌面交互验收未完成，不能报告 PASS。未进入 Phase 2。

## 2. 完成功能

- 220 DIP 的透明、无边框、默认置顶 PetWindow；中性矢量占位，不依赖角色名或素材文件。
- Windows 拖拽适配，拖拽完成后校正并保存位置；双击打开控制中心，右键提供常用菜单。
- Show / Hide / Toggle / OpenControlCenter / CloseControlCenter / Exit 统一应用命令，控制中心和托盘共用。
- 物理像素位置恢复、负坐标处理、离屏校正；PerMonitorV2 基础和 DPI/工作区变更后的可见性保护。
- 托盘显示/隐藏/控制中心/退出；默认关闭控制中心隐藏到托盘，显式退出保存状态并释放资源。
- 配置 schema 1→2 迁移、原子局部更新、中英文本地化、窗口与配置自动测试。

## 3. 主要新增/修改文件

| 文件/目录 | 职责 |
|---|---|
| `src/DesktopPet.Domain/Platform/WindowGeometry.cs` | 强类型 DIP/像素尺寸、保存位置、DPI 尺寸换算 |
| `src/DesktopPet.Application/Windows/` | 平台端口、位置策略、生命周期、窗口命令、托盘菜单定义 |
| `src/DesktopPet.Application/Configuration/` | schema 2、窗口偏好和关闭策略 |
| `src/DesktopPet.Infrastructure/Configuration/JsonSettingsService.cs` | 兼容迁移、锁内局部更新、原子持久化 |
| `src/DesktopPet.Windows/Windowing/` | Win32、WPF 窗口、Dispatcher 和 NotifyIcon 适配 |
| `src/DesktopPet.App/Views/PetWindow.xaml`、`ViewModels/PetWindowViewModel.cs` | 占位视图和本地化展示 |
| `src/DesktopPet.App/Bootstrap/`、`App.xaml.cs`、控制中心 View/ViewModel | DI、宿主边界、事件接线及窗口命令入口 |
| `tests/` 中的 WindowPlacement/WindowLifecycle/WindowSettings/WindowsWindow/WindowEventBridgeTests、StartupSmokeTests | 纯策略、持久化、原生窗口及进程回归 |
| `tools/Verify-Phase1.ps1`、README、执行规范第 21 节 | 可重复验证、运行说明与跨模块契约 |

保留原有 9 个项目，无新增 Project；Windows 项目增加 WPF/WinForms 框架引用，删除框架已提供的冗余 DPAPI 包引用并更新其锁文件。

## 4. 架构决策

Application 管理状态和策略，通过端口调用平台；Windows 层负责 Win32/WPF/托盘，App 是组合根与展示层。Domain/Application 不依赖 WPF、Win32、SQLite。UI 不读写数据库或 JSON。

WindowEventBridge 统一处理异步事件异常；WindowLifecycleService 串行处理状态保存，IUiDispatcher 保证窗口操作回到 UI 线程。App.xaml.cs 仅 20 行，两种 Window Code-Behind 均只初始化视图和 DataContext。没有 Service Locator 业务逻辑或全局可变业务状态。

双库及其 migration 不变：app.db / ai.db 仍只有 Phase 0 迁移账本，无新增业务表。

## 5. Windows API / DPI 实现

- 延用 `app.manifest` 的 PerMonitorV2；生产启动使用生成的 x64 exe。
- `EnumDisplayMonitors` + `GetMonitorInfo` 获取虚拟桌面物理 Bounds/WorkingArea；`GetWindowRect` 获取实际 HWND 尺寸，`SetWindowPos` 设置物理原点，不把全局坐标除以 DPI 后赋给 WPF Left/Top。
- `GetDpiForWindow` 读取实际窗口 DPI；96 只是 DIP 单位定义，并非固定屏幕 DPI 假设。[Microsoft API 说明](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
- `WM_DPICHANGED` 不被拦截或二次缩放，交给 WPF；稍后校正可见范围。`WM_DISPLAYCHANGE` / `WM_SETTINGCHANGE` 仅用于基础可见性保护，没有实现漫游或完整热插拔策略。[Microsoft 消息说明](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)

## 6. Position Persistence 策略

`ISettingsService` 保存到现有 `config/settings.json`，字段为 PetWindow.Position（物理像素原点、显示器设备标识）、IsVisible、Topmost。拖拽完成、显示/隐藏与退出时保存，不按鼠标帧频写盘。

无历史位置时置于主屏工作区右下角，留 24 物理像素边距；有限且可见的负坐标原样恢复。数值非法使用默认值；离屏/显示器移除时选择当前可用工作区并夹取。窗口大于工作区时对齐工作区原点；完全无有效显示器则明确报错。移动到另一屏后重新读取实际物理尺寸再校正。

schema 1→2 保留已有偏好；临时文件落盘后原子替换，`.bak` 保留上一次配置（不是永久迁移归档）。未来 schema 拒绝降级，损坏配置保留原件并恢复默认。并发更新在锁内合并最新快照。

## 7. Tray / Lifecycle 策略

NotifyIcon 的四个菜单项映射到显式注册的 Application Command；双击托盘打开控制中心，小精灵右键复用菜单。

- Show/Hide/Toggle：改变可见性，可重新显示，持久化偏好。
- Close：控制中心默认 HideToTray，可配置 Exit；小精灵关闭请求映射 HidePet。
- Exit：停止接收事件、保存状态、Dispose 托盘/菜单/图标、真正 Close 两个窗口、停止并释放宿主、结束进程。保存失败仍清理资源并报告错误；清理幂等。

## 8. 自动测试结果

SDK 10.0.400 / .NET 10.0.11；由 Verify-Phase1.ps1 执行等价的 locked `dotnet restore`、`dotnet build -c <配置>`、`dotnet test -c <配置>`。

| 验证 | Restore | Build | Unit | Integration | 失败/跳过 |
|---|---|---|---|---|---|
| 修改前 Phase 0 基线 Debug | 成功 | 0 警告 / 0 错误 | 17 | 25 | 0 / 0 |
| 最终 Phase 1 Debug | 成功 | 0 警告 / 0 错误 | 41 | 34 | 0 / 0 |
| 最终 Phase 1 Release | 成功 | 0 警告 / 0 错误 | 41 | 34 | 0 / 0 |

每种配置 75 项通过；新增 33 项，原有 42 项无回归。覆盖默认/负/无效/间隙/移除显示器位置、100/125/150/200% 尺寸数学、命令、取消、退出保存故障、配置迁移与并发更新、托盘事件接线、真实 HWND 样式/DPI/可见性/关闭拦截、真实进程双窗口渲染及重启位置恢复。

最终 TRX（忽略目录）：Debug `phase-1_net10.0_20260827233224.trx` / `...233233.trx`；Release `phase-1_net10.0_20260827233100.trx` / `...233108.trx`，均在 `artifacts/TestResults/<配置>/`。

首次构建发现的 NU1510 已通过删除冗余包引用解决，未压制警告。迁移测试夹具中的错误枚举已修正并增加偏好保留断言，未删减失败测试。

## 9. 实际人工测试环境与结果

Windows 11 家庭版中文版，10.0.26200，x64。原生集成测试实际枚举到 1 个显示器；测试 HWND DPI 为 1.25，220 DIP 对应 275×275 物理像素。这是自动测试记录，不是混合 DPI 人工验收。

已实际启动 Debug exe（`--portable`），确认进程响应、启动日志无错误、配置已保存；期间位置从 (2261,1141) 更新至 (2078,664)，但未据此推断全部交互通过。为最终重编译，已核验路径后清理本次启动的测试进程，便携数据保留；该清理不算正常 Exit 验收。

computer-use 桌面工具两次在初始化阶段失败，返回 `windows sandbox failed: helper_unknown_error: setup refresh had errors`，未获得截图或完成输入操作。按技能要求停止 UI 重试，不用其他自动化绕过。透明外观、真实置顶遮挡、拖拽手感、托盘点击、Show/Hide/Exit 完整交互链均**待人工确认**，无 PASS 声明。

待人工验收：启动 exe → 拖动并双击/右键 → 验证控制中心关闭后托盘可找回 → 托盘显示/隐藏 → 显式退出确认进程结束 → 重启确认位置；随后在 Windows 10/11、100/125/150%、负坐标多屏及热插拔环境重复检查。

## 10. Open Decisions

- 文档未规定位置持久化单位和默认边距，本阶段选物理像素原点 + 显示器标识、24 px 边距；窗口尺寸独立使用 DIP。
- 关闭策略沿用设计默认 HideToTray；启动仍展示基础控制中心，保持 Phase 0 的可发现入口。完整设置与分页留待 Phase 5。
- 设备标识目前来自 Windows display device name，重排可能变化，因此恢复以当前工作区/坐标为准，标识仅辅助。
- 未发现需要扩大 Phase 1 的文档冲突；Phase 0 的 AI 数据库失败降级决定继续有效。

## 11. Risks / Technical Debt

- 人工验收未完成是本阶段阻塞项，需补齐后才可改 PASS。
- Windows 10、多显示器、混合 DPI、显示器热插拔/重新排列、不同任务栏位置未真实交互回归；单屏数值测试不能代替这些环境。
- 托盘暂用系统中性图标；占位矢量不代表正式角色。多实例共享配置的跨进程互斥未实现，请勿同时运行多个实例共用数据目录。
- 极端工作区小于窗口时仅保证原点可见；完整显示拓扑、漫游、穿透、会话/电源行为属于后续阶段。

## 12. Git 状态

开始及结束检查均为 main；origin 为 `https://github.com/zhiuanL/elfin.git`，`v0.0-phase0` 存在。未 init、reset、rebase、改 origin、重写历史、commit 或 push。

已检查 `git status`、`git diff --stat`、`git diff` 和新增文件；`git diff --check` 无空白错误。Git 的 LF→CRLF 提示来自现有换行策略，不是编译警告。

本阶段代码/测试/文档尚未暂存；原有未跟踪文件 `e-0-solution-bootstrap` 原样保留。数据库、bin/obj、logs、UserData、本地 SDK/包缓存及测试结果均被忽略，未纳入待提交文件。建议验收后提交名：`phase-1-pet-window-foundation`；本次不自动提交或推送。

## 13. Phase 2 建议

先补齐 Phase 1 人工验收并获确认。之后仅在收到新指令时开展 Character/Animation：Basic 角色包、Validator、PNG/序列帧与 fallback；本次未开始。
